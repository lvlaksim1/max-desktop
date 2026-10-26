[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
    [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Read-only diagnostics. Do not change, disconnect, bypass or reconfigure the VPN,
# routing, DNS, proxy, firewall, certificates, or application settings.
# Report only protocol/status categories; no credentials, addresses, tokens or keys.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json

function Test-CurlHead {
    param([string]$Target, [string[]]$Extra = @())
    $argsList = @('--silent', '--head', '--output', 'NUL', '--write-out', '%{http_code}', '--max-time', '12')
    $argsList += $Extra
    $argsList += $Target
    try {
        $rawStatus = & curl.exe @argsList 2>$null
        $exit = [int]$LASTEXITCODE
        $status = ([string]($rawStatus | Select-Object -Last 1)).Trim()
        if ($status -notmatch '^[0-9]{3}$') { $status = '000' }
        return [ordered]@{ exit_code = $exit; http_status = $status }
    }
    catch {
        return [ordered]@{ exit_code = 99; http_status = '000' }
    }
}

$hostName = 'ws-api.oneme.ru'
$webSocketUrl = 'https://ws-api.oneme.ru/websocket'
$webPageUrl = 'https://web.max.ru/'
$dnsV4 = 0
$dnsV6 = 0
try {
    foreach ($entry in [System.Net.Dns]::GetHostAddresses($hostName)) {
        if ($entry.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) { $dnsV4++ }
        if ($entry.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) { $dnsV6++ }
    }
}
catch { }

$proxySetting = 'unknown'
try {
    $target = New-Object System.Uri($webSocketUrl)
    $proxy = [System.Net.WebRequest]::GetSystemWebProxy()
    if ($proxy.IsBypassed($target)) { $proxySetting = 'direct' }
    elseif ($proxy.GetProxy($target).AbsoluteUri -eq $target.AbsoluteUri) { $proxySetting = 'direct' }
    else { $proxySetting = 'proxy' }
}
catch { }

$tcp443 = 'unknown'
try {
    $tcp443 = [string][bool](Test-NetConnection -ComputerName $hostName -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue)
}
catch { }

$curlDefault = Test-CurlHead -Target $webSocketUrl
$curlIPv4 = Test-CurlHead -Target $webSocketUrl -Extra @('--ipv4')
$curlIPv6 = Test-CurlHead -Target $webSocketUrl -Extra @('--ipv6')
$curlWeb = Test-CurlHead -Target $webPageUrl

$dotNetHttp = 'not_tested'
try {
    $req = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($webSocketUrl)
    $req.Method = 'HEAD'
    $req.Timeout = 12000
    $req.ReadWriteTimeout = 12000
    $req.AllowAutoRedirect = $false
    $response = $req.GetResponse()
    try { $dotNetHttp = 'HTTP' + ([int]$response.StatusCode) }
    finally { $response.Close() }
}
catch [System.Net.WebException] {
    $webException = $_.Exception
    if ($null -ne $webException.Response) {
        try { $dotNetHttp = 'HTTP' + ([int]$webException.Response.StatusCode) }
        finally { $webException.Response.Close() }
    } else { $dotNetHttp = [string]$webException.Status }
}
catch { $dotNetHttp = 'other_error' }

$brief = ('ws={0}/{1};ipv4={2}/{3};ipv6={4}/{5};web={6}/{7};dotnet={8};tcp443={9};proxy={10};dns4={11};dns6={12}' -f
    $curlDefault.exit_code, $curlDefault.http_status,
    $curlIPv4.exit_code, $curlIPv4.http_status,
    $curlIPv6.exit_code, $curlIPv6.http_status,
    $curlWeb.exit_code, $curlWeb.http_status,
    $dotNetHttp, $tcp443, $proxySetting, $dnsV4, $dnsV6)

$result = [ordered]@{
    status = ('diagnostics: ' + $brief)
    request_id = [string]$request.request_id
    vpn_configuration_changed = $false
    endpoint = $hostName
    curl_default = $curlDefault
    curl_ipv4 = $curlIPv4
    curl_ipv6 = $curlIPv6
    curl_web = $curlWeb
    dotnet_http = $dotNetHttp
    tcp_port_443_reachable = $tcp443
    system_proxy_route = $proxySetting
    dns_ipv4_count = $dnsV4
    dns_ipv6_count = $dnsV6
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8

# Phase 2: differential HTTPS tests through the existing VPN/system proxy.
# This does not edit network settings or disable the VPN. No proxy endpoint or
# credentials are written to GitHub or to the compact project result.
function Get-ProxyFor {
    param([string]$Target)
    try {
        $uri = New-Object System.Uri($Target)
        $sys = [System.Net.WebRequest]::GetSystemWebProxy()
        if ($sys.IsBypassed($uri)) { return $null }
        $p = $sys.GetProxy($uri)
        if ($null -eq $p -or $p.AbsoluteUri -eq $uri.AbsoluteUri) { return $null }
        return $p
    }
    catch { return $null }
}
function Test-ExplicitProxy {
    param([string]$Target)
    $proxyUri = Get-ProxyFor -Target $Target
    if ($null -eq $proxyUri) { return [ordered]@{ route = 'direct_or_unknown'; exit_code = -1; http_status = '000' } }
    if ($proxyUri.Scheme -notin @('http','https','socks5','socks5h') -or
        -not [string]::IsNullOrEmpty($proxyUri.UserInfo)) {
        return [ordered]@{ route = 'unsupported_or_authenticated'; exit_code = -2; http_status = '000' }
    }
    # This uses the configured Windows VPN-related proxy if present,
    # preserving end-to-end certificate and hostname validation.
    $argsList = @('--silent','--head','--output','NUL','--write-out','%{http_code}',
        '--max-time','12','--proxy',$proxyUri.AbsoluteUri,$Target)
    try {
        $raw = & curl.exe @argsList 2>$null
        $exit = [int]$LASTEXITCODE
        $http = ([string]($raw | Select-Object -Last 1)).Trim()
        if ($http -notmatch '^[0-9]{3}$') { $http = '000' }
        return [ordered]@{ route = $proxyUri.Scheme; exit_code = $exit; http_status = $http }
    }
    catch {
        return [ordered]@{ route = $proxyUri.Scheme; exit_code = 99; http_status = '000' }
    }
}

$controlGithub = Test-CurlHead -Target 'https://github.com/'
$controlMail = Test-CurlHead -Target 'https://mail.ru/'
$controlMicrosoft = Test-CurlHead -Target 'https://www.microsoft.com/'
$proxyWs = Test-ExplicitProxy -Target $webSocketUrl
$proxyWeb = Test-ExplicitProxy -Target $webPageUrl
$proxyGithub = Test-ExplicitProxy -Target 'https://github.com/'
$proxyMail = Test-ExplicitProxy -Target 'https://mail.ru/'

$extended = (';github={0}/{1};mail={2}/{3};microsoft={4}/{5};pxws={6}/{7}/{8};pxweb={9}/{10}/{11};pxgh={12}/{13}/{14};pxmail={15}/{16}/{17}' -f
  $controlGithub.exit_code, $controlGithub.http_status,
  $controlMail.exit_code, $controlMail.http_status,
  $controlMicrosoft.exit_code, $controlMicrosoft.http_status,
  $proxyWs.route, $proxyWs.exit_code, $proxyWs.http_status,
  $proxyWeb.route, $proxyWeb.exit_code, $proxyWeb.http_status,
  $proxyGithub.route, $proxyGithub.exit_code, $proxyGithub.http_status,
  $proxyMail.route, $proxyMail.exit_code, $proxyMail.http_status)
$result.status = $result.status + $extended
$result.controls = [ordered]@{ github = $controlGithub; mail = $controlMail; microsoft = $controlMicrosoft }
$result.explicit_proxy = [ordered]@{ ws = $proxyWs; web = $proxyWeb; github = $proxyGithub; mail = $proxyMail }
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

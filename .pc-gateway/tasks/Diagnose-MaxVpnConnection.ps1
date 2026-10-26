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
exit 0

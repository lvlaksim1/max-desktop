[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
    [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Read-only. Never change or bypass VPN settings, system proxy, DNS, certificates or routing.
# Only status and CONNECT response codes are reported; proxy URI and credentials stay local.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$ws = 'https://ws-api.oneme.ru/websocket'
$web = 'https://web.max.ru/'
$github = 'https://github.com/'
$proxyRoute = 'none'
$proxyUri = $null
try {
    $target = New-Object System.Uri($ws)
    $systemProxy = [System.Net.WebRequest]::GetSystemWebProxy()
    if (-not $systemProxy.IsBypassed($target)) {
        $candidate = $systemProxy.GetProxy($target)
        if ($candidate.AbsoluteUri -ne $target.AbsoluteUri) {
            $proxyRoute = $candidate.Scheme
            if ($candidate.Scheme -in @('http','https','socks5','socks5h') -and
                [string]::IsNullOrEmpty($candidate.UserInfo)) {
                $proxyUri = $candidate
            }
        }
    }
}
catch { $proxyRoute = 'lookup_error' }

function Check-TLS {
    param([string]$Url, [string]$Version)
    if ($null -eq $script:proxyUri) {
        return 'no_proxy'
    }
    $argList = @('--silent','--head','--output','NUL','--write-out',
        '%{http_connect}/%{http_code}','--max-time','12','--proxy',$script:proxyUri.AbsoluteUri)
    if ($Version -eq '1.2') { $argList += @('--tlsv1.2','--tls-max','1.2') }
    if ($Version -eq '1.3') { $argList += @('--tlsv1.3','--tls-max','1.3') }
    $argList += $Url
    try {
        $raw = & curl.exe @argList 2>$null
        $exitCode = [int]$LASTEXITCODE
        $codes = ([string]($raw | Select-Object -Last 1)).Trim()
        if ($codes -notmatch '^[0-9]{3}/[0-9]{3}$') { $codes = '000/000' }
        return ([string]$exitCode + '/' + $codes)
    }
    catch {
        return '99/000/000'
    }
}

$wsDefault = Check-TLS -Url $ws -Version 'default'
$wsTls12 = Check-TLS -Url $ws -Version '1.2'
$wsTls13 = Check-TLS -Url $ws -Version '1.3'
$webDefault = Check-TLS -Url $web -Version 'default'
$githubDefault = Check-TLS -Url $github -Version 'default'

$summary = ('proxy={0};ws={1};tls12={2};tls13={3};web={4};github={5}' -f
    $proxyRoute, $wsDefault, $wsTls12, $wsTls13, $webDefault, $githubDefault)
$result = [ordered]@{
    status = ('tls-proxy-check: ' + $summary)
    request_id = [string]$request.request_id
    proxy_type = $proxyRoute
    ws_default = $wsDefault
    ws_tls12 = $wsTls12
    ws_tls13 = $wsTls13
    web_default = $webDefault
    github_default = $githubDefault
    vpn_configuration_changed = $false
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

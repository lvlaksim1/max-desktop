[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
    [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Read-only. Never edit or disable VPN, proxy, adapter, DNS, routes or services.
# Return only bounded classifications; do not output IPs, ports, usernames or paths.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$uri = New-Object System.Uri('https://ws-api.oneme.ru/websocket')
$proxyKind = 'unknown'
$proxyLocation = 'unknown'
$localOwner = 'not_applicable'
$envProxy = [ordered]@{}
foreach ($key in @('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NO_PROXY')) {
    $envProxy[$key] = [bool][Environment]::GetEnvironmentVariable($key, 'Process')
}
try {
    $sys = [System.Net.WebRequest]::GetSystemWebProxy()
    if ($sys.IsBypassed($uri)) {
        $proxyKind = 'direct'
        $proxyLocation = 'none'
    }
    else {
        $p = $sys.GetProxy($uri)
        if ($null -ne $p -and $p.AbsoluteUri -ne $uri.AbsoluteUri) {
            $proxyKind = $p.Scheme
            $proxyLocation = 'remote_or_private'
            if ($p.Host -in @('127.0.0.1','localhost','::1','[::1]')) {
                $proxyLocation = 'loopback'
                try {
                    $connections = @(Get-NetTCPConnection -State Listen -LocalPort $p.Port -ErrorAction SilentlyContinue)
                    $ids = @($connections | Select-Object -ExpandProperty OwningProcess -Unique)
                    foreach ($id in $ids) {
                        $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
                        if ($null -eq $proc) { continue }
                        $name = [string]$proc.ProcessName
                        $candidates = @('sing-box','xray','v2ray','v2rayN','hiddify','clash','mihomo','nekoray','nekobox','amnezia','wireguard','outline','openvpn','tun2socks','throne','shadowsocks','zapret','goodbyedpi','vpn')
                        foreach ($word in $candidates) {
                            if ($name.IndexOf($word,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
                                $localOwner = $word
                                break
                            }
                        }
                        if ($localOwner -ne 'not_applicable') { break }
                        $localOwner = 'unidentified'
                    }
                }
                catch { $localOwner = 'query_unavailable' }
            }
        }
        else {
            $proxyKind = 'direct'
            $proxyLocation = 'none'
        }
    }
}
catch { $proxyKind = 'lookup_error' }

$vpnAdapters = @()
try {
    $adapters = @(Get-NetAdapter -IncludeHidden -ErrorAction Stop)
    foreach ($adapter in $adapters) {
        $name = [string]$adapter.Name + ' ' + [string]$adapter.InterfaceDescription
        foreach ($vendor in @('WireGuard','Wintun','TAP-Windows','TUN','OpenVPN','Amnezia','Hiddify','Clash','sing-box','Outline','VPN')) {
            if ($name.IndexOf($vendor,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and $vpnAdapters -notcontains $vendor) {
                $vpnAdapters += $vendor
            }
        }
    }
}
catch { }
if ($vpnAdapters.Count -gt 6) { $vpnAdapters = @($vpnAdapters | Select-Object -First 6) }
$adapterText = if ($vpnAdapters.Count) { $vpnAdapters -join ',' } else { 'no_classification' }
$envText = @($envProxy.Keys | Where-Object { $envProxy[$_] }) -join ','
if (-not $envText) { $envText = 'none' }
$status = "proxyowner: scheme=$proxyKind;location=$proxyLocation;owner=$localOwner;adapters=$adapterText;envproxy=$envText"
$result = [ordered]@{
    status = $status
    request_id = [string]$request.request_id
    proxy_type = $proxyKind
    proxy_location = $proxyLocation
    identified_local_proxy_process_class = $localOwner
    adapter_keyword_classes = $vpnAdapters
    env_proxy_variable_names_set = $envText
    vpn_settings_changed = $false
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

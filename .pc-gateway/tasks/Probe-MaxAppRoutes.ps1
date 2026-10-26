[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
    [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Read-only per-process network experiments. Never modify VPN, DNS, adapter,
# routing tables, certificates, firewall or proxy configuration. Never log
# host IP addresses, proxy ports, credentials or local filesystem paths.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json

function Invoke-Curl {
    param([string]$Url, [string[]]$Extra = @())
    $arguments = @('--silent','--head','--output','NUL','--write-out','%{http_code}','--max-time','10')
    $arguments += $Extra
    $arguments += $Url
    try {
        $output = & curl.exe @arguments 2>$null
        $exitCode = [int]$LASTEXITCODE
        $status = ([string]($output | Select-Object -Last 1)).Trim()
        if ($status -notmatch '^[0-9]{3}$') { $status = '000' }
        return ('{0}/{1}' -f $exitCode,$status)
    } catch { return '99/000' }
}

$maxUrl = 'https://ws-api.oneme.ru/websocket'
$otherMaxUrl = 'https://web.max.ru/'
$control = 'https://github.com/'

$routes = [ordered]@{
    default_max = Invoke-Curl -Url $maxUrl
    direct_max = Invoke-Curl -Url $maxUrl -Extra @('--noproxy','*')
    direct_web = Invoke-Curl -Url $otherMaxUrl -Extra @('--noproxy','*')
    direct_control = Invoke-Curl -Url $control -Extra @('--noproxy','*')
}
$physicalAddresses = @()
try {
    $adapters = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' })
    foreach ($adapter in $adapters) {
        $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $adapter.ifIndex -ErrorAction SilentlyContinue)
        foreach ($entry in $addresses) {
            $value = [string]$entry.IPAddress
            if ($value -and $value -notmatch '^(127\.|169\.254\.|0\.)' -and $physicalAddresses -notcontains $value) {
                $physicalAddresses += $value
            }
        }
    }
} catch { }
$testPhysical = @($physicalAddresses | Select-Object -First 2)
$index = 0
foreach ($address in $testPhysical) {
    $index++
    $routes[('interface' + $index + '_max')] = Invoke-Curl -Url $maxUrl -Extra @('--noproxy','*','--interface',$address)
    $routes[('interface' + $index + '_web')] = Invoke-Curl -Url $otherMaxUrl -Extra @('--noproxy','*','--interface',$address)
    $routes[('interface' + $index + '_github')] = Invoke-Curl -Url $control -Extra @('--noproxy','*','--interface',$address)
}

$proxyOptions = @()
try {
    $owners = @(Get-CimInstance Win32_Process -Filter "Name='xray.exe'" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessId)
    $ports = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $owners -contains $_.OwningProcess -and $_.LocalAddress -in @('127.0.0.1','::1') } | Select-Object -ExpandProperty LocalPort -Unique)
    $proxyOptions = @($ports | Sort-Object | Select-Object -First 3)
} catch { }
$proxyIndex=0
foreach ($port in $proxyOptions) {
    $proxyIndex++
    $proxyUrl = 'http://127.0.0.1:' + [string]$port
    $socks = 'socks5h://127.0.0.1:' + [string]$port
    $routes[('local' + $proxyIndex + '_http_max')] = Invoke-Curl -Url $maxUrl -Extra @('--proxy',$proxyUrl)
    $routes[('local' + $proxyIndex + '_socks_max')] = Invoke-Curl -Url $maxUrl -Extra @('--proxy',$socks)
    $routes[('local' + $proxyIndex + '_http_github')] = Invoke-Curl -Url $control -Extra @('--proxy',$proxyUrl)
    $routes[('local' + $proxyIndex + '_socks_github')] = Invoke-Curl -Url $control -Extra @('--proxy',$socks)
}

$items = @()
foreach ($key in $routes.Keys) { $items += ($key + '=' + [string]$routes[$key]) }
$summary = 'app-routes: phy=' + $testPhysical.Count + ';localports=' + $proxyOptions.Count + ';' + ($items -join ';')
$result = [ordered]@{
    status = $summary
    request_id = [string]$request.request_id
    route_results = $routes
    physical_interfaces_tested = $testPhysical.Count
    xray_local_ports_tested = $proxyOptions.Count
    system_settings_changed = $false
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

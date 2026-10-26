[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
    [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Read-only probe of native Android MAX TCP/TLS gateways.
# The currently active system VPN and Windows routing/proxy/DNS/firewall are NOT changed.
# No account login, INIT, SMS, credentials, or authentication payloads.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json

$bound = $null
$physicalCount = 0
try {
    $adapters = @(Get-NetAdapter -Physical -ErrorAction Stop |
        Where-Object { $_.Status -eq 'Up' })
    foreach ($adapter in $adapters) {
        $ips = @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notmatch '^(127\.|169\.254\.|0\.)' })
        foreach ($entry in $ips) {
            $physicalCount++
            if ($null -eq $bound) {
                $bound = [System.Net.IPAddress]::Parse([string]$entry.IPAddress)
            }
        }
    }
}
catch { }

$hosts = @('api2.oneme.ru','api.oneme.ru','api-tg.oneme.ru','ws-api.oneme.ru')
$results = [ordered]@{}
foreach ($hostName in $hosts) {
    $tcp = $null
    $tls = $null
    $status = 'not_tested'
    $trusted = $false
    $tcpConnected = $false
    try {
        if ($null -eq $bound) { throw 'no_physical_ipv4' }
        $ip = @([System.Net.Dns]::GetHostAddresses($hostName) |
            Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork } |
            Select-Object -First 1)
        if ($ip.Count -eq 0) { throw 'no_remote_ipv4' }
        $local = New-Object System.Net.IPEndPoint($bound,0)
        $tcp = New-Object System.Net.Sockets.TcpClient($local)
        $task = $tcp.ConnectAsync($ip[0],443)
        if (-not $task.Wait(10000)) { throw 'tcp_timeout' }
        if (-not $tcp.Connected) { throw 'tcp_not_connected' }
        $tcpConnected = $true

        $validator = [System.Net.Security.RemoteCertificateValidationCallback]{
            param($sender,$certificate,$chain,$errors)
            return $errors -eq [System.Net.Security.SslPolicyErrors]::None
        }
        $tls = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, $validator)
        $tls.ReadTimeout = 12000
        $tls.WriteTimeout = 12000
        $tls.AuthenticateAsClient($hostName)
        $trusted = $tls.IsAuthenticated
        $status = if ($trusted) { 'tls_verified' } else { 'tls_not_authenticated' }
    }
    catch {
        $category = [string]$_.Exception.Message
        if ($category -notin @('no_physical_ipv4','no_remote_ipv4','tcp_timeout','tcp_not_connected')) {
            $category = if ($tcpConnected) {'tls_handshake_failed'} else {'tcp_connect_failed'}
        }
        $status = $category
    }
    finally {
        if ($null -ne $tls) { $tls.Dispose() }
        if ($null -ne $tcp) { $tcp.Dispose() }
    }
    $results[$hostName] = $status
}
$parts = @($results.Keys | ForEach-Object { $_ + '=' + $results[$_] })
$result = [ordered]@{
    status = ('android-tls: physical_ipv4_count=' + $physicalCount + ';' + ($parts -join ';'))
    request_id = [string]$request.request_id
    hosts = $results
    vpn_changed = $false
    account_requests_sent = $false
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

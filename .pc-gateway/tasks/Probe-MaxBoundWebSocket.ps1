[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
    [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Read-only WebSocket upgrade diagnostic. No SMS, INIT, accounts or VPN changes.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$hostName = 'ws-api.oneme.ru'
$status = 'not_started'
$tcpOk = $false
$tlsOk = $false
$http = '000'
$sourceCount = 0
$client = $null
$tls = $null
try {
    $physical = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' })
    $localIpv4 = $null
    foreach ($adapter in $physical) {
        $ipv4 = @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -notmatch '^(127\.|169\.254\.|0\.)' })
        foreach ($entry in $ipv4) {
            $sourceCount++
            if ($null -eq $localIpv4) { $localIpv4 = [System.Net.IPAddress]::Parse([string]$entry.IPAddress) }
        }
    }
    if ($null -eq $localIpv4) { throw 'no_physical_ipv4' }
    $remoteAddresses = @([System.Net.Dns]::GetHostAddresses($hostName) |
        Where-Object { $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork })
    if ($remoteAddresses.Count -eq 0) { throw 'no_remote_ipv4' }
    $localEndpoint = New-Object System.Net.IPEndPoint($localIpv4,0)
    $client = New-Object System.Net.Sockets.TcpClient($localEndpoint)
    $client.NoDelay = $true
    $connection = $client.ConnectAsync($remoteAddresses[0],443)
    if (-not $connection.Wait(9000)) { throw 'tcp_timeout' }
    if (-not $client.Connected) { throw 'tcp_not_connected' }
    $tcpOk = $true
    $validation = [System.Net.Security.RemoteCertificateValidationCallback]{
        param($sender,$certificate,$chain,$sslPolicyErrors)
        return ($sslPolicyErrors -eq [System.Net.Security.SslPolicyErrors]::None)
    }
    $tls = New-Object System.Net.Security.SslStream($client.GetStream(),$false,$validation)
    $tls.ReadTimeout = 9000
    $tls.WriteTimeout = 9000
    $tls.AuthenticateAsClient($hostName)
    $tlsOk = $true

    $key = [Convert]::ToBase64String((1..16 | ForEach-Object { [byte](Get-Random -Minimum 0 -Maximum 256) }))
    $crlf = [string][char]13 + [string][char]10
    $requestText = ('GET /websocket HTTP/1.1' + $crlf +
        'Host: ' + $hostName + $crlf +
        'Upgrade: websocket' + $crlf +
        'Connection: Upgrade' + $crlf +
        'Sec-WebSocket-Version: 13' + $crlf +
        'Sec-WebSocket-Key: ' + $key + $crlf +
        'Origin: https://web.max.ru' + $crlf +
        'User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0.0.0 Safari/537.36' + $crlf + $crlf)
    $requestBytes = [System.Text.Encoding]::ASCII.GetBytes($requestText)
    $tls.Write($requestBytes,0,$requestBytes.Length)
    $tls.Flush()

    $buffer = New-Object System.IO.MemoryStream
    $one = New-Object byte[] 1
    $sequence = 0
    while ($buffer.Length -lt 4096) {
        $numRead = $tls.Read($one,0,1)
        if ($numRead -le 0) { throw 'upgrade_eof' }
        $buffer.WriteByte($one[0])
        if ($one[0] -eq 13 -and ($sequence -eq 0 -or $sequence -eq 2)) { $sequence++ }
        elseif ($one[0] -eq 10 -and ($sequence -eq 1 -or $sequence -eq 3)) { $sequence++ }
        else { $sequence = $(if ($one[0] -eq 13) {1} else {0}) }
        if ($sequence -eq 4) { break }
    }
    $received = [System.Text.Encoding]::ASCII.GetString($buffer.ToArray())
    if ($received -match '^HTTP/1\.[01] ([0-9]{3})') { $http = $matches[1] }
    $status = if ($http -eq '101') {'websocket_upgrade_ok'} else {'websocket_upgrade_http_' + $http}
}
catch {
    $category = [string]$_.Exception.Message
    if ($category -notin @('no_physical_ipv4','no_remote_ipv4','tcp_timeout','tcp_not_connected','upgrade_eof')) {
        if (-not $tcpOk) { $category = 'tcp_error' }
        elseif (-not $tlsOk) { $category = 'tls_error' }
        else { $category = 'upgrade_error' }
    }
    $status = $category
}
finally {
    if ($null -ne $tls) { $tls.Dispose() }
    if ($null -ne $client) { $client.Dispose() }
}
$result = [ordered]@{
    status = ('bound-websocket: ' + $status + ';tcp=' + $tcpOk + ';tls=' + $tlsOk + ';http=' + $http + ';sources=' + $sourceCount)
    request_id = [string]$request.request_id
    tcp_connected = $tcpOk
    tls_validated = $tlsOk
    upgrade_status_code = $http
    outcome = $status
    system_configuration_changed = $false
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$GatewayRequestPath,
  [Parameter(Mandatory=$true)][string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$request=Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$eligible=0
$withGateway=0
$selected=$false
try {
 foreach ($network in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
  if ($network.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up) { continue }
  if ($network.NetworkInterfaceType -notin @([System.Net.NetworkInformation.NetworkInterfaceType]::Ethernet,
    [System.Net.NetworkInformation.NetworkInterfaceType]::Wireless80211)) { continue }
  $description=[string]$network.Description+' '+[string]$network.Name
  $excluded=$false
  foreach($token in @('TUN','TAP','Wintun','WireGuard','VPN','Xray','sing-box','Clash')) {
   if($description.IndexOf($token,[StringComparison]::OrdinalIgnoreCase) -ge 0){$excluded=$true;break}
  }
  if($excluded){continue}
  $eligible++
  $properties=$network.GetIPProperties()
  $gateway=@($properties.GatewayAddresses | Where-Object {$_.Address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork})
  if($gateway.Count -eq 0){continue}
  $withGateway++
  $addresses=@($properties.UnicastAddresses | Where-Object {
    $_.Address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork -and
    -not [System.Net.IPAddress]::IsLoopback($_.Address) -and
    -not ($_.Address.ToString().StartsWith('169.254.'))
  })
  if($addresses.Count -gt 0){$selected=$true;break}
 }
}catch {}
$result=[ordered]@{
 status=('physical-selection: eligible='+$eligible+';gateway='+$withGateway+';selected='+$selected)
 request_id=[string]$request.request_id
 selected=$selected
 network_settings_changed=$false
}
$result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

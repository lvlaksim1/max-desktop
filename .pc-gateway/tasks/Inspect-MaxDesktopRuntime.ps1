[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$GatewayRequestPath,
  [Parameter(Mandatory=$true)][string]$GatewayResultPath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$req=Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$r=[ordered]@{
 request_id=[string]$req.request_id
 status='not_checked'
 runner_session=[System.Diagnostics.Process]::GetCurrentProcess().SessionId
 app_process_count=0
 app_same_session_count=0
 webview2_process_count=0
 app_version='not_available'
 account_data_accessed=$false
 settings_changed=$false
}
try {
 $apps=@(Get-Process -Name 'MaxDesktop.App' -ErrorAction SilentlyContinue)
 $r.app_process_count=$apps.Count
 $r.app_same_session_count=@($apps | Where-Object {$_.SessionId -eq $r.runner_session}).Count
 $r.webview2_process_count=@(Get-Process -Name 'msedgewebview2' -ErrorAction SilentlyContinue).Count
 if($apps.Count -gt 0) {
  try {
   $ver=[string]$apps[0].MainModule.FileVersionInfo.ProductVersion
   if($ver -match '^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$'){$r.app_version=$ver}
  }catch{}
 }
 $r.status='runtime_presence_checked'
}
catch { $r.status='runtime_presence_inconclusive' }
$r | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

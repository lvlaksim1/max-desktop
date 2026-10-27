[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$GatewayRequestPath,
  [Parameter(Mandatory=$true)][string]$GatewayResultPath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$req=Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$result=[ordered]@{
 request_id=[string]$req.request_id
 status='inconclusive'
 app_process_count=0
 visible_window_count=0
 miniapp_window_count=0
 same_session=$false
 secrets_read=$false
 settings_changed=$false
}
try{
 $processes=@(Get-Process -Name 'MaxDesktop.App' -ErrorAction SilentlyContinue)
 $result.app_process_count=$processes.Count
 $result.same_session=(@($processes | Where-Object {$_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId}).Count -gt 0)
 if($processes.Count -gt 0){
  Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class MaxWindowPresence {
  public delegate bool Visitor(IntPtr hwnd, IntPtr ignored);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Visitor callback, IntPtr ignored);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hwnd, StringBuilder buffer, int max);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
'@ -ErrorAction Stop
  $ids=@($processes | ForEach-Object { [uint32]$_.Id })
  $seen=0
  $mini=0
  [MaxWindowPresence]::EnumWindows({
    param($hwnd,$ignored)
    [uint32]$pid=0
    [void][MaxWindowPresence]::GetWindowThreadProcessId($hwnd,[ref]$pid)
    if(($ids -contains $pid) -and [MaxWindowPresence]::IsWindowVisible($hwnd)){
      $seen++
      $builder=New-Object System.Text.StringBuilder 256
      [void][MaxWindowPresence]::GetWindowText($hwnd,$builder,256)
      # Only compare a stable product-owned window prefix, never emit titles.
      if($builder.ToString().StartsWith('Мини-приложение MAX',[StringComparison]::Ordinal)){
        $mini++
      }
    }
    return $true
  },[IntPtr]::Zero) | Out-Null
  $result.visible_window_count=$seen
  $result.miniapp_window_count=$mini
  $result.status='window_presence:visible='+$seen+';miniapp='+$mini+';same_session='+$result.same_session
 }
 else {
  $result.status='no_desktop_app_process'
 }
}catch{ $result.status='window_check_failed' }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

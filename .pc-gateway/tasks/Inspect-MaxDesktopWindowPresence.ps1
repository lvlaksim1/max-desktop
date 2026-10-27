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
using System.Collections.Generic;
public static class MaxWindowPresence {
  public delegate bool Visitor(IntPtr hwnd, IntPtr ignored);
  [DllImport("user32.dll")] public static extern bool EnumWindows(Visitor callback, IntPtr ignored);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder buffer, int max);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
  public static int[] Count(uint[] pids) {
    var accepted = new HashSet<uint>(pids);
    int visible=0, mini=0;
    Visitor visitor=(hwnd,ignored) => {
      uint pid;
      GetWindowThreadProcessId(hwnd, out pid);
      if (accepted.Contains(pid) && IsWindowVisible(hwnd)) {
        visible++;
        var title=new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        if (title.ToString().StartsWith("Мини-приложение MAX",StringComparison.Ordinal)) mini++;
      }
      return true;
    };
    EnumWindows(visitor,IntPtr.Zero);
    return new int[] { visible, mini };
  }
}
'@ -ErrorAction Stop
  $ids=@($processes | ForEach-Object { [uint32]$_.Id })
  $counts=[MaxWindowPresence]::Count([uint32[]]$ids)
  $seen=[int]$counts[0]
  $mini=[int]$counts[1]
  $result.visible_window_count=$seen
  $result.miniapp_window_count=$mini
  $result.status='window_presence:visible='+$seen+';miniapp='+$mini+';same_session='+$result.same_session
 }
 else {
  $result.status='no_desktop_app_process'
 }
}catch{ $result.status='window_check_failed_'+$_.Exception.GetType().Name }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

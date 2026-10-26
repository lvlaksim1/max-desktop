[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
  [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Read-only classification, without exposing filesystem paths, command lines, network endpoints, keys or secrets.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$family = @('v2rayN','Hiddify','NekoRay','NekoBox','Throne','Amnezia','Clash','Mihomo',
            'sing-box','Xray','Outline','WireGuard','OpenVPN','Proxifier','Netch')
$detected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
try {
    foreach ($process in @(Get-Process -ErrorAction SilentlyContinue)) {
        $n = [string]$process.ProcessName
        foreach ($candidate in $family) {
            if ($n.IndexOf($candidate,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
                [void]$detected.Add($candidate)
            }
        }
    }
}
catch { }
$pathFamily = 'unknown'
try {
    $list = @(Get-CimInstance Win32_Process -Filter "Name='xray.exe'" -ErrorAction SilentlyContinue)
    foreach ($p in $list) {
        $path = [string]$p.ExecutablePath
        foreach ($candidate in $family) {
            if ($path.IndexOf($candidate,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and $candidate -ne 'Xray') {
                $pathFamily = $candidate
                break
            }
        }
        if ($pathFamily -ne 'unknown') { break }
    }
}
catch { }
$detectedList = @($family | Where-Object { $detected.Contains($_) })
$status = 'vpn-manager: processes=' + ($(if($detectedList.Count){$detectedList -join ','}else{'none'})) + ';xray_path_family=' + $pathFamily
$result = [ordered]@{
    status = $status
    request_id = [string]$request.request_id
    known_vpn_process_classes = $detectedList
    xray_executable_path_family = $pathFamily
    system_settings_changed = $false
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

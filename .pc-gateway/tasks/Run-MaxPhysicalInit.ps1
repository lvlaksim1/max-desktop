[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string]$GatewayRequestPath,
  [Parameter(Mandatory = $true)] [string]$GatewayResultPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Read-only live SESSION_INIT through the physical adapter with VPN still on.
# No phone, SMS, account, token, secret, proxy or VPN settings are touched.
$request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$command = Get-Command dotnet -ErrorAction SilentlyContinue
$state = 'dotnet_unavailable'
$detail = 'none'
$code = -1
if ($null -ne $command) {
    try {
        $output = @(& $command.Source run --project (Join-Path $projectRoot 'tests\MaxDesktop.WebSocket.Probe\MaxDesktop.WebSocket.Probe.csproj') -c Release -- --physical 2>&1)
        $code = [int]$LASTEXITCODE
        $safe = @($output | ForEach-Object { [string]$_ } | Where-Object { $_ -match '^(WS_PROBE_STATE=|WS_PROBE_DETAIL=|WS_PROBE_FAILURE=)' })
        $stateLine = @($safe | Where-Object { $_ -like 'WS_PROBE_STATE=*' } | Select-Object -Last 1)
        $detailLine = @($safe | Where-Object { $_ -like 'WS_PROBE_DETAIL=*' } | Select-Object -Last 1)
        $failLine = @($safe | Where-Object { $_ -like 'WS_PROBE_FAILURE=*' } | Select-Object -Last 1)
        if ($stateLine.Count) { $state = [string]$stateLine[0].Substring(15) }
        elseif ($failLine.Count) { $state = [string]$failLine[0].Substring(17) }
        else { $state = 'no_probe_output' }
        if ($detailLine.Count) {
            if ($detailLine[0] -match 'SESSION_INIT') { $detail = 'SESSION_INIT_CONFIRMED' }
            else { $detail = 'diagnostic_non_success' }
        }
    }
    catch {
        $state = 'probe_execution_error'
    }
}
$result = [ordered]@{
    status = ('physical-init: state=' + $state + ';detail=' + $detail + ';exit=' + $code)
    request_id = [string]$request.request_id
    state = $state
    detail_class = $detail
    dotnet_exit_code = $code
    system_configuration_changed = $false
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

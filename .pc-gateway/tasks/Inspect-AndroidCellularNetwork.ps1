[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GatewayRequestPath,
    [Parameter(Mandatory=$true)][string]$GatewayResultPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Only a read-only Android ConnectivityService query. No MAX process access,
# HTTP traffic capture, privileges, screenshot or secret collection.
$result = [ordered]@{
    status = 'unknown'
    adb_device_available = $false
    network_agent_count = 0
    cellular_agent_count = 0
    cellular_internet_agent_count = 0
    cellular_internet_validated_agent_count = 0
    parsing = 'not_started'
    settings_changed = $false
    max_data_accessed = $false
}
try {
    $request = Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $result['request_id'] = [string]$request.request_id

    $serial = [string]$request.args.serial
    if ([string]::IsNullOrWhiteSpace($serial) -or $serial -notmatch '^[A-Za-z0-9._:-]{4,80}$') {
        throw 'invalid_device_selector'
    }

    $adb = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($null -eq $adb) {
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
            (Join-Path $env:USERPROFILE 'platform-tools\adb.exe'),
            (Join-Path $env:USERPROFILE 'AppData\Local\Android\Sdk\platform-tools\adb.exe'),
            'C:\platform-tools\adb.exe'
        )
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $adb = Get-Item -LiteralPath $candidate
                break
            }
        }
    }
    if ($null -eq $adb) {
        $result.status = 'adb_not_found_in_runner_environment'
        $result.parsing = 'not_attempted'
    }
    else {
        $adbPath = [string]$adb.Source
        if ($adb -is [System.IO.FileInfo]) { $adbPath = [string]$adb.FullName }

        $state = ((& $adbPath -s $serial get-state 2>$null) -join '').Trim()
        if ($LASTEXITCODE -ne 0 -or $state -ne 'device') {
            $result.status = 'adb_device_not_available_to_runner'
            $result.parsing = 'not_attempted'
        }
        else {
            $result.adb_device_available = $true
            $dump = (& $adbPath -s $serial shell dumpsys connectivity 2>$null) -join "`n"
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dump)) {
                $result.status = 'connectivity_dump_unavailable'
                $result.parsing = 'no_data'
            }
            else {
                $lines = $dump -split "\r?\n"
                $agents = New-Object System.Collections.Generic.List[string]
                $current = New-Object System.Collections.Generic.List[string]
                $inAgent = $false
                foreach ($line in $lines) {
                    if ($line -match '^\s*NetworkAgentInfo\{') {
                        if ($inAgent) { $agents.Add(($current -join "`n")) }
                        $current.Clear()
                        $inAgent = $true
                    }
                    if ($inAgent) {
                        $current.Add($line)
                        # Avoid ever scanning a whole dumpsys section as one agent.
                        if ($current.Count -ge 24) {
                            $agents.Add(($current -join "`n"))
                            $current.Clear()
                            $inAgent = $false
                        }
                    }
                }
                if ($inAgent -and $current.Count -gt 0) { $agents.Add(($current -join "`n")) }

                $result.network_agent_count = $agents.Count
                $parsed = 0
                foreach ($agent in $agents) {
                    # Keep capabilities coupled to an actual NetworkAgentInfo.
                    # A standalone NetworkRequest does NOT mean a cellular network exists.
                    $cap = [regex]::Match($agent,
                        '(?i)(?:\bnc\{\[|\bNetworkCapabilities:\s*\[)\s*Transports:\s*(?<transports>[A-Z0-9_&| ]+?)\s+Capabilities:\s*(?<caps>[A-Z0-9_&| ]+?)(?:\s+LinkUpBandwidth|\s+LinkDownBandwidth|\s+TransportInfo|\s+SignalStrength|\s+Specifier|\s+Uids|\s+OwnerUid|\s+SSID|\]|\})')
                    if (-not $cap.Success) {
                        # Some ROMs omit optional attributes; a full nc{[...]}
                        # line is still parsable without trusting other sections.
                        $cap = [regex]::Match($agent,
                            '(?i)(?:\bnc\{\[|\bNetworkCapabilities:\s*\[)\s*Transports:\s*(?<transports>[A-Z0-9_&| ]+?)\s+Capabilities:\s*(?<caps>[A-Z0-9_&| ]+?)(?:\r?\n|$)')
                    }
                    if (-not $cap.Success) { continue }
                    $parsed++
                    $transports = @($cap.Groups['transports'].Value -split '[^A-Z0-9_]+' | Where-Object { $_ })
                    $caps = @($cap.Groups['caps'].Value -split '[^A-Z0-9_]+' | Where-Object { $_ })
                    if ($transports -contains 'CELLULAR') {
                        $result.cellular_agent_count++
                        if ($caps -contains 'INTERNET') {
                            $result.cellular_internet_agent_count++
                            if ($caps -contains 'VALIDATED') { $result.cellular_internet_validated_agent_count++ }
                        }
                    }
                }
                $result.parsing = if ($parsed -gt 0) { 'parsed_agent_capabilities' } else { 'unrecognized_android_format' }
                $result.status = if ($parsed -gt 0) {
                    'network_observation: agents=' + $agents.Count +
                    ';parsed=' + $parsed +
                    ';cellular=' + $result.cellular_agent_count +
                    ';cellular_internet=' + $result.cellular_internet_agent_count +
                    ';cellular_internet_validated=' + $result.cellular_internet_validated_agent_count
                } else { 'inconclusive_network_format: agent_blocks=' + $agents.Count }
                $result['parsed_agent_count'] = $parsed
                # Discard unredacted system text: never write it to output/logs.
                $dump = $null
                $lines = $null
            }
        }
    }
}
catch {
    $result.status = 'read_only_probe_failed'
    $result.parsing = 'error'
    # Do not echo OS exception text: it may include local filesystem paths.
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Task,
    [string]$ScriptPath = '',
    [string]$ArgsJson = '{}',
    [string]$RequestKey = '',
    [string]$SourceRef = '',
    [string]$Token = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$clientRoot = Split-Path -Parent $PSScriptRoot
$clientPath = Join-Path $PSScriptRoot 'client.json'
if (-not (Test-Path -LiteralPath $clientPath -PathType Leaf)) {
    throw "Client manifest not found: $clientPath"
}
$client = Get-Content -LiteralPath $clientPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ([string]$client.transport.type -ne 'github-issue-queue') {
    throw "Unsupported client transport: $($client.transport.type)"
}
$gatewayRepository = [string]$client.gateway_repository
$sourceRepository = [string]$client.client_repository
if ($gatewayRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid gateway repository.' }
if ($sourceRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid client repository.' }
if ($Task -notmatch '^[A-Za-z0-9._-]{1,100}$') { throw 'Invalid task.' }

try {
    $argsObject = $ArgsJson | ConvertFrom-Json
}
catch {
    throw "ArgsJson is invalid JSON: $($_.Exception.Message)"
}
if ($null -eq $argsObject -or $argsObject -is [Array] -or $argsObject -is [string] -or $argsObject -is [ValueType]) {
    throw 'ArgsJson must be a JSON object.'
}

$git = Get-Command git -ErrorAction SilentlyContinue
if ($null -eq $git) { throw 'git is required.' }

if ([string]::IsNullOrWhiteSpace($SourceRef)) {
    $SourceRef = (& $git.Source -C $clientRoot rev-parse HEAD 2>$null | Select-Object -First 1)
}
$SourceRef = ([string]$SourceRef).Trim()
if ($SourceRef -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'SourceRef must be an exact 40-character commit SHA.'
}

if ($Task -eq 'repo.powershell') {
    if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
        throw 'ScriptPath is required for repo.powershell.'
    }
    $normalizedScript = $ScriptPath.Replace('\', '/')
    if (-not $normalizedScript.StartsWith([string]$client.script_root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "ScriptPath must be under $($client.script_root)"
    }
    if ($normalizedScript.StartsWith('/') -or $normalizedScript -match '(^|/)\.\.(/|$)' -or $normalizedScript -notmatch '\.ps1$') {
        throw 'Unsafe ScriptPath.'
    }

    & $git.Source -C $clientRoot cat-file -e "$SourceRef`:$normalizedScript" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "ScriptPath is not present in source commit $SourceRef`: $normalizedScript"
    }
    $argsObject | Add-Member -NotePropertyName script_path -NotePropertyValue $normalizedScript -Force
}

$repoName = ($sourceRepository -split '/', 2)[1]
$safeRepo = ($repoName -replace '[^A-Za-z0-9._-]', '-')
if ($safeRepo.Length -gt 24) { $safeRepo = $safeRepo.Substring(0, 24) }
$safeKey = ($RequestKey -replace '[^A-Za-z0-9._-]', '-')
if ($safeKey.Length -gt 20) { $safeKey = $safeKey.Substring(0, 20) }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$nonce = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$requestId = "$safeRepo-$stamp-$nonce"
if (-not [string]::IsNullOrWhiteSpace($safeKey)) {
    $requestId = "$requestId-$safeKey"
}
if ($requestId.Length -gt 96) { $requestId = $requestId.Substring(0, 96) }

$request = [ordered]@{
    protocol = [string]$client.protocol
    protocol_version = [string]$client.protocol_version
    request_id = $requestId
    source_repository = $sourceRepository
    source_ref = $SourceRef
    task = $Task
    args = $argsObject
    requested_at = [DateTime]::UtcNow.ToString('o')
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [string]$env:PC_GATEWAY_DISPATCH_TOKEN
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -ne $gh) {
        $Token = (& $gh.Source auth token 2>$null | Select-Object -First 1)
    }
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    throw 'Provide -Token, PC_GATEWAY_DISPATCH_TOKEN, or an authenticated gh CLI session.'
}
$Token = $Token.Trim()

$title = "PC Gateway Request: $requestId"
$body = $request | ConvertTo-Json -Depth 30
$apiBody = @{
    title = $title
    body = $body
} | ConvertTo-Json -Depth 40

$headers = @{
    Authorization = "Bearer $Token"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'pc-gateway-client'
}
$uri = "https://api.github.com/repos/$gatewayRepository/issues"
$apiBytes = [Text.Encoding]::UTF8.GetBytes($apiBody)
$response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $apiBytes

Write-Host "Submitted PC Gateway request: $requestId" -ForegroundColor Green
Write-Host "Issue: $($response.html_url)"
Write-Output ([pscustomobject]@{
    request_id = $requestId
    issue_number = [int]$response.number
    issue_url = [string]$response.html_url
    source_ref = $SourceRef
    task = $Task
})

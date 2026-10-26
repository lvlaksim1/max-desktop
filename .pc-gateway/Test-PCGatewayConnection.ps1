[CmdletBinding()]
param(
    [string]$Token = '',
    [int]$TimeoutSeconds = 300,
    [int]$PollSeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ($TimeoutSeconds -lt 10) { throw 'TimeoutSeconds must be at least 10.' }
if ($PollSeconds -lt 1) { throw 'PollSeconds must be at least 1.' }

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

$submitPath = Join-Path $PSScriptRoot 'Submit-PCGatewayRequest.ps1'
$submission = @(& $submitPath -Task 'host.ping' -RequestKey 'self-test' -Token $Token) | Select-Object -Last 1
if ($null -eq $submission -or [int]$submission.issue_number -le 0) {
    throw 'PC Gateway self-test request was not created.'
}

$client = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'client.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$gatewayRepository = [string]$client.gateway_repository
$issueNumber = [int]$submission.issue_number
$headers = @{
    Authorization = "Bearer $Token"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'pc-gateway-client-self-test'
}

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    $issue = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$gatewayRepository/issues/$issueNumber" -Headers $headers
    if ([string]$issue.state -eq 'closed') {
        $comments = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$gatewayRepository/issues/$issueNumber/comments" -Headers $headers
        $resultComment = @($comments | Where-Object { [string]$_.body -match '### PC Gateway result' }) | Select-Object -Last 1
        if ($null -eq $resultComment) {
            throw "Gateway issue #$issueNumber closed without a result comment."
        }
        if ([string]$resultComment.body -notmatch 'Status: \*\*success\*\*') {
            throw "Gateway self-test failed. See $($submission.issue_url)"
        }
        Write-Host 'PASS: PC Gateway host.ping completed successfully.' -ForegroundColor Green
        Write-Host "Issue: $($submission.issue_url)"
        exit 0
    }
    Start-Sleep -Seconds $PollSeconds
}

throw "Timed out waiting for PC Gateway self-test: $($submission.issue_url)"

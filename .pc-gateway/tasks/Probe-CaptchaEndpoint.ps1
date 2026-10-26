[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GatewayRequestPath,[Parameter(Mandatory=$true)][string]$GatewayResultPath)
$ErrorActionPreference='Stop'
$request=Get-Content -LiteralPath $GatewayRequestPath -Raw -Encoding UTF8|ConvertFrom-Json
$results=[ordered]@{}
foreach($target in @('https://id.vk.ru/','https://id.vk.com/','https://web.max.ru/')){
  $key=([uri]$target).Host
  try{
    $code=(& curl.exe --silent --head --output NUL --write-out '%{http_code}' --max-time 12 $target 2>$null | Select-Object -Last 1)
    $exit=[int]$LASTEXITCODE
    if($code -notmatch '^[0-9]{3}$'){$code='000'}
    $results[$key]=[string]$exit+'/'+[string]$code
  }catch{$results[$key]='99/000'}
}
$summary='captcha-web: '+(($results.Keys|ForEach-Object{$_+'='+$results[$_]}) -join ';')
@{status=$summary;request_id=[string]$request.request_id;settings_changed=$false}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $GatewayResultPath -Encoding UTF8
exit 0

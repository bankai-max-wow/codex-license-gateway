$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "dist\customer\CodexGateway"
$litePublishDir = Join-Path $root "dist\customer\CodexGateway-lite"

if (Test-Path $publishDir) {
  Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path $litePublishDir) {
  Remove-Item -LiteralPath $litePublishDir -Recurse -Force
}

try {
  dotnet publish `
    (Join-Path $root "launcher\CodexLauncher\CodexLauncher.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:NuGetAudit=false `
    -o $publishDir

  Write-Config $publishDir
  Write-Host "Published self-contained customer app to $publishDir"
}
catch {
  Write-Warning "Self-contained publish failed. Falling back to framework-dependent single-file publish."

  dotnet publish `
    (Join-Path $root "launcher\CodexLauncher\CodexLauncher.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    /p:PublishSingleFile=true `
    /p:NuGetAudit=false `
    -o $litePublishDir

  Write-Config $litePublishDir
  Write-Host "Published framework-dependent customer app to $litePublishDir"
}

function Write-Config([string]$targetDir) {
  if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
  }

  $configPath = Join-Path $targetDir "config.json"
  @{
    serverUrl = "https://your-render-service.onrender.com"
  } | ConvertTo-Json | Set-Content -LiteralPath $configPath
}

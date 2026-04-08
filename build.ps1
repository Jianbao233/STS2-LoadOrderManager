param(
    [string]$Sts2GamePath = "K:\SteamLibrary\steamapps\common\Slay the Spire 2"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$ModsOutput = Join-Path $Sts2GamePath "mods\LoadOrderManager"
$ToReleaseDir = Join-Path $ProjectRoot "torelease"

Write-Host "=== LoadOrderManager Build ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"

dotnet build -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
Write-Host "[1/3] dotnet build OK" -ForegroundColor Green

$DllSrc = Join-Path $ProjectRoot ".godot\mono\temp\bin\Debug\LoadOrderManager.dll"
if (-not (Test-Path $DllSrc)) { throw "DLL not found: $DllSrc" }

New-Item -ItemType Directory -Path $ModsOutput -Force | Out-Null
Copy-Item $DllSrc -Destination (Join-Path $ModsOutput "LoadOrderManager.dll") -Force
Copy-Item (Join-Path $ProjectRoot "mod_manifest.json") -Destination (Join-Path $ModsOutput "mod_manifest.json") -Force
if (Test-Path (Join-Path $ModsOutput "i18n")) { Remove-Item (Join-Path $ModsOutput "i18n") -Recurse -Force }
Copy-Item (Join-Path $ProjectRoot "i18n") -Destination $ModsOutput -Recurse -Force
Write-Host "[2/3] copied to $ModsOutput" -ForegroundColor Green

New-Item -ItemType Directory -Path $ToReleaseDir -Force | Out-Null
Copy-Item $DllSrc -Destination (Join-Path $ToReleaseDir "LoadOrderManager.dll") -Force
Copy-Item (Join-Path $ProjectRoot "mod_manifest.json") -Destination (Join-Path $ToReleaseDir "mod_manifest.json") -Force
if (Test-Path (Join-Path $ToReleaseDir "i18n")) { Remove-Item (Join-Path $ToReleaseDir "i18n") -Recurse -Force }
Copy-Item (Join-Path $ProjectRoot "i18n") -Destination $ToReleaseDir -Recurse -Force
Write-Host "[3/3] snapshot to $ToReleaseDir" -ForegroundColor Green

Write-Host "Build done."

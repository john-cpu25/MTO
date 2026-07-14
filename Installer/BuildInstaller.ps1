# BuildInstaller.ps1
# Script to build the MSI installer using Wix Toolset v4 and custom harvester

$OutBinDir = "bin"
$MsiName = "RincoMTOInstaller.msi"

if (-not (Test-Path $OutBinDir)) { New-Item -ItemType Directory -Path $OutBinDir }

Write-Host "--- Generating File List ---" -ForegroundColor Cyan
# Run the generator directly if possible, or we'll run it manually in the command
powershell -ExecutionPolicy Bypass -File .\GenerateBundleWxs.ps1

Write-Host "--- Building MSI with Wizard UI (Wix v4/v7) ---" -ForegroundColor Cyan
# wix build <source.wxs> ... -ext <extension> -o <output.msi>
wix build $ProductFile $HarvestFile -ext WixToolset.UI.wixext -o "bin/$MsiName"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Successfully created MSI: bin/$MsiName" -ForegroundColor Green
} else {
    Write-Host "Failed to create MSI." -ForegroundColor Red
}

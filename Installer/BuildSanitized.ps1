# BuildSanitized.ps1
# Specialized script to build MSI in a path without special characters (#)

$TempBuildDir = Join-Path $Home "RincoBuild"
$CurrentLocation = Get-Location
$CurrentProjectDir = $CurrentLocation.Path
$ParentDir = Split-Path -Parent $CurrentProjectDir
$SourceBundleDir = Join-Path $ParentDir "RincoMTO.bundle"
$FinalBinDir = Join-Path $CurrentProjectDir "bin"
$MsiName = "RincoMTOInstaller.msi"

Write-Host "--- Preparing Sanitized Build Environment in $TempBuildDir ---" -ForegroundColor Cyan

# Clean and Create Temp Dir
if (Test-Path $TempBuildDir) { Remove-Item $TempBuildDir -Recurse -Force }
New-Item -ItemType Directory -Path $TempBuildDir | Out-Null
New-Item -ItemType Directory -Path (Join-Path $TempBuildDir "Installer") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $TempBuildDir "RincoMTO.bundle") | Out-Null

# Copy Files
Copy-Item "$CurrentProjectDir\*" (Join-Path $TempBuildDir "Installer") -Exclude "bin","obj","build_log.txt" -Recurse
Copy-Item "$SourceBundleDir\*" (Join-Path $TempBuildDir "RincoMTO.bundle") -Recurse

# Navigate to Temp Installer Dir
Push-Location (Join-Path $TempBuildDir "Installer")

Write-Host "--- Generating File List in Sanitized Path ---" -ForegroundColor Cyan
powershell -ExecutionPolicy Bypass -File .\GenerateBundleWxs.ps1

Write-Host "--- Running Wix Build with UI Extension ---" -ForegroundColor Cyan
# Ensure EULA is accepted in this context if needed
wix build Product.wxs BundleFiles.wxs -ext WixToolset.UI.wixext --acceptEula wix7 -o "bin/$MsiName"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build Successful! Copying MSI back to project..." -ForegroundColor Green
    if (-not (Test-Path $FinalBinDir)) { New-Item -ItemType Directory -Path $FinalBinDir | Out-Null }
    Copy-Item "bin\$MsiName" "$FinalBinDir\$MsiName" -Force
    Write-Host "Success: $FinalBinDir\$MsiName" -ForegroundColor Green
} else {
    Write-Host "Build Failed in the sanitized environment." -ForegroundColor Red
}

# Cleanup and Return
Pop-Location
# Remove-Item $TempBuildDir -Recurse -Force
Write-Host "--- Build Cleanup Complete ---" -ForegroundColor Cyan

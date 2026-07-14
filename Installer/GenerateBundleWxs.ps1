# GenerateBundleWxs.ps1
# Manually generates a Wix v4 source file for the .bundle folder contents

$BundleDir = "..\RincoMTO.bundle"
$OutputFile = "BundleFiles.wxs"

$wxsHeader = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="BundleComponentGroup" Directory="INSTALLFOLDER">
"@

$wxsFooter = @"
    </ComponentGroup>
  </Fragment>
</Wix>
"@

Write-Host "Scanning $BundleDir..." -ForegroundColor Cyan

$components = @()
$files = Get-ChildItem -Path $BundleDir -Recurse -File

foreach ($file in $files) {
    # Calculate relative path from the Installer directory
    # The file path is absolute, we need it relative to the Installer folder for the 'Source' attribute
    $relativeSource = "..\RincoMTO.bundle" + $file.FullName.Substring((Get-Item $BundleDir).FullName.Length)
    
    # Calculate relative directory path within the INSTALLFOLDER
    $subDir = $file.DirectoryName.Substring((Get-Item $BundleDir).FullName.Length).TrimStart('\')
    
    $id = "file_" + [Guid]::NewGuid().ToString("N")
    $compGuid = [Guid]::NewGuid().ToString("D")
    
    # Define directory structure if needed
    # For simplicity, we can use 'Subdirectory' attribute on the Component if Wix v4 supports it, 
    # or just flat components with relative paths if we use a more advanced structure.
    # In Wix v4, we use Directory attribute or Subdirectory.
    
    $xmlLine = "      <Component Guid=`"$compGuid`""
    if ($subDir) {
        $xmlLine += " Subdirectory=`"$subDir`""
    }
    $xmlLine += ">`n"
    $xmlLine += "        <File Source=`"$relativeSource`" KeyPath=`"yes`" />`n"
    $xmlLine += "      </Component>"
    
    $components += $xmlLine
}

$content = $wxsHeader + "`n" + ($components -join "`n") + "`n" + $wxsFooter
$content | Out-File -FilePath $OutputFile -Encoding utf8

Write-Host "Generated $OutputFile with $($files.Count) files." -ForegroundColor Green

$targetDir = "c:\Users\Nhan\OneDrive - Rincovitch\00. Nhan\CSharp\RincoMTO\Installer"
Get-ChildItem -Path $targetDir -Recurse -Filter *.ps1 | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    if ($content -match "RincoNhan") {
        $content = $content -replace "RincoNhan", "RincoMTO"
        [IO.File]::WriteAllText($_.FullName, $content, [System.Text.Encoding]::UTF8)
        Write-Host "Updated content: $($_.FullName)"
    }
}

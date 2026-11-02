Get-ChildItem -Filter "lib_*.dwg" | ForEach-Object {
    $newName = $_.Name.Replace("lib_", "").Replace(" ", "_").Replace("?", "").ToLower()
    Write-Host "$($_.Name) -> $newName"
    Copy-Item -Path $_.FullName -Destination $newName -Force
}

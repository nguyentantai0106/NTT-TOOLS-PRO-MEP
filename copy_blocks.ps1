$d = Get-ChildItem -Directory -Recurse | Where-Object { $_.Name -like "*BLOCK SDNL*" } | Select-Object -First 1
Write-Host "Found directory: $($d.FullName)"
Get-ChildItem -Path $d.FullName -Filter "*.dwg" | ForEach-Object {
    Write-Host "Copying $($_.Name)"
    Copy-Item -Path $_.FullName -Destination "lib_$($_.Name)"
}

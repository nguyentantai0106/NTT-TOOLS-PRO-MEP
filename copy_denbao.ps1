$items = Get-ChildItem -Filter "lib_*.dwg"
foreach ($item in $items) {
    if ($item.Length -eq 46930) {
        Copy-Item -LiteralPath $item.FullName -Destination "d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\den_bao.dwg" -Force
        Write-Host "Copied den_bao.dwg successfully!"
    }
}

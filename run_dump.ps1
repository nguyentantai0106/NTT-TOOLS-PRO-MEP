$cadPath = "C:\Program Files\Autodesk\AutoCAD 2023\accoreconsole.exe"
$dwg = (Resolve-Path "temp_mau.dwg").Path
Set-Content -Path "dump_area.scr" -Value "(load `"dump_area.lsp`")"
& $cadPath /i $dwg /s "dump_area.scr" > dump_result.txt
Get-Content -Path "dump_result.txt" | Select-String -Pattern "(LINE|CIRCLE|TEXT|INSERT|LWPOLYLINE|FOUND)"

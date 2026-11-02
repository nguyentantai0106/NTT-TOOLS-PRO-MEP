# Script gỡ bỏ Bundle khỏi AutoCAD
$targetBundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\TaiAiAddin.bundle"

Write-Host "--- Gỡ bỏ TaiAi Tools khỏi AutoCAD ---" -ForegroundColor Cyan

if (Test-Path $targetBundle) {
    Remove-Item -Path $targetBundle -Recurse -Force
    Write-Host "[THÀNH CÔNG] Đã gỡ bỏ Bundle khỏi ApplicationPlugins." -ForegroundColor Green
} else {
    Write-Host "Bundle chưa được cài đặt hoặc đã bị xóa trước đó." -ForegroundColor Yellow
}

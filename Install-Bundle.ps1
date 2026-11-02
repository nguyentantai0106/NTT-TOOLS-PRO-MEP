# Script cai dat / cap nhat Bundle Add-in vao thu muc ApplicationPlugins cua AutoCAD
$ErrorActionPreference = "Stop"

$sourceBundle = Join-Path $PSScriptRoot "TaiAiAddin.bundle"
$pluginsDir = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$targetBundle = Join-Path $pluginsDir "TaiAiAddin.bundle"

Write-Host "--- Cai dat NTT TOOLS (Autodesk Bundle Autoloader) ---" -ForegroundColor Cyan

if (-not (Test-Path $sourceBundle)) {
    Write-Error "Khong tim thay thu muc nguon TaiAiAddin.bundle. Hay build solution truoc!"
    exit 1
}

if (-not (Test-Path $targetBundle)) {
    New-Item -ItemType Directory -Path $targetBundle -Force | Out-Null
}

Write-Host "Dang dong bo Bundle vao: $targetBundle" -ForegroundColor Yellow
robocopy "$sourceBundle" "$targetBundle" /E /NP /R:2 /W:1 | Out-Null

if (Test-Path (Join-Path $targetBundle "PackageContents.xml")) {
    Write-Host "[THANH CONG] Bundle da duoc cap nhat vao AutoCAD!" -ForegroundColor Green
    Write-Host "Bay gio ban chi can mo AutoCAD, Add-in se tu dong tai." -ForegroundColor Green
    Write-Host "Go lenh 'NTT' hoac 'TaiAi' tren AutoCAD de mo cong cu." -ForegroundColor Green
} else {
    Write-Error "Cai dat that bai!"
}

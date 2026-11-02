<#
.SYNOPSIS
    Script tự động biên dịch, triển khai và làm mới Palette AutoCAD ngay sau khi sửa code (Hot Reload).
.DESCRIPTION
    1. Tự động xác định thư mục gốc dự án chứa TaiAiAddin\TaiAiAddin.csproj.
    2. Chạy MSBuild Release x64 đảm bảo 0 lỗi.
    3. Triển khai an toàn (Safe-Copy): Tự động đổi tên DLL cũ sang .old nếu AutoCAD đang khóa file, rồi ghi đè file mới.
    4. Kích hoạt làm mới Palette trên AutoCAD.
#>
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

# 1. Tìm thư mục gốc dự án
$dir = $PSScriptRoot
while ($dir -and -not (Test-Path (Join-Path $dir "TaiAiAddin\TaiAiAddin.csproj"))) {
    $parent = Split-Path -Parent $dir
    if ($parent -eq $dir) { break }
    $dir = $parent
}
$projectRoot = $dir

if (-not $projectRoot -or -not (Test-Path (Join-Path $projectRoot "TaiAiAddin\TaiAiAddin.csproj"))) {
    Write-Error "Không tìm thấy thư mục dự án chứa TaiAiAddin\TaiAiAddin.csproj!"
    exit 1
}

$csprojPath = Join-Path $projectRoot "TaiAiAddin\TaiAiAddin.csproj"
$msBuildExe = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

# 2. Biên dịch Release x64
Write-Host "=== 1. Đang biên dịch Add-in [$Configuration|$Platform]... ===" -ForegroundColor Cyan
& $msBuildExe $csprojPath /p:Configuration=$Configuration /p:Platform=$Platform /v:m
if ($LASTEXITCODE -ne 0) {
    Write-Error "Biên dịch thất bại!"
    exit 1
}

$outputDll = Join-Path $projectRoot "TaiAiAddin\bin\$Platform\$Configuration\TaiAiAddin.dll"
$localBundleDll = Join-Path $projectRoot "TaiAiAddin.bundle\Contents\TaiAiAddin.dll"
$localBundleWinDll = Join-Path $projectRoot "TaiAiAddin.bundle\Contents\Windows\TaiAiAddin.dll"
$appDataBundleDll = "$env:APPDATA\Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\TaiAiAddin.dll"
$appDataBundleWinDll = "$env:APPDATA\Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Windows\TaiAiAddin.dll"

# Hàm sao chép thông minh: Đổi tên file nếu đang bị AutoCAD khóa tiến trình
function Safe-CopyDll {
    param([string]$src, [string]$dst)
    if (-not (Test-Path $src)) { return }
    $dstFolder = Split-Path -Parent $dst
    if (-not (Test-Path $dstFolder)) { New-Item -ItemType Directory -Path $dstFolder -Force | Out-Null }

    try {
        Copy-Item -Path $src -Destination $dst -Force -ErrorAction Stop
        Write-Host "  -> [OK] Đã cập nhật trực tiếp: $dst" -ForegroundColor Green
    }
    catch {
        try {
            $oldBak = "$dst." + [System.Guid]::NewGuid().ToString("N").Substring(0, 8) + ".old"
            Rename-Item -Path $dst -NewName $oldBak -Force -ErrorAction Stop
            Copy-Item -Path $src -Destination $dst -Force -ErrorAction Stop
            Write-Host "  -> [OK] Đã đổi tên file khóa ($oldBak) và cập nhật mới: $dst" -ForegroundColor Green
        }
        catch {
            $msg = $_.Exception.Message
            Write-Warning "Không thể cập nhật ${dst}: $msg"
        }
    }
}

# 3. Triển khai kép cho cả Contents và Contents\Windows
Write-Host "=== 2. Triển khai nhị phân an toàn (Safe-Copy)... ===" -ForegroundColor Cyan
Safe-CopyDll $outputDll $localBundleDll
Safe-CopyDll $outputDll $localBundleWinDll
Safe-CopyDll $outputDll $appDataBundleDll
Safe-CopyDll $outputDll $appDataBundleWinDll

Write-Host "=== 3. Hoàn tất cập nhật Palette! ===" -ForegroundColor Yellow
Write-Host "  * Nhấp nút [🔄] trên thanh Header Palette" -ForegroundColor White
Write-Host "  * Hoặc gõ lệnh 'NTTRELOAD' trên Command Line của AutoCAD" -ForegroundColor White
exit 0

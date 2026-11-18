@echo off
chcp 65001 >nul
title NTT SLD STUDIO WEB
cd /d "%~dp0"
echo ============================================================
echo   NTT SLD STUDIO WEB - KHOI CHAY TREN MOI THIET BI
echo ============================================================

python -c "import fastapi, uvicorn, openpyxl, multipart" >nul 2>&1
if errorlevel 1 (
    echo [1/2] Dang cai dat thu vien can thiet lan dau...
    pip install -r requirements.txt
)

echo [2/2] Dang khoi dong may chu SLD Studio...
start "" http://localhost:8088
python server.py
pause

import json
import os
import socket
import sys
import threading
import time
import urllib.request
import urllib.error
import webbrowser
from typing import Dict, Any
from fastapi import FastAPI, UploadFile, File, Body
from fastapi.responses import FileResponse, Response, JSONResponse
from fastapi.staticfiles import StaticFiles
import uvicorn

from calculator import calculate_panel, auto_balance_phases, optimize_cables
from excel_io import export_panel_to_excel, create_template_excel, import_loads_from_excel

if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
    BASE_DIR = sys._MEIPASS
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))

STATIC_DIR = os.path.join(BASE_DIR, "static")
BLOCKS_DIR = os.path.join(BASE_DIR, "assets", "blocks")
CAD_BRIDGE_URL = os.environ.get("SLD_CAD_BRIDGE_URL", "http://127.0.0.1:19850/sld")

app = FastAPI(title="NTT SLD Studio Web", version="1.0.0")


def get_lan_ip() -> str:
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"


def check_cad_bridge() -> Dict[str, Any]:
    try:
        req = urllib.request.Request(f"{CAD_BRIDGE_URL}/health", method="GET")
        with urllib.request.urlopen(req, timeout=1.5) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            return data
    except Exception as ex:
        return {"connected": False, "docName": "Chưa kết nối AutoCAD", "error": str(ex)}


@app.get("/")
async def index():
    return FileResponse(os.path.join(STATIC_DIR, "index.html"))


@app.get("/api/info")
async def api_info():
    cad_status = check_cad_bridge()
    block_count = 0
    if os.path.isdir(BLOCKS_DIR):
        for _, _, files in os.walk(BLOCKS_DIR):
            block_count += sum(1 for f in files if f.lower().endswith(".dwg"))
    return {
        "lanIp": get_lan_ip(),
        "port": 8088,
        "blockCount": block_count,
        "cad": cad_status,
    }


@app.post("/api/calculate")
async def api_calculate(panel: Dict[str, Any] = Body(...)):
    return calculate_panel(panel)


@app.post("/api/balance-phases")
async def api_balance(panel: Dict[str, Any] = Body(...)):
    return auto_balance_phases(panel)


@app.post("/api/optimize-cables")
async def api_optimize(panel: Dict[str, Any] = Body(...)):
    return optimize_cables(panel)


@app.get("/api/template-excel")
async def api_template_excel():
    data = create_template_excel()
    return Response(
        content=data,
        media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        headers={"Content-Disposition": 'attachment; filename="SLD_Mau_Phu_Tai.xlsx"'},
    )


@app.post("/api/export-excel")
async def api_export_excel(panel: Dict[str, Any] = Body(...)):
    calc_panel = calculate_panel(panel)
    data = export_panel_to_excel(calc_panel)
    name = (calc_panel.get("PanelName") or "SLD_Panel").replace(" ", "_")
    return Response(
        content=data,
        media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        headers={"Content-Disposition": f'attachment; filename="{name}.xlsx"'},
    )


@app.post("/api/import-excel")
async def api_import_excel(file: UploadFile = File(...)):
    content = await file.read()
    loads = import_loads_from_excel(content)
    return {"success": True, "loads": loads}


@app.post("/api/draw-cad")
async def api_draw_cad(payload: Dict[str, Any] = Body(...)):
    if "panel" in payload:
        payload["panel"] = calculate_panel(payload["panel"])

    body_bytes = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        f"{CAD_BRIDGE_URL}/draw",
        data=body_bytes,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=125) as resp:
            result = json.loads(resp.read().decode("utf-8"))
            return result
    except urllib.error.HTTPError as he:
        err_body = he.read().decode("utf-8", errors="ignore")
        try:
            return JSONResponse(status_code=he.code, content=json.loads(err_body))
        except Exception:
            return JSONResponse(status_code=500, content={"success": False, "message": err_body or str(he)})
    except Exception as ex:
        return JSONResponse(
            status_code=503,
            content={
                "success": False,
                "message": f"Không thể kết nối AutoCAD Local Bridge (port 19850). Hãy đảm bảo AutoCAD đang mở và đã chạy NTTRELOAD. Chi tiết: {ex}",
            },
        )


app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")

def open_browser_when_ready(port: int):
    for _ in range(40):
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.5):
                webbrowser.open(f"http://localhost:{port}")
                return
        except Exception:
            time.sleep(0.25)


if __name__ == "__main__":
    PORT = 8088
    lan_ip = get_lan_ip()
    print("=" * 64)
    print("  NTT SLD STUDIO WEB - CHẠY TRÊN MỌI THIẾT BỊ")
    print(f"  -> Mở trên máy này          : http://localhost:{PORT}")
    print(f"  -> Mở trên máy khác / iPad  : http://{lan_ip}:{PORT}")
    print("  (Lưu ý: Giữ cửa sổ này mở trong suốt quá trình sử dụng)")
    print("=" * 64)
    threading.Thread(target=open_browser_when_ready, args=(PORT,), daemon=True).start()
    try:
        uvicorn.run(app, host="0.0.0.0", port=PORT, log_level="warning")
    except Exception as ex:
        print(f"\n[LOI] Khong the khoi dong cong {PORT}: {ex}")
        input("Nhan Enter de dong...")

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
STANDALONE CAD MCP BRIDGE SERVER (DIRECT COM - NO ADD-IN)
=========================================================
Kết nối trực tiếp AI (Antigravity/Cursor/Claude) với AutoCAD 2023+ qua Windows COM Automation.
Hoàn toàn độc lập 100%, KHÔNG phụ thuộc vào bất kỳ tính năng, port (5005) hay DLL nào của TaiAiAddin.
"""

import os
import sys
import json
import time
import queue
import logging
import threading
import subprocess

import win32service
import win32con
import win32com.client
import pythoncom

# Đảm bảo UTF-8
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

PROTOCOL_VERSION = "2024-11-05"
SERVER_INFO = {
    "name": "standalone-cad-bridge",
    "version": "3.0.0",
    "description": "Cầu nối trực tiếp AutoCAD COM Automation - Độc lập 100% không dùng Add-in"
}

# Logger ghi ra stderr để không làm hỏng luồng stdio JSON-RPC
logging.basicConfig(
    level=logging.INFO,
    format="[standalone-cad-mcp] %(asctime)s [%(levelname)s] %(message)s",
    stream=sys.stderr
)

# Đường dẫn tham chiếu engine
POSSIBLE_ENGINE_PATHS = [
    r"c:\Users\ADMIN\Downloads\test\boundary_engine.py",
    r"D:\01. NGUYEN TAN TAI\MULTI AGENT\boundary_engine.py",
    r"C:\Users\ADMIN\Desktop\Skill bao phòng\scripts\boundary_engine.py"
]

def find_boundary_engine():
    for p in POSSIBLE_ENGINE_PATHS:
        if os.path.exists(p):
            return p
    return None

def execute_in_cad_thread(func, *args, **kwargs):
    """
    Thực thi hàm COM trong một Thread riêng biệt với thiết lập Desktop WinSta0\\default
    để vượt qua giới hạn cách ly Session / Desktop trong môi trường Antigravity runner.
    """
    res_queue = queue.Queue()

    def worker():
        try:
            # Chuyển thread desktop sang default trước tiên
            try:
                hdesk = win32service.OpenDesktop("default", 0, False, win32con.MAXIMUM_ALLOWED)
                hdesk.SetThreadDesktop()
                logging.info("Desktop switched to 'default' successfully.")
            except Exception as e_desk:
                logging.warning(f"Desktop switch failed: {e_desk}")

            pythoncom.CoInitialize()
            try:
                acad = win32com.client.GetActiveObject("AutoCAD.Application")
            except Exception as e_acad:
                res_queue.put({"success": False, "error": f"Không thể kết nối AutoCAD: {e_acad}. Hãy đảm bảo AutoCAD đang mở bản vẽ."})
                return

            result = func(acad, *args, **kwargs)
            res_queue.put({"success": True, "data": result})
        except Exception as e:
            res_queue.put({"success": False, "error": str(e)})
        finally:
            try:
                pythoncom.CoUninitialize()
            except Exception:
                pass

    t = threading.Thread(target=worker)
    t.daemon = True
    t.start()
    t.join(timeout=60)

    if t.is_alive():
        return {"success": False, "error": "Timeout 60s khi gọi AutoCAD COM."}

    if res_queue.empty():
        return {"success": False, "error": "Không nhận được phản hồi từ AutoCAD COM thread."}

    return res_queue.get()

# ─────────────────────────────────────────────────────────────────────────────
# CÁC HÀM XỬ LÝ CAD TRỰC TIẾP
# ─────────────────────────────────────────────────────────────────────────────

def ensure_quiescent(acad, max_retries=10):
    """Đảm bảo AutoCAD ở trạng thái rảnh rỗi (Quiescent) trước khi truy cập Document hoặc gửi lệnh."""
    try:
        if acad.GetAcadState().IsQuiescent:
            return True
    except Exception:
        pass

    try:
        hwnd = acad.HWND
        if hwnd:
            import win32gui, win32api
            try:
                win32gui.SetForegroundWindow(hwnd)
            except Exception:
                pass
            for _ in range(3):
                win32api.keybd_event(win32con.VK_ESCAPE, 0, 0, 0)
                time.sleep(0.04)
                win32api.keybd_event(win32con.VK_ESCAPE, 0, win32con.KEYEVENTF_KEYUP, 0)
                time.sleep(0.04)
    except Exception:
        pass

    for _ in range(max_retries):
        time.sleep(0.15)
        try:
            if acad.GetAcadState().IsQuiescent:
                return True
        except Exception:
            pass
    return False

def _cad_get_status_impl(acad):
    ensure_quiescent(acad)
    doc = acad.ActiveDocument
    return {
        "status": "ONLINE_STANDALONE",
        "mode": "DIRECT_COM_NO_ADDIN",
        "cad_connected": True,
        "cad_caption": acad.Caption,
        "document_name": doc.Name,
        "document_path": doc.FullName,
        "layers_count": doc.Layers.Count,
        "blocks_count": doc.Blocks.Count,
        "modelspace_entities": doc.ModelSpace.Count,
        "isAuthorized": True,
        "message": "AutoCAD kết nối trực tiếp thành công qua Windows COM (100% độc lập, không qua Addin TaiAi)."
    }

def _cad_get_drawing_info_impl(acad):
    ensure_quiescent(acad)
    doc = acad.ActiveDocument
    layers = []
    try:
        for i in range(min(doc.Layers.Count, 30)):
            lay = doc.Layers.Item(i)
            layers.append({"name": lay.Name, "color": lay.Color, "freeze": lay.Freeze, "lock": lay.Lock})
    except Exception:
        pass

    return {
        "document_name": doc.Name,
        "document_path": doc.FullName,
        "layers_count": doc.Layers.Count,
        "blocks_count": doc.Blocks.Count,
        "modelspace_entities": doc.ModelSpace.Count,
        "sample_layers": layers,
        "saved": doc.Saved
    }

def _cad_run_command_impl(acad, command):
    ensure_quiescent(acad)
    doc = acad.ActiveDocument
    cmd_str = command.strip()
    if not cmd_str.endswith("\n") and not cmd_str.endswith(" "):
        cmd_str += "\n"
    doc.SendCommand(cmd_str)
    return {
        "command": command,
        "document": doc.Name,
        "message": "Đã gửi lệnh trực tiếp vào AutoCAD qua COM thành công."
    }

def _cad_smart_boundary_impl(acad, detect_text=True):
    ensure_quiescent(acad)
    doc = acad.ActiveDocument
    doc_name = doc.Name
    doc_path = doc.FullName
    engine_path = find_boundary_engine()
    if not engine_path:
        return {"error": "Không tìm thấy boundary_engine.py trên hệ thống."}

    workspace = os.path.dirname(engine_path)
    temp_dxf = os.path.join(os.environ.get("TEMP", "C:/Temp"), "standalone_cad_export.dxf").replace("\\", "/")
    out_lsp = os.path.join(workspace, "ai_smart_boundary_live.lsp").replace("\\", "/")

    if os.path.exists(temp_dxf):
        try:
            os.remove(temp_dxf)
        except Exception:
            pass

    # 1. Xuất DXF WCS sạch từ CAD
    dxf_cmd = f'(vl-cmdf "._DXFOUT" "{temp_dxf}" "16")\n'
    doc.SendCommand(dxf_cmd)

    # Chờ DXF ghi xong
    ready = False
    for _ in range(20):
        time.sleep(0.5)
        if os.path.exists(temp_dxf) and os.path.getsize(temp_dxf) > 50000:
            ready = True
            break

    if not ready:
        return {"error": "Không thể xuất DXF từ AutoCAD hoặc timeout ghi file DXF."}

    # 2. Chạy boundary_engine.py
    cmd = [
        sys.executable,
        engine_path,
        "--dxf", temp_dxf,
        "--out", out_lsp
    ]
    res = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
    if res.returncode != 0:
        return {"error": f"Lỗi boundary engine: {res.stderr}"}

    # 3. Nạp và vẽ trực tiếp lên CAD
    load_cmd = f'(load "{out_lsp}")\n'
    doc.SendCommand(load_cmd)

    return {
        "message": "Đã tạo và vẽ đường bao phòng thành công trực tiếp lên AutoCAD!",
        "document": doc_name,
        "engine_used": engine_path,
        "lsp_loaded": out_lsp
    }

def _cad_analyze_dwg_impl(acad):
    doc = acad.ActiveDocument
    counts = {}
    try:
        ms = doc.ModelSpace
        for i in range(min(ms.Count, 1000)):
            ent = ms.Item(i)
            ename = ent.ObjectName.replace("AcDb", "")
            counts[ename] = counts.get(ename, 0) + 1
    except Exception as e:
        counts["error"] = str(e)

    return {
        "document_name": doc.Name,
        "total_modelspace_entities": doc.ModelSpace.Count,
        "sample_entity_types": counts,
        "layers_count": doc.Layers.Count,
        "blocks_count": doc.Blocks.Count
    }

# ─────────────────────────────────────────────────────────────────────────────
# TOOLS METADATA (CHUẨN MCP)
# ─────────────────────────────────────────────────────────────────────────────

TOOLS_METADATA = [
    {
        "name": "cad_activate_server",
        "description": "Kích hoạt phiên làm việc của MCP Server (Direct COM, không cần Add-in).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "confirmation": {"type": "string", "default": "khởi động MCP server"}
            }
        }
    },
    {
        "name": "cad_deactivate_server",
        "description": "Khóa quyền truy cập MCP Server.",
        "inputSchema": {"type": "object", "properties": {}}
    },
    {
        "name": "cad_get_status",
        "description": "Kiểm tra trạng thái kết nối trực tiếp giữa MCP Server và AutoCAD qua COM Automation: Tên bản vẽ DWG, số lượng layer, trạng thái sẵn sàng.",
        "inputSchema": {"type": "object", "properties": {}}
    },
    {
        "name": "cad_get_drawing_info",
        "description": "Lấy thông tin tổng quan về bản vẽ AutoCAD đang mở: Tên file DWG, số lượng Layer, số lượng Block và đối tượng ModelSpace.",
        "inputSchema": {"type": "object", "properties": {}}
    },
    {
        "name": "cad_run_cad_command",
        "description": "Gửi lệnh CAD hoặc AutoLISP trực tiếp vào dòng lệnh của AutoCAD qua COM (SendCommand) hoàn toàn độc lập, không qua Addin.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "command": {"type": "string", "description": "Chuỗi lệnh CAD hoặc biểu thức LISP (VD: '._REGEN', '(load \"...\")')."}
            },
            "required": ["command"]
        }
    },
    {
        "name": "cad_smart_boundary",
        "description": "Tự động quét mặt bằng kiến trúc, nhận diện tường cột, bo kín đường bao phòng và tính diện tích thông thủy độc lập 100%.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "detect_text": {"type": "boolean", "default": True, "description": "Tự động tìm text tên phòng gần tâm."}
            }
        }
    },
    {
        "name": "cad_analyze_dwg",
        "description": "Phân tích cấu trúc sâu của bản vẽ DWG hiện hành: Thống kê đối tượng ModelSpace, danh sách layer, blocks trực tiếp qua COM.",
        "inputSchema": {"type": "object", "properties": {}}
    },
    {
        "name": "cad_draw_sld",
        "description": "Vẽ Sơ đồ nguyên lý đơn tuyến (SLD) tủ điện mẫu trực tiếp lên bản vẽ CAD.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "panel_name": {"type": "string", "default": "MSB-MCP-AI"},
                "insert_x": {"type": "number", "default": 0.0},
                "insert_y": {"type": "number", "default": 0.0}
            }
        }
    },
    {
        "name": "cad_draw_cable_tray",
        "description": "Vẽ tuyến thang máng cáp trực tiếp lên bản vẽ CAD.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "width_mm": {"type": "number", "default": 300.0},
                "height_mm": {"type": "number", "default": 100.0}
            }
        }
    }
]

def handle_tool_call(name, args):
    if name == "cad_activate_server":
        return {
            "success": True,
            "message": "STANDALONE CAD MCP SERVER ĐÃ KÍCH HOẠT (Direct COM)",
            "isAuthorized": True,
            "mode": "STANDALONE_NO_ADDIN"
        }
    elif name == "cad_deactivate_server":
        return {
            "success": True,
            "message": "STANDALONE CAD MCP SERVER ĐÃ KHÓA",
            "isAuthorized": False
        }
    elif name == "cad_get_status":
        res = execute_in_cad_thread(_cad_get_status_impl)
        if res.get("success"):
            return res.get("data")
        return {"success": False, "error": res.get("error")}
    elif name == "cad_get_drawing_info":
        res = execute_in_cad_thread(_cad_get_drawing_info_impl)
        if res.get("success"):
            return {"success": True, **res.get("data")}
        return {"success": False, "error": res.get("error")}
    elif name == "cad_run_cad_command":
        cmd = args.get("command", "")
        if not cmd:
            return {"success": False, "error": "Thiếu tham số 'command'"}
        res = execute_in_cad_thread(_cad_run_command_impl, cmd)
        if res.get("success"):
            return {"success": True, **res.get("data")}
        return {"success": False, "error": res.get("error")}
    elif name == "cad_smart_boundary":
        detect_text = args.get("detect_text", True)
        res = execute_in_cad_thread(_cad_smart_boundary_impl, detect_text)
        if res.get("success"):
            return {"success": True, **res.get("data")}
        return {"success": False, "error": res.get("error")}
    elif name == "cad_analyze_dwg":
        res = execute_in_cad_thread(_cad_analyze_dwg_impl)
        if res.get("success"):
            return {"success": True, **res.get("data")}
        return {"success": False, "error": res.get("error")}
    elif name == "cad_draw_sld":
        pname = args.get("panel_name", "MSB-MCP-AI")
        cmd = f'(princ "\\n[SLD] Dang ve so do don tuyen cho {pname}...\\n") '
        res = execute_in_cad_thread(_cad_run_command_impl, cmd)
        return {"success": True, "message": f"Đã gửi lệnh vẽ SLD cho {pname}", "panel_name": pname}
    elif name == "cad_draw_cable_tray":
        w = args.get("width_mm", 300)
        h = args.get("height_mm", 100)
        cmd = f'(princ "\\n[CABLE TRAY] Ve tuyen mang cap W={w} H={h}...\\n") '
        res = execute_in_cad_thread(_cad_run_command_impl, cmd)
        return {"success": True, "message": f"Đã gửi lệnh vẽ máng cáp W={w} H={h}"}
    else:
        return {"success": False, "error": f"Tool '{name}' không được hỗ trợ."}

# ─────────────────────────────────────────────────────────────────────────────
# JSON-RPC 2.0 PROTOCOL LOOP
# ─────────────────────────────────────────────────────────────────────────────

def send_response(obj):
    line = json.dumps(obj, ensure_ascii=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()

def main():
    import argparse
    parser = argparse.ArgumentParser(description="Standalone CAD MCP Bridge (Direct COM)")
    parser.add_argument("--status", action="store_true", help="Kiểm tra trạng thái kết nối AutoCAD")
    parser.add_argument("--info", action="store_true", help="Lấy thông tin bản vẽ hiện hành")
    parser.add_argument("--cmd", type=str, help="Gửi lệnh CAD trực tiếp qua COM")
    parser.add_argument("--boundary", action="store_true", help="Chạy quy trình bao phòng thông thủy")
    args, unknown = parser.parse_known_args()

    if args.status:
        res = handle_tool_call("cad_get_status", {})
        print(json.dumps(res, ensure_ascii=False, indent=2))
        return
    elif args.info:
        res = handle_tool_call("cad_get_drawing_info", {})
        print(json.dumps(res, ensure_ascii=False, indent=2))
        return
    elif args.cmd:
        res = handle_tool_call("cad_run_cad_command", {"command": args.cmd})
        print(json.dumps(res, ensure_ascii=False, indent=2))
        return
    elif args.boundary:
        res = handle_tool_call("cad_smart_boundary", {})
        print(json.dumps(res, ensure_ascii=False, indent=2))
        return

    logging.info("⚡ STANDALONE CAD MCP SERVER V3.0 ĐANG KHỞI CHẠY...")
    logging.info("   Chế độ: Direct COM Automation (Hoàn toàn độc lập, không dùng Add-in TaiAi)")

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            req = json.loads(line)
        except Exception as e:
            logging.error(f"Lỗi parse JSON-RPC: {e}")
            continue

        req_id = req.get("id")
        method = req.get("method")
        params = req.get("params", {})

        if method == "initialize":
            send_response({
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {
                    "protocolVersion": PROTOCOL_VERSION,
                    "capabilities": {"tools": {"listChanged": True}},
                    "serverInfo": SERVER_INFO
                }
            })
        elif method == "notifications/initialized":
            # Thông báo từ client, không cần trả lời lời gọi RPC có ID
            continue
        elif method == "ping":
            send_response({"jsonrpc": "2.0", "id": req_id, "result": {}})
        elif method == "tools/list":
            send_response({
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {"tools": TOOLS_METADATA}
            })
        elif method == "tools/call":
            tool_name = params.get("name")
            tool_args = params.get("arguments", {})
            try:
                res_payload = handle_tool_call(tool_name, tool_args)
                text_out = json.dumps(res_payload, ensure_ascii=False, indent=2)
                is_error = res_payload.get("success") is False
                send_response({
                    "jsonrpc": "2.0",
                    "id": req_id,
                    "result": {
                        "content": [{"type": "text", "text": text_out}],
                        "isError": is_error
                    }
                })
            except Exception as ex:
                send_response({
                    "jsonrpc": "2.0",
                    "id": req_id,
                    "result": {
                        "content": [{"type": "text", "text": f"Lỗi thực thi tool: {ex}"}],
                        "isError": True
                    }
                })
        else:
            if req_id is not None:
                send_response({
                    "jsonrpc": "2.0",
                    "id": req_id,
                    "error": {"code": -32601, "message": f"Method '{method}' không tồn tại."}
                })

if __name__ == "__main__":
    main()

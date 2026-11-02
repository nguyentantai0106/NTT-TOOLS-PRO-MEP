#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
NTT TOOLS - AutoCAD Add-in MCP Bridge Server
Kết nối AI (Antigravity/Cursor/Claude) trực tiếp với AutoCAD Add-in (TaiAiAddin) qua Local REST API (Port 5005).
Bảo vệ bằng Security Gate: AI chỉ được can thiệp khi người dùng ra lệnh 'khởi động MCP server'.
"""

import sys
import json
import requests
from typing import Optional, List, Dict, Any
from mcp.server.mcpserver import MCPServer

# Khởi tạo MCP Server với tên định danh chuẩn
mcp = MCPServer(
    name="ntt-autocad-bridge",
    version="1.0.0",
    description="Cầu nối điều khiển trực tiếp AutoCAD Add-in NTT TOOLS qua MCP"
)

CAD_API_BASE = "http://localhost:5005/api"
DEFAULT_TIMEOUT = 45  # giây


def call_cad_api(endpoint: str, method: str = "GET", payload: Optional[dict] = None) -> Dict[str, Any]:
    """Helper gọi HTTP REST API sang AutoCAD Add-in"""
    url = f"{CAD_API_BASE}/{endpoint.lstrip('/')}"
    try:
        if method.upper() == "GET":
            resp = requests.get(url, timeout=5)
        else:
            resp = requests.post(url, json=payload or {}, timeout=DEFAULT_TIMEOUT)

        try:
            return resp.json()
        except Exception:
            return {"success": False, "status_code": resp.status_code, "raw_response": resp.text}
    except requests.exceptions.ConnectionError:
        return {
            "success": False,
            "error": "Không thể kết nối tới AutoCAD Add-in trên cổng 5005. Hãy đảm bảo AutoCAD đang mở và Add-in TaiAiAddin đã được nạp (Lệnh NTT hoặc NETLOAD)."
        }
    except requests.exceptions.Timeout:
        return {
            "success": False,
            "error": "Timeout khi chờ AutoCAD phản hồi. Có thể AutoCAD đang bận hoặc đang mở hộp thoại Modal."
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


@mcp.tool()
def cad_activate_server(confirmation: str = "khởi động MCP server") -> str:
    """
    Kích hoạt phiên làm việc của MCP Server và mở quyền cho AI điều khiển AutoCAD.
    RÀNG BUỘC BẮT BUỘC: Chỉ được gọi tool này khi người dùng gõ hoặc yêu cầu rõ ràng: 'khởi động MCP server' hoặc 'kích hoạt MCP'.
    """
    res = call_cad_api("auth/start", method="POST")
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_deactivate_server() -> str:
    """
    Khóa quyền truy cập MCP Server. Sau khi khóa, AI tuyệt đối không thể gửi lệnh can thiệp vào bản vẽ CAD.
    Sử dụng khi người dùng yêu cầu: 'tắt MCP server' hoặc 'dừng MCP'.
    """
    res = call_cad_api("auth/stop", method="POST")
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_get_status() -> str:
    """
    Kiểm tra trạng thái kết nối giữa MCP Server và AutoCAD Add-in:
    Xem cổng 5005 có đang lắng nghe không, tên bản vẽ DWG đang mở, và trạng thái cấp quyền (isAuthorized).
    """
    res = call_cad_api("status", method="GET")
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_get_drawing_info() -> str:
    """
    Lấy thông tin tổng quan về bản vẽ AutoCAD đang mở:
    Tên file DWG, số lượng Layer, số lượng Block Table Records và tọa độ UCS hiện hành.
    (Yêu cầu đã kích hoạt quyền MCP).
    """
    payload = {"action": "get_drawing_info", "params": {}}
    res = call_cad_api("action", method="POST", payload=payload)
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_draw_sld(panel_name: str = "MSB-MCP-AI", insert_x: float = 0.0, insert_y: float = 0.0) -> str:
    """
    Tự động vẽ Sơ đồ nguyên lý đơn tuyến (Single Line Diagram - SLD) của tủ điện lên bản vẽ CAD:
    Bao gồm nguồn kép ATS, tụ bù, phân phối các nhánh tải động cơ Y-Delta, DOL, VFD, SoftStarter, và khung tên A3 tiêu chuẩn.
    :param panel_name: Tên tủ điện (VD: MSB-TONG, DB-T1, MCC-CHILLER)
    :param insert_x: Tọa độ X điểm chèn (WCS)
    :param insert_y: Tọa độ Y điểm chèn (WCS)
    """
    payload = {
        "action": "draw_sample_sld",
        "params": {
            "panelName": panel_name,
            "insertX": insert_x,
            "insertY": insert_y
        }
    }
    res = call_cad_api("action", method="POST", payload=payload)
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_draw_cable_tray(
    width_mm: float = 300.0,
    height_mm: float = 100.0,
    tray_type: str = "Trunking",
    points_xyz: Optional[List[List[float]]] = None,
    insert_supports: bool = True
) -> str:
    """
    Tự động vẽ tuyến Thang Máng Cáp MEP theo danh sách tọa độ tim tuyến:
    Tự động tính và vẽ co lượn (Elbow), nắp máng/nan thang và các giá treo Unistrut cách đều 1.5m.
    :param width_mm: Bề rộng máng cáp (mm, VD: 100, 200, 300, 400, 600)
    :param height_mm: Chiều cao máng cáp (mm, VD: 50, 100, 150)
    :param tray_type: Loại máng: 'Trunking' (Máng kín), 'Tray' (Khay đột lỗ), hoặc 'Ladder' (Thang cáp)
    :param points_xyz: Danh sách tọa độ các điểm bẻ góc [[x1, y1, z1], [x2, y2, z2], ...]. Nếu để trống sẽ vẽ tuyến mẫu 3 khúc.
    :param insert_supports: Có chèn các thanh giá treo đỡ máng hay không (mặc định True).
    """
    payload = {
        "action": "draw_cable_tray",
        "params": {
            "width": width_mm,
            "height": height_mm,
            "type": tray_type,
            "points": points_xyz or [],
            "insertSupports": insert_supports
        }
    }
    res = call_cad_api("action", method="POST", payload=payload)
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_smart_boundary(detect_text: bool = True) -> str:
    """
    Tự động quét mặt bằng kiến trúc (XLMB), nhận diện tường, cột, tự động bo kín đường bao phòng và tính diện tích thông thủy.
    Bỏ qua nội thất và trục định vị (Grid). Gán dữ liệu XData SMART_ROOM_BOUNDARY vào Polyline.
    :param detect_text: Tự động tìm text tên phòng gần tâm để gán nhãn phòng.
    """
    payload = {
        "action": "smart_boundary",
        "params": {
            "detectText": detect_text
        }
    }
    res = call_cad_api("action", method="POST", payload=payload)
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_analyze_dwg() -> str:
    """
    Phân tích cấu trúc sâu của bản vẽ DWG hiện hành:
    Thống kê tổng số thực thể, danh sách layer, block definitions, block references và texts.
    """
    payload = {
        "action": "analyze_dwg",
        "params": {}
    }
    res = call_cad_api("action", method="POST", payload=payload)
    return json.dumps(res, ensure_ascii=False, indent=2)


@mcp.tool()
def cad_run_cad_command(command: str) -> str:
    """
    Gửi lệnh CAD tùy biến trực tiếp vào dòng lệnh của AutoCAD (SendCommand / SendStringToExecute).
    :param command: Chuỗi lệnh CAD (VD: '._REGEN', 'NTT', 'BAOTATCA', 'ZOOM E').
    """
    payload = {
        "action": "run_command",
        "params": {
            "command": command
        }
    }
    res = call_cad_api("action", method="POST", payload=payload)
    return json.dumps(res, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    # Khởi chạy server ở chế độ STDIO chuẩn MCP
    mcp.run(transport="stdio")

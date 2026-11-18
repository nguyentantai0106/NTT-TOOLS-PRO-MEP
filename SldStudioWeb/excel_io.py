import io
from typing import Dict, List, Any
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side


def export_panel_to_excel(panel: Dict[str, Any]) -> bytes:
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = (panel.get("PanelName") or "SLD_PANEL")[:31]

    header_fill = PatternFill(start_color="1E2433", end_color="1E2433", fill_type="solid")
    header_font = Font(name="Segoe UI", size=11, bold=True, color="00E5FF")
    title_font = Font(name="Segoe UI", size=14, bold=True, color="0F172A")
    thin_border = Border(
        left=Side(style="thin", color="CBD5E1"),
        right=Side(style="thin", color="CBD5E1"),
        top=Side(style="thin", color="CBD5E1"),
        bottom=Side(style="thin", color="CBD5E1"),
    )

    ws["A1"] = f"BẢNG PHỤ TẢI TỦ ĐIỆN: {panel.get('PanelName', 'DB-01')}"
    ws["A1"].font = title_font
    ws["A2"] = (
        f"Từ tủ: {panel.get('ParentPanelName', 'MSB')} | "
        f"Pđặt: {panel.get('TotalInstalledPowerKW', 0)} kW | "
        f"Ptt: {panel.get('TotalCalculatedPowerKW', 0)} kW | "
        f"Itt: {panel.get('TotalCurrentA', 0)} A | "
        f"CB Tổng: {panel.get('MainBreakerSpec', '')}"
    )

    headers = [
        "STT", "Lộ", "Tên phụ tải", "P (kW)", "Số pha", "Pha gán",
        "Itt (A)", "Thiết bị bảo vệ", "Cáp / Dây dẫn", "Dài (m)",
        "ΔU (%)", "Khởi động", "Điều khiển"
    ]
    ws.append([])
    ws.append(headers)
    header_row = 4
    for col_idx in range(1, len(headers) + 1):
        cell = ws.cell(row=header_row, column=col_idx)
        cell.fill = header_fill
        cell.font = header_font
        cell.alignment = Alignment(horizontal="center", vertical="center")
        cell.border = thin_border

    for idx, ld in enumerate(panel.get("Loads") or [], start=1):
        phase_str = "1P" if ld.get("Phase") == "SinglePhase" else "3P"
        row = [
            idx,
            ld.get("CircuitNumber", f"C{idx}"),
            ld.get("LoadName", ""),
            float(ld.get("PowerKW") or 0.0),
            phase_str,
            ld.get("AssignedPhase", "R-Y-B"),
            float(ld.get("CurrentA") or 0.0),
            ld.get("BreakerSpec", ""),
            ld.get("CableSpec", ""),
            float(ld.get("CableLength") or 25.0),
            float(ld.get("VoltageDropPercent") or 0.0),
            ld.get("Starter", "Direct"),
            ld.get("Control", "None"),
        ]
        ws.append(row)
        r_idx = header_row + idx
        for c_idx in range(1, len(headers) + 1):
            ws.cell(row=r_idx, column=c_idx).border = thin_border

    widths = [6, 10, 32, 10, 9, 10, 10, 24, 34, 10, 10, 14, 14]
    for i, w in enumerate(widths, start=1):
        ws.column_dimensions[openpyxl.utils.get_column_letter(i)].width = w

    buf = io.BytesIO()
    wb.save(buf)
    return buf.getvalue()


def create_template_excel() -> bytes:
    sample_panel = {
        "PanelName": "DB-MAU",
        "ParentPanelName": "MSB",
        "TotalInstalledPowerKW": 28.5,
        "TotalCalculatedPowerKW": 24.2,
        "TotalCurrentA": 43.3,
        "MainBreakerSpec": "MCCB 3P-63A-18kA",
        "Loads": [
            {"CircuitNumber": "C1", "LoadName": "Chiếu sáng tầng 1", "PowerKW": 2.5, "Phase": "SinglePhase", "AssignedPhase": "R", "CurrentA": 13.4, "BreakerSpec": "MCB 1P-20A-6kA", "CableSpec": "Cu/XLPE/PVC (2x1C2.5) + E2.5mm2", "CableLength": 30, "VoltageDropPercent": 1.2, "Starter": "Direct", "Control": "Timer"},
            {"CircuitNumber": "C2", "LoadName": "Ổ cắm khu văn phòng", "PowerKW": 3.5, "Phase": "SinglePhase", "AssignedPhase": "Y", "CurrentA": 18.7, "BreakerSpec": "RCBO 1P+N-25A-6kA 30mA", "CableSpec": "Cu/XLPE/PVC (2x1C4) + E4mm2", "CableLength": 25, "VoltageDropPercent": 1.1, "Starter": "Direct", "Control": "None"},
            {"CircuitNumber": "C3", "LoadName": "Quạt tăng áp cầu thang", "PowerKW": 11.0, "Phase": "ThreePhase", "AssignedPhase": "R-Y-B", "CurrentA": 19.7, "BreakerSpec": "MCB 3P-32A-10kA", "CableSpec": "Cu/XLPE/PVC (4x1C6) + E6mm2", "CableLength": 45, "VoltageDropPercent": 1.6, "Starter": "StarDelta", "Control": "BMS"},
            {"CircuitNumber": "C4", "LoadName": "Bơm nước sinh hoạt", "PowerKW": 7.5, "Phase": "ThreePhase", "AssignedPhase": "R-Y-B", "CurrentA": 13.4, "BreakerSpec": "MCB 3P-20A-10kA", "CableSpec": "Cu/XLPE/PVC (4x1C4) + E4mm2", "CableLength": 35, "VoltageDropPercent": 1.4, "Starter": "VFD", "Control": "FloatSwitch"},
        ]
    }
    return export_panel_to_excel(sample_panel)


def import_loads_from_excel(file_bytes: bytes) -> List[Dict[str, Any]]:
    wb = openpyxl.load_workbook(io.BytesIO(file_bytes), data_only=True)
    ws = wb.active

    loads: List[Dict[str, Any]] = []
    header_row = None
    col_map: Dict[str, int] = {}

    for r_idx in range(1, min(15, ws.max_row + 1)):
        row_vals = [str(ws.cell(row=r_idx, column=c).value or "").strip().lower() for c in range(1, ws.max_column + 1)]
        if any("tên" in v or "load" in v or "công suất" in v or "p (kw)" in v for v in row_vals):
            header_row = r_idx
            for c_idx, v in enumerate(row_vals, start=1):
                if "lộ" in v or "circuit" in v or "mạch" in v:
                    col_map["circuit"] = c_idx
                elif "tên" in v or "load" in v or "phạm vi" in v:
                    col_map["name"] = c_idx
                elif "p (kw)" in v or "công suất" in v or v == "p":
                    col_map["power"] = c_idx
                elif "số pha" in v or v == "pha":
                    col_map["phase"] = c_idx
                elif "pha gán" in v:
                    col_map["assigned"] = c_idx
                elif "bảo vệ" in v or "cb" in v or "aptomat" in v:
                    col_map["breaker"] = c_idx
                elif "dài" in v or "length" in v:
                    col_map["length"] = c_idx
                elif "khởi động" in v or "starter" in v:
                    col_map["starter"] = c_idx
                elif "điều khiển" in v or "control" in v:
                    col_map["control"] = c_idx
            break

    start_row = (header_row + 1) if header_row else 2
    idx = 1
    for r_idx in range(start_row, ws.max_row + 1):
        name_val = str(ws.cell(row=r_idx, column=col_map.get("name", 3)).value or "").strip()
        power_raw = ws.cell(row=r_idx, column=col_map.get("power", 4)).value
        if not name_val and power_raw is None:
            continue
        try:
            p_kw = float(power_raw or 0.0)
        except Exception:
            p_kw = 0.0

        circuit = str(ws.cell(row=r_idx, column=col_map.get("circuit", 2)).value or f"C{idx}").strip()
        phase_raw = str(ws.cell(row=r_idx, column=col_map.get("phase", 5)).value or "3P").strip().upper()
        phase = "SinglePhase" if ("1" in phase_raw or "SINGLE" in phase_raw) else "ThreePhase"
        assigned = str(ws.cell(row=r_idx, column=col_map.get("assigned", 6)).value or ("R" if phase == "SinglePhase" else "R-Y-B")).strip()

        cb_raw = str(ws.cell(row=r_idx, column=col_map.get("breaker", 8)).value or "").strip()
        b_type = "RCBO" if "RCBO" in cb_raw.upper() else ("MCCB" if "MCCB" in cb_raw.upper() else "MCB")

        len_raw = ws.cell(row=r_idx, column=col_map.get("length", 10)).value
        try:
            length_m = float(len_raw or 25.0)
        except Exception:
            length_m = 25.0

        starter_raw = str(ws.cell(row=r_idx, column=col_map.get("starter", 12)).value or "Direct").strip()
        control_raw = str(ws.cell(row=r_idx, column=col_map.get("control", 13)).value or "None").strip()

        loads.append({
            "Stt": idx,
            "CircuitNumber": circuit,
            "LoadName": name_val or f"Phụ tải {idx}",
            "PowerKW": p_kw,
            "Phase": phase,
            "AssignedPhase": assigned,
            "BreakerType": b_type,
            "CableLength": length_m,
            "Starter": starter_raw if starter_raw in ("Direct", "DOL", "StarDelta", "VFD", "SoftStarter") else "Direct",
            "Control": control_raw if control_raw in ("None", "AutoManual", "FloatSwitch", "PressureSwitch", "BMS", "PLC", "Timer") else "None",
            "CableType": "Cu/XLPE/PVC",
            "LoadType": "Chiếu sáng" if "chiếu sáng" in name_val.lower() else "Động cơ",
            "DrawOutgoingWire": True,
        })
        idx += 1

    return loads

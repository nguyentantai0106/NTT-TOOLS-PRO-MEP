import math
from typing import Dict, List, Any

STANDARD_BREAKER_RATINGS = [
    16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160,
    200, 250, 300, 350, 400, 500, 630, 800, 1000, 1250, 1600
]

STANDARD_CABLE_SIZES = [
    "1.5", "2.5", "4", "6", "10", "16", "25", "35",
    "50", "70", "95", "120", "150", "185", "240", "300"
]

# Điện trở 1 chiều R (Ohm/km) ở 75°C theo TCVN / IEC 60228
COPPER_RESISTANCE = {
    "1.5": 14.8, "2.5": 8.91, "4": 5.57, "6": 3.71,
    "10": 2.24, "16": 1.41, "25": 0.889, "35": 0.641,
    "50": 0.473, "70": 0.328, "95": 0.236, "120": 0.188,
    "150": 0.153, "185": 0.123, "240": 0.094, "300": 0.077
}

# Dòng điện cho phép Icp (A) cáp đồng XLPE/PVC đi trong ống/thang máng ở 30-35°C
CABLE_AMPACITY = {
    "1.5": 19, "2.5": 26, "4": 34, "6": 44,
    "10": 61, "16": 82, "25": 108, "35": 135,
    "50": 168, "70": 215, "95": 260, "120": 305,
    "150": 350, "185": 405, "240": 480, "300": 550
}

STANDARD_REACTANCE_X = 0.08  # Ohm/km


def select_breaker_rating(required_a: float) -> int:
    for r in STANDARD_BREAKER_RATINGS:
        if r >= required_a:
            return r
    return STANDARD_BREAKER_RATINGS[-1]


def select_cable_size(cb_rating: float, min_size: str = "1.5") -> str:
    start_idx = STANDARD_CABLE_SIZES.index(min_size) if min_size in STANDARD_CABLE_SIZES else 0
    for sz in STANDARD_CABLE_SIZES[start_idx:]:
        if CABLE_AMPACITY[sz] >= cb_rating:
            return sz
    return "300"


def get_ground_size(phase_size: str) -> str:
    try:
        s = float(phase_size)
    except Exception:
        return "2.5"
    if s <= 16:
        return phase_size
    if s <= 35:
        return "16"
    # >= 50 mm2 -> S/2 làm tròn theo chuẩn
    half = s / 2.0
    for sz in STANDARD_CABLE_SIZES:
        if float(sz) >= half:
            return sz
    return "150"


def format_cable_spec(cable_type: str, phase: str, size: str, ground: str, starter: str) -> str:
    if phase == "SinglePhase":
        return f"{cable_type} (2x1C{size}) + E{ground}mm2"
    if starter == "StarDelta":
        return f"{cable_type} 2x(3x1C{size}) + E{ground}mm2"
    return f"{cable_type} (4x1C{size}) + E{ground}mm2"


def calculate_voltage_drop(load: Dict[str, Any], voltage_v: float, cos_phi: float) -> None:
    sz = str(load.get("ConductorSize") or "2.5")
    if sz not in COPPER_RESISTANCE:
        sz = "2.5"
    r_ohm = COPPER_RESISTANCE[sz]
    x_ohm = STANDARD_REACTANCE_X
    length_m = float(load.get("CableLength") or 25.0)
    current_a = float(load.get("CurrentA") or 0.0)
    sin_phi = math.sqrt(max(0.0, 1.0 - cos_phi * cos_phi))
    phase = load.get("Phase", "ThreePhase")

    if phase == "SinglePhase":
        u_ph = 220.0
        du_pct = (2.0 * current_a * (length_m / 1000.0) * (r_ohm * cos_phi + x_ohm * sin_phi) / u_ph) * 100.0
    else:
        u_ll = voltage_v if voltage_v > 0 else 380.0
        du_pct = (math.sqrt(3.0) * current_a * (length_m / 1000.0) * (r_ohm * cos_phi + x_ohm * sin_phi) / u_ll) * 100.0

    load["VoltageDropPercent"] = round(du_pct, 2)
    load_type = (load.get("LoadType") or "").lower()
    limit = 3.0 if ("chiếu sáng" in load_type or "light" in load_type) else 5.0
    load["IsVoltageDropExceeded"] = du_pct > limit


def calculate_load(load: Dict[str, Any], voltage_v: float, cos_phi: float, default_cable_type: str) -> None:
    p_kw = float(load.get("PowerKW") or 0.0)
    phase = load.get("Phase", "ThreePhase")
    cos_p = cos_phi if 0.1 <= cos_phi <= 1.0 else 0.85

    if phase == "SinglePhase":
        i_tt = (p_kw * 1000.0) / (220.0 * cos_p)
    else:
        u_ll = voltage_v if voltage_v > 0 else 380.0
        i_tt = (p_kw * 1000.0) / (math.sqrt(3.0) * u_ll * cos_p)

    load["CurrentA"] = round(i_tt, 1)

    is_spare = bool(load.get("IsSpare")) or ("spare" in (load.get("CircuitNumber") or "").lower()) or ("dự phòng" in (load.get("LoadName") or "").lower())
    load["IsSpare"] = is_spare

    # Chọn CB
    cb_rating = select_breaker_rating(max(10.0, i_tt * 1.25))
    b_type = (load.get("BreakerType") or "").strip()
    if not b_type:
        b_type = "MCCB" if cb_rating > 63 else "MCB"
        load["BreakerType"] = b_type

    poles = "1P" if phase == "SinglePhase" else "3P"
    if "rcbo" in b_type.lower() or "rccb" in b_type.lower():
        poles = "1P+N" if phase == "SinglePhase" else "3P+N"
        icu = "6kA 30mA"
    elif "mccb" in b_type.lower():
        icu = "25kA" if cb_rating >= 100 else "18kA"
    else:
        icu = "6kA" if phase == "SinglePhase" else "10kA"

    if not load.get("AutoKeepSpec"):
        load["BreakerSpec"] = f"{b_type} {poles}-{cb_rating}A-{icu}"

    # Chọn Cáp
    cable_type = load.get("CableType") or default_cable_type or "Cu/XLPE/PVC"
    load["CableType"] = cable_type
    min_sz = "1.5" if phase == "SinglePhase" else "2.5"
    sz = select_cable_size(cb_rating, min_sz)
    g_sz = get_ground_size(sz)
    load["ConductorSize"] = sz
    load["GroundSize"] = g_sz
    load["CableSpec"] = format_cable_spec(cable_type, phase, sz, g_sz, load.get("Starter", "Direct"))
    load["GroundWireSpec"] = f"E-{g_sz}mm2"

    # Phân bổ công suất pha
    if phase == "ThreePhase":
        load["AssignedPhase"] = "R-Y-B"
        per = round(p_kw / 3.0, 3)
        load["PowerR"] = per
        load["PowerY"] = per
        load["PowerB"] = per
    else:
        ap = (load.get("AssignedPhase") or "R").strip().upper()
        if ap not in ("R", "Y", "B", "A", "C"):
            ap = "R"
        load["AssignedPhase"] = ap
        load["PowerR"] = p_kw if ap in ("R", "A") else 0.0
        load["PowerY"] = p_kw if ap == "Y" else 0.0
        load["PowerB"] = p_kw if ap in ("B", "C") else 0.0

    calculate_voltage_drop(load, voltage_v, cos_p)


def auto_balance_phases(panel: Dict[str, Any]) -> Dict[str, Any]:
    loads = panel.get("Loads") or []
    three_phase = []
    single_phase = []
    for ld in loads:
        if ld.get("Phase") == "SinglePhase":
            single_phase.append(ld)
        else:
            three_phase.append(ld)

    sum_r = sum(float(x.get("PowerKW") or 0.0) / 3.0 for x in three_phase)
    sum_y = sum_r
    sum_b = sum_r

    # LPT Heuristic: sắp xếp 1P giảm dần theo công suất
    single_sorted = sorted(single_phase, key=lambda x: float(x.get("PowerKW") or 0.0), reverse=True)
    for ld in single_sorted:
        p = float(ld.get("PowerKW") or 0.0)
        if sum_r <= sum_y and sum_r <= sum_b:
            ld["AssignedPhase"] = "R"
            ld["PowerR"], ld["PowerY"], ld["PowerB"] = p, 0.0, 0.0
            sum_r += p
        elif sum_y <= sum_r and sum_y <= sum_b:
            ld["AssignedPhase"] = "Y"
            ld["PowerR"], ld["PowerY"], ld["PowerB"] = 0.0, p, 0.0
            sum_y += p
        else:
            ld["AssignedPhase"] = "B"
            ld["PowerR"], ld["PowerY"], ld["PowerB"] = 0.0, 0.0, p
            sum_b += p

    return calculate_panel(panel)


def optimize_cables(panel: Dict[str, Any]) -> Dict[str, Any]:
    voltage_v = float(panel.get("VoltageV") or 380.0)
    cos_phi = float(panel.get("PowerFactor") or 0.85)
    for ld in (panel.get("Loads") or []):
        calculate_voltage_drop(ld, voltage_v, cos_phi)
        while ld.get("IsVoltageDropExceeded"):
            cur_sz = str(ld.get("ConductorSize") or "2.5")
            if cur_sz not in STANDARD_CABLE_SIZES:
                break
            idx = STANDARD_CABLE_SIZES.index(cur_sz)
            if idx + 1 >= len(STANDARD_CABLE_SIZES):
                break
            next_sz = STANDARD_CABLE_SIZES[idx + 1]
            g_sz = get_ground_size(next_sz)
            ld["ConductorSize"] = next_sz
            ld["GroundSize"] = g_sz
            ld["CableSpec"] = format_cable_spec(
                ld.get("CableType") or "Cu/XLPE/PVC",
                ld.get("Phase", "ThreePhase"),
                next_sz, g_sz, ld.get("Starter", "Direct")
            )
            calculate_voltage_drop(ld, voltage_v, cos_phi)
    return calculate_panel(panel)


def calculate_panel(panel: Dict[str, Any]) -> Dict[str, Any]:
    voltage_v = float(panel.get("VoltageV") or 380.0)
    cos_phi = float(panel.get("PowerFactor") or 0.85)
    kdt = float(panel.get("SimultaneousFactor") or 0.85)
    ksd = float(panel.get("UsageFactor") or 1.0)
    default_cable = panel.get("DefaultCableType") or "Cu/XLPE/PVC"

    loads = panel.get("Loads") or []
    sum_p = 0.0
    sum_r = 0.0
    sum_y = 0.0
    sum_b = 0.0

    for i, ld in enumerate(loads):
        ld["Stt"] = i + 1
        calculate_load(ld, voltage_v, cos_phi, default_cable)
        if not ld.get("IsSpare"):
            sum_p += float(ld.get("PowerKW") or 0.0)
            sum_r += float(ld.get("PowerR") or 0.0)
            sum_y += float(ld.get("PowerY") or 0.0)
            sum_b += float(ld.get("PowerB") or 0.0)

    p_tt = sum_p * kdt * ksd
    i_tt = (p_tt * 1000.0) / (math.sqrt(3.0) * voltage_v * cos_phi) if voltage_v > 0 and cos_phi > 0 else 0.0

    panel["TotalInstalledPowerKW"] = round(sum_p, 2)
    panel["TotalCalculatedPowerKW"] = round(p_tt, 2)
    panel["TotalCurrentA"] = round(i_tt, 1)
    panel["PhasePowerR"] = round(sum_r, 2)
    panel["PhasePowerY"] = round(sum_y, 2)
    panel["PhasePowerB"] = round(sum_b, 2)

    avg_ph = (sum_r + sum_y + sum_b) / 3.0
    if avg_ph > 0.001:
        max_dev = max(abs(sum_r - avg_ph), abs(sum_y - avg_ph), abs(sum_b - avg_ph))
        panel["UnbalancePercent"] = round((max_dev / avg_ph) * 100.0, 1)
    else:
        panel["UnbalancePercent"] = 0.0

    # Chọn CB tổng & Cáp tổng
    main_cb_rating = select_breaker_rating(max(32.0, i_tt * 1.25))
    main_cb_type = "ACB" if main_cb_rating >= 800 else "MCCB"
    icu = "50kA" if main_cb_rating >= 800 else ("36kA" if main_cb_rating >= 250 else "25kA")
    panel["MainBreakerSpec"] = f"{main_cb_type} 3P-{main_cb_rating}A-{icu}"

    main_sz = select_cable_size(main_cb_rating, "10")
    main_g = get_ground_size(main_sz)
    panel["MainCableSpec"] = f"{default_cable} (4x1C{main_sz}) + E{main_g}mm2"

    # Tính tụ bù nếu bật
    if panel.get("HasCapacitorBank"):
        target_cos = float(panel.get("TargetCosPhi") or 0.95)
        phi1 = math.acos(min(1.0, max(0.1, cos_phi)))
        phi2 = math.acos(min(1.0, max(0.1, target_cos)))
        qc = max(0.0, p_tt * (math.tan(phi1) - math.tan(phi2)))
        panel["CapacitorBankKvar"] = round(math.ceil(qc / 5.0) * 5.0, 1)
    else:
        panel["CapacitorBankKvar"] = 0.0

    return panel

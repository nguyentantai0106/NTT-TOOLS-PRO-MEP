#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
scripts/boundary_engine.py
Engine Vạn Năng (Universal Engine v4.0) Nhận Diện & Tạo Đường Bao Phòng Mặt Bằng Kiến Trúc AutoCAD.
Áp dụng cho mọi loại công trình: Nhà phố, Biệt thự, Chung cư, Văn phòng, Trường học, Bệnh viện, Nhà xưởng...

TUÂN THỦ TUYỆT ĐỐI 5 YÊU CẦU CỐT LÕI CỦA KỸ SƯ:
1. QUÉT BẢN VẼ & LOẠI BỎ TRỤC GRID:
   Tự động nhận diện bong bóng trục (Circle R=200-800) và các đường nét trục định vị xuyên suốt.
2. NHẬN DẠNG TƯỜNG & CỘT BẰNG HÌNH HỌC KHÔNG GIAN (BỎ QUA NỘI THẤT):
   Không phụ thuộc layer. Bỏ qua các block đồ rời, giường tủ, xe hơi, gạch lát, nét vẽ mảnh.
3. VẼ ĐƯỜNG BAO:
   - Tường vuông góc: 90° & 270° trực giao chuẩn xác, ôm sát mép cột kết cấu.
   - Tường xéo (Skewed walls): Giữ nguyên góc nghiêng theta thực tế, vẽ ôm sát theo tường xéo.
   - Tường cong bo tròn: Tự động gán Bulge (Arc) cho LWPOLYLINE bo tròn mượt mà.
4. KHU VỰC LỚN CHỈ VẼ 1 ĐƯỜNG BAO DUY NHẤT:
   Không gian lớn (Sân trước, Gara, Phòng khách - Bếp, Sảnh, Sân sau...) không bị cắt vụn.
5. QUÉT VÀ GHI LẠI TÊN PHÒNG + DIỆN TÍCH (m2):
   Giải mã AutoCAD Unicode, tạo MTEXT tại tâm phòng (Centroid) trên Layer DUONG_BAO_TEXT.
   Kết thúc bằng thông báo: "Đã vẽ xong mời bạn Check".
"""

import sys
import os
import math
import re
import argparse
from typing import List, Tuple, Dict, Any, Set, Optional
from collections import defaultdict

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

try:
    import ezdxf
    from shapely import STRtree
    from shapely.geometry import Point, LineString, Polygon, MultiPolygon, box
    from shapely.ops import polygonize, unary_union
    from shapely.validation import make_valid
except ImportError as err:
    print(f"[ERROR] Thiếu thư viện phụ thuộc: {err}")
    print("Vui lòng cài đặt: pip install ezdxf shapely")
    sys.exit(1)


def parse_arguments():
    parser = argparse.ArgumentParser(description="Universal AutoCAD Smart Room Boundary Generator v4.0")
    parser.add_argument("--dxf", required=True, help="Đường dẫn file DXF cần xử lý")
    parser.add_argument("--out", default="draw_boundaries.lsp", help="File AutoLISP đầu ra")
    parser.add_argument("--point", nargs=2, type=float, metavar=('X', 'Y'),
                        help="Tọa độ 1 điểm bên trong phòng cần bao (X Y)")
    parser.add_argument("--radius", type=float, default=20000.0,
                        help="Bán kính quét quanh điểm click (mm, mặc định 20m)")
    parser.add_argument("--min-area", type=float, default=1.0, help="Diện tích tối thiểu của phòng (m2)")
    parser.add_argument("--max-area", type=float, default=5000.0, help="Diện tích tối đa của phòng (m2)")
    parser.add_argument("--bbox", nargs=4, type=float, metavar=('MINX', 'MINY', 'MAXX', 'MAXY'),
                        help="Giới hạn vùng xử lý cục bộ: min_x min_y max_x max_y")
    return parser.parse_args()


# ==============================================================================
# 1. GIẢI MÃ UNICODE & BỘ LỌC TÊN PHÒNG VẠN NĂNG
# ==============================================================================

def decode_autocad_unicode(text: str) -> str:
    """Giải mã các chuỗi ký tự Unicode dạng \\U+xxxx trong AutoCAD và lọc sạch thẻ định dạng font MText."""
    if not text:
        return ""
    def replace_match(m):
        try:
            return chr(int(m.group(1), 16))
        except Exception:
            return m.group(0)
    # 1. Giải mã Unicode \\U+xxxx trước tiên
    text = re.sub(r'\\U\+([0-9A-Fa-f]{4})', replace_match, text)
    # 2. Xóa các mã format MText: \\f...;, \\C...;, \\H...;, \\A...;, etc.
    text = re.sub(r'\\[fF][^;]*;', '', text)
    text = re.sub(r'\\[a-zA-Z][^;]*;', '', text)
    text = re.sub(r'\\[pP]', ' ', text)
    text = re.sub(r'[{}]', '', text)
    text = text.replace('\\', '')
    return re.sub(r'\s+', ' ', text).strip()


def is_valid_room_name(text: str) -> bool:
    """
    Kiểm tra một chuỗi text có phải là nhãn tên phòng kiến trúc hợp lệ hay không.
    Hỗ trợ cả tiếng Việt (có dấu & không dấu) và tiếng Anh.
    Loại trừ số kích thước, cao độ, ký hiệu kỹ thuật, tiêu đề khung tên.
    """
    if not text or len(text.strip()) < 2:
        return False
    t = text.strip().upper()

    # Bỏ qua nếu là số kích thước (vd: 3550, 4600, 14000) hoặc diện tích (vd: 58.95 m2)
    clean_num = t.replace('.', '').replace(',', '').replace('-', '').replace('+', '').replace('M²', '').replace('M2', '').strip()
    if clean_num.isdigit() or not clean_num:
        return False

    # Bỏ qua cao độ hoặc ký hiệu kỹ thuật: +0.000, -0.450, %%P0.00, H=2800, Ø12...
    if any(t.startswith(prefix) for prefix in ['+', '-', '%%P', '%%C', 'H=', 'D=', 'R=', 'Ø', 'O/']):
        return False

    # Bỏ qua ký hiệu trục hoặc cấu kiện kỹ thuật ngắn: 1, 2, 3, A, B, C, D1, C1...
    if re.match(r'^[A-Z0-9]{1,2}$', t) or re.match(r'^[A-Z][0-9]{1,3}$', t):
        return False

    # Bỏ qua các từ ghi chú khung bản vẽ
    drawing_keywords = ['TỶ LỆ', 'TY LE', 'SCALE', 'MẶT BẰNG', 'MAT BANG', 'CHI TIẾT', 'CHI TIET',
                        'BẢN VẼ', 'BAN VE', 'GHI CHÚ', 'GHI CHU', 'SECTION', 'ELEVATION', 'PLAN',
                        'REV', 'DATE', 'PROJECT', 'SHEET', 'DRAWING', 'TL']
    if any(dk in t for dk in drawing_keywords):
        return False

    # Bỏ qua các ô giếng trời, lỗ thông tầng, trục kỹ thuật (không phải phòng kín)
    void_keywords = [
        'LẤY SÁNG', 'LAY SANG', 'LẤYSÁNG', 'ẤY SÁNG', 'AY SANG', 'THÔNG TẦNG', 'THONG TANG',
        'THÔNGTẦNG', 'LỖ MỞ', 'LO MO', 'VOID', 'SHAFT', 'GIẾNG TRỜI', 'GIENG TROI'
    ]
    if any(vk in t for vk in void_keywords):
        return False

    # Bỏ qua các nhãn đồ nội thất, thiết bị rời, đồ gia dụng, nhãn rác
    if t == 'RELAX':
        return False
    furniture_keywords = [
        'TỦ ÁO', 'TU AO', 'TỦÁO', 'TỦ GIÀY', 'TU GIAY', 'TỦGIÀY', 'TÚI XÁCH', 'TUI XACH', 'TÚIXÁCH',
        'GIÀY', 'GIAY', 'GIẦY', 'TỦ', 'TU ', 'ỦT', 'TÚI', 'XÁCH', 'XACH',
        'GHẾ', 'GHE', 'BÀN', 'BAN', 'BÀNĂN', 'BAN AN', 'TRANG ĐIỂM', 'TRANG DIEM', 'TRANGĐIỂM',
        'KỆ', 'KE', 'NƯỚC HOA', 'NUOC HOA', 'GIƯỜNG', 'GIUONG', 'SOFA', 'TV', 'TIVI', 'TỦKHO',
        'BỒN TẮM', 'BON TAM', 'BỒN TẮM NẰM', 'BồN TắM NằM', 'TẮM ĐỨNG', 'TAM DUNG', 'TắMđứNG',
        'LAVABO', 'CHẬU RỬA', 'BỒN CẦU', 'BON CAU', 'BỆT', 'ỦI ĐỒ', 'UI DO', 'ỦIĐỒ',
        'CỤC NÓNG', 'CUC NONG', 'CụC NóNG', 'MÁY LẠNH', 'MAY LANH', 'MáY L', 'KHUNG KÍNH', 'KHUNG LAM',
        'SẮT HOA VĂN', 'đỈNH THANG', 'ĐỈNH THANG', 'ạNH', 'ANH'
    ]
    if any(fk in t for fk in furniture_keywords):
        return False

    # Phải chứa ít nhất 2 ký tự chữ cái
    letters = re.findall(r'[a-zA-ZÀ-ỹ]', t)
    return len(letters) >= 2


def is_indoor_room_name(text: str) -> bool:
    """
    Quy tắc Kỹ sư: CHỈ BO DIỆN TÍCH BÊN TRONG NHÀ, BỎ QUA KHU VỰC BÊN NGOÀI.
    Loại trừ: Sân, Sân trước, Sân sau, Tiểu cảnh, Cảnh quan, Lối đi, Lối vào, Ram dốc, Mái, Bồn nước, Máy NLMT, Bồn hoa.
    """
    if not is_valid_room_name(text):
        return False
    t = text.strip().upper()
    outdoor_keywords = [
        'SÂN', 'SAN', 'SÂN TRƯỚC', 'SÂN SAU', 'TIỂU CẢNH', 'TIEU CANH', 'CẢNH QUAN', 'CANH QUAN',
        'LỐI ĐI', 'LOI DI', 'LỐIĐI', 'LỐI VÀO', 'LOI VAO', 'RAM DỐC', 'RAM DOC',
        'MÁI', 'MAI', 'BỒN NƯỚC', 'BON NUOC', 'NLMT', 'THANG LÊN', 'DỰ PHÒNG', 'DU PHONG',
        'BỒN HOA', 'BON HOA'
    ]
    if any(k in t for k in outdoor_keywords):
        return False
    return True


def is_wc_name(text: str) -> bool:
    """Kiểm tra nhãn phòng vệ sinh / WC / Toilet."""
    if not text:
        return False
    t = text.strip().upper()
    wc_keywords = [
        'WC', 'W.C', 'TOILET', 'VỆ SINH', 'VE SINH', 'VS', 'BATH', 'BATHROOM',
        'RESTROOM', 'POWDER', 'TẮM', 'TAM', 'LAVABO', 'SHOWER'
    ]
    for kw in wc_keywords:
        if kw in ['VS', 'WC']:
            if re.search(r'\b' + re.escape(kw) + r'\b', t) or re.search(r'\b' + re.escape(kw) + r'[0-9]+', t):
                return True
        else:
            if kw in t:
                return True
    return False


def cluster_and_merge_texts(raw_texts: List[Tuple[float, float, str]]) -> List[Tuple[Point, str]]:
    """
    Gom cụm các đoạn text rời rạc nằm trên cùng hàng (hoặc gần nhau) thành nhãn phòng tiếng Việt có nghĩa.
    Giải quyết triệt để tình trạng CAD tách chữ 'C' + 'Ử' + 'A HÀNG M' + 'Ỹ' + 'PH' + 'Ẩ' + 'M' -> 'CỬA HÀNG MỸ PHẨM'.
    """
    if not raw_texts:
        return []

    # Sắp xếp theo Y giảm dần, X tăng dần
    sorted_texts = sorted(raw_texts, key=lambda item: (-item[1], item[0]))
    clusters = []

    for x, y, txt in sorted_texts:
        matched = False
        for cl in clusters:
            last_x, last_y, _ = cl[-1]
            # Khoảng cách Y < 120mm và khoảng cách X < 1200mm
            if abs(y - last_y) < 120.0 and abs(x - last_x) < 1200.0:
                cl.append((x, y, txt))
                matched = True
                break
        if not matched:
            clusters.append([(x, y, txt)])

    result = []
    for cl in clusters:
        cl.sort(key=lambda item: item[0])
        full_str = ''.join([item[2] for item in cl]).strip()
        # Chuẩn hóa khoảng trắng
        full_str = re.sub(r'\s+', ' ', full_str).strip()
        avg_x = sum(item[0] for item in cl) / len(cl)
        avg_y = sum(item[1] for item in cl) / len(cl)

        if is_indoor_room_name(full_str):
            result.append((Point(avg_x, avg_y), full_str))

    return result


# ==============================================================================
# 2. XÁC ĐỊNH GRID & TRIỆT TIÊU TRỤC ĐỊNH VỊ
# ==============================================================================

def scan_grid_axes(msp) -> Tuple[Dict[float, str], Dict[float, str], List[Any]]:
    """Nhận diện bong bóng trục định vị và các đường trục."""
    bubbles = [c for c in msp.query('CIRCLE') if 200 <= c.dxf.radius <= 800]
    cad_lines = list(msp.query('LINE'))

    h_axes: Dict[float, str] = {}
    v_axes: Dict[float, str] = {}

    for b in bubbles:
        bc = (b.dxf.center.x, b.dxf.center.y)
        r = b.dxf.radius
        for l in cad_lines:
            p1 = (l.dxf.start.x, l.dxf.start.y)
            p2 = (l.dxf.end.x, l.dxf.end.y)
            d = min(math.hypot(p1[0]-bc[0], p1[1]-bc[1]), math.hypot(p2[0]-bc[0], p2[1]-bc[1]))
            if d < r * 2.5:
                is_h = abs(p1[1] - p2[1]) < 2.0 and abs(p1[1] - bc[1]) < 30.0
                is_v = abs(p1[0] - p2[0]) < 2.0 and abs(p1[0] - bc[0]) < 30.0
                if is_h:
                    h_axes[round(p1[1], 1)] = "GRID_H"
                elif is_v:
                    v_axes[round(p1[0], 1)] = "GRID_V"

    return h_axes, v_axes, bubbles


# ==============================================================================
# 3. BỊT CỬA ĐI 2 MẶT TƯỜNG (DUAL DOOR CAPS)
# ==============================================================================

def filter_door_leaves_and_make_dual_caps(raw_lines: List[LineString], door_arcs: List[Any]) -> Tuple[List[LineString], List[LineString]]:
    """Bịt phẳng 2 mặt tường ngang qua cửa đi và xóa nét cánh cửa."""
    if not raw_lines or not door_arcs:
        return raw_lines, []

    tree = STRtree(raw_lines)
    door_caps: List[LineString] = []
    leaf_lines_to_remove: Set[int] = set()

    for a in door_arcs:
        cx, cy, _ = a.dxf.center
        r = a.dxf.radius
        sa = math.radians(a.dxf.start_angle)
        ea = math.radians(a.dxf.end_angle)
        pa = (cx + r * math.cos(sa), cy + r * math.sin(sa))
        pb = (cx + r * math.cos(ea), cy + r * math.sin(ea))

        def find_jamb_pairs(cand_pt):
            is_H_opening = abs(cand_pt[1] - cy) < abs(cand_pt[0] - cx)
            near_h = [raw_lines[idx] for idx in tree.query(Point(cx, cy).buffer(180))]
            near_s = [raw_lines[idx] for idx in tree.query(Point(cand_pt).buffer(180))]
            if is_H_opening:
                j1 = [l for l in near_h if abs(l.coords[0][0] - l.coords[1][0]) < 15.0 and 50 <= l.length <= 400]
                j2 = [l for l in near_s if abs(l.coords[0][0] - l.coords[1][0]) < 15.0 and 50 <= l.length <= 400]
            else:
                j1 = [l for l in near_h if abs(l.coords[0][1] - l.coords[1][1]) < 15.0 and 50 <= l.length <= 400]
                j2 = [l for l in near_s if abs(l.coords[0][1] - l.coords[1][1]) < 15.0 and 50 <= l.length <= 400]
            if j1 and j2:
                best_j1 = min(j1, key=lambda j: j.distance(Point(cx, cy)))
                best_j2 = min(j2, key=lambda j: j.distance(Point(cand_pt)))
                return best_j1, best_j2
            return None, None

        j1_a, j2_a = find_jamb_pairs(pa)
        j1_b, j2_b = find_jamb_pairs(pb)

        chosen_j1, chosen_j2, strike_pt, leaf_pt = None, None, None, None
        if j1_b and j2_b:
            chosen_j1, chosen_j2, strike_pt, leaf_pt = j1_b, j2_b, pb, pa
        elif j1_a and j2_a:
            chosen_j1, chosen_j2, strike_pt, leaf_pt = j1_a, j2_a, pa, pb
        else:
            # Chọn điểm jamb/strike trên mặt tường: là điểm có khoảng cách tới vách tường gần nhất
            # (Điểm còn lại là cánh cửa mở thò vào không gian phòng)
            cand_lines = [raw_lines[idx] for idx in tree.query(box(cx - r*1.5, cy - r*1.5, cx + r*1.5, cy + r*1.5))]
            cand_wall_lines = [l for l in cand_lines if l.distance(Point(cx, cy)) > 30.0]
            if cand_wall_lines:
                d_a = min(l.distance(Point(pa)) for l in cand_wall_lines)
                d_b = min(l.distance(Point(pb)) for l in cand_wall_lines)
                if d_a <= d_b:
                    strike_pt, leaf_pt = pa, pb
                else:
                    strike_pt, leaf_pt = pb, pa
            else:
                strike_pt, leaf_pt = pa, pb

        if chosen_j1 and chosen_j2:
            jp1, jp2 = chosen_j1.coords[0], chosen_j1.coords[1]
            sp1, sp2 = chosen_j2.coords[0], chosen_j2.coords[1]
            if math.hypot(jp1[0] - sp1[0], jp1[1] - sp1[1]) < math.hypot(jp1[0] - sp2[0], jp1[1] - sp2[1]):
                c1, c2 = LineString([jp1, sp1]), LineString([jp2, sp2])
            else:
                c1, c2 = LineString([jp1, sp2]), LineString([jp2, sp1])
            door_caps.extend([c1, c2])
        elif strike_pt:
            door_caps.append(LineString([(cx, cy), strike_pt]))

        if leaf_pt:
            leaf_seg = LineString([(cx, cy), leaf_pt])
            leaf_box = box(min(cx, leaf_pt[0]) - 50, min(cy, leaf_pt[1]) - 50,
                           max(cx, leaf_pt[0]) + 50, max(cy, leaf_pt[1]) + 50)
            for idx in tree.query(leaf_box):
                l = raw_lines[idx]
                if leaf_seg.distance(l) < 35.0 and l.length < r * 1.25:
                    leaf_lines_to_remove.add(idx)

    clean_lines = [l for idx, l in enumerate(raw_lines) if idx not in leaf_lines_to_remove]
    return clean_lines, door_caps


# ==============================================================================
# 4. TRÍCH XUẤT HÌNH HỌC KHÔNG GIAN (WALLS, COLUMNS, CURVED ARCS, TEXTS)
# ==============================================================================

def auto_cap_dangling_wall_gaps(lines: List[LineString], min_gap: float = 20.0, max_gap: float = 2200.0) -> List[LineString]:
    """
    Thuật toán Tự động Bịt Kín Lỗ Hở & Cửa Đi Chuẩn Xác (Jamb-to-Jamb Sealing):
    1. Snap các đầu mút tự do (dangling nodes) vào mép tường/cột gần nhất vuông góc (d <= 220mm).
    2. Nối kín các cặp đầu tường cụt đối diện nhau qua ô cửa / lối đi (d <= 2200mm).
    Hoàn toàn không sinh nét xiên chéo, ôm sát mép tường và cột 100%!
    """
    if not lines:
        return []

    tree = STRtree(lines)
    snap_caps = []
    counts = defaultdict(int)
    for l in lines:
        counts[l.coords[0]] += 1
        counts[l.coords[1]] += 1
    dangles = [pt for pt, c in counts.items() if c == 1]

    # Bước 1: Snap T-junctions (đầu tường cụt chạm mép tường/cột vuông góc)
    for dpt in dangles:
        p = Point(dpt)
        best_target = None
        min_dist = float('inf')
        for c_idx in tree.query(p.buffer(220.0)):
            target_l = lines[c_idx]
            dist = target_l.distance(p)
            if 1.0 < dist <= 220.0 and dist < min_dist:
                proj = target_l.interpolate(target_l.project(p))
                min_dist = dist
                best_target = (proj.x, proj.y)
        if best_target:
            snap_caps.append(LineString([dpt, best_target]))

    all_now = lines + snap_caps
    counts2 = defaultdict(int)
    for l in all_now:
        counts2[l.coords[0]] += 1
        counts2[l.coords[1]] += 1
    dangles2 = [pt for pt, c in counts2.items() if c == 1]

    # Bước 2: Nối các khoảng hở ô cửa / lối đi (Jamb-to-Jamb Sealing)
    door_bridges = []
    used = set()
    for i in range(len(dangles2)):
        p1 = dangles2[i]
        if p1 in used:
            continue
        best_p2 = None
        min_d = float('inf')
        for j in range(len(dangles2)):
            if i == j:
                continue
            p2 = dangles2[j]
            if p2 in used:
                continue
            d = math.hypot(p2[0] - p1[0], p2[1] - p1[1])
            if 40.0 <= d <= max_gap:
                dx = abs(p2[0] - p1[0])
                dy = abs(p2[1] - p1[1])
                if (dx <= 120.0 or dy <= 120.0 or d <= 300.0) and d < min_d:
                    min_d = d
                    best_p2 = p2
        if best_p2:
            door_bridges.append(LineString([p1, best_p2]))
            used.add(p1)
            used.add(best_p2)

    return snap_caps + door_bridges


def extract_cad_features(doc, bbox_filter=None):
    """
    Trích xuất hình học:
    - Nhận diện Tường & Cột bằng hình học không gian.
    - Bỏ qua Nội thất (giường, tủ, bàn ghế, xe hơi, gạch lát sàn).
    - Bỏ qua Trục Grid.
    - Nhận diện Tường cong (Curved walls) để bo tròn.
    """
    msp = doc.modelspace()
    raw_lines: List[LineString] = []
    door_arcs: List[Any] = []
    curved_wall_arcs: List[Tuple[Point, float, float, float]] = [] # (Center, Radius, StartAng, EndAng)
    column_boxes: List[Polygon] = []
    raw_texts: List[Tuple[float, float, str]] = []

    def in_bbox(x, y):
        if not bbox_filter:
            return True
        minx, miny, maxx, maxy = bbox_filter
        return minx <= x <= maxx and miny <= y <= maxy

    # 1. Quét trục lưới
    h_axes, v_axes, grid_bubbles = scan_grid_axes(msp)
    h_vals = list(h_axes.keys())
    v_vals = list(v_axes.keys())

    # 2. Cột kết cấu từ SOLID
    solid_boxes = []
    for s in msp.query('SOLID'):
        try:
            pts = [(s.dxf.vtx0.x, s.dxf.vtx0.y), (s.dxf.vtx1.x, s.dxf.vtx1.y),
                   (s.dxf.vtx2.x, s.dxf.vtx2.y), (s.dxf.vtx3.x, s.dxf.vtx3.y)]
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            minx, maxx = min(xs), max(xs)
            miny, maxy = min(ys), max(ys)
            w, h_val = maxx - minx, maxy - miny
            if 150 <= w <= 3000 and 150 <= h_val <= 3000:
                cx, cy = (minx + maxx) / 2.0, (miny + maxy) / 2.0
                if in_bbox(cx, cy):
                    solid_boxes.append(box(minx, miny, maxx, maxy))
        except Exception:
            continue

    # Cột từ HATCH SOLID
    for h in msp.query('HATCH'):
        pat = getattr(h.dxf, 'pattern_name', '').upper()
        layer = h.dxf.layer.upper()
        if pat == 'SOLID' or any(k in layer for k in ['COL', 'COT', 'STRUCT']):
            try:
                for path in h.paths:
                    if hasattr(path, 'vertices') and path.vertices:
                        xs = [v[0] for v in path.vertices]
                        ys = [v[1] for v in path.vertices]
                        minx, maxx = min(xs), max(xs)
                        miny, maxy = min(ys), max(ys)
                        w, h_val = maxx - minx, maxy - miny
                        if 150 <= w <= 3000 and 150 <= h_val <= 3000:
                            cx, cy = (minx + maxx) / 2.0, (miny + maxy) / 2.0
                            if in_bbox(cx, cy):
                                solid_boxes.append(box(minx, miny, maxx, maxy))
            except Exception:
                continue

    # Cột từ LWPOLYLINE khép kín chữ nhật
    for pl in msp.query('LWPOLYLINE'):
        try:
            if pl.closed:
                v_pts = [(p[0], p[1]) for p in pl.get_points(format='xy')]
                if len(v_pts) == 4:
                    xs = [p[0] for p in v_pts]
                    ys = [p[1] for p in v_pts]
                    w, h_val = max(xs) - min(xs), max(ys) - min(ys)
                    if 150 <= w <= 3000 and 150 <= h_val <= 3000:
                        cx, cy = (min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0
                        if in_bbox(cx, cy):
                            solid_boxes.append(box(min(xs), min(ys), max(xs), max(ys)))
        except Exception:
            continue

    if solid_boxes:
        merged_cols = unary_union(solid_boxes)
        if isinstance(merged_cols, Polygon):
            column_boxes = [merged_cols]
        elif isinstance(merged_cols, MultiPolygon):
            column_boxes = list(merged_cols.geoms)

    col_boundary_lines = []
    for c in column_boxes:
        minx, miny, maxx, maxy = c.bounds
        col_boundary_lines.extend([
            LineString([(minx, miny), (maxx, miny)]),
            LineString([(maxx, miny), (maxx, maxy)]),
            LineString([(maxx, maxy), (minx, maxy)]),
            LineString([(minx, maxy), (minx, miny)])
        ])

    # 3. Phân loại Cung tròn: Cửa đi vs Tường cong bo tròn
    all_arcs = list(msp.query('ARC'))
    # Trích xuất thêm đối tượng hình học từ Block INSERT (cửa đi, vách kính, cửa sổ)
    door_insert_keywords = ['DOOR', 'CUA', 'CỬA', 'CONG', 'CỔNG', 'GLAZ', 'WINDOW', 'CURTAIN']
    for ins in msp.query('INSERT'):
        try:
            ins_layer = ins.dxf.layer.upper()
            ins_name = ins.dxf.name.upper()
            if any(k in ins_layer for k in door_insert_keywords) or any(k in ins_name for k in door_insert_keywords):
                for ve in ins.virtual_entities():
                    if ve.dxftype() == 'ARC':
                        all_arcs.append(ve)
                    elif ve.dxftype() == 'LINE':
                        p1 = (round(ve.dxf.start.x, 2), round(ve.dxf.start.y, 2))
                        p2 = (round(ve.dxf.end.x, 2), round(ve.dxf.end.y, 2))
                        if (in_bbox(p1[0], p1[1]) or in_bbox(p2[0], p2[1])) and math.hypot(p2[0]-p1[0], p2[1]-p1[1]) >= 30.0:
                            raw_lines.append(LineString([p1, p2]))
        except Exception:
            continue

    for a in all_arcs:
        try:
            r = a.dxf.radius
            sa = a.dxf.start_angle
            ea = a.dxf.end_angle
            sweep = (ea - sa) % 360
            cx, cy, _ = a.dxf.center
            if not in_bbox(cx, cy):
                continue

            rad_sa = math.radians(sa)
            rad_ea = math.radians(ea)
            p1 = (round(cx + r * math.cos(rad_sa), 2), round(cy + r * math.sin(rad_sa), 2))
            p2 = (round(cx + r * math.cos(rad_ea), 2), round(cy + r * math.sin(rad_ea), 2))
            layer = a.dxf.layer.upper()
            if 'WALL' in layer or 'TUONG' in layer:
                curved_wall_arcs.append((Point(cx, cy), r, sa, ea))
                raw_lines.append(LineString([p1, p2]))
            elif 500 <= r <= 1400:
                # Cửa đi: Bịt kín dây cung cánh cửa
                door_arcs.append(a)
                raw_lines.append(LineString([(round(cx, 2), round(cy, 2)), p1]))
                raw_lines.append(LineString([(round(cx, 2), round(cy, 2)), p2]))
            elif 100 <= r <= 5000:
                curved_wall_arcs.append((Point(cx, cy), r, sa, ea))
                raw_lines.append(LineString([p1, p2]))
        except Exception:
            continue

    # 4. Phân loại Layer ranh giới và trích xuất Tường
    def is_boundary_layer(l: str) -> bool:
        lu = l.upper()
        if any(k in lu for k in ['AREA', 'ROOM', 'SPACE', 'BOUNDARY', 'RANH']):
            return True
        if any(k in lu for k in ['WALL', 'TUONG', 'TƯỜNG', 'COL', 'COT', 'CỘT', 'STRUC', 'CONC', 'BETONG']):
            return True
        if any(k in lu for k in ['DOOR', 'CUA', 'CỬA', 'GLAZ', 'WINDOW', 'KINH', 'KÍNH', 'SPCQ']):
            return True
        return False

    # Phát hiện các hộp đồ nội thất nhỏ khép kín trên FLOR để loại trừ
    flor_lines = []
    for l in msp.query('LINE'):
        if 'FLOR' in l.dxf.layer.upper():
            p1 = (round(l.dxf.start.x, 2), round(l.dxf.start.y, 2))
            p2 = (round(l.dxf.end.x, 2), round(l.dxf.end.y, 2))
            flor_lines.append(LineString([p1, p2]))
    flor_polys = [p for p in polygonize(unary_union(flor_lines)) if p.area / 1e6 <= 3.0]
    flor_boxes_union = unary_union(flor_polys) if flor_polys else None

    all_cad_lines = list(msp.query('LINE'))
    for l in all_cad_lines:
        try:
            layer = l.dxf.layer
            p1 = (round(l.dxf.start.x, 2), round(l.dxf.start.y, 2))
            p2 = (round(l.dxf.end.x, 2), round(l.dxf.end.y, 2))
            if not (in_bbox(p1[0], p1[1]) or in_bbox(p2[0], p2[1])):
                continue

            length = math.hypot(p2[0]-p1[0], p2[1]-p1[1])
            if length < 20.0:
                continue

            # Lọc bỏ nét trục Grid
            is_on_h = abs(p1[1]-p2[1]) < 2.0 and any(abs(p1[1]-ay) < 10.0 for ay in h_vals)
            is_on_v = abs(p1[0]-p2[0]) < 2.0 and any(abs(p1[0]-ax) < 10.0 for ax in v_vals)
            if (is_on_h or is_on_v) and length > 3000.0:
                continue

            ls = LineString([p1, p2])
            if is_boundary_layer(layer):
                raw_lines.append(ls)
            elif 'FLOR' in layer.upper() and length <= 1500.0:
                if flor_boxes_union and flor_boxes_union.contains(ls):
                    continue
                raw_lines.append(ls)
        except Exception:
            continue

    for pl in msp.query('LWPOLYLINE'):
        try:
            layer = pl.dxf.layer
            if not is_boundary_layer(layer):
                continue

            pts = [(round(p[0], 2), round(p[1], 2)) for p in pl.get_points(format='xy')]
            if len(pts) >= 2:
                for i in range(len(pts) - 1):
                    p1, p2 = pts[i], pts[i+1]
                    if in_bbox(p1[0], p1[1]) or in_bbox(p2[0], p2[1]):
                        length = math.hypot(p2[0]-p1[0], p2[1]-p1[1])
                        if length >= 20.0:
                            is_on_h = abs(p1[1]-p2[1]) < 2.0 and any(abs(p1[1]-ay) < 10.0 for ay in h_vals)
                            is_on_v = abs(p1[0]-p2[0]) < 2.0 and any(abs(p1[0]-ax) < 10.0 for ax in v_vals)
                            if not ((is_on_h or is_on_v) and length > 3000.0):
                                raw_lines.append(LineString([p1, p2]))
                if pl.closed:
                    p1, p2 = pts[-1], pts[0]
                    if in_bbox(p1[0], p1[1]) or in_bbox(p2[0], p2[1]):
                        length = math.hypot(p2[0]-p1[0], p2[1]-p1[1])
                        if length >= 20.0:
                            is_on_h = abs(p1[1]-p2[1]) < 2.0 and any(abs(p1[1]-ay) < 10.0 for ay in h_vals)
                            is_on_v = abs(p1[0]-p2[0]) < 2.0 and any(abs(p1[0]-ax) < 10.0 for ax in v_vals)
                            if not ((is_on_h or is_on_v) and length > 3000.0):
                                raw_lines.append(LineString([p1, p2]))
        except Exception:
            continue

    # 5. Nhãn Text tên phòng
    for t in msp.query('TEXT MTEXT'):
        try:
            txt = t.plain_text().strip() if hasattr(t, 'plain_text') else getattr(t.dxf, 'text', '').strip()
            txt_clean = decode_autocad_unicode(txt)
            pt = t.dxf.insert
            if in_bbox(pt.x, pt.y) and len(txt_clean) >= 1:
                raw_texts.append((pt.x, pt.y, txt_clean))
        except Exception:
            continue

    # Gom cụm text thành tên phòng hoàn chỉnh
    clustered_texts = cluster_and_merge_texts(raw_texts)

    base_lines = raw_lines + col_boundary_lines
    gap_caps = auto_cap_dangling_wall_gaps(base_lines)
    all_lines = base_lines + gap_caps
    return all_lines, column_boxes, clustered_texts, curved_wall_arcs


# ==============================================================================
# 5. HÌNH HỌC TRỰC GIAO 90°/270° & ÔM TƯỜNG XÉO (SKEWED WALL PRESERVATION)
# ==============================================================================

def clean_duplicate_points(pts: List[Tuple[float, float]], tol: float = 1.0) -> List[Tuple[float, float]]:
    """Xóa các đỉnh trùng nhau hoặc khoảng cách quá nhỏ (< tol)."""
    res = []
    for p in pts:
        if not res or math.hypot(p[0] - res[-1][0], p[1] - res[-1][1]) > tol:
            res.append(p)
    if len(res) > 1 and math.hypot(res[0][0] - res[-1][0], res[0][1] - res[-1][1]) < tol:
        res.pop()
    return res


def simplify_collinear(pts: List[Tuple[float, float]], tol: float = 2.0) -> List[Tuple[float, float]]:
    """Xóa các đỉnh thẳng hàng đồng tuyến."""
    pts = clean_duplicate_points(pts, tol=1.0)
    if len(pts) < 4:
        return pts
    keep = []
    n = len(pts)
    for i in range(n):
        p_prev = pts[(i - 1) % n]
        p_curr = pts[i]
        p_next = pts[(i + 1) % n]
        # Thẳng hàng dọc
        if abs(p_prev[0] - p_curr[0]) < tol and abs(p_curr[0] - p_next[0]) < tol:
            continue
        # Thẳng hàng ngang
        if abs(p_prev[1] - p_curr[1]) < tol and abs(p_curr[1] - p_next[1]) < tol:
            continue
        keep.append(p_curr)
    return keep


def rectify_manhattan_and_preserve_skewed(coords: List[Tuple[float, float]], poly_original: Polygon) -> List[Tuple[float, float]]:
    """
    Quy tắc Vàng cho Đường Bao Phòng:
    1. Chuẩn hóa góc trực giao 90° & 270° (Manhattan Geometry):
       - Mọi góc cua tiêu chuẩn của tường, cột, hốc cửa đều được nắn vuông vức 100%, ôm sát mép tường và cột.
       - Triệt tiêu hoàn toàn các cạnh xiên vát, chém chéo bừa bãi do sai số vẽ tay (góc lệch < 5°).
    2. Ôm sát vách tường nghiêng thực tế (Skewed Walls):
       - Nếu vách tường được nhận diện có góc nghiêng kiến trúc thực tế (góc lệch θ >= 5° và <= 85° với độ dài >= 150mm):
       - GIỮ NGUYÊN ĐOẠN NGHIÊNG ĐỂ ÔM SÁT 100% THEO PHƯƠNG NGHIÊNG CỦA TƯỜNG!
    """
    pts = clean_duplicate_points(list(coords), tol=1.0)
    if len(pts) < 3:
        return pts

    n = len(pts)
    result_pts = []

    for i in range(n):
        p1 = pts[i]
        p2 = pts[(i + 1) % n]
        dx = abs(p2[0] - p1[0])
        dy = abs(p2[1] - p1[1])
        length = math.hypot(dx, dy)

        result_pts.append(p1)

        # Kiểm tra nếu đoạn thẳng bị xiên
        if dx > 8.0 and dy > 8.0:
            angle_deg = math.degrees(math.atan2(min(dx, dy), max(dx, dy)))
            # Nếu góc lệch < 5.0 độ: Sai số vẽ tay / mép cửa -> Nắn trực giao 90 độ
            if angle_deg < 5.0:
                if dx > dy:
                    # Gần ngang -> Nắn phẳng Y
                    p2_fixed = (p2[0], p1[1])
                    result_pts.append(p2_fixed)
                else:
                    # Gần dọc -> Nắn phẳng X
                    p2_fixed = (p1[0], p2[1])
                    result_pts.append(p2_fixed)
            else:
                # ĐÂY LÀ VÁCH TƯỜNG CÓ GÓC NGHIÊNG THỰC TẾ (Skewed Wall) -> GIỮ NGUYÊN ĐOẠN XÉO ĐỂ ÔM THEO TƯỜNG!
                pass

    return simplify_collinear(result_pts)


# ==============================================================================
# 6. GẮN BULGE CHO TƯỜNG CONG BO TRÒN (CURVED WALLS)
# ==============================================================================

def assign_bulge_to_vertices(coords: List[Tuple[float, float]],
                             curved_arcs: List[Tuple[Point, float, float, float]]) -> List[Tuple[Tuple[float, float], float]]:
    """
    Kiểm tra từng cạnh của đa giác:
    Nếu cạnh tiếp giáp một cung tròn tường cong, tính bulge = tan(sweep / 4.0)
    để LWPOLYLINE bo tròn hoàn hảo.
    Trả về danh sách: [((x, y), bulge), ...]
    """
    if not curved_arcs:
        return [(p, 0.0) for p in coords]

    result = []
    n = len(coords)

    for i in range(n):
        p1 = coords[i]
        p2 = coords[(i + 1) % n]
        edge_mid = Point((p1[0] + p2[0]) / 2.0, (p1[1] + p2[1]) / 2.0)
        edge_len = math.hypot(p2[0] - p1[0], p2[1] - p1[1])

        matched_bulge = 0.0

        for center_pt, r, sa, ea in curved_arcs:
            cx, cy = center_pt.x, center_pt.y
            d1 = math.hypot(p1[0] - cx, p1[1] - cy)
            d2 = math.hypot(p2[0] - cx, p2[1] - cy)

            # Hai đầu mút gần mặt cong bán kính R (sai số < 60mm)
            if abs(d1 - r) < 60.0 and abs(d2 - r) < 60.0 and edge_len > 100.0:
                sweep = (ea - sa) % 360
                sweep_rad = math.radians(sweep)
                if 20 <= sweep <= 180:
                    bulge_val = math.tan(sweep_rad / 4.0)

                    # Xác định chiều cong (CW hay CCW)
                    v_edge = (p2[0] - p1[0], p2[1] - p1[1])
                    v_to_c = (cx - p1[0], cy - p1[1])
                    cross_prod = v_edge[0] * v_to_c[1] - v_edge[1] * v_to_c[0]

                    if cross_prod < 0:
                        matched_bulge = bulge_val
                    else:
                        matched_bulge = -bulge_val
                    break

        result.append((p1, matched_bulge))

    return result


# ==============================================================================
# 7. HỢP NHẤT KHÔNG GIAN LỚN (UNIFY LARGE OPEN SPACES)
# ==============================================================================

def unify_adjacent_polygons_for_large_spaces(valid_polys: List[Polygon],
                                             texts: List[Tuple[Point, str]],
                                             wc_polygons: List[Polygon]) -> List[Tuple[Polygon, List[str]]]:
    """
    Quy tắc Vàng cho Không Gian Lớn:
    - Với các khu vực lớn (Sân trước, Gara, Cửa hàng, Sảnh, Sân sau, Bếp - Khách...):
      Vẽ đúng 1 ĐƯỜNG BAO DUY NHẤT, không được chia cắt vụn vặt.
    - Cơ chế Thông minh:
      + Phân loại: Đa giác có tên (Named Rooms) vs Mảnh vụn không tên (Unnamed Fragments: gạch sàn, thềm, nội thất).
      + Gộp các mảnh vụn không tên vào phòng có tên kề cận nhất (chia sẻ cạnh chung > 150mm).
      + Nếu 2 đa giác có cùng nhãn phòng: Gộp lại thành 1 phòng thống nhất.
      + Tuyệt đối KHÔNG gộp xuyên qua cửa đi giữa 2 phòng chức năng độc lập (WC, Kho, Phòng ngủ...).
    """
    if not valid_polys:
        return []

    named_items: List[Tuple[Polygon, List[str]]] = []
    unnamed_polys: List[Polygon] = []

    for p in valid_polys:
        matched_names = []
        for pt, txt in texts:
            if p.contains(pt) and is_indoor_room_name(txt):
                # Làm sạch text
                clean_t = re.sub(r'S\s*=\s*[0-9\.,]+\s*m[²2]', '', txt, flags=re.IGNORECASE).strip()
                if clean_t and is_indoor_room_name(clean_t) and clean_t not in matched_names:
                    matched_names.append(clean_t)
        if matched_names:
            named_items.append((p, matched_names))
        else:
            unnamed_polys.append(p)

    def is_subzone_text(n_list: List[str]) -> bool:
        combined = " ".join(n_list).upper()
        sub_kw = ['CLOSET', 'THAY ĐỒ', 'THAY DO', 'TRANG ĐIỂM', 'TRANG DIEM', 'RELAX',
                  'TỦ ÁO', 'TU AO', 'TỦ GIÀY', 'TU GIAY', 'TIỀN SẢNH', 'TIEN SANH', 'LỐI ĐI', 'LOI DI']
        return any(k in combined for k in sub_kw)

    def is_main_room_text(n_list: List[str]) -> bool:
        combined = " ".join(n_list).upper()
        main_kw = ['MASTER', 'NGỦ', 'NGU', 'BEDROOM', 'KHÁCH', 'KHACH', 'LIVING']
        return any(k in combined for k in main_kw)

    # Gộp các phòng có cùng tên hoặc phân khu liên thông nếu chúng tiếp giáp nhau
    consolidated_named: List[Tuple[Polygon, List[str]]] = []
    used = set()
    for i in range(len(named_items)):
        if i in used:
            continue
        curr_poly, curr_names = named_items[i]
        curr_set = set(curr_names)
        to_merge = [curr_poly]

        for j in range(i + 1, len(named_items)):
            if j in used:
                continue
            other_poly, other_names = named_items[j]

            # Kiểm tra: cùng tên phòng hoặc là phân khu chức năng liên thông (Closet, Relax + Master)
            same_name = bool(curr_set.intersection(set(other_names)))
            is_subzone_combo = (is_main_room_text(curr_names) and is_subzone_text(other_names)) or \
                               (is_subzone_text(curr_names) and is_main_room_text(other_names))
            is_any_wc = any(is_wc_name(n) for n in curr_names + other_names)

            if (same_name or (is_subzone_combo and not is_any_wc)) and curr_poly.intersection(other_poly).length > 80.0:
                to_merge.append(other_poly)
                if is_main_room_text(other_names) and not is_main_room_text(curr_names):
                    curr_names = [n for n in other_names if is_main_room_text([n])] + curr_names
                used.add(j)

        if len(to_merge) > 1:
            try:
                u = unary_union(to_merge)
                if isinstance(u, MultiPolygon):
                    u = max(u.geoms, key=lambda g: g.area)
                consolidated_named.append((u, curr_names))
            except Exception:
                consolidated_named.append((curr_poly, curr_names))
        else:
            consolidated_named.append((curr_poly, curr_names))
        used.add(i)

    # Gộp các mảnh vụn không tên vào phòng có tên kề cận
    final_rooms: List[Tuple[Polygon, List[str]]] = []
    unnamed_used = set()

    for p, names in consolidated_named:
        attached = [p]
        for idx, up in enumerate(unnamed_polys):
            if idx not in unnamed_used:
                inter = p.intersection(up)
                # Chỉ gộp các hốc tường, hốc tủ âm nhỏ (< 2.5m2) nằm trong phòng, không gộp ban công hoặc không gian lớn
                if inter.length > 150.0 and up.area / 1e6 < 2.5:
                    attached.append(up)
                    unnamed_used.add(idx)

        if len(attached) > 1:
            try:
                u = unary_union(attached)
                if isinstance(u, MultiPolygon):
                    u = max(u.geoms, key=lambda g: g.area)
                final_rooms.append((u, names))
            except Exception:
                final_rooms.append((p, names))
        else:
            final_rooms.append((p, names))

    return final_rooms


def detect_floor_bounding_boxes(lines: List[LineString], min_gap: float = 4000.0) -> List[Tuple[float, float, float, float]]:
    """
    Tự động phát hiện và cô lập ranh giới các tầng độc lập (Floor Bounding Boxes).
    Phân tách các cụm nhà dọc trục Y (hoặc khoảng cách trống > min_gap).
    Đảm bảo 100% không để tia hay đa giác phóng xuyên qua tầng khác!
    """
    if not lines:
        return []
    y_intervals = []
    for l in lines:
        c1, c2 = l.coords[0], l.coords[1]
        y_intervals.append((min(c1[1], c2[1]), max(c1[1], c2[1]), min(c1[0], c2[0]), max(c1[0], c2[0])))
    y_intervals.sort(key=lambda item: item[0])
    clusters = []
    curr_ymin, curr_ymax, curr_xmin, curr_xmax = y_intervals[0]
    for ymin, ymax, xmin, xmax in y_intervals[1:]:
        if ymin <= curr_ymax + min_gap:
            curr_ymax = max(curr_ymax, ymax)
            curr_xmin = min(curr_xmin, xmin)
            curr_xmax = max(curr_xmax, xmax)
        else:
            clusters.append((curr_xmin - 500, curr_ymin - 500, curr_xmax + 500, curr_ymax + 500))
            curr_ymin, curr_ymax, curr_xmin, curr_xmax = ymin, ymax, xmin, xmax
    clusters.append((curr_xmin - 500, curr_ymin - 500, curr_xmax + 500, curr_ymax + 500))
    return clusters


def recover_unclosed_room_by_ray_casting(seed_pt: Tuple[float, float], lines: List[LineString],
                                         min_area: float = 1.0, max_area: float = 75.0,
                                         floor_bbox: Optional[Tuple[float, float, float, float]] = None,
                                         radius: float = 6500.0, num_rays: int = 72) -> Optional[Polygon]:
    """
    Thuật toán Bounded Manhattan Recovery cứu cánh cho các không gian phòng không khép kín (Open Gap Rooms):
    - Bán kính khống chế tối đa 6.5m (vừa vặn diện tích phòng, không văng bừa bãi).
    - Cắt tỉa nghiêm ngặt (clamp) trong hộp ranh giới tầng (floor_bbox).
    - Nắn trực giao Manhattan 90° & 270°, loại bỏ hoàn toàn các nét xiên vát nhọn.
    """
    sx, sy = seed_pt
    if floor_bbox:
        b_minx, b_miny, b_maxx, b_maxy = floor_bbox
        if not (b_minx <= sx <= b_maxx and b_miny <= sy <= b_maxy):
            return None
    else:
        b_minx, b_miny, b_maxx, b_maxy = -1e9, -1e9, 1e9, 1e9

    seed_point = Point(sx, sy)
    local_lines = [l for l in lines if l.distance(seed_point) <= radius]
    if not local_lines:
        return None

    tree = STRtree(local_lines)
    hit_points = []
    angle_step = 360.0 / num_rays

    for idx in range(num_rays):
        deg = idx * angle_step
        rad = math.radians(deg)
        ex = sx + radius * math.cos(rad)
        ey = sy + radius * math.sin(rad)

        if floor_bbox:
            ex = max(b_minx + 50.0, min(b_maxx - 50.0, ex))
            ey = max(b_miny + 50.0, min(b_maxy - 50.0, ey))

        ray = LineString([(sx, sy), (ex, ey)])
        hits = []

        try:
            for cand_idx in tree.query(ray):
                cand_l = local_lines[cand_idx]
                if ray.intersects(cand_l):
                    inter = ray.intersection(cand_l)
                    if isinstance(inter, Point):
                        d = math.hypot(inter.x - sx, inter.y - sy)
                        if d >= 30.0:
                            hits.append((d, inter))
                    elif isinstance(inter, LineString):
                        d = math.hypot(inter.coords[0][0] - sx, inter.coords[0][1] - sy)
                        if d >= 30.0:
                            hits.append((d, Point(inter.coords[0])))
                    elif hasattr(inter, 'geoms'):
                        for g in inter.geoms:
                            if isinstance(g, Point):
                                d = math.hypot(g.x - sx, g.y - sy)
                                if d >= 30.0:
                                    hits.append((d, g))
        except Exception:
            continue

        if hits:
            hits.sort(key=lambda h: h[0])
            hit_points.append(hits[0][1])

    if len(hit_points) < 8:
        return None

    try:
        poly_raw = Polygon([(p.x, p.y) for p in hit_points])
        if not poly_raw.is_valid:
            poly_raw = poly_raw.buffer(0)
            if isinstance(poly_raw, MultiPolygon):
                poly_raw = max(poly_raw.geoms, key=lambda g: g.area)

        if poly_raw.is_empty or not isinstance(poly_raw, Polygon):
            return None

        area_m2 = poly_raw.area / 1e6
        if area_m2 < min_area or area_m2 > max_area or not poly_raw.contains(seed_point):
            return None

        # Nắn trực giao Manhattan 90° & 270°, triệt tiêu hoàn toàn nét xiên vát
        ext_coords = list(poly_raw.exterior.coords)
        rectified = rectify_manhattan_and_preserve_skewed(ext_coords, poly_raw)
        rect_poly = Polygon(rectified)
        if not rect_poly.is_valid:
            rect_poly = rect_poly.buffer(0)
        if isinstance(rect_poly, MultiPolygon):
            rect_poly = max(rect_poly.geoms, key=lambda g: g.area)

        if rect_poly.is_empty or not isinstance(rect_poly, Polygon):
            return poly_raw

        return rect_poly
    except Exception:
        return None


# ==============================================================================
# 8. HÀM XỬ LÝ CHÍNH (MAIN PROCESSOR)
# ==============================================================================

def process_floor_plan(dxf_path: str, out_lsp_path: str, min_area: float, max_area: float,
                       bbox_filter=None, pick_point=None, point_radius=20000.0):
    if pick_point:
        px, py = pick_point
        bbox_filter = (px - point_radius, py - point_radius, px + point_radius, py + point_radius)
        print(f"[SMART-ENGINE] Đang xử lý phòng tại điểm click: ({px:.1f}, {py:.1f})")

    print(f"[1/4] Đang mở bản vẽ DXF: {dxf_path}...")
    doc = ezdxf.readfile(dxf_path)

    print(f"[2/4] Đang phân tích hình học không gian, quét trục lưới & bịt cửa...")
    all_lines, columns, texts, curved_arcs = extract_cad_features(doc, bbox_filter)
    print(f"      - Nét hình học tường/cột đã khóa kín & bịt cửa: {len(all_lines)}")
    print(f"      - Khối cột kết cấu nhận diện: {len(columns)}")
    print(f"      - Cung tròn tường cong nhận diện: {len(curved_arcs)}")
    print(f"      - Nhãn tên phòng tìm thấy: {len(texts)}")

    if not all_lines:
        print("[WARN] Không tìm thấy đoạn thẳng nào trong phạm vi quét!")
        return

    print(f"[3/4] Đang giải thuật tìm đa giác phòng & phân lập ranh giới từng tầng...")
    
    # 1. Phát hiện các tầng độc lập (Floor Isolation)
    floor_boxes = detect_floor_bounding_boxes(all_lines)
    if not floor_boxes:
        xs = [pt[0] for l in all_lines for pt in l.coords]
        ys = [pt[1] for l in all_lines for pt in l.coords]
        floor_boxes = [(min(xs)-500, min(ys)-500, max(xs)+500, max(ys)+500)]

    print(f"      - Phát hiện {len(floor_boxes)} tầng/khối nhà độc lập.")

    all_floor_rooms = []

    for f_idx, fb in enumerate(floor_boxes):
        b_minx, b_miny, b_maxx, b_maxy = fb
        
        # Lọc nét và text nằm trong tầng này
        f_lines = [l for l in all_lines if (b_minx <= l.coords[0][0] <= b_maxx and b_miny <= l.coords[0][1] <= b_maxy)]
        if not f_lines:
            continue
            
        f_texts = [(pt, txt) for pt, txt in texts if (b_minx <= pt.x <= b_maxx and b_miny <= pt.y <= b_maxy)]

        # Tìm đa giác khép kín bằng polygonize bên trong tầng
        merged_lines = unary_union(f_lines)
        raw_polygons = [p for p in polygonize(merged_lines) if min_area <= p.area / 1e6 <= max_area]

        # Nhận diện WC tầng này
        wc_points = [pt for pt, txt in f_texts if is_wc_name(txt)]
        wc_polygons = [p for p in raw_polygons if any(p.contains(wpt) for wpt in wc_points) and p.area / 1e6 < 30.0]

        # Hợp nhất các phân khu liên thông trong cùng tầng (Pure Planar Cycle)
        unified_rooms = unify_adjacent_polygons_for_large_spaces(raw_polygons, f_texts, wc_polygons)
        for poly, names in unified_rooms:
            all_floor_rooms.append((poly, names))

    if pick_point:
        target_pt = Point(pick_point[0], pick_point[1])
        candidates = [(p, names) for p, names in all_floor_rooms if p.contains(target_pt)]
        if not candidates:
            near_cands = [(p, names) for p, names in all_floor_rooms if p.distance(target_pt) <= 300.0]
            if near_cands:
                candidates = near_cands

        if candidates:
            best_p, best_names = min(candidates, key=lambda item: item[0].area)
            target_rooms = [(best_p, best_names)]
        else:
            print("[WARN] Không tìm thấy phòng kín hợp lệ chứa điểm click vừa chọn!")
            return
    else:
        target_rooms = all_floor_rooms

    valid_rooms = []
    room_idx = 1

    for cur_poly, room_names in target_rooms:
        final_area_m2 = cur_poly.area / 1e6
        if final_area_m2 < min_area or final_area_m2 > max_area:
            continue

        unique_texts = []
        for t in room_names:
            t_clean = t.strip()
            if t_clean and t_clean not in unique_texts:
                unique_texts.append(t_clean)

        # Lọc bỏ tên con/tên ngắn nếu đã được chứa trong tên dài hơn (vd: 'LÀM VIỆC' nằm trong 'P. LÀM VIỆC')
        filtered_names = []
        for n in unique_texts:
            if any(n != other and n in other for other in unique_texts):
                continue
            filtered_names.append(n)

        room_name = " - ".join(filtered_names) if filtered_names else f"PHÒNG {room_idx:02d}"
        room_code = f"P-{room_idx:02d}"

        # CHUẨN HÓA TRỰC GIAO 90° & ÔM TƯỜNG XÉO
        try:
            ext_coords = list(cur_poly.exterior.coords)
            rectified_coords = rectify_manhattan_and_preserve_skewed(ext_coords, cur_poly)
            if len(rectified_coords) < 3:
                rectified_coords = clean_duplicate_points(ext_coords)
        except Exception:
            rectified_coords = clean_duplicate_points(list(cur_poly.exterior.coords))

        # GẮN BULGE CHO CÁC ĐOẠN TƯỜNG CONG BO TRÒN
        vertex_bulges = assign_bulge_to_vertices(rectified_coords, curved_arcs)

        # Trọng tâm phòng (Centroid) để đặt nhãn text
        centroid = cur_poly.representative_point()

        valid_rooms.append({
            "code": room_code,
            "name": room_name,
            "area": round(final_area_m2, 2),
            "perimeter": round(cur_poly.exterior.length / 1e3, 2),
            "vertices": vertex_bulges, # [((x, y), bulge), ...]
            "centroid": (centroid.x, centroid.y)
        })
        room_idx += 1

    print(f"[4/4] Đã hoàn thành xử lý {len(valid_rooms)} phòng (Hợp nhất không gian lớn, ôm tường xéo, bo tròn tường cong).")
    print(f"      Đang xuất file AutoLISP: {out_lsp_path}...")

    # Ghi file AutoLISP
    with open(out_lsp_path, "w", encoding="utf-8") as f:
        f.write(";;; Universal Smart Room Boundary AutoLISP Output v4.2\n")
        f.write("(defun c:VE_DUONG_BAO ()\n")
        f.write("  (setvar \"CMDECHO\" 0)\n")
        f.write("  (if (not (tblsearch \"APPID\" \"SMART_ROOM_BOUNDARY\"))\n")
        f.write("    (regapp \"SMART_ROOM_BOUNDARY\")\n")
        f.write("  )\n")
        f.write("  (if (not (tblsearch \"LAYER\" \"DUONG_BAO\"))\n")
        f.write("    (command \"-LAYER\" \"M\" \"DUONG_BAO\" \"C\" \"1\" \"DUONG_BAO\" \"LW\" \"0.35\" \"DUONG_BAO\" \"\")\n")
        f.write("  )\n")
        f.write("  (if (not (tblsearch \"LAYER\" \"DUONG_BAO_TEXT\"))\n")
        f.write("    (command \"-LAYER\" \"M\" \"DUONG_BAO_TEXT\" \"C\" \"3\" \"DUONG_BAO_TEXT\" \"\")\n")
        f.write("  )\n\n")

        for r in valid_rooms:
            v_list = r["vertices"]
            f.write(f"  ;; Phòng: {r['name']} ({r['code']}) - Diện tích: {r['area']} m2\n")
            f.write(f"  (setq pl-ent (list '(0 . \"LWPOLYLINE\") '(100 . \"AcDbEntity\") '(8 . \"DUONG_BAO\")\n")
            f.write(f"                     '(100 . \"AcDbPolyline\") (cons 90 {len(v_list)}) '(70 . 1)))\n")

            for (pt, bulge) in v_list:
                f.write(f"  (setq pl-ent (append pl-ent (list (cons 10 '({pt[0]:.2f} {pt[1]:.2f})))))\n")
                if abs(bulge) > 1e-4:
                    f.write(f"  (setq pl-ent (append pl-ent (list (cons 42 {bulge:.5f}))))\n")

            f.write("  (setq xd (list \"SMART_ROOM_BOUNDARY\"\n")
            f.write(f"                 '(1000 . \"{r['code']}\")\n")
            f.write(f"                 '(1000 . \"{r['name']}\")\n")
            f.write(f"                 '(1040 . {r['area']})\n")
            f.write(f"                 '(1040 . {r['perimeter']})\n")
            f.write("                 '(1040 . 2800.0)))\n")
            f.write("  (setq pl-ent (append pl-ent (list (cons -3 (list xd)))))\n")
            f.write("  (entmakex pl-ent)\n\n")

            # Tạo MText hiển thị Tên phòng + Diện tích tại tâm phòng
            cx, cy = r["centroid"]
            f.write(f"  ;; MText tên phòng & diện tích\n")
            f.write(f"  (entmakex (list '(0 . \"MTEXT\") '(100 . \"AcDbEntity\") '(8 . \"DUONG_BAO_TEXT\")\n")
            f.write(f"                  '(100 . \"AcDbMText\") (cons 10 '({cx:.2f} {cy:.2f} 0.0))\n")
            f.write(f"                  (cons 40 250.0) (cons 71 5) (cons 1 \"{r['name']}\\\\PS = {r['area']:.2f} m²\")))\n\n")

        f.write("  (setvar \"CMDECHO\" 1)\n")
        f.write(f"  (princ \"\\n[XLMB] Đã tạo thành công {len(valid_rooms)} đường bao phòng & nhãn diện tích!\\n\")\n")
        f.write("  (princ \"\\nĐã vẽ xong mời bạn Check\\n\")\n")
        f.write("  (princ)\n")
        f.write(")\n")
        f.write("(c:VE_DUONG_BAO)\n")

    print("[SUCCESS] Hoàn thành xuất file AutoLISP.")


if __name__ == "__main__":
    args = parse_arguments()
    process_floor_plan(args.dxf, args.out, args.min_area, args.max_area,
                       args.bbox, args.point, args.radius)

﻿﻿# Bộ Quy Chuẩn Kỹ Thuật AutoCAD Add-in (AI Coding Rules)

Tài liệu này là quy chuẩn kỹ thuật bắt buộc khi phát triển add-in AutoCAD (.NET C#/WPF). Mọi phản hồi và mã nguồn sinh ra phải tuân thủ nghiêm ngặt 5 phần dưới đây.

---

## 1. Cổng Thẩm Định Đặc Tính & Xác Nhận Bắt Buộc (The 85%+ Gate)

> [!IMPORTANT]
> **ĐIỀU KIỆN TIÊN QUYẾT BẮT BUỘC TRƯỚC KHI CHẠY CODE**: Cấm tuyệt đối hành vi "viết mã mù quáng" (Blind Coding) khi chưa khảo sát đầy đủ đặc tính kỹ thuật và chưa có sự xác nhận của kỹ sư.

1. **Khóa mã nguồn (Pre-Coding Block):** Khi nhận yêu cầu mới hoặc sửa đổi tính năng, AI TUYỆT ĐỐI KHÔNG ĐƯỢC sinh mã hay sửa file code ngay.
2. **Luôn luôn hỏi các đặc tính chức năng:** Chủ động khảo sát chi tiết qua công cụ `ask_question` (kèm phương án khuyến nghị `(Recommended)`) dựa trên Khung 5 Trụ Cột (Mục 5).
3. **Ngưỡng dữ liệu thu thập $\ge 85\%$:** Liên tục truy vấn đến khi dữ liệu thu thập đạt tối thiểu 85% độ rõ ràng về hình học CAD, vị trí, layer, công thức và UI. Chưa đạt 85% → **CẤM DỪNG HỎI**.
4. **Vòng lặp xác nhận (Loop Until User Confirmation):**
   * Đạt $\ge 85\%$ → Xuất **Bản Đặc Tả Kỹ Thuật Tinh Gọn (Mini Spec)** (10–15 dòng: Input → Logic → CAD Entities → UI → Edge Cases).
   * **Hỏi xin xác nhận rõ ràng**: Chỉ khi kỹ sư bấm/gõ **Xác nhận / Đồng ý / Chốt phương án**, AI mới được phép viết code và biên dịch. Nếu chưa xác nhận hoặc yêu cầu chỉnh sửa, AI tiếp tục hỏi lại cho đến khi được duyệt 100%.

---

## 2. Tối Ưu Hóa Token & Kiến Trúc Dự Án

* **Diff-Only Output:** Chỉ xuất phương thức/khối lệnh thay đổi, giữ nguyên phần khác bằng `// ... existing code ...`. Tuyệt đối không in lại cả file nếu không được yêu cầu.
* **Zero Fluff:** Bỏ lời chào xã giao, không giải thích lại lý thuyết cơ bản CAD API (`Transaction`, `Database`, `Editor`). Trả lời trực diện vào mã nguồn và giải pháp.
* **Shared Helpers:** Đóng gói thao tác lặp lại vào static helper (`CadHelper`, `LayerHelper`, `UiHelper`). Lệnh `[CommandMethod]` chỉ đóng vai trò điểm vào mỏng (1 dòng gọi Service/View).
* **Cấu trúc phân tầng chuẩn:**
  * `Core/`: Extension methods, CAD API wrapper (`AcDbMgd`, `AcMgd`, `AcCoreMgd`).
  * `Models/`: DTO/POCO thuần túy, độc lập với `ObjectId`, an toàn đa luồng.
  * `Services/`: Logic nghiệp vụ (tính toán, thuật toán, trích xuất dữ liệu).
  * `UI/`: WPF MVVM (`Views/`, `ViewModels/`, `Themes/Theme.xaml`).
  * `Commands/`: Entry point mỏng gọi Service/View.

---

## 3. Phân Loại Tác Vụ CAD: Fast Task vs. Heavy Data Task

| Tiêu chuẩn kỹ thuật | Fast Task (< 500 đối tượng) | Heavy Data Task (> 1.000 đối tượng) |
| :--- | :--- | :--- |
| **Cơ chế Transaction** | 1 Transaction duy nhất hoặc `StartOpenCloseTransaction()`. | Chia lô (**Batch 2.000–5.000 đối tượng/tr**) hoặc dùng `Side Database` (`new Database(false, true)` + `ReadDwgFile`). |
| **Đa luồng & An toàn** | Chạy trực tiếp trên Main Thread (STA). | **CẤM đa luồng trên AcDb Database**. Quy trình 3 bước chuẩn: **Extract** (Main Thread) → **Compute** (`Task.Run` / `Parallel.ForEach`) → **Apply** (`doc.LockDocument()` trên Main Thread). |
| **Tương tác & Hiển thị**| Lọc nhanh tại chỗ bằng `SelectionFilter` + `RXClass`. Xem trước mượt bằng `EntityJig`/`DrawJig` (60 FPS). | Tắt làm mới đồ họa tạm thời, gom nhóm Undo, hiển thị Progress Bar có nút Hủy. |

---

## 4. Chuẩn UI/UX WPF Dark Mode & An Toàn Thực Chiến

### 4.1. Bảng màu Dark Mode Tương Phản Cao (WCAG AAA)
* **Nền chính:** `#12161F` (RGB: 18, 22, 31) | **Nền Card/Group:** `#1E2433` (RGB: 30, 36, 51)
* **Hover/Active:** `#2A3246` (RGB: 42, 50, 70) | **Viền mảnh 1px:** `#333D54` (RGB: 51, 61, 84)
* **Nhấn chính (Cyan):** `#00E5FF` (RGB: 0, 229, 255) | **Nhấn phụ (Amber):** `#FF9100` (RGB: 255, 145, 0)
* **Chữ chính:** `#F8FAFC` | **Chữ phụ/Đơn vị:** `#94A3B8`
* **Trạng thái:** Thành công `#00E676` | Cảnh báo `#FFD600` | Nguy hiểm/Lỗi `#FF5252`
* **Bố cục & Bo góc:** 8px spatial grid (`Margin`/`Padding` bội số 4, 8). `CornerRadius="6px"` cho button/input, `8px` cho Card.

### 4.2. Typography & Tiếng Việt Kỹ Thuật
* **100% Tiếng Việt có dấu:** Chuẩn thuật ngữ xây dựng – cơ điện trên toàn bộ UI, tooltip, thông báo và dòng nhắc lệnh CAD (`Editor.WriteMessage`).
* **Font UI:** `"Segoe UI Variable Display, Inter, Roboto, Segoe UI, sans-serif"`.
* **Font Số liệu/Tọa độ (Monospace):** `"JetBrains Mono, Cascadia Code, Consolas"` (áp dụng cho cột tọa độ X, Y, Z, công suất, chiều dài).
* **Mã hóa file:** Bắt buộc lưu mọi file `.cs`, `.xaml`, `.md` ở định dạng **UTF-8 with BOM**.

### 4.3. An Toàn Bản Vẽ & Hệ Thống
* **Document Lock:** Bắt buộc dùng `using (DocumentLock docLock = doc.LockDocument())` trước khi mở Transaction từ Palette/Modeless Window để chống lỗi `eLockViolation`.
* **Commit/Abort an toàn:** Luôn gọi `tr.Commit()` ở dòng cuối của `try`. Bọc `using (Transaction tr = ...)` để tự động `Abort()` nếu có ngoại lệ.
* **Hệ tọa độ WCS $\leftrightarrow$ UCS:** Lấy điểm từ Editor (UCS) phải `.TransformBy(ed.CurrentUserCoordinateSystem)` về Database (WCS) và ngược lại bằng `.Inverse()`.
* **Biến hệ thống & Undo:** Lưu và hoàn trả biến CAD (`OSMODE`, `CMDECHO`) trong khối `finally`. Gom cụm thao tác nhiều bước vào 1 mốc Undo (`Ctrl+Z`).
* **Đa phiên bản:** Multi-targeting `<TargetFrameworks>net48;net8.0-windows</TargetFrameworks>` (.NET 4.8 cho CAD 2021–2024, .NET 8 cho CAD 2025–2026).

### 4.4. Tự Động Cập Nhật Palette (Hot-Reload NTTRELOAD)
Sau mỗi lần sửa mã nguồn hoặc giao diện Palette:
1. Biên dịch Release x64 đảm bảo **0 lỗi**.
2. Safe-Copy DLL: Ghi đè file DLL vào bundle dự án và `%APPDATA%\Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\`.
3. Kích hoạt lệnh `NTTRELOAD` qua MCP tool `send_command` để nạp in-memory byte loader (`Assembly.Load(rawBytes)`), cập nhật tức thì trên CAD mà không cần khởi động lại.

---

## 5. Khung 5 Trụ Cột Khảo Sát Đặc Tính (5-Pillars Checklist)

Khi phỏng vấn thu thập thông tin đạt ngưỡng $\ge 85\%$, AI đối chiếu 5 trụ cột sau:
1. **Nghiệp vụ & Trình tự:** Chiều dòng điện/dòng tín hiệu từ đâu đến đâu? Thiết bị nào nằm trước, thiết bị nào nằm sau? Công thức/tiêu chuẩn áp dụng (TCVN, IEC, NFPA)?
2. **Giao diện & Thao tác:** Kích hoạt từ Palette dock hay Modal? Có Live Preview Canvas đồng bộ 1:1 không? Bố cục các nút bấm?
3. **Hình học CAD & Thư viện:** Dùng block mẫu file DWG hay vẽ hình học thuần? Tên Layer, ColorIndex, kiểu nét? Hướng xoay góc, tiếp điểm mở về hướng nào (`flipped`), khoảng cách offset, tỷ lệ scale?
4. **Tình huống biên (Edge Cases):** Bấm ESC giữa chừng, chọn sai đối tượng, layer bị khóa, dữ liệu âm/rỗng xử lý an toàn ra sao?
5. **Hiệu năng & Môi trường:** Fast Task hay Heavy Task? Phiên bản AutoCAD mục tiêu (.NET 4.8 hay .NET 8)?

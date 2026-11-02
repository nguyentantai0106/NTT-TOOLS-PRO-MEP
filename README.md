# NTT-MEP-CAD-Addin

Bộ công cụ Add-in AutoCAD (.NET C#/WPF) & Cầu nối AI MCP Server chuyên dụng cho Kỹ sư Cơ Điện MEP (Cơ - Điện - Nước - HVAC - PCCC).

---

## 🚀 Các Tính Năng Chính

1. **Sơ Đồ Nguyên Lý Tủ Điện (Single Line Diagram - SLD):**
   * Tự động tính toán phụ tải, dòng định mức, sụt áp TCVN & cân pha thông minh (LPT).
   * Khởi động động cơ DOL, Star-Delta (Y-Δ 2x3C), VFD, SoftStarter kèm điều khiển liên động.
   * Tự động tính toán dung lượng tụ bù kVAR và kiểm tra ngắn mạch Isc/Icu.
   * Vẽ sơ đồ đơn tuyến chuẩn hóa, mặt ngoài cánh tủ và khung tên tiêu chuẩn A3.

2. **Cây Phân Phối Tủ Điện (Riser Diagram):**
   * Quản lý cây phân cấp nguồn từ Trạm biến áp / MSB đến các tủ phụ tầng/khu vực.

3. **Thang Máng Cáp MEP (Cable Tray / Trunking / Ladder):**
   * Vẽ tương tác trực quan thời gian thực (Live Dynamic Draw Jig).
   * Tự động sinh tuyến theo tim Polyline, tạo co lượn (Elbow) và rải giá treo Unistrut 1.5m.
   * Thống kê khối lượng vật tư và xuất bảng BOM trực tiếp lên bản vẽ CAD.

4. **Xử Lý Mặt Bằng (XLMB - Smart Room Boundary):**
   * Quét tự động giải mã XREF và Block lồng nhau.
   * Nhận diện hình học tường và cột, tự động bo viền phòng kín và tính diện tích thông thủy.
   * Gán XData định danh phòng (SMART_ROOM_BOUNDARY).

5. **AI Remote Control qua MCP Server (`ntt-cad-bridge`):**
   * Tích hợp Local REST API nội bộ trên cổng 5005.
   * Cho phép các AI Assistant (Antigravity, Claude, Cursor) trực tiếp gọi các lệnh vẽ và phân tích CAD thông qua Model Context Protocol (MCP).
   * Khóa bảo mật: Chỉ kích hoạt khi kỹ sư cấp quyền.

---

## 🛠️ Công Nghệ & Môi Trường

* **Ngôn ngữ:** C# (.NET Framework 4.8 / .NET 8.0), Python 3.10+ (MCP Server).
* **Giao diện:** WPF XAML Dark Mode chuẩn WCAG AAA.
* **AutoCAD API:** `acdbmgd.dll`, `acmgd.dll`, `accoremgd.dll`.
* **Cơ chế Hot-Reload:** `NTTRELOAD` (In-Memory Byte Loader).

---

## 👤 Tác Giả

* **Kỹ sư:** Nguyễn Tấn Tài

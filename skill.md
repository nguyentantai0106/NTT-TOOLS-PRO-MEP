---
name: cad-mep-addin-builder
description: Tạo các chức năng hệ thống MEP (Cơ - Điện - Nước) tốc độ cao, tiết kiệm token, tích hợp thư viện block đa nguồn và giao diện WPF UI/UX chuẩn mực cho add-in Autodesk CAD/Revit.
---

# Kỹ Thuật Add-in CAD MEP (CAD MEP Addin Builder)

Quy chuẩn kỹ thuật chuyên sâu cho add-in MEP (Cơ - Điện - Nước) trên Autodesk CAD (.NET C#/WPF), tối ưu tốc độ sinh mã, chống sập ứng dụng và vận hành thư viện block đa nguồn.

---

## 1. Tối Ưu Hóa Token & Tốc Độ Sinh Mã
* **Delta-Only Code:** Chỉ xuất phương thức/khối lệnh thay đổi, giữ nguyên phần khác bằng `// ... existing code ...`.
* **Metadata Schema Nhẹ:** Nén dữ liệu block thành JSON siêu nhẹ (ID, hệ, tên file DWG, connector), không truyền dữ liệu hình học thô vào prompt.
* **Kiểu dữ liệu mạnh:** Thao tác trực tiếp qua LINQ, `ObjectId`, `BlockTableRecord` (CAD), tránh reflection chậm.

---

## 2. Thư Viện Block MEP Đa Nguồn (Unified Multi-Source Engine)

### 2.1. Phân Tầng Hệ & Hệ Con Toàn Diện
* **Hệ Cơ (HVAC):**
  * *Hệ con:* Gió lạnh (SA), Gió hồi (RA), Gió tươi (FA), Hút khói (SE), Thông gió (EA), Tăng áp (SP), Chiller/Condenser (CHW/CW).
  * *Block:* Miệng gió (diffuser, linear slot, louver), van gió (VAV, MD, FD, NRD), thiết bị (AHU, FCU, PAU, quạt ly tâm, jetfan, chiller, cooling tower).
* **Hệ Điện (Electrical):**
  * *Hệ con:* Động lực (Power), Chiếu sáng (Lighting), Điện nhẹ (ELV/ICT/CCTV), Chống sét & Tiếp địa (Lightning/Earthing).
  * *Block:* Tủ điện (MDB, DB), đèn, công tắc, ổ cắm, khay máng cáp (cable tray), ống luồn (conduit), đầu báo cháy, kim thu sét.
* **Hệ Nước & PCCC (Plumbing & Fire):**
  * *Hệ con:* Cấp nước lạnh/nóng (DCW/DHW), Thoát nước thải/mưa (Soil/Waste/Storm), Chữa cháy (Sprinkler, Hydrant).
  * *Block:* Cụm bơm, bình áp, van PRV/cổng, đồng hồ nước, đầu phun sprinkler, tủ chữa cháy, hố ga, phễu thu, bẫy mỡ.

### 2.2. Nguồn Truy Xuất & Quy Trình Nạp Block An Toàn
* **Đa nguồn:** File local template (`.dwg`, `.dwt`), Server nội bộ/Cloud (ACC/BIM360), hoặc quét `BlockTableRecord` có sẵn trong bản vẽ.
* **Lazy Loading & Cache:** Chỉ nạp file DWG nguồn khi kích hoạt phân hệ; lưu cache `ObjectId` trong RAM phiên làm việc.
* **Deep Clone an toàn:** Nhập block từ file ngoài bằng `Database.Insert()` trong 1 transaction duy nhất; tự động đổi tên khi trùng.
* **Chuẩn hóa Gốc & Đơn vị:** Đặt BasePoint tại tâm hình học, tỷ lệ scale 1:1. File nguồn Inch/Feet tự động scale hệ số `25.4` sang Metric (mm).

---

## 3. An Toàn Cơ Sở Dữ Liệu & Khóa Bản Vẽ
* **Document Lock:** Bắt buộc bọc `using (doc.LockDocument())` trước khi mở Transaction từ Palette/Modeless.
* **Transaction Scope:** Bọc thao tác trong `using (Transaction tr = db.TransactionManager.StartTransaction())`, gọi `tr.Commit()` cuối khối `try`.
* **RollBack Tự Động:** Tự động hoàn tác an toàn khi gặp ngoại lệ chèn block hoặc thiếu connector.
* **Tối ưu đồ họa:** Tắt `Database.TransactionManager.EnableGraphicsFlush(false)` khi chèn lô, chỉ bật lại khi commit.

---

## 4. Chuẩn Giao Diện WPF Palette Thư Viện
* **TreeView 3 tầng:** `Hệ chính (Cơ/Điện/Nước)` → `Hệ con (SA/Power/Sprinkler)` → `Nhóm thiết bị`.
* **Thumbnail Grid:** Lưới icon/vector thu nhỏ, tìm kiếm nhanh thời gian thực theo từ khóa/kích thước.
* **Kéo thả 1-Click:** Kéo block trực tiếp thả vào bản vẽ CAD hoặc pick điểm chèn với thông số định sẵn.
* **Bất đồng bộ mượt mà:** Nạp thư viện bằng `async/await` ngoài luồng UI, hiển thị ProgressBar tránh treo ứng dụng.

---

## 5. Bẫy Kỹ Thuật Bắt Buộc Tránh (Gotchas)
1. **Trùng tên Block (Name Collision):** Thêm prefix phân hệ khi nhập từ nhiều nguồn (ví dụ: `HVAC_SA_Diffuser_600x600`).
2. **Cấm nạp đồng bộ trên UI Thread:** Không đọc DWG nặng trên luồng giao diện; đọc ngầm bằng `Database(false, true)` rồi đưa về Main Thread.
3. **Bảo toàn Connector & Attribute:** Giữ nguyên thuộc tính CAD (TAG) và cổng kết nối để tính toán lưu lượng, tải điện và định tuyến tự động.
4. **Chuẩn hóa đơn vị:** Kiểm tra đơn vị nguồn (`INSUNITS`), tự động scale về Millimeters.

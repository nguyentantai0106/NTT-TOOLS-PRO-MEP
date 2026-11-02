using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ xuất Bảng tính toán phụ tải & Bóc tách khối lượng vật tư (BOQ) tủ điện ra file Excel .xls
    /// Sử dụng chuẩn XML Spreadsheet 2003 (SpreadsheetML) không cần cài Microsoft Office.
    /// Kẻ khung viền đầy đủ, căn chỉnh độ rộng cột và chiều cao hàng vừa vặn nội dung.
    /// </summary>
    public class ExcelExportService
    {
        public bool ExportPanelToExcel(ElectricalPanel panel, string filePath)
        {
            if (panel == null || string.IsNullOrEmpty(filePath)) return false;

            try
            {
                StringBuilder sb = new StringBuilder();

                // 1. XML Header & Workbook Declaration
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
                sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
                sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
                sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
                sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
                sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

                // 2. Document Properties
                sb.AppendLine(" <DocumentProperties xmlns=\"urn:schemas-microsoft-com:office:office\">");
                sb.AppendLine("  <Author>NTT TOOLS - Nguyen Tan Tai</Author>");
                sb.AppendLine("  <LastAuthor>NTT TOOLS</LastAuthor>");
                sb.AppendLine(string.Format("  <Created>{0:yyyy-MM-ddTHH:mm:ssZ}</Created>", DateTime.UtcNow));
                sb.AppendLine(string.Format("  <Title>Bảng Tính Phụ Tải &amp; BOQ Tủ Điện {0}</Title>", EscapeXml(panel.PanelName)));
                sb.AppendLine(" </DocumentProperties>");

                // 3. Styles Collection
                sb.AppendLine(" <Styles>");

                // Default Style
                sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
                sb.AppendLine("   <Alignment ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Color=\"#000000\"/>");
                sb.AppendLine("  </Style>");

                // Title Style (Tiêu đề lớn)
                sb.AppendLine("  <Style ss:ID=\"Title\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"15\" ss:Bold=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("  </Style>");

                // Subtitle Style
                sb.AppendLine("  <Style ss:ID=\"Subtitle\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Italic=\"1\" ss:Color=\"#555555\"/>");
                sb.AppendLine("  </Style>");

                // Info Label & Value
                sb.AppendLine("  <Style ss:ID=\"InfoLabel\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Left\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#333333\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#F0F4F8\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"InfoValue\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Left\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#005A9E\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#CCCCCC\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                // Table Header Style (Xanh Navy đậm, chữ trắng)
                sb.AppendLine("  <Style ss:ID=\"Header\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\" ss:WrapText=\"1\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#005A9E\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                // SubHeader
                sb.AppendLine("  <Style ss:ID=\"SubHeader\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#007ACC\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                // Data Cells
                sb.AppendLine("  <Style ss:ID=\"CellCenter\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellLeft\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Left\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellNumber\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <NumberFormat ss:Format=\"#,##0.00\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellPhase3P\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#D32F2F\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellPhase1P\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10\" ss:Bold=\"1\" ss:Color=\"#2E7D32\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellStarter\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#E65100\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellDropOk\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Color=\"#2E7D32\"/>");
                sb.AppendLine("   <NumberFormat ss:Format=\"0.00%\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"CellDropExceeded\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"9.5\" ss:Bold=\"1\" ss:Color=\"#D32F2F\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#FFEBEE\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <NumberFormat ss:Format=\"0.00%\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D32F2F\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D32F2F\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D32F2F\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D32F2F\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"TotalRow\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Center\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10.5\" ss:Bold=\"1\" ss:Color=\"#B71C1C\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#FFF3E0\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Double\" ss:Weight=\"3\" ss:Color=\"#B71C1C\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#B71C1C\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine("  <Style ss:ID=\"TotalRowNumber\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Right\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10.5\" ss:Bold=\"1\" ss:Color=\"#B71C1C\"/>");
                sb.AppendLine("   <NumberFormat ss:Format=\"#,##0.00\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#FFF3E0\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Double\" ss:Weight=\"3\" ss:Color=\"#B71C1C\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#B71C1C\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#D0D0D0\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                // BOQ Category Header
                sb.AppendLine("  <Style ss:ID=\"BOQCategory\">");
                sb.AppendLine("   <Alignment ss:Horizontal=\"Left\" ss:Vertical=\"Center\"/>");
                sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" ss:Size=\"10.5\" ss:Bold=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("   <Interior ss:Color=\"#E3F2FD\" ss:Pattern=\"Solid\"/>");
                sb.AppendLine("   <Borders>");
                sb.AppendLine("    <Border ss:Position=\"Bottom\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Top\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Left\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("    <Border ss:Position=\"Right\" ss:LineStyle=\"Continuous\" ss:Weight=\"1\" ss:Color=\"#003366\"/>");
                sb.AppendLine("   </Borders>");
                sb.AppendLine("  </Style>");

                sb.AppendLine(" </Styles>");

                // ==========================================
                // WORKSHEET 1: BẢNG TÍNH TOÁN PHỤ TẢI CHI TIẾT
                // ==========================================
                sb.AppendLine(string.Format(" <Worksheet ss:Name=\"{0}\">", EscapeXml(panel.PanelName)));
                sb.AppendLine("  <Table ss:DefaultRowHeight=\"20\">");

                // Căn chỉnh độ rộng từng cột
                sb.AppendLine("   <Column ss:Width=\"35\"/>");   // Cột 1: STT
                sb.AppendLine("   <Column ss:Width=\"180\"/>");  // Cột 2: Tên Phụ Tải
                sb.AppendLine("   <Column ss:Width=\"55\"/>");   // Cột 3: Số Mạch
                sb.AppendLine("   <Column ss:Width=\"60\"/>");   // Cột 4: Pha
                sb.AppendLine("   <Column ss:Width=\"75\"/>");   // Cột 5: Tổng P (kW)
                sb.AppendLine("   <Column ss:Width=\"60\"/>");   // Cột 6: Pha R
                sb.AppendLine("   <Column ss:Width=\"60\"/>");   // Cột 7: Pha Y
                sb.AppendLine("   <Column ss:Width=\"60\"/>");   // Cột 8: Pha B
                sb.AppendLine("   <Column ss:Width=\"70\"/>");   // Cột 9: Dòng Itt (A)
                sb.AppendLine("   <Column ss:Width=\"130\"/>");  // Cột 10: Aptomat (MCCB/MCB)
                sb.AppendLine("   <Column ss:Width=\"220\"/>");  // Cột 11: Cáp Điện Đầy Đủ
                sb.AppendLine("   <Column ss:Width=\"55\"/>");   // Cột 12: Dài L (m)
                sb.AppendLine("   <Column ss:Width=\"65\"/>");   // Cột 13: Sụt áp ΔU%
                sb.AppendLine("   <Column ss:Width=\"75\"/>");   // Cột 14: Khởi Động
                sb.AppendLine("   <Column ss:Width=\"75\"/>");   // Cột 15: Điều Khiển

                // DÒNG 1: Tiêu đề Bảng tính
                sb.AppendLine("   <Row ss:Height=\"30\">");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"14\" ss:StyleID=\"Title\"><Data ss:Type=\"String\">BẢNG TÍNH TOÁN PHỤ TẢI &amp; CHỌN CÁP/APTOMAT TỦ ĐIỆN {0}</Data></Cell>", EscapeXml(panel.PanelName.ToUpper())));
                sb.AppendLine("   </Row>");

                // DÒNG 2: Phụ đề công cụ
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine("    <Cell ss:MergeAcross=\"14\" ss:StyleID=\"Subtitle\"><Data ss:Type=\"String\">NTT TOOLS - Thiết kế / Shopdrawing / BKL • Tự động cân pha &amp; kiểm tra sụt áp TCVN</Data></Cell>");
                sb.AppendLine("   </Row>");
                sb.AppendLine("   <Row ss:Height=\"8\"></Row>");

                // KHỐI THÔNG TIN CHUNG CỦA TỦ ĐIỆN
                double unbalance = ElectricalCalculator.GetPhaseUnbalancePercentage(panel);

                // Hàng thông số 1
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine("    <Cell ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Tên Tủ Điện:</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(panel.PanelName)));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Tổng Công Suất Đặt (Pđặt):</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0:N2} kW</Data></Cell>", panel.TotalInstalledPowerKW));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Aptomat Tổng (Main CB):</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"3\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(panel.MainBreakerSpec)));
                sb.AppendLine("   </Row>");

                // Hàng thông số 2
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine("    <Cell ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Hệ Số Đồng Thời (Kđt):</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0:N2}</Data></Cell>", panel.SimultaneousFactor));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Công Suất Tính Toán (Ptt):</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0:N2} kW</Data></Cell>", panel.TotalCalculatedPowerKW));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Cáp Nguồn Tổng (Main Cable):</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"3\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(panel.MainCableSpec)));
                sb.AppendLine("   </Row>");

                // Hàng thông số 3
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine("    <Cell ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Hệ Số Cosφ / Điện áp:</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0:N2} / {1:N0} V</Data></Cell>", panel.PowerFactor, panel.VoltageV));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Dòng Điện Tính Toán (Itt):</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0:N2} A</Data></Cell>", panel.TotalCurrentA));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Tụ Bù / Dòng Ngắn Mạch:</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"3\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0} kVAR ({1} Cấp) | Isc={2:N1}kA ({3})</Data></Cell>",
                    panel.CapacitorBankKvar, panel.CapacitorSteps, panel.ShortCircuitKA, panel.SuggestedMainIcu));
                sb.AppendLine("   </Row>");

                // Hàng thông số 4
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine("    <Cell ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Phân Bổ Công Suất Pha:</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">R:{0:N1} | Y:{1:N1} | B:{2:N1} kW</Data></Cell>",
                    panel.PhasePowerR, panel.PhasePowerY, panel.PhasePowerB));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Độ Lệch Pha Cực Đại:</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">{0:N1} %</Data></Cell>", unbalance));
                sb.AppendLine("    <Cell><Data ss:Type=\"String\"></Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"1\" ss:StyleID=\"InfoLabel\"><Data ss:Type=\"String\">Kích Thước Vỏ Tủ Dự Kiến:</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"3\" ss:StyleID=\"InfoValue\"><Data ss:Type=\"String\">Cao {0} x Rộng {1} x Sâu {2} mm</Data></Cell>",
                    panel.EstimatedHeight, panel.EstimatedWidth, panel.EstimatedDepth));
                sb.AppendLine("   </Row>");
                sb.AppendLine("   <Row ss:Height=\"10\"></Row>");

                // TIÊU ĐỀ CỘT CỦA BẢNG DỮ LIỆU
                sb.AppendLine("   <Row ss:Height=\"24\">");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">STT</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Tên Phụ Tải</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Số Mạch</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Pha</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Tổng P&#10;(kW)</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeAcross=\"2\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Phân Bổ Pha (kW)</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Dòng Itt&#10;(A)</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Aptomat Bảo Vệ&#10;(MCCB / MCB)</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Quy Cách Cáp Điện &amp; PE</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Dài L&#10;(m)</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Sụt Áp&#10;ΔU %</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Khởi Động</Data></Cell>");
                sb.AppendLine("    <Cell ss:MergeDown=\"1\" ss:StyleID=\"Header\"><Data ss:Type=\"String\">Điều Khiển</Data></Cell>");
                sb.AppendLine("   </Row>");

                // Hàng phụ cho Pha R, Y, B
                sb.AppendLine("   <Row ss:Height=\"18\">");
                sb.AppendLine("    <Cell ss:Index=\"6\" ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Pha R</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Pha Y</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"SubHeader\"><Data ss:Type=\"String\">Pha B</Data></Cell>");
                sb.AppendLine("   </Row>");

                // CÁC DÒNG DỮ LIỆU PHỤ TẢI
                double sumKw = 0;
                double sumR = 0;
                double sumY = 0;
                double sumB = 0;

                int rowIdx = 1;
                foreach (ElectricalLoad load in panel.Loads)
                {
                    sumKw += load.PowerKW;
                    sumR += load.PowerR;
                    sumY += load.PowerY;
                    sumB += load.PowerB;

                    string phaseStyle = load.Phase == PhaseType.ThreePhase ? "CellPhase3P" : "CellPhase1P";
                    string dropStyle = load.IsVoltageDropExceeded ? "CellDropExceeded" : "CellDropOk";

                    sb.AppendLine("   <Row ss:Height=\"20\">");
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", rowIdx++));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(load.LoadName)));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(load.CircuitNumber)));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"{0}\"><Data ss:Type=\"String\">{1}</Data></Cell>", phaseStyle, EscapeXml(load.PhaseDisplay)));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", load.PowerKW));

                    // Pha R
                    if (load.PowerR > 0.0001)
                        sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", load.PowerR));
                    else
                        sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">-</Data></Cell>");

                    // Pha Y
                    if (load.PowerY > 0.0001)
                        sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", load.PowerY));
                    else
                        sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">-</Data></Cell>");

                    // Pha B
                    if (load.PowerB > 0.0001)
                        sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", load.PowerB));
                    else
                        sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">-</Data></Cell>");

                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", load.CurrentA));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(load.BreakerSpec)));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(load.FullCableString)));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0:0.#}</Data></Cell>", load.CableLength));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"{0}\"><Data ss:Type=\"Number\">{1:0.0000}</Data></Cell>", dropStyle, load.VoltageDropPercent / 100.0));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellStarter\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(load.StarterDisplay)));
                    sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(load.ControlDisplay)));
                    sb.AppendLine("   </Row>");
                }

                // DÒNG TỔNG CỘNG
                sb.AppendLine("   <Row ss:Height=\"22\">");
                sb.AppendLine("    <Cell ss:MergeAcross=\"3\" ss:StyleID=\"TotalRow\"><Data ss:Type=\"String\">TỔNG CỘNG CÔNG SUẤT (kW)</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"TotalRowNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", sumKw));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"TotalRowNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", sumR));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"TotalRowNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", sumY));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"TotalRowNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", sumB));
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"TotalRowNumber\"><Data ss:Type=\"Number\">{0:0.00}</Data></Cell>", panel.TotalCurrentA));
                sb.AppendLine("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"TotalRow\"><Data ss:Type=\"String\">HỆ THỐNG ĐẠT CHUẨN CÂN BẰNG TẢI &amp; AN TOÀN SỤT ÁP</Data></Cell>");
                sb.AppendLine("   </Row>");

                sb.AppendLine("  </Table>");
                sb.AppendLine(" </Worksheet>");

                // ==========================================
                // WORKSHEET 2: BÓC TÁCH KHỐI LƯỢNG VẬT TƯ (BOQ / MTO)
                // ==========================================
                WriteBoqWorksheet(sb, panel);

                sb.AppendLine("</Workbook>");

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void WriteBoqWorksheet(StringBuilder sb, ElectricalPanel panel)
        {
            sb.AppendLine(" <Worksheet ss:Name=\"Bóc Tách Khối Lượng BOQ\">");
            sb.AppendLine("  <Table ss:DefaultRowHeight=\"20\">");
            sb.AppendLine("   <Column ss:Width=\"40\"/>");
            sb.AppendLine("   <Column ss:Width=\"220\"/>");
            sb.AppendLine("   <Column ss:Width=\"240\"/>");
            sb.AppendLine("   <Column ss:Width=\"60\"/>");
            sb.AppendLine("   <Column ss:Width=\"70\"/>");
            sb.AppendLine("   <Column ss:Width=\"120\"/>");

            // Header BOQ
            sb.AppendLine("   <Row ss:Height=\"28\">");
            sb.AppendLine(string.Format("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"Title\"><Data ss:Type=\"String\">BẢNG BÓC TÁCH KHỐI LƯỢNG VẬT TƯ THIẾT BỊ (BOQ) - TỦ ĐIỆN {0}</Data></Cell>", EscapeXml(panel.PanelName.ToUpper())));
            sb.AppendLine("   </Row>");
            sb.AppendLine("   <Row ss:Height=\"8\"></Row>");

            sb.AppendLine("   <Row ss:Height=\"24\">");
            sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">STT</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Danh Mục Vật Tư / Thiết Bị</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Quy Cách / Thông Số Kỹ Thuật</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">ĐVT</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Số Lượng</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">Ghi Chú</Data></Cell>");
            sb.AppendLine("   </Row>");

            int boqIndex = 1;

            // 1. THIẾT BỊ ĐÓNG CẮT
            sb.AppendLine("   <Row ss:Height=\"22\">");
            sb.AppendLine("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"BOQCategory\"><Data ss:Type=\"String\">I. THIẾT BỊ ĐÓNG CẮT (SWITCHGEAR)</Data></Cell>");
            sb.AppendLine("   </Row>");

            // Main Breaker
            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Aptomat Tổng Tủ Điện</Data></Cell>");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(panel.MainBreakerSpec)));
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Aptomat chính Incomer</Data></Cell>");
            sb.AppendLine("   </Row>");

            // Thống kê Breaker nhánh
            Dictionary<string, int> breakerCounts = new Dictionary<string, int>();
            foreach (var l in panel.Loads)
            {
                if (!string.IsNullOrEmpty(l.BreakerSpec))
                {
                    if (!breakerCounts.ContainsKey(l.BreakerSpec)) breakerCounts[l.BreakerSpec] = 0;
                    breakerCounts[l.BreakerSpec]++;
                }
            }
            foreach (var kvp in breakerCounts)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Aptomat Phân Phối Nhánh</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(kvp.Key)));
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Cái</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", kvp.Value));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Lộ phụ tải</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            // 2. BỘ KHỞI ĐỘNG ĐỘNG CƠ
            sb.AppendLine("   <Row ss:Height=\"22\">");
            sb.AppendLine("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"BOQCategory\"><Data ss:Type=\"String\">II. BỘ KHỞI ĐỘNG ĐỘNG CƠ &amp; ĐIỀU KHIỂN</Data></Cell>");
            sb.AppendLine("   </Row>");

            int dolCount = 0, ydCount = 0, vfdCount = 0, ssCount = 0;
            foreach (var l in panel.Loads)
            {
                if (l.Starter == StarterType.DOL) dolCount++;
                else if (l.Starter == StarterType.StarDelta) ydCount++;
                else if (l.Starter == StarterType.VFD) vfdCount++;
                else if (l.Starter == StarterType.SoftStarter) ssCount++;
            }

            if (dolCount > 0)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bộ Khởi Động Trực Tiếp (DOL)</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Contactor + Rơ le nhiệt bảo vệ quá tải</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", dolCount));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Khởi động bơm/quạt</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            if (ydCount > 0)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bộ Khởi Động Sao - Tam Giác (Y-Δ)</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">3 Contactor + Rơ le nhiệt + Rơ le thời gian Timer</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", ydCount));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Khởi động tải động lực lớn</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            if (vfdCount > 0)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Biến Tần Điều Khiển (VFD / Inverter)</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bộ điều khiển tần số công nghiệp tích hợp bộ lọc</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", vfdCount));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Điều tốc bơm/quạt HVAC</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            if (ssCount > 0)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bộ Khởi Động Mềm (Soft Starter)</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bộ khởi động mềm điện tử tích hợp Bypass</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", ssCount));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Khởi động êm giảm dòng</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            // 3. THIẾT BỊ PHỤ TRỢ & ĐO LƯỜNG
            sb.AppendLine("   <Row ss:Height=\"22\">");
            sb.AppendLine("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"BOQCategory\"><Data ss:Type=\"String\">III. THIẾT BỊ ĐO LƯỜNG &amp; PHỤ TRỢ TỦ ĐIỆN</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Đèn Báo Pha (R - Y - B)</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Đèn LED 220VAC màu Đỏ, Vàng, Xanh</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ (3 cái)</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Báo nguồn mặt tủ</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Biến Dòng Đo Lường (MCT)</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Biến dòng đo lường tỷ số phù hợp dòng tủ /5A</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ (3 cái)</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Đo dòng 3 pha</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Đồng Hồ Đa Năng Điện Tử (PM)</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Power Meter kỹ thuật số đo V, A, Hz, kW, Cosφ</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Cái</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Gắn mặt tủ</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Rơ Le Bảo Vệ (OV/UV/EF/OC)</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bảo vệ quá áp, thấp áp, chạm đất, quá dòng</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Bộ</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bảo vệ hệ thống</Data></Cell>");
            sb.AppendLine("   </Row>");

            if (panel.HasCapacitorBank && panel.CapacitorBankKvar > 0)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Hệ Thống Tụ Bù Công Suất Phản Kháng</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Dung lượng {0} kVAR ({1} Cấp bù tự động)</Data></Cell>", panel.CapacitorBankKvar, panel.CapacitorSteps));
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Hệ</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bộ điều khiển + Contactor tụ bù</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            // 4. CÁP ĐIỆN VÀ DÂY DẪN (TỔNG SỐ MÉT)
            sb.AppendLine("   <Row ss:Height=\"22\">");
            sb.AppendLine("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"BOQCategory\"><Data ss:Type=\"String\">IV. DÂY CÁP ĐIỆN VÀ DẪN NGUỒN</Data></Cell>");
            sb.AppendLine("   </Row>");

            Dictionary<string, double> cableLengths = new Dictionary<string, double>();
            foreach (var l in panel.Loads)
            {
                if (!string.IsNullOrEmpty(l.FullCableString))
                {
                    if (!cableLengths.ContainsKey(l.FullCableString)) cableLengths[l.FullCableString] = 0;
                    cableLengths[l.FullCableString] += l.CableLength;
                }
            }
            foreach (var kvp in cableLengths)
            {
                sb.AppendLine("   <Row ss:Height=\"20\">");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Cáp Điện Xuất Lộ Phụ Tải</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">{0}</Data></Cell>", EscapeXml(kvp.Key)));
                sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Mét</Data></Cell>");
                sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0:0.#}</Data></Cell>", kvp.Value));
                sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Tuyến cáp nhánh</Data></Cell>");
                sb.AppendLine("   </Row>");
            }

            // 5. VỎ TỦ ĐIỆN VÀ PHỤ KIỆN
            sb.AppendLine("   <Row ss:Height=\"22\">");
            sb.AppendLine("    <Cell ss:MergeAcross=\"5\" ss:StyleID=\"BOQCategory\"><Data ss:Type=\"String\">V. VỎ TỦ ĐIỆN &amp; PHỤ KIỆN LẮP RÁP</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Vỏ Tủ Điện Sơn Tĩnh Điện (Trong Nhà)</Data></Cell>");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Kích thước {0} x {1} x {2} mm, tole dày 1.5 - 2.0mm</Data></Cell>",
                panel.EstimatedHeight, panel.EstimatedWidth, panel.EstimatedDepth));
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Cái</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">IP42/IP54 có khóa cánh tủ</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("   <Row ss:Height=\"20\">");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">{0}</Data></Cell>", boqIndex++));
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Hệ Thống Thanh Cái Đồng Chính (Busbar)</Data></Cell>");
            sb.AppendLine(string.Format("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Đồng đỏ nguyên chất 99.9% mạ thiếc, chịu dòng {0:N0}A</Data></Cell>", panel.TotalCurrentA));
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"String\">Hệ</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellCenter\"><Data ss:Type=\"Number\">1</Data></Cell>");
            sb.AppendLine("    <Cell ss:StyleID=\"CellLeft\"><Data ss:Type=\"String\">Bao gồm sứ đỡ thanh cái</Data></Cell>");
            sb.AppendLine("   </Row>");

            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;")
                        .Replace("'", "&apos;");
        }
    }
}

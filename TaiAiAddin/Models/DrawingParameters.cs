using System;



namespace TaiAiAddin.Models



{



    /// <summary>



    /// Các tham số kích thước và cấu hình Sơ đồ nguyên lý



    /// Cho phép người dùng tinh chỉnh trực tiếp trên giao diện Preview



    /// và tùy chọn thêm/bớt các khối thiết bị phụ trợ (Cầu chì, Đèn báo pha, Relay, MCT, SPD, Spare, ATS, Khung tên, Mặt tủ)



    /// </summary>



            public enum LayoutScheme
    {
        LayoutBasic,    // Lựa chọn 1: LAYOUT BASIC (Sơ đồ dọc truyền thống)
        SouthRegion,    // Lựa chọn 2: Kiểu Miền Nam
        NorthRegion,    // Lựa chọn 3: Kiểu Miền Bắc
        SldV2Mcc        // Lựa chọn 4: SDNL V2 (Tủ Động Lực MCC / PCCC)
    }

public enum SldDiagramType

    {

        StandardSchedule,   // Bảng phân phối truyền thống

        TreeSinglePanel,    // Cây phân cấp nội bộ 1 tủ

        TreeSystemRiser     // Cây phân cấp toàn hệ thống (Riser)

    }



    public enum TreeOrientation

    {

        VerticalTopDown,    // Cây đứng (Trên xuống)

        HorizontalLeftRight // Cây ngang / dọc (Trái qua phải)

    }



    public enum SymbolRegion

    {

        North_Tcvn,         // Chuẩn Miền Bắc (TCVN / EVN NPC)

        South_Iec           // Chuẩn Miền Nam (IEC 60617 / EVN SPC)

    }



public class DrawingParameters



    {



                // 0. Cấu hình Sơ đồ nguyên lý dạng cây & Chuẩn vùng miền

                public LayoutScheme LayoutScheme { get; set; }
        public string ReferenceTemplateDwgPath { get; set; }
public SldDiagramType DiagramType { get; set; }

        public TreeOrientation TreeOrientation { get; set; }

        public SymbolRegion SymbolRegion { get; set; }



// 1. Tùy chọn thêm/bớt các khối thiết bị phụ trợ



        public bool IncludeFuse { get; set; }          // Cầu chì bảo vệ đo lường (Mặc định: true)



        public bool IncludeLamps { get; set; }         // Đèn báo pha R-Y-B (Mặc định: true)



        public bool IncludeRelay { get; set; }         // Rơ le bảo vệ OV/UV/EF/OC (Mặc định: true)



        public bool IncludeMct { get; set; }           // Biến dòng 3xMCT & Đồng hồ PM (Mặc định: true)



        public bool IncludeSpd { get; set; }           // Chống sét lan truyền SPD (Mặc định: true)



        public bool IncludeSpare { get; set; }         // Lộ dự phòng Spare (Mặc định: false)

        public int SpareCount { get; set; }            // Tổng số lộ dự phòng

        public int SpareCount3P { get; set; }          // Số lộ dự phòng 3 pha (Mặc định: 1)

        public int SpareCount1P { get; set; }          // Số lộ dự phòng 1 pha (Mặc định: 0)

        public bool DrawSpareOutgoingWire { get; set; }// Vẽ đường dây đi ra từ CB dự phòng (Mặc định: false - không vẽ dây)



        // 2. Kích thước bảng phụ tải



        public double ColWidth { get; set; }           // Bề rộng mỗi cột lộ phụ tải (Mặc định: 26)



        public double HeaderMainWidth { get; set; }    // Bề rộng cột tiêu đề chính (Mặc định: 24)



        public double HeaderSubWidth { get; set; }     // Bề rộng cột phụ R, Y, B (Mặc định: 8)



        public double RowNameHeight { get; set; }      // Chiều cao hàng Tên phụ tải (Mặc định: 65)



        public double RowCircuitHeight { get; set; }   // Chiều cao hàng Số mạch (Mặc định: 18)



        public double RowPowerHeight { get; set; }     // Chiều cao hàng Công suất (Mặc định: 36)



        public double RowCableHeight { get; set; }     // Chiều cao hàng Cáp điện (Mặc định: 90)



        public double TextSize { get; set; }           // Cỡ chữ chung (Mặc định: 2.2)



        public bool SplitCableTwoLines { get; set; }   // Ngắt cáp thành 2 dòng (Cáp pha \n Cáp tiếp địa)



        // 3. Kích thước khoang thiết bị phía trên



        public double CabinetHeight { get; set; }      // Chiều cao vỏ tủ điện (Mặc định: 145)



        public double MeteringBlockScale { get; set; } // Tỷ lệ scale cho Block đo lường (Mặc định: 0.1)



        public double RelayScale { get; set; }         // Tỷ lệ scale cho Block Relay (Mặc định: 0.334)



        public bool AutoFitSpacing { get; set; }       // Tự động co giãn kích thước chống đè chữ/block (Mặc định: true)



        // 4. Khung bản vẽ kỹ thuật (Title Block A3/A2)



        public bool IncludeTitleBlock { get; set; }    // Lồng khung bản vẽ kỹ thuật



        public string PaperSize { get; set; }          // "A3" hoặc "A2"



        public string ProjectName { get; set; }        // Tên dự án



        public string DesignerName { get; set; }       // Người thiết kế / Kỹ sư



        public string DrawingScale { get; set; }       // Tỷ lệ bản vẽ (vd: "1:25")



        public string DrawnBy { get; set; }            // Người vẽ



        public string CheckedBy { get; set; }          // Người kiểm tra



        public string ApprovedBy { get; set; }         // Người duyệt



        public string DrawingTitle { get; set; }       // Tên bản vẽ



        public string DrawingNumber { get; set; }      // Ký hiệu bản vẽ (vd: "EL-01")



        public string Revision { get; set; }           // Lần sửa đổi (vd: "00")



        // 5. Mặt ngoài cánh tủ điện (Front View Layout) & Bảng BOQ & Bảng chú thích ký hiệu (Legend)



        public bool DrawFrontView { get; set; }        // Vẽ mặt ngoài cánh tủ thành bản vẽ riêng (Sheet 02)



        public bool DrawBoqTable { get; set; }         // Chèn bảng bóc tách vật tư BOQ lên CAD



        public bool IncludeLegend { get; set; }        // Bảng chú thích ký hiệu (Legend Table)



        public double LegendOffsetX { get; set; }      // Tọa độ dời tự do bảng chú thích X



        public double LegendOffsetY { get; set; }      // Tọa độ dời tự do bảng chú thích Y

        // 6. Danh sách các đối tượng tương tác tùy biến Move, Copy, Rotate, Scale trên Sơ đồ nguyên lý
        public System.Collections.Generic.List<SldInteractiveItem> SldCustomItems { get; set; }

        public SldInteractiveItem GetOrCreateCustomItem(string id, string displayName, string itemType, double w = 30.0, double h = 30.0)
        {
            if (SldCustomItems == null) SldCustomItems = new System.Collections.Generic.List<SldInteractiveItem>();
            foreach (var it in SldCustomItems)
            {
                if (string.Equals(it.Id, id, StringComparison.OrdinalIgnoreCase))
                    return it;
            }
            var newItem = new SldInteractiveItem(id, displayName, itemType, w, h);
            SldCustomItems.Add(newItem);
            return newItem;
        }

        public SldInteractiveItem FindCustomItem(string id)
        {
            if (SldCustomItems == null) return null;
            foreach (var it in SldCustomItems)
            {
                if (string.Equals(it.Id, id, StringComparison.OrdinalIgnoreCase))
                    return it;
            }
            return null;
        }

        public DrawingParameters Clone()
        {
            DrawingParameters copy = (DrawingParameters)this.MemberwiseClone();
            if (this.SldCustomItems != null)
            {
                copy.SldCustomItems = new System.Collections.Generic.List<SldInteractiveItem>();
                foreach (var it in this.SldCustomItems)
                {
                    copy.SldCustomItems.Add(it.CloneExact());
                }
            }
            return copy;
        }

        public void ResetAllCustomItems()
        {
            if (SldCustomItems == null) return;
            SldCustomItems.RemoveAll(x => x.IsCopied);
            foreach (var it in SldCustomItems)
            {
                it.Reset();
            }
        }



        public double TotalHeaderWidth



        {



            get { return HeaderMainWidth + HeaderSubWidth; }



        }



        public double TotalTableHeight



        {



            get { return RowNameHeight + RowCircuitHeight + RowPowerHeight; }



        }



        public DrawingParameters()



        {



                                    LayoutScheme = LayoutScheme.LayoutBasic;
DiagramType = SldDiagramType.TreeSinglePanel;

            TreeOrientation = TreeOrientation.VerticalTopDown;

            SymbolRegion = SymbolRegion.North_Tcvn;



IncludeFuse = true;



            IncludeLamps = true;



            IncludeRelay = true;



            IncludeMct = true;



            IncludeSpd = true;



            IncludeSpare = false;

            SpareCount = 1;

            SpareCount3P = 1;

            SpareCount1P = 0;

            DrawSpareOutgoingWire = false;



            ColWidth = 26.0;



            HeaderMainWidth = 24.0;



            HeaderSubWidth = 8.0;



            RowNameHeight = 65.0;



            RowCircuitHeight = 18.0;



            RowPowerHeight = 36.0;



            RowCableHeight = 90.0;



            TextSize = 2.2;



            SplitCableTwoLines = true;



            CabinetHeight = 145.0;



            MeteringBlockScale = 0.1;



            RelayScale = 0.334;



            AutoFitSpacing = true;



            IncludeTitleBlock = false;



            PaperSize = "A3";



            ProjectName = "CÔNG TRÌNH XÂY DỰNG TÒA NHÀ & NHÀ XƯỞNG";



            DesignerName = "NTT TOOLS";



            DrawingScale = "1:25";



            DrawnBy = "NGUYỄN TẤN TÀI";



            CheckedBy = "CAO VĂN SIÊU";



            ApprovedBy = "DƯƠNG CAO SƠN";



            DrawingTitle = "SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN";



            DrawingNumber = "EL-01";



            Revision = "00";



            DrawFrontView = false;



            DrawBoqTable = false;



            IncludeLegend = false;



            LegendOffsetX = 0.0;



            LegendOffsetY = 0.0;
            SldCustomItems = new System.Collections.Generic.List<SldInteractiveItem>();



        }



        /// <summary>



        /// Tự động tính toán các kích thước tối ưu dựa trên số lượng phụ tải và độ dài chuỗi ký tự



        /// giúp các đối tượng text, block, line/pline không bao giờ đè lên nhau.



        /// </summary>



        public void AutoFitForPanel(ElectricalPanel panel)



        {



            if (panel == null || panel.Loads == null) return;



            // 1. Kiểm tra độ dài tên phụ tải để co dãn chiều cao hàng Tên tải



            int maxNameLen = 0;



            int maxCableLen = 0;



            foreach (var load in panel.Loads)



            {



                if (!string.IsNullOrEmpty(load.LoadName) && load.LoadName.Length > maxNameLen)



                    maxNameLen = load.LoadName.Length;



                if (!string.IsNullOrEmpty(load.FullCableString) && load.FullCableString.Length > maxCableLen)



                    maxCableLen = load.FullCableString.Length;



            }



            // Chiều cao hàng tên tải



            if (maxNameLen > 30) RowNameHeight = 75.0;



            else if (maxNameLen > 20) RowNameHeight = 65.0;



            else RowNameHeight = 55.0;



            // Chiều cao hàng cáp



            if (SplitCableTwoLines) RowCableHeight = 90.0;



            else RowCableHeight = Math.Max(90.0, maxCableLen * 2.2 * 0.85 + 20.0);



            // Bề rộng cột: Đảm bảo vừa vặn tên phụ tải dài nhất nếu xoay đứng hoặc 2 dòng



            if (maxNameLen > 25) ColWidth = 28.0;



            else ColWidth = 26.0;



            // Chiều cao vỏ tủ: mở rộng nếu có thêm ATS



            if (panel.Incomer == IncomerType.DualATS || panel.Incomer == IncomerType.DualInterlock)



            {



                CabinetHeight = 175.0;



            }



            else



            {



                CabinetHeight = 145.0;



            }



        }



    }



}


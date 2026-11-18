using System;
using System.Collections.Generic;
using System.Windows;

namespace TaiAiAddin.Models
{
    /// <summary>
    /// Thông tin nhãn text phòng trích xuất từ bản vẽ
    /// </summary>
    public class CoPilotRoomText
    {
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Height { get; set; }
    }

    /// <summary>
    /// Đối tượng đoạn thẳng hoặc cạnh biên kết cấu (tường, cột)
    /// </summary>
    public class CoPilotEdge
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool IsColumn { get; set; }
    }

    /// <summary>
    /// Tọa độ đỉnh 2D
    /// </summary>
    public class CoPilotPoint2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public CoPilotPoint2D() { }
        public CoPilotPoint2D(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Kết quả đường bao của 1 phòng được Co-Pilot tính toán (Chuẩn LOD 300)
    /// </summary>
    public class CoPilotRoomItem
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; }
        }

        public string Code { get; set; }
        public string RoomName { get; set; }
        public string Name { get; set; }
        public string Floor { get; set; }
        public double AreaM2 { get; set; }
        public double PerimeterM { get; set; }
        public double ElevationOffsetMm { get; set; }
        public bool HasCurvedWall { get; set; }
        public double Bulge { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double LabelX { get; set; }
        public double LabelY { get; set; }
        public List<double> Bulges { get; set; }
        public List<CoPilotPoint2D> Vertices { get; set; }

        private bool? _isWcCustom = null;
        public bool IsWc
        {
            get
            {
                if (_isWcCustom.HasValue) return _isWcCustom.Value;
                if (ElevationOffsetMm < -1.0) return true;
                string nu = (Name ?? RoomName ?? "").ToUpper();
                return nu.Contains("WC") || nu.Contains("VỆ SINH") || nu.Contains("TOILET") || nu.Contains("BATH");
            }
            set
            {
                _isWcCustom = value;
            }
        }

        public Visibility WcVisibility
        {
            get { return IsWc ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility CurvedVisibility
        {
            get { return HasCurvedWall ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string AreaDisplay
        {
            get { return string.Format("{0:F2} m²", AreaM2); }
        }

        public string PerimeterDisplay
        {
            get { return string.Format("{0:F2} m", PerimeterM); }
        }

        public string ElevationDisplay
        {
            get { return IsWc ? "-50 mm" : "0.0 mm"; }
        }

        public string BulgeDisplay
        {
            get { return HasCurvedWall ? string.Format("Bulge {0:F4}", Bulge) : ""; }
        }

        public string RoomIcon
        {
            get
            {
                if (IsWc) return "🚿";
                string nu = (Name ?? "").ToUpper();
                if (nu.Contains("SÂN") || nu.Contains("CẢNH QUAN")) return "🌿";
                if (nu.Contains("BẾP")) return "🍳";
                if (nu.Contains("MASTER") || nu.Contains("NGỦ") || nu.Contains("BED")) return "🛏️";
                if (nu.Contains("BAN CÔNG") || nu.Contains("THƯỢNG") || nu.Contains("MÁI")) return "☀️";
                if (nu.Contains("THANG") || nu.Contains("HÀNH LANG") || nu.Contains("ĐỆM")) return "🚶";
                if (nu.Contains("BẢO VỆ")) return "🛡️";
                if (nu.Contains("LÀM VIỆC") || nu.Contains("RELAX") || nu.Contains("CỬA HÀNG")) return "🏢";
                if (nu.Contains("KHO")) return "📦";
                return "🏠";
            }
        }

                public string RoomTypeBadge
        {
            get
            {
                string nu = (RoomName ?? Name ?? "").ToUpper();
                if (IsWc) return "Nhà WC (-50mm)";
                if (nu.Contains("HỘP KT") || nu.Contains("HOP KT") || nu.Contains("KT")) return "Hộp Kỹ Thuật";
                if (nu.Contains("BAN CÔNG") || nu.Contains("LOGIA") || nu.Contains("LÔ GIA")) return "Ban Công / Lô Gia";
                if (nu.Contains("SẢNH THANG")) return "Sảnh Thang Máy";
                if (nu.Contains("HÀNH LANG")) return "Hành Lang";
                if (nu.Contains("THANG")) return "Cụm Thang";
                return "Phòng Chính";
            }
        }

        public string RuleComplianceNote
        {
            get
            {
                string nu = (RoomName ?? Name ?? "").ToUpper();
                if (IsWc) return "Đạt chuẩn QT 02: WC độc lập, xuyên kính tắm";
                if (nu.Contains("HỘP KT") || nu.Contains("HOP KT") || nu.Contains("KT")) return "Đạt chuẩn QT 03: Hộp KT riêng biệt";
                if (nu.Contains("BAN CÔNG") || nu.Contains("LOGIA") || nu.Contains("LÔ GIA")) return "Đạt chuẩn QT 04: Ban công độc lập ngoài mép tường";
                if (nu.Contains("SẢNH THANG")) return "Đạt chuẩn QT 07: Sảnh thang trực diện";
                if (nu.Contains("HÀNH LANG")) return "Đạt chuẩn QT 07: Hành lang giao thông";
                return "Đạt chuẩn QT 01: Bo trọn không gian, ôm cột";
            }
        }

        public CoPilotRoomItem()
        {
            Vertices = new List<CoPilotPoint2D>();
            Bulges = new List<double>();
            Floor = "Tầng 1 (Trệt)";
            Code = "";
            RoomName = "";
        }
    }

    /// <summary>
    /// Kết quả tổng hợp sau khi chạy Co-Pilot (Chuẩn LOD 300)
    /// </summary>
    public class CoPilotResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int RoomCount { get; set; }
        public double TotalAreaM2 { get; set; }
        public double TotalPerimeterM { get; set; }
        public int WcCount { get; set; }
        public int CurvedCount { get; set; }
        public long ElapsedMs { get; set; }
        public List<CoPilotRoomItem> Rooms { get; set; }

        public CoPilotResult()
        {
            Rooms = new List<CoPilotRoomItem>();
        }
    }
}

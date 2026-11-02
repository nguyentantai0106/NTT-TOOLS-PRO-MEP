using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TaiAiAddin.Models
{
    /// <summary>
    /// Đỉnh của đường bao phòng kèm thông số độ cong Bulge (hỗ trợ tường bo tròn)
    /// </summary>
    public class RoomBoundaryVertex
    {
        public Point2d Point { get; set; }
        public double Bulge { get; set; }

        public RoomBoundaryVertex(Point2d pt, double bulge = 0.0)
        {
            Point = pt;
            Bulge = bulge;
        }

        public RoomBoundaryVertex(double x, double y, double bulge = 0.0)
        {
            Point = new Point2d(x, y);
            Bulge = bulge;
        }
    }

    /// <summary>
    /// Thông tin chi tiết một phòng được nhận diện
    /// </summary>
    public class RoomInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public double AreaM2 { get; set; }
        public double PerimeterM { get; set; }
        public double CeilingHeightMm { get; set; }
        public Point3d Centroid { get; set; }
        public List<RoomBoundaryVertex> Vertices { get; set; }
        public ObjectId PolylineId { get; set; }
        public ObjectId TextId { get; set; }
        public bool HasCurvedWall { get; set; }

        public RoomInfo()
        {
            Code = "";
            Name = "";
            CeilingHeightMm = 2800.0;
            Vertices = new List<RoomBoundaryVertex>();
            PolylineId = ObjectId.Null;
            TextId = ObjectId.Null;
            HasCurvedWall = false;
        }

        public string DisplayText
        {
            get
            {
                return string.Format("{0} - {1} ({2:F2} m²)", Code, Name, AreaM2);
            }
        }
    }

    /// <summary>
    /// Tùy chọn cấu hình cho tính năng XLMB (Xử Lý Mặt Bằng)
    /// </summary>
    public class RoomBoundaryOptions
    {
        public double MinAreaM2 { get; set; }
        public double MaxAreaM2 { get; set; }
        public double TextHeight { get; set; }
        public string BoundaryLayer { get; set; }
        public string TextLayer { get; set; }
        public int BoundaryColorIndex { get; set; } // Màu 1 - Đỏ
        public int TextColorIndex { get; set; }     // Màu 3 - Xanh lá
        public double SnapToleranceMm { get; set; }
        public bool RoundCurvedWalls { get; set; }
        public bool AttachXData { get; set; }
        public bool CreateMText { get; set; }
        public bool ExcludeFurniture { get; set; }
        public bool ExcludeGrids { get; set; }
        public bool AutoCloseCornerGaps { get; set; }
        public Point2d? PickPoint { get; set; }

        public RoomBoundaryOptions()
        {
            MinAreaM2 = 1.0;
            MaxAreaM2 = 3000.0;
            TextHeight = 250.0;
            BoundaryLayer = "DUONG_BAO";
            TextLayer = "DUONG_BAO_TEXT";
            BoundaryColorIndex = 1;
            TextColorIndex = 3;
            SnapToleranceMm = 20.0;
            RoundCurvedWalls = true;
            AttachXData = true;
            CreateMText = true;
            ExcludeFurniture = true;
            ExcludeGrids = true;
            AutoCloseCornerGaps = true;
            PickPoint = null;
        }
    }

    /// <summary>
    /// Kết quả quét và tạo đường bao phòng
    /// </summary>
    public class RoomBoundaryResult
    {
        public bool Success { get; set; }
        public int RoomCount { get; set; }
        public double TotalAreaM2 { get; set; }
        public List<RoomInfo> Rooms { get; set; }
        public string Message { get; set; }

        public RoomBoundaryResult()
        {
            Success = false;
            RoomCount = 0;
            TotalAreaM2 = 0.0;
            Rooms = new List<RoomInfo>();
            Message = "";
        }
    }
}

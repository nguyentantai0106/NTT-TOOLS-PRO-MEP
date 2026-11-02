using System;

namespace TaiAiAddin.Models
{
    public enum CableTrayType
    {
        Trunking, // Máng cáp trơn có nắp
        Ladder,   // Thang cáp có bậc thang giằng (250mm/300mm)
        Tray      // Khay cáp đột lỗ thông khí
    }

    public class CableTrayParams
    {
        public CableTrayType Type { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double ThicknessMm { get; set; }
        public double SupportSpacingM { get; set; }
        public double BendRadiusMm { get; set; }
        public bool InsertSupports { get; set; }
        public bool AddAnnotations { get; set; }
        public double RungSpacingMm { get; set; } // Khoảng cách bậc thang Ladder

        public CableTrayParams()
        {
            Type = CableTrayType.Trunking;
            WidthMm = 300.0;
            HeightMm = 100.0;
            ThicknessMm = 1.5;
            SupportSpacingM = 1.5;
            BendRadiusMm = 150.0;
            InsertSupports = true;
            AddAnnotations = true;
            RungSpacingMm = 250.0;
        }
    }

    public class CableTrayRouteResult
    {
        public string TypeName { get; set; }
        public string DimensionLabel { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double ThicknessMm { get; set; }
        public double TotalLengthM { get; set; }
        public int SupportCount { get; set; }
        public int ElbowCount { get; set; }
        public int Elbow90Count { get; set; }
        public int Elbow45Count { get; set; }
        public int SegmentCount { get; set; }
        public double SheetMetalWeightKg { get; set; }
    }
}

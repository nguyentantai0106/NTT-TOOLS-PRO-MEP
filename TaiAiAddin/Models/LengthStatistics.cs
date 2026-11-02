using System;

namespace TaiAiAddin.Models
{
    /// <summary>
    /// Model chứa dữ liệu thống kê chiều dài theo từng Layer
    /// </summary>
    public class LayerLengthStat
    {
        public int Stt { get; set; }
        public string LayerName { get; set; }
        public int LineCount { get; set; }
        public int PolylineCount { get; set; }

        public int TotalCount 
        { 
            get { return LineCount + PolylineCount; } 
        }

        public double TotalLength { get; set; }

        public string FormattedLengthM 
        { 
            get { return (TotalLength / 1000.0).ToString("N2"); } 
        }

        public string FormattedLengthMm 
        { 
            get { return TotalLength.ToString("N2"); } 
        }
    }

    /// <summary>
    /// Tổng hợp kết quả thống kê
    /// </summary>
    public class SummaryResult
    {
        public int TotalLines { get; set; }
        public int TotalPolylines { get; set; }

        public int GrandTotalCount 
        { 
            get { return TotalLines + TotalPolylines; } 
        }

        public double GrandTotalLength { get; set; }

        public string FormattedGrandTotalM 
        { 
            get { return (GrandTotalLength / 1000.0).ToString("N2"); } 
        }
    }
}

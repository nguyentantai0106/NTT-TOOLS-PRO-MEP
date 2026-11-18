using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Autodesk.AutoCAD.Geometry;

namespace TaiAiAddin.Models
{
    public class LayerMappingItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _targetLayer;

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public string SourceLayer { get; set; }
        public int EntityCount { get; set; }

        public string TargetLayer
        {
            get { return _targetLayer; }
            set
            {
                if (_targetLayer != value)
                {
                    _targetLayer = value;
                    OnPropertyChanged("TargetLayer");
                }
            }
        }

        public List<string> AvailableTargetLayers { get; set; }

        public string StatusNote { get; set; }
        public double Confidence { get; set; }
        public bool IsMixedLayer { get; set; }
        public bool IsLearnedFromDictionary { get; set; }

        public LayerMappingItem()
        {
            Confidence = 0.0;
            AvailableTargetLayers = new List<string>
            {
                "AI_XD_TUONG",
                "AI_XD_COT",
                "AI_XD_CUA",
                "AI_XD_VACH_KINH",
                "AI_XD_NOI_THAT",
                "AI_XD_DIM_TEXT",
                "AI_XD_GRID",
                "AI_XD_RAC",
                "-- Phân loại chi tiết (Hỗn hợp) --",
                "-- Bỏ Qua (Không Đổi) --"
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public class SuspiciousEntityItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _suggestedLayer;

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public string HandleStr { get; set; }
        public string EntityType { get; set; }
        public string CurrentLayer { get; set; }

        public string SuggestedLayer
        {
            get { return _suggestedLayer; }
            set
            {
                if (_suggestedLayer != value)
                {
                    _suggestedLayer = value;
                    OnPropertyChanged("SuggestedLayer");
                }
            }
        }

        public List<string> AvailableTargetLayers { get; set; }

        public string Reason { get; set; }
        public Point3d CenterPoint { get; set; }
        public bool IsFixed { get; set; }

        /// <summary>Độ tin cậy chẩn đoán AI (0..1). Dưới 0.85 → nhóm "Cần kiểm tra".</summary>
        public double Confidence { get; set; }
        /// <summary>True nếu là đối tượng mới ngoài vùng tri thức đã học / hình học chưa chắc chắn.</summary>
        public bool IsOutsideKnowledge { get; set; }
        public bool NeedsReview { get { return IsOutsideKnowledge || Confidence < 0.85; } }
        public string ConfidenceDisplay
        {
            get
            {
                if (IsOutsideKnowledge) return string.Format("{0:F0}% ⚠ Mới (Ngoài tri thức)", Confidence * 100.0);
                return string.Format("{0:F0}%{1}", Confidence * 100.0, NeedsReview ? " ⚠ Cần kiểm tra" : "");
            }
        }

        public SuspiciousEntityItem()
        {
            Confidence = 0.95;
            AvailableTargetLayers = new List<string>
            {
                "AI_XD_TUONG",
                "AI_XD_COT",
                "AI_XD_CUA",
                "AI_XD_VACH_KINH",
                "AI_XD_NOI_THAT",
                "AI_XD_DIM_TEXT",
                "AI_XD_GRID",
                "AI_XD_RAC",
                "-- Bỏ Qua (Không Đổi) --"
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public enum TextLanguageMode
    {
        VietnameseOnly = 0,
        VietnameseEnglish = 1,
        VietnameseChinese = 2,
        Trilingual = 3
    }

    public static class RoomNameDictionary
    {
        private static readonly Dictionary<string, Tuple<string, string>> Translations = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "NGỦ", Tuple.Create("BEDROOM", "卧室") },
            { "KHÁCH", Tuple.Create("LIVING ROOM", "客厅") },
            { "BẾP", Tuple.Create("KITCHEN", "厨房") },
            { "ĂN", Tuple.Create("DINING ROOM", "餐厅") },
            { "WC", Tuple.Create("TOILET", "卫生间") },
            { "VỆ SINH", Tuple.Create("RESTROOM", "卫生间") },
            { "TẮM", Tuple.Create("BATHROOM", "浴室") },
            { "BAN CÔNG", Tuple.Create("BALCONY", "阳台") },
            { "LÔ GIA", Tuple.Create("LOGGIA", "阳台") },
            { "LOGIA", Tuple.Create("LOGGIA", "阳台") },
            { "HÀNH LANG", Tuple.Create("CORRIDOR", "走廊") },
            { "SẢNH THANG", Tuple.Create("ELEVATOR LOBBY", "电梯厅") },
            { "SẢNH", Tuple.Create("LOBBY", "大堂") },
            { "THANG MÁY", Tuple.Create("ELEVATOR", "电梯") },
            { "THANG BỘ", Tuple.Create("STAIRCASE", "楼梯") },
            { "HỘP KỸ THUẬT", Tuple.Create("SHAFT", "管井") },
            { "HỘP KT", Tuple.Create("SHAFT", "管井") },
            { "HỘP GEN", Tuple.Create("PIPE SHAFT", "管井") },
            { "KỸ THUẬT", Tuple.Create("TECHNICAL ROOM", "设备房") },
            { "KHO", Tuple.Create("STORAGE", "储藏室") },
            { "GIẶT", Tuple.Create("LAUNDRY", "洗衣房") },
            { "LÀM VIỆC", Tuple.Create("STUDY / WORK", "书房 / 工作间") },
            { "VĂN PHÒNG", Tuple.Create("OFFICE", "办公室") },
            { "HỌP", Tuple.Create("MEETING ROOM", "会议室") },
            { "PHÒNG KTX", Tuple.Create("DORMITORY", "宿舍") },
            { "KTX", Tuple.Create("DORMITORY", "宿舍") },
            { "CĂN HỘ", Tuple.Create("APARTMENT", "公寓") },
            { "PHÒNG CHỨC NĂNG", Tuple.Create("FUNCTION ROOM", "功能房") },
            { "SINH HOẠT CHUNG", Tuple.Create("COMMON ROOM", "多功能厅") },
            { "THANG", Tuple.Create("STAIRCASE", "楼梯") },
            { "PHÒNG", Tuple.Create("ROOM", "房间") }
        };

        public static string FormatBilingualRoomName(string vnName, TextLanguageMode mode)
        {
            if (string.IsNullOrEmpty(vnName) || mode == TextLanguageMode.VietnameseOnly)
                return vnName;

            string cleanName = vnName.Trim();
            if (cleanName.StartsWith("PH-", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = "PHÒNG " + cleanName.Substring(3).Trim();
            }
            else if (cleanName.StartsWith("P-", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = "PHÒNG " + cleanName.Substring(2).Trim();
            }

            string en = "";
            string cn = "";

            // Ưu tiên so khớp các từ khóa dài trước để tránh nhận diện nhầm
            foreach (var kvp in Translations.OrderByDescending(k => k.Key.Length))
            {
                if (cleanName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    en = kvp.Value.Item1;
                    cn = kvp.Value.Item2;
                    break;
                }
            }

            if (string.IsNullOrEmpty(en))
            {
                en = "ROOM";
                cn = "房间";
            }

            switch (mode)
            {
                case TextLanguageMode.VietnameseEnglish:
                    return string.Format("{0} / {1}", cleanName, en);
                case TextLanguageMode.VietnameseChinese:
                    return string.Format("{0} / {1}", cleanName, cn);
                case TextLanguageMode.Trilingual:
                    return string.Format("{0} / {1} / {2}", cleanName, en, cn);
                default:
                    return cleanName;
            }
        }
    }

    public class TextFormatConfig
    {
        public bool IsAutoTextHeight { get; set; }
        public double HeightMainRoom { get; set; }
        public double HeightWcBalcony { get; set; }
        public double HeightHopKt { get; set; }
        public TextLanguageMode LanguageMode { get; set; }

        public TextFormatConfig()
        {
            IsAutoTextHeight = true;
            HeightMainRoom = 200.0;
            HeightWcBalcony = 100.0;
            HeightHopKt = 100.0;
            LanguageMode = TextLanguageMode.VietnameseOnly;
        }

        public double GetHeightForRoom(string roomName, bool isWc)
        {
            if (IsAutoTextHeight)
            {
                if (isWc) return 100.0;
                string upper = (roomName ?? "").ToUpperInvariant();
                if (upper.Contains("BAN CÔNG") || upper.Contains("LOGIA") || upper.Contains("LÔ GIA")) return 100.0;
                if (upper.Contains("KT") || upper.Contains("GEN")) return 100.0;
                return 200.0;
            }
            else
            {
                if (isWc) return HeightWcBalcony;
                string upper = (roomName ?? "").ToUpperInvariant();
                if (upper.Contains("BAN CÔNG") || upper.Contains("LOGIA") || upper.Contains("LÔ GIA")) return HeightWcBalcony;
                if (upper.Contains("KT") || upper.Contains("GEN")) return HeightHopKt;
                return HeightMainRoom;
            }
        }

        public string BuildMTextContent(string roomName, double areaM2, bool isWc)
        {
            string upper = (roomName ?? "").ToUpperInvariant();
            bool isHopKt = upper.Contains("KT") || upper.Contains("GEN");

            string formattedName = RoomNameDictionary.FormatBilingualRoomName(roomName, LanguageMode);

            if (isHopKt)
            {
                // Hộp KT: Chỉ ghi tên, KHÔNG ghi diện tích
                return formattedName;
            }
            else
            {
                // Các phòng công năng: Dòng trên Tên phòng, Dòng dưới diện tích
                return string.Format("{0}\\PS={1:F2} m²", formattedName, areaM2);
            }
        }
    }
}

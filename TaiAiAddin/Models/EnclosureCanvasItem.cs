using System;

namespace TaiAiAddin.Models
{
    /// <summary>
    /// Đối tượng phần tử tương tác trên khung Preview Mặt Bằng Vỏ Tủ
    /// Cho phép di chuyển chuột (Drag &amp; Drop), chỉnh sửa tọa độ, thay đổi text, xóa hoặc nhân bản
    /// </summary>
    public class EnclosureCanvasItem
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; } // "View", "DoorBlock", "InternalEquipment", "Annotation"

        // Độ dịch chuyển so với vị trí mặc định tính bằng Milimét (mm) trên mô hình
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        // Kích thước ước lượng (mm)
        public double Width { get; set; }
        public double Height { get; set; }

        // Nhãn text tùy chỉnh nếu có
        public string CustomText { get; set; }

        // Trạng thái tương tác
        public bool IsVisible { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsLocked { get; set; }
        public bool IsSelected { get; set; }

        public EnclosureCanvasItem()
        {
            OffsetX = 0.0;
            OffsetY = 0.0;
            IsVisible = true;
            IsDeleted = false;
            IsLocked = false;
            IsSelected = false;
        }

        public EnclosureCanvasItem(string id, string displayName, string category, double w = 0, double h = 0, string text = "")
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            Width = w;
            Height = h;
            CustomText = text;
            OffsetX = 0.0;
            OffsetY = 0.0;
            IsVisible = true;
            IsDeleted = false;
            IsLocked = false;
            IsSelected = false;
        }

        public void ResetPosition()
        {
            OffsetX = 0.0;
            OffsetY = 0.0;
        }
    }
}

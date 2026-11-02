using System;

namespace TaiAiAddin.Models
{
    /// <summary>
    /// Đối tượng phần tử tương tác đồ họa trên Preview Sơ Đồ Nguyên Lý Điện (SLD)
    /// Hỗ trợ Move, Copy, Rotate, Scale và đồng bộ sang AutoCAD
    /// </summary>
    public class SldInteractiveItem
    {
        public string Id { get; set; }           // "MAIN_BREAKER", "ACCESSORY_LAMPS", "ACCESSORY_PM", "LOAD_CB_0", "COPIED_xxx"
        public string DisplayName { get; set; }  // "Aptomat Tổng", "Đèn Báo Pha", "Đồng Hồ PM", "MCB Lộ 1"
        public string ItemType { get; set; }     // "MainBreaker", "BranchBreaker", "PM", "Lamps", "Fuse", "Relay", "MCT", "SPD"
        public int LoadIndex { get; set; }       // Index lộ tải (nếu có)

        // Tọa độ dịch chuyển (CAD mm / Canvas units)
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        // Kích thước khung bao (Bounding Box) trên Canvas (WPF coordinates)
        public double Width { get; set; }
        public double Height { get; set; }

        // Tọa độ gốc mặc định trên Canvas
        public double BaseX { get; set; }
        public double BaseY { get; set; }

        // Góc xoay theo độ: 0, 90, 180, 270 (hoặc góc bất kỳ)
        public double RotationAngle { get; set; }

        // Tỷ lệ Scale: 0.5x đến 3.0x (mặc định 1.0)
        public double Scale { get; set; }

        // Trạng thái tương tác
        public bool IsSelected { get; set; }
        public bool IsCopied { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsDragging { get; set; }

        public SldInteractiveItem()
        {
            LoadIndex = -1;
            OffsetX = 0.0;
            OffsetY = 0.0;
            Width = 30.0;
            Height = 30.0;
            BaseX = 0.0;
            BaseY = 0.0;
            RotationAngle = 0.0;
            Scale = 1.0;
            IsSelected = false;
            IsCopied = false;
            IsDeleted = false;
            IsDragging = false;
        }

        public SldInteractiveItem(string id, string displayName, string itemType, double w = 30.0, double h = 30.0)
        {
            Id = id;
            DisplayName = displayName;
            ItemType = itemType;
            LoadIndex = -1;
            OffsetX = 0.0;
            OffsetY = 0.0;
            Width = w;
            Height = h;
            BaseX = 0.0;
            BaseY = 0.0;
            RotationAngle = 0.0;
            Scale = 1.0;
            IsSelected = false;
            IsCopied = false;
            IsDeleted = false;
            IsDragging = false;
        }

        public SldInteractiveItem CloneExact()
        {
            return new SldInteractiveItem
            {
                Id = this.Id,
                DisplayName = this.DisplayName,
                ItemType = this.ItemType,
                LoadIndex = this.LoadIndex,
                OffsetX = this.OffsetX,
                OffsetY = this.OffsetY,
                Width = this.Width,
                Height = this.Height,
                BaseX = this.BaseX,
                BaseY = this.BaseY,
                RotationAngle = this.RotationAngle,
                Scale = this.Scale,
                IsSelected = false,
                IsCopied = this.IsCopied,
                IsDeleted = this.IsDeleted,
                IsDragging = false
            };
        }

        public SldInteractiveItem Clone(string newId = null)
        {
            return new SldInteractiveItem
            {
                Id = newId ?? (Id + "_COPY_" + Guid.NewGuid().ToString().Substring(0, 4)),
                DisplayName = DisplayName + " (Bản sao)",
                ItemType = ItemType,
                LoadIndex = LoadIndex,
                OffsetX = OffsetX + 15.0,
                OffsetY = OffsetY + 15.0,
                Width = Width,
                Height = Height,
                BaseX = BaseX,
                BaseY = BaseY,
                RotationAngle = RotationAngle,
                Scale = Scale,
                IsSelected = true,
                IsCopied = true,
                IsDeleted = false,
                IsDragging = false
            };
        }

        public void Reset()
        {
            OffsetX = 0.0;
            OffsetY = 0.0;
            RotationAngle = 0.0;
            Scale = 1.0;
            IsDeleted = false;
        }
    }
}

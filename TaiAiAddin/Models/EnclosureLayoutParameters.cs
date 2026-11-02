using System;
using System.Collections.Generic;

namespace TaiAiAddin.Models
{
    /// <summary>
    /// Tham số cấu hình chi tiết cho Bản vẽ Mặt Bằng Bố Trí Vỏ Tủ Điện
    /// Bao gồm kích thước, nhãn text, cờ bật/tắt và danh sách các phần tử tương tác kéo thả (CustomItems)
    /// </summary>
    public class EnclosureLayoutParameters
    {
        // Kích thước hình học vỏ tủ (mm)
        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }
        public double PlinthHeight { get; set; }
        public double CanopyHeight { get; set; }

        // Nhãn chữ và thông tin kỹ thuật căn chỉnh được
        public string PanelName { get; set; }
        public string VoltageSpec { get; set; }
        public string CableHoleSpec { get; set; }
        public string MaterialSpec { get; set; }
        public string ProtectionRating { get; set; }
        public string ColorSpec { get; set; }
        public double CadTextHeight { get; set; }

        // Tùy chọn Thêm / Bỏ các Hình chiếu (Views)
        public bool IncludeFrontView { get; set; }
        public bool IncludeInnerDoorView { get; set; }
        public bool IncludeInternalView { get; set; }
        public bool IncludeSideView { get; set; }
        public bool IncludeBottomView { get; set; }

        // Tùy chọn Thêm / Bỏ các Khối thiết bị (Blocks & Components)
        public bool IncludeNameplate { get; set; }
        public bool IncludePhaseLampsAndMeter { get; set; }
        public bool IncludeControlStations { get; set; }
        public bool IncludeEmergencyStop { get; set; }
        public bool IncludeDoorLock { get; set; }
        public bool IncludeVentilationFan { get; set; }
        public bool IncludeCopperBusbar { get; set; }
        public bool IncludeMainMccb { get; set; }
        public bool IncludeBranchMcbs { get; set; }
        public bool UseLargeFrameMcb { get; set; } // Tùy chọn MCB khung lớn (mcb 2p L / MCB 3P L)
        public bool IncludeMotorStarters { get; set; }
        public bool IncludeControlRelays { get; set; }
        public bool IncludeTerminalBlocks { get; set; }
        public bool IncludeWiringDucts { get; set; }
        public bool IncludeDimensions { get; set; }
        public bool IncludeSpecTable { get; set; }

        // Danh sách các phần tử tương tác di chuyển (CustomItems)
        public Dictionary<string, EnclosureCanvasItem> CustomItems { get; set; }

        public EnclosureLayoutParameters()
        {
            // Kích thước mặc định chuẩn công nghiệp theo TD.dwg
            Width = 700.0;
            Height = 1625.0;
            Depth = 400.0;
            PlinthHeight = 100.0;
            CanopyHeight = 75.0;

            // Thông số chữ mặc định
            PanelName = "DB-HVAC-01";
            VoltageSpec = "3P4W 380V/220V, 50Hz";
            CableHoleSpec = "450 x 220 mm";
            MaterialSpec = "Thép cán nguội (SPCC) dày 1.5 - 2.0 mm";
            ProtectionRating = "IP43 (Trong nhà) / IP54";
            ColorSpec = "Sơn tĩnh điện công nghiệp RAL 7035";
            CadTextHeight = 12.0;

            // Mặc định hiển thị đầy đủ
            IncludeFrontView = true;
            IncludeInnerDoorView = true;
            IncludeInternalView = true;
            IncludeSideView = true;
            IncludeBottomView = true;

            IncludeNameplate = true;
            IncludePhaseLampsAndMeter = true;
            IncludeControlStations = true;
            IncludeEmergencyStop = true;
            IncludeDoorLock = true;
            IncludeVentilationFan = true;

            IncludeCopperBusbar = true;
            IncludeMainMccb = true;
            IncludeBranchMcbs = true;
            UseLargeFrameMcb = false;
            IncludeMotorStarters = true;
            IncludeControlRelays = true;
            IncludeTerminalBlocks = true;
            IncludeWiringDucts = true;

            IncludeDimensions = true;
            IncludeSpecTable = true;

            CustomItems = new Dictionary<string, EnclosureCanvasItem>(StringComparer.OrdinalIgnoreCase);
        }

        public EnclosureCanvasItem GetOrCreateItem(string id, string displayName = "", string category = "", double w = 0, double h = 0, string text = "")
        {
            if (CustomItems == null) CustomItems = new Dictionary<string, EnclosureCanvasItem>(StringComparer.OrdinalIgnoreCase);
            if (CustomItems.ContainsKey(id))
            {
                EnclosureCanvasItem it = CustomItems[id];
                if (!string.IsNullOrEmpty(displayName)) it.DisplayName = displayName;
                if (!string.IsNullOrEmpty(category)) it.Category = category;
                if (w > 0) it.Width = w;
                if (h > 0) it.Height = h;
                if (!string.IsNullOrEmpty(text) && string.IsNullOrEmpty(it.CustomText)) it.CustomText = text;
                return it;
            }

            EnclosureCanvasItem newItem = new EnclosureCanvasItem(id, displayName, category, w, h, text);
            CustomItems[id] = newItem;
            return newItem;
        }

        public double GetOffsetX(string id)
        {
            if (CustomItems != null)
            {
                if (CustomItems.ContainsKey(id)) return CustomItems[id].OffsetX;
                if (id == "AOM_STATIONS" && CustomItems.ContainsKey("HOA_STATIONS")) return CustomItems["HOA_STATIONS"].OffsetX;
                if (id == "HOA_STATIONS" && CustomItems.ContainsKey("AOM_STATIONS")) return CustomItems["AOM_STATIONS"].OffsetX;
            }
            return 0.0;
        }

        public double GetOffsetY(string id)
        {
            if (CustomItems != null)
            {
                if (CustomItems.ContainsKey(id)) return CustomItems[id].OffsetY;
                if (id == "AOM_STATIONS" && CustomItems.ContainsKey("HOA_STATIONS")) return CustomItems["HOA_STATIONS"].OffsetY;
                if (id == "HOA_STATIONS" && CustomItems.ContainsKey("AOM_STATIONS")) return CustomItems["AOM_STATIONS"].OffsetY;
            }
            return 0.0;
        }

        public bool IsItemDeleted(string id)
        {
            if (CustomItems != null)
            {
                if (CustomItems.ContainsKey(id)) return CustomItems[id].IsDeleted;
                if (id == "AOM_STATIONS" && CustomItems.ContainsKey("HOA_STATIONS")) return CustomItems["HOA_STATIONS"].IsDeleted;
                if (id == "HOA_STATIONS" && CustomItems.ContainsKey("AOM_STATIONS")) return CustomItems["AOM_STATIONS"].IsDeleted;
            }
            return false;
        }

        public string GetCustomText(string id, string defaultText = "")
        {
            if (CustomItems != null && CustomItems.ContainsKey(id) && !string.IsNullOrEmpty(CustomItems[id].CustomText))
                return CustomItems[id].CustomText;
            return defaultText;
        }

        public void ResetAllOffsets()
        {
            if (CustomItems == null) return;
            foreach (var kv in CustomItems)
            {
                kv.Value.OffsetX = 0.0;
                kv.Value.OffsetY = 0.0;
                kv.Value.IsDeleted = false;
            }
        }
    }
}

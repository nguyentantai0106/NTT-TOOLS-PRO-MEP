using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TaiAiAddin.Views
{
    public partial class CustomBlockDialog : Window
    {
        public class ComboItem
        {
            public string Display { get; set; }
            public string Code { get; set; }
            public string DefaultLayer { get; set; }
            public double DefaultHeight { get; set; }

            public override string ToString()
            {
                return Display;
            }
        }

        public string FinalBlockName { get; private set; }
        public string FinalTargetLayer { get; private set; }
        public double FinalWidth { get; private set; }
        public double FinalHeight { get; private set; }
        public double FinalDepth { get; private set; }

        public CustomBlockDialog(double initialWidth, double initialDepth, string defaultLayer = "AI_XD_CUA")
        {
            InitializeComponent();

            InitCategories();
            InitTargetLayers(defaultLayer);

            double w = initialWidth > 0 ? Math.Round(initialWidth) : 1000;
            double d = initialDepth > 0 ? Math.Round(initialDepth) : 100;

            txtWidth.Text = w.ToString("0");
            txtDepth.Text = d.ToString("0");
            txtHeight.Text = "2200";

            UpdateBlockNamePreview();
        }

        private void InitCategories()
        {
            var categories = new List<ComboItem>
            {
                new ComboItem { Display = "🚪 Cửa (CUA)", Code = "CUA", DefaultLayer = "AI_XD_CUA" },
                new ComboItem { Display = "🛋️ Nội Thất (NOITHAT)", Code = "NOITHAT", DefaultLayer = "AI_XD_NOI_THAT" },
                new ComboItem { Display = "🚽 Thiết Bị Vệ Sinh (THIETBI_VS)", Code = "THIETBI_VS", DefaultLayer = "AI_XD_NOI_THAT" },
                new ComboItem { Display = "🏛️ Kết Cấu / Cột (KETCAU)", Code = "KETCAU", DefaultLayer = "AI_XD_COT" },
                new ComboItem { Display = "📦 Tùy Chọn Khác (KHAC)", Code = "KHAC", DefaultLayer = "0" }
            };

            cboCategory.ItemsSource = categories;
            cboCategory.SelectedIndex = 0;
        }

        private void InitTargetLayers(string defaultLayer)
        {
            var layers = new List<string>
            {
                "AI_XD_CUA",
                "AI_XD_NOI_THAT",
                "AI_XD_COT",
                "AI_XD_VACH_KINH",
                "AI_XD_TUONG",
                "0"
            };

            cboTargetLayer.ItemsSource = layers;
            int idx = layers.IndexOf(defaultLayer);
            cboTargetLayer.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void CboCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboItem cat = cboCategory.SelectedItem as ComboItem;
            if (cat == null) return;

            // Đổi layer đích theo nhóm
            if (cboTargetLayer != null && !string.IsNullOrEmpty(cat.DefaultLayer))
            {
                List<string> lList = cboTargetLayer.ItemsSource as List<string>;
                int lIdx = lList != null ? lList.IndexOf(cat.DefaultLayer) : -1;
                if (lIdx >= 0) cboTargetLayer.SelectedIndex = lIdx;
            }

            var subcats = new List<ComboItem>();
            switch (cat.Code)
            {
                case "CUA":
                    subcats.Add(new ComboItem { Display = "🚪 Cửa đi (CUADI)", Code = "CUADI", DefaultHeight = 2200 });
                    subcats.Add(new ComboItem { Display = "🪟 Cửa sổ (CUASO)", Code = "CUASO", DefaultHeight = 1400 });
                    break;
                case "NOITHAT":
                    subcats.Add(new ComboItem { Display = "🛏️ Giường (GIUONG)", Code = "GIUONG", DefaultHeight = 900 });
                    subcats.Add(new ComboItem { Display = "🛋️ Ghế / Sofa (SOFA)", Code = "SOFA", DefaultHeight = 850 });
                    subcats.Add(new ComboItem { Display = "🪑 Bàn (BAN)", Code = "BAN", DefaultHeight = 750 });
                    subcats.Add(new ComboItem { Display = "🚪 Tủ áo / Tủ tường (TU)", Code = "TU", DefaultHeight = 2400 });
                    subcats.Add(new ComboItem { Display = "🍳 Bếp (BEP)", Code = "BEP", DefaultHeight = 850 });
                    break;
                case "THIETBI_VS":
                    subcats.Add(new ComboItem { Display = "🚽 Bồn cầu (BONCAU)", Code = "BONCAU", DefaultHeight = 750 });
                    subcats.Add(new ComboItem { Display = "🚿 Lavabo (LAVABO)", Code = "LAVABO", DefaultHeight = 850 });
                    subcats.Add(new ComboItem { Display = "🚿 Sen tắm (SENTAM)", Code = "SENTAM", DefaultHeight = 2000 });
                    subcats.Add(new ComboItem { Display = "🛁 Bồn tắm (BONTAM)", Code = "BONTAM", DefaultHeight = 600 });
                    subcats.Add(new ComboItem { Display = "🕳️ Phễu thu sàn (PHEUTHU)", Code = "PHEUTHU", DefaultHeight = 50 });
                    break;
                case "KETCAU":
                    subcats.Add(new ComboItem { Display = "🏛️ Cột chữ nhật (COT_CN)", Code = "COT_CN", DefaultHeight = 3300 });
                    subcats.Add(new ComboItem { Display = "🏛️ Cột tròn (COT_TRON)", Code = "COT_TRON", DefaultHeight = 3300 });
                    subcats.Add(new ComboItem { Display = "🧱 Vách cứng (VACH_CUNG)", Code = "VACH_CUNG", DefaultHeight = 3300 });
                    break;
                default:
                    subcats.Add(new ComboItem { Display = "📦 Chi tiết khác (KHAC)", Code = "KHAC", DefaultHeight = 1000 });
                    break;
            }

            cboSubcategory.ItemsSource = subcats;
            cboSubcategory.SelectedIndex = 0;
        }

        private void CboSubcategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboItem subcat = cboSubcategory.SelectedItem as ComboItem;
            if (subcat == null) return;

            if (txtHeight != null && subcat.DefaultHeight > 0)
            {
                txtHeight.Text = subcat.DefaultHeight.ToString("0");
            }

            ComboItem cat = cboCategory.SelectedItem as ComboItem;
            string catCode = cat != null ? cat.Code : "CUA";

            var details = new List<ComboItem>();
            if (catCode == "CUA")
            {
                details.Add(new ComboItem { Display = "1 cánh (1CANH)", Code = "1CANH" });
                details.Add(new ComboItem { Display = "2 cánh (2CANH)", Code = "2CANH" });
                details.Add(new ComboItem { Display = "4 cánh (4CANH)", Code = "4CANH" });
                details.Add(new ComboItem { Display = "Cửa lùa / trượt (LUA)", Code = "LUA" });
                details.Add(new ComboItem { Display = "Cửa kính (KINH)", Code = "KINH" });
                details.Add(new ComboItem { Display = "Cửa thoát hiểm (THOATHIEM)", Code = "THOATHIEM" });
                details.Add(new ComboItem { Display = "Cửa cuốn (CUON)", Code = "CUON" });
            }
            else if (catCode == "NOITHAT")
            {
                details.Add(new ComboItem { Display = "Tiêu chuẩn (STANDARD)", Code = "STANDARD" });
                details.Add(new ComboItem { Display = "Đơn (DON)", Code = "DON" });
                details.Add(new ComboItem { Display = "Đôi (DOI)", Code = "DOI" });
                details.Add(new ComboItem { Display = "Góc chữ L (GOC)", Code = "GOC" });
                details.Add(new ComboItem { Display = "Âm tường (AMTUONG)", Code = "AMTUONG" });
            }
            else if (catCode == "THIETBI_VS")
            {
                details.Add(new ComboItem { Display = "Đặt sàn (DATSAN)", Code = "DATSAN" });
                details.Add(new ComboItem { Display = "Treo tường (TREOTUONG)", Code = "TREOTUONG" });
                details.Add(new ComboItem { Display = "Âm bàn (AMBAN)", Code = "AMBAN" });
            }
            else if (catCode == "KETCAU")
            {
                details.Add(new ComboItem { Display = "Tiêu chuẩn (STANDARD)", Code = "STANDARD" });
                details.Add(new ComboItem { Display = "Góc chữ L (GOC_L)", Code = "GOC_L" });
                details.Add(new ComboItem { Display = "Chữ T (CHU_T)", Code = "CHU_T" });
            }
            else
            {
                details.Add(new ComboItem { Display = "Tiêu chuẩn (STANDARD)", Code = "STANDARD" });
            }

            cboDetailType.ItemsSource = details;
            cboDetailType.SelectedIndex = 0;
            UpdateBlockNamePreview();
        }

        private void CboDetailType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBlockNamePreview();
        }

        private void Dimension_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBlockNamePreview();
        }

        private void UpdateBlockNamePreview()
        {
            if (txtPreviewBlockName == null) return;

            ComboItem cat = cboCategory != null ? cboCategory.SelectedItem as ComboItem : null;
            ComboItem subcat = cboSubcategory != null ? cboSubcategory.SelectedItem as ComboItem : null;
            ComboItem detail = cboDetailType != null ? cboDetailType.SelectedItem as ComboItem : null;

            string catCode = cat != null ? cat.Code : "CUA";
            string subcatCode = subcat != null ? subcat.Code : "CUADI";
            string detailCode = detail != null ? detail.Code : "1CANH";

            double w = 1000, h = 2200, d = 100;
            if (txtWidth != null && double.TryParse(txtWidth.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out w)) { }
            if (txtHeight != null && double.TryParse(txtHeight.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out h)) { }
            if (txtDepth != null && double.TryParse(txtDepth.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) { }

            // Công thức: <loại block>_<tên block>_<loại chi tiết>_<kích thước>
            // Ví dụ: CUA_CUADI_1CANH_1000x2200x100
            string name = string.Format("{0}_{1}_{2}_{3:0}x{4:0}x{5:0}", catCode, subcatCode, detailCode, w, h, d);
            txtPreviewBlockName.Text = name;
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            UpdateBlockNamePreview();
            FinalBlockName = txtPreviewBlockName.Text;
            FinalTargetLayer = cboTargetLayer.SelectedItem as string ?? "AI_XD_CUA";

            double w = 1000, h = 2200, d = 100;
            double.TryParse(txtWidth.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out w);
            double.TryParse(txtHeight.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out h);
            double.TryParse(txtDepth.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            FinalWidth = w;
            FinalHeight = h;
            FinalDepth = d;

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}

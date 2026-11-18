using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using TaiAiAddin.Models;
using TaiAiAddin.Services;

namespace TaiAiAddin.Views.Tabs
{
    /// <summary>
    /// UserControl Phân hệ Thiết Kế & Vẽ Thang Máng Cáp (TMC Live Block Động)
    /// Độc lập, module hóa chuẩn WPF MVVM/Clean Architecture
    /// </summary>
    public partial class CableTrayTabControl : UserControl
    {
        public CableTrayTabControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// ĐỌC CẤU HÌNH THÔNG SỐ TỪ GIAO DIỆN
        /// </summary>
        public CableTrayParams GetCableTrayParamsFromUi()
        {
            CableTrayParams p = new CableTrayParams();

            // Loại máng
            p.Type = CableTrayType.Trunking;
            if (cbTmcType != null)
            {
                int idx = cbTmcType.SelectedIndex;
                if (idx == 0) p.Type = CableTrayType.Trunking;
                else if (idx == 1) p.Type = CableTrayType.Ladder;
                else if (idx == 2) p.Type = CableTrayType.PerforatedTray;
            }

            // Kích thước Width
            double w = 300;
            if (cbTmcWidth != null)
            {
                string textW = cbTmcWidth.Text;
                if (cbTmcWidth.SelectedItem is ComboBoxItem itemW && itemW.Content != null)
                    textW = itemW.Content.ToString();
                double.TryParse(textW, NumberStyles.Any, CultureInfo.InvariantCulture, out w);
            }
            p.Width = w > 0 ? w : 300;

            // Kích thước Height
            double h = 100;
            if (cbTmcHeight != null)
            {
                string textH = cbTmcHeight.Text;
                if (cbTmcHeight.SelectedItem is ComboBoxItem itemH && itemH.Content != null)
                    textH = itemH.Content.ToString();
                double.TryParse(textH, NumberStyles.Any, CultureInfo.InvariantCulture, out h);
            }
            p.Height = h > 0 ? h : 100;

            // Khoảng cách giá treo
            double spacing = 1.5;
            if (txtTmcSupportSpacing != null && !string.IsNullOrEmpty(txtTmcSupportSpacing.Text))
            {
                double.TryParse(txtTmcSupportSpacing.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out spacing);
            }
            p.SupportSpacing = spacing > 0 ? spacing : 1.5;

            // Độ dày tôn
            double thick = 1.5;
            if (txtTmcThickness != null && !string.IsNullOrEmpty(txtTmcThickness.Text))
            {
                double.TryParse(txtTmcThickness.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out thick);
            }
            p.SheetThickness = thick > 0 ? thick : 1.5;

            // Tùy chọn cờ
            p.InsertSupports = chkTmcInsertSupports == null || chkTmcInsertSupports.IsChecked == true;
            p.AddAnnotations = chkTmcAddAnnotations == null || chkTmcAddAnnotations.IsChecked == true;

            return p;
        }

        public void OnTmcDimensionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Tự động đồng bộ khi chọn kích thước từ danh sách
        }

        public void OnTmcDimensionChanged(object sender, RoutedEventArgs e)
        {
            // Tự động đồng bộ khi gõ kích thước tự do
        }

        /// <summary>
        /// CẬP NHẬT KÍCH THƯỚC CHO BLOCK ĐANG CHỌN TRÊN CAD
        /// </summary>
        private void BtnUpdateSelectedTmc_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            CableTrayParams p = GetCableTrayParamsFromUi();
            CableTrayService.UpdateSelectedCableTrayBlock(doc, p.Width, p.Height);
        }

        /// <summary>
        /// VẼ TRỰC TIẾP TỪNG LOẠI THANG MÁNG CÁP LIVE JIG
        /// </summary>
        public void StartDrawTmcWithType(CableTrayType type, string typeName)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            CableTrayParams p = GetCableTrayParamsFromUi();
            p.Type = type;

            Editor ed = doc.Editor;
            ed.WriteMessage(string.Format("\n[NTT TMC] Kích hoạt vẽ {0} Live Block W={1}mm, H={2}mm...", typeName, p.Width, p.Height));

            CableTrayService.StartLiveCableTrayDrawing(doc, p);
        }

        public void DrawTray()
        {
            StartDrawTmcWithType(CableTrayType.PerforatedTray, "Khay Cáp (Tray)");
        }

        public void DrawTrunking()
        {
            StartDrawTmcWithType(CableTrayType.Trunking, "Máng Cáp (Trunking)");
        }

        public void DrawLadder()
        {
            StartDrawTmcWithType(CableTrayType.Ladder, "Thang Điện (Ladder)");
        }

        private void BtnDrawTray_Click(object sender, RoutedEventArgs e)
        {
            DrawTray();
        }

        private void BtnDrawTrunking_Click(object sender, RoutedEventArgs e)
        {
            DrawTrunking();
        }

        private void BtnDrawLadder_Click(object sender, RoutedEventArgs e)
        {
            DrawLadder();
        }

        /// <summary>
        /// VẼ TUYẾN THANG MÁNG THEO POLYLINE
        /// </summary>
        private void BtnRouteTmcAlongPl_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            CableTrayParams p = GetCableTrayParamsFromUi();
            CableTrayService.RouteAlongSelectedPolyline(doc, p);
        }

        /// <summary>
        /// XUẤT BẢNG BÓC TÁCH KHỐI LƯỢNG VÀO CAD (TABLE)
        /// </summary>
        private void BtnDrawTmcBOMTable_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            CableTrayService.InsertCableTrayBOMTable(doc);
        }

        /// <summary>
        /// MỞ THƯ MỤC BLOCK THANG MÁNG CÁP
        /// </summary>
        private void BtnOpenTmcFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetDir = @"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\Thang máng";
                if (!Directory.Exists(targetDir))
                {
                    targetDir = @"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện";
                }
                if (Directory.Exists(targetDir))
                {
                    Process.Start("explorer.exe", targetDir);
                }
                else
                {
                    MessageBox.Show("Chưa tìm thấy thư mục Thư Viện Thang Máng Cáp tại:\n" + targetDir, "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở thư mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

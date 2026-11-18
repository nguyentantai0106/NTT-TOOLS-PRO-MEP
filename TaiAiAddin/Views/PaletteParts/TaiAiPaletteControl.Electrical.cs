using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;
using TaiAiAddin.Services;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TaiAiAddin.Views
{
    public partial class TaiAiPaletteControl : UserControl
    {

        #region Các Button Thiết Kế Tủ Điện & Bóc Tách Khối Lượng Dây Ống
        private void BtnOpenElectricalDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Reflection.Assembly asm = typeof(TaiAiPaletteControl).Assembly;
                Window dialog = null;
                try
                {
                    Type dlgType = asm.GetType("TaiAiAddin.Views.ElectricalPanelDialog");
                    if (dlgType != null)
                    {
                        dialog = Activator.CreateInstance(dlgType) as Window;
                    }
                }
                catch { }

                if (dialog == null)
                {
                    dialog = new ElectricalPanelDialog();
                }

                try
                {
                    new System.Windows.Interop.WindowInteropHelper(dialog).Owner = Application.MainWindow.Handle;
                }
                catch { }
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Lỗi mở giao diện Tủ điện: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenEnclosureDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnclosureLayoutDialog dialog = new EnclosureLayoutDialog();
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Lỗi mở giao diện Mặt Bằng Bố Trí Vỏ Tủ: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSelectAndCalculate_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    txtStatus.Text = "Đang chờ người dùng quét chọn đối tượng trên bản vẽ...";

                    _currentStats = _calculatorService.CollectAndCalculateLengths(doc, out _currentSummary);

                    if (_currentStats == null || _currentStats.Count == 0)
                    {
                        txtStatus.Text = "Không có đối tượng Line/Pline nào được chọn.";
                        return;
                    }

                    bool isMeters = rbMeters.IsChecked == true;
                    dgResults.ItemsSource = null;
                    dgResults.ItemsSource = _currentStats;

                    txtTotalCount.Text = string.Format("{0} đối tượng", _currentSummary.GrandTotalCount);
                    txtTotalLength.Text = isMeters
                        ? string.Format("{0} m", _currentSummary.FormattedGrandTotalM)
                        : string.Format("{0:N2} mm", _currentSummary.GrandTotalLength);

                    btnCreateTableOnly.IsEnabled = true;
                    txtStatus.Text = string.Format("Đã quét thành công {0} đối tượng qua {1} Layer.", _currentSummary.GrandTotalCount, _currentStats.Count);

                    if (chkAutoCreateTable.IsChecked == true)
                    {
                        txtStatus.Text = "Vui lòng click chọn điểm đặt Bảng trên bản vẽ...";
                        bool created = _calculatorService.CreateSummaryTable(doc, _currentStats, _currentSummary, isMeters);
                        if (created)
                        {
                            txtStatus.Text = "Đã tạo bảng thống kê thành công trên AutoCAD!";
                        }
                        else
                        {
                            txtStatus.Text = "Đã hoàn thành thống kê (Chưa chèn bảng).";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);
                if (doc.Editor != null)
                {
                    doc.Editor.WriteMessage(string.Format("\n[TaiAi Error] {0}\n{1}", ex.Message, ex.StackTrace));
                }
            }
        }

        private void BtnCreateTableOnly_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (_currentStats == null || _currentStats.Count == 0)
            {
                MessageBox.Show("Chưa có dữ liệu thống kê. Vui lòng quét chọn đối tượng trước!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    txtStatus.Text = "Vui lòng chọn điểm đặt Bảng trên bản vẽ...";
                    bool isMeters = rbMeters.IsChecked == true;
                    bool created = _calculatorService.CreateSummaryTable(doc, _currentStats, _currentSummary, isMeters);

                    if (created)
                    {
                        txtStatus.Text = "Đã chèn lại bảng thành công!";
                    }
                    else
                    {
                        txtStatus.Text = "Hủy thao tác chèn bảng.";
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);
                if (doc.Editor != null)
                {
                    doc.Editor.WriteMessage(string.Format("\n[TaiAi Error] {0}", ex.Message));
                }
            }
        }
        #endregion

        #region Nghiệp Vụ Thang Máng Cáp (TMC)
        private CableTrayParams GetCableTrayParamsFromUi()
        {
            CableTrayType trayType = CableTrayType.Trunking;
            if (cbTmcType != null && cbTmcType.SelectedIndex == 1)
                trayType = CableTrayType.Ladder;
            else if (cbTmcType != null && cbTmcType.SelectedIndex == 2)
                trayType = CableTrayType.Tray;

            double widthMm = 300;
            if (cbTmcWidth != null)
            {
                string wText = cbTmcWidth.Text;
                ComboBoxItem itemW = cbTmcWidth.SelectedItem as ComboBoxItem;
                if (string.IsNullOrEmpty(wText) && itemW != null && itemW.Content != null)
                    wText = itemW.Content.ToString();
                if (!string.IsNullOrEmpty(wText))
                {
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(wText, @"\d+");
                    if (match.Success) double.TryParse(match.Value, out widthMm);
                }
                if (widthMm <= 0) widthMm = 300;
            }

            double heightMm = 100;
            if (cbTmcHeight != null)
            {
                string hText = cbTmcHeight.Text;
                ComboBoxItem itemH = cbTmcHeight.SelectedItem as ComboBoxItem;
                if (string.IsNullOrEmpty(hText) && itemH != null && itemH.Content != null)
                    hText = itemH.Content.ToString();
                if (!string.IsNullOrEmpty(hText))
                {
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(hText, @"\d+");
                    if (match.Success) double.TryParse(match.Value, out heightMm);
                }
                if (heightMm <= 0) heightMm = 100;
            }

            double supportSpacingM = 1.5;
            if (txtTmcSupportSpacing != null)
            {
                double.TryParse(txtTmcSupportSpacing.Text, out supportSpacingM);
                if (supportSpacingM <= 0) supportSpacingM = 1.5;
            }

            double thicknessMm = 1.5;
            if (txtTmcThickness != null)
            {
                double.TryParse(txtTmcThickness.Text, out thicknessMm);
                if (thicknessMm <= 0) thicknessMm = 1.5;
            }

            bool insertSupports = (chkTmcInsertSupports != null && chkTmcInsertSupports.IsChecked == true);
            bool addAnnotations = (chkTmcAddAnnotations != null && chkTmcAddAnnotations.IsChecked == true);

            return new CableTrayParams
            {
                Type = trayType,
                WidthMm = widthMm,
                HeightMm = heightMm,
                ThicknessMm = thicknessMm,
                SupportSpacingM = supportSpacingM,
                InsertSupports = insertSupports,
                AddAnnotations = addAnnotations
            };
        }

        public void OnTmcDimensionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OnTmcDimensionChanged(sender, e);
        }

        public void OnTmcDimensionChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;
                PromptSelectionResult psr = ed.SelectImplied();
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    CableTrayParams p = GetCableTrayParamsFromUi();
                    int count = CableTrayService.UpdateBlockReferenceDimensions(doc, p, psr.Value);
                    if (count > 0 && txtStatus != null)
                    {
                        txtStatus.Text = string.Format("Đã tự động điều chỉnh {0} Block theo thông số W{1:F0}xH{2:F0}!",
                            count, p.WidthMm, p.HeightMm);
                    }
                }
            }
            catch { }
        }

        private void BtnUpdateSelectedTmc_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                CableTrayParams p = GetCableTrayParamsFromUi();
                Application.MainWindow.Focus();
                int count = CableTrayService.UpdateBlockReferenceDimensions(doc, p);
                if (count > 0 && txtStatus != null)
                {
                    txtStatus.Text = string.Format("Đã cập nhật thành công {0} Block Thang Máng Cáp theo kích thước W{1:F0}xH{2:F0}!",
                        count, p.WidthMm, p.HeightMm);
                }
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi cập nhật: {0}", ex.Message);
            }
        }

        private void StartDrawTmcWithType(CableTrayType type, string typeName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Đồng bộ ngầm ComboBox
                if (cbTmcType != null)
                {
                    if (type == CableTrayType.Trunking) cbTmcType.SelectedIndex = 0;
                    else if (type == CableTrayType.Ladder) cbTmcType.SelectedIndex = 1;
                    else if (type == CableTrayType.Tray) cbTmcType.SelectedIndex = 2;
                }

                CableTrayParams p = GetCableTrayParamsFromUi();
                p.Type = type; // Gán đúng loại của nút bấm
                CableTrayService.ActiveParams = p;

                if (txtStatus != null)
                {
                    txtStatus.Text = string.Format("⚡ Live Jig {0} (W{1}xH{2}): Click điểm đầu trên CAD để bắt đầu kéo dãn...", typeName, p.WidthMm, p.HeightMm);
                }

                // Gửi lệnh NTTTMC vào AutoCAD để chạy trong Native Command Context: chuột kéo Jig mượt mà 60 FPS, bắt điểm OSNAP chuẩn xác
                doc.SendStringToExecute("NTTTMC\n", true, false, false);
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = "Lỗi kích hoạt Live Jig: " + ex.Message;
            }
        }

        private void BtnDrawTray_Click(object sender, RoutedEventArgs e)
        {
            StartDrawTmcWithType(CableTrayType.Tray, "Khay Cáp (Tray)");
        }

        private void BtnDrawTrunking_Click(object sender, RoutedEventArgs e)
        {
            StartDrawTmcWithType(CableTrayType.Trunking, "Trunking (Máng Cáp)");
        }

        private void BtnDrawLadder_Click(object sender, RoutedEventArgs e)
        {
            StartDrawTmcWithType(CableTrayType.Ladder, "Thang Điện (Ladder)");
        }

        private void BtnDrawTmcManual_Click(object sender, RoutedEventArgs e)
        {
            BtnDrawTrunking_Click(sender, e);
        }
        #endregion

        private void BtnRouteTmcAlongPl_Click(object sender, RoutedEventArgs e)

        {

            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)

            {

                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }

            Editor ed = doc.Editor;



            try

            {

                CableTrayParams p = GetCableTrayParamsFromUi();

                Application.MainWindow.Focus();



                int routedCount = 0;

                while (true)

                {

                    PromptEntityOptions peo = new PromptEntityOptions("\n[NTT TOOLS] Chọn đường Polyline (PL) định tuyến Thang Máng Cáp (hoặc ENTER/ESC để kết thúc): ");

                    peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline!");

                    peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

                    peo.AllowNone = true;

                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK) break;



                    using (DocumentLock docLock = doc.LockDocument())

                    {

                        CableTrayRouteResult res = CableTrayService.RouteCableTrayAlongPolyline(doc, per.ObjectId, p);

                        if (res != null)

                        {

                            _paletteLastTmcResult = res;

                            _paletteLastTmcParams = p;

                            routedCount++;

                            txtStatus.Text = string.Format("Đã vẽ xong tuyến {0} {1}: {2:F2}m, {3} Co 90°, {4} Co 45°, {5} Giá đỡ.",

                                res.TypeName, res.DimensionLabel, res.TotalLengthM, res.Elbow90Count, res.Elbow45Count, res.SupportCount);

                        }

                    }

                }

            }

            catch (Exception ex)

            {

                txtStatus.Text = string.Format("Lỗi định tuyến TMC: {0}", ex.Message);

                if (ed != null)

                {

                    ed.WriteMessage(string.Format("\n[NTT Error] {0}", ex.Message));

                }

            }

        }



        private void BtnDrawTmcBOMTable_Click(object sender, RoutedEventArgs e)

        {

            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)

            {

                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }



            try

            {

                CableTrayParams p = _paletteLastTmcParams ?? GetCableTrayParamsFromUi();

                CableTrayRouteResult res = _paletteLastTmcResult;



                if (res == null)

                {

                    res = new CableTrayRouteResult

                    {

                        TypeName = p.Type == CableTrayType.Ladder ? "Thang Cáp (Cable Ladder)" : (p.Type == CableTrayType.Tray ? "Khay Cáp (Perforated Cable Tray)" : "Máng Cáp (Cable Trunking)"),

                        DimensionLabel = string.Format("{0}x{1} mm", p.WidthMm, p.HeightMm),

                        WidthMm = p.WidthMm,

                        HeightMm = p.HeightMm,

                        ThicknessMm = p.ThicknessMm,

                        TotalLengthM = 15.0,

                        SupportCount = 11,

                        ElbowCount = 2,

                        SegmentCount = 3,

                        SheetMetalWeightKg = 110.0

                    };

                }



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    txtStatus.Text = "Click chọn điểm đặt Bảng Bóc Tách Khối Lượng TMC trên bản vẽ...";

                    CableTrayService.DrawCableTrayBOMTableInCad(doc, res, p);

                    txtStatus.Text = "Đã xuất Bảng Bóc Tách Khối Lượng TMC vào bản vẽ CAD!";

                }

            }

            catch (Exception ex)

            {

                txtStatus.Text = string.Format("Lỗi xuất bảng BOM: {0}", ex.Message);

                if (doc.Editor != null)

                {

                    doc.Editor.WriteMessage(string.Format("\n[NTT Error] {0}", ex.Message));

                }

            }

        }



        private void BtnOpenTmcFolder_Click(object sender, RoutedEventArgs e)

        {

            CableTrayService.OpenLibraryFolder();

            if (txtStatus != null) txtStatus.Text = "Đã mở thư mục Thư Viện Thang Máng Cáp trong File Explorer.";

        }







    }
}

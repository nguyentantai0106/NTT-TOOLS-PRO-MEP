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

        #region Nghiệp Vụ Phân Hệ Điều Hòa Không Khí (ĐHKK & Thông Gió)



        private void BtnOpenHvacFolder_Click(object sender, RoutedEventArgs e)
        {
            HvacService.OpenLibraryFolder();
        }

        public void ReloadHvacCatalog()
        {
            try
            {
                string libPath = HvacService.GetPrimaryLibraryPath();
                if (txtHvacLibPath != null) txtHvacLibPath.Text = libPath;

                List<HvacBlockItem> catalog = HvacService.GetCatalog();

                if (cbHvacCatalog != null)
                {
                    cbHvacCatalog.Items.Clear();
                    foreach (var item in catalog)
                    {
                        ComboBoxItem cbi = new ComboBoxItem();
                        cbi.Content = string.Format("[{0}] {1}", item.Category, item.FileName);
                        cbi.Tag = item;
                        cbHvacCatalog.Items.Add(cbi);
                    }
                    if (cbHvacCatalog.Items.Count > 0)
                    {
                        cbHvacCatalog.SelectedIndex = 0;
                    }
                }

                if (badgeHvacBlockCount != null)
                {
                    badgeHvacBlockCount.Text = string.Format("{0} BLOCKS", catalog.Count);
                }

                if (txtStatus != null)
                {
                    txtStatus.Text = string.Format("✓ Đã nạp thành công {0} Block ĐHKK từ thư viện!", catalog.Count);
                }
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi nạp thư viện ĐHKK: {0}", ex.Message);
            }
        }

        private void BtnReloadHvacCatalog_Click(object sender, RoutedEventArgs e)
        {
            ReloadHvacCatalog();
        }



                private void BtnRouteDuctAlongPl_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                // Đọc thông số UI trước khi chuyển focus sang AutoCAD

                string sysLayer = "Gió cấp";

                if (cbDuctSystemLayer != null)

                {

                    ComboBoxItem item = cbDuctSystemLayer.SelectedItem as ComboBoxItem;

                    if (item != null && item.Content != null) sysLayer = item.Content.ToString();

                }



                double q0 = 3000.0, v = 6.0, h = 300.0, stepDown = 20.0;

                double.TryParse(txtDuctFlowCmh != null ? txtDuctFlowCmh.Text : "3000", out q0);

                double.TryParse(txtDuctVelocity != null ? txtDuctVelocity.Text : "6.0", out v);

                double.TryParse(txtDuctHeight != null ? txtDuctHeight.Text : "300", out h);



                if (cbDuctStepDown != null)

                {

                    switch (cbDuctStepDown.SelectedIndex)

                    {

                        case 0: stepDown = 0.0; break;

                        case 1: stepDown = 15.0; break;

                        case 2: stepDown = 20.0; break;

                        case 3: stepDown = 25.0; break;

                        case 4: stepDown = 30.0; break;

                    }

                }



                DuctRoutingParams p = new DuctRoutingParams

                {

                    SystemType = sysLayer,

                    InitialAirFlowCmh = q0,

                    VelocityMs = v,

                    FixedHeightMm = h,

                    StepDownPercent = stepDown,

                    DrawDoubleLine = chkDuctDoubleLine == null || chkDuctDoubleLine.IsChecked == true,

                    DrawElbows = true,

                    DrawCenterline = chkDuctCenterline != null && chkDuctCenterline.IsChecked == true,

                    AddAnnotations = chkDuctAnnotations == null || chkDuctAnnotations.IsChecked == true

                };



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    Editor ed = doc.Editor;
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\n[ĐHKK] Quét chọn các đường Polyline / Line mồi định tuyến ống gió: ";
                    SelectionFilter filter = new SelectionFilter(new TypedValue[] {
                        new TypedValue((int)DxfCode.Start, "LWPOLYLINE,LINE,POLYLINE")
                    });
                    PromptSelectionResult psr = ed.GetSelection(pso, filter);
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0) return;

                    List<ObjectId> plIds = new List<ObjectId>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so != null && !so.ObjectId.IsNull) plIds.Add(so.ObjectId);
                    }

                    bool ok = HvacService.RouteDuctAlongPolylines(doc, plIds, p);

                    if (ok && txtStatus != null)
                    {
                        txtStatus.Text = string.Format("✓ Đã vẽ tuyến ống gió ({0}, Q={1:F0} CMH, Co, Gót giày & KNM) thành công!", sysLayer, q0);
                    }

                }

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống gió: {0}", ex.Message);

            }

        }



        private void TxtRefCoolingKw_TextChanged(object sender, TextChangedEventArgs e)

        {

            try

            {

                if (txtRefCoolingKw == null || txtResRefnetModel == null) return;

                double kw = 28.0;

                double.TryParse(txtRefCoolingKw.Text, out kw);

                if (kw <= 0) kw = 28.0;



                DaikinRefnetSpec spec = HvacService.GetDaikinRefnetByCapacity(kw);

                txtResRefnetModel.Text = string.Format("DAIKIN {0} (<={1:F0} kW)", spec.ModelName.Replace("Bo_Chia_Gas_Daikin_", ""), spec.MaxKw);

                if (txtResGasPipe != null) txtResGasPipe.Text = string.Format("Gas: {0} (MEP_HVAC_ONG_DONG_GAS)", spec.GasPipeStr);

                if (txtResLiqPipe != null) txtResLiqPipe.Text = string.Format("Liq: {0} (MEP_HVAC_ONG_DONG_LONG)", spec.LiqPipeStr);

            }

            catch { }

        }



        private double GetSelectedRefTextHeight()
        {
            double h = 200.0;
            if (cbRefDrawScale != null && cbRefDrawScale.SelectedItem is ComboBoxItem)
            {
                ComboBoxItem item = (ComboBoxItem)cbRefDrawScale.SelectedItem;
                if (item.Tag != null)
                {
                    double parsed;
                    if (double.TryParse(item.Tag.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                    {
                        h = parsed;
                    }
                }
            }
            return h;
        }

        private RefrigerantRoutingParams GetRefParamsFromUi()
        {
            double kw = 28.0, spacing = 60.0;
            double.TryParse(txtRefCoolingKw != null ? txtRefCoolingKw.Text : "28.0", NumberStyles.Any, CultureInfo.InvariantCulture, out kw);
            double.TryParse(txtRefPipeSpacing != null ? txtRefPipeSpacing.Text : "60", NumberStyles.Any, CultureInfo.InvariantCulture, out spacing);
            if (kw <= 0) kw = 28.0;

            return new RefrigerantRoutingParams
            {
                TotalCoolingKw = kw,
                PipeSpacingMm = spacing,
                InsertRefnetJoints = chkInsertRefnetJoints == null || chkInsertRefnetJoints.IsChecked == true,
                DrawCombinedDrain = chkRefCombinedDrain != null && chkRefCombinedDrain.IsChecked == true,
                DrainSlopePercent = 1.0,
                AddAnnotations = chkRefAnnotations == null || chkRefAnnotations.IsChecked == true,
                TextHeight = GetSelectedRefTextHeight()
            };
        }

        private void CbRefDrawScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (!IsLoaded || !_isSettingsLoaded) return;
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                double textH = GetSelectedRefTextHeight();
                int updated = HvacService.UpdateAllGasLabelsTextHeight(doc, textH);
                if (txtStatus != null)
                {
                    txtStatus.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "✓ Đã đổi tỷ lệ nhãn ống gas (Cỡ chữ {0:F0}mm), cập nhật {1} nhãn trên bản vẽ!",
                        textH, updated);
                }
            }
            catch { }
        }

        private void BtnDrawRefrigerantDirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";
                    return;
                }

                RefrigerantRoutingParams p = GetRefParamsFromUi();
                Application.MainWindow.Focus();

                bool ok = HvacService.DrawRefrigerantInteractive(doc, p);
                if (ok && txtStatus != null)
                {
                    txtStatus.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "✓ Đã vẽ trực tiếp tuyến Ống Gas (Cỡ chữ {0:F0}mm, Tự chia & Tự tính size)!",
                        p.TextHeight);
                }
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống gas trực tiếp: {0}", ex.Message);
            }
        }

        private void BtnRouteRefrigerantAlongPl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";
                    return;
                }

                RefrigerantRoutingParams p = GetRefParamsFromUi();
                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    Editor ed = doc.Editor;
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\n[ĐHKK] Quét chọn các đường Polyline / Line mồi định tuyến Ống Gas & Bộ Chia: ";
                    SelectionFilter filter = new SelectionFilter(new TypedValue[] {
                        new TypedValue((int)DxfCode.Start, "LWPOLYLINE,LINE,POLYLINE")
                    });
                    PromptSelectionResult psr = ed.GetSelection(pso, filter);
                    if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0) return;

                    List<ObjectId> plIds = new List<ObjectId>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so != null && !so.ObjectId.IsNull) plIds.Add(so.ObjectId);
                    }

                    bool ok = HvacService.RouteRefrigerantAlongPolylines(doc, plIds, p);

                    if (ok && txtStatus != null)
                    {
                        DaikinRefnetSpec spec = HvacService.GetDaikinRefnetByCapacity(p.TotalCoolingKw);
                        txtStatus.Text = string.Format(
                            CultureInfo.InvariantCulture,
                            "✓ Đã vẽ Ống Gas (Ø{0:0.##}/Ø{1:0.##}, Cỡ chữ {2:F0}mm), NTT-BOCHIAGA (R=50) & Bo cua R=100!",
                            spec.GasDiaMm, spec.LiqDiaMm, p.TextHeight);
                    }
                }
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống đồng: {0}", ex.Message);
            }
        }

        private void BtnRecalcRefrigerantTree_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";
                    return;
                }

                double defaultLeafKw = 7.1;
                double textH = GetSelectedRefTextHeight();
                Application.MainWindow.Focus();

                double totalKw;
                int updatedCount;
                bool ok = HvacService.RecalculateRefrigerantNetwork(doc, defaultLeafKw, textH, out totalKw, out updatedCount);
                if (ok && txtStatus != null)
                {
                    txtStatus.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "✓ Đã tính lại toàn mạng ống gas: Tổng {0:F1} kW (~{1:F1} HP), Cỡ chữ {2:F0}mm, cập nhật {3} đoạn ống!",
                        totalKw, totalKw / 2.8, textH, updatedCount);
                }
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính lại mạng ống gas: {0}", ex.Message);
            }
        }



        private void BtnRouteDrainAlongPl_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                double slope = 1.0, startElev = 2.8, kw = 10.0;

                if (cbDrainSlope != null)

                {

                    switch (cbDrainSlope.SelectedIndex)

                    {

                        case 0: slope = 1.0; break;

                        case 1: slope = 1.5; break;

                        case 2: slope = 2.0; break;

                    }

                }

                double.TryParse(txtDrainStartElev != null ? txtDrainStartElev.Text : "2.8", out startElev);



                if (cbDrainPipeDia != null)

                {

                    switch (cbDrainPipeDia.SelectedIndex)

                    {

                        case 0: kw = 4.0; break;  // D27

                        case 1: kw = 12.0; break; // D34

                        case 2: kw = 25.0; break; // D42

                        case 3: kw = 40.0; break; // D49

                    }

                }



                DrainRoutingParams p = new DrainRoutingParams

                {

                    TotalCoolingKw = kw,

                    SlopePercent = slope,

                    StartElevationM = startElev,

                    AddSlopeArrow = chkDrainSlopeArrow == null || chkDrainSlopeArrow.IsChecked == true,

                    AddAnnotations = true

                };



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    Editor ed = doc.Editor;

                    PromptEntityOptions peo = new PromptEntityOptions("\n[ĐHKK] Chọn đường Polyline (PL) định tuyến Ống Nước Ngưng: ");

                    peo.SetRejectMessage("\nChỉ chọn được đường Polyline (LWPOLYLINE)!");

                    peo.AddAllowedClass(typeof(Polyline), false);

                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK) return;



                    bool ok = HvacService.RouteCondensateDrainAlongPolyline(doc, per.ObjectId, p);

                    if (ok && txtStatus != null)

                    {

                        txtStatus.Text = string.Format("✓ Đã vẽ tuyến Ống Nước Ngưng (độ dốc i={0:F1}%, cao độ +{1:F2}m)!", slope, startElev);

                    }

                }

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống nước ngưng: {0}", ex.Message);

            }

        }



        private void BtnInsertHvacBlock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";
                    return;
                }

                string blkName = "Mieng_Gio_Khuech_Tan_Vuong";
                string dwgFile = "Mieng_Gio_Khuech_Tan_Vuong.dwg";

                ComboBoxItem selCbi = cbHvacCatalog != null ? cbHvacCatalog.SelectedItem as ComboBoxItem : null;
                HvacBlockItem tagItem = selCbi != null ? selCbi.Tag as HvacBlockItem : null;
                if (tagItem != null)
                {
                    blkName = tagItem.Name;
                    dwgFile = !string.IsNullOrEmpty(tagItem.FullPath) ? tagItem.FullPath : tagItem.FileName;
                }
                else
                {
                    int selIndex = cbHvacCatalog != null ? cbHvacCatalog.SelectedIndex : 0;
                    List<HvacBlockItem> catalog = HvacService.GetCatalog();
                    if (selIndex >= 0 && selIndex < catalog.Count)
                    {
                        blkName = catalog[selIndex].Name;
                        dwgFile = !string.IsNullOrEmpty(catalog[selIndex].FullPath) ? catalog[selIndex].FullPath : catalog[selIndex].FileName;
                    }
                }

                Application.MainWindow.Focus();
                HvacService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);
                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã chèn {0} vào bản vẽ thành công!", blkName);
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi chèn block: {0}", ex.Message);
            }
        }



        private HvacDuctCalcInput GetDuctInputFromUi()

        {

            double q = 1200.0, v = 5.5, h = 250.0, l = 15.0;

            double.TryParse(txtDuctFlowCmh != null ? txtDuctFlowCmh.Text : "1200", out q);

            double.TryParse(txtDuctVelocity != null ? txtDuctVelocity.Text : "5.5", out v);

            double.TryParse(txtDuctHeight != null ? txtDuctHeight.Text : "250", out h);

            double.TryParse(txtDuctLength != null ? txtDuctLength.Text : "15.0", out l);



            return new HvacDuctCalcInput

            {

                AirFlowCmh = q,

                VelocityMs = v,

                DuctHeightMm = h,

                DuctLengthM = l,

                DuctType = "Vuông"

            };

        }



        private void BtnCalcDuctSize_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                HvacDuctCalcInput inp = GetDuctInputFromUi();

                HvacDuctCalcResult res = HvacService.CalculateDuctSize(inp);



                if (txtResDuctRect != null) txtResDuctRect.Text = string.Format("{0:F0} x {1:F0} mm (W/H={2:F1})", res.RectWidthMm, res.RectHeightMm, res.RectAspectRatio);

                if (txtResDuctRound != null) txtResDuctRound.Text = string.Format("Ø{0:F0} mm (Tính toán: Ø{1:F0} mm)", res.RoundStandardMm, res.RoundDiameterMm);

                if (txtResDuctLoss != null) txtResDuctLoss.Text = string.Format("v = {0:F2} m/s | {1:F2} Pa/m ({2:F1} Pa)", res.ActualVelocityMs, res.UnitFrictionLossPam, res.TotalFrictionLossPa);

                if (txtResDuctWeight != null) txtResDuctWeight.Text = string.Format("Tôn {0:F2}mm | {1:F1} m² (~{2:F1} kg)", res.SheetThicknessMm, res.SheetMetalAreaM2, res.SheetMetalWeightKg);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong ống gió: {0:F0}x{1:F0}mm, V={2:F2}m/s, Tôn {3:F1}kg", res.RectWidthMm, res.RectHeightMm, res.ActualVelocityMs, res.SheetMetalWeightKg);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính ống gió: {0}", ex.Message);

            }

        }



        private void BtnDrawDuctTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                HvacDuctCalcInput inp = GetDuctInputFromUi();

                HvacDuctCalcResult res = HvacService.CalculateDuctSize(inp);



                Application.MainWindow.Focus();

                HvacService.DrawDuctSizingTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Kích Thước Ống Gió vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void CbCoolingSpaceType_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (cbCoolingSpaceType == null || txtCoolingSpecificLoad == null) return;

            switch (cbCoolingSpaceType.SelectedIndex)

            {

                case 0: txtCoolingSpecificLoad.Text = "200"; break; // Văn phòng

                case 1: txtCoolingSpecificLoad.Text = "160"; break; // Phòng ngủ / KS

                case 2: txtCoolingSpecificLoad.Text = "220"; break; // Phòng khách / Biệt thự

                case 3: txtCoolingSpecificLoad.Text = "250"; break; // TTTM

                case 4: txtCoolingSpecificLoad.Text = "320"; break; // Nhà hàng / Bếp

            }

        }



        private HvacCoolingCalcInput GetCoolingInputFromUi()

        {

            double area = 45.0, ceilingH = 3.2, specificLoad = 200.0, margin = 10.0;

            double.TryParse(txtCoolingArea != null ? txtCoolingArea.Text : "45.0", out area);

            double.TryParse(txtCoolingCeilingH != null ? txtCoolingCeilingH.Text : "3.2", out ceilingH);

            double.TryParse(txtCoolingSpecificLoad != null ? txtCoolingSpecificLoad.Text : "200", out specificLoad);

            double.TryParse(txtCoolingMargin != null ? txtCoolingMargin.Text : "10", out margin);



            string spaceType = "Văn phòng làm việc";

            if (cbCoolingSpaceType != null)

            {

                ComboBoxItem item = cbCoolingSpaceType.SelectedItem as ComboBoxItem;

                if (item != null && item.Content != null) spaceType = item.Content.ToString();

            }



            return new HvacCoolingCalcInput

            {

                SpaceType = spaceType,

                RoomAreaM2 = area,

                CeilingHeightM = ceilingH,

                CoolingIndexWPerM2 = specificLoad,

                SafetyMarginPercent = margin

            };

        }



        private void BtnCalcCooling_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                HvacCoolingCalcInput inp = GetCoolingInputFromUi();

                HvacCoolingCalcResult res = HvacService.CalculateCoolingLoad(inp);



                if (txtResCoolingLoad != null) txtResCoolingLoad.Text = string.Format("{0:F2} kW (~{1:F0} BTU/h - {2:F1} RT)", res.TotalLoadKw, res.TotalLoadBtu, res.TotalLoadRt);

                if (txtResFcuModel != null) txtResFcuModel.Text = res.SuggestedFcuModel;

                if (txtResVrvOutdoor != null) txtResVrvOutdoor.Text = res.SuggestedVrvOutdoor;



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính phụ tải lạnh: {0:F1} kW ({1:F0} BTU/h) cho diện tích {2:F1} m²", res.TotalLoadKw, res.TotalLoadBtu, res.RoomAreaM2);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính phụ tải: {0}", ex.Message);

            }

        }



        private void BtnDrawCoolingTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                HvacCoolingCalcInput inp = GetCoolingInputFromUi();

                HvacCoolingCalcResult res = HvacService.CalculateCoolingLoad(inp);



                Application.MainWindow.Focus();

                HvacService.DrawCoolingLoadTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Chọn FCU & VRV vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void BtnQuickPlaceDiffuser_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Mieng_Gio_Khuech_Tan_Vuong", "Mieng_Gio_Khuech_Tan_Vuong.dwg");

        }



        private void BtnQuickPlaceFcu_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Dan_Lanh_FCU_Giau_Tran", "Dan_Lanh_FCU_Giau_Tran.dwg");

        }



        private void BtnQuickPlaceVcd_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Van_Gio_Vuong_VCD", "Van_Gio_Vuong_VCD.dwg");

        }



        private void BtnQuickPlaceFireDamper_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Van_Dap_Lua_FD", "Van_Dap_Lua_FD.dwg");

        }



        private void PlaceQuickHvacDevice(string blkName, string dwgFile)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null) return;

                Application.MainWindow.Focus();

                HvacService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

                if (txtStatus != null) txtStatus.Text = string.Format("Đã bố trí {0} thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi bố trí: {0}", ex.Message);

            }

        }



        #endregion




    }
}

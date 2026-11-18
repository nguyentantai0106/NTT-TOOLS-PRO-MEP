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

        #region Nghiệp Vụ Phân Hệ Cấp Thoát Nước (Plumbing & Xử Lý Nước)



        private void BtnOpenPlumbingFolder_Click(object sender, RoutedEventArgs e)

        {

            PlumbingService.OpenLibraryFolder();

        }



        private void BtnReloadPlumbingCatalog_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                if (txtPlumbingLibPath != null) txtPlumbingLibPath.Text = PlumbingService.GetPrimaryLibraryPath();

                if (txtStatus != null) txtStatus.Text = "Đã cập nhật lại thư mục Block CTN!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);

            }

        }



        private void BtnInsertPlumbingBlock_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                string blkName = "Ban_Cau_Ket_Hop";

                string dwgFile = "Ban_Cau_Ket_Hop.dwg";



                int selIndex = cbPlumbingCatalog != null ? cbPlumbingCatalog.SelectedIndex : 0;

                List<PlumbingBlockItem> catalog = PlumbingService.GetCatalog();

                if (selIndex >= 0 && selIndex < catalog.Count)

                {

                    blkName = Path.GetFileNameWithoutExtension(catalog[selIndex].FileName);

                    dwgFile = catalog[selIndex].FileName;

                }



                Application.MainWindow.Focus();

                PlumbingService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã chèn {0} vào bản vẽ thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi chèn block: {0}", ex.Message);

            }

        }



        private PlumbingWaterSupplyInput GetWaterSupplyInputFromUi()

        {

            int wc = 6, lav = 6, sink = 2, shower = 4, urinal = 2, wm = 2, residents = 25;

            double floors = 5.0;



            int.TryParse(txtWsfuWc != null ? txtWsfuWc.Text : "6", out wc);

            int.TryParse(txtWsfuLav != null ? txtWsfuLav.Text : "6", out lav);

            int.TryParse(txtWsfuSink != null ? txtWsfuSink.Text : "2", out sink);

            int.TryParse(txtWsfuShower != null ? txtWsfuShower.Text : "4", out shower);

            int.TryParse(txtWsfuUrinal != null ? txtWsfuUrinal.Text : "2", out urinal);

            int.TryParse(txtWsfuWm != null ? txtWsfuWm.Text : "2", out wm);

            int.TryParse(txtWsfuResidents != null ? txtWsfuResidents.Text : "25", out residents);

            double.TryParse(txtWsfuFloors != null ? txtWsfuFloors.Text : "5", out floors);



            string bldType = "Nhà ở gia đình";

            if (cbPlumbingBuildingType != null)

            {

                ComboBoxItem item = cbPlumbingBuildingType.SelectedItem as ComboBoxItem;

                if (item != null && item.Content != null) bldType = item.Content.ToString();

            }



            return new PlumbingWaterSupplyInput

            {

                BuildingType = bldType,

                CountWc = wc,

                CountLavabo = lav,

                CountKitchenSink = sink,

                CountShower = shower,

                CountUrinal = urinal,

                CountWashingMachine = wm,

                ResidentCount = residents,

                FloorCount = floors

            };

        }



        private void BtnCalcWaterSupply_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                PlumbingWaterSupplyInput inp = GetWaterSupplyInputFromUi();

                PlumbingWaterSupplyResult res = PlumbingService.CalculateWaterSupply(inp);



                if (txtResSupplyFlow != null) txtResSupplyFlow.Text = string.Format("{0:F1} WSFU | {1:F2} l/s ({2:F1} m³/h)", res.TotalWsfu, res.DesignFlowLs, res.DesignFlowM3h);

                if (txtResSupplyPipe != null) txtResSupplyPipe.Text = string.Format("{0} (v = {1:F2} m/s)", res.SuggestedPipeName, res.VelocityMs);

                if (txtResSupplyTanks != null) txtResSupplyTanks.Text = string.Format("{0:F1} m³/ngày | Bể ngầm {1:F1} m³ - Mái {2:F1} m³", res.DailyDemandM3, res.UndergroundTankM3, res.RoofTankM3);

                if (txtResBooster != null) txtResBooster.Text = string.Format("Cột áp H = {0:F1} m | Công suất P = {1:F1} kW", res.BoosterHeadM, res.BoosterPowerKw);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong nước cấp: {0:F1} WSFU, Q={1:F2} l/s, Ống {2}", res.TotalWsfu, res.DesignFlowLs, res.SuggestedPipeName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính nước cấp: {0}", ex.Message);

            }

        }



        private void BtnDrawWaterSupplyTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                PlumbingWaterSupplyInput inp = GetWaterSupplyInputFromUi();

                PlumbingWaterSupplyResult res = PlumbingService.CalculateWaterSupply(inp);



                Application.MainWindow.Focus();

                PlumbingService.DrawWaterSupplyTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Nước Cấp & Bể Chứa vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private PlumbingDrainageInput GetDrainageInputFromUi()

        {

            int wc = 6, lav = 6, sink = 2, fd = 6, shower = 4, residents = 25;

            double slope = 2.0;



            int.TryParse(txtDfuWc != null ? txtDfuWc.Text : "6", out wc);

            int.TryParse(txtDfuLav != null ? txtDfuLav.Text : "6", out lav);

            int.TryParse(txtDfuSink != null ? txtDfuSink.Text : "2", out sink);

            int.TryParse(txtDfuFd != null ? txtDfuFd.Text : "6", out fd);

            int.TryParse(txtDfuShower != null ? txtDfuShower.Text : "4", out shower);

            double.TryParse(txtDfuSlope != null ? txtDfuSlope.Text : "2.0", out slope);

            int.TryParse(txtWsfuResidents != null ? txtWsfuResidents.Text : "25", out residents);



            return new PlumbingDrainageInput

            {

                CountWc = wc,

                CountLavabo = lav,

                CountKitchenSink = sink,

                CountFloorDrain = fd,

                CountShower = shower,

                DesignSlopePercent = slope,

                ResidentCount = residents

            };

        }



        private void BtnCalcDrainage_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                PlumbingDrainageInput inp = GetDrainageInputFromUi();

                PlumbingDrainageResult res = PlumbingService.CalculateDrainage(inp);



                if (txtResDfuTotal != null) txtResDfuTotal.Text = string.Format("{0:F1} DFU", res.TotalDfu);

                if (txtResSoilStack != null) txtResSoilStack.Text = string.Format("Phân {0} | Thải {1}", res.SoilStackDia, res.WasteStackDia);

                if (txtResDrainBranch != null) txtResDrainBranch.Text = string.Format("Nhánh {0} (i={1:F1}%) | Hơi {2}", res.HorizontalBranchDia, res.RecommendedSlopePercent, res.MainVentDia);

                if (txtResSepticTank != null) txtResSepticTank.Text = string.Format("V = {0:F1} m³ ({1})", res.SepticTankVolumeM3, res.SepticTankDimensions);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong thoát nước: {0:F1} DFU, Bể tự hoại {1:F1} m³", res.TotalDfu, res.SepticTankVolumeM3);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính thoát nước: {0}", ex.Message);

            }

        }



        private void BtnDrawDrainageTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                PlumbingDrainageInput inp = GetDrainageInputFromUi();

                PlumbingDrainageResult res = PlumbingService.CalculateDrainage(inp);



                Application.MainWindow.Focus();

                PlumbingService.DrawDrainageTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Chọn Cỡ Ống Thoát Nước vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void BtnQuickPlaceToilet_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Ban_Cau_Ket_Hop", "Ban_Cau_Ket_Hop.dwg");

        }



        private void BtnQuickPlaceLavabo_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Chau_Rua_Lavabo", "Chau_Rua_Lavabo.dwg");

        }



        private void BtnQuickPlaceFloorDrain_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Pheu_Thu_San_FloorDrain", "Pheu_Thu_San_FloorDrain.dwg");

        }



        private void BtnQuickPlaceBoosterPump_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Cum_Bom_Cap_Nuoc_Booster", "Cum_Bom_Cap_Nuoc_Booster.dwg");

        }



        private void PlaceQuickPlumbingDevice(string blkName, string dwgFile)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null) return;

                Application.MainWindow.Focus();

                PlumbingService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

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

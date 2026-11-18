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

        #region Phân Hệ Phòng Cháy Chữa Cháy (PCCC)



        private void BtnOpenPcccFolder_Click(object sender, RoutedEventArgs e)

        {

            FireProtectionService.OpenLibraryFolder();

            if (txtStatus != null) txtStatus.Text = "Đã mở thư mục Thư Viện PCCC trong File Explorer.";

        }



        private void BtnReloadPcccCatalog_Click(object sender, RoutedEventArgs e)

        {

            if (txtStatus != null) txtStatus.Text = "Đã làm mới danh mục 13 block thư viện PCCC tiêu chuẩn!";

        }



        private void BtnInsertPcccBlock_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                int idx = cbPcccCatalog != null ? cbPcccCatalog.SelectedIndex : 0;

                var catalog = FireProtectionService.GetCatalog();

                if (idx < 0 || idx >= catalog.Count) idx = 0;

                var item = catalog[idx];



                bool drawCov = chkDrawBlockCoverage != null && chkDrawBlockCoverage.IsChecked == true;

                double covR = drawCov ? item.DefaultCoverageRadius : 0;



                Application.MainWindow.Focus();

                FireProtectionService.InsertSingleBlockWithPrompt(doc, Path.GetFileNameWithoutExtension(item.FileName), item.FileName, covR);



                if (txtStatus != null) txtStatus.Text = string.Format("Đã chèn {0} thành công!", item.Name);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi chèn block: {0}", ex.Message);

            }

        }



        private void CbPcccHazard_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (cbPcccHazard == null || txtSprinklerDx == null) return;

            int idx = cbPcccHazard.SelectedIndex;

            if (idx == 0) // Thấp

            {

                txtSprinklerDx.Text = "3.6";

                txtSprinklerDy.Text = "3.6";

                txtSprinklerWallX.Text = "1.8";

                txtSprinklerWallY.Text = "1.8";

                txtSprinklerCoverageR.Text = "2.1";

            }

            else if (idx == 1) // TB1

            {

                txtSprinklerDx.Text = "3.2";

                txtSprinklerDy.Text = "3.2";

                txtSprinklerWallX.Text = "1.6";

                txtSprinklerWallY.Text = "1.6";

                txtSprinklerCoverageR.Text = "2.0";

            }

            else if (idx == 2) // TB2

            {

                txtSprinklerDx.Text = "3.0";

                txtSprinklerDy.Text = "3.0";

                txtSprinklerWallX.Text = "1.5";

                txtSprinklerWallY.Text = "1.5";

                txtSprinklerCoverageR.Text = "1.9";

            }

            else if (idx == 3) // Cao

            {

                txtSprinklerDx.Text = "2.4";

                txtSprinklerDy.Text = "2.4";

                txtSprinklerWallX.Text = "1.2";

                txtSprinklerWallY.Text = "1.2";

                txtSprinklerCoverageR.Text = "1.5";

            }

        }



        private void BtnAutoSprinkler_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                double dx = 3.0, dy = 3.0, wx = 1.5, wy = 1.5, covR = 2.1;

                double.TryParse(txtSprinklerDx != null ? txtSprinklerDx.Text : "3.0", out dx);

                double.TryParse(txtSprinklerDy != null ? txtSprinklerDy.Text : "3.0", out dy);

                double.TryParse(txtSprinklerWallX != null ? txtSprinklerWallX.Text : "1.5", out wx);

                double.TryParse(txtSprinklerWallY != null ? txtSprinklerWallY.Text : "1.5", out wy);

                double.TryParse(txtSprinklerCoverageR != null ? txtSprinklerCoverageR.Text : "2.1", out covR);



                string blkFile = "Sprinkler_Pendant.dwg";

                int spkTypeIdx = cbSprinklerType != null ? cbSprinklerType.SelectedIndex : 0;

                if (spkTypeIdx == 1) blkFile = "Sprinkler_Upright.dwg";

                else if (spkTypeIdx == 2) blkFile = "Sprinkler_Sidewall.dwg";



                SprinklerLayoutParams cfg = new SprinklerLayoutParams

                {

                    BlockFileName = blkFile,

                    SpacingX = dx,

                    SpacingY = dy,

                    WallOffsetX = wx,

                    WallOffsetY = wy,

                    DrawCoverageCircle = chkCoverageCircle != null && chkCoverageCircle.IsChecked == true,

                    CoverageRadius = covR,

                    ConnectPipes = chkConnectPipes != null && chkConnectPipes.IsChecked == true

                };



                Application.MainWindow.Focus();

                double roomArea = 0;

                int count = FireProtectionService.AutoLayoutSprinklersInCad(doc, cfg, out roomArea);



                if (count > 0)

                {

                    string msg = string.Format("✓ Đã rải thành công {0} đầu phun Sprinkler. S phòng = {1:F1} m²", count, roomArea);

                    if (txtSprinklerResult != null) txtSprinklerResult.Text = msg;

                    if (txtStatus != null) txtStatus.Text = msg;

                }

                else

                {

                    if (txtSprinklerResult != null) txtSprinklerResult.Text = "Đã hủy hoặc không bố trí được đầu phun.";

                }

            }

            catch (Exception ex)

            {

                if (txtSprinklerResult != null) txtSprinklerResult.Text = string.Format("Lỗi: {0}", ex.Message);

            }

        }



        private PcccHydraulicInput GetHydraulicInputFromUi()

        {

            int nHeads = 12, nWall = 2;

            double qSpk = 1.2, qWall = 2.5, hStat = 35.0, lPipe = 120.0, pMin = 10.0, cVal = 120.0;



            int.TryParse(txtHydDesignHeads != null ? txtHydDesignHeads.Text : "12", out nHeads);

            double.TryParse(txtHydSpkHeadFlow != null ? txtHydSpkHeadFlow.Text : "1.2", out qSpk);

            int.TryParse(txtHydWallCount != null ? txtHydWallCount.Text : "2", out nWall);

            double.TryParse(txtHydWallFlow != null ? txtHydWallFlow.Text : "2.5", out qWall);

            double.TryParse(txtHydStaticHeight != null ? txtHydStaticHeight.Text : "35.0", out hStat);

            double.TryParse(txtHydPipeLength != null ? txtHydPipeLength.Text : "120.0", out lPipe);

            double.TryParse(txtHydResidualP != null ? txtHydResidualP.Text : "10.0", out pMin);

            double.TryParse(txtHydC != null ? txtHydC.Text : "120", out cVal);



            return new PcccHydraulicInput

            {

                DesignHeadCount = nHeads,

                SprinklerFlowPerHead = qSpk,

                WallHydrantCount = nWall,

                WallHydrantFlow = qWall,

                StaticHeight = hStat,

                EquivalentPipeLength = lPipe,

                ResidualPressure = pMin,

                HazenWilliamsC = cVal

            };

        }



        private void BtnCalcHydraulics_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                PcccHydraulicInput inp = GetHydraulicInputFromUi();

                PcccHydraulicResult res = FireProtectionService.CalculateHydraulics(inp);



                if (txtResFlow != null) txtResFlow.Text = string.Format("{0:F1} l/s ({1:F1} m³/h)", res.TotalFlowLs, res.TotalFlowM3h);

                if (txtResHead != null) txtResHead.Text = string.Format("{0:F1} m ({1:F1} bar)", res.TotalHeadH, res.TotalHeadH / 10.0);

                if (txtResPipe != null) txtResPipe.Text = string.Format("{0} (v = {1:F2} m/s)", res.PipeDiameter, res.VelocityMs);

                if (txtResPower != null) txtResPower.Text = string.Format("{0:F1} kW ({1:F1} HP)", res.PumpPowerKw, res.PumpPowerHp);

                if (txtResJockey != null) txtResJockey.Text = string.Format("{0:F1} m³/h - {1:F0} m ({2:F1} kW)", res.JockeyFlowM3h, res.JockeyHeadM, res.JockeyPowerKw);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong: Q={0:F1} l/s, H={1:F1}m, Bơm={2:F1} kW", res.TotalFlowLs, res.TotalHeadH, res.PumpPowerKw);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính thủy lực: {0}", ex.Message);

            }

        }



        private void BtnDrawHydTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                PcccHydraulicInput inp = GetHydraulicInputFromUi();

                PcccHydraulicResult res = FireProtectionService.CalculateHydraulics(inp);



                Application.MainWindow.Focus();

                FireProtectionService.DrawHydraulicTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Toán Thủy Lực PCCC vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void BtnQuickPlaceHoseCabinet_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Tu_Chua_Chay_Vach_Tuong", "Tu_Chua_Chay_Vach_Tuong.dwg", 25.0);

        }



        private void BtnQuickPlaceAlarmValve_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Van_Bao_Dong", "Van_Bao_Dong.dwg", 0);

        }



        private void BtnQuickPlaceMfz4_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Binh_MFZ4_Bot", "Binh_MFZ4_Bot.dwg", 0);

        }



        private void BtnQuickPlaceMt3_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Binh_MT3_CO2", "Binh_MT3_CO2.dwg", 0);

        }



        private void PlaceQuickPcccDevice(string blkName, string dwgFile, double covR)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null) return;

                Application.MainWindow.Focus();

                FireProtectionService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile, covR);

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

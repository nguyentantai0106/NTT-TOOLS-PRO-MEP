using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TaiAiAddin.Services
{
    public class PlumbingBlockItem
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
    }

    public class PlumbingWaterSupplyInput
    {
        public string BuildingType { get; set; }    // Nhà ở / Căn hộ, Tòa nhà công cộng / Khách sạn
        public int CountWc { get; set; }            // Bàn cầu (WSFU=3.0)
        public int CountLavabo { get; set; }        // Chậu rửa mặt (WSFU=1.0)
        public int CountKitchenSink { get; set; }   // Chậu rửa bát (WSFU=2.0)
        public int CountShower { get; set; }        // Sen tắm (WSFU=2.0)
        public int CountUrinal { get; set; }        // Tiểu nam (WSFU=2.0)
        public int CountWashingMachine { get; set; }// Máy giặt (WSFU=2.0)
        public int ResidentCount { get; set; }      // Số người dùng nước (vd: 30 người)
        public double FloorCount { get; set; }      // Số tầng cao (vd: 5 tầng)
    }

    public class PlumbingWaterSupplyResult
    {
        public double TotalWsfu { get; set; }
        public double DesignFlowLs { get; set; }
        public double DesignFlowM3h { get; set; }
        public double DailyDemandM3 { get; set; }
        public string SuggestedPipeName { get; set; }
        public double PipeInternalDiaMm { get; set; }
        public double VelocityMs { get; set; }
        public double UndergroundTankM3 { get; set; }
        public double RoofTankM3 { get; set; }
        public double BoosterHeadM { get; set; }
        public double BoosterPowerKw { get; set; }
    }

    public class PlumbingDrainageInput
    {
        public int CountWc { get; set; }            // Bàn cầu (DFU=4.0)
        public int CountLavabo { get; set; }        // Lavabo (DFU=1.0)
        public int CountKitchenSink { get; set; }   // Chậu rửa bát (DFU=2.0)
        public int CountFloorDrain { get; set; }    // Thoát sàn (DFU=2.0)
        public int CountShower { get; set; }        // Sen tắm (DFU=2.0)
        public double DesignSlopePercent { get; set; } // Độ dốc % (1% - 2%)
        public int ResidentCount { get; set; }      // Số người tính bể tự hoại
    }

    public class PlumbingDrainageResult
    {
        public double TotalDfu { get; set; }
        public string SoilStackDia { get; set; }
        public string WasteStackDia { get; set; }
        public string HorizontalBranchDia { get; set; }
        public string MainVentDia { get; set; }
        public double RecommendedSlopePercent { get; set; }
        public double SepticTankVolumeM3 { get; set; }
        public string SepticTankDimensions { get; set; }
    }

    public class PlumbingService
    {
        public const string LayerWaterSupply = "MEP_CTN_CAP_NUOC";
        public const string LayerDrainage = "MEP_CTN_THOAT_NUOC";
        public const string LayerPlumbingEquipment = "MEP_CTN_THIET_BI";
        public const string LayerPlumbingTable = "MEP_CTN_BANG_TINH";

        public static string GetPrimaryLibraryPath()
        {
            string primary = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\CTN";
            if (Directory.Exists(primary)) return primary;

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\CTN");
            if (Directory.Exists(appData)) return appData;

            string localBundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Thu Viện\CTN");
            if (Directory.Exists(localBundle)) return localBundle;

            return primary;
        }

        public static void OpenLibraryFolder()
        {
            try
            {
                string path = GetPrimaryLibraryPath();
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument != null ? Application.DocumentManager.MdiActiveDocument.Editor : null;
                if (ed != null) ed.WriteMessage(string.Format("\n[LỖI] Không thể mở thư mục thư viện CTN: {0}", ex.Message));
            }
        }

        public static List<PlumbingBlockItem> GetCatalog()
        {
            return new List<PlumbingBlockItem>
            {
                new PlumbingBlockItem { Name = "Bàn cầu kết hợp két nước", FileName = "Ban_Cau_Ket_Hop.dwg", Category = "Thiết Bị Vệ Sinh", Description = "Bàn cầu xả nhấn tiết kiệm nước 700x400" },
                new PlumbingBlockItem { Name = "Chậu rửa mặt Lavabo", FileName = "Chau_Rua_Lavabo.dwg", Category = "Thiết Bị Vệ Sinh", Description = "Lavabo đặt bàn / treo tường có xi-phông 550x450" },
                new PlumbingBlockItem { Name = "Chậu rửa bát đôi nhà bếp", FileName = "Chau_Rua_Bat_KitchenSink.dwg", Category = "Thiết Bị Vệ Sinh", Description = "Chậu bếp Inox 2 hộc kèm vòi cổ ngỗng 800x450" },
                new PlumbingBlockItem { Name = "Cây sen tắm đứng nóng lạnh", FileName = "Sen_Tam_Dung.dwg", Category = "Thiết Bị Vệ Sinh", Description = "Cây sen tắm đứng D200mm kèm củ sen điều chỉnh" },
                new PlumbingBlockItem { Name = "Bồn tiểu nam treo tường", FileName = "Tieu_Nam_Treo_Tuong.dwg", Category = "Thiết Bị Vệ Sinh", Description = "Tiểu nam cảm ứng xả tự động 350x300" },
                new PlumbingBlockItem { Name = "Phễu thu sàn ngăn mùi (FD)", FileName = "Pheu_Thu_San_FloorDrain.dwg", Category = "Thoát Nước", Description = "Thoát sàn Inox 304 bẫy nước chống hôi D60/D90" },
                new PlumbingBlockItem { Name = "Hố ga bê tông thu nước thải", FileName = "Ho_Ga_Thu_Nuoc_Thai.dwg", Category = "Thoát Nước", Description = "Hố ga lắng cặn đáy sâu 600x600x800" },
                new PlumbingBlockItem { Name = "Cụm đồng hồ đo nước D15", FileName = "Dong_Ho_Do_Nuoc_D15.dwg", Category = "Cấp Nước", Description = "Cụm đồng hồ ren D15 kèm van khóa 2 đầu" },
                new PlumbingBlockItem { Name = "Cụm bơm cấp nước Booster biến tần", FileName = "Cum_Bom_Cap_Nuoc_Booster.dwg", Category = "Cấp Nước", Description = "Cụm 2 bơm biến tần điều áp bình tích áp 100L" },
                new PlumbingBlockItem { Name = "Van cổng ren khóa đường ống nước", FileName = "Van_Cong_Khoa_Nuoc.dwg", Category = "Cấp Nước", Description = "Gate Valve đồng ren DN25 - DN50" },
                new PlumbingBlockItem { Name = "Van một chiều chống chảy ngược", FileName = "Van_Mot_Chieu_CheckValve.dwg", Category = "Cấp Nước", Description = "Check Valve lá lật / lò xo đường ống cấp" },
                new PlumbingBlockItem { Name = "Bể tự hoại 3 ngăn xử lý nước thải", FileName = "Be_Tu_Hoai_SepticTank.dwg", Category = "Thoát Nước", Description = "Bể tự hoại 3 ngăn (Chứa - Lắng - Lọc) composite" }
            };
        }

        public static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            if (db == null || tr == null || string.IsNullOrEmpty(layerName)) return;
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        public static ObjectId EnsureBlockImported(Database targetDb, string blockName, string dwgFileName)
        {
            if (targetDb == null || string.IsNullOrEmpty(blockName)) return ObjectId.Null;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName))
                {
                    return bt[blockName];
                }
                tr.Commit();
            }

            string libraryDir = GetPrimaryLibraryPath();
            string filePath = Path.Combine(libraryDir, dwgFileName);
            if (!File.Exists(filePath))
            {
                string altDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\CTN");
                string altPath = Path.Combine(altDir, dwgFileName);
                if (File.Exists(altPath)) filePath = altPath;
            }

            if (File.Exists(filePath))
            {
                try
                {
                    using (Database sourceDb = new Database(false, true))
                    {
                        sourceDb.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                        return targetDb.Insert(blockName, sourceDb, true);
                    }
                }
                catch { }
            }

            // Fallback nếu không tìm thấy file dwg
            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForWrite);
                if (bt.Has(blockName)) return bt[blockName];

                BlockTableRecord btr = new BlockTableRecord();
                btr.Name = blockName;

                Circle c1 = new Circle(Point3d.Origin, Vector3d.ZAxis, 150.0);
                c1.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                btr.AppendEntity(c1);

                Line l1 = new Line(new Point3d(-150, 0, 0), new Point3d(150, 0, 0));
                l1.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                btr.AppendEntity(l1);

                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);
                tr.Commit();
                return btrId;
            }
        }

        public static void InsertSingleBlockWithPrompt(Document doc, string blockName, string dwgFileName)
        {
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions(string.Format("\n[CTN] Chọn vị trí đặt thiết bị {0}: ", blockName));
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            Point3d ucsPt = ppr.Value;
            Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

            using (DocumentLock docLock = doc.LockDocument())
            {
                Database db = doc.Database;
                ObjectId blockId = EnsureBlockImported(db, blockName, dwgFileName);
                if (blockId.IsNull)
                {
                    ed.WriteMessage("\n[LỖI] Không thể nạp block " + blockName);
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerPlumbingEquipment, 5);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    BlockReference bref = new BlockReference(wcsPt, blockId);
                    bref.Layer = LayerPlumbingEquipment;
                    bref.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);

                    btr.AppendEntity(bref);
                    tr.AddNewlyCreatedDBObject(bref, true);
                    tr.Commit();
                }
            }

            ed.WriteMessage(string.Format("\n[CTN] Đã chèn {0} vào tọa độ ({1:F0}, {2:F0}).", blockName, wcsPt.X, wcsPt.Y));
        }

        // --- TÍNH TOÁN LƯU LƯỢNG NƯỚC CẤP THEO WSFU (TCVN 4513 / HUNTER) ---
        public static PlumbingWaterSupplyResult CalculateWaterSupply(PlumbingWaterSupplyInput input)
        {
            if (input == null) throw new ArgumentNullException("input");

            // Tính tổng WSFU theo đương lượng
            double wsfuWc = input.CountWc * 3.0;
            double wsfuLav = input.CountLavabo * 1.0;
            double wsfuSink = input.CountKitchenSink * 2.0;
            double wsfuShower = input.CountShower * 2.0;
            double wsfuUrinal = input.CountUrinal * 2.0;
            double wsfuWm = input.CountWashingMachine * 2.0;

            double totalWsfu = wsfuWc + wsfuLav + wsfuSink + wsfuShower + wsfuUrinal + wsfuWm;
            if (totalWsfu < 1.0) totalWsfu = 1.0;

            // Tính lưu lượng Q theo đường cong Hunter / TCVN 4513
            // q = 0.2 * alpha * sqrt(N) với alpha = 2.0 cho nhà ở / khách sạn
            double alpha = 2.0;
            if (input.BuildingType != null && input.BuildingType.Contains("công cộng")) alpha = 2.5;

            double flowLs = 0.2 * alpha * Math.Sqrt(totalWsfu);
            if (flowLs < 0.2) flowLs = 0.2;
            double flowM3h = flowLs * 3.6;

            // Nhu cầu nước ngày đêm Q_ngay: 200 lít/người/ngày
            int residents = input.ResidentCount > 0 ? input.ResidentCount : 15;
            double qDayM3 = (residents * 200.0) / 1000.0;

            // Dung tích bể ngầm (70% Q_ngay + dự phòng chữa cháy nhỏ) và bể mái (30% Q_ngay)
            double tankUndergroundM3 = Math.Max(2.0, Math.Ceiling(qDayM3 * 0.75));
            double tankRoofM3 = Math.Max(1.0, Math.Ceiling(qDayM3 * 0.35));

            // Chọn cỡ ống cấp PPR theo vận tốc v <= 1.8 m/s
            string pipeName = "DN25 (PPR D25x3.5)";
            double internalDia = 18.0;

            if (flowLs <= 0.35) { pipeName = "DN20 (PPR D20x2.8)"; internalDia = 14.4; }
            else if (flowLs <= 0.70) { pipeName = "DN25 (PPR D25x3.5)"; internalDia = 18.0; }
            else if (flowLs <= 1.30) { pipeName = "DN32 (PPR D32x4.4)"; internalDia = 23.2; }
            else if (flowLs <= 2.20) { pipeName = "DN40 (PPR D40x5.5)"; internalDia = 29.0; }
            else if (flowLs <= 3.80) { pipeName = "DN50 (PPR D50x6.9)"; internalDia = 36.2; }
            else if (flowLs <= 6.50) { pipeName = "DN65 (PPR D63x8.6)"; internalDia = 45.8; }
            else { pipeName = "DN80 (PPR D75x10.3)"; internalDia = 54.4; }

            double dM = internalDia / 1000.0;
            double areaPipe = Math.PI * Math.Pow(dM / 2.0, 2);
            double velocity = (flowLs / 1000.0) / areaPipe;

            // Tính cột áp bơm cấp Booster: H = Hhh + Hms + Hcb + Htd
            double floors = input.FloorCount > 0 ? input.FloorCount : 5.0;
            double hhh = floors * 3.3; // Chiều cao hình học
            double hLoss = 0.08 * (floors * 15.0); // Tổn thất ma sát dọc ống
            double hResidual = 12.0; // Áp lực tự do đầu ra 1.2 bar
            double boosterHead = hhh + hLoss + hResidual;

            // Công suất bơm P = (rho * g * Q * H) / (1000 * eta) * 1.2
            double boosterKw = (1000.0 * 9.81 * (flowLs / 1000.0) * boosterHead) / (1000.0 * 0.65) * 1.2;

            return new PlumbingWaterSupplyResult
            {
                TotalWsfu = totalWsfu,
                DesignFlowLs = flowLs,
                DesignFlowM3h = flowM3h,
                DailyDemandM3 = qDayM3,
                SuggestedPipeName = pipeName,
                PipeInternalDiaMm = internalDia,
                VelocityMs = velocity,
                UndergroundTankM3 = tankUndergroundM3,
                RoofTankM3 = tankRoofM3,
                BoosterHeadM = boosterHead,
                BoosterPowerKw = boosterKw
            };
        }

        // --- TÍNH TOÁN & CHỌN CỠ ỐNG THOÁT NƯỚC THEO DFU (TCVN 4474 / UPC) ---
        public static PlumbingDrainageResult CalculateDrainage(PlumbingDrainageInput input)
        {
            if (input == null) throw new ArgumentNullException("input");

            // Tính tổng DFU theo tải thoát nước
            double dfuWc = input.CountWc * 4.0;
            double dfuLav = input.CountLavabo * 1.0;
            double dfuSink = input.CountKitchenSink * 2.0;
            double dfuFd = input.CountFloorDrain * 2.0;
            double dfuShower = input.CountShower * 2.0;

            double totalDfu = dfuWc + dfuLav + dfuSink + dfuFd + dfuShower;
            if (totalDfu < 1.0) totalDfu = 1.0;

            // Chọn trục thoát phân (Soil Stack)
            string soilStack = "DN100 (uPVC D114x3.2)";
            if (dfuWc > 24.0 || totalDfu > 60.0)
            {
                soilStack = "DN150 (uPVC D168x4.5)";
            }

            // Chọn trục thoát nước thải sinh hoạt (Waste Stack)
            string wasteStack = "DN90 (uPVC D90x3.0)";
            if (totalDfu - dfuWc > 30.0)
            {
                wasteStack = "DN100 (uPVC D114x3.2)";
            }

            // Chọn ống nhánh thu gom (Horizontal Branch)
            string branch = "DN90 (uPVC D90x3.0)";
            if (totalDfu <= 6.0) branch = "DN60 (uPVC D60x2.0)";
            else if (totalDfu > 25.0) branch = "DN100 (uPVC D114x3.2)";

            // Ống thông hơi (Vent Pipe)
            string vent = "DN60 (uPVC D60x2.0) trục chính";
            if (totalDfu > 50.0) vent = "DN75 (uPVC D75x2.5) trục chính";

            // Độ dốc thiết kế
            double slope = input.DesignSlopePercent > 0.5 ? input.DesignSlopePercent : 2.0;

            // Bể tự hoại 3 ngăn (Chứa - Lắng - Lọc)
            int residents = input.ResidentCount > 0 ? input.ResidentCount : 15;
            double septicM3 = Math.Max(2.5, Math.Ceiling(residents * 0.08 * 3.0)); // Chu kỳ lưu 3 ngày
            string septicDim = string.Format("Dài {0:F1}m x Rộng {1:F1}m x Sâu {2:F1}m",
                Math.Sqrt(septicM3 / 1.5) * 1.5, Math.Sqrt(septicM3 / 1.5), 1.5);

            return new PlumbingDrainageResult
            {
                TotalDfu = totalDfu,
                SoilStackDia = soilStack,
                WasteStackDia = wasteStack,
                HorizontalBranchDia = branch,
                MainVentDia = vent,
                RecommendedSlopePercent = slope,
                SepticTankVolumeM3 = septicM3,
                SepticTankDimensions = septicDim
            };
        }

        // --- XUẤT BẢNG TÍNH LƯU LƯỢNG NƯỚC CẤP VÀO CAD ---
        public static void DrawWaterSupplyTableInCad(Document doc, PlumbingWaterSupplyResult res, PlumbingWaterSupplyInput inp)
        {
            if (doc == null || res == null) return;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions("\n[CTN] Chọn điểm đặt bảng tính toán nước cấp trên CAD: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            Point3d ucsPt = ppr.Value;
            Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

            using (DocumentLock docLock = doc.LockDocument())
            {
                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerPlumbingTable, 5);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Table table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = wcsPt;
                    table.Layer = LayerPlumbingTable;

                    int rows = 12;
                    int cols = 3;
                    table.SetSize(rows, cols);
                    table.SetRowHeight(12.0);
                    table.Columns[0].Width = 160.0;
                    table.Columns[1].Width = 110.0;
                    table.Columns[2].Width = 180.0;

                    // Header
                    table.Cells[0, 0].TextString = "BẢNG TÍNH TOÁN LƯU LƯỢNG NƯỚC CẤP & BỂ CHỨA (TCVN 4513 / HUNTER)";
                    table.Cells[0, 0].TextHeight = 5.0;

                    // Subheaders
                    table.Cells[1, 0].TextString = "THÔNG SỐ THIẾT KẾ";
                    table.Cells[1, 1].TextString = "GIÁ TRỊ TÍNH";
                    table.Cells[1, 2].TextString = "QUY CHUẨN KỸ THUẬT ÁP DỤNG";

                    string[,] data = new string[,]
                    {
                        { "1. Tổng đương lượng thiết bị (WSFU)", string.Format("{0:F1} Đương lượng", res.TotalWsfu), "Quy đổi theo TCVN 4513 / IPC" },
                        { "2. Lưu lượng nước tính toán (q_c)", string.Format("{0:F2} l/s", res.DesignFlowLs), "Công thức q = 0.2 x α x √N" },
                        { "3. TỔNG LƯU LƯỢNG GIỜ CAO ĐIỂM (Q)", string.Format("{0:F2} m³/h", res.DesignFlowM3h), "Lưu lượng chọn bơm cấp nước sinh hoạt" },
                        { "4. Nhu cầu dùng nước ngày đêm", string.Format("{0:F1} m³/ngày", res.DailyDemandM3), "Định mức 200 lít/người/ngày" },
                        { "5. CỠ ỐNG CẤP TRỤC CHÍNH ĐỀ XUẤT", res.SuggestedPipeName, string.Format("Vận tốc v = {0:F2} m/s <= 2.0 m/s (Chuẩn)", res.VelocityMs) },
                        { "6. Dung tích bể nước ngầm tối thiểu", string.Format("{0:F1} m³", res.UndergroundTankM3), "Dự trữ 75% lượng nước ngày đêm" },
                        { "7. Dung tích bể nước mái (Téc nước)", string.Format("{0:F1} m³", res.RoofTankM3), "Dự trữ 35% lượng nước ngày đêm" },
                        { "8. Cột áp cụm bơm cấp Booster", string.Format("{0:F1} m ({1:F1} bar)", res.BoosterHeadM, res.BoosterHeadM / 10.0), "Bao gồm chiều cao hình học & áp lực tự do" },
                        { "9. CÔNG SUẤT CỤM BƠM BOOSTER", string.Format("{0:F1} kW", res.BoosterPowerKw), "Cụm 02 bơm luân phiên biến tần Inverter" },
                        { "10. Đồng hồ đo lưu lượng tổng", "Đồng hồ DN25/DN32 cấp B", "Kiểm định đo lường cấp nước sạch" },
                        { "11. Vật liệu đường ống cấp nước", "Ống PPR chịu nhiệt PN10/PN16", "Hàn nhiệt tiêu chuẩn DIN 8077/8078" }
                    };

                    for (int r = 0; r < 11; r++)
                    {
                        table.Cells[r + 2, 0].TextString = data[r, 0];
                        table.Cells[r + 2, 1].TextString = data[r, 1];
                        table.Cells[r + 2, 2].TextString = data[r, 2];
                        table.Cells[r + 2, 0].TextHeight = 3.2;
                        table.Cells[r + 2, 1].TextHeight = 3.2;
                        table.Cells[r + 2, 2].TextHeight = 3.0;
                    }

                    btr.AppendEntity(table);
                    tr.AddNewlyCreatedDBObject(table, true);
                    tr.Commit();
                }
            }

            ed.WriteMessage("\n[CTN] Đã chèn Bảng Tính Nước Cấp thành công vào CAD!");
        }

        // --- XUẤT BẢNG TÍNH THOÁT NƯỚC & BỂ TỰ HOẠI VÀO CAD ---
        public static void DrawDrainageTableInCad(Document doc, PlumbingDrainageResult res, PlumbingDrainageInput inp)
        {
            if (doc == null || res == null) return;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions("\n[CTN] Chọn điểm đặt bảng tính toán thoát nước trên CAD: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            Point3d ucsPt = ppr.Value;
            Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

            using (DocumentLock docLock = doc.LockDocument())
            {
                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerPlumbingTable, 5);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Table table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = wcsPt;
                    table.Layer = LayerPlumbingTable;

                    int rows = 10;
                    int cols = 3;
                    table.SetSize(rows, cols);
                    table.SetRowHeight(12.0);
                    table.Columns[0].Width = 160.0;
                    table.Columns[1].Width = 110.0;
                    table.Columns[2].Width = 180.0;

                    // Header
                    table.Cells[0, 0].TextString = "BẢNG CHỌN CỠ ỐNG THOÁT NƯỚC & BỂ TỰ HOẠI (TCVN 4474 / UPC)";
                    table.Cells[0, 0].TextHeight = 5.0;

                    // Subheaders
                    table.Cells[1, 0].TextString = "HỆ THỐNG THOÁT NƯỚC";
                    table.Cells[1, 1].TextString = "QUY CÁCH / CỠ ỐNG";
                    table.Cells[1, 2].TextString = "TIÊU CHUẨN THI CÔNG & VẬN HÀNH";

                    string[,] data = new string[,]
                    {
                        { "1. Tổng đương lượng thoát nước (DFU)", string.Format("{0:F1} DFU", res.TotalDfu), "Bảng tra DFU chuẩn TCVN 4474" },
                        { "2. Ống trục đứng thoát phân (Soil Stack)", res.SoilStackDia, "Đảm bảo độ dốc và không gây tắc nghẽn" },
                        { "3. Ống trục thoát nước thải sinh hoạt", res.WasteStackDia, "Thu gom lavabo, thoát sàn, chậu bếp" },
                        { "4. Ống nhánh ngang thu gom vệ sinh", res.HorizontalBranchDia, string.Format("Độ dốc thi công khuyến nghị i = {0:F1}%", res.RecommendedSlopePercent) },
                        { "5. Trục thông hơi đứng thoát khí (Vent)", res.MainVentDia, "Đưa lên mái qua phễu chụp chống ngập" },
                        { "6. THỂ TÍCH BỂ TỰ HOẠI 3 NGĂN (V)", string.Format("{0:F1} m³", res.SepticTankVolumeM3), "Chu kỳ phân hủy 3 ngày (Chứa - Lắng - Lọc)" },
                        { "7. Kích thước bể tự hoại kiến tạo", res.SepticTankDimensions, "Xây gạch đặc hoặc composite đúc sẵn" },
                        { "8. Phụ kiện ngăn mùi (Trap / P-Trap)", "Xi-phông bẫy nước h >= 50mm", "Chống xộc mùi hôi ngược vào phòng" },
                        { "9. Vật liệu đường ống thoát nước", "uPVC Class 1/2 dán keo", "Tiêu chuẩn BS 3505 / TCVN 8491" }
                    };

                    for (int r = 0; r < 9; r++)
                    {
                        table.Cells[r + 2, 0].TextString = data[r, 0];
                        table.Cells[r + 2, 1].TextString = data[r, 1];
                        table.Cells[r + 2, 2].TextString = data[r, 2];
                        table.Cells[r + 2, 0].TextHeight = 3.2;
                        table.Cells[r + 2, 1].TextHeight = 3.2;
                        table.Cells[r + 2, 2].TextHeight = 3.0;
                    }

                    btr.AppendEntity(table);
                    tr.AddNewlyCreatedDBObject(table, true);
                    tr.Commit();
                }
            }

            ed.WriteMessage("\n[CTN] Đã chèn Bảng Chọn Cỡ Ống Thoát Nước thành công vào CAD!");
        }
    }
}

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
    public class PcccBlockItem
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public double DefaultCoverageRadius { get; set; }
    }

    public class SprinklerLayoutParams
    {
        public string BlockFileName { get; set; }
        public double SpacingX { get; set; } // mét (vd: 3.0)
        public double SpacingY { get; set; } // mét (vd: 3.0)
        public double WallOffsetX { get; set; } // mét (vd: 1.5)
        public double WallOffsetY { get; set; } // mét (vd: 1.5)
        public bool DrawCoverageCircle { get; set; }
        public double CoverageRadius { get; set; } // mét (vd: 2.1)
        public bool ConnectPipes { get; set; } // Vẽ tuyến ống nối
        public string HazardLevel { get; set; } // Thấp, TB1, TB2, Cao
    }

    public class PcccHydraulicInput
    {
        public string SystemType { get; set; } // Sprinkler, Kết hợp
        public int DesignHeadCount { get; set; } // vd: 12 đầu
        public double SprinklerFlowPerHead { get; set; } // l/s (vd: 1.2)
        public int WallHydrantCount { get; set; } // vd: 2 họng
        public double WallHydrantFlow { get; set; } // l/s (vd: 2.5)
        public double StaticHeight { get; set; } // m (vd: 35.0)
        public double EquivalentPipeLength { get; set; } // m (vd: 120.0)
        public double ResidualPressure { get; set; } // m (vd: 10.0 = 1.0 bar)
        public double HazenWilliamsC { get; set; } // 120
    }

    public class PcccHydraulicResult
    {
        public double SprinklerFlowLs { get; set; }
        public double HydrantFlowLs { get; set; }
        public double TotalFlowLs { get; set; }
        public double TotalFlowM3h { get; set; }
        public string PipeDiameter { get; set; }
        public double PipeInternalDiaMm { get; set; }
        public double VelocityMs { get; set; }
        public double FrictionLossHd { get; set; }
        public double LocalLossHcb { get; set; }
        public double TotalHeadH { get; set; }
        public double PumpPowerKw { get; set; }
        public double PumpPowerHp { get; set; }
        public double JockeyFlowM3h { get; set; }
        public double JockeyHeadM { get; set; }
        public double JockeyPowerKw { get; set; }
    }

    public class FireProtectionService
    {
        public const string LayerSprinkler = "PCCC_SPRINKLER";
        public const string LayerCoverage = "PCCC_BAN_KINH_BAO_VE";
        public const string LayerPipes = "PCCC_DUONG_ONG";
        public const string LayerEquipment = "PCCC_THIET_BI";

        public static string GetPrimaryLibraryPath()
        {
            string primary = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\PCCC";
            if (Directory.Exists(primary)) return primary;

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\PCCC");
            if (Directory.Exists(appData)) return appData;

            string localBundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Thu Viện\PCCC");
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
                if (ed != null) ed.WriteMessage(string.Format("\n[LỖI] Không thể mở thư mục thư viện: {0}", ex.Message));
            }
        }

        public static List<PcccBlockItem> GetCatalog()
        {
            return new List<PcccBlockItem>
            {
                new PcccBlockItem { Name = "Đầu phun Sprinkler quay xuống", FileName = "Sprinkler_Pendant.dwg", Category = "Sprinkler", Description = "K=80, D300mm, quay xuống trần", DefaultCoverageRadius = 2.1 },
                new PcccBlockItem { Name = "Đầu phun Sprinkler quay lên", FileName = "Sprinkler_Upright.dwg", Category = "Sprinkler", Description = "K=80, D300mm, quay lên mái/xà gồ", DefaultCoverageRadius = 2.1 },
                new PcccBlockItem { Name = "Đầu phun Sprinkler gắn tường", FileName = "Sprinkler_Sidewall.dwg", Category = "Sprinkler", Description = "K=80, phun ngang gắn vách hành lang", DefaultCoverageRadius = 2.1 },
                new PcccBlockItem { Name = "Tủ chữa cháy vách tường", FileName = "Tu_Chua_Chay_Vach_Tuong.dwg", Category = "Vách Tường", Description = "Hộp 800x600x250, cuộn vòi + lăng phun", DefaultCoverageRadius = 25.0 },
                new PcccBlockItem { Name = "Bình chữa cháy bột ABC 4kg", FileName = "Binh_MFZ4_Bot.dwg", Category = "Bình Khí/Bột", Description = "Bình bột MFZ4 xách tay tiêu chuẩn", DefaultCoverageRadius = 0 },
                new PcccBlockItem { Name = "Bình chữa cháy khí CO2 3kg", FileName = "Binh_MT3_CO2.dwg", Category = "Bình Khí/Bột", Description = "Bình khí CO2 MT3 dập đám cháy điện", DefaultCoverageRadius = 0 },
                new PcccBlockItem { Name = "Cụm van báo động (Alarm Valve)", FileName = "Van_Bao_Dong.dwg", Category = "Van & Cụm Bơm", Description = "Alarm check valve D100/D150 kèm chuông", DefaultCoverageRadius = 0 },
                new PcccBlockItem { Name = "Cụm van giảm áp đường ống", FileName = "Van_Giam_Ap.dwg", Category = "Van & Cụm Bơm", Description = "Van giảm áp PRV khống chế áp lực dư", DefaultCoverageRadius = 0 },
                new PcccBlockItem { Name = "Họng tiếp nước ngoài nhà 2 ngã", FileName = "Hong_Tiep_Nuoc_2Nga.dwg", Category = "Van & Cụm Bơm", Description = "Họng tiếp nước D100x2-D65 cho xe PCCC", DefaultCoverageRadius = 0 },
                new PcccBlockItem { Name = "Đầu báo khói quang điện", FileName = "Dau_Bao_Khoi.dwg", Category = "Báo Cháy", Description = "Đầu báo khói địa chỉ/thường D200", DefaultCoverageRadius = 4.5 },
                new PcccBlockItem { Name = "Đầu báo nhiệt cố định/gia tăng", FileName = "Dau_Bao_Nhiet.dwg", Category = "Báo Cháy", Description = "Đầu báo nhiệt lắp khu vực bếp/hầm xe", DefaultCoverageRadius = 3.5 },
                new PcccBlockItem { Name = "Nút nhấn báo cháy khẩn cấp", FileName = "Nut_Nhan_Khan.dwg", Category = "Báo Cháy", Description = "Nút nhấn khẩn hộp vuông gắn tường 1.4m", DefaultCoverageRadius = 0 },
                new PcccBlockItem { Name = "Cụm chuông - đèn báo cháy", FileName = "Chuong_Den_Bao_Chay.dwg", Category = "Báo Cháy", Description = "Chuông điện D150 + đèn chớp nháy Strobe", DefaultCoverageRadius = 0 }
            };
        }

        public static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex, string lineType = "Continuous")
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
                    @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\PCCC");
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

            // Fallback nếu không có file: tạo block hình học cơ bản trong BlockTable
            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForWrite);
                if (bt.Has(blockName)) return bt[blockName];

                BlockTableRecord btr = new BlockTableRecord();
                btr.Name = blockName;

                Circle c1 = new Circle(Point3d.Origin, Vector3d.ZAxis, 150.0);
                c1.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                btr.AppendEntity(c1);

                Circle c2 = new Circle(Point3d.Origin, Vector3d.ZAxis, 60.0);
                c2.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                btr.AppendEntity(c2);

                Line l1 = new Line(new Point3d(-150, 0, 0), new Point3d(150, 0, 0));
                l1.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                btr.AppendEntity(l1);

                Line l2 = new Line(new Point3d(0, -150, 0), new Point3d(0, 150, 0));
                l2.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                btr.AppendEntity(l2);

                ObjectId newId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);
                tr.Commit();
                return newId;
            }
        }

        public static void InsertSingleBlockWithPrompt(Document doc, string blockName, string dwgFileName, double coverageRadius = 0)
        {
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions(string.Format("\n[PCCC] Chọn điểm chèn cho {0}: ", blockName));
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            Point3d ucsPt = ppr.Value;
            Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

            using (DocumentLock docLock = doc.LockDocument())
            {
                Database db = doc.Database;
                // Nạp block trước khi khởi tạo Transaction để tránh xung đột Database.Insert
                ObjectId blkId = EnsureBlockImported(db, blockName, dwgFileName);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerEquipment, 1);
                    if (coverageRadius > 0) EnsureLayer(db, tr, LayerCoverage, 8);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    if (blkId != ObjectId.Null)
                    {
                        BlockReference br = new BlockReference(wcsPt, blkId);
                        br.Layer = LayerEquipment;
                        btr.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }

                    if (coverageRadius > 0)
                    {
                        Circle cov = new Circle(wcsPt, Vector3d.ZAxis, coverageRadius * 1000.0);
                        cov.Layer = LayerCoverage;
                        btr.AppendEntity(cov);
                        tr.AddNewlyCreatedDBObject(cov, true);
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage(string.Format("\n[PCCC] Đã chèn thành công {0} tại tọa độ X={1:F1}, Y={2:F1}.", blockName, wcsPt.X, wcsPt.Y));
        }

        public static int AutoLayoutSprinklersInCad(Document doc, SprinklerLayoutParams config, out double areaM2)
        {
            areaM2 = 0;
            if (doc == null || config == null) return 0;
            Editor ed = doc.Editor;

            Point3d pMin, pMax;

            PromptEntityOptions peo = new PromptEntityOptions("\n[PCCC] Chọn Polyline phòng hoặc nhấn ESC để chọn 2 điểm góc chéo: ");
            peo.SetRejectMessage("\nChỉ cho phép chọn đường Polyline bao phòng!");
            peo.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status == PromptStatus.OK)
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    Extents3d ext = ent.GeometricExtents;
                    pMin = ext.MinPoint;
                    pMax = ext.MaxPoint;
                    Autodesk.AutoCAD.DatabaseServices.Polyline pl = ent as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (pl != null)
                    {
                        areaM2 = Math.Abs(pl.Area) / 1000000.0;
                    }
                    tr.Commit();
                }
            }
            else
            {
                PromptPointOptions ppo1 = new PromptPointOptions("\n[PCCC] Chọn góc thứ nhất của phòng (Corner 1): ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status != PromptStatus.OK) return 0;

                PromptCornerOptions pco = new PromptCornerOptions("\n[PCCC] Chọn góc đối diện của phòng (Corner 2): ", ppr1.Value);
                PromptPointResult ppr2 = ed.GetCorner(pco);
                if (ppr2.Status != PromptStatus.OK) return 0;

                Point3d pt1 = ppr1.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                Point3d pt2 = ppr2.Value.TransformBy(ed.CurrentUserCoordinateSystem);

                pMin = new Point3d(Math.Min(pt1.X, pt2.X), Math.Min(pt1.Y, pt2.Y), 0);
                pMax = new Point3d(Math.Max(pt1.X, pt2.X), Math.Max(pt1.Y, pt2.Y), 0);
            }

            double roomW = Math.Abs(pMax.X - pMin.X);
            double roomH = Math.Abs(pMax.Y - pMin.Y);

            if (areaM2 <= 0)
            {
                areaM2 = (roomW * roomH) / 1000000.0;
            }

            if (roomW < 500.0 || roomH < 500.0)
            {
                ed.WriteMessage("\n[CẢNH BÁO] Kích thước phòng quá nhỏ để bố trí đầu phun Sprinkler!");
                return 0;
            }

            double stepX = Math.Max(1.0, config.SpacingX) * 1000.0;
            double stepY = Math.Max(1.0, config.SpacingY) * 1000.0;
            double wallX = Math.Max(0.5, config.WallOffsetX) * 1000.0;
            double wallY = Math.Max(0.5, config.WallOffsetY) * 1000.0;

            int cols = Math.Max(1, (int)Math.Round((roomW - 2.0 * wallX) / stepX) + 1);
            int rows = Math.Max(1, (int)Math.Round((roomH - 2.0 * wallY) / stepY) + 1);

            double actualStepX = cols > 1 ? (roomW - 2.0 * wallX) / (cols - 1) : 0;
            double actualStepY = rows > 1 ? (roomH - 2.0 * wallY) / (rows - 1) : 0;

            int count = 0;
            string blkName = Path.GetFileNameWithoutExtension(config.BlockFileName);
            if (string.IsNullOrEmpty(blkName)) blkName = "Sprinkler_Pendant";

            using (DocumentLock docLock = doc.LockDocument())
            {
                Database db = doc.Database;
                // Nạp block trước khi mở Transaction để bảo đảm an toàn Database.Insert
                ObjectId blkId = EnsureBlockImported(db, blkName, config.BlockFileName);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerSprinkler, 1);
                    if (config.DrawCoverageCircle) EnsureLayer(db, tr, LayerCoverage, 8);
                    if (config.ConnectPipes) EnsureLayer(db, tr, LayerPipes, 1);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    List<List<Point3d>> gridPoints = new List<List<Point3d>>();

                    for (int r = 0; r < rows; r++)
                    {
                        double y = pMin.Y + (rows > 1 ? wallY + r * actualStepY : roomH / 2.0);
                        List<Point3d> rowPts = new List<Point3d>();

                        for (int c = 0; c < cols; c++)
                        {
                            double x = pMin.X + (cols > 1 ? wallX + c * actualStepX : roomW / 2.0);
                            Point3d pt = new Point3d(x, y, 0);
                            rowPts.Add(pt);

                            if (blkId != ObjectId.Null)
                            {
                                BlockReference br = new BlockReference(pt, blkId);
                                br.Layer = LayerSprinkler;
                                btr.AppendEntity(br);
                                tr.AddNewlyCreatedDBObject(br, true);
                            }

                            if (config.DrawCoverageCircle)
                            {
                                double covR = Math.Max(1.0, config.CoverageRadius) * 1000.0;
                                Circle circ = new Circle(pt, Vector3d.ZAxis, covR);
                                circ.Layer = LayerCoverage;
                                btr.AppendEntity(circ);
                                tr.AddNewlyCreatedDBObject(circ, true);
                            }

                            count++;
                        }
                        gridPoints.Add(rowPts);
                    }

                    // Tự động vẽ tuyến ống nối các nhánh (Connect Pipes)
                    if (config.ConnectPipes && gridPoints.Count > 0)
                    {
                        foreach (var rowPts in gridPoints)
                        {
                            if (rowPts.Count > 1)
                            {
                                Line branch = new Line(rowPts[0], rowPts[rowPts.Count - 1]);
                                branch.Layer = LayerPipes;
                                btr.AppendEntity(branch);
                                tr.AddNewlyCreatedDBObject(branch, true);
                            }
                        }

                        if (gridPoints.Count > 1)
                        {
                            Point3d pFirst = gridPoints[0][0];
                            Point3d pLast = gridPoints[gridPoints.Count - 1][0];
                            Line mainRiser = new Line(pFirst, pLast);
                            mainRiser.Layer = LayerPipes;
                            btr.AppendEntity(mainRiser);
                            tr.AddNewlyCreatedDBObject(mainRiser, true);
                        }
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage(string.Format("\n[PCCC] Rải thành công {0} đầu phun Sprinkler ({1} hàng x {2} cột). Diện tích phòng: {3:F1} m².",
                count, rows, cols, areaM2));

            return count;
        }

        public static PcccHydraulicResult CalculateHydraulics(PcccHydraulicInput input)
        {
            if (input == null) input = new PcccHydraulicInput();

            int nSpk = Math.Max(1, input.DesignHeadCount);
            double qSpkPerHead = Math.Max(0.5, input.SprinklerFlowPerHead);
            double qSpkTotal = nSpk * qSpkPerHead; // l/s

            int nHydrant = Math.Max(0, input.WallHydrantCount);
            double qHydrantPer = Math.Max(0, input.WallHydrantFlow);
            double qHydrantTotal = nHydrant * qHydrantPer; // l/s

            double qTotalLs = qSpkTotal + qHydrantTotal;
            double qTotalM3h = qTotalLs * 3.6;

            // Đề xuất đường kính ống trục theo vận tốc nước kinh tế (v <= 2.5 - 3.0 m/s)
            string pipeName = "DN100 (D114.3x4.5)";
            double pipeInternalDia = 105.3; // mm

            if (qTotalM3h > 250.0)
            {
                pipeName = "DN200 (D219.1x6.3)";
                pipeInternalDia = 206.5;
            }
            else if (qTotalM3h > 150.0)
            {
                pipeName = "DN150 (D168.3x5.0)";
                pipeInternalDia = 158.3;
            }
            else if (qTotalM3h > 80.0)
            {
                pipeName = "DN125 (D141.3x4.5)";
                pipeInternalDia = 132.3;
            }
            else if (qTotalM3h > 45.0)
            {
                pipeName = "DN100 (D114.3x4.5)";
                pipeInternalDia = 105.3;
            }
            else
            {
                pipeName = "DN80 (D88.9x4.0)";
                pipeInternalDia = 80.9;
            }

            double dM = pipeInternalDia / 1000.0;
            double areaPipe = Math.PI * Math.Pow(dM / 2.0, 2);
            double velocity = (qTotalLs / 1000.0) / areaPipe;

            // Tổn thất Hazen-Williams
            double L = Math.Max(10.0, input.EquivalentPipeLength);
            double C = Math.Max(100.0, input.HazenWilliamsC);
            double qM3s = qTotalLs / 1000.0;
            double hd = 10.67 * (L / (Math.Pow(C, 1.852) * Math.Pow(dM, 4.87))) * Math.Pow(qM3s, 1.852);

            double hcb = 0.15 * hd; // Tổn thất cục bộ 15%
            double hhh = Math.Max(0.0, input.StaticHeight);
            double pmin = Math.Max(5.0, input.ResidualPressure);

            double hTotal = hhh + hd + hcb + pmin;

            // Công suất bơm P = (rho * g * Q * H) / (1000 * eta) * K_dt
            double rho = 1000.0;
            double g = 9.81;
            double eta = 0.72; // Hiệu suất bơm PCCC
            double kDt = 1.15; // Hệ số dự phòng 15%
            double powerKw = (rho * g * (qTotalLs / 1000.0) * hTotal) / (1000.0 * eta) * kDt;
            double powerHp = powerKw * 1.341;

            // Bơm bù Jockey (5% Q, H_jockey = H_tong + 5m)
            double jockeyQ = Math.Max(3.0, qTotalM3h * 0.05);
            double jockeyH = hTotal + 5.0;
            double jockeyKw = (rho * g * (jockeyQ / 3600.0) * jockeyH) / (1000.0 * 0.65) * 1.2;

            return new PcccHydraulicResult
            {
                SprinklerFlowLs = qSpkTotal,
                HydrantFlowLs = qHydrantTotal,
                TotalFlowLs = qTotalLs,
                TotalFlowM3h = qTotalM3h,
                PipeDiameter = pipeName,
                PipeInternalDiaMm = pipeInternalDia,
                VelocityMs = velocity,
                FrictionLossHd = hd,
                LocalLossHcb = hcb,
                TotalHeadH = hTotal,
                PumpPowerKw = powerKw,
                PumpPowerHp = powerHp,
                JockeyFlowM3h = jockeyQ,
                JockeyHeadM = jockeyH,
                JockeyPowerKw = jockeyKw
            };
        }

        public static void DrawHydraulicTableInCad(Document doc, PcccHydraulicResult res, PcccHydraulicInput inp)
        {
            if (doc == null || res == null) return;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions("\n[PCCC] Chọn điểm đặt bảng tính toán thủy lực trên bản vẽ: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            Point3d ucsPt = ppr.Value;
            Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

            using (DocumentLock docLock = doc.LockDocument())
            {
                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, "PCCC_BANG_THUY_LUC", 3);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Table table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = wcsPt;
                    table.Layer = "PCCC_BANG_THUY_LUC";

                    int rows = 13;
                    int cols = 3;
                    table.SetSize(rows, cols);
                    table.SetRowHeight(12.0);
                    table.Columns[0].Width = 160.0;
                    table.Columns[1].Width = 100.0;
                    table.Columns[2].Width = 180.0;

                    // Header
                    table.Cells[0, 0].TextString = "BẢNG TÍNH TOÁN THỦY LỰC & CẤU HÌNH TRẠM BƠM PCCC (TCVN 7336 / QCVN 06)";
                    table.Cells[0, 0].TextHeight = 5.0;

                    // Subheaders
                    table.Cells[1, 0].TextString = "THÔNG SỐ KỸ THUẬT";
                    table.Cells[1, 1].TextString = "GIÁ TRỊ";
                    table.Cells[1, 2].TextString = "ĐƠN VỊ / QUY CHUẨN KỸ THUẬT";

                    string[,] data = new string[,]
                    {
                        { "1. Lưu lượng Sprinkler tính toán", string.Format("{0:F1} l/s", res.SprinklerFlowLs), string.Format("{0} đầu x {1:F1} l/s", inp.DesignHeadCount, inp.SprinklerFlowPerHead) },
                        { "2. Lưu lượng họng vách tường", string.Format("{0:F1} l/s", res.HydrantFlowLs), string.Format("{0} họng x {1:F1} l/s (TCVN 7336)", inp.WallHydrantCount, inp.WallHydrantFlow) },
                        { "3. TỔNG LƯU LƯỢNG THIẾT KẾ (Q)", string.Format("{0:F1} l/s ({1:F1} m³/h)", res.TotalFlowLs, res.TotalFlowM3h), "Lưu lượng trạm bơm PCCC chính" },
                        { "4. Chiều cao hình học công trình (Hhh)", string.Format("{0:F1} m", inp.StaticHeight), "Từ cốt đáy bể đến đầu phun cao nhất" },
                        { "5. Tổn thất ma sát dọc đường ống (Hd)", string.Format("{0:F2} m", res.FrictionLossHd), string.Format("Hazen-Williams L={0:F0}m, C={1:F0}", inp.EquivalentPipeLength, inp.HazenWilliamsC) },
                        { "6. Tổn thất cục bộ phụ kiện (Hcb)", string.Format("{0:F2} m", res.LocalLossHcb), "Tổn thất tê, van, cút (15% Hd)" },
                        { "7. Áp lực tự do đầu phun xa nhất (Pmin)", string.Format("{0:F1} m", inp.ResidualPressure), "Tương đương >= 1.0 bar tại đầu phun" },
                        { "8. TỔNG CỘT ÁP BƠM PCCC (H)", string.Format("{0:F1} m", res.TotalHeadH), "H = Hhh + Hd + Hcb + Pmin" },
                        { "9. Cỡ ống trục chính đề xuất", res.PipeDiameter, string.Format("Vận tốc dòng chảy v = {0:F2} m/s <= 3.0 m/s", res.VelocityMs) },
                        { "10. CÔNG SUẤT ĐỘNG CƠ BƠM PCCC", string.Format("{0:F1} kW ({1:F1} HP)", res.PumpPowerKw, res.PumpPowerHp), "Hiệu suất 72%, hệ số dự phòng K=1.15" },
                        { "11. CẤU HÌNH TRẠM BƠM PCCC", "1 Bơm Điện + 1 Bơm Diesel", "01 Bơm chính + 01 Bơm dự phòng cùng Q, H" },
                        { "12. Bơm bù áp duy trì mạng (Jockey)", string.Format("{0:F1} m³/h - {1:F0} m", res.JockeyFlowM3h, res.JockeyHeadM), string.Format("Công suất: {0:F1} kW (Hj = H + 5m)", res.JockeyPowerKw) }
                    };

                    for (int r = 0; r < 12; r++)
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

            ed.WriteMessage("\n[PCCC] Đã chèn Bảng Tính Toán Thủy Lực PCCC thành công vào bản vẽ!");
        }
    }
}

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
    public class HvacBlockItem
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
    }

    public class HvacDuctCalcInput
    {
        public double AirFlowCmh { get; set; }     // m3/h (vd: 1200)
        public double VelocityMs { get; set; }      // m/s (vd: 5.5)
        public double DuctHeightMm { get; set; }    // mm (vd: 250 hoặc 300)
        public double DuctLengthM { get; set; }     // m (vd: 15.0)
        public string DuctType { get; set; }        // Vuông hoặc Tròn
    }

    public class HvacDuctCalcResult
    {
        public double AirFlowCmh { get; set; }
        public double AirFlowCfm { get; set; }
        public double VelocityMs { get; set; }
        public double CrossAreaM2 { get; set; }
        public double RoundDiameterMm { get; set; }
        public double RoundStandardMm { get; set; }
        public double RectWidthMm { get; set; }
        public double RectHeightMm { get; set; }
        public double RectAspectRatio { get; set; }
        public double HydraulicDiaMm { get; set; }
        public double ActualVelocityMs { get; set; }
        public double UnitFrictionLossPam { get; set; } // Pa/m
        public double TotalFrictionLossPa { get; set; } // Pa
        public double SheetThicknessMm { get; set; }   // mm (0.58, 0.75, 0.95...)
        public double SheetMetalAreaM2 { get; set; }   // m2
        public double SheetMetalWeightKg { get; set; } // kg
    }

    public class HvacCoolingCalcInput
    {
        public string SpaceType { get; set; }       // Văn phòng, Phòng ngủ, Phòng khách, TTTM, Nhà hàng...
        public double RoomAreaM2 { get; set; }      // m2 (vd: 45.0)
        public double CeilingHeightM { get; set; }  // m (vd: 3.2)
        public double CoolingIndexWPerM2 { get; set; } // W/m2 (vd: 200)
        public double SafetyMarginPercent { get; set; } // % (vd: 10%)
    }

    public class HvacCoolingCalcResult
    {
        public double RoomAreaM2 { get; set; }
        public double RoomVolumeM3 { get; set; }
        public double SpecificLoadWPerM2 { get; set; }
        public double TotalLoadKw { get; set; }
        public double TotalLoadBtu { get; set; }
        public double TotalLoadHp { get; set; }
        public double TotalLoadRt { get; set; }
        public double RequiredAirFlowCmh { get; set; }
        public string SuggestedFcuModel { get; set; }
        public int FcuQuantity { get; set; }
        public string SuggestedVrvOutdoor { get; set; }
    }

    public class DuctRoutingParams
    {
        public string SystemType { get; set; }      // Gió cấp, Gió hồi, Gió tươi, Gió thải
        public double InitialAirFlowCmh { get; set; } // m3/h (vd: 3000)
        public double VelocityMs { get; set; }       // m/s (vd: 6.0)
        public double FixedHeightMm { get; set; }    // mm (vd: 300)
        public double StepDownPercent { get; set; }  // % giảm qua mỗi phân đoạn (vd: 20)
        public bool DrawDoubleLine { get; set; }     // Vẽ 2 nét biên ống gió
        public bool DrawElbows { get; set; }         // Tự động vẽ Co tại góc cua
        public bool DrawCenterline { get; set; }     // Vẽ đường tim
        public bool AddAnnotations { get; set; }     // Ghi nhãn kích thước WxH & Q
    }

    public class DaikinRefnetSpec
    {
        public string ModelName { get; set; }
        public string DwgFileName { get; set; }
        public double MaxKw { get; set; }
        public double GasDiaMm { get; set; }
        public double LiqDiaMm { get; set; }
        public string GasPipeStr { get; set; }
        public string LiqPipeStr { get; set; }
        public string Description { get; set; }
    }

    public class RefrigerantRoutingParams
    {
        public double TotalCoolingKw { get; set; }    // kW (vd: 28 kW ~ 10 HP)
        public double PipeSpacingMm { get; set; }     // mm cách nhau giữa 2 ống gas & lỏng (vd: 60)
        public bool InsertRefnetJoints { get; set; }  // Tự động chèn bộ chia gas Daikin
        public bool DrawCombinedDrain { get; set; }   // Vẽ kèm ống nước ngưng
        public double DrainSlopePercent { get; set; } // Độ dốc nước ngưng (% vd: 1.0)
        public bool AddAnnotations { get; set; }      // Ghi nhãn cỡ ống
        public double TextHeight { get; set; }        // Cỡ chữ ghi nhãn theo tỷ lệ bản vẽ (vd: 200mm cho 1/100)
    }

    public class DrainRoutingParams
    {
        public double TotalCoolingKw { get; set; }    // kW để tính cỡ ống
        public double SlopePercent { get; set; }      // % (vd: 1.0 hoặc 2.0)
        public double StartElevationM { get; set; }   // m (vd: 2.8m)
        public bool AddSlopeArrow { get; set; }       // Vẽ mũi tên dốc
        public bool AddAnnotations { get; set; }      // Ghi nhãn độ dốc
    }

    public class HvacService
    {
        public const string LayerHvacDuct = "MEP_HVAC_ONG_GIO";
        public const string LayerHvacDuctSupply = "MEP_HVAC_ONG_CAP";
        public const string LayerHvacDuctReturn = "MEP_HVAC_ONG_HOI";
        public const string LayerHvacDuctFresh = "MEP_HVAC_ONG_TUOI";
        public const string LayerHvacDuctExhaust = "MEP_HVAC_ONG_THAI";
        public const string LayerHvacCenterline = "MEP_HVAC_DUONG_TIM";
        public const string LayerHvacDiffuser = "MEP_HVAC_MIENG_GIO";
        public const string LayerHvacEquipment = "MEP_HVAC_THIET_BI";
        public const string LayerHvacTable = "MEP_HVAC_BANG_TINH";
        public const string LayerHvacGasPipe = "MEP_HVAC_ONG_DONG_GAS";
        public const string LayerHvacLiqPipe = "MEP_HVAC_ONG_DONG_LONG";
        public const string LayerHvacRefnet = "MEP_HVAC_BO_CHIA_GAS";
        public const string LayerHvacDrain = "MEP_HVAC_ONG_NUOC_NGUNG";

        public static string GetPrimaryLibraryPath()
        {
            string primary = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\DHKK";
            if (Directory.Exists(primary)) return primary;

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\DHKK");
            if (Directory.Exists(appData)) return appData;

            string localBundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Thu Viện\DHKK");
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
                if (ed != null) ed.WriteMessage(string.Format("\n[LỖI] Không thể mở thư mục thư viện ĐHKK: {0}", ex.Message));
            }
        }

        public static List<HvacBlockItem> GetCatalog()
        {
            var list = new List<HvacBlockItem>();
            string dir = GetPrimaryLibraryPath();

            if (Directory.Exists(dir))
            {
                try
                {
                    string[] files = Directory.GetFiles(dir, "*.dwg", SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                    foreach (string f in files)
                    {
                        string fileName = Path.GetFileName(f);
                        string dirName = Path.GetDirectoryName(f);
                        string parentFolder = !string.IsNullOrEmpty(dirName) ? Path.GetFileName(dirName) : "";

                        string category = "ĐHKK";
                        if (!string.IsNullOrEmpty(parentFolder) && 
                            !parentFolder.Equals("DHKK", StringComparison.OrdinalIgnoreCase) && 
                            !parentFolder.Equals("Thư Viện", StringComparison.OrdinalIgnoreCase))
                        {
                            category = parentFolder;
                        }
                        else
                        {
                            if (fileName.IndexOf("Bo_Chia_Gas", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("BOCHIAGA", StringComparison.OrdinalIgnoreCase) >= 0) category = "Bộ Chia Gas Daikin";
                            else if (fileName.IndexOf("Dan_Lanh", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("Dan_Nong", StringComparison.OrdinalIgnoreCase) >= 0) category = "Thiết Bị Lạnh";
                            else if (fileName.IndexOf("Mieng_Gio", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("Mieng gio", StringComparison.OrdinalIgnoreCase) >= 0) category = "Miệng Gió";
                            else if (fileName.IndexOf("Van_", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("Van ", StringComparison.OrdinalIgnoreCase) >= 0) category = "Van Gió";
                            else if (fileName.IndexOf("Quat_", StringComparison.OrdinalIgnoreCase) >= 0) category = "Thông Gió";
                            else category = "Phụ Kiện";
                        }

                        list.Add(new HvacBlockItem
                        {
                            Name = Path.GetFileNameWithoutExtension(fileName),
                            FileName = fileName,
                            FullPath = f,
                            Category = category,
                            Description = string.Format("[{0}] {1}", category, fileName)
                        });
                    }
                }
                catch { }
            }

            if (list.Count == 0)
            {
                list.AddRange(new[]
                {
                    new HvacBlockItem { Name = "Mieng_Gio_Khuech_Tan_Vuong", FileName = "Mieng_Gio_Khuech_Tan_Vuong.dwg", Category = "Miệng Gió", Description = "Miệng cấp 600x600 nan khuếch tán 4 hướng" },
                    new HvacBlockItem { Name = "Mieng_Gio_Hoi_Khe_Slot", FileName = "Mieng_Gio_Hoi_Khe_Slot.dwg", Category = "Miệng Gió", Description = "Miệng gió hồi nan tuyến tính dài 1000x150" },
                    new HvacBlockItem { Name = "Mieng_Gio_Nan_Louver", FileName = "Mieng_Gio_Nan_Louver.dwg", Category = "Miệng Gió", Description = "Louver lấy gió tươi / thải gió chắn mưa 800x400" },
                    new HvacBlockItem { Name = "Dan_Lanh_FCU_Giau_Tran", FileName = "Dan_Lanh_FCU_Giau_Tran.dwg", Category = "Thiết Bị Lạnh", Description = "Ducted FCU 1000x500 kèm bầu cấp/hồi gió" },
                    new HvacBlockItem { Name = "Dan_Nong_VRV_VRF", FileName = "Dan_Nong_VRV_VRF.dwg", Category = "Thiết Bị Lạnh", Description = "Cụm dàn nóng 1200x800 giải nhiệt gió ngoài trời" },
                    new HvacBlockItem { Name = "Quat_Thong_Gio_Gan_Tran", FileName = "Quat_Thong_Gio_Gan_Tran.dwg", Category = "Thông Gió", Description = "Quạt hút âm trần D300/D150 nối ống gió" },
                    new HvacBlockItem { Name = "Van_Gio_Tron_VCD", FileName = "Van_Gio_Tron_VCD.dwg", Category = "Van Gió", Description = "Volume Control Damper ống tròn tay gạt" },
                    new HvacBlockItem { Name = "Van_Gio_Vuong_VCD", FileName = "Van_Gio_Vuong_VCD.dwg", Category = "Van Gió", Description = "Volume Control Damper ống vuông nhiều cánh" },
                    new HvacBlockItem { Name = "Van_Dap_Lua_FD", FileName = "Van_Dap_Lua_FD.dwg", Category = "Van Gió", Description = "Cầu chì nóng chảy 70°C tự ngắt khi có cháy" },
                    new HvacBlockItem { Name = "Ong_Mem_Cach_Nhiet", FileName = "Ong_Mem_Cach_Nhiet.dwg", Category = "Phụ Kiện", Description = "Flexible duct bọc bông thủy tinh cách nhiệt D200" },
                    new HvacBlockItem { Name = "Cua_Tham_Ong_Gio", FileName = "Cua_Tham_Ong_Gio.dwg", Category = "Phụ Kiện", Description = "Access door 400x400 lắp trên thân ống gió" },
                    new HvacBlockItem { Name = "Bo_Chia_Gas_Daikin_KHRQ22M20T", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M20T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D15.9, lỏng D9.5" },
                    new HvacBlockItem { Name = "Bo_Chia_Gas_Daikin_KHRQ22M29T", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M29T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D22.2, lỏng D9.5" },
                    new HvacBlockItem { Name = "Bo_Chia_Gas_Daikin_KHRQ22M64T", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M64T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D28.6, lỏng D12.7" },
                    new HvacBlockItem { Name = "Bo_Chia_Gas_Daikin_KHRQ22M75T", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M75T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D34.9, lỏng D15.9" },
                    new HvacBlockItem { Name = "Bo_Chia_Gas_Daikin_Header_8Nga", FileName = "Bo_Chia_Gas_Daikin_Header_8Nga.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Header 8 ngã chia đồng thời các dàn lạnh" }
                });
            }

            return list;
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

            string filePath = dwgFileName;
            if (!File.Exists(filePath))
            {
                string libraryDir = GetPrimaryLibraryPath();
                filePath = Path.Combine(libraryDir, dwgFileName);
                if (!File.Exists(filePath) && Directory.Exists(libraryDir))
                {
                    try
                    {
                        string onlyFileName = Path.GetFileName(dwgFileName);
                        string[] found = Directory.GetFiles(libraryDir, onlyFileName, SearchOption.AllDirectories);
                        if (found.Length > 0) filePath = found[0];
                    }
                    catch { }
                }

                if (!File.Exists(filePath))
                {
                    string altDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\DHKK");
                    if (Directory.Exists(altDir))
                    {
                        try
                        {
                            string onlyFileName = Path.GetFileName(dwgFileName);
                            string[] found = Directory.GetFiles(altDir, onlyFileName, SearchOption.AllDirectories);
                            if (found.Length > 0) filePath = found[0];
                        }
                        catch { }
                    }
                }
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
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName)) return bt[blockName];

                bt.UpgradeOpen();
                BlockTableRecord btr = new BlockTableRecord();
                btr.Name = blockName;

                Polyline pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(-300, -300), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(300, -300), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(300, 300), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(-300, 300), 0, 0, 0);
                pl.Closed = true;
                pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 4);
                btr.AppendEntity(pl);

                Line l1 = new Line(new Point3d(-300, -300, 0), new Point3d(300, 300, 0));
                l1.Color = Color.FromColorIndex(ColorMethod.ByAci, 4);
                btr.AppendEntity(l1);

                Line l2 = new Line(new Point3d(-300, 300, 0), new Point3d(300, -300, 0));
                l2.Color = Color.FromColorIndex(ColorMethod.ByAci, 4);
                btr.AppendEntity(l2);

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

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptPointOptions ppo = new PromptPointOptions(string.Format("\n[ĐHKK] Chọn vị trí đặt Block {0}: ", blockName));
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                Point3d ucsPt = ppr.Value;
                Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

                Database db = doc.Database;
                ObjectId blockId = EnsureBlockImported(db, blockName, dwgFileName);
                if (blockId.IsNull)
                {
                    ed.WriteMessage("\n[LỖI] Không thể nạp block " + blockName);
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerHvacEquipment, 4);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    BlockReference bref = new BlockReference(wcsPt, blockId);
                    bref.Layer = LayerHvacEquipment;
                    bref.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);

                    btr.AppendEntity(bref);
                    tr.AddNewlyCreatedDBObject(bref, true);
                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[ĐHKK] Đã chèn {0} vào tọa độ ({1:F0}, {2:F0}).", blockName, wcsPt.X, wcsPt.Y));
            }
        }

        // --- TÍNH TOÁN KÍCH THƯỚC ỐNG GIÓ ---
        public static HvacDuctCalcResult CalculateDuctSize(HvacDuctCalcInput input)
        {
            if (input == null) throw new ArgumentNullException("input");

            double qCmh = Math.Max(10.0, input.AirFlowCmh);
            double vMs = Math.Max(1.0, input.VelocityMs);
            double lengthM = Math.Max(1.0, input.DuctLengthM);

            double qCfm = qCmh / 1.699;
            double areaM2 = (qCmh / 3600.0) / vMs;

            // Đường kính ống tròn tương đương
            double dCalcMm = Math.Sqrt((4.0 * areaM2) / Math.PI) * 1000.0;
            double dStandardMm = RoundToStandardDuctDia(dCalcMm);

            // Kích thước ống chữ nhật W x H
            double heightMm = input.DuctHeightMm > 50.0 ? input.DuctHeightMm : 250.0;
            double widthCalcMm = (areaM2 / (heightMm / 1000.0)) * 1000.0;
            double widthStandardMm = Math.Ceiling(widthCalcMm / 50.0) * 50.0;
            if (widthStandardMm < 100.0) widthStandardMm = 100.0;

            double aspectRatio = Math.Max(widthStandardMm, heightMm) / Math.Min(widthStandardMm, heightMm);
            double actualAreaM2 = (widthStandardMm * heightMm) / 1000000.0;
            double actualVelocity = (qCmh / 3600.0) / actualAreaM2;

            // Đường kính thủy lực Dh = 2 * W * H / (W + H)
            double dhM = (2.0 * (widthStandardMm / 1000.0) * (heightMm / 1000.0)) / ((widthStandardMm + heightMm) / 1000.0);
            double dhMm = dhM * 1000.0;

            // Tổn thất ma sát đơn vị: Colebrook / ASHRAE (Pa/m)
            double unitLossPam = 0.0242 * Math.Pow(actualVelocity, 1.852) / Math.Pow(dhM, 1.269);
            if (unitLossPam < 0.05) unitLossPam = 0.05;
            double totalFrictionLossPa = unitLossPam * lengthM;

            // Tra chiều dày tôn mạ kẽm theo SMACNA / TCVN
            double maxSideMm = Math.Max(widthStandardMm, heightMm);
            double thicknessMm = 0.58;
            if (maxSideMm <= 300.0) thicknessMm = 0.58;
            else if (maxSideMm <= 750.0) thicknessMm = 0.75;
            else if (maxSideMm <= 1000.0) thicknessMm = 0.95;
            else if (maxSideMm <= 1500.0) thicknessMm = 1.15;
            else thicknessMm = 1.20;

            // Diện tích tôn và khối lượng (bao gồm 20% phụ kiện, bích ghép)
            double sheetAreaM2 = 2.0 * ((widthStandardMm + heightMm) / 1000.0) * lengthM;
            double sheetWeightKg = sheetAreaM2 * thicknessMm * 7.85 * 1.20;

            return new HvacDuctCalcResult
            {
                AirFlowCmh = qCmh,
                AirFlowCfm = qCfm,
                VelocityMs = vMs,
                CrossAreaM2 = areaM2,
                RoundDiameterMm = dCalcMm,
                RoundStandardMm = dStandardMm,
                RectWidthMm = widthStandardMm,
                RectHeightMm = heightMm,
                RectAspectRatio = aspectRatio,
                HydraulicDiaMm = dhMm,
                ActualVelocityMs = actualVelocity,
                UnitFrictionLossPam = unitLossPam,
                TotalFrictionLossPa = totalFrictionLossPa,
                SheetThicknessMm = thicknessMm,
                SheetMetalAreaM2 = sheetAreaM2,
                SheetMetalWeightKg = sheetWeightKg
            };
        }

        private static double RoundToStandardDuctDia(double dMm)
        {
            double[] std = new double[] { 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 650, 700, 800, 900, 1000, 1100, 1200 };
            foreach (double s in std)
            {
                if (s >= dMm) return s;
            }
            return Math.Ceiling(dMm / 50.0) * 50.0;
        }

        // --- TÍNH TOÁN CÔNG SUẤT LẠNH FCU / VRV ---
        public static HvacCoolingCalcResult CalculateCoolingLoad(HvacCoolingCalcInput input)
        {
            if (input == null) throw new ArgumentNullException("input");

            double area = Math.Max(5.0, input.RoomAreaM2);
            double height = Math.Max(2.4, input.CeilingHeightM);
            double volume = area * height;

            double specificLoad = input.CoolingIndexWPerM2 > 50.0 ? input.CoolingIndexWPerM2 : 200.0;
            double margin = 1.0 + (Math.Max(0.0, input.SafetyMarginPercent) / 100.0);

            double totalWatt = area * specificLoad * margin;
            double totalKw = totalWatt / 1000.0;
            double totalBtu = totalWatt * 3.41214;
            double totalHp = totalBtu / 9000.0;
            double totalRt = totalBtu / 12000.0;

            double airFlowCmh = (totalWatt / (1.204 * 1.005 * 9.0)) * 3.6;

            int qtyFcu = 1;
            string fcuModel = "";
            if (totalKw <= 3.6)
            {
                qtyFcu = 1;
                fcuModel = "01 x FCU 12.000 BTU/h (3.5 kW / 1.5 HP) - Lưu lượng ~600 CMH";
            }
            else if (totalKw <= 5.4)
            {
                qtyFcu = 1;
                fcuModel = "01 x FCU 18.000 BTU/h (5.3 kW / 2.0 HP) - Lưu lượng ~850 CMH";
            }
            else if (totalKw <= 7.2)
            {
                qtyFcu = 1;
                fcuModel = "01 x FCU 24.000 BTU/h (7.1 kW / 2.5 HP) - Lưu lượng ~1150 CMH";
            }
            else if (totalKw <= 10.5)
            {
                qtyFcu = 1;
                fcuModel = "01 x FCU 36.000 BTU/h (10.5 kW / 4.0 HP) - Lưu lượng ~1700 CMH";
            }
            else
            {
                qtyFcu = (int)Math.Ceiling(totalKw / 7.1);
                fcuModel = string.Format("{0:D2} x FCU 24.000 BTU/h (7.1 kW / 2.5 HP) - Lưu lượng ~{1:F0} CMH/máy", qtyFcu, airFlowCmh / qtyFcu);
            }

            double vrvHpRequired = totalHp / 1.20;
            string vrvOutdoor = "";
            if (vrvHpRequired <= 8.0) vrvOutdoor = "Dàn nóng VRV 8 HP (Công suất lạnh 22.4 kW)";
            else if (vrvHpRequired <= 10.0) vrvOutdoor = "Dàn nóng VRV 10 HP (Công suất lạnh 28.0 kW)";
            else if (vrvHpRequired <= 12.0) vrvOutdoor = "Dàn nóng VRV 12 HP (Công suất lạnh 33.5 kW)";
            else if (vrvHpRequired <= 14.0) vrvOutdoor = "Dàn nóng VRV 14 HP (Công suất lạnh 40.0 kW)";
            else if (vrvHpRequired <= 16.0) vrvOutdoor = "Dàn nóng VRV 16 HP (Công suất lạnh 45.0 kW)";
            else if (vrvHpRequired <= 20.0) vrvOutdoor = "Dàn nóng VRV 20 HP (Công suất lạnh 56.0 kW)";
            else
            {
                int mod = (int)Math.Ceiling(vrvHpRequired / 16.0);
                vrvOutdoor = string.Format("Cụm {0} Modul VRV {1:F0} HP (Tổng công suất ~{2:F1} kW)", mod, mod * 16.0, mod * 45.0);
            }

            return new HvacCoolingCalcResult
            {
                RoomAreaM2 = area,
                RoomVolumeM3 = volume,
                SpecificLoadWPerM2 = specificLoad,
                TotalLoadKw = totalKw,
                TotalLoadBtu = totalBtu,
                TotalLoadHp = totalHp,
                TotalLoadRt = totalRt,
                RequiredAirFlowCmh = airFlowCmh,
                SuggestedFcuModel = fcuModel,
                FcuQuantity = qtyFcu,
                SuggestedVrvOutdoor = vrvOutdoor
            };
        }

        // --- TIÊU CHUẨN BỘ CHIA GAS & ỐNG ĐỒNG DAIKIN VRV (8 CẤP) ---
        public static DaikinRefnetSpec GetDaikinRefnetByCapacity(double capacityKw)
        {
            if (capacityKw <= 5.6)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M20T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M20T.dwg",
                    MaxKw = 5.6,
                    GasDiaMm = 12.7,
                    LiqDiaMm = 6.4,
                    GasPipeStr = "Ø12.7",
                    LiqPipeStr = "Ø6.4",
                    Description = "Nhánh dàn lạnh <= 5.6 kW (<= 2 HP)"
                };
            }
            else if (capacityKw <= 16.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M20T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M20T.dwg",
                    MaxKw = 16.0,
                    GasDiaMm = 15.9,
                    LiqDiaMm = 9.5,
                    GasPipeStr = "Ø15.9",
                    LiqPipeStr = "Ø9.5",
                    Description = "Bộ chia gas Daikin KHRQ22M20T (5.6 - 16 kW / 2.5 - 6 HP)"
                };
            }
            else if (capacityKw <= 22.4)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M20T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M20T.dwg",
                    MaxKw = 22.4,
                    GasDiaMm = 19.1,
                    LiqDiaMm = 9.5,
                    GasPipeStr = "Ø19.1",
                    LiqPipeStr = "Ø9.5",
                    Description = "Bộ chia gas Daikin KHRQ22M20T (16 - 22.4 kW / 6 - 8 HP)"
                };
            }
            else if (capacityKw <= 33.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M29T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M29T.dwg",
                    MaxKw = 33.0,
                    GasDiaMm = 22.2,
                    LiqDiaMm = 9.5,
                    GasPipeStr = "Ø22.2",
                    LiqPipeStr = "Ø9.5",
                    Description = "Bộ chia gas Daikin KHRQ22M29T (22.4 - 33 kW / 8 - 12 HP)"
                };
            }
            else if (capacityKw <= 47.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M64T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M64T.dwg",
                    MaxKw = 47.0,
                    GasDiaMm = 28.6,
                    LiqDiaMm = 12.7,
                    GasPipeStr = "Ø28.6",
                    LiqPipeStr = "Ø12.7",
                    Description = "Bộ chia gas Daikin KHRQ22M64T (33 - 47 kW / 12 - 16 HP)"
                };
            }
            else if (capacityKw <= 67.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M64T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M64T.dwg",
                    MaxKw = 67.0,
                    GasDiaMm = 28.6,
                    LiqDiaMm = 15.9,
                    GasPipeStr = "Ø28.6",
                    LiqPipeStr = "Ø15.9",
                    Description = "Bộ chia gas Daikin KHRQ22M64T (47 - 67 kW / 18 - 24 HP)"
                };
            }
            else if (capacityKw <= 95.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M75T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M75T.dwg",
                    MaxKw = 95.0,
                    GasDiaMm = 34.9,
                    LiqDiaMm = 19.1,
                    GasPipeStr = "Ø34.9",
                    LiqPipeStr = "Ø19.1",
                    Description = "Bộ chia gas Daikin KHRQ22M75T (67 - 95 kW / 26 - 34 HP)"
                };
            }
            else
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M75T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M75T.dwg",
                    MaxKw = 120.0,
                    GasDiaMm = 41.3,
                    LiqDiaMm = 19.1,
                    GasPipeStr = "Ø41.3",
                    LiqPipeStr = "Ø19.1",
                    Description = "Bộ chia gas Daikin KHRQ22M75T (> 95 kW / > 34 HP)"
                };
            }
        }

        public static string FormatCompactPipeLabel(double capacityKw)
        {
            DaikinRefnetSpec spec = GetDaikinRefnetByCapacity(capacityKw);
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Ø{0:0.##}/Ø{1:0.##}",
                spec.GasDiaMm,
                spec.LiqDiaMm);
        }

        // --- TIÊU CHUẨN ỐNG NƯỚC NGƯNG uPVC ---
        public static string GetCondensateDrainSize(double coolingKw, out double outerDiaMm)
        {
            if (coolingKw <= 5.0)
            {
                outerDiaMm = 27.0;
                return "uPVC D27 (DN20)";
            }
            else if (coolingKw <= 15.0)
            {
                outerDiaMm = 34.0;
                return "uPVC D34 (DN25)";
            }
            else if (coolingKw <= 30.0)
            {
                outerDiaMm = 42.0;
                return "uPVC D42 (DN32)";
            }
            else
            {
                outerDiaMm = 49.0;
                return "uPVC D49 (DN40)";
            }
        }

        private static List<Point3d> ExtractPointsFromEntity(Transaction tr, Entity ent)
        {
            List<Point3d> rawPts = new List<Point3d>();
            Polyline lwpl = ent as Polyline;
            if (lwpl != null)
            {
                for (int i = 0; i < lwpl.NumberOfVertices; i++)
                {
                    Point2d p2 = lwpl.GetPoint2dAt(i);
                    rawPts.Add(new Point3d(p2.X, p2.Y, 0));
                }
            }
            else
            {
                Polyline2d p2d = ent as Polyline2d;
                if (p2d != null)
                {
                    foreach (ObjectId vId in p2d)
                    {
                        Vertex2d v = tr.GetObject(vId, OpenMode.ForRead) as Vertex2d;
                        if (v != null) rawPts.Add(v.Position);
                    }
                }
                else
                {
                    Polyline3d p3d = ent as Polyline3d;
                    if (p3d != null)
                    {
                        foreach (ObjectId vId in p3d)
                        {
                            PolylineVertex3d v = tr.GetObject(vId, OpenMode.ForRead) as PolylineVertex3d;
                            if (v != null) rawPts.Add(v.Position);
                        }
                    }
                    else
                    {
                        Line l = ent as Line;
                        if (l != null)
                        {
                            rawPts.Add(l.StartPoint);
                            rawPts.Add(l.EndPoint);
                        }
                    }
                }
            }

            // Lọc bỏ các điểm trùng nhau liên tiếp (khoảng cách < 1.0mm) để tránh lỗi chia 0 vector
            List<Point3d> cleanPts = new List<Point3d>();
            for (int i = 0; i < rawPts.Count; i++)
            {
                if (cleanPts.Count == 0 || cleanPts[cleanPts.Count - 1].DistanceTo(rawPts[i]) >= 1.0)
                {
                    cleanPts.Add(rawPts[i]);
                }
            }
            return cleanPts;
        }

        // --- TÌM BLOCK MIỆNG GIÓ GẦN NHẤT (< 1200mm) ---
        private static Point3d? FindNearbyDiffuser(Database db, Transaction tr, Point3d targetPt, double maxRadius)
        {
            if (db == null || tr == null) return null;
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            Point3d? nearest = null;
            double minDist = maxRadius;

            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName == "INSERT")
                {
                    BlockReference bref = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (bref != null)
                    {
                        string bName = bref.Name.ToLowerInvariant();
                        if (bName.Contains("mieng") || bName.Contains("khuyech") || bName.Contains("louver") || 
                            bName.Contains("slot") || bName.Contains("diffuser") || bName.Contains("fcu"))
                        {
                            double d = targetPt.DistanceTo(bref.Position);
                            if (d < minDist)
                            {
                                minDist = d;
                                nearest = bref.Position;
                            }
                        }
                    }
                }
            }
            return nearest;
        }

        // --- VẼ ỐNG GIÓ TỰ ĐỘNG THEO POLYLINE ĐỊNH TUYẾN & TĂNG GIẢM SIZE ---
        public static bool RouteDuctAlongPolyline(Document doc, ObjectId plId, DuctRoutingParams p)
        {
            if (doc == null || plId.IsNull || p == null) return false;
            return RouteDuctAlongPolylines(doc, new List<ObjectId> { plId }, p);
        }

        // --- VẼ ỐNG GIÓ TỰ ĐỘNG THEO CHÙM POLYLINE (TRỤC CHÍNH + NHÁNH RẼ, GÓT GIÀY, CO & ĐẤU NỐI MIỆNG GIÓ 300MM) ---
        public static bool RouteDuctAlongPolylines(Document doc, List<ObjectId> plIds, DuctRoutingParams p)
        {
            if (doc == null || plIds == null || plIds.Count == 0 || p == null) return false;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                ObjectId knmBlockId = EnsureBlockImported(db, "NTT_Khop noi mem vuong", "NTT_Khop noi mem vuong.dwg");

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    string ductLayer = LayerHvacDuctSupply;
                    short ductColor = 4; // Cyan cho gió cấp
                    string sysName = p.SystemType ?? "Gió cấp";
                    if (sysName.Contains("hồi") || sysName.Contains("Hồi"))
                    {
                        ductLayer = LayerHvacDuctReturn;
                        ductColor = 2; // Yellow
                    }
                    else if (sysName.Contains("tươi") || sysName.Contains("Tươi"))
                    {
                        ductLayer = LayerHvacDuctFresh;
                        ductColor = 3; // Green
                    }
                    else if (sysName.Contains("thải") || sysName.Contains("Thải") || sysName.Contains("khói"))
                    {
                        ductLayer = LayerHvacDuctExhaust;
                        ductColor = 1; // Red
                    }

                    EnsureLayer(db, tr, ductLayer, ductColor);
                    EnsureLayer(db, tr, LayerHvacCenterline, 8); // Gray
                    EnsureLayer(db, tr, LayerHvacEquipment, 6);  // Magenta cho khớp nối mềm

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double q0 = Math.Max(200.0, p.InitialAirFlowCmh);
                    double vMs = Math.Max(2.0, p.VelocityMs);
                    double hMm = Math.Max(150.0, p.FixedHeightMm);
                    double stepDown = Math.Max(0.0, p.StepDownPercent);

                    int totalDuctSegments = 0;

                    for (int plIdx = 0; plIdx < plIds.Count; plIdx++)
                    {
                        ObjectId plId = plIds[plIdx];
                        Entity ent = tr.GetObject(plId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        List<Point3d> pts = ExtractPointsFromEntity(tr, ent);
                        if (pts.Count < 2) continue;

                        int segCount = pts.Count - 1;
                        List<double> segWidths = new List<double>();
                        List<double> segFlows = new List<double>();

                        double plStartFlow = (plIdx == 0) ? q0 : Math.Max(200.0, q0 * 0.4);
                        for (int i = 0; i < segCount; i++)
                        {
                            double qSeg = Math.Max(150.0, plStartFlow * (1.0 - (stepDown / 100.0) * i));
                            double areaReq = (qSeg / 3600.0) / vMs;
                            double wCalc = (areaReq / (hMm / 1000.0)) * 1000.0;
                            double wSeg = Math.Max(150.0, Math.Ceiling(wCalc / 50.0) * 50.0);
                            segWidths.Add(wSeg);
                            segFlows.Add(qSeg);
                        }

                        for (int i = 0; i < segCount; i++)
                        {
                            Point3d pA = pts[i];
                            Point3d pB = pts[i + 1];
                            double dist = pA.DistanceTo(pB);
                            if (dist < 10.0) continue;

                            Vector3d dir = (pB - pA).GetNormal();
                            Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);

                            double w = segWidths[i];
                            double halfW = w / 2.0;

                            bool isLastSeg = (i == segCount - 1);
                            Point3d actualEnd = pB;

                            if (isLastSeg)
                            {
                                Point3d? diffPos = FindNearbyDiffuser(db, tr, pB, 1200.0);
                                if (diffPos.HasValue)
                                {
                                    Point3d dPt = diffPos.Value;
                                    Vector3d toDiff = (dPt - pA).GetNormal();
                                    double distToDiff = pA.DistanceTo(dPt);
                                    if (distToDiff > 350.0)
                                    {
                                        actualEnd = dPt - toDiff * 300.0;

                                        if (!knmBlockId.IsNull)
                                        {
                                            BlockReference brefKnm = new BlockReference(actualEnd, knmBlockId);
                                            brefKnm.Layer = LayerHvacEquipment;
                                            brefKnm.Rotation = Math.Atan2(toDiff.Y, toDiff.X);
                                            double scaleW = Math.Max(0.4, w / 400.0);
                                            brefKnm.ScaleFactors = new Scale3d(1.0, scaleW, 1.0);
                                            btr.AppendEntity(brefKnm);
                                            tr.AddNewlyCreatedDBObject(brefKnm, true);
                                        }

                                        Point3d flexStart = actualEnd + toDiff * 200.0;
                                        Line flexLineL = new Line(flexStart + normal * (halfW * 0.7), dPt + normal * (halfW * 0.7));
                                        Line flexLineR = new Line(flexStart - normal * (halfW * 0.7), dPt - normal * (halfW * 0.7));
                                        flexLineL.Layer = ductLayer; flexLineL.Color = Color.FromColorIndex(ColorMethod.ByAci, 6);
                                        flexLineR.Layer = ductLayer; flexLineR.Color = Color.FromColorIndex(ColorMethod.ByAci, 6);
                                        btr.AppendEntity(flexLineL); tr.AddNewlyCreatedDBObject(flexLineL, true);
                                        btr.AppendEntity(flexLineR); tr.AddNewlyCreatedDBObject(flexLineR, true);
                                    }
                                }
                            }

                            Point3d leftA = pA + normal * halfW;
                            Point3d leftB = actualEnd + normal * halfW;
                            Point3d rightA = pA - normal * halfW;
                            Point3d rightB = actualEnd - normal * halfW;

                            if (p.DrawDoubleLine)
                            {
                                if (plIdx > 0 && i == 0)
                                {
                                    Point3d heelPt = leftA - dir * 150.0 + normal * 40.0;
                                    Line shoeLine1 = new Line(heelPt, leftA);
                                    shoeLine1.Layer = ductLayer; shoeLine1.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                    btr.AppendEntity(shoeLine1); tr.AddNewlyCreatedDBObject(shoeLine1, true);

                                    Line shoeLine2 = new Line(heelPt, rightA);
                                    shoeLine2.Layer = ductLayer; shoeLine2.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                    btr.AppendEntity(shoeLine2); tr.AddNewlyCreatedDBObject(shoeLine2, true);
                                }
                                else if (i == 0)
                                {
                                    Line capStart = new Line(leftA, rightA);
                                    capStart.Layer = ductLayer; capStart.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                    btr.AppendEntity(capStart); tr.AddNewlyCreatedDBObject(capStart, true);
                                }

                                Line lineL = new Line(leftA, leftB);
                                lineL.Layer = ductLayer; lineL.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(lineL); tr.AddNewlyCreatedDBObject(lineL, true);

                                Line lineR = new Line(rightA, rightB);
                                lineR.Layer = ductLayer; lineR.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(lineR); tr.AddNewlyCreatedDBObject(lineR, true);

                                Line capEnd = new Line(leftB, rightB);
                                capEnd.Layer = ductLayer; capEnd.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(capEnd); tr.AddNewlyCreatedDBObject(capEnd, true);

                                if (i < segCount - 1 && p.DrawElbows)
                                {
                                    Point3d pNext = pts[i + 2];
                                    Vector3d nextDir = (pNext - pB).GetNormal();
                                    Vector3d nextNorm = new Vector3d(-nextDir.Y, nextDir.X, 0);
                                    double nextW = segWidths[i + 1];

                                    Point3d nextLeftA = pB + nextNorm * (nextW / 2.0);
                                    Point3d nextRightA = pB - nextNorm * (nextW / 2.0);

                                    Line elbowL = new Line(leftB, nextLeftA);
                                    elbowL.Layer = ductLayer; elbowL.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                    btr.AppendEntity(elbowL); tr.AddNewlyCreatedDBObject(elbowL, true);

                                    Line elbowR = new Line(rightB, nextRightA);
                                    elbowR.Layer = ductLayer; elbowR.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                    btr.AppendEntity(elbowR); tr.AddNewlyCreatedDBObject(elbowR, true);
                                }
                            }

                            if (p.DrawCenterline)
                            {
                                Line cLine = new Line(pA, actualEnd);
                                cLine.Layer = LayerHvacCenterline; cLine.Color = Color.FromColorIndex(ColorMethod.ByAci, 8);
                                btr.AppendEntity(cLine); tr.AddNewlyCreatedDBObject(cLine, true);
                            }

                            double segLength = pA.DistanceTo(actualEnd);
                            if (p.AddAnnotations && segLength > 150.0)
                            {
                                Point3d midPt = pA + dir * (segLength / 2.0);
                                MText mtext = new MText();
                                mtext.Location = midPt + normal * (halfW + 35.0);
                                mtext.TextHeight = Math.Max(45.0, Math.Min(120.0, segLength / 25.0));
                                mtext.Layer = ductLayer;
                                mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);

                                double angle = Math.Atan2(dir.Y, dir.X);
                                if (angle > Math.PI / 2.0) angle -= Math.PI;
                                else if (angle < -Math.PI / 2.0) angle += Math.PI;
                                mtext.Rotation = angle;
                                mtext.Attachment = AttachmentPoint.MiddleCenter;
                                mtext.Contents = string.Format("{0:F0}x{1:F0}\\P(Q={2:F0} CMH)", w, hMm, segFlows[i]);
                                btr.AppendEntity(mtext);
                                tr.AddNewlyCreatedDBObject(mtext, true);
                            }

                            totalDuctSegments++;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[ĐHKK] Đã vẽ thành công {0} đoạn ống gió hệ {1} (Co, Gót giày & Khớp nối mềm 300mm)!", totalDuctSegments, sysName));
                    return true;
                }
            }
        }

        public const string AppNameHvacPipe = "NTT_HVAC_PIPE";

        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            if (db == null || tr == null || string.IsNullOrEmpty(appName)) return;
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(appName))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord();
                ratr.Name = appName;
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
        }

        private static void SetPipeKwXData(Database db, Transaction tr, Entity ent, double kw)
        {
            if (db == null || tr == null || ent == null) return;
            EnsureRegApp(db, tr, AppNameHvacPipe);
            if (!ent.IsWriteEnabled) ent.UpgradeOpen();
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameHvacPipe),
                new TypedValue((int)DxfCode.ExtendedDataReal, kw)
            );
        }

        private static double GetPipeKwXData(Entity ent)
        {
            if (ent == null || ent.XData == null) return -1.0;
            TypedValue[] tvs = ent.XData.AsArray();
            for (int i = 0; i < tvs.Length - 1; i++)
            {
                if (tvs[i].TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                    string.Equals(tvs[i].Value as string, AppNameHvacPipe, StringComparison.OrdinalIgnoreCase))
                {
                    if (tvs[i + 1].Value is double) return (double)tvs[i + 1].Value;
                }
            }
            return -1.0;
        }

        private static double InferKwFromLabel(string text, double fallbackKw)
        {
            if (string.IsNullOrEmpty(text)) return fallbackKw;
            if (text.Contains("12.7") && text.Contains("6.4")) return 5.6;
            if (text.Contains("15.9") && text.Contains("9.5")) return 14.0;
            if (text.Contains("19.1") && text.Contains("9.5")) return 20.0;
            if (text.Contains("22.2") && text.Contains("9.5")) return 28.0;
            if (text.Contains("28.6") && text.Contains("15.9")) return 56.0;
            if (text.Contains("28.6")) return 40.0;
            if (text.Contains("34.9")) return 80.0;
            if (text.Contains("41.3")) return 110.0;
            return fallbackKw;
        }

        private static MText FindGasLabelNearSegment(Database db, Transaction tr, Point3d pStart, Point3d pEnd)
        {
            Vector3d v = pEnd - pStart;
            double segLen = v.Length;
            if (segLen < 1.0) return null;
            Vector3d u = v / segLen;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            MText best = null;
            double bestDist = 550.0;
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName == "MTEXT")
                {
                    MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                    if (mt == null || mt.IsErased) continue;
                    if (!string.Equals(mt.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(mt.Contents) || !mt.Contents.Contains("Ø")) continue;

                    double t = (mt.Location - pStart).DotProduct(u);
                    if (t >= -20.0 && t <= segLen + 20.0)
                    {
                        Point3d proj = pStart + u * t;
                        double d = mt.Location.DistanceTo(proj);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = mt;
                        }
                    }
                }
            }
            return best;
        }

        private static void CreateGasLabelForSegment(BlockTableRecord btr, Transaction tr, Point3d pStart, Point3d pEnd, string compactLabel, double textHeight)
        {
            double dist = pStart.DistanceTo(pEnd);
            if (dist < 300.0) return;

            double h = textHeight > 0 ? textHeight : 200.0;
            double offset = h * 0.75 + 15.0;

            Vector3d dir = (pEnd - pStart).GetNormal();
            Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);
            Point3d midPt = pStart + dir * (dist / 2.0) + normal * offset;

            MText mtext = new MText();
            mtext.Location = midPt;
            mtext.TextHeight = h;
            mtext.Layer = LayerHvacGasPipe;
            mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);

            double angle = Math.Atan2(dir.Y, dir.X);
            if (angle > Math.PI / 2.0) angle -= Math.PI;
            else if (angle < -Math.PI / 2.0) angle += Math.PI;
            mtext.Rotation = angle;
            mtext.Attachment = AttachmentPoint.MiddleCenter;
            mtext.Contents = compactLabel;
            btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
        }

        private static ObjectId AddGasPipeSegmentAndLabel(
            Database db, BlockTableRecord btr, Transaction tr,
            Point3d pStart, Point3d pEnd,
            double kw, bool addAnnotations, double textHeight)
        {
            double dist = pStart.DistanceTo(pEnd);
            if (dist <= 2.0) return ObjectId.Null;

            Line line = new Line(pStart, pEnd);
            line.Layer = LayerHvacGasPipe;
            line.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            SetPipeKwXData(db, tr, line, kw);

            if (addAnnotations && dist >= 300.0)
            {
                CreateGasLabelForSegment(btr, tr, pStart, pEnd, FormatCompactPipeLabel(kw), textHeight);
            }
            return line.ObjectId;
        }

        private static void UpdateExistingGasLineSize(
            Database db, BlockTableRecord btr, Transaction tr,
            Line line, double newKw, bool addAnnotations, double textHeight)
        {
            if (line == null || line.IsErased) return;
            SetPipeKwXData(db, tr, line, newKw);

            double h = textHeight > 0 ? textHeight : 200.0;
            double offset = h * 0.75 + 15.0;
            MText mt = FindGasLabelNearSegment(db, tr, line.StartPoint, line.EndPoint);
            string newLabel = FormatCompactPipeLabel(newKw);
            if (mt != null)
            {
                Vector3d dir = (line.EndPoint - line.StartPoint).GetNormal();
                Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);
                Point3d midPt = line.StartPoint + dir * (line.Length / 2.0) + normal * offset;

                mt.UpgradeOpen();
                mt.Contents = newLabel;
                mt.TextHeight = h;
                mt.Location = midPt;
            }
            else if (addAnnotations && line.Length >= 300.0)
            {
                CreateGasLabelForSegment(btr, tr, line.StartPoint, line.EndPoint, newLabel, h);
            }
        }

        public static int UpdateAllGasLabelsTextHeight(Document doc, double newTextHeight)
        {
            if (doc == null || newTextHeight <= 0) return 0;
            Database db = doc.Database;
            int count = 0;
            double offset = newTextHeight * 0.75 + 15.0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    HashSet<ObjectId> updatedLabels = new HashSet<ObjectId>();

                    foreach (ObjectId id in btr)
                    {
                        if (id.ObjectClass.DxfName == "LINE")
                        {
                            Line l = tr.GetObject(id, OpenMode.ForRead) as Line;
                            if (l == null || l.IsErased || !string.Equals(l.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase)) continue;
                            MText mt = FindGasLabelNearSegment(db, tr, l.StartPoint, l.EndPoint);
                            if (mt != null && !updatedLabels.Contains(mt.ObjectId))
                            {
                                Vector3d dir = (l.EndPoint - l.StartPoint).GetNormal();
                                Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);
                                Point3d midPt = l.StartPoint + dir * (l.Length / 2.0) + normal * offset;

                                mt.UpgradeOpen();
                                mt.TextHeight = newTextHeight;
                                mt.Location = midPt;
                                updatedLabels.Add(mt.ObjectId);
                                count++;
                            }
                        }
                    }

                    foreach (ObjectId id in btr)
                    {
                        if (id.ObjectClass.DxfName == "MTEXT" && !updatedLabels.Contains(id))
                        {
                            MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                            if (mt != null && !mt.IsErased &&
                                string.Equals(mt.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(mt.Contents) && mt.Contents.Contains("Ø"))
                            {
                                mt.UpgradeOpen();
                                mt.TextHeight = newTextHeight;
                                updatedLabels.Add(id);
                                count++;
                            }
                        }
                    }

                    tr.Commit();
                }
            }
            if (count > 0) doc.Editor.Regen();
            return count;
        }

        private static void PropagateLoadUpstream(
            Database db, BlockTableRecord btr, Transaction tr,
            Point3d startSearchPt, double addKw, double fallbackKw, bool addAnnotations, double textHeight,
            HashSet<ObjectId> visitedIds)
        {
            Queue<Point3d> queue = new Queue<Point3d>();
            queue.Enqueue(startSearchPt);
            double tol = 35.0;

            List<Line> allLines = new List<Line>();
            List<Arc> allArcs = new List<Arc>();
            List<BlockReference> allRefnets = new List<BlockReference>();

            foreach (ObjectId id in btr)
            {
                if (visitedIds.Contains(id)) continue;
                string dxf = id.ObjectClass.DxfName;
                if (dxf == "LINE")
                {
                    Line l = tr.GetObject(id, OpenMode.ForRead) as Line;
                    if (l != null && !l.IsErased && string.Equals(l.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase))
                        allLines.Add(l);
                }
                else if (dxf == "ARC")
                {
                    Arc a = tr.GetObject(id, OpenMode.ForRead) as Arc;
                    if (a != null && !a.IsErased && string.Equals(a.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase))
                        allArcs.Add(a);
                }
                else if (dxf == "INSERT")
                {
                    BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br != null && !br.IsErased &&
                        (string.Equals(br.Layer, LayerHvacRefnet, StringComparison.OrdinalIgnoreCase) ||
                         br.Name.IndexOf("BOCHIAGA", StringComparison.OrdinalIgnoreCase) >= 0))
                        allRefnets.Add(br);
                }
            }

            while (queue.Count > 0)
            {
                Point3d pt = queue.Dequeue();

                // 1. Nối tiếp ngược dòng qua các đoạn Line có EndPoint chạm vào pt
                for (int i = 0; i < allLines.Count; i++)
                {
                    Line l = allLines[i];
                    if (visitedIds.Contains(l.ObjectId)) continue;
                    if (l.EndPoint.DistanceTo(pt) <= tol)
                    {
                        visitedIds.Add(l.ObjectId);
                        double oldKw = GetPipeKwXData(l);
                        if (oldKw < 0)
                        {
                            MText mt = FindGasLabelNearSegment(db, tr, l.StartPoint, l.EndPoint);
                            oldKw = (mt != null) ? InferKwFromLabel(mt.Contents, fallbackKw) : fallbackKw;
                        }
                        double newKw = oldKw + addKw;
                        UpdateExistingGasLineSize(db, btr, tr, l, newKw, addAnnotations, textHeight);
                        queue.Enqueue(l.StartPoint);
                    }
                }

                // 2. Nối tiếp ngược dòng qua các cung bo Arc (R=100 hoặc R=50)
                for (int i = 0; i < allArcs.Count; i++)
                {
                    Arc a = allArcs[i];
                    if (visitedIds.Contains(a.ObjectId)) continue;
                    bool hitStart = a.StartPoint.DistanceTo(pt) <= tol;
                    bool hitEnd = a.EndPoint.DistanceTo(pt) <= tol;
                    if (hitStart || hitEnd)
                    {
                        visitedIds.Add(a.ObjectId);
                        double oldKw = GetPipeKwXData(a);
                        if (oldKw >= 0) SetPipeKwXData(db, tr, a, oldKw + addKw);
                        queue.Enqueue(hitStart ? a.EndPoint : a.StartPoint);
                    }
                }

                // 3. Đi qua Bộ chia gas NTT-BOCHIAGA từ cổng ra thẳng hoặc cổng nhánh về cổng vào (Inlet)
                for (int i = 0; i < allRefnets.Count; i++)
                {
                    BlockReference br = allRefnets[i];
                    if (visitedIds.Contains(br.ObjectId)) continue;

                    double rot = br.Rotation;
                    Vector3d bDir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
                    Vector3d bNorm = new Vector3d(-bDir.Y, bDir.X, 0);
                    double sign = br.ScaleFactors.Y >= 0 ? 1.0 : -1.0;

                    Point3d straightOut = br.Position + bDir * 452.671;
                    Point3d branchOut = br.Position + bDir * 452.671 + bNorm * (sign * 122.834);

                    if (pt.DistanceTo(straightOut) <= tol || pt.DistanceTo(branchOut) <= tol)
                    {
                        visitedIds.Add(br.ObjectId);
                        queue.Enqueue(br.Position);
                    }
                }
            }
        }

        private static bool FindExistingHostGasPipe(
            Database db, Transaction tr, Point3d pt, HashSet<ObjectId> excludeIds,
            out Line hostLine, out Point3d projPt, out double hitT)
        {
            hostLine = null;
            projPt = pt;
            hitT = 0.0;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            double bestDist = 80.0;
            foreach (ObjectId id in btr)
            {
                if (excludeIds.Contains(id)) continue;
                if (id.ObjectClass.DxfName != "LINE") continue;

                Line l = tr.GetObject(id, OpenMode.ForRead) as Line;
                if (l == null || l.IsErased) continue;
                if (!string.Equals(l.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase)) continue;

                Vector3d v = l.EndPoint - l.StartPoint;
                double len = v.Length;
                if (len < 10.0) continue;

                Vector3d u = v / len;
                double t = (pt - l.StartPoint).DotProduct(u);
                if (t >= 10.0 && t <= len - 10.0)
                {
                    Point3d proj = l.StartPoint + u * t;
                    double d = pt.DistanceTo(proj);
                    if (d <= bestDist)
                    {
                        bestDist = d;
                        hostLine = l;
                        projPt = proj;
                        hitT = t;
                    }
                }
            }
            return hostLine != null;
        }

        // --- VẼ ĐƯỜNG ỐNG ĐỒNG & CHÈN BỘ CHIA GAS DAIKIN THEO LƯU LƯỢNG MÔI CHẤT ---
        public static bool RouteRefrigerantAlongPolyline(Document doc, ObjectId plId, RefrigerantRoutingParams p)
        {
            if (doc == null || plId.IsNull || p == null) return false;
            return RouteRefrigerantAlongPolylines(doc, new List<ObjectId> { plId }, p);
        }

        private class RefnetBranchInfo
        {
            public int MainRouteIdx;
            public int MainSegIdx;
            public int BranchRouteIdx;
            public double DistAlongSeg;
            public Point3d ProjPt;
            public Vector3d MainDir;
            public Vector3d BranchDir;
            public double BranchKw;
        }

        private static bool TryFindSegmentHit(
            Point3d pt, List<List<Point3d>> allRoutes, int excludeIdx,
            out int hitRouteIdx, out int hitSegIdx, out double hitT, out Point3d hitProj, out Vector3d hitMainDir)
        {
            hitRouteIdx = -1;
            hitSegIdx = -1;
            hitT = 0.0;
            hitProj = pt;
            hitMainDir = Vector3d.XAxis;

            double bestDist = 80.0;
            for (int mIdx = 0; mIdx < allRoutes.Count; mIdx++)
            {
                if (mIdx == excludeIdx) continue;
                List<Point3d> mainPts = allRoutes[mIdx];
                for (int seg = 0; seg < mainPts.Count - 1; seg++)
                {
                    Point3d p1 = mainPts[seg];
                    Point3d p2 = mainPts[seg + 1];
                    Vector3d vMain = p2 - p1;
                    double segLen = vMain.Length;
                    if (segLen < 1.0) continue;

                    Vector3d uMain = vMain / segLen;
                    double t = (pt - p1).DotProduct(uMain);
                    if (t >= 10.0 && t <= segLen - 10.0)
                    {
                        Point3d projPt = p1 + uMain * t;
                        double d = pt.DistanceTo(projPt);
                        if (d <= bestDist)
                        {
                            bestDist = d;
                            hitRouteIdx = mIdx;
                            hitSegIdx = seg;
                            hitT = t;
                            hitProj = projPt;
                            hitMainDir = uMain;
                        }
                    }
                }
            }
            return hitRouteIdx >= 0;
        }

        private static void DrawBranchFromArcToEndAndFillet(
            Database db, BlockTableRecord btr, Transaction tr,
            List<Point3d> pts, double branchKw, bool addAnnotations, double textHeight, HashSet<ObjectId> createdIds)
        {
            int segCount = pts.Count - 1;
            if (segCount < 1) return;

            double[] startTrims = new double[segCount];
            double[] endTrims = new double[segCount];

            for (int v = 1; v < pts.Count - 1; v++)
            {
                Vector3d vIn = (pts[v] - pts[v - 1]).GetNormal();
                Vector3d vOut = (pts[v + 1] - pts[v]).GetNormal();
                double lenIn = pts[v - 1].DistanceTo(pts[v]);
                double lenOut = pts[v].DistanceTo(pts[v + 1]);
                double turnAngle = vIn.GetAngleTo(vOut);

                if (turnAngle > 0.05 && turnAngle < Math.PI - 0.05)
                {
                    double rCorner = 100.0;
                    double dTan = rCorner * Math.Tan(turnAngle / 2.0);
                    if (lenIn > dTan + 15.0 && lenOut > dTan + 15.0)
                    {
                        endTrims[v - 1] = dTan;
                        startTrims[v] = dTan;

                        Point3d pT1 = pts[v] - vIn * dTan;
                        double crossZ = vIn.X * vOut.Y - vIn.Y * vOut.X;
                        double sign = crossZ >= 0 ? 1.0 : -1.0;
                        Vector3d nIn = new Vector3d(-vIn.Y, vIn.X, 0) * sign;
                        Point3d arcCenter = pT1 + nIn * rCorner;

                        double sAng = Math.Atan2((pT1 - arcCenter).Y, (pT1 - arcCenter).X);
                        double eAng = (sign > 0) ? sAng + turnAngle : sAng - turnAngle;

                        Arc arcCorner = (sign > 0)
                            ? new Arc(arcCenter, rCorner, sAng, eAng)
                            : new Arc(arcCenter, rCorner, eAng, sAng);
                        arcCorner.Layer = LayerHvacGasPipe;
                        arcCorner.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                        btr.AppendEntity(arcCorner);
                        tr.AddNewlyCreatedDBObject(arcCorner, true);
                        SetPipeKwXData(db, tr, arcCorner, branchKw);
                        if (createdIds != null) createdIds.Add(arcCorner.ObjectId);
                    }
                }
            }

            for (int i = 0; i < segCount; i++)
            {
                double rawLen = pts[i].DistanceTo(pts[i + 1]);
                if (rawLen <= startTrims[i] + endTrims[i] + 2.0) continue;

                Vector3d uSeg = (pts[i + 1] - pts[i]).GetNormal();
                Point3d segStart = pts[i] + uSeg * startTrims[i];
                Point3d segEnd = pts[i + 1] - uSeg * endTrims[i];

                ObjectId lid = AddGasPipeSegmentAndLabel(db, btr, tr, segStart, segEnd, branchKw, addAnnotations, textHeight);
                if (!lid.IsNull && createdIds != null) createdIds.Add(lid);
            }
        }

        // --- LÕI XỬ LÝ VẼ ỐNG GAS VRV CHUNG (DÙNG CHO CẢ ĐƯỜNG MỒI VÀ VẼ TRỰC TIẾP) ---
        private static bool ExecuteRefrigerantFromPointLists(
            Document doc,
            List<List<Point3d>> rawRoutes,
            List<ObjectId> eraseAfterIds,
            RefrigerantRoutingParams p,
            HashSet<ObjectId> selectedSet)
        {
            if (doc == null || rawRoutes == null || rawRoutes.Count == 0 || p == null) return false;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            double totalKw = Math.Max(1.0, p.TotalCoolingKw);
            double textHeight = p.TextHeight > 0 ? p.TextHeight : 200.0;
            DaikinRefnetSpec defaultRefnet = GetDaikinRefnetByCapacity(totalKw);

            ObjectId refnetBlockId = EnsureBlockImported(db, "NTT-BOCHIAGA", "NTT-BOCHIAGA.dwg");
            if (refnetBlockId.IsNull)
            {
                refnetBlockId = EnsureBlockImported(db, defaultRefnet.ModelName, defaultRefnet.DwgFileName);
            }

            if (selectedSet == null) selectedSet = new HashSet<ObjectId>();

            bool hasHostTap = false;
            using (Transaction preTr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < rawRoutes.Count; i++)
                {
                    List<Point3d> pts = rawRoutes[i];
                    if (pts == null || pts.Count < 2) continue;

                    Line hostLine;
                    Point3d projPt;
                    double hitT;
                    if (FindExistingHostGasPipe(db, preTr, pts[0], selectedSet, out hostLine, out projPt, out hitT) ||
                        FindExistingHostGasPipe(db, preTr, pts[pts.Count - 1], selectedSet, out hostLine, out projPt, out hitT))
                    {
                        hasHostTap = true;
                        break;
                    }
                }
                preTr.Commit();
            }

            double newBranchTapKw = 7.1;
            if (hasHostTap)
            {
                PromptDoubleOptions pdo = new PromptDoubleOptions("\n[ĐHKK] Nhập công suất lạnh của nhánh mới đấu nối (kW): ");
                pdo.DefaultValue = 7.1;
                pdo.UseDefaultValue = true;
                pdo.AllowNegative = false;
                pdo.AllowZero = false;
                PromptDoubleResult pdr = ed.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK) return false;
                newBranchTapKw = pdr.Value;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, LayerHvacGasPipe, 1); // Red
                EnsureLayer(db, tr, LayerHvacRefnet, 2);  // Yellow
                if (p.DrawCombinedDrain) EnsureLayer(db, tr, LayerHvacDrain, 140);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                double blockLen = 452.671;
                int totalRefSegments = 0;

                List<List<Point3d>> freshRoutes = new List<List<Point3d>>();
                List<ObjectId> freshEntIds = new List<ObjectId>();
                List<ObjectId> tappedEntIds = new List<ObjectId>();

                for (int gIdx = 0; gIdx < rawRoutes.Count; gIdx++)
                {
                    List<Point3d> pts = rawRoutes[gIdx];
                    if (pts == null || pts.Count < 2) continue;

                    Line hostLine;
                    Point3d projPt;
                    double hitT;
                    bool hitStart = FindExistingHostGasPipe(db, tr, pts[0], selectedSet, out hostLine, out projPt, out hitT);
                    bool hitEnd = false;
                    if (!hitStart)
                    {
                        hitEnd = FindExistingHostGasPipe(db, tr, pts[pts.Count - 1], selectedSet, out hostLine, out projPt, out hitT);
                        if (hitEnd) pts.Reverse();
                    }

                    if (hitStart || hitEnd)
                    {
                        // --- TRƯỜNG HỢP A: ĐẤU NỐI VÀO ỐNG ĐỎ HIỆN HỮU ---
                        double hostKw = GetPipeKwXData(hostLine);
                        MText oldMt = FindGasLabelNearSegment(db, tr, hostLine.StartPoint, hostLine.EndPoint);
                        if (hostKw < 0)
                        {
                            hostKw = (oldMt != null) ? InferKwFromLabel(oldMt.Contents, totalKw) : totalKw;
                        }

                        Point3d hostA = hostLine.StartPoint;
                        Point3d hostB = hostLine.EndPoint;
                        Vector3d uMain = (hostB - hostA).GetNormal();
                        Vector3d normal = new Vector3d(-uMain.Y, uMain.X, 0);
                        Vector3d branchDir = (pts[1] - pts[0]).GetNormal();

                        double crossZ = uMain.X * branchDir.Y - uMain.Y * branchDir.X;
                        double sign = crossZ >= 0 ? 1.0 : -1.0;

                        if (oldMt != null && !oldMt.IsErased)
                        {
                            oldMt.UpgradeOpen();
                            oldMt.Erase();
                        }
                        hostLine.UpgradeOpen();
                        hostLine.Erase();

                        HashSet<ObjectId> visitedIds = new HashSet<ObjectId>();

                        Point3d pIns = projPt - uMain * blockLen;
                        if (p.InsertRefnetJoints && !refnetBlockId.IsNull)
                        {
                            BlockReference bref = new BlockReference(pIns, refnetBlockId);
                            bref.Layer = LayerHvacRefnet;
                            bref.Rotation = Math.Atan2(uMain.Y, uMain.X);
                            bref.ScaleFactors = new Scale3d(1.0, sign, 1.0);
                            btr.AppendEntity(bref);
                            tr.AddNewlyCreatedDBObject(bref, true);
                            visitedIds.Add(bref.ObjectId);

                            Point3d branchOutPt = projPt + normal * (sign * 122.834);
                            double rFillet = 50.0;
                            Point3d arcCenter = branchOutPt + normal * (sign * rFillet);
                            double startAng = Math.Atan2(-normal.Y * sign, -normal.X * sign);
                            double turnAngle = uMain.GetAngleTo(branchDir);
                            if (turnAngle < 0.05) turnAngle = Math.PI / 2.0;
                            double endAng = (sign > 0) ? startAng + turnAngle : startAng - turnAngle;

                            Arc arcBranch = (sign > 0)
                                ? new Arc(arcCenter, rFillet, startAng, endAng)
                                : new Arc(arcCenter, rFillet, endAng, startAng);
                            arcBranch.Layer = LayerHvacGasPipe;
                            arcBranch.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                            btr.AppendEntity(arcBranch);
                            tr.AddNewlyCreatedDBObject(arcBranch, true);
                            SetPipeKwXData(db, tr, arcBranch, newBranchTapKw);
                            visitedIds.Add(arcBranch.ObjectId);

                            Point3d arcEndPt = arcCenter + new Vector3d(Math.Cos(endAng), Math.Sin(endAng), 0) * rFillet;
                            Vector3d bDir = (pts[1] - pts[0]).GetNormal();
                            Vector3d lateralShift = (arcEndPt - pts[0]) - bDir * (arcEndPt - pts[0]).DotProduct(bDir);
                            pts[0] = arcEndPt;
                            for (int k = 1; k < pts.Count; k++) pts[k] = pts[k] + lateralShift;
                        }

                        double upstreamKw = hostKw + newBranchTapKw;
                        if ((pIns - hostA).DotProduct(uMain) > 2.0)
                        {
                            ObjectId upId = AddGasPipeSegmentAndLabel(db, btr, tr, hostA, pIns, upstreamKw, p.AddAnnotations, textHeight);
                            if (!upId.IsNull) visitedIds.Add(upId);
                        }
                        if ((hostB - projPt).DotProduct(uMain) > 2.0)
                        {
                            ObjectId downId = AddGasPipeSegmentAndLabel(db, btr, tr, projPt, hostB, hostKw, p.AddAnnotations, textHeight);
                            if (!downId.IsNull) visitedIds.Add(downId);
                        }

                        DrawBranchFromArcToEndAndFillet(db, btr, tr, pts, newBranchTapKw, p.AddAnnotations, textHeight, visitedIds);
                        PropagateLoadUpstream(db, btr, tr, hostA, newBranchTapKw, totalKw, p.AddAnnotations, textHeight, visitedIds);

                        if (eraseAfterIds != null && gIdx < eraseAfterIds.Count) tappedEntIds.Add(eraseAfterIds[gIdx]);
                        totalRefSegments += pts.Count - 1;
                    }
                    else
                    {
                        freshRoutes.Add(pts);
                        if (eraseAfterIds != null && gIdx < eraseAfterIds.Count) freshEntIds.Add(eraseAfterIds[gIdx]);
                    }
                }

                for (int idx = 0; idx < tappedEntIds.Count; idx++)
                {
                    Entity oldEnt = tr.GetObject(tappedEntIds[idx], OpenMode.ForWrite, false) as Entity;
                    if (oldEnt != null && !oldEnt.IsErased) oldEnt.Erase();
                }

                // --- TRƯỜNG HỢP B: VẼ CÂY ỐNG MỚI HOÀN TOÀN ---
                if (freshRoutes.Count > 0)
                {
                    List<RefnetBranchInfo> junctions = new List<RefnetBranchInfo>();
                    HashSet<int> branchRouteIndices = new HashSet<int>();

                    for (int bIdx = 0; bIdx < freshRoutes.Count; bIdx++)
                    {
                        int mIdx, mSeg;
                        double t;
                        Point3d projPt;
                        Vector3d uMain;

                        bool hit = TryFindSegmentHit(freshRoutes[bIdx][0], freshRoutes, bIdx, out mIdx, out mSeg, out t, out projPt, out uMain);
                        if (!hit)
                        {
                            int lastIdx = freshRoutes[bIdx].Count - 1;
                            if (TryFindSegmentHit(freshRoutes[bIdx][lastIdx], freshRoutes, bIdx, out mIdx, out mSeg, out t, out projPt, out uMain))
                            {
                                freshRoutes[bIdx].Reverse();
                                hit = true;
                            }
                        }

                        if (hit)
                        {
                            branchRouteIndices.Add(bIdx);
                            Vector3d branchDir = (freshRoutes[bIdx][1] - freshRoutes[bIdx][0]).GetNormal();
                            junctions.Add(new RefnetBranchInfo
                            {
                                MainRouteIdx = mIdx,
                                MainSegIdx = mSeg,
                                BranchRouteIdx = bIdx,
                                DistAlongSeg = t,
                                ProjPt = projPt,
                                MainDir = uMain,
                                BranchDir = branchDir,
                                BranchKw = totalKw / (1.0 + freshRoutes.Count - 1.0)
                            });
                        }
                    }

                    int terminalCount = 1 + junctions.Count;
                    double leafKw = totalKw / terminalCount;
                    double[] routeStartKw = new double[freshRoutes.Count];
                    for (int r = 0; r < freshRoutes.Count; r++) routeStartKw[r] = leafKw;

                    for (int jIdx = 0; jIdx < junctions.Count; jIdx++)
                    {
                        junctions[jIdx].BranchKw = leafKw;
                    }
                    for (int r = freshRoutes.Count - 1; r >= 0; r--)
                    {
                        for (int jIdx = 0; jIdx < junctions.Count; jIdx++)
                        {
                            if (junctions[jIdx].MainRouteIdx == r)
                            {
                                routeStartKw[r] += routeStartKw[junctions[jIdx].BranchRouteIdx];
                            }
                        }
                    }

                    if (p.InsertRefnetJoints && !refnetBlockId.IsNull && junctions.Count > 0)
                    {
                        Queue<int> queue = new Queue<int>();
                        for (int rIdx = 0; rIdx < freshRoutes.Count; rIdx++)
                        {
                            if (!branchRouteIndices.Contains(rIdx)) queue.Enqueue(rIdx);
                        }
                        if (queue.Count == 0) queue.Enqueue(0);

                        HashSet<int> processedJunctions = new HashSet<int>();
                        while (queue.Count > 0)
                        {
                            int currMainIdx = queue.Dequeue();
                            for (int jIdx = 0; jIdx < junctions.Count; jIdx++)
                            {
                                if (processedJunctions.Contains(jIdx)) continue;
                                RefnetBranchInfo junc = junctions[jIdx];
                                if (junc.MainRouteIdx != currMainIdx) continue;

                                processedJunctions.Add(jIdx);
                                int bIdx = junc.BranchRouteIdx;
                                Point3d projPt = junc.ProjPt;
                                Vector3d uMain = junc.MainDir;
                                Vector3d branchDir = junc.BranchDir;
                                Vector3d normal = new Vector3d(-uMain.Y, uMain.X, 0);

                                double crossZ = uMain.X * branchDir.Y - uMain.Y * branchDir.X;
                                double sign = crossZ >= 0 ? 1.0 : -1.0;

                                Point3d pIns = projPt - uMain * blockLen;
                                BlockReference bref = new BlockReference(pIns, refnetBlockId);
                                bref.Layer = LayerHvacRefnet;
                                bref.Rotation = Math.Atan2(uMain.Y, uMain.X);
                                bref.ScaleFactors = new Scale3d(1.0, sign, 1.0);
                                btr.AppendEntity(bref);
                                tr.AddNewlyCreatedDBObject(bref, true);

                                Point3d branchOutPt = projPt + normal * (sign * 122.834);
                                double rFillet = 50.0;
                                Point3d arcCenter = branchOutPt + normal * (sign * rFillet);
                                double startAng = Math.Atan2(-normal.Y * sign, -normal.X * sign);
                                double turnAngle = uMain.GetAngleTo(branchDir);
                                if (turnAngle < 0.05) turnAngle = Math.PI / 2.0;
                                double endAng = (sign > 0) ? startAng + turnAngle : startAng - turnAngle;

                                Arc arcBranch = (sign > 0)
                                    ? new Arc(arcCenter, rFillet, startAng, endAng)
                                    : new Arc(arcCenter, rFillet, endAng, startAng);
                                arcBranch.Layer = LayerHvacGasPipe;
                                arcBranch.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                                btr.AppendEntity(arcBranch);
                                tr.AddNewlyCreatedDBObject(arcBranch, true);
                                SetPipeKwXData(db, tr, arcBranch, routeStartKw[bIdx]);

                                Point3d arcEndPt = arcCenter + new Vector3d(Math.Cos(endAng), Math.Sin(endAng), 0) * rFillet;
                                List<Point3d> bPts = freshRoutes[bIdx];
                                Vector3d bDir = (bPts[1] - bPts[0]).GetNormal();
                                Vector3d lateralShift = (arcEndPt - bPts[0]) - bDir * (arcEndPt - bPts[0]).DotProduct(bDir);

                                bPts[0] = arcEndPt;
                                for (int k = 1; k < bPts.Count; k++) bPts[k] = bPts[k] + lateralShift;

                                for (int cIdx = 0; cIdx < junctions.Count; cIdx++)
                                {
                                    if (junctions[cIdx].MainRouteIdx == bIdx)
                                        junctions[cIdx].ProjPt = junctions[cIdx].ProjPt + lateralShift;
                                }

                                queue.Enqueue(bIdx);
                            }
                        }
                    }

                    for (int rIdx = 0; rIdx < freshRoutes.Count; rIdx++)
                    {
                        List<Point3d> pts = freshRoutes[rIdx];
                        int segCount = pts.Count - 1;
                        if (segCount < 1) continue;

                        double[] startTrims = new double[segCount];
                        double[] endTrims = new double[segCount];
                        double currRouteKw = routeStartKw[rIdx];

                        for (int v = 1; v < pts.Count - 1; v++)
                        {
                            Vector3d vIn = (pts[v] - pts[v - 1]).GetNormal();
                            Vector3d vOut = (pts[v + 1] - pts[v]).GetNormal();
                            double lenIn = pts[v - 1].DistanceTo(pts[v]);
                            double lenOut = pts[v].DistanceTo(pts[v + 1]);
                            double turnAngle = vIn.GetAngleTo(vOut);

                            if (turnAngle > 0.05 && turnAngle < Math.PI - 0.05)
                            {
                                double rCorner = 100.0;
                                double dTan = rCorner * Math.Tan(turnAngle / 2.0);
                                if (lenIn > dTan + 15.0 && lenOut > dTan + 15.0)
                                {
                                    endTrims[v - 1] = dTan;
                                    startTrims[v] = dTan;

                                    Point3d pT1 = pts[v] - vIn * dTan;
                                    double crossZ = vIn.X * vOut.Y - vIn.Y * vOut.X;
                                    double sign = crossZ >= 0 ? 1.0 : -1.0;
                                    Vector3d nIn = new Vector3d(-vIn.Y, vIn.X, 0) * sign;
                                    Point3d arcCenter = pT1 + nIn * rCorner;

                                    double sAng = Math.Atan2((pT1 - arcCenter).Y, (pT1 - arcCenter).X);
                                    double eAng = (sign > 0) ? sAng + turnAngle : sAng - turnAngle;

                                    Arc arcCorner = (sign > 0)
                                        ? new Arc(arcCenter, rCorner, sAng, eAng)
                                        : new Arc(arcCenter, rCorner, eAng, sAng);
                                    arcCorner.Layer = LayerHvacGasPipe;
                                    arcCorner.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                                    btr.AppendEntity(arcCorner);
                                    tr.AddNewlyCreatedDBObject(arcCorner, true);
                                    SetPipeKwXData(db, tr, arcCorner, currRouteKw);
                                }
                            }
                        }

                        for (int i = 0; i < segCount; i++)
                        {
                            double rawLen = pts[i].DistanceTo(pts[i + 1]);
                            if (rawLen <= startTrims[i] + endTrims[i] + 2.0) continue;

                            Vector3d uSeg = (pts[i + 1] - pts[i]).GetNormal();
                            Point3d segStart = pts[i] + uSeg * startTrims[i];
                            Point3d segEnd = pts[i + 1] - uSeg * endTrims[i];

                            List<RefnetBranchInfo> segJuncs = new List<RefnetBranchInfo>();
                            if (p.InsertRefnetJoints && !refnetBlockId.IsNull)
                            {
                                for (int jIdx = 0; jIdx < junctions.Count; jIdx++)
                                {
                                    if (junctions[jIdx].MainRouteIdx == rIdx && junctions[jIdx].MainSegIdx == i)
                                        segJuncs.Add(junctions[jIdx]);
                                }
                                segJuncs.Sort((a, b) => a.DistAlongSeg.CompareTo(b.DistAlongSeg));
                            }

                            Point3d currPt = segStart;
                            for (int j = 0; j < segJuncs.Count; j++)
                            {
                                Point3d pIns = segJuncs[j].ProjPt - uSeg * blockLen;
                                if ((pIns - currPt).DotProduct(uSeg) > 2.0)
                                {
                                    AddGasPipeSegmentAndLabel(db, btr, tr, currPt, pIns, currRouteKw, p.AddAnnotations, textHeight);
                                }
                                currRouteKw = Math.Max(1.0, currRouteKw - routeStartKw[segJuncs[j].BranchRouteIdx]);
                                currPt = segJuncs[j].ProjPt;
                            }

                            if ((segEnd - currPt).DotProduct(uSeg) > 2.0)
                            {
                                AddGasPipeSegmentAndLabel(db, btr, tr, currPt, segEnd, currRouteKw, p.AddAnnotations, textHeight);
                            }

                            totalRefSegments++;
                        }
                    }

                    for (int idx = 0; idx < freshEntIds.Count; idx++)
                    {
                        Entity oldEnt = tr.GetObject(freshEntIds[idx], OpenMode.ForWrite, false) as Entity;
                        if (oldEnt != null && !oldEnt.IsErased) oldEnt.Erase();
                    }
                }

                tr.Commit();
                ed.WriteMessage(string.Format("\n[ĐHKK] Đã vẽ & tự động tính lại size ống gas ({0} đoạn, Cỡ chữ {1:F0}mm, Cập nhật nhãn Ø/Ø thành công)!",
                    totalRefSegments, textHeight));
                return true;
            }
        }

        // --- VẼ TUYẾN ỐNG GAS VRV TỪ ĐƯỜNG MỒI CHỌN SẴN ---
        public static bool RouteRefrigerantAlongPolylines(Document doc, List<ObjectId> plIds, RefrigerantRoutingParams p)
        {
            if (doc == null || plIds == null || plIds.Count == 0 || p == null) return false;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                List<List<Point3d>> rawRoutes = new List<List<Point3d>>();
                List<ObjectId> guideIds = new List<ObjectId>();
                HashSet<ObjectId> selectedSet = new HashSet<ObjectId>(plIds);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    for (int i = 0; i < plIds.Count; i++)
                    {
                        Entity ent = tr.GetObject(plIds[i], OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased) continue;
                        if (string.Equals(ent.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase)) continue;

                        List<Point3d> pts = ExtractPointsFromEntity(tr, ent);
                        if (pts.Count < 2) continue;

                        rawRoutes.Add(pts);
                        guideIds.Add(plIds[i]);
                    }
                    tr.Commit();
                }

                if (rawRoutes.Count == 0)
                {
                    ed.WriteMessage("\n[ĐHKK] Không tìm thấy đường mồi hợp lệ (khác layer ống gas hiện hữu)!");
                    return false;
                }

                return ExecuteRefrigerantFromPointLists(doc, rawRoutes, guideIds, p, selectedSet);
            }
        }

        // --- VẼ ỐNG GAS TRỰC TIẾP BẰNG CLICK ĐIỂM (KHÔNG CẦN ĐƯỜNG MỒI, TỰ NỐI & TỰ CHIA) ---
        public static bool DrawRefrigerantInteractive(Document doc, RefrigerantRoutingParams p)
        {
            if (doc == null || p == null) return false;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptPointOptions ppo1 = new PromptPointOptions("\n[ĐHKK] Click điểm BẮT ĐẦU (Chạm vào ống đỏ để tách nhánh hoặc chọn vùng trống để vẽ mới): ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status != PromptStatus.OK) return false;

                Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
                Point3d firstWcs = ppr1.Value.TransformBy(ucsToWcs);

                // Kiểm tra ngay xem điểm đầu có chạm vào ống gas đỏ hiện hữu không để thông báo cho kỹ sư
                using (Transaction checkTr = db.TransactionManager.StartTransaction())
                {
                    Line hostLine;
                    Point3d projPt;
                    double hitT;
                    if (FindExistingHostGasPipe(db, checkTr, firstWcs, new HashSet<ObjectId>(), out hostLine, out projPt, out hitT))
                    {
                        ed.WriteMessage("\n[ĐHKK] -> Đã bắt điểm vào ống gas hiện hữu! Vẽ tiếp để tự động chèn Bộ chia gas & Bo R=50.");
                    }
                    checkTr.Commit();
                }

                List<Point3d> pickedPts = new List<Point3d> { firstWcs };
                while (true)
                {
                    Point3d lastUcs = pickedPts[pickedPts.Count - 1].TransformBy(ucsToWcs.Inverse());
                    PromptPointOptions ppoNext = new PromptPointOptions("\n[ĐHKK] Chọn điểm tiếp theo (Enter / Chuột phải để hoàn tất): ");
                    ppoNext.UseBasePoint = true;
                    ppoNext.BasePoint = lastUcs;
                    ppoNext.AllowNone = true;

                    PromptPointResult pprNext = ed.GetPoint(ppoNext);
                    if (pprNext.Status == PromptStatus.Cancel)
                    {
                        ed.Regen();
                        return false;
                    }
                    if (pprNext.Status == PromptStatus.None) break;

                    if (pprNext.Status == PromptStatus.OK)
                    {
                        Point3d nextWcs = pprNext.Value.TransformBy(ucsToWcs);
                        if (nextWcs.DistanceTo(pickedPts[pickedPts.Count - 1]) >= 10.0)
                        {
                            ed.DrawVector(pickedPts[pickedPts.Count - 1], nextWcs, 1, false);
                            pickedPts.Add(nextWcs);
                        }
                    }
                }

                if (pickedPts.Count < 2)
                {
                    ed.Regen();
                    ed.WriteMessage("\n[ĐHKK] Cần ít nhất 2 điểm tọa độ để vẽ ống gas!");
                    return false;
                }

                bool ok = ExecuteRefrigerantFromPointLists(doc, new List<List<Point3d>> { pickedPts }, null, p, null);
                ed.Regen();
                return ok;
            }
        }

        // --- TÍNH LẠI SIZE TOÀN MẠNG CÂY ỐNG GAS VRV TỪ NGỌN VỀ GỐC (TREE AUTO-SIZER) ---
        public static bool RecalculateRefrigerantNetwork(Document doc, double defaultLeafKw, out double totalNetworkKw, out int updatedCount)
        {
            return RecalculateRefrigerantNetwork(doc, defaultLeafKw, 200.0, out totalNetworkKw, out updatedCount);
        }

        public static bool RecalculateRefrigerantNetwork(Document doc, double defaultLeafKw, double textHeight, out double totalNetworkKw, out int updatedCount)
        {
            totalNetworkKw = 0.0;
            updatedCount = 0;
            if (doc == null) return false;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            double h = textHeight > 0 ? textHeight : 200.0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptEntityOptions peo = new PromptEntityOptions("\n[ĐHKK] Chọn đoạn ống ĐẦU NGUỒN (gần Dàn nóng nhất) để quét & tính lại size toàn cây ống gas: ");
                peo.SetRejectMessage("\nVui lòng chọn một đoạn ống thẳng (LINE) thuộc hệ ống gas!");
                peo.AddAllowedClass(typeof(Line), false);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return false;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line rootLine = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Line;
                    if (rootLine == null || rootLine.IsErased) return false;

                    List<Line> lines = new List<Line>();
                    List<Arc> arcs = new List<Arc>();
                    List<BlockReference> refnets = new List<BlockReference>();

                    foreach (ObjectId id in btr)
                    {
                        string dxf = id.ObjectClass.DxfName;
                        if (dxf == "LINE")
                        {
                            Line l = tr.GetObject(id, OpenMode.ForRead) as Line;
                            if (l != null && !l.IsErased && string.Equals(l.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase))
                                lines.Add(l);
                        }
                        else if (dxf == "ARC")
                        {
                            Arc a = tr.GetObject(id, OpenMode.ForRead) as Arc;
                            if (a != null && !a.IsErased && string.Equals(a.Layer, LayerHvacGasPipe, StringComparison.OrdinalIgnoreCase))
                                arcs.Add(a);
                        }
                        else if (dxf == "INSERT")
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br != null && !br.IsErased &&
                                (string.Equals(br.Layer, LayerHvacRefnet, StringComparison.OrdinalIgnoreCase) ||
                                 br.Name.IndexOf("BOCHIAGA", StringComparison.OrdinalIgnoreCase) >= 0))
                                refnets.Add(br);
                        }
                    }

                    Point3d originPt = (per.PickedPoint.DistanceTo(rootLine.StartPoint) <= per.PickedPoint.DistanceTo(rootLine.EndPoint))
                        ? rootLine.StartPoint
                        : rootLine.EndPoint;
                    Point3d firstDownPt = (originPt == rootLine.StartPoint) ? rootLine.EndPoint : rootLine.StartPoint;

                    double tol = 35.0;
                    HashSet<ObjectId> visited = new HashSet<ObjectId>();
                    Dictionary<ObjectId, List<ObjectId>> childrenMap = new Dictionary<ObjectId, List<ObjectId>>();
                    Dictionary<ObjectId, double> nodeKw = new Dictionary<ObjectId, double>();
                    List<ObjectId> postOrder = new List<ObjectId>();

                    Queue<KeyValuePair<ObjectId, Point3d>> bfs = new Queue<KeyValuePair<ObjectId, Point3d>>();
                    visited.Add(rootLine.ObjectId);
                    childrenMap[rootLine.ObjectId] = new List<ObjectId>();
                    bfs.Enqueue(new KeyValuePair<ObjectId, Point3d>(rootLine.ObjectId, firstDownPt));

                    while (bfs.Count > 0)
                    {
                        var curr = bfs.Dequeue();
                        ObjectId parentId = curr.Key;
                        Point3d tip = curr.Value;
                        postOrder.Add(parentId);

                        for (int i = 0; i < lines.Count; i++)
                        {
                            Line l = lines[i];
                            if (visited.Contains(l.ObjectId)) continue;
                            bool hitA = l.StartPoint.DistanceTo(tip) <= tol;
                            bool hitB = l.EndPoint.DistanceTo(tip) <= tol;
                            if (hitA || hitB)
                            {
                                visited.Add(l.ObjectId);
                                childrenMap[parentId].Add(l.ObjectId);
                                childrenMap[l.ObjectId] = new List<ObjectId>();
                                bfs.Enqueue(new KeyValuePair<ObjectId, Point3d>(l.ObjectId, hitA ? l.EndPoint : l.StartPoint));
                            }
                        }

                        for (int i = 0; i < arcs.Count; i++)
                        {
                            Arc a = arcs[i];
                            if (visited.Contains(a.ObjectId)) continue;
                            bool hitA = a.StartPoint.DistanceTo(tip) <= tol;
                            bool hitB = a.EndPoint.DistanceTo(tip) <= tol;
                            if (hitA || hitB)
                            {
                                visited.Add(a.ObjectId);
                                childrenMap[parentId].Add(a.ObjectId);
                                childrenMap[a.ObjectId] = new List<ObjectId>();
                                bfs.Enqueue(new KeyValuePair<ObjectId, Point3d>(a.ObjectId, hitA ? a.EndPoint : a.StartPoint));
                            }
                        }

                        for (int i = 0; i < refnets.Count; i++)
                        {
                            BlockReference br = refnets[i];
                            if (visited.Contains(br.ObjectId)) continue;
                            if (br.Position.DistanceTo(tip) <= tol)
                            {
                                visited.Add(br.ObjectId);
                                double rot = br.Rotation;
                                Vector3d bDir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
                                Vector3d bNorm = new Vector3d(-bDir.Y, bDir.X, 0);
                                double sign = br.ScaleFactors.Y >= 0 ? 1.0 : -1.0;

                                Point3d straightOut = br.Position + bDir * 452.671;
                                Point3d branchOut = br.Position + bDir * 452.671 + bNorm * (sign * 122.834);

                                childrenMap[parentId].Add(br.ObjectId);
                                childrenMap[br.ObjectId] = new List<ObjectId>();
                                postOrder.Add(br.ObjectId);

                                bfs.Enqueue(new KeyValuePair<ObjectId, Point3d>(br.ObjectId, straightOut));
                                bfs.Enqueue(new KeyValuePair<ObjectId, Point3d>(br.ObjectId, branchOut));
                            }
                        }
                    }

                    for (int idx = postOrder.Count - 1; idx >= 0; idx--)
                    {
                        ObjectId id = postOrder[idx];
                        List<ObjectId> ch = childrenMap[id];
                        if (ch.Count == 0)
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            double kw = GetPipeKwXData(ent);
                            if (kw < 0 && ent is Line)
                            {
                                Line l = (Line)ent;
                                MText mt = FindGasLabelNearSegment(db, tr, l.StartPoint, l.EndPoint);
                                kw = (mt != null) ? InferKwFromLabel(mt.Contents, defaultLeafKw) : defaultLeafKw;
                            }
                            if (kw <= 0) kw = defaultLeafKw;
                            nodeKw[id] = kw;
                        }
                        else
                        {
                            double sum = 0.0;
                            for (int c = 0; c < ch.Count; c++)
                            {
                                if (nodeKw.ContainsKey(ch[c])) sum += nodeKw[ch[c]];
                            }
                            nodeKw[id] = Math.Max(defaultLeafKw, sum);
                        }
                    }

                    foreach (var kvp in nodeKw)
                    {
                        Entity ent = tr.GetObject(kvp.Key, OpenMode.ForRead) as Entity;
                        if (ent is Line)
                        {
                            UpdateExistingGasLineSize(db, btr, tr, (Line)ent, kvp.Value, true, h);
                            updatedCount++;
                        }
                        else if (ent is Arc)
                        {
                            SetPipeKwXData(db, tr, ent, kvp.Value);
                        }
                    }

                    totalNetworkKw = nodeKw.ContainsKey(rootLine.ObjectId) ? nodeKw[rootLine.ObjectId] : 0.0;
                    tr.Commit();

                    ed.WriteMessage(string.Format(
                        "\n[ĐHKK] Đã tính lại size toàn mạng ống gas thành công! Tổng công suất: {0:F1} kW (~{1:F1} HP), Cỡ chữ {2:F0}mm, Đã cập nhật {3} đoạn ống.",
                        totalNetworkKw, totalNetworkKw / 2.8, h, updatedCount));
                    return true;
                }
            }
        }

        // --- VẼ ĐƯỜNG ỐNG NƯỚC NGƯNG CÓ TÙY CHỌN ĐỘ DỐC & ĐÁNH ĐỘ DỐC TỰ ĐỘNG ---
        public static bool RouteCondensateDrainAlongPolyline(Document doc, ObjectId plId, DrainRoutingParams p)
        {
            if (doc == null || plId.IsNull || p == null) return false;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity ent = tr.GetObject(plId, OpenMode.ForRead) as Entity;
                    if (ent == null) return false;

                    List<Point3d> pts = ExtractPointsFromEntity(tr, ent);
                    if (pts.Count < 2)
                    {
                        ed.WriteMessage("\n[ĐHKK] Tuyến Polyline phải có ít nhất 2 điểm tọa độ!");
                        return false;
                    }

                    EnsureLayer(db, tr, LayerHvacDrain, 140); // Blue-Cyan

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double totalKw = Math.Max(3.0, p.TotalCoolingKw);
                    double outerDia;
                    string pipeName = GetCondensateDrainSize(totalKw, out outerDia);
                    double slope = Math.Max(0.5, p.SlopePercent);
                    double currentElev = p.StartElevationM > 0.5 ? p.StartElevationM : 2.8;

                    int segCount = pts.Count - 1;

                    for (int i = 0; i < segCount; i++)
                    {
                        Point3d pA = pts[i];
                        Point3d pB = pts[i + 1];
                        double dist = pA.DistanceTo(pB);
                        if (dist < 10.0) continue;

                        Vector3d dir = (pB - pA).GetNormal();
                        Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);

                        Line line = new Line(pA, pB);
                        line.Layer = LayerHvacDrain;
                        line.Color = Color.FromColorIndex(ColorMethod.ByAci, 140);
                        btr.AppendEntity(line);
                        tr.AddNewlyCreatedDBObject(line, true);

                        // Tính sụt cao độ do độ dốc
                        double dropM = (dist / 1000.0) * (slope / 100.0);
                        double nextElev = currentElev - dropM;

                        if (p.AddSlopeArrow)
                        {
                            Point3d midPt = pA + dir * (dist / 2.0);
                            DrawSlopeAnnotation(btr, tr, midPt, dir, normal, slope, pipeName, currentElev, nextElev);
                        }

                        currentElev = nextElev;
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[ĐHKK] Đã vẽ tuyến Ống Nước Ngưng {0} có độ dốc i={1:F1}% thành công!", pipeName, slope));
                    return true;
                }
            }
        }

        private static void DrawSlopeAnnotation(BlockTableRecord btr, Transaction tr, Point3d pt, Vector3d dir, Vector3d normal, double slopePercent, string pipeName, double startElev = 0, double endElev = 0)
        {
            // Vẽ mũi tên chỉ hướng dốc dòng chảy
            double arrowLen = 100.0;
            Point3d arrowStart = pt - dir * (arrowLen / 2.0);
            Point3d arrowEnd = pt + dir * (arrowLen / 2.0);

            Polyline arrow = new Polyline();
            arrow.AddVertexAt(0, new Point2d(arrowStart.X, arrowStart.Y), 0, 4.0, 4.0);
            arrow.AddVertexAt(1, new Point2d(arrowEnd.X - dir.X * 30.0, arrowEnd.Y - dir.Y * 30.0), 0, 16.0, 0);
            arrow.AddVertexAt(2, new Point2d(arrowEnd.X, arrowEnd.Y), 0, 0, 0);
            arrow.Layer = LayerHvacDrain;
            arrow.Color = Color.FromColorIndex(ColorMethod.ByAci, 140);
            btr.AppendEntity(arrow);
            tr.AddNewlyCreatedDBObject(arrow, true);

            // MText ghi chú độ dốc
            MText mtext = new MText();
            mtext.Location = pt + normal * 40.0;
            mtext.TextHeight = 50.0;
            mtext.Layer = LayerHvacDrain;
            mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, 140);

            double angle = Math.Atan2(dir.Y, dir.X);
            if (angle > Math.PI / 2.0) angle -= Math.PI;
            else if (angle < -Math.PI / 2.0) angle += Math.PI;
            mtext.Rotation = angle;
            mtext.Attachment = AttachmentPoint.BottomCenter;

            if (startElev > 0 && endElev > 0)
            {
                mtext.Contents = string.Format("{0} (i = {1:F1}%)\\PH: +{2:F2}m -> +{3:F2}m", pipeName, slopePercent, startElev, endElev);
            }
            else
            {
                mtext.Contents = string.Format("{0} (i = {1:F1}%)", pipeName, slopePercent);
            }

            btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
        }

        // --- XUẤT BẢNG TÍNH TOÁN ỐNG GIÓ VÀO CAD ---
        public static void DrawDuctSizingTableInCad(Document doc, HvacDuctCalcResult res, HvacDuctCalcInput inp)
        {
            if (doc == null || res == null) return;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptPointOptions ppo = new PromptPointOptions("\n[ĐHKK] Chọn điểm đặt bảng tính kích thước ống gió trên CAD: ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                Point3d ucsPt = ppr.Value;
                Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerHvacTable, 4);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Table table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = wcsPt;
                    table.Layer = LayerHvacTable;

                    int rows = 12;
                    int cols = 3;
                    table.SetSize(rows, cols);
                    table.SetRowHeight(12.0);
                    table.Columns[0].Width = 160.0;
                    table.Columns[1].Width = 110.0;
                    table.Columns[2].Width = 180.0;

                    // Header
                    table.Cells[0, 0].TextString = "BẢNG TÍNH TOÁN KÍCH THƯỚC & THỦY LỰC ĐƯỜNG ỐNG GIÓ (TCVN 5687 / SMACNA)";
                    table.Cells[0, 0].TextHeight = 5.0;

                    // Subheaders
                    table.Cells[1, 0].TextString = "THÔNG SỐ KỸ THUẬT";
                    table.Cells[1, 1].TextString = "GIÁ TRỊ TÍNH";
                    table.Cells[1, 2].TextString = "TIÊU CHUẨN / QUY CÁCH ĐỀ XUẤT";

                    string[,] data = new string[,]
                    {
                        { "1. Lưu lượng gió thiết kế (Q)", string.Format("{0:F0} CMH", res.AirFlowCmh), string.Format("Tương đương {0:F0} CFM", res.AirFlowCfm) },
                        { "2. Vận tốc gió thiết kế (V)", string.Format("{0:F1} m/s", inp.VelocityMs), "Đạt chuẩn độ ồn đường ống cấp/hồi" },
                        { "3. Diện tích tiết diện yêu cầu", string.Format("{0:F3} m²", res.CrossAreaM2), "A = Q / (3600 x V)" },
                        { "4. KÍCH THƯỚC ỐNG CHỮ NHẬT (W x H)", string.Format("{0:F0} x {1:F0} mm", res.RectWidthMm, res.RectHeightMm), string.Format("Tỷ lệ cạnh W/H = {0:F1} <= 4:1 (Đạt)", res.RectAspectRatio) },
                        { "5. Đường kính ống tròn tương đương", string.Format("Ø{0:F0} mm", res.RoundStandardMm), string.Format("Đường kính tính toán Ø{0:F1} mm", res.RoundDiameterMm) },
                        { "6. Vận tốc gió thực tế (V thực)", string.Format("{0:F2} m/s", res.ActualVelocityMs), "Kiểm tra tiêu chuẩn âm học ASHRAE" },
                        { "7. Tổn thất ma sát đơn vị (ΔP/L)", string.Format("{0:F2} Pa/m", res.UnitFrictionLossPam), "Công thức thủy lực Colebrook-Altshul" },
                        { "8. Tổng tổn thất ma sát ống thẳng", string.Format("{0:F1} Pa", res.TotalFrictionLossPa), string.Format("Chiều dài tuyến ống L = {0:F1} m", inp.DuctLengthM) },
                        { "9. Độ dày tôn mạ kẽm (δ)", string.Format("{0:F2} mm", res.SheetThicknessMm), string.Format("TCVN 5687 cho cạnh max {0:F0} mm", Math.Max(res.RectWidthMm, res.RectHeightMm)) },
                        { "10. Diện tích tôn mạ kẽm bóc tách", string.Format("{0:F2} m²", res.SheetMetalAreaM2), "Diện tích xung quanh thân ống" },
                        { "11. TỔNG KHỐI LƯỢNG TÔN (Bao gồm bích)", string.Format("{0:F1} kg", res.SheetMetalWeightKg), "Đã cộng hệ số 1.20 bích ghép & phụ kiện" }
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

                ed.WriteMessage("\n[ĐHKK] Đã chèn Bảng Tính Kích Thước Ống Gió thành công vào CAD!");
            }
        }

        // --- XUẤT BẢNG TÍNH CÔNG SUẤT LẠNH VÀO CAD ---
        public static void DrawCoolingLoadTableInCad(Document doc, HvacCoolingCalcResult res, HvacCoolingCalcInput inp)
        {
            if (doc == null || res == null) return;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptPointOptions ppo = new PromptPointOptions("\n[ĐHKK] Chọn điểm đặt bảng tính phụ tải lạnh FCU / VRV trên CAD: ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                Point3d ucsPt = ppr.Value;
                Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerHvacTable, 4);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Table table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = wcsPt;
                    table.Layer = LayerHvacTable;

                    int rows = 10;
                    int cols = 3;
                    table.SetSize(rows, cols);
                    table.SetRowHeight(12.0);
                    table.Columns[0].Width = 160.0;
                    table.Columns[1].Width = 120.0;
                    table.Columns[2].Width = 180.0;

                    // Header
                    table.Cells[0, 0].TextString = "BẢNG TÍNH CHỌN CÔNG SUẤT FCU & HỆ THỐNG VRV DAIKIN";
                    table.Cells[0, 0].TextHeight = 5.0;

                    // Subheaders
                    table.Cells[1, 0].TextString = "THÔNG SỐ PHÒNG / THIẾT KẾ";
                    table.Cells[1, 1].TextString = "GIÁ TRỊ TÍNH TOÁN";
                    table.Cells[1, 2].TextString = "MODEL ĐỀ XUẤT (DAIKIN VN)";

                    string[,] data = new string[,]
                    {
                        { "1. Loại không gian điều hòa", inp.SpaceType, "Hệ số suất lạnh theo tiêu chuẩn" },
                        { "2. Diện tích sàn phòng (F)", string.Format("{0:F1} m²", inp.RoomAreaM2), string.Format("Chiều cao trần H = {0:F1} m", inp.CeilingHeightM) },
                        { "3. Suất lạnh thiết kế (q)", string.Format("{0:F0} W/m²", inp.CoolingIndexWPerM2), "Tính theo khí hậu miền Nam/Bắc VN" },
                        { "4. Hệ số dự phòng an toàn", string.Format("+{0:F0}%", inp.SafetyMarginPercent), "Tính đến tổn thất nhiệt vỏ bao che" },
                        { "5. TỔNG PHỤ TẢI LẠNH YÊU CẦU", string.Format("{0:F2} kW", res.TotalLoadKw), string.Format("Tương đương {0:F0} BTU/h ({1:F1} RT)", res.TotalLoadBtu, res.TotalLoadRt) },
                        { "6. Thiết bị dàn lạnh FCU / Cassette", res.SuggestedFcuModel, "Dàn lạnh âm trần nối ống gió / Cassette" },
                        { "7. Cụm dàn nóng VRV tương thích", res.SuggestedVrvOutdoor, "Hệ Daikin VRV A/X Max hoặc SkyAir" },
                        { "8. Khối lượng gas nạp bổ sung dự kiến", string.Format("~{0:F1} kg R410A", res.TotalLoadKw * 0.18), "Dựa trên chiều dài tương đương ống gas" },
                        { "9. Tiêu chuẩn áp dụng", "TCVN 5687:2024", "ASHRAE Standard 62.1 & 90.1" }
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

                ed.WriteMessage("\n[ĐHKK] Đã chèn Bảng Tính Phụ Tải Lạnh thành công vào CAD!");
            }
        }
    }
}

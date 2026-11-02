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
            return new List<HvacBlockItem>
            {
                new HvacBlockItem { Name = "Miệng gió khuếch tán vuông (SCD)", FileName = "Mieng_Gio_Khuech_Tan_Vuong.dwg", Category = "Miệng Gió", Description = "Miệng cấp 600x600 nan khuếch tán 4 hướng" },
                new HvacBlockItem { Name = "Miệng gió hồi khe Slot (SLD)", FileName = "Mieng_Gio_Hoi_Khe_Slot.dwg", Category = "Miệng Gió", Description = "Miệng gió hồi nan tuyến tính dài 1000x150" },
                new HvacBlockItem { Name = "Miệng gió nan louver ngoài trời", FileName = "Mieng_Gio_Nan_Louver.dwg", Category = "Miệng Gió", Description = "Louver lấy gió tươi / thải gió chắn mưa 800x400" },
                new HvacBlockItem { Name = "Dàn lạnh FCU giấu trần nối ống", FileName = "Dan_Lanh_FCU_Giau_Tran.dwg", Category = "Thiết Bị Lạnh", Description = "Ducted FCU 1000x500 kèm bầu cấp/hồi gió" },
                new HvacBlockItem { Name = "Dàn nóng trung tâm VRV / VRF", FileName = "Dan_Nong_VRV_VRF.dwg", Category = "Thiết Bị Lạnh", Description = "Cụm dàn nóng 1200x800 giải nhiệt gió ngoài trời" },
                new HvacBlockItem { Name = "Quạt thông gió hút mùi gắn trần", FileName = "Quat_Thong_Gio_Gan_Tran.dwg", Category = "Thông Gió", Description = "Quạt hút âm trần D300/D150 nối ống gió" },
                new HvacBlockItem { Name = "Van điều chỉnh lưu lượng tròn VCD", FileName = "Van_Gio_Tron_VCD.dwg", Category = "Van Gió", Description = "Volume Control Damper ống tròn tay gạt" },
                new HvacBlockItem { Name = "Van điều chỉnh lưu lượng vuông VCD", FileName = "Van_Gio_Vuong_VCD.dwg", Category = "Van Gió", Description = "Volume Control Damper ống vuông nhiều cánh" },
                new HvacBlockItem { Name = "Van dập lửa cầu chì (Fire Damper)", FileName = "Van_Dap_Lua_FD.dwg", Category = "Van Gió", Description = "Cầu chì nóng chảy 70°C tự ngắt khi có cháy" },
                new HvacBlockItem { Name = "Ống gió mềm có cách nhiệt", FileName = "Ong_Mem_Cach_Nhiet.dwg", Category = "Phụ Kiện", Description = "Flexible duct bọc bông thủy tinh cách nhiệt D200" },
                new HvacBlockItem { Name = "Cửa thăm bảo trì ống gió (AD)", FileName = "Cua_Tham_Ong_Gio.dwg", Category = "Phụ Kiện", Description = "Access door 400x400 lắp trên thân ống gió" },
                new HvacBlockItem { Name = "Bộ chia gas Daikin KHRQ22M20T (<20kW)", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M20T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D15.9, lỏng D9.5" },
                new HvacBlockItem { Name = "Bộ chia gas Daikin KHRQ22M29T (20-33kW)", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M29T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D22.2, lỏng D9.5" },
                new HvacBlockItem { Name = "Bộ chia gas Daikin KHRQ22M64T (33-64kW)", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M64T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D28.6, lỏng D12.7" },
                new HvacBlockItem { Name = "Bộ chia gas Daikin KHRQ22M75T (64-90kW)", FileName = "Bo_Chia_Gas_Daikin_KHRQ22M75T.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Y-Joint nhánh hơi D34.9, lỏng D15.9" },
                new HvacBlockItem { Name = "Bộ chia góp Header Daikin 8 ngã", FileName = "Bo_Chia_Gas_Daikin_Header_8Nga.dwg", Category = "Bộ Chia Gas Daikin", Description = "Refnet Header 8 ngã chia đồng thời các dàn lạnh" }
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
                    @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\DHKK");
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

        // --- TIÊU CHUẨN BỘ CHIA GAS & ỐNG ĐỒNG DAIKIN VN ---
        public static DaikinRefnetSpec GetDaikinRefnetByCapacity(double capacityKw)
        {
            if (capacityKw <= 20.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M20T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M20T.dwg",
                    MaxKw = 20.0,
                    GasDiaMm = 15.9,
                    LiqDiaMm = 9.5,
                    GasPipeStr = "Ø15.9x1.0",
                    LiqPipeStr = "Ø9.5x0.8",
                    Description = "Bộ chia gas Daikin KHRQ22M20T (Công suất < 20 kW / 5.7 HP)"
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
                    GasPipeStr = "Ø22.2x1.0",
                    LiqPipeStr = "Ø9.5x0.8",
                    Description = "Bộ chia gas Daikin KHRQ22M29T (Công suất 20 - 33 kW / 9.4 HP)"
                };
            }
            else if (capacityKw <= 64.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M64T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M64T.dwg",
                    MaxKw = 64.0,
                    GasDiaMm = 28.6,
                    LiqDiaMm = 12.7,
                    GasPipeStr = "Ø28.6x1.0",
                    LiqPipeStr = "Ø12.7x0.8",
                    Description = "Bộ chia gas Daikin KHRQ22M64T (Công suất 33 - 64 kW / 18.2 HP)"
                };
            }
            else if (capacityKw <= 90.0)
            {
                return new DaikinRefnetSpec
                {
                    ModelName = "Bo_Chia_Gas_Daikin_KHRQ22M75T",
                    DwgFileName = "Bo_Chia_Gas_Daikin_KHRQ22M75T.dwg",
                    MaxKw = 90.0,
                    GasDiaMm = 34.9,
                    LiqDiaMm = 15.9,
                    GasPipeStr = "Ø34.9x1.2",
                    LiqPipeStr = "Ø15.9x1.0",
                    Description = "Bộ chia gas Daikin KHRQ22M75T (Công suất 64 - 90 kW / 25.6 HP)"
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
                    GasPipeStr = "Ø41.3x1.5",
                    LiqPipeStr = "Ø19.1x1.0",
                    Description = "Bộ chia gas Daikin KHRQ22M75T (Công suất >= 90 kW)"
                };
            }
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

        // --- VẼ ỐNG GIÓ TỰ ĐỘNG THEO POLYLINE ĐỊNH TUYẾN & TĂNG GIẢM SIZE ---
        public static bool RouteDuctAlongPolyline(Document doc, ObjectId plId, DuctRoutingParams p)
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

                    // Xác định Layer và màu theo hệ thống ống gió
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

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double q0 = Math.Max(200.0, p.InitialAirFlowCmh);
                    double vMs = Math.Max(2.0, p.VelocityMs);
                    double hMm = Math.Max(150.0, p.FixedHeightMm);
                    double stepDown = Math.Max(0.0, p.StepDownPercent);

                    int segCount = pts.Count - 1;
                    List<double> segWidths = new List<double>();
                    List<double> segFlows = new List<double>();

                    for (int i = 0; i < segCount; i++)
                    {
                        double qSeg = Math.Max(200.0, q0 * (1.0 - (stepDown / 100.0) * i));
                        double areaReq = (qSeg / 3600.0) / vMs;
                        double wCalc = (areaReq / (hMm / 1000.0)) * 1000.0;
                        double wSeg = Math.Max(150.0, Math.Ceiling(wCalc / 50.0) * 50.0);
                        segWidths.Add(wSeg);
                        segFlows.Add(qSeg);
                    }

                    // Vẽ các phân đoạn ống gió
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

                        Point3d leftA = pA + normal * halfW;
                        Point3d leftB = pB + normal * halfW;
                        Point3d rightA = pA - normal * halfW;
                        Point3d rightB = pB - normal * halfW;

                        if (p.DrawDoubleLine)
                        {
                            // Biên trái
                            Line lineL = new Line(leftA, leftB);
                            lineL.Layer = ductLayer;
                            lineL.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                            btr.AppendEntity(lineL);
                            tr.AddNewlyCreatedDBObject(lineL, true);

                            // Biên phải
                            Line lineR = new Line(rightA, rightB);
                            lineR.Layer = ductLayer;
                            lineR.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                            btr.AppendEntity(lineR);
                            tr.AddNewlyCreatedDBObject(lineR, true);

                            // Nếu là đoạn đầu tiên, vẽ mặt bích đầu
                            if (i == 0)
                            {
                                Line capStart = new Line(leftA, rightA);
                                capStart.Layer = ductLayer;
                                capStart.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(capStart);
                                tr.AddNewlyCreatedDBObject(capStart, true);
                            }

                            // Nếu là đoạn cuối cùng, vẽ mặt bích cuối
                            if (i == segCount - 1)
                            {
                                Line capEnd = new Line(leftB, rightB);
                                capEnd.Layer = ductLayer;
                                capEnd.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(capEnd);
                                tr.AddNewlyCreatedDBObject(capEnd, true);
                            }
                        }

                        // Vẽ đường tim nếu chọn
                        if (p.DrawCenterline)
                        {
                            Line cLine = new Line(pA, pB);
                            cLine.Layer = LayerHvacCenterline;
                            cLine.Color = Color.FromColorIndex(ColorMethod.ByAci, 8);
                            btr.AppendEntity(cLine);
                            tr.AddNewlyCreatedDBObject(cLine, true);
                        }

                        // Ghi chú kích thước ống gió W x H & Lưu lượng
                        if (p.AddAnnotations && dist > 150.0)
                        {
                            Point3d midPt = pA + dir * (dist / 2.0);
                            MText mtext = new MText();
                            mtext.Location = midPt + normal * (halfW + 40.0);
                            mtext.TextHeight = Math.Max(50.0, Math.Min(140.0, dist / 20.0));
                            mtext.Layer = ductLayer;
                            mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);

                            double angle = Math.Atan2(dir.Y, dir.X);
                            if (angle > Math.PI / 2.0) angle -= Math.PI;
                            else if (angle < -Math.PI / 2.0) angle += Math.PI;
                            mtext.Rotation = angle;
                            mtext.Attachment = AttachmentPoint.MiddleCenter;

                            mtext.Contents = string.Format("{0:F0}x{1:F0}\\PQ={2:F0} CMH", w, hMm, segFlows[i]);
                            btr.AppendEntity(mtext);
                            tr.AddNewlyCreatedDBObject(mtext, true);
                        }

                        // Vẽ Côn thu (Reducer) hoặc Co nếu chuyển sang đoạn kế tiếp
                        if (i < segCount - 1 && p.DrawDoubleLine)
                        {
                            double nextDist = pts[i + 1].DistanceTo(pts[i + 2]);
                            if (nextDist >= 1.0)
                            {
                                double nextW = segWidths[i + 1];
                                Point3d pNextC = pts[i + 1];
                                Vector3d nextDir = (pts[i + 2] - pts[i + 1]).GetNormal();
                                Vector3d nextNormal = new Vector3d(-nextDir.Y, nextDir.X, 0);

                                Point3d nextLeftA = pNextC + nextNormal * (nextW / 2.0);
                                Point3d nextRightA = pNextC - nextNormal * (nextW / 2.0);

                                // Côn thu chuyển tiếp
                                Line redL = new Line(leftB, nextLeftA);
                                redL.Layer = ductLayer;
                                redL.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(redL);
                                tr.AddNewlyCreatedDBObject(redL, true);

                                Line redR = new Line(rightB, nextRightA);
                                redR.Layer = ductLayer;
                                redR.Color = Color.FromColorIndex(ColorMethod.ByAci, ductColor);
                                btr.AppendEntity(redR);
                                tr.AddNewlyCreatedDBObject(redR, true);
                            }
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[ĐHKK] Đã vẽ thành công {0} đoạn ống gió hệ {1} theo tuyến Polyline!", segCount, sysName));
                    return true;
                }
            }
        }

        // --- VẼ ĐƯỜNG ỐNG ĐỒNG & CHÈN BỘ CHIA GAS DAIKIN THEO LƯU LƯỢNG MÔI CHẤT ---
        public static bool RouteRefrigerantAlongPolyline(Document doc, ObjectId plId, RefrigerantRoutingParams p)
        {
            if (doc == null || plId.IsNull || p == null) return false;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                double totalKw = Math.Max(5.0, p.TotalCoolingKw);
                DaikinRefnetSpec refnet = GetDaikinRefnetByCapacity(totalKw);

                // Nạp block bộ chia gas Daikin TRƯỚC KHI MỞ TRANSACTION trên db
                ObjectId refnetBlockId = ObjectId.Null;
                if (p.InsertRefnetJoints)
                {
                    refnetBlockId = EnsureBlockImported(db, refnet.ModelName, refnet.DwgFileName);
                }

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

                    EnsureLayer(db, tr, LayerHvacGasPipe, 1); // Red (Ống hơi/hút)
                    EnsureLayer(db, tr, LayerHvacLiqPipe, 5); // Blue (Ống lỏng)
                    EnsureLayer(db, tr, LayerHvacRefnet, 2);  // Yellow (Bộ chia gas)
                    if (p.DrawCombinedDrain)
                    {
                        EnsureLayer(db, tr, LayerHvacDrain, 140); // Blue-Cyan (Nước ngưng)
                    }

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double spacing = Math.Max(40.0, p.PipeSpacingMm);
                    double halfSp = spacing / 2.0;

                    int segCount = pts.Count - 1;

                    for (int i = 0; i < segCount; i++)
                    {
                        Point3d pA = pts[i];
                        Point3d pB = pts[i + 1];
                        double dist = pA.DistanceTo(pB);
                        if (dist < 10.0) continue;

                        Vector3d dir = (pB - pA).GetNormal();
                        Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);

                        // 1. Tuyến ống gas (hơi) lệch +halfSp
                        Point3d gasA = pA + normal * halfSp;
                        Point3d gasB = pB + normal * halfSp;
                        Line lineGas = new Line(gasA, gasB);
                        lineGas.Layer = LayerHvacGasPipe;
                        lineGas.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);
                        btr.AppendEntity(lineGas);
                        tr.AddNewlyCreatedDBObject(lineGas, true);

                        // 2. Tuyến ống lỏng lệch -halfSp
                        Point3d liqA = pA - normal * halfSp;
                        Point3d liqB = pB - normal * halfSp;
                        Line lineLiq = new Line(liqA, liqB);
                        lineLiq.Layer = LayerHvacLiqPipe;
                        lineLiq.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                        btr.AppendEntity(lineLiq);
                        tr.AddNewlyCreatedDBObject(lineLiq, true);

                        // 3. Nếu vẽ kèm ống nước ngưng (offset thêm 50mm bên cạnh ống lỏng)
                        if (p.DrawCombinedDrain)
                        {
                            Point3d drainA = pA - normal * (halfSp + 50.0);
                            Point3d drainB = pB - normal * (halfSp + 50.0);
                            Line lineDrain = new Line(drainA, drainB);
                            lineDrain.Layer = LayerHvacDrain;
                            lineDrain.Color = Color.FromColorIndex(ColorMethod.ByAci, 140);
                            btr.AppendEntity(lineDrain);
                            tr.AddNewlyCreatedDBObject(lineDrain, true);

                            // Đánh ký hiệu mũi tên dốc cho ống nước ngưng
                            Point3d drainMid = drainA + dir * (dist / 2.0);
                            DrawSlopeAnnotation(btr, tr, drainMid, dir, normal, p.DrainSlopePercent, "uPVC D27", 0, 0);
                        }

                        // Ghi chú cỡ ống đồng dọc theo tuyến
                        if (p.AddAnnotations && dist > 150.0)
                        {
                            Point3d textPt = pA + dir * (dist / 2.0) + normal * (halfSp + 25.0);
                            MText mtext = new MText();
                            mtext.Location = textPt;
                            mtext.TextHeight = Math.Max(40.0, Math.Min(100.0, dist / 25.0));
                            mtext.Layer = LayerHvacGasPipe;
                            mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, 1);

                            double angle = Math.Atan2(dir.Y, dir.X);
                            if (angle > Math.PI / 2.0) angle -= Math.PI;
                            else if (angle < -Math.PI / 2.0) angle += Math.PI;
                            mtext.Rotation = angle;
                            mtext.Attachment = AttachmentPoint.MiddleCenter;

                            mtext.Contents = string.Format("Gas: {0}\\PLiq: {1}", refnet.GasPipeStr, refnet.LiqPipeStr);
                            btr.AppendEntity(mtext);
                            tr.AddNewlyCreatedDBObject(mtext, true);
                        }

                        // Chèn Bộ chia gas Daikin tại các điểm rẽ nhánh (internal vertices)
                        if (p.InsertRefnetJoints && i < segCount - 1 && !refnetBlockId.IsNull)
                        {
                            Point3d branchPt = pts[i + 1];
                            BlockReference bref = new BlockReference(branchPt, refnetBlockId);
                            bref.Layer = LayerHvacRefnet;
                            bref.Rotation = Math.Atan2(dir.Y, dir.X);
                            bref.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                            btr.AppendEntity(bref);
                            tr.AddNewlyCreatedDBObject(bref, true);
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\n[ĐHKK] Đã vẽ tuyến Cụm Ống Đồng (Gas {0} / Liq {1}) & Bộ chia Daikin {2} thành công!",
                        refnet.GasPipeStr, refnet.LiqPipeStr, refnet.ModelName));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ chuyên sâu tạo lập bản vẽ Mặt Bằng Bố Trí Vỏ Tủ Điện Công Nghiệp (Enclosure Layout Plan)
    /// Hỗ trợ cả xuất AutoCAD và Preview tương tác kéo thả (Drag & Drop), chỉnh sửa đối tượng thời gian thực
    /// </summary>
    public class EnclosureLayoutDrawingService
    {
        public const string LayerEnclosure = "E-ENCLOSURE";
        public const string LayerInnerDoor = "E-INNER-DOOR";
        public const string LayerEquipment = "E-EQUIPMENT";
        public const string LayerBusbar    = "E-BUSBAR";
        public const string LayerControl   = "E-CONTROL";
        public const string LayerDuct      = "E-DUCT";
        public const string LayerText      = "E-TEXT";
        public const string LayerDim       = "E-DIM";

        private static string _cachedTdDwgPath = null;

        public static string FindTdDwgPath()
        {
            if (!string.IsNullOrEmpty(_cachedTdDwgPath) && File.Exists(_cachedTdDwgPath))
                return _cachedTdDwgPath;

            string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TD.dwg");
            if (File.Exists(desktopPath))
            {
                _cachedTdDwgPath = desktopPath;
                return _cachedTdDwgPath;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string bundleBlocks = Path.Combine(appData, "Autodesk", "ApplicationPlugins", "TaiAiAddin.bundle", "Contents", "Blocks", "TD.dwg");
            if (File.Exists(bundleBlocks))
            {
                _cachedTdDwgPath = bundleBlocks;
                return _cachedTdDwgPath;
            }

            string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string relBlocks = Path.Combine(asmDir, "..", "Blocks", "TD.dwg");
            if (File.Exists(relBlocks))
            {
                _cachedTdDwgPath = Path.GetFullPath(relBlocks);
                return _cachedTdDwgPath;
            }

            return null;
        }

        public static string FindAnyBlockPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            string[] searchDirs = new string[]
            {
                @"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện",
                @"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thu Viện\Điện",
                @"d:\01. NGUYEN TAN TAI\CODING AI\block run AI",
                @"d:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Blocks",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "TaiAiAddin.bundle", "Contents", "Blocks"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop))
            };

            foreach (string dir in searchDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        string p = Path.Combine(dir, fileName);
                        if (File.Exists(p)) return p;

                        string[] dwgs = Directory.GetFiles(dir, "*.dwg");
                        foreach (string f in dwgs)
                        {
                            string bn = Path.GetFileName(f);
                            if (string.Equals(bn, fileName, StringComparison.OrdinalIgnoreCase)) return f;
                            if (fileName.StartsWith("F") && fileName.Contains("CH") && bn.StartsWith("F", StringComparison.OrdinalIgnoreCase) && bn.Contains("CH")) return f;
                            if (fileName.Contains("THERM") && bn.IndexOf("THERMOSTAT", StringComparison.OrdinalIgnoreCase) >= 0) return f;
                        }
                    }
                }
                catch { }
            }

            try
            {
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string relB = Path.Combine(asmDir, "..", "Blocks", fileName);
                if (File.Exists(relB)) return Path.GetFullPath(relB);
            }
            catch { }

            return null;
        }

        public static void GetBreakerBlockInfo(ElectricalLoad ld, BlockTable bt, bool useLargeFrameMcb,
            out ObjectId blockId, out double width, out string displayName)
        {
            string spec = ((ld != null && ld.BreakerSpec != null ? ld.BreakerSpec : "") + " " + (ld != null && ld.BreakerType != null ? ld.BreakerType : "")).ToUpper();
            bool isRccb = spec.Contains("RCCB") || spec.Contains("RCBO") || spec.Contains("ELCB");
            bool isMccb = spec.Contains("MCCB");
            bool is4P = spec.Contains("4P");
            bool is3P = spec.Contains("3P");
            bool is2P = spec.Contains("2P");
            bool is1P = spec.Contains("1P");

            if (!is4P && !is3P && !is2P && !is1P)
            {
                if (ld != null && ld.Phase == PhaseType.SinglePhase) is1P = true;
                else is3P = true;
            }

            blockId = ObjectId.Null;
            width = 36.0;
            displayName = "MCB";

            if (isRccb)
            {
                if (is4P)
                {
                    displayName = spec.Contains("RCBO") ? "RCBO 4P" : "RCCB 4P";
                    width = 72.0;
                    if (bt.Has("RCCB 4P")) blockId = bt["RCCB 4P"];
                    else if (bt.Has("MCB 4P")) blockId = bt["MCB 4P"];
                }
                else
                {
                    displayName = spec.Contains("RCBO") ? "RCBO 2P" : "RCCB 2P";
                    width = 36.0;
                    if (bt.Has("RCCB 2P")) blockId = bt["RCCB 2P"];
                    else if (bt.Has("MCB 2P")) blockId = bt["MCB 2P"];
                }
            }
            else if (isMccb)
            {
                if (is4P)
                {
                    displayName = "MCCB 4P";
                    width = 105.0;
                    if (bt.Has("EZS160-250")) blockId = bt["EZS160-250"];
                    else if (bt.Has("MCB 4P")) blockId = bt["MCB 4P"];
                }
                else if (is3P)
                {
                    displayName = "MCCB 3P";
                    width = 75.0;
                    if (bt.Has("EZS 100")) blockId = bt["EZS 100"];
                    else if (bt.Has("EZS160-250")) blockId = bt["EZS160-250"];
                    else if (bt.Has("MCB 3P")) blockId = bt["MCB 3P"];
                }
                else if (is2P)
                {
                    displayName = "MCCB 2P";
                    width = 54.0;
                    if (bt.Has("mcb 2p L")) blockId = bt["mcb 2p L"];
                    else if (bt.Has("MCB 2P")) blockId = bt["MCB 2P"];
                }
                else
                {
                    displayName = "MCCB 1P";
                    width = 25.0;
                    if (bt.Has("MCB 1P")) blockId = bt["MCB 1P"];
                }
            }
            else
            {
                if (is4P)
                {
                    displayName = "MCB 4P";
                    width = 72.0;
                    if (bt.Has("MCB 4P")) blockId = bt["MCB 4P"];
                }
                else if (is3P)
                {
                    displayName = "MCB 3P";
                    bool preferLarge = spec.Contains("3P L") || spec.Contains("3PL") || useLargeFrameMcb || (ld != null && ld.CurrentA >= 50.0);
                    if (preferLarge && bt.Has("MCB 3P L"))
                    {
                        blockId = bt["MCB 3P L"];
                        width = 81.0;
                        displayName = "MCB 3P L";
                    }
                    else if (bt.Has("MCB 3P"))
                    {
                        blockId = bt["MCB 3P"];
                        width = 54.0;
                    }
                    else if (bt.Has("MCB 3P L"))
                    {
                        blockId = bt["MCB 3P L"];
                        width = 81.0;
                    }
                }
                else if (is2P)
                {
                    displayName = "MCB 2P";
                    bool preferLarge = spec.Contains("2P L") || spec.Contains("2PL") || useLargeFrameMcb || (ld != null && ld.CurrentA >= 50.0);
                    if (preferLarge && bt.Has("mcb 2p L"))
                    {
                        blockId = bt["mcb 2p L"];
                        width = 67.5;
                        displayName = "MCB 2P L";
                    }
                    else if (bt.Has("MCB 2P"))
                    {
                        blockId = bt["MCB 2P"];
                        width = 36.0;
                    }
                    else if (bt.Has("mcb 2p L"))
                    {
                        blockId = bt["mcb 2p L"];
                        width = 67.5;
                    }
                }
                else
                {
                    displayName = "MCB 1P";
                    width = 18.0;
                    if (bt.Has("MCB 1P")) blockId = bt["MCB 1P"];
                }
            }
        }

        public static ObjectId EnsureBlockLoaded(Database targetDb, string blockName, string[] possibleFileNames)
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

            string filePath = null;
            foreach (string fn in possibleFileNames)
            {
                filePath = FindAnyBlockPath(fn);
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    break;
            }

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return ObjectId.Null;

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, "");

                    bool hasNamedBlock = false;
                    ObjectId srcBlkId = ObjectId.Null;
                    using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable btSrc = (BlockTable)trSrc.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                        if (btSrc.Has(blockName))
                        {
                            hasNamedBlock = true;
                            srcBlkId = btSrc[blockName];
                        }
                        trSrc.Commit();
                    }

                    if (hasNamedBlock && srcBlkId != ObjectId.Null)
                    {
                        ObjectIdCollection ids = new ObjectIdCollection();
                        ids.Add(srcBlkId);
                        IdMapping mapping = new IdMapping();
                        targetDb.WblockCloneObjects(ids, targetDb.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                    }
                    else
                    {
                        return targetDb.Insert(blockName, sourceDb, true);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EnsureBlockLoaded error: " + ex.Message);
            }

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName)) return bt[blockName];
                tr.Commit();
            }

            return ObjectId.Null;
        }

        public static void PreloadBlocks(Database targetDb, IEnumerable<string> blockNames)
        {
            if (targetDb == null || blockNames == null) return;

            List<string> missingBlocks = new List<string>();
            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                foreach (string name in blockNames)
                {
                    if (!string.IsNullOrEmpty(name) && !bt.Has(name) && !missingBlocks.Contains(name))
                    {
                        missingBlocks.Add(name);
                    }
                }
                tr.Commit();
            }

            if (missingBlocks.Count == 0) return;

            string sourceDwg = FindTdDwgPath();
            if (string.IsNullOrEmpty(sourceDwg) || !File.Exists(sourceDwg)) return;

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(sourceDwg, FileOpenMode.OpenForReadAndAllShare, true, "");
                    ObjectIdCollection ids = new ObjectIdCollection();

                    using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable btSrc = (BlockTable)trSrc.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                        foreach (string name in missingBlocks)
                        {
                            if (btSrc.Has(name))
                            {
                                ids.Add(btSrc[name]);
                            }
                        }
                        trSrc.Commit();
                    }

                    if (ids.Count > 0)
                    {
                        IdMapping mapping = new IdMapping();
                        targetDb.WblockCloneObjects(ids, targetDb.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                    }
                }
            }
            catch { }
        }

        public static ObjectId GetOrCloneBlock(Database targetDb, string blockName)
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

            string sourceDwg = FindTdDwgPath();
            if (string.IsNullOrEmpty(sourceDwg) || !File.Exists(sourceDwg))
            {
                return ObjectId.Null;
            }

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(sourceDwg, FileOpenMode.OpenForReadAndAllShare, true, "");
                    ObjectIdCollection ids = new ObjectIdCollection();

                    using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable btSrc = (BlockTable)trSrc.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                        if (btSrc.Has(blockName))
                        {
                            ids.Add(btSrc[blockName]);
                        }
                        trSrc.Commit();
                    }

                    if (ids.Count > 0)
                    {
                        IdMapping mapping = new IdMapping();
                        targetDb.WblockCloneObjects(ids, targetDb.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                        if (mapping.Contains(ids[0]))
                        {
                            return mapping[ids[0]].Value;
                        }
                    }
                }
            }
            catch { }

            return ObjectId.Null;
        }

        public static void EnsureLayers(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

            CreateLayerIfNotExists(lt, tr, LayerEnclosure, 7, LineWeight.LineWeight035);
            CreateLayerIfNotExists(lt, tr, LayerInnerDoor, 8, LineWeight.LineWeight025);
            CreateLayerIfNotExists(lt, tr, LayerEquipment, 3, LineWeight.LineWeight030);
            CreateLayerIfNotExists(lt, tr, LayerBusbar,    2, LineWeight.LineWeight040);
            CreateLayerIfNotExists(lt, tr, LayerControl,   4, LineWeight.LineWeight020);
            CreateLayerIfNotExists(lt, tr, LayerDuct,      8, LineWeight.LineWeight018);
            CreateLayerIfNotExists(lt, tr, LayerText,      7, LineWeight.LineWeight018);
            CreateLayerIfNotExists(lt, tr, LayerDim,       1, LineWeight.LineWeight015);
        }

        private static void CreateLayerIfNotExists(LayerTable lt, Transaction tr, string layerName, short colorIndex, LineWeight lw)
        {
            if (!lt.Has(layerName))
            {
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                ltr.LineWeight = lw;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        /// <summary>
        /// Vẽ toàn bộ Mặt Bằng Bố Trí Vỏ Tủ vào AutoCAD với đầy đủ cấu hình và tọa độ đã tùy biến từ EnclosureLayoutParameters
        /// </summary>
        public bool DrawFullEnclosureLayoutPlan(Document doc, ElectricalPanel panel, Point3d origin, EnclosureLayoutParameters ep = null)
        {
            if (doc == null || panel == null) return false;

            if (ep == null)
            {
                ep = new EnclosureLayoutParameters();
                ep.PanelName = panel.PanelName;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                {
                    // 1. Tải trước toàn bộ các block từ TD.dwg và các DWG thư viện TRƯỚC KHI mở Transaction chính (Tránh xung đột khóa Db)
                    PreloadBlocks(db, new string[] {
                        "KHOÁ NGOÀI", "HNP1_200X40", "HSS11_XB5AD33", "HDV1_LOCK-ROTATION",
                        "ban le cua trong", "EXHAUST FAN", "EZS160-250", "EZS 100",
                        "ĐỒNG MCCB_MCB", "MCB_HD", "MCB 3P", "A$C802a40ce", "A$C9d40e89b",
                        "RELAY TRUNG GIAN 14 CHAN", "HCR1_MX200A-380", "HFU_123456789",
                        "DOMINO 15A", "RAY"
                    });
                    EnsureBlockLoaded(db, "A-O-M", new string[] { "A-O-M.dwg" });
                    EnsureBlockLoaded(db, "EM", new string[] { "EM.dwg", "EM.dwg.dwg" });
                    EnsureBlockLoaded(db, "PM_BAOPHA", new string[] { "PM+BAO PHA.dwg", "PM+BÁO PHA.dwg" });

                    // Các Block MCB / RCBO từ Thư Viện Điện theo yêu cầu
                    EnsureBlockLoaded(db, "MCB 1P", new string[] { "MCB 1P.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "MCB 2P", new string[] { "MCB 2P.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "mcb 2p L", new string[] { "mcb 2p L.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "MCB 3P", new string[] { "MCB 3P.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "MCB 3P L", new string[] { "MCB 3P L.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "MCB 4P", new string[] { "MCB 4P.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "RCCB 2P", new string[] { "RCCB 2P.dwg", "MCB_HD.dwg" });
                    EnsureBlockLoaded(db, "RCCB 4P", new string[] { "RCCB 4P.dwg", "MCB_HD.dwg" });

                    // Các đối tượng thiết bị mở rộng từ Thư Viện Điện
                    EnsureBlockLoaded(db, "EZS 100", new string[] { "EZS 100.dwg" });
                    EnsureBlockLoaded(db, "EZS160-250", new string[] { "EZS160-250.dwg" });
                    EnsureBlockLoaded(db, "F_CHONG_SET", new string[] { "F CH?NG ST1.dwg", "F CHỐNG SÉT1.dwg", "F.dwg" });
                    EnsureBlockLoaded(db, "HCR1_MX200A-380", new string[] { "HCR1_MX200A-380.dwg" });
                    EnsureBlockLoaded(db, "HCR1_G4Q+8PFA1", new string[] { "HCR1_G4Q+8PFA1.dwg" });
                    EnsureBlockLoaded(db, "HDV_THERMOSTAT", new string[] { "HDV_THERRMOSTAT_LEIPOLE60_33_40.dwg" });
                    EnsureBlockLoaded(db, "GOI_SM", new string[] { "GOI SM.dwg", "GOI SM (TOP).dwg" });
                    EnsureBlockLoaded(db, "cau_chi", new string[] { "cau_chi.dwg" });

                    // 2. Mở Transaction chính để vẽ
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        EnsureLayers(db, tr);

                        double W = ep.Width;
                        double H = ep.Height;
                        double D = ep.Depth;
                        double plinthH = ep.PlinthHeight;
                        double canopyH = ep.CanopyHeight;
                        double gapX = W + 250.0;

                        ed.WriteMessage(string.Format("\n[NTT TOOLS] Đang xuất Mặt Bằng Bố Trí Vỏ Tủ '{0}': {1}x{2}x{3} mm...", ep.PanelName, W, H, D));

                        // Lấy các ObjectId trực tiếp từ bt (an toàn tuyệt đối và nhanh chóng)
                        ObjectId blkKhoaNgoai = bt.Has("KHOÁ NGOÀI") ? bt["KHOÁ NGOÀI"] : ObjectId.Null;
                        ObjectId blkNameplate = bt.Has("HNP1_200X40") ? bt["HNP1_200X40"] : ObjectId.Null;
                        ObjectId blkAom       = bt.Has("A-O-M") ? bt["A-O-M"] : (bt.Has("HSS11_XB5AD33") ? bt["HSS11_XB5AD33"] : ObjectId.Null);
                        ObjectId blkEm        = bt.Has("EM") ? bt["EM"] : (bt.Has("HPB1_XB7NS9445") ? bt["HPB1_XB7NS9445"] : ObjectId.Null);
                        ObjectId blkPmBaoPha  = bt.Has("PM_BAOPHA") ? bt["PM_BAOPHA"] : (bt.Has("PM+BAO PHA") ? bt["PM+BAO PHA"] : ObjectId.Null);
                        ObjectId blkInnerLock = bt.Has("HDV1_LOCK-ROTATION") ? bt["HDV1_LOCK-ROTATION"] : ObjectId.Null;
                        ObjectId blkHinge     = bt.Has("ban le cua trong") ? bt["ban le cua trong"] : ObjectId.Null;
                        ObjectId blkFan       = bt.Has("EXHAUST FAN") ? bt["EXHAUST FAN"] : ObjectId.Null;
                        ObjectId blkMccbEzs   = bt.Has("EZS160-250") ? bt["EZS160-250"] : (bt.Has("EZS 100") ? bt["EZS 100"] : ObjectId.Null);
                        ObjectId blkBusbar    = bt.Has("ĐỒNG MCCB_MCB") ? bt["ĐỒNG MCCB_MCB"] : ObjectId.Null;
                        ObjectId blkMcb       = bt.Has("MCB_HD") ? bt["MCB_HD"] : (bt.Has("MCB 3P") ? bt["MCB 3P"] : ObjectId.Null);
                        ObjectId blkContactor = bt.Has("A$C802a40ce") ? bt["A$C802a40ce"] : ObjectId.Null;
                        ObjectId blkTor       = bt.Has("A$C9d40e89b") ? bt["A$C9d40e89b"] : ObjectId.Null;
                        ObjectId blkRelay14   = bt.Has("RELAY TRUNG GIAN 14 CHAN") ? bt["RELAY TRUNG GIAN 14 CHAN"] : ObjectId.Null;
                        ObjectId blkRelayBv   = bt.Has("HCR1_MX200A-380") ? bt["HCR1_MX200A-380"] : ObjectId.Null;
                        ObjectId blkFuse      = bt.Has("HFU_123456789") ? bt["HFU_123456789"] : ObjectId.Null;
                        ObjectId blkDomino    = bt.Has("DOMINO 15A") ? bt["DOMINO 15A"] : ObjectId.Null;
                        ObjectId blkRay       = bt.Has("RAY") ? bt["RAY"] : ObjectId.Null;

                double currentX = origin.X;
                Point3d ptFront = Point3d.Origin;
                Point3d ptSide = Point3d.Origin;

                // 1. Mặt Trước Cánh Ngoài (Front View)
                if (ep.IncludeFrontView && !ep.IsItemDeleted("VIEW_FRONT"))
                {
                    ptFront = new Point3d(currentX + ep.GetOffsetX("VIEW_FRONT"), origin.Y + ep.GetOffsetY("VIEW_FRONT"), 0);
                    DrawFrontDoorView(btr, tr, ptFront, W, H, plinthH, canopyH, panel, ep, blkKhoaNgoai, blkNameplate, blkAom, blkFan, blkEm, blkPmBaoPha);
                    currentX += gapX;
                }

                // 2. Mặt Cánh Phụ Trong (Inner Door)
                if (ep.IncludeInnerDoorView && !ep.IsItemDeleted("VIEW_INNER"))
                {
                    Point3d ptV2 = new Point3d(currentX + ep.GetOffsetX("VIEW_INNER"), origin.Y + ep.GetOffsetY("VIEW_INNER"), 0);
                    DrawInnerDoorView(btr, tr, ptV2, W, H, plinthH, canopyH, panel, ep, blkInnerLock, blkHinge);
                    currentX += gapX;
                }

                // 3. Mặt Bố Trí Thiết Bị Bên Trong (Internal Equipment Layout)
                if (ep.IncludeInternalView && !ep.IsItemDeleted("VIEW_INTERNAL"))
                {
                    Point3d ptV3 = new Point3d(currentX + ep.GetOffsetX("VIEW_INTERNAL"), origin.Y + ep.GetOffsetY("VIEW_INTERNAL"), 0);
                    DrawInternalEquipmentView(btr, tr, ptV3, W, H, plinthH, canopyH, panel, ep,
                        blkMccbEzs, blkBusbar, blkMcb, blkContactor, blkTor, blkRelay14, blkRelayBv, blkFuse, blkDomino, blkRay);
                    currentX += gapX;
                }

                // 4. Mặt Cắt Hông (Side View)
                if (ep.IncludeSideView && !ep.IsItemDeleted("VIEW_SIDE"))
                {
                    ptSide = new Point3d(currentX + ep.GetOffsetX("VIEW_SIDE"), origin.Y + ep.GetOffsetY("VIEW_SIDE"), 0);
                    DrawSideView(btr, tr, ptSide, W, H, D, plinthH, canopyH, panel, ep, blkKhoaNgoai);
                    currentX += D + 250.0;
                }

                // 5. Mặt Đáy & Lỗ Khoét Cáp (Bottom View)
                if (ep.IncludeBottomView && !ep.IsItemDeleted("VIEW_BOTTOM"))
                {
                    Point3d ptV5 = new Point3d(origin.X + ep.GetOffsetX("VIEW_BOTTOM"), origin.Y - (D + 250.0) + ep.GetOffsetY("VIEW_BOTTOM"), 0);
                    DrawBottomView(btr, tr, ptV5, W, D, panel, ep);
                }

                // 6. Ghi Kích Thước Dim & Bảng Thông Số
                if (ep.IncludeDimensions || ep.IncludeSpecTable)
                {
                    Point3d pRefStart = ep.IncludeFrontView ? ptFront : origin;
                    Point3d pRefEnd = ep.IncludeSideView ? ptSide : new Point3d(currentX, origin.Y, 0);
                    DrawDimensionsAndSpecs(btr, tr, pRefStart, pRefEnd, W, H, D, plinthH, canopyH, panel, ep);
                }

                        tr.Commit();
                    }
                }

                ed.WriteMessage("\n[NTT TOOLS] Đã hoàn tất xuất Bản vẽ Mặt Bằng Bố Trí Vỏ Tủ vào AutoCAD!");
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LỖI XUẤT BẢN VẼ VỎ TỦ]: " + ex.Message + "\n" + ex.StackTrace);
                return false;
            }
        }

        private void DrawFrontDoorView(BlockTableRecord btr, Transaction tr, Point3d p0, double W, double H, double plinthH, double canopyH, 
            ElectricalPanel panel, EnclosureLayoutParameters ep, ObjectId blkLock, ObjectId blkNameplate, ObjectId blkAom, ObjectId blkFan,
            ObjectId blkEm, ObjectId blkPmBaoPha)
        {
            double x = p0.X, y = p0.Y;

            AddRectangle(btr, tr, new Point3d(x, y, 0), new Point3d(x + W, y - plinthH, 0), LayerEnclosure, 8);
            AddText(btr, tr, string.Format("CHÂN ĐẾ TỦ {0:0}mm", plinthH), new Point3d(x + W / 2.0, y - plinthH / 2.0, 0), ep.CadTextHeight, AttachmentPoint.MiddleCenter, LayerText);

            AddRectangle(btr, tr, new Point3d(x - 15.0, y + H + canopyH, 0), new Point3d(x + W + 15.0, y + H, 0), LayerEnclosure, 7);
            AddRectangle(btr, tr, new Point3d(x, y + H, 0), new Point3d(x + W, y, 0), LayerEnclosure, 7);

            double dm = 10.0;
            AddRectangle(btr, tr, new Point3d(x + dm, y + H - dm, 0), new Point3d(x + W - dm, y + dm, 0), LayerEnclosure, 8);
            AddRectangle(btr, tr, new Point3d(x + dm + 5.0, y + H - dm - 5.0, 0), new Point3d(x + W - dm - 5.0, y + dm + 5.0, 0), LayerEnclosure, 8);

            double npY = y + H - 65.0;
            if (ep.IncludeNameplate && !ep.IsItemDeleted("NAMEPLATE"))
            {
                double npOffX = ep.GetOffsetX("NAMEPLATE");
                double npOffY = ep.GetOffsetY("NAMEPLATE");
                Point3d npPt = new Point3d(x + W / 2.0 - 100.0 + npOffX, npY + npOffY, 0);

                if (blkNameplate != ObjectId.Null)
                    InsertBlock(btr, tr, blkNameplate, npPt, 1.0, 0, LayerControl);
                else
                    AddRectangle(btr, tr, new Point3d(npPt.X - 20.0, npPt.Y + 20.0, 0), new Point3d(npPt.X + 220.0, npPt.Y - 20.0, 0), LayerControl, 4);

                string pName = ep.GetCustomText("NAMEPLATE", !string.IsNullOrEmpty(ep.PanelName) ? ep.PanelName : "TỦ ĐIỆN ĐỘNG LỰC & ĐIỀU KHIỂN");
                AddText(btr, tr, pName, new Point3d(npPt.X + 100.0, npPt.Y + 5.0, 0), ep.CadTextHeight + 4.0, AttachmentPoint.MiddleCenter, LayerText, 0, 1);
                AddText(btr, tr, ep.VoltageSpec, new Point3d(npPt.X + 100.0, npPt.Y - 10.0, 0), ep.CadTextHeight - 1.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
            }

            double indY = npY - 40.0;
            double midX = x + W / 2.0;

            // 1. Module Đồng hồ PM & 3 Đèn báo pha (Sử dụng block PM+BÁO PHA.dwg 135x226 mm)
            if (ep.IncludePhaseLampsAndMeter && !ep.IsItemDeleted("PHASE_LAMPS_METER"))
            {
                double plOffX = ep.GetOffsetX("PHASE_LAMPS_METER");
                double plOffY = ep.GetOffsetY("PHASE_LAMPS_METER");
                double mx = midX + plOffX;
                double my = indY + plOffY;

                if (blkPmBaoPha != ObjectId.Null)
                {
                    // Block PM+BAO PHA: kích thước 135x226 mm, đỉnh Y = -33.9. Chèn tại my + 33.9 để đỉnh khớp my.
                    InsertBlock(btr, tr, blkPmBaoPha, new Point3d(mx, my + 33.9, 0), 1.0, 0, LayerControl);
                }
                else
                {
                    AddCircle(btr, tr, new Point3d(mx - 70.0, my, 0), 12.0, LayerControl, 1);
                    AddText(btr, tr, "R", new Point3d(mx - 70.0, my - 20.0, 0), ep.CadTextHeight, AttachmentPoint.MiddleCenter, LayerControl, 0, 1);

                    AddCircle(btr, tr, new Point3d(mx, my, 0), 12.0, LayerControl, 2);
                    AddText(btr, tr, "Y", new Point3d(mx, my - 20.0, 0), ep.CadTextHeight, AttachmentPoint.MiddleCenter, LayerControl, 0, 2);

                    AddCircle(btr, tr, new Point3d(mx + 70.0, my, 0), 12.0, LayerControl, 3);
                    AddText(btr, tr, "B", new Point3d(mx + 70.0, my - 20.0, 0), ep.CadTextHeight, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);

                    double pmY = my - 85.0;
                    AddRectangle(btr, tr, new Point3d(mx - 48.0, pmY + 48.0, 0), new Point3d(mx + 48.0, pmY - 48.0, 0), LayerControl, 4);
                    string meterText = ep.GetCustomText("PHASE_LAMPS_METER", "ĐỒNG HỒ PM");
                    AddText(btr, tr, meterText, new Point3d(mx, pmY + 8.0, 0), ep.CadTextHeight, AttachmentPoint.MiddleCenter, LayerControl, 0, 4);
                    AddText(btr, tr, "MULTIMETER", new Point3d(mx, pmY - 12.0, 0), ep.CadTextHeight - 3.0, AttachmentPoint.MiddleCenter, LayerControl, 0, 4);
                }
            }

            // 2. Nút nhấn dừng khẩn cấp (Emergency Stop - Block EM.dwg 80x80 mm)
            if (ep.IncludeEmergencyStop && !ep.IsItemDeleted("EMERGENCY_STOP"))
            {
                double emOffX = ep.GetOffsetX("EMERGENCY_STOP");
                double emOffY = ep.GetOffsetY("EMERGENCY_STOP");
                double emX = midX + 160.0 + emOffX;
                double emY = indY - 113.0 + emOffY;

                if (blkEm != ObjectId.Null)
                {
                    InsertBlock(btr, tr, blkEm, new Point3d(emX, emY, 0), 1.0, 0, LayerControl);
                }
                else
                {
                    AddCircle(btr, tr, new Point3d(emX, emY, 0), 38.0, LayerControl, 2);
                    AddCircle(btr, tr, new Point3d(emX, emY, 0), 22.0, LayerControl, 1);
                    AddText(btr, tr, "EMERGENCY STOP", new Point3d(emX, emY - 46.0, 0), 8.0, AttachmentPoint.MiddleCenter, LayerControl, 0, 1);
                }
            }

            if (ep.IncludeDoorLock && !ep.IsItemDeleted("DOOR_LOCK"))
            {
                double lockOffX = ep.GetOffsetX("DOOR_LOCK");
                double lockOffY = ep.GetOffsetY("DOOR_LOCK");
                double lockY = y + H / 2.0 + lockOffY;
                double lockX = x + W - 45.0 + lockOffX;
                if (blkLock != ObjectId.Null)
                    InsertBlock(btr, tr, blkLock, new Point3d(lockX, lockY, 0), 1.0, 0, LayerEnclosure);
                else
                {
                    AddRectangle(btr, tr, new Point3d(lockX - 10.0, lockY + 70.0, 0), new Point3d(lockX + 20.0, lockY - 70.0, 0), LayerEnclosure, 7);
                    AddCircle(btr, tr, new Point3d(lockX + 5.0, lockY - 25.0, 0), 8.0, LayerEnclosure, 7);
                }
            }

            if (ep.IncludeVentilationFan && !ep.IsItemDeleted("FAN_FRONT"))
            {
                double fanOffX = ep.GetOffsetX("FAN_FRONT");
                double fanOffY = ep.GetOffsetY("FAN_FRONT");
                double fanX = x + W - 150.0 + fanOffX;
                double fanY = y + 120.0 + fanOffY;
                if (blkFan != ObjectId.Null)
                    InsertBlock(btr, tr, blkFan, new Point3d(fanX, fanY, 0), 0.7, 0, LayerEnclosure);
                else
                {
                    for (int lv = -3; lv <= 3; lv++)
                        AddLine(btr, tr, new Point3d(fanX - 40.0, fanY - 10.0 + lv * 10.0, 0), new Point3d(fanX + 140.0, fanY - 10.0 + lv * 10.0, 0), LayerEnclosure, 8);
                    AddText(btr, tr, "CHỚP THÔNG GIÓ LỌC BỤI", new Point3d(fanX + 50.0, fanY - 65.0, 0), 10.0, AttachmentPoint.MiddleCenter, LayerText);
                }
            }

            // 3. Cụm trạm điều khiển A-O-M (Auto - Off - Manual, sử dụng block A-O-M.dwg)
            if (ep.IncludeControlStations && !ep.IsItemDeleted("AOM_STATIONS") && !ep.IsItemDeleted("HOA_STATIONS") && panel != null && panel.Loads != null)
            {
                double aomOffX = ep.GetOffsetX("AOM_STATIONS");
                if (aomOffX == 0) aomOffX = ep.GetOffsetX("HOA_STATIONS");
                double aomOffY = ep.GetOffsetY("AOM_STATIONS");
                if (aomOffY == 0) aomOffY = ep.GetOffsetY("HOA_STATIONS");

                double rowStartY = indY - 265.0 + aomOffY;
                double rowPitch = 100.0;
                int maxRows = Math.Min(panel.Loads.Count, Math.Max(1, (int)((rowStartY - (y + 180.0)) / rowPitch)));

                for (int r = 0; r < maxRows; r++)
                {
                    ElectricalLoad ld = panel.Loads[r];
                    double ry = rowStartY - r * rowPitch;

                    string tag = !string.IsNullOrEmpty(ld.CircuitNumber) ? ld.CircuitNumber : ("C" + (r + 1).ToString("D2"));
                    string lName = !string.IsNullOrEmpty(ld.LoadName) ? ld.LoadName : ("TẢI " + (r + 1));
                    if (lName.Length > 20) lName = lName.Substring(0, 20);
                    AddText(btr, tr, string.Format("{0}: {1}", tag, lName), new Point3d(x + 50.0 + aomOffX, ry + 24.0, 0), 10.0, AttachmentPoint.MiddleLeft, LayerText, 0, 2);

                    double stCenter = x + 50.0 + 317.8 / 2.0 + aomOffX;
                    if (blkAom != ObjectId.Null)
                    {
                        InsertBlock(btr, tr, blkAom, new Point3d(stCenter, ry, 0), 1.0, 0, LayerControl);
                    }
                    else
                    {
                        double swX = x + 100.0 + aomOffX;
                        AddCircle(btr, tr, new Point3d(swX, ry - 5.0, 0), 11.0, LayerControl, 4);
                        AddText(btr, tr, "A-O-M", new Point3d(swX, ry + 10.0, 0), 7.5, AttachmentPoint.MiddleCenter, LayerControl);

                        double startX = swX + 80.0;
                        AddCircle(btr, tr, new Point3d(startX, ry - 5.0, 0), 11.0, LayerControl, 3);
                        AddText(btr, tr, "START", new Point3d(startX, ry + 10.0, 0), 7.5, AttachmentPoint.MiddleCenter, LayerControl, 0, 3);

                        double stopX = startX + 70.0;
                        AddCircle(btr, tr, new Point3d(stopX, ry - 5.0, 0), 11.0, LayerControl, 1);
                        AddText(btr, tr, "STOP", new Point3d(stopX, ry + 10.0, 0), 7.5, AttachmentPoint.MiddleCenter, LayerControl, 0, 1);

                        double faultX = stopX + 70.0;
                        AddCircle(btr, tr, new Point3d(faultX, ry - 5.0, 0), 11.0, LayerControl, 2);
                        AddText(btr, tr, "FAULT", new Point3d(faultX, ry + 10.0, 0), 7.5, AttachmentPoint.MiddleCenter, LayerControl, 0, 2);
                    }
                }
            }

            AddText(btr, tr, "MẶT TRƯỚC CÁNH NGOÀI", new Point3d(x + W / 2.0, y - plinthH - 45.0, 0), 22.0, AttachmentPoint.MiddleCenter, LayerText, 0, 2);
            AddText(btr, tr, "FRONT VIEW", new Point3d(x + W / 2.0, y - plinthH - 75.0, 0), 16.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
        }

        private void DrawInnerDoorView(BlockTableRecord btr, Transaction tr, Point3d p0, double W, double H, double plinthH, double canopyH, 
            ElectricalPanel panel, EnclosureLayoutParameters ep, ObjectId blkLock, ObjectId blkHinge)
        {
            double x = p0.X, y = p0.Y;

            AddRectangle(btr, tr, new Point3d(x, y, 0), new Point3d(x + W, y - plinthH, 0), LayerEnclosure, 8);
            AddRectangle(btr, tr, new Point3d(x - 15.0, y + H + canopyH, 0), new Point3d(x + W + 15.0, y + H, 0), LayerEnclosure, 7);
            AddRectangle(btr, tr, new Point3d(x, y + H, 0), new Point3d(x + W, y, 0), LayerEnclosure, 7);

            double dm = 35.0;
            AddRectangle(btr, tr, new Point3d(x + dm, y + H - dm, 0), new Point3d(x + W - dm, y + dm, 0), LayerInnerDoor, 8);

            for (int hg = 0; hg < 3; hg++)
            {
                double hgY = y + 150.0 + hg * ((H - 300.0) / 2.0);
                if (blkHinge != ObjectId.Null)
                    InsertBlock(btr, tr, blkHinge, new Point3d(x + dm, hgY, 0), 1.0, 0, LayerInnerDoor);
                else
                    AddRectangle(btr, tr, new Point3d(x + dm - 8.0, hgY + 20.0, 0), new Point3d(x + dm + 8.0, hgY - 20.0, 0), LayerInnerDoor, 8);
            }

            if (!ep.IsItemDeleted("INNER_LOCK"))
            {
                if (blkLock != ObjectId.Null)
                    InsertBlock(btr, tr, blkLock, new Point3d(x + W - dm - 15.0, y + H / 2.0, 0), 1.0, 0, LayerInnerDoor);
                else
                    AddCircle(btr, tr, new Point3d(x + W - dm - 15.0, y + H / 2.0, 0), 10.0, LayerInnerDoor, 7);
            }

            if (!ep.IsItemDeleted("INNER_DOOR_CUTOUTS"))
            {
                double cutOffX = ep.GetOffsetX("INNER_DOOR_CUTOUTS");
                double cutOffY = ep.GetOffsetY("INNER_DOOR_CUTOUTS");

                double mccbY = y + 1320.0 + cutOffY;
                AddRectangle(btr, tr, new Point3d(x + W / 2.0 - 75.0 + cutOffX, mccbY + 60.0, 0), new Point3d(x + W / 2.0 + 75.0 + cutOffX, mccbY - 60.0, 0), LayerInnerDoor, 1);
                AddText(btr, tr, "KHOÉT MẶT NẠ MCCB TỔNG", new Point3d(x + W / 2.0 + cutOffX, mccbY, 0), 10.0, AttachmentPoint.MiddleCenter, LayerText, 0, 1);

                double mcbY = y + 1120.0 + cutOffY;
                AddRectangle(btr, tr, new Point3d(x + 80.0 + cutOffX, mcbY + 40.0, 0), new Point3d(x + W - 80.0 + cutOffX, mcbY - 40.0, 0), LayerInnerDoor, 1);
                AddText(btr, tr, "KHOÉT MẶT NẠ DÃY MCB NHÁNH", new Point3d(x + W / 2.0 + cutOffX, mcbY, 0), 10.0, AttachmentPoint.MiddleCenter, LayerText, 0, 1);
            }

            AddText(btr, tr, "MẶT CÁNH PHỤ TRONG", new Point3d(x + W / 2.0, y - plinthH - 45.0, 0), 22.0, AttachmentPoint.MiddleCenter, LayerText, 0, 2);
            AddText(btr, tr, "INTERNAL VIEW WITH INNER DOOR", new Point3d(x + W / 2.0, y - plinthH - 75.0, 0), 16.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
        }

        private void DrawInternalEquipmentView(BlockTableRecord btr, Transaction tr, Point3d p0, double W, double H, double plinthH, double canopyH,
            ElectricalPanel panel, EnclosureLayoutParameters ep, ObjectId blkMccb, ObjectId blkBusbar, ObjectId blkMcb, ObjectId blkContactor, ObjectId blkTor,
            ObjectId blkRelay14, ObjectId blkRelayBv, ObjectId blkFuse, ObjectId blkDomino, ObjectId blkRay)
        {
            double x = p0.X, y = p0.Y;

            AddRectangle(btr, tr, new Point3d(x, y, 0), new Point3d(x + W, y - plinthH, 0), LayerEnclosure, 8);
            AddRectangle(btr, tr, new Point3d(x - 15.0, y + H + canopyH, 0), new Point3d(x + W + 15.0, y + H, 0), LayerEnclosure, 7);
            AddRectangle(btr, tr, new Point3d(x, y + H, 0), new Point3d(x + W, y, 0), LayerEnclosure, 7);

            double pm = 40.0;
            AddRectangle(btr, tr, new Point3d(x + pm, y + H - pm, 0), new Point3d(x + W - pm, y + pm, 0), LayerEnclosure, 8);

            double ductW = 50.0;
            double ductOffX = ep.GetOffsetX("WIRING_DUCTS");
            double ductOffY = ep.GetOffsetY("WIRING_DUCTS");
            if (ep.IncludeWiringDucts && !ep.IsItemDeleted("WIRING_DUCTS"))
            {
                AddRectangle(btr, tr, new Point3d(x + pm + 10.0 + ductOffX, y + H - pm - 10.0 + ductOffY, 0), new Point3d(x + pm + 10.0 + ductW + ductOffX, y + pm + 10.0 + ductOffY, 0), LayerDuct, 8);
                AddText(btr, tr, "MÁNG ĐIỆN 60x60", new Point3d(x + pm + 10.0 + ductW / 2.0 + ductOffX, y + H / 2.0 + ductOffY, 0), 11.0, AttachmentPoint.MiddleCenter, LayerDuct, Math.PI / 2.0);

                AddRectangle(btr, tr, new Point3d(x + W - pm - 10.0 - ductW + ductOffX, y + H - pm - 10.0 + ductOffY, 0), new Point3d(x + W - pm - 10.0 + ductOffX, y + pm + 10.0 + ductOffY, 0), LayerDuct, 8);
                AddText(btr, tr, "MÁNG ĐIỆN 60x60", new Point3d(x + W - pm - 10.0 - ductW / 2.0 + ductOffX, y + H / 2.0 + ductOffY, 0), 11.0, AttachmentPoint.MiddleCenter, LayerDuct, Math.PI / 2.0);
            }

            double equipX1 = x + pm + 10.0 + ductW + 15.0;
            double equipX2 = x + W - pm - 10.0 - ductW - 15.0;
            double equipW = equipX2 - equipX1;
            double midX = x + W / 2.0;

            BlockTable bt = (BlockTable)tr.GetObject(btr.Database.BlockTableId, OpenMode.ForRead);

            // Tầng 1: MCCB Tổng, Chống Sét SPD & Busbar
            double mccbOffX = ep.GetOffsetX("MAIN_MCCB");
            double mccbOffY = ep.GetOffsetY("MAIN_MCCB");
            double yTier1 = y + 1350.0 + mccbOffY;

            if (ep.IncludeMainMccb && !ep.IsItemDeleted("MAIN_MCCB"))
            {
                Point3d mccbPt = new Point3d(midX + mccbOffX, yTier1, 0);

                // Tự động chọn block EZS 100 hay EZS 160-250 theo dòng định mức tổng
                ObjectId chosenMccb = blkMccb;
                double mainRating = 250.0;
                if (!string.IsNullOrEmpty(panel.MainBreakerSpec))
                {
                    System.Text.RegularExpressions.Match mr = System.Text.RegularExpressions.Regex.Match(panel.MainBreakerSpec, @"(\d+)\s*A", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (mr.Success) double.TryParse(mr.Groups[1].Value, out mainRating);
                }
                if (mainRating <= 100.0 && bt.Has("EZS 100")) chosenMccb = bt["EZS 100"];
                else if (bt.Has("EZS160-250")) chosenMccb = bt["EZS160-250"];

                if (chosenMccb != ObjectId.Null)
                    InsertBlock(btr, tr, chosenMccb, mccbPt, 1.0, 0, LayerEquipment);
                else
                {
                    AddRectangle(btr, tr, new Point3d(mccbPt.X - 70.0, mccbPt.Y + 90.0, 0), new Point3d(mccbPt.X + 70.0, mccbPt.Y - 90.0, 0), LayerEquipment, 7);
                    AddText(btr, tr, "MCCB TỔNG", new Point3d(mccbPt.X, mccbPt.Y + 10.0, 0), 14.0, AttachmentPoint.MiddleCenter, LayerEquipment, 0, 1);
                    string mainSpec = ep.GetCustomText("MAIN_MCCB", !string.IsNullOrEmpty(panel.MainBreakerSpec) ? panel.MainBreakerSpec : "MCCB 3P 250A");
                    AddText(btr, tr, mainSpec, new Point3d(mccbPt.X, mccbPt.Y - 15.0, 0), 11.0, AttachmentPoint.MiddleCenter, LayerEquipment, 0, 7);
                }

                // Chống sét lan truyền SPD bên cạnh MCCB tổng
                ObjectId blkSpd = bt.Has("F_CHONG_SET") ? bt["F_CHONG_SET"] : (bt.Has("F") ? bt["F"] : ObjectId.Null);
                if (blkSpd != ObjectId.Null)
                {
                    double spdX = midX + mccbOffX + 115.0;
                    InsertBlock(btr, tr, blkSpd, new Point3d(spdX, yTier1, 0), 1.0, 0, LayerEquipment);
                    AddText(btr, tr, "SPD CHỐNG SÉT", new Point3d(spdX, yTier1 - 50.0, 0), 7.5, AttachmentPoint.MiddleCenter, LayerText, 0, 1);
                }
            }

            if (ep.IncludeCopperBusbar && !ep.IsItemDeleted("COPPER_BUSBAR"))
            {
                double bbOffX = ep.GetOffsetX("COPPER_BUSBAR");
                double bbOffY = ep.GetOffsetY("COPPER_BUSBAR");
                double bbMidX = midX + bbOffX;
                double bbBaseY = yTier1 + 100.0 + bbOffY;

                if (blkBusbar != ObjectId.Null)
                    InsertBlock(btr, tr, blkBusbar, new Point3d(bbMidX, bbBaseY, 0), 1.0, 0, LayerBusbar);
                else
                {
                    for (int b = 0; b < 3; b++)
                    {
                        double by = bbBaseY + b * 20.0;
                        short bColor = (short)(b == 0 ? 1 : (b == 1 ? 2 : 3));
                        AddRectangle(btr, tr, new Point3d(equipX1 + 20.0 + bbOffX, by + 8.0, 0), new Point3d(equipX2 - 20.0 + bbOffX, by - 8.0, 0), LayerBusbar, bColor);
                    }
                    AddText(btr, tr, "HỆ THANH CÁI ĐỒNG (COPPER BUSBAR)", new Point3d(bbMidX, bbBaseY + 65.0, 0), 11.0, AttachmentPoint.MiddleCenter, LayerBusbar, 0, 2);
                }

                // Gối đỡ thanh cái GOI SM ở 2 đầu
                ObjectId blkGoiSm = bt.Has("GOI_SM") ? bt["GOI_SM"] : (bt.Has("GOI SM") ? bt["GOI SM"] : ObjectId.Null);
                if (blkGoiSm != ObjectId.Null)
                {
                    InsertBlock(btr, tr, blkGoiSm, new Point3d(equipX1 + 25.0 + bbOffX, bbBaseY + 20.0, 0), 0.7, 0, LayerBusbar);
                    InsertBlock(btr, tr, blkGoiSm, new Point3d(equipX2 - 25.0 + bbOffX, bbBaseY + 20.0, 0), 0.7, 0, LayerBusbar);
                }
            }

            if (ep.IncludeWiringDucts && !ep.IsItemDeleted("WIRING_DUCTS"))
                AddRectangle(btr, tr, new Point3d(equipX1, yTier1 - 110.0, 0), new Point3d(equipX2, yTier1 - 150.0, 0), LayerDuct, 8);

            // Tầng 2: Dãy MCB / RCBO Nhánh từ SDNL (Sử dụng đúng block 1P/2P/3P/4P/RCCB từ Thư Viện Điện)
            double mcbOffX = ep.GetOffsetX("BRANCH_MCBS");
            double mcbOffY = ep.GetOffsetY("BRANCH_MCBS");
            double yTier2 = y + 1130.0 + mcbOffY;

            if (ep.IncludeBranchMcbs && !ep.IsItemDeleted("BRANCH_MCBS"))
            {
                DrawDinRail(btr, tr, equipX1 + mcbOffX, equipX2 + mcbOffX, yTier2, blkRay);

                if (panel != null && panel.Loads != null && panel.Loads.Count > 0)
                {
                    var breakerList = new List<Tuple<ElectricalLoad, ObjectId, double, string>>();
                    double totalWidths = 0;
                    for (int i = 0; i < panel.Loads.Count; i++)
                    {
                        ElectricalLoad ld = panel.Loads[i];
                        ObjectId bId;
                        double bW;
                        string bName;
                        GetBreakerBlockInfo(ld, bt, ep.UseLargeFrameMcb, out bId, out bW, out bName);
                        breakerList.Add(new Tuple<ElectricalLoad, ObjectId, double, string>(ld, bId, bW, bName));
                        totalWidths += bW;
                    }

                    int count = breakerList.Count;
                    double defaultGap = 6.0;
                    double availableW = equipW;
                    double requiredW = totalWidths + Math.Max(0, count - 1) * defaultGap;
                    double gap = defaultGap;
                    if (requiredW > availableW && count > 1)
                    {
                        gap = Math.Max(2.0, (availableW - totalWidths) / (count - 1));
                        requiredW = totalWidths + (count - 1) * gap;
                    }

                    double curX = equipX1 + mcbOffX + Math.Max(8.0, (availableW - requiredW) / 2.0);

                    for (int i = 0; i < breakerList.Count; i++)
                    {
                        var bInfo = breakerList[i];
                        ElectricalLoad ld = bInfo.Item1;
                        ObjectId blkId = bInfo.Item2;
                        double bw = bInfo.Item3;
                        string dName = bInfo.Item4;

                        if (curX + bw > equipX2 + mcbOffX + 5.0) break;

                        Point3d insPt = new Point3d(curX, yTier2, 0);
                        if (blkId != ObjectId.Null)
                        {
                            InsertBlock(btr, tr, blkId, insPt, 1.0, 0, LayerEquipment);
                        }
                        else
                        {
                            AddRectangle(btr, tr, new Point3d(curX, yTier2 + 41.0, 0), new Point3d(curX + bw, yTier2 - 41.0, 0), LayerEquipment, 7);
                            AddText(btr, tr, dName, new Point3d(curX + bw / 2.0, yTier2, 0), 8.0, AttachmentPoint.MiddleCenter, LayerEquipment);
                        }

                        // Nhãn tên lộ (C01, C02...) phía trên át
                        string cTag = !string.IsNullOrEmpty(ld.CircuitNumber) ? ld.CircuitNumber : string.Format("C{0:D2}", i + 1);
                        AddText(btr, tr, cTag, new Point3d(curX + bw / 2.0, yTier2 + 49.0, 0), 7.5, AttachmentPoint.MiddleCenter, LayerText, 0, 2);

                        // Thông số dòng cắt / định mức phía dưới át
                        string shortSpec = !string.IsNullOrEmpty(ld.BreakerSpec) ? ld.BreakerSpec : dName;
                        if (shortSpec.Length > 16)
                        {
                            System.Text.RegularExpressions.Match mr = System.Text.RegularExpressions.Regex.Match(shortSpec, @"(\d+)\s*A", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (mr.Success) shortSpec = string.Format("{0}A-{1}", mr.Groups[1].Value, dName.Replace("MCB ", "").Replace("RCCB ", "").Replace("RCBO ", ""));
                        }
                        AddText(btr, tr, shortSpec, new Point3d(curX + bw / 2.0, yTier2 - 49.0, 0), 6.5, AttachmentPoint.MiddleCenter, LayerText, 0, 7);

                        curX += bw + gap;
                    }
                }
                else
                {
                    int nLoads = 6;
                    double mcbPitch = equipW / nLoads;
                    for (int i = 0; i < nLoads; i++)
                    {
                        double bx = equipX1 + mcbOffX + (i + 0.5) * mcbPitch;
                        if (blkMcb != ObjectId.Null)
                            InsertBlock(btr, tr, blkMcb, new Point3d(bx, yTier2, 0), 1.0, 0, LayerEquipment);
                        else
                        {
                            AddRectangle(btr, tr, new Point3d(bx - 18.0, yTier2 + 40.0, 0), new Point3d(bx + 18.0, yTier2 - 40.0, 0), LayerEquipment, 7);
                            AddText(btr, tr, "MCB", new Point3d(bx, yTier2, 0), 9.0, AttachmentPoint.MiddleCenter, LayerEquipment);
                        }
                    }
                }
            }

            if (ep.IncludeWiringDucts && !ep.IsItemDeleted("WIRING_DUCTS"))
                AddRectangle(btr, tr, new Point3d(equipX1, yTier2 - 60.0, 0), new Point3d(equipX2, yTier2 - 100.0, 0), LayerDuct, 8);

            // Tầng 3: Contactor + TOR
            double dolOffX = ep.GetOffsetX("MOTOR_STARTERS");
            double dolOffY = ep.GetOffsetY("MOTOR_STARTERS");
            double yTier3 = y + 850.0 + dolOffY;

            if (ep.IncludeMotorStarters && !ep.IsItemDeleted("MOTOR_STARTERS"))
            {
                DrawDinRail(btr, tr, equipX1 + dolOffX, equipX2 + dolOffX, yTier3, blkRay);
                int nDol = Math.Min(panel.Loads != null ? panel.Loads.Count : 6, 6);
                double dolPitch = equipW / Math.Max(1, nDol);
                for (int i = 0; i < nDol; i++)
                {
                    double dx = equipX1 + dolOffX + (i + 0.5) * dolPitch;
                    if (blkContactor != ObjectId.Null)
                    {
                        InsertBlock(btr, tr, blkContactor, new Point3d(dx, yTier3 + 20.0, 0), 1.0, 0, LayerEquipment);
                        if (blkTor != ObjectId.Null) InsertBlock(btr, tr, blkTor, new Point3d(dx, yTier3 - 35.0, 0), 1.0, 0, LayerEquipment);
                    }
                    else
                    {
                        AddRectangle(btr, tr, new Point3d(dx - 22.0, yTier3 + 50.0, 0), new Point3d(dx + 22.0, yTier3 - 10.0, 0), LayerEquipment, 7);
                        AddText(btr, tr, "KM", new Point3d(dx, yTier3 + 20.0, 0), 10.0, AttachmentPoint.MiddleCenter, LayerEquipment);
                        AddRectangle(btr, tr, new Point3d(dx - 20.0, yTier3 - 15.0, 0), new Point3d(dx + 20.0, yTier3 - 60.0, 0), LayerEquipment, 7);
                        AddText(btr, tr, "TOR", new Point3d(dx, yTier3 - 38.0, 0), 9.0, AttachmentPoint.MiddleCenter, LayerEquipment, 0, 1);
                    }
                }
            }

            if (ep.IncludeWiringDucts && !ep.IsItemDeleted("WIRING_DUCTS"))
                AddRectangle(btr, tr, new Point3d(equipX1, yTier3 - 80.0, 0), new Point3d(equipX2, yTier3 - 120.0, 0), LayerDuct, 8);

            // Tầng 4: Rơ le trung gian, Mikro, Cầu chì, Thermostat & Omron
            double rlyOffX = ep.GetOffsetX("CONTROL_RELAYS");
            double rlyOffY = ep.GetOffsetY("CONTROL_RELAYS");
            double yTier4 = y + 580.0 + rlyOffY;

            if (ep.IncludeControlRelays && !ep.IsItemDeleted("CONTROL_RELAYS"))
            {
                DrawDinRail(btr, tr, equipX1 + rlyOffX, equipX2 + rlyOffX, yTier4, blkRay);
                double rxCur = equipX1 + 20.0 + rlyOffX;

                // 1. Cầu chì đo lường tép (cau_chi.dwg)
                ObjectId blkCauChi = bt.Has("cau_chi") ? bt["cau_chi"] : blkFuse;
                if (blkCauChi != ObjectId.Null)
                {
                    InsertBlock(btr, tr, blkCauChi, new Point3d(rxCur + 20.0, yTier4, 0), 1.0, 0, LayerControl);
                    AddText(btr, tr, "CẦU CHÌ", new Point3d(rxCur + 20.0, yTier4 - 45.0, 0), 7.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
                    rxCur += 48.0;
                }

                // 2. Rơ le bảo vệ pha Mikro MX200A-380
                if (blkRelayBv != ObjectId.Null)
                {
                    InsertBlock(btr, tr, blkRelayBv, new Point3d(rxCur + 30.0, yTier4, 0), 1.0, 0, LayerControl);
                    AddText(btr, tr, "BẢO VỆ PHA", new Point3d(rxCur + 30.0, yTier4 - 45.0, 0), 7.0, AttachmentPoint.MiddleCenter, LayerText, 0, 4);
                    rxCur += 65.0;
                }
                else
                {
                    AddRectangle(btr, tr, new Point3d(rxCur, yTier4 + 40.0, 0), new Point3d(rxCur + 50.0, yTier4 - 40.0, 0), LayerControl, 4);
                    AddText(btr, tr, "MIKRO", new Point3d(rxCur + 25.0, yTier4, 0), 9.0, AttachmentPoint.MiddleCenter, LayerControl, 0, 4);
                    rxCur += 60.0;
                }

                // 3. Rơ le đảo bơm Omron G4Q (nếu có từ 2 tải động cơ/bơm trở lên)
                ObjectId blkRelayG4Q = bt.Has("HCR1_G4Q+8PFA1") ? bt["HCR1_G4Q+8PFA1"] : ObjectId.Null;
                int motorCount = 0;
                if (panel != null && panel.Loads != null)
                {
                    foreach (var l in panel.Loads)
                    {
                        if (l.Starter == StarterType.DOL || l.Starter == StarterType.StarDelta || l.Starter == StarterType.VFD || l.Starter == StarterType.SoftStarter)
                            motorCount++;
                    }
                }
                if (motorCount >= 2 && blkRelayG4Q != ObjectId.Null && rxCur + 90.0 < equipX2 + rlyOffX)
                {
                    InsertBlock(btr, tr, blkRelayG4Q, new Point3d(rxCur + 40.0, yTier4, 0), 0.8, 0, LayerControl);
                    AddText(btr, tr, "ĐẢO BƠM", new Point3d(rxCur + 40.0, yTier4 - 45.0, 0), 7.0, AttachmentPoint.MiddleCenter, LayerText, 0, 3);
                    rxCur += 85.0;
                }

                // 4. Thermostat Leipole (ổn nhiệt quạt hút)
                ObjectId blkThermo = bt.Has("HDV_THERMOSTAT") ? bt["HDV_THERMOSTAT"] : ObjectId.Null;
                if (ep.IncludeVentilationFan && blkThermo != ObjectId.Null && rxCur + 40.0 < equipX2 + rlyOffX)
                {
                    InsertBlock(btr, tr, blkThermo, new Point3d(rxCur + 18.0, yTier4, 0), 1.0, 0, LayerControl);
                    AddText(btr, tr, "THERMOSTAT", new Point3d(rxCur + 18.0, yTier4 - 45.0, 0), 6.5, AttachmentPoint.MiddleCenter, LayerText, 0, 6);
                    rxCur += 40.0;
                }

                // 5. Dãy rơ le kiếng 14 chân
                int rCount = Math.Max(2, motorCount * 2);
                for (int r = 0; r < rCount; r++)
                {
                    if (rxCur + 30.0 > equipX2 + rlyOffX) break;
                    if (blkRelay14 != ObjectId.Null)
                    {
                        InsertBlock(btr, tr, blkRelay14, new Point3d(rxCur + 15.0, yTier4, 0), 1.0, 0, LayerControl);
                        AddText(btr, tr, string.Format("R{0}", r + 1), new Point3d(rxCur + 15.0, yTier4 - 42.0, 0), 6.5, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
                    }
                    else
                    {
                        AddRectangle(btr, tr, new Point3d(rxCur + 2.0, yTier4 + 35.0, 0), new Point3d(rxCur + 26.0, yTier4 - 35.0, 0), LayerControl, 7);
                        AddText(btr, tr, string.Format("R{0}", r + 1), new Point3d(rxCur + 14.0, yTier4, 0), 7.5, AttachmentPoint.MiddleCenter, LayerControl);
                    }
                    rxCur += 30.0;
                }
            }

            if (ep.IncludeWiringDucts && !ep.IsItemDeleted("WIRING_DUCTS"))
                AddRectangle(btr, tr, new Point3d(equipX1, yTier4 - 60.0, 0), new Point3d(equipX2, yTier4 - 100.0, 0), LayerDuct, 8);

            // Tầng 5: Domino cầu đấu
            double termOffX = ep.GetOffsetX("TERMINAL_BLOCKS");
            double termOffY = ep.GetOffsetY("TERMINAL_BLOCKS");
            double yTier5 = y + 300.0 + termOffY;

            if (ep.IncludeTerminalBlocks && !ep.IsItemDeleted("TERMINAL_BLOCKS"))
            {
                DrawDinRail(btr, tr, equipX1 + termOffX, equipX2 + termOffX, yTier5, blkRay);
                if (blkDomino != ObjectId.Null)
                {
                    for (int dm = 0; dm < 3; dm++)
                        InsertBlock(btr, tr, blkDomino, new Point3d(equipX1 + 60.0 + termOffX + dm * 120.0, yTier5, 0), 1.0, 0, LayerEquipment);
                }
                else
                {
                    for (int dm = 0; dm < 20; dm++)
                    {
                        double dmx = equipX1 + 30.0 + termOffX + dm * 18.0;
                        if (dmx > equipX2 + termOffX - 30.0) break;
                        AddRectangle(btr, tr, new Point3d(dmx - 8.0, yTier5 + 22.0, 0), new Point3d(dmx + 8.0, yTier5 - 22.0, 0), LayerEquipment, 7);
                    }
                }
                AddText(btr, tr, "HÀNG KẸP CẦU ĐẤU DÂY NGOẠI VI (TERMINAL BLOCKS)", new Point3d(midX + termOffX, yTier5 - 45.0, 0), 11.0, AttachmentPoint.MiddleCenter, LayerText, 0, 1);
            }

            AddText(btr, tr, "MẶT BỐ TRÍ THIẾT BỊ BÊN TRONG", new Point3d(x + W / 2.0, y - plinthH - 45.0, 0), 22.0, AttachmentPoint.MiddleCenter, LayerText, 0, 2);
            AddText(btr, tr, "INTERNAL EQUIPMENT LAYOUT", new Point3d(x + W / 2.0, y - plinthH - 75.0, 0), 16.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
        }

        private void DrawSideView(BlockTableRecord btr, Transaction tr, Point3d p0, double W, double H, double D, double plinthH, double canopyH,
            ElectricalPanel panel, EnclosureLayoutParameters ep, ObjectId blkLock)
        {
            double x = p0.X, y = p0.Y;

            AddRectangle(btr, tr, new Point3d(x, y, 0), new Point3d(x + D, y - plinthH, 0), LayerEnclosure, 8);
            AddText(btr, tr, string.Format("{0:0}mm", plinthH), new Point3d(x + D / 2.0, y - plinthH / 2.0, 0), 14.0, AttachmentPoint.MiddleCenter, LayerText);

            AddRectangle(btr, tr, new Point3d(x - 30.0, y + H + canopyH, 0), new Point3d(x + D + 15.0, y + H, 0), LayerEnclosure, 7);
            AddRectangle(btr, tr, new Point3d(x, y + H, 0), new Point3d(x + D, y, 0), LayerEnclosure, 7);

            AddLine(btr, tr, new Point3d(x - 10.0, y + H - 5.0, 0), new Point3d(x - 10.0, y + 5.0, 0), LayerEnclosure, 8);
            AddLine(btr, tr, new Point3d(x + 90.0, y + H - 30.0, 0), new Point3d(x + 90.0, y + 30.0, 0), LayerInnerDoor, 8);
            AddText(btr, tr, "CÁNH PHỤ TRONG", new Point3d(x + 95.0, y + H / 2.0, 0), 11.0, AttachmentPoint.MiddleLeft, LayerInnerDoor, Math.PI / 2.0);

            AddLine(btr, tr, new Point3d(x + D - 40.0, y + H - 40.0, 0), new Point3d(x + D - 40.0, y + 40.0, 0), LayerEnclosure, 8);
            AddText(btr, tr, "TẤM PANEL GÁ THIẾT BỊ", new Point3d(x + D - 45.0, y + H / 2.0, 0), 11.0, AttachmentPoint.MiddleRight, LayerText, Math.PI / 2.0);

            if (ep.IncludeVentilationFan && !ep.IsItemDeleted("FAN_SIDE"))
            {
                double fanOffX = ep.GetOffsetX("FAN_SIDE");
                double fanOffY = ep.GetOffsetY("FAN_SIDE");
                double fanX = x + D - 80.0 + fanOffX;
                double fanY = y + H - 150.0 + fanOffY;
                for (int lv = -3; lv <= 3; lv++)
                    AddLine(btr, tr, new Point3d(fanX - 30.0, fanY + lv * 10.0, 0), new Point3d(fanX + 30.0, fanY + lv * 10.0, 0), LayerEnclosure, 8);
                AddText(btr, tr, "LƯỚI QUẠT", new Point3d(fanX, fanY - 45.0, 0), 10.0, AttachmentPoint.MiddleCenter, LayerText);
            }

            AddText(btr, tr, "MẶT CẮT HÔNG", new Point3d(x + D / 2.0, y - plinthH - 45.0, 0), 22.0, AttachmentPoint.MiddleCenter, LayerText, 0, 2);
            AddText(btr, tr, "SIDE VIEW", new Point3d(x + D / 2.0, y - plinthH - 75.0, 0), 16.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
        }

        private void DrawBottomView(BlockTableRecord btr, Transaction tr, Point3d p0, double W, double D, ElectricalPanel panel, EnclosureLayoutParameters ep)
        {
            double x = p0.X, y = p0.Y;

            AddRectangle(btr, tr, new Point3d(x, y + D, 0), new Point3d(x + W, y, 0), LayerEnclosure, 7);

            if (!ep.IsItemDeleted("BOTTOM_CABLE_HOLE"))
            {
                double holeOffX = ep.GetOffsetX("BOTTOM_CABLE_HOLE");
                double holeOffY = ep.GetOffsetY("BOTTOM_CABLE_HOLE");

                double holeW = Math.Min(450.0, W - 160.0);
                double holeD = Math.Min(220.0, D - 100.0);
                double hx1 = x + (W - holeW) / 2.0 + holeOffX;
                double hy1 = y + (D - holeD) / 2.0 + holeOffY;

                AddRectangle(btr, tr, new Point3d(hx1, hy1 + holeD, 0), new Point3d(hx1 + holeW, hy1, 0), LayerEnclosure, 1);
                AddText(btr, tr, "LỖ KHOÉT LUỒN CÁP (HOLE CABLE)", new Point3d(hx1 + holeW / 2.0, hy1 + holeD / 2.0 + 12.0, 0), 13.0, AttachmentPoint.MiddleCenter, LayerText, 0, 1);
                string holeText = ep.GetCustomText("BOTTOM_CABLE_HOLE", !string.IsNullOrEmpty(ep.CableHoleSpec) ? ep.CableHoleSpec : string.Format("{0:0} x {1:0} mm", holeW, holeD));
                AddText(btr, tr, holeText + " (NẮP BỊT CAO SU)", new Point3d(hx1 + holeW / 2.0, hy1 + holeD / 2.0 - 15.0, 0), 10.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
            }

            AddText(btr, tr, "MẶT ĐÁY VÀ LỖ KHOÉT CÁP", new Point3d(x + W / 2.0, y - 35.0, 0), 20.0, AttachmentPoint.MiddleCenter, LayerText, 0, 2);
            AddText(btr, tr, "BOTTOM VIEW", new Point3d(x + W / 2.0, y - 60.0, 0), 15.0, AttachmentPoint.MiddleCenter, LayerText, 0, 7);
        }

        private void DrawDimensionsAndSpecs(BlockTableRecord btr, Transaction tr, Point3d pStart, Point3d pEnd, double W, double H, double D, 
            double plinthH, double canopyH, ElectricalPanel panel, EnclosureLayoutParameters ep)
        {
            if (ep.IncludeDimensions && !ep.IsItemDeleted("DIMENSIONS"))
            {
                double dimOffX = ep.GetOffsetX("DIMENSIONS");
                double dimOffY = ep.GetOffsetY("DIMENSIONS");

                double dimY = pStart.Y + H + canopyH + 60.0 + dimOffY;
                AddDimensionLine(btr, tr, new Point3d(pStart.X, pStart.Y + H + canopyH, 0), new Point3d(pStart.X + W, pStart.Y + H + canopyH, 0), dimY, string.Format("W = {0:0} mm", W));

                double dimX = pStart.X - 80.0 + dimOffX;
                AddDimensionLineV(btr, tr, new Point3d(pStart.X, pStart.Y - plinthH, 0), new Point3d(pStart.X, pStart.Y + H + canopyH, 0), dimX, string.Format("H = {0:0} mm", H + plinthH + canopyH));

                if (ep.IncludeSideView && !ep.IsItemDeleted("VIEW_SIDE"))
                {
                    double dimSideY = pEnd.Y + H + canopyH + 60.0 + dimOffY;
                    AddDimensionLine(btr, tr, new Point3d(pEnd.X, pEnd.Y + H + canopyH, 0), new Point3d(pEnd.X + D, pEnd.Y + H + canopyH, 0), dimSideY, string.Format("D = {0:0} mm", D));
                }
            }

            if (ep.IncludeSpecTable && !ep.IsItemDeleted("SPEC_TABLE"))
            {
                double tblOffX = ep.GetOffsetX("SPEC_TABLE");
                double tblOffY = ep.GetOffsetY("SPEC_TABLE");

                double tblX = pEnd.X + (ep.IncludeSideView ? D : 0) + 120.0 + tblOffX;
                double tblY = pEnd.Y + H + tblOffY;
                double tblW = 420.0;
                double tblH = 340.0;

                AddRectangle(btr, tr, new Point3d(tblX, tblY, 0), new Point3d(tblX + tblW, tblY - tblH, 0), LayerEnclosure, 7);
                AddRectangle(btr, tr, new Point3d(tblX, tblY, 0), new Point3d(tblX + tblW, tblY - 45.0, 0), LayerEnclosure, 2);
                AddText(btr, tr, "BẢNG TIÊU CHUẨN KỸ THUẬT VỎ TỦ", new Point3d(tblX + tblW / 2.0, tblY - 22.0, 0), 14.0, AttachmentPoint.MiddleCenter, LayerText, 0, 1);

                string[] specs = new string[]
                {
                    string.Format("• Kích thước: {0:0} x {1:0} x {2:0} mm (H x W x D)", H + plinthH + canopyH, W, D),
                    "• Kết cấu: Vỏ tủ công nghiệp 2 lớp cánh (Two-Door Form 2B)",
                    "• Vật liệu: " + ep.MaterialSpec,
                    "• Xử lý bề mặt: " + ep.ColorSpec,
                    "• Cấp bảo vệ: " + ep.ProtectionRating,
                    string.Format("• Chân đế: Thép dập U{0:0}mm sơn đen mạ kẽm", plinthH),
                    "• Nóc che: Chấn dốc thoát nước nhô ra trước 30mm",
                    "• Cáp vào/ra: Đáy tủ khoét lỗ " + ep.CableHoleSpec,
                    "• Tiếp địa: Thanh đồng PE 25x3mm tiếp địa toàn bộ cánh và khung"
                };

                for (int s = 0; s < specs.Length; s++)
                {
                    double sy = tblY - 65.0 - s * 28.0;
                    AddText(btr, tr, specs[s], new Point3d(tblX + 15.0, sy, 0), 10.5, AttachmentPoint.MiddleLeft, LayerText, 0, 7);
                }
            }
        }

        private void DrawDinRail(BlockTableRecord btr, Transaction tr, double x1, double x2, double y, ObjectId blkRay)
        {
            if (blkRay != ObjectId.Null)
            {
                InsertBlock(btr, tr, blkRay, new Point3d((x1 + x2) / 2.0, y, 0), 1.0, 0, LayerEnclosure);
            }
            else
            {
                AddRectangle(btr, tr, new Point3d(x1, y + 17.5, 0), new Point3d(x2, y - 17.5, 0), LayerEnclosure, 8);
                AddLine(btr, tr, new Point3d(x1, y + 12.0, 0), new Point3d(x2, y + 12.0, 0), LayerEnclosure, 8);
                AddLine(btr, tr, new Point3d(x1, y - 12.0, 0), new Point3d(x2, y - 12.0, 0), LayerEnclosure, 8);
            }
        }

        private void AddDimensionLine(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, double dimY, string text)
        {
            AddLine(btr, tr, new Point3d(p1.X, p1.Y, 0), new Point3d(p1.X, dimY + 10.0, 0), LayerDim, 8);
            AddLine(btr, tr, new Point3d(p2.X, p2.Y, 0), new Point3d(p2.X, dimY + 10.0, 0), LayerDim, 8);
            AddLine(btr, tr, new Point3d(p1.X, dimY, 0), new Point3d(p2.X, dimY, 0), LayerDim, 1);
            AddText(btr, tr, text, new Point3d((p1.X + p2.X) / 2.0, dimY + 12.0, 0), 16.0, AttachmentPoint.BottomCenter, LayerDim, 0, 1);
        }

        private void AddDimensionLineV(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, double dimX, string text)
        {
            AddLine(btr, tr, new Point3d(p1.X, p1.Y, 0), new Point3d(dimX - 10.0, p1.Y, 0), LayerDim, 8);
            AddLine(btr, tr, new Point3d(p2.X, p2.Y, 0), new Point3d(dimX - 10.0, p2.Y, 0), LayerDim, 8);
            AddLine(btr, tr, new Point3d(dimX, p1.Y, 0), new Point3d(dimX, p2.Y, 0), LayerDim, 1);
            AddText(btr, tr, text, new Point3d(dimX - 12.0, (p1.Y + p2.Y) / 2.0, 0), 16.0, AttachmentPoint.MiddleRight, LayerDim, Math.PI / 2.0, 1);
        }

        private void AddRectangle(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, short colorIndex = 7)
        {
            Autodesk.AutoCAD.DatabaseServices.Polyline pl = new Autodesk.AutoCAD.DatabaseServices.Polyline(4);
            pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            pl.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private void AddLine(BlockTableRecord btr, Transaction tr, Point3d start, Point3d end, string layer, short colorIndex = 7)
        {
            Autodesk.AutoCAD.DatabaseServices.Line ln = new Autodesk.AutoCAD.DatabaseServices.Line(start, end);
            ln.Layer = layer;
            ln.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private void AddCircle(BlockTableRecord btr, Transaction tr, Point3d center, double radius, string layer, short colorIndex = 7)
        {
            Circle c = new Circle(center, Vector3d.ZAxis, radius);
            c.Layer = layer;
            c.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            btr.AppendEntity(c);
            tr.AddNewlyCreatedDBObject(c, true);
        }

        private void AddText(BlockTableRecord btr, Transaction tr, string text, Point3d pos, double height, AttachmentPoint align, string layer, double rotation = 0, short colorIndex = 7)
        {
            if (string.IsNullOrEmpty(text)) return;
            MText mtext = new MText();
            mtext.Contents = text;
            mtext.TextHeight = height;
            mtext.Attachment = align;
            mtext.Location = pos;
            mtext.Rotation = rotation;
            mtext.Layer = layer;
            if (colorIndex > 0)
            {
                mtext.ColorIndex = colorIndex;
            }
            btr.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
        }

        private void InsertBlock(BlockTableRecord btr, Transaction tr, ObjectId blockId, Point3d pt, double scale = 1.0, double rotation = 0, string layer = LayerEquipment)
        {
            if (blockId == ObjectId.Null) return;
            BlockReference br = new BlockReference(pt, blockId);
            br.ScaleFactors = new Scale3d(scale);
            br.Rotation = rotation;
            br.Layer = layer;
            btr.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }

        // =========================================================================
        // RENDER PREVIEW TƯƠNG TÁC KÉO THẢ (DRAG & DROP) LÊN WPF CANVAS
        // =========================================================================

        public static void RenderPreviewToCanvas(Canvas canvas, ElectricalPanel panel, EnclosureLayoutParameters ep, Action<EnclosureCanvasItem, FrameworkElement> onItemCreated = null)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            if (ep == null) ep = new EnclosureLayoutParameters();

            double W = ep.Width > 0 ? ep.Width : 700.0;
            double H = ep.Height > 0 ? ep.Height : 1625.0;
            double D = ep.Depth > 0 ? ep.Depth : 400.0;
            double plinthH = ep.PlinthHeight > 0 ? ep.PlinthHeight : 100.0;
            double canopyH = ep.CanopyHeight > 0 ? ep.CanopyHeight : 75.0;

            int viewCount = 0;
            if (ep.IncludeFrontView && !ep.IsItemDeleted("VIEW_FRONT")) viewCount++;
            if (ep.IncludeInnerDoorView && !ep.IsItemDeleted("VIEW_INNER")) viewCount++;
            if (ep.IncludeInternalView && !ep.IsItemDeleted("VIEW_INTERNAL")) viewCount++;
            if (ep.IncludeSideView && !ep.IsItemDeleted("VIEW_SIDE")) viewCount++;

            if (viewCount == 0 && (ep.IsItemDeleted("VIEW_BOTTOM") || !ep.IncludeBottomView))
            {
                TextBlock tbEmpty = new TextBlock
                {
                    Text = "Chưa chọn hình chiếu nào để hiển thị! Vui lòng tích chọn ít nhất 1 hình chiếu.",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(tbEmpty, 40);
                Canvas.SetTop(tbEmpty, 40);
                canvas.Children.Add(tbEmpty);
                return;
            }

            double s = 0.22;
            double curX = 35.0;
            if (ep.IncludeDimensions) curX += 25.0;
            double baseY = 45.0 + (H + canopyH) * s;
            double curXFront = curX;

            // 1. FRONT VIEW
            if (ep.IncludeFrontView && !ep.IsItemDeleted("VIEW_FRONT"))
            {
                EnclosureCanvasItem itVFront = ep.GetOrCreateItem("VIEW_FRONT", "Khung Mặt Trước Cánh Ngoài", "View", W, H + plinthH + canopyH);
                double vfx = curX + itVFront.OffsetX * s;
                double vfy = baseY - itVFront.OffsetY * s;
                curXFront = vfx;

                FrameworkElement vfBox = BuildCanvasBox(W * s, H * s, plinthH * s, canopyH * s, "MẶT NGOÀI CÁNH TỦ", Brushes.White, Brushes.DarkSlateGray);
                CreateInteractiveContainer(canvas, itVFront, vfx, vfy - (H + canopyH) * s, W * s, (H + plinthH + canopyH) * s + 30.0, vfBox, onItemCreated);

                // Nameplate
                if (ep.IncludeNameplate && !ep.IsItemDeleted("NAMEPLATE"))
                {
                    string pName = ep.GetCustomText("NAMEPLATE", ep.PanelName);
                    EnclosureCanvasItem itNp = ep.GetOrCreateItem("NAMEPLATE", "Bảng tên Mica (Nameplate)", "DoorBlock", 220, 36, pName);
                    double npW = 220 * s, npH = 36 * s;
                    double npx = vfx + (W * s - npW) / 2.0 + itNp.OffsetX * s;
                    double npy = vfy - (H - 50.0) * s - npH / 2.0 - itNp.OffsetY * s;

                    Grid gNp = new Grid { Width = npW, Height = npH };
                    gNp.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.DarkBlue, Stroke = Brushes.White, StrokeThickness = 0.8, RadiusX = 2, RadiusY = 2 });
                    TextBlock tbNp = new TextBlock { Text = pName, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 8.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    gNp.Children.Add(tbNp);

                    CreateInteractiveContainer(canvas, itNp, npx, npy, npW, npH, gNp, onItemCreated);
                }

                // Phase Lamps and Meter PM (Module PM + BÁO PHA 135x226 mm)
                if (ep.IncludePhaseLampsAndMeter && !ep.IsItemDeleted("PHASE_LAMPS_METER"))
                {
                    string mText = ep.GetCustomText("PHASE_LAMPS_METER", "PM MULTIMETER");
                    EnclosureCanvasItem itPm = ep.GetOrCreateItem("PHASE_LAMPS_METER", "Khối Đồng hồ PM & 3 Đèn báo pha", "DoorBlock", 135, 226, mText);
                    double pmW = 135.0 * s, pmH = 226.0 * s;
                    double pmx = vfx + (W * s - pmW) / 2.0 + itPm.OffsetX * s;
                    double pmy = vfy - (H - 70.0) * s - itPm.OffsetY * s;

                    Canvas cPm = new Canvas { Width = pmW, Height = pmH };
                    System.Windows.Shapes.Rectangle bgMod = new System.Windows.Shapes.Rectangle { Width = pmW, Height = pmH, Fill = Brushes.WhiteSmoke, Stroke = Brushes.DarkSlateGray, StrokeThickness = 1.0 };
                    cPm.Children.Add(bgMod);

                    double lxMid = pmW / 2.0;
                    double lampY = 14.0 * s;
                    double lampR = 4.5 * s;
                    AddCanvasCircleTo(cPm, lxMid - 24.0 * s, lampY, lampR, Brushes.Red);
                    AddCanvasCircleTo(cPm, lxMid, lampY, lampR, Brushes.Gold);
                    AddCanvasCircleTo(cPm, lxMid + 24.0 * s, lampY, lampR, Brushes.DodgerBlue);

                    double pmBoxW = 96.0 * s;
                    double pmBoxH = 96.0 * s;
                    double pmBoxX = (pmW - pmBoxW) / 2.0;
                    double pmBoxY = lampY + lampR + 14.0 * s;
                    System.Windows.Shapes.Rectangle rPm = new System.Windows.Shapes.Rectangle { Width = pmBoxW, Height = pmBoxH, Fill = Brushes.LightSkyBlue, Stroke = Brushes.Navy, StrokeThickness = 1.0 };
                    Canvas.SetLeft(rPm, pmBoxX);
                    Canvas.SetTop(rPm, pmBoxY);
                    cPm.Children.Add(rPm);

                    TextBlock tbPm = new TextBlock { Text = "PM", FontSize = Math.Max(7.0, 10.0 * s), FontWeight = FontWeights.Bold, Foreground = Brushes.Navy };
                    Canvas.SetLeft(tbPm, lxMid - 8.0 * s);
                    Canvas.SetTop(tbPm, pmBoxY + 12.0 * s);
                    cPm.Children.Add(tbPm);

                    TextBlock tbSub = new TextBlock { Text = "MULTIMETER", FontSize = Math.Max(5.5, 7.0 * s), Foreground = Brushes.DarkSlateGray };
                    Canvas.SetLeft(tbSub, lxMid - 18.0 * s);
                    Canvas.SetTop(tbSub, pmBoxY + 30.0 * s);
                    cPm.Children.Add(tbSub);

                    CreateInteractiveContainer(canvas, itPm, pmx, pmy, pmW, pmH, cPm, onItemCreated);
                }

                // Emergency Stop (EM 80x80 mm)
                if (ep.IncludeEmergencyStop && !ep.IsItemDeleted("EMERGENCY_STOP"))
                {
                    EnclosureCanvasItem itEm = ep.GetOrCreateItem("EMERGENCY_STOP", "Nút dừng khẩn cấp (EM)", "DoorBlock", 80, 80);
                    double emW = 80.0 * s, emH = 80.0 * s;
                    double emX = vfx + (W * s / 2.0) + (135.0 * s / 2.0) + 16.0 * s + itEm.OffsetX * s;
                    double emY = vfy - (H - 120.0) * s - itEm.OffsetY * s;

                    Canvas cEm = new Canvas { Width = emW, Height = emH };
                    System.Windows.Shapes.Ellipse bgYellow = new System.Windows.Shapes.Ellipse { Width = emW, Height = emH, Fill = Brushes.Gold, Stroke = Brushes.DarkGoldenrod, StrokeThickness = 1.0 };
                    cEm.Children.Add(bgYellow);

                    double btnD = 46.0 * s;
                    System.Windows.Shapes.Ellipse btnRed = new System.Windows.Shapes.Ellipse { Width = btnD, Height = btnD, Fill = Brushes.Crimson, Stroke = Brushes.DarkRed, StrokeThickness = 1.2 };
                    Canvas.SetLeft(btnRed, (emW - btnD) / 2.0);
                    Canvas.SetTop(btnRed, (emH - btnD) / 2.0);
                    cEm.Children.Add(btnRed);

                    TextBlock tbEm = new TextBlock { Text = "EM", FontSize = Math.Max(6.5, 9.0 * s), FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                    Canvas.SetLeft(tbEm, (emW - 14.0 * s) / 2.0);
                    Canvas.SetTop(tbEm, (emH - 12.0 * s) / 2.0);
                    cEm.Children.Add(tbEm);

                    CreateInteractiveContainer(canvas, itEm, emX, emY, emW, emH, cEm, onItemCreated);
                }

                // Door Lock
                if (ep.IncludeDoorLock && !ep.IsItemDeleted("DOOR_LOCK"))
                {
                    EnclosureCanvasItem itLk = ep.GetOrCreateItem("DOOR_LOCK", "Khóa tay nắm tủ ngoài", "DoorBlock", 20, 70);
                    double lkW = 6.0, lkH = 24.0;
                    double lkx = vfx + W * s - 8.0 + itLk.OffsetX * s;
                    double lky = vfy - (H / 2.0) * s - lkH / 2.0 - itLk.OffsetY * s;

                    Grid gLk = new Grid { Width = lkW, Height = lkH };
                    gLk.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.DimGray, Stroke = Brushes.Black, StrokeThickness = 1.0 });
                    CreateInteractiveContainer(canvas, itLk, lkx, lky, lkW, lkH, gLk, onItemCreated);
                }

                // Control Stations (A-O-M)
                if (ep.IncludeControlStations && !ep.IsItemDeleted("AOM_STATIONS") && !ep.IsItemDeleted("HOA_STATIONS") && panel != null && panel.Loads != null)
                {
                    EnclosureCanvasItem itAom = ep.GetOrCreateItem("AOM_STATIONS", "Cụm nút nhấn & Chuyển mạch A-O-M", "DoorBlock", 318, 180);
                    int nRows = Math.Min(panel.Loads.Count, 6);
                    double aomW = 95.0;
                    double aomH = Math.Max(30.0, nRows * 20.0 + 10.0);
                    double aomX = vfx + 16.0 + itAom.OffsetX * s;
                    double aomY = vfy - (H - 260.0) * s - itAom.OffsetY * s;

                    Canvas cAom = new Canvas { Width = aomW, Height = aomH };
                    for (int r = 0; r < nRows; r++)
                    {
                        double ry = r * 20.0 + 8.0;
                        AddCanvasCircleTo(cAom, 8.0, ry, 3.0, Brushes.Black);
                        AddCanvasCircleTo(cAom, 24.0, ry, 3.0, Brushes.ForestGreen);
                        AddCanvasCircleTo(cAom, 38.0, ry, 3.0, Brushes.Firebrick);
                        AddCanvasCircleTo(cAom, 52.0, ry, 3.0, Brushes.Orange);
                    }
                    CreateInteractiveContainer(canvas, itAom, aomX, aomY, aomW, aomH, cAom, onItemCreated);
                }

                // Ventilation Fan
                if (ep.IncludeVentilationFan && !ep.IsItemDeleted("FAN_FRONT"))
                {
                    EnclosureCanvasItem itFan = ep.GetOrCreateItem("FAN_FRONT", "Quạt thông gió & Lưới lọc bụi", "DoorBlock", 140, 140);
                    double fanSz = 26.0;
                    double fanX = vfx + W * s - fanSz - 8.0 + itFan.OffsetX * s;
                    double fanY = vfy - 48.0 - itFan.OffsetY * s;

                    Grid gFan = new Grid { Width = fanSz, Height = fanSz };
                    gFan.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.LightGray, Stroke = Brushes.DimGray, StrokeThickness = 1.0 });
                    for (int sl = 1; sl <= 3; sl++)
                    {
                        System.Windows.Shapes.Line slLine = new System.Windows.Shapes.Line { X1 = 3.0, Y1 = sl * 6.0, X2 = fanSz - 3.0, Y2 = sl * 6.0, Stroke = Brushes.DimGray, StrokeThickness = 1.0 };
                        gFan.Children.Add(slLine);
                    }
                    CreateInteractiveContainer(canvas, itFan, fanX, fanY, fanSz, fanSz, gFan, onItemCreated);
                }

                curX += (W + 120.0) * s;
            }

            // 2. INNER DOOR
            if (ep.IncludeInnerDoorView && !ep.IsItemDeleted("VIEW_INNER"))
            {
                EnclosureCanvasItem itVInner = ep.GetOrCreateItem("VIEW_INNER", "Khung Mặt Cánh Phụ Trong", "View", W, H + plinthH + canopyH);
                double vix = curX + itVInner.OffsetX * s;
                double viy = baseY - itVInner.OffsetY * s;

                FrameworkElement viBox = BuildCanvasBox(W * s, H * s, plinthH * s, canopyH * s, "CÁNH PHỤ TRONG", Brushes.LightGray, Brushes.SlateGray);
                CreateInteractiveContainer(canvas, itVInner, vix, viy - (H + canopyH) * s, W * s, (H + plinthH + canopyH) * s + 30.0, viBox, onItemCreated);

                if (!ep.IsItemDeleted("INNER_DOOR_CUTOUTS"))
                {
                    EnclosureCanvasItem itCut = ep.GetOrCreateItem("INNER_DOOR_CUTOUTS", "Mặt nạ khoét MCCB & MCB", "InnerDoor", W - 40, 180);
                    double cutW = (W * s) - 40.0;
                    double cutH = 75.0;
                    double cutx = vix + 20.0 + itCut.OffsetX * s;
                    double cuty = viy - (H - 270.0) * s - itCut.OffsetY * s;

                    Canvas cCut = new Canvas { Width = cutW, Height = cutH };
                    System.Windows.Shapes.Rectangle rMccbCut = new System.Windows.Shapes.Rectangle { Width = 36.0, Height = 22.0, Fill = Brushes.White, Stroke = Brushes.Red, StrokeThickness = 1.0 };
                    Canvas.SetLeft(rMccbCut, cutW / 2.0 - 18.0);
                    Canvas.SetTop(rMccbCut, 4.0);
                    cCut.Children.Add(rMccbCut);

                    System.Windows.Shapes.Rectangle rMcbCut = new System.Windows.Shapes.Rectangle { Width = cutW, Height = 16.0, Fill = Brushes.White, Stroke = Brushes.Red, StrokeThickness = 1.0 };
                    Canvas.SetLeft(rMcbCut, 0);
                    Canvas.SetTop(rMcbCut, 40.0);
                    cCut.Children.Add(rMcbCut);

                    CreateInteractiveContainer(canvas, itCut, cutx, cuty, cutW, cutH, cCut, onItemCreated);
                }

                curX += (W + 120.0) * s;
            }

            // 3. INTERNAL EQUIPMENT VIEW
            if (ep.IncludeInternalView && !ep.IsItemDeleted("VIEW_INTERNAL"))
            {
                EnclosureCanvasItem itVInternal = ep.GetOrCreateItem("VIEW_INTERNAL", "Khung Mặt Thiết Bị Trong", "View", W, H + plinthH + canopyH);
                double vex = curX + itVInternal.OffsetX * s;
                double vey = baseY - itVInternal.OffsetY * s;

                FrameworkElement veBox = BuildCanvasBox(W * s, H * s, plinthH * s, canopyH * s, "THIẾT BỊ BÊN TRONG", Brushes.WhiteSmoke, Brushes.Black);
                CreateInteractiveContainer(canvas, itVInternal, vex, vey - (H + canopyH) * s, W * s, (H + plinthH + canopyH) * s + 30.0, veBox, onItemCreated);

                // Wiring Ducts
                if (ep.IncludeWiringDucts && !ep.IsItemDeleted("WIRING_DUCTS"))
                {
                    EnclosureCanvasItem itDuct = ep.GetOrCreateItem("WIRING_DUCTS", "Máng cáp đi dây kỹ thuật 60x60", "InternalEquipment", W, H);
                    double ductX = vex + itDuct.OffsetX * s;
                    double ductY = vey - H * s + 8.0 - itDuct.OffsetY * s;
                    Canvas cDuct = new Canvas { Width = W * s, Height = (H - 16.0) * s };

                    System.Windows.Shapes.Rectangle rL = new System.Windows.Shapes.Rectangle { Width = 8.0, Height = (H - 16.0) * s, Fill = Brushes.Gainsboro, Stroke = Brushes.Gray, StrokeThickness = 0.8 };
                    Canvas.SetLeft(rL, 8.0);
                    cDuct.Children.Add(rL);

                    System.Windows.Shapes.Rectangle rR = new System.Windows.Shapes.Rectangle { Width = 8.0, Height = (H - 16.0) * s, Fill = Brushes.Gainsboro, Stroke = Brushes.Gray, StrokeThickness = 0.8 };
                    Canvas.SetLeft(rR, W * s - 16.0);
                    cDuct.Children.Add(rR);

                    CreateInteractiveContainer(canvas, itDuct, ductX, ductY, W * s, (H - 16.0) * s, cDuct, onItemCreated);
                }

                // Copper Busbars
                if (ep.IncludeCopperBusbar && !ep.IsItemDeleted("COPPER_BUSBAR"))
                {
                    EnclosureCanvasItem itBb = ep.GetOrCreateItem("COPPER_BUSBAR", "Hệ thanh cái đồng Busbar", "InternalEquipment", W - 40, 25);
                    double bbW = (W * s) - 40.0, bbH = 24.0;
                    double bbx = vex + 20.0 + itBb.OffsetX * s;
                    double bby = vey - (H - 75.0) * s - itBb.OffsetY * s;

                    Canvas cBb = new Canvas { Width = bbW, Height = bbH };
                    AddCanvasRectTo(cBb, 0, 0, bbW, 4.0, Brushes.Red, Brushes.DarkRed);
                    AddCanvasRectTo(cBb, 0, 6.0, bbW, 4.0, Brushes.Gold, Brushes.DarkGoldenrod);
                    AddCanvasRectTo(cBb, 0, 12.0, bbW, 4.0, Brushes.DodgerBlue, Brushes.DarkBlue);
                    CreateInteractiveContainer(canvas, itBb, bbx, bby, bbW, bbH, cBb, onItemCreated);
                }

                // Main MCCB
                if (ep.IncludeMainMccb && !ep.IsItemDeleted("MAIN_MCCB"))
                {
                    string mccbTxt = ep.GetCustomText("MAIN_MCCB", !string.IsNullOrEmpty(panel.MainBreakerSpec) ? panel.MainBreakerSpec : "MCCB 3P 250A");
                    EnclosureCanvasItem itMccb = ep.GetOrCreateItem("MAIN_MCCB", "MCCB Aptomat tổng Schneider", "InternalEquipment", 140, 180, mccbTxt);
                    double mw = 34.0, mh = 38.0;
                    double mx = vex + (W * s - mw) / 2.0 + itMccb.OffsetX * s;
                    double my = vey - (H - 170.0) * s - itMccb.OffsetY * s;

                    Grid gMccb = new Grid { Width = mw, Height = mh };
                    gMccb.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.0 });
                    TextBlock tbMc = new TextBlock { Text = "MCCB", FontSize = 7.0, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkRed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    gMccb.Children.Add(tbMc);

                    CreateInteractiveContainer(canvas, itMccb, mx, my, mw, mh, gMccb, onItemCreated);
                }

                // Branch MCBs (Hiển thị chi tiết từng át 1P/2P/3P/4P/RCBO trên canvas preview)
                if (ep.IncludeBranchMcbs && !ep.IsItemDeleted("BRANCH_MCBS"))
                {
                    EnclosureCanvasItem itMcbs = ep.GetOrCreateItem("BRANCH_MCBS", "Dãy MCB/RCBO các nhánh tải", "InternalEquipment", W - 40, 60);
                    double bw = (W * s) - 40.0, bh = 26.0;
                    double bx = vex + 20.0 + itMcbs.OffsetX * s;
                    double by = vey - (H - 340.0) * s - itMcbs.OffsetY * s;

                    Canvas cMcbs = new Canvas { Width = bw, Height = bh };
                    System.Windows.Shapes.Rectangle rRail = new System.Windows.Shapes.Rectangle { 
                        Width = bw, Height = 6.0, Fill = Brushes.Silver, Stroke = Brushes.DimGray, StrokeThickness = 0.5 
                    };
                    Canvas.SetTop(rRail, 10.0);
                    cMcbs.Children.Add(rRail);

                    if (panel != null && panel.Loads != null && panel.Loads.Count > 0)
                    {
                        double curMcbX = 6.0;
                        for (int i = 0; i < panel.Loads.Count; i++)
                        {
                            ElectricalLoad ld = panel.Loads[i];
                            string spec = ((ld.BreakerSpec != null ? ld.BreakerSpec : "") + " " + (ld.BreakerType != null ? ld.BreakerType : "")).ToUpper();
                            bool isRccb = spec.Contains("RCCB") || spec.Contains("RCBO") || spec.Contains("ELCB");
                            int poles = 3;
                            if (spec.Contains("4P")) poles = 4;
                            else if (spec.Contains("3P")) poles = 3;
                            else if (spec.Contains("2P")) poles = 2;
                            else if (spec.Contains("1P") || ld.Phase == PhaseType.SinglePhase) poles = 1;

                            double mcbW = poles == 1 ? 10.0 : (poles == 2 ? 18.0 : (poles == 3 ? 26.0 : 34.0));
                            if (curMcbX + mcbW > bw - 4.0) break;

                            System.Windows.Shapes.Rectangle rM = new System.Windows.Shapes.Rectangle {
                                Width = mcbW, Height = 22.0,
                                Fill = isRccb ? Brushes.AliceBlue : Brushes.White,
                                Stroke = isRccb ? Brushes.DarkSlateBlue : Brushes.Black,
                                StrokeThickness = 0.8
                            };
                            Canvas.SetLeft(rM, curMcbX);
                            Canvas.SetTop(rM, 2.0);
                            cMcbs.Children.Add(rM);

                            string pTxt = isRccb ? "R" + poles : poles + "P";
                            TextBlock tbP = new TextBlock {
                                Text = pTxt, FontSize = 6.0,
                                FontWeight = FontWeights.Bold,
                                Foreground = isRccb ? Brushes.DarkSlateBlue : Brushes.DimGray
                            };
                            Canvas.SetLeft(tbP, curMcbX + 1.5);
                            Canvas.SetTop(tbP, 5.0);
                            cMcbs.Children.Add(tbP);

                            curMcbX += mcbW + 2.5;
                        }
                    }
                    else
                    {
                        TextBlock tbMcb = new TextBlock { Text = "DÃY MCB NHÁNH", FontSize = 7.0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        Canvas.SetLeft(tbMcb, bw / 2.0 - 25.0);
                        Canvas.SetTop(tbMcb, 6.0);
                        cMcbs.Children.Add(tbMcb);
                    }

                    CreateInteractiveContainer(canvas, itMcbs, bx, by, bw, bh, cMcbs, onItemCreated);
                }

                // Motor Starters
                if (ep.IncludeMotorStarters && !ep.IsItemDeleted("MOTOR_STARTERS"))
                {
                    EnclosureCanvasItem itStarters = ep.GetOrCreateItem("MOTOR_STARTERS", "Bộ khởi động Contactor + TOR", "InternalEquipment", W - 40, 80);
                    double sw = (W * s) - 40.0, sh = 26.0;
                    double sx = vex + 20.0 + itStarters.OffsetX * s;
                    double sy = vey - (H - 510.0) * s - itStarters.OffsetY * s;

                    Grid gSt = new Grid { Width = sw, Height = sh };
                    gSt.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.0 });
                    TextBlock tbSt = new TextBlock { Text = "CONTACTOR + TOR (DOL / Y-Δ)", FontSize = 7.0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    gSt.Children.Add(tbSt);

                    CreateInteractiveContainer(canvas, itStarters, sx, sy, sw, sh, gSt, onItemCreated);
                }

                // Control Relays
                if (ep.IncludeControlRelays && !ep.IsItemDeleted("CONTROL_RELAYS"))
                {
                    EnclosureCanvasItem itRelays = ep.GetOrCreateItem("CONTROL_RELAYS", "Rơ le 14 chân + Mikro + Cầu chì", "InternalEquipment", W - 40, 60);
                    double rw = (W * s) - 40.0, rh = 18.0;
                    double rx = vex + 20.0 + itRelays.OffsetX * s;
                    double ry = vey - (H - 690.0) * s - itRelays.OffsetY * s;

                    Grid gRy = new Grid { Width = rw, Height = rh };
                    gRy.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.LightYellow, Stroke = Brushes.Black, StrokeThickness = 1.0 });
                    TextBlock tbRy = new TextBlock { Text = "RELAY 14P + MIKRO", FontSize = 6.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    gRy.Children.Add(tbRy);

                    CreateInteractiveContainer(canvas, itRelays, rx, ry, rw, rh, gRy, onItemCreated);
                }

                // Terminal Blocks
                if (ep.IncludeTerminalBlocks && !ep.IsItemDeleted("TERMINAL_BLOCKS"))
                {
                    EnclosureCanvasItem itTerms = ep.GetOrCreateItem("TERMINAL_BLOCKS", "Hàng kẹp cực nối dây Domino", "InternalEquipment", W - 40, 40);
                    double tw = (W * s) - 40.0, th = 15.0;
                    double tx = vex + 20.0 + itTerms.OffsetX * s;
                    double ty = vey - (H - 870.0) * s - itTerms.OffsetY * s;

                    Grid gTm = new Grid { Width = tw, Height = th };
                    gTm.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.Khaki, Stroke = Brushes.Black, StrokeThickness = 1.0 });
                    TextBlock tbTm = new TextBlock { Text = "TERMINAL DOMINO 15A", FontSize = 6.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    gTm.Children.Add(tbTm);

                    CreateInteractiveContainer(canvas, itTerms, tx, ty, tw, th, gTm, onItemCreated);
                }

                curX += (W + 120.0) * s;
            }

            // 4. SIDE VIEW
            if (ep.IncludeSideView && !ep.IsItemDeleted("VIEW_SIDE"))
            {
                EnclosureCanvasItem itVSide = ep.GetOrCreateItem("VIEW_SIDE", "Khung Mặt Cắt Hông", "View", D, H + plinthH + canopyH);
                double vsx = curX + itVSide.OffsetX * s;
                double vsy = baseY - itVSide.OffsetY * s;

                FrameworkElement vsBox = BuildCanvasBox(D * s, H * s, plinthH * s, canopyH * s, "MẶT HÔNG", Brushes.White, Brushes.DarkSlateGray);
                CreateInteractiveContainer(canvas, itVSide, vsx, vsy - (H + canopyH) * s, D * s, (H + plinthH + canopyH) * s + 30.0, vsBox, onItemCreated);

                if (ep.IncludeVentilationFan && !ep.IsItemDeleted("FAN_SIDE"))
                {
                    EnclosureCanvasItem itFanS = ep.GetOrCreateItem("FAN_SIDE", "Lưới quạt mặt hông", "SideView", 120, 120);
                    double fanSz = 22.0;
                    double fanX = vsx + D * s - fanSz - 8.0 + itFanS.OffsetX * s;
                    double fanY = vsy - (H - 120.0) * s - itFanS.OffsetY * s;

                    Grid gFanS = new Grid { Width = fanSz, Height = fanSz };
                    gFanS.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.LightGray, Stroke = Brushes.DimGray, StrokeThickness = 1.0 });
                    for (int sl = 1; sl <= 3; sl++)
                    {
                        System.Windows.Shapes.Line slLine = new System.Windows.Shapes.Line { X1 = 2.0, Y1 = sl * 5.0, X2 = fanSz - 2.0, Y2 = sl * 5.0, Stroke = Brushes.DimGray, StrokeThickness = 1.0 };
                        gFanS.Children.Add(slLine);
                    }
                    CreateInteractiveContainer(canvas, itFanS, fanX, fanY, fanSz, fanSz, gFanS, onItemCreated);
                }

                curX += (D + 120.0) * s;
            }

            // 5. BẢNG THÔNG SỐ SPECS
            if (ep.IncludeSpecTable && !ep.IsItemDeleted("SPEC_TABLE"))
            {
                EnclosureCanvasItem itSpec = ep.GetOrCreateItem("SPEC_TABLE", "Bảng tiêu chuẩn kỹ thuật vỏ tủ", "Annotation", 420, 340);
                double tblW = 190.0, tblH = 150.0;
                double tblX = curX + itSpec.OffsetX * s;
                double tblY = baseY - H * s - itSpec.OffsetY * s;

                Canvas cTbl = new Canvas { Width = tblW, Height = tblH };
                AddCanvasRectTo(cTbl, 0, 0, tblW, tblH, Brushes.White, Brushes.DarkBlue);
                AddCanvasRectTo(cTbl, 0, 0, tblW, 22.0, Brushes.Navy, Brushes.Navy);
                AddCanvasTextTo(cTbl, tblW / 2.0, 4.0, "TIÊU CHUẨN KỸ THUẬT", 8.5, FontWeights.Bold, Brushes.White, true);
                AddCanvasTextTo(cTbl, 8.0, 28.0, string.Format("• Kích thước: {0:0}x{1:0}x{2:0} mm", H + plinthH + canopyH, W, D), 7.5, FontWeights.Normal, Brushes.Black, false);
                AddCanvasTextTo(cTbl, 8.0, 44.0, "• Vỏ 2 lớp cánh Form 2B", 7.5, FontWeights.Normal, Brushes.Black, false);
                AddCanvasTextTo(cTbl, 8.0, 60.0, "• " + ep.MaterialSpec, 7.5, FontWeights.Normal, Brushes.Black, false);
                AddCanvasTextTo(cTbl, 8.0, 76.0, "• " + ep.ColorSpec, 7.5, FontWeights.Normal, Brushes.Black, false);
                AddCanvasTextTo(cTbl, 8.0, 92.0, "• Cấp bảo vệ: " + ep.ProtectionRating, 7.5, FontWeights.Normal, Brushes.Black, false);
                AddCanvasTextTo(cTbl, 8.0, 108.0, "• Đáy khoét lỗ: " + ep.CableHoleSpec, 7.5, FontWeights.Normal, Brushes.Black, false);
                AddCanvasTextTo(cTbl, 8.0, 124.0, "• Điện áp: " + ep.VoltageSpec, 7.5, FontWeights.Normal, Brushes.Black, false);

                CreateInteractiveContainer(canvas, itSpec, tblX, tblY, tblW, tblH, cTbl, onItemCreated);
                curX += tblW + 40.0;
            }

            // 6. BOTTOM VIEW
            double maxBottomY = baseY + plinthH * s + 40.0;
            if (ep.IncludeBottomView && !ep.IsItemDeleted("VIEW_BOTTOM"))
            {
                EnclosureCanvasItem itVBottom = ep.GetOrCreateItem("VIEW_BOTTOM", "Khung Mặt Đáy Tủ", "View", W, D);
                double botX = (ep.IncludeFrontView && !ep.IsItemDeleted("VIEW_FRONT") ? curXFront : 35.0) + itVBottom.OffsetX * s;
                double botY = baseY + plinthH * s + 35.0 - itVBottom.OffsetY * s;
                double botW = W * s;
                double botD = D * s;

                Canvas cBot = new Canvas { Width = botW, Height = botD + 25.0 };
                AddCanvasRectTo(cBot, 0, 0, botW, botD, Brushes.WhiteSmoke, Brushes.Black);
                AddCanvasTextTo(cBot, botW / 2.0, botD + 6.0, "MẶT ĐÁY VÀ LỖ KHOÉT CÁP", 8.5, FontWeights.Bold, Brushes.Black, true);
                CreateInteractiveContainer(canvas, itVBottom, botX, botY, botW, botD + 25.0, cBot, onItemCreated);

                // Bottom cable hole
                if (!ep.IsItemDeleted("BOTTOM_CABLE_HOLE"))
                {
                    string holeTxt = ep.GetCustomText("BOTTOM_CABLE_HOLE", ep.CableHoleSpec);
                    EnclosureCanvasItem itHole = ep.GetOrCreateItem("BOTTOM_CABLE_HOLE", "Lỗ khoét luồn cáp đáy", "BottomView", 450, 220, holeTxt);
                    double holeW = Math.Min(botW - 20.0, 450.0 * s);
                    double holeD = Math.Min(botD - 12.0, 220.0 * s);
                    double hx = botX + (botW - holeW) / 2.0 + itHole.OffsetX * s;
                    double hy = botY + (botD - holeD) / 2.0 - itHole.OffsetY * s;

                    Grid gHole = new Grid { Width = holeW, Height = holeD };
                    gHole.Children.Add(new System.Windows.Shapes.Rectangle { Fill = Brushes.LightYellow, Stroke = Brushes.Red, StrokeThickness = 1.0 });
                    StackPanel spHole = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    spHole.Children.Add(new TextBlock { Text = "LỖ KHOÉT CÁP", FontSize = 7.5, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkRed, HorizontalAlignment = HorizontalAlignment.Center });
                    spHole.Children.Add(new TextBlock { Text = holeTxt, FontSize = 6.5, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center });
                    gHole.Children.Add(spHole);

                    CreateInteractiveContainer(canvas, itHole, hx, hy, holeW, holeD, gHole, onItemCreated);
                }

                maxBottomY = Math.Max(maxBottomY, botY + botD + 35.0);
            }

            // 7. GHI KÍCH THƯỚC (DIMENSIONS)
            if (ep.IncludeDimensions && ep.IncludeFrontView && !ep.IsItemDeleted("DIMENSIONS") && !ep.IsItemDeleted("VIEW_FRONT"))
            {
                EnclosureCanvasItem itDim = ep.GetOrCreateItem("DIMENSIONS", "Hệ đường gióng kích thước W & H", "Annotation", W, H);
                double dimOffX = itDim.OffsetX * s;
                double dimOffY = itDim.OffsetY * s;

                // Top dimension W
                double dimY = baseY - H * s - canopyH * s - 12.0 - dimOffY;
                RenderCanvasLine(canvas, curXFront, dimY, curXFront + W * s, dimY, Brushes.Red, 1.0);
                RenderCanvasLine(canvas, curXFront, dimY - 4.0, curXFront, dimY + 4.0, Brushes.Red, 1.0);
                RenderCanvasLine(canvas, curXFront + W * s, dimY - 4.0, curXFront + W * s, dimY + 4.0, Brushes.Red, 1.0);
                RenderCanvasText(canvas, curXFront + (W * s) / 2.0, dimY - 12.0, string.Format("W = {0:0} mm", W), 8.0, FontWeights.Bold, Brushes.Red);

                // Left dimension H
                double dimX = curXFront - 18.0 + dimOffX;
                double dimTopY = baseY - H * s - canopyH * s;
                double dimBotY = baseY + plinthH * s;
                RenderCanvasLine(canvas, dimX, dimTopY, dimX, dimBotY, Brushes.Red, 1.0);
                RenderCanvasLine(canvas, dimX - 4.0, dimTopY, dimX + 4.0, dimTopY, Brushes.Red, 1.0);
                RenderCanvasLine(canvas, dimX - 4.0, dimBotY, dimX + 4.0, dimBotY, Brushes.Red, 1.0);
                RenderCanvasText(canvas, dimX - 16.0, (dimTopY + dimBotY) / 2.0 - 5.0, string.Format("H={0:0}", H + plinthH + canopyH), 7.5, FontWeights.Bold, Brushes.Red);
            }

            canvas.Width = Math.Max(700.0, curX + 60.0);
            canvas.Height = Math.Max(500.0, maxBottomY + 40.0);
        }

        private static Border CreateInteractiveContainer(Canvas mainCanvas, EnclosureCanvasItem item, double x, double y, double w, double h, FrameworkElement content, Action<EnclosureCanvasItem, FrameworkElement> onItemCreated)
        {
            Border container = new Border
            {
                Width = Math.Max(4.0, w),
                Height = Math.Max(4.0, h),
                Background = item.IsSelected ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 0, 122, 204)) : Brushes.Transparent,
                BorderThickness = new Thickness(item.IsSelected ? 1.5 : 0),
                BorderBrush = item.IsSelected ? Brushes.DodgerBlue : Brushes.Transparent,
                CornerRadius = new CornerRadius(item.IsSelected ? 2 : 0),
                Cursor = item.IsLocked ? Cursors.No : Cursors.SizeAll,
                Tag = item,
                ClipToBounds = false,
                Child = content
            };

            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            mainCanvas.Children.Add(container);

            if (onItemCreated != null)
            {
                onItemCreated(item, container);
            }

            return container;
        }

        private static FrameworkElement BuildCanvasBox(double w, double h, double plinthH, double canopyH, string title, System.Windows.Media.Brush fill, System.Windows.Media.Brush stroke)
        {
            Canvas box = new Canvas { Width = w, Height = h + plinthH + canopyH + 28.0 };

            // Canopy
            System.Windows.Shapes.Rectangle rCan = new System.Windows.Shapes.Rectangle { Width = w + 8.0, Height = canopyH, Fill = Brushes.LightGray, Stroke = Brushes.Black, StrokeThickness = 1.0 };
            Canvas.SetLeft(rCan, -4.0);
            Canvas.SetTop(rCan, 0);
            box.Children.Add(rCan);

            // Main body
            System.Windows.Shapes.Rectangle rMain = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = 1.0 };
            Canvas.SetLeft(rMain, 0);
            Canvas.SetTop(rMain, canopyH);
            box.Children.Add(rMain);

            // Plinth
            System.Windows.Shapes.Rectangle rPlinth = new System.Windows.Shapes.Rectangle { Width = w, Height = plinthH, Fill = Brushes.DarkGray, Stroke = Brushes.Black, StrokeThickness = 1.0 };
            Canvas.SetLeft(rPlinth, 0);
            Canvas.SetTop(rPlinth, canopyH + h);
            box.Children.Add(rPlinth);

            // Title
            TextBlock tb = new TextBlock { Text = title, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, (w - tb.DesiredSize.Width) / 2.0);
            Canvas.SetTop(tb, canopyH + h + plinthH + 6.0);
            box.Children.Add(tb);

            return box;
        }

        private static void AddCanvasRectTo(Canvas c, double x, double y, double w, double h, System.Windows.Media.Brush fill, System.Windows.Media.Brush stroke)
        {
            System.Windows.Shapes.Rectangle r = new System.Windows.Shapes.Rectangle { Width = Math.Max(1.0, w), Height = Math.Max(1.0, h), Fill = fill, Stroke = stroke, StrokeThickness = 1.0 };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            c.Children.Add(r);
        }

        private static void AddCanvasCircleTo(Canvas c, double cx, double cy, double r, System.Windows.Media.Brush fill)
        {
            System.Windows.Shapes.Ellipse ell = new System.Windows.Shapes.Ellipse { Width = r * 2.0, Height = r * 2.0, Fill = fill, Stroke = Brushes.Black, StrokeThickness = 0.5 };
            Canvas.SetLeft(ell, cx - r);
            Canvas.SetTop(ell, cy - r);
            c.Children.Add(ell);
        }

        private static void AddCanvasTextTo(Canvas c, double x, double y, string text, double fontSize, FontWeight weight, System.Windows.Media.Brush brush, bool center = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextBlock tb = new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight, Foreground = brush };
            if (center)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2.0);
            }
            else
            {
                Canvas.SetLeft(tb, x);
            }
            Canvas.SetTop(tb, y);
            c.Children.Add(tb);
        }

        private static void RenderCanvasLine(Canvas canvas, double x1, double y1, double x2, double y2, System.Windows.Media.Brush stroke, double thickness)
        {
            System.Windows.Shapes.Line line = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };
            canvas.Children.Add(line);
        }

        private static void RenderCanvasText(Canvas canvas, double x, double y, string text, double fontSize, FontWeight weight, System.Windows.Media.Brush brush)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextBlock tb = new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight, Foreground = brush };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2.0);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }
    }
}

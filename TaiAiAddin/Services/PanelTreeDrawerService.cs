using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ vẽ Sơ đồ nguyên lý dạng cây (Tree Single Line Diagram)
    /// Hỗ trợ cả 2 bố cục: Cây Đứng (Top-Down) và Cây Ngang / Riser (Left-Right)
    /// và 2 trường phái ký hiệu: Chuẩn Miền Bắc (TCVN/EVN NPC) & Chuẩn Miền Nam (IEC/EVN SPC)
    /// Tuân thủ 100% định dạng Text, Layer, Table và Block từ bản vẽ tham khảo MAU TU DIEN MIEN NAM.dwg.
    /// </summary>
    public static class PanelTreeDrawerService
    {
        private const string SouthBlockDir = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\BLOCK SDNL MIEN NAM";
        private const string DefaultTemplateDwg = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\MAU TU DIEN MIEN NAM.dwg";

        // Tên Layer chuẩn hóa đồng bộ 100% với bản vẽ mẫu đối chiếu
        private const string LayerBusbar = "E-BUSBAR";
        private const string LayerWire = "E-WIRE";
        private const string LayerDevice = "E-equi";
        private const string LayerText = "E-TEXT";
        private const string LayerFrame = "E-SCHEMATIC-FRAME";
        private const string LayerControl = "E-CONTROL";

        // Danh sách ID đối tượng vừa vẽ để phục vụ đóng gói Group và gắn XData cho Double-Click
        private static readonly List<ObjectId> _currentDrawnEntities = new List<ObjectId>();

        public static bool DrawTreeDiagram(Document doc, ElectricalPanel panel, Point3d origin, DrawingParameters p)
        {
            if (doc == null || panel == null) return false;
            if (p == null) p = new DrawingParameters();

            Database db = doc.Database;
            using (doc.LockDocument())
            {
                _currentDrawnEntities.Clear();

                // BƯỚC 1: Trích xuất Style 100% (Database phụ đọc ngầm bản vẽ mẫu)
                // và nạp toàn bộ Block từ thư mục BLOCK SDNL MIEN NAM
                ExtractAndSyncTemplate(db, p.ReferenceTemplateDwgPath);
                PreloadSldV2Blocks(db);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    EnsureLayer(db, tr, LayerBusbar, 1);  // Đỏ / Đồng (Thanh cái)
                    EnsureLayer(db, tr, LayerWire, 4);    // Cyan (Dây dẫn)
                    EnsureLayer(db, tr, LayerDevice, 3);  // Xanh lá (Thiết bị)
                    EnsureLayer(db, tr, LayerText, 7);    // Trắng/Đen (Văn bản)
                    EnsureLayer(db, tr, LayerFrame, 2);
                    EnsureLayer(db, tr, LayerControl, 4);   // Vàng (Khung tên)

                    EnsureTextStyle(db, tr, "Arial_2", "arial.ttf", 2.0);

                    if (p.LayoutScheme == LayoutScheme.SldV2Mcc)
                    {
                        DrawSldV2MccDiagram(db, tr, btr, panel, origin, p);
                    }
                    else if (p.TreeOrientation == TreeOrientation.HorizontalLeftRight)
                    {
                        DrawHorizontalTree(db, tr, btr, panel, origin, p);
                    }
                    else
                    {
                        DrawVerticalTree(db, tr, btr, panel, origin, p);
                    }

                    // BƯỚC 2: Gắn XData & Tạo AutoCAD Group để Double-Click mở đúng tủ & Preview
                    try
                    {
                        RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
                        if (!rat.Has(PanelStorageService.RegAppName))
                        {
                            RegAppTableRecord ratr = new RegAppTableRecord();
                            ratr.Name = PanelStorageService.RegAppName;
                            rat.Add(ratr);
                            tr.AddNewlyCreatedDBObject(ratr, true);
                        }

                        // Gắn dữ liệu cấu hình đầy đủ của tủ vào thực thể đầu tiên
                        ResultBuffer fullRb = PanelStorageService.SerializePanelToXData(panel, origin, p);
                        if (_currentDrawnEntities.Count > 0)
                        {
                            try
                            {
                                DBObject mainEnt = tr.GetObject(_currentDrawnEntities[0], OpenMode.ForWrite);
                                if (mainEnt != null)
                                {
                                    mainEnt.XData = fullRb;
                                }
                            }
                            catch { }
                        }

                        // Gắn XData nhận diện panelName lên tất cả các đối tượng đã vẽ (bắt đầu từ index 1 để không đè index 0)
                        ResultBuffer idRb = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, PanelStorageService.RegAppName),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, panel.PanelName)
                        );

                        for (int i = 1; i < _currentDrawnEntities.Count; i++)
                        {
                            ObjectId id = _currentDrawnEntities[i];
                            if (id.IsValid && !id.IsNull && !id.IsErased)
                            {
                                try
                                {
                                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                                    if (obj != null && !(obj is AttributeReference))
                                    {
                                        obj.XData = idRb;
                                    }
                                }
                                catch { }
                            }
                        }

                        // Gom các đối tượng vào Group TAIAI_PANEL_<panelName>
                        DBDictionary groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                        string cleanName = "TAIAI_PANEL_" + panel.PanelName.Replace("-", "_").Replace(" ", "_");
                        if (groupDict.Contains(cleanName))
                        {
                            Group oldG = tr.GetObject(groupDict.GetAt(cleanName), OpenMode.ForWrite) as Group;
                            if (oldG != null) oldG.Erase();
                        }
                        Group newG = new Group("Tủ điện " + panel.PanelName, true);
                        foreach (ObjectId id in _currentDrawnEntities)
                        {
                            if (id.IsValid && !id.IsNull && !id.IsErased)
                            {
                                try { newG.Append(id); } catch { }
                            }
                        }
                        groupDict.SetAt(cleanName, newG);
                        tr.AddNewlyCreatedDBObject(newG, true);
                        try
                        {
                            newG.XData = fullRb;
                        }
                        catch { }

                        // Lưu đồng bộ vào NamedObjectDictionary (NOD) của bản vẽ DWG
                        PanelStorageService.SavePanelToNod(db, panel);
                    }
                    catch { }

                    // Cập nhật bộ nhớ cache PanelStorageService
                    PanelStorageService.AddOrUpdatePanel(panel);

                    tr.Commit();
                    return true;
                }
            }
        }

        #region BƯỚC 1: Trích Xuất Style & Nạp Block Từ File Mẫu Đối Chiếu
        public static void ExtractAndSyncTemplate(Database targetDb, string userTemplatePath)
        {
            if (targetDb == null) return;

            string templatePath = (!string.IsNullOrEmpty(userTemplatePath) && File.Exists(userTemplatePath))
                ? userTemplatePath
                : DefaultTemplateDwg;

            // 1. Quét ngầm file DWG mẫu đối chiếu bằng Side Database (chạy nền không mở tab CAD)
            if (File.Exists(templatePath))
            {
                try
                {
                    using (Database sourceDb = new Database(false, true))
                    {
                        sourceDb.ReadDwgFile(templatePath, FileOpenMode.OpenForReadAndAllShare, true, "");

                        using (Transaction trSrc = sourceDb.TransactionManager.StartTransaction())
                        using (Transaction trTgt = targetDb.TransactionManager.StartTransaction())
                        {
                            // A. Sao chép và chuẩn hóa Layer
                            LayerTable ltSrc = (LayerTable)trSrc.GetObject(sourceDb.LayerTableId, OpenMode.ForRead);
                            LayerTable ltTgt = (LayerTable)trTgt.GetObject(targetDb.LayerTableId, OpenMode.ForWrite);

                            foreach (ObjectId lId in ltSrc)
                            {
                                LayerTableRecord ltrSrc = (LayerTableRecord)trSrc.GetObject(lId, OpenMode.ForRead);
                                string lName = ltrSrc.Name;

                                if (lName.StartsWith("E-", StringComparison.OrdinalIgnoreCase) ||
                                    lName.StartsWith("VNCC-", StringComparison.OrdinalIgnoreCase) ||
                                    lName.IndexOf("SCHEM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    lName.IndexOf("WIRE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    lName.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    lName.IndexOf("BUSBAR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    lName.IndexOf("EQUI", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (!ltTgt.Has(lName))
                                    {
                                        LayerTableRecord ltrNew = new LayerTableRecord();
                                        ltrNew.Name = lName;
                                        ltrNew.Color = ltrSrc.Color;
                                        ltrNew.LineWeight = ltrSrc.LineWeight;
                                        ltTgt.Add(ltrNew);
                                        trTgt.AddNewlyCreatedDBObject(ltrNew, true);
                                    }
                                }
                            }

                            // B. Sao chép TextStyle (Arial_2, ARIAL, Text KT, v.v.)
                            TextStyleTable stSrc = (TextStyleTable)trSrc.GetObject(sourceDb.TextStyleTableId, OpenMode.ForRead);
                            TextStyleTable stTgt = (TextStyleTable)trTgt.GetObject(targetDb.TextStyleTableId, OpenMode.ForWrite);

                            foreach (ObjectId sId in stSrc)
                            {
                                TextStyleTableRecord strSrc = (TextStyleTableRecord)trSrc.GetObject(sId, OpenMode.ForRead);
                                string sName = strSrc.Name;
                                if (!string.IsNullOrEmpty(sName) && (sName.IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0 || sName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    if (!stTgt.Has(sName))
                                    {
                                        TextStyleTableRecord strNew = new TextStyleTableRecord();
                                        strNew.Name = sName;
                                        strNew.FileName = strSrc.FileName;
                                        strNew.TextSize = strSrc.TextSize;
                                        strNew.XScale = strSrc.XScale;
                                        try { strNew.ObliquingAngle = strSrc.ObliquingAngle; } catch { }
                                        stTgt.Add(strNew);
                                        trTgt.AddNewlyCreatedDBObject(strNew, true);
                                    }
                                }
                            }

                            trSrc.Commit();
                            trTgt.Commit();
                        }
                    }
                }
                catch { }
            }

            // 2. Nạp toàn bộ Block từ thư mục BLOCK SDNL MIEN NAM
            if (Directory.Exists(SouthBlockDir))
            {
                string[] dwgFiles = Directory.GetFiles(SouthBlockDir, "*.dwg");
                foreach (string dwg in dwgFiles)
                {
                    string blockName = Path.GetFileNameWithoutExtension(dwg);
                    ImportBlockFromDwg(targetDb, blockName, dwg);
                }
            }

            // 3. Nạp toàn bộ Block từ thư mục BLOCK SDL V2 (Tủ Động Lực MCC / PCCC)
            string sldV2Dir = GetSldV2BlockDirectory();
            if (Directory.Exists(sldV2Dir))
            {
                string[] dwgFiles = Directory.GetFiles(sldV2Dir, "*.dwg");
                foreach (string dwg in dwgFiles)
                {
                    string blockName = Path.GetFileNameWithoutExtension(dwg);
                    ImportBlockFromDwg(targetDb, blockName, dwg);
                }
            }
        }

        public static string GetSldV2BlockDirectory()
        {
            try
            {
                string asmLoc = "";
                try { asmLoc = System.Reflection.Assembly.GetExecutingAssembly().Location; } catch { }
                string asmDir = !string.IsNullOrEmpty(asmLoc) ? Path.GetDirectoryName(asmLoc) : "";
                DirectoryInfo parentInfo = !string.IsNullOrEmpty(asmDir) ? Directory.GetParent(asmDir) : null;
                string parentDir = parentInfo != null ? parentInfo.FullName : "";

                List<string> candidates = new List<string>();
                if (!string.IsNullOrEmpty(asmDir))
                {
                    candidates.Add(Path.Combine(Path.Combine(Path.Combine(asmDir, "Thư Viện"), "Điện"), @"BLOCK SDL V2\BLOCK SDL V2"));
                    candidates.Add(Path.Combine(Path.Combine(Path.Combine(asmDir, "Thư Viện"), "Điện"), "BLOCK SDL V2"));
                }
                if (!string.IsNullOrEmpty(parentDir))
                {
                    candidates.Add(Path.Combine(Path.Combine(Path.Combine(parentDir, "Thư Viện"), "Điện"), @"BLOCK SDL V2\BLOCK SDL V2"));
                    candidates.Add(Path.Combine(Path.Combine(Path.Combine(parentDir, "Thư Viện"), "Điện"), "BLOCK SDL V2"));
                }
                candidates.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\BLOCK SDL V2\BLOCK SDL V2");
                candidates.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\BLOCK SDL V2");

                foreach (string path in candidates)
                {
                    if (Directory.Exists(path)) return path;
                }
            }
            catch { }
            return @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\BLOCK SDL V2\BLOCK SDL V2";
        }

        private static void PreloadSldV2Blocks(Database db)
        {
            if (db == null) return;
            string sldV2Dir = GetSldV2BlockDirectory();
            if (!Directory.Exists(sldV2Dir)) return;

            string[] blocksToLoad = new string[]
            {
                "aptomat",
                "main aptomat",
                "ASP_EL_INDICATING LED LIGHTS",
                "ASP_EL_FUSE",
                "ASP_EL_VOLTMETER",
                "ASP_EL_VOLTAGE METER SELECTION SWITCH",
                "ASP_EL_AMMETER",
                "ASP_EL_AMPER METER SELECTOR SWITCH",
                "ASP_EL_STAR-DELTA MOTOR STARTING",
                "ASP_EL_DIRECT ONLINE MOTOR STARTING",
                "ASP_EL_VARIABLE SPEED DRIVE",
                "Xbase_Note & Legen (ELEC)$0$ASP_EL_VARIABLE SPEED DRIVE",
                "2_dwg_CT-4345304-MAIN SINGLE LINE DIAGRAM SYSTEM -02 DD",
                "AMPE METER",
                "ĐÈN BÁO PHA",
                "VOLT METTER"
            };

            foreach (string bName in blocksToLoad)
            {
                string dwgPath = Path.Combine(sldV2Dir, bName + ".dwg");
                if (File.Exists(dwgPath))
                {
                    ImportBlockFromDwg(db, bName, dwgPath);
                }
            }
        }

        public static ObjectId GetOrLoadRcboBlock(Database db)
        {
            if (db == null) return ObjectId.Null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has("RCBO")) return bt["RCBO"];
                tr.Commit();
            }

            List<string> searchPaths = new List<string>();
            searchPaths.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\RCBO.dwg");
            searchPaths.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Thư Viện\Điện\RCBO.dwg");
            searchPaths.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\RCBO.dwg");

            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    searchPaths.Add(Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thư Viện\Điện\RCBO.dwg"));
                    searchPaths.Add(Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\RCBO.dwg"));
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory))
                {
                    searchPaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Thư Viện\Điện\RCBO.dwg"));
                }
            }
            catch { }

            try
            {
                string asmLoc = typeof(PanelTreeDrawerService).Assembly.Location;
                if (!string.IsNullOrEmpty(asmLoc))
                {
                    string asmDir = Path.GetDirectoryName(asmLoc);
                    if (!string.IsNullOrEmpty(asmDir))
                    {
                        searchPaths.Add(Path.Combine(asmDir, @"Thư Viện\Điện\RCBO.dwg"));
                        searchPaths.Add(Path.Combine(asmDir, "RCBO.dwg"));
                    }
                }
            }
            catch { }

            foreach (string p in searchPaths)
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    ObjectId bId = ImportBlockFromDwg(db, "RCBO", p);
                    if (bId != ObjectId.Null && !bId.IsErased) return bId;
                }
            }
            return ObjectId.Null;
        }

        private static ObjectId ImportBlockFromDwg(Database targetDb, string blockName, string dwgPath)
        {
            if (targetDb == null || string.IsNullOrEmpty(dwgPath) || !File.Exists(dwgPath)) return ObjectId.Null;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName))
                {
                    return bt[blockName];
                }
                tr.Commit();
            }

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, true, "");
                    return targetDb.Insert(blockName, sourceDb, true);
                }
            }
            catch
            {
                return ObjectId.Null;
            }
        }
        private static ObjectId EnsureLinetype(Database db, Transaction tr, string linetypeName)
        {
            if (db == null || tr == null || string.IsNullOrEmpty(linetypeName)) return ObjectId.Null;
            try
            {
                LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(linetypeName)) return ltt[linetypeName];
                try
                {
                    db.LoadLineTypeFile(linetypeName, "acad.lin");
                    if (ltt.Has(linetypeName)) return ltt[linetypeName];
                }
                catch
                {
                    try
                    {
                        db.LoadLineTypeFile(linetypeName, "acadiso.lin");
                        if (ltt.Has(linetypeName)) return ltt[linetypeName];
                    }
                    catch { }
                }
            }
            catch { }
            return ObjectId.Null;
        }

        #endregion

        #region Vẽ Cây Đứng (Top-Down Tree) Chuẩn Miền Nam / Miền Bắc
        private static void DrawVerticalTree(Database db, Transaction tr, BlockTableRecord btr, ElectricalPanel panel, Point3d origin, DrawingParameters p)
        {
            int loadCount = panel.Loads != null ? panel.Loads.Count : 0;
            int branchCount = Math.Max(1, loadCount);
            if (p.IncludeSpare) branchCount += Math.Max(1, p.SpareCount3P + p.SpareCount1P);

            bool isSouth = (p.SymbolRegion == SymbolRegion.South_Iec || p.LayoutScheme == LayoutScheme.SouthRegion);

            double branchSpacing = 50.0;
            double meterBranchWidth = isSouth ? 55.0 : 0.0;
            double totalWidth = Math.Max(420.0, branchCount * branchSpacing + meterBranchWidth + 100.0);
            double totalHeight = 300.0;

            double startX = origin.X + 45.0 + meterBranchWidth;
            double busbarY = origin.Y + totalHeight - 95.0;
            double endX = startX + (branchCount - 1) * branchSpacing;
            double midX = (startX + endX) / 2.0;

            if (p.IncludeTitleBlock)
            {
                DrawSheetFrame(btr, tr, db, origin, totalWidth + 40.0, totalHeight + 20.0, panel.PanelName, p);
            }

            // Nguồn vào Incomer trên đỉnh & MCCB Tổng (Tương tác tự do: Move, Rotate, Scale, Copy)
            double incomerTopY = busbarY + 75.0;
            Point3d mainCbPt = new Point3d(midX, busbarY + 40.0, 0);

            SldInteractiveItem mainCustom = (p != null) ? p.FindCustomItem("MAIN_BREAKER") : null;
            double mainCadOffX = 0, mainCadOffY = 0, mainCadRot = 0, mainCadScale = 1.0;
            if (mainCustom != null && !mainCustom.IsDeleted)
            {
                mainCadOffX = mainCustom.OffsetX / 2.8;
                mainCadOffY = -mainCustom.OffsetY / 2.8;
                mainCadRot = mainCustom.RotationAngle * Math.PI / 180.0;
                mainCadScale = mainCustom.Scale;
            }

            Point3d actualMainPt = new Point3d(mainCbPt.X + mainCadOffX, mainCbPt.Y + mainCadOffY, 0);

            if (mainCustom == null || !mainCustom.IsDeleted)
            {
                AddOrthogonalWire(btr, tr, new Point3d(midX, incomerTopY, 0), new Point3d(actualMainPt.X, actualMainPt.Y + 12.0 * mainCadScale, 0), LayerWire, 4);
                AddOrthogonalWire(btr, tr, new Point3d(actualMainPt.X, actualMainPt.Y - 12.0 * mainCadScale, 0), new Point3d(midX, busbarY, 0), LayerWire, 4);

                if (isSouth)
                {
                    BlockReference brMain = InsertBlockReference(btr, tr, db, "CB-MCB-MCCB tổng", actualMainPt, mainCadScale, mainCadRot);
                    if (brMain == null)
                    {
                        DrawBreakerSymbolVector(btr, tr, db, actualMainPt, true, 3, SymbolRegion.South_Iec, false, mainCadScale);
                    }

                    string incomerText = string.Format("NGUỒN VÀO TỦ: {0}\nMCCB TỔNG: {1}\nCÁP: {2}\nPtt = {3:0.##} kW, Itt = {4:0.#} A",
                        panel.PanelName,
                        panel.Incomer.ToString(),
                        !string.IsNullOrEmpty(panel.MainCableSpec) ? panel.MainCableSpec : "Cu/XLPE/PVC 4x25mm2 + E16mm2",
                        panel.TotalCalculatedPowerKW, panel.TotalCurrentA);
                    DrawMText(btr, tr, db, incomerText, new Point3d(actualMainPt.X + 16.0 * mainCadScale, actualMainPt.Y + 12.0, 0), 2.2, AttachmentPoint.MiddleLeft, LayerText);
                }
                else
                {
                    DrawBreakerSymbolVector(btr, tr, db, actualMainPt, true, 3, SymbolRegion.North_Tcvn, false, mainCadScale);
                    string incomerText = string.Format("NGUỒN VÀO: {0}\n{1}\nP = {2:0.##} kW, I = {3:0.#} A",
                        panel.Incomer.ToString(),
                        !string.IsNullOrEmpty(panel.MainCableSpec) ? panel.MainCableSpec : "Cu/XLPE/PVC 4x25mm2",
                        panel.TotalCalculatedPowerKW, panel.TotalCurrentA);
                    DrawMText(btr, tr, db, incomerText, new Point3d(actualMainPt.X + 15.0 * mainCadScale, actualMainPt.Y + 5.0, 0), 2.4, AttachmentPoint.MiddleLeft, LayerText);
                }
            }

            if (isSouth)
            {
                // Cụm đo đếm & Đèn báo pha
                double meterX = origin.X + 40.0;
                DrawLine(btr, tr, new Point3d(meterX, busbarY, 0), new Point3d(meterX, busbarY + 65.0, 0), LayerWire, 4);
                DrawLine(btr, tr, new Point3d(meterX, busbarY, 0), new Point3d(startX - 15.0, busbarY, 0), LayerWire, 4);

                Point3d fusePt = new Point3d(meterX, busbarY + 20.0, 0);
                InsertBlockReference(btr, tr, db, "fuse", fusePt, 1.0, 0.0);

                Point3d pmPt = new Point3d(meterX, busbarY + 42.0, 0);
                InsertBlockReference(btr, tr, db, "PM", pmPt, 1.0, 0.0);

                Point3d lampPt = new Point3d(meterX, busbarY + 62.0, 0);
                InsertBlockReference(btr, tr, db, "ĐÈN BÁO", lampPt, 1.0, 0.0);

                DrawMText(btr, tr, db, "ĐO LƯỜNG\n& BÁO PHA", new Point3d(meterX - 10.0, busbarY + 45.0, 0), 1.8, AttachmentPoint.MiddleRight, LayerText);
            }


            // Thanh cái chính
            DrawBusbar(btr, tr, new Point3d(startX - 20.0, busbarY, 0), new Point3d(endX + 20.0, busbarY, 0), LayerBusbar);
            double busbarCurrent = Math.Ceiling(panel.TotalCurrentA * 1.25 / 10.0) * 10;
            if (busbarCurrent < 63) busbarCurrent = 63;
            DrawMText(btr, tr, db, string.Format("THANH CÁI BUSBAR CHÍNH 3P+N: {0}A", busbarCurrent),
                new Point3d(startX - 20.0, busbarY + 5.0, 0), 2.2, AttachmentPoint.BottomLeft, LayerText);

            // Các nhánh rẽ xuống
            double branchBottomY = busbarY - 110.0;

            for (int i = 0; i < loadCount; i++)
            {
                ElectricalLoad load = panel.Loads[i];
                int phases = (load.Phase == PhaseType.ThreePhase ? 3 : 1);
                double branchX = startX + i * branchSpacing;

                Point3d cbPt = new Point3d(branchX, busbarY - 40.0, 0);

                SldInteractiveItem brkCustom = (p != null) ? p.FindCustomItem("LOAD_CB_" + i) : null;
                double bOffX = 0, bOffY = 0, bRot = 0, bScale = 1.0;
                if (brkCustom != null && !brkCustom.IsDeleted)
                {
                    bOffX = brkCustom.OffsetX / 2.8;
                    bOffY = -brkCustom.OffsetY / 2.8;
                    bRot = brkCustom.RotationAngle * Math.PI / 180.0;
                    bScale = brkCustom.Scale;
                }
                Point3d actualCbPt = new Point3d(cbPt.X + bOffX, cbPt.Y + bOffY, 0);

                if (brkCustom == null || !brkCustom.IsDeleted)
                {
                    AddOrthogonalWire(btr, tr, new Point3d(branchX, busbarY, 0), new Point3d(actualCbPt.X, actualCbPt.Y + 12.0 * bScale, 0), LayerWire, 4);
                    AddOrthogonalWire(btr, tr, new Point3d(actualCbPt.X, actualCbPt.Y - 12.0 * bScale, 0), new Point3d(branchX, branchBottomY, 0), LayerWire, 4);

                    if (isSouth)
                    {
                        BlockReference brBranch = InsertBlockReference(btr, tr, db, "MCB-MCCB-CB", actualCbPt, bScale, bRot);
                        if (brBranch == null)
                        {
                            DrawBreakerSymbolVector(btr, tr, db, actualCbPt, false, phases, SymbolRegion.South_Iec, false, bScale);
                        }
                    }
                    else
                    {
                        DrawBreakerSymbolVector(btr, tr, db, actualCbPt, false, phases, SymbolRegion.North_Tcvn, false, bScale);
                    }
                }

                string loadDesc = string.Format("{0}\n{1}\n{2:0.##}kW - {3}P\nI={4:0.#}A\n{5}",
                    !string.IsNullOrEmpty(load.CircuitNumber) ? load.CircuitNumber : ("C" + (i + 1)),
                    !string.IsNullOrEmpty(load.LoadName) ? load.LoadName : "Phụ tải",
                    load.PowerKW, phases,
                    load.CurrentA,
                    !string.IsNullOrEmpty(load.CableSpec) ? load.CableSpec : "2x2.5mm2");
                DrawMText(btr, tr, db, loadDesc, new Point3d(branchX, branchBottomY - 4.0, 0), 1.9, AttachmentPoint.TopCenter, LayerText);
            }

            // Nhánh dự phòng
            if (p.IncludeSpare)
            {
                int spareTotal = Math.Max(1, p.SpareCount3P + p.SpareCount1P);
                for (int s = 0; s < spareTotal; s++)
                {
                    int idx = loadCount + s;
                    double branchX = startX + idx * branchSpacing;

                    Point3d cbPt = new Point3d(branchX, busbarY - 40.0, 0);
                    int phases = (s < p.SpareCount3P) ? 3 : 1;

                    SldInteractiveItem spCustom = (p != null) ? p.FindCustomItem("SPARE_CB_" + s) : null;
                    double spOffX = 0, spOffY = 0, spRot = 0, spScale = 1.0;
                    if (spCustom != null && !spCustom.IsDeleted)
                    {
                        spOffX = spCustom.OffsetX / 2.8;
                        spOffY = -spCustom.OffsetY / 2.8;
                        spRot = spCustom.RotationAngle * Math.PI / 180.0;
                        spScale = spCustom.Scale;
                    }
                    Point3d actualSpPt = new Point3d(cbPt.X + spOffX, cbPt.Y + spOffY, 0);

                    if (spCustom == null || !spCustom.IsDeleted)
                    {
                        AddOrthogonalWire(btr, tr, new Point3d(branchX, busbarY, 0), new Point3d(actualSpPt.X, actualSpPt.Y + 12.0 * spScale, 0), LayerWire, 4);
                        AddOrthogonalWire(btr, tr, new Point3d(actualSpPt.X, actualSpPt.Y - 12.0 * spScale, 0), new Point3d(branchX, branchBottomY, 0), LayerWire, 4);

                        if (isSouth)
                        {
                            BlockReference brSp = InsertBlockReference(btr, tr, db, "MCB-MCCB-CB", actualSpPt, spScale, spRot);
                            if (brSp == null)
                            {
                                DrawBreakerSymbolVector(btr, tr, db, actualSpPt, false, phases, SymbolRegion.South_Iec, false, spScale);
                            }
                        }
                        else
                        {
                            DrawBreakerSymbolVector(btr, tr, db, actualSpPt, false, phases, SymbolRegion.North_Tcvn, false, spScale);
                        }
                    }

                    string spareDesc = string.Format("SPARE {0}\n{1}P - DỰ PHÒNG\nMCB {1}P 16A", s + 1, phases);
                    DrawMText(btr, tr, db, spareDesc, new Point3d(branchX, branchBottomY - 4.0, 0), 1.9, AttachmentPoint.TopCenter, LayerText);
                }
            }

            // Thanh tiếp địa PE
            if (isSouth)
            {
                double peY = branchBottomY - 45.0;
                DrawBusbar(btr, tr, new Point3d(startX - 20.0, peY, 0), new Point3d(endX + 20.0, peY, 0), LayerDevice);
                Point3d peBlockPt = new Point3d(startX - 30.0, peY, 0);
                InsertBlockReference(btr, tr, db, "PE", peBlockPt, 1.0, 0.0);
                DrawMText(btr, tr, db, "THANH NỐI ĐẤT TIẾP ĐỊA (PE BUSBAR)", new Point3d(startX - 20.0, peY - 4.0, 0), 2.0, AttachmentPoint.TopLeft, LayerText);
            }

            // Bảng tính toán phụ tải
            DrawCalculationScheduleTable(btr, tr, db, panel, new Point3d(origin.X + 20.0, origin.Y - 15.0, 0), totalWidth);

            // BƯỚC VẼ ĐỐI TƯỢNG SAO CHÉP (COPIED ITEMS TRÊN BẢN VẼ CAD)
            DrawCopiedItemsInCad(btr, tr, db, origin, p, isSouth);
        }
        #endregion

        #region Vẽ Cây Ngang / Riser (Horizontal Left-Right Tree)
        private static void DrawHorizontalTree(Database db, Transaction tr, BlockTableRecord btr, ElectricalPanel panel, Point3d origin, DrawingParameters p)
        {
            int loadCount = panel.Loads != null ? panel.Loads.Count : 0;
            int branchCount = Math.Max(1, loadCount);
            if (p.IncludeSpare) branchCount += Math.Max(1, p.SpareCount3P + p.SpareCount1P);

            bool isSouth = (p.SymbolRegion == SymbolRegion.South_Iec || p.LayoutScheme == LayoutScheme.SouthRegion);

            // NẾU LÀ KIỂU MIỀN NAM: Áp dụng 100% dạng tủ điện mẫu nằm ngang từ MAU TU DIEN MIEN NAM.dwg
            if (isSouth)
            {
                DrawSouthHorizontalDiagram(db, tr, btr, panel, origin, p);
                return;
            }

            double rowSpacing = 28.0;
            double totalHeight = Math.Max(280.0, branchCount * rowSpacing + 80.0);
            double totalWidth = 420.0;

            double startY = origin.Y + totalHeight - 50.0;
            double busbarX = origin.X + 90.0;
            double endY = startY - (branchCount - 1) * rowSpacing;
            double midY = (startY + endY) / 2.0;

            if (p.IncludeTitleBlock)
            {
                DrawSheetFrame(btr, tr, db, origin, totalWidth + 40.0, totalHeight + 20.0, panel.PanelName + " - RISER", p);
            }

            // Nguồn vào Incomer bên trái (Tương tác tự do: Move, Rotate, Scale, Copy)
            double incomerLeftX = origin.X + 15.0;
            Point3d mainCbPt = new Point3d(incomerLeftX + 38.0, midY, 0);

            SldInteractiveItem mainCustom = (p != null) ? p.FindCustomItem("MAIN_BREAKER") : null;
            double mainCadOffX = 0, mainCadOffY = 0, mainCadRot = 0, mainCadScale = 1.0;
            if (mainCustom != null && !mainCustom.IsDeleted)
            {
                mainCadOffX = mainCustom.OffsetX / 2.8;
                mainCadOffY = -mainCustom.OffsetY / 2.8;
                mainCadRot = mainCustom.RotationAngle * Math.PI / 180.0;
                mainCadScale = mainCustom.Scale;
            }

            Point3d actualMainPt = new Point3d(mainCbPt.X + mainCadOffX, mainCbPt.Y + mainCadOffY, 0);

            if (mainCustom == null || !mainCustom.IsDeleted)
            {
                AddOrthogonalWireH(btr, tr, new Point3d(incomerLeftX, midY, 0), new Point3d(actualMainPt.X - 12.0 * mainCadScale, actualMainPt.Y, 0), LayerWire, 4);
                DrawBreakerSymbolVector(btr, tr, db, actualMainPt, true, 3, SymbolRegion.North_Tcvn, true, mainCadScale);
                AddOrthogonalWireH(btr, tr, new Point3d(actualMainPt.X + 12.0 * mainCadScale, actualMainPt.Y, 0), new Point3d(busbarX, midY, 0), LayerWire, 4);

                string incomerText = string.Format("NGUỒN VÀO:\n{0}\n{1:0.##}kW - {2:0.#}A",
                    panel.Incomer.ToString(), panel.TotalCalculatedPowerKW, panel.TotalCurrentA);
                DrawMText(btr, tr, db, incomerText, new Point3d(actualMainPt.X - 15.0, actualMainPt.Y + 14.0, 0), 2.2, AttachmentPoint.BottomLeft, LayerText);
            }

            // Thanh cái trục đứng
            DrawBusbar(btr, tr, new Point3d(busbarX, startY + 15.0, 0), new Point3d(busbarX, endY - 15.0, 0), LayerBusbar);
            DrawMText(btr, tr, db, "BUSBAR", new Point3d(busbarX - 4.0, startY + 18.0, 0), 2.2, AttachmentPoint.BottomLeft, LayerText);

            // Các nhánh rẽ ngang
            for (int i = 0; i < loadCount; i++)
            {
                ElectricalLoad load = panel.Loads[i];
                int phases = (load.Phase == PhaseType.ThreePhase ? 3 : 1);
                double branchY = startY - i * rowSpacing;
                double branchRightX = busbarX + 240.0;

                Point3d cbPt = new Point3d(busbarX + 42.0, branchY, 0);

                SldInteractiveItem brkCustom = (p != null) ? p.FindCustomItem("LOAD_CB_" + i) : null;
                double bOffX = 0, bOffY = 0, bRot = 0, bScale = 1.0;
                if (brkCustom != null && !brkCustom.IsDeleted)
                {
                    bOffX = brkCustom.OffsetX / 2.8;
                    bOffY = -brkCustom.OffsetY / 2.8;
                    bRot = brkCustom.RotationAngle * Math.PI / 180.0;
                    bScale = brkCustom.Scale;
                }
                Point3d actualCbPt = new Point3d(cbPt.X + bOffX, cbPt.Y + bOffY, 0);

                if (brkCustom == null || !brkCustom.IsDeleted)
                {
                    AddOrthogonalWireH(btr, tr, new Point3d(busbarX, branchY, 0), new Point3d(actualCbPt.X - 12.0 * bScale, actualCbPt.Y, 0), LayerWire, 4);
                    DrawBreakerSymbolVector(btr, tr, db, actualCbPt, false, phases, SymbolRegion.North_Tcvn, true, bScale);
                    AddOrthogonalWireH(btr, tr, new Point3d(actualCbPt.X + 12.0 * bScale, actualCbPt.Y, 0), new Point3d(branchRightX, branchY, 0), LayerWire, 4);
                }

                string desc = string.Format("{0}: {1} ({2:0.##}kW) | {3}",
                    !string.IsNullOrEmpty(load.CircuitNumber) ? load.CircuitNumber : ("C" + (i + 1)),
                    !string.IsNullOrEmpty(load.LoadName) ? load.LoadName : "Tải nhánh",
                    load.PowerKW,
                    !string.IsNullOrEmpty(load.CableSpec) ? load.CableSpec : "Cáp 2x2.5");
                DrawMText(btr, tr, db, desc, new Point3d(busbarX + 85.0, branchY + 3.0, 0), 2.0, AttachmentPoint.BottomLeft, LayerText);
            }

            // Dự phòng Spare
            if (p.IncludeSpare)
            {
                int spareTotal = Math.Max(1, p.SpareCount3P + p.SpareCount1P);
                for (int s = 0; s < spareTotal; s++)
                {
                    int idx = loadCount + s;
                    double branchY = startY - idx * rowSpacing;
                    double branchRightX = busbarX + 240.0;

                    Point3d cbPt = new Point3d(busbarX + 42.0, branchY, 0);
                    int phases = (s < p.SpareCount3P) ? 3 : 1;

                    SldInteractiveItem spCustom = (p != null) ? p.FindCustomItem("SPARE_CB_" + s) : null;
                    double spOffX = 0, spOffY = 0, spRot = 0, spScale = 1.0;
                    if (spCustom != null && !spCustom.IsDeleted)
                    {
                        spOffX = spCustom.OffsetX / 2.8;
                        spOffY = -spCustom.OffsetY / 2.8;
                        spRot = spCustom.RotationAngle * Math.PI / 180.0;
                        spScale = spCustom.Scale;
                    }
                    Point3d actualSpPt = new Point3d(cbPt.X + spOffX, cbPt.Y + spOffY, 0);

                    if (spCustom == null || !spCustom.IsDeleted)
                    {
                        AddOrthogonalWireH(btr, tr, new Point3d(busbarX, branchY, 0), new Point3d(actualSpPt.X - 12.0 * spScale, actualSpPt.Y, 0), LayerWire, 4);
                        DrawBreakerSymbolVector(btr, tr, db, actualSpPt, false, phases, SymbolRegion.North_Tcvn, true, spScale);
                        AddOrthogonalWireH(btr, tr, new Point3d(actualSpPt.X + 12.0 * spScale, actualSpPt.Y, 0), new Point3d(branchRightX, branchY, 0), LayerWire, 4);
                    }

                    DrawMText(btr, tr, db, string.Format("SPARE {0}: LỘ DỰ PHÒNG {1}P", s + 1, phases),
                        new Point3d(busbarX + 85.0, branchY + 3.0, 0), 2.0, AttachmentPoint.BottomLeft, LayerText);
                }
            }

            // BƯỚC VẼ ĐỐI TƯỢNG SAO CHÉP (COPIED ITEMS TRÊN BẢN VẼ CAD)
            DrawCopiedItemsInCad(btr, tr, db, origin, p, false);
        }

        #region Tủ Điện Mẫu Nằm Ngang Kiểu Miền Nam (Trích Xuất Chuẩn 100% Từ MAU TU DIEN MIEN NAM.dwg)
        private static void DrawSouthHorizontalDiagram(Database db, Transaction tr, BlockTableRecord btr, ElectricalPanel panel, Point3d origin, DrawingParameters p)
        {
            int loadCount = panel.Loads != null ? panel.Loads.Count : 0;
            int branchCount = Math.Max(1, loadCount);
            if (p.IncludeSpare) branchCount += Math.Max(1, p.SpareCount3P + p.SpareCount1P);

            double rowH = 8.0;          // Khoảng cách hàng chuẩn 8.0mm từ MAU TU DIEN MIEN NAM.dwg
            double headerTitleH = 8.0;  // Hàng 1: SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN
            double headerInfoH = 8.0;   // Hàng 2: TÊN TỦ ĐIỆN / VỊ TRÍ ĐẶT TỦ
            double headerFromH = 8.0;   // Hàng 3: TỪ TỦ / KÍCH THƯỚC
            double colHeaderH = 16.0;   // Hàng tiêu đề các cột (2 tầng cho cụm pha)
            double topHeadersTotalH = headerTitleH + headerInfoH + headerFromH + colHeaderH; // 40.0mm
            double summaryH = 32.0;     // 4 hàng tổng kết đáy (mỗi hàng 8.0mm)

            double totalTableHeight = topHeadersTotalH + branchCount * rowH + summaryH;
            double tableTopY = origin.Y + totalTableHeight;
            double tableBottomY = origin.Y;

            // Tọa độ các thành phần chuẩn hóa 100% theo MAU TU DIEN MIEN NAM.dwg:
            double busbarX = origin.X + 25.0;
            double tableStartX = busbarX + 50.0;   // Mép trái của bảng (X0)
            double switchCenterX = tableStartX - 28.0; // Điểm đặt tiếp điểm hở ngoài bảng (chuẩn hàng dọc theo rule)

            // Bề rộng các cột chuẩn (Tổng = 300.0mm):
            double colProtectW = 50.0;   // THIẾT BỊ BẢO VỆ
            double colStarterW = 20.0;   // KHỞI ĐỘNG
            double colTagW = 14.0;       // LỘ
            double colCableW = 76.0;     // CÁP / DÂY
            double colPhaseAW = 14.0;    // A (W)
            double colPhaseBW = 14.0;    // B (W)
            double colPhaseCW = 14.0;    // C (W)
            double colPhaseABCW = 14.0;  // ABC (W)
            double colPhaseGroupW = colPhaseAW + colPhaseBW + colPhaseCW + colPhaseABCW; // 56.0
            double colDescW = 64.0;      // PHẠM VI CẤP ĐIỆN
            double colNoteW = 20.0;      // GHI CHÚ
            double totalTableW = colProtectW + colStarterW + colTagW + colCableW + colPhaseGroupW + colDescW + colNoteW; // 300.0mm
            double tableEndX = tableStartX + totalTableW;

            // X offsets của từng cột
            double colProtectX = tableStartX;
            double colStarterX = colProtectX + colProtectW;
            double colTagX = colStarterX + colStarterW;
            double colCableX = colTagX + colTagW;
            double colPhaseAX = colCableX + colCableW;
            double colPhaseBX = colPhaseAX + colPhaseAW;
            double colPhaseCX = colPhaseBX + colPhaseBW;
            double colPhaseABCX = colPhaseCX + colPhaseCW;
            double colDescX = colPhaseABCX + colPhaseABCW;
            double colNoteX = colDescX + colDescW;

            // 1. KẺ CÁC ĐƯỜNG NGANG KHỐI TIÊU ĐỀ (HEADER ROWS 1..4)
            // Đường đỉnh bảng
            DrawLine(btr, tr, new Point3d(tableStartX, tableTopY, 0), new Point3d(tableEndX, tableTopY, 0), LayerFrame, 2);
            // Ngăn Hàng 1 và Hàng 2
            DrawLine(btr, tr, new Point3d(tableStartX, tableTopY - 8.0, 0), new Point3d(tableEndX, tableTopY - 8.0, 0), LayerFrame, 2);
            // Ngăn Hàng 2 và Hàng 3
            DrawLine(btr, tr, new Point3d(tableStartX, tableTopY - 16.0, 0), new Point3d(tableEndX, tableTopY - 16.0, 0), LayerFrame, 2);
            // Ngăn Hàng 3 và Hàng 4
            DrawLine(btr, tr, new Point3d(tableStartX, tableTopY - 24.0, 0), new Point3d(tableEndX, tableTopY - 24.0, 0), LayerFrame, 2);
            // Ngăn Hàng 4 và các hàng phụ tải
            DrawLine(btr, tr, new Point3d(tableStartX, tableTopY - 40.0, 0), new Point3d(tableEndX, tableTopY - 40.0, 0), LayerFrame, 2);
            // Đường ngang phân tầng cụm Pha trong Hàng 4
            DrawLine(btr, tr, new Point3d(colPhaseAX, tableTopY - 32.0, 0), new Point3d(colDescX, tableTopY - 32.0, 0), LayerWire, 4);

            // 2. TEXT NỘI DUNG TIÊU ĐỀ (HEADER TEXTS)
            // Hàng 1: SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN (Căn trái chuẩn 100% bản vẽ mẫu)
            DrawMText(btr, tr, db, "SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN", new Point3d(tableStartX + 2.0, tableTopY - 4.0, 0), 2.3, AttachmentPoint.MiddleLeft, LayerText);

            // Hàng 2: TÊN TỦ ĐIỆN / VỊ TRÍ ĐẶT TỦ
            DrawMText(btr, tr, db, "TÊN TỦ ĐIỆN:", new Point3d(tableStartX + 2.0, tableTopY - 12.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);
            DrawMText(btr, tr, db, panel.PanelName.ToUpper(), new Point3d(colStarterX + 2.0, tableTopY - 12.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);
            string locationStr = "TẦNG ĐIỂN HÌNH";
            DrawMText(btr, tr, db, "VỊ TRÍ ĐẶT TỦ: " + locationStr, new Point3d(colPhaseAX + 2.0, tableTopY - 12.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);

            // Hàng 3: TỪ TỦ / KÍCH THƯỚC
            string fromSource = !string.IsNullOrEmpty(panel.ParentPanelName) ? panel.ParentPanelName : "MSB";
            DrawMText(btr, tr, db, "TỪ TỦ:", new Point3d(tableStartX + 2.0, tableTopY - 20.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);
            DrawMText(btr, tr, db, fromSource, new Point3d(colStarterX + 2.0, tableTopY - 20.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);
            string dimStr = "800x600x250";
            DrawMText(btr, tr, db, "KÍCH THƯỚC: " + dimStr, new Point3d(colPhaseAX + 2.0, tableTopY - 20.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);

            // Hàng 4: TIÊU ĐỀ CÁC CỘT
            DrawMText(btr, tr, db, "THIẾT BỊ BẢO VỆ", new Point3d(colProtectX + colProtectW / 2.0, tableTopY - 32.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "KHỞI ĐỘNG", new Point3d(colStarterX + colStarterW / 2.0, tableTopY - 32.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "LỘ", new Point3d(colTagX + colTagW / 2.0, tableTopY - 32.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "CÁP / DÂY", new Point3d(colCableX + colCableW / 2.0, tableTopY - 32.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText);

            DrawMText(btr, tr, db, "PHA, CÔNG SUẤT (W)", new Point3d(colPhaseAX + colPhaseGroupW / 2.0, tableTopY - 28.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "A", new Point3d(colPhaseAX + colPhaseAW / 2.0, tableTopY - 36.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "B", new Point3d(colPhaseBX + colPhaseBW / 2.0, tableTopY - 36.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "C", new Point3d(colPhaseCX + colPhaseCW / 2.0, tableTopY - 36.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "ABC", new Point3d(colPhaseABCX + colPhaseABCW / 2.0, tableTopY - 36.0, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);

            DrawMText(btr, tr, db, "PHẠM VI CẤP ĐIỆN", new Point3d(colDescX + colDescW / 2.0, tableTopY - 32.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "GHI CHÚ", new Point3d(colNoteX + colNoteW / 2.0, tableTopY - 32.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);

            double topBranchY = tableTopY - 40.0;
            double curY = topBranchY;

            // Biến cộng dồn công suất các pha
            double sumA = 0, sumB = 0, sumC = 0, sumABC = 0;

            // 3. VẼ CÁC HÀNG PHỤ TẢI NẰM NGANG
            for (int i = 0; i < loadCount; i++)
            {
                ElectricalLoad load = panel.Loads[i];
                double rowY = curY - rowH / 2.0;

                // Đường viền ngang của hàng trong bảng
                DrawLine(btr, tr, new Point3d(tableStartX, curY - rowH, 0), new Point3d(tableEndX, curY - rowH, 0), LayerWire, 4);

                Point3d swPt = new Point3d(switchCenterX, rowY, 0);
                SldInteractiveItem brkCustom = (p != null) ? p.FindCustomItem("LOAD_CB_" + i) : null;
                double bOffX = 0, bOffY = 0, bRot = 0, bScale = 1.0;
                if (brkCustom != null && !brkCustom.IsDeleted)
                {
                    bOffX = brkCustom.OffsetX / 2.8;
                    bOffY = -brkCustom.OffsetY / 2.8;
                    bRot = brkCustom.RotationAngle * Math.PI / 180.0;
                    bScale = brkCustom.Scale;
                }
                Point3d actualSwPt = new Point3d(swPt.X + bOffX, swPt.Y + bOffY, 0);

                if (brkCustom == null || !brkCustom.IsDeleted)
                {
                    bool isRcbo = (!string.IsNullOrEmpty(load.BreakerType) && (load.BreakerType.IndexOf("RCBO", StringComparison.OrdinalIgnoreCase) >= 0 || load.BreakerType.IndexOf("RCCB", StringComparison.OrdinalIgnoreCase) >= 0))
                        || (!string.IsNullOrEmpty(load.BreakerSpec) && (load.BreakerSpec.IndexOf("RCBO", StringComparison.OrdinalIgnoreCase) >= 0 || load.BreakerSpec.IndexOf("RCCB", StringComparison.OrdinalIgnoreCase) >= 0));

                    ObjectId rcboBlockId = isRcbo ? GetOrLoadRcboBlock(db) : ObjectId.Null;

                    AddOrthogonalWireH(btr, tr, new Point3d(busbarX, rowY, 0), new Point3d(actualSwPt.X - 6.25 * bScale, actualSwPt.Y, 0), LayerWire, 3);

                    if (isRcbo && rcboBlockId != ObjectId.Null && !rcboBlockId.IsErased)
                    {
                        // Chèn thẳng Block RCBO nguyên khối từ thư viện, không phân rã, luôn giữ hướng ngang
                        BlockReference blkRcbo = new BlockReference(new Point3d(actualSwPt.X - 6.25 * bScale, actualSwPt.Y, 0), rcboBlockId);
                        double scaleVal = (1.0 / 150.0) * bScale;
                        blkRcbo.ScaleFactors = new Scale3d(scaleVal, scaleVal, 1.0);
                        blkRcbo.Rotation = 0.0;
                        blkRcbo.Layer = LayerDevice;
                        btr.AppendEntity(blkRcbo);
                        tr.AddNewlyCreatedDBObject(blkRcbo, true);
                    }
                    else
                    {
                        DrawSouthBranchSwitch(btr, tr, actualSwPt, LayerDevice, false, bScale);
                    }

                    AddOrthogonalWireH(btr, tr, new Point3d(actualSwPt.X + 6.25 * bScale, actualSwPt.Y, 0), new Point3d(tableStartX, rowY, 0), LayerWire, 3);
                }

                // CỘT 1: THIẾT BỊ BẢO VỆ (CHỈ CHỨA CHỮ, KHÔNG CÓ KÝ HIỆU HÌNH HỌC CHÈN LÊN)
                string breakerStr;
                if (!string.IsNullOrEmpty(load.BreakerSpec))
                {
                    breakerStr = load.BreakerSpec;
                }
                else if (!string.IsNullOrEmpty(load.BreakerType))
                {
                    breakerStr = string.Format("{0} {1}A", load.BreakerType, Math.Max(16, Math.Ceiling(load.CurrentA * 1.25)));
                }
                else
                {
                    breakerStr = load.Phase == PhaseType.ThreePhase
                        ? string.Format("MCB-3P-{0}A-10kA", Math.Max(16, Math.Ceiling(load.CurrentA * 1.25)))
                        : string.Format("MCB-1P-{0}A-6kA", Math.Max(10, Math.Ceiling(load.CurrentA * 1.25)));
                }
                DrawMText(btr, tr, db, breakerStr, new Point3d(colProtectX + 3.0, rowY, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);

                // CỘT 2: KHỞI ĐỘNG
                string starter = (load.Starter != StarterType.Direct && !string.IsNullOrEmpty(load.StarterDisplay))
                    ? load.StarterDisplay
                    : ((load.LoadName != null && (load.LoadName.IndexOf("quạt", StringComparison.OrdinalIgnoreCase) >= 0 || load.LoadName.IndexOf("bơm", StringComparison.OrdinalIgnoreCase) >= 0 || load.LoadName.IndexOf("fcu", StringComparison.OrdinalIgnoreCase) >= 0))
                        ? "DOL" : "-");
                DrawMText(btr, tr, db, starter, new Point3d(colStarterX + colStarterW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);

                // CỘT 3: LỘ (Tag)
                string tag = !string.IsNullOrEmpty(load.CircuitNumber) ? load.CircuitNumber : ("C" + (i + 1));
                DrawMText(btr, tr, db, tag, new Point3d(colTagX + colTagW / 2.0, rowY, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);

                // CỘT 4: CÁP / DÂY
                string cableStr = !string.IsNullOrEmpty(load.CableSpec)
                    ? load.CableSpec
                    : (load.Phase == PhaseType.ThreePhase ? "4Cu/PVC(1x4)+(1x4)E PVC D25" : "2Cu/PVC(1x2.5)+(1x2.5)E PVC D20");
                DrawMText(btr, tr, db, cableStr, new Point3d(colCableX + 3.0, rowY, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);

                // CỘT 5: PHÂN BỔ CÔNG SUẤT (A - B - C - ABC)
                double pWatts = load.PowerKW * 1000.0;
                string valA = "", valB = "", valC = "", valABC = "";

                if (load.Phase == PhaseType.ThreePhase)
                {
                    double pPerPh = pWatts / 3.0;
                    sumA += pPerPh; sumB += pPerPh; sumC += pPerPh;
                    sumABC += pWatts;
                    valA = string.Format("{0:0}", pPerPh);
                    valB = string.Format("{0:0}", pPerPh);
                    valC = string.Format("{0:0}", pPerPh);
                    valABC = string.Format("{0:0}", pWatts);
                }
                else
                {
                    int phIdx;
                    if (load.PowerR > 0.0001 && load.PowerY <= 0.0001 && load.PowerB <= 0.0001)
                    {
                        phIdx = 0;
                    }
                    else if (load.PowerY > 0.0001 && load.PowerR <= 0.0001 && load.PowerB <= 0.0001)
                    {
                        phIdx = 1;
                    }
                    else if (load.PowerB > 0.0001 && load.PowerR <= 0.0001 && load.PowerY <= 0.0001)
                    {
                        phIdx = 2;
                    }
                    else
                    {
                        string ap = (load.AssignedPhase ?? "").Trim().ToUpper();
                        if (ap == "R" || ap == "A" || ap == "1") phIdx = 0;
                        else if (ap == "Y" || ap == "PHA B" || ap == "2") phIdx = 1;
                        else if (ap == "B" || ap == "C" || ap == "PHA C" || ap == "3") phIdx = 2;
                        else phIdx = i % 3;
                    }

                    if (phIdx == 0) { sumA += pWatts; valA = string.Format("{0:0}", pWatts); }
                    else if (phIdx == 1) { sumB += pWatts; valB = string.Format("{0:0}", pWatts); }
                    else { sumC += pWatts; valC = string.Format("{0:0}", pWatts); }
                }

                if (!string.IsNullOrEmpty(valA)) DrawMText(btr, tr, db, valA, new Point3d(colPhaseAX + colPhaseAW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
                if (!string.IsNullOrEmpty(valB)) DrawMText(btr, tr, db, valB, new Point3d(colPhaseBX + colPhaseBW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
                if (!string.IsNullOrEmpty(valC)) DrawMText(btr, tr, db, valC, new Point3d(colPhaseCX + colPhaseCW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);

                // CỘT 6: PHẠM VI CẤP ĐIỆN (Load Name)
                string desc = !string.IsNullOrEmpty(load.LoadName) ? load.LoadName : "Cấp nguồn phụ tải";
                DrawMText(btr, tr, db, desc, new Point3d(colDescX + 3.0, rowY, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);

                // CỘT 7: GHI CHÚ (STT)
                DrawMText(btr, tr, db, (i + 1).ToString(), new Point3d(colNoteX + colNoteW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);

                curY -= rowH;
            }

            // 3.1. VẼ ĐƯỜNG TRỤC LIÊN KẾT NHÓM & NHÁNH ĐIỀU KHIỂN (ÁP DỤNG DUY NHẤT CHO BMS, PLC, TIMER)
            ObjectId dashedLtId = EnsureLinetype(db, tr, "DASHED");
            if (dashedLtId == ObjectId.Null) dashedLtId = EnsureLinetype(db, tr, "HIDDEN");

            Action<Point3d, Point3d, short> drawCadDashedLine = (ptA, ptB, colIdx) =>
            {
                Line l = new Line(ptA, ptB);
                l.Layer = LayerControl;
                l.ColorIndex = colIdx;
                if (dashedLtId != ObjectId.Null && dashedLtId != db.ContinuousLinetype)
                {
                    l.LinetypeId = dashedLtId;
                    l.LinetypeScale = 1.0;
                }
                btr.AppendEntity(l);
                tr.AddNewlyCreatedDBObject(l, true);
            };

            Action<Point3d, double, short> drawCadLeftArrow = (tipPt, arrSize, colIdx) =>
            {
                Solid arrow = new Solid(
                    tipPt,
                    new Point3d(tipPt.X + arrSize, tipPt.Y + arrSize * 0.35, 0),
                    new Point3d(tipPt.X + arrSize, tipPt.Y - arrSize * 0.35, 0)
                );
                arrow.Layer = LayerControl;
                arrow.ColorIndex = colIdx;
                btr.AppendEntity(arrow);
                tr.AddNewlyCreatedDBObject(arrow, true);
            };

            ControlMode[] validModes = new ControlMode[] { ControlMode.BMS, ControlMode.PLC, ControlMode.Timer };
            int groupIndexCounter = 0;

            foreach (ControlMode mode in validModes)
            {
                List<Tuple<int, ElectricalLoad, double>> groupedLoads = new List<Tuple<int, ElectricalLoad, double>>();
                for (int i = 0; i < loadCount; i++)
                {
                    ElectricalLoad ld = panel.Loads[i];
                    if (ld.Control == mode && ld.IsControlGrouped)
                    {
                        double rowY = tableTopY - topHeadersTotalH - i * rowH - rowH / 2.0;
                        groupedLoads.Add(new Tuple<int, ElectricalLoad, double>(i, ld, rowY));
                    }
                }

                if (groupedLoads.Count > 0)
                {
                    double trunkX = colStarterX - 4.0 - groupIndexCounter * 4.0;
                    double trunkTopY = tableTopY - topHeadersTotalH + 2.5 + groupIndexCounter * 2.5;
                    double arrowTipX = switchCenterX - 14.0 - groupIndexCounter * 12.0;
                    short ctrlColor = 4; // Cyan

                    string modeLabel = (mode == ControlMode.BMS) ? "BMS" : (mode == ControlMode.PLC ? "PLC" : "TIMER");
                    double minBranchEndY = double.MaxValue;

                    foreach (var tuple in groupedLoads)
                    {
                        double rY = tuple.Item3;
                        double branchEndY = rY + 3.0;
                        minBranchEndY = Math.Min(minBranchEndY, branchEndY);
                        drawCadDashedLine(new Point3d(colStarterX + 2.0, rY, 0), new Point3d(trunkX, branchEndY, 0), ctrlColor);
                    }

                    drawCadDashedLine(new Point3d(trunkX, minBranchEndY, 0), new Point3d(trunkX, trunkTopY, 0), ctrlColor);
                    drawCadDashedLine(new Point3d(trunkX, trunkTopY, 0), new Point3d(arrowTipX, trunkTopY, 0), ctrlColor);
                    drawCadLeftArrow(new Point3d(arrowTipX, trunkTopY, 0), 2.2, ctrlColor);
                    DrawMText(btr, tr, db, modeLabel, new Point3d(arrowTipX + 3.0, trunkTopY + 0.6, 0), 2.0, AttachmentPoint.BottomLeft, LayerControl);

                    groupIndexCounter++;
                }

                for (int i = 0; i < loadCount; i++)
                {
                    ElectricalLoad ld = panel.Loads[i];
                    if (ld.Control == mode && !ld.IsControlGrouped)
                    {
                        double rowY = tableTopY - topHeadersTotalH - i * rowH - rowH / 2.0;
                        double lineY = rowY + 2.5;
                        double indArrowTipX = tableStartX - 14.0;
                        short ctrlColor = 4;
                        string modeLabel = (mode == ControlMode.BMS) ? "BMS" : (mode == ControlMode.PLC ? "PLC" : "TIMER");

                        drawCadDashedLine(new Point3d(colStarterX + 2.0, lineY, 0), new Point3d(indArrowTipX, lineY, 0), ctrlColor);
                        drawCadLeftArrow(new Point3d(indArrowTipX, lineY, 0), 2.0, ctrlColor);
                        DrawMText(btr, tr, db, modeLabel, new Point3d(indArrowTipX + 2.5, lineY + 0.5, 0), 1.8, AttachmentPoint.BottomLeft, LayerControl);
                    }
                }
            }

            // 4. CÁC HÀNG DỰ PHÒNG (SPARE)
            if (p.IncludeSpare)
            {
                int spareTotal = Math.Max(1, p.SpareCount3P + p.SpareCount1P);
                for (int s = 0; s < spareTotal; s++)
                {
                    double rowY = curY - rowH / 2.0;
                    DrawLine(btr, tr, new Point3d(tableStartX, curY - rowH, 0), new Point3d(tableEndX, curY - rowH, 0), LayerWire, 4);

                    Point3d swPt = new Point3d(switchCenterX, rowY, 0);
                    SldInteractiveItem spCustom = (p != null) ? p.FindCustomItem("SPARE_CB_" + s) : null;
                    double spOffX = 0, spOffY = 0, spRot = 0, spScale = 1.0;
                    if (spCustom != null && !spCustom.IsDeleted)
                    {
                        spOffX = spCustom.OffsetX / 2.8;
                        spOffY = -spCustom.OffsetY / 2.8;
                        spRot = spCustom.RotationAngle * Math.PI / 180.0;
                        spScale = spCustom.Scale;
                    }
                    Point3d actualSwPt = new Point3d(swPt.X + spOffX, swPt.Y + spOffY, 0);

                    if (spCustom == null || !spCustom.IsDeleted)
                    {
                        AddOrthogonalWireH(btr, tr, new Point3d(busbarX, rowY, 0), new Point3d(actualSwPt.X - 6.25 * spScale, actualSwPt.Y, 0), LayerWire, 3);
                        DrawSouthBranchSwitch(btr, tr, actualSwPt, LayerDevice, false, spScale);
                        AddOrthogonalWireH(btr, tr, new Point3d(actualSwPt.X + 6.25 * spScale, actualSwPt.Y, 0), new Point3d(tableStartX, rowY, 0), LayerWire, 3);
                    }

                    int phases = (s < p.SpareCount3P) ? 3 : 1;
                    DrawMText(btr, tr, db, string.Format("MCB-{0}P-16A-6kA", phases), new Point3d(colProtectX + 3.0, rowY, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
                    DrawMText(btr, tr, db, "-", new Point3d(colStarterX + colStarterW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
                    DrawMText(btr, tr, db, string.Format("SP{0}", s + 1), new Point3d(colTagX + colTagW / 2.0, rowY, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
                    DrawMText(btr, tr, db, "-", new Point3d(colCableX + 3.0, rowY, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);
                    DrawMText(btr, tr, db, "DỰ PHÒNG", new Point3d(colDescX + 3.0, rowY, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);
                    DrawMText(btr, tr, db, (loadCount + s + 1).ToString(), new Point3d(colNoteX + colNoteW / 2.0, rowY, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);

                    curY -= rowH;
                }
            }

            double bottomBranchY = curY;

            // 5. CÁC HÀNG TỔNG KẾT ĐÁY BẢNG (4 HÀNG TỔNG KẾT CHUẨN)
            double curSumY = bottomBranchY;
            double sRowH = 8.0;

            // Hàng 1: ĐÈN BÁO PHA / TỔNG CÔNG SUẤT THEO PHA
            DrawLine(btr, tr, new Point3d(tableStartX, curSumY - sRowH, 0), new Point3d(tableEndX, curSumY - sRowH, 0), LayerFrame, 2);
            DrawMText(btr, tr, db, "ĐÈN BÁO PHA 3x2W", new Point3d(colProtectX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0}", sumA), new Point3d(colPhaseAX + colPhaseAW / 2.0, curSumY - sRowH / 2.0, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0}", sumB), new Point3d(colPhaseBX + colPhaseBW / 2.0, curSumY - sRowH / 2.0, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0}", sumC), new Point3d(colPhaseCX + colPhaseCW / 2.0, curSumY - sRowH / 2.0, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0}", sumABC), new Point3d(colPhaseABCX + colPhaseABCW / 2.0, curSumY - sRowH / 2.0, 0), 1.8, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "TỔNG (W)", new Point3d(colDescX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            curSumY -= sRowH;

            // Hàng 2: ĐỒNG HỒ PM / CÔNG SUẤT THIẾT KẾ
            DrawLine(btr, tr, new Point3d(tableStartX, curSumY - sRowH, 0), new Point3d(tableEndX, curSumY - sRowH, 0), LayerFrame, 2);
            DrawMText(btr, tr, db, "ĐỒNG HỒ PM", new Point3d(colProtectX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0}", sumA + sumB + sumC), new Point3d(colPhaseAX + colPhaseGroupW / 2.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "CÔNG SUẤT THIẾT KẾ (W)", new Point3d(colDescX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            curSumY -= sRowH;

            // Hàng 3: BIẾN DÒNG CT / HỆ SỐ ĐỒNG THỜI
            DrawLine(btr, tr, new Point3d(tableStartX, curSumY - sRowH, 0), new Point3d(tableEndX, curSumY - sRowH, 0), LayerFrame, 2);
            DrawMText(btr, tr, db, "CT 50/5A", new Point3d(colProtectX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            double kdt = panel.SimultaneousFactor > 0 ? panel.SimultaneousFactor : 0.8;
            DrawMText(btr, tr, db, string.Format("{0:0.##}", kdt), new Point3d(colPhaseAX + colPhaseGroupW / 2.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "HỆ SỐ ĐỒNG THỜI", new Point3d(colDescX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            curSumY -= sRowH;

            // Hàng 4: MCCB NGUỒN TỔNG / CÔNG SUẤT TÍNH TOÁN
            DrawLine(btr, tr, new Point3d(tableStartX, curSumY - sRowH, 0), new Point3d(tableEndX, curSumY - sRowH, 0), LayerFrame, 2);
            string mainBreaker = !string.IsNullOrEmpty(panel.MainBreakerSpec) ? panel.MainBreakerSpec : "MCCB-3P-32A-18kA";
            DrawMText(btr, tr, db, mainBreaker, new Point3d(colProtectX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            DrawMText(btr, tr, db, "ĐẦU VÀO", new Point3d(colStarterX + colStarterW / 2.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0}", panel.TotalCalculatedPowerKW * 1000.0), new Point3d(colPhaseAX + colPhaseGroupW / 2.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("CÔNG SUẤT TÍNH TOÁN (W)  [{0:0.#} A]", panel.TotalCurrentA), new Point3d(colDescX + 3.0, curSumY - sRowH / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
            curSumY -= sRowH;

            // 6. KẺ CÁC VẠCH CHIA CỘT DỌC CỦA TOÀN BẢNG (CHUẨN TỌA ĐỘ VÀ PHÂN TẦNG THEO RULE)
            // Viền trái & viền phải: chạy trọn chiều cao bảng
            DrawLine(btr, tr, new Point3d(tableStartX, tableTopY, 0), new Point3d(tableStartX, tableBottomY, 0), LayerFrame, 2);
            DrawLine(btr, tr, new Point3d(tableEndX, tableTopY, 0), new Point3d(tableEndX, tableBottomY, 0), LayerFrame, 2);

            // Các vạch ngăn xuất phát từ Hàng 2 (TÊN TỦ ĐIỆN, VỊ TRÍ ĐẶT TỦ) chạy thẳng xuống đáy bảng:
            DrawLine(btr, tr, new Point3d(colStarterX, tableTopY - 8.0, 0), new Point3d(colStarterX, tableBottomY, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(colTagX, tableTopY - 8.0, 0), new Point3d(colTagX, tableBottomY, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(colPhaseAX, tableTopY - 8.0, 0), new Point3d(colPhaseAX, tableBottomY, 0), LayerWire, 4);

            // Các vạch ngăn xuất phát từ Hàng 4 (TIÊU ĐỀ CỘT):
            DrawLine(btr, tr, new Point3d(colCableX, tableTopY - 24.0, 0), new Point3d(colCableX, bottomBranchY, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(colDescX, tableTopY - 24.0, 0), new Point3d(colDescX, tableBottomY, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(colNoteX, tableTopY - 24.0, 0), new Point3d(colNoteX, bottomBranchY, 0), LayerWire, 4);

            // Các vạch chia sub-columns pha A, B, C, ABC (từ tầng dưới Hàng 4 xuống hết Hàng Đèn Báo Pha):
            DrawLine(btr, tr, new Point3d(colPhaseBX, tableTopY - 32.0, 0), new Point3d(colPhaseBX, bottomBranchY - 8.0, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(colPhaseCX, tableTopY - 32.0, 0), new Point3d(colPhaseCX, bottomBranchY - 8.0, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(colPhaseABCX, tableTopY - 32.0, 0), new Point3d(colPhaseABCX, bottomBranchY - 8.0, 0), LayerWire, 4);

            // 7. VẼ THANH CÁI BUSBAR TRỤC ĐỨNG (LAYER E-BUSBAR)
            double busbarTopY = topBranchY;
            double busbarBottomY = bottomBranchY - 28.0; // Đi thẳng xuống gặp dây nguồn hàng MCCB
            DrawBusbar(btr, tr, new Point3d(busbarX, busbarTopY, 0), new Point3d(busbarX, busbarBottomY, 0), LayerBusbar);
            DrawMText(btr, tr, db, "BUSBAR", new Point3d(busbarX - 4.0, busbarTopY + 3.0, 0), 2.0, AttachmentPoint.BottomLeft, LayerText);

            // 8. CỤM ĐO ĐẾM VÀ ĐÈN BÁO PHA CHUẨN 100% THEO RULE (media_1793512457503_f49c574b.png)
            DrawSouthMeteringCluster(btr, tr, db, busbarX, tableStartX, bottomBranchY, tableBottomY, p);

            // 9. KÝ HIỆU TIẾP ĐỊA PE DƯỚI ĐÁY BẢNG
            Point3d pePos = new Point3d(colStarterX + 5.0, tableBottomY, 0);
            BlockReference brPe = InsertBlockReference(btr, tr, db, "PE", pePos, 1.0, 0.0, 10.0);
            if (brPe == null)
            {
                // Vẽ ký hiệu tiếp địa chuẩn (3 vạch chân rết)
                DrawLine(btr, tr, new Point3d(pePos.X, pePos.Y, 0), new Point3d(pePos.X, pePos.Y - 5.0, 0), LayerDevice, 3);
                DrawLine(btr, tr, new Point3d(pePos.X - 5.0, pePos.Y - 5.0, 0), new Point3d(pePos.X + 5.0, pePos.Y - 5.0, 0), LayerDevice, 3);
                DrawLine(btr, tr, new Point3d(pePos.X - 3.2, pePos.Y - 7.0, 0), new Point3d(pePos.X + 3.2, pePos.Y - 7.0, 0), LayerDevice, 3);
                DrawLine(btr, tr, new Point3d(pePos.X - 1.5, pePos.Y - 9.0, 0), new Point3d(pePos.X + 1.5, pePos.Y - 9.0, 0), LayerDevice, 3);
                DrawMText(btr, tr, db, "PE", new Point3d(pePos.X + 8.0, pePos.Y - 6.0, 0), 2.0, AttachmentPoint.MiddleLeft, LayerText);
            }

            // BƯỚC VẼ ĐỐI TƯỢNG SAO CHÉP (COPIED ITEMS TRÊN BẢN VẼ CAD)
            DrawCopiedItemsInCad(btr, tr, db, origin, p, true);
        }

        private static void DrawSouthMeteringCluster(BlockTableRecord btr, Transaction tr, Database db, double busbarX, double tableStartX, double bottomBranchY, double tableBottomY, DrawingParameters p)
        {
            double X0 = tableStartX;
            double yIncomer = bottomBranchY - 28.0; // Tâm hàng MCCB Nguồn tổng
            double yRow3 = bottomBranchY - 20.0;    // Tâm hàng CT 50/5A
            double yRow2 = bottomBranchY - 12.0;    // Tâm hàng ĐỒNG HỒ PM
            double yRow1 = bottomBranchY - 4.0;     // Tâm hàng ĐÈN BÁO PHA 3x2W

            // Tọa độ X chuẩn theo Rule & đóng cắt Incomer đặt trước Đèn báo (khi dòng điện đi từ ĐẦU VÀO vào tủ):
            // Busbar (X0 - 50.0) -> PM (X0 - 38.0) -> Nút giao Đèn & FUSE (X0 - 26.0) -> Dao Incomer (X0 - 13.0) -> Mép bảng ĐẦU VÀO (X0)
            double pmX = X0 - 38.0;
            double nodeX = X0 - 26.0;
            double switchX = X0 - 13.0;
            double arrowX = X0 - 2.84;

            // 1. DÂY NGUỒN ĐẦU VÀO NẰM NGANG TẠI TÂM HÀNG MCCB (Y = yIncomer)
            // Đoạn 1: Từ Busbar đến Nút giao đèn (chạy ngang qua biến dòng PM tại pmX)
            DrawLine(btr, tr, new Point3d(busbarX, yIncomer, 0), new Point3d(nodeX, yIncomer, 0), LayerWire, 4);

            // Ký hiệu CT biến dòng & Hộp đồng hồ PM tại pmX:
            // Biến dòng CT ôm dây tại pmX:
            DrawCircle(btr, tr, new Point3d(pmX, yIncomer, 0), 0.75, LayerDevice, 3);
            // Dây tín hiệu đứng đi từ CT lên hộp đồng hồ PM tại tâm hàng ĐỒNG HỒ PM (yRow2):
            DrawLine(btr, tr, new Point3d(pmX, yIncomer, 0), new Point3d(pmX, yRow2 - 2.8, 0), LayerWire, 4);
            // Hộp đồng hồ PM (4.6 x 5.6) tại hàng ĐỒNG HỒ PM:
            DrawRectangle(btr, tr, new Point3d(pmX - 2.3, yRow2 - 2.8, 0), 4.6, 5.6, LayerDevice, 3);
            DrawMText(btr, tr, db, "PM", new Point3d(pmX, yRow2, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText);

            // Nút giao tròn tại (nodeX, yIncomer)
            DrawCircle(btr, tr, new Point3d(nodeX, yIncomer, 0), 0.53, LayerWire, 2);

            Point3d swPt = new Point3d(switchX, yIncomer, 0);
            SldInteractiveItem mainCustom = (p != null) ? p.FindCustomItem("MAIN_BREAKER") : null;
            double mainCadOffX = 0, mainCadOffY = 0, mainCadRot = 0, mainCadScale = 1.0;
            if (mainCustom != null && !mainCustom.IsDeleted)
            {
                mainCadOffX = mainCustom.OffsetX / 2.8;
                mainCadOffY = -mainCustom.OffsetY / 2.8;
                mainCadRot = mainCustom.RotationAngle * Math.PI / 180.0;
                mainCadScale = mainCustom.Scale;
            }
            Point3d actualSwPt = new Point3d(swPt.X + mainCadOffX, swPt.Y + mainCadOffY, 0);

            if (mainCustom == null || !mainCustom.IsDeleted)
            {
                AddOrthogonalWireH(btr, tr, new Point3d(nodeX, yIncomer, 0), new Point3d(actualSwPt.X - 6.25 * mainCadScale, actualSwPt.Y, 0), LayerWire, 4);
                DrawSouthBranchSwitch(btr, tr, actualSwPt, LayerDevice, flipped: true, scale: mainCadScale);
                AddOrthogonalWireH(btr, tr, new Point3d(actualSwPt.X + 6.25 * mainCadScale, actualSwPt.Y, 0), new Point3d(arrowX, yIncomer, 0), LayerWire, 4);
            }
            // Mũi tên đầu vào tại mép bảng
            DrawLine(btr, tr, new Point3d(X0, yIncomer - 1.4, 0), new Point3d(arrowX, yIncomer, 0), LayerWire, 4);
            DrawLine(btr, tr, new Point3d(X0, yIncomer + 1.4, 0), new Point3d(arrowX, yIncomer, 0), LayerWire, 4);

            // 2. NHÁNH ĐỨNG CẦU CHÌ FUSE VÀ 3 ĐÈN BÁO PHA R - Y - B (TỪ NÚT GIAO LÊN CÁC HÀNG)
            // Tọa độ Y chính xác của các đèn và cầu chì theo rule:
            double yFuseBottom = yIncomer + 5.63;
            double yFuseTop = yIncomer + 10.12;
            double yLampR = yIncomer + 17.01;
            double yLampY = yIncomer + 23.74;
            double yLampB = yIncomer + 30.20;

            // Dây từ Nút giao lên chân Cầu chì FUSE
            DrawLine(btr, tr, new Point3d(nodeX, yIncomer, 0), new Point3d(nodeX, yFuseBottom, 0), LayerWire, 4);

            // Hộp Cầu chì FUSE đứng (Rộng 1.35mm, Cao 4.49mm) tại hàng CT 50/5A
            DrawRectangle(btr, tr, new Point3d(nodeX - 0.675, yFuseBottom, 0), 1.35, 4.49, LayerDevice, 2);
            DrawLine(btr, tr, new Point3d(nodeX, yFuseBottom, 0), new Point3d(nodeX, yFuseTop, 0), LayerDevice, 2);
            // Chữ FUSE xoay dọc 90 độ đặt bên trái cầu chì
            DrawMText(btr, tr, db, "FUSE", new Point3d(nodeX - 3.4, (yFuseBottom + yFuseTop) / 2.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0);

            // Dây từ Cầu chì lên Đèn R
            DrawLine(btr, tr, new Point3d(nodeX, yFuseTop, 0), new Point3d(nodeX, yLampR - 2.14, 0), LayerWire, 4);

            // Đèn R (Màu đỏ - Color 1)
            DrawCircle(btr, tr, new Point3d(nodeX, yLampR, 0), 2.14, LayerDevice, 1);
            DrawLine(btr, tr, new Point3d(nodeX - 1.51, yLampR - 1.51, 0), new Point3d(nodeX + 1.51, yLampR + 1.51, 0), LayerDevice, 1);
            DrawLine(btr, tr, new Point3d(nodeX - 1.51, yLampR + 1.51, 0), new Point3d(nodeX + 1.51, yLampR - 1.51, 0), LayerDevice, 1);
            DrawMText(btr, tr, db, "R", new Point3d(nodeX - 3.7, yLampR, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0);

            // Dây từ Đèn R lên Đèn Y
            DrawLine(btr, tr, new Point3d(nodeX, yLampR + 2.14, 0), new Point3d(nodeX, yLampY - 2.14, 0), LayerWire, 4);

            // Đèn Y (Màu vàng - Color 2)
            DrawCircle(btr, tr, new Point3d(nodeX, yLampY, 0), 2.14, LayerDevice, 2);
            DrawLine(btr, tr, new Point3d(nodeX - 1.51, yLampY - 1.51, 0), new Point3d(nodeX + 1.51, yLampY + 1.51, 0), LayerDevice, 2);
            DrawLine(btr, tr, new Point3d(nodeX - 1.51, yLampY + 1.51, 0), new Point3d(nodeX + 1.51, yLampY - 1.51, 0), LayerDevice, 2);
            DrawMText(btr, tr, db, "Y", new Point3d(nodeX - 3.7, yLampY, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0);

            // Dây từ Đèn Y lên Đèn B
            DrawLine(btr, tr, new Point3d(nodeX, yLampY + 2.14, 0), new Point3d(nodeX, yLampB - 2.14, 0), LayerWire, 4);

            // Đèn B (Màu xanh dương - Color 3 hoặc 5)
            DrawCircle(btr, tr, new Point3d(nodeX, yLampB, 0), 2.14, LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(nodeX - 1.51, yLampB - 1.51, 0), new Point3d(nodeX + 1.51, yLampB + 1.51, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(nodeX - 1.51, yLampB + 1.51, 0), new Point3d(nodeX + 1.51, yLampB - 1.51, 0), LayerDevice, 3);
            DrawMText(btr, tr, db, "B", new Point3d(nodeX - 3.7, yLampB, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText, Math.PI / 2.0);
        }
        #endregion
        #endregion

        #region Bảng Table Tính Toán & Phân Bổ Phụ Tải (Schedule Table)
        private static void DrawCalculationScheduleTable(BlockTableRecord btr, Transaction tr, Database db, ElectricalPanel panel, Point3d tableOrigin, double availableWidth)
        {
            if (panel == null || panel.Loads == null || panel.Loads.Count == 0) return;

            int loadCount = panel.Loads.Count;
            double rowHeight = 7.0;
            double headerHeight = 9.0;
            double titleHeight = 11.0;

            // Độ rộng các cột
            double[] colWidths = new double[] { 14.0, 20.0, 75.0, 22.0, 16.0, 22.0, 36.0, 48.0, 32.0 };
            string[] headers = new string[] { "STT", "MÃ LỘ", "TÊN PHỤ TẢI", "Ptt (kW)", "PHA", "Itt (A)", "THIẾT BỊ ĐÓNG CẮT", "TIẾT DIỆN CÁP", "GHI CHÚ" };

            double tableWidth = 0;
            foreach (double w in colWidths) tableWidth += w;

            double curY = tableOrigin.Y;

            // 1. Tiêu đề Bảng
            DrawRectangle(btr, tr, new Point3d(tableOrigin.X, curY - titleHeight, 0), tableWidth, titleHeight, LayerFrame, 2);
            string titleText = string.Format("BẢNG TÍNH TOÁN & PHÂN BỔ PHỤ TẢI TỦ {0} (CHUẨN IEC / EVN SPC)", panel.PanelName.ToUpper());
            DrawMText(btr, tr, db, titleText, new Point3d(tableOrigin.X + tableWidth / 2.0, curY - titleHeight / 2.0, 0), 2.8, AttachmentPoint.MiddleCenter, LayerText);
            curY -= titleHeight;

            // 2. Hàng Tiêu Đề Cột (Header Row)
            DrawRectangle(btr, tr, new Point3d(tableOrigin.X, curY - headerHeight, 0), tableWidth, headerHeight, LayerFrame, 2);
            double curX = tableOrigin.X;
            for (int c = 0; c < headers.Length; c++)
            {
                DrawMText(btr, tr, db, headers[c], new Point3d(curX + colWidths[c] / 2.0, curY - headerHeight / 2.0, 0), 2.0, AttachmentPoint.MiddleCenter, LayerText);
                curX += colWidths[c];
                if (c < headers.Length - 1)
                {
                    DrawLine(btr, tr, new Point3d(curX, curY, 0), new Point3d(curX, curY - headerHeight, 0), LayerWire, 4);
                }
            }
            curY -= headerHeight;

            // 3. Các Hàng Dữ Liệu Phụ Tải
            for (int i = 0; i < loadCount; i++)
            {
                ElectricalLoad load = panel.Loads[i];
                DrawRectangle(btr, tr, new Point3d(tableOrigin.X, curY - rowHeight, 0), tableWidth, rowHeight, LayerWire, 4);

                curX = tableOrigin.X;
                string[] rowData = new string[]
                {
                    (i + 1).ToString(),
                    !string.IsNullOrEmpty(load.CircuitNumber) ? load.CircuitNumber : ("C" + (i + 1)),
                    !string.IsNullOrEmpty(load.LoadName) ? load.LoadName : "Phụ tải",
                    string.Format("{0:0.##}", load.PowerKW),
                    load.Phase == PhaseType.ThreePhase ? "3P" : (load.AssignedPhase ?? "1P"),
                    string.Format("{0:0.#}", load.CurrentA),
                    (!string.IsNullOrEmpty(load.BreakerSpec) ? load.BreakerSpec : (!string.IsNullOrEmpty(load.BreakerType) ? string.Format("{0} {1}A", load.BreakerType, Math.Max(16, Math.Ceiling(load.CurrentA * 1.25))) : (load.Phase == PhaseType.ThreePhase ? string.Format("MCB 3P {0:0.#}A", Math.Max(16, Math.Ceiling(load.CurrentA * 1.25))) : string.Format("MCB 1P {0:0.#}A", Math.Max(10, Math.Ceiling(load.CurrentA * 1.25)))))),
                    !string.IsNullOrEmpty(load.CableSpec) ? load.CableSpec : (load.Phase == PhaseType.ThreePhase ? "4x4+E4mm2" : "2x2.5+E2.5mm2"),
                    ""
                };

                for (int c = 0; c < rowData.Length; c++)
                {
                    AttachmentPoint ap = (c == 2) ? AttachmentPoint.MiddleLeft : AttachmentPoint.MiddleCenter;
                    double textX = (c == 2) ? (curX + 3.0) : (curX + colWidths[c] / 2.0);
                    DrawMText(btr, tr, db, rowData[c], new Point3d(textX, curY - rowHeight / 2.0, 0), 1.9, ap, LayerText);

                    curX += colWidths[c];
                    if (c < rowData.Length - 1)
                    {
                        DrawLine(btr, tr, new Point3d(curX, curY, 0), new Point3d(curX, curY - rowHeight, 0), LayerWire, 4);
                    }
                }
                curY -= rowHeight;
            }

            // 4. Hàng Tổng Kết (Summary Row)
            DrawRectangle(btr, tr, new Point3d(tableOrigin.X, curY - rowHeight, 0), tableWidth, rowHeight, LayerFrame, 2);
            DrawMText(btr, tr, db, "TỔNG CỘNG:", new Point3d(tableOrigin.X + colWidths[0] + colWidths[1] + colWidths[2] / 2.0, curY - rowHeight / 2.0, 0), 2.1, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0.##}", panel.TotalCalculatedPowerKW), new Point3d(tableOrigin.X + colWidths[0] + colWidths[1] + colWidths[2] + colWidths[3] / 2.0, curY - rowHeight / 2.0, 0), 2.1, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, "3P", new Point3d(tableOrigin.X + colWidths[0] + colWidths[1] + colWidths[2] + colWidths[3] + colWidths[4] / 2.0, curY - rowHeight / 2.0, 0), 2.1, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, string.Format("{0:0.#}", panel.TotalCurrentA), new Point3d(tableOrigin.X + colWidths[0] + colWidths[1] + colWidths[2] + colWidths[3] + colWidths[4] + colWidths[5] / 2.0, curY - rowHeight / 2.0, 0), 2.1, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, panel.Incomer.ToString(), new Point3d(tableOrigin.X + colWidths[0] + colWidths[1] + colWidths[2] + colWidths[3] + colWidths[4] + colWidths[5] + colWidths[6] / 2.0, curY - rowHeight / 2.0, 0), 2.1, AttachmentPoint.MiddleCenter, LayerText);
            DrawMText(btr, tr, db, panel.MainCableSpec ?? "", new Point3d(tableOrigin.X + colWidths[0] + colWidths[1] + colWidths[2] + colWidths[3] + colWidths[4] + colWidths[5] + colWidths[6] + 3.0, curY - rowHeight / 2.0, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
        }
        #endregion

        #region Sơ Đồ Nguyên Lý Tủ Động Lực MCC / PCCC (SDNL V2 - Bottom-to-Top)
        private static void DrawSldV2MccDiagram(Database db, Transaction tr, BlockTableRecord btr, ElectricalPanel panel, Point3d origin, DrawingParameters p)
        {
            int loadCount = (panel.Loads != null) ? panel.Loads.Count : 0;
            int branchCount = Math.Max(1, loadCount);
            if (p.IncludeSpare) branchCount += Math.Max(1, p.SpareCount3P + p.SpareCount1P);

            // 1. KÍCH THƯỚC VÀ THAM SỐ TỌA ĐỘ
            double branchPitch = 48.0;      // Bước giữa các nhánh xuất tuyến
            double leftMargin = 70.0;       // Lề trái
            double rightSpareW = 40.0;      // Phần dự phòng 20% và tiếp địa ở cuối busbar
            double busbarStartX = origin.X + leftMargin;
            double busbarEndX = busbarStartX + branchCount * branchPitch + rightSpareW;
            double busbarY = origin.Y + 115.0; // Cao độ Busbar ngang

            // 2. VẼ THANH CÁI BUSBAR CHÍNH (LAYER E-BUSBAR)
            DrawBusbar(btr, tr, new Point3d(busbarStartX - 5.0, busbarY, 0), new Point3d(busbarEndX, busbarY, 0), LayerBusbar);
            string busbarRatingStr = (panel.TotalCurrentA > 0)
                ? string.Format("4x CU BUSBAR {0:0}A", Math.Max(100, Math.Ceiling(panel.TotalCurrentA * 1.25 / 25.0) * 25.0))
                : "4x CU BUSBAR 125A";
            // RULE: Đưa tag busbar xuống dưới thanh cái để tránh chạm vào tag lộ nhánh (C01, C02...) ở trên
            DrawMText(btr, tr, db, busbarRatingStr, new Point3d(busbarStartX - 3.0, busbarY - 6.0, 0), 2.2, AttachmentPoint.TopLeft, LayerText);

            // 3. NGUỒN CẤP INCOMER TỪ DƯỚI LÊN (BOTTOM-TO-TOP)
            int incomerBranchIndex = Math.Min(3, Math.Max(1, branchCount / 2));
            double incomerX = busbarStartX + incomerBranchIndex * branchPitch;
            double mainRatingA = panel.TotalCurrentA > 0 ? Math.Ceiling(panel.TotalCurrentA * 1.25) : 200.0;

            double yTopLed = busbarY - 14.0;
            double yMct = busbarY - 28.0;
            double yVolt = busbarY - 45.0;
            double mainBreakerY = busbarY - 70.0;
            double yBottomLed = mainBreakerY - 20.0;
            double incomerBottomY = yBottomLed - 16.0;

            // Dây nguồn chính (vertical trunk)
            // Đoạn 1: từ đáy lên MCCB tổng
            DrawLine(btr, tr, new Point3d(incomerX, incomerBottomY, 0), new Point3d(incomerX, mainBreakerY - 8.0, 0), LayerWire, 4);
            // Đoạn 2: từ MCCB tổng lên Busbar
            DrawLine(btr, tr, new Point3d(incomerX, mainBreakerY + 8.0, 0), new Point3d(incomerX, busbarY, 0), LayerWire, 4);
            DrawCircle(btr, tr, new Point3d(incomerX, busbarY, 0), 1.2, LayerBusbar, 1);

            // Mũi tên và text nguồn cấp tại đáy
            DrawMccIncomerArrow(btr, tr, db, new Point3d(incomerX, incomerBottomY, 0));
            string parentPanelStr = !string.IsNullOrEmpty(panel.ParentPanelName) ? panel.ParentPanelName : "MSB";
            string incomerBottomText = string.Format("TỪ TỦ CHÍNH {0}\nFROM SWITCH BOARD {0}\nCÁP ( XEM BẢN VẼ HỆ THỐNG ĐIỆN )\nCABLE ( REFER DRAWING OF ELECTRICAL SYSTEM)", parentPanelStr);
            DrawMText(btr, tr, db, incomerBottomText, new Point3d(incomerX, incomerBottomY - 4.0, 0), 1.8, AttachmentPoint.TopCenter, LayerText);

            // CỤM 1: ĐÈN BÁO PHA R Y B + CẦU CHÌ 3x2A PHÍA DƯỚI MCCB TỔNG (SỬ DỤNG BLOCK 'ĐÈN BÁO PHA')
            BlockReference brLedBottom = InsertBlockReference(btr, tr, db, "ĐÈN BÁO PHA", new Point3d(incomerX, yBottomLed, 0), 1.0, 0.0, 0.0, false);
            if (brLedBottom == null)
            {
                DrawCircle(btr, tr, new Point3d(incomerX, yBottomLed, 0), 0.7, LayerWire, 4);
                DrawLine(btr, tr, new Point3d(incomerX, yBottomLed, 0), new Point3d(incomerX - 25.0, yBottomLed, 0), LayerWire, 4);
                InsertBlockReference(btr, tr, db, "ASP_EL_FUSE", new Point3d(incomerX - 9.0, yBottomLed, 0), 1.0, 0.0, 8.0);
                DrawMText(btr, tr, db, "3x2A", new Point3d(incomerX - 9.0, yBottomLed + 2.5, 0), 1.6, AttachmentPoint.BottomCenter, LayerDevice, 0.0, 3);
                InsertBlockReference(btr, tr, db, "ASP_EL_INDICATING LED LIGHTS", new Point3d(incomerX - 19.0, yBottomLed, 0), 1.0, 0.0, 12.0);
            }

            // CỤM 2: MCCB TỔNG 3P {Rating}A 36kA
            DrawMccBreakerSymbol(btr, tr, db, new Point3d(incomerX, mainBreakerY, 0), "MCCB", 3, mainRatingA, 36.0, 1.2);

            // CỤM 3: ĐO ÁP (VOLTAGE BRANCH - SỬ DỤNG BLOCK 'VOLT METTER')
            BlockReference brVolt = InsertBlockReference(btr, tr, db, "VOLT METTER", new Point3d(incomerX, yVolt, 0), 1.0, 0.0, 0.0, false);
            if (brVolt == null)
            {
                DrawCircle(btr, tr, new Point3d(incomerX, yVolt, 0), 0.7, LayerWire, 4);
                DrawLine(btr, tr, new Point3d(incomerX, yVolt, 0), new Point3d(incomerX - 30.0, yVolt, 0), LayerWire, 4);
                InsertBlockReference(btr, tr, db, "ASP_EL_FUSE", new Point3d(incomerX - 8.0, yVolt, 0), 1.0, 0.0, 7.5);
                DrawMText(btr, tr, db, "3x2A", new Point3d(incomerX - 8.0, yVolt + 2.5, 0), 1.6, AttachmentPoint.BottomCenter, LayerDevice, 0.0, 3);
                InsertBlockReference(btr, tr, db, "ASP_EL_VOLTAGE METER SELECTION SWITCH", new Point3d(incomerX - 16.5, yVolt, 0), 1.0, 0.0, 8.0);
                InsertBlockReference(btr, tr, db, "ASP_EL_VOLTMETER", new Point3d(incomerX - 25.0, yVolt, 0), 1.0, 0.0, 7.5);
                DrawMText(btr, tr, db, "500V", new Point3d(incomerX - 29.5, yVolt, 0), 1.8, AttachmentPoint.MiddleRight, LayerDevice, 0.0, 3);
            }

            // CỤM 4: ĐO DÒNG (CURRENT BRANCH - RẼ PHẢI TẠI yMct)
            BlockReference brCt = InsertBlockReference(btr, tr, db, "2_dwg_CT-4345304-MAIN SINGLE LINE DIAGRAM SYSTEM -02 DD", new Point3d(incomerX, yMct, 0), 1.0, 0.0, 5.0, true);
            if (brCt == null)
            {
                DrawMccCtSymbol(btr, tr, db, new Point3d(incomerX, yMct, 0));
            }
            DrawMText(btr, tr, db, string.Format("3xMCT\n{0:0}/5A", mainRatingA), new Point3d(incomerX - 2.5, yMct, 0), 1.7, AttachmentPoint.MiddleRight, LayerDevice, 0.0, 3);

            BlockReference brAm = InsertBlockReference(btr, tr, db, "AMPE METER", new Point3d(incomerX, yMct, 0), 1.0, 0.0, 0.0, false);
            if (brAm == null)
            {
                DrawLine(btr, tr, new Point3d(incomerX, yMct, 0), new Point3d(incomerX + 22.0, yMct, 0), LayerWire, 4);
                InsertBlockReference(btr, tr, db, "ASP_EL_AMPER METER SELECTOR SWITCH", new Point3d(incomerX + 10.0, yMct, 0), 1.0, 0.0, 8.0);
                InsertBlockReference(btr, tr, db, "ASP_EL_AMMETER", new Point3d(incomerX + 18.5, yMct, 0), 1.0, 0.0, 7.5);
            }

            // CỤM 5: ĐÈN BÁO PHA R Y B + CẦU CHÌ 3x2A PHÍA TRÊN (SỬ DỤNG BLOCK 'ĐÈN BÁO PHA')
            BlockReference brLedTop = InsertBlockReference(btr, tr, db, "ĐÈN BÁO PHA", new Point3d(incomerX, yTopLed, 0), 1.0, 0.0, 0.0, false);
            if (brLedTop == null)
            {
                DrawCircle(btr, tr, new Point3d(incomerX, yTopLed, 0), 0.7, LayerWire, 4);
                DrawLine(btr, tr, new Point3d(incomerX, yTopLed, 0), new Point3d(incomerX - 25.0, yTopLed, 0), LayerWire, 4);
                InsertBlockReference(btr, tr, db, "ASP_EL_FUSE", new Point3d(incomerX - 9.0, yTopLed, 0), 1.0, 0.0, 8.0);
                DrawMText(btr, tr, db, "3x2A", new Point3d(incomerX - 9.0, yTopLed + 2.5, 0), 1.6, AttachmentPoint.BottomCenter, LayerDevice, 0.0, 3);
                InsertBlockReference(btr, tr, db, "ASP_EL_INDICATING LED LIGHTS", new Point3d(incomerX - 19.0, yTopLed, 0), 1.0, 0.0, 12.0);
            }

            // 4. CÁC NHÁNH XUẤT TUYẾN ĐI LÊN TRÊN (FEEDERS TO TOP)
            List<double> sdStarterYList = new List<double>();
            List<double> sdBranchXList = new List<double>();
            double thermalSensorX = 0, thermalSensorY = 0;
            double cbY = busbarY + 28.0;
            double starterY = cbY + 30.0;
            double motorY = starterY + 40.0;

            for (int i = 0; i < loadCount; i++)
            {
                ElectricalLoad load = panel.Loads[i];
                double bX = busbarStartX + i * branchPitch + 15.0;
                string tag = !string.IsNullOrEmpty(load.CircuitNumber) ? load.CircuitNumber : ("P" + (i + 1));

                DrawCircle(btr, tr, new Point3d(bX, busbarY, 0), 0.9, LayerDevice, 3);
                DrawMText(btr, tr, db, tag, new Point3d(bX - 2.0, busbarY + 5.0, 0), 1.9, AttachmentPoint.BottomRight, LayerText);

                DrawLine(btr, tr, new Point3d(bX, busbarY, 0), new Point3d(bX, cbY - 7.0, 0), LayerWire, 4);

                int poles = (load.Phase == PhaseType.ThreePhase ? 3 : (load.AssignedPhase != null && load.AssignedPhase.Contains("2") ? 2 : 1));
                string brkType = (load.Phase == PhaseType.ThreePhase && load.PowerKW >= 5.5) ? "MCCB" : "MCB";
                double ratingA = Math.Max(16.0, Math.Ceiling(load.CurrentA * 1.25));
                double kA = (poles == 3) ? 25.0 : 10.0;

                string loadType = DetectMccLoadType(load);
                double textStartY = motorY + 15.0;

                DrawLine(btr, tr, new Point3d(bX, cbY + 7.0, 0), new Point3d(bX, starterY - 6.0, 0), LayerWire, 4);

                // RULE 1: Aptomat nhánh - Chèn Block 'aptomat' xoay 180 độ (Math.PI) theo đúng rule (tiếp điểm trên, mở sang phải, dấu X dưới)
                BlockReference brBranch = InsertBlockReference(btr, tr, db, "aptomat", new Point3d(bX, cbY, 0), 1.0, Math.PI, 14.0);
                if (brBranch == null)
                {
                    DrawMccBreakerSymbol(btr, tr, db, new Point3d(bX, cbY, 0), brkType, poles, ratingA, kA, 1.0);
                }
                else
                {
                    string brkSpec = string.Format("{0} {1}P {2:0}A", brkType, poles, ratingA);
                    DrawMText(btr, tr, db, brkSpec, new Point3d(bX + 6.0, cbY, 0), 1.6, AttachmentPoint.MiddleLeft, LayerText);
                }

                if (loadType == "STAR_DELTA")
                {
                    BlockReference brStarter = InsertBlockReference(btr, tr, db, "ASP_EL_STAR-DELTA MOTOR STARTING", new Point3d(bX, starterY, 0), 1.0, 0.0, 20.0);
                    if (brStarter == null)
                    {
                        DrawMccStarterBox(btr, tr, db, new Point3d(bX, starterY, 0), "S/D");
                    }
                    sdStarterYList.Add(starterY);
                    sdBranchXList.Add(bX);

                    DrawLine(btr, tr, new Point3d(bX - 1.5, starterY + 6.0, 0), new Point3d(bX - 1.5, motorY - 6.0, 0), LayerWire, 4);
                    DrawLine(btr, tr, new Point3d(bX + 1.5, starterY + 6.0, 0), new Point3d(bX + 1.5, motorY - 6.0, 0), LayerWire, 4);
                    DrawMccMotorSymbol(btr, tr, db, new Point3d(bX, motorY, 0), 6.0);
                }
                else if (loadType == "DOL")
                {
                    BlockReference brStarter = InsertBlockReference(btr, tr, db, "ASP_EL_DIRECT ONLINE MOTOR STARTING", new Point3d(bX, starterY, 0), 1.0, 0.0, 20.0);
                    if (brStarter == null)
                    {
                        DrawMccStarterBox(btr, tr, db, new Point3d(bX, starterY, 0), "DOL");
                    }
                    DrawLine(btr, tr, new Point3d(bX, starterY + 6.0, 0), new Point3d(bX, motorY - 6.0, 0), LayerWire, 4);
                    DrawMccMotorSymbol(btr, tr, db, new Point3d(bX, motorY, 0), 6.0);

                    if (load.LoadName != null && load.LoadName.IndexOf("EAF", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        thermalSensorX = bX;
                        thermalSensorY = motorY;
                    }
                }
                else if (loadType == "VFD")
                {
                    BlockReference brStarter = InsertBlockReference(btr, tr, db, "ASP_EL_VARIABLE SPEED DRIVE", new Point3d(bX, starterY, 0), 1.0, 0.0, 20.0);
                    if (brStarter == null)
                    {
                        brStarter = InsertBlockReference(btr, tr, db, "Xbase_Note & Legen (ELEC)$0$ASP_EL_VARIABLE SPEED DRIVE", new Point3d(bX, starterY, 0), 1.0, 0.0, 20.0);
                    }
                    if (brStarter == null)
                    {
                        DrawMccStarterBox(btr, tr, db, new Point3d(bX, starterY, 0), "VFD");
                    }
                    DrawLine(btr, tr, new Point3d(bX, starterY + 6.0, 0), new Point3d(bX, motorY - 6.0, 0), LayerWire, 4);
                    DrawMccMotorSymbol(btr, tr, db, new Point3d(bX, motorY, 0), 6.0);
                }
                else if (loadType == "DAMPER")
                {
                    DrawMccDamperSymbol(btr, tr, db, new Point3d(bX, starterY, 0));
                    DrawLine(btr, tr, new Point3d(bX, starterY + 6.0, 0), new Point3d(bX, motorY, 0), LayerWire, 4);
                }
                else if (loadType == "SENSOR")
                {
                    DrawMccSensorSymbol(btr, tr, db, new Point3d(bX, starterY, 0));
                    DrawLine(btr, tr, new Point3d(bX, starterY + 6.0, 0), new Point3d(bX, motorY, 0), LayerWire, 4);
                    DrawMText(btr, tr, db, "... x5", new Point3d(bX + 6.0, starterY, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);
                }
                else if (loadType == "CONTROL")
                {
                    DrawLine(btr, tr, new Point3d(bX, cbY + 7.0, 0), new Point3d(bX, motorY, 0), LayerWire, 4);
                    DrawRectangle(btr, tr, new Point3d(bX - 4.0, starterY - 3.0, 0), 8.0, 6.0, LayerDevice, 3);
                    DrawMText(btr, tr, db, "DK", new Point3d(bX, starterY, 0), 1.9, AttachmentPoint.MiddleCenter, LayerText);
                }
                else
                {
                    DrawLine(btr, tr, new Point3d(bX, cbY + 7.0, 0), new Point3d(bX, motorY, 0), LayerWire, 4);
                }

                // 5. TEXT THÔNG TIN CÁP & LỘ TRÌNH XOAY DỌC 90 ĐỘ
                DrawMccVerticalRotatedText(btr, tr, db, new Point3d(bX + 4.0, textStartY, 0), load, loadType);
            }

            // 6. CÁC ĐƯỜNG TÍN HIỆU LIÊN ĐỘNG NGANG (PCCC, CO, MIMIC, THERMAL)
            DrawMccInterlockSignals(btr, tr, db, busbarStartX, sdStarterYList, sdBranchXList, thermalSensorX, thermalSensorY);

            // 7. PHẦN DỰ PHÒNG KHÔNG GIAN VÀ TIẾP ĐỊA ĐUÔI BUSBAR
            double spareAreaX = busbarEndX - rightSpareW + 5.0;
            DrawLine(btr, tr, new Point3d(spareAreaX, busbarY, 0), new Point3d(spareAreaX, busbarY - 35.0, 0), LayerFrame, 2);
            DrawLine(btr, tr, new Point3d(spareAreaX, busbarY - 35.0, 0), new Point3d(busbarEndX, busbarY - 35.0, 0), LayerFrame, 2);
            DrawLine(btr, tr, new Point3d(busbarEndX, busbarY, 0), new Point3d(busbarEndX, busbarY - 35.0, 0), LayerFrame, 2);

            string spareTxt = "20% SPARE SPACE\nDỰ PHÒNG 20% KHÔNG GIAN TỦ ĐIỆN";
            DrawMText(btr, tr, db, spareTxt, new Point3d(spareAreaX + 2.0, busbarY - 18.0, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);

            Point3d pePt = new Point3d(busbarEndX + 5.0, busbarY - 35.0, 0);
            DrawLine(btr, tr, new Point3d(busbarEndX, busbarY - 35.0, 0), pePt, LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(pePt.X, pePt.Y - 5.0, 0), new Point3d(pePt.X, pePt.Y + 5.0, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(pePt.X + 2.0, pePt.Y - 3.2, 0), new Point3d(pePt.X + 2.0, pePt.Y + 3.2, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(pePt.X + 4.0, pePt.Y - 1.5, 0), new Point3d(pePt.X + 4.0, pePt.Y + 1.5, 0), LayerDevice, 3);
            DrawMText(btr, tr, db, "E", new Point3d(pePt.X + 7.0, pePt.Y, 0), 2.2, AttachmentPoint.MiddleLeft, LayerText);

            // 8. KHUNG VỎ TỦ MÀU VÀNG & KHUNG TÊN TỦ ĐẶT BÊN TRONG GÓC DƯỚI BÊN TRÁI (RULE 3)
            double xCabinetLeft = busbarStartX - 42.0;
            double xCabinetRight = busbarEndX + 16.0;
            double yCabinetBottom = yBottomLed - 8.0;
            double yCabinetTop = starterY + 16.0;

            // Khung vỏ tủ chính (Layer E-SCHEMATIC-FRAME, màu Vàng)
            DrawRectangle(btr, tr, new Point3d(xCabinetLeft, yCabinetBottom, 0), xCabinetRight - xCabinetLeft, yCabinetTop - yCabinetBottom, LayerFrame, 2);

            // Hộp tên tủ đóng khung kép đặt bên trong góc dưới bên trái
            double pBoxW = 46.0;
            double pBoxH = 14.0;
            double pBoxX = xCabinetLeft + 8.0;
            double pBoxY = yCabinetBottom + 18.0;

            DrawRectangle(btr, tr, new Point3d(pBoxX, pBoxY, 0), pBoxW, pBoxH, LayerFrame, 2);
            DrawRectangle(btr, tr, new Point3d(pBoxX + 1.2, pBoxY + 1.2, 0), pBoxW - 2.4, pBoxH - 2.4, LayerFrame, 2);

            string pName = !string.IsNullOrEmpty(panel.PanelName) ? panel.PanelName : "DB-HVAC-01";
            DrawMText(btr, tr, db, pName, new Point3d(pBoxX + pBoxW / 2.0, pBoxY + pBoxH / 2.0, 0), 3.2, AttachmentPoint.MiddleCenter, LayerText);

            // Vị trí đặt tủ đặt ngay bên dưới khung tên tủ
            string loc = "(ĐẶT TẠI PHÒNG QUẠT THẢI GIÓ - KHOANG 2 - TẦNG HẦM 2)\n(LOCATED AT EXHAUST AIR FAN ROOM - ZONE 2 - BASEMENT 2)";
            DrawMText(btr, tr, db, loc, new Point3d(pBoxX, pBoxY - 3.0, 0), 1.6, AttachmentPoint.TopLeft, LayerText);
        }

        private static string DetectMccLoadType(ElectricalLoad load)
        {
            if (load == null) return "NORMAL";
            string name = (load.LoadName ?? "").ToUpper();
            if (load.Starter == StarterType.StarDelta || name.Contains("S/D") || name.Contains("SAO") || (load.Phase == PhaseType.ThreePhase && load.PowerKW >= 15.0))
                return "STAR_DELTA";
            if (load.Starter == StarterType.DOL || load.Starter == StarterType.Direct || name.Contains("DOL") || name.Contains("QUẠT") || name.Contains("QUAT") || name.Contains("FAN") || name.Contains("BƠM") || name.Contains("PUMP"))
                return "DOL";
            if (load.Starter == StarterType.VFD || load.Starter == StarterType.SoftStarter || name.Contains("VFD") || name.Contains("BIẾN TẦN") || name.Contains("BIEN TAN"))
                return "VFD";
            if (name.Contains("VAN") || name.Contains("MFD") || name.Contains("DAMPER"))
                return "DAMPER";
            if (name.Contains("CẢM BIẾN") || name.Contains("CAM BIEN") || name.Contains("SENSOR") || name.Contains("CO"))
                return "SENSOR";
            if (name.Contains("ĐIỀU KHIỂN") || name.Contains("DIEU KHIEN") || name.Contains("CONTROL") || name.Contains("DK"))
                return "CONTROL";
            if (name.Contains("DỰ PHÒNG") || name.Contains("DU PHONG") || name.Contains("SPARE") || name.Contains("SPACE"))
                return "SPARE";
            return "NORMAL";
        }

        private static void DrawMccIncomerArrow(BlockTableRecord btr, Transaction tr, Database db, Point3d tip)
        {
            double arrSize = 3.5;
            Solid arrow = new Solid(
                tip,
                new Point3d(tip.X - arrSize * 0.5, tip.Y - arrSize, 0),
                new Point3d(tip.X + arrSize * 0.5, tip.Y - arrSize, 0)
            );
            arrow.Layer = LayerWire;
            arrow.ColorIndex = 4;
            btr.AppendEntity(arrow);
            tr.AddNewlyCreatedDBObject(arrow, true);
            _currentDrawnEntities.Add(arrow.ObjectId);
        }

        private static void DrawMccBreakerSymbol(BlockTableRecord btr, Transaction tr, Database db, Point3d center, string breakerType, int poles, double ratingA, double kA, double scale = 1.0)
        {
            double s = 4.0 * scale;
            // Dây trên xuống tiếp điểm trên
            DrawLine(btr, tr, new Point3d(center.X, center.Y + s * 1.5, 0), new Point3d(center.X, center.Y + s * 0.4, 0), LayerWire, 4);
            // Lưỡi dao mở từ tiếp điểm trên chếch sang TRÁI (để không đè vào text bên phải)
            DrawLine(btr, tr, new Point3d(center.X, center.Y + s * 0.4, 0), new Point3d(center.X - s * 0.9, center.Y - s * 0.8, 0), LayerDevice, 3);
            // Dấu chéo X ngắt bảo vệ ở phía dưới
            DrawLine(btr, tr, new Point3d(center.X - s * 0.25, center.Y - s * 0.6, 0), new Point3d(center.X + s * 0.25, center.Y - s * 0.1, 0), LayerDevice, 1);
            DrawLine(btr, tr, new Point3d(center.X - s * 0.25, center.Y - s * 0.1, 0), new Point3d(center.X + s * 0.25, center.Y - s * 0.6, 0), LayerDevice, 1);
            // Dây dưới nối lên
            DrawLine(btr, tr, new Point3d(center.X, center.Y - s * 0.6, 0), new Point3d(center.X, center.Y - s * 1.5, 0), LayerWire, 4);
            // Tiếp điểm tròn ở trên và dưới
            DrawCircle(btr, tr, new Point3d(center.X, center.Y + s * 0.4, 0), 0.5 * scale, LayerDevice, 3);
            DrawCircle(btr, tr, new Point3d(center.X, center.Y - s * 0.6, 0), 0.5 * scale, LayerDevice, 3);

            string txt = string.Format("{0}\n{1}P {2:0.#}A\n{3:0.#}kA", breakerType, poles, ratingA, kA);
            DrawMText(btr, tr, db, txt, new Point3d(center.X + 4.5, center.Y, 0), 1.9, AttachmentPoint.MiddleLeft, LayerText);
        }

        private static void DrawMccStarterBox(BlockTableRecord btr, Transaction tr, Database db, Point3d center, string starterType, double scale = 1.0)
        {
            double w = 12.0 * scale;
            double h = 9.0 * scale;
            DrawRectangle(btr, tr, new Point3d(center.X - w / 2.0, center.Y - h / 2.0, 0), w, h, LayerDevice, 3);
            DrawMText(btr, tr, db, starterType, center, 2.0 * scale, AttachmentPoint.MiddleCenter, LayerText);

            if (starterType == "S/D")
            {
                double swX = center.X + w / 2.0 + 3.0;
                DrawRectangle(btr, tr, new Point3d(swX, center.Y - 3.0, 0), 6.0, 6.0, LayerDevice, 3);
                DrawLine(btr, tr, new Point3d(swX, center.Y - 3.0, 0), new Point3d(swX + 6.0, center.Y + 3.0, 0), LayerDevice, 3);
                DrawMText(btr, tr, db, "x2", new Point3d(swX + 1.5, center.Y + 5.5, 0), 1.8, AttachmentPoint.BottomLeft, LayerText);
            }
        }

        private static void DrawMccMotorSymbol(BlockTableRecord btr, Transaction tr, Database db, Point3d center, double radius = 5.0)
        {
            DrawCircle(btr, tr, center, radius, LayerDevice, 3);
            DrawMText(btr, tr, db, "M", center, 3.2, AttachmentPoint.MiddleCenter, LayerText);
        }

                private static void DrawMccCtSymbol(BlockTableRecord btr, Transaction tr, Database db, Point3d center)
        {
            double w = 1.4;
            double h = 1.6;
            // Dấu ngoặc ôm trục dây thẳng đứng từ bên trái sang
            DrawLine(btr, tr, new Point3d(center.X - w, center.Y + h, 0), new Point3d(center.X + w, center.Y + h, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(center.X - w, center.Y + h, 0), new Point3d(center.X - w, center.Y - h, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(center.X - w, center.Y - h, 0), new Point3d(center.X + w, center.Y - h, 0), LayerDevice, 3);
        }

        private static void DrawMccDamperSymbol(BlockTableRecord btr, Transaction tr, Database db, Point3d center)
        {
            double s = 4.0;
            DrawLine(btr, tr, new Point3d(center.X - s, center.Y - s, 0), new Point3d(center.X + s, center.Y + s, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(center.X + s, center.Y - s, 0), new Point3d(center.X - s, center.Y + s, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(center.X - s, center.Y - s, 0), new Point3d(center.X - s, center.Y + s, 0), LayerDevice, 3);
            DrawLine(btr, tr, new Point3d(center.X + s, center.Y - s, 0), new Point3d(center.X + s, center.Y + s, 0), LayerDevice, 3);
            DrawMText(btr, tr, db, "VAN", new Point3d(center.X, center.Y - s - 2.0, 0), 1.8, AttachmentPoint.TopCenter, LayerText);
        }

        private static void DrawMccSensorSymbol(BlockTableRecord btr, Transaction tr, Database db, Point3d center)
        {
            DrawRectangle(btr, tr, new Point3d(center.X - 4.5, center.Y - 3.5, 0), 9.0, 7.0, LayerDevice, 3);
            DrawMText(btr, tr, db, "CO", center, 2.0, AttachmentPoint.MiddleCenter, LayerText);
        }

        private static void DrawMccVerticalRotatedText(BlockTableRecord btr, Transaction tr, Database db, Point3d pt, ElectricalLoad load, string loadType)
        {
            string cable = !string.IsNullOrEmpty(load.CableSpec)
                ? load.CableSpec
                : ((load.Phase == PhaseType.ThreePhase && load.PowerKW >= 15.0)
                    ? "4x3C x 25mm2 Cu/XLPE/LSZH + 1C-16.0mm2 Cu PVC(E)"
                    : ((load.Phase == PhaseType.ThreePhase)
                        ? "1x4C-6.0mm2 Cu/XLPE/PVC + 1C-6.0mm2 Cu PVC(E)"
                        : (loadType == "DAMPER"
                            ? "1x2C-2.5mm2 Cu/MICA/XLPE/LSZH + 1C-2.5mm2 Cu PVC(E)"
                            : "2x1C-2.5mm2 Cu XLPE/PVC + 1C-2.5mm2 Cu PVC(E)")));

            string routeVN = (cable.Contains("MICA") || cable.Contains("LSZH"))
                ? "ĐI TRONG MÁNG CÁP/ 2x ỐNG ĐIỆN GI\nIN CABLE TRAY/ 2XGI CONDUIT"
                : "ĐI TRONG MÁNG CÁP/ ỐNG ĐIỆN\nIN CABLE TRAY/ CONDUIT";

            string name = !string.IsNullOrEmpty(load.LoadName) ? load.LoadName : "PHỤ TẢI";
            string devVN = string.Format("{0} ({1:0.##} kW)", name, load.PowerKW);
            string devTypeVN = (loadType == "STAR_DELTA" || loadType == "DOL") ? "QUẠT THÔNG GIÓ" :
                               (loadType == "DAMPER") ? "VAN" :
                               (loadType == "SENSOR") ? "CẢM BIẾN CO" :
                               (loadType == "CONTROL") ? "MẠCH ĐIỀU KHIỂN\nCONTROL CIRCUITS" : "";

            string fullText = string.Format("{0}\n{1}\n{2}\n{3}", cable, routeVN, devVN, devTypeVN);
            DrawMText(btr, tr, db, fullText, pt, 2.0, AttachmentPoint.BottomLeft, LayerText, Math.PI / 2.0);
        }

        private static void DrawMccInterlockSignals(BlockTableRecord btr, Transaction tr, Database db, double leftX, List<double> sdStarterYList, List<double> sdBranchXList, double thermalX, double thermalY)
        {
            ObjectId dashedLtId = EnsureLinetype(db, tr, "DASHED");
            if (dashedLtId == ObjectId.Null) dashedLtId = EnsureLinetype(db, tr, "HIDDEN");

            double signalStartX = leftX - 60.0;
            double yFACP = (sdStarterYList.Count > 0) ? sdStarterYList[0] - 8.0 : 160.0;
            double yCO = yFACP - 6.0;
            double yMimic = yFACP + 6.0;

            string sigText = "- TỪ TỦ MIMIC PANEL / FROM MIMIC PANEL\n" +
                             "- TÍN HIỆU TỪ TRUNG TÂM BÁO CHÁY (FACP) / SIGNAL FROM FIRE ALARM CONTROL PANEL\n" +
                             "- TÍN HIỆU TỪ CẢM BIẾN CO (ĐẾN QUẠT THẢI GIÓ) / SIGNAL FROM CO SENSOR (TO EXHAUST AIR FAN)";
            DrawMText(btr, tr, db, sigText, new Point3d(signalStartX, yMimic + 10.0, 0), 1.8, AttachmentPoint.BottomLeft, LayerControl);

            if (sdBranchXList.Count > 0)
            {
                double maxSdX = sdBranchXList[sdBranchXList.Count - 1] + 12.0;

                Action<double> drawSignalH = (yLine) =>
                {
                    Line l = new Line(new Point3d(signalStartX, yLine, 0), new Point3d(maxSdX, yLine, 0));
                    l.Layer = LayerControl;
                    l.ColorIndex = 4;
                    if (dashedLtId != ObjectId.Null && dashedLtId != db.ContinuousLinetype)
                    {
                        l.LinetypeId = dashedLtId;
                        l.LinetypeScale = 1.0;
                    }
                    btr.AppendEntity(l);
                    tr.AddNewlyCreatedDBObject(l, true);
                    _currentDrawnEntities.Add(l.ObjectId);
                };

                drawSignalH(yMimic);
                drawSignalH(yFACP);
                drawSignalH(yCO);
            }

            if (thermalX > 0 && thermalY > 0)
            {
                double tLineY = thermalY + 15.0;
                Line lTh = new Line(new Point3d(thermalX, thermalY, 0), new Point3d(thermalX, tLineY, 0));
                lTh.Layer = LayerControl;
                lTh.ColorIndex = 4;
                btr.AppendEntity(lTh);
                tr.AddNewlyCreatedDBObject(lTh, true);
                _currentDrawnEntities.Add(lTh.ObjectId);

                DrawMText(btr, tr, db, "- CẢM BIẾN NHIỆT ĐỘ\n- THERMAL SENSOR", new Point3d(thermalX, tLineY + 2.0, 0), 1.8, AttachmentPoint.BottomCenter, LayerControl);
            }
        }

        #endregion
        #region Helpers Chèn Block & Ký Hiệu Vector Fallback
        private static BlockReference InsertBlockReference(BlockTableRecord btr, Transaction tr, Database db, string blockName, Point3d insertPt, double scale = 1.0, double rotationRad = 0.0, double maxDimension = 15.0, bool autoCenter = true)
        {
            try
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(blockName))
                {
                    string sldV2Dir = GetSldV2BlockDirectory();
                    string dwgPath = Path.Combine(sldV2Dir, blockName + ".dwg");
                    if (File.Exists(dwgPath))
                    {
                        ImportBlockFromDwg(db, blockName, dwgPath);
                    }
                }
                if (!bt.Has(blockName)) return null;

                ObjectId btrId = bt[blockName];
                BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                // Tự động Scale thu nhỏ và Căn giữa hình học Block (Auto-Scale & Auto-Center)
                double finalScale = scale;
                Point3d finalPt = insertPt;

                try
                {
                    Extents3d? ext = GetBlockRecordExtents(blockDef, tr);
                    if (ext.HasValue)
                    {
                        double w = Math.Abs(ext.Value.MaxPoint.X - ext.Value.MinPoint.X);
                        double h = Math.Abs(ext.Value.MaxPoint.Y - ext.Value.MinPoint.Y);
                        double maxDim = Math.Max(w, h);
                        if (maxDimension > 0 && maxDim > maxDimension && maxDim > 0.01)
                        {
                            finalScale = (maxDimension / maxDim) * scale;
                        }

                        if (autoCenter)
                        {
                            // Căn chỉnh để tâm hình học của block trùng đúng với insertPt (kể cả khi xoay rotationRad)
                            double geomCenterX = (ext.Value.MinPoint.X + ext.Value.MaxPoint.X) / 2.0;
                            double geomCenterY = (ext.Value.MinPoint.Y + ext.Value.MaxPoint.Y) / 2.0;
                            double rotCos = Math.Cos(rotationRad);
                            double rotSin = Math.Sin(rotationRad);
                            double rotCenterX = geomCenterX * rotCos - geomCenterY * rotSin;
                            double rotCenterY = geomCenterX * rotSin + geomCenterY * rotCos;
                            finalPt = new Point3d(
                                insertPt.X - rotCenterX * finalScale,
                                insertPt.Y - rotCenterY * finalScale,
                                insertPt.Z
                            );
                        }
                    }
                }
                catch { }

                BlockReference br = new BlockReference(finalPt, btrId);
                br.ScaleFactors = new Scale3d(finalScale, finalScale, finalScale);
                br.Rotation = rotationRad;
                br.Layer = LayerDevice;

                btr.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                _currentDrawnEntities.Add(br.ObjectId);
                return br;
            }
            catch
            {
                return null;
            }
        }

        private static Extents3d? GetBlockRecordExtents(BlockTableRecord btrRecord, Transaction tr)
        {
            Extents3d ext = new Extents3d();
            bool hasGeom = false;
            foreach (ObjectId id in btrRecord)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null && ent.Visible)
                {
                    try
                    {
                        if (!hasGeom)
                        {
                            ext = ent.GeometricExtents;
                            hasGeom = true;
                        }
                        else
                        {
                            ext.AddExtents(ent.GeometricExtents);
                        }
                    }
                    catch { }
                }
            }
            return hasGeom ? ext : (Extents3d?)null;
        }

        private static void DrawBreakerSymbolVector(BlockTableRecord btr, Transaction tr, Database db, Point3d center, bool isMain, int phaseCount, SymbolRegion region, bool isHorizontal, double scale = 1.0)
        {
            double size = (isMain ? 10.0 : 7.0) * scale;

            if (region == SymbolRegion.North_Tcvn)
            {
                // CHUẨN MIỀN BẮC (TCVN / EVN NPC): Hộp chữ nhật có đường gạch chéo
                double w = isHorizontal ? size * 1.4 : size;
                double h = isHorizontal ? size : size * 1.4;

                DrawRectangle(btr, tr, new Point3d(center.X - w / 2, center.Y - h / 2, 0), w, h, LayerDevice, 3);
                DrawLine(btr, tr, new Point3d(center.X - w / 2, center.Y - h / 2, 0), new Point3d(center.X + w / 2, center.Y + h / 2, 0), LayerDevice, 3);

                string label = isMain ? string.Format("Ap {0}P", phaseCount) : string.Format("MCB {0}P", phaseCount);
                Point3d txtPt = isHorizontal ? new Point3d(center.X, center.Y + h / 2 + 2.0, 0) : new Point3d(center.X + w / 2 + 2.0, center.Y, 0);
                DrawMText(btr, tr, db, label, txtPt, 1.8, AttachmentPoint.MiddleLeft, LayerText);
            }
            else
            {
                // CHUẨN MIỀN NAM (IEC / EVN SPC): Tiếp điểm hở kèm ký hiệu ngắt
                if (!isHorizontal)
                {
                    DrawLine(btr, tr, new Point3d(center.X, center.Y - size, 0), new Point3d(center.X, center.Y - size * 0.3, 0), LayerDevice, 3);
                    DrawLine(btr, tr, new Point3d(center.X, center.Y - size * 0.3, 0), new Point3d(center.X + size * 0.6, center.Y + size * 0.5, 0), LayerDevice, 3);
                    DrawLine(btr, tr, new Point3d(center.X + size * 0.5, center.Y + size * 0.5, 0), new Point3d(center.X, center.Y + size, 0), LayerDevice, 3);
                    DrawLine(btr, tr, new Point3d(center.X + size * 0.1, center.Y + size * 0.3, 0), new Point3d(center.X + size * 0.5, center.Y - size * 0.1, 0), LayerDevice, 1);

                    string label = isMain ? string.Format("MCCB {0}P\nI>", phaseCount) : string.Format("MCB {0}P\nI>", phaseCount);
                    DrawMText(btr, tr, db, label, new Point3d(center.X + size * 0.8, center.Y, 0), 1.8, AttachmentPoint.MiddleLeft, LayerText);
                }
                else
                {
                    DrawLine(btr, tr, new Point3d(center.X - size, center.Y, 0), new Point3d(center.X - size * 0.3, center.Y, 0), LayerDevice, 3);
                    DrawLine(btr, tr, new Point3d(center.X - size * 0.3, center.Y, 0), new Point3d(center.X + size * 0.5, center.Y + size * 0.6, 0), LayerDevice, 3);
                    DrawLine(btr, tr, new Point3d(center.X + size * 0.5, center.Y + size * 0.6, 0), new Point3d(center.X + size, center.Y, 0), LayerDevice, 3);

                    string label = isMain ? string.Format("MCCB {0}P (I>)", phaseCount) : string.Format("MCB {0}P (I>)", phaseCount);
                    DrawMText(btr, tr, db, label, new Point3d(center.X, center.Y + size * 0.8, 0), 1.8, AttachmentPoint.BottomCenter, LayerText);
                }
            }
        }
        #endregion

        #region Helpers Vẽ Hình Học Cơ Bản & Text Chuẩn Hóa
        private static void AddOrthogonalWire(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, short colorIndex = 4)
        {
            if (Math.Abs(p1.X - p2.X) < 0.2 || Math.Abs(p1.Y - p2.Y) < 0.2)
            {
                DrawLine(btr, tr, p1, p2, layer, colorIndex);
            }
            else
            {
                double midY = (p1.Y + p2.Y) / 2.0;
                Point3d pm1 = new Point3d(p1.X, midY, 0);
                Point3d pm2 = new Point3d(p2.X, midY, 0);
                DrawLine(btr, tr, p1, pm1, layer, colorIndex);
                DrawLine(btr, tr, pm1, pm2, layer, colorIndex);
                DrawLine(btr, tr, pm2, p2, layer, colorIndex);
            }
        }

        private static void AddOrthogonalWireH(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, short colorIndex = 4)
        {
            if (Math.Abs(p1.X - p2.X) < 0.2 || Math.Abs(p1.Y - p2.Y) < 0.2)
            {
                DrawLine(btr, tr, p1, p2, layer, colorIndex);
            }
            else
            {
                double midX = (p1.X + p2.X) / 2.0;
                Point3d pm1 = new Point3d(midX, p1.Y, 0);
                Point3d pm2 = new Point3d(midX, p2.Y, 0);
                DrawLine(btr, tr, p1, pm1, layer, colorIndex);
                DrawLine(btr, tr, pm1, pm2, layer, colorIndex);
                DrawLine(btr, tr, pm2, p2, layer, colorIndex);
            }
        }

        private static void DrawCopiedItemsInCad(BlockTableRecord btr, Transaction tr, Database db, Point3d origin, DrawingParameters p, bool isSouth)
        {
            if (p == null || p.SldCustomItems == null) return;
            foreach (var cItem in p.SldCustomItems)
            {
                if (cItem.IsCopied && !cItem.IsDeleted)
                {
                    double cOffX = cItem.OffsetX / 2.8;
                    double cOffY = -cItem.OffsetY / 2.8;
                    double cRot = cItem.RotationAngle * Math.PI / 180.0;
                    double cScale = cItem.Scale;
                    Point3d cPos = new Point3d(origin.X + 80.0 + cOffX, origin.Y + 150.0 + cOffY, 0);

                    if (isSouth)
                    {
                        BlockReference br = InsertBlockReference(btr, tr, db, "MCB-MCCB-CB", cPos, cScale, cRot);
                        if (br == null)
                        {
                            DrawBreakerSymbolVector(btr, tr, db, cPos, false, 3, SymbolRegion.South_Iec, false, cScale);
                        }
                    }
                    else
                    {
                        DrawBreakerSymbolVector(btr, tr, db, cPos, false, 3, SymbolRegion.North_Tcvn, false, cScale);
                    }
                    DrawMText(btr, tr, db, cItem.DisplayName, new Point3d(cPos.X + 10.0 * cScale, cPos.Y, 0), 1.8 * cScale, AttachmentPoint.MiddleLeft, LayerText);
                }
            }
        }

        private static void DrawBusbar(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer)
        {
            Polyline pl = new Polyline(2);
            pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 1.8, 1.8);
            pl.AddVertexAt(1, new Point2d(p2.X, p2.Y), 0, 1.8, 1.8);
            pl.Layer = layer;
            pl.ColorIndex = 1; // Màu Đỏ đồng
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            _currentDrawnEntities.Add(pl.ObjectId);
        }

        private static void DrawLine(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, string layer, short color)
        {
            Line l = new Line(p1, p2);
            l.Layer = layer;
            l.ColorIndex = color;
            btr.AppendEntity(l);
            tr.AddNewlyCreatedDBObject(l, true);
            _currentDrawnEntities.Add(l.ObjectId);
        }

        private static void DrawCircle(BlockTableRecord btr, Transaction tr, Point3d center, double radius, string layer, short color)
        {
            Circle c = new Circle(center, Vector3d.ZAxis, radius);
            c.Layer = layer;
            c.ColorIndex = color;
            btr.AppendEntity(c);
            tr.AddNewlyCreatedDBObject(c, true);
            _currentDrawnEntities.Add(c.ObjectId);
        }

        private static void DrawSouthBranchSwitch(BlockTableRecord btr, Transaction tr, Point3d center, string layer, bool flipped = false, double scale = 1.0)
        {
            double cx = center.X;
            double cy = center.Y;

            // 1. Vòng tròn tiếp điểm trái (R = 0.75) và đoạn dây đệm
            DrawCircle(btr, tr, new Point3d(cx - 3.0 * scale, cy, 0), 0.75 * scale, layer, 2);
            DrawLine(btr, tr, new Point3d(cx - 6.25 * scale, cy, 0), new Point3d(cx - 3.75 * scale, cy, 0), layer, 2);

            // 2. Vòng tròn tiếp điểm phải (R = 0.75) và đoạn dây đệm
            DrawCircle(btr, tr, new Point3d(cx + 3.0 * scale, cy, 0), 0.75 * scale, layer, 2);
            DrawLine(btr, tr, new Point3d(cx + 3.75 * scale, cy, 0), new Point3d(cx + 6.25 * scale, cy, 0), layer, 2);

            // 3. Lưỡi dao tiếp điểm hở (Polyline hình chữ nhật nghiêng góc ~25 độ)
            Polyline blade = new Polyline(4);
            if (!flipped)
            {
                // Mặc định nhánh phụ tải: chốt nối bên phải, mở chếch lên trên về bên trái
                blade.AddVertexAt(0, new Point2d(cx + 2.41 * scale, cy + 0.52 * scale), 0, 0, 0);
                blade.AddVertexAt(1, new Point2d(cx - 2.44 * scale, cy + 2.78 * scale), 0, 0, 0);
                blade.AddVertexAt(2, new Point2d(cx - 2.63 * scale, cy + 2.37 * scale), 0, 0, 0);
                blade.AddVertexAt(3, new Point2d(cx + 2.22 * scale, cy + 0.11 * scale), 0, 0, 0);
            }
            else
            {
                // Nguồn tổng Incomer: chốt nối bên trái, xoay ngược lại mở chếch lên trên về bên phải (hướng về ĐẦU VÀO)
                blade.AddVertexAt(0, new Point2d(cx - 2.41 * scale, cy + 0.52 * scale), 0, 0, 0);
                blade.AddVertexAt(1, new Point2d(cx + 2.44 * scale, cy + 2.78 * scale), 0, 0, 0);
                blade.AddVertexAt(2, new Point2d(cx + 2.63 * scale, cy + 2.37 * scale), 0, 0, 0);
                blade.AddVertexAt(3, new Point2d(cx - 2.22 * scale, cy + 0.11 * scale), 0, 0, 0);
            }
            blade.Closed = true;
            blade.Layer = layer;
            blade.ColorIndex = 2; // Màu vàng chuẩn bản vẽ mẫu
            btr.AppendEntity(blade);
            tr.AddNewlyCreatedDBObject(blade, true);
            _currentDrawnEntities.Add(blade.ObjectId);
        }

        private static void DrawRectangle(BlockTableRecord btr, Transaction tr, Point3d lowerLeft, double w, double h, string layer, short color)
        {
            Polyline pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(lowerLeft.X, lowerLeft.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(lowerLeft.X + w, lowerLeft.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(lowerLeft.X + w, lowerLeft.Y + h), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(lowerLeft.X, lowerLeft.Y + h), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            pl.ColorIndex = color;
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            _currentDrawnEntities.Add(pl.ObjectId);
        }

        private static void DrawMText(BlockTableRecord btr, Transaction tr, Database db, string text, Point3d location, double height, AttachmentPoint attach, string layer, double rotation = 0.0, short colorIndex = 7)
        {
            MText mt = new MText();
            mt.Contents = text;
            mt.Location = location;
            mt.TextHeight = height;
            mt.Attachment = attach;
            mt.Layer = layer;
            mt.ColorIndex = colorIndex;
            if (rotation != 0.0)
            {
                mt.Rotation = rotation;
            }

            // Gán TextStyle chuẩn Arial_2 nếu có
            try
            {
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has("Arial_2"))
                {
                    mt.TextStyleId = tst["Arial_2"];
                }
                else if (tst.Has("ARIAL"))
                {
                    mt.TextStyleId = tst["ARIAL"];
                }
            }
            catch { }

            btr.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
            _currentDrawnEntities.Add(mt.ObjectId);
        }

        private static void DrawSheetFrame(BlockTableRecord btr, Transaction tr, Database db, Point3d origin, double width, double height, string title, DrawingParameters p)
        {
            DrawRectangle(btr, tr, origin, width, height, LayerFrame, 2);
            DrawRectangle(btr, tr, new Point3d(origin.X + 5, origin.Y + 5, 0), width - 10, height - 10, LayerFrame, 2);

            string titleText = string.Format("SƠ ĐỒ NGUYÊN LÝ DẠNG CÂY - {0}\n[{1}]",
                title, p.SymbolRegion == SymbolRegion.North_Tcvn ? "CHUẨN MIỀN BẮC (TCVN / EVN NPC)" : "CHUẨN MIỀN NAM (IEC / EVN SPC)");
            DrawMText(btr, tr, db, titleText, new Point3d(origin.X + 15, origin.Y + height - 12, 0), 3.2, AttachmentPoint.TopLeft, LayerFrame);
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
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

        private static void EnsureTextStyle(Database db, Transaction tr, string styleName, string fontFileName, double height)
        {
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (!tst.Has(styleName))
            {
                tst.UpgradeOpen();
                TextStyleTableRecord tstr = new TextStyleTableRecord();
                tstr.Name = styleName;
                tstr.FileName = fontFileName;
                tstr.TextSize = height;
                tst.Add(tstr);
                tr.AddNewlyCreatedDBObject(tstr, true);
            }
        }
        #endregion
    }
}

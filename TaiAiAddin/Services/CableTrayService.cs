using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Gi = Autodesk.AutoCAD.GraphicsInterface;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
        public class CableTrayService
    {
        /// <summary>
        /// Thông số cấu hình đang kích hoạt từ giao diện Palette (dùng cho lệnh NTTTMC không cần hỏi lại)
        /// </summary>
        public static CableTrayParams ActiveParams = null;

        /// <summary>
        /// JIG VẼ TRỰC QUAN THỜI GIAN THỰC (REAL-TIME DYNAMIC DRAW JIG) CHO THANG MÁNG CÁP
        /// Hiển thị 2 cạnh biên máng cáp (cách tim W/2), đường tim, nan thang/gờ khay, giá treo và mũi tên hướng đi khi rê chuột
        /// </summary>
        public class CableTrayDrawJig : DrawJig
        {
            private Point3d _startWcs;
            private Point3d _curWcs;
            private CableTrayParams _params;
            private Matrix3d _ucsToWcs;
            private Matrix3d _wcsToUcs;

            public Point3d CurrentWcsPoint
            {
                get { return _curWcs; }
            }

            public CableTrayDrawJig(Point3d startWcs, CableTrayParams p, Matrix3d ucsToWcs)
            {
                _startWcs = startWcs;
                _curWcs = startWcs;
                _params = p;
                _ucsToWcs = ucsToWcs;
                _wcsToUcs = ucsToWcs.Inverse();
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions opts = new JigPromptPointOptions("\n[TMC LIVE JIG] Click chọn điểm tiếp theo trên tuyến (hoặc ENTER/ESC để hoàn thành): ");
                opts.UserInputControls = UserInputControls.Accept3dCoordinates |
                                         UserInputControls.NullResponseAccepted |
                                         UserInputControls.AnyBlankTerminatesInput;
                opts.BasePoint = _startWcs.TransformBy(_wcsToUcs);
                opts.UseBasePoint = true;
                opts.Cursor = CursorType.RubberBand;

                PromptPointResult res = prompts.AcquirePoint(opts);
                if (res.Status == PromptStatus.OK)
                {
                    Point3d newWcs = res.Value.TransformBy(_ucsToWcs);
                    if (newWcs.DistanceTo(_curWcs) < 0.5)
                    {
                        return SamplerStatus.NoChange;
                    }
                    _curWcs = newWcs;
                    return SamplerStatus.OK;
                }

                return SamplerStatus.Cancel;
            }

            protected override bool WorldDraw(Gi.WorldDraw draw)
            {
                Vector3d dir3d = _curWcs - _startWcs;
                Vector3d dir2d = new Vector3d(dir3d.X, dir3d.Y, 0);
                double dist = dir2d.Length;
                if (dist < 5.0) return true;

                Vector3d dir = dir2d.GetNormal();
                Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);
                double halfW = Math.Max(50.0, _params.WidthMm / 2.0);

                Point3d leftStart = _startWcs + normal * halfW;
                Point3d leftEnd = _curWcs + normal * halfW;
                Point3d rightStart = _startWcs - normal * halfW;
                Point3d rightEnd = _curWcs - normal * halfW;

                // Độ dày nét vẽ rõ nét chuẩn MEP
                draw.SubEntityTraits.LineWeight = LineWeight.LineWeight030;

                // 1. Vẽ 2 cạnh biên máng cáp màu cam MEP đặc trưng (ACI 30)
                draw.SubEntityTraits.Color = 30;
                draw.Geometry.WorldLine(leftStart, leftEnd);
                draw.Geometry.WorldLine(rightStart, rightEnd);

                // 2. Vẽ 2 nắp chặn/mặt cắt đầu mút
                draw.Geometry.WorldLine(leftStart, rightStart);
                draw.Geometry.WorldLine(leftEnd, rightEnd);

                // 3. Đường tim định tâm (Centerline) màu 8 (xám)
                draw.SubEntityTraits.Color = 8;
                draw.Geometry.WorldLine(_startWcs, _curWcs);

                // 4. Mũi tên chỉ hướng di chuyển tại đầu con trỏ chuột (Cyan)
                double arrowSize = Math.Min(halfW * 0.7, 120.0);
                if (dist > arrowSize * 1.5)
                {
                    Point3d arrowTip = _curWcs;
                    Point3d arrowLeft = arrowTip - dir * arrowSize + normal * (arrowSize * 0.4);
                    Point3d arrowRight = arrowTip - dir * arrowSize - normal * (arrowSize * 0.4);
                    draw.SubEntityTraits.Color = 4; // Cyan
                    draw.Geometry.WorldLine(arrowTip, arrowLeft);
                    draw.Geometry.WorldLine(arrowTip, arrowRight);
                }

                // 5. Bổ sung chi tiết theo chủng loại:
                if (_params.Type == CableTrayType.Ladder)
                {
                    // Thang Cáp (Ladder): Vẽ các nan/bậc thang cách nhau 250mm
                    draw.SubEntityTraits.Color = 30;
                    double rungStep = 250.0;
                    double curStep = rungStep;
                    while (curStep < dist - 50.0)
                    {
                        Point3d ptA = _startWcs + dir * curStep + normal * halfW;
                        Point3d ptB = _startWcs + dir * curStep - normal * halfW;
                        draw.Geometry.WorldLine(ptA, ptB);
                        curStep += rungStep;
                    }
                }
                else if (_params.Type == CableTrayType.Tray)
                {
                    // Khay Cáp (Perforated Tray): Vẽ 2 đường viền gờ trong
                    draw.SubEntityTraits.Color = 8;
                    double flangeW = Math.Min(30.0, halfW / 4.0);
                    draw.Geometry.WorldLine(_startWcs + normal * (halfW - flangeW), _curWcs + normal * (halfW - flangeW));
                    draw.Geometry.WorldLine(_startWcs - normal * (halfW - flangeW), _curWcs - normal * (halfW - flangeW));
                }

                // 6. Hiển thị mô phỏng vị trí Giá Đỡ Unistrut (nhô ra 100mm mỗi đầu)
                if (_params.InsertSupports && dist >= 300.0)
                {
                    draw.SubEntityTraits.Color = 8;
                    double supStep = Math.Max(1.0, _params.SupportSpacingM) * 1000.0;
                    double supPos = Math.Min(300.0, dist / 2.0);
                    double supHalfLen = halfW + 100.0;
                    while (supPos <= dist)
                    {
                        Point3d supCenter = _startWcs + dir * supPos;
                        Point3d supA = supCenter + normal * supHalfLen;
                        Point3d supB = supCenter - normal * supHalfLen;
                        draw.Geometry.WorldLine(supA, supB);

                        // Ti treo 2 đầu (Rods) màu đỏ
                        draw.SubEntityTraits.Color = 1;
                        Point3d rodA = supCenter + normal * (halfW + 50.0);
                        Point3d rodB = supCenter - normal * (halfW + 50.0);
                        draw.Geometry.WorldLine(rodA - dir * 20.0, rodA + dir * 20.0);
                        draw.Geometry.WorldLine(rodB - dir * 20.0, rodB + dir * 20.0);
                        draw.SubEntityTraits.Color = 8;

                        supPos += supStep;
                    }
                }

                // 7. Hiển thị nhãn kích thước & chiều dài động thời gian thực (Real-time Length Preview)
                if (dist > 350.0)
                {
                    draw.SubEntityTraits.Color = 7;
                    string typePrefix = _params.Type == CableTrayType.Ladder ? "THANG" : (_params.Type == CableTrayType.Tray ? "KHAY" : "MÁNG");
                    string textInfo = string.Format("{0} W{1:F0}xH{2:F0} (L={3:F2}m)", typePrefix, _params.WidthMm, _params.HeightMm, dist / 1000.0);
                    Point3d textPt = _startWcs + dir * (dist / 2.0) + normal * (halfW + 40.0);
                    Vector3d textDir = dir;
                    if (textDir.X < 0) textDir = -textDir; // Hướng chữ thuận chiều đọc
                    double textHeight = Math.Max(40.0, Math.Min(90.0, _params.WidthMm / 4.0));
                    try
                    {
                        draw.Geometry.Text(textPt, new Vector3d(0, 0, 1), textDir, textHeight, 1.0, 0.0, textInfo);
                    }
                    catch { }
                }

                return true;
            }
        }

        public const string LayerElecTmc = "MEP_ELEC_TMC";
        public const string LayerElecTmcSupport = "MEP_ELEC_TMC_GIA_DO";
        public const string LayerElecTmcText = "MEP_ELEC_TMC_TEXT";
        public const string LayerElecTmcCenterline = "MEP_ELEC_TMC_TAM";

        public static string GetPrimaryLibraryPath()
        {
            string projectRoot = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS";
            string[] candidates = new string[]
            {
                Path.Combine(projectRoot, @"Thư Viện\Điện\Thang máng"),
                Path.Combine(projectRoot, @"thư viện\Điện\Thang máng"),
                Path.Combine(projectRoot, @"Thư Viện\Điện"),
                Path.Combine(projectRoot, @"thư viện\Điện")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i])) return candidates[i];
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] appDataCandidates = new string[]
            {
                Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\Điện\Thang máng"),
                Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\thư viện\Điện\Thang máng"),
                Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\Điện"),
                Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\thư viện\Điện")
            };
            for (int i = 0; i < appDataCandidates.Length; i++)
            {
                if (Directory.Exists(appDataCandidates[i])) return appDataCandidates[i];
            }

            return candidates[0];
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
                if (ed != null) ed.WriteMessage(string.Format("\n[NTT Error] Không thể mở thư mục Thư Viện: {0}", ex.Message));
            }
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

        public static ObjectId EnsureBlockImported(Database targetDb, string blockName, string dwgFileName, bool forceReload = false)
        {
            if (targetDb == null || string.IsNullOrEmpty(blockName)) return ObjectId.Null;

            if (!forceReload)
            {
                using (Transaction tr = targetDb.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                    if (bt.Has(blockName)) return bt[blockName];
                    tr.Commit();
                }
            }

            string libraryDir = GetPrimaryLibraryPath();
            string filePath = Path.Combine(libraryDir, dwgFileName);
            if (!File.Exists(filePath))
            {
                string alt1 = Path.Combine(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\thư viện\Điện", dwgFileName);
                if (File.Exists(alt1)) filePath = alt1;
                else
                {
                    string alt2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Thu Viện\Điện\Thang máng", dwgFileName);
                    if (File.Exists(alt2)) filePath = alt2;
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

            // Fallback: Tạo Block chuẩn nếu không tìm thấy file dwg
            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName)) return bt[blockName];

                bt.UpgradeOpen();
                BlockTableRecord btr = new BlockTableRecord();
                btr.Name = blockName;

                if (blockName.Contains("GIADO"))
                {
                    // Fallback Giá đỡ Unistrut chuẩn 300mm x 50mm
                    Polyline pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(-150, 25), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(150, 25), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(150, -25), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(-150, -25), 0, 0, 0);
                    pl.Closed = true;
                    pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 8);
                    btr.AppendEntity(pl);

                    Circle c1 = new Circle(new Point3d(-125, 0, 0), Vector3d.ZAxis, 6.0);
                    c1.Color = Color.FromColorIndex(ColorMethod.ByAci, 7);
                    Circle c2 = new Circle(new Point3d(125, 0, 0), Vector3d.ZAxis, 6.0);
                    c2.Color = Color.FromColorIndex(ColorMethod.ByAci, 7);
                    btr.AppendEntity(c1);
                    btr.AppendEntity(c2);
                }
                else if (blockName.Contains("Elbow_45"))
                {
                    // Fallback Co 45 độ (Base 300mm x 300mm)
                    Polyline pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(212.1, 150), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(212.1, -150), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(-62.1, -150), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(-256.0, 43.9), 0, 0, 0);
                    pl.AddVertexAt(4, new Point2d(-43.9, 256.0), 0, 0, 0);
                    pl.AddVertexAt(5, new Point2d(62.1, 150), 0, 0, 0);
                    pl.Closed = true;
                    pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                    btr.AppendEntity(pl);
                }
                else if (blockName.Contains("Elbow"))
                {
                    // Fallback Co vuông 90 độ (Base 200mm, port offset 250mm)
                    Polyline pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(-100, 250), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(100, 250), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(100, 150), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(150, 100), 0, 0, 0);
                    pl.AddVertexAt(4, new Point2d(250, 100), 0, 0, 0);
                    pl.AddVertexAt(5, new Point2d(250, -100), 0, 0, 0);
                    pl.AddVertexAt(6, new Point2d(-100, -100), 0, 0, 0);
                    pl.Closed = true;
                    pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                    btr.AppendEntity(pl);
                }
                else if (blockName.Contains("Ladder"))
                {
                    // Fallback Thang cáp (Base 300mm x 300mm)
                    Line railL = new Line(new Point3d(0, 150, 0), new Point3d(300, 150, 0));
                    Line railR = new Line(new Point3d(0, -150, 0), new Point3d(300, -150, 0));
                    Line rung = new Line(new Point3d(150, 130, 0), new Point3d(150, -130, 0));
                    railL.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                    railR.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                    rung.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                    btr.AppendEntity(railL);
                    btr.AppendEntity(railR);
                    btr.AppendEntity(rung);
                }
                else
                {
                    // Fallback Thân máng cáp chuẩn dài 500mm x rộng 300mm
                    Polyline pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(0, 150), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(500, 150), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(500, -150), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(0, -150), 0, 0, 0);
                    pl.Closed = true;
                    pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                    btr.AppendEntity(pl);
                }

                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);
                tr.Commit();
                return btrId;
            }
        }

        /// <summary>
        /// Tạo hoặc lấy Block Khay Cáp Đột Lỗ Thông Khí (Perforated Cable Tray)
        /// Base kích thước: Dài 1000mm, Rộng 300mm (từ Y=-150 đến Y=+150), các lỗ đột 60x12mm cách nhau 180mm
        /// </summary>
        public static ObjectId EnsurePerforatedTrayBlock(Database targetDb)
        {
            if (targetDb == null) return ObjectId.Null;
            string blockName = "HTS_Cable_Tray_Perforated";

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(blockName)) return bt[blockName];

                bt.UpgradeOpen();
                BlockTableRecord btr = new BlockTableRecord();
                btr.Name = blockName;

                // 1. Hai đường viền ngoài
                Line lTop = new Line(new Point3d(0, 150, 0), new Point3d(1000, 150, 0));
                lTop.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                Line lBot = new Line(new Point3d(0, -150, 0), new Point3d(1000, -150, 0));
                lBot.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                btr.AppendEntity(lTop);
                btr.AppendEntity(lBot);

                // 2. Hai gờ nẹp bích thành khay 15mm
                Line fTop = new Line(new Point3d(0, 135, 0), new Point3d(1000, 135, 0));
                fTop.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                Line fBot = new Line(new Point3d(0, -135, 0), new Point3d(1000, -135, 0));
                fBot.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                btr.AppendEntity(fTop);
                btr.AppendEntity(fBot);

                // 3. Các hàng lỗ đột thông khí (Slots 60x12mm) chia đều mỗi 180mm dọc khay
                for (double x = 120.0; x <= 900.0; x += 180.0)
                {
                    double[] rowYs = new double[] { 65.0, -65.0 };
                    for (int r = 0; r < rowYs.Length; r++)
                    {
                        double y = rowYs[r];
                        Polyline slotPl = new Polyline();
                        slotPl.AddVertexAt(0, new Point2d(x - 30.0, y - 6.0), 0, 0, 0);
                        slotPl.AddVertexAt(1, new Point2d(x + 30.0, y - 6.0), 0, 0, 0);
                        slotPl.AddVertexAt(2, new Point2d(x + 30.0, y + 6.0), 0, 0, 0);
                        slotPl.AddVertexAt(3, new Point2d(x - 30.0, y + 6.0), 0, 0, 0);
                        slotPl.Closed = true;
                        slotPl.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                        btr.AppendEntity(slotPl);
                    }
                }

                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);
                tr.Commit();
                return btrId;
            }
        }

        /// <summary>
        /// Gán và đồng bộ các thuộc tính AttributeReference (Tag: CABLETRAY, LADDER, WXH)
        /// theo đúng kích thước WxH đã chọn/nhập từ bảng thông số
        /// </summary>
        public static void AttachAttributesToBlockReference(BlockReference bref, ObjectId blockDefId, Transaction tr, CableTrayParams p, double lengthMm)
        {
            if (bref == null || blockDefId.IsNull || tr == null || p == null) return;

            BlockTableRecord btrDef = tr.GetObject(blockDefId, OpenMode.ForRead) as BlockTableRecord;
            if (btrDef == null) return;

            string attrValue = string.Format("W{0:F0}xH{1:F0}", p.WidthMm, p.HeightMm);

            foreach (ObjectId id in btrDef)
            {
                AttributeDefinition attDef = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (attDef != null && !attDef.Constant)
                {
                    AttributeReference attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, bref.BlockTransform);

                    string tag = (attDef.Tag ?? "").ToUpperInvariant();
                    if (tag == "CABLETRAY" || tag == "LADDER" || tag == "WXH" || tag == "SIZE" || tag == "DIMENSION")
                    {
                        attRef.TextString = attrValue;
                    }
                    else if (tag == "LENGTH" || tag == "L" || tag == "CHIEUDAI")
                    {
                        attRef.TextString = string.Format("{0:F0}", lengthMm);
                    }
                    else
                    {
                        attRef.TextString = attrValue;
                    }

                    bref.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }

        /// <summary>
        /// Gán thuộc tính Dynamic Block nếu có; nếu không có thuộc tính động thì co dãn ScaleFactors chính xác
        /// </summary>
        public static void ApplyDynamicBlockProperties(BlockReference bref, double lengthMm, double widthMm, double baseLength, double baseWidth)
        {
            if (bref == null) return;
            bool hasLengthProp = false;
            bool hasWidthProp = false;

            if (bref.IsDynamicBlock)
            {
                foreach (DynamicBlockReferenceProperty prop in bref.DynamicBlockReferencePropertyCollection)
                {
                    string pName = prop.PropertyName;
                    if (pName.Equals("Distance1", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("Length", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("Distance", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("ChieuDai", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("D1", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("LengthMm", StringComparison.OrdinalIgnoreCase))
                    {
                        try { prop.Value = lengthMm; hasLengthProp = true; } catch { }
                    }

                    if (pName.Equals("Width", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("ChieuRong", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("W", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("Distance2", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("WidthMm", StringComparison.OrdinalIgnoreCase))
                    {
                        try { prop.Value = widthMm; hasWidthProp = true; } catch { }
                    }
                }
            }

            double scaleX = hasLengthProp ? 1.0 : (lengthMm / Math.Max(1.0, baseLength));
            double scaleY = hasWidthProp ? 1.0 : (widthMm / Math.Max(1.0, baseWidth));
            bref.ScaleFactors = new Scale3d(scaleX, scaleY, 1.0);
        }

        /// <summary>
        /// Chèn một đoạn Thang/Máng Cáp dưới dạng BlockReference (Dynamic Block Reference)
        /// Kèm rải Block giá treo HTS-GIADO vuông góc nhô ra 100mm mỗi đầu và nét tim định tâm
        /// </summary>
        public static void DrawSingleSegmentBlock(BlockTableRecord btr, Transaction tr,
            Point3d startPt, Point3d endPt, CableTrayParams p,
            ObjectId segmentBlockId, double baseLength, double baseWidth,
            ObjectId supportBlockId, ref int supportCount,
            List<ObjectId> outCreatedIds = null)
        {
            double dist = startPt.DistanceTo(endPt);
            if (dist < 5.0) return;

            Vector3d dir = (endPt - startPt).GetNormal();
            Vector3d normal = new Vector3d(-dir.Y, dir.X, 0);
            double rot = Math.Atan2(dir.Y, dir.X);
            double halfW = Math.Max(50.0, p.WidthMm / 2.0);

            // 1. CHÈN BLOCK THÂN MÁNG/THANG/KHAY (BLOCK REFERENCE CHÍNH)
            if (!segmentBlockId.IsNull)
            {
                BlockReference bref = new BlockReference(startPt, segmentBlockId);
                bref.Layer = LayerElecTmc;
                bref.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                bref.Rotation = rot;

                ApplyDynamicBlockProperties(bref, dist, p.WidthMm, baseLength, baseWidth);

                btr.AppendEntity(bref);
                tr.AddNewlyCreatedDBObject(bref, true);
                if (outCreatedIds != null) outCreatedIds.Add(bref.ObjectId);

                AttachAttributesToBlockReference(bref, segmentBlockId, tr, p, dist);
            }

            // 2. VẼ ĐƯỜNG TIM ĐỊNH TÂM (CENTERLINE)
            Line cLine = new Line(startPt, endPt);
            cLine.Layer = LayerElecTmcCenterline;
            cLine.Color = Color.FromColorIndex(ColorMethod.ByAci, 8);
            btr.AppendEntity(cLine);
            tr.AddNewlyCreatedDBObject(cLine, true);
            if (outCreatedIds != null) outCreatedIds.Add(cLine.ObjectId);

            // 3. RẢI GIÁ ĐỠ TREO TRẦN UNISTRUT (HTS-GIADO.dwg) VUÔNG GÓC VÀ NHÔ RA MỖI BÊN 100MM
            if (p.InsertSupports && !supportBlockId.IsNull && dist >= 250.0)
            {
                // Base length của HTS-GIADO.dwg là 300mm.
                // Chiều dài giá đỡ = W + 100mm + 100mm = W + 200mm.
                double supportLength = p.WidthMm + 200.0;
                double scaleSupX = supportLength / 300.0;
                double rotSup = rot + Math.PI / 2.0; // Vuông góc 90 độ với tim máng

                double supportStepMm = Math.Max(1.0, p.SupportSpacingM) * 1000.0;
                double supPos = Math.Min(300.0, dist / 2.0);

                while (supPos <= dist)
                {
                    Point3d supPt = startPt + dir * supPos;
                    BlockReference brefSup = new BlockReference(supPt, supportBlockId);
                    brefSup.Layer = LayerElecTmcSupport;
                    brefSup.Color = Color.FromColorIndex(ColorMethod.ByAci, 8);
                    brefSup.Rotation = rotSup;
                    brefSup.ScaleFactors = new Scale3d(scaleSupX, 1.0, 1.0);

                    btr.AppendEntity(brefSup);
                    tr.AddNewlyCreatedDBObject(brefSup, true);
                    if (outCreatedIds != null) outCreatedIds.Add(brefSup.ObjectId);

                    AttachAttributesToBlockReference(brefSup, supportBlockId, tr, p, supportLength);
                    supportCount++;

                    supPos += supportStepMm;
                }
            }

            // 4. GHI CHÚ MTEXT KÍCH THƯỚC WxH
            if (p.AddAnnotations && dist > 400.0)
            {
                Point3d midPt = startPt + dir * (dist / 2.0) + normal * (halfW + 35.0);
                MText mtext = new MText();
                mtext.Location = midPt;
                mtext.TextHeight = Math.Max(35.0, Math.Min(80.0, dist / 25.0));
                mtext.Layer = LayerElecTmcText;
                mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, 7);
                mtext.Attachment = AttachmentPoint.MiddleCenter;

                double angle = rot;
                if (angle > Math.PI / 2.0) angle -= Math.PI;
                else if (angle < -Math.PI / 2.0) angle += Math.PI;
                mtext.Rotation = angle;

                string typePrefix = p.Type == CableTrayType.Ladder ? "THANG CÁP" : (p.Type == CableTrayType.Tray ? "KHAY CÁP" : "MÁNG CÁP");
                mtext.Contents = string.Format("{0} {1:F0}x{2:F0} (L={3:F2}m)", typePrefix, p.WidthMm, p.HeightMm, dist / 1000.0);
                btr.AppendEntity(mtext);
                tr.AddNewlyCreatedDBObject(mtext, true);
                if (outCreatedIds != null) outCreatedIds.Add(mtext.ObjectId);
            }
        }



        /// <summary>
        /// ĐỘNG CƠ ĐỊNH TUYẾN THANG MÁNG CÁP THEO DANH SÁCH TỌA ĐỘ TIM (CENTERLINE PATH)
        /// Tự động chèn Co 90°, Co 45° chuẩn kích thước (W=300 ra đúng phụ kiện 300mm),
        /// chèn Block Thân Máng Động và rải Giá Đỡ Unistrut 1.5m
        /// </summary>
        public static CableTrayRouteResult RouteCableTrayAlongPoints(Document doc, List<Point3d> pts, CableTrayParams p)
        {
            return RouteCableTrayAlongPointsInternal(doc, pts, p, null, null, true);
        }

        /// <summary>
        /// ĐỘNG CƠ ĐỊNH TUYẾN NỘI BỘ:
        /// Hỗ trợ xóa các đối tượng tạm từ bước pick trước và xuất danh sách các ObjectId vừa tạo để hiển thị ngay lập tức.
        /// </summary>
        public static CableTrayRouteResult RouteCableTrayAlongPointsInternal(
            Document doc,
            List<Point3d> pts,
            CableTrayParams p,
            List<ObjectId> oldEntitiesToErase = null,
            List<ObjectId> outCreatedIds = null,
            bool writeMessage = true)
        {
            if (doc == null || pts == null || pts.Count < 2 || p == null) return null;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                bool isLadder = (p.Type == CableTrayType.Ladder);
                bool isTray = (p.Type == CableTrayType.Tray);

                // 1. TÌM BLOCK THÂN MÁNG
                string segmentBlockName;
                ObjectId segmentBlockId;
                double segBaseLength;
                double segBaseWidth = 300.0;

                if (isLadder)
                {
                    // Thang cáp sử dụng block: HTS Ladder.dwg
                    segmentBlockName = "HTS_Ladder";
                    segmentBlockId = EnsureBlockImported(db, segmentBlockName, "HTS Ladder.dwg", true);
                    segBaseLength = 300.0;
                }
                else
                {
                    // Cả Khay Cáp (Tray) và Máng Cáp (Trunking) đều sử dụng block: HTS Cable tray.dwg
                    segmentBlockName = "HTS_Cable_Tray";
                    segmentBlockId = EnsureBlockImported(db, segmentBlockName, "HTS Cable tray.dwg", true);
                    segBaseLength = 500.0;
                }

                // 2. TÌM BLOCK CO 90° VÀ CO 45°
                string elbow90File = isLadder ? "HTS Elbow ladder.dwg" : "HTS Elbow cable tray.dwg";
                string elbow90Name = isLadder ? "HTS_Elbow_Ladder" : "HTS_Elbow_Cable_Tray";

                string elbow45File = isLadder ? "HTS Elbow 45 ladder.dwg" : "HTS Elbow 45 cable tray.dwg";
                string elbow45Name = isLadder ? "HTS_Elbow_45_Ladder" : "HTS_Elbow_45_Cable_Tray";

                ObjectId elbow90BlockId = EnsureBlockImported(db, elbow90Name, elbow90File);
                ObjectId elbow45BlockId = EnsureBlockImported(db, elbow45Name, elbow45File);
                ObjectId supportBlockId = p.InsertSupports ? EnsureBlockImported(db, "HTS_GIADO", "HTS-GIADO.dwg") : ObjectId.Null;

                double scale90 = isLadder ? (p.WidthMm / 300.0) : (p.WidthMm / 200.0);
                double fitDist90 = isLadder ? (1.0 * p.WidthMm) : (1.25 * p.WidthMm);

                double scale45 = isLadder ? (p.WidthMm / 100.0) : (p.WidthMm / 300.0);
                double fitDist45 = isLadder ? (0.4071 * p.WidthMm) : (0.707 * p.WidthMm);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Xóa các thực thể tạm thời từ lần pick trước (nếu có)
                    if (oldEntitiesToErase != null && oldEntitiesToErase.Count > 0)
                    {
                        for (int k = 0; k < oldEntitiesToErase.Count; k++)
                        {
                            ObjectId id = oldEntitiesToErase[k];
                            if (id.IsValid && !id.IsErased)
                            {
                                DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                                obj.Erase(true);
                            }
                        }
                    }

                    EnsureLayer(db, tr, LayerElecTmc, 30);
                    EnsureLayer(db, tr, LayerElecTmcSupport, 8);
                    EnsureLayer(db, tr, LayerElecTmcCenterline, 8);
                    EnsureLayer(db, tr, LayerElecTmcText, 7);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double halfW = Math.Max(50.0, p.WidthMm / 2.0);
                    int numPts = pts.Count;
                    double[] cutBefore = new double[numPts];
                    double[] cutAfter = new double[numPts];

                    int elbow90Count = 0;
                    int elbow45Count = 0;
                    int supportCount = 0;
                    double totalLengthMm = 0;

                    // BƯỚC 1: TÍNH TOÁN KHOẢNG LÙI TẠI CÁC GÓC RẼ
                    for (int i = 1; i < numPts - 1; i++)
                    {
                        Point3d pPrev = pts[i - 1];
                        Point3d pCur = pts[i];
                        Point3d pNext = pts[i + 1];

                        Vector3d v1 = (pCur - pPrev).GetNormal();
                        Vector3d v2 = (pNext - pCur).GetNormal();

                        double cosA = v1.DotProduct(v2);
                        double crossZ = v1.X * v2.Y - v1.Y * v2.X;
                        double defAngleRad = Math.Atan2(crossZ, cosA);
                        double defAngleDeg = Math.Abs(defAngleRad * (180.0 / Math.PI));

                        if (defAngleDeg >= 65.0 && defAngleDeg <= 115.0)
                        {
                            cutBefore[i] = fitDist90;
                            cutAfter[i] = fitDist90;
                        }
                        else if (defAngleDeg >= 25.0 && defAngleDeg <= 65.0)
                        {
                            cutBefore[i] = fitDist45;
                            cutAfter[i] = fitDist45;
                        }
                        else if (defAngleDeg > 5.0)
                        {
                            double miterFit = halfW * Math.Tan((defAngleDeg * Math.PI / 180.0) / 2.0);
                            cutBefore[i] = Math.Min(miterFit, pCur.DistanceTo(pPrev) / 3.0);
                            cutAfter[i] = Math.Min(miterFit, pCur.DistanceTo(pNext) / 3.0);
                        }
                    }

                    // BƯỚC 2: CHÈN PHỤ KIỆN BLOCK CO 90° VÀ CO 45°
                    for (int i = 1; i < numPts - 1; i++)
                    {
                        Point3d pPrev = pts[i - 1];
                        Point3d pCur = pts[i];
                        Point3d pNext = pts[i + 1];

                        Vector3d v1 = (pCur - pPrev).GetNormal();
                        Vector3d v2 = (pNext - pCur).GetNormal();

                        double cosA = v1.DotProduct(v2);
                        double crossZ = v1.X * v2.Y - v1.Y * v2.X;
                        double defAngleRad = Math.Atan2(crossZ, cosA);
                        double defAngleDeg = Math.Abs(defAngleRad * (180.0 / Math.PI));
                        double alpha1 = Math.Atan2(v1.Y, v1.X);

                        if (defAngleDeg >= 65.0 && defAngleDeg <= 115.0 && !elbow90BlockId.IsNull)
                        {
                            elbow90Count++;
                            double rot = alpha1 + Math.PI;
                            double sY = (crossZ >= 0) ? -scale90 : scale90;

                            BlockReference brefElbow = new BlockReference(pCur, elbow90BlockId);
                            brefElbow.Layer = LayerElecTmc;
                            brefElbow.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                            brefElbow.Rotation = rot;
                            brefElbow.ScaleFactors = new Scale3d(scale90, sY, 1.0);

                            btr.AppendEntity(brefElbow);
                            tr.AddNewlyCreatedDBObject(brefElbow, true);
                            if (outCreatedIds != null) outCreatedIds.Add(brefElbow.ObjectId);

                            AttachAttributesToBlockReference(brefElbow, elbow90BlockId, tr, p, p.WidthMm);
                        }
                        else if (defAngleDeg >= 25.0 && defAngleDeg <= 65.0 && !elbow45BlockId.IsNull)
                        {
                            elbow45Count++;
                            double rot = alpha1 + Math.PI;
                            double sY = (crossZ >= 0) ? -scale45 : scale45;

                            BlockReference brefElbow45 = new BlockReference(pCur, elbow45BlockId);
                            brefElbow45.Layer = LayerElecTmc;
                            brefElbow45.Color = Color.FromColorIndex(ColorMethod.ByAci, 30);
                            brefElbow45.Rotation = rot;
                            brefElbow45.ScaleFactors = new Scale3d(scale45, sY, 1.0);

                            btr.AppendEntity(brefElbow45);
                            tr.AddNewlyCreatedDBObject(brefElbow45, true);
                            if (outCreatedIds != null) outCreatedIds.Add(brefElbow45.ObjectId);

                            AttachAttributesToBlockReference(brefElbow45, elbow45BlockId, tr, p, p.WidthMm);
                        }
                    }

                    // BƯỚC 3: CHÈN CÁC ĐOẠN THẲNG DƯỚI DẠNG BLOCK REFERENCE
                    for (int i = 0; i < numPts - 1; i++)
                    {
                        Point3d pA = pts[i];
                        Point3d pB = pts[i + 1];
                        double fullSegDist = pA.DistanceTo(pB);
                        if (fullSegDist < 5.0) continue;

                        Vector3d dir = (pB - pA).GetNormal();

                        double startCut = cutAfter[i];
                        double endCut = cutBefore[i + 1];

                        if (startCut + endCut > fullSegDist)
                        {
                            double ratio = fullSegDist / (startCut + endCut + 1.0);
                            startCut *= ratio;
                            endCut *= ratio;
                        }

                        Point3d segStart = pA + dir * startCut;
                        Point3d segEnd = pB - dir * endCut;
                        double segDist = segStart.DistanceTo(segEnd);
                        if (segDist < 5.0) continue;

                        totalLengthMm += segDist;

                        // Chèn đoạn thẳng Block Reference
                        DrawSingleSegmentBlock(btr, tr, segStart, segEnd, p,
                            segmentBlockId, segBaseLength, segBaseWidth,
                            supportBlockId, ref supportCount, outCreatedIds);
                    }

                    tr.Commit();

                    string typeStr = isLadder ? "Thang Cáp (Ladder)" : (isTray ? "Khay Cáp (Perforated Tray)" : "Máng Cáp (Trunking)");
                    double totalM = totalLengthMm / 1000.0;
                    double perimeterM = (2.0 * p.HeightMm + p.WidthMm) / 1000.0;
                    double weightKg = totalM * perimeterM * p.ThicknessMm * 7.85 * 1.15;

                    CableTrayRouteResult res = new CableTrayRouteResult();
                    res.TypeName = typeStr;
                    res.DimensionLabel = string.Format("{0:F0}x{1:F0} mm", p.WidthMm, p.HeightMm);
                    res.WidthMm = p.WidthMm;
                    res.HeightMm = p.HeightMm;
                    res.ThicknessMm = p.ThicknessMm;
                    res.TotalLengthM = totalM;
                    res.SupportCount = supportCount;
                    res.ElbowCount = elbow90Count + elbow45Count;
                    res.Elbow90Count = elbow90Count;
                    res.Elbow45Count = elbow45Count;
                    res.SegmentCount = numPts - 1;
                    res.SheetMetalWeightKg = weightKg;

                    if (writeMessage)
                    {
                        ed.WriteMessage(string.Format("\n[TMC] Tuyến {0} ({1:F0}x{2:F0}mm): Dài {3:F2}m, {4} Co 90°, {5} Co 45°, {6} Giá đỡ!",
                            typeStr, p.WidthMm, p.HeightMm, totalM, elbow90Count, elbow45Count, supportCount));
                    }

                    return res;
                }
            }
        }



        /// <summary>
        /// Trích xuất danh sách điểm tọa độ từ thực thể Polyline hoặc Line
        /// </summary>
        public static List<Point3d> ExtractPointsFromEntity(Transaction tr, Entity ent)
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
                Line l = ent as Line;
                if (l != null)
                {
                    rawPts.Add(l.StartPoint);
                    rawPts.Add(l.EndPoint);
                }
            }

            List<Point3d> cleanPts = new List<Point3d>();
            for (int i = 0; i < rawPts.Count; i++)
            {
                if (cleanPts.Count == 0 || cleanPts[cleanPts.Count - 1].DistanceTo(rawPts[i]) >= 5.0)
                {
                    cleanPts.Add(rawPts[i]);
                }
            }
            return cleanPts;
        }

        /// <summary>
        /// Định tuyến Thang Máng Cáp tự động theo đối tượng Polyline trên bản vẽ
        /// </summary>
        public static CableTrayRouteResult RouteCableTrayAlongPolyline(Document doc, ObjectId plId, CableTrayParams p)
        {
            if (doc == null || plId.IsNull || p == null) return null;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<Point3d> pts;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(plId, OpenMode.ForRead) as Entity;
                if (ent == null) return null;
                pts = ExtractPointsFromEntity(tr, ent);
                tr.Commit();
            }

            if (pts == null || pts.Count < 2)
            {
                ed.WriteMessage("\n[TMC] Tuyến định tâm phải có ít nhất 2 điểm tọa độ!");
                return null;
            }

            return RouteCableTrayAlongPoints(doc, pts, p);
        }

        /// <summary>
        /// VẼ THỦ CÔNG LIÊN TỤC THEO ĐƯỜNG ĐỊNH TÂM VỚI REAL-TIME JIG:
        /// - Khi di chuyển chuột: Hiển thị ngay khung hình máng cáp trực quan (2 cạnh biên W, tim, nan thang/gờ khay) qua DrawJig.
        /// - Khi click mỗi điểm: Ngay lập tức chèn và hiển thị Block Thang Máng Cáp / Co trên bản vẽ mà không cần chờ kết thúc lệnh!
        /// - Nhấn ENTER hoặc ESC để kết thúc tuyến và tự động hoàn thiện báo cáo.
        /// </summary>
        public static bool DrawCableTrayManualWithJig(Document doc, CableTrayParams p)
        {
            if (doc == null || p == null) return false;
            Editor ed = doc.Editor;

            PromptPointOptions ppo1 = new PromptPointOptions("\n[TMC LIVE JIG] Click chọn điểm đầu tuyến Thang Máng Cáp (hoặc ENTER để chọn Polyline có sẵn, ESC để thoát): ");
            ppo1.AllowNone = true;
            PromptPointResult ppr1 = ed.GetPoint(ppo1);

            // Người dùng nhấn ENTER -> Chọn đường Polyline/Line có sẵn
            if (ppr1.Status == PromptStatus.None)
            {
                PromptEntityOptions peo = new PromptEntityOptions("\n[TMC] Chọn đường Polyline định tâm trên bản vẽ: ");
                peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline hoặc Line!");
                peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
                peo.AddAllowedClass(typeof(Line), exactMatch: false);
                peo.AllowNone = true;

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK)
                {
                    CableTrayRouteResult res = RouteCableTrayAlongPolyline(doc, per.ObjectId, p);
                    return res != null;
                }
                return false;
            }

            if (ppr1.Status != PromptStatus.OK)
            {
                // ESC hoặc Hủy -> Thoát lệnh
                return false;
            }

            Point3d ucsStart = ppr1.Value;
            Point3d wcsStart = ucsStart.TransformBy(ed.CurrentUserCoordinateSystem);

            List<Point3d> pathPts = new List<Point3d>();
            pathPts.Add(wcsStart);

            List<ObjectId> routeEntities = new List<ObjectId>();
            CableTrayRouteResult lastRes = null;

            // Pick liên tục các điểm trên đường định tâm với CableTrayDrawJig:
            // TỪNG ĐOẠN ĐƯỢC VẼ VÀ HIỂN THỊ NGAY LẬP TỨC TRÊN BẢN VẼ SAU MỖI CÚ CLICK!
            while (true)
            {
                Point3d lastWcs = pathPts[pathPts.Count - 1];
                CableTrayDrawJig jig = new CableTrayDrawJig(lastWcs, p, ed.CurrentUserCoordinateSystem);

                PromptResult pr = ed.Drag(jig);
                if (pr.Status != PromptStatus.OK)
                {
                    // ENTER hoặc ESC -> Hoàn tất tuyến hiện tại
                    break;
                }

                Point3d nextWcs = jig.CurrentWcsPoint;
                if (nextWcs.DistanceTo(lastWcs) < 5.0)
                {
                    continue;
                }

                pathPts.Add(nextWcs);

                // HIỂN THỊ MÁNG CÁP NGAY LẬP TỨC: Cập nhật cơ sở dữ liệu và màn hình ngay tại điểm vừa pick!
                List<ObjectId> newCreatedIds = new List<ObjectId>();
                lastRes = RouteCableTrayAlongPointsInternal(doc, pathPts, p,
                    oldEntitiesToErase: routeEntities,
                    outCreatedIds: newCreatedIds,
                    writeMessage: false);

                routeEntities = newCreatedIds;
                ed.UpdateScreen();
            }

            if (pathPts.Count >= 2 && lastRes != null)
            {
                string typeStr = p.Type == CableTrayType.Ladder ? "Thang Cáp" : (p.Type == CableTrayType.Tray ? "Khay Cáp" : "Máng Cáp");
                ed.WriteMessage(string.Format("\n[TMC] Hoàn tất vẽ tuyến {0} ({1:F0}x{2:F0}mm): Dài {3:F2}m, {4} Co 90°, {5} Co 45°, {6} Giá đỡ!\n",
                    typeStr, p.WidthMm, p.HeightMm, lastRes.TotalLengthM, lastRes.Elbow90Count, lastRes.Elbow45Count, lastRes.SupportCount));
                return true;
            }

            return false;
        }

                /// <summary>
        /// XUẤT BẢNG BÓC TÁCH KHỐI LƯỢNG THANG MÁNG CÁP VÀO BẢN VẼ CAD (TCVN 9208:2012)
        /// </summary>
        public static void DrawCableTrayBOMTableInCad(Document doc, CableTrayRouteResult res, CableTrayParams p)
        {
            if (doc == null || res == null) return;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptPointOptions ppo = new PromptPointOptions("\n[TMC] Chọn điểm chèn Bảng Bóc Tách Khối Lượng Thang Máng Cáp trên CAD: ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                Point3d ucsPt = ppr.Value;
                Point3d wcsPt = ucsPt.TransformBy(ed.CurrentUserCoordinateSystem);

                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LayerElecTmc, 30);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Table table = new Table();
                    table.TableStyle = db.Tablestyle;
                    table.Position = wcsPt;
                    table.Layer = LayerElecTmc;

                    int rows = 10;
                    int cols = 5;
                    table.SetSize(rows, cols);
                    table.SetRowHeight(12.0);
                    table.Columns[0].Width = 35.0;  // STT
                    table.Columns[1].Width = 200.0; // Hạng mục / Quy cách
                    table.Columns[2].Width = 50.0;  // ĐVT
                    table.Columns[3].Width = 60.0;  // Số lượng
                    table.Columns[4].Width = 140.0; // Ghi chú

                    // Header
                    table.Cells[0, 0].TextHeight = 5.0;
                    table.Cells[0, 0].TextString = "BẢNG TỔNG HỢP KHỐI LƯỢNG THANG MÁNG CÁP (TCVN 9208:2012)";

                    string[] headers = new string[] { "STT", "Danh Mục Thiết Bị / Phụ Kiện", "ĐVT", "Số Lượng", "Quy Cách Kỹ Thuật" };
                    for (int c = 0; c < cols; c++)
                    {
                        table.Cells[1, c].TextHeight = 3.5;
                        table.Cells[1, c].TextString = headers[c];
                    }

                    int r = 2;
                    // Dòng 1: Thân máng
                    table.Cells[r, 0].TextString = "1";
                    table.Cells[r, 1].TextString = string.Format("Thân {0} W{1:F0}xH{2:F0}", res.TypeName, res.WidthMm, res.HeightMm);
                    table.Cells[r, 2].TextString = "m";
                    table.Cells[r, 3].TextString = string.Format("{0:F2}", res.TotalLengthM);
                    table.Cells[r, 4].TextString = string.Format("Tôn tráng kẽm d={0:F1}mm", res.ThicknessMm);
                    r++;

                    // Dòng 2: Co 90°
                    table.Cells[r, 0].TextString = "2";
                    table.Cells[r, 1].TextString = string.Format("Co vuông 90° (Elbow 90) W{0:F0}xH{1:F0}", res.WidthMm, res.HeightMm);
                    table.Cells[r, 2].TextString = "Cái";
                    table.Cells[r, 3].TextString = res.Elbow90Count.ToString();
                    table.Cells[r, 4].TextString = "Phụ kiện cùng quy cách";
                    r++;

                    // Dòng 3: Co 45°
                    table.Cells[r, 0].TextString = "3";
                    table.Cells[r, 1].TextString = string.Format("Co lơi 45° (Elbow 45) W{0:F0}xH{1:F0}", res.WidthMm, res.HeightMm);
                    table.Cells[r, 2].TextString = "Cái";
                    table.Cells[r, 3].TextString = res.Elbow45Count.ToString();
                    table.Cells[r, 4].TextString = "Phụ kiện cùng quy cách";
                    r++;

                    // Dòng 4: Giá đỡ Unistrut
                    table.Cells[r, 0].TextString = "4";
                    table.Cells[r, 1].TextString = string.Format("Bộ giá đỡ ti ren Unistrut (L={0:F0}mm)", res.WidthMm + 200.0);
                    table.Cells[r, 2].TextString = "Bộ";
                    table.Cells[r, 3].TextString = res.SupportCount.ToString();
                    table.Cells[r, 4].TextString = string.Format("Nhô 100mm mỗi đầu, bước {0:F1}m", p.SupportSpacingM);
                    r++;

                    // Dòng 5: Khối lượng tôn ước tính
                    table.Cells[r, 0].TextString = "5";
                    table.Cells[r, 1].TextString = "Khối lượng tôn tấm ước tính";
                    table.Cells[r, 2].TextString = "Kg";
                    table.Cells[r, 3].TextString = string.Format("{0:F1}", res.SheetMetalWeightKg);
                    table.Cells[r, 4].TextString = "Tỷ trọng thép 7.85 T/m³ + 15% hao hụt";
                    r++;

                    btr.AppendEntity(table);
                    tr.AddNewlyCreatedDBObject(table, true);
                    tr.Commit();

                    ed.WriteMessage("\n[TMC] Đã chèn thành công Bảng Bóc Tách Khối Lượng vào bản vẽ!");
                }
            }
        }

        /// <summary>
        /// Cập nhật kích thước (Scale, Dynamic Properties và Attribute WxH) cho các BlockReference được chọn
        /// </summary>
        public static int UpdateBlockReferenceDimensions(Document doc, CableTrayParams p, SelectionSet explicitSelection = null)
        {
            if (doc == null || p == null) return 0;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            int updatedCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                SelectionSet sset = explicitSelection;
                if (sset == null || sset.Count == 0)
                {
                    PromptSelectionResult psr = ed.SelectImplied();
                    if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                    {
                        sset = psr.Value;
                    }
                }

                if (sset == null || sset.Count == 0)
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\n[NTT TOOLS] Chọn các Block Thang Máng Cáp cần cập nhật kích thước: ";
                    pso.AllowDuplicates = false;
                    pso.RejectObjectsFromNonCurrentSpace = true;

                    TypedValue[] tvs = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
                    SelectionFilter filter = new SelectionFilter(tvs);
                    PromptSelectionResult psrUser = ed.GetSelection(pso, filter);
                    if (psrUser.Status == PromptStatus.OK && psrUser.Value != null)
                    {
                        sset = psrUser.Value;
                    }
                }

                if (sset == null || sset.Count == 0)
                {
                    ed.WriteMessage("\n[NTT TOOLS] Không có Block Thang Máng Cáp nào được chọn!");
                    return 0;
                }

                string attrValue = string.Format("W{0:F0}xH{1:F0}", p.WidthMm, p.HeightMm);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in sset)
                    {
                        if (so == null) continue;
                        BlockReference bref = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as BlockReference;
                        if (bref == null) continue;

                        string bName = bref.Name != null ? bref.Name.ToUpperInvariant() : "";
                        if (bref.IsDynamicBlock)
                        {
                            BlockTableRecord effBtr = tr.GetObject(bref.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            if (effBtr != null && effBtr.Name != null) bName = effBtr.Name.ToUpperInvariant();
                        }

                        bool isSupport = bName.Contains("GIADO");
                        bool isElbow90 = bName.Contains("ELBOW") && !bName.Contains("45");
                        bool isElbow45 = bName.Contains("ELBOW") && bName.Contains("45");
                        bool isLadder = bName.Contains("LADDER");

                        if (isSupport)
                        {
                            double supLen = p.WidthMm + 200.0;
                            bref.ScaleFactors = new Scale3d(supLen / 300.0, 1.0, 1.0);
                        }
                        else if (isElbow90)
                        {
                            double sX = isLadder ? (p.WidthMm / 300.0) : (p.WidthMm / 200.0);
                            double signY = bref.ScaleFactors.Y < 0 ? -1.0 : 1.0;
                            bref.ScaleFactors = new Scale3d(sX, signY * sX, 1.0);
                        }
                        else if (isElbow45)
                        {
                            double sX = isLadder ? (p.WidthMm / 100.0) : (p.WidthMm / 300.0);
                            double signY = bref.ScaleFactors.Y < 0 ? -1.0 : 1.0;
                            bref.ScaleFactors = new Scale3d(sX, signY * sX, 1.0);
                        }
                        else
                        {
                            // Thân máng hoặc thang cáp: baseWidth chuẩn là 300mm
                            double scaleY = p.WidthMm / 300.0;
                            bref.ScaleFactors = new Scale3d(bref.ScaleFactors.X, scaleY, 1.0);

                            // Cập nhật thuộc tính Dynamic Block nếu có
                            if (bref.IsDynamicBlock)
                            {
                                foreach (DynamicBlockReferenceProperty prop in bref.DynamicBlockReferencePropertyCollection)
                                {
                                    string pName = prop.PropertyName;
                                    if (pName.Equals("Width", StringComparison.OrdinalIgnoreCase) ||
                                        pName.Equals("ChieuRong", StringComparison.OrdinalIgnoreCase) ||
                                        pName.Equals("W", StringComparison.OrdinalIgnoreCase) ||
                                        pName.Equals("Distance2", StringComparison.OrdinalIgnoreCase) ||
                                        pName.Equals("WidthMm", StringComparison.OrdinalIgnoreCase))
                                    {
                                        try { prop.Value = p.WidthMm; } catch { }
                                    }
                                }
                            }
                        }

                        // Cập nhật thuộc tính Attributes
                        bool foundAttr = false;
                        foreach (ObjectId attId in bref.AttributeCollection)
                        {
                            AttributeReference attRef = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                            if (attRef != null)
                            {
                                string tag = (attRef.Tag ?? "").ToUpperInvariant();
                                if (tag == "CABLETRAY" || tag == "LADDER" || tag == "WXH" || tag == "SIZE" || tag == "DIMENSION")
                                {
                                    attRef.TextString = attrValue;
                                    foundAttr = true;
                                }
                            }
                        }

                        // Nếu BlockReference chưa có AttributeReference, tự động bổ sung
                        if (!foundAttr)
                        {
                            AttachAttributesToBlockReference(bref, bref.BlockTableRecord, tr, p, 1000.0);
                        }

                        updatedCount++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage(string.Format("\n[NTT TOOLS] Đã tự động điều chỉnh {0} Block Thang Máng Cáp theo kích thước {1}!",
                    updatedCount, attrValue));
            }

            return updatedCount;
        }
    }
}

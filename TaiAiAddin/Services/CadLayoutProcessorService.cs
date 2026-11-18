using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    public static class CadLayoutProcessorService
    {
        public const string LayerKhungBao = "AI_XD_KHUNG_BAO";
        public const string LayerTuong = "AI_XD_TUONG";
        public const string LayerCot = "AI_XD_COT";
        public const string LayerCua = "AI_XD_CUA";
        public const string LayerVachKinh = "AI_XD_VACH_KINH";
        public const string LayerNoiThat = "AI_XD_NOI_THAT";
        public const string LayerDimText = "AI_XD_DIM_TEXT";
        public const string LayerGrid = "AI_XD_GRID";
        public const string LayerRac = "AI_XD_RAC";
        public const string LayerDuongBao = "DUONG_BAO";
        public const string LayerDuongBaoText = "DUONG_BAO_TEXT";
        public const string LayerDiemNoi = "AI_XD_DIEM_NOI";

        public static void EnsureStandardLayers(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            CreateLayer(lt, tr, db, LayerKhungBao, 6, 35);      // Magenta
            CreateLayer(lt, tr, db, LayerTuong, 7, 25);         // White
            CreateLayer(lt, tr, db, LayerCot, 3, 35);           // Green
            CreateLayer(lt, tr, db, LayerCua, 30, 25);          // Orange
            CreateLayer(lt, tr, db, LayerVachKinh, 5, 25);      // Blue
            CreateLayer(lt, tr, db, LayerNoiThat, 8, 15);       // Gray
            CreateLayer(lt, tr, db, LayerDimText, 8, 15);       // Gray
            CreateLayer(lt, tr, db, LayerGrid, 8, 15);          // Gray
            CreateLayer(lt, tr, db, LayerRac, 250, 13);         // Dark Gray
            CreateLayer(lt, tr, db, LayerDuongBao, 1, 35);      // Red
            CreateLayer(lt, tr, db, LayerDuongBaoText, 3, 20);  // Green
        }

        private static void CreateLayer(LayerTable lt, Transaction tr, Database db, string name, short colorIdx, int lineWeight)
        {
            if (!lt.Has(name))
            {
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, colorIdx)
                };
                if (lineWeight > 0)
                {
                    ltr.LineWeight = (LineWeight)lineWeight;
                }
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        public const string TextStyleUnicode = "AI_UNICODE_TEXT";

        public static ObjectId EnsureUnicodeTextStyle(Database db, Transaction tr)
        {
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(TextStyleUnicode))
            {
                return tst[TextStyleUnicode];
            }

            tst.UpgradeOpen();
            TextStyleTableRecord tsr = new TextStyleTableRecord
            {
                Name = TextStyleUnicode,
                FileName = "arial.ttf"
            };
            ObjectId id = tst.Add(tsr);
            tr.AddNewlyCreatedDBObject(tsr, true);
            return id;
        }

        /// <summary>
        /// Tự động quét ModelSpace để tìm khung bao hiện hữu trên layer AI_XD_KHUNG_BAO
        /// </summary>
        public static Extents3d? DetectExistingKhungBao(Document doc)
        {
            if (doc == null) return null;
            Database db = doc.Database;
            Extents3d? totalBounds = null;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && string.Equals(ent.Layer, LayerKhungBao, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                Extents3d ext = ent.GeometricExtents;
                                if (!totalBounds.HasValue)
                                    totalBounds = ext;
                                else
                                {
                                    Extents3d cur = totalBounds.Value;
                                    cur.AddExtents(ext);
                                    totalBounds = cur;
                                }
                            }
                            catch { }
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }

            return totalBounds;
        }

        /// <summary>
        /// Xóa sạch các đường bao và nhãn text DUONG_BAO / DUONG_BAO_TEXT nằm ngoài phạm vi khung bao đã khóa
        /// </summary>
        public static int PurgeBoundariesOutsideScope(Document doc, Extents3d scopeBounds)
        {
            if (doc == null) return 0;
            Database db = doc.Database;
            int count = 0;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && (string.Equals(ent.Layer, LayerDuongBao, StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(ent.Layer, LayerDuongBaoText, StringComparison.OrdinalIgnoreCase)))
                        {
                            try
                            {
                                Extents3d ext = ent.GeometricExtents;
                                double midX = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                                double midY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;

                                // Nếu tâm đối tượng nằm ngoài khung bao (+ dung sai 10mm)
                                if (midX < scopeBounds.MinPoint.X - 10.0 || midX > scopeBounds.MaxPoint.X + 10.0 ||
                                    midY < scopeBounds.MinPoint.Y - 10.0 || midY > scopeBounds.MaxPoint.Y + 10.0)
                                {
                                    ent.UpgradeOpen();
                                    ent.Erase();
                                    count++;
                                }
                            }
                            catch { }
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }

            return count;
        }

        /// <summary>
        /// Tạo hoặc cập nhật khung bao trên layer AI_XD_KHUNG_BAO từ 2 điểm
        /// </summary>
        public static Extents3d CreateKhungBao(Document doc, Point3d pt1, Point3d pt2)
        {
            Database db = doc.Database;
            double minX = Math.Min(pt1.X, pt2.X);
            double minY = Math.Min(pt1.Y, pt2.Y);
            double maxX = Math.Max(pt1.X, pt2.X);
            double maxY = Math.Max(pt1.Y, pt2.Y);

            Extents3d bounds = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureStandardLayers(db, tr);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                Polyline pl = new Polyline(4);
                pl.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
                pl.Closed = true;
                pl.Layer = LayerKhungBao;
                pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 6); // Magenta

                btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
            }

            return bounds;
        }

        /// <summary>
        /// Quét tất cả các layer hiện có bên trong khung bao, tự động học từ Từ điển + Hình học thực tế + Tên Layer
        /// </summary>
        public static List<LayerMappingItem> ScanLayers(Document doc, Extents3d? bounds)
        {
            return LayerLearningService.ScanAndLearnLayers(doc, bounds);
        }

        /// <summary>
        /// Ánh xạ layer: ưu tiên Từ điển kỹ sư đã học, sau đó đối chiếu từ khóa tên layer
        /// </summary>
        public static string SuggestAiLayer(string sourceLayer)
        {
            string learned;
            if (LayerLearningService.TryGetLearnedMapping(sourceLayer, out learned))
            {
                return learned;
            }
            string kw = SuggestAiLayerKeywordOnly(sourceLayer);
            return string.IsNullOrEmpty(kw) ? LayerRac : kw;
        }

        /// <summary>
        /// Đối chiếu thuần từ khóa tên Layer; trả về chuỗi rỗng nếu là Layer lạ chưa có từ khóa
        /// </summary>
        public static string SuggestAiLayerKeywordOnly(string sourceLayer)
        {
            string up = (sourceLayer ?? "").ToUpperInvariant();
            if (up.StartsWith("AI_") || up == "0" || up == "DEFPOINTS" || up.StartsWith("NON-PRINT") || up.StartsWith("DUONG_BAO"))
            {
                return "-- Bỏ Qua (Không Đổi) --";
            }

            // 1. Cột / Vách cứng BTCT / Dầm sàn
            if (up.Contains("COT") || up.Contains("DAM") || up.Contains("COLS") || up.Contains("COL_") || 
                up.Contains("BE TONG") || up.Contains("BETONG") || up.Contains("STR_") || up.Contains("VACH_BT") || up.Contains("VACH_CUONG"))
            {
                return LayerCot;
            }

            // 2. Cửa đi, cửa lùa, cửa thông phòng
            if (up.Contains("DOOR") || up.Contains("CUA_DI") || up.Contains("CUA_LUA") || up.Contains("CUA_THONG") || 
                up.Contains("CANH_CUA") || (up.Contains("CUA") && !up.Contains("CUA_SO") && !up.Contains("CUASO")))
            {
                return LayerCua;
            }

            // 3. Vách kính, cửa sổ, lam chớp, vách tắm
            if (up.Contains("GLAZ") || up.Contains("GLASS") || up.Contains("KINH") || up.Contains("WINDOW") || 
                up.Contains("CUA_SO") || up.Contains("CUASO") || up.Contains("CHOP") || up.Contains("LOUVER") || up.Contains("VACH_KINH"))
            {
                return LayerVachKinh;
            }

            // 4. Nội thất, thiết bị vệ sinh, thiết bị bếp, sàn gỗ, giường tủ
            if (up.Contains("NOITHAT") || up.Contains("NOI_THAT") || up.Contains("FURN") || up.Contains("FURNITURE") || 
                up.Contains("THIETBI") || up.Contains("THIET_BI") || up.Contains("EQUIP") || up.Contains("SAN_GO") || 
                up.Contains("TBVS") || up.Contains("PLUMB") || up.Contains("SANITA") || up.Contains("TOILET") || 
                up.Contains("LAVABO") || up.Contains("CHAU_RUA") || up.Contains("BON_CAU") || up.Contains("BED") || 
                up.Contains("GIUONG") || up.Contains("SOFA") || up.Contains("TABLE") || up.Contains("BAN_") || 
                up.Contains("CHAIR") || up.Contains("GHE_") || up.Contains("WARDROBE") || up.Contains("TU_AO") || 
                up.Contains("CLOSET") || up.Contains("KITCHEN") || up.Contains("BEP") || up.Contains("SHOWER") || up.Contains("BATHTUB"))
            {
                return LayerNoiThat;
            }

            // 5. Tường xây, vách ngăn, vữa trát hoàn thiện
            if (up.Contains("WALL") || up.Contains("TUONG") || up.Contains("TRAT") || up.Contains("FINISH") || 
                up.Contains("PATT") || up.Contains("XAY") || up.Contains("BRICK") || up.Contains("PARTITION"))
            {
                return LayerTuong;
            }

            // 6. Ghi chú, Text, Kích thước DIM
            if (up.Contains("DIM") || up.Contains("TEXT") || up.Contains("GHI CHU") || up.Contains("GHICHU") || 
                up.Contains("DIENTICH") || up.Contains("NOTE") || up.Contains("TAG") || up.Contains("LABEL") || up.Contains("LEADER"))
            {
                return LayerDimText;
            }

            // 7. Trục định vị
            if (up.Contains("TRUC") || up.Contains("GRID") || up.Contains("AXIS"))
            {
                return LayerGrid;
            }

            // 8. Chưa có từ khóa phù hợp
            return "";
        }

        /// <summary>
        /// TỰ ĐỘNG QUÉT VÀ DI DỜI CÁC ĐỐI TƯỢNG KIẾN TRÚC TRÊN LAYER 0 SANG ĐÚNG LAYER CHUẨN (AI_XD_TUONG, AI_XD_COT, AI_XD_CUA, AI_XD_NOI_THAT)
        /// Dùng chung bộ động cơ phân loại hình học thông minh (Tiếp giáp Cột & Tri thức Cửa) thay vì di dời mù quáng theo chiều dài.
        /// </summary>
        public static int MigrateWallEntitiesFromLayerZero(Document doc, Extents3d? bounds)
        {
            if (doc == null) return 0;
            List<SuspiciousEntityItem> classified = ScanSmartGeometryAndPatterns(doc, bounds, false);
            var layerZeroTargets = classified.Where(x =>
                string.Equals(x.CurrentLayer, "0", StringComparison.OrdinalIgnoreCase) &&
                x.IsSelected &&
                !string.IsNullOrEmpty(x.SuggestedLayer) &&
                !x.SuggestedLayer.StartsWith("--") &&
                (x.SuggestedLayer == LayerTuong || x.SuggestedLayer == LayerCot || x.SuggestedLayer == LayerCua || x.SuggestedLayer == LayerVachKinh || x.SuggestedLayer == LayerNoiThat)
            ).ToList();

            return ApplySuspiciousClassifications(doc, layerZeroTargets);
        }

        /// <summary>
        /// Thực thi chuẩn hóa layer: Mở khóa, gán layer mới, tự động Purge toàn bộ layer cũ không còn sử dụng
        /// </summary>
        public static int ApplyLayerStandardization(Document doc, List<LayerMappingItem> mappings, Extents3d? bounds)
        {
            int purged;
            return ApplyLayerStandardization(doc, mappings, bounds, out purged);
        }

        public static int ApplyLayerStandardization(Document doc, List<LayerMappingItem> mappings, Extents3d? bounds, out int purgedLayerCount)
        {
            purgedLayerCount = 0;
            if (mappings == null || mappings.Count == 0) return 0;
            Dictionary<string, string> mapDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mappings)
            {
                if (m.IsSelected && !string.IsNullOrEmpty(m.TargetLayer) && !m.TargetLayer.StartsWith("--"))
                {
                    mapDict[m.SourceLayer] = m.TargetLayer;
                }
            }
            if (mapDict.Count == 0) return 0;

            Database db = doc.Database;
            int changedCount = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                foreach (ObjectId layId in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForWrite);
                    ltr.IsLocked = false;
                    ltr.IsOff = false;
                }

                EnsureStandardLayers(db, tr);

                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null || ent.Layer == LayerKhungBao) continue;

                    if (bounds.HasValue)
                    {
                        try
                        {
                            Extents3d entExt = ent.GeometricExtents;
                            if (entExt.MaxPoint.X < bounds.Value.MinPoint.X || entExt.MinPoint.X > bounds.Value.MaxPoint.X ||
                                entExt.MaxPoint.Y < bounds.Value.MinPoint.Y || entExt.MinPoint.Y > bounds.Value.MaxPoint.Y)
                            {
                                continue;
                            }
                        }
                        catch { }
                    }

                    string targetLay;
                    if (mapDict.TryGetValue(ent.Layer, out targetLay))
                    {
                        ent.Layer = targetLay;
                        changedCount++;
                    }
                }

                // TỰ ĐỘNG PURGE TOÀN BỘ LAYER RÁC KHÔNG CÒN ĐỐI TƯỢNG SỬ DỤNG
                purgedLayerCount = PurgeUnusedLayers(db, tr);

                tr.Commit();
            }

            return changedCount;
        }

        /// <summary>
        /// Purge toàn bộ các layer rác không còn đối tượng sử dụng, bảo tồn tuyệt đối AI_XD_*, 0, Defpoints, DUONG_BAO*
        /// </summary>
        public static int PurgeUnusedLayers(Database db, Transaction tr)
        {
            if (db == null || tr == null) return 0;
            int purged = 0;
            try
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ObjectIdCollection layIdsToPurge = new ObjectIdCollection();

                foreach (ObjectId layId in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForRead);
                    string name = (ltr.Name ?? "").ToUpperInvariant();

                    // BẢO VỆ TUYỆT ĐỐI: 0, DEFPOINTS, AI_XD_*, DUONG_BAO*
                    if (name == "0" || name == "DEFPOINTS" || name.StartsWith("AI_XD_") || name.StartsWith("DUONG_BAO"))
                    {
                        continue;
                    }

                    layIdsToPurge.Add(layId);
                }

                if (layIdsToPurge.Count > 0)
                {
                    // Chuyển Clayer an toàn nếu Clayer nằm trong diện bị Purge
                    LayerTableRecord curLtr = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
                    string curName = (curLtr.Name ?? "").ToUpperInvariant();
                    if (curName != "0" && !curName.StartsWith("AI_XD_"))
                    {
                        if (lt.Has("AI_XD_TUONG"))
                        {
                            try { db.Clayer = lt["AI_XD_TUONG"]; } catch { }
                        }
                        else if (lt.Has("0"))
                        {
                            try { db.Clayer = lt["0"]; } catch { }
                        }
                    }

                    // Gọi db.Purge native API của AutoCAD để lọc ra các layer thực sự không còn đối tượng nào
                    db.Purge(layIdsToPurge);

                    // layIdsToPurge sau Purge chỉ còn lại các ObjectId an toàn 100% để xóa
                    foreach (ObjectId pId in layIdsToPurge)
                    {
                        try
                        {
                            DBObject obj = tr.GetObject(pId, OpenMode.ForWrite);
                            obj.Erase(true);
                            purged++;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return purged;
        }

        /// <summary>
        /// Quét các đối tượng nghi ngờ (Vách kính tắm, Cung cửa rời, Nét hở)
        /// </summary>
        public static List<SuspiciousEntityItem> ScanSuspiciousEntities(Document doc, Extents3d? bounds)
        {
            return ScanFurnitureAndShapePatterns(doc, bounds);
        }

        /// <summary>
        /// Quét và nhận diện chuyên sâu hình dạng nội thất & đối tượng topo bằng tri thức GNN (5.672+ Chữ Ký)
        /// </summary>
        public static List<SuspiciousEntityItem> ScanFurnitureAndShapePatterns(Document doc, Extents3d? bounds)
        {
            return ScanSmartGeometryAndPatterns(doc, bounds, true);
        }

        /// <summary>
        /// Quét phân tích Tường song song (>100mm) và các nét rời rạc
        /// </summary>
        public static List<SuspiciousEntityItem> ScanParallelWallsAndUnpaired(Document doc, Extents3d? bounds)
        {
            return ScanSmartGeometryAndPatterns(doc, bounds, true);
        }

        private class Segment2D
        {
            public ObjectId Id;
            public string HandleStr;
            public string Layer;
            public Point2d P1;
            public Point2d P2;
            public double Length;
            public double EntityMaxDim;
            public Vector2d Dir;
            public Vector2d Norm;
            public bool IsPaired;
            public bool IsFurnitureSeg;
            public bool IsGridLinetype;
            public Point3d MidPoint;
            public double MinX { get { return Math.Min(P1.X, P2.X); } }
            public double MaxX { get { return Math.Max(P1.X, P2.X); } }
            public double MinY { get { return Math.Min(P1.Y, P2.Y); } }
            public double MaxY { get { return Math.Max(P1.Y, P2.Y); } }
        }

        private class ArcCandidateInfo
        {
            public string HandleStr;
            public string Layer;
            public Point2d StartPt;
            public Point2d EndPt;
            public Point2d Center;
            public Point3d MidPoint;
            public double Radius;
        }

        private class UnknownShapeInfo
        {
            public string HandleStr;
            public string Layer;
            public string EntityType;
            public string SuggestedLayer;
            public string Reason;
            public Point3d Center;
            public double Radius;
        }

        private class RectCandidate
        {
            public ObjectId Id;
            public string HandleStr;
            public string Layer;
            public double Width;
            public double Height;
            public Point3d Center;
            public Extents3d Extents;
            public bool HasAnchorOverlap;
        }

        private class WallPairInfo
        {
            public Segment2D Seg1;
            public Segment2D Seg2;
            public double Distance;
            public Point2d MidlineP1;
            public Point2d MidlineP2;
            public bool IsDirectColumnTouch;
            public bool IsPropagatedWall;
            public bool IsConfirmedWall { get { return IsDirectColumnTouch || IsPropagatedWall; } }
            public double MinX { get { return Math.Min(Seg1.MinX, Seg2.MinX); } }
            public double MaxX { get { return Math.Max(Seg1.MaxX, Seg2.MaxX); } }
            public double MinY { get { return Math.Min(Seg1.MinY, Seg2.MinY); } }
            public double MaxY { get { return Math.Max(Seg1.MaxY, Seg2.MaxY); } }
        }

        private class DoorClusterInfo
        {
            public Point2d Center;
            public double Radius;
            public Point2d PStart;
            public Point2d PEnd;
        }

        /// <summary>
        /// ĐỘNG CƠ CHẨN ĐOÁN & HỌC HÌNH HỌC AI TOÀN DIỆN (Tường Tiếp Giáp Cột, Cửa Theo Tri Thức Đã Học, Nội Thất, Dim, Grid)
        /// </summary>
        public static List<SuspiciousEntityItem> ScanSmartGeometryAndPatterns(Document doc, Extents3d? bounds, bool includeUnpaired)
        {
            return ScanSmartGeometryAndPatterns(doc, bounds, includeUnpaired, null, null);
        }

        /// <summary>
        /// Bản có tiến trình + hủy. progress(phần trăm 0-100, nội dung). isCancelled() trả true sẽ ném OperationCanceledException (bản vẽ không bị thay đổi).
        /// </summary>
        public static List<SuspiciousEntityItem> ScanSmartGeometryAndPatterns(
            Document doc, Extents3d? bounds, bool includeUnpaired,
            Action<int, string> progress, Func<bool> isCancelled)
        {
            Database db = doc.Database;
            List<SuspiciousEntityItem> items = new List<SuspiciousEntityItem>();
            List<Segment2D> segments = new List<Segment2D>();
            List<RectCandidate> candidateColumns = new List<RectCandidate>();
            List<WallPairInfo> wallPairs = new List<WallPairInfo>();
            List<DoorClusterInfo> doorClusters = new List<DoorClusterInfo>();
            List<Extents3d> doorZoneBoxes = new List<Extents3d>();
            List<Extents3d> furnitureBoxes = new List<Extents3d>();
            List<Tuple<Point2d, Point2d>> wallHatchEdges = new List<Tuple<Point2d, Point2d>>();
            List<ArcCandidateInfo> otherArcs = new List<ArcCandidateInfo>();
            List<UnknownShapeInfo> otherShapes = new List<UnknownShapeInfo>();
            List<Tuple<Point2d, double>> gridCircles = new List<Tuple<Point2d, double>>();
            List<Point2d> shortGridTextPts = new List<Point2d>();
            HashSet<string> processedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ArchitecturalKnowledgeService.EnsureKnowledgeLoaded();
            ArchitecturalKnowledgeService.ResetScanCaches();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (progress != null) progress(2, "Pha 1: Thu thập mỏ neo & tri thức thuần hình học...");
                List<Extents3d> columnAnchors = ArchitecturalKnowledgeService.ExtractStructuralColumnAnchors(db, tr, bounds);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                int totalEnt = 0;
                foreach (ObjectId cid in btr) totalEnt++;
                int doneEnt = 0;

                // =========================================================================
                // PHA 1: TRÍCH XUẤT NGUYÊN THỦY THUẦN HÌNH HỌC (BỎ QUA MỌI TÊN LAYER AI_XD_* CŨ -> LŨY ĐẲNG 100%)
                // =========================================================================
                foreach (ObjectId id in btr)
                {
                    doneEnt++;
                    if ((doneEnt % 150) == 0)
                    {
                        if (isCancelled != null && isCancelled()) throw new OperationCanceledException();
                        if (progress != null)
                            progress(5 + (int)(55.0 * doneEnt / Math.Max(1, totalEnt)),
                                string.Format("Pha 1: Quét hình học {0}/{1}...", doneEnt, totalEnt));
                    }

                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !ent.Visible ||
                        string.Equals(ent.Layer, LayerKhungBao, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ent.Layer, LayerDiemNoi, StringComparison.OrdinalIgnoreCase)) continue;

                    Extents3d eExt = new Extents3d();
                    Point3d midPt = Point3d.Origin;
                    double width = 0, height = 0;
                    bool hasExtents = false;
                    try
                    {
                        eExt = ent.GeometricExtents;
                        hasExtents = true;
                        midPt = new Point3d((eExt.MinPoint.X + eExt.MaxPoint.X) / 2.0, (eExt.MinPoint.Y + eExt.MaxPoint.Y) / 2.0, 0);
                        width = Math.Abs(eExt.MaxPoint.X - eExt.MinPoint.X);
                        height = Math.Abs(eExt.MaxPoint.Y - eExt.MinPoint.Y);
                    }
                    catch { }

                    if (bounds.HasValue && hasExtents)
                    {
                        if (eExt.MaxPoint.X < bounds.Value.MinPoint.X || eExt.MinPoint.X > bounds.Value.MaxPoint.X ||
                            eExt.MaxPoint.Y < bounds.Value.MinPoint.Y || eExt.MinPoint.Y > bounds.Value.MaxPoint.Y)
                        {
                            continue;
                        }
                    }

                    string hStr = ent.Handle.ToString();

                    // 1. KÍCH THƯỚC (DIMENSION) & CHỈ DẪN (LEADER) -> AI_XD_DIM_TEXT
                    if (ent is Dimension || ent is Leader || ent is MLeader)
                    {
                        AddItemOnce(processedHandles, items, hStr, "Kích Thước (Dim/Leader)", ent.Layer, LayerDimText,
                            "Kích thước đo đạc / Chỉ dẫn kỹ thuật công trình", midPt, true);
                        continue;
                    }

                    // 2. TEXT & NHÃN TRỤC ĐỊNH VỊ (GRID)
                    if (ent is DBText || ent is MText)
                    {
                        string txtContent = (ent is DBText) ? ((DBText)ent).TextString : ((MText)ent).Contents;
                        txtContent = (txtContent ?? "").Trim();

                        bool isGrid = txtContent.IndexOf("TRUC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      txtContent.IndexOf("GRID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      txtContent.IndexOf("AXIS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      (txtContent.Length >= 1 && txtContent.Length <= 3 && width <= 800.0 && height <= 800.0 &&
                                       System.Text.RegularExpressions.Regex.IsMatch(txtContent, @"^[A-Za-z0-9]+$"));

                        if (isGrid) shortGridTextPts.Add(new Point2d(midPt.X, midPt.Y));

                        AddItemOnce(processedHandles, items, hStr,
                            isGrid ? "Trục Định Vị (Grid)" : "Text Ghi Chú",
                            ent.Layer,
                            isGrid ? LayerGrid : LayerDimText,
                            isGrid ? string.Format("Nhãn trục định vị '{0}'", txtContent)
                                   : string.Format("Ghi chú kỹ thuật: '{0}'", txtContent.Length > 20 ? txtContent.Substring(0, 20) + "..." : txtContent),
                            midPt, true);
                        continue;
                    }

                    // 3. CUNG TRÒN ARC: Cung xoay cửa (R=480..1450mm, 40..120°) vs Cung tường cong / Đối tượng lạ
                    if (ent is Arc)
                    {
                        Arc arc = (Arc)ent;
                        double sweep = arc.EndAngle - arc.StartAngle;
                        if (sweep < 0) sweep += 2.0 * Math.PI;
                        double sweepDeg = sweep * 180.0 / Math.PI;

                        if (arc.Radius >= 480.0 && arc.Radius <= 1450.0 && sweepDeg >= 40.0 && sweepDeg <= 120.0)
                        {
                            AddItemOnce(processedHandles, items, hStr,
                                string.Format("Cung Mở Cửa (R={0:F0}mm)", arc.Radius),
                                ent.Layer, LayerCua,
                                string.Format("Cung xoay cánh cửa đi (R={0:F0}mm, góc {1:F0}°) -> AI_XD_CUA", arc.Radius, sweepDeg),
                                midPt, true);

                            doorClusters.Add(new DoorClusterInfo
                            {
                                Center = new Point2d(arc.Center.X, arc.Center.Y),
                                Radius = arc.Radius,
                                PStart = new Point2d(arc.StartPoint.X, arc.StartPoint.Y),
                                PEnd = new Point2d(arc.EndPoint.X, arc.EndPoint.Y)
                            });
                            continue;
                        }

                        otherArcs.Add(new ArcCandidateInfo
                        {
                            HandleStr = hStr,
                            Layer = ent.Layer,
                            StartPt = new Point2d(arc.StartPoint.X, arc.StartPoint.Y),
                            EndPt = new Point2d(arc.EndPoint.X, arc.EndPoint.Y),
                            Center = new Point2d(arc.Center.X, arc.Center.Y),
                            MidPoint = midPt,
                            Radius = arc.Radius
                        });
                        continue;
                    }

                    // 4. CIRCLE / ELLIPSE / SPLINE: Kiểm tra bong bóng trục Grid hoặc đối tượng mới ngoài tri thức
                    if (ent is Circle)
                    {
                        Circle c = (Circle)ent;
                        if (c.Radius >= 80.0 && c.Radius <= 550.0)
                        {
                            gridCircles.Add(Tuple.Create(new Point2d(c.Center.X, c.Center.Y), c.Radius));
                        }
                        otherShapes.Add(new UnknownShapeInfo
                        {
                            HandleStr = hStr,
                            Layer = ent.Layer,
                            EntityType = string.Format("Hình Tròn Circle (R={0:F0}mm)", c.Radius),
                            SuggestedLayer = (c.Radius >= 80.0 && c.Radius <= 550.0) ? LayerGrid : LayerNoiThat,
                            Reason = string.Format("Đường tròn R={0:F0}mm (Kiểm tra thuộc Trục Grid hay Thiết bị)", c.Radius),
                            Center = midPt,
                            Radius = c.Radius
                        });
                        continue;
                    }

                    if (ent is Ellipse || ent is Spline)
                    {
                        otherShapes.Add(new UnknownShapeInfo
                        {
                            HandleStr = hStr,
                            Layer = ent.Layer,
                            EntityType = (ent is Ellipse) ? "Đường Cong Ellipse" : "Đường Cong Spline",
                            SuggestedLayer = LayerNoiThat,
                            Reason = "Hình học cong tự do ngoài mẫu tường/cửa chuẩn (Kiểm tra Nội thất hay Chi tiết mới)",
                            Center = midPt,
                            Radius = Math.Max(width, height) / 2.0
                        });
                        continue;
                    }

                    // 5. BLOCK REFERENCE: PHÂN LOẠI SÂU HÌNH HỌC NỘI TẠI + TRI THỨC ĐÃ HỌC (v27, v35, v36)
                    if (ent is BlockReference)
                    {
                        KnowledgeClassificationResult blkRes = ArchitecturalKnowledgeService.ClassifyBlockReference((BlockReference)ent, tr);
                        AddItemOnce(processedHandles, items, hStr, blkRes.EntityType, ent.Layer, blkRes.SuggestedLayer,
                            blkRes.Reason, midPt, true, blkRes.Confidence, blkRes.IsOutsideKnowledge);

                        if (blkRes.SuggestedLayer == LayerCua && hasExtents)
                        {
                            doorZoneBoxes.Add(eExt);
                        }
                        if (blkRes.SuggestedLayer == LayerNoiThat && hasExtents && !blkRes.IsOutsideKnowledge)
                        {
                            furnitureBoxes.Add(eExt);
                        }
                        if (blkRes.IsStructuralColumn && hasExtents)
                        {
                            columnAnchors.Add(eExt);
                        }
                        continue;
                    }

                    // 6. SOLID CỘT BTCT -> AI_XD_COT
                    if (ent is Solid)
                    {
                        if (Math.Min(width, height) >= 120.0 && Math.Max(width, height) <= 3500.0)
                        {
                            AddItemOnce(processedHandles, items, hStr, "Cột Solid BTCT", ent.Layer, LayerCot,
                                string.Format("Khối Solid {0:F0}x{1:F0}mm đặc trưng Cột kết cấu", width, height), midPt, true, 0.98);
                            if (hasExtents) columnAnchors.Add(eExt);
                        }
                        continue;
                    }

                    // 7. HATCH: Phân tách rạch ròi Hatch Cột SOLID vs Hatch Tường xây gạch (trích xuất cạnh HatchLoop thực tế!)
                    if (ent is Hatch && hasExtents)
                    {
                        Hatch hat = (Hatch)ent;
                        string patUp = (hat.PatternName ?? "").ToUpperInvariant();
                        if (patUp == "SOLID" || patUp.Contains("CONC"))
                        {
                            List<Extents3d> colLoops = ArchitecturalKnowledgeService.ExtractHatchLoopBoxes(hat, eExt, true);
                            if (colLoops.Count > 0 && colLoops.All(lb => Math.Min(lb.MaxPoint.X - lb.MinPoint.X, lb.MaxPoint.Y - lb.MinPoint.Y) <= 900.0 &&
                                                                        Math.Max(lb.MaxPoint.X - lb.MinPoint.X, lb.MaxPoint.Y - lb.MinPoint.Y) <= 3500.0))
                            {
                                AddItemOnce(processedHandles, items, hStr, "Hatch Cột BTCT", ent.Layer, LayerCot,
                                    string.Format("Mẫu Hatch đặc ({0}) đặc trưng Cột BTCT -> AI_XD_COT", hat.PatternName), midPt, true, 0.96);
                            }
                            else
                            {
                                wallHatchEdges.AddRange(ArchitecturalKnowledgeService.ExtractHatchLoopEdges(hat));
                                AddItemOnce(processedHandles, items, hStr, "Hatch Tường Xây (SOLID)", ent.Layer, LayerTuong,
                                    "Hatch đặc dạng dải tường liên hoàn -> AI_XD_TUONG", midPt, true, 0.94);
                            }
                        }
                        else if (patUp.Contains("ANSI") || patUp.Contains("BRICK") || patUp.Contains("AR-") ||
                                 patUp.Contains("MASON") || patUp.Contains("WALL") || patUp.Contains("USER"))
                        {
                            wallHatchEdges.AddRange(ArchitecturalKnowledgeService.ExtractHatchLoopEdges(hat));
                            AddItemOnce(processedHandles, items, hStr, "Hatch Tường Xây", ent.Layer, LayerTuong,
                                string.Format("Mẫu Hatch tường gạch ({0}) -> AI_XD_TUONG", hat.PatternName), midPt, true, 0.96);
                        }
                        else
                        {
                            AddItemOnce(processedHandles, items, hStr, "Hatch Mẫu Lạ Ngoài Tri Thức", ent.Layer, LayerNoiThat,
                                string.Format("Mẫu Hatch '{0}' chưa thuộc nhóm Cột/Tường chuẩn", hat.PatternName), midPt, false, 0.72, true);
                        }
                        continue;
                    }

                    // 8. ĐOẠN THẲNG LINE: KHÔNG cắt theo Length >= 6000 ở Pha 1! Cho tất cả vào Pha 2 để tường bao 7m-27m luôn được ghép cặp trước!
                    if (ent is Line)
                    {
                        Line line = (Line)ent;
                        string ltUp = (line.Linetype ?? "").ToUpperInvariant();
                        bool isGridLt = ltUp.Contains("CENTER") || ltUp.Contains("DASHDOT") || ltUp.Contains("AXIS") || ltUp.Contains("GRID");
                        TryAddSegment(segments, ent.ObjectId, hStr, ent.Layer,
                            new Point2d(line.StartPoint.X, line.StartPoint.Y),
                            new Point2d(line.EndPoint.X, line.EndPoint.Y), 20.0, line.Length, isGridLt);
                        continue;
                    }

                    // 9. POLYLINE: Cung cửa Bulge / Cột có Hatch SOLID / Phân rã cạnh vào segments để ghép tường & tách nội thất công bằng
                    if (ent is Polyline)
                    {
                        Polyline pl = (Polyline)ent;
                        double entMaxDim = Math.Max(width, height);
                        double entMinDim = Math.Min(width, height);
                        string ltUp = (pl.Linetype ?? "").ToUpperInvariant();
                        bool isGridLt = ltUp.Contains("CENTER") || ltUp.Contains("DASHDOT") || ltUp.Contains("AXIS") || ltUp.Contains("GRID");

                        // a. Phát hiện cung xoay cửa vẽ bằng Polyline Bulge
                        if (TryExtractBulgeDoorArcs(pl, doorClusters))
                        {
                            AddItemOnce(processedHandles, items, hStr, "Cung Cửa (Polyline Bulge)", ent.Layer, LayerCua,
                                "Polyline chứa cung cong bán kính cửa (R=480..1450mm) -> AI_XD_CUA", midPt, true);
                            if (hasExtents) doorZoneBoxes.Add(eExt);
                            continue;
                        }

                        // b. Cột BTCT (Polyline khép kín chồng phần ruột vào Mỏ neo Hatch SOLID / Solid BTCT)
                        if (pl.Closed && pl.NumberOfVertices >= 4 && pl.NumberOfVertices <= 16 &&
                            entMinDim >= 140.0 && entMinDim <= 900.0 && entMaxDim >= 140.0 && entMaxDim <= 3500.0)
                        {
                            bool overlapsExistingAnchor = hasExtents && columnAnchors.Any(a => ArchitecturalKnowledgeService.BoxesOverlapInterior2D(a, eExt, 45.0));
                            if (overlapsExistingAnchor)
                            {
                                AddItemOnce(processedHandles, items, hStr, "Cột / Vách BTCT (Có Hatch)", ent.Layer, LayerCot,
                                    string.Format("Khối kết cấu {0:F0}x{1:F0}mm chứa Hatch/Solid Cột chịu lực -> AI_XD_COT", width, height),
                                    midPt, true, 0.98);
                                if (hasExtents) columnAnchors.Add(eExt);
                                continue;
                            }

                            // Nếu là hộp rỗng khép kín nằm trên biên tường hoặc hộp kích thước 140..2500mm, lưu ứng viên để xét sau khi đã dựng xong tường
                            bool isCompactBoxPerimeter = pl.Length <= 2.25 * (width + height) + 120.0;
                            if (isCompactBoxPerimeter && entMaxDim <= 2500.0)
                            {
                                candidateColumns.Add(new RectCandidate
                                {
                                    Id = ent.ObjectId,
                                    HandleStr = hStr,
                                    Layer = ent.Layer,
                                    Width = width,
                                    Height = height,
                                    Center = midPt,
                                    Extents = eExt,
                                    HasAnchorOverlap = false
                                });
                                // Đồng thời phân rã cạnh nếu tiết diện giống đoạn tường (95..350mm) để có thể bắt cặp tường ở Pha 2!
                                if (entMinDim >= 95.0 && entMinDim <= 360.0)
                                {
                                    AddPolylineSegments(pl, segments, hStr, entMaxDim, isGridLt);
                                }
                                continue;
                            }
                        }

                        // c. Kiểm tra Profile Hình học Nội thất rời đặc trưng (chỉ khi không phải dải tường)
                        string furnProfile;
                        if (pl.Closed && ArchitecturalKnowledgeService.MatchesFurnitureRectProfile(width, height, pl.NumberOfVertices, pl.Closed, out furnProfile))
                        {
                            AddItemOnce(processedHandles, items, hStr, "Hình Học Nội Thất", ent.Layer, LayerNoiThat,
                                "Khớp tri thức hình dạng nội thất: " + furnProfile, midPt, true, 0.95);
                            if (hasExtents) furnitureBoxes.Add(eExt);
                            continue;
                        }

                        // d. Phân rã các cạnh thẳng của Polyline
                        AddPolylineSegments(pl, segments, hStr, entMaxDim, isGridLt);
                    }
                }

                // =========================================================================
                // PHA 2: GHÉP CẶP TƯỜNG SONG SONG TRƯỚC -> XÁC THỰC ĐA CHỨNG CỨ -> LAN TRUYỀN L/T & TƯỜNG CONG
                // =========================================================================
                if (progress != null) progress(65, "Pha 2: Ghép cặp tường & xác thực đa chứng cứ...");
                if (isCancelled != null && isCancelled()) throw new OperationCanceledException();
                FindParallelWallCandidatePairs(segments, wallPairs);
                ConfirmWallsByMultiEvidenceGraph(wallPairs, columnAnchors, wallHatchEdges, candidateColumns, doorClusters, doorZoneBoxes);

                bool hasAnyStructuralOrHatchEvidence = columnAnchors.Count > 0 || wallHatchEdges.Count > 0;
                double buildingMinX = double.MaxValue, buildingMinY = double.MaxValue;
                double buildingMaxX = double.MinValue, buildingMaxY = double.MinValue;

                foreach (var wp in wallPairs.OrderByDescending(w => w.IsConfirmedWall).ThenByDescending(w => w.IsDirectColumnTouch))
                {
                    bool confirmed = wp.IsConfirmedWall || (!hasAnyStructuralOrHatchEvidence && Math.Max(wp.Seg1.Length, wp.Seg2.Length) >= 750.0);
                    string targetLayer = confirmed ? LayerTuong : LayerNoiThat;
                    double pairConf = wp.IsDirectColumnTouch ? 0.97 : (wp.IsPropagatedWall ? 0.95 : (confirmed ? 0.86 : 0.92));
                    string entityType = confirmed ? "Tường Kiến Trúc" : "Cụm Song Song (Nội Thất)";
                    string reason = confirmed
                        ? string.Format("Cặp nét song song d={0:F0}mm {1}", wp.Distance,
                            wp.IsDirectColumnTouch ? "xác thực Cột / Hatch Tường / Khung nhà chính" : "đấu nối khung Tường (L/T/Cửa)")
                        : string.Format("Cặp song song d={0:F0}mm ngắn & độc lập trong phòng -> Nội thất (AI_XD_NOI_THAT)", wp.Distance);

                    if (confirmed)
                    {
                        if (wp.MinX < buildingMinX) buildingMinX = wp.MinX;
                        if (wp.MinY < buildingMinY) buildingMinY = wp.MinY;
                        if (wp.MaxX > buildingMaxX) buildingMaxX = wp.MaxX;
                        if (wp.MaxY > buildingMaxY) buildingMaxY = wp.MaxY;
                    }
                    else
                    {
                        wp.Seg1.IsFurnitureSeg = true;
                        wp.Seg2.IsFurnitureSeg = true;
                        double bMinX = Math.Min(wp.Seg1.MinX, wp.Seg2.MinX), bMaxX = Math.Max(wp.Seg1.MaxX, wp.Seg2.MaxX);
                        double bMinY = Math.Min(wp.Seg1.MinY, wp.Seg2.MinY), bMaxY = Math.Max(wp.Seg1.MaxY, wp.Seg2.MaxY);
                        furnitureBoxes.Add(new Extents3d(new Point3d(bMinX, bMinY, 0), new Point3d(bMaxX, bMaxY, 0)));
                    }

                    AddItemOnce(processedHandles, items, wp.Seg1.HandleStr, entityType, wp.Seg1.Layer, targetLayer, reason, wp.Seg1.MidPoint, true, pairConf);
                    AddItemOnce(processedHandles, items, wp.Seg2.HandleStr, entityType, wp.Seg2.Layer, targetLayer, reason, wp.Seg2.MidPoint, true, pairConf);
                }

                // Khóa toàn bộ nét bịt đầu tường (End-Cap) & các cung tường cong liên kết tường -> AI_XD_TUONG
                MarkConfirmedWallEndCapSegments(segments, wallPairs, processedHandles, items);
                ClassifyCurvedWallArcs(otherArcs, wallPairs, wallHatchEdges, processedHandles, items);

                // Giải quyết các candidateColumns dựa trên kết quả Tường & Hatch (tránh nhầm lẫn Cột vs Hộp tường vs Táp đầu giường)
                foreach (var col in candidateColumns)
                {
                    if (processedHandles.Contains(col.HandleStr)) continue;
                    double minD = Math.Min(col.Width, col.Height);
                    double maxD = Math.Max(col.Width, col.Height);
                    bool alignsWallHatch = DoesBoxAlignWithHatchEdges(col.Extents, wallHatchEdges);

                    if (alignsWallHatch && minD >= 95.0 && minD <= 360.0)
                    {
                        AddItemOnce(processedHandles, items, col.HandleStr, "Tường Xây (Có Hatch Gạch)", col.Layer, LayerTuong,
                            string.Format("Khối tường {0:F0}x{1:F0}mm khớp Hatch tường xây -> AI_XD_TUONG", col.Width, col.Height),
                            col.Center, true, 0.96);
                    }
                    else if (col.HasAnchorOverlap || (!hasAnyStructuralOrHatchEvidence && minD <= 235.0) ||
                             (columnAnchors.Count > 0 && minD >= 180.0 && minD <= 235.0 && maxD <= 450.0))
                    {
                        AddItemOnce(processedHandles, items, col.HandleStr, "Cột BTCT (Kết Cấu)", col.Layer, LayerCot,
                            string.Format("Tiết diện {0:F0}x{1:F0}mm đặc trưng Cột kết cấu -> AI_XD_COT", col.Width, col.Height),
                            col.Center, true, 0.95);
                    }
                    else
                    {
                        AddItemOnce(processedHandles, items, col.HandleStr, "Hộp Nội Thất / Tủ Kệ Rỗng", col.Layer, LayerNoiThat,
                            string.Format("Khối hộp rỗng {0:F0}x{1:F0}mm (không có Hatch cột đặc) -> Nội Thất (AI_XD_NOI_THAT)", col.Width, col.Height),
                            col.Center, true, 0.94);
                        furnitureBoxes.Add(col.Extents);
                    }
                }

                // =========================================================================
                // PHA 3: ĐƯỜNG TRỤC ĐỊNH VỊ (AI_XD_GRID) — CHỈ XÉT CÁC NÉT ĐƠN CHƯA GHÉP TƯỜNG
                // =========================================================================
                if (progress != null) progress(76, "Pha 3: Xác thực Trục Grid định vị...");
                MarkGridAxisSegments(segments, gridCircles, shortGridTextPts,
                    buildingMinX, buildingMinY, buildingMaxX, buildingMaxY, processedHandles, items);
                ClassifyGridBubblesAndUnknownShapes(otherShapes, shortGridTextPts, processedHandles, items);

                // =========================================================================
                // PHA 4: CỤM CẦU THANG, CÁNH CỬA, NAN LAM/MÓC ÁO, THIẾT BỊ MẢNH & GOM CỤM NỘI THẤT
                // =========================================================================
                if (progress != null) progress(84, "Pha 4: Nhận diện Cầu thang, Cửa & Gom cụm Nội thất...");
                if (isCancelled != null && isCancelled()) throw new OperationCanceledException();
                MarkDoorClusterSegments(segments, doorClusters, doorZoneBoxes, processedHandles, items);
                MarkStaircaseSegments(segments, furnitureBoxes, processedHandles, items);
                MarkLouverArraySegments(segments, furnitureBoxes, processedHandles, items);
                MarkNarrowFurniturePairs(segments, furnitureBoxes, wallHatchEdges, processedHandles, items);
                PropagateConnectedFurnitureSegments(segments, furnitureBoxes, processedHandles, items);

                // =========================================================================
                // PHA 5: NÉT DƯ RỜI RẠC (AI_XD_RAC) & BÁO CÁO ĐỐI TƯỢNG MỚI NGOÀI TRI THỨC
                // =========================================================================
                if (progress != null) progress(94, "Pha 5: Phân loại nét dư & tổng hợp đối tượng mới...");
                if (includeUnpaired)
                {
                    foreach (var seg in segments)
                    {
                        if (!seg.IsPaired && !processedHandles.Contains(seg.HandleStr))
                        {
                            // Nét đơn dài 1500..5500mm không thuộc Tường, Cầu thang hay Grid có thể là chi tiết kiến trúc mới cần báo kỹ sư
                            bool ambiguousLong = seg.Length >= 1500.0;
                            double conf = ambiguousLong ? 0.78 : 0.90;
                            AddItemOnce(processedHandles, items, seg.HandleStr,
                                ambiguousLong ? "Đường Đơn Dài Ngoài Spec" : "Đường 1 Nét Rời (Rác)",
                                seg.Layer, LayerRac,
                                ambiguousLong
                                    ? string.Format("Đường đơn L={0:F0}mm không có cặp tường đối ứng & không phải Grid (Cần kiểm tra)", seg.Length)
                                    : string.Format("Đường 1 nét ngắn đơn lẻ L={0:F0}mm -> AI_XD_RAC", seg.Length),
                                seg.MidPoint, !ambiguousLong, conf, ambiguousLong);
                        }
                    }
                }

                tr.Commit();
            }

            return items;
        }

        private static void AddPolylineSegments(Polyline pl, List<Segment2D> segments, string handleStr, double entMaxDim, bool isGridLt)
        {
            for (int v = 0; v < pl.NumberOfVertices; v++)
            {
                if (Math.Abs(pl.GetBulgeAt(v)) < 1e-4)
                {
                    int nextIdx = v + 1;
                    if (nextIdx >= pl.NumberOfVertices)
                    {
                        if (pl.Closed) nextIdx = 0;
                        else continue;
                    }
                    TryAddSegment(segments, pl.ObjectId, handleStr, pl.Layer, pl.GetPoint2dAt(v), pl.GetPoint2dAt(nextIdx), 20.0, entMaxDim, isGridLt);
                }
            }
        }

        private static void AddItemOnce(
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items,
            string handleStr, string entityType, string curLayer, string suggestedLayer,
            string reason, Point3d centerPoint, bool isSelected, double confidence = 0.95, bool isOutsideKnowledge = false)
        {
            if (string.IsNullOrEmpty(handleStr) || processedHandles.Contains(handleStr)) return;
            processedHandles.Add(handleStr);
            bool needsRev = isOutsideKnowledge || confidence < 0.85;
            items.Add(new SuspiciousEntityItem
            {
                HandleStr = handleStr,
                EntityType = entityType,
                CurrentLayer = curLayer,
                SuggestedLayer = suggestedLayer,
                Reason = reason,
                CenterPoint = centerPoint,
                Confidence = confidence,
                IsOutsideKnowledge = isOutsideKnowledge,
                IsSelected = isSelected && !needsRev
            });
        }

        private static void TryAddSegment(List<Segment2D> segments, ObjectId id, string handleStr, string layer,
            Point2d p1, Point2d p2, double minLen, double entityMaxDim = 0.0, bool isGridLinetype = false)
        {
            Vector2d dir = p2 - p1;
            double len = dir.Length;
            if (len < minLen) return;
            Vector2d dirNorm = dir / len;
            segments.Add(new Segment2D
            {
                Id = id,
                HandleStr = handleStr,
                Layer = layer,
                P1 = p1,
                P2 = p2,
                Length = len,
                EntityMaxDim = entityMaxDim > 0.0 ? entityMaxDim : len,
                Dir = dirNorm,
                Norm = new Vector2d(-dirNorm.Y, dirNorm.X),
                IsPaired = false,
                IsFurnitureSeg = false,
                IsGridLinetype = isGridLinetype,
                MidPoint = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0)
            });
        }

        private static bool TryExtractBulgeDoorArcs(Polyline pl, List<DoorClusterInfo> doorClusters)
        {
            bool found = false;
            int nv = pl.NumberOfVertices;
            for (int v = 0; v < nv; v++)
            {
                double bulge = pl.GetBulgeAt(v);
                if (Math.Abs(bulge) <= 0.08) continue;

                int nextIdx = (v + 1 < nv) ? v + 1 : (pl.Closed ? 0 : -1);
                if (nextIdx < 0) continue;

                Point2d p1 = pl.GetPoint2dAt(v);
                Point2d p2 = pl.GetPoint2dAt(nextIdx);
                double chord = p1.GetDistanceTo(p2);
                double absB = Math.Abs(bulge);
                double r = chord * (1.0 + absB * absB) / (4.0 * absB);
                double sweepDeg = 4.0 * Math.Atan(absB) * 180.0 / Math.PI;

                if (r >= 480.0 && r <= 1450.0 && sweepDeg >= 40.0 && sweepDeg <= 120.0)
                {
                    double halfChord = chord / 2.0;
                    double dMid = (r * r > halfChord * halfChord) ? Math.Sqrt(r * r - halfChord * halfChord) : 0.0;
                    Point2d midChord = new Point2d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
                    Vector2d dir = (p2 - p1) / Math.Max(chord, 1e-6);
                    Vector2d leftNorm = new Vector2d(-dir.Y, dir.X);
                    double sign = (bulge > 0) ? 1.0 : -1.0;
                    Point2d center = midChord + leftNorm * (sign * dMid);

                    doorClusters.Add(new DoorClusterInfo { Center = center, Radius = r, PStart = p1, PEnd = p2 });
                    found = true;
                }
            }
            return found;
        }

        private static bool BoxesOverlap2D(Extents3d a, Extents3d b, double tol)
        {
            return !(a.MaxPoint.X + tol < b.MinPoint.X || a.MinPoint.X - tol > b.MaxPoint.X ||
                     a.MaxPoint.Y + tol < b.MinPoint.Y || a.MinPoint.Y - tol > b.MaxPoint.Y);
        }

        private static void MarkDoorClusterSegments(
            List<Segment2D> segments, List<DoorClusterInfo> doorClusters, List<Extents3d> doorZoneBoxes,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            foreach (var seg in segments)
            {
                if (seg.IsPaired) continue;
                // Không bao giờ gán các Polyline tường/phòng dài (>1450mm) thành cửa chỉ vì 1 đoạn ngắn chạm cửa
                if (seg.EntityMaxDim > 1450.0) continue;

                foreach (var dc in doorClusters)
                {
                    double dToCenter = ArchitecturalKnowledgeService.DistancePointToSegment2D(dc.Center, seg.P1, seg.P2);
                    double dToStart = ArchitecturalKnowledgeService.DistancePointToSegment2D(dc.PStart, seg.P1, seg.P2);
                    double dToEnd = ArchitecturalKnowledgeService.DistancePointToSegment2D(dc.PEnd, seg.P1, seg.P2);

                    // 1. Cánh cửa mở quay: nối từ Tâm bản lề (dc.Center) tới điểm đầu hoặc điểm cuối cung Arc
                    bool isSwingLeaf = seg.Length >= 400.0 && seg.Length <= (dc.Radius + 450.0) &&
                                       dToCenter <= 95.0 && (dToStart <= 95.0 || dToEnd <= 95.0);

                    // 2. Đố cửa / Nắp đầu cánh nhỏ ngay tại bản lề hoặc đầu mút cung cửa (<= 220mm)
                    bool isSmallJambOrCap = seg.Length <= 220.0 && seg.EntityMaxDim <= 220.0 &&
                                            (dToCenter <= 65.0 || dToStart <= 65.0 || dToEnd <= 65.0);

                    if (isSwingLeaf || isSmallJambOrCap)
                    {
                        seg.IsPaired = true;
                        AddItemOnce(processedHandles, items, seg.HandleStr, "Cánh Cửa / Khung Cửa", seg.Layer, LayerCua,
                            string.Format("Nét cánh cửa/đố cửa nối trực tiếp cung xoay cửa (R={0:F0}mm)", dc.Radius), seg.MidPoint, true, 0.97);
                        break;
                    }
                }

                if (seg.IsPaired) continue;

                // Bảo vệ các nét ngắn nằm gọn bên trong vùng Block cửa không bị ghép nhầm thành Tường
                foreach (var dBox in doorZoneBoxes)
                {
                    double bw = dBox.MaxPoint.X - dBox.MinPoint.X;
                    double bh = dBox.MaxPoint.Y - dBox.MinPoint.Y;
                    if (seg.Length <= 1400.0 && seg.EntityMaxDim <= Math.Max(bw, bh) + 60.0 &&
                        seg.P1.X >= dBox.MinPoint.X - 20.0 && seg.P1.X <= dBox.MaxPoint.X + 20.0 &&
                        seg.P1.Y >= dBox.MinPoint.Y - 20.0 && seg.P1.Y <= dBox.MaxPoint.Y + 20.0 &&
                        seg.P2.X >= dBox.MinPoint.X - 20.0 && seg.P2.X <= dBox.MaxPoint.X + 20.0 &&
                        seg.P2.Y >= dBox.MinPoint.Y - 20.0 && seg.P2.Y <= dBox.MaxPoint.Y + 20.0)
                    {
                        seg.IsPaired = true;
                        AddItemOnce(processedHandles, items, seg.HandleStr, "Nét Trong Vùng Cửa", seg.Layer, LayerCua,
                            "Nét nằm gọn trong phạm vi Block Cửa -> Bảo vệ sang AI_XD_CUA", seg.MidPoint, true, 0.95);
                        break;
                    }
                }
            }
        }

        /// <summary>Chỉ mục lưới không gian (ô 2000mm) thay cho so cặp O(n²).</summary>
        private class SegmentGrid
        {
            private const double Cell = 2000.0;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly List<Segment2D> _segs;

            public SegmentGrid(List<Segment2D> segs)
            {
                _segs = segs;
                for (int i = 0; i < segs.Count; i++)
                {
                    int x0 = CellOf(segs[i].MinX), x1 = CellOf(segs[i].MaxX);
                    int y0 = CellOf(segs[i].MinY), y1 = CellOf(segs[i].MaxY);
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cy = y0; cy <= y1; cy++)
                        {
                            long key = Key(cx, cy);
                            List<int> lst;
                            if (!_cells.TryGetValue(key, out lst)) { lst = new List<int>(); _cells[key] = lst; }
                            lst.Add(i);
                        }
                }
            }

            private static int CellOf(double v) { return (int)Math.Floor(v / Cell); }
            private static long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }

            /// <summary>Danh sách chỉ số đoạn nằm gần sA (bbox mở rộng margin), đã sắp tăng dần.</summary>
            public List<int> Near(Segment2D s, double margin)
            {
                HashSet<int> set = new HashSet<int>();
                int x0 = CellOf(s.MinX - margin), x1 = CellOf(s.MaxX + margin);
                int y0 = CellOf(s.MinY - margin), y1 = CellOf(s.MaxY + margin);
                for (int cx = x0; cx <= x1; cx++)
                    for (int cy = y0; cy <= y1; cy++)
                    {
                        List<int> lst;
                        if (_cells.TryGetValue(Key(cx, cy), out lst))
                            foreach (int idx in lst) set.Add(idx);
                    }
                List<int> res = new List<int>(set);
                res.Sort();
                return res;
            }
        }

        private static void MarkLouverArraySegments(
            List<Segment2D> segments, List<Extents3d> furnitureBoxes,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            int n = segments.Count;
            SegmentGrid grid = new SegmentGrid(segments);
            for (int i = 0; i < n; i++)
            {
                Segment2D sA = segments[i];
                if (sA.IsPaired || sA.Length < 80.0 || sA.Length > 1800.0 || sA.EntityMaxDim > 1850.0) continue;

                Point2d midA = new Point2d(sA.MidPoint.X, sA.MidPoint.Y);
                List<int> cluster = new List<int> { i };
                List<double> offsets = new List<double> { 0.0 };

                foreach (int j in grid.Near(sA, 900.0))
                {
                    if (i == j) continue;
                    Segment2D sB = segments[j];
                    if (sB.IsPaired || sB.EntityMaxDim > 1850.0) continue;
                    if (Math.Abs(sB.Length - sA.Length) > Math.Max(80.0, sA.Length * 0.25)) continue;
                    if (Math.Abs(sA.Dir.X * sB.Dir.X + sA.Dir.Y * sB.Dir.Y) < 0.995) continue;

                    // Dùng tâm đoạn thẳng để bất biến với chiều vẽ P1 <-> P2 (giống hệt DiagnoseSelection)
                    Vector2d vMid = new Point2d(sB.MidPoint.X, sB.MidPoint.Y) - midA;
                    double along = Math.Abs(vMid.X * sA.Dir.X + vMid.Y * sA.Dir.Y);
                    if (along > 220.0) continue;

                    double off = vMid.X * sA.Norm.X + vMid.Y * sA.Norm.Y;
                    if (Math.Abs(off) >= 15.0 && Math.Abs(off) <= 900.0)
                    {
                        if (!offsets.Any(o => Math.Abs(o - off) < 10.0)) offsets.Add(off);
                        cluster.Add(j);
                    }
                }

                // Đồng bộ 100% với ô AI Chẩn đoán: >= 3 thanh song song xếp liên tiếp -> Cụm Nan Lam / Móc Tủ Áo (AI_XD_NOI_THAT)
                if (offsets.Count >= 3)
                {
                    offsets.Sort();
                    double maxStep = 0;
                    for (int k = 1; k < offsets.Count; k++)
                    {
                        double step = offsets[k] - offsets[k - 1];
                        if (step > maxStep) maxStep = step;
                    }

                    if (maxStep <= 450.0)
                    {
                        double cMinX = double.MaxValue, cMinY = double.MaxValue, cMaxX = double.MinValue, cMaxY = double.MinValue;
                        string reason = string.Format("Phát hiện {0} thanh song song xếp liên tiếp (L~{1:F0}mm) -> Nội Thất / Móc Tủ Áo (AI_XD_NOI_THAT)", offsets.Count, sA.Length);
                        foreach (int idx in cluster)
                        {
                            Segment2D sc = segments[idx];
                            sc.IsPaired = true;
                            sc.IsFurnitureSeg = true;
                            if (sc.MinX < cMinX) cMinX = sc.MinX;
                            if (sc.MinY < cMinY) cMinY = sc.MinY;
                            if (sc.MaxX > cMaxX) cMaxX = sc.MaxX;
                            if (sc.MaxY > cMaxY) cMaxY = sc.MaxY;
                            AddItemOnce(processedHandles, items, sc.HandleStr, "Cụm Nan Lam / Móc Tủ Áo",
                                sc.Layer, LayerNoiThat, reason, sc.MidPoint, true, 0.96);
                        }
                        if (cMinX <= cMaxX && cMinY <= cMaxY)
                        {
                            furnitureBoxes.Add(new Extents3d(new Point3d(cMinX, cMinY, 0), new Point3d(cMaxX, cMaxY, 0)));
                        }
                    }
                }
            }
        }

        private static void MarkNarrowFurniturePairs(
            List<Segment2D> segments, List<Extents3d> furnitureBoxes, List<Tuple<Point2d, Point2d>> wallHatchEdges,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            SegmentGrid grid = new SegmentGrid(segments);
            for (int i = 0; i < segments.Count; i++)
            {
                Segment2D sA = segments[i];
                if (sA.IsPaired || sA.EntityMaxDim > 1850.0 || sA.Length < 200.0 || sA.Length > 1800.0) continue;
                // Cấm cướp nét mặt tường (nét có đối tác tường song song 95-260mm hoặc trùng cạnh Hatch tường gạch)
                if (HasStandardWallPartnerOrHatch(sA, i, grid, segments, wallHatchEdges)) continue;

                foreach (int j in grid.Near(sA, 100.0))
                {
                    if (j <= i) continue;
                    Segment2D sB = segments[j];
                    if (sB.IsPaired || sB.EntityMaxDim > 1850.0 || sB.Length < 200.0 || sB.Length > 1800.0) continue;

                    if (Math.Abs(sA.Dir.X * sB.Dir.X + sA.Dir.Y * sB.Dir.Y) < 0.995) continue;
                    if (HasStandardWallPartnerOrHatch(sB, j, grid, segments, wallHatchEdges)) continue;

                    Vector2d vAB1 = new Vector2d(sB.P1.X - sA.P1.X, sB.P1.Y - sA.P1.Y);
                    Vector2d vAB2 = new Vector2d(sB.P2.X - sA.P1.X, sB.P2.Y - sA.P1.Y);
                    double dist = Math.Abs(vAB1.X * sA.Norm.X + vAB1.Y * sA.Norm.Y);

                    if (dist >= 18.0 && dist <= 85.0)
                    {
                        double proj1 = vAB1.X * sA.Dir.X + vAB1.Y * sA.Dir.Y;
                        double proj2 = vAB2.X * sA.Dir.X + vAB2.Y * sA.Dir.Y;
                        double overlap = Math.Min(sA.Length, Math.Max(proj1, proj2)) - Math.Max(0.0, Math.Min(proj1, proj2));
                        double minLen = Math.Min(sA.Length, sB.Length);
                        if (overlap >= Math.Max(180.0, minLen * 0.70))
                        {
                            sA.IsPaired = true;
                            sB.IsPaired = true;
                            sA.IsFurnitureSeg = true;
                            sB.IsFurnitureSeg = true;
                            string r = string.Format("Cặp nét song song mảnh d={0:F0}mm (L={1:F0}mm: TV treo tường, kệ bàn, móc áo) -> AI_XD_NOI_THAT", dist, Math.Max(sA.Length, sB.Length));
                            AddItemOnce(processedHandles, items, sA.HandleStr, "Thiết Bị Mảnh / TV / Kệ", sA.Layer, LayerNoiThat, r, sA.MidPoint, true, 0.95);
                            AddItemOnce(processedHandles, items, sB.HandleStr, "Thiết Bị Mảnh / TV / Kệ", sB.Layer, LayerNoiThat, r, sB.MidPoint, true, 0.95);

                            double bMinX = Math.Min(sA.MinX, sB.MinX), bMaxX = Math.Max(sA.MaxX, sB.MaxX);
                            double bMinY = Math.Min(sA.MinY, sB.MinY), bMaxY = Math.Max(sA.MaxY, sB.MaxY);
                            furnitureBoxes.Add(new Extents3d(new Point3d(bMinX, bMinY, 0), new Point3d(bMaxX, bMaxY, 0)));
                            break;
                        }
                    }
                }
            }
        }

        private static bool SegmentAlignsWithHatchEdge(Point2d p1, Point2d p2, List<Tuple<Point2d, Point2d>> hatchEdges)
        {
            if (hatchEdges == null || hatchEdges.Count == 0) return false;
            Vector2d d1 = p2 - p1;
            double l1 = d1.Length;
            if (l1 < 50.0) return false;
            Vector2d u1 = d1 / l1;
            Vector2d n1 = new Vector2d(-u1.Y, u1.X);

            foreach (var he in hatchEdges)
            {
                Vector2d d2 = he.Item2 - he.Item1;
                double l2 = d2.Length;
                if (l2 < 50.0) continue;
                Vector2d u2 = d2 / l2;
                if (Math.Abs(u1.X * u2.X + u1.Y * u2.Y) < 0.992) continue;

                Vector2d v1 = he.Item1 - p1;
                double perp = Math.Abs(v1.X * n1.X + v1.Y * n1.Y);
                if (perp <= 40.0)
                {
                    double pr1 = v1.X * u1.X + v1.Y * u1.Y;
                    double pr2 = (he.Item2 - p1).X * u1.X + (he.Item2 - p1).Y * u1.Y;
                    double ov = Math.Min(l1, Math.Max(pr1, pr2)) - Math.Max(0.0, Math.Min(pr1, pr2));
                    if (ov >= Math.Min(120.0, Math.Min(l1, l2) * 0.50)) return true;
                }
            }
            return false;
        }

        private static bool DoesBoxAlignWithHatchEdges(Extents3d box, List<Tuple<Point2d, Point2d>> hatchEdges)
        {
            if (hatchEdges == null || hatchEdges.Count == 0) return false;
            Point2d pBL = new Point2d(box.MinPoint.X, box.MinPoint.Y);
            Point2d pBR = new Point2d(box.MaxPoint.X, box.MinPoint.Y);
            Point2d pTR = new Point2d(box.MaxPoint.X, box.MaxPoint.Y);
            Point2d pTL = new Point2d(box.MinPoint.X, box.MaxPoint.Y);
            return SegmentAlignsWithHatchEdge(pBL, pBR, hatchEdges) ||
                   SegmentAlignsWithHatchEdge(pBR, pTR, hatchEdges) ||
                   SegmentAlignsWithHatchEdge(pTR, pTL, hatchEdges) ||
                   SegmentAlignsWithHatchEdge(pTL, pBL, hatchEdges);
        }

        private static bool HasStandardWallPartnerOrHatch(
            Segment2D seg, int selfIdx, SegmentGrid grid, List<Segment2D> segments, List<Tuple<Point2d, Point2d>> wallHatchEdges)
        {
            if (SegmentAlignsWithHatchEdge(seg.P1, seg.P2, wallHatchEdges)) return true;

            foreach (int k in grid.Near(seg, 280.0))
            {
                if (k == selfIdx) continue;
                Segment2D cand = segments[k];
                if (cand.Length < 180.0) continue;
                if (Math.Abs(seg.Dir.X * cand.Dir.X + seg.Dir.Y * cand.Dir.Y) < 0.998) continue;

                Vector2d v1 = new Vector2d(cand.P1.X - seg.P1.X, cand.P1.Y - seg.P1.Y);
                Vector2d v2 = new Vector2d(cand.P2.X - seg.P1.X, cand.P2.Y - seg.P1.Y);
                double dist = Math.Abs((v1.X * seg.Norm.X + v1.Y * seg.Norm.Y + v2.X * seg.Norm.X + v2.Y * seg.Norm.Y) / 2.0);
                if (dist >= 95.0 && dist <= 260.0)
                {
                    double p1 = v1.X * seg.Dir.X + v1.Y * seg.Dir.Y;
                    double p2 = v2.X * seg.Dir.X + v2.Y * seg.Dir.Y;
                    double ov = Math.Min(seg.Length, Math.Max(p1, p2)) - Math.Max(0.0, Math.Min(p1, p2));
                    if (ov >= 150.0 && ov / Math.Min(seg.Length, cand.Length) >= 0.45)
                        return true;
                }
            }
            return false;
        }

        private static void MarkStaircaseSegments(
            List<Segment2D> segments, List<Extents3d> furnitureBoxes,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            int n = segments.Count;
            if (n < 3) return;
            SegmentGrid grid = new SegmentGrid(segments);
            List<Extents3d> stairBoxes = new List<Extents3d>();

            for (int i = 0; i < n; i++)
            {
                Segment2D sA = segments[i];
                if (sA.IsPaired || sA.IsGridLinetype || sA.Length < 450.0 || sA.Length > 1900.0) continue;

                Point2d midA = new Point2d(sA.MidPoint.X, sA.MidPoint.Y);
                List<int> cluster = new List<int> { i };
                List<double> offsets = new List<double> { 0.0 };

                foreach (int j in grid.Near(sA, 3800.0))
                {
                    if (i == j) continue;
                    Segment2D sB = segments[j];
                    if (sB.IsPaired || sB.IsGridLinetype) continue;
                    if (Math.Abs(sB.Length - sA.Length) > Math.Max(100.0, sA.Length * 0.25)) continue;
                    if (Math.Abs(sA.Dir.X * sB.Dir.X + sA.Dir.Y * sB.Dir.Y) < 0.995) continue;

                    Vector2d vMid = new Point2d(sB.MidPoint.X, sB.MidPoint.Y) - midA;
                    double along = Math.Abs(vMid.X * sA.Dir.X + vMid.Y * sA.Dir.Y);
                    if (along > 220.0) continue;

                    double off = vMid.X * sA.Norm.X + vMid.Y * sA.Norm.Y;
                    if (Math.Abs(off) >= 180.0 && Math.Abs(off) <= 3600.0)
                    {
                        if (!offsets.Any(o => Math.Abs(o - off) < 20.0)) offsets.Add(off);
                        cluster.Add(j);
                    }
                }

                if (offsets.Count >= 4)
                {
                    offsets.Sort();
                    int uniformSteps = 0;
                    for (int k = 1; k < offsets.Count; k++)
                    {
                        double step = offsets[k] - offsets[k - 1];
                        if (step >= 180.0 && step <= 360.0) uniformSteps++;
                    }

                    if (uniformSteps >= 3)
                    {
                        double bMinX = double.MaxValue, bMinY = double.MaxValue, bMaxX = double.MinValue, bMaxY = double.MinValue;
                        string reason = string.Format("Cụm {0} bậc cầu thang song song đều (bước 180-360mm) -> AI_XD_NOI_THAT", offsets.Count);
                        foreach (int idx in cluster)
                        {
                            Segment2D sc = segments[idx];
                            sc.IsPaired = true;
                            sc.IsFurnitureSeg = true;
                            if (sc.MinX < bMinX) bMinX = sc.MinX;
                            if (sc.MinY < bMinY) bMinY = sc.MinY;
                            if (sc.MaxX > bMaxX) bMaxX = sc.MaxX;
                            if (sc.MaxY > bMaxY) bMaxY = sc.MaxY;
                            AddItemOnce(processedHandles, items, sc.HandleStr, "Cụm Bậc Cầu Thang",
                                sc.Layer, LayerNoiThat, reason, sc.MidPoint, true, 0.95);
                        }
                        if (bMinX <= bMaxX && bMinY <= bMaxY)
                        {
                            Extents3d sb = new Extents3d(new Point3d(bMinX, bMinY, 0), new Point3d(bMaxX, bMaxY, 0));
                            stairBoxes.Add(sb);
                            furnitureBoxes.Add(sb);
                        }
                    }
                }
            }

            // Gom thêm cốn thang / đường dẫn tay vịn / mũi tên chạm cụm cầu thang
            if (stairBoxes.Count > 0)
            {
                foreach (var seg in segments)
                {
                    if (seg.IsPaired || seg.EntityMaxDim > 3800.0) continue;
                    foreach (var sb in stairBoxes)
                    {
                        if (ArchitecturalKnowledgeService.DistanceSegmentToBox2D(seg.P1, seg.P2, sb) <= 35.0)
                        {
                            seg.IsPaired = true;
                            seg.IsFurnitureSeg = true;
                            AddItemOnce(processedHandles, items, seg.HandleStr, "Chi Tiết Cầu Thang (Cốn/Mũi Tên)",
                                seg.Layer, LayerNoiThat, "Nét viền/mũi tên đấu nối cụm bậc cầu thang -> AI_XD_NOI_THAT", seg.MidPoint, true, 0.93);
                            break;
                        }
                    }
                }
            }
        }

        private static void MarkGridAxisSegments(
            List<Segment2D> segments, List<Tuple<Point2d, double>> gridCircles, List<Point2d> shortGridTextPts,
            double bMinX, double bMinY, double bMaxX, double bMaxY,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            bool hasWallBounds = bMinX < bMaxX && bMinY < bMaxY;
            foreach (var seg in segments)
            {
                if (seg.IsPaired || processedHandles.Contains(seg.HandleStr)) continue;

                bool touchesBubble = false;
                foreach (var gc in gridCircles)
                {
                    if (!shortGridTextPts.Any(tp => tp.GetDistanceTo(gc.Item1) <= gc.Item2 + 40.0)) continue;
                    double d1 = seg.P1.GetDistanceTo(gc.Item1) - gc.Item2;
                    double d2 = seg.P2.GetDistanceTo(gc.Item1) - gc.Item2;
                    if (Math.Abs(d1) <= 120.0 || Math.Abs(d2) <= 120.0)
                    {
                        touchesBubble = true;
                        break;
                    }
                }

                bool protrudesOutsideHouse = false;
                if (hasWallBounds && seg.Length >= 4000.0)
                {
                    bool p1Out = seg.P1.X < bMinX - 350.0 || seg.P1.X > bMaxX + 350.0 || seg.P1.Y < bMinY - 350.0 || seg.P1.Y > bMaxY + 350.0;
                    bool p2Out = seg.P2.X < bMinX - 350.0 || seg.P2.X > bMaxX + 350.0 || seg.P2.Y < bMinY - 350.0 || seg.P2.Y > bMaxY + 350.0;
                    if (p1Out || p2Out) protrudesOutsideHouse = true;
                }

                if (seg.IsGridLinetype || touchesBubble || protrudesOutsideHouse || seg.Length >= 5500.0)
                {
                    seg.IsPaired = true;
                    AddItemOnce(processedHandles, items, seg.HandleStr, "Đường Trục Grid (Định Vị)", seg.Layer, LayerGrid,
                        string.Format("Đường đơn không có cặp tường đối ứng (L={0:F0}mm{1}) -> AI_XD_GRID",
                            seg.Length, seg.IsGridLinetype ? ", nét tâm trục" : (touchesBubble ? ", nối bong bóng trục" : ", trục vượt khung")),
                        seg.MidPoint, true, 0.95);
                }
            }
        }

        private static void ClassifyGridBubblesAndUnknownShapes(
            List<UnknownShapeInfo> otherShapes, List<Point2d> shortGridTextPts,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            foreach (var shp in otherShapes)
            {
                if (processedHandles.Contains(shp.HandleStr)) continue;
                Point2d c2 = new Point2d(shp.Center.X, shp.Center.Y);
                if (shp.SuggestedLayer == LayerGrid && shortGridTextPts.Any(tp => tp.GetDistanceTo(c2) <= shp.Radius + 40.0))
                {
                    AddItemOnce(processedHandles, items, shp.HandleStr, "Vòng Tròn Bong Bóng Trục", shp.Layer, LayerGrid,
                        string.Format("Vòng tròn R={0:F0}mm bao quanh nhãn trục -> AI_XD_GRID", shp.Radius), shp.Center, true, 0.96);
                }
                else
                {
                    AddItemOnce(processedHandles, items, shp.HandleStr, shp.EntityType, shp.Layer, shp.SuggestedLayer,
                        shp.Reason, shp.Center, false, 0.72, true);
                }
            }
        }

        private static void ClassifyCurvedWallArcs(
            List<ArcCandidateInfo> otherArcs, List<WallPairInfo> wallPairs, List<Tuple<Point2d, Point2d>> wallHatchEdges,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            if (otherArcs.Count == 0) return;
            for (int i = 0; i < otherArcs.Count; i++)
            {
                var a1 = otherArcs[i];
                if (processedHandles.Contains(a1.HandleStr)) continue;

                bool hasConcentricTwin = false;
                for (int j = 0; j < otherArcs.Count; j++)
                {
                    if (i == j) continue;
                    var a2 = otherArcs[j];
                    if (a1.Center.GetDistanceTo(a2.Center) <= 35.0)
                    {
                        double dr = Math.Abs(a1.Radius - a2.Radius);
                        if (dr >= 95.0 && dr <= 350.0) { hasConcentricTwin = true; break; }
                    }
                }

                bool touchesConfirmedWall = wallPairs.Any(wp => wp.IsConfirmedWall &&
                    (ArchitecturalKnowledgeService.DistancePointToSegment2D(a1.StartPt, wp.Seg1.P1, wp.Seg1.P2) <= 55.0 ||
                     ArchitecturalKnowledgeService.DistancePointToSegment2D(a1.StartPt, wp.Seg2.P1, wp.Seg2.P2) <= 55.0 ||
                     ArchitecturalKnowledgeService.DistancePointToSegment2D(a1.EndPt, wp.Seg1.P1, wp.Seg1.P2) <= 55.0 ||
                     ArchitecturalKnowledgeService.DistancePointToSegment2D(a1.EndPt, wp.Seg2.P1, wp.Seg2.P2) <= 55.0));

                if (hasConcentricTwin || touchesConfirmedWall)
                {
                    AddItemOnce(processedHandles, items, a1.HandleStr, "Cung Tường Cong (Arc)", a1.Layer, LayerTuong,
                        string.Format("Cung tròn R={0:F0}mm {1} -> AI_XD_TUONG",
                            a1.Radius, hasConcentricTwin ? "cặp đồng tâm 95-350mm" : "đấu nối tường xây"),
                        a1.MidPoint, true, 0.92);
                }
                else
                {
                    AddItemOnce(processedHandles, items, a1.HandleStr, string.Format("Cung Cong Mới (R={0:F0}mm)", a1.Radius),
                        a1.Layer, LayerNoiThat,
                        string.Format("Cung Arc R={0:F0}mm không phải cung cửa và chưa khớp tường -> Ngoài vùng tri thức", a1.Radius),
                        a1.MidPoint, false, 0.72, true);
                }
            }
        }

        private static void MarkConfirmedWallEndCapSegments(
            List<Segment2D> segments, List<WallPairInfo> wallPairs,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            if (segments.Count == 0 || wallPairs.Count == 0) return;
            foreach (var seg in segments)
            {
                if (seg.IsPaired || processedHandles.Contains(seg.HandleStr)) continue;
                if (seg.Length < 65.0 || seg.Length > 360.0 || seg.EntityMaxDim > 420.0) continue;

                foreach (var wp in wallPairs)
                {
                    if (!wp.IsConfirmedWall) continue;
                    if (seg.MaxX + 35.0 < wp.MinX || seg.MinX - 35.0 > wp.MaxX ||
                        seg.MaxY + 35.0 < wp.MinY || seg.MinY - 35.0 > wp.MaxY)
                        continue;

                    double d1ToS1 = ArchitecturalKnowledgeService.DistancePointToSegment2D(seg.P1, wp.Seg1.P1, wp.Seg1.P2);
                    double d2ToS2 = ArchitecturalKnowledgeService.DistancePointToSegment2D(seg.P2, wp.Seg2.P1, wp.Seg2.P2);
                    double d1ToS2 = ArchitecturalKnowledgeService.DistancePointToSegment2D(seg.P1, wp.Seg2.P1, wp.Seg2.P2);
                    double d2ToS1 = ArchitecturalKnowledgeService.DistancePointToSegment2D(seg.P2, wp.Seg1.P1, wp.Seg1.P2);

                    if ((d1ToS1 <= 30.0 && d2ToS2 <= 30.0) || (d1ToS2 <= 30.0 && d2ToS1 <= 30.0))
                    {
                        seg.IsPaired = true;
                        AddItemOnce(processedHandles, items, seg.HandleStr, "Đầu Tường Xây (End-Cap)", seg.Layer, LayerTuong,
                            string.Format("Nét bịt đầu tường xây (L={0:F0}mm) nối 2 mặt tường -> AI_XD_TUONG", seg.Length), seg.MidPoint, true, 0.96);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Gom cụm không gian nội thất (Spatial Connected-Component Clustering):
        /// Tự động thu thập các nét chi tiết bên trong hộp nội thất (dấu X/+, ngăn tủ), nắp móc áo 30mm, chân đế TV,
        /// và thanh suốt treo quần áo cắt ngang qua >= 3 móc áo để đưa toàn bộ cụm về AI_XD_NOI_THAT đồng nhất.
        /// </summary>
        private static void PropagateConnectedFurnitureSegments(
            List<Segment2D> segments, List<Extents3d> furnitureBoxes,
            HashSet<string> processedHandles, List<SuspiciousEntityItem> items)
        {
            if (segments.Count == 0) return;
            SegmentGrid grid = new SegmentGrid(segments);

            for (int pass = 0; pass < 2; pass++)
            {
                bool anyAdded = false;
                for (int i = 0; i < segments.Count; i++)
                {
                    Segment2D seg = segments[i];
                    if (seg.IsPaired || seg.EntityMaxDim > 2200.0) continue;

                    // 1. Nét nằm trọn bên trong vùng bao của một Hộp Nội Thất (ví dụ: chữ thập + 3CF1/3CF2 trong hộp 3CF5)
                    bool insideFurnBox = false;
                    foreach (var fBox in furnitureBoxes)
                    {
                        double fw = fBox.MaxPoint.X - fBox.MinPoint.X;
                        double fh = fBox.MaxPoint.Y - fBox.MinPoint.Y;
                        if (seg.Length <= Math.Max(fw, fh) + 40.0 &&
                            seg.P1.X >= fBox.MinPoint.X - 25.0 && seg.P1.X <= fBox.MaxPoint.X + 25.0 &&
                            seg.P1.Y >= fBox.MinPoint.Y - 25.0 && seg.P1.Y <= fBox.MaxPoint.Y + 25.0 &&
                            seg.P2.X >= fBox.MinPoint.X - 25.0 && seg.P2.X <= fBox.MaxPoint.X + 25.0 &&
                            seg.P2.Y >= fBox.MinPoint.Y - 25.0 && seg.P2.Y <= fBox.MaxPoint.Y + 25.0)
                        {
                            insideFurnBox = true;
                            break;
                        }
                    }

                    if (insideFurnBox)
                    {
                        seg.IsPaired = true;
                        seg.IsFurnitureSeg = true;
                        anyAdded = true;
                        AddItemOnce(processedHandles, items, seg.HandleStr, "Chi Tiết Trong Cụm Nội Thất", seg.Layer, LayerNoiThat,
                            "Nét chi tiết nằm trong phạm vi Hộp/Cụm Nội Thất -> Gom cụm AI_XD_NOI_THAT", seg.MidPoint, true, 0.95);
                        continue;
                    }

                    // 2. Nắp đậy ngắn / Chân đế (<= 160mm, ví dụ nắp móc áo 30mm hoặc đầu bịt TV 50mm) chạm trực tiếp vào nét Nội Thất
                    if (seg.Length <= 160.0 && seg.EntityMaxDim <= 160.0)
                    {
                        bool touchesFurnSeg = false;
                        foreach (int j in grid.Near(seg, 50.0))
                        {
                            if (i == j) continue;
                            Segment2D other = segments[j];
                            if (!other.IsFurnitureSeg) continue;
                            if (ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(seg.P1, seg.P2, other.P1, other.P2) <= 25.0)
                            {
                                touchesFurnSeg = true;
                                break;
                            }
                        }

                        if (!touchesFurnSeg)
                        {
                            foreach (var fBox in furnitureBoxes)
                            {
                                if (ArchitecturalKnowledgeService.DistanceSegmentToBox2D(seg.P1, seg.P2, fBox) <= 20.0)
                                {
                                    touchesFurnSeg = true;
                                    break;
                                }
                            }
                        }

                        if (touchesFurnSeg)
                        {
                            seg.IsPaired = true;
                            seg.IsFurnitureSeg = true;
                            anyAdded = true;
                            AddItemOnce(processedHandles, items, seg.HandleStr, "Nắp / Chân Đế Nội Thất", seg.Layer, LayerNoiThat,
                                string.Format("Chi tiết ngắn (L={0:F0}mm) đấu nối trực tiếp cụm Nội Thất -> AI_XD_NOI_THAT", seg.Length), seg.MidPoint, true, 0.95);
                            continue;
                        }
                    }

                    // 3. Thanh suốt treo đồ / Thanh giằng nội thất (L <= 2200mm) cắt ngang qua >= 3 thanh nội thất (ví dụ thanh suốt 45C2 cắt qua 16 móc áo)
                    int touchedFurnCount = 0;
                    foreach (int j in grid.Near(seg, 40.0))
                    {
                        if (i == j) continue;
                        Segment2D other = segments[j];
                        if (!other.IsFurnitureSeg) continue;
                        if (ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(seg.P1, seg.P2, other.P1, other.P2) <= 15.0)
                        {
                            touchedFurnCount++;
                            if (touchedFurnCount >= 3) break;
                        }
                    }

                    if (touchedFurnCount >= 3)
                    {
                        seg.IsPaired = true;
                        seg.IsFurnitureSeg = true;
                        anyAdded = true;
                        AddItemOnce(processedHandles, items, seg.HandleStr, "Thanh Suốt / Khung Cụm Nội Thất", seg.Layer, LayerNoiThat,
                            string.Format("Thanh liên kết (L={0:F0}mm) giao cắt {1}+ chi tiết nội thất -> Gom cụm AI_XD_NOI_THAT", seg.Length, touchedFurnCount), seg.MidPoint, true, 0.96);
                    }
                }
                if (!anyAdded) break;
            }
        }

        private static void FindParallelWallCandidatePairs(List<Segment2D> segments, List<WallPairInfo> wallPairs)
        {
            int segCount = segments.Count;
            SegmentGrid grid = new SegmentGrid(segments);
            HashSet<long> addedPairs = new HashSet<long>();

            for (int i = 0; i < segCount; i++)
            {
                Segment2D sA = segments[i];
                if (sA.IsPaired || sA.IsGridLinetype || sA.Length < 180.0) continue;

                // Tìm bề dày tường chuẩn tốt nhất của sA để cho phép 1 nét tường dài ghép với NHIỀU đoạn tường đối diện cùng bề dày
                double bestSignedDist = 0.0;
                double bestScore = -1.0;
                List<Tuple<int, double, double>> validPartners = new List<Tuple<int, double, double>>();

                foreach (int j in grid.Near(sA, 420.0))
                {
                    if (j == i) continue;
                    Segment2D sB = segments[j];
                    if (sB.IsPaired || sB.IsGridLinetype || sB.Length < 180.0) continue;
                    if (sA.HandleStr == sB.HandleStr && Math.Max(sA.Length, sB.Length) < 500.0) continue;

                    if (Math.Abs(sA.Dir.X * sB.Dir.X + sA.Dir.Y * sB.Dir.Y) < 0.998) continue;

                    Vector2d vAB1 = new Vector2d(sB.P1.X - sA.P1.X, sB.P1.Y - sA.P1.Y);
                    double signed1 = vAB1.X * sA.Norm.X + vAB1.Y * sA.Norm.Y;
                    Vector2d vAB2 = new Vector2d(sB.P2.X - sA.P1.X, sB.P2.Y - sA.P1.Y);
                    double signed2 = vAB2.X * sA.Norm.X + vAB2.Y * sA.Norm.Y;
                    double signedDist = (signed1 + signed2) / 2.0;
                    double dist = Math.Abs(signedDist);

                    if (dist < 95.0 || dist > 400.0) continue;

                    double projB1 = vAB1.X * sA.Dir.X + vAB1.Y * sA.Dir.Y;
                    double projB2 = vAB2.X * sA.Dir.X + vAB2.Y * sA.Dir.Y;
                    double overlap = Math.Min(sA.Length, Math.Max(projB1, projB2)) - Math.Max(0.0, Math.Min(projB1, projB2));

                    if (overlap >= 110.0 || (overlap > 0 && overlap / Math.Min(sA.Length, sB.Length) >= 0.30))
                    {
                        validPartners.Add(Tuple.Create(j, signedDist, overlap));
                        double stdBonus = (Math.Abs(dist - 100.0) <= 15.0 || Math.Abs(dist - 110.0) <= 15.0 ||
                                           Math.Abs(dist - 150.0) <= 15.0 || Math.Abs(dist - 200.0) <= 25.0 ||
                                           Math.Abs(dist - 220.0) <= 20.0) ? 2.0 : 1.0;
                        double score = overlap * stdBonus / Math.Max(dist, 100.0);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSignedDist = signedDist;
                        }
                    }
                }

                if (validPartners.Count == 0) continue;

                // Bắt buộc tổng chiều dài các đoạn tường đối diện (cùng bề dày chuẩn) phải phủ tối thiểu 45% chiều dài sA.
                // Nếu < 45% (ví dụ đường trục 1 nét dài 27.7m vô tình lướt qua 1 đoạn tường ngắn 1m), TUYỆT ĐỐI KHÔNG ghép làm tường!
                double totalSameFaceOverlap = 0.0;
                foreach (var partner in validPartners)
                {
                    double signedDist = partner.Item2;
                    if (signedDist * bestSignedDist > 0 && Math.Abs(Math.Abs(signedDist) - Math.Abs(bestSignedDist)) <= 18.0)
                    {
                        totalSameFaceOverlap += partner.Item3;
                    }
                }
                if (totalSameFaceOverlap / sA.Length < 0.45) continue;

                foreach (var partner in validPartners)
                {
                    int j = partner.Item1;
                    double signedDist = partner.Item2;
                    double ov = partner.Item3;
                    // Chỉ ghép với các đoạn nằm trên cùng mặt tường đối diện (cùng phía và cùng bề dày tường ±18mm)
                    if (signedDist * bestSignedDist < 0 || Math.Abs(Math.Abs(signedDist) - Math.Abs(bestSignedDist)) > 18.0)
                        continue;

                    Segment2D sB = segments[j];
                    // Chặn chiều ngược lại: không cho 1 đoạn ngắn sA kéo theo 1 đường trục dài 1 nét sB (>= 2500mm) vào cặp tường
                    if (sB.Length >= 2500.0 && sA.Length < sB.Length * 0.45 && ov / sB.Length < 0.45)
                        continue;

                    int minIdx = Math.Min(i, j), maxIdx = Math.Max(i, j);
                    long pairKey = ((long)minIdx << 32) ^ (uint)maxIdx;
                    if (addedPairs.Contains(pairKey)) continue;
                    addedPairs.Add(pairKey);

                    wallPairs.Add(new WallPairInfo
                    {
                        Seg1 = sA,
                        Seg2 = sB,
                        Distance = Math.Abs(signedDist),
                        MidlineP1 = new Point2d((sA.P1.X + sB.P1.X) / 2.0, (sA.P1.Y + sB.P1.Y) / 2.0),
                        MidlineP2 = new Point2d((sA.P2.X + sB.P2.X) / 2.0, (sA.P2.Y + sB.P2.Y) / 2.0)
                    });
                }
            }

            foreach (var wp in wallPairs)
            {
                wp.Seg1.IsPaired = true;
                wp.Seg2.IsPaired = true;
            }
        }

        private static void ConfirmWallsByMultiEvidenceGraph(
            List<WallPairInfo> wallPairs, List<Extents3d> columnAnchors, List<Tuple<Point2d, Point2d>> wallHatchEdges,
            List<RectCandidate> candidateColumns, List<DoorClusterInfo> doorClusters, List<Extents3d> doorZoneBoxes)
        {
            if (wallPairs.Count == 0) return;

            // Nếu bản vẽ là Layer 0 thô chưa có Solid/Hatch cột hay Hatch tường, nâng cấp ứng viên chữ nhật nối với cặp tường dài >= 600mm
            if (columnAnchors.Count == 0 && (wallHatchEdges == null || wallHatchEdges.Count == 0) && candidateColumns.Count > 0)
            {
                foreach (var col in candidateColumns)
                {
                    if (col.Width < 180.0 || col.Height < 180.0 || Math.Min(col.Width, col.Height) > 450.0) continue;
                    bool touchesLongPair = wallPairs.Any(wp =>
                        Math.Max(wp.Seg1.Length, wp.Seg2.Length) >= 750.0 &&
                        (ArchitecturalKnowledgeService.DistanceSegmentToBox2D(wp.Seg1.P1, wp.Seg1.P2, col.Extents) <= 105.0 ||
                         ArchitecturalKnowledgeService.DistanceSegmentToBox2D(wp.Seg2.P1, wp.Seg2.P2, col.Extents) <= 105.0));
                    if (touchesLongPair)
                    {
                        col.HasAnchorOverlap = true;
                        columnAnchors.Add(col.Extents);
                    }
                }
            }

            const double TouchTol = 105.0;
            Queue<int> queue = new Queue<int>();

            // 1. Hạt giống Đa Chứng Cứ (Multi-Evidence Seeds — hoạt động chuẩn xác trên cả Chung cư & Nhà dân):
            //    (a) Tiếp giáp Mỏ neo Cột kết cấu
            //    (b) Cạnh tường trùng biên đa giác Hatch tường xây (ANSI31 / Brick / SOLID dải tường)
            //    (c) Tường chính dài >= 1500mm (95..350mm) thuộc khung bao/vách ngăn công trình
            //    (d) Đỡ/kẹp ngay cạnh Cửa đi (95..260mm)
            for (int i = 0; i < wallPairs.Count; i++)
            {
                WallPairInfo wp = wallPairs[i];
                bool isSeed = false;

                foreach (var anchor in columnAnchors)
                {
                    if (wp.MaxX + TouchTol < anchor.MinPoint.X || wp.MinX - TouchTol > anchor.MaxPoint.X ||
                        wp.MaxY + TouchTol < anchor.MinPoint.Y || wp.MinY - TouchTol > anchor.MaxPoint.Y)
                        continue;

                    if (ArchitecturalKnowledgeService.DistanceSegmentToBox2D(wp.Seg1.P1, wp.Seg1.P2, anchor) <= TouchTol ||
                        ArchitecturalKnowledgeService.DistanceSegmentToBox2D(wp.Seg2.P1, wp.Seg2.P2, anchor) <= TouchTol)
                    {
                        isSeed = true;
                        break;
                    }
                }

                if (!isSeed && wallHatchEdges != null && wallHatchEdges.Count > 0 && wp.Distance >= 95.0 && wp.Distance <= 360.0)
                {
                    if (SegmentAlignsWithHatchEdge(wp.Seg1.P1, wp.Seg1.P2, wallHatchEdges) ||
                        SegmentAlignsWithHatchEdge(wp.Seg2.P1, wp.Seg2.P2, wallHatchEdges))
                    {
                        isSeed = true;
                    }
                }

                if (!isSeed && wp.Distance >= 95.0 && wp.Distance <= 350.0 && Math.Max(wp.Seg1.Length, wp.Seg2.Length) >= 1500.0)
                {
                    isSeed = true;
                }

                if (!isSeed && wp.Distance >= 95.0 && wp.Distance <= 260.0)
                {
                    bool touchesDoor = doorClusters.Any(dc =>
                        ArchitecturalKnowledgeService.DistancePointToSegment2D(dc.Center, wp.MidlineP1, wp.MidlineP2) <= 180.0) ||
                        doorZoneBoxes.Any(dbx =>
                            ArchitecturalKnowledgeService.DistanceSegmentToBox2D(wp.Seg1.P1, wp.Seg1.P2, dbx) <= 60.0 ||
                            ArchitecturalKnowledgeService.DistanceSegmentToBox2D(wp.Seg2.P1, wp.Seg2.P2, dbx) <= 60.0);
                    if (touchesDoor) isSeed = true;
                }

                if (isSeed)
                {
                    wp.IsDirectColumnTouch = true;
                    queue.Enqueue(i);
                }
            }

            // 2. Lan truyền có kiểm soát:
            //    a) Cùng một nét tường liên tục (chia sẻ Seg1/Seg2) hoặc nối thẳng trục qua ô cửa.
            //    b) Góc tường chữ L / chữ T cùng bề dày chuẩn (|cur.Distance - next.Distance| <= 25mm, d=95..260mm):
            //       bắt trọn cả các đoạn mỏ tường chữ L ngắn (180..800mm) như mỏ tường 410x1800mm đỡ cửa phòng vệ sinh!
            //    c) Tường ngăn phòng khác bề dày (d=95..260mm, L>=400mm) đấu nối cả 2 đầu vào khung Tường/Cột/Cửa.
            while (queue.Count > 0)
            {
                int curIdx = queue.Dequeue();
                WallPairInfo cur = wallPairs[curIdx];

                for (int j = 0; j < wallPairs.Count; j++)
                {
                    WallPairInfo next = wallPairs[j];
                    if (next.IsConfirmedWall) continue;

                    bool connected = false;

                    // a1. Chia sẻ chung một cạnh tường dài đã chạm cột
                    if (ReferenceEquals(cur.Seg1, next.Seg1) || ReferenceEquals(cur.Seg1, next.Seg2) ||
                        ReferenceEquals(cur.Seg2, next.Seg1) || ReferenceEquals(cur.Seg2, next.Seg2))
                    {
                        connected = true;
                    }

                    // a2. Nối thẳng hàng trên cùng trục tường (qua ô cửa hoặc nối đầu trực tiếp)
                    if (!connected &&
                        Math.Abs(cur.Seg1.Dir.X * next.Seg1.Dir.X + cur.Seg1.Dir.Y * next.Seg1.Dir.Y) >= 0.995 &&
                        Math.Abs(cur.Distance - next.Distance) <= 35.0)
                    {
                        double perpDiff = Math.Abs((next.MidlineP1 - cur.MidlineP1).X * cur.Seg1.Norm.X + (next.MidlineP1 - cur.MidlineP1).Y * cur.Seg1.Norm.Y);
                        if (perpDiff <= 60.0)
                        {
                            double gap = ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(cur.MidlineP1, cur.MidlineP2, next.MidlineP1, next.MidlineP2);
                            if (gap <= TouchTol)
                            {
                                connected = true;
                            }
                            else if (gap <= 1650.0)
                            {
                                Point2d midCur = new Point2d((cur.MidlineP1.X + cur.MidlineP2.X) / 2.0, (cur.MidlineP1.Y + cur.MidlineP2.Y) / 2.0);
                                Point2d midNext = new Point2d((next.MidlineP1.X + next.MidlineP2.X) / 2.0, (next.MidlineP1.Y + next.MidlineP2.Y) / 2.0);
                                double bx = (midCur.X + midNext.X) / 2.0;
                                double by = (midCur.Y + midNext.Y) / 2.0;
                                bool hasDoorBetween = doorClusters.Any(dc => Math.Sqrt(Math.Pow(dc.Center.X - bx, 2) + Math.Pow(dc.Center.Y - by, 2)) <= 1400.0) ||
                                                      doorZoneBoxes.Any(dbx => bx >= dbx.MinPoint.X - 600 && bx <= dbx.MaxPoint.X + 600 &&
                                                                               by >= dbx.MinPoint.Y - 600 && by <= dbx.MaxPoint.Y + 600);
                                if (hasDoorBetween) connected = true;
                            }
                        }
                    }

                    // b1. Lan truyền góc tường chữ L / chữ T cùng bề dày chuẩn (d = 95..260mm, lệch bề dày <= 25mm):
                    // Cho phép cả tường dài (1800mm) và mỏ tường chữ L ngắn (180..800mm, ví dụ 410mm) truyền qua nhau khi 2 mặt tường khép góc (<= 55mm).
                    if (!connected && next.Distance >= 95.0 && next.Distance <= 260.0 && Math.Abs(cur.Distance - next.Distance) <= 25.0)
                    {
                        if (!(cur.MaxX + TouchTol < next.MinX || cur.MinX - TouchTol > next.MaxX ||
                              cur.MaxY + TouchTol < next.MinY || cur.MinY - TouchTol > next.MaxY))
                        {
                            double d11 = ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(cur.Seg1.P1, cur.Seg1.P2, next.Seg1.P1, next.Seg1.P2);
                            double d12 = ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(cur.Seg1.P1, cur.Seg1.P2, next.Seg2.P1, next.Seg2.P2);
                            double d21 = ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(cur.Seg2.P1, cur.Seg2.P2, next.Seg1.P1, next.Seg1.P2);
                            double d22 = ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(cur.Seg2.P1, cur.Seg2.P2, next.Seg2.P1, next.Seg2.P2);
                            double minCornerDist = Math.Min(Math.Min(d11, d12), Math.Min(d21, d22));
                            if (minCornerDist <= 55.0)
                            {
                                connected = true;
                            }
                        }
                    }

                    // b2. Tường ngăn phòng khác bề dày (dài >= 400mm, dày chuẩn 95..260mm): Cả 2 đầu mút đều tựa vào Tường/Cột/Cửa
                    if (!connected && Math.Max(next.Seg1.Length, next.Seg2.Length) >= 400.0 && next.Distance >= 95.0 && next.Distance <= 260.0)
                    {
                        if (!(cur.MaxX + TouchTol < next.MinX || cur.MinX - TouchTol > next.MaxX ||
                              cur.MaxY + TouchTol < next.MinY || cur.MinY - TouchTol > next.MaxY))
                        {
                            bool end1Anchored = IsEndpointAnchoredToStructure(next.MidlineP1, wallPairs, columnAnchors, doorClusters, doorZoneBoxes, j);
                            bool end2Anchored = IsEndpointAnchoredToStructure(next.MidlineP2, wallPairs, columnAnchors, doorClusters, doorZoneBoxes, j);
                            if (end1Anchored && end2Anchored)
                            {
                                connected = true;
                            }
                        }
                    }

                    if (connected)
                    {
                        next.IsPropagatedWall = true;
                        queue.Enqueue(j);
                    }
                }
            }
        }

        private static bool IsEndpointAnchoredToStructure(
            Point2d pt, List<WallPairInfo> wallPairs, List<Extents3d> columnAnchors,
            List<DoorClusterInfo> doorClusters, List<Extents3d> doorZoneBoxes, int selfIdx)
        {
            const double Tol = 160.0;
            foreach (var col in columnAnchors)
            {
                if (pt.X >= col.MinPoint.X - Tol && pt.X <= col.MaxPoint.X + Tol &&
                    pt.Y >= col.MinPoint.Y - Tol && pt.Y <= col.MaxPoint.Y + Tol)
                    return true;
            }
            foreach (var dc in doorClusters)
            {
                if (pt.GetDistanceTo(dc.Center) <= dc.Radius + Tol) return true;
            }
            foreach (var dbx in doorZoneBoxes)
            {
                if (pt.X >= dbx.MinPoint.X - Tol && pt.X <= dbx.MaxPoint.X + Tol &&
                    pt.Y >= dbx.MinPoint.Y - Tol && pt.Y <= dbx.MaxPoint.Y + Tol)
                    return true;
            }
            for (int k = 0; k < wallPairs.Count; k++)
            {
                if (k == selfIdx) continue;
                WallPairInfo wp = wallPairs[k];
                if (!wp.IsConfirmedWall) continue;
                if (pt.X < wp.MinX - Tol || pt.X > wp.MaxX + Tol || pt.Y < wp.MinY - Tol || pt.Y > wp.MaxY + Tol) continue;
                if (ArchitecturalKnowledgeService.DistancePointToSegment2D(pt, wp.Seg1.P1, wp.Seg1.P2) <= Tol ||
                    ArchitecturalKnowledgeService.DistancePointToSegment2D(pt, wp.Seg2.P1, wp.Seg2.P2) <= Tol)
                    return true;
            }
            return false;
        }

        public static int ApplySuspiciousClassifications(Document doc, List<SuspiciousEntityItem> items)
        {
            if (items == null || items.Count == 0) return 0;
            var selectedItems = items.Where(x => x.IsSelected && !string.IsNullOrEmpty(x.SuggestedLayer) && !x.SuggestedLayer.StartsWith("--")).ToList();
            if (selectedItems.Count == 0) return 0;

            Database db = doc.Database;
            int count = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureStandardLayers(db, tr);

                foreach (var item in selectedItems)
                {
                    long handleVal;
                    if (long.TryParse(item.HandleStr, System.Globalization.NumberStyles.HexNumber, null, out handleVal))
                    {
                        Handle h = new Handle(handleVal);
                        ObjectId objId;
                        if (db.TryGetObjectId(h, out objId))
                        {
                            Entity ent = tr.GetObject(objId, OpenMode.ForWrite) as Entity;
                            if (ent != null)
                            {
                                ent.Layer = item.SuggestedLayer;
                                item.CurrentLayer = item.SuggestedLayer;
                                item.IsFixed = true;
                                count++;
                            }
                        }
                    }
                }

                tr.Commit();
            }

            return count;
        }

        private static ObjectId _lastHighlightedId = ObjectId.Null;

        /// <summary>
        /// Hủy highlight và hủy toàn bộ lệnh chọn đối tượng trong CAD
        /// </summary>
        public static void UnhighlightAll(Document doc)
        {
            if (doc == null) return;
            try
            {
                if (_lastHighlightedId != ObjectId.Null && !_lastHighlightedId.IsErased)
                {
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Entity prev = tr.GetObject(_lastHighlightedId, OpenMode.ForRead) as Entity;
                        if (prev != null) prev.Unhighlight();
                        tr.Commit();
                    }
                }
            }
            catch { }
            _lastHighlightedId = ObjectId.Null;

            try
            {
                doc.Editor.SetImpliedSelection(new ObjectId[0]);
            }
            catch { }
        }

        /// <summary>
        /// Zoom màn hình CAD tới đối tượng cụ thể và hủy ngay lệnh chọn đối tượng
        /// </summary>
        public static void ZoomToEntity(Document doc, string handleStr)
        {
            if (string.IsNullOrEmpty(handleStr) || doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                long handleVal;
                if (long.TryParse(handleStr, System.Globalization.NumberStyles.HexNumber, null, out handleVal))
                {
                    Handle h = new Handle(handleVal);
                    ObjectId objId;
                    if (db.TryGetObjectId(h, out objId))
                    {
                        Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                        if (ent != null)
                        {
                            Extents3d ext = ent.GeometricExtents;
                            Point3d min = ext.MinPoint;
                            Point3d max = ext.MaxPoint;
                            double buffer = 500.0;

                            using (ViewTableRecord view = ed.GetCurrentView())
                            {
                                view.CenterPoint = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
                                view.Height = Math.Max(max.Y - min.Y + buffer * 2, 1000.0);
                                view.Width = Math.Max(max.X - min.X + buffer * 2, 1000.0);
                                ed.SetCurrentView(view);
                            }

                            // Hủy highlight đối tượng trước đó
                            if (_lastHighlightedId != ObjectId.Null && !_lastHighlightedId.IsErased && _lastHighlightedId != objId)
                            {
                                try
                                {
                                    Entity prev = tr.GetObject(_lastHighlightedId, OpenMode.ForRead) as Entity;
                                    if (prev != null) prev.Unhighlight();
                                }
                                catch { }
                            }

                            // Highlight đối tượng mới
                            ent.Highlight();
                            _lastHighlightedId = objId;

                            // HỦY NGAY LỆNH CHỌN ĐỐI TƯỢNG TRONG CAD (Clear Implied Selection & Grips)
                            try
                            {
                                ed.SetImpliedSelection(new ObjectId[0]);
                            }
                            catch { }
                        }
                    }
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Đóng gói các nét cửa rời rạc thành Block trên layer AI_XD_CUA
        /// </summary>
        public static int PackageDoorBlocks(Document doc, Extents3d? bounds)
        {
            Database db = doc.Database;
            List<ObjectId> doorEntIds = new List<ObjectId>();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent is BlockReference) continue;

                    if (string.Equals(ent.Layer, LayerCua, StringComparison.OrdinalIgnoreCase))
                    {
                        if (bounds.HasValue)
                        {
                            try
                            {
                                Extents3d e = ent.GeometricExtents;
                                if (e.MaxPoint.X < bounds.Value.MinPoint.X || e.MinPoint.X > bounds.Value.MaxPoint.X ||
                                    e.MaxPoint.Y < bounds.Value.MinPoint.Y || e.MinPoint.Y > bounds.Value.MaxPoint.Y)
                                    continue;
                            }
                            catch { }
                        }
                        doorEntIds.Add(id);
                    }
                }
                tr.Commit();
            }

            if (doorEntIds.Count == 0) return 0;

            List<List<ObjectId>> clusters = new List<List<ObjectId>>();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<Tuple<ObjectId, Extents3d>> items = new List<Tuple<ObjectId, Extents3d>>();
                foreach (var id in doorEntIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        try { items.Add(Tuple.Create(id, ent.GeometricExtents)); } catch { }
                    }
                }

                while (items.Count > 0)
                {
                    var first = items[0];
                    items.RemoveAt(0);
                    List<ObjectId> cluster = new List<ObjectId> { first.Item1 };
                    Extents3d curBox = first.Item2;

                    bool expanded = true;
                    while (expanded)
                    {
                        expanded = false;
                        for (int i = items.Count - 1; i >= 0; i--)
                        {
                            var candidate = items[i];
                            double dist = Math.Sqrt(Math.Pow((curBox.MinPoint.X + curBox.MaxPoint.X)/2.0 - (candidate.Item2.MinPoint.X + candidate.Item2.MaxPoint.X)/2.0, 2) +
                                                    Math.Pow((curBox.MinPoint.Y + curBox.MaxPoint.Y)/2.0 - (candidate.Item2.MinPoint.Y + candidate.Item2.MaxPoint.Y)/2.0, 2));

                            if (dist < 1200.0)
                            {
                                cluster.Add(candidate.Item1);
                                curBox.AddExtents(candidate.Item2);
                                items.RemoveAt(i);
                                expanded = true;
                            }
                        }
                    }
                    clusters.Add(cluster);
                }
                tr.Commit();
            }

            int blockCount = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (var cluster in clusters)
                {
                    if (cluster.Count == 0) continue;
                    blockCount++;
                    string blkName = string.Format("AI_DOOR_{0:mmss}_{1:D3}", DateTime.Now, blockCount);

                    BlockTableRecord btrNew = new BlockTableRecord { Name = blkName };
                    Point3d origin = Point3d.Origin;

                    try
                    {
                        Entity firstEnt = tr.GetObject(cluster[0], OpenMode.ForRead) as Entity;
                        origin = firstEnt.GeometricExtents.MinPoint;
                    }
                    catch { }

                    btrNew.Origin = origin;
                    bt.Add(btrNew);
                    tr.AddNewlyCreatedDBObject(btrNew, true);

                    foreach (var id in cluster)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null)
                        {
                            Entity clone = (Entity)ent.Clone();
                            clone.Layer = "0";
                            btrNew.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                            ent.Erase();
                        }
                    }

                    BlockReference br = new BlockReference(origin, btrNew.ObjectId)
                    {
                        Layer = LayerCua
                    };
                    ms.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                }

                tr.Commit();
            }

            return blockCount;
        }

        public static int ChangeEntitiesLayer(Document doc, IEnumerable<ObjectId> entityIds, string targetLayer)
        {
            if (doc == null || entityIds == null || string.IsNullOrEmpty(targetLayer)) return 0;
            Database db = doc.Database;
            int count = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureStandardLayers(db, tr);

                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(targetLayer))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord { Name = targetLayer };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }

                foreach (ObjectId id in entityIds)
                {
                    if (id.IsNull || id.IsErased) continue;
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent != null)
                    {
                        ent.Layer = targetLayer;
                        count++;
                    }
                }

                tr.Commit();
            }

            return count;
        }

        public static ObjectId PackageSelectedEntitiesToBlock(Document doc, IEnumerable<ObjectId> selectedIds, string targetLayer, string blockName, Point3d? customBasePoint = null)
        {
            if (doc == null || selectedIds == null) return ObjectId.Null;
            Database db = doc.Database;
            ObjectId newBlockRefId = ObjectId.Null;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureStandardLayers(db, tr);

                List<ObjectId> validIds = new List<ObjectId>();
                Point3d minPt = new Point3d(double.MaxValue, double.MaxValue, double.MaxValue);
                Point3d maxPt = new Point3d(double.MinValue, double.MinValue, double.MinValue);
                bool hasBounds = false;

                foreach (ObjectId id in selectedIds)
                {
                    if (id.IsNull || id.IsErased) continue;
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        validIds.Add(id);
                        try
                        {
                            Extents3d box = ent.GeometricExtents;
                            if (box.MinPoint.X < minPt.X) minPt = new Point3d(box.MinPoint.X, minPt.Y, minPt.Z);
                            if (box.MinPoint.Y < minPt.Y) minPt = new Point3d(minPt.X, box.MinPoint.Y, minPt.Z);
                            if (box.MaxPoint.X > maxPt.X) maxPt = new Point3d(box.MaxPoint.X, maxPt.Y, maxPt.Z);
                            if (box.MaxPoint.Y > maxPt.Y) maxPt = new Point3d(maxPt.X, box.MaxPoint.Y, maxPt.Z);
                            hasBounds = true;
                        }
                        catch { }
                    }
                }

                if (validIds.Count == 0) return ObjectId.Null;

                // Điểm Pickpoint (Base Point): Đặt ở góc dưới trái của bounding box
                Point3d origin = customBasePoint.HasValue
                    ? customBasePoint.Value
                    : (hasBounds ? new Point3d(minPt.X, minPt.Y, 0) : Point3d.Origin);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                if (string.IsNullOrEmpty(blockName))
                {
                    blockName = string.Format("AI_BLOCK_{0:yyyyMMdd_HHmmss}", DateTime.Now);
                }

                string uniqueName = blockName;
                int suffix = 1;
                while (bt.Has(uniqueName))
                {
                    uniqueName = string.Format("{0}_{1:D2}", blockName, suffix++);
                }

                BlockTableRecord btrNew = new BlockTableRecord { Name = uniqueName, Origin = origin };
                bt.Add(btrNew);
                tr.AddNewlyCreatedDBObject(btrNew, true);

                foreach (ObjectId id in validIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent != null)
                    {
                        Entity clone = (Entity)ent.Clone();
                        clone.Layer = "0";
                        btrNew.AppendEntity(clone);
                        tr.AddNewlyCreatedDBObject(clone, true);
                        ent.Erase();
                    }
                }

                BlockReference br = new BlockReference(origin, btrNew.ObjectId);
                if (!string.IsNullOrEmpty(targetLayer))
                {
                    br.Layer = targetLayer;
                }
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                newBlockRefId = br.ObjectId;

                tr.Commit();
            }

            return newBlockRefId;
        }

        /// <summary>
        /// Rải nhãn MText tự động vào tâm thông thủy an toàn của các phòng (Overload doc)
        /// </summary>
        public static int AutoPlaceRoomTexts(Document doc)
        {
            return AutoPlaceRoomTexts(doc, null, null, null);
        }

        public static int AutoPlaceRoomTexts(Document doc, List<CoPilotRoomItem> rooms)
        {
            return AutoPlaceRoomTexts(doc, rooms, null, null);
        }

        public static string InferRoomNameFromCad(Database db, Transaction tr, Polyline pl, double areaM2)
        {
            if (pl == null) return "PHÒNG CHỨC NĂNG";

            if (pl.XData != null)
            {
                TypedValue[] tvs = pl.XData.AsArray();
                if (tvs != null && tvs.Length >= 4)
                {
                    string xName = tvs[2].Value != null ? tvs[2].Value.ToString() : "";
                    if (!string.IsNullOrEmpty(xName) && !xName.StartsWith("PH-", StringComparison.OrdinalIgnoreCase) && !xName.StartsWith("P-", StringComparison.OrdinalIgnoreCase))
                    {
                        return xName;
                    }
                }
            }

            Extents3d ext = pl.GeometricExtents;
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            bool hasStairs = false;
            bool hasWcFixtures = false;
            bool hasBed = false;
            bool hasKitchen = false;
            string foundCadText = null;
            int stepLines = 0;

            foreach (ObjectId id in ms)
            {
                if (id == pl.ObjectId) continue;
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                Extents3d? entExt = null;
                try { entExt = ent.GeometricExtents; } catch { }
                if (!entExt.HasValue) continue;

                double midX = (entExt.Value.MinPoint.X + entExt.Value.MaxPoint.X) / 2.0;
                double midY = (entExt.Value.MinPoint.Y + entExt.Value.MaxPoint.Y) / 2.0;
                if (midX < ext.MinPoint.X || midX > ext.MaxPoint.X || midY < ext.MinPoint.Y || midY > ext.MaxPoint.Y)
                    continue;

                DBText dt = ent as DBText;
                if (dt != null && !string.IsNullOrEmpty(dt.TextString))
                {
                    string s = dt.TextString.Trim();
                    if (IsArchitecturalRoomName(s)) { foundCadText = s; break; }
                }
                MText mt = ent as MText;
                if (mt != null && !string.IsNullOrEmpty(mt.Contents) && !string.Equals(mt.Layer, LayerDuongBaoText, StringComparison.OrdinalIgnoreCase))
                {
                    string s = mt.Contents.Trim();
                    if (IsArchitecturalRoomName(s)) { foundCadText = s; break; }
                }

                BlockReference br = ent as BlockReference;
                if (br != null)
                {
                    string bName = (br.Name ?? "").ToUpperInvariant();
                    if (bName.Contains("STAIR") || bName.Contains("THANG") || bName.Contains("BAC")) hasStairs = true;
                    if (bName.Contains("WC") || bName.Contains("TOILET") || bName.Contains("BATH") || bName.Contains("LAVABO") || bName.Contains("CHAU") || bName.Contains("BON") || bName.Contains("SEN") || bName.Contains("XI")) hasWcFixtures = true;
                    if (bName.Contains("BED") || bName.Contains("GIUONG")) hasBed = true;
                    if (bName.Contains("BEP") || bName.Contains("KITCHEN") || bName.Contains("GAS")) hasKitchen = true;
                }

                Line ln = ent as Line;
                if (ln != null)
                {
                    double len = ln.Length;
                    if (len >= 600.0 && len <= 3500.0)
                    {
                        stepLines++;
                    }
                }
            }

            if (!string.IsNullOrEmpty(foundCadText)) return foundCadText;
            if (hasStairs || stepLines >= 4) return "THANG BỘ";
            if (hasWcFixtures) return "WC";
            if (hasBed) return "PHÒNG NGỦ";
            if (hasKitchen) return "PHÒNG BẾP";

            if (areaM2 <= 1.8) return "HỘP KT";
            if (areaM2 <= 5.5) return "WC";
            if (areaM2 >= 20.0) return "PHÒNG NGỦ";
            return "PHÒNG CHỨC NĂNG";
        }

        private static bool IsArchitecturalRoomName(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string u = text.ToUpperInvariant();
            return u.Contains("NGỦ") || u.Contains("KHÁCH") || u.Contains("BẾP") || u.Contains("ĂN") ||
                   u.Contains("WC") || u.Contains("VỆ SINH") || u.Contains("TẮM") || u.Contains("THANG") ||
                   u.Contains("BAN CÔNG") || u.Contains("LOGIA") || u.Contains("LÔ GIA") || u.Contains("KT") ||
                   u.Contains("GEN") || u.Contains("KHO") || u.Contains("CHỨC NĂNG") || u.Contains("KTX") ||
                   u.Contains("LÀM VIỆC") || u.Contains("SINH HOẠT");
        }

        /// <summary>
        /// Rải nhãn MText tự động vào tâm thông thủy an toàn của các phòng kèm cấu hình cỡ chữ và đa ngữ
        /// </summary>
        public static int AutoPlaceRoomTexts(Document doc, List<CoPilotRoomItem> rooms, TextFormatConfig textConfig, Extents3d? scopeBounds)
        {
            if (doc == null) return 0;
            Database db = doc.Database;
            int count = 0;

            if (textConfig == null) textConfig = new TextFormatConfig();

            // 1. Xóa các nhãn MText cũ trên DUONG_BAO_TEXT trong phạm vi khung bao để tránh đè chữ
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                List<ObjectId> oldTexts = new List<ObjectId>();
                foreach (ObjectId id in ms)
                {
                    MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                    if (mt != null && string.Equals(mt.Layer, LayerDuongBaoText, StringComparison.OrdinalIgnoreCase))
                    {
                        if (scopeBounds.HasValue)
                        {
                            Point3d loc = mt.Location;
                            if (loc.X >= scopeBounds.Value.MinPoint.X && loc.X <= scopeBounds.Value.MaxPoint.X &&
                                loc.Y >= scopeBounds.Value.MinPoint.Y && loc.Y <= scopeBounds.Value.MaxPoint.Y)
                            {
                                oldTexts.Add(id);
                            }
                        }
                        else
                        {
                            oldTexts.Add(id);
                        }
                    }
                }
                foreach (ObjectId oid in oldTexts)
                {
                    DBObject obj = tr.GetObject(oid, OpenMode.ForWrite);
                    obj.Erase();
                }
                tr.Commit();
            }

            // 2. Chuẩn bị danh sách phòng
            if (rooms == null || rooms.Count == 0)
            {
                // Thử tải từ Multi-Agent cache
                var loaded = CoPilotRoomBoundaryService.LoadMultiAgentRoomPolygons();
                if (loaded != null && loaded.Count > 0)
                {
                    if (scopeBounds.HasValue)
                    {
                        Extents3d kb = scopeBounds.Value;
                        rooms = loaded.Where(r => {
                            double rx = r.CenterX != 0 ? r.CenterX : (r.Vertices != null && r.Vertices.Count > 0 ? r.Vertices[0].X : 0);
                            double ry = r.CenterY != 0 ? r.CenterY : (r.Vertices != null && r.Vertices.Count > 0 ? r.Vertices[0].Y : 0);
                            return rx >= kb.MinPoint.X - 10.0 && rx <= kb.MaxPoint.X + 10.0 &&
                                   ry >= kb.MinPoint.Y - 10.0 && ry <= kb.MaxPoint.Y + 10.0;
                        }).ToList();
                    }
                    else
                    {
                        rooms = loaded;
                    }
                }
            }

            if (rooms == null || rooms.Count == 0)
            {
                // Fallback: Quét các Polyline khép kín trên layer DUONG_BAO và suy luận công năng
                rooms = new List<CoPilotRoomItem>();
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        Polyline pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (pl != null && pl.Closed && (string.Equals(pl.Layer, LayerDuongBao, StringComparison.OrdinalIgnoreCase) || string.Equals(pl.Layer, "AI_BO_PHONG", StringComparison.OrdinalIgnoreCase)))
                        {
                            double areaM2 = pl.Area / 1000000.0;
                            if (areaM2 >= 0.5)
                            {
                                Extents3d ext = pl.GeometricExtents;
                                double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                                double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;

                                if (scopeBounds.HasValue)
                                {
                                    if (cx < scopeBounds.Value.MinPoint.X || cx > scopeBounds.Value.MaxPoint.X ||
                                        cy < scopeBounds.Value.MinPoint.Y || cy > scopeBounds.Value.MaxPoint.Y)
                                    {
                                        continue;
                                    }
                                }

                                CoPilotRoomItem item = new CoPilotRoomItem();
                                item.AreaM2 = areaM2;
                                item.CenterX = cx;
                                item.CenterY = cy;
                                item.RoomName = InferRoomNameFromCad(db, tr, pl, areaM2);
                                if (item.RoomName.IndexOf("WC", StringComparison.OrdinalIgnoreCase) >= 0)
                                    item.IsWc = true;
                                rooms.Add(item);
                            }
                        }
                    }
                    tr.Commit();
                }
                if (rooms.Count == 0) return 0;
            }

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureStandardLayers(db, tr);
                ObjectId unicodeStyle = EnsureUnicodeTextStyle(db, tr);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (var r in rooms)
                {
                    Point3d textPt = Point3d.Origin;
                    if (r.LabelX != 0 || r.LabelY != 0)
                    {
                        textPt = new Point3d(r.LabelX, r.LabelY, 0);
                    }
                    else if (r.CenterX != 0 || r.CenterY != 0)
                    {
                        textPt = new Point3d(r.CenterX, r.CenterY, 0);
                    }
                    else if (r.Vertices != null && r.Vertices.Count > 0)
                    {
                        double cx = r.Vertices.Average(v => v.X);
                        double cy = r.Vertices.Average(v => v.Y);
                        textPt = new Point3d(cx, cy, 0);
                    }

                    if (scopeBounds.HasValue)
                    {
                        if (textPt.X < scopeBounds.Value.MinPoint.X - 5.0 || textPt.X > scopeBounds.Value.MaxPoint.X + 5.0 ||
                            textPt.Y < scopeBounds.Value.MinPoint.Y - 5.0 || textPt.Y > scopeBounds.Value.MaxPoint.Y + 5.0)
                        {
                            continue;
                        }
                    }

                    string rName = r.RoomName;
                    if (string.IsNullOrEmpty(rName) || rName.StartsWith("PH-", StringComparison.OrdinalIgnoreCase) || rName.StartsWith("P-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (r.AreaM2 <= 1.8) rName = "HỘP KT";
                        else if (r.IsWc || r.AreaM2 <= 5.5) { rName = "WC"; r.IsWc = true; }
                        else if (r.AreaM2 >= 20.0) rName = "PHÒNG NGỦ";
                        else rName = "PHÒNG CHỨC NĂNG";
                    }

                    string textContent = textConfig.BuildMTextContent(rName, r.AreaM2, r.IsWc);
                    double textHeight = textConfig.GetHeightForRoom(rName, r.IsWc);

                    MText mt = new MText
                    {
                        Location = textPt,
                        Contents = textContent,
                        TextHeight = textHeight,
                        TextStyleId = unicodeStyle,
                        Attachment = AttachmentPoint.MiddleCenter,
                        Layer = LayerDuongBaoText,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 3) // Green
                    };

                    ms.AppendEntity(mt);
                    tr.AddNewlyCreatedDBObject(mt, true);
                    count++;
                }

                tr.Commit();
            }

            return count;
        }
    }
}

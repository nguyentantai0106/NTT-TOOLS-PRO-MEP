using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    public class LearnedShapePattern
    {
        public string PatternId { get; set; }
        public string Name { get; set; }
        public string TargetLayer { get; set; }
        public int EntityCount { get; set; }
        public int SegCount { get; set; }
        public int ArcCount { get; set; }
        public int CircleCount { get; set; }
        public double MajorSpan { get; set; }
        public double MinorSpan { get; set; }
        public double TotalLength { get; set; }
        public bool IsArrayPattern { get; set; }
        public double ArrayBarLength { get; set; }
        public int ArrayBarCount { get; set; }
        public List<double> SortedSegLengths { get; set; }
        public List<double> SortedRadialDistances { get; set; }

        public LearnedShapePattern()
        {
            SortedSegLengths = new List<double>();
            SortedRadialDistances = new List<double>();
        }
    }

    public class ShapeClusterCandidate
    {
        public List<ObjectId> EntityIds = new List<ObjectId>();
        public HashSet<string> HandleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string CurrentLayerDisplay { get; set; }
        public LearnedShapePattern Signature { get; set; }
        public LearnedShapePattern MatchedPattern { get; set; }
        public Extents3d Extents { get; set; }
    }

    public class ShapeLearnAndApplySummary
    {
        public int PatternsLearned { get; set; }
        public int TotalSavedPatterns { get; set; }
        public int MatchedClusters { get; set; }
        public int CandidateEntitiesToMove { get; set; }
        public int EntitiesChanged { get; set; }
        public string SummaryMessage { get; set; }
    }

    /// <summary>
    /// Động cơ Học Mẫu Hình Học từ 1 phòng ví dụ hoặc 1 cụm chọn sẵn, hỗ trợ bất biến
    /// Tịnh tiến + Xoay mọi góc + Đối xứng gương (Mirror) để quét gom đồng loạt trong Khung Bao.
    /// </summary>
    public static class ShapePatternLearningService
    {
        private static readonly object _lock = new object();
        private static List<LearnedShapePattern> _patterns;
        private static List<LearnedShapePattern> _step2SessionPatterns = new List<LearnedShapePattern>();
        private static HashSet<string> _step2SourceLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string GetPatternFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "TaiAiAddin", "learned_shape_patterns.json");
        }

        public static List<LearnedShapePattern> LoadPatterns()
        {
            lock (_lock)
            {
                if (_patterns != null)
                {
                    return new List<LearnedShapePattern>(_patterns);
                }

                _patterns = new List<LearnedShapePattern>();
                try
                {
                    string path = GetPatternFilePath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path, Encoding.UTF8);
                        if (!string.IsNullOrEmpty(json))
                        {
                            JavaScriptSerializer js = new JavaScriptSerializer();
                            var loaded = js.Deserialize<List<LearnedShapePattern>>(json);
                            if (loaded != null)
                            {
                                foreach (var p in loaded)
                                {
                                    if (p != null && !string.IsNullOrEmpty(p.TargetLayer) &&
                                        p.SortedSegLengths != null && p.SortedRadialDistances != null)
                                    {
                                        _patterns.Add(p);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                return new List<LearnedShapePattern>(_patterns);
            }
        }

        public static int GetPatternCount()
        {
            return LoadPatterns().Count;
        }

        public static void ClearPatterns()
        {
            lock (_lock)
            {
                _patterns = new List<LearnedShapePattern>();
                _step2SessionPatterns = new List<LearnedShapePattern>();
                _step2SourceLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = GetPatternFilePath();
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        public static List<LearnedShapePattern> GetActiveStep2Patterns()
        {
            lock (_lock)
            {
                if (_step2SessionPatterns != null && _step2SessionPatterns.Count > 0)
                {
                    return new List<LearnedShapePattern>(_step2SessionPatterns);
                }
            }
            return LoadPatterns();
        }

        public static HashSet<string> GetActiveStep2SourceLayers()
        {
            lock (_lock)
            {
                return new HashSet<string>(_step2SourceLayers, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void RegisterStep2SessionRule(
            IEnumerable<LearnedShapePattern> learnedPatterns, IEnumerable<string> sourceLayers)
        {
            lock (_lock)
            {
                if (sourceLayers != null)
                {
                    foreach (string lay in sourceLayers)
                    {
                        if (!string.IsNullOrEmpty(lay)) _step2SourceLayers.Add(lay.Trim());
                    }
                }
                if (learnedPatterns != null)
                {
                    foreach (var pat in learnedPatterns)
                    {
                        if (pat == null) continue;
                        bool replaced = false;
                        for (int i = 0; i < _step2SessionPatterns.Count; i++)
                        {
                            double ignored;
                            if (string.Equals(_step2SessionPatterns[i].TargetLayer, pat.TargetLayer, StringComparison.OrdinalIgnoreCase) &&
                                PatternsMatch(pat, _step2SessionPatterns[i], out ignored))
                            {
                                _step2SessionPatterns[i] = pat;
                                replaced = true;
                                break;
                            }
                        }
                        if (!replaced) _step2SessionPatterns.Add(pat);
                    }
                }
            }
        }

        private static void SavePatternsLocked()
        {
            try
            {
                string path = GetPatternFilePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(path, js.Serialize(_patterns), new UTF8Encoding(true));
            }
            catch { }
        }

        private class ExtractedEntGeom
        {
            public ObjectId Id;
            public string Handle;
            public string Layer;
            public double MinX, MinY, MaxX, MaxY;
            public int ArcCount;
            public int CircleCount;
            public List<Tuple<Point2d, Point2d, double>> Segs = new List<Tuple<Point2d, Point2d, double>>();
            public List<Point2d> SamplePts = new List<Point2d>();
            public List<double> PrimLengths = new List<double>();
        }

        private class WallObstacleContext
        {
            public List<Extents3d> ColBoxes = new List<Extents3d>();
            public List<Extents3d> WallHatchBoxes = new List<Extents3d>();
            public List<Tuple<Point2d, Point2d, Vector2d, double>> LongWallSegs = new List<Tuple<Point2d, Point2d, Vector2d, double>>();
        }

        /// <summary>
        /// Học trực tiếp các cụm hình học rời rạc bên trong tập đối tượng kỹ sư đang chọn trên CAD (tách từng món nội thất).
        /// </summary>
        public static List<LearnedShapePattern> LearnPatternsFromSelectedIds(
            Document doc, IEnumerable<ObjectId> selectedIds, string targetLayer)
        {
            List<LearnedShapePattern> newlyLearned = new List<LearnedShapePattern>();
            if (doc == null || selectedIds == null || string.IsNullOrEmpty(targetLayer) || targetLayer.StartsWith("--"))
                return newlyLearned;

            List<ObjectId> idList = selectedIds.Where(id => !id.IsNull && !id.IsErased).ToList();
            if (idList.Count == 0) return newlyLearned;

            Database db = doc.Database;
            List<ExtractedEntGeom> geoms = new List<ExtractedEntGeom>();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in idList)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    ExtractedEntGeom g = ExtractEntityGeometry(ent);
                    if (g != null) geoms.Add(g);
                }
                tr.Commit();
            }

            if (geoms.Count == 0) return newlyLearned;

            // Tách từng cụm độc lập trong vùng chọn (VD: 1 lần quét gồm cả kệ tủ + giàn móc áo 25 nét)
            List<List<ExtractedEntGeom>> subClusters = ClusterConnectedGeoms(geoms, 5.0, 300);
            foreach (var cl in subClusters)
            {
                LearnedShapePattern pat = BuildSignature(cl, targetLayer);
                if (pat != null && IsPatternComplexEnough(pat))
                {
                    AddOrUpdatePattern(pat);
                    newlyLearned.Add(pat);
                }
            }

            // Fallback: nếu vùng chọn có nhiều nét ngắn rời nhưng ghép tổng lại >= 3 nét -> học nguyên cụm
            if (newlyLearned.Count == 0)
            {
                LearnedShapePattern combined = BuildSignature(geoms, targetLayer);
                if (combined != null && IsPatternComplexEnough(combined))
                {
                    AddOrUpdatePattern(combined);
                    newlyLearned.Add(combined);
                }
            }
            return newlyLearned;
        }

        /// <summary>
        /// Học đồng bộ ngay tại Bước 2 (khi kỹ sư chọn nét và bấm Đổi Layer), lưu nguồn + đích cho Bước 3.
        /// </summary>
        public static List<LearnedShapePattern> LearnStep2PatternsFromSelection(
            Document doc, IEnumerable<ObjectId> selectedIds, IEnumerable<string> sourceLayers, string targetLayer)
        {
            List<LearnedShapePattern> learned = LearnPatternsFromSelectedIds(doc, selectedIds, targetLayer);
            RegisterStep2SessionRule(learned, sourceLayers);
            return learned;
        }

        /// <summary>
        /// Học toàn bộ các cụm hình học nằm trên các layer đích hợp lệ (AI_XD_NOI_THAT, AI_XD_CUA, AI_XD_VACH_KINH, AI_XD_COT)
        /// bên trong vùng phòng mẫu vừa quét trên CAD.
        /// </summary>
        public static List<LearnedShapePattern> LearnPatternsFromSampleRegion(Document doc, Extents3d sampleBounds)
        {
            List<LearnedShapePattern> newlyLearned = new List<LearnedShapePattern>();
            if (doc == null) return newlyLearned;

            Database db = doc.Database;
            Dictionary<string, List<ExtractedEntGeom>> byTargetLayer = new Dictionary<string, List<ExtractedEntGeom>>(StringComparer.OrdinalIgnoreCase);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !ent.Visible) continue;
                    string lay = (ent.Layer ?? "").ToUpperInvariant();
                    if (lay != CadLayoutProcessorService.LayerNoiThat &&
                        lay != CadLayoutProcessorService.LayerCua &&
                        lay != CadLayoutProcessorService.LayerVachKinh &&
                        lay != CadLayoutProcessorService.LayerCot)
                    {
                        continue;
                    }

                    ExtractedEntGeom g = ExtractEntityGeometry(ent);
                    if (g == null) continue;
                    if (g.MaxX < sampleBounds.MinPoint.X || g.MinX > sampleBounds.MaxPoint.X ||
                        g.MaxY < sampleBounds.MinPoint.Y || g.MinY > sampleBounds.MaxPoint.Y)
                    {
                        continue;
                    }

                    List<ExtractedEntGeom> list;
                    if (!byTargetLayer.TryGetValue(lay, out list))
                    {
                        list = new List<ExtractedEntGeom>();
                        byTargetLayer[lay] = list;
                    }
                    list.Add(g);
                }
                tr.Commit();
            }

            foreach (var kvp in byTargetLayer)
            {
                List<List<ExtractedEntGeom>> clusters = ClusterConnectedGeoms(kvp.Value, 5.0, 200);
                foreach (var cl in clusters)
                {
                    LearnedShapePattern pat = BuildSignature(cl, kvp.Key);
                    if (pat != null && IsPatternComplexEnough(pat))
                    {
                        AddOrUpdatePattern(pat);
                        newlyLearned.Add(pat);
                    }
                }
            }

            return newlyLearned;
        }

        /// <summary>
        /// Quét toàn bộ Khung Bao (hoặc bản vẽ) tìm các cụm nét rời khớp mẫu đã học (hỗ trợ xoay + đối xứng gương).
        /// </summary>
        public static List<ShapeClusterCandidate> FindMatchingClustersInScope(
            Document doc, Extents3d? bounds, IEnumerable<LearnedShapePattern> targetPatterns = null,
            IEnumerable<string> step2SourceLayers = null)
        {
            List<ShapeClusterCandidate> matches = new List<ShapeClusterCandidate>();
            if (doc == null) return matches;

            List<LearnedShapePattern> patterns = (targetPatterns != null) ? targetPatterns.ToList() : LoadPatterns();
            if (patterns.Count == 0) return matches;

            HashSet<string> allowedSourceFilter = null;
            if (step2SourceLayers != null)
            {
                var srcList = step2SourceLayers.Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (srcList.Count > 0)
                {
                    allowedSourceFilter = new HashSet<string>(srcList, StringComparer.OrdinalIgnoreCase);
                }
            }
            HashSet<string> patternTargets = new HashSet<string>(
                patterns.Select(p => (p.TargetLayer ?? "").ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase);

            Database db = doc.Database;
            List<ExtractedEntGeom> candidates = new List<ExtractedEntGeom>();
            WallObstacleContext wallCtx = new WallObstacleContext();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                List<Entity> wallLayerCurves = new List<Entity>();

                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !ent.Visible) continue;
                    string lay = (ent.Layer ?? "").ToUpperInvariant();

                    if (lay == CadLayoutProcessorService.LayerKhungBao ||
                        lay == CoPilotRoomBoundaryService.LAYER_DIEM_NOI ||
                        lay == CadLayoutProcessorService.LayerDimText ||
                        lay == CadLayoutProcessorService.LayerGrid)
                    {
                        continue;
                    }

                    if (bounds.HasValue && !IsEntityInsideBounds(ent, bounds.Value)) continue;

                    if (lay == CadLayoutProcessorService.LayerCot)
                    {
                        try { wallCtx.ColBoxes.Add(ent.GeometricExtents); } catch { }
                        if (allowedSourceFilter == null || !allowedSourceFilter.Contains(lay)) continue;
                    }

                    // Bỏ qua cung xoay cửa đi (R >= 450) để không bao giờ chạm vào cửa thật,
                    // nhưng cho phép Line ngắn rơi nhầm ở AI_XD_CUA/AI_XD_RAC tham gia ghép đủ cụm đồ nội thất rời
                    if (ent is Arc)
                    {
                        Arc aEnt = (Arc)ent;
                        if (aEnt.Radius >= 450.0) continue;
                    }
                    if (lay == CadLayoutProcessorService.LayerVachKinh &&
                        !patternTargets.Contains(lay) &&
                        (allowedSourceFilter == null || !allowedSourceFilter.Contains(lay)))
                    {
                        continue;
                    }

                    if (ent is Hatch)
                    {
                        Hatch ht = (Hatch)ent;
                        string patUp = (ht.PatternName ?? "").ToUpperInvariant();
                        if (patUp == "SOLID" || patUp.Contains("ANSI") || patUp.Contains("BRICK") || patUp.Contains("AR-"))
                        {
                            try { wallCtx.WallHatchBoxes.Add(ht.GeometricExtents); } catch { }
                        }
                        continue;
                    }

                    if (ent is DBText || ent is MText || ent is Dimension || ent is Leader || ent is BlockReference || ent is Solid)
                    {
                        continue;
                    }

                    if (lay == CadLayoutProcessorService.LayerTuong)
                    {
                        wallLayerCurves.Add(ent);
                    }

                    ExtractedEntGeom g = ExtractEntityGeometry(ent);
                    if (g == null) continue;

                    if (lay == CadLayoutProcessorService.LayerCua &&
                        !patternTargets.Contains(lay) &&
                        (allowedSourceFilter == null || !allowedSourceFilter.Contains(lay)) &&
                        (g.Segs.Count == 0 || g.Segs.Max(s => s.Item3) > 850.0))
                    {
                        continue;
                    }

                    candidates.Add(g);
                }

                // Thu thập tập cạnh tường xây thực sự (>= 1200mm) trên AI_XD_TUONG để bảo vệ tường khỏi bị tách nhầm
                foreach (Entity wEnt in wallLayerCurves)
                {
                    AddLongSegmentsForWallCheck(wEnt, wallCtx.LongWallSegs);
                }
                tr.Commit();
            }

            // Lọc bỏ đoạn tường thật khỏi tập ứng viên gom cụm (nhưng giữ nguyên thanh dài thuộc đồ nội thất rời như giàn móc áo 1959mm!)
            List<ExtractedEntGeom> filtered = new List<ExtractedEntGeom>();
            foreach (var g in candidates)
            {
                if (IsProtectedStructuralWallGeom(g, wallCtx)) continue;
                filtered.Add(g);
            }

            // Gom cụm liên thông (<= 5mm) theo giới hạn kích thước lớn nhất của tập mẫu
            int maxEntsInCluster = Math.Max(120, patterns.Max(p => p.EntityCount) * 2);
            List<List<ExtractedEntGeom>> clusters = ClusterConnectedGeoms(filtered, 5.0, maxEntsInCluster);

            foreach (var cl in clusters)
            {
                // Nếu phần lớn đối tượng trong cụm ĐÃ nằm đúng TargetLayer rồi thì kiểm tra nhanh trong So khớp
                LearnedShapePattern sig = BuildSignature(cl, "");
                if (sig == null || !IsPatternComplexEnough(sig)) continue;

                LearnedShapePattern matched = FindBestMatchingPattern(sig, patterns);
                if (matched == null) continue;

                // Kiểm tra xem có ít nhất 1 đối tượng trong cụm đang nằm khác TargetLayer không
                bool hasMislayered = cl.Any(e => !string.Equals(e.Layer, matched.TargetLayer, StringComparison.OrdinalIgnoreCase));
                if (!hasMislayered) continue;

                var cand = new ShapeClusterCandidate
                {
                    Signature = sig,
                    MatchedPattern = matched,
                    CurrentLayerDisplay = string.Join(", ", cl.Select(e => e.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()),
                    Extents = ComputeExtents(cl)
                };
                foreach (var g in cl)
                {
                    cand.EntityIds.Add(g.Id);
                    if (!string.IsNullOrEmpty(g.Handle)) cand.HandleSet.Add(g.Handle);
                }
                matches.Add(cand);
            }

            return matches;
        }

        /// <summary>
        /// Thực thi chuyển toàn bộ đối tượng của các cụm đã khớp sang TargetLayer chuẩn trong 1 Transaction.
        /// </summary>
        public static int ApplyMatchedClusters(Document doc, List<ShapeClusterCandidate> matches)
        {
            if (doc == null || matches == null || matches.Count == 0) return 0;
            Database db = doc.Database;
            int changed = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CadLayoutProcessorService.EnsureStandardLayers(db, tr);
                foreach (var m in matches)
                {
                    if (m == null || m.MatchedPattern == null) continue;
                    string dstLayer = m.MatchedPattern.TargetLayer;
                    foreach (ObjectId id in m.EntityIds)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null && !string.Equals(ent.Layer, dstLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            ent.Layer = dstLayer;
                            changed++;
                        }
                    }
                }
                tr.Commit();
            }
            return changed;
        }

        /// <summary>
        /// Tích hợp vào 1-Click: nâng cấp toàn bộ SuspiciousEntityItem thuộc các cụm khớp với Mẫu Hình Học đã học.
        /// </summary>
        public static int UpgradeClassificationsByLearnedShapes(
            Document doc, Extents3d? bounds, List<SuspiciousEntityItem> items)
        {
            if (doc == null || items == null || items.Count == 0) return 0;
            var patterns = LoadPatterns();
            if (patterns.Count == 0) return 0;

            var matches = FindMatchingClustersInScope(doc, bounds, patterns);
            if (matches.Count == 0) return 0;

            Dictionary<string, ShapeClusterCandidate> byHandle = new Dictionary<string, ShapeClusterCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in matches)
            {
                foreach (string h in m.HandleSet)
                {
                    byHandle[h] = m;
                }
            }

            int upgraded = 0;
            HashSet<string> existingHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrEmpty(it.HandleStr)) continue;
                existingHandles.Add(it.HandleStr);
                ShapeClusterCandidate m;
                if (byHandle.TryGetValue(it.HandleStr, out m) && m.MatchedPattern != null)
                {
                    it.SuggestedLayer = m.MatchedPattern.TargetLayer;
                    it.IsOutsideKnowledge = false;
                    it.Confidence = 0.97;
                    it.IsSelected = true;
                    it.Reason = string.Format("🎓 Khớp mẫu phòng đã học [{0}]", m.MatchedPattern.Name);
                    upgraded++;
                }
            }

            // Bổ sung các đối tượng thuộc cụm đã khớp nhưng chưa nằm trong items (VD: nét ngắn < 20mm)
            Database db = doc.Database;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (var m in matches)
                {
                    if (m == null || m.MatchedPattern == null) continue;
                    foreach (ObjectId id in m.EntityIds)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        string hStr = ent.Handle.ToString();
                        if (existingHandles.Contains(hStr)) continue;
                        existingHandles.Add(hStr);
                        if (string.Equals(ent.Layer, m.MatchedPattern.TargetLayer, StringComparison.OrdinalIgnoreCase)) continue;

                        Point3d center = Point3d.Origin;
                        try
                        {
                            Extents3d ext = ent.GeometricExtents;
                            center = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5, (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5, 0);
                        }
                        catch { }

                        items.Add(new SuspiciousEntityItem
                        {
                            HandleStr = hStr,
                            EntityType = ent.GetType().Name,
                            CurrentLayer = ent.Layer,
                            SuggestedLayer = m.MatchedPattern.TargetLayer,
                            Reason = string.Format("🎓 Khớp mẫu phòng đã học [{0}]", m.MatchedPattern.Name),
                            CenterPoint = center,
                            IsSelected = true,
                            Confidence = 0.97,
                            IsOutsideKnowledge = false
                        });
                        upgraded++;
                    }
                }
                tr.Commit();
            }
            return upgraded;
        }

        private static bool IsProtectedStructuralWallGeom(ExtractedEntGeom g, WallObstacleContext ctx)
        {
            // Bảo vệ khỏi gom dính cột BTCT hoặc nét tường dài thuộc tường kép/Hatch tường trên AI_XD_TUONG
            if (IsInsideAnyBox(g, ctx.ColBoxes, 3.0)) return true;
            if (!string.Equals(g.Layer, CadLayoutProcessorService.LayerTuong, StringComparison.OrdinalIgnoreCase))
                return false;

            if (g.Segs.Count == 0) return false;
            double maxSegLen = g.Segs.Max(s => s.Item3);
            if (maxSegLen < 1200.0) return false;

            // 1. Tường dài >= 2200mm trên AI_XD_TUONG luôn là tuyến tường bao/ngăn phòng
            if (maxSegLen >= 2200.0) return true;

            // 2. Nét dài >= 1200mm chạm sát Hatch tường xây -> chắc chắn là mép tường
            if (IsNearAnyBox(g, ctx.WallHatchBoxes, 8.0)) return true;

            // 2. Có cạnh tường song song đối diện sạch 90..260mm cùng nằm trên AI_XD_TUONG với chiều dài >= 1500mm
            foreach (var seg in g.Segs)
            {
                if (seg.Item3 < 1500.0) continue;
                Vector2d v = seg.Item2 - seg.Item1;
                Vector2d u = v / seg.Item3;
                Vector2d n = new Vector2d(-u.Y, u.X);

                foreach (var w in ctx.LongWallSegs)
                {
                    if (w.Item4 < 1500.0) continue;
                    if (Math.Abs(u.DotProduct(w.Item3)) < 0.995) continue;
                    double perp = Math.Abs((w.Item1 - seg.Item1).DotProduct(n));
                    if (perp >= 90.0 && perp <= 260.0)
                    {
                        double p1 = (w.Item1 - seg.Item1).DotProduct(u);
                        double p2 = (w.Item2 - seg.Item1).DotProduct(u);
                        double ov = Math.Min(seg.Item3, Math.Max(p1, p2)) - Math.Max(0.0, Math.Min(p1, p2));
                        if (ov >= Math.Min(seg.Item3, w.Item4) * 0.72) return true;
                    }
                }
            }
            return false;
        }

        private static bool IsInsideAnyBox(ExtractedEntGeom g, List<Extents3d> boxes, double tol)
        {
            double cx = (g.MinX + g.MaxX) * 0.5;
            double cy = (g.MinY + g.MaxY) * 0.5;
            foreach (var b in boxes)
            {
                if (cx >= b.MinPoint.X - tol && cx <= b.MaxPoint.X + tol &&
                    cy >= b.MinPoint.Y - tol && cy <= b.MaxPoint.Y + tol)
                    return true;
            }
            return false;
        }

        private static bool IsNearAnyBox(ExtractedEntGeom g, List<Extents3d> boxes, double tol)
        {
            foreach (var b in boxes)
            {
                if (g.MaxX >= b.MinPoint.X - tol && g.MinX <= b.MaxPoint.X + tol &&
                    g.MaxY >= b.MinPoint.Y - tol && g.MinY <= b.MaxPoint.Y + tol)
                    return true;
            }
            return false;
        }

        private static void AddLongSegmentsForWallCheck(Entity ent, List<Tuple<Point2d, Point2d, Vector2d, double>> segs)
        {
            if (ent is Line)
            {
                Line ln = (Line)ent;
                Point2d a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                Point2d b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len >= 1200.0) segs.Add(Tuple.Create(a, b, (b - a) / len, len));
            }
            else if (ent is Polyline)
            {
                Polyline pl = (Polyline)ent;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    int next = (i + 1) % pl.NumberOfVertices;
                    if (next == 0 && !pl.Closed) break;
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(next);
                    double len = a.GetDistanceTo(b);
                    if (len >= 1200.0) segs.Add(Tuple.Create(a, b, (b - a) / len, len));
                }
            }
        }

        private static ExtractedEntGeom ExtractEntityGeometry(Entity ent)
        {
            if (ent == null) return null;
            if (!(ent is Line || ent is Polyline || ent is Arc || ent is Circle || ent is Ellipse || ent is Spline))
                return null;

            ExtractedEntGeom g = new ExtractedEntGeom
            {
                Id = ent.ObjectId,
                Handle = ent.Handle.ToString(),
                Layer = ent.Layer ?? "0",
                MinX = double.MaxValue,
                MinY = double.MaxValue,
                MaxX = double.MinValue,
                MaxY = double.MinValue
            };

            if (ent is Line)
            {
                Line ln = (Line)ent;
                Point2d a = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                Point2d b = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                double len = a.GetDistanceTo(b);
                if (len < 1.0) return null;
                AddSeg(g, a, b, len);
            }
            else if (ent is Polyline)
            {
                Polyline pl = (Polyline)ent;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    int next = (i + 1) % pl.NumberOfVertices;
                    if (next == 0 && !pl.Closed) break;
                    Point2d a = pl.GetPoint2dAt(i);
                    Point2d b = pl.GetPoint2dAt(next);
                    double bulge = pl.GetBulgeAt(i);
                    double chord = a.GetDistanceTo(b);
                    if (chord < 1.0 && Math.Abs(bulge) < 1e-4) continue;
                    double arcLen = chord;
                    if (Math.Abs(bulge) > 1e-4)
                    {
                        double theta = 4.0 * Math.Atan(Math.Abs(bulge));
                        double r = chord / (2.0 * Math.Sin(theta * 0.5));
                        arcLen = r * theta;
                        g.ArcCount++;
                    }
                    AddSeg(g, a, b, arcLen);
                }
            }
            else if (ent is Arc)
            {
                Arc arc = (Arc)ent;
                g.ArcCount++;
                double len = Math.Abs(arc.Length);
                if (len < 1.0) return null;
                Point2d ps = new Point2d(arc.StartPoint.X, arc.StartPoint.Y);
                Point2d pe = new Point2d(arc.EndPoint.X, arc.EndPoint.Y);
                Point2d pc = new Point2d(arc.Center.X, arc.Center.Y);
                AddSeg(g, ps, pe, len);
                g.SamplePts.Add(pc);
            }
            else if (ent is Circle)
            {
                Circle c = (Circle)ent;
                g.CircleCount++;
                double len = 2.0 * Math.PI * c.Radius;
                Point2d pc = new Point2d(c.Center.X, c.Center.Y);
                g.PrimLengths.Add(len);
                g.SamplePts.Add(pc);
                g.MinX = pc.X - c.Radius; g.MaxX = pc.X + c.Radius;
                g.MinY = pc.Y - c.Radius; g.MaxY = pc.Y + c.Radius;
                g.Segs.Add(Tuple.Create(new Point2d(pc.X - c.Radius, pc.Y), new Point2d(pc.X + c.Radius, pc.Y), len));
            }
            else if (ent is Curve)
            {
                Curve cv = (Curve)ent;
                try
                {
                    Extents3d ext = cv.GeometricExtents;
                    g.MinX = ext.MinPoint.X; g.MinY = ext.MinPoint.Y;
                    g.MaxX = ext.MaxPoint.X; g.MaxY = ext.MaxPoint.Y;
                    double len = Math.Abs(cv.GetDistanceAtParameter(cv.EndParam) - cv.GetDistanceAtParameter(cv.StartParam));
                    Point3d p1 = cv.StartPoint, p2 = cv.EndPoint;
                    Point2d a = new Point2d(p1.X, p1.Y), b = new Point2d(p2.X, p2.Y);
                    g.PrimLengths.Add(len);
                    g.SamplePts.Add(new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
                    g.Segs.Add(Tuple.Create(a, b, len));
                }
                catch { return null; }
            }

            if (g.PrimLengths.Count == 0) return null;
            return g;
        }

        private static void AddSeg(ExtractedEntGeom g, Point2d a, Point2d b, double primLen)
        {
            g.Segs.Add(Tuple.Create(a, b, primLen));
            g.PrimLengths.Add(primLen);
            g.SamplePts.Add(new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
            IncludePt(g, a);
            IncludePt(g, b);
        }

        private static void IncludePt(ExtractedEntGeom g, Point2d p)
        {
            if (p.X < g.MinX) g.MinX = p.X;
            if (p.Y < g.MinY) g.MinY = p.Y;
            if (p.X > g.MaxX) g.MaxX = p.X;
            if (p.Y > g.MaxY) g.MaxY = p.Y;
        }

        private static List<List<ExtractedEntGeom>> ClusterConnectedGeoms(
            List<ExtractedEntGeom> geoms, double touchTol, int maxClusterSize)
        {
            List<List<ExtractedEntGeom>> clusters = new List<List<ExtractedEntGeom>>();
            if (geoms == null || geoms.Count == 0) return clusters;

            bool[] visited = new bool[geoms.Count];
            for (int i = 0; i < geoms.Count; i++)
            {
                if (visited[i]) continue;
                visited[i] = true;
                List<ExtractedEntGeom> comp = new List<ExtractedEntGeom> { geoms[i] };
                Queue<int> q = new Queue<int>();
                q.Enqueue(i);

                while (q.Count > 0 && comp.Count < maxClusterSize)
                {
                    int curIdx = q.Dequeue();
                    var cur = geoms[curIdx];
                    for (int j = 0; j < geoms.Count; j++)
                    {
                        if (visited[j]) continue;
                        var nxt = geoms[j];
                        if (nxt.MaxX < cur.MinX - touchTol || nxt.MinX > cur.MaxX + touchTol ||
                            nxt.MaxY < cur.MinY - touchTol || nxt.MinY > cur.MaxY + touchTol)
                        {
                            continue;
                        }
                        if (GeomsTouch(cur, nxt, touchTol))
                        {
                            visited[j] = true;
                            comp.Add(nxt);
                            q.Enqueue(j);
                            if (comp.Count >= maxClusterSize) break;
                        }
                    }
                }
                clusters.Add(comp);
            }
            return clusters;
        }

        private static bool GeomsTouch(ExtractedEntGeom a, ExtractedEntGeom b, double tol)
        {
            foreach (var sa in a.Segs)
            {
                foreach (var sb in b.Segs)
                {
                    if (ArchitecturalKnowledgeService.DistanceSegmentToSegment2D(sa.Item1, sa.Item2, sb.Item1, sb.Item2) <= tol)
                        return true;
                }
            }
            return false;
        }

        private static LearnedShapePattern BuildSignature(List<ExtractedEntGeom> cl, string targetLayer)
        {
            if (cl == null || cl.Count == 0) return null;

            List<Point2d> allPts = new List<Point2d>();
            List<double> segLens = new List<double>();
            List<Tuple<Point2d, Point2d, double>> coreSegs = new List<Tuple<Point2d, Point2d, double>>();
            int arcCnt = 0, cirCnt = 0;
            double totalLen = 0.0;

            // Ưu tiên tính chữ ký trên các đoạn cốt lõi >= 40mm để không bị lệch bởi các nét tíc/móc phụ < 40mm (VD: 30mm trên AI_XD_RAC)
            bool hasCore40 = cl.Any(g => g.Segs.Any(s => s.Item3 >= 40.0));

            foreach (var g in cl)
            {
                arcCnt += g.ArcCount;
                cirCnt += g.CircleCount;
                for (int si = 0; si < g.Segs.Count; si++)
                {
                    var sg = g.Segs[si];
                    double L = (si < g.PrimLengths.Count) ? g.PrimLengths[si] : sg.Item3;
                    if (hasCore40 && L < 40.0) continue;

                    segLens.Add(Math.Round(L, 1));
                    totalLen += L;
                    coreSegs.Add(sg);
                    allPts.Add(sg.Item1);
                    allPts.Add(sg.Item2);
                    allPts.Add(new Point2d((sg.Item1.X + sg.Item2.X) * 0.5, (sg.Item1.Y + sg.Item2.Y) * 0.5));
                }
            }

            if (allPts.Count == 0 || segLens.Count == 0) return null;

            // Tính trọng tâm và trục chính PCA 2D -> hoàn toàn bất biến theo mọi góc xoay & đối xứng gương
            double cx = 0.0, cy = 0.0;
            foreach (var p in allPts) { cx += p.X; cy += p.Y; }
            cx /= allPts.Count;
            cy /= allPts.Count;

            double sxx = 0.0, syy = 0.0, sxy = 0.0;
            foreach (var p in allPts)
            {
                double dx = p.X - cx, dy = p.Y - cy;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }
            double angle = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            Vector2d uAxis = new Vector2d(Math.Cos(angle), Math.Sin(angle));
            Vector2d vAxis = new Vector2d(-uAxis.Y, uAxis.X);

            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in allPts)
            {
                Vector2d d = new Vector2d(p.X - cx, p.Y - cy);
                double pu = d.DotProduct(uAxis);
                double pv = d.DotProduct(vAxis);
                if (pu < minU) minU = pu; if (pu > maxU) maxU = pu;
                if (pv < minV) minV = pv; if (pv > maxV) maxV = pv;
            }

            double span1 = maxU - minU;
            double span2 = maxV - minV;
            double major = Math.Round(Math.Max(span1, span2), 1);
            double minor = Math.Round(Math.Min(span1, span2), 1);

            // Khoảng cách từ trọng tâm mẫu đến từng tâm đoạn (bất biến tuyệt đối với xoay + mirror!)
            List<double> radials = new List<double>();
            foreach (var sg in coreSegs)
            {
                double mx = (sg.Item1.X + sg.Item2.X) * 0.5;
                double my = (sg.Item1.Y + sg.Item2.Y) * 0.5;
                double rx = mx - cx, ry = my - cy;
                radials.Add(Math.Round(Math.Sqrt(rx * rx + ry * ry), 1));
            }

            // Nhận diện mẫu Dãy Thanh Song Song lặp lại (VD: giàn móc áo 442mm, nan lam, bậc thang)
            bool isArrayPat = false;
            double bestBarLen = 0.0;
            int bestBarCnt = 0;
            DetectRepeatingParallelBarArray(coreSegs, minor, out isArrayPat, out bestBarLen, out bestBarCnt);

            segLens.Sort((a, b) => b.CompareTo(a));
            radials.Sort((a, b) => b.CompareTo(a));

            LearnedShapePattern pat = new LearnedShapePattern
            {
                PatternId = Guid.NewGuid().ToString("N").Substring(0, 8),
                TargetLayer = targetLayer ?? "",
                EntityCount = cl.Count,
                SegCount = segLens.Count,
                ArcCount = arcCnt,
                CircleCount = cirCnt,
                MajorSpan = major,
                MinorSpan = minor,
                TotalLength = Math.Round(totalLen, 1),
                IsArrayPattern = isArrayPat,
                ArrayBarLength = Math.Round(bestBarLen, 1),
                ArrayBarCount = bestBarCnt,
                SortedSegLengths = segLens,
                SortedRadialDistances = radials,
                Name = isArrayPat
                    ? string.Format("Dãy {0} ({1} thanh {2:F0}mm | {3:F0}x{4:F0})", FormatShortAiName(targetLayer), bestBarCnt, bestBarLen, major, minor)
                    : string.Format("Mẫu {0} ({1} nét | {2:F0}x{3:F0}mm)", FormatShortAiName(targetLayer), segLens.Count, major, minor)
            };
            return pat;
        }

        private static void DetectRepeatingParallelBarArray(
            List<Tuple<Point2d, Point2d, double>> coreSegs, double minorSpan,
            out bool isArray, out double barLen, out int barCount)
        {
            isArray = false;
            barLen = 0.0;
            barCount = 0;
            if (coreSegs == null || coreSegs.Count < 8) return;

            for (int i = 0; i < coreSegs.Count; i++)
            {
                double l1 = coreSegs[i].Item3;
                if (l1 < 80.0 || l1 > 1500.0) continue;
                Vector2d u1 = (coreSegs[i].Item2 - coreSegs[i].Item1) / l1;

                int cnt = 0;
                double sumL = 0.0;
                double tolL = Math.Max(15.0, l1 * 0.08);

                for (int j = 0; j < coreSegs.Count; j++)
                {
                    double l2 = coreSegs[j].Item3;
                    if (Math.Abs(l2 - l1) > tolL) continue;
                    Vector2d u2 = (coreSegs[j].Item2 - coreSegs[j].Item1) / l2;
                    if (Math.Abs(u1.DotProduct(u2)) >= 0.985)
                    {
                        cnt++;
                        sumL += l2;
                    }
                }

                if (cnt > barCount)
                {
                    barCount = cnt;
                    barLen = sumL / Math.Max(1, cnt);
                }
            }

            if (barCount >= 8 &&
                ((double)barCount / coreSegs.Count) >= 0.60 &&
                minorSpan <= barLen + 80.0)
            {
                isArray = true;
            }
        }

        private static bool IsPatternComplexEnough(LearnedShapePattern pat)
        {
            if (pat == null) return false;
            return pat.SegCount >= 3 && pat.MajorSpan >= 100.0 && pat.TotalLength >= 250.0;
        }

        private static LearnedShapePattern FindBestMatchingPattern(
            LearnedShapePattern sig, List<LearnedShapePattern> patterns)
        {
            LearnedShapePattern best = null;
            double bestErr = double.MaxValue;

            foreach (var p in patterns)
            {
                double err;
                if (PatternsMatch(sig, p, out err) && err < bestErr)
                {
                    bestErr = err;
                    best = p;
                }
            }
            return best;
        }

        private static bool PatternsMatch(LearnedShapePattern a, LearnedShapePattern b, out double errScore)
        {
            errScore = double.MaxValue;
            if (a == null || b == null) return false;
            if (a.IsArrayPattern != b.IsArrayPattern) return false;

            // Khớp theo Họ Dãy Thanh Song Song (VD: giàn móc áo 442mm ở các tủ dài ngắn khác nhau 1200..2200mm)
            if (b.IsArrayPattern)
            {
                double barTol = Math.Max(20.0, b.ArrayBarLength * 0.10);
                double minTolArr = Math.Max(45.0, b.MinorSpan * 0.15);
                if (Math.Abs(a.ArrayBarLength - b.ArrayBarLength) > barTol) return false;
                if (Math.Abs(a.MinorSpan - b.MinorSpan) > minTolArr) return false;
                if (a.ArrayBarCount < Math.Max(6, Math.Min(b.ArrayBarCount / 2, 10))) return false;
                if (a.MajorSpan < b.MajorSpan * 0.50 || a.MajorSpan > b.MajorSpan * 1.75) return false;

                errScore = Math.Abs(a.ArrayBarLength - b.ArrayBarLength) / Math.Max(1.0, b.ArrayBarLength)
                         + Math.Abs(a.MinorSpan - b.MinorSpan) / Math.Max(1.0, b.MinorSpan);
                return true;
            }

            if (a.ArcCount != b.ArcCount || a.CircleCount != b.CircleCount) return false;

            int maxSegs = Math.Max(a.SegCount, b.SegCount);
            if (Math.Abs(a.SegCount - b.SegCount) > Math.Max(1, (int)Math.Round(maxSegs * 0.12))) return false;

            double majTol = Math.Max(35.0, b.MajorSpan * 0.10);
            double minTol = Math.Max(35.0, b.MinorSpan * 0.10);
            if (Math.Abs(a.MajorSpan - b.MajorSpan) > majTol) return false;
            if (Math.Abs(a.MinorSpan - b.MinorSpan) > minTol) return false;

            double totTol = Math.Max(50.0, b.TotalLength * 0.12);
            if (Math.Abs(a.TotalLength - b.TotalLength) > totTol) return false;

            double segDiff = CompareSortedFeatureLists(a.SortedSegLengths, b.SortedSegLengths);
            if (segDiff > 0.14) return false;

            double radDiff = CompareSortedFeatureLists(a.SortedRadialDistances, b.SortedRadialDistances);
            if (radDiff > 0.16) return false;

            errScore = Math.Abs(a.MajorSpan - b.MajorSpan) / Math.Max(1.0, b.MajorSpan)
                     + Math.Abs(a.TotalLength - b.TotalLength) / Math.Max(1.0, b.TotalLength)
                     + segDiff + radDiff;
            return true;
        }

        private static double CompareSortedFeatureLists(List<double> a, List<double> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return 1.0;
            int n = Math.Min(24, Math.Min(a.Count, b.Count));
            double sumRel = 0.0;
            for (int k = 0; k < n; k++)
            {
                int ia = (int)Math.Floor((double)k * a.Count / n);
                int ib = (int)Math.Floor((double)k * b.Count / n);
                double va = a[Math.Min(ia, a.Count - 1)];
                double vb = b[Math.Min(ib, b.Count - 1)];
                double denom = Math.Max(25.0, Math.Max(va, vb));
                sumRel += Math.Abs(va - vb) / denom;
            }
            return sumRel / n;
        }

        private static void AddOrUpdatePattern(LearnedShapePattern newPat)
        {
            lock (_lock)
            {
                LoadPatterns();
                for (int i = 0; i < _patterns.Count; i++)
                {
                    double ignored;
                    if (string.Equals(_patterns[i].TargetLayer, newPat.TargetLayer, StringComparison.OrdinalIgnoreCase) &&
                        PatternsMatch(newPat, _patterns[i], out ignored))
                    {
                        _patterns[i] = newPat;
                        SavePatternsLocked();
                        return;
                    }
                }
                _patterns.Add(newPat);
                SavePatternsLocked();
            }
        }

        private static bool IsEntityInsideBounds(Entity ent, Extents3d bounds)
        {
            try
            {
                Extents3d ext = ent.GeometricExtents;
                return !(ext.MaxPoint.X < bounds.MinPoint.X || ext.MinPoint.X > bounds.MaxPoint.X ||
                         ext.MaxPoint.Y < bounds.MinPoint.Y || ext.MinPoint.Y > bounds.MaxPoint.Y);
            }
            catch { return true; }
        }

        private static Extents3d ComputeExtents(List<ExtractedEntGeom> cl)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var g in cl)
            {
                if (g.MinX < x0) x0 = g.MinX;
                if (g.MinY < y0) y0 = g.MinY;
                if (g.MaxX > x1) x1 = g.MaxX;
                if (g.MaxY > y1) y1 = g.MaxY;
            }
            return new Extents3d(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
        }

        private static string FormatShortAiName(string aiLayer)
        {
            if (string.IsNullOrEmpty(aiLayer)) return "Mẫu";
            switch (aiLayer.ToUpperInvariant())
            {
                case "AI_XD_NOI_THAT": return "Nội thất";
                case "AI_XD_CUA": return "Cửa";
                case "AI_XD_VACH_KINH": return "Vách kính";
                case "AI_XD_COT": return "Cột";
                case "AI_XD_TUONG": return "Tường";
                default: return aiLayer;
            }
        }
    }
}

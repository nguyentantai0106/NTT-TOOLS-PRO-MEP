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
    public class DrawingLayerModeInfo
    {
        public bool IsLayerZeroDrawing { get; set; }
        public int TotalNonAiEntities { get; set; }
        public int LayerZeroEntities { get; set; }
        public int DistinctOrganizedLayers { get; set; }
        public double LayerZeroRatio { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Bộ máy phân biệt Bản vẽ Layer 0 (chẩn đoán thuần hình học) vs Bản vẽ có sẵn Layer
    /// (học theo thống kê thực thể trong từng Layer + Từ điển kỹ sư đã xác nhận).
    /// </summary>
    public static class LayerLearningService
    {
        public const string MixedLayerAction = "-- Phân loại chi tiết (Hỗn hợp) --";
        public const string SkipLayerAction = "-- Bỏ Qua (Không Đổi) --";
        private const double LayerZeroThresholdRatio = 0.70;
        private const double HomogeneousThresholdRatio = 0.75;

        private static readonly object _dictLock = new object();
        private static Dictionary<string, string> _learnedMap;

        private static string GetDictionaryFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "TaiAiAddin", "learned_layer_mappings.json");
        }

        private static bool IsSystemOrAiLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return true;
            string up = layerName.Trim().ToUpperInvariant();
            return up == "0" || up == "DEFPOINTS" || up.StartsWith("AI_") ||
                   up.StartsWith("NON-PRINT") || up.StartsWith("DUONG_BAO");
        }

        private static bool IsValidAiTarget(string targetLayer)
        {
            if (string.IsNullOrEmpty(targetLayer)) return false;
            return !targetLayer.StartsWith("--") && targetLayer.StartsWith("AI_XD_", StringComparison.OrdinalIgnoreCase);
        }

        public static Dictionary<string, string> LoadLearnedMappings()
        {
            lock (_dictLock)
            {
                if (_learnedMap != null)
                {
                    return new Dictionary<string, string>(_learnedMap, StringComparer.OrdinalIgnoreCase);
                }

                _learnedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = GetDictionaryFilePath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path, Encoding.UTF8);
                        if (!string.IsNullOrEmpty(json))
                        {
                            JavaScriptSerializer js = new JavaScriptSerializer();
                            Dictionary<string, object> raw = js.DeserializeObject(json) as Dictionary<string, object>;
                            if (raw != null)
                            {
                                foreach (var kvp in raw)
                                {
                                    string key = (kvp.Key ?? "").Trim();
                                    string val = kvp.Value != null ? kvp.Value.ToString().Trim() : "";
                                    if (!IsSystemOrAiLayer(key) && IsValidAiTarget(val))
                                    {
                                        _learnedMap[key] = val;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                return new Dictionary<string, string>(_learnedMap, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static bool TryGetLearnedMapping(string sourceLayer, out string targetLayer)
        {
            targetLayer = null;
            if (IsSystemOrAiLayer(sourceLayer)) return false;
            var dict = LoadLearnedMappings();
            return dict.TryGetValue(sourceLayer.Trim(), out targetLayer) && IsValidAiTarget(targetLayer);
        }

        public static int GetLearnedMappingCount()
        {
            return LoadLearnedMappings().Count;
        }

        public static void ClearLearnedMappings()
        {
            lock (_dictLock)
            {
                _learnedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = GetDictionaryFilePath();
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        public static int SaveConfirmedLayerMappings(IEnumerable<LayerMappingItem> items)
        {
            if (items == null) return 0;
            lock (_dictLock)
            {
                LoadLearnedMappings();
                int saved = 0;
                foreach (var it in items)
                {
                    if (it == null || !it.IsSelected || it.IsMixedLayer) continue;
                    string src = (it.SourceLayer ?? "").Trim();
                    string dst = (it.TargetLayer ?? "").Trim();
                    if (IsSystemOrAiLayer(src) || !IsValidAiTarget(dst)) continue;
                    _learnedMap[src] = dst;
                    saved++;
                }

                if (saved > 0)
                {
                    PersistLearnedMappingsLocked();
                }
                return saved;
            }
        }

        private static void PersistLearnedMappingsLocked()
        {
            try
            {
                string path = GetDictionaryFilePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                JavaScriptSerializer js = new JavaScriptSerializer();
                string json = js.Serialize(_learnedMap);
                File.WriteAllText(path, json, new UTF8Encoding(true));
            }
            catch { }
        }

        public static DrawingLayerModeInfo AnalyzeDrawingLayerMode(Document doc, Extents3d? bounds)
        {
            DrawingLayerModeInfo info = new DrawingLayerModeInfo();
            if (doc == null)
            {
                info.IsLayerZeroDrawing = true;
                info.Description = "Layer 0 (Thuần hình học)";
                return info;
            }

            Database db = doc.Database;
            HashSet<string> organizedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalNonAi = 0;
            int layerZeroCnt = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !IsEntityInsideBounds(ent, bounds)) continue;
                    string lay = (ent.Layer ?? "").Trim();
                    string up = lay.ToUpperInvariant();
                    if (up.StartsWith("AI_XD_") || up.StartsWith("DUONG_BAO") || up == "DEFPOINTS") continue;

                    totalNonAi++;
                    if (up == "0" || string.IsNullOrEmpty(up))
                    {
                        layerZeroCnt++;
                    }
                    else
                    {
                        organizedLayers.Add(lay);
                    }
                }
                tr.Commit();
            }

            double zeroRatio = totalNonAi > 0 ? ((double)layerZeroCnt / totalNonAi) : 1.0;
            bool isZeroMode = (totalNonAi == 0) || (organizedLayers.Count == 0) || (zeroRatio >= LayerZeroThresholdRatio);

            info.TotalNonAiEntities = totalNonAi;
            info.LayerZeroEntities = layerZeroCnt;
            info.DistinctOrganizedLayers = organizedLayers.Count;
            info.LayerZeroRatio = zeroRatio;
            info.IsLayerZeroDrawing = isZeroMode;
            info.Description = isZeroMode
                ? string.Format("Bản vẽ Layer 0 ({0:F0}% trên Layer 0) → Chẩn đoán thuần hình học", zeroRatio * 100.0)
                : string.Format("Bản vẽ có sẵn {0} Layer ({1:F0}% đã phân Layer) → Học theo từng Layer", organizedLayers.Count, (1.0 - zeroRatio) * 100.0);
            return info;
        }

        private class LayerQuickStats
        {
            public int TotalCount;
            public Dictionary<string, int> Votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public void AddVote(string targetLayer, int weight = 1)
            {
                if (!IsValidAiTarget(targetLayer) || weight <= 0) return;
                if (!Votes.ContainsKey(targetLayer)) Votes[targetLayer] = 0;
                Votes[targetLayer] += weight;
            }
        }

        /// <summary>
        /// Quét nhanh toàn bộ Layer trong khung bao, kết hợp Từ điển đã học + Thống kê thực thể bên trong từng Layer + Từ khóa tên Layer.
        /// </summary>
        public static List<LayerMappingItem> ScanAndLearnLayers(Document doc, Extents3d? bounds)
        {
            List<LayerMappingItem> result = new List<LayerMappingItem>();
            if (doc == null) return result;

            Database db = doc.Database;
            ArchitecturalKnowledgeService.EnsureKnowledgeLoaded();
            var learnedDict = LoadLearnedMappings();
            Dictionary<string, LayerQuickStats> statsByLayer = new Dictionary<string, LayerQuickStats>(StringComparer.OrdinalIgnoreCase);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || string.Equals(ent.Layer, CadLayoutProcessorService.LayerKhungBao, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsEntityInsideBounds(ent, bounds)) continue;

                    string lay = ent.Layer ?? "0";
                    LayerQuickStats st;
                    if (!statsByLayer.TryGetValue(lay, out st))
                    {
                        st = new LayerQuickStats();
                        statsByLayer[lay] = st;
                    }
                    st.TotalCount++;

                    if (IsSystemOrAiLayer(lay)) continue;
                    CollectQuickEntityVote(ent, tr, st);
                }
                tr.Commit();
            }

            foreach (var kvp in statsByLayer)
            {
                LayerMappingItem item = BuildLayerMappingItem(kvp.Key, kvp.Value, learnedDict);
                result.Add(item);
            }

            return result.OrderByDescending(x => x.EntityCount).ToList();
        }

        private static void CollectQuickEntityVote(Entity ent, Transaction tr, LayerQuickStats st)
        {
            if (ent is DBText || ent is MText || ent is Dimension || ent is Leader || ent is MLeader)
            {
                st.AddVote(CadLayoutProcessorService.LayerDimText, 1);
                return;
            }

            if (ent is BlockReference)
            {
                var bRes = ArchitecturalKnowledgeService.ClassifyBlockReference((BlockReference)ent, tr);
                if (bRes != null && IsValidAiTarget(bRes.SuggestedLayer) && !bRes.IsOutsideKnowledge)
                {
                    st.AddVote(bRes.SuggestedLayer, 1);
                }
                return;
            }

            if (ent is Hatch)
            {
                Hatch ht = (Hatch)ent;
                string pat = (ht.PatternName ?? "").ToUpperInvariant();
                if (pat == "SOLID")
                {
                    try
                    {
                        Extents3d ext = ht.GeometricExtents;
                        double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                        double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                        if (w >= 140.0 && h >= 140.0 && w <= 2600.0 && h <= 2600.0)
                            st.AddVote(CadLayoutProcessorService.LayerCot, 1);
                    }
                    catch { }
                }
                else if (pat.Contains("ANSI") || pat.Contains("BRICK") || pat.Contains("AR-"))
                {
                    st.AddVote(CadLayoutProcessorService.LayerTuong, 1);
                }
                return;
            }

            if (ent is Arc)
            {
                Arc arc = (Arc)ent;
                double sweepDeg = Math.Abs(arc.EndAngle - arc.StartAngle) * 180.0 / Math.PI;
                if (sweepDeg > 180.0) sweepDeg = 360.0 - sweepDeg;
                if (arc.Radius >= 450.0 && arc.Radius <= 1500.0 && sweepDeg >= 65.0 && sweepDeg <= 125.0)
                {
                    st.AddVote(CadLayoutProcessorService.LayerCua, 1);
                }
                return;
            }

            if (ent is Circle)
            {
                Circle c = (Circle)ent;
                if (c.Radius >= 200.0 && c.Radius <= 650.0)
                {
                    st.AddVote(CadLayoutProcessorService.LayerGrid, 1);
                }
                return;
            }

            if (ent is Polyline)
            {
                Polyline pl = (Polyline)ent;
                if (pl.Closed && pl.NumberOfVertices >= 4 && pl.NumberOfVertices <= 12)
                {
                    try
                    {
                        Extents3d ext = pl.GeometricExtents;
                        double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                        double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                        double minD = Math.Min(w, h), maxD = Math.Max(w, h);
                        if (minD >= 150.0 && maxD <= 1800.0 && pl.Area >= 20000.0)
                        {
                            st.AddVote(CadLayoutProcessorService.LayerCot, 1);
                        }
                    }
                    catch { }
                }
            }
        }

        private static LayerMappingItem BuildLayerMappingItem(
            string sourceLayer, LayerQuickStats st, Dictionary<string, string> learnedDict)
        {
            LayerMappingItem item = new LayerMappingItem
            {
                SourceLayer = sourceLayer,
                EntityCount = st != null ? st.TotalCount : 0
            };

            if (IsSystemOrAiLayer(sourceLayer))
            {
                item.TargetLayer = SkipLayerAction;
                item.IsSelected = false;
                item.Confidence = 1.0;
                item.StatusNote = "Hệ thống / Layer chuẩn";
                return item;
            }

            string dictTarget = null;
            bool inDict = learnedDict != null && learnedDict.TryGetValue(sourceLayer.Trim(), out dictTarget);
            string kwTarget = CadLayoutProcessorService.SuggestAiLayerKeywordOnly(sourceLayer);

            int totalVotes = 0;
            string topVoteLayer = null;
            int topVoteCount = 0;
            string secondVoteLayer = null;
            int secondVoteCount = 0;

            if (st != null && st.Votes.Count > 0)
            {
                var ordered = st.Votes.OrderByDescending(v => v.Value).ToList();
                totalVotes = ordered.Sum(v => v.Value);
                topVoteLayer = ordered[0].Key;
                topVoteCount = ordered[0].Value;
                if (ordered.Count > 1)
                {
                    secondVoteLayer = ordered[1].Key;
                    secondVoteCount = ordered[1].Value;
                }
            }

            double dominantRatio = totalVotes > 0 ? ((double)topVoteCount / totalVotes) : 0.0;

            if (inDict)
            {
                item.TargetLayer = dictTarget;
                item.IsSelected = true;
                item.IsLearnedFromDictionary = true;
                item.Confidence = 0.96;
                item.StatusNote = string.Format("🧠 Đã nhớ chuẩn (96% → {0})", FormatShortAiName(dictTarget));
                return item;
            }

            if (totalVotes >= 3 && dominantRatio < HomogeneousThresholdRatio)
            {
                item.TargetLayer = MixedLayerAction;
                item.IsSelected = false;
                item.IsMixedLayer = true;
                item.Confidence = dominantRatio;
                double secondRatio = totalVotes > 0 ? ((double)secondVoteCount * 100.0 / totalVotes) : 0.0;
                item.StatusNote = string.Format("🔀 Hỗn hợp ({0:F0}% {1}, {2:F0}% {3}) → Tách từng đối tượng",
                    dominantRatio * 100.0, FormatShortAiName(topVoteLayer),
                    secondRatio, FormatShortAiName(secondVoteLayer));
                return item;
            }

            if (totalVotes >= 2 && dominantRatio >= HomogeneousThresholdRatio)
            {
                item.TargetLayer = topVoteLayer;
                item.IsSelected = true;
                item.Confidence = dominantRatio;
                item.StatusNote = string.Format("🧠 Học từ hình học ({0:F0}% - {1}/{2} mẫu)",
                    dominantRatio * 100.0, topVoteCount, totalVotes);
                return item;
            }

            if (totalVotes == 1 && IsValidAiTarget(topVoteLayer) &&
                (string.IsNullOrEmpty(kwTarget) || string.Equals(kwTarget, topVoteLayer, StringComparison.OrdinalIgnoreCase)))
            {
                item.TargetLayer = topVoteLayer;
                item.IsSelected = true;
                item.Confidence = 0.85;
                item.StatusNote = string.Format("🧠 Học từ mẫu thực tế ({0})", FormatShortAiName(topVoteLayer));
                return item;
            }

            if (IsValidAiTarget(kwTarget))
            {
                item.TargetLayer = kwTarget;
                item.IsSelected = true;
                item.Confidence = 0.85;
                item.StatusNote = "📌 Nhận diện theo tên Layer (85%)";
                return item;
            }

            item.TargetLayer = CadLayoutProcessorService.LayerRac;
            item.IsSelected = false;
            item.Confidence = 0.50;
            item.StatusNote = "❓ Layer lạ (< 85% tin cậy) - Cần kiểm tra";
            return item;
        }

        /// <summary>
        /// Kết hợp kết quả ScanSmartGeometryAndPatterns với tri thức Layer sẵn có để học chính xác từng Layer
        /// (bao gồm cả nét Line/Polyline tường, cầu thang, trục Grid) và nâng cấp các nét đồng nhất trên Layer đã học.
        /// </summary>
        public static void RefineAndLearnLayers(
            Document doc, Extents3d? bounds,
            List<SuspiciousEntityItem> classifiedItems,
            IList<LayerMappingItem> gridMappingsToUpdate)
        {
            if (doc == null || classifiedItems == null) return;

            var mode = AnalyzeDrawingLayerMode(doc, bounds);
            if (mode.IsLayerZeroDrawing) return; // Bản vẽ Layer 0 giữ 100% thuần hình học

            var learnedDict = LoadLearnedMappings();
            Dictionary<string, LayerQuickStats> combinedStats = new Dictionary<string, LayerQuickStats>(StringComparer.OrdinalIgnoreCase);

            // 1. Lấy thống kê Text/MText/Dimension/Block/Hatch từng Layer
            Database db = doc.Database;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || string.Equals(ent.Layer, CadLayoutProcessorService.LayerKhungBao, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsEntityInsideBounds(ent, bounds)) continue;

                    string lay = ent.Layer ?? "0";
                    LayerQuickStats st;
                    if (!combinedStats.TryGetValue(lay, out st))
                    {
                        st = new LayerQuickStats();
                        combinedStats[lay] = st;
                    }
                    st.TotalCount++;
                    if (IsSystemOrAiLayer(lay)) continue;
                    if (ent is DBText || ent is MText || ent is Dimension || ent is Leader || ent is MLeader)
                    {
                        st.AddVote(CadLayoutProcessorService.LayerDimText, 1);
                    }
                }
                tr.Commit();
            }

            // 2. Gom phiếu từ toàn bộ chẩn đoán hình học chi tiết (chỉ tính các mục đủ tin cậy, trừ rác rơi rớt)
            foreach (var it in classifiedItems)
            {
                if (it == null || IsSystemOrAiLayer(it.CurrentLayer)) continue;
                LayerQuickStats st;
                if (!combinedStats.TryGetValue(it.CurrentLayer, out st))
                {
                    st = new LayerQuickStats();
                    combinedStats[it.CurrentLayer] = st;
                }
                if (IsValidAiTarget(it.SuggestedLayer) && !it.IsOutsideKnowledge)
                {
                    int w = (it.SuggestedLayer == CadLayoutProcessorService.LayerRac && it.Confidence < 0.92) ? 0 : 1;
                    if (w > 0) st.AddVote(it.SuggestedLayer, w);
                }
            }

            // 3. Quyết định Layer thuần nhất (>= 75%) vs Layer hỗn hợp (< 75%)
            Dictionary<string, LayerMappingItem> decidedMap = new Dictionary<string, LayerMappingItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in combinedStats)
            {
                decidedMap[kvp.Key] = BuildLayerMappingItem(kvp.Key, kvp.Value, learnedDict);
            }

            // 4. Đồng bộ lại bảng UI _layerMappings nếu có truyền vào
            if (gridMappingsToUpdate != null)
            {
                gridMappingsToUpdate.Clear();
                foreach (var m in decidedMap.Values.OrderByDescending(x => x.EntityCount))
                {
                    gridMappingsToUpdate.Add(m);
                }
            }

            // 5. Áp tri thức Layer đã học lên các đối tượng thuộc Layer đồng nhất (>=75% hoặc Từ điển)
            foreach (var it in classifiedItems)
            {
                if (it == null || IsSystemOrAiLayer(it.CurrentLayer)) continue;
                LayerMappingItem layerDecision;
                if (!decidedMap.TryGetValue(it.CurrentLayer, out layerDecision)) continue;
                if (layerDecision.IsMixedLayer || !IsValidAiTarget(layerDecision.TargetLayer)) continue;

                // Không ép đè nếu bản thân thực thể là Block/Hatch/Cửa đã được nhận diện cực chắc thuộc nhóm khác rõ rệt
                bool highConfidenceConflict = !it.IsOutsideKnowledge &&
                    it.Confidence >= 0.92 &&
                    IsValidAiTarget(it.SuggestedLayer) &&
                    it.SuggestedLayer != CadLayoutProcessorService.LayerRac &&
                    !string.Equals(it.SuggestedLayer, layerDecision.TargetLayer, StringComparison.OrdinalIgnoreCase);

                if (!highConfidenceConflict &&
                    !string.Equals(it.SuggestedLayer, layerDecision.TargetLayer, StringComparison.OrdinalIgnoreCase))
                {
                    it.SuggestedLayer = layerDecision.TargetLayer;
                    it.IsOutsideKnowledge = false;
                    it.Confidence = Math.Max(it.Confidence, layerDecision.Confidence);
                    it.IsSelected = true;
                    it.Reason = string.Format("🧠 Học theo Layer [{0}] ({1})", it.CurrentLayer, layerDecision.StatusNote);
                }
                else if (string.Equals(it.SuggestedLayer, layerDecision.TargetLayer, StringComparison.OrdinalIgnoreCase) &&
                         (it.IsOutsideKnowledge || it.Confidence < 0.85))
                {
                    it.IsOutsideKnowledge = false;
                    it.Confidence = Math.Max(0.88, layerDecision.Confidence);
                    it.IsSelected = true;
                    it.Reason = string.Format("🧠 Xác nhận theo Layer [{0}] -> {1}", it.CurrentLayer, FormatShortAiName(layerDecision.TargetLayer));
                }
            }
        }

        private static bool IsEntityInsideBounds(Entity ent, Extents3d? bounds)
        {
            if (!bounds.HasValue) return true;
            try
            {
                Extents3d entExt = ent.GeometricExtents;
                if (entExt.MaxPoint.X < bounds.Value.MinPoint.X || entExt.MinPoint.X > bounds.Value.MaxPoint.X ||
                    entExt.MaxPoint.Y < bounds.Value.MinPoint.Y || entExt.MinPoint.Y > bounds.Value.MaxPoint.Y)
                {
                    return false;
                }
            }
            catch { }
            return true;
        }

        private static string FormatShortAiName(string aiLayer)
        {
            if (string.IsNullOrEmpty(aiLayer)) return "Khác";
            switch (aiLayer.ToUpperInvariant())
            {
                case "AI_XD_TUONG": return "Tường";
                case "AI_XD_COT": return "Cột";
                case "AI_XD_CUA": return "Cửa";
                case "AI_XD_VACH_KINH": return "Vách kính";
                case "AI_XD_NOI_THAT": return "Nội thất";
                case "AI_XD_DIM_TEXT": return "Dim/Text";
                case "AI_XD_GRID": return "Trục Grid";
                case "AI_XD_RAC": return "Rác/Phụ";
                default: return aiLayer;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TaiAiAddin.Services
{
    public class KnowledgeClassificationResult
    {
        public string EntityType { get; set; }
        public string SuggestedLayer { get; set; }
        public string Reason { get; set; }
        public double Confidence { get; set; }
        public bool IsDoorSwingBlock { get; set; }
        public bool IsStructuralColumn { get; set; }
        public bool IsOutsideKnowledge { get; set; }
        public double PrimaryArcRadius { get; set; }
    }

    public class LiveSelectionDiagnosis
    {
        public string SuggestedLayer { get; set; }
        public string EntityType { get; set; }
        public string Reason { get; set; }
        public bool IsLayerMismatch { get; set; }
        public string CurrentLayer { get; set; }
    }

    internal class DoorWindowTemplateEntry
    {
        public string Key;
        public string CleanFamilyPrefix;
        public string Category;
        public double Width;
        public double Height;
        public int LineCount;
        public int ArcCount;
        public int HatchCount;
        public int CircleCount;
        public string TargetLayer;
    }

    internal class FurnitureRectProfile
    {
        public string TypeName;
        public double Width;
        public double Height;
        public double Tolerance;
        public bool RequiresClosed;
    }

    internal class BlockGeometryStats
    {
        public double LocalWidth;
        public double LocalHeight;
        public int LineCount;
        public int PolylineCount;
        public int ArcCount;
        public int DoorSwingArcCount;
        public double MaxDoorArcRadius;
        public int SmallCircleCount;
        public int BigCircleCount;
        public int EllipseCount;
        public int SplineCount;
        public int WipeoutCount;
        public int HatchCount;
        public int SolidCount;
        public int TextOrAttribCount;
        public bool ContainsShortGridLabel;
        public int SlidingLeafParallelPairs;
        public int DoorLeafLikeCount;

        public int TotalEntities
        {
            get { return LineCount + PolylineCount + ArcCount + SmallCircleCount + BigCircleCount + EllipseCount + SplineCount + HatchCount + SolidCount; }
        }

        public BlockGeometryStats CloneStats()
        {
            return (BlockGeometryStats)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// Dịch vụ Tri thức Kiến trúc & Học Hình học CAD (Nạp từ furniture_knowledge_base.json: v27, v33, v34, v35, v36).
    /// Chịu trách nhiệm phân loại Block Cửa/Vách kính/Cột/Nội thất, kiểm tra Tường tiếp giáp Cột và chẩn đoán Live Selection.
    /// </summary>
    public static class ArchitecturalKnowledgeService
    {
        private static readonly object _syncLock = new object();
        private static bool _isLoaded = false;

        private static readonly List<DoorWindowTemplateEntry> _v35DoorWindowTemplates = new List<DoorWindowTemplateEntry>();
        private static readonly HashSet<string> _framelessDoorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _furnitureBlockNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _registeredFurnitureLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FurnitureRectProfile> _furnitureRectProfiles = new List<FurnitureRectProfile>();

        private static readonly string[] CabinetFurnitureKeywords = new string[]
        {
            "CABINET", "TU_BEP", "TUBEP", "TU_AO", "TUAO", "WARDROBE", "CLOSET",
            "FRIDGE", "TU_LANH", "TULANH", "TU LANH", "OVEN", "SHOWER", "TOILET", "LAVABO",
            "BON_CAU", "XI BET", "XIBET", "CHAU_RUA", "SOFA", "SEATER", "GIUONG", "BED_", "_BED",
            "DINING", "TABLE", "CHAIR", "DESK", "MAY GIAT", "WASHER", "STOVE", "SINK",
            "BASIN", "KHAN GIAY", "LOUVER", "LUVER"
        };

        private static readonly string[] DoorKeywords = new string[]
        {
            "DOOR", "CUA_DI", "CUADI", "CUA_LUA", "CUALUA", "CUA_TRUOT", "CUATRUOT",
            "CUAMO", "CUA_MO", "CUATHANGMAY", "THANGMAY", "PANIC", "D4_", "D5_", "D3_", "D2_", "CUA"
        };

        private static readonly string[] WindowKeywords = new string[]
        {
            "WINDOW", "CUASO", "CUA_SO", "CSO", "VACH_KINH", "VACHKINH", "GLAZ", "KINH", "CURTAIN"
        };

        private static readonly string[] ColumnKeywords = new string[]
        {
            "COT", "COL", "COLUMN", "TRUC_COT", "BETONG", "VACH_BT"
        };

        private static readonly string[] GridKeywords = new string[]
        {
            "TRUC", "GRID", "AXIS", "BUBBLE"
        };

        private static string GetLeafBlockName(string fullBlkName)
        {
            if (string.IsNullOrEmpty(fullBlkName)) return "";
            int idx = fullBlkName.LastIndexOf('$');
            return (idx >= 0 && idx + 1 < fullBlkName.Length)
                ? fullBlkName.Substring(idx + 1).Trim()
                : fullBlkName.Trim();
        }

        public static string LoadedKnowledgeSummary { get; private set; }

        public static void EnsureKnowledgeLoaded()
        {
            if (_isLoaded) return;

            lock (_syncLock)
            {
                if (_isLoaded) return;
                LoadKnowledgeInternal();
                _isLoaded = true;
            }
        }

        private static void LoadKnowledgeInternal()
        {
            LoadedKnowledgeSummary = "Chế độ quy tắc hình học mặc định";
            string jsonPath = ResolveKnowledgeJsonPath();
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                SeedDefaultProfiles();
                return;
            }

            try
            {
                string raw = File.ReadAllText(jsonPath);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 32 * 1024 * 1024;
                Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(raw);
                if (root == null)
                {
                    SeedDefaultProfiles();
                    return;
                }

                // 1. Nạp thư viện Cửa & Vách kính cao tầng v35 (160 mẫu)
                object v35Obj;
                if (root.TryGetValue("highrise_door_and_window_library_v35", out v35Obj) && v35Obj is Dictionary<string, object>)
                {
                    var v35Dict = (Dictionary<string, object>)v35Obj;
                    foreach (var kvp in v35Dict)
                    {
                        var info = kvp.Value as Dictionary<string, object>;
                        if (info == null) continue;

                        DoorWindowTemplateEntry entry = new DoorWindowTemplateEntry();
                        entry.Key = kvp.Key;
                        entry.CleanFamilyPrefix = ExtractCleanFamilyPrefix(kvp.Key);
                        entry.Category = GetStr(info, "category").ToUpperInvariant();
                        entry.Width = GetDouble(info, "width");
                        entry.Height = GetDouble(info, "height");
                        entry.TargetLayer = entry.Category == "WINDOW_EXTERIOR"
                            ? CadLayoutProcessorService.LayerVachKinh
                            : CadLayoutProcessorService.LayerCua;

                        object typesObj;
                        if (info.TryGetValue("types", out typesObj) && typesObj is Dictionary<string, object>)
                        {
                            var typesDict = (Dictionary<string, object>)typesObj;
                            entry.LineCount = GetInt(typesDict, "LINE");
                            entry.ArcCount = GetInt(typesDict, "ARC");
                            entry.HatchCount = GetInt(typesDict, "HATCH");
                            entry.CircleCount = GetInt(typesDict, "CIRCLE");
                        }

                        _v35DoorWindowTemplates.Add(entry);
                    }
                }

                // 2. Nạp danh sách cửa không chân (frameless_door_blocks_to_ignore)
                object framelessObj;
                if (root.TryGetValue("frameless_door_blocks_to_ignore", out framelessObj) && framelessObj is Dictionary<string, object>)
                {
                    foreach (string k in ((Dictionary<string, object>)framelessObj).Keys)
                    {
                        if (!string.IsNullOrEmpty(k)) _framelessDoorNames.Add(k);
                    }
                }

                // 3. Nạp từ điển Block Nội thất (loại trừ các block cửa thuần túy khỏi danh sách nội thất)
                object furnBlocksObj;
                if (root.TryGetValue("furniture_blocks", out furnBlocksObj) && furnBlocksObj is Dictionary<string, object>)
                {
                    foreach (string k in ((Dictionary<string, object>)furnBlocksObj).Keys)
                    {
                        AddFurnitureNameSafe(k);
                    }
                }

                object allSigObj;
                if (root.TryGetValue("all_block_signatures_to_ignore", out allSigObj) && allSigObj is object[])
                {
                    foreach (object item in (object[])allSigObj)
                    {
                        if (item != null) AddFurnitureNameSafe(item.ToString());
                    }
                }

                // 4. Nạp các layer nội thất đã học
                object regLayObj;
                if (root.TryGetValue("registered_furniture_layers", out regLayObj) && regLayObj is object[])
                {
                    foreach (object item in (object[])regLayObj)
                    {
                        if (item != null)
                        {
                            string ln = item.ToString().Trim();
                            if (!string.IsNullOrEmpty(ln)) _registeredFurnitureLayers.Add(ln);
                        }
                    }
                }

                // 5. Nạp hồ sơ hình dạng nội thất rời (v27 learned_furniture_shape_profiles)
                object v27Obj;
                if (root.TryGetValue("layer0_geometric_learning_v27", out v27Obj) && v27Obj is Dictionary<string, object>)
                {
                    var v27Dict = (Dictionary<string, object>)v27Obj;
                    object profObj;
                    if (v27Dict.TryGetValue("learned_furniture_shape_profiles", out profObj) && profObj is object[])
                    {
                        foreach (object p in (object[])profObj)
                        {
                            var pd = p as Dictionary<string, object>;
                            if (pd == null) continue;
                            string action = GetStr(pd, "action");
                            if (!action.Contains("FURNITURE") && !action.Contains("DOOR_LEAF")) continue;

                            object sizeObj;
                            if (pd.TryGetValue("size_mm", out sizeObj) && sizeObj is object[])
                            {
                                object[] arr = (object[])sizeObj;
                                if (arr.Length >= 2)
                                {
                                    double w = Convert.ToDouble(arr[0]);
                                    double h = Convert.ToDouble(arr[1]);
                                    double tol = GetDouble(pd, "tolerance_mm");
                                    if (tol <= 0) tol = 15.0;
                                    _furnitureRectProfiles.Add(new FurnitureRectProfile
                                    {
                                        TypeName = GetStr(pd, "type"),
                                        Width = Math.Min(w, h),
                                        Height = Math.Max(w, h),
                                        Tolerance = tol,
                                        RequiresClosed = GetStr(pd, "entity").IndexOf("Closed", StringComparison.OrdinalIgnoreCase) >= 0
                                    });
                                }
                            }
                        }
                    }
                }

                if (_furnitureRectProfiles.Count == 0) SeedDefaultProfiles();

                LoadedKnowledgeSummary = string.Format("Đã nạp tri thức JSON: {0} mẫu Cửa/Vách kính (v35), {1} Block Nội thất, {2} Profile hình học",
                    _v35DoorWindowTemplates.Count, _furnitureBlockNames.Count, _furnitureRectProfiles.Count);
            }
            catch
            {
                SeedDefaultProfiles();
            }
        }

        private static void SeedDefaultProfiles()
        {
            _furnitureRectProfiles.Clear();
            _furnitureRectProfiles.Add(new FurnitureRectProfile { TypeName = "Tủ giày (450x1200)", Width = 450, Height = 1200, Tolerance = 12, RequiresClosed = true });
            _furnitureRectProfiles.Add(new FurnitureRectProfile { TypeName = "Tủ thấp (755x904)", Width = 755.5, Height = 903.8, Tolerance = 18, RequiresClosed = true });
            _furnitureRectProfiles.Add(new FurnitureRectProfile { TypeName = "Táp đầu giường (450x450)", Width = 450, Height = 450, Tolerance = 12, RequiresClosed = true });
            _furnitureRectProfiles.Add(new FurnitureRectProfile { TypeName = "Ghế xoay (478x725)", Width = 478.4, Height = 724.6, Tolerance = 18, RequiresClosed = true });
            _furnitureRectProfiles.Add(new FurnitureRectProfile { TypeName = "Bàn ăn (700x1000)", Width = 700, Height = 1000, Tolerance = 18, RequiresClosed = true });
        }

        private static void AddFurnitureNameSafe(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string up = name.Trim().ToUpperInvariant();
            if (up.StartsWith("*U")) return; // Không khóa cứng block ẩn danh *U vì CAD tái sinh liên tục theo bản vẽ

            bool isPureDoor = (up.Contains("DOOR") || up.Contains("CUA")) && !CabinetFurnitureKeywords.Any(c => up.Contains(c));
            if (isPureDoor) return;

            _furnitureBlockNames.Add(name.Trim());
        }

        private static string ResolveKnowledgeJsonPath()
        {
            List<string> candidates = new List<string>();
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    candidates.Add(Path.Combine(asmDir, "Scripts", "furniture_knowledge_base.json"));
                    candidates.Add(Path.Combine(asmDir, "furniture_knowledge_base.json"));
                }
            }
            catch { }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            candidates.Add(Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Scripts\furniture_knowledge_base.json"));
            candidates.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Scripts\furniture_knowledge_base.json");
            candidates.Add(@"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Scripts\furniture_knowledge_base.json");

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string ExtractCleanFamilyPrefix(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey)) return "";
            int idx = rawKey.IndexOf("-MĂ_T", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return rawKey.Substring(0, idx).Trim();
            return rawKey.Trim();
        }

        public static bool IsRegisteredFurnitureLayer(string layerName)
        {
            EnsureKnowledgeLoaded();
            return !string.IsNullOrEmpty(layerName) && _registeredFurnitureLayers.Contains(layerName.Trim());
        }

        public static bool MatchesFurnitureRectProfile(double width, double height, int vertexCount, bool isClosed, out string matchedProfileName)
        {
            EnsureKnowledgeLoaded();
            matchedProfileName = null;

            double minD = Math.Min(width, height);
            double maxD = Math.Max(width, height);

            // Quy tắc v34: TV màn hình phẳng / kệ mảnh khép kín (18-90mm x 400-1850mm)
            if (isClosed && vertexCount >= 4 && vertexCount <= 8 &&
                minD >= 18.0 && minD < 92.0 && maxD >= 400.0 && maxD <= 1850.0)
            {
                matchedProfileName = string.Format("Kệ TV / Thanh nội thất mảnh ({0:F0}x{1:F0}mm)", minD, maxD);
                return true;
            }

            // Giường ngủ tiêu chuẩn
            if (isClosed && minD >= 1200.0 && minD <= 2200.0 && maxD >= 1800.0 && maxD <= 2400.0)
            {
                matchedProfileName = string.Format("Giường ngủ ({0:F0}x{1:F0}mm)", minD, maxD);
                return true;
            }

            // Tủ quần áo tiêu chuẩn
            if (isClosed && minD >= 450.0 && minD <= 750.0 && maxD >= 1000.0 && maxD <= 3500.0)
            {
                matchedProfileName = string.Format("Tủ quần áo ({0:F0}x{1:F0}mm)", minD, maxD);
                return true;
            }

            foreach (var prof in _furnitureRectProfiles)
            {
                if (prof.RequiresClosed && !isClosed) continue;
                if (Math.Abs(minD - prof.Width) <= prof.Tolerance && Math.Abs(maxD - prof.Height) <= prof.Tolerance)
                {
                    matchedProfileName = string.Format("{0} ({1:F0}x{2:F0}mm)", prof.TypeName, width, height);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Phân loại BlockReference chuẩn xác bằng Hình học nội tại (trích xuất WCS + xoay/lật Matrix44) kết hợp Tri thức đã học v27/v35/v36.
        /// Khắc phục triệt để lỗi nhận nhầm Block cửa (ví dụ 930x900mm) thành Nội thất.
        /// </summary>
        public static KnowledgeClassificationResult ClassifyBlockReference(BlockReference br, Transaction tr)
        {
            EnsureKnowledgeLoaded();

            string blkName = GetEffectiveBlockName(br, tr);
            string upName = blkName.ToUpperInvariant();
            string leafUpName = GetLeafBlockName(upName);
            BlockGeometryStats stats = ExtractBlockGeometryStats(br, tr);

            double w = stats.LocalWidth;
            double h = stats.LocalHeight;
            double minD = Math.Min(w, h);
            double maxD = Math.Max(w, h);

            // 1. Kiểm tra Block Trục Grid (Bong bóng trục)
            if (GridKeywords.Any(k => leafUpName.Contains(k)) ||
                (stats.ContainsShortGridLabel && (stats.SmallCircleCount + stats.BigCircleCount >= 1) && stats.TotalEntities <= 20))
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Trục Định Vị",
                    SuggestedLayer = CadLayoutProcessorService.LayerGrid,
                    Reason = string.Format("Block bong bóng trục '{0}'", leafUpName),
                    Confidence = 0.95
                };
            }

            // 2. Kiểm tra Cửa không chân (Frameless Door v26: D1600, *U873, *U874)
            if (_framelessDoorNames.Contains(blkName) || _framelessDoorNames.Contains(leafUpName))
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Cửa Không Chân",
                    SuggestedLayer = CadLayoutProcessorService.LayerCua,
                    Reason = string.Format("Block cửa không chân '{0}' theo quy chuẩn v26", leafUpName),
                    Confidence = 0.98
                };
            }

            // 3. Bảo vệ Nội thất / Tủ / Kệ TV / Nan Lam / Thiết bị (kể cả Block có chữ DOOR như "Base Cabinet 1 Door" hoặc góc bo tròn)
            bool isTvOrCabinet = leafUpName == "TV" || leafUpName.StartsWith("TV_") || leafUpName.EndsWith("_TV") ||
                                 CabinetFurnitureKeywords.Any(k => leafUpName.Contains(k));
            if (isTvOrCabinet && (stats.DoorSwingArcCount == 0 || minD < 380.0))
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Nội Thất / Tủ",
                    SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                    Reason = string.Format("Block nội thất '{0}' ({1:F0}x{2:F0}mm)", leafUpName, w, h),
                    Confidence = 0.96
                };
            }

            // 4. [ƯU TIÊN CAO NHẤT THEO HÌNH HỌC THỰC TẾ] Cửa Đi Mở Quay (Swing Door: v27 DOOR_BLOCK + v35 + v36)
            // Chỉ nhận khối có 1..4 cung xoay cửa thực thụ (ArcCount <= 8), loại trừ Sofa/Lavabo có nhiều cung bo tròn nhỏ
            if (!isTvOrCabinet &&
                stats.DoorSwingArcCount >= 1 && stats.DoorSwingArcCount <= 4 && stats.ArcCount <= 8 &&
                stats.BigCircleCount == 0 && stats.EllipseCount == 0 && stats.SplineCount == 0 &&
                maxD >= 520.0 && maxD <= 3000.0 && minD >= 380.0 && minD <= 2600.0)
            {
                string leafDesc = stats.DoorSwingArcCount >= 2 ? "Cửa 2 Cánh Mở Quay" : "Cửa Đi 1 Cánh Mở Quay";
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Cửa Đi (Hình Học)",
                    SuggestedLayer = CadLayoutProcessorService.LayerCua,
                    Reason = string.Format("Block '{0}' ({1:F0}x{2:F0}mm) có {3} cung xoay cửa R={4:F0}mm ({5})",
                        leafUpName, w, h, stats.DoorSwingArcCount, stats.MaxDoorArcRadius, leafDesc),
                    Confidence = 0.99,
                    IsDoorSwingBlock = true,
                    PrimaryArcRadius = stats.MaxDoorArcRadius
                };
            }

            // 5. Đối chiếu Thư viện Cửa & Vách Kính Cao Tầng đã học (160 mẫu v35: tên hoặc hình dạng đối ứng)
            DoorWindowTemplateEntry v35Match = FindV35DoorWindowMatch(leafUpName, w, h, stats);
            if (v35Match != null)
            {
                bool isWindow = v35Match.TargetLayer == CadLayoutProcessorService.LayerVachKinh;
                return new KnowledgeClassificationResult
                {
                    EntityType = isWindow ? "Block Cửa Sổ / Vách Kính (v35)" : "Block Cửa (Tri Thức v35)",
                    SuggestedLayer = v35Match.TargetLayer,
                    Reason = string.Format("Block '{0}' ({1:F0}x{2:F0}mm) khớp mẫu đã học [{3}]",
                        leafUpName, w, h, v35Match.Category),
                    Confidence = 0.96
                };
            }

            // 6. Từ khóa tên Block Cửa sổ / Vách kính / Cửa đi / Cột (trên leafUpName)
            if (WindowKeywords.Any(k => leafUpName.Contains(k)) ||
                System.Text.RegularExpressions.Regex.IsMatch(leafUpName, @"^(CS|S)\s*[-_]?\d{3,4}"))
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Vách Kính / Cửa Sổ",
                    SuggestedLayer = CadLayoutProcessorService.LayerVachKinh,
                    Reason = string.Format("Block vách kính/cửa sổ '{0}' ({1:F0}x{2:F0}mm)", leafUpName, w, h),
                    Confidence = 0.92
                };
            }

            if ((DoorKeywords.Any(k => leafUpName.Contains(k)) ||
                 System.Text.RegularExpressions.Regex.IsMatch(leafUpName, @"^C\s*\d{3,4}$")) && !isTvOrCabinet)
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Cửa Đi",
                    SuggestedLayer = CadLayoutProcessorService.LayerCua,
                    Reason = string.Format("Block cửa kiến trúc '{0}' ({1:F0}x{2:F0}mm)", leafUpName, w, h),
                    Confidence = 0.94
                };
            }

            if (ColumnKeywords.Any(k => leafUpName.Contains(k)))
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Cột BTCT",
                    SuggestedLayer = CadLayoutProcessorService.LayerCot,
                    Reason = string.Format("Block cột kết cấu '{0}' ({1:F0}x{2:F0}mm)", leafUpName, w, h),
                    Confidence = 0.95,
                    IsStructuralColumn = true
                };
            }

            // 7. Cửa Lùa / Cửa Trượt & Cửa Sổ Kính theo Hình học nội tại (Block ẩn danh *U... không có cung Arc)
            if (stats.ArcCount == 0 && stats.BigCircleCount == 0 && stats.EllipseCount == 0 && stats.SplineCount == 0 &&
                minD >= 75.0 && minD <= 320.0 && maxD >= 900.0)
            {
                if (stats.SlidingLeafParallelPairs >= 2 && stats.HatchCount == 0)
                {
                    return new KnowledgeClassificationResult
                    {
                        EntityType = "Block Cửa Lùa (Hình Học)",
                        SuggestedLayer = CadLayoutProcessorService.LayerCua,
                        Reason = string.Format("Block '{0}' ({1:F0}x{2:F0}mm) có cặp cánh trượt song song đặc trưng Cửa Lùa", blkName, w, h),
                        Confidence = 0.90
                    };
                }

                if (stats.HatchCount >= 1 || stats.LineCount >= 6)
                {
                    return new KnowledgeClassificationResult
                    {
                        EntityType = "Block Cửa Sổ / Vách Kính",
                        SuggestedLayer = CadLayoutProcessorService.LayerVachKinh,
                        Reason = string.Format("Block '{0}' ({1:F0}x{2:F0}mm) dạng khung kính kẹp (WALL_WINDOW_BLOCK v27)", blkName, w, h),
                        Confidence = 0.88
                    };
                }
            }

            // 8. Block Cột Kết Cấu theo Hình Học (Có Hatch/Solid, tiết diện vuông/chữ nhật gọn, không cong)
            if ((stats.SolidCount >= 1 || stats.HatchCount >= 1) &&
                stats.ArcCount == 0 && stats.EllipseCount == 0 && stats.SplineCount == 0 &&
                minD >= 140.0 && maxD <= 1500.0 && maxD / Math.Max(minD, 1.0) <= 4.5)
            {
                return new KnowledgeClassificationResult
                {
                    EntityType = "Block Cột Kết Cấu (Hình Học)",
                    SuggestedLayer = CadLayoutProcessorService.LayerCot,
                    Reason = string.Format("Block '{0}' tiết diện {1:F0}x{2:F0}mm chứa Solid/Hatch đặc trưng Cột", blkName, w, h),
                    Confidence = 0.90,
                    IsStructuralColumn = true
                };
            }

            // 9. Block Nội Thất / Thiết Bị Vệ Sinh (v27 & 3.036 mẫu đã học)
            // Nếu KHÔNG khớp từ điển 3.036 Block đã học → đánh dấu Đối Tượng Mới Ngoài Vùng Tri Thức để báo kỹ sư kiểm tra!
            bool inFurnitureDict = _furnitureBlockNames.Contains(blkName) || _furnitureBlockNames.Contains(leafUpName);
            bool hasFurnShapeHint = stats.EllipseCount > 0 || stats.SplineCount > 0 || stats.BigCircleCount > 0 || stats.WipeoutCount > 0;
            string shapeNote = hasFurnShapeHint
                ? "chứa Ellipse/Spline/Circle (ngoài từ điển Block)"
                : (inFurnitureDict ? "khớp từ điển 3.036 Block Nội thất" : string.Format("Block mới chưa có trong tri thức ({0:F0}x{1:F0}mm)", w, h));

            return new KnowledgeClassificationResult
            {
                EntityType = inFurnitureDict ? "Block Nội Thất (Đã Học)" : "Block Mới Ngoài Tri Thức",
                SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                Reason = string.Format("Block '{0}' ({1})", blkName, shapeNote),
                Confidence = inFurnitureDict ? 0.95 : (hasFurnShapeHint ? 0.80 : 0.75),
                IsOutsideKnowledge = !inFurnitureDict
            };
        }

        private static DoorWindowTemplateEntry FindV35DoorWindowMatch(string upName, double w, double h, BlockGeometryStats stats)
        {
            // 1. Khớp theo tên block / family prefix
            foreach (var entry in _v35DoorWindowTemplates)
            {
                string keyUp = entry.Key.ToUpperInvariant();
                if (keyUp == upName) return entry;

                if (!upName.StartsWith("*U") && !upName.StartsWith("A$"))
                {
                    string cleanUp = entry.CleanFamilyPrefix.ToUpperInvariant();
                    if (cleanUp.Length >= 5 && (upName.Contains(cleanUp) || cleanUp.Contains(upName)))
                    {
                        return entry;
                    }
                }
            }

            // 2. Khớp theo Dấu vân tay Hình học (kích thước + tỷ lệ Line/Arc/Hatch) cho Block ẩn danh *U...
            if (stats.BigCircleCount == 0 && stats.EllipseCount == 0 && stats.SplineCount == 0 && w >= 100 && h >= 100)
            {
                double minD = Math.Min(w, h);
                double maxD = Math.Max(w, h);

                foreach (var entry in _v35DoorWindowTemplates)
                {
                    double eMin = Math.Min(entry.Width, entry.Height);
                    double eMax = Math.Max(entry.Width, entry.Height);
                    if (Math.Abs(minD - eMin) <= 15.0 && Math.Abs(maxD - eMax) <= 20.0)
                    {
                        bool arcCompatible = (entry.ArcCount == 0 && stats.ArcCount == 0) || (entry.ArcCount > 0 && stats.ArcCount > 0);
                        if (arcCompatible && stats.TotalEntities >= 10)
                        {
                            return entry;
                        }
                    }
                }
            }

            return null;
        }

        public static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            if (br == null) return "";
            string blkName = br.Name ?? "";
            if (br.IsDynamicBlock && tr != null)
            {
                try
                {
                    BlockTableRecord dynBtr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (dynBtr != null && !string.IsNullOrEmpty(dynBtr.Name))
                    {
                        blkName = dynBtr.Name;
                    }
                }
                catch { }
            }
            return blkName;
        }

        // Cache thống kê hình học theo định nghĩa block (+ tỷ lệ/lật): mỗi block chỉ bóc tách 1 lần.
        private static readonly Dictionary<string, BlockGeometryStats> _blockStatsCache = new Dictionary<string, BlockGeometryStats>();
        private static Database _anchorCacheDb;
        private static int _anchorCacheCount = -1;
        private static string _anchorCacheBounds = "";
        private static List<Extents3d> _anchorCache;

        public static void ResetScanCaches()
        {
            _blockStatsCache.Clear();
            _anchorCache = null;
            _anchorCacheDb = null;
            _anchorCacheCount = -1;
        }

        private static BlockGeometryStats ExtractBlockGeometryStats(BlockReference br, Transaction tr)
        {
            string cacheKey = string.Format("{0}|{1:F3}|{2:F3}|{3}",
                br.BlockTableRecord.Handle.Value, br.ScaleFactors.X, br.ScaleFactors.Y, br.IsDynamicBlock ? br.DynamicBlockTableRecord.Handle.Value : 0L);

            BlockGeometryStats cached;
            BlockGeometryStats stats;
            if (_blockStatsCache.TryGetValue(cacheKey, out cached))
            {
                stats = cached.CloneStats();
            }
            else
            {
                stats = ComputeBlockGeometryStats(br, tr);
                _blockStatsCache[cacheKey] = stats.CloneStats();
            }

            // Thuộc tính (nhãn trục) là riêng của từng bản sao nên luôn đọc lại (rất nhẹ)
            foreach (ObjectId attId in br.AttributeCollection)
            {
                try
                {
                    AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null)
                    {
                        stats.TextOrAttribCount++;
                        string t = (att.TextString ?? "").Trim();
                        if (t.Length >= 1 && t.Length <= 3) stats.ContainsShortGridLabel = true;
                    }
                }
                catch { }
            }
            return stats;
        }

        private static BlockGeometryStats ComputeBlockGeometryStats(BlockReference br, Transaction tr)
        {
            BlockGeometryStats stats = new BlockGeometryStats();
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            List<Tuple<Point2d, Point2d>> internalSegs = new List<Tuple<Point2d, Point2d>>();

            // Lấy scale thực tế của BlockReference (bỏ qua Rotation để giữ chuẩn W x H cục bộ khi cửa xoay xéo góc)
            double sx = Math.Abs(br.ScaleFactors.X);
            double sy = Math.Abs(br.ScaleFactors.Y);
            if (sx < 1e-4) sx = 1.0;
            if (sy < 1e-4) sy = 1.0;
            Matrix3d localScaleMat = new Matrix3d(new double[]
            {
                sx, 0, 0, 0,
                0, sy, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1
            });

            if (br.ScaleFactors.X < 0)
            {
                localScaleMat = Matrix3d.Mirroring(new Line3d(Point3d.Origin, Vector3d.YAxis)) * localScaleMat;
            }
            if (br.ScaleFactors.Y < 0)
            {
                localScaleMat = Matrix3d.Mirroring(new Line3d(Point3d.Origin, Vector3d.XAxis)) * localScaleMat;
            }

            // (Thuộc tính nhãn trục được đọc riêng từng bản sao ở ExtractBlockGeometryStats)

            ObjectId btrId = br.BlockTableRecord;
            if (!btrId.IsNull)
            {
                InspectBlockTableRecordRecursive(btrId, localScaleMat, tr, stats, internalSegs, ref minX, ref minY, ref maxX, ref maxY, 0);
            }

            if (minX <= maxX && minY <= maxY)
            {
                stats.LocalWidth = Math.Abs(maxX - minX);
                stats.LocalHeight = Math.Abs(maxY - minY);
            }
            else
            {
                try
                {
                    Extents3d ext = br.GeometricExtents;
                    stats.LocalWidth = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                    stats.LocalHeight = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                }
                catch { }
            }

            // Đếm cặp đoạn thẳng song song cách đều 20..90mm (dấu hiệu cánh cửa lùa)
            stats.SlidingLeafParallelPairs = CountParallelSlidingPairs(internalSegs);

            return stats;
        }

        private static void InspectBlockTableRecordRecursive(
            ObjectId btrId, Matrix3d transform, Transaction tr,
            BlockGeometryStats stats, List<Tuple<Point2d, Point2d>> internalSegs,
            ref double minX, ref double minY, ref double maxX, ref double maxY, int depth)
        {
            if (depth > 3 || btrId.IsNull) return;
            BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return;

            double scaleEst = GetMatrixScaleEstimate(transform);

            foreach (ObjectId id in btr)
            {
                Entity sub = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (sub == null || !sub.Visible) continue;

                if (sub is BlockReference)
                {
                    BlockReference nested = (BlockReference)sub;
                    Matrix3d nestedMat = transform * nested.BlockTransform;
                    InspectBlockTableRecordRecursive(nested.BlockTableRecord, nestedMat, tr, stats, internalSegs, ref minX, ref minY, ref maxX, ref maxY, depth + 1);
                    continue;
                }

                if (sub is Line)
                {
                    stats.LineCount++;
                    Line ln = (Line)sub;
                    Point3d p1 = ln.StartPoint.TransformBy(transform);
                    Point3d p2 = ln.EndPoint.TransformBy(transform);
                    IncludePoint(p1.X, p1.Y, ref minX, ref minY, ref maxX, ref maxY);
                    IncludePoint(p2.X, p2.Y, ref minX, ref minY, ref maxX, ref maxY);

                    double len = p1.DistanceTo(p2);
                    if (len >= 400.0)
                    {
                        internalSegs.Add(Tuple.Create(new Point2d(p1.X, p1.Y), new Point2d(p2.X, p2.Y)));
                        if (len <= 1350.0) stats.DoorLeafLikeCount++;
                    }
                }
                else if (sub is Arc)
                {
                    stats.ArcCount++;
                    Arc arc = (Arc)sub;
                    double r = arc.Radius * scaleEst;
                    double sweep = arc.EndAngle - arc.StartAngle;
                    if (sweep < 0) sweep += 2.0 * Math.PI;
                    double sweepDeg = sweep * 180.0 / Math.PI;

                    if (r >= 420.0 && r <= 1450.0 && sweepDeg >= 30.0 && sweepDeg <= 120.0)
                    {
                        stats.DoorSwingArcCount++;
                        if (r > stats.MaxDoorArcRadius) stats.MaxDoorArcRadius = r;
                    }

                    // Lấy mẫu 5 điểm trên cung để cập nhật đúng Bounding Box khối block
                    for (int s = 0; s <= 4; s++)
                    {
                        double ang = arc.StartAngle + (sweep * s) / 4.0;
                        Point3d pt = new Point3d(
                            arc.Center.X + arc.Radius * Math.Cos(ang),
                            arc.Center.Y + arc.Radius * Math.Sin(ang), 0).TransformBy(transform);
                        IncludePoint(pt.X, pt.Y, ref minX, ref minY, ref maxX, ref maxY);
                    }
                }
                else if (sub is Polyline)
                {
                    stats.PolylineCount++;
                    Polyline pl = (Polyline)sub;
                    int nv = pl.NumberOfVertices;
                    double pMinX = double.MaxValue, pMinY = double.MaxValue, pMaxX = double.MinValue, pMaxY = double.MinValue;

                    for (int v = 0; v < nv; v++)
                    {
                        Point2d v2 = pl.GetPoint2dAt(v);
                        Point3d vp = new Point3d(v2.X, v2.Y, 0).TransformBy(transform);
                        IncludePoint(vp.X, vp.Y, ref minX, ref minY, ref maxX, ref maxY);
                        IncludePoint(vp.X, vp.Y, ref pMinX, ref pMinY, ref pMaxX, ref pMaxY);

                        int nextIdx = (v + 1 < nv) ? v + 1 : (pl.Closed ? 0 : -1);
                        if (nextIdx >= 0)
                        {
                            Point2d n2 = pl.GetPoint2dAt(nextIdx);
                            Point3d np = new Point3d(n2.X, n2.Y, 0).TransformBy(transform);
                            double bulge = pl.GetBulgeAt(v);
                            if (Math.Abs(bulge) > 0.08)
                            {
                                double chord = Math.Sqrt(Math.Pow(np.X - vp.X, 2) + Math.Pow(np.Y - vp.Y, 2));
                                double absB = Math.Abs(bulge);
                                double r = chord * (1.0 + absB * absB) / (4.0 * absB);
                                double sweepDeg = 4.0 * Math.Atan(absB) * 180.0 / Math.PI;
                                if (r >= 420.0 && r <= 1450.0 && sweepDeg >= 30.0 && sweepDeg <= 120.0)
                                {
                                    stats.ArcCount++;
                                    stats.DoorSwingArcCount++;
                                    if (r > stats.MaxDoorArcRadius) stats.MaxDoorArcRadius = r;
                                }
                            }
                            else
                            {
                                double segLen = vp.DistanceTo(np);
                                if (segLen >= 400.0)
                                {
                                    internalSegs.Add(Tuple.Create(new Point2d(vp.X, vp.Y), new Point2d(np.X, np.Y)));
                                }
                            }
                        }
                    }

                    double pw = Math.Abs(pMaxX - pMinX);
                    double ph = Math.Abs(pMaxY - pMinY);
                    if (Math.Min(pw, ph) >= 20.0 && Math.Min(pw, ph) <= 70.0 && Math.Max(pw, ph) >= 450.0 && Math.Max(pw, ph) <= 1350.0)
                    {
                        stats.DoorLeafLikeCount++;
                    }
                }
                else if (sub is Circle)
                {
                    Circle circ = (Circle)sub;
                    double r = circ.Radius * scaleEst;
                    if (r > 80.0) stats.BigCircleCount++;
                    else stats.SmallCircleCount++;

                    Point3d c = circ.Center.TransformBy(transform);
                    IncludePoint(c.X - r, c.Y - r, ref minX, ref minY, ref maxX, ref maxY);
                    IncludePoint(c.X + r, c.Y + r, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (sub is Ellipse)
                {
                    stats.EllipseCount++;
                    TryIncludeExtents(sub, transform, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (sub is Spline)
                {
                    stats.SplineCount++;
                    TryIncludeExtents(sub, transform, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (sub is Wipeout)
                {
                    stats.WipeoutCount++;
                }
                else if (sub is Hatch)
                {
                    stats.HatchCount++;
                    TryIncludeExtents(sub, transform, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (sub is Solid)
                {
                    stats.SolidCount++;
                    TryIncludeExtents(sub, transform, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (sub is DBText || sub is MText)
                {
                    stats.TextOrAttribCount++;
                    string t = (sub is DBText) ? ((DBText)sub).TextString : ((MText)sub).Contents;
                    t = (t ?? "").Trim();
                    if (t.Length >= 1 && t.Length <= 3) stats.ContainsShortGridLabel = true;
                }
            }
        }

        private static int CountParallelSlidingPairs(List<Tuple<Point2d, Point2d>> segs)
        {
            if (segs.Count < 2 || segs.Count > 250) return 0;
            int pairs = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                Vector2d d1 = segs[i].Item2 - segs[i].Item1;
                double l1 = d1.Length;
                if (l1 < 400.0) continue;
                Vector2d u1 = d1 / l1;
                Vector2d n1 = new Vector2d(-u1.Y, u1.X);

                for (int j = i + 1; j < segs.Count; j++)
                {
                    Vector2d d2 = segs[j].Item2 - segs[j].Item1;
                    double l2 = d2.Length;
                    if (l2 < 400.0) continue;
                    Vector2d u2 = d2 / l2;
                    if (Math.Abs(u1.X * u2.X + u1.Y * u2.Y) < 0.995) continue;

                    Vector2d v12 = segs[j].Item1 - segs[i].Item1;
                    double perp = Math.Abs(v12.X * n1.X + v12.Y * n1.Y);
                    if (perp >= 15.0 && perp <= 90.0)
                    {
                        double proj = v12.X * u1.X + v12.Y * u1.Y;
                        double projEnd = (segs[j].Item2 - segs[i].Item1).X * u1.X + (segs[j].Item2 - segs[i].Item1).Y * u1.Y;
                        double overlap = Math.Min(l1, Math.Max(proj, projEnd)) - Math.Max(0.0, Math.Min(proj, projEnd));
                        if (overlap >= 300.0) pairs++;
                    }
                }
            }
            return pairs;
        }

        /// <summary>
        /// Thu thập toàn bộ Bounding Box của các Cột kết cấu thực thụ trong bản vẽ dùng làm Mỏ Neo (Structural Anchors) cho thuật toán nhận diện Tường.
        /// </summary>
        public static List<Extents3d> ExtractStructuralColumnAnchors(Database db, Transaction tr, Extents3d? bounds)
        {
            if (db == null || tr == null) return new List<Extents3d>();

            // Cache: chỉ tính lại khi đổi bản vẽ / số đối tượng / phạm vi
            int entCount = 0;
            BlockTableRecord msCount = tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead) as BlockTableRecord;
            if (msCount != null) foreach (ObjectId cid in msCount) entCount++;
            string bKey = bounds.HasValue
                ? string.Format("{0:F0},{1:F0},{2:F0},{3:F0}", bounds.Value.MinPoint.X, bounds.Value.MinPoint.Y, bounds.Value.MaxPoint.X, bounds.Value.MaxPoint.Y)
                : "all";
            if (_anchorCache != null && ReferenceEquals(_anchorCacheDb, db) && _anchorCacheCount == entCount && _anchorCacheBounds == bKey)
            {
                return new List<Extents3d>(_anchorCache);
            }

            List<Extents3d> computed = ComputeStructuralColumnAnchors(db, tr, bounds);
            _anchorCache = new List<Extents3d>(computed);
            _anchorCacheDb = db;
            _anchorCacheCount = entCount;
            _anchorCacheBounds = bKey;
            return computed;
        }

        private static List<Extents3d> ComputeStructuralColumnAnchors(Database db, Transaction tr, Extents3d? bounds)
        {
            List<Extents3d> anchors = new List<Extents3d>();
            if (db == null || tr == null) return anchors;

            BlockTableRecord ms = tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead) as BlockTableRecord;
            if (ms == null) return anchors;

            foreach (ObjectId id in ms)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                string layUp = (ent.Layer ?? "").ToUpperInvariant();
                if (layUp == CadLayoutProcessorService.LayerKhungBao || layUp == CadLayoutProcessorService.LayerDiemNoi || layUp.StartsWith("DUONG_BAO")) continue;

                Extents3d ext;
                try { ext = ent.GeometricExtents; }
                catch { continue; }

                if (bounds.HasValue)
                {
                    if (ext.MaxPoint.X < bounds.Value.MinPoint.X || ext.MinPoint.X > bounds.Value.MaxPoint.X ||
                        ext.MaxPoint.Y < bounds.Value.MinPoint.Y || ext.MinPoint.Y > bounds.Value.MaxPoint.Y)
                        continue;
                }

                double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                if (Math.Max(w, h) < 100.0) continue;

                if (ent is Solid && Math.Min(w, h) >= 120.0 && Math.Max(w, h) <= 3500.0)
                {
                    anchors.Add(ext);
                    continue;
                }

                if (ent is Hatch)
                {
                    Hatch hat = (Hatch)ent;
                    string patUp = (hat.PatternName ?? "").ToUpperInvariant();
                    // Chỉ nhận Hatch SOLID hoặc bê tông (CONC) làm Cột kết cấu; loại bỏ ANSI31/BRICK/AR- (Hatch tường xây gạch)
                    // và kiểm tra tiết diện gọn (<= 6x tỷ lệ) để không nhận nhầm Hatch cả hệ tường chữ U/L làm Cột.
                    if (patUp == "SOLID" || patUp.Contains("CONC"))
                    {
                        List<Extents3d> loopBoxes = ExtractHatchLoopBoxes(hat, ext, true);
                        foreach (var lb in loopBoxes)
                        {
                            double lw = Math.Abs(lb.MaxPoint.X - lb.MinPoint.X);
                            double lh = Math.Abs(lb.MaxPoint.Y - lb.MinPoint.Y);
                            double lMin = Math.Min(lw, lh);
                            double lMax = Math.Max(lw, lh);
                            if (lMin >= 140.0 && lMin <= 900.0 && lMax >= 140.0 && lMax <= 3500.0 && lMax / lMin <= 15.0)
                            {
                                anchors.Add(lb);
                            }
                        }
                    }
                    continue;
                }

                if (ent is BlockReference)
                {
                    var blkRes = ClassifyBlockReference((BlockReference)ent, tr);
                    if (blkRes != null && blkRes.IsStructuralColumn)
                    {
                        anchors.Add(ext);
                    }
                    continue;
                }
            }

            return anchors;
        }

        public static List<Tuple<Point2d, Point2d>> ExtractHatchLoopEdges(Hatch hat)
        {
            List<Tuple<Point2d, Point2d>> edges = new List<Tuple<Point2d, Point2d>>();
            if (hat == null) return edges;
            try
            {
                int nLoops = hat.NumberOfLoops;
                for (int li = 0; li < nLoops; li++)
                {
                    try
                    {
                        HatchLoop loop = hat.GetLoopAt(li);
                        if (loop == null) continue;
                        if (loop.IsPolyline && loop.Polyline != null)
                        {
                            int n = loop.Polyline.Count;
                            for (int v = 0; v < n; v++)
                            {
                                int nxt = (v + 1) % n;
                                Point2d p1 = loop.Polyline[v].Vertex;
                                Point2d p2 = loop.Polyline[nxt].Vertex;
                                if (p1.GetDistanceTo(p2) >= 40.0)
                                    edges.Add(Tuple.Create(p1, p2));
                            }
                        }
                        else if (loop.Curves != null)
                        {
                            foreach (Curve2d c2d in loop.Curves)
                            {
                                LineSegment2d ls = c2d as LineSegment2d;
                                if (ls != null && ls.Length >= 40.0)
                                {
                                    edges.Add(Tuple.Create(ls.StartPoint, ls.EndPoint));
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return edges;
        }

        public static List<Extents3d> ExtractHatchLoopBoxes(Hatch hat, Extents3d fallbackExt, bool compactPolygonOnly = false)
        {
            List<Extents3d> res = new List<Extents3d>();
            if (hat == null) return res;
            try
            {
                int nLoops = hat.NumberOfLoops;
                for (int li = 0; li < nLoops; li++)
                {
                    try
                    {
                        HatchLoop loop = hat.GetLoopAt(li);
                        if (loop == null) continue;

                        if (compactPolygonOnly)
                        {
                            int vCnt = loop.IsPolyline && loop.Polyline != null
                                ? loop.Polyline.Count
                                : (loop.Curves != null ? loop.Curves.Count : 0);
                            if (vCnt > 12) continue;
                        }

                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;
                        if (loop.IsPolyline && loop.Polyline != null)
                        {
                            foreach (BulgeVertex bv in loop.Polyline)
                            {
                                if (bv.Vertex.X < minX) minX = bv.Vertex.X;
                                if (bv.Vertex.Y < minY) minY = bv.Vertex.Y;
                                if (bv.Vertex.X > maxX) maxX = bv.Vertex.X;
                                if (bv.Vertex.Y > maxY) maxY = bv.Vertex.Y;
                            }
                        }
                        else if (loop.Curves != null)
                        {
                            foreach (Curve2d c2d in loop.Curves)
                            {
                                if (c2d == null) continue;
                                try
                                {
                                    BoundBlock2d bb = c2d.OrthoBoundBlock;
                                    Point2d pMin = bb.GetMinimumPoint();
                                    Point2d pMax = bb.GetMaximumPoint();
                                    if (pMin.X < minX) minX = pMin.X;
                                    if (pMin.Y < minY) minY = pMin.Y;
                                    if (pMax.X > maxX) maxX = pMax.X;
                                    if (pMax.Y > maxY) maxY = pMax.Y;
                                }
                                catch { }
                            }
                        }

                        if (minX < maxX && minY < maxY)
                        {
                            res.Add(new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0)));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (res.Count == 0 && !compactPolygonOnly)
            {
                res.Add(fallbackExt);
            }
            return res;
        }

        public static bool BoxesOverlapInterior2D(Extents3d a, Extents3d b, double minInteriorOverlap = 50.0)
        {
            double ox = Math.Min(a.MaxPoint.X, b.MaxPoint.X) - Math.Max(a.MinPoint.X, b.MinPoint.X);
            double oy = Math.Min(a.MaxPoint.Y, b.MaxPoint.Y) - Math.Max(a.MinPoint.Y, b.MinPoint.Y);
            return ox >= minInteriorOverlap && oy >= minInteriorOverlap;
        }

        public static double DistancePointToSegment2D(Point2d p, Point2d a, Point2d b)
        {
            Vector2d ab = b - a;
            double lenSq = ab.X * ab.X + ab.Y * ab.Y;
            if (lenSq < 1e-6) return p.GetDistanceTo(a);
            double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            Point2d proj = new Point2d(a.X + t * ab.X, a.Y + t * ab.Y);
            return p.GetDistanceTo(proj);
        }

        public static double DistanceSegmentToSegment2D(Point2d a1, Point2d a2, Point2d b1, Point2d b2)
        {
            if (SegmentsIntersect2D(a1, a2, b1, b2)) return 0.0;
            double d1 = DistancePointToSegment2D(a1, b1, b2);
            double d2 = DistancePointToSegment2D(a2, b1, b2);
            double d3 = DistancePointToSegment2D(b1, a1, a2);
            double d4 = DistancePointToSegment2D(b2, a1, a2);
            return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
        }

        public static double DistanceSegmentToBox2D(Point2d p1, Point2d p2, Extents3d box)
        {
            double minX = box.MinPoint.X, maxX = box.MaxPoint.X;
            double minY = box.MinPoint.Y, maxY = box.MaxPoint.Y;

            // Nằm gọn bên trong hộp
            if (p1.X >= minX && p1.X <= maxX && p1.Y >= minY && p1.Y <= maxY) return 0.0;
            if (p2.X >= minX && p2.X <= maxX && p2.Y >= minY && p2.Y <= maxY) return 0.0;

            Point2d c1 = new Point2d(minX, minY);
            Point2d c2 = new Point2d(maxX, minY);
            Point2d c3 = new Point2d(maxX, maxY);
            Point2d c4 = new Point2d(minX, maxY);

            double dTop = DistanceSegmentToSegment2D(p1, p2, c4, c3);
            double dBot = DistanceSegmentToSegment2D(p1, p2, c1, c2);
            double dLeft = DistanceSegmentToSegment2D(p1, p2, c1, c4);
            double dRight = DistanceSegmentToSegment2D(p1, p2, c2, c3);
            return Math.Min(Math.Min(dTop, dBot), Math.Min(dLeft, dRight));
        }

        private static bool SegmentsIntersect2D(Point2d a1, Point2d a2, Point2d b1, Point2d b2)
        {
            double o1 = Cross2D(a1, a2, b1);
            double o2 = Cross2D(a1, a2, b2);
            double o3 = Cross2D(b1, b2, a1);
            double o4 = Cross2D(b1, b2, a2);
            return (o1 * o2 < 0.0) && (o3 * o4 < 0.0);
        }

        private static double Cross2D(Point2d o, Point2d a, Point2d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>
        /// Chẩn đoán tức thì cụm đối tượng đang chọn trên AutoCAD cho LIVE CAD INSPECTOR.
        /// Áp dụng đồng bộ quy tắc Cửa (theo tri thức đã học) và Tường tiếp giáp Cột.
        /// </summary>
        public static LiveSelectionDiagnosis DiagnoseSelection(Document doc, IEnumerable<ObjectId> selectedIds)
        {
            if (doc == null || selectedIds == null) return null;
            List<ObjectId> idList = selectedIds.Where(id => !id.IsNull && !id.IsErased).ToList();
            if (idList.Count == 0) return null;

            EnsureKnowledgeLoaded();
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<Entity> ents = new List<Entity>();
                HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in idList)
                {
                    Entity e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e != null)
                    {
                        ents.Add(e);
                        if (!string.IsNullOrEmpty(e.Layer)) layers.Add(e.Layer);
                    }
                }
                if (ents.Count == 0) return null;

                string curLayDisplay = layers.Count == 1 ? layers.First() : string.Format("Đa layer ({0})", layers.Count);

                // 1. Chọn duy nhất 1 BlockReference -> Chẩn đoán sâu hình học nội tại & Tri thức v27/v35/v36
                if (ents.Count == 1 && ents[0] is BlockReference)
                {
                    var res = ClassifyBlockReference((BlockReference)ents[0], tr);
                    tr.Commit();
                    return new LiveSelectionDiagnosis
                    {
                        SuggestedLayer = res.SuggestedLayer,
                        EntityType = res.EntityType,
                        Reason = res.Reason,
                        CurrentLayer = curLayDisplay,
                        IsLayerMismatch = !string.Equals(curLayDisplay, res.SuggestedLayer, StringComparison.OrdinalIgnoreCase)
                    };
                }

                // 2. Chọn toàn BlockReference cùng loại
                if (ents.All(e => e is BlockReference))
                {
                    var results = ents.Select(e => ClassifyBlockReference((BlockReference)e, tr)).ToList();
                    var topGroup = results.GroupBy(r => r.SuggestedLayer).OrderByDescending(g => g.Count()).First();
                    var rep = topGroup.First();
                    tr.Commit();
                    return new LiveSelectionDiagnosis
                    {
                        SuggestedLayer = topGroup.Key,
                        EntityType = string.Format("{0} ({1} Block)", rep.EntityType, ents.Count),
                        Reason = rep.Reason,
                        CurrentLayer = curLayDisplay,
                        IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, topGroup.Key, StringComparison.OrdinalIgnoreCase)
                    };
                }

                // 3. Kiểm tra Cung xoay cửa (Arc R=480..1450)
                if (ents.Any(e => e is Arc && ((Arc)e).Radius >= 480.0 && ((Arc)e).Radius <= 1450.0))
                {
                    Arc arc = (Arc)ents.First(e => e is Arc && ((Arc)e).Radius >= 480.0 && ((Arc)e).Radius <= 1450.0);
                    tr.Commit();
                    return new LiveSelectionDiagnosis
                    {
                        SuggestedLayer = CadLayoutProcessorService.LayerCua,
                        EntityType = "Cụm Nét Cửa Đi",
                        Reason = string.Format("Chứa cung xoay cửa R={0:F0}mm (Cửa đi mở quay -> AI_XD_CUA)", arc.Radius),
                        CurrentLayer = curLayDisplay,
                        IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerCua, StringComparison.OrdinalIgnoreCase)
                    };
                }

                // 4. Chọn cụm Line / Polyline (Ví dụ: cụm 22 nét nan lam hoặc cặp tường song song)
                if (ents.All(e => e is Line || e is Polyline))
                {
                    List<Tuple<Point2d, Point2d>> segs = new List<Tuple<Point2d, Point2d>>();
                    foreach (var ent in ents)
                    {
                        if (ent is Line)
                        {
                            Line ln = (Line)ent;
                            if (ln.Length >= 30.0) segs.Add(Tuple.Create(new Point2d(ln.StartPoint.X, ln.StartPoint.Y), new Point2d(ln.EndPoint.X, ln.EndPoint.Y)));
                        }
                        else if (ent is Polyline)
                        {
                            Polyline pl = (Polyline)ent;
                            for (int v = 0; v < pl.NumberOfVertices; v++)
                            {
                                int nxt = (v + 1 < pl.NumberOfVertices) ? v + 1 : (pl.Closed ? 0 : -1);
                                if (nxt < 0) continue;
                                Point2d p1 = pl.GetPoint2dAt(v), p2 = pl.GetPoint2dAt(nxt);
                                if (p1.GetDistanceTo(p2) >= 30.0) segs.Add(Tuple.Create(p1, p2));
                            }
                        }
                    }

                    // Kiểm tra kiểu nét Trục (CENTER/DASHDOT/AXIS/GRID) trước — nét Trục định vị thực sự dùng kiểu nét tâm
                    bool anyGridLinetype = ents.Any(e =>
                    {
                        string lt = (e.Linetype ?? "").ToUpperInvariant();
                        return lt.Contains("CENTER") || lt.Contains("DASHDOT") || lt.Contains("AXIS") || lt.Contains("GRID");
                    });

                    // Trích xuất mỏ neo Cột toàn bản vẽ để kiểm chứng tiếp giáp Cột & Hatch Cột
                    List<Extents3d> anchors = ExtractStructuralColumnAnchors(db, tr, null);

                    // 4a. Nếu chọn Polyline khép kín tiết diện gọn (Hộp nội thất rỗng vs Cột BTCT có Hatch)
                    if (ents.All(e => e is Polyline && ((Polyline)e).Closed))
                    {
                        Polyline pl0 = (Polyline)ents[0];
                        try
                        {
                            Extents3d pe = pl0.GeometricExtents;
                            double pw = Math.Abs(pe.MaxPoint.X - pe.MinPoint.X);
                            double ph = Math.Abs(pe.MaxPoint.Y - pe.MinPoint.Y);
                            double pMin = Math.Min(pw, ph);
                            double pMax = Math.Max(pw, ph);
                            bool hasColumnHatch = anchors.Any(a => BoxesOverlapInterior2D(a, pe, 50.0));

                            if (hasColumnHatch && pMin >= 140.0 && pMin <= 900.0 && pMax <= 4500.0)
                            {
                                tr.Commit();
                                return new LiveSelectionDiagnosis
                                {
                                    SuggestedLayer = CadLayoutProcessorService.LayerCot,
                                    EntityType = "Cột / Vách BTCT (Có Hatch)",
                                    Reason = string.Format("Khối khép kín {0:F0}x{1:F0}mm chứa Hatch/Solid Cột chịu lực -> AI_XD_COT", pw, ph),
                                    CurrentLayer = curLayDisplay,
                                    IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerCot, StringComparison.OrdinalIgnoreCase)
                                };
                            }

                            if (!hasColumnHatch && pMin >= 240.0 && pMin <= 1250.0 && pMax >= 240.0 && pMax <= 2500.0)
                            {
                                tr.Commit();
                                return new LiveSelectionDiagnosis
                                {
                                    SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                                    EntityType = "Hộp Nội Thất / Tủ Kệ Rỗng",
                                    Reason = string.Format("Khối chữ nhật rỗng {0:F0}x{1:F0}mm (không có Hatch cột đặc) -> AI_XD_NOI_THAT", pw, ph),
                                    CurrentLayer = curLayDisplay,
                                    IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerNoiThat, StringComparison.OrdinalIgnoreCase)
                                };
                            }
                        }
                        catch { }
                    }

                    // Kiểm tra cặp Tường (95-400mm) TRƯỚC khi xét đường 1 nét dài (để tường bao 7m-15m không bị cắt nhầm làm Grid!)
                    int denseParallelCount = CountCloselySpacedParallelBars(segs);
                    bool hasNarrowFurniturePair = HasParallelWallPair(segs, 18.0, 85.0);
                    bool touchesAnyColumn = anchors.Any(col => segs.Any(s => DistanceSegmentToBox2D(s.Item1, s.Item2, col) <= 100.0));
                    bool hasWallThicknessPair = HasParallelWallPair(segs, 95.0, 400.0);
                    double maxSelLen = segs.Count > 0 ? segs.Max(s => s.Item1.GetDistanceTo(s.Item2)) : 0.0;

                    tr.Commit();

                    if (anyGridLinetype && !hasWallThicknessPair)
                    {
                        return new LiveSelectionDiagnosis
                        {
                            SuggestedLayer = CadLayoutProcessorService.LayerGrid,
                            EntityType = "Đường Trục Grid (Kiểu nét Trục)",
                            Reason = string.Format("Đường nét tâm Trục định vị (L={0:F0}mm) -> AI_XD_GRID", maxSelLen),
                            CurrentLayer = curLayDisplay,
                            IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerGrid, StringComparison.OrdinalIgnoreCase)
                        };
                    }

                    if (denseParallelCount >= 3)
                    {
                        return new LiveSelectionDiagnosis
                        {
                            SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                            EntityType = "Cụm Cầu Thang / Nan Lam / Móc Áo",
                            Reason = string.Format("Phát hiện {0} thanh song song xếp liên tiếp -> Nội Thất / Cầu thang (AI_XD_NOI_THAT)", denseParallelCount),
                            CurrentLayer = curLayDisplay,
                            IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerNoiThat, StringComparison.OrdinalIgnoreCase)
                        };
                    }

                    if (hasNarrowFurniturePair && !hasWallThicknessPair)
                    {
                        return new LiveSelectionDiagnosis
                        {
                            SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                            EntityType = "Thiết Bị Mảnh / TV / Kệ (20-85mm)",
                            Reason = "Cặp nét song song mảnh (20-85mm: TV treo tường, kệ, móc áo) -> AI_XD_NOI_THAT",
                            CurrentLayer = curLayDisplay,
                            IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerNoiThat, StringComparison.OrdinalIgnoreCase)
                        };
                    }

                    if (hasWallThicknessPair && (touchesAnyColumn || anchors.Count == 0 || maxSelLen >= 1500.0))
                    {
                        return new LiveSelectionDiagnosis
                        {
                            SuggestedLayer = CadLayoutProcessorService.LayerTuong,
                            EntityType = "Cặp Tường Kiến Trúc",
                            Reason = touchesAnyColumn
                                ? "Đường song song dày tường (95-400mm) có tiếp giáp Cột kết cấu -> AI_XD_TUONG"
                                : "Cặp nét tường song song (95-400mm) thuộc khung tường chính -> AI_XD_TUONG",
                            CurrentLayer = curLayDisplay,
                            IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerTuong, StringComparison.OrdinalIgnoreCase)
                        };
                    }

                    if (hasWallThicknessPair && !touchesAnyColumn)
                    {
                        return new LiveSelectionDiagnosis
                        {
                            SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                            EntityType = "Nét Song Song Rời (Nội Thất)",
                            Reason = "Các đường song song KHÔNG tiếp giáp Cột kết cấu -> Nội Thất / Thiết bị (AI_XD_NOI_THAT)",
                            CurrentLayer = curLayDisplay,
                            IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerNoiThat, StringComparison.OrdinalIgnoreCase)
                        };
                    }

                    // Kiểm tra kích thước Polyline rời có khớp nội thất không
                    if (ents.Count == 1 && ents[0] is Polyline)
                    {
                        try
                        {
                            Extents3d pe = ents[0].GeometricExtents;
                            string prof;
                            if (MatchesFurnitureRectProfile(pe.MaxPoint.X - pe.MinPoint.X, pe.MaxPoint.Y - pe.MinPoint.Y, ((Polyline)ents[0]).NumberOfVertices, ((Polyline)ents[0]).Closed, out prof))
                            {
                                return new LiveSelectionDiagnosis
                                {
                                    SuggestedLayer = CadLayoutProcessorService.LayerNoiThat,
                                    EntityType = "Hình Học Nội Thất",
                                    Reason = "Khớp kích thước nội thất đã học: " + prof,
                                    CurrentLayer = curLayDisplay,
                                    IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerNoiThat, StringComparison.OrdinalIgnoreCase)
                                };
                            }
                        }
                        catch { }
                    }

                    // Đường 1 nét không có cặp tường 2 nét đối ứng (95-400mm):
                    // Dài >= 6000mm hoặc kiểu nét CENTER/DASHDOT -> AI_XD_GRID; còn lại -> AI_XD_RAC
                    double maxSegLen = segs.Count > 0 ? segs.Max(s => s.Item1.GetDistanceTo(s.Item2)) : 0.0;
                    bool hasGridLinetype = ents.Any(e =>
                    {
                        string lt = (e.Linetype ?? "").ToUpperInvariant();
                        return lt.Contains("CENTER") || lt.Contains("DASHDOT") || lt.Contains("AXIS") || lt.Contains("GRID");
                    });

                    if (maxSegLen >= 6000.0 || hasGridLinetype)
                    {
                        return new LiveSelectionDiagnosis
                        {
                            SuggestedLayer = CadLayoutProcessorService.LayerGrid,
                            EntityType = "Đường Trục Grid (1 Nét)",
                            Reason = string.Format("Đường 1 nét dài L={0:F0}mm (>=6000mm, không thuộc spec tường 2 nét) -> AI_XD_GRID", maxSegLen),
                            CurrentLayer = curLayDisplay,
                            IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerGrid, StringComparison.OrdinalIgnoreCase)
                        };
                    }

                    return new LiveSelectionDiagnosis
                    {
                        SuggestedLayer = CadLayoutProcessorService.LayerRac,
                        EntityType = "Đường 1 Nét Rời (Rác)",
                        Reason = string.Format("Đường 1 nét đơn lẻ (L={0:F0}mm) không có cặp tường song song 95-400mm -> AI_XD_RAC", maxSegLen),
                        CurrentLayer = curLayDisplay,
                        IsLayerMismatch = layers.Count > 1 || !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerRac, StringComparison.OrdinalIgnoreCase)
                    };
                }

                // 5. Dimension / Text / Solid mặc định
                tr.Commit();
                if (ents.All(e => e is Dimension || e is Leader || e is MLeader))
                {
                    return new LiveSelectionDiagnosis
                    {
                        SuggestedLayer = CadLayoutProcessorService.LayerDimText,
                        EntityType = "Kích Thước (Dim)",
                        Reason = "Đối tượng kích thước đo đạc -> AI_XD_DIM_TEXT",
                        CurrentLayer = curLayDisplay,
                        IsLayerMismatch = !string.Equals(curLayDisplay, CadLayoutProcessorService.LayerDimText, StringComparison.OrdinalIgnoreCase)
                    };
                }

                return new LiveSelectionDiagnosis
                {
                    SuggestedLayer = curLayDisplay.StartsWith("AI_XD_") ? curLayDisplay : CadLayoutProcessorService.LayerNoiThat,
                    EntityType = string.Format("Hỗn hợp ({0} đối tượng)", ents.Count),
                    Reason = "Nhóm đối tượng tổng hợp (Kiểm tra thuộc tính trên Live Preview)",
                    CurrentLayer = curLayDisplay,
                    IsLayerMismatch = false
                };
            }
        }

        private static bool HasParallelWallPair(List<Tuple<Point2d, Point2d>> segs, double minThick, double maxThick)
        {
            for (int i = 0; i < segs.Count; i++)
            {
                Vector2d d1 = segs[i].Item2 - segs[i].Item1;
                double l1 = d1.Length;
                if (l1 < 180.0) continue;
                Vector2d u1 = d1 / l1;
                Vector2d n1 = new Vector2d(-u1.Y, u1.X);

                double totalOverlap = 0.0;
                for (int j = 0; j < segs.Count; j++)
                {
                    if (i == j) continue;
                    Vector2d d2 = segs[j].Item2 - segs[j].Item1;
                    double l2 = d2.Length;
                    if (l2 < 180.0) continue;
                    Vector2d u2 = d2 / l2;
                    if (Math.Abs(u1.X * u2.X + u1.Y * u2.Y) < 0.995) continue;

                    Vector2d v12 = segs[j].Item1 - segs[i].Item1;
                    double perp = Math.Abs(v12.X * n1.X + v12.Y * n1.Y);
                    if (perp >= minThick && perp <= maxThick)
                    {
                        double proj1 = v12.X * u1.X + v12.Y * u1.Y;
                        double proj2 = (segs[j].Item2 - segs[i].Item1).X * u1.X + (segs[j].Item2 - segs[i].Item1).Y * u1.Y;
                        double overlap = Math.Min(l1, Math.Max(proj1, proj2)) - Math.Max(0.0, Math.Min(proj1, proj2));
                        if (overlap >= 110.0) totalOverlap += overlap;
                    }
                }
                if (totalOverlap / l1 >= 0.45) return true;
            }
            return false;
        }

        private static int CountCloselySpacedParallelBars(List<Tuple<Point2d, Point2d>> segs)
        {
            int maxCluster = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                Vector2d d1 = segs[i].Item2 - segs[i].Item1;
                double l1 = d1.Length;
                if (l1 < 120.0 || l1 > 1800.0) continue;
                Vector2d u1 = d1 / l1;
                Vector2d n1 = new Vector2d(-u1.Y, u1.X);

                Point2d mid1 = new Point2d((segs[i].Item1.X + segs[i].Item2.X) / 2.0, (segs[i].Item1.Y + segs[i].Item2.Y) / 2.0);
                List<double> offsets = new List<double> { 0.0 };
                for (int j = 0; j < segs.Count; j++)
                {
                    if (i == j) continue;
                    Vector2d d2 = segs[j].Item2 - segs[j].Item1;
                    double l2 = d2.Length;
                    if (Math.Abs(l2 - l1) > Math.Max(80.0, l1 * 0.25)) continue;
                    Vector2d u2 = d2 / l2;
                    if (Math.Abs(u1.X * u2.X + u1.Y * u2.Y) < 0.995) continue;

                    Point2d mid2 = new Point2d((segs[j].Item1.X + segs[j].Item2.X) / 2.0, (segs[j].Item1.Y + segs[j].Item2.Y) / 2.0);
                    Vector2d vMid = mid2 - mid1;
                    double along = Math.Abs(vMid.X * u1.X + vMid.Y * u1.Y);
                    if (along > 180.0) continue;

                    double off = vMid.X * n1.X + vMid.Y * n1.Y;
                    if (Math.Abs(off) <= 900.0 && Math.Abs(off) >= 15.0)
                    {
                        if (!offsets.Any(x => Math.Abs(x - off) < 10.0)) offsets.Add(off);
                    }
                }
                if (offsets.Count > maxCluster) maxCluster = offsets.Count;
            }
            return maxCluster;
        }

        private static void IncludePoint(double x, double y, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private static void TryIncludeExtents(Entity ent, Matrix3d transform, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            try
            {
                Extents3d ext = ent.GeometricExtents;
                Point3d p1 = ext.MinPoint.TransformBy(transform);
                Point3d p2 = ext.MaxPoint.TransformBy(transform);
                IncludePoint(p1.X, p1.Y, ref minX, ref minY, ref maxX, ref maxY);
                IncludePoint(p2.X, p2.Y, ref minX, ref minY, ref maxX, ref maxY);
            }
            catch { }
        }

        private static double GetMatrixScaleEstimate(Matrix3d mat)
        {
            Vector3d ex = Vector3d.XAxis.TransformBy(mat);
            Vector3d ey = Vector3d.YAxis.TransformBy(mat);
            double scale = (ex.Length + ey.Length) / 2.0;
            return scale > 1e-4 ? scale : 1.0;
        }

        private static string GetStr(Dictionary<string, object> dict, string key)
        {
            object v;
            return (dict.TryGetValue(key, out v) && v != null) ? v.ToString() : "";
        }

        private static double GetDouble(Dictionary<string, object> dict, string key)
        {
            object v;
            if (dict.TryGetValue(key, out v) && v != null)
            {
                double d;
                if (double.TryParse(v.ToString(), out d)) return d;
            }
            return 0.0;
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            object v;
            if (dict.TryGetValue(key, out v) && v != null)
            {
                int i;
                if (int.TryParse(v.ToString(), out i)) return i;
            }
            return 0;
        }
    }
}

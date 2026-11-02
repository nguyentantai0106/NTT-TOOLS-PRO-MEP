using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ C# chuyên sâu để đọc, phân tích và giải mã cấu trúc bản vẽ DWG (đặc biệt là bản vẽ tủ điện TD.dwg)
    /// </summary>
    public class DwgAnalyzerService
    {
        public class LayerInfo
        {
            public string Name { get; set; }
            public int ColorIndex { get; set; }
            public string LineWeight { get; set; }
            public bool IsPlottable { get; set; }
            public int EntityCount { get; set; }
        }

        public class BlockDefInfo
        {
            public string Name { get; set; }
            public bool IsDynamic { get; set; }
            public bool IsXref { get; set; }
            public int EntityCount { get; set; }
            public Dictionary<string, int> EntityTypes { get; set; }

            public BlockDefInfo()
            {
                EntityTypes = new Dictionary<string, int>();
            }
        }

        public class BlockRefInfo
        {
            public string BlockName { get; set; }
            public string EffectiveName { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double ScaleX { get; set; }
            public double ScaleY { get; set; }
            public double RotationDeg { get; set; }
            public string Layer { get; set; }
            public Dictionary<string, string> Attributes { get; set; }
            public Dictionary<string, string> DynamicProperties { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }

            public BlockRefInfo()
            {
                Attributes = new Dictionary<string, string>();
                DynamicProperties = new Dictionary<string, string>();
            }
        }

        public class TextInfo
        {
            public string Content { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Height { get; set; }
            public double RotationDeg { get; set; }
            public string Layer { get; set; }
            public string StyleName { get; set; }
        }

        public class DimensionInfo
        {
            public string Type { get; set; }
            public double Measurement { get; set; }
            public string DimensionText { get; set; }
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public string Layer { get; set; }
        }

        public class PolylineRectInfo
        {
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsClosed { get; set; }
            public string Layer { get; set; }
            public int VertexCount { get; set; }
        }

        public class AnalysisResult
        {
            public string FilePath { get; set; }
            public double ExtentsMinX { get; set; }
            public double ExtentsMinY { get; set; }
            public double ExtentsMaxX { get; set; }
            public double ExtentsMaxY { get; set; }
            public double TotalWidth { get; set; }
            public double TotalHeight { get; set; }
            public int TotalEntities { get; set; }

            public List<LayerInfo> Layers { get; set; }
            public List<BlockDefInfo> BlockDefinitions { get; set; }
            public List<BlockRefInfo> BlockReferences { get; set; }
            public List<TextInfo> Texts { get; set; }
            public List<DimensionInfo> Dimensions { get; set; }
            public List<PolylineRectInfo> Rectangles { get; set; }
            public Dictionary<string, int> EntityTypeCounts { get; set; }

            public AnalysisResult()
            {
                Layers = new List<LayerInfo>();
                BlockDefinitions = new List<BlockDefInfo>();
                BlockReferences = new List<BlockRefInfo>();
                Texts = new List<TextInfo>();
                Dimensions = new List<DimensionInfo>();
                Rectangles = new List<PolylineRectInfo>();
                EntityTypeCounts = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Phân tích file DWG từ đường dẫn chỉ định
        /// </summary>
        public static AnalysisResult AnalyzeDwgFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Không tìm thấy file DWG: " + filePath);
            }

            using (Database db = new Database(false, true))
            {
                db.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, "");
                return AnalyzeDatabase(db, filePath);
            }
        }

        /// <summary>
        /// Phân tích Database AutoCAD đã nạp
        /// </summary>
        public static AnalysisResult AnalyzeDatabase(Database db, string filePath = "")
        {
            AnalysisResult result = new AnalysisResult();
            result.FilePath = filePath;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. Phân tích Layers
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                Dictionary<string, LayerInfo> layerDict = new Dictionary<string, LayerInfo>();
                foreach (ObjectId lId in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lId, OpenMode.ForRead);
                    LayerInfo lInfo = new LayerInfo
                    {
                        Name = ltr.Name,
                        ColorIndex = ltr.Color.ColorIndex,
                        LineWeight = ltr.LineWeight.ToString(),
                        IsPlottable = ltr.IsPlottable,
                        EntityCount = 0
                    };
                    result.Layers.Add(lInfo);
                    layerDict[ltr.Name] = lInfo;
                }

                // 2. Phân tích Block Definitions trong BlockTable
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId bId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bId, OpenMode.ForRead);
                    if (btr.IsLayout) continue; // Bỏ qua Model/Paper spaces

                    BlockDefInfo def = new BlockDefInfo
                    {
                        Name = btr.Name,
                        IsDynamic = btr.IsDynamicBlock,
                        IsXref = btr.IsFromExternalReference,
                        EntityCount = 0
                    };

                    foreach (ObjectId entId in btr)
                    {
                        def.EntityCount++;
                        DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                        string tName = obj.GetType().Name;
                        if (!def.EntityTypes.ContainsKey(tName)) def.EntityTypes[tName] = 0;
                        def.EntityTypes[tName]++;
                    }
                    result.BlockDefinitions.Add(def);
                }

                // 3. Phân tích ModelSpace
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                int entityCount = 0;

                foreach (ObjectId entId in modelSpace)
                {
                    Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    entityCount++;
                    string entType = ent.GetType().Name;
                    if (!result.EntityTypeCounts.ContainsKey(entType)) result.EntityTypeCounts[entType] = 0;
                    result.EntityTypeCounts[entType]++;

                    if (layerDict.ContainsKey(ent.Layer))
                    {
                        layerDict[ent.Layer].EntityCount++;
                    }

                    // Cập nhật Extents
                    try
                    {
                        Extents3d ext = ent.GeometricExtents;
                        if (ext.MinPoint.X < minX) minX = ext.MinPoint.X;
                        if (ext.MinPoint.Y < minY) minY = ext.MinPoint.Y;
                        if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                        if (ext.MaxPoint.Y > maxY) maxY = ext.MaxPoint.Y;
                    }
                    catch { }

                    // Phân tích BlockReference
                    BlockReference br = ent as BlockReference;
                    if (br != null)
                    {
                        string effName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            BlockTableRecord effBtr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                            effName = effBtr.Name;
                        }

                        BlockRefInfo bri = new BlockRefInfo
                        {
                            BlockName = br.Name,
                            EffectiveName = effName,
                            X = Math.Round(br.Position.X, 2),
                            Y = Math.Round(br.Position.Y, 2),
                            Z = Math.Round(br.Position.Z, 2),
                            ScaleX = Math.Round(br.ScaleFactors.X, 3),
                            ScaleY = Math.Round(br.ScaleFactors.Y, 3),
                            RotationDeg = Math.Round(br.Rotation * 180.0 / Math.PI, 1),
                            Layer = br.Layer
                        };

                        try
                        {
                            Extents3d bExt = br.GeometricExtents;
                            bri.Width = Math.Round(bExt.MaxPoint.X - bExt.MinPoint.X, 2);
                            bri.Height = Math.Round(bExt.MaxPoint.Y - bExt.MinPoint.Y, 2);
                        }
                        catch { }

                        // Attributes
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (att != null)
                            {
                                bri.Attributes[att.Tag] = att.TextString;
                            }
                        }

                        // Dynamic Properties
                        if (br.IsDynamicBlock)
                        {
                            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                            {
                                if (prop.Value != null)
                                {
                                    bri.DynamicProperties[prop.PropertyName] = prop.Value.ToString();
                                }
                            }
                        }

                        result.BlockReferences.Add(bri);
                        continue;
                    }

                    // Phân tích Text & MText
                    DBText dbText = ent as DBText;
                    if (dbText != null)
                    {
                        result.Texts.Add(new TextInfo
                        {
                            Content = dbText.TextString,
                            X = Math.Round(dbText.Position.X, 2),
                            Y = Math.Round(dbText.Position.Y, 2),
                            Height = Math.Round(dbText.Height, 2),
                            RotationDeg = Math.Round(dbText.Rotation * 180.0 / Math.PI, 1),
                            Layer = dbText.Layer,
                            StyleName = dbText.TextStyleName
                        });
                        continue;
                    }

                    MText mText = ent as MText;
                    if (mText != null)
                    {
                        result.Texts.Add(new TextInfo
                        {
                            Content = mText.Text,
                            X = Math.Round(mText.Location.X, 2),
                            Y = Math.Round(mText.Location.Y, 2),
                            Height = Math.Round(mText.TextHeight, 2),
                            RotationDeg = Math.Round(mText.Rotation * 180.0 / Math.PI, 1),
                            Layer = mText.Layer,
                            StyleName = mText.TextStyleName
                        });
                        continue;
                    }

                    // Phân tích Dimension
                    Dimension dim = ent as Dimension;
                    if (dim != null)
                    {
                        DimensionInfo dInfo = new DimensionInfo
                        {
                            Type = dim.GetType().Name,
                            Measurement = Math.Round(dim.Measurement, 2),
                            DimensionText = dim.DimensionText,
                            Layer = dim.Layer
                        };
                        try
                        {
                            Extents3d dExt = dim.GeometricExtents;
                            dInfo.MinX = Math.Round(dExt.MinPoint.X, 2);
                            dInfo.MinY = Math.Round(dExt.MinPoint.Y, 2);
                            dInfo.MaxX = Math.Round(dExt.MaxPoint.X, 2);
                            dInfo.MaxY = Math.Round(dExt.MaxPoint.Y, 2);
                        }
                        catch { }
                        result.Dimensions.Add(dInfo);
                        continue;
                    }

                    // Phân tích Polyline (Đặc biệt để tìm khung vỏ tủ, thanh gá, máng cáp)
                    Polyline pl = ent as Polyline;
                    if (pl != null)
                    {
                        try
                        {
                            Extents3d plExt = pl.GeometricExtents;
                            double w = plExt.MaxPoint.X - plExt.MinPoint.X;
                            double h = plExt.MaxPoint.Y - plExt.MinPoint.Y;

                            // Chỉ lưu các hình khối có kích thước đáng kể
                            if (w > 10.0 || h > 10.0)
                            {
                                result.Rectangles.Add(new PolylineRectInfo
                                {
                                    MinX = Math.Round(plExt.MinPoint.X, 2),
                                    MinY = Math.Round(plExt.MinPoint.Y, 2),
                                    MaxX = Math.Round(plExt.MaxPoint.X, 2),
                                    MaxY = Math.Round(plExt.MaxPoint.Y, 2),
                                    Width = Math.Round(w, 2),
                                    Height = Math.Round(h, 2),
                                    IsClosed = pl.Closed,
                                    Layer = pl.Layer,
                                    VertexCount = pl.NumberOfVertices
                                });
                            }
                        }
                        catch { }
                        continue;
                    }
                }

                if (minX != double.MaxValue)
                {
                    result.ExtentsMinX = Math.Round(minX, 2);
                    result.ExtentsMinY = Math.Round(minY, 2);
                    result.ExtentsMaxX = Math.Round(maxX, 2);
                    result.ExtentsMaxY = Math.Round(maxY, 2);
                    result.TotalWidth = Math.Round(maxX - minX, 2);
                    result.TotalHeight = Math.Round(maxY - minY, 2);
                }
                result.TotalEntities = entityCount;

                tr.Commit();
            }

            return result;
        }

        /// <summary>
        /// Xuất báo cáo phân tích chi tiết định dạng Markdown / Text
        /// </summary>
        public static string GenerateAnalysisReport(AnalysisResult res)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("           BÁO CÁO PHÂN TÍCH BẢN VẼ TỦ ĐIỆN CHI TIẾT (C# CAD ANALYZER)");
            sb.AppendLine("================================================================================");
            sb.AppendLine(string.Format("File phân tích      : {0}", res.FilePath));
            sb.AppendLine(string.Format("Tổng số thực thể    : {0}", res.TotalEntities));
            sb.AppendLine(string.Format("Kích thước vùng vẽ  : Rộng = {0} mm x Cao = {1} mm", res.TotalWidth, res.TotalHeight));
            sb.AppendLine(string.Format("Tọa độ góc dưới trái: ({0}, {1})", res.ExtentsMinX, res.ExtentsMinY));
            sb.AppendLine(string.Format("Tọa độ góc trên phải: ({0}, {1})", res.ExtentsMaxX, res.ExtentsMaxY));
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("1. THỐNG KÊ CÁC LOẠI ĐỐI TƯỢNG (ENTITY TYPES):");
            sb.AppendLine("--------------------------------------------------------------------------------");
            foreach (var kvp in res.EntityTypeCounts)
            {
                sb.AppendLine(string.Format("   - {0,-25}: {1,6} đối tượng", kvp.Key, kvp.Value));
            }
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("2. DANH SÁCH LỚP (LAYERS ĐƯỢC SỬ DỤNG):");
            sb.AppendLine("--------------------------------------------------------------------------------");
            foreach (var l in res.Layers)
            {
                if (l.EntityCount > 0)
                {
                    sb.AppendLine(string.Format("   - Layer: {0,-25} | Màu: {1,3} | Số đối tượng: {2,5} | In: {3}", l.Name, l.ColorIndex, l.EntityCount, l.IsPlottable));
                }
            }
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(string.Format("3. DANH SÁCH CÁC BLOCK ĐƯỢC CHÈN (BLOCK REFERENCES) - TỔNG CỘNG: {0}:", res.BlockReferences.Count));
            sb.AppendLine("--------------------------------------------------------------------------------");
            // Nhóm theo tên block
            Dictionary<string, List<BlockRefInfo>> blocksByName = new Dictionary<string, List<BlockRefInfo>>();
            foreach (var br in res.BlockReferences)
            {
                string bName = string.IsNullOrEmpty(br.EffectiveName) ? br.BlockName : br.EffectiveName;
                if (!blocksByName.ContainsKey(bName)) blocksByName[bName] = new List<BlockRefInfo>();
                blocksByName[bName].Add(br);
            }

            foreach (var kvp in blocksByName)
            {
                sb.AppendLine(string.Format("   * Block: [{0}] (Số lượng: {1})", kvp.Key, kvp.Value.Count));
                int showCount = Math.Min(3, kvp.Value.Count);
                for (int i = 0; i < showCount; i++)
                {
                    var b = kvp.Value[i];
                    sb.AppendLine(string.Format("     + Vị trí ({0}, {1}) | Layer: {2} | Kích thước: W={3} x H={4} | Góc xoay: {5}°",
                        b.X, b.Y, b.Layer, b.Width, b.Height, b.RotationDeg));
                    if (b.Attributes.Count > 0)
                    {
                        sb.Append("       Thuộc tính (Attributes): ");
                        foreach (var att in b.Attributes)
                        {
                            sb.Append(string.Format("[{0}={1}] ", att.Key, att.Value));
                        }
                        sb.AppendLine();
                    }
                }
                if (kvp.Value.Count > 3)
                {
                    sb.AppendLine(string.Format("     ... và {0} đối tượng khác cùng loại.", kvp.Value.Count - 3));
                }
            }
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(string.Format("4. KÍCH THƯỚC ĐO ĐƯỢC (DIMENSIONS) - TỔNG CỘNG: {0}:", res.Dimensions.Count));
            sb.AppendLine("--------------------------------------------------------------------------------");
            // Lọc các dimension quan trọng (ví dụ kích thước vỏ tủ, chiều cao, chiều rộng các ngăn)
            var sortedDims = new List<DimensionInfo>(res.Dimensions);
            sortedDims.Sort((a, b) => b.Measurement.CompareTo(a.Measurement));
            int dimLimit = Math.Min(30, sortedDims.Count);
            for (int i = 0; i < dimLimit; i++)
            {
                var d = sortedDims[i];
                string dimVal = string.IsNullOrEmpty(d.DimensionText) ? d.Measurement.ToString("0.##") : d.DimensionText;
                sb.AppendLine(string.Format("   - Dim: {0,8} mm | Text: '{1}' | Tọa độ: ({2}, {3}) -> ({4}, {5}) | Layer: {6}",
                    d.Measurement, dimVal, d.MinX, d.MinY, d.MaxX, d.MaxY, d.Layer));
            }
            if (sortedDims.Count > 30)
            {
                sb.AppendLine(string.Format("   ... và {0} kích thước đo khác.", sortedDims.Count - 30));
            }
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(string.Format("5. CÁC HÌNH KHỐI LỚN (KHUNG VỎ TỦ, NGĂN TỦ, THANH GÁ, MÁNG CÁP) - TỔNG CỘNG: {0}:", res.Rectangles.Count));
            sb.AppendLine("--------------------------------------------------------------------------------");
            var sortedRects = new List<PolylineRectInfo>(res.Rectangles);
            sortedRects.Sort((a, b) => (b.Width * b.Height).CompareTo(a.Width * a.Height));
            int rectLimit = Math.Min(25, sortedRects.Count);
            for (int i = 0; i < rectLimit; i++)
            {
                var r = sortedRects[i];
                sb.AppendLine(string.Format("   - Khung: W={0,7} mm x H={1,7} mm | Tọa độ: ({2}, {3}) -> ({4}, {5}) | Đóng: {6,-5} | Layer: {7}",
                    r.Width, r.Height, r.MinX, r.MinY, r.MaxX, r.MaxY, r.IsClosed, r.Layer));
            }
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(string.Format("6. NỘI DUNG CHỮ & GHI CHÚ CHÍNH (TEXT & MTEXT) - TỔNG CỘNG: {0}:", res.Texts.Count));
            sb.AppendLine("--------------------------------------------------------------------------------");
            // Lọc ra các dòng text có độ dài >= 2 và hiển thị
            int textShown = 0;
            foreach (var t in res.Texts)
            {
                string clean = t.Content.Replace("\r", " ").Replace("\n", " ").Trim();
                if (clean.Length >= 2 && textShown < 50)
                {
                    sb.AppendLine(string.Format("   - [{0,6}, {1,6}] (h={2,3}mm, layer={3}): \"{4}\"",
                        t.X, t.Y, t.Height, t.Layer, clean));
                    textShown++;
                }
            }
            if (res.Texts.Count > 50)
            {
                sb.AppendLine(string.Format("   ... và {0} dòng chữ khác.", res.Texts.Count - 50));
            }
            sb.AppendLine();

            sb.AppendLine("================================================================================");
            sb.AppendLine("                   KẾT THÚC BÁO CÁO PHÂN TÍCH");
            sb.AppendLine("================================================================================");

            return sb.ToString();
        }
    }
}

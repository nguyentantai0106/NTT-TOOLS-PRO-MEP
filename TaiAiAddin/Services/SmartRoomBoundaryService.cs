using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ Xử Lý Mặt Bằng (XLMB) v3:
    /// - Đệ quy giải mã toàn bộ XREF & Block lồng nhau (tối đa 5 cấp), nhân ma trận Matrix3d về WCS.
    /// - Nhận diện Tường & Cột thuần túy bằng Hình Học Không Gian (Parallel Strip 100-350mm), không phụ thuộc Layer.
    /// - Hỗ trợ toàn diện: Tường trực giao 90°/270°, Tường cong bo tròn (Bulge) và Tường xéo (Skewed walls) ôm sát theo phương nghiêng.
    /// - Không gian lớn chỉ tạo 1 đường bao duy nhất, không bị vỡ vụn hay chia cắt.
    /// - Tự động loại trừ Trục Grid và Nội thất (bàn, ghế, giường, tủ, thiết bị vệ sinh).
    /// </summary>
    public static class SmartRoomBoundaryService
    {
        #region Cấu Trúc Dữ Liệu Hình Học Nội Bộ (Planar Geometry Graph)

        public class GeoSegment
        {
            public Point2d Start { get; set; }
            public Point2d End { get; set; }
            public bool IsCurved { get; set; }
            public Point2d ArcCenter { get; set; }
            public double ArcRadius { get; set; }
            public double Bulge { get; set; }
            public bool IsSkewedWall { get; set; }
            public string SourceLayer { get; set; }

            public GeoSegment(Point2d s, Point2d e, string layer = "")
            {
                Start = s;
                End = e;
                SourceLayer = layer;
                IsCurved = false;
                Bulge = 0.0;
                IsSkewedWall = false;
            }

            public double Length
            {
                get { return Start.GetDistanceTo(End); }
            }

            public double AngleRad
            {
                get { return Math.Atan2(End.Y - Start.Y, End.X - Start.X); }
            }
        }

        public class RoomSeedText
        {
            public string Text { get; set; }
            public Point2d Position { get; set; }
            public double Height { get; set; }

            public RoomSeedText(string text, Point2d pos, double h)
            {
                Text = text;
                Position = pos;
                Height = h;
            }
        }

        private class GraphNode
        {
            public int Id { get; set; }
            public Point2d Point { get; set; }
            public List<HalfEdge> OutEdges { get; set; }

            public GraphNode(int id, Point2d pt)
            {
                Id = id;
                Point = pt;
                OutEdges = new List<HalfEdge>();
            }
        }

        private class HalfEdge
        {
            public GraphNode From { get; set; }
            public GraphNode To { get; set; }
            public double Angle { get; set; }
            public double Bulge { get; set; }
            public bool IsSkewed { get; set; }
            public bool Visited { get; set; }
            public HalfEdge Twin { get; set; }

            public HalfEdge(GraphNode from, GraphNode to, double bulge = 0.0, bool isSkewed = false)
            {
                From = from;
                To = to;
                Bulge = bulge;
                IsSkewed = isSkewed;
                Visited = false;
                Angle = Math.Atan2(to.Point.Y - from.Point.Y, to.Point.X - from.Point.X);
            }
        }

        #endregion

        #region Điểm Vào Chính (Main Execution - Universal Boundary Engine v4.0)

        /// <summary>
        /// Thực thi lệnh XLMB:
        /// 1. Quét bản vẽ (hoặc Enter để chọn toàn bộ).
        /// 2. Bỏ qua Trục Grid.
        /// 3. Nhận dạng tường, cột bằng hình học không gian (bỏ qua nội thất).
        /// 4. Vẽ đường bao: bo tròn tường cong (Bulge), ôm sát tường xéo (Skewed walls).
        /// 5. Khu vực lớn chỉ vẽ 1 đường bao duy nhất (không chia cắt vụn vặt).
        /// 6. Quét và ghi lại Tên phòng + Diện tích m² (MText tại tâm phòng).
        /// 7. Kết thúc bằng câu: "Đã vẽ xong mời bạn Check".
        /// </summary>
        public static RoomBoundaryResult ExecuteRoomBoundary(Document doc, RoomBoundaryOptions options)
        {
            if (options == null) options = new RoomBoundaryOptions();
            RoomBoundaryResult result = new RoomBoundaryResult();

            if (doc == null)
            {
                result.Message = "Không tìm thấy tài liệu CAD đang mở!";
                return result;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                bool hasBbox = false;
                string pointArgs = "";

                if (options.PickPoint.HasValue)
                {
                    pointArgs = string.Format(CultureInfo.InvariantCulture, "--point {0:F2} {1:F2}",
                        options.PickPoint.Value.X, options.PickPoint.Value.Y);
                    ed.WriteMessage(string.Format("\n[BAOPHONG] Đang xử lý bo phòng tại tọa độ ({0:F2}, {1:F2})...",
                        options.PickPoint.Value.X, options.PickPoint.Value.Y));
                }
                else
                {
                    // 1. Nhắc người dùng quét chọn vùng mặt bằng (hoặc Enter để quét toàn bộ)
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\n[XLMB] Quét chọn vùng mặt bằng kiến trúc (hoặc nhấn ENTER để quét toàn bộ): ";
                    pso.AllowDuplicates = false;

                    PromptSelectionResult psr = ed.GetSelection(pso);

                    if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                    {
                        SelectionSet sset = psr.Value;
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            foreach (SelectedObject so in sset)
                            {
                                if (so == null) continue;
                                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                                if (ent != null)
                                {
                                    try
                                    {
                                        Extents3d ext = ent.GeometricExtents;
                                        if (ext.MinPoint.X < minX) minX = ext.MinPoint.X;
                                        if (ext.MinPoint.Y < minY) minY = ext.MinPoint.Y;
                                        if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                                        if (ext.MaxPoint.Y > maxY) maxY = ext.MaxPoint.Y;
                                        hasBbox = true;
                                    }
                                    catch { }
                                }
                            }
                            tr.Commit();
                        }
                        ed.WriteMessage(string.Format("\n[XLMB] Đã chọn vùng mặt bằng ({0} đối tượng).", sset.Count));
                    }
                    else if (psr.Status == PromptStatus.None || psr.Status == PromptStatus.Error)
                    {
                        ed.WriteMessage("\n[XLMB] Đã chọn quét toàn bộ bản vẽ.");
                    }
                    else
                    {
                        ed.WriteMessage("\n[XLMB] Đã hủy lệnh.");
                        result.Message = "Người dùng đã hủy lệnh.";
                        return result;
                    }
                }

                ed.WriteMessage("\n[XLMB] Đang phân tích hình học không gian, tự tìm góc & bo kín phòng...");

                string tempDir = Path.GetTempPath();
                string stamp = DateTime.Now.Ticks.ToString();
                string dxfPath = Path.Combine(tempDir, "plan_" + stamp + ".dxf");
                string outLsp = Path.Combine(tempDir, "res_" + stamp + ".lsp");

                // Xuất DXF an toàn
                using (doc.LockDocument())
                {
                    db.DxfOut(dxfPath, 16, DwgVersion.Current);
                }

                // Tìm Python và Script
                string pyExe = FindPythonExecutable();
                string pyScript = FindBoundaryEngineScript();

                if (string.IsNullOrEmpty(pyExe) || !File.Exists(pyExe))
                {
                    ed.WriteMessage("\n[XLMB] Lỗi: Không tìm thấy trình thông dịch Python!");
                    result.Message = "Không tìm thấy Python.";
                    return result;
                }

                if (string.IsNullOrEmpty(pyScript) || !File.Exists(pyScript))
                {
                    ed.WriteMessage("\n[XLMB] Lỗi: Không tìm thấy file script boundary_engine.py!");
                    result.Message = "Không tìm thấy boundary_engine.py.";
                    return result;
                }

                string bboxArgs = "";
                if (hasBbox && minX < maxX && minY < maxY)
                {
                    minX -= 500.0;
                    minY -= 500.0;
                    maxX += 500.0;
                    maxY += 500.0;
                    bboxArgs = string.Format(CultureInfo.InvariantCulture, "--bbox {0:F1} {1:F1} {2:F1} {3:F1}",
                        minX, minY, maxX, maxY);
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = pyExe;
                psi.Arguments = string.Format("\"{0}\" --dxf \"{1}\" --out \"{2}\" {3} {4}",
                    pyScript, dxfPath, outLsp, bboxArgs, pointArgs);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process proc = new Process())
                {
                    proc.StartInfo = psi;
                    System.Text.StringBuilder stdOut = new System.Text.StringBuilder();
                    System.Text.StringBuilder stdErr = new System.Text.StringBuilder();

                    proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    bool exited = proc.WaitForExit(30000);
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        ed.WriteMessage("\n[XLMB] Quá thời gian xử lý (30 giây)!");
                    }

                    if (File.Exists(outLsp))
                    {
                        doc.SendStringToExecute(string.Format("(load \"{0}\")\n", outLsp.Replace('\\', '/')), true, false, false);
                        result.Success = true;
                    }
                    else
                    {
                        ed.WriteMessage("\n[XLMB] Không tạo được đường bao phòng hợp lệ.");
                        if (stdErr.Length > 0)
                        {
                            ed.WriteMessage(string.Format("\n[XLMB Log]: {0}", stdErr.ToString()));
                        }
                        result.Message = "Không tìm thấy phòng kín hợp lệ.";
                    }
                }

                try
                {
                    if (File.Exists(dxfPath)) File.Delete(dxfPath);
                }
                catch { }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n[XLMB] Lỗi trong quá trình xử lý: {0}", ex.Message));
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        private static string FindPythonExecutable()
        {
            string[] candidates = new string[]
            {
                @"C:\Users\ADMIN\AppData\Local\Programs\Orange\python.exe",
                @"C:\Program Files\Python312\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python310\python.exe",
                @"C:\Users\ADMIN\AppData\Local\Programs\Python\Python312\python.exe",
                @"C:\Users\ADMIN\AppData\Local\Programs\Python\Python311\python.exe",
                @"C:\Users\ADMIN\AppData\Local\Programs\Python\Python310\python.exe"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }

            return "python.exe";
        }

        private static string FindBoundaryEngineScript()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            string[] candidates = new string[]
            {
                Path.Combine(asmDir, "Scripts", "boundary_engine.py"),
                Path.Combine(asmDir, "boundary_engine.py"),
                Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Scripts\boundary_engine.py"),
                @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Scripts\boundary_engine.py",
                @"D:\01. NGUYEN TAN TAI\CODING AI\SKILL XU LY MAT BANG\skills\smart-room-boundary\scripts\boundary_engine.py"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }

            return "";
        }

        #endregion

        #region Trích Xuất Đệ Quy XREF & Block Lồng Nhau (Deep XREF Traversal)

        private static void ExtractCadGeometryWithXrefs(
            Transaction tr,
            SelectionSet sset,
            RoomBoundaryOptions options,
            List<GeoSegment> rawSegments,
            List<GeoSegment> curvedWalls,
            List<Arc> potentialDoorArcs,
            List<RoomSeedText> seedTexts,
            List<Circle> potentialGridBubbles,
            List<GeoSegment> potentialGridLines)
        {
            foreach (SelectedObject so in sset)
            {
                if (so == null || so.ObjectId.IsNull) continue;
                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;

                // Nếu là BlockReference (Bao gồm XREF hoặc Block chứa mặt bằng)
                BlockReference blkRef = ent as BlockReference;
                if (blkRef != null)
                {
                    string blkName = (blkRef.Name != null ? blkRef.Name : "").ToUpperInvariant();
                    if (options.ExcludeFurniture && IsFurnitureName(blkName, ent.Layer))
                    {
                        continue;
                    }

                    BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (btr != null)
                    {
                        ExtractFromBlockTableRecord(tr, btr, blkRef.BlockTransform, 1, options,
                            rawSegments, curvedWalls, potentialDoorArcs, seedTexts, potentialGridBubbles, potentialGridLines);
                    }
                    continue;
                }

                // Nếu là đối tượng độc lập trong ModelSpace
                ProcessSingleEntity(ent, Matrix3d.Identity, options, rawSegments, curvedWalls,
                    potentialDoorArcs, seedTexts, potentialGridBubbles, potentialGridLines);
            }
        }

        private static void ExtractFromBlockTableRecord(
            Transaction tr,
            BlockTableRecord btr,
            Matrix3d transform,
            int depth,
            RoomBoundaryOptions options,
            List<GeoSegment> rawSegments,
            List<GeoSegment> curvedWalls,
            List<Arc> potentialDoorArcs,
            List<RoomSeedText> seedTexts,
            List<Circle> potentialGridBubbles,
            List<GeoSegment> potentialGridLines)
        {
            if (depth > 5 || btr == null) return;

            foreach (ObjectId id in btr)
            {
                if (id.IsNull || id.IsErased) continue;
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;

                BlockReference childRef = ent as BlockReference;
                if (childRef != null)
                {
                    string childName = (childRef.Name != null ? childRef.Name : "").ToUpperInvariant();
                    if (options.ExcludeFurniture && IsFurnitureName(childName, ent.Layer))
                    {
                        continue;
                    }

                    Matrix3d childTransform = transform * childRef.BlockTransform;
                    BlockTableRecord childBtr = tr.GetObject(childRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (childBtr != null)
                    {
                        ExtractFromBlockTableRecord(tr, childBtr, childTransform, depth + 1, options,
                            rawSegments, curvedWalls, potentialDoorArcs, seedTexts, potentialGridBubbles, potentialGridLines);
                    }
                    continue;
                }

                ProcessSingleEntity(ent, transform, options, rawSegments, curvedWalls,
                    potentialDoorArcs, seedTexts, potentialGridBubbles, potentialGridLines);
            }
        }

        private static void ProcessSingleEntity(
            Entity ent,
            Matrix3d transform,
            RoomBoundaryOptions options,
            List<GeoSegment> rawSegments,
            List<GeoSegment> curvedWalls,
            List<Arc> potentialDoorArcs,
            List<RoomSeedText> seedTexts,
            List<Circle> potentialGridBubbles,
            List<GeoSegment> potentialGridLines)
        {
            string layerUpper = (ent.Layer != null ? ent.Layer : "").ToUpperInvariant();

            // 1. Line
            Line line = ent as Line;
            if (line != null)
            {
                Point3d p1Wcs = line.StartPoint.TransformBy(transform);
                Point3d p2Wcs = line.EndPoint.TransformBy(transform);
                Point2d s = new Point2d(p1Wcs.X, p1Wcs.Y);
                Point2d e = new Point2d(p2Wcs.X, p2Wcs.Y);
                if (s.GetDistanceTo(e) > 10.0)
                {
                    GeoSegment seg = new GeoSegment(s, e, ent.Layer);
                    if (IsGridLinetypeOrLayer(ent, layerUpper))
                    {
                        potentialGridLines.Add(seg);
                    }
                    else
                    {
                        rawSegments.Add(seg);
                    }
                }
                return;
            }

            // 2. Polyline
            Polyline pline = ent as Polyline;
            if (pline != null)
            {
                int n = pline.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    if (i == n - 1 && !pline.Closed) break;
                    int nextIdx = (i + 1) % n;

                    Point3d p1Wcs = pline.GetPoint3dAt(i).TransformBy(transform);
                    Point3d p2Wcs = pline.GetPoint3dAt(nextIdx).TransformBy(transform);
                    Point2d s = new Point2d(p1Wcs.X, p1Wcs.Y);
                    Point2d e = new Point2d(p2Wcs.X, p2Wcs.Y);
                    if (s.GetDistanceTo(e) < 10.0) continue;

                    double bulge = pline.GetBulgeAt(i);
                    if (Math.Abs(bulge) > 1e-4 && options.RoundCurvedWalls)
                    {
                        GeoSegment arcSeg = new GeoSegment(s, e, ent.Layer);
                        arcSeg.IsCurved = true;
                        arcSeg.Bulge = bulge;
                        curvedWalls.Add(arcSeg);
                        rawSegments.Add(arcSeg);
                    }
                    else
                    {
                        rawSegments.Add(new GeoSegment(s, e, ent.Layer));
                    }
                }
                return;
            }

            // 3. Arc
            Arc arc = ent as Arc;
            if (arc != null)
            {
                Point3d centerWcs = arc.Center.TransformBy(transform);
                Point3d spWcs = arc.StartPoint.TransformBy(transform);
                Point3d epWcs = arc.EndPoint.TransformBy(transform);

                double scaleFactor = Math.Sqrt(Math.Abs(transform[0, 0] * transform[1, 1]));
                if (scaleFactor < 1e-3) scaleFactor = 1.0;
                double r = arc.Radius * scaleFactor;

                double sweepAngle = Math.Abs(arc.EndAngle - arc.StartAngle);
                if (sweepAngle > Math.PI) sweepAngle = 2 * Math.PI - sweepAngle;
                double sweepDeg = sweepAngle * 180.0 / Math.PI;

                bool isDoorSwing = (r >= 550.0 && r <= 1250.0 && sweepDeg >= 70.0 && sweepDeg <= 110.0);
                if (isDoorSwing)
                {
                    potentialDoorArcs.Add(arc);
                }
                else if (options.RoundCurvedWalls)
                {
                    Point2d s = new Point2d(spWcs.X, spWcs.Y);
                    Point2d e = new Point2d(epWcs.X, epWcs.Y);
                    double incAngle = arc.EndAngle - arc.StartAngle;
                    if (incAngle < 0) incAngle += 2 * Math.PI;
                    double bulge = Math.Tan(incAngle / 4.0);

                    GeoSegment arcSeg = new GeoSegment(s, e, ent.Layer);
                    arcSeg.IsCurved = true;
                    arcSeg.ArcCenter = new Point2d(centerWcs.X, centerWcs.Y);
                    arcSeg.ArcRadius = r;
                    arcSeg.Bulge = bulge;
                    curvedWalls.Add(arcSeg);
                    rawSegments.Add(arcSeg);
                }
                return;
            }

            // 4. Circle (Grid bubble candidate)
            Circle circle = ent as Circle;
            if (circle != null)
            {
                double scaleFactor = Math.Sqrt(Math.Abs(transform[0, 0] * transform[1, 1]));
                if (scaleFactor < 1e-3) scaleFactor = 1.0;
                double r = circle.Radius * scaleFactor;
                if (r >= 200.0 && r <= 800.0)
                {
                    potentialGridBubbles.Add(circle);
                }
                return;
            }

            // 5. Solid (Column)
            Solid solid = ent as Solid;
            if (solid != null)
            {
                ExtractSolidColumn(solid, transform, rawSegments);
                return;
            }

            // 6. Hatch (Column)
            Hatch hatch = ent as Hatch;
            if (hatch != null)
            {
                string pat = hatch.PatternName != null ? hatch.PatternName.ToUpperInvariant() : "";
                if (pat == "SOLID" || layerUpper.Contains("COT") || layerUpper.Contains("COL") || layerUpper.Contains("STRUCT"))
                {
                    ExtractHatchColumn(hatch, transform, rawSegments);
                }
                return;
            }

            // 7. DBText & MText
            DBText dbText = ent as DBText;
            if (dbText != null)
            {
                Point3d posWcs = dbText.Position.TransformBy(transform);
                string raw = CleanRoomText(dbText.TextString);
                if (IsLikelyRoomName(raw))
                {
                    seedTexts.Add(new RoomSeedText(raw, new Point2d(posWcs.X, posWcs.Y), dbText.Height));
                }
                return;
            }

            MText mText = ent as MText;
            if (mText != null)
            {
                Point3d locWcs = mText.Location.TransformBy(transform);
                string raw = CleanRoomText(mText.Contents);
                if (IsLikelyRoomName(raw))
                {
                    seedTexts.Add(new RoomSeedText(raw, new Point2d(locWcs.X, locWcs.Y), mText.TextHeight));
                }
                return;
            }
        }

        private static bool IsFurnitureName(string blkName, string layer)
        {
            string b = blkName.ToUpperInvariant();
            string l = (layer != null ? layer : "").ToUpperInvariant();

            string[] kw = new string[]
            {
                "NOITHAT", "FURNITURE", "SAN", "GIA_DUNG", "BED", "SOFA", "CHAIR", "TABLE",
                "WC", "TBVS", "LAVABO", "BON", "BẾP", "BEP", "TỦ", "TU", "GIƯỜNG", "GIUONG",
                "BÀN", "BAN", "GHẾ", "GHE", "CAR", "XE", "CAY", "PLANT"
            };

            return kw.Any(k => b.Contains(k) || l.Contains(k));
        }

        private static bool IsFurnitureEntity(Entity ent, string layerUpper)
        {
            if (layerUpper.Contains("NOITHAT") || layerUpper.Contains("FURNITURE") || layerUpper.Contains("SAN") ||
                layerUpper.Contains("GIA_DUNG") || layerUpper.Contains("BED") || layerUpper.Contains("SOFA") ||
                layerUpper.Contains("TABLE") || layerUpper.Contains("CHAIR") || layerUpper.Contains("WC_TB") ||
                layerUpper.Contains("TBVS") || layerUpper.Contains("LAVABO") || layerUpper.Contains("BON_CAU") ||
                layerUpper.Contains("DIM") || layerUpper.Contains("DEFPOINTS"))
            {
                return true;
            }

            if (ent is Dimension || ent is Leader || ent is MLeader)
            {
                return true;
            }

            return false;
        }

        private static bool IsGridLinetypeOrLayer(Entity ent, string layerUpper)
        {
            if (layerUpper.Contains("GRID") || layerUpper.Contains("TRUC") || layerUpper.Contains("TRỤC") ||
                layerUpper.Contains("AXIS") || layerUpper.Contains("TIM") || layerUpper.Contains("CENTER"))
            {
                return true;
            }

            string lt = (ent.Linetype != null ? ent.Linetype : "").ToUpperInvariant();
            return (lt.Contains("CENTER") || lt.Contains("DASH") || lt.Contains("DOT") || lt.Contains("GRID") || lt.Contains("PHANTOM"));
        }

        private static void ExtractSolidColumn(Solid solid, Matrix3d transform, List<GeoSegment> list)
        {
            Point3d p0W = solid.GetPointAt(0).TransformBy(transform);
            Point3d p1W = solid.GetPointAt(1).TransformBy(transform);
            Point3d p2W = solid.GetPointAt(2).TransformBy(transform);
            Point3d p3W = solid.GetPointAt(3).TransformBy(transform);

            Point2d p0 = new Point2d(p0W.X, p0W.Y);
            Point2d p1 = new Point2d(p1W.X, p1W.Y);
            Point2d p2 = new Point2d(p2W.X, p2W.Y);
            Point2d p3 = new Point2d(p3W.X, p3W.Y);

            list.Add(new GeoSegment(p0, p1, solid.Layer));
            list.Add(new GeoSegment(p1, p3, solid.Layer));
            list.Add(new GeoSegment(p3, p2, solid.Layer));
            list.Add(new GeoSegment(p2, p0, solid.Layer));
        }

        private static void ExtractHatchColumn(Hatch hatch, Matrix3d transform, List<GeoSegment> list)
        {
            try
            {
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    if (loop.IsPolyline)
                    {
                        BulgeVertexCollection bvc = loop.Polyline;
                        int cnt = bvc.Count;
                        for (int j = 0; j < cnt; j++)
                        {
                            BulgeVertex v1 = bvc[j];
                            BulgeVertex v2 = bvc[(j + 1) % cnt];
                            Point3d p1W = new Point3d(v1.Vertex.X, v1.Vertex.Y, 0).TransformBy(transform);
                            Point3d p2W = new Point3d(v2.Vertex.X, v2.Vertex.Y, 0).TransformBy(transform);
                            list.Add(new GeoSegment(new Point2d(p1W.X, p1W.Y), new Point2d(p2W.X, p2W.Y), hatch.Layer));
                        }
                    }
                    else
                    {
                        Curve2dCollection curves = loop.Curves;
                        foreach (Curve2d c in curves)
                        {
                            LineSegment2d ls = c as LineSegment2d;
                            if (ls != null)
                            {
                                Point3d p1W = new Point3d(ls.StartPoint.X, ls.StartPoint.Y, 0).TransformBy(transform);
                                Point3d p2W = new Point3d(ls.EndPoint.X, ls.EndPoint.Y, 0).TransformBy(transform);
                                list.Add(new GeoSegment(new Point2d(p1W.X, p1W.Y), new Point2d(p2W.X, p2W.Y), hatch.Layer));
                            }
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Nhận Diện Trục Grid & Lọc Bằng Hình Học Không Gian (Wall Strip Filter)

        private static List<GeoSegment> FilterGridSystem(
            List<GeoSegment> allSegments,
            List<Circle> bubbles,
            List<GeoSegment> explicitGridLines)
        {
            HashSet<GeoSegment> toRemove = new HashSet<GeoSegment>(explicitGridLines);

            // Bỏ các đường thẳng đi xuyên qua tâm các bóng trục Grid
            if (bubbles != null && bubbles.Count > 0)
            {
                foreach (GeoSegment s in allSegments)
                {
                    if (s.Length > 2000.0) // Đường trục thường rất dài
                    {
                        foreach (Circle b in bubbles)
                        {
                            Point2d bc = new Point2d(b.Center.X, b.Center.Y);
                            if (s.Start.GetDistanceTo(bc) < b.Radius * 1.5 || s.End.GetDistanceTo(bc) < b.Radius * 1.5)
                            {
                                toRemove.Add(s);
                                break;
                            }
                        }
                    }
                }
            }

            return allSegments.Where(s => !toRemove.Contains(s)).ToList();
        }

        /// <summary>
        /// Bộ lọc Hình Học Không Gian cốt lõi:
        /// TƯỜNG = Các cặp đường thẳng song song cách nhau bề dày tường (100 - 350mm) có chiều dài >= 350mm.
        /// Áp dụng hoàn hảo cho cả Tường trực giao (0°, 90°) và Tường xéo (góc nghiêng θ bất kỳ)!
        /// </summary>
        private static List<GeoSegment> FilterWallsAndColumnsBySpatialGeometry(
            List<GeoSegment> candidates,
            List<Arc> doorArcs,
            List<GeoSegment> curvedWalls)
        {
            List<GeoSegment> wallSegments = new List<GeoSegment>();
            HashSet<GeoSegment> confirmedWalls = new HashSet<GeoSegment>();

            int count = candidates.Count;

            // 1. Dò tìm các cặp đường thẳng song song tạo thành dải tường kiến trúc (Parallel Strip)
            for (int i = 0; i < count; i++)
            {
                GeoSegment s1 = candidates[i];
                if (s1.IsCurved)
                {
                    confirmedWalls.Add(s1);
                    continue;
                }

                if (s1.Length < 100.0) continue;

                double angle1 = s1.AngleRad;

                for (int j = i + 1; j < count; j++)
                {
                    GeoSegment s2 = candidates[j];
                    if (s2.IsCurved || s2.Length < 100.0) continue;

                    // Kiểm tra song song (cùng phương hoặc ngược phương)
                    double angleDiff = Math.Abs(angle1 - s2.AngleRad);
                    if (angleDiff > Math.PI) angleDiff -= Math.PI;

                    // Cho phép sai lệch góc song song trong khoảng 4° (sin < 0.07)
                    if (Math.Sin(angleDiff) < 0.07)
                    {
                        // Tính khoảng cách vuông góc giữa 2 đoạn thẳng
                        double perpDist = ComputePerpendicularDistance(s1, s2);

                        // Bề dày tường kiến trúc chuẩn: 80mm đến 350mm
                        if (perpDist >= 80.0 && perpDist <= 350.0)
                        {
                            // Kiểm tra độ phủ chiếu (overlap) dọc theo phương tường
                            double overlap = ComputeProjectionOverlap(s1, s2);
                            if (overlap >= 150.0)
                            {
                                // Xác định góc nghiêng: Nếu không phải thuần ngang/dọc thì đánh dấu là TƯỜNG XÉO (Skewed Wall)
                                double deg = Math.Abs(angle1 * 180.0 / Math.PI) % 90.0;
                                bool isSkewed = (deg > 5.0 && deg < 85.0);

                                s1.IsSkewedWall = isSkewed;
                                s2.IsSkewedWall = isSkewed;

                                confirmedWalls.Add(s1);
                                confirmedWalls.Add(s2);
                            }
                        }
                    }
                }
            }

            // 2. Bổ sung các đoạn nách tường ngắn vuông góc nối giữa 2 mép tường (Door Jambs & Wall Ends)
            for (int i = 0; i < count; i++)
            {
                GeoSegment s = candidates[i];
                if (confirmedWalls.Contains(s)) continue;

                // Đoạn nối nách tường có chiều dài đúng bằng bề dày tường 80 - 350mm
                if (s.Length >= 80.0 && s.Length <= 350.0)
                {
                    bool touchesWallStart = confirmedWalls.Any(w => w.Start.GetDistanceTo(s.Start) < 25.0 || w.End.GetDistanceTo(s.Start) < 25.0);
                    bool touchesWallEnd = confirmedWalls.Any(w => w.Start.GetDistanceTo(s.End) < 25.0 || w.End.GetDistanceTo(s.End) < 25.0);
                    if (touchesWallStart && touchesWallEnd)
                    {
                        confirmedWalls.Add(s);
                    }
                }
            }

            // 3. Khóa kín 2 mặt nách cửa đi (Door Capping)
            List<GeoSegment> doorCaps = GenerateDoorCaps(doorArcs, confirmedWalls.ToList());
            foreach (GeoSegment cap in doorCaps)
            {
                confirmedWalls.Add(cap);
            }

            // Bổ sung các tường cong
            foreach (GeoSegment cw in curvedWalls)
            {
                confirmedWalls.Add(cw);
            }

            wallSegments.AddRange(confirmedWalls);
            return wallSegments;
        }

        private static double ComputePerpendicularDistance(GeoSegment s1, GeoSegment s2)
        {
            Vector2d dir = (s1.End - s1.Start).GetNormal();
            Vector2d normal = new Vector2d(-dir.Y, dir.X);

            Vector2d v1 = s2.Start - s1.Start;
            Vector2d v2 = s2.End - s1.Start;

            double d1 = Math.Abs(v1.X * normal.X + v1.Y * normal.Y);
            double d2 = Math.Abs(v2.X * normal.X + v2.Y * normal.Y);

            return (d1 + d2) * 0.5;
        }

        private static double ComputeProjectionOverlap(GeoSegment s1, GeoSegment s2)
        {
            Vector2d dir = (s1.End - s1.Start).GetNormal();

            double p1A = 0.0;
            double p1B = s1.Length;

            Vector2d vA = s2.Start - s1.Start;
            Vector2d vB = s2.End - s1.Start;

            double p2A = vA.X * dir.X + vA.Y * dir.Y;
            double p2B = vB.X * dir.X + vB.Y * dir.Y;

            double min2 = Math.Min(p2A, p2B);
            double max2 = Math.Max(p2A, p2B);

            double overlapStart = Math.Max(p1A, min2);
            double overlapEnd = Math.Min(p1B, max2);

            return Math.Max(0.0, overlapEnd - overlapStart);
        }

        private static List<GeoSegment> GenerateDoorCaps(List<Arc> doorArcs, List<GeoSegment> wallLines)
        {
            List<GeoSegment> caps = new List<GeoSegment>();
            if (doorArcs == null || doorArcs.Count == 0 || wallLines == null || wallLines.Count == 0) return caps;

            List<Point2d> wallEndpoints = new List<Point2d>();
            foreach (GeoSegment w in wallLines)
            {
                wallEndpoints.Add(w.Start);
                wallEndpoints.Add(w.End);
            }

            foreach (Arc arc in doorArcs)
            {
                Point2d center = new Point2d(arc.Center.X, arc.Center.Y);
                Point2d arcStart = new Point2d(arc.StartPoint.X, arc.StartPoint.Y);
                Point2d arcEnd = new Point2d(arc.EndPoint.X, arc.EndPoint.Y);

                Point2d hingeCandidate = Point2d.Origin;
                Point2d strikeCandidate = Point2d.Origin;
                double minHingeDist = double.MaxValue;
                double minStrikeDist = double.MaxValue;

                foreach (Point2d ep in wallEndpoints)
                {
                    double dHinge = ep.GetDistanceTo(center);
                    if (dHinge < minHingeDist && dHinge < 350.0)
                    {
                        minHingeDist = dHinge;
                        hingeCandidate = ep;
                    }

                    double dStrike = Math.Min(ep.GetDistanceTo(arcStart), ep.GetDistanceTo(arcEnd));
                    if (dStrike < minStrikeDist && dStrike < 350.0)
                    {
                        minStrikeDist = dStrike;
                        strikeCandidate = ep;
                    }
                }

                if (minHingeDist < 350.0 && minStrikeDist < 350.0 && hingeCandidate.GetDistanceTo(strikeCandidate) > 300.0)
                {
                    caps.Add(new GeoSegment(hingeCandidate, strikeCandidate, "DOOR_CAP"));
                }
                else
                {
                    Point2d farPt = (center.GetDistanceTo(arcStart) > center.GetDistanceTo(arcEnd)) ? arcStart : arcEnd;
                    caps.Add(new GeoSegment(center, farPt, "DOOR_CAP"));
                }
            }

            return caps;
        }

        #endregion

        #region Dò Tìm Chu Trình & Hợp Nhất Không Gian Lớn (Planar Polygonization)

        private static GraphNode FindOrCreateNode(List<GraphNode> nodes, Point2d pt, double snapTol)
        {
            foreach (GraphNode n in nodes)
            {
                if (n.Point.GetDistanceTo(pt) <= snapTol)
                {
                    return n;
                }
            }
            GraphNode newNode = new GraphNode(nodes.Count, pt);
            nodes.Add(newNode);
            return newNode;
        }

        private static List<List<RoomBoundaryVertex>> BuildPlanarPolygons(
            List<GeoSegment> segments,
            List<GeoSegment> curvedWalls,
            double snapTol)
        {
            List<List<RoomBoundaryVertex>> result = new List<List<RoomBoundaryVertex>>();
            if (segments == null || segments.Count < 3) return result;

            List<GraphNode> nodes = new List<GraphNode>();
            List<HalfEdge> allEdges = new List<HalfEdge>();

            foreach (GeoSegment seg in segments)
            {
                GraphNode n1 = FindOrCreateNode(nodes, seg.Start, snapTol);
                GraphNode n2 = FindOrCreateNode(nodes, seg.End, snapTol);
                if (n1 == n2) continue;

                double bulgeForward = seg.IsCurved ? seg.Bulge : 0.0;
                double bulgeBackward = seg.IsCurved ? -seg.Bulge : 0.0;

                HalfEdge e1 = new HalfEdge(n1, n2, bulgeForward, seg.IsSkewedWall);
                HalfEdge e2 = new HalfEdge(n2, n1, bulgeBackward, seg.IsSkewedWall);
                e1.Twin = e2;
                e2.Twin = e1;

                n1.OutEdges.Add(e1);
                n2.OutEdges.Add(e2);
                allEdges.Add(e1);
                allEdges.Add(e2);
            }

            foreach (GraphNode n in nodes)
            {
                n.OutEdges.Sort((a, b) => a.Angle.CompareTo(b.Angle));
            }

            foreach (HalfEdge edge in allEdges)
            {
                if (edge.Visited) continue;

                List<RoomBoundaryVertex> cycle = new List<RoomBoundaryVertex>();
                HalfEdge curr = edge;

                while (!curr.Visited)
                {
                    curr.Visited = true;
                    cycle.Add(new RoomBoundaryVertex(curr.From.Point, curr.Bulge));

                    GraphNode nextNode = curr.To;
                    if (nextNode.OutEdges.Count == 0) break;

                    int twinIdx = nextNode.OutEdges.IndexOf(curr.Twin);
                    if (twinIdx < 0) break;

                    int nextEdgeIdx = (twinIdx - 1 + nextNode.OutEdges.Count) % nextNode.OutEdges.Count;
                    curr = nextNode.OutEdges[nextEdgeIdx];

                    if (curr == edge)
                    {
                        break;
                    }
                }

                if (cycle.Count >= 3 && curr == edge)
                {
                    double signedArea = ComputeSignedArea(cycle);
                    if (signedArea > 0)
                    {
                        CheckAndApplyCurvedWalls(cycle, curvedWalls);
                        result.Add(cycle);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Yêu cầu quan trọng: Với các khu vực lớn chỉ vẽ 1 đường bao duy nhất bao trọn không gian,
        /// không cắt vụn hay chia nhiều đường bao con chắp vá.
        /// </summary>
        private static List<List<RoomBoundaryVertex>> UnifyLargeOpenSpaces(
            List<List<RoomBoundaryVertex>> polygons,
            double minAreaM2)
        {
            List<List<RoomBoundaryVertex>> unified = new List<List<RoomBoundaryVertex>>();

            // Lọc sơ bộ các đa giác hợp lệ (loại trừ hốc tường < 0.8 m2)
            List<List<RoomBoundaryVertex>> validPolys = new List<List<RoomBoundaryVertex>>();
            foreach (var p in polygons)
            {
                double areaM2 = Math.Abs(ComputeSignedArea(p)) / 1000000.0;
                if (areaM2 >= minAreaM2)
                {
                    validPolys.Add(p);
                }
            }

            if (validPolys.Count <= 1) return validPolys;

            // Kiểm tra các đa giác tiếp giáp nhau không có tường ngăn vật lý để hợp nhất
            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                for (int i = 0; i < validPolys.Count; i++)
                {
                    for (int j = i + 1; j < validPolys.Count; j++)
                    {
                        List<RoomBoundaryVertex> merged = TryMergePolygons(validPolys[i], validPolys[j]);
                        if (merged != null)
                        {
                            validPolys[i] = merged;
                            validPolys.RemoveAt(j);
                            mergedAny = true;
                            break;
                        }
                    }
                    if (mergedAny) break;
                }
            }

            return validPolys;
        }

        private static List<RoomBoundaryVertex> TryMergePolygons(List<RoomBoundaryVertex> p1, List<RoomBoundaryVertex> p2)
        {
            // Tìm cạnh chung giữa p1 và p2 (cùng 2 đỉnh nhưng ngược chiều)
            int n1 = p1.Count;
            int n2 = p2.Count;

            for (int i = 0; i < n1; i++)
            {
                Point2d a1 = p1[i].Point;
                Point2d a2 = p1[(i + 1) % n1].Point;

                for (int j = 0; j < n2; j++)
                {
                    Point2d b1 = p2[j].Point;
                    Point2d b2 = p2[(j + 1) % n2].Point;

                    // Nếu chia sẻ cạnh chung (a1 ≈ b2 và a2 ≈ b1)
                    if (a1.GetDistanceTo(b2) < 25.0 && a2.GetDistanceTo(b1) < 25.0)
                    {
                        // Ghép 2 chu trình lại thành 1 chu trình bao ngoài duy nhất
                        List<RoomBoundaryVertex> merged = new List<RoomBoundaryVertex>();

                        for (int k = 0; k <= i; k++) merged.Add(p1[k]);
                        for (int k = 1; k < n2; k++)
                        {
                            int idx = (j + 1 + k) % n2;
                            merged.Add(p2[idx]);
                        }
                        for (int k = i + 2; k < n1; k++) merged.Add(p1[k]);

                        return merged;
                    }
                }
            }

            return null;
        }

        private static double ComputeSignedArea(List<RoomBoundaryVertex> pts)
        {
            double area = 0.0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Point2d p1 = pts[i].Point;
                Point2d p2 = pts[(i + 1) % n].Point;
                area += (p1.X * p2.Y - p2.X * p1.Y);
            }
            return area * 0.5;
        }

        private static void CheckAndApplyCurvedWalls(List<RoomBoundaryVertex> cycle, List<GeoSegment> curvedWalls)
        {
            if (curvedWalls == null || curvedWalls.Count == 0) return;

            int n = cycle.Count;
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(cycle[i].Bulge) > 1e-4) continue;

                Point2d p1 = cycle[i].Point;
                Point2d p2 = cycle[(i + 1) % n].Point;

                foreach (GeoSegment cw in curvedWalls)
                {
                    double d1 = p1.GetDistanceTo(cw.Start) + p2.GetDistanceTo(cw.End);
                    double d2 = p1.GetDistanceTo(cw.End) + p2.GetDistanceTo(cw.Start);

                    if (d1 < 40.0)
                    {
                        cycle[i].Bulge = cw.Bulge;
                        break;
                    }
                    else if (d2 < 40.0)
                    {
                        cycle[i].Bulge = -cw.Bulge;
                        break;
                    }
                }
            }
        }

        #endregion

        #region Xử Lý Dữ Liệu Phòng & Tạo Đối Tượng CAD

        private static List<RoomInfo> ProcessAndFilterRooms(
            List<List<RoomBoundaryVertex>> polygons,
            List<RoomSeedText> seedTexts,
            RoomBoundaryOptions options)
        {
            List<RoomInfo> rooms = new List<RoomInfo>();
            int roomIndex = 1;

            foreach (List<RoomBoundaryVertex> poly in polygons)
            {
                double areaMm2 = Math.Abs(ComputeSignedArea(poly));
                double areaM2 = areaMm2 / 1000000.0;

                if (areaM2 < options.MinAreaM2 || areaM2 > options.MaxAreaM2)
                {
                    continue;
                }

                double perimeterMm = 0.0;
                double cx = 0.0, cy = 0.0;
                int n = poly.Count;
                for (int i = 0; i < n; i++)
                {
                    perimeterMm += poly[i].Point.GetDistanceTo(poly[(i + 1) % n].Point);
                    cx += poly[i].Point.X;
                    cy += poly[i].Point.Y;
                }
                double perimeterM = perimeterMm / 1000.0;
                Point2d center2d = new Point2d(cx / n, cy / n);

                List<string> matchedNames = new List<string>();
                Point2d textPos = center2d;

                foreach (RoomSeedText st in seedTexts)
                {
                    if (IsPointInsidePolygon(st.Position, poly))
                    {
                        matchedNames.Add(st.Text);
                        textPos = st.Position;
                    }
                }

                string roomName = matchedNames.Count > 0 ? string.Join(" & ", matchedNames.Distinct()) : string.Format("PHÒNG {0:D2}", roomIndex);
                string roomCode = string.Format("P-{0:D2}", roomIndex);

                bool hasCurve = poly.Any(v => Math.Abs(v.Bulge) > 1e-4);

                RoomInfo room = new RoomInfo();
                room.Code = roomCode;
                room.Name = roomName;
                room.AreaM2 = Math.Round(areaM2, 2);
                room.PerimeterM = Math.Round(perimeterM, 2);
                room.Centroid = new Point3d(textPos.X, textPos.Y, 0);
                room.Vertices = poly;
                room.HasCurvedWall = hasCurve;

                rooms.Add(room);
                roomIndex++;
            }

            return rooms;
        }

        private static bool IsPointInsidePolygon(Point2d pt, List<RoomBoundaryVertex> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d pi = poly[i].Point;
                Point2d pj = poly[j].Point;

                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static void CreateCadRoomEntities(
            BlockTableRecord btr,
            Transaction tr,
            RoomInfo room,
            RoomBoundaryOptions options)
        {
            // 1. Tạo đường bao LWPOLYLINE khép kín
            Polyline pline = new Polyline();
            pline.SetDatabaseDefaults();
            pline.Layer = options.BoundaryLayer;
            pline.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)options.BoundaryColorIndex);
            pline.LineWeight = LineWeight.LineWeight035;

            int n = room.Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                RoomBoundaryVertex v = room.Vertices[i];
                pline.AddVertexAt(i, v.Point, v.Bulge, 0, 0);
            }
            pline.Closed = true;

            // 2. Đính kèm XData SMART_ROOM_BOUNDARY
            if (options.AttachXData)
            {
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, "SMART_ROOM_BOUNDARY"),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, room.Code),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, room.Name),
                    new TypedValue((int)DxfCode.ExtendedDataReal, room.AreaM2),
                    new TypedValue((int)DxfCode.ExtendedDataReal, room.PerimeterM),
                    new TypedValue((int)DxfCode.ExtendedDataReal, room.CeilingHeightMm)
                );
                pline.XData = rb;
            }

            ObjectId plId = btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
            room.PolylineId = plId;

            // 3. Tạo MText hiển thị Tên phòng và Diện tích tại tâm phòng
            if (options.CreateMText)
            {
                MText mtext = new MText();
                mtext.SetDatabaseDefaults();
                mtext.Layer = options.TextLayer;
                mtext.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)options.TextColorIndex);
                mtext.Location = room.Centroid;
                mtext.TextHeight = options.TextHeight;
                mtext.Attachment = AttachmentPoint.MiddleCenter;

                mtext.Contents = string.Format("{{\\C1;\\fSegoe UI|b1;{0}}}\n{{\\C3;\\fJetBrains Mono;S = {1:F2} m²}}",
                    room.Name, room.AreaM2);

                ObjectId txtId = btr.AppendEntity(mtext);
                tr.AddNewlyCreatedDBObject(mtext, true);
                room.TextId = txtId;
            }
        }

        private static void EnsureLayersAndAppId(Database db, Transaction tr, RoomBoundaryOptions options)
        {
            RegAppTable regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!regTable.Has("SMART_ROOM_BOUNDARY"))
            {
                regTable.UpgradeOpen();
                RegAppTableRecord rtr = new RegAppTableRecord();
                rtr.Name = "SMART_ROOM_BOUNDARY";
                regTable.Add(rtr);
                tr.AddNewlyCreatedDBObject(rtr, true);
            }

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(options.BoundaryLayer))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = options.BoundaryLayer;
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)options.BoundaryColorIndex);
                ltr.LineWeight = LineWeight.LineWeight035;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            if (!lt.Has(options.TextLayer))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = options.TextLayer;
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)options.TextColorIndex);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private static string CleanRoomText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string clean = Regex.Replace(raw, @"\\[A-Za-z0-9]+;?", " ");
            clean = Regex.Replace(clean, @"\{|\}", " ");
            clean = clean.Replace("\\P", " ").Replace("\r", " ").Replace("\n", " ");
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            return clean;
        }

        private static bool IsLikelyRoomName(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt) || txt.Length < 2) return false;
            double dummy;
            if (double.TryParse(txt.Replace(",", "."), out dummy)) return false;
            if (txt.StartsWith("+") || txt.StartsWith("-") || txt.Contains("%%P") || txt.Contains("±")) return false;

            string u = txt.ToUpperInvariant();
            string[] keywords = new string[]
            {
                "PHÒNG", "P.", "PHONG", "NGỦ", "NGU", "KHÁCH", "KHACH", "BẾP", "BEP", "ĂN", "AN",
                "WC", "VỆ SINH", "VE SINH", "TOILET", "THỜ", "THO", "KHO", "SHC", "SINH HOẠT",
                "BAN CÔNG", "BAN CONG", "LOGIA", "LÔ GIA", "LO GIA", "SÂN", "SAN", "GARAGE",
                "NHÀ XE", "NHA XE", "GIẶT", "GIAT", "SẢNH", "SANH", "HÀNH LANG", "HANH LANG",
                "LOBBY", "OFFICE", "MEETING", "CHỨC NĂNG", "MASTER", "BEDROOM", "LIVING", "KITCHEN"
            };

            return keywords.Any(k => u.Contains(k));
        }

        #endregion
    }
}

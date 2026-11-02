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
    public class LengthCalculatorService
    {
        /// <summary>
        /// Thực hiện chọn đối tượng Line và Polyline trên bản vẽ và thống kê theo Layer
        /// </summary>
        public List<LayerLengthStat> CollectAndCalculateLengths(Document doc, out SummaryResult summary)
        {
            summary = new SummaryResult();
            var resultList = new List<LayerLengthStat>();

            if (doc == null) return resultList;
            Editor ed = doc.Editor;

            // 1. Tạo bộ lọc chỉ chọn Line, Polyline (Lwpolyline), Polyline2d, Polyline3d
            TypedValue[] filterList = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter filter = new SelectionFilter(filterList);

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\n[TaiAi] Quét chọn các đối tượng Line / Polyline cần thống kê: ";

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
            {
                ed.WriteMessage("\n[TaiAi] Không có đối tượng nào được chọn hoặc lệnh đã bị hủy.");
                return resultList;
            }

            // 2. Duyệt qua các đối tượng được chọn trong Transaction
            Database db = doc.Database;
            Dictionary<string, LayerLengthStat> layerDict = new Dictionary<string, LayerLengthStat>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in psr.Value)
                {
                    if (selObj == null) continue;

                    Entity ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    string layerName = ent.Layer;
                    if (!layerDict.ContainsKey(layerName))
                    {
                        LayerLengthStat newStat = new LayerLengthStat();
                        newStat.LayerName = layerName;
                        newStat.LineCount = 0;
                        newStat.PolylineCount = 0;
                        newStat.TotalLength = 0;
                        layerDict[layerName] = newStat;
                    }

                    LayerLengthStat stat = layerDict[layerName];

                    Line line = ent as Line;
                    if (line != null)
                    {
                        stat.LineCount++;
                        stat.TotalLength += line.Length;
                        summary.TotalLines++;
                        summary.GrandTotalLength += line.Length;
                        continue;
                    }

                    Polyline pline = ent as Polyline;
                    if (pline != null)
                    {
                        stat.PolylineCount++;
                        stat.TotalLength += pline.Length;
                        summary.TotalPolylines++;
                        summary.GrandTotalLength += pline.Length;
                        continue;
                    }

                    Polyline2d p2d = ent as Polyline2d;
                    if (p2d != null)
                    {
                        stat.PolylineCount++;
                        stat.TotalLength += p2d.Length;
                        summary.TotalPolylines++;
                        summary.GrandTotalLength += p2d.Length;
                        continue;
                    }

                    Polyline3d p3d = ent as Polyline3d;
                    if (p3d != null)
                    {
                        stat.PolylineCount++;
                        stat.TotalLength += p3d.Length;
                        summary.TotalPolylines++;
                        summary.GrandTotalLength += p3d.Length;
                        continue;
                    }
                }

                tr.Commit();
            }

            // Đánh số thứ tự và sắp xếp theo tên Layer
            int index = 1;
            foreach (KeyValuePair<string, LayerLengthStat> kvp in layerDict.OrderBy(x => x.Key))
            {
                kvp.Value.Stt = index++;
                resultList.Add(kvp.Value);
            }

            return resultList;
        }

        /// <summary>
        /// Tạo đối tượng Table trong AutoCAD chứa kết quả thống kê
        /// </summary>
        public bool CreateSummaryTable(Document doc, List<LayerLengthStat> stats, SummaryResult summary, bool unitInMeters)
        {
            if (doc == null || stats == null || stats.Count == 0) return false;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // 1. Cho người dùng chỉ định điểm đặt bảng
            PromptPointOptions ppo = new PromptPointOptions("\n[TaiAi] Chọn điểm đặt bảng thống kê (Insertion Point): ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[TaiAi] Hủy đặt bảng.");
                return false;
            }

            Point3d insertPoint = ppr.Value;

            // 2. Tạo Table trong Transaction
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                Table table = new Table();
                table.TableStyle = db.Tablestyle;
                table.Position = insertPoint;

                // Cấu trúc bảng: 1 hàng Tiêu đề + 1 hàng Header + N hàng Dữ liệu + 1 hàng Tổng cộng
                int numRows = stats.Count + 3;
                int numCols = 5;

                table.SetSize(numRows, numCols);
                table.SetRowHeight(8.0);

                // Độ rộng từng cột: STT, Layer, Số Line, Số Pline, Tổng chiều dài
                table.Columns[0].Width = 15.0; // STT
                table.Columns[1].Width = 45.0; // Layer
                table.Columns[2].Width = 25.0; // Line
                table.Columns[3].Width = 25.0; // Pline
                table.Columns[4].Width = 35.0; // Chiều dài

                // --- HÀNG 0: TIÊU ĐỀ BẢNG (TITLE) ---
                table.Rows[0].Height = 12.0;
                table.MergeCells(CellRange.Create(table, 0, 0, 0, numCols - 1));
                Cell titleCell = table.Cells[0, 0];
                titleCell.TextString = "BẢNG THỐNG KÊ ĐỘ DÀI LINE & POLYLINE";
                titleCell.TextHeight = 4.0;
                titleCell.Alignment = CellAlignment.MiddleCenter;

                // --- HÀNG 1: TIÊU ĐỀ CÁC CỘT (HEADER) ---
                table.Rows[1].Height = 10.0;
                string[] headers = new string[]
                {
                    "STT",
                    "Tên Layer",
                    "SL Line",
                    "SL Pline",
                    unitInMeters ? "Tổng dài (m)" : "Tổng dài (mm)"
                };

                for (int col = 0; col < numCols; col++)
                {
                    Cell cell = table.Cells[1, col];
                    cell.TextString = headers[col];
                    cell.TextHeight = 3.0;
                    cell.Alignment = CellAlignment.MiddleCenter;
                }

                // --- CÁC HÀNG DỮ LIỆU ---
                for (int i = 0; i < stats.Count; i++)
                {
                    int row = i + 2;
                    LayerLengthStat item = stats[i];
                    table.Rows[row].Height = 7.0;

                    // STT
                    table.Cells[row, 0].TextString = item.Stt.ToString();
                    table.Cells[row, 0].Alignment = CellAlignment.MiddleCenter;

                    // Layer
                    table.Cells[row, 1].TextString = item.LayerName;
                    table.Cells[row, 1].Alignment = CellAlignment.MiddleLeft;

                    // Số lượng Line
                    table.Cells[row, 2].TextString = item.LineCount.ToString();
                    table.Cells[row, 2].Alignment = CellAlignment.MiddleCenter;

                    // Số lượng Pline
                    table.Cells[row, 3].TextString = item.PolylineCount.ToString();
                    table.Cells[row, 3].Alignment = CellAlignment.MiddleCenter;

                    // Tổng dài
                    table.Cells[row, 4].TextString = unitInMeters ? item.FormattedLengthM : item.FormattedLengthMm;
                    table.Cells[row, 4].Alignment = CellAlignment.MiddleRight;

                    // Chỉnh chiều cao chữ các ô dữ liệu
                    for (int col = 0; col < numCols; col++)
                    {
                        table.Cells[row, col].TextHeight = 2.5;
                    }
                }

                // --- HÀNG CUỐI: TỔNG CỘNG (SUMMARY) ---
                int lastRow = numRows - 1;
                table.Rows[lastRow].Height = 9.0;

                // Merge STT và Layer thành "TỔNG CỘNG"
                table.MergeCells(CellRange.Create(table, lastRow, 0, lastRow, 1));
                Cell totalLabelCell = table.Cells[lastRow, 0];
                totalLabelCell.TextString = "TỔNG CỘNG";
                totalLabelCell.TextHeight = 3.0;
                totalLabelCell.Alignment = CellAlignment.MiddleCenter;

                table.Cells[lastRow, 2].TextString = summary.TotalLines.ToString();
                table.Cells[lastRow, 2].TextHeight = 3.0;
                table.Cells[lastRow, 2].Alignment = CellAlignment.MiddleCenter;

                table.Cells[lastRow, 3].TextString = summary.TotalPolylines.ToString();
                table.Cells[lastRow, 3].TextHeight = 3.0;
                table.Cells[lastRow, 3].Alignment = CellAlignment.MiddleCenter;

                double totalLengthVal = unitInMeters ? (summary.GrandTotalLength / 1000.0) : summary.GrandTotalLength;
                table.Cells[lastRow, 4].TextString = totalLengthVal.ToString("N2");
                table.Cells[lastRow, 4].TextHeight = 3.0;
                table.Cells[lastRow, 4].Alignment = CellAlignment.MiddleRight;

                // 3. Thêm bảng vào CurrentSpace (ModelSpace hoặc PaperSpace)
                btr.AppendEntity(table);
                tr.AddNewlyCreatedDBObject(table, true);

                tr.Commit();
            }

            ed.WriteMessage("\n[TaiAi] Đã chèn bảng thống kê thành công!");
            return true;
        }
    }
}

using System;



using System.Collections.Generic;



using System.Diagnostics;



using System.Globalization;



using System.IO;



using System.Reflection;



using System.Text.RegularExpressions;



using Autodesk.AutoCAD.ApplicationServices;



using Autodesk.AutoCAD.DatabaseServices;



using Autodesk.AutoCAD.EditorInput;



using Autodesk.AutoCAD.Geometry;
using Gi = Autodesk.AutoCAD.GraphicsInterface;
using TaiAiAddin.Models;



using System.Linq;



using System.Web.Script.Serialization;



namespace TaiAiAddin.Services



{



    /// <summary>



    /// CAD In-App Co-Pilot: Tự động trích xuất, gọi Universal Boundary Engine V3.0 (Python/Shapely)



    /// để nhận diện đa giác biên phức tạp (ôm cột 90/270°, khóa kín cửa 2 mặt tường, bo góc tường cong Bulge, gán sàn âm WC -50mm LOD 300).



    /// </summary>



    public static class CoPilotRoomBoundaryService



    {



        public const string LAYER_BOUNDARY = "DUONG_BAO";

        public const string LAYER_BOUNDARY_TEXT = "DUONG_BAO_TEXT";

        public const string LAYER_DIEM_NOI = "AI_XD_DIEM_NOI";

        public const string XDATA_APP_NAME = "SMART_ROOM_BOUNDARY";



        /// <summary>



        /// Khởi chạy Co-Pilot tự động bao toàn bộ các phòng trên mặt bằng (LOD 300)



        /// </summary>



        public static CoPilotResult ExecuteAllRooms(Document doc, bool isBatchAll = true)



        {



            CoPilotResult result = new CoPilotResult();



            if (doc == null)



            {



                result.Message = "Tài liệu CAD hiện hành không hợp lệ.";



                return result;



            }



            Database db = doc.Database;



            Editor ed = doc.Editor;



            Stopwatch sw = Stopwatch.StartNew();



            try



            {



                string pyExe = FindPythonExecutable();



                string pyScript = FindBoundaryEngineScript();



                if (string.IsNullOrEmpty(pyExe) || !File.Exists(pyExe))



                {



                    result.Message = "Không tìm thấy trình thông dịch Python!";



                    ed.WriteMessage("\n[Co-Pilot] Lỗi: Không tìm thấy trình thông dịch Python (cần thư viện ezdxf, shapely).");



                    return result;



                }



                if (string.IsNullOrEmpty(pyScript) || !File.Exists(pyScript))



                {



                    result.Message = "Không tìm thấy file boundary_engine.py!";



                    ed.WriteMessage("\n[Co-Pilot] Lỗi: Không tìm thấy file script boundary_engine.py.");



                    return result;



                }



                ed.WriteMessage("\n⚡ [Co-Pilot LOD 300] Đang xuất dữ liệu mặt bằng kiến trúc sang Engine hình học không gian...");



                string tempDir = Path.GetTempPath();



                string stamp = DateTime.Now.Ticks.ToString();



                string dxfPath = Path.Combine(tempDir, "copilot_plan_" + stamp + ".dxf");



                string outLsp = Path.Combine(tempDir, "copilot_res_" + stamp + ".lsp");



                List<ObjectId> tempDoorBridges = null;
                try
                {
                    SetDiemNoiLayerVisibility(db, true);
                    tempDoorBridges = CreateTemporaryStraightDoorBridges(doc, null);
                    using (doc.LockDocument())
                    {
                        db.DxfOut(dxfPath, 16, DwgVersion.Current);
                    }
                }
                finally
                {
                    CleanupTemporaryStraightDoorBridges(doc, tempDoorBridges);
                }



                ed.WriteMessage("\n⚡ [Co-Pilot LOD 300] Đang phân tích hình học, ôm cột 90°, khóa kín cửa, bo tường cong & hạ sàn WC -50mm...");



                // 2. Chạy boundary_engine.py



                ProcessStartInfo psi = new ProcessStartInfo();



                psi.FileName = pyExe;



                psi.Arguments = string.Format("\"{0}\" --dxf \"{1}\" --out \"{2}\"", pyScript, dxfPath, outLsp);



                psi.CreateNoWindow = true;



                psi.UseShellExecute = false;



                psi.RedirectStandardOutput = true;



                psi.RedirectStandardError = true;



                using (Process p = Process.Start(psi))



                {



                    p.WaitForExit(60000); // 60 giây tối đa



                }



                if (!File.Exists(outLsp))



                {



                    result.Message = "Engine không sinh được kết quả đường bao.";



                    ed.WriteMessage("\n[Co-Pilot] Engine không tạo được file kết quả.");



                    return result;



                }



                // 3. Phân tích kết quả từ file LISP để cập nhật Palette



                List<CoPilotRoomItem> rooms = ParseRoomsFromLsp(outLsp);



                double totalArea = 0;



                double totalPerimeter = 0;



                int wcCount = 0;



                int curvedCount = 0;



                foreach (CoPilotRoomItem rm in rooms)



                {



                    totalArea += rm.AreaM2;



                    totalPerimeter += rm.PerimeterM;



                    if (rm.IsWc) wcCount++;



                    if (rm.HasCurvedWall) curvedCount++;



                }



                // 4. Nạp và thực thi mã vẽ AutoLISP trực tiếp vào AutoCAD



                string lspCmd = string.Format("(load \"{0}\")\n", outLsp.Replace("\\", "/"));



                doc.SendStringToExecute(lspCmd, true, false, false);



                sw.Stop();



                result.Success = true;



                result.RoomCount = rooms.Count;



                result.TotalAreaM2 = totalArea;



                result.TotalPerimeterM = totalPerimeter;



                result.WcCount = wcCount;



                result.CurvedCount = curvedCount;



                result.ElapsedMs = sw.ElapsedMilliseconds;



                result.Rooms = rooms;



                result.Message = string.Format("Co-Pilot đã vẽ xong {0} phòng ({1:F2} m², {2} phòng WC sàn âm -50mm, {3} phòng cong) trong {4} ms!", 



                    result.RoomCount, result.TotalAreaM2, result.WcCount, result.CurvedCount, result.ElapsedMs);



                ed.WriteMessage(string.Format("\n⚡ [Co-Pilot LOD 300] Đã phân tích xong {0} phòng ({1:F2} m²). Đang vẽ lên bản vẽ...", 



                    result.RoomCount, result.TotalAreaM2));



                ed.WriteMessage("\nĐã vẽ xong mời bạn Check.\n");



            }



            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Lỗi Co-Pilot: " + ex.Message;
                ed.WriteMessage("\n[Co-Pilot Lỗi] " + ex.ToString());
            }
            finally
            {
                SetDiemNoiLayerVisibility(db, false);
            }

            return result;



        }



        /// <summary>



        /// Khởi chạy Co-Pilot cho 1 phòng cụ thể khi người dùng click chọn 1 điểm



        /// </summary>



                /// <summary>
        /// QUÉT & BAO NHIỀU PHÒNG LIÊN TỤC TRÊN CAD (PICK LIÊN TỤC CHO ĐẾN KHI ENTER/ESC)
        /// Tự động cộng dồn tất cả các phòng đã click vào danh sách. Khóa cứng theo scopeBounds (AI_XD_KHUNG_BAO).
        /// </summary>
        public static CoPilotResult ExecutePickMultipleRooms(Document doc, Extents3d? scopeBounds = null)
        {
            CoPilotResult result = new CoPilotResult();
            if (doc == null) return result;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<CoPilotRoomItem> allRooms = new List<CoPilotRoomItem>();

            try
            {
                // QUY TẮC CỐT LÕI: LÀM MỚI DỮ LIỆU NỀN TRƯỚC KHI QUÉT MỚI (XÓA SẠCH DUONG_BAO VÀ DUONG_BAO_TEXT CŨ)
                ClearAiBoundaries(doc);

                string pyExe = FindPythonExecutable();
                string pyScript = FindBoundaryEngineScript();
                string tempDir = Path.GetTempPath();
                string stamp = DateTime.Now.Ticks.ToString();
                string dxfPath = Path.Combine(tempDir, "copilot_multipick_" + stamp + ".dxf");

                bool hasPython = !string.IsNullOrEmpty(pyExe) && File.Exists(pyExe) && !string.IsNullOrEmpty(pyScript) && File.Exists(pyScript);

                // BẬT SÁNG LAYER ĐIỂM NỐI (AI_XD_DIEM_NOI) & BẮN THẲNG QUA CÁC Ô CỬA CÓ 2 MÉP TƯỜNG/CỘT TIẾP GIÁP
                UnhideAllLayers(db);
                SetDiemNoiLayerVisibility(db, true);
                List<ObjectId> tempDoorBridges = CreateTemporaryStraightDoorBridges(doc, scopeBounds);

                if (hasPython)
                {
                    ed.WriteMessage("\n⚡ [Co-Pilot LOD 300] Đang chuẩn bị dữ liệu mặt bằng để quét nhiều phòng...");
                    using (doc.LockDocument())
                    {
                        db.DxfOut(dxfPath, 16, DwgVersion.Current);
                    }
                }

                int roomIndex = 1;
                ed.WriteMessage("\n🎯 [BẮT ĐẦU QUÉT NHIỀU PHÒNG] Click chuột vào từng phòng trên CAD. Bấm ENTER hoặc phím ESC khi chọn xong!\n");
                ed.UpdateScreen();

                try
                {
                    while (true)
                    {
                        PromptPointOptions ppo = new PromptPointOptions(
                            string.Format("\n[Bao Phòng AI] Click chọn điểm trong lòng phòng thứ {0} (Bấm ENTER/ESC để kết thúc): ", roomIndex));
                        ppo.AllowNone = true;

                        PromptPointResult ppr = ed.GetPoint(ppo);
                        if (ppr.Status != PromptStatus.OK)
                        {
                            break;
                        }

                        Point3d pt = ppr.Value;

                        // LỚP 1: KIỂM SOÁT ĐIỂM CHỌN - CHẶN TUYỆT ĐỐI NẾU CLICK NGOÀI KHUNG BAO
                        if (scopeBounds.HasValue)
                        {
                            double minX = scopeBounds.Value.MinPoint.X;
                            double maxX = scopeBounds.Value.MaxPoint.X;
                            double minY = scopeBounds.Value.MinPoint.Y;
                            double maxY = scopeBounds.Value.MaxPoint.Y;

                            if (pt.X < minX || pt.X > maxX || pt.Y < minY || pt.Y > maxY)
                            {
                                ed.WriteMessage("\n⚠️ [CHẶN TUYỆT ĐỐI] Điểm chọn nằm NGOÀI phạm vi khung bao đã khóa (AI_XD_KHUNG_BAO)! Vui lòng click bên trong khung.");
                                continue;
                            }
                        }

                        ed.WriteMessage(string.Format("\n⚡ Đang tính toán đường bao phòng thứ {0} tại ({1:F1}, {2:F1})...", roomIndex, pt.X, pt.Y));

                        List<CoPilotRoomItem> pickedRooms = null;

                        // 1. Phân tích qua Python boundary_engine nếu có
                        if (hasPython && File.Exists(dxfPath))
                        {
                            string outLsp = Path.Combine(tempDir, string.Format("copilot_pick_res_{0}_{1}.lsp", stamp, roomIndex));
                            ProcessStartInfo psi = new ProcessStartInfo();
                            psi.FileName = pyExe;
                            psi.Arguments = string.Format(CultureInfo.InvariantCulture,
                                "\"{0}\" --dxf \"{1}\" --point {2:F2} {3:F2} --out \"{4}\"",
                                pyScript, dxfPath, pt.X, pt.Y, outLsp);
                            psi.CreateNoWindow = true;
                            psi.UseShellExecute = false;
                            using (Process p = Process.Start(psi))
                            {
                                p.WaitForExit(30000);
                            }

                            if (File.Exists(outLsp))
                            {
                                pickedRooms = ParseRoomsFromLsp(outLsp);
                                try { File.Delete(outLsp); } catch { }
                            }
                        }

                        // 2. Fallback nhanh & chuẩn xác bằng AutoCAD TraceBoundary native API
                        if (pickedRooms == null || pickedRooms.Count == 0)
                        {
                            using (doc.LockDocument())
                            {
                                Dictionary<ObjectId, bool> savedLayerStates = new Dictionary<ObjectId, bool>();

                                // BƯỚC 2A: TẮT TRIỆT ĐỂ MỌI LAYER NỘI THẤT, DIM, TEXT, RAC, KHUNG BAO, ĐƯỜNG BAO CŨ...
                                // CHỈ GIỮ BẬT SÁNG DUY NHẤT CÁC LAYER TẠO BIÊN PHÒNG: TUONG, COT, CUA, VACH KINH
                                using (Transaction trOff = db.TransactionManager.StartTransaction())
                                {
                                    try
                                    {
                                        LayerTable lt = trOff.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                                        // Đặt Clayer về AI_XD_TUONG để không bị lỗi khi tắt current layer
                                        if (lt.Has("AI_XD_TUONG"))
                                        {
                                            try { db.Clayer = lt["AI_XD_TUONG"]; } catch { }
                                        }
                                        else if (lt.Has(LAYER_BOUNDARY))
                                        {
                                            try { db.Clayer = lt[LAYER_BOUNDARY]; } catch { }
                                        }

                                        foreach (ObjectId layId in lt)
                                        {
                                            LayerTableRecord ltr = trOff.GetObject(layId, OpenMode.ForWrite) as LayerTableRecord;
                                            if (ltr == null) continue;

                                            savedLayerStates[layId] = ltr.IsOff;

                                            string lName = (ltr.Name ?? "").Trim().ToUpperInvariant();

                                            // CORE SPEC: Xác định các layer cấu trúc biên bao phòng (TẮT AI_XD_CUA/DOOR để bắn thẳng qua cửa, không bao quanh cánh/khuôn/cung cửa!)
                                            bool isStructuralBoundary = (lName == "AI_XD_TUONG" || lName == "AI_XD_COT" || lName == "AI_XD_VACH_KINH" || lName == "AI_XD_DIEM_NOI");

                                            if (!isStructuralBoundary && !lName.StartsWith("AI_XD_"))
                                            {
                                                bool isDoorLayer = (lName.Contains("CUA") || lName.Contains("DOOR")) &&
                                                                   !lName.Contains("CUA_SO") && !lName.Contains("CUASO") && !lName.Contains("KINH") && !lName.Contains("WINDOW");
                                                bool isWallOrCol = !isDoorLayer && (lName.Contains("TUONG") || lName.Contains("WALL") || lName.Contains("COT") || lName.Contains("COLU") || lName.Contains("KINH") || lName.Contains("GLASS"));
                                                bool isNoise = (lName.Contains("NOI_THAT") || lName.Contains("FURN") || lName.Contains("VATDUNG") || lName.Contains("BED") || lName.Contains("SOFA") || lName.Contains("TABLE") || lName.Contains("CHAIR") || lName.Contains("DIM") || lName.Contains("TEXT") || lName.Contains("GRID") || lName.Contains("TRUC") || lName.Contains("HATCH") || lName.Contains("RAC") || lName.Contains("SAN") || lName.Contains("FLOR") || lName.Contains("DEFPOINTS") || lName == "0");
                                                if (isWallOrCol && !isNoise)
                                                {
                                                    isStructuralBoundary = true;
                                                }
                                            }

                                            if (isStructuralBoundary)
                                            {
                                                ltr.IsOff = false; // BẬT SÁNG ĐỂ BẮT BIÊN
                                            }
                                            else
                                            {
                                                // TẮT TRIỆT ĐỂ: Bắn xuyên qua 100% đồ rời nội thất, bỏ qua dim, text, hatch, rác
                                                if (layId != db.Clayer)
                                                {
                                                    ltr.IsOff = true;
                                                }
                                            }
                                        }

                                        trOff.Commit();
                                    }
                                    catch
                                    {
                                        trOff.Abort();
                                    }
                                }

                                // BƯỚC 2B: THỰC THI TRACE BOUNDARY TRÊN NỀN BẢN VẼ CHỈ CÒN TƯỜNG - CỘT - CỬA
                                DBObjectCollection objColl = null;
                                try
                                {
                                    objColl = ed.TraceBoundary(pt, false);
                                }
                                catch { }

                                // BƯỚC 2C: PHỤC HỒI TRẠNG THÁI HIỂN THỊ CÁC LAYER
                                using (Transaction trOn = db.TransactionManager.StartTransaction())
                                {
                                    try
                                    {
                                        foreach (KeyValuePair<ObjectId, bool> kvp in savedLayerStates)
                                        {
                                            try
                                            {
                                                LayerTableRecord ltr = trOn.GetObject(kvp.Key, OpenMode.ForWrite) as LayerTableRecord;
                                                if (ltr != null)
                                                {
                                                    string lName = (ltr.Name ?? "").Trim().ToUpperInvariant();
                                                    // DUONG_BAO và DUONG_BAO_TEXT luôn ưu tiên BẬT SÁNG
                                                    if (lName == LAYER_BOUNDARY.ToUpperInvariant() || lName == LAYER_BOUNDARY_TEXT.ToUpperInvariant())
                                                    {
                                                        ltr.IsOff = false;
                                                    }
                                                    else
                                                    {
                                                        ltr.IsOff = kvp.Value;
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                        trOn.Commit();
                                    }
                                    catch
                                    {
                                        trOn.Abort();
                                    }
                                }

                                // BƯỚC 2D: TRÍCH XUẤT ĐA GIÁC ĐƯỜNG BAO LỚN NHẤT
                                if (objColl != null && objColl.Count > 0)
                                {
                                    pickedRooms = new List<CoPilotRoomItem>();
                                    Polyline outerPl = null;
                                    double maxArea = 0;

                                    foreach (DBObject obj in objColl)
                                    {
                                        Polyline pl = obj as Polyline;
                                        if (pl != null && pl.Closed && pl.Area > 400000.0) // S > 0.4 m2
                                        {
                                            if (pl.Area > maxArea)
                                            {
                                                maxArea = pl.Area;
                                                outerPl = pl;
                                            }
                                        }
                                    }

                                    if (outerPl != null)
                                    {
                                        CoPilotRoomItem item = new CoPilotRoomItem();
                                        item.AreaM2 = outerPl.Area / 1000000.0;
                                        item.PerimeterM = outerPl.Length / 1000.0;
                                        Extents3d ext = outerPl.GeometricExtents;
                                        item.CenterX = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                                        item.CenterY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                                        item.LabelX = item.CenterX;
                                        item.LabelY = item.CenterY;
                                        item.RoomName = string.Format("PH-{0:D2}", roomIndex);

                                        for (int v = 0; v < outerPl.NumberOfVertices; v++)
                                        {
                                            Point2d vp = outerPl.GetPoint2dAt(v);
                                            item.Vertices.Add(new CoPilotPoint2D(vp.X, vp.Y));
                                            item.Bulges.Add(outerPl.GetBulgeAt(v));
                                        }
                                        pickedRooms.Add(item);
                                    }

                                    foreach (DBObject obj in objColl)
                                    {
                                        try { obj.Dispose(); } catch { }
                                    }
                                }
                            }
                        }

                        // LỚP 2: CẮT GỌT / KHÓA CHẶT ĐA GIÁC TRONG PHẠM VI KHUNG BAO (TUYỆT ĐỐI KHÔNG TRÀN VIỀN)
                        if (pickedRooms != null && pickedRooms.Count > 0)
                        {
                            if (scopeBounds.HasValue)
                            {
                                List<CoPilotRoomItem> boundedRooms = new List<CoPilotRoomItem>();
                                foreach (var rm in pickedRooms)
                                {
                                    var clipped = ClipRoomToBoundary(rm, scopeBounds.Value);
                                    if (clipped != null && clipped.AreaM2 >= 0.3)
                                    {
                                        boundedRooms.Add(clipped);
                                    }
                                    else
                                    {
                                        ed.WriteMessage("\n❌ [TRÀN VIỀN BỊ LOẠI BỎ] Đường bao phòng tràn ra ngoài phạm vi khung bao đã bị loại bỏ tuyệt đối.");
                                    }
                                }
                                pickedRooms = boundedRooms;
                            }

                            foreach (var rm in pickedRooms)
                            {
                                allRooms.Add(rm);
                                DrawSingleRoomOnCad(doc, rm);
                                ed.WriteMessage(string.Format("\n✅ [Phòng {0}]: {1} | S = {2:F2} m² | P = {3:F2} m (Khóa chặt trong Khung Bao)",
                                    roomIndex, rm.RoomName, rm.AreaM2, rm.PerimeterM));
                            }
                            if (pickedRooms.Count > 0)
                            {
                                roomIndex++;
                            }
                        }
                        else
                        {
                            ed.WriteMessage("\n⚠️ Không tìm thấy đường bao khép kín quanh vị trí click này (vui lòng kiểm tra nét tường/cửa).");
                        }
                    }
                }
                finally
                {
                    // Dọn sạch các đoạn thẳng tạm bắn qua cửa, tắt AI_XD_DIEM_NOI & phục hồi hiển thị các layer khác
                    CleanupTemporaryStraightDoorBridges(doc, tempDoorBridges);
                    UnhideAllLayers(db);
                    SetDiemNoiLayerVisibility(db, false);
                    ed.UpdateScreen();
                }

                // Dọn tệp DXF tạm
                if (File.Exists(dxfPath))
                {
                    try { File.Delete(dxfPath); } catch { }
                }

                result.Rooms = allRooms;
                result.RoomCount = allRooms.Count;
                result.TotalAreaM2 = allRooms.Sum(r => r.AreaM2);
                result.TotalPerimeterM = allRooms.Sum(r => r.PerimeterM);
                result.WcCount = allRooms.Count(r => r.IsWc);
                result.Success = allRooms.Count > 0;
                result.Message = string.Format("Đã quét thành công {0} phòng trong phạm vi Khung Bao.", allRooms.Count);
                ed.WriteMessage(string.Format("\n🎉 [HOÀN TẤT] Tổng cộng đã quét: {0} phòng | Tổng S = {1:F2} m² (Toàn bộ nằm trọn vẹn trong Khung Bao).\n", allRooms.Count, result.TotalAreaM2));
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\n[Lỗi Quét Nhiều Phòng] " + ex.Message);
                result.Message = ex.Message;
            }

            return result;
        }

        public static CoPilotResult ExecutePickRoom(Document doc)



        {



            CoPilotResult result = new CoPilotResult();



            if (doc == null) return result;



            Editor ed = doc.Editor;



            PromptPointOptions ppo = new PromptPointOptions("\n[Co-Pilot] Click 1 điểm bất kỳ bên trong phòng cần bao: ");



            ppo.AllowNone = false;



            PromptPointResult ppr = ed.GetPoint(ppo);



            if (ppr.Status != PromptStatus.OK) return result;



            Point3d pt = ppr.Value;



            Database db = doc.Database;



            Stopwatch sw = Stopwatch.StartNew();



            try



            {



                string pyExe = FindPythonExecutable();



                string pyScript = FindBoundaryEngineScript();



                if (string.IsNullOrEmpty(pyExe) || !File.Exists(pyExe) ||



                    string.IsNullOrEmpty(pyScript) || !File.Exists(pyScript))



                {



                    ed.WriteMessage("\n[Co-Pilot] Lỗi: Không tìm thấy Python hoặc script.");



                    return result;



                }



                string tempDir = Path.GetTempPath();



                string stamp = DateTime.Now.Ticks.ToString();



                string dxfPath = Path.Combine(tempDir, "copilot_pick_" + stamp + ".dxf");



                string outLsp = Path.Combine(tempDir, "copilot_pick_res_" + stamp + ".lsp");



                SetDiemNoiLayerVisibility(db, true);
                List<ObjectId> tempPickBridges = null;
                try
                {
                    tempPickBridges = CreateTemporaryStraightDoorBridges(doc, null);
                    using (doc.LockDocument())
                    {
                        db.DxfOut(dxfPath, 16, DwgVersion.Current);
                    }
                }
                finally
                {
                    CleanupTemporaryStraightDoorBridges(doc, tempPickBridges);
                }



                ed.WriteMessage(string.Format("\n⚡ [Co-Pilot] Đang phân tích ranh giới tại tọa độ ({0:F2}, {1:F2})...", pt.X, pt.Y));



                ProcessStartInfo psi = new ProcessStartInfo();



                psi.FileName = pyExe;



                psi.Arguments = string.Format(CultureInfo.InvariantCulture,



                    "\"{0}\" --dxf \"{1}\" --point {2:F2} {3:F2} --out \"{4}\"",



                    pyScript, dxfPath, pt.X, pt.Y, outLsp);



                psi.CreateNoWindow = true;



                psi.UseShellExecute = false;



                using (Process p = Process.Start(psi))



                {



                    p.WaitForExit(45000);



                }



                if (!File.Exists(outLsp))



                {



                    ed.WriteMessage("\n[Co-Pilot] Không xác định được ranh giới quanh điểm click.");



                    return result;



                }



                List<CoPilotRoomItem> rooms = ParseRoomsFromLsp(outLsp);



                double totalArea = 0;



                double totalPerimeter = 0;



                int wcCount = 0;



                int curvedCount = 0;



                foreach (CoPilotRoomItem rm in rooms)



                {



                    totalArea += rm.AreaM2;



                    totalPerimeter += rm.PerimeterM;



                    if (rm.IsWc) wcCount++;



                    if (rm.HasCurvedWall) curvedCount++;



                }



                string lspCmd = string.Format("(load \"{0}\")\n", outLsp.Replace("\\", "/"));



                doc.SendStringToExecute(lspCmd, true, false, false);



                sw.Stop();



                result.Success = true;



                result.RoomCount = rooms.Count;



                result.TotalAreaM2 = totalArea;



                result.TotalPerimeterM = totalPerimeter;



                result.WcCount = wcCount;



                result.CurvedCount = curvedCount;



                result.ElapsedMs = sw.ElapsedMilliseconds;



                result.Rooms = rooms;



                result.Message = string.Format("Đã bao thành công phòng ({0:F2} m²)!", totalArea);



                ed.WriteMessage(string.Format("\n⚡ [Co-Pilot] Đã phân tích xong ({0:F2} m²). Đang vẽ lên bản vẽ...", totalArea));



                ed.WriteMessage("\nĐã vẽ xong mời bạn Check.\n");



            }



            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                ed.WriteMessage("\n[Co-Pilot Lỗi] " + ex.ToString());
            }
            finally
            {
                SetDiemNoiLayerVisibility(db, false);
            }

            return result;



        }



        /// <summary>



        /// Xóa sạch tất cả đường bao AI và text nhãn cũ trên bản vẽ bằng Transaction đồng bộ (STA Safe)



        /// </summary>



        public static void ClearAiBoundaries(Document doc)



        {



            if (doc == null) return;



            Database db = doc.Database;



            try



            {



                using (DocumentLock docLock = doc.LockDocument())



                using (Transaction tr = db.TransactionManager.StartTransaction())



                {



                    BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;



                    if (btr != null)



                    {



                        List<ObjectId> toDelete = new List<ObjectId>();



                        foreach (ObjectId id in btr)



                        {



                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;



                            if (ent == null) continue;



                            if (string.Equals(ent.Layer, LAYER_BOUNDARY, StringComparison.OrdinalIgnoreCase) ||



                                string.Equals(ent.Layer, LAYER_BOUNDARY_TEXT, StringComparison.OrdinalIgnoreCase) ||



                                string.Equals(ent.Layer, "AI_DUONG_BAO_PHONG", StringComparison.OrdinalIgnoreCase))



                            {



                                toDelete.Add(id);



                            }



                            else if (ent.XData != null)



                            {



                                ResultBuffer rb = ent.GetXDataForApplication(XDATA_APP_NAME);



                                if (rb != null)



                                {



                                    toDelete.Add(id);



                                    rb.Dispose();



                                }



                            }



                        }



                        for (int i = 0; i < toDelete.Count; i++)



                        {



                            DBObject obj = tr.GetObject(toDelete[i], OpenMode.ForWrite);



                            obj.Erase(true);



                        }



                    }



                    tr.Commit();



                }



            }



            catch { }



        }



        /// <summary>



        /// Làm mới toàn bộ dữ liệu mặt bằng: Xóa sạch 100% file cache (.pkl, .jsonl, .json, .lsp, .dxf tạm),



        /// gỡ bỏ mọi nhánh khóa cứng dữ liệu cũ (Zero-Hardcode Guard) và xóa đường bao AI cũ trên CAD.



        /// </summary>



        public static string RefreshDataAndPurgeHardcode(Document doc)



        {



            int deletedFiles = 0;



            int sanitizedScripts = 0;



            try



            {



                // 1. Xóa đường bao AI cũ trên bản vẽ hiện hành



                if (doc != null)



                {



                    ClearAiBoundaries(doc);



                }



                // 2. Xóa toàn bộ file tạm và cache trong %TEMP%



                string tempDir = Path.GetTempPath();



                string[] tempPatterns = new string[]



                {



                    "wcs_cache_*.pkl",



                    "true_wcs_cache*.pkl",



                    "copilot_plan_*.dxf",



                    "copilot_res_*.lsp",



                    "copilot_pick_*.dxf",



                    "copilot_pick_res_*.lsp",



                    "plan_*.dxf",



                    "res_*.lsp",



                    "ve_54_duong_bao_chuan.lsp",



                    "ve_bao_mb01.lsp"



                };



                foreach (string pat in tempPatterns)



                {



                    try



                    {



                        string[] files = Directory.GetFiles(tempDir, pat);



                        foreach (string f in files)



                        {



                            try { File.Delete(f); deletedFiles++; } catch { }



                        }



                    }



                    catch { }



                }



                // 3. Xóa các file dữ liệu mặt bằng cũ lưu cứng tại các thư mục Workspace



                string[] workspaceDirs = new string[]



                {



                    @"D:\01. NGUYEN TAN TAI\MULTI AGENT",



                    @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT",



                    @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS"



                };



                string[] staleDataNames = new string[]



                {



                    "mb01_spatial.jsonl",



                    "mb01_rooms.json",



                    "ve_bao_mb01.lsp",



                    "ve_54_duong_bao_chuan.lsp",



                    "true_room_polygons.json",

                    "multi_agent_trigger.json",



                    "drawing_spatial.jsonl",



                    "nha_dan_t2.dxf",



                    "test_nha_dan.lsp",



                    "out_nha_dan_v2.lsp"



                };



                foreach (string wdir in workspaceDirs)



                {



                    if (!Directory.Exists(wdir)) continue;



                    foreach (string sname in staleDataNames)



                    {



                        string fp = Path.Combine(wdir, sname);



                        if (File.Exists(fp))



                        {



                            try { File.Delete(fp); deletedFiles++; } catch { }



                        }



                    }



                    // Dọn các file .pkl cache trong workspace



                    try



                    {



                        foreach (string pkl in Directory.GetFiles(wdir, "*.pkl", SearchOption.AllDirectories))



                        {



                            try { File.Delete(pkl); deletedFiles++; } catch { }



                        }



                    }



                    catch { }



                }



                // 4. Dự phòng kiểm tra & khóa chặn mọi nhánh hardcode trong các script boundary_engine.py / smart_boundary_compiler.py



                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);



                string asmLoc = "";



                try { asmLoc = Assembly.GetExecutingAssembly().Location; } catch { }



                string asmDir = !string.IsNullOrEmpty(asmLoc) ? (Path.GetDirectoryName(asmLoc) ?? "") : "";



                string[] engineScripts = new string[]



                {



                    !string.IsNullOrEmpty(asmDir) ? Path.Combine(asmDir, "Scripts", "boundary_engine.py") : "",



                    Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Scripts\boundary_engine.py"),



                    @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Scripts\boundary_engine.py",



                    @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Scripts\boundary_engine.py",



                    @"D:\01. NGUYEN TAN TAI\MULTI AGENT\smart_boundary_compiler.py",



                    @"D:\01. NGUYEN TAN TAI\MULTI AGENT\.agents\skills\cad-spatial-boundary\scripts\smart_boundary_compiler.py",



                    @"D:\01. NGUYEN TAN TAI\MULTI AGENT\.agents\skills\cad-spatial-boundary\scripts\boundary_engine.py",



                    @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT\smart_boundary_compiler.py",



                    @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT\.agents\skills\cad-spatial-boundary\scripts\smart_boundary_compiler.py",



                    @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT\.agents\skills\cad-spatial-boundary\scripts\boundary_engine.py"



                };



                foreach (string sp in engineScripts)



                {



                    if (!string.IsNullOrEmpty(sp) && File.Exists(sp))



                    {



                        try



                        {



                            string content = File.ReadAllText(sp, System.Text.Encoding.UTF8);



                            bool modified = false;



                            // Vô hiệu hóa fallback về MB01_converted.dxf nếu còn sót



                            if (content.Contains("mb01_fallback ="))



                            {



                                content = Regex.Replace(content, @"if\s+not\s+os\.path\.exists\(dxf_target\):[\s\S]*?dxf_target\s*=\s*mb01_fallback", "if not os.path.exists(dxf_target):\n        return");



                                modified = true;



                            }



                            // Vô hiệu hóa cache .pkl cố định của phiên cũ nếu còn sót



                            if (content.Contains("4837a2f6-d2a9-4cc0-9dd4-46577bbdf773"))



                            {



                                content = Regex.Replace(content, @"default_cache\s*=\s*r""[^""]*4837a2f6[^""]*""", "default_cache = \"\"");



                                modified = true;



                            }



                            if (modified)



                            {



                                File.WriteAllText(sp, content, System.Text.Encoding.UTF8);



                                sanitizedScripts++;



                            }



                        }



                        catch { }



                    }



                }



                string msg = string.Format("✓ Đã làm mới dữ liệu & xóa sạch Hardcode/Cache cũ ({0} tệp cache đã dọn, {1} script kiểm tra Zero-Hardcode). Sẵn sàng quét mặt bằng mới!", deletedFiles, sanitizedScripts);



                if (doc != null && doc.Editor != null)



                {



                    doc.Editor.WriteMessage("\n⚡ [XLMB - LÀM MỚI DỮ LIỆU] " + msg + "\n");



                }



                return msg;



            }



            catch (Exception ex)



            {



                return "Lỗi khi làm mới dữ liệu: " + ex.Message;



            }



        }



        /// <summary>



        /// Phóng to màn hình AutoCAD đến đúng vị trí của phòng đang chọn



        /// </summary>



        public static void ZoomToRoom(Document doc, CoPilotRoomItem room)
        {
            if (doc == null || room == null) return;

            Point2d pickOrigin = Point2d.Origin;
            bool hasPickOrigin = false;

            // 1. LẤY GỐC TỌA ĐỘ PICK POINT CỦA TÊN PHÒNG
            // Ưu tiên LabelX, LabelY (tọa độ chèn chính xác của Text tên phòng từ Multi-Agent/DWG)
            if (Math.Abs(room.LabelX) > 0.001 || Math.Abs(room.LabelY) > 0.001)
            {
                pickOrigin = new Point2d(room.LabelX, room.LabelY);
                hasPickOrigin = true;
            }
            else if (Math.Abs(room.CenterX) > 0.001 || Math.Abs(room.CenterY) > 0.001)
            {
                pickOrigin = new Point2d(room.CenterX, room.CenterY);
                hasPickOrigin = true;
            }

            // Quét bổ sung Text Entity thực tế trên bản vẽ nếu chưa có pickOrigin
            if (!hasPickOrigin)
            {
                try
                {
                    Database db = doc.Database;
                    if (db != null)
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                            if (btr != null)
                            {
                                string targetName = (room.RoomName ?? room.Name ?? "").Trim();
                                string targetCode = (room.Code ?? "").Trim();

                                foreach (ObjectId id in btr)
                                {
                                    if (id.ObjectClass.DxfName.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
                                        id.ObjectClass.DxfName.Equals("MTEXT", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                        if (ent == null) continue;

                                        string textVal = "";
                                        Point3d textPos = Point3d.Origin;
                                        DBText dbText = ent as DBText;
                                        if (dbText != null)
                                        {
                                            textVal = dbText.TextString;
                                            textPos = dbText.Position;
                                        }
                                        else
                                        {
                                            MText mText = ent as MText;
                                            if (mText != null)
                                            {
                                                textVal = mText.Text;
                                                textPos = mText.Location;
                                            }
                                        }

                                        if (string.IsNullOrEmpty(textVal)) continue;

                                        bool match = false;
                                        if (!string.IsNullOrEmpty(targetCode) && textVal.IndexOf(targetCode, StringComparison.OrdinalIgnoreCase) >= 0)
                                            match = true;
                                        else if (!string.IsNullOrEmpty(targetName) && textVal.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                                            match = true;

                                        if (match)
                                        {
                                            pickOrigin = new Point2d(textPos.X, textPos.Y);
                                            hasPickOrigin = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            tr.Commit();
                        }
                    }
                }
                catch { }
            }

            // 2. TÍNH TOÁN BOUNDING BOX PHÒNG (TỪ VERTICES)
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            bool hasBounds = false;

            if (room.Vertices != null && room.Vertices.Count >= 2)
            {
                foreach (var v in room.Vertices)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                }
                if (maxX > minX && maxY > minY)
                {
                    hasBounds = true;
                }
            }

            if (!hasPickOrigin)
            {
                if (hasBounds)
                {
                    pickOrigin = new Point2d((minX + maxX) / 2.0, (minY + maxY) / 2.0);
                    hasPickOrigin = true;
                }
                else
                {
                    return;
                }
            }

            // 3. TÍNH BÁN KÍNH PHỦ TỪ GỐC PICK POINT ĐỂ MẶT BẰNG PHÒNG NẰM GỌN TRONG MÀN HÌNH
            double halfW, halfH;
            if (hasBounds)
            {
                double leftDelta = Math.Abs(pickOrigin.X - minX);
                double rightDelta = Math.Abs(maxX - pickOrigin.X);
                double bottomDelta = Math.Abs(pickOrigin.Y - minY);
                double topDelta = Math.Abs(maxY - pickOrigin.Y);

                halfW = Math.Max(leftDelta, rightDelta);
                halfH = Math.Max(bottomDelta, topDelta);

                if (halfW < 100.0) halfW = (maxX - minX) / 2.0;
                if (halfH < 100.0) halfH = (maxY - minY) / 2.0;
            }
            else
            {
                double estSpan = Math.Sqrt(Math.Max(4.0, room.AreaM2)) * 1000.0;
                halfW = estSpan * 0.7;
                halfH = estSpan * 0.7;
            }

            // Margin an toàn 35% (tối thiểu 1500mm) để bao trọn trục, dim và lọt thỏm trong vùng hiển thị
            double padX = Math.Max(1500.0, halfW * 0.35);
            double padY = Math.Max(1500.0, halfH * 0.35);
            double targetSpanW = (halfW + padX) * 2.0;
            double targetSpanH = (halfH + padY) * 2.0;

            // 4. ÁP DỤNG VIEWPORT: ĐẶT TARGET VỀ WCS PICK POINT VÀ DCS CENTER VỀ GỐC (0,0)
            bool directSuccess = false;
            try
            {
                using (doc.LockDocument())
                {
                    Editor ed = doc.Editor;
                    using (ViewTableRecord view = ed.GetCurrentView())
                    {
                        double screenAspect = view.Width / Math.Max(1.0, view.Height);
                        double finalHeight = targetSpanH;
                        if (targetSpanW / screenAspect > finalHeight)
                        {
                            finalHeight = targetSpanW / screenAspect;
                        }
                        double finalWidth = finalHeight * screenAspect;

                        // Chuẩn Autodesk ObjectARX: Đặt Target tại tọa độ WCS của Pick Point,
                        // và đặt CenterPoint trong DCS về (0,0) để triệt tiêu lệch tọa độ DCS.
                        view.Target = new Point3d(pickOrigin.X, pickOrigin.Y, 0.0);
                        view.CenterPoint = Point2d.Origin;
                        view.Height = finalHeight;
                        view.Width = finalWidth;

                        ed.SetCurrentView(view);
                        ed.UpdateScreen();
                        directSuccess = true;
                    }
                }
            }
            catch
            {
                directSuccess = false;
            }

            if (!directSuccess)
            {
                double viewSize = Math.Max(targetSpanW, targetSpanH);
                string zoomCmd = string.Format(CultureInfo.InvariantCulture,
                    "\x1B\x1B_.ZOOM _C *{0:F2},*{1:F2},0 {2:F2}\n", pickOrigin.X, pickOrigin.Y, viewSize);
                doc.SendStringToExecute(zoomCmd, true, false, false);
            }
        }



        #region HELPER TRÍCH XUẤT KẾT QUẢ & ĐƯỜNG DẪN



        private static List<CoPilotRoomItem> ParseRoomsFromLsp(string lspPath)

        {

            List<CoPilotRoomItem> list = new List<CoPilotRoomItem>();

            if (!File.Exists(lspPath)) return list;



            try

            {

                string[] lines = File.ReadAllLines(lspPath, System.Text.Encoding.UTF8);

                CoPilotRoomItem currentItem = null;



                Regex regHeader = new Regex(@";;\s*Ph[oòóõọôồốỗộơờớỡợ]ng:\s*(?<name>[^\(\)]+?)\s*\((?<code>[A-Za-z0-9\-_]+)\)\s*-\s*Di[eệ][nệ]n\s*t[ií]ch:\s*(?<area>[\d\.]+)\s*m2", RegexOptions.IgnoreCase);

                Regex regPt = new Regex(@"\(cons\s+10\s+'\(\s*(?<x>[-?\d\.]+)\s+(?<y>[-?\d\.]+)\s*\)\)", RegexOptions.IgnoreCase);

                Regex regBulge = new Regex(@"\(cons\s+42\s+(?<bVal>[-?\d\.]+)\)", RegexOptions.IgnoreCase);

                Regex regMTextPt = new Regex(@"AcDbMText""\)\s*\(cons\s+10\s+'\(\s*(?<lx>[-?\d\.]+)\s+(?<ly>[-?\d\.]+)", RegexOptions.IgnoreCase);



                for (int i = 0; i < lines.Length; i++)

                {

                    string ln = lines[i].Trim();



                    Match mh = regHeader.Match(ln);

                    if (mh.Success)

                    {

                        if (currentItem != null && currentItem.Vertices != null && currentItem.Vertices.Count >= 3)

                        {

                            list.Add(currentItem);

                        }



                        currentItem = new CoPilotRoomItem();

                        currentItem.RoomName = mh.Groups["name"].Value.Trim();

                        currentItem.Code = mh.Groups["code"].Value.Trim();

                        double a = 0;

                        double.TryParse(mh.Groups["area"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out a);

                        currentItem.AreaM2 = a;

                        currentItem.Floor = "Mặt bằng hiện hành";



                        string lowerName = currentItem.RoomName.ToLowerInvariant();

                        if (lowerName.Contains("wc") || lowerName.Contains("vệ sinh") || lowerName.Contains("toilet") || lowerName.Contains("tắm"))

                        {

                            currentItem.IsWc = true;

                        }

                        continue;

                    }



                    if (currentItem != null)

                    {

                        Match mp = regPt.Match(ln);

                        if (mp.Success)

                        {

                            double vx = 0, vy = 0;

                            double.TryParse(mp.Groups["x"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out vx);

                            double.TryParse(mp.Groups["y"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out vy);

                            currentItem.Vertices.Add(new CoPilotPoint2D(vx, vy));

                            continue;

                        }



                        Match mb = regBulge.Match(ln);

                        if (mb.Success)

                        {

                            double bv = 0;

                            double.TryParse(mb.Groups["bVal"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out bv);

                            currentItem.Bulges.Add(bv);

                            if (Math.Abs(bv) > 1e-4) currentItem.HasCurvedWall = true;

                            continue;

                        }



                        Match mtx = regMTextPt.Match(ln);

                        if (mtx.Success)

                        {

                            double lx = 0, ly = 0;

                            double.TryParse(mtx.Groups["lx"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out lx);

                            double.TryParse(mtx.Groups["ly"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out ly);

                            currentItem.LabelX = lx;

                            currentItem.LabelY = ly;

                            continue;

                        }

                    }

                }



                if (currentItem != null && currentItem.Vertices != null && currentItem.Vertices.Count >= 3)

                {

                    list.Add(currentItem);

                }

            }

            catch { }



            return list;

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



            string asmLoc = "";



            try { asmLoc = Assembly.GetExecutingAssembly().Location; } catch { }



            string asmDir = !string.IsNullOrEmpty(asmLoc) ? (Path.GetDirectoryName(asmLoc) ?? "") : "";



            string[] candidates = new string[]



            {



                !string.IsNullOrEmpty(asmDir) ? Path.Combine(asmDir, "Scripts", "boundary_engine.py") : "",



                !string.IsNullOrEmpty(asmDir) ? Path.Combine(asmDir, "boundary_engine.py") : "",



                Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Scripts\boundary_engine.py"),



                @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Scripts\boundary_engine.py",



                @"D:\01. NGUYEN TAN TAI\MULTI AGENT\.agents\skills\cad-spatial-boundary\scripts\boundary_engine.py",



                @"D:\01. NGUYEN TAN TAI\CODING AI\SKILL XU LY MAT BANG\skills\smart-room-boundary\scripts\boundary_engine.py"



            };



            for (int i = 0; i < candidates.Length; i++)



            {



                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i])) return candidates[i];



            }



            return "";



        }



        #endregion



        #region Multi-Agent Integration & Real-Time Synchronization (V2.6 Architecture)



        /// <summary>



        /// KÍCH HOẠT QUY TRÌNH MULTI-AGENT VỀ ANTIGRAVITY QUA MCP (TRIGGER ONLY - KHÔNG TỰ VẼ BỪA)



        /// </summary>



        public static CoPilotResult TriggerMultiAgentWorkflow(Document doc, bool includeMain = true, bool includeCorridor = true, bool includeStair = true, bool includeWc = true)



        {



            CoPilotResult result = new CoPilotResult();



            if (doc == null)



            {



                result.Message = "Tài liệu CAD hiện hành không hợp lệ.";



                return result;



            }



            Editor ed = doc.Editor;



            List<string> parts = new List<string>();



            if (includeMain) parts.Add("Phòng chức năng");



            if (includeCorridor) parts.Add("Hành lang & Sảnh");



            if (includeStair) parts.Add("Hố thang & Thang bộ");



            if (includeWc) parts.Add("Nhà WC (-50mm)");



            string scopeDesc = parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "Tất cả";



            string dwgPath = doc.Name;



            ed.WriteMessage(string.Format("\n⚡ [CHỈ HUY TRƯỞNG AI] Đã kích hoạt quy trình Multi-Agent về Antigravity qua MCP!"));



            ed.WriteMessage(string.Format("\n⚡ [CHỈ HUY TRƯỞNG AI] Bản vẽ hiện hành: [{0}]", dwgPath));



            ed.WriteMessage(string.Format("\n⚡ [CHỈ HUY TRƯỞNG AI] Phạm vi yêu cầu: [{0}]", scopeDesc));



            ed.WriteMessage("\n⚡ [CHỈ HUY TRƯỞNG AI] Đang gửi yêu cầu về Antigravity (Chỉ Huy Trưởng Agon + 3 Flash Agents) để bóc tách hình học không gian...");



            // Ghi file tín hiệu Trigger gửi về Antigravity



            try



            {



                Dictionary<string, object> triggerData = new Dictionary<string, object>();



                triggerData["event"] = "TRIGGER_MULTI_AGENT_WORKFLOW";



                triggerData["timestamp"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");



                triggerData["drawing_path"] = dwgPath;



                triggerData["scope_desc"] = scopeDesc;



                triggerData["include_main"] = includeMain;



                triggerData["include_corridor"] = includeCorridor;



                triggerData["include_stair"] = includeStair;



                triggerData["include_wc"] = includeWc;



                triggerData["status"] = "REQUESTED_TO_ANTIGRAVITY";



                JavaScriptSerializer ser = new JavaScriptSerializer();



                string jsonContent = ser.Serialize(triggerData);



                string[] targetDirs = new string[]



                {



                    @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT",



                    @"D:\01. NGUYEN TAN TAI\MULTI AGENT"



                };



                foreach (string d in targetDirs)



                {



                    if (Directory.Exists(d))



                    {



                        File.WriteAllText(Path.Combine(d, "multi_agent_trigger.json"), jsonContent, System.Text.Encoding.UTF8);



                    }



                }



            }



            catch { }



            result.Success = true;



            result.Message = string.Format("Đã gửi tín hiệu kích hoạt Multi-Agent về Antigravity cho bản vẽ [{0}] lúc {1:HH:mm:ss}!", Path.GetFileName(dwgPath), DateTime.Now);



            return result;



        }



        public static CoPilotResult ExecuteMultiAgentWorkflow(Document doc, bool includeMain = true, bool includeCorridor = true, bool includeStair = true, bool includeWc = true)



        {



            return TriggerMultiAgentWorkflow(doc, includeMain, includeCorridor, includeStair, includeWc);



        }



        public static CoPilotResult ExecuteMultiAgentWorkflow(Document doc, string scope)



        {



            scope = string.IsNullOrEmpty(scope) ? "ALL" : scope.ToUpperInvariant();



            if (scope == "WC") return TriggerMultiAgentWorkflow(doc, false, false, false, true);



            if (scope == "STAIR") return TriggerMultiAgentWorkflow(doc, false, false, true, false);



            if (scope == "CORRIDOR") return TriggerMultiAgentWorkflow(doc, false, true, false, false);



            return TriggerMultiAgentWorkflow(doc, true, true, true, true);



        }



        /// <summary>



        /// VẼ ĐƯỜNG BAO LÊN CAD DỰA TRÊN DỮ LIỆU ĐÃ ĐƯỢC MULTI-AGENT TÍNH TOÁN CHUẨN XÁC



        /// </summary>



        /// <summary>
        /// VẼ TỨC THÌ 1 PHÒNG LÊN AUTOCAD (ĐƯỜNG BAO LWPOLYLINE ĐỎ + NHÃN MTEXT TÂM PHÒNG)
        /// Phục vụ tính năng: Pick tới đâu hiện đường bao tới đó
        /// </summary>
        /// <summary>
        /// BẬT SÁNG TOÀN BỘ 100% CÁC LAYER TRÊN BẢN VẼ (KHÔI PHỤC HIỂN THỊ ĐẦY ĐỦ)
        /// Mở khóa (IsLocked = false) và bật sáng (IsOff = false) cho tất cả layer.
        /// </summary>
        public static void SetDiemNoiLayerVisibility(Database db, bool isVisible)
        {
            if (db == null) return;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForWrite) as LayerTable;
                    if (!lt.Has(LAYER_DIEM_NOI))
                    {
                        LayerTableRecord newLtr = new LayerTableRecord();
                        newLtr.Name = LAYER_DIEM_NOI;
                        newLtr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6); // Magenta
                        newLtr.LineWeight = LineWeight.LineWeight030;
                        newLtr.IsOff = !isVisible;
                        lt.Add(newLtr);
                        tr.AddNewlyCreatedDBObject(newLtr, true);
                    }
                    else
                    {
                        ObjectId lid = lt[LAYER_DIEM_NOI];
                        LayerTableRecord ltr = tr.GetObject(lid, OpenMode.ForWrite) as LayerTableRecord;
                        if (ltr != null)
                        {
                            if (!isVisible && db.Clayer == lid && lt.Has("0"))
                            {
                                try { db.Clayer = lt["0"]; } catch { }
                            }
                            ltr.IsOff = !isVisible;
                            ltr.IsLocked = false;
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        public static int UnhideAllLayers(Database db)
        {
            if (db == null) return 0;
            int count = 0;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    foreach (ObjectId ltrId in lt)
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(ltrId, OpenMode.ForWrite);
                        // Layer AI_XD_DIEM_NOI chỉ bật khi đang làm Bao Phòng, luôn tắt khi bình thường
                        if (string.Equals(ltr.Name, LAYER_DIEM_NOI, StringComparison.OrdinalIgnoreCase))
                        {
                            ltr.IsOff = true;
                            continue;
                        }
                        if (ltr.IsOff || ltr.IsLocked)
                        {
                            ltr.IsOff = false;
                            ltr.IsLocked = false;
                            count++;
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }
            return count;
        }

        /// <summary>
        /// CÔ LẬP LAYER: CHỈ GIỮ LẠI TƯỜNG, CỘT, CỬA, VÁCH KÍNH & KHUNG BAO (CORE SPEC)
        /// Ẩn 100% tất cả các layer còn lại (kể cả AI_XD_NOI_THAT, AI_XD_DIM_TEXT, layer 0, rác, dim, nội thất).
        /// </summary>
        public static Dictionary<ObjectId, bool> IsolateStructuralLayersForBoundary(Database db, Transaction tr)
        {
            Dictionary<ObjectId, bool> previousStates = new Dictionary<ObjectId, bool>();
            if (db == null || tr == null) return previousStates;

            EnsureLayersAndAppId(db, tr);
            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (lt.Has("AI_XD_TUONG"))
            {
                try { db.Clayer = lt["AI_XD_TUONG"]; } catch { }
            }
            else if (lt.Has(LAYER_BOUNDARY))
            {
                try { db.Clayer = lt[LAYER_BOUNDARY]; } catch { }
            }

            foreach (ObjectId ltrId in lt)
            {
                LayerTableRecord ltr = tr.GetObject(ltrId, OpenMode.ForWrite) as LayerTableRecord;
                previousStates[ltrId] = ltr.IsOff;

                string name = (ltr.Name ?? "").ToUpperInvariant();

                bool isStrictStructural = false;
                if (name == "AI_XD_TUONG" || name == "AI_XD_COT" || name == "AI_XD_CUA" || 
                    name == "AI_XD_VACH_KINH" || name == "AI_XD_KHUNG_BAO" || name == "AI_XD_DIEM_NOI" ||
                    name == "DUONG_BAO" || name == "DUONG_BAO_TEXT" || name.StartsWith("DUONG_BAO") || name.StartsWith("HTS-PHONG"))
                {
                    isStrictStructural = true;
                }
                else
                {
                    bool isIgnored = name.Contains("NOI_THAT") || name.Contains("FURNITURE") || name.Contains("INTERIOR") ||
                                     name.Contains("DIM") || name.Contains("TEXT") || name.Contains("GRID") ||
                                     name.Contains("RAC") || name.Contains("RÁC") || name.Contains("HATCH") ||
                                     name == "0" || name == "DEFPOINTS";

                    if (!isIgnored)
                    {
                        bool isWall = name.Contains("TUONG") || name.Contains("TƯỜNG") || name.Contains("WALL") || name.Contains("VACH") || name.Contains("VÁCH");
                        bool isCol = name.Contains("COT") || name.Contains("CỘT") || name.Contains("COL") || name.Contains("PILLAR");
                        bool isDoor = name.Contains("CUA") || name.Contains("CỬA") || name.Contains("DOOR") || name.Contains("KINH") || name.Contains("KÍNH");
                        if (isWall || isCol || isDoor)
                        {
                            isStrictStructural = true;
                        }
                    }
                }

                if (isStrictStructural)
                {
                    ltr.IsOff = false; // CHỈ BẬT DUY NHẤT TƯỜNG, CỘT, CỬA, VÁCH KÍNH, KHUNG BAO
                }
                else
                {
                    ltr.IsOff = true;  // ẨN 100% TẤT CẢ LAYER KHÁC (NỘI THẤT, DIM, TEXT, 0, RÁC...)
                }
            }
            return previousStates;
        }

        public static void RestoreLayerStates(Database db, Transaction tr, Dictionary<ObjectId, bool> previousStates)
        {
            if (db == null || tr == null || previousStates == null) return;
            foreach (var kvp in previousStates)
            {
                try
                {
                    LayerTableRecord ltr = tr.GetObject(kvp.Key, OpenMode.ForWrite) as LayerTableRecord;
                    if (ltr != null)
                    {
                        ltr.IsOff = kvp.Value;
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// CẮT GỌT ĐA GIÁC PHÒNG THEO PHẠM VI KHUNG BAO (SUTHERLAND-HODGMAN CLIPPING)
        /// Đảm bảo 100% đỉnh và cạnh phòng nằm trọn vẹn bên trong Khung Bao, tuyệt đối không tràn viền.
        /// </summary>
        public static CoPilotRoomItem ClipRoomToBoundary(CoPilotRoomItem rm, Extents3d bounds)
        {
            if (rm == null || rm.Vertices == null || rm.Vertices.Count < 3) return null;

            double minX = bounds.MinPoint.X;
            double maxX = bounds.MaxPoint.X;
            double minY = bounds.MinPoint.Y;
            double maxY = bounds.MaxPoint.Y;

            bool allInside = true;
            for (int i = 0; i < rm.Vertices.Count; i++)
            {
                double vx = rm.Vertices[i].X;
                double vy = rm.Vertices[i].Y;
                if (vx < minX - 1.0 || vx > maxX + 1.0 || vy < minY - 1.0 || vy > maxY + 1.0)
                {
                    allInside = false;
                    break;
                }
            }
            if (allInside) return rm;

            List<Point2d> poly = new List<Point2d>();
            for (int i = 0; i < rm.Vertices.Count; i++)
            {
                poly.Add(new Point2d(rm.Vertices[i].X, rm.Vertices[i].Y));
            }

            // 1. Cắt mép Trái: X >= minX
            poly = ClipEdge(poly, 0, minX);
            if (poly.Count < 3) return null;

            // 2. Cắt mép Phải: X <= maxX
            poly = ClipEdge(poly, 1, maxX);
            if (poly.Count < 3) return null;

            // 3. Cắt mép Dưới: Y >= minY
            poly = ClipEdge(poly, 2, minY);
            if (poly.Count < 3) return null;

            // 4. Cắt mép Trên: Y <= maxY
            poly = ClipEdge(poly, 3, maxY);
            if (poly.Count < 3) return null;

            CoPilotRoomItem clipped = new CoPilotRoomItem();
            clipped.RoomName = rm.RoomName;
            clipped.Name = rm.Name;
            clipped.Code = rm.Code;
            clipped.Floor = rm.Floor;
            clipped.ElevationOffsetMm = rm.ElevationOffsetMm;
            clipped.HasCurvedWall = rm.HasCurvedWall;
            clipped.Bulge = rm.Bulge;
            clipped.IsWc = rm.IsWc;
            clipped.Vertices = new List<CoPilotPoint2D>();
            clipped.Bulges = new List<double>();

            double area = 0.0;
            double perim = 0.0;
            double cx = 0.0, cy = 0.0;

            for (int i = 0; i < poly.Count; i++)
            {
                Point2d cur = poly[i];
                Point2d next = poly[(i + 1) % poly.Count];
                clipped.Vertices.Add(new CoPilotPoint2D(cur.X, cur.Y));
                clipped.Bulges.Add(0.0);

                area += (cur.X * next.Y - next.X * cur.Y);
                perim += cur.GetDistanceTo(next);
                cx += cur.X;
                cy += cur.Y;
            }

            clipped.AreaM2 = Math.Abs(area) * 0.5 / 1000000.0;
            clipped.PerimeterM = perim / 1000.0;
            clipped.CenterX = cx / poly.Count;
            clipped.CenterY = cy / poly.Count;
            clipped.LabelX = clipped.CenterX;
            clipped.LabelY = clipped.CenterY;

            return (clipped.AreaM2 >= 0.3) ? clipped : null;
        }

        private static List<Point2d> ClipEdge(List<Point2d> inPoly, int edgeType, double edgeVal)
        {
            List<Point2d> outPoly = new List<Point2d>();
            if (inPoly == null || inPoly.Count == 0) return outPoly;

            Point2d prev = inPoly[inPoly.Count - 1];
            bool prevIn = IsInsideEdge(prev, edgeType, edgeVal);

            for (int i = 0; i < inPoly.Count; i++)
            {
                Point2d cur = inPoly[i];
                bool curIn = IsInsideEdge(cur, edgeType, edgeVal);

                if (curIn)
                {
                    if (!prevIn)
                    {
                        outPoly.Add(IntersectEdge(prev, cur, edgeType, edgeVal));
                    }
                    outPoly.Add(cur);
                }
                else if (prevIn)
                {
                    outPoly.Add(IntersectEdge(prev, cur, edgeType, edgeVal));
                }

                prev = cur;
                prevIn = curIn;
            }

            return outPoly;
        }

        private static bool IsInsideEdge(Point2d p, int edgeType, double val)
        {
            switch (edgeType)
            {
                case 0: return p.X >= val; // Left
                case 1: return p.X <= val; // Right
                case 2: return p.Y >= val; // Bottom
                case 3: return p.Y <= val; // Top
                default: return true;
            }
        }

        private static Point2d IntersectEdge(Point2d p1, Point2d p2, int edgeType, double val)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            switch (edgeType)
            {
                case 0: // X = val
                case 1:
                    if (Math.Abs(dx) < 1e-6) return new Point2d(val, p1.Y);
                    return new Point2d(val, p1.Y + dy * (val - p1.X) / dx);

                case 2: // Y = val
                case 3:
                    if (Math.Abs(dy) < 1e-6) return new Point2d(p1.X, val);
                    return new Point2d(p1.X + dx * (val - p1.Y) / dy, val);

                default:
                    return p1;
            }
        }

        private class WallSegment2D
        {
            public Point2d Start;
            public Point2d End;
            public Vector2d Dir;
            public double Length;

            public WallSegment2D(Point2d s, Point2d e)
            {
                Start = s;
                End = e;
                Vector2d v = e - s;
                Length = v.Length;
                Dir = (Length > 1e-4) ? (v / Length) : new Vector2d(1, 0);
            }
        }

        private class DoorThresholdDrawJig : DrawJig
        {
            private readonly Point3d _startWcs;
            private Point3d _curWcs;
            private readonly List<WallSegment2D> _wallSegs;
            private double _manualThickness; // <= 0 => Tự động nhận dạng độ dày 2 mép tường
            private int _flipSide;

            public Point3d CurrentWcsPoint { get { return _curWcs; } }
            public double ManualThickness { get { return _manualThickness; } }
            public int FlipSide { get { return _flipSide; } }
            public double ResolvedThickStart { get; private set; }
            public double ResolvedThickEnd { get; private set; }
            public int ResolvedSign { get; private set; }

            public DoorThresholdDrawJig(
                Point3d startWcs, List<WallSegment2D> wallSegs, double initialManualThickness, int initialFlip)
            {
                _startWcs = startWcs;
                _curWcs = startWcs;
                _wallSegs = wallSegs ?? new List<WallSegment2D>();
                _manualThickness = initialManualThickness;
                _flipSide = initialFlip >= 0 ? 1 : -1;
                ResolvedThickStart = initialManualThickness > 0 ? initialManualThickness : 110.0;
                ResolvedThickEnd = ResolvedThickStart;
                ResolvedSign = _flipSide;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                UpdateResolvedGeometry();
                string promptMsg = string.Format(
                    "\nPick điểm đầu tường đối diện (Dày tự dò {0:F0}/{1:F0}mm)", ResolvedThickStart, ResolvedThickEnd);
                JigPromptPointOptions opts = new JigPromptPointOptions(promptMsg);
                opts.Keywords.Add("F", "F", "Đổi phía (F)");
                opts.Keywords.Add("100", "100", "Dày 100");
                opts.Keywords.Add("110", "110", "Dày 110");
                opts.Keywords.Add("200", "200", "Dày 200");
                opts.Keywords.Add("220", "220", "Dày 220");
                opts.Keywords.Add("300", "300", "Dày 300");
                opts.Keywords.Add("AUTO", "AUTO", "Tự động đo 2 mép (AUTO)");
                opts.UserInputControls = UserInputControls.Accept3dCoordinates |
                                         UserInputControls.NullResponseAccepted;
                opts.BasePoint = _startWcs;
                opts.UseBasePoint = true;
                opts.Cursor = CursorType.RubberBand;

                PromptPointResult res = prompts.AcquirePoint(opts);
                if (res.Status == PromptStatus.Keyword)
                {
                    string kw = (res.StringResult ?? "").ToUpperInvariant();
                    if (kw == "F") _flipSide = -_flipSide;
                    else if (kw == "AUTO") _manualThickness = 0.0;
                    else
                    {
                        double val;
                        if (double.TryParse(kw, out val) && val >= 50.0) _manualThickness = val;
                    }
                    UpdateResolvedGeometry();
                    return SamplerStatus.OK;
                }

                if (res.Status == PromptStatus.OK)
                {
                    Point3d newWcs = res.Value;
                    if (newWcs.DistanceTo(_curWcs) < 0.5) return SamplerStatus.NoChange;
                    _curWcs = newWcs;
                    UpdateResolvedGeometry();
                    return SamplerStatus.OK;
                }

                return SamplerStatus.Cancel;
            }

            public void UpdateResolvedGeometry()
            {
                Vector3d v = _curWcs - _startWcs;
                Vector2d v2 = new Vector2d(v.X, v.Y);
                if (v2.Length < 10.0) return;
                Vector2d dir = v2 / v2.Length;
                Vector2d norm = new Vector2d(-dir.Y, dir.X);

                Point2d pStart = new Point2d(_startWcs.X, _startWcs.Y);
                Point2d pEnd = new Point2d(_curWcs.X, _curWcs.Y);

                double t1, t2;
                int s1, s2;
                bool f1 = DetectWallProfileAtEndpoint(pStart, dir, norm, _wallSegs, out t1, out s1);
                bool f2 = DetectWallProfileAtEndpoint(pEnd, dir, norm, _wallSegs, out t2, out s2);

                int autoSign = f1 ? s1 : (f2 ? s2 : 1);
                ResolvedSign = autoSign * _flipSide;

                if (_manualThickness > 10.0)
                {
                    ResolvedThickStart = _manualThickness;
                    ResolvedThickEnd = _manualThickness;
                }
                else
                {
                    ResolvedThickStart = f1 ? t1 : (f2 ? t2 : 110.0);
                    ResolvedThickEnd = f2 ? t2 : (f1 ? t1 : 110.0);
                }
            }

            public void GetMlineCenterline(out Point2d c1, out Point2d c2, out double scale, out Point2d? stepStart, out Point2d? stepEnd)
            {
                UpdateResolvedGeometry();
                Point2d a = new Point2d(_startWcs.X, _startWcs.Y);
                Point2d b = new Point2d(_curWcs.X, _curWcs.Y);
                Vector2d v = b - a;
                double len = v.Length;
                stepStart = null;
                stepEnd = null;
                if (len < 1e-4)
                {
                    c1 = a;
                    c2 = b;
                    scale = ResolvedThickStart;
                    return;
                }

                Vector2d dir = v / len;
                Vector2d shiftVec = new Vector2d(-dir.Y, dir.X) * ResolvedSign;
                scale = Math.Min(ResolvedThickStart, ResolvedThickEnd);

                c1 = a + shiftVec * (scale * 0.5);
                c2 = b + shiftVec * (scale * 0.5);

                if (ResolvedThickStart > scale + 12.0)
                {
                    stepStart = a + shiftVec * ResolvedThickStart;
                }
                if (ResolvedThickEnd > scale + 12.0)
                {
                    stepEnd = b + shiftVec * ResolvedThickEnd;
                }
            }

            protected override bool WorldDraw(Gi.WorldDraw draw)
            {
                if (_curWcs.DistanceTo(_startWcs) < 5.0) return true;
                Point2d c1, c2;
                double scale;
                Point2d? stepStart, stepEnd;
                GetMlineCenterline(out c1, out c2, out scale, out stepStart, out stepEnd);

                Vector2d v = c2 - c1;
                double len = v.Length;
                if (len < 1e-4) return true;
                Vector2d dir = v / len;
                Vector2d norm = new Vector2d(-dir.Y, dir.X);
                double half = scale * 0.5;

                Point3d r1a = new Point3d(c1.X - norm.X * half, c1.Y - norm.Y * half, 0);
                Point3d r1b = new Point3d(c2.X - norm.X * half, c2.Y - norm.Y * half, 0);
                Point3d r2a = new Point3d(c1.X + norm.X * half, c1.Y + norm.Y * half, 0);
                Point3d r2b = new Point3d(c2.X + norm.X * half, c2.Y + norm.Y * half, 0);

                draw.SubEntityTraits.LineWeight = LineWeight.LineWeight030;
                draw.SubEntityTraits.Color = 6; // Magenta khớp màu AI_XD_DIEM_NOI
                draw.Geometry.WorldLine(r1a, r1b);
                draw.Geometry.WorldLine(r2a, r2b);

                // Hiển thị nét khóa bậc tường nếu 2 đầu mép tường có độ dày khác nhau
                if (stepStart.HasValue)
                {
                    Point3d stP = new Point3d(stepStart.Value.X, stepStart.Value.Y, 0);
                    Point3d shiftEnd = new Point3d(_startWcs.X + (ResolvedSign * (-dir.Y) * scale), _startWcs.Y + (ResolvedSign * dir.X * scale), 0);
                    draw.Geometry.WorldLine(shiftEnd, stP);
                }
                if (stepEnd.HasValue)
                {
                    Point3d edP = new Point3d(stepEnd.Value.X, stepEnd.Value.Y, 0);
                    Point3d shiftEnd = new Point3d(_curWcs.X + (ResolvedSign * (-dir.Y) * scale), _curWcs.Y + (ResolvedSign * dir.X * scale), 0);
                    draw.Geometry.WorldLine(shiftEnd, edP);
                }

                draw.SubEntityTraits.Color = 8;
                draw.Geometry.WorldLine(r1a, r2a);
                draw.Geometry.WorldLine(r1b, r2b);
                return true;
            }
        }

        /// <summary>
        /// DÒ & NỐI CÁC ĐẦU TƯỜNG HỞ CỬA & KHE HỞ TƯỜNG BẰNG MLINE TRÊN LAYER AI_XD_DIEM_NOI.
        /// - Dynamic Input bám theo phím F12 chuẩn lệnh AutoCAD gốc (tắt F12 hiện Command Line, bật F12 hiện song song).
        /// - Pick [P]: Tự nhận dạng độ dày cả 2 đầu tường để tạo MLINE 2 đường song song + tự nối khép kín góc/bậc tường.
        /// - Tự động [A]/[W]: Xử lý khe thẳng hàng 1..1800mm (MLINE), kéo dài đầu lửng 1..350mm chạm tường/cột & khép khe vi sai 0.1..15mm.
        /// - Layer AI_XD_DIEM_NOI tự bật khi chạy Bao Phòng và tự tắt khi không còn làm Bao Phòng.
        /// </summary>
        public static int CloseDoorGapsWithDoubleWallLines(Document doc, Extents3d? scopeBounds)
        {
            if (doc == null) return 0;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            int totalConnected = 0;

            try
            {
                // Tạm bật AI_XD_DIEM_NOI trong khi thao tác Nối ngạch cửa để kỹ sư quan sát MLINE live, sau đó tắt lại khi kết thúc (chỉ bật khi Bao phòng)
                using (doc.LockDocument())
                {
                    SetDiemNoiLayerVisibility(db, true);
                }

                PromptKeywordOptions pko = new PromptKeywordOptions("\nAI Nối Ngạch Cửa MLINE - Chọn chế độ ");
                pko.Keywords.Add("A", "A", "Tự động dò (A)");
                pko.Keywords.Add("W", "W", "Quét vùng (W)");
                pko.Keywords.Add("P", "P", "Pick 2 điểm (P)");
                pko.Keywords.Default = "A";
                pko.AllowNone = true;

                PromptResult pr = ed.GetKeywords(pko);
                string mode = "A";
                if (pr.Status == PromptStatus.OK && !string.IsNullOrEmpty(pr.StringResult))
                {
                    mode = pr.StringResult.ToUpperInvariant();
                }
                else if (pr.Status != PromptStatus.None && pr.Status != PromptStatus.OK)
                {
                    return 0;
                }

                Extents3d? filterBounds = scopeBounds;

                if (mode == "W")
                {
                    PromptPointOptions ppo1 = new PromptPointOptions("\nChọn góc thứ nhất của vùng nối ngạch cửa: ");
                    PromptPointResult ppr1 = ed.GetPoint(ppo1);
                    if (ppr1.Status != PromptStatus.OK) return 0;

                    PromptCornerOptions pco = new PromptCornerOptions("\nChọn góc đối diện của vùng nối ngạch cửa: ", ppr1.Value);
                    PromptPointResult ppr2 = ed.GetCorner(pco);
                    if (ppr2.Status != PromptStatus.OK) return 0;

                    Point3d min = new Point3d(Math.Min(ppr1.Value.X, ppr2.Value.X), Math.Min(ppr1.Value.Y, ppr2.Value.Y), 0);
                    Point3d max = new Point3d(Math.Max(ppr1.Value.X, ppr2.Value.X), Math.Max(ppr1.Value.Y, ppr2.Value.Y), 0);
                    filterBounds = new Extents3d(min, max);
                }
                else if (mode == "P")
                {
                    double manualThickness = 0.0; // 0 = Tự động dò độ dày 2 đầu mép tường
                    int activeFlip = 1;
                    int manualMlineCount = 0;

                    while (true)
                    {
                        DoorCloseSceneData sceneP;
                        using (doc.LockDocument())
                        using (Transaction trRead = db.TransactionManager.StartTransaction())
                        {
                            sceneP = CollectStructuralSceneForDoorClosing(db, trRead, null);
                            trRead.Commit();
                        }
                        List<WallSegment2D> wallSegs = sceneP.WallAndColSegs;

                        string thickText = manualThickness > 0 ? string.Format("{0:F0}mm", manualThickness) : "Tự dò 2 mép tường";
                        PromptPointOptions ppoA = new PromptPointOptions(
                            string.Format("\nPick điểm đầu mép tường thứ 1 ({0}) ", thickText));
                        ppoA.Keywords.Add("AUTO", "AUTO", "Tự dò (AUTO)");
                        ppoA.Keywords.Add("100", "100", "Dày 100");
                        ppoA.Keywords.Add("110", "110", "Dày 110");
                        ppoA.Keywords.Add("200", "200", "Dày 200");
                        ppoA.Keywords.Add("220", "220", "Dày 220");
                        ppoA.AllowNone = true;

                        PromptPointResult pprA = ed.GetPoint(ppoA);
                        if (pprA.Status == PromptStatus.Keyword)
                        {
                            string kw = (pprA.StringResult ?? "").ToUpperInvariant();
                            if (kw == "AUTO") manualThickness = 0.0;
                            else
                            {
                                double parsed;
                                if (double.TryParse(kw, out parsed) && parsed >= 50.0) manualThickness = parsed;
                            }
                            continue;
                        }
                        if (pprA.Status != PromptStatus.OK) break;

                        Point3d rawStartWcs = pprA.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                        Point2d snappedStart = SnapPointToWallVertex(new Point2d(rawStartWcs.X, rawStartWcs.Y), wallSegs, 25.0);
                        Point3d startWcs = new Point3d(snappedStart.X, snappedStart.Y, 0);

                        DoorThresholdDrawJig jig = new DoorThresholdDrawJig(startWcs, wallSegs, manualThickness, activeFlip);
                        PromptResult jigRes = ed.Drag(jig);
                        if (jigRes.Status != PromptStatus.OK) break;

                        manualThickness = jig.ManualThickness;
                        activeFlip = jig.FlipSide;

                        Point3d rawEndWcs = jig.CurrentWcsPoint;
                        if (rawEndWcs.DistanceTo(startWcs) < 20.0) continue;
                        Point2d snappedEnd = SnapPointToWallVertex(new Point2d(rawEndWcs.X, rawEndWcs.Y), wallSegs, 25.0);

                        Point2d a = snappedStart;
                        Point2d b = snappedEnd;
                        Vector2d ab = b - a;
                        double abLen = ab.Length;
                        if (abLen < 10.0) continue;
                        Vector2d abDir = ab / abLen;
                        Vector2d abNorm = new Vector2d(-abDir.Y, abDir.X);

                        double t1, t2;
                        int s1, s2;
                        bool f1 = DetectWallProfileAtEndpoint(a, abDir, abNorm, wallSegs, out t1, out s1);
                        bool f2 = DetectWallProfileAtEndpoint(b, abDir, abNorm, wallSegs, out t2, out s2);
                        int finalSign = (f1 ? s1 : (f2 ? s2 : 1)) * activeFlip;
                        double thStart = manualThickness > 10.0 ? manualThickness : (f1 ? t1 : (f2 ? t2 : 110.0));
                        double thEnd = manualThickness > 10.0 ? manualThickness : (f2 ? t2 : (f1 ? t1 : 110.0));

                        double scale = Math.Min(thStart, thEnd);
                        Vector2d shiftVec = abNorm * finalSign;
                        Point2d c1 = a + shiftVec * (scale * 0.5);
                        Point2d c2 = b + shiftVec * (scale * 0.5);

                        using (DocumentLock docLock = doc.LockDocument())
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            EnsureDiemNoiLayer(db, tr);
                            ObjectId mlStyleId = EnsureDoorMlineStyle(db, tr);
                            BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                            CreateDoorMlineEntity(btr, tr, db, mlStyleId, c1, c2, scale);
                            manualMlineCount++;

                            // Khép bậc lệch dày nếu 2 đầu tường dày khác nhau (> 12mm) -> Không bao giờ hở tường
                            if (thStart > scale + 12.0)
                            {
                                AddClosureLineOnDiemNoi(btr, tr, a + shiftVec * scale, a + shiftVec * thStart);
                            }
                            if (thEnd > scale + 12.0)
                            {
                                AddClosureLineOnDiemNoi(btr, tr, b + shiftVec * scale, b + shiftVec * thEnd);
                            }

                            // Tự động kéo khít nếu điểm a/b/twin cách mép tường đối diện <= 25mm
                            Point2d aTwin = a + shiftVec * scale;
                            Point2d bTwin = b + shiftVec * scale;
                            AutoCloseEndpointToNearbyWall(btr, tr, a, abDir * (-1.0), wallSegs, 30.0);
                            AutoCloseEndpointToNearbyWall(btr, tr, b, abDir, wallSegs, 30.0);
                            AutoCloseEndpointToNearbyWall(btr, tr, aTwin, abDir * (-1.0), wallSegs, 30.0);
                            AutoCloseEndpointToNearbyWall(btr, tr, bTwin, abDir, wallSegs, 30.0);

                            tr.Commit();
                        }

                        ed.UpdateScreen();
                        ed.WriteMessage(string.Format(
                            "\n✅ Đã tạo MLINE ngạch cửa trên layer '{0}' (Dày: {1:F0}/{2:F0}mm -> Scale {3:F0}mm).",
                            LAYER_DIEM_NOI, thStart, thEnd, scale));
                    }
                    return manualMlineCount;
                }

                // CHẾ ĐỘ TỰ ĐỘNG [A] / QUÉT VÙNG [W] (SIẾT CHUẨN):
                // 1) Chỉ lấy AI_XD_TUONG làm tường (AI_XD_COT làm điểm chạm/vật cản, AI_XD_CUA làm chứng cứ cửa)
                // 2) Khe cửa 550..1800mm: Bắt buộc có AI_XD_CUA gần khe HOẶC có nét bịt đầu tường kép 2 bên, và CẤM cắt tường/cột khác
                // 3) Khe hở "tròn đỏ" 1..250mm: Chỉ nối khi đủ cặp tường song song thật + Extend đầu tường >=150mm trong bán kính <= 50mm
                // 4) Không xóa AI_XD_DIEM_NOI cũ; chỉ chặn vẽ đè lên vị trí đã nối hoặc sẵn tường
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureDiemNoiLayer(db, tr);
                    ObjectId mlStyleId = EnsureDoorMlineStyle(db, tr);
                    BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                    DoorCloseSceneData scene = CollectStructuralSceneForDoorClosing(db, tr, filterBounds);
                    List<WallSegment2D> wallSegs = scene.WallSegs;
                    List<WallSegment2D> colSegs = scene.ColSegs;
                    List<Extents3d> colBoxes = scene.ColBoxes;
                    List<Extents3d> doorBoxes = scene.DoorBoxes;
                    List<WallSegment2D> existingDiemNoi = scene.ExistingDiemNoiSegs;

                    List<WallSegment2D> blockedSegs = new List<WallSegment2D>(wallSegs.Count + existingDiemNoi.Count);
                    blockedSegs.AddRange(wallSegs);
                    blockedSegs.AddRange(existingDiemNoi);

                    List<KeyValuePair<Point2d, Point2d>> rawGaps = new List<KeyValuePair<Point2d, Point2d>>();
                    for (int i = 0; i < wallSegs.Count; i++)
                    {
                        WallSegment2D s1 = wallSegs[i];
                        if (s1.Length < 120.0) continue;
                        for (int j = i + 1; j < wallSegs.Count; j++)
                        {
                            WallSegment2D s2 = wallSegs[j];
                            if (s2.Length < 120.0) continue;
                            if (Math.Abs(s1.Dir.DotProduct(s2.Dir)) < 0.995) continue;

                            Point2d pt1, pt2;
                            if (CheckFacingDoorGap(s1, s2, out pt1, out pt2))
                            {
                                if (DoesSegmentCrossAnyObstacle(pt1, pt2, wallSegs, colBoxes, colSegs)) continue;
                                if (!IsGapAlreadySpanned(pt1, pt2, blockedSegs, rawGaps))
                                {
                                    rawGaps.Add(new KeyValuePair<Point2d, Point2d>(pt1, pt2));
                                }
                            }
                        }
                    }

                    // Ghép đôi 2 khe song song thành MLINE chuẩn; tuyệt đối KHÔNG tự bịa nét đôi cho nét đơn lẻ
                    bool[] usedGap = new bool[rawGaps.Count];
                    List<KeyValuePair<Point2d, Point2d>> createdGapLines = new List<KeyValuePair<Point2d, Point2d>>();
                    List<WallSegment2D> createdCenters = new List<WallSegment2D>();
                    for (int i = 0; i < rawGaps.Count; i++)
                    {
                        if (usedGap[i]) continue;
                        usedGap[i] = true;

                        Point2d a1 = rawGaps[i].Key;
                        Point2d b1 = rawGaps[i].Value;
                        Vector2d v1 = b1 - a1;
                        double len1 = v1.Length;
                        if (len1 < 1.0) continue;
                        Vector2d d1 = v1 / len1;
                        Vector2d n1 = new Vector2d(-d1.Y, d1.X);
                        Point2d mid1 = new Point2d((a1.X + b1.X) * 0.5, (a1.Y + b1.Y) * 0.5);

                        int partnerIdx = -1;
                        double bestPerp = double.MaxValue;
                        for (int j = i + 1; j < rawGaps.Count; j++)
                        {
                            if (usedGap[j]) continue;
                            Point2d a2 = rawGaps[j].Key;
                            Point2d b2 = rawGaps[j].Value;
                            Vector2d v2 = b2 - a2;
                            double len2 = v2.Length;
                            if (len2 < 1.0) continue;
                            Vector2d d2 = v2 / len2;
                            if (Math.Abs(d1.DotProduct(d2)) < 0.995) continue;

                            Point2d mid2 = new Point2d((a2.X + b2.X) * 0.5, (a2.Y + b2.Y) * 0.5);
                            Vector2d dm = mid2 - mid1;
                            double along = Math.Abs(dm.DotProduct(d1));
                            double perp = Math.Abs(dm.DotProduct(n1));
                            if (perp >= 85.0 && perp <= 380.0 && along <= 150.0 && perp < bestPerp)
                            {
                                bestPerp = perp;
                                partnerIdx = j;
                            }
                        }

                        if (partnerIdx >= 0)
                        {
                            usedGap[partnerIdx] = true;
                            Point2d a2 = rawGaps[partnerIdx].Key;
                            Point2d b2 = rawGaps[partnerIdx].Value;
                            if (a1.GetDistanceTo(b2) + b1.GetDistanceTo(a2) < a1.GetDistanceTo(a2) + b1.GetDistanceTo(b2))
                            {
                                Point2d tmp = a2; a2 = b2; b2 = tmp;
                            }

                            if (DoesSegmentCrossAnyObstacle(a2, b2, wallSegs, colBoxes, colSegs)) continue;
                            if (IsGapAlreadySpanned(a1, b1, blockedSegs, createdGapLines) &&
                                IsGapAlreadySpanned(a2, b2, blockedSegs, createdGapLines)) continue;

                            Point2d c1 = new Point2d((a1.X + a2.X) * 0.5, (a1.Y + a2.Y) * 0.5);
                            Point2d c2 = new Point2d((b1.X + b2.X) * 0.5, (b1.Y + b2.Y) * 0.5);
                            if (DoesSegmentCrossAnyObstacle(c1, c2, wallSegs, colBoxes, colSegs)) continue;

                            double openDist = Math.Max(len1, a2.GetDistanceTo(b2));
                            if (openDist > 250.0)
                            {
                                // Ô cửa 250..1800mm: BẮT BUỘC có Block Cửa AI_XD_CUA HOẶC đủ 2 đầu bịt tường vuông góc thật (khi >= 500mm)
                                bool hasDoor = IsOpeningNearAnyDoorBox(a1, b1, a2, b2, doorBoxes, 160.0);
                                bool hasJambCaps = openDist >= 500.0 &&
                                                   HasWallJambCapBetween(a1, a2, wallSegs) &&
                                                   HasWallJambCapBetween(b1, b2, wallSegs);
                                if (!hasDoor && !hasJambCaps) continue;
                            }

                            double mScale = bestPerp;
                            if (IsMlineOverlappingExisting(c1, c2, mScale, createdCenters, existingDiemNoi)) continue;
                            CreateDoorMlineEntity(btr, tr, db, mlStyleId, c1, c2, mScale);
                            createdGapLines.Add(new KeyValuePair<Point2d, Point2d>(a1, b1));
                            createdGapLines.Add(new KeyValuePair<Point2d, Point2d>(a2, b2));
                            createdCenters.Add(new WallSegment2D(c1, c2));
                            totalConnected++;
                        }
                        else
                        {
                            // Chỉ hở 1 cạnh khi cạnh đối diện đã là tường AI_XD_TUONG sẵn có -> xác nhận tường đối diện thật
                            Point2d oppA, oppB;
                            if (TryFindParallelOppositeWallRail(a1, b1, wallSegs, out oppA, out oppB))
                            {
                                if (len1 > 250.0)
                                {
                                    bool hasDoor = IsOpeningNearAnyDoorBox(a1, b1, oppA, oppB, doorBoxes, 160.0);
                                    bool hasJambCaps = len1 >= 500.0 &&
                                                       HasWallJambCapBetween(a1, oppA, wallSegs) &&
                                                       HasWallJambCapBetween(b1, oppB, wallSegs);
                                    if (!hasDoor && !hasJambCaps) continue;
                                }

                                if (IsGapAlreadySpanned(a1, b1, existingDiemNoi, createdGapLines)) continue;
                                Point2d c1 = new Point2d((a1.X + oppA.X) * 0.5, (a1.Y + oppA.Y) * 0.5);
                                Point2d c2 = new Point2d((b1.X + oppB.X) * 0.5, (b1.Y + oppB.Y) * 0.5);
                                if (DoesSegmentCrossAnyObstacle(c1, c2, wallSegs, colBoxes, colSegs)) continue;

                                double mScale = a1.GetDistanceTo(oppA);
                                if (IsMlineOverlappingExisting(c1, c2, mScale, createdCenters, existingDiemNoi)) continue;
                                CreateDoorMlineEntity(btr, tr, db, mlStyleId, c1, c2, mScale);
                                createdGapLines.Add(new KeyValuePair<Point2d, Point2d>(a1, b1));
                                createdCenters.Add(new WallSegment2D(c1, c2));
                                totalConnected++;
                            }
                        }
                    }

                    // Pass 2: Kéo dài đầu tường dài >= 150mm bị hở <= 50mm chạm tường/cột gần nhất (bỏ bắn tia 350mm cho nét ngắn)
                    int extCount = ExtendOpenWallEndpointsToPerpTargets(btr, tr, wallSegs, colSegs, colBoxes, existingDiemNoi);
                    // Pass 3: Khép khe lệch vi sai góc siêu nhỏ 0.1..15mm trên AI_XD_TUONG
                    int microCount = CloseMicroCornerGaps(btr, tr, wallSegs, existingDiemNoi);
                    totalConnected += extCount + microCount;

                    tr.Commit();
                }

                ed.UpdateScreen();
                ed.WriteMessage(string.Format(
                    "\n✅ [AI NỐI NGẠCH CỬA] Đã tạo {0} điểm nối MLINE trên layer '{1}' (tự bật khi chạy Bao Phòng & tự tắt khi xong).\n",
                    totalConnected, LAYER_DIEM_NOI));
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\n[Lỗi Nối Ngạch Cửa] " + ex.Message);
            }
            finally
            {
                // Layer Điểm Nối tự động tắt khi không làm Bao Phòng
                try
                {
                    using (doc.LockDocument())
                    {
                        SetDiemNoiLayerVisibility(db, false);
                    }
                    ed.UpdateScreen();
                }
                catch { }
            }

            return totalConnected;
        }

        private class DoorCloseSceneData
        {
            public List<WallSegment2D> WallSegs = new List<WallSegment2D>();
            public List<WallSegment2D> ColSegs = new List<WallSegment2D>();
            public List<WallSegment2D> WallAndColSegs = new List<WallSegment2D>();
            public List<Extents3d> ColBoxes = new List<Extents3d>();
            public List<Extents3d> DoorBoxes = new List<Extents3d>();
            public List<WallSegment2D> ExistingDiemNoiSegs = new List<WallSegment2D>();
        }

        private static DoorCloseSceneData CollectStructuralSceneForDoorClosing(
            Database db, Transaction tr, Extents3d? filterBounds)
        {
            DoorCloseSceneData data = new DoorCloseSceneData();
            BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return data;

            bool hasAiTuong = false;
            List<Entity> allEnts = new List<Entity>();
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                allEnts.Add(ent);
                if (string.Equals(ent.Layer, "AI_XD_TUONG", StringComparison.OrdinalIgnoreCase))
                {
                    hasAiTuong = true;
                }
            }

            foreach (Entity ent in allEnts)
            {
                string lay = (ent.Layer ?? "").ToUpperInvariant();
                if (lay == "AI_XD_COT")
                {
                    try { data.ColBoxes.Add(ent.GeometricExtents); }
                    catch { }
                    Extract2DSegmentsFromEntity(ent, filterBounds, data.ColSegs);
                    continue;
                }

                if (lay == "AI_XD_CUA" || (!hasAiTuong && (lay.Contains("DOOR") || lay.Contains("CUA"))))
                {
                    try
                    {
                        Extents3d de = ent.GeometricExtents;
                        Point2d dMid = new Point2d((de.MinPoint.X + de.MaxPoint.X) * 0.5, (de.MinPoint.Y + de.MaxPoint.Y) * 0.5);
                        if (!filterBounds.HasValue || IsPointInExtents(dMid, filterBounds.Value))
                            data.DoorBoxes.Add(de);
                    }
                    catch { }
                    continue;
                }

                if (lay == LAYER_DIEM_NOI)
                {
                    Extract2DSegmentsFromEntity(ent, filterBounds, data.ExistingDiemNoiSegs);
                    continue;
                }

                bool isPureWall = hasAiTuong
                    ? (lay == "AI_XD_TUONG")
                    : ((lay.Contains("TUONG") || lay.Contains("WALL")) &&
                       !lay.Contains("VACH") && !lay.Contains("KINH") &&
                       !lay.Contains("THANG") && !lay.Contains("STAIR") &&
                       !lay.Contains("HATCH") && !lay.Contains("NOI_THAT") &&
                       !lay.Contains("DOOR") && !lay.Contains("CUA"));

                if (isPureWall)
                {
                    Extract2DSegmentsFromEntity(ent, filterBounds, data.WallSegs);
                }
            }

            data.WallAndColSegs.AddRange(data.WallSegs);
            data.WallAndColSegs.AddRange(data.ColSegs);
            return data;
        }

        private static void Extract2DSegmentsFromEntity(Entity ent, Extents3d? filterBounds, List<WallSegment2D> target)
        {
            if (ent is Line)
            {
                Line ln = (Line)ent;
                Point2d p1 = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                Point2d p2 = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                if (p1.GetDistanceTo(p2) > 5.0 &&
                    (!filterBounds.HasValue || IsPointInExtents(p1, filterBounds.Value) || IsPointInExtents(p2, filterBounds.Value)))
                {
                    target.Add(new WallSegment2D(p1, p2));
                }
            }
            else if (ent is Polyline)
            {
                Polyline pl = (Polyline)ent;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    int next = (i + 1) % pl.NumberOfVertices;
                    if (next == 0 && !pl.Closed) break;
                    Point2d p1 = pl.GetPoint2dAt(i);
                    Point2d p2 = pl.GetPoint2dAt(next);
                    if (p1.GetDistanceTo(p2) > 5.0 &&
                        (!filterBounds.HasValue || IsPointInExtents(p1, filterBounds.Value) || IsPointInExtents(p2, filterBounds.Value)))
                    {
                        target.Add(new WallSegment2D(p1, p2));
                    }
                }
            }
            else if (ent is Mline)
            {
                Mline ml = (Mline)ent;
                int nv = ml.NumberOfVertices;
                if (nv >= 2)
                {
                    double halfScale = Math.Abs(ml.Scale) * 0.5;
                    for (int i = 0; i < nv - 1; i++)
                    {
                        Point3d v0 = ml.VertexAt(i);
                        Point3d v1 = ml.VertexAt(i + 1);
                        Point2d p0 = new Point2d(v0.X, v0.Y);
                        Point2d p1 = new Point2d(v1.X, v1.Y);
                        Vector2d dv = p1 - p0;
                        if (dv.Length < 5.0) continue;
                        Vector2d u = dv / dv.Length;
                        Vector2d n = new Vector2d(-u.Y, u.X);
                        if (!filterBounds.HasValue || IsPointInExtents(p0, filterBounds.Value) || IsPointInExtents(p1, filterBounds.Value))
                        {
                            target.Add(new WallSegment2D(p0 - n * halfScale, p1 - n * halfScale));
                            target.Add(new WallSegment2D(p0 + n * halfScale, p1 + n * halfScale));
                        }
                    }
                }
            }
        }

        private static bool DetectWallProfileAtEndpoint(
            Point2d pt, Vector2d bridgeDir, Vector2d bridgeNorm, List<WallSegment2D> wallSegs,
            out double thickness, out int signSide)
        {
            thickness = 110.0;
            signSide = 1;
            double bestScore = double.MaxValue;
            bool found = false;

            // 1. Ưu tiên dò mép tường đối diện song song gần điểm pick (80..500mm theo phương vuông góc cầu nối)
            foreach (var ws in wallSegs)
            {
                if (ws.Length < 100.0) continue;
                if (Math.Abs(ws.Dir.DotProduct(bridgeDir)) < 0.96) continue;

                double dStart = ws.Start.GetDistanceTo(pt);
                double dEnd = ws.End.GetDistanceTo(pt);
                double dSeg = ArchitecturalKnowledgeService.DistancePointToSegment2D(pt, ws.Start, ws.End);
                double minDist = Math.Min(dStart, Math.Min(dEnd, dSeg));
                if (minDist > 520.0) continue;

                double signedPerp = (ws.Start - pt).DotProduct(bridgeNorm);
                double absPerp = Math.Abs(signedPerp);
                if (absPerp >= 75.0 && absPerp <= 500.0)
                {
                    if (minDist < bestScore)
                    {
                        bestScore = minDist;
                        thickness = Math.Round(absPerp);
                        signSide = signedPerp >= 0 ? 1 : -1;
                        found = true;
                    }
                }
            }

            if (found) return true;

            // 2. Dò nét bịt đầu tường vuông góc chạm vào pt (chiều dài 75..500mm)
            foreach (var ws in wallSegs)
            {
                if (ws.Length < 75.0 || ws.Length > 500.0) continue;
                if (Math.Abs(ws.Dir.DotProduct(bridgeDir)) > 0.25) continue;

                double d1 = ws.Start.GetDistanceTo(pt);
                double d2 = ws.End.GetDistanceTo(pt);
                if (Math.Min(d1, d2) <= 35.0)
                {
                    Vector2d off = (d1 <= d2) ? (ws.End - ws.Start) : (ws.Start - ws.End);
                    double signedPerp = off.DotProduct(bridgeNorm);
                    if (Math.Abs(signedPerp) >= 75.0 && Math.Abs(signedPerp) <= 500.0)
                    {
                        thickness = Math.Round(Math.Abs(signedPerp));
                        signSide = signedPerp >= 0 ? 1 : -1;
                        return true;
                    }
                }
            }

            return false;
        }

        private static Point2d SnapPointToWallVertex(Point2d pt, List<WallSegment2D> wallSegs, double snapTol)
        {
            Point2d best = pt;
            double bestD = snapTol;
            foreach (var ws in wallSegs)
            {
                double d1 = ws.Start.GetDistanceTo(pt);
                if (d1 < bestD) { bestD = d1; best = ws.Start; }
                double d2 = ws.End.GetDistanceTo(pt);
                if (d2 < bestD) { bestD = d2; best = ws.End; }
            }
            return best;
        }

        private static bool IsPointInsideAnyBox(Point2d pt, List<Extents3d> boxes, double tol)
        {
            foreach (var b in boxes)
            {
                if (pt.X >= b.MinPoint.X - tol && pt.X <= b.MaxPoint.X + tol &&
                    pt.Y >= b.MinPoint.Y - tol && pt.Y <= b.MaxPoint.Y + tol)
                    return true;
            }
            return false;
        }

        private static bool IsEndpointFree(Point2d pt, WallSegment2D selfSeg, List<WallSegment2D> allSegs, double tol = 0.5)
        {
            foreach (var other in allSegs)
            {
                if (ReferenceEquals(other, selfSeg)) continue;
                if (ArchitecturalKnowledgeService.DistancePointToSegment2D(pt, other.Start, other.End) <= tol)
                    return false;
            }
            return true;
        }

        private static int ExtendOpenWallEndpointsToPerpTargets(
            BlockTableRecord btr, Transaction tr,
            List<WallSegment2D> wallSegs, List<WallSegment2D> colSegs,
            List<Extents3d> colBoxes, List<WallSegment2D> existingDiemNoi)
        {
            int added = 0;
            List<WallSegment2D> allTargets = new List<WallSegment2D>(wallSegs.Count + colSegs.Count);
            allTargets.AddRange(wallSegs);
            allTargets.AddRange(colSegs);

            List<WallSegment2D> closedAlready = new List<WallSegment2D>(allTargets.Count + existingDiemNoi.Count);
            closedAlready.AddRange(allTargets);
            closedAlready.AddRange(existingDiemNoi);

            List<KeyValuePair<Point2d, Point2d>> extLines = new List<KeyValuePair<Point2d, Point2d>>();

            foreach (var ws in wallSegs)
            {
                // Siết chuẩn: chỉ xử lý đầu tường thật dài >= 150mm, kéo tối đa <= 50mm (không bắn tia 350mm cho nét ngắn)
                if (ws.Length < 150.0) continue;
                Point2d[] pts = new Point2d[] { ws.Start, ws.End };
                Vector2d[] dirs = new Vector2d[] { -ws.Dir, ws.Dir };

                for (int endIdx = 0; endIdx < 2; endIdx++)
                {
                    Point2d p = pts[endIdx];
                    Vector2d ray = dirs[endIdx];
                    if (!IsEndpointFree(p, ws, closedAlready, 0.5)) continue;

                    double bestT = 50.1;
                    Point2d bestHit = Point2d.Origin;
                    bool hasHit = false;

                    foreach (var target in allTargets)
                    {
                        if (ReferenceEquals(target, ws)) continue;
                        if (Math.Abs(ray.DotProduct(target.Dir)) > 0.20) continue; // chỉ chạm tường/cột gần vuông góc

                        Vector2d vTarget = target.End - target.Start;
                        double denom = ray.X * (-vTarget.Y) - ray.Y * (-vTarget.X);
                        if (Math.Abs(denom) < 1e-6) continue;

                        Vector2d rhs = target.Start - p;
                        double t = (rhs.X * (-vTarget.Y) - rhs.Y * (-vTarget.X)) / denom;
                        double u = (ray.X * rhs.Y - ray.Y * rhs.X) / denom;

                        if (t > 0.5 && t <= 50.0 && u >= -0.02 && u <= 1.02 && t < bestT)
                        {
                            bestT = t;
                            bestHit = p + ray * t;
                            hasHit = true;
                        }
                    }

                    if (hasHit &&
                        !DoesSegmentCrossAnyObstacle(p, bestHit, wallSegs, colBoxes, colSegs) &&
                        !IsGapAlreadySpanned(p, bestHit, closedAlready, extLines))
                    {
                        extLines.Add(new KeyValuePair<Point2d, Point2d>(p, bestHit));
                    }
                }
            }

            foreach (var el in extLines)
            {
                AddClosureLineOnDiemNoi(btr, tr, el.Key, el.Value);
                added++;
            }
            return added;
        }

        private static int CloseMicroCornerGaps(
            BlockTableRecord btr, Transaction tr,
            List<WallSegment2D> wallSegs, List<WallSegment2D> existingDiemNoi)
        {
            int added = 0;
            List<WallSegment2D> touchRefs = new List<WallSegment2D>(wallSegs.Count + existingDiemNoi.Count);
            touchRefs.AddRange(wallSegs);
            touchRefs.AddRange(existingDiemNoi);

            List<Point2d> freePts = new List<Point2d>();
            foreach (var ws in wallSegs)
            {
                if (ws.Length < 40.0) continue;
                if (IsEndpointFree(ws.Start, ws, touchRefs, 0.2)) freePts.Add(ws.Start);
                if (IsEndpointFree(ws.End, ws, touchRefs, 0.2)) freePts.Add(ws.End);
            }

            bool[] paired = new bool[freePts.Count];
            for (int i = 0; i < freePts.Count; i++)
            {
                if (paired[i]) continue;
                int bestJ = -1;
                double bestD = 15.0;
                for (int j = i + 1; j < freePts.Count; j++)
                {
                    if (paired[j]) continue;
                    double d = freePts[i].GetDistanceTo(freePts[j]);
                    if (d >= 0.1 && d <= bestD)
                    {
                        bestD = d;
                        bestJ = j;
                    }
                }
                if (bestJ >= 0)
                {
                    paired[i] = true;
                    paired[bestJ] = true;
                    AddClosureLineOnDiemNoi(btr, tr, freePts[i], freePts[bestJ]);
                    added++;
                }
            }
            return added;
        }

        private static void AutoCloseEndpointToNearbyWall(
            BlockTableRecord btr, Transaction tr, Point2d pt, Vector2d rayDir, List<WallSegment2D> wallSegs, double maxDist)
        {
            if (!IsEndpointFree(pt, null, wallSegs, 0.5)) return;
            double bestT = maxDist + 0.1;
            Point2d bestHit = Point2d.Origin;
            bool found = false;

            foreach (var target in wallSegs)
            {
                Vector2d vt = target.End - target.Start;
                double denom = rayDir.X * (-vt.Y) - rayDir.Y * (-vt.X);
                if (Math.Abs(denom) < 1e-6) continue;
                Vector2d rhs = target.Start - pt;
                double t = (rhs.X * (-vt.Y) - rhs.Y * (-vt.X)) / denom;
                double u = (rayDir.X * rhs.Y - rayDir.Y * rhs.X) / denom;
                if (t > 0.2 && t <= maxDist && u >= -0.05 && u <= 1.05 && t < bestT)
                {
                    bestT = t;
                    bestHit = pt + rayDir * t;
                    found = true;
                }
            }
            if (found) AddClosureLineOnDiemNoi(btr, tr, pt, bestHit);
        }

        private static void AddClosureLineOnDiemNoi(BlockTableRecord btr, Transaction tr, Point2d p1, Point2d p2)
        {
            if (p1.GetDistanceTo(p2) < 0.05) return;
            Line ln = new Line(new Point3d(p1.X, p1.Y, 0), new Point3d(p2.X, p2.Y, 0));
            ln.SetDatabaseDefaults();
            ln.Layer = LAYER_DIEM_NOI;
            ln.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static ObjectId EnsureDoorMlineStyle(Database db, Transaction tr)
        {
            const string styleName = "AI_NGACH_CUA";
            DBDictionary mlDict = tr.GetObject(db.MLStyleDictionaryId, OpenMode.ForWrite) as DBDictionary;
            if (mlDict.Contains(styleName))
            {
                return mlDict.GetAt(styleName);
            }

            ObjectId byLayerLtId = db.ByLayerLinetype;
            MlineStyle mls = new MlineStyle();
            mls.Name = styleName;
            mls.Description = "Ngach cua 2 net song song AI_XD_DIEM_NOI";
            mls.ShowMiters = false;
            mls.StartSquareCap = false;
            mls.EndSquareCap = false;
            mls.StartInnerArcs = false;
            mls.EndInnerArcs = false;
            mls.StartRoundCap = false;
            mls.EndRoundCap = false;
            mls.StartAngle = Math.PI / 2.0;
            mls.EndAngle = Math.PI / 2.0;

            Autodesk.AutoCAD.Colors.Color byLayer = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
            mls.Elements.Add(new MlineStyleElement(0.5, byLayer, byLayerLtId), false);
            mls.Elements.Add(new MlineStyleElement(-0.5, byLayer, byLayerLtId), false);

            ObjectId id = mlDict.SetAt(styleName, mls);
            tr.AddNewlyCreatedDBObject(mls, true);
            return id;
        }

        private static void CreateDoorMlineEntity(
            BlockTableRecord btr, Transaction tr, Database db, ObjectId mlStyleId,
            Point2d c1, Point2d c2, double scale)
        {
            Mline ml = new Mline();
            ml.SetDatabaseDefaults(db);
            ml.Style = mlStyleId;
            ml.Scale = Math.Max(10.0, scale);
            ml.Justification = MlineJustification.Zero;
            ml.Normal = Vector3d.ZAxis;
            ml.Layer = LAYER_DIEM_NOI;
            ml.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
            ml.AppendSegment(new Point3d(c1.X, c1.Y, 0));
            ml.AppendSegment(new Point3d(c2.X, c2.Y, 0));
            btr.AppendEntity(ml);
            tr.AddNewlyCreatedDBObject(ml, true);
        }

        private static bool TryFindParallelOppositeWallRail(
            Point2d a, Point2d b, List<WallSegment2D> wallSegs, out Point2d oppA, out Point2d oppB)
        {
            oppA = Point2d.Origin;
            oppB = Point2d.Origin;
            Vector2d ab = b - a;
            double len = ab.Length;
            if (len < 1.0) return false;
            Vector2d dir = ab / len;
            Vector2d norm = new Vector2d(-dir.Y, dir.X);

            double bestPerp = double.MaxValue;
            bool found = false;

            foreach (var ws in wallSegs)
            {
                if (ws.Length < Math.Max(80.0, len * 0.70)) continue;
                if (Math.Abs(dir.DotProduct(ws.Dir)) < 0.995) continue;

                double perp = (ws.Start - a).DotProduct(norm);
                double absPerp = Math.Abs(perp);
                if (absPerp < 85.0 || absPerp > 380.0) continue;

                double pr1 = (ws.Start - a).DotProduct(dir);
                double pr2 = (ws.End - a).DotProduct(dir);
                double ov = Math.Min(len, Math.Max(pr1, pr2)) - Math.Max(0.0, Math.Min(pr1, pr2));
                if (ov >= len * 0.70 && absPerp < bestPerp)
                {
                    bestPerp = absPerp;
                    oppA = a + norm * perp;
                    oppB = b + norm * perp;
                    found = true;
                }
            }
            return found;
        }

        private static bool DoesSegmentCrossAnyObstacle(
            Point2d p1, Point2d p2,
            List<WallSegment2D> wallSegs, List<Extents3d> colBoxes, List<WallSegment2D> colSegs)
        {
            Point2d q25 = new Point2d(p1.X + (p2.X - p1.X) * 0.25, p1.Y + (p2.Y - p1.Y) * 0.25);
            Point2d mid = new Point2d((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);
            Point2d q75 = new Point2d(p1.X + (p2.X - p1.X) * 0.75, p1.Y + (p2.Y - p1.Y) * 0.75);

            if (IsPointInsideAnyBox(mid, colBoxes, 5.0) ||
                IsPointInsideAnyBox(q25, colBoxes, 2.0) ||
                IsPointInsideAnyBox(q75, colBoxes, 2.0))
            {
                return true;
            }

            foreach (var ws in wallSegs)
            {
                if (SegmentsProperlyIntersect(p1, p2, ws.Start, ws.End)) return true;
            }
            foreach (var cs in colSegs)
            {
                if (SegmentsProperlyIntersect(p1, p2, cs.Start, cs.End)) return true;
            }
            return false;
        }

        private static bool SegmentsProperlyIntersect(Point2d a1, Point2d a2, Point2d b1, Point2d b2)
        {
            Vector2d r = a2 - a1;
            Vector2d s = b2 - b1;
            double rxs = r.X * s.Y - r.Y * s.X;
            if (Math.Abs(rxs) < 1e-6) return false;

            Vector2d bma = b1 - a1;
            double t = (bma.X * s.Y - bma.Y * s.X) / rxs;
            double u = (bma.X * r.Y - bma.Y * r.X) / rxs;
            return t > 0.04 && t < 0.96 && u > 0.02 && u < 0.98;
        }

        private static bool IsOpeningNearAnyDoorBox(
            Point2d a1, Point2d b1, Point2d a2, Point2d b2, List<Extents3d> doorBoxes, double margin)
        {
            if (doorBoxes == null || doorBoxes.Count == 0) return false;
            double minX = Math.Min(Math.Min(a1.X, b1.X), Math.Min(a2.X, b2.X)) - margin;
            double maxX = Math.Max(Math.Max(a1.X, b1.X), Math.Max(a2.X, b2.X)) + margin;
            double minY = Math.Min(Math.Min(a1.Y, b1.Y), Math.Min(a2.Y, b2.Y)) - margin;
            double maxY = Math.Max(Math.Max(a1.Y, b1.Y), Math.Max(a2.Y, b2.Y)) + margin;

            foreach (var dbx in doorBoxes)
            {
                if (dbx.MaxPoint.X >= minX && dbx.MinPoint.X <= maxX &&
                    dbx.MaxPoint.Y >= minY && dbx.MinPoint.Y <= maxY)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsMlineOverlappingExisting(
            Point2d c1, Point2d c2, double scale,
            List<WallSegment2D> createdCenters, List<WallSegment2D> existingDiemNoi)
        {
            Vector2d v = c2 - c1;
            double len = v.Length;
            if (len < 1.0) return true;
            Vector2d u = v / len;
            Point2d mid = new Point2d((c1.X + c2.X) * 0.5, (c1.Y + c2.Y) * 0.5);
            double tol = Math.Max(45.0, scale * 0.65);

            foreach (var cs in createdCenters)
            {
                if (Math.Abs(u.DotProduct(cs.Dir)) < 0.96) continue;
                if (ArchitecturalKnowledgeService.DistancePointToSegment2D(mid, cs.Start, cs.End) <= tol)
                {
                    return true;
                }
            }

            foreach (var es in existingDiemNoi)
            {
                if (Math.Abs(u.DotProduct(es.Dir)) < 0.96) continue;
                if (ArchitecturalKnowledgeService.DistancePointToSegment2D(mid, es.Start, es.End) <= tol &&
                    es.Length >= len * 0.5)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasWallJambCapBetween(Point2d p1, Point2d p2, List<WallSegment2D> wallSegs)
        {
            double targetDist = p1.GetDistanceTo(p2);
            if (targetDist < 75.0 || targetDist > 380.0) return false;
            Vector2d capDir = (p2 - p1) / targetDist;

            bool hasCapSeg = false;
            foreach (var ws in wallSegs)
            {
                if (ws.Length < 75.0 || ws.Length > 400.0) continue;
                bool matchDirect = ws.Start.GetDistanceTo(p1) <= 12.0 && ws.End.GetDistanceTo(p2) <= 12.0;
                bool matchReverse = ws.Start.GetDistanceTo(p2) <= 12.0 && ws.End.GetDistanceTo(p1) <= 12.0;
                if (matchDirect || matchReverse)
                {
                    hasCapSeg = true;
                    break;
                }
            }
            if (!hasCapSeg) return false;

            // Loại trừ góc rẽ hành lang / phòng (HKHL, Bếp): nếu có nét tường chạy tiếp ra ngoài p1 hoặc p2 theo phương capDir -> KHÔNG phải bịt đầu cửa
            foreach (var ws in wallSegs)
            {
                if (ws.Length <= targetDist + 60.0) continue;
                if (Math.Abs(capDir.DotProduct(ws.Dir)) < 0.96) continue;
                double d1 = ArchitecturalKnowledgeService.DistancePointToSegment2D(p1, ws.Start, ws.End);
                double d2 = ArchitecturalKnowledgeService.DistancePointToSegment2D(p2, ws.Start, ws.End);
                if (d1 <= 15.0 || d2 <= 15.0) return false;
            }
            return true;
        }

        private static bool CheckFacingDoorGap(WallSegment2D s1, WallSegment2D s2, out Point2d pt1, out Point2d pt2)
        {
            pt1 = Point2d.Origin;
            pt2 = Point2d.Origin;

            Point2d[] pts1 = new Point2d[] { s1.Start, s1.End };
            Point2d[] pts2 = new Point2d[] { s2.Start, s2.End };

            double bestDist = double.MaxValue;
            bool found = false;

            for (int i = 0; i < 2; i++)
            {
                Point2d pA = pts1[i];
                Point2d otherA = pts1[1 - i];
                Vector2d awayA = (pA - otherA);
                if (awayA.Length < 1e-4) continue;
                awayA = awayA / awayA.Length;

                for (int j = 0; j < 2; j++)
                {
                    Point2d pB = pts2[j];
                    Point2d otherB = pts2[1 - j];
                    Vector2d awayB = (pB - otherB);
                    if (awayB.Length < 1e-4) continue;
                    awayB = awayB / awayB.Length;

                    Vector2d vAB = pB - pA;
                    double dist = vAB.Length;

                    if (dist < 1.0 || dist > 1800.0) continue;

                    double proj = Math.Abs(vAB.DotProduct(s1.Dir));
                    double perpSq = dist * dist - proj * proj;
                    // Siết sai số lệch trục <= 5mm (25mm²) để không bắt chéo tường
                    if (perpSq > 25.0) continue;

                    if (vAB.DotProduct(awayA) <= 0) continue;
                    if ((-vAB).DotProduct(awayB) <= 0) continue;

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        pt1 = pA;
                        pt2 = pB;
                        found = true;
                    }
                }
            }

            return found;
        }

        private static bool IsGapAlreadySpanned(Point2d p1, Point2d p2, List<WallSegment2D> existingSegs, List<KeyValuePair<Point2d, Point2d>> newGaps)
        {
            Point2d mid = new Point2d((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);
            double targetLen = p1.GetDistanceTo(p2);
            Vector2d targetDir = targetLen > 1e-4 ? ((p2 - p1) / targetLen) : new Vector2d(1, 0);

            for (int i = 0; i < newGaps.Count; i++)
            {
                Point2d gMid = new Point2d((newGaps[i].Key.X + newGaps[i].Value.X) * 0.5, (newGaps[i].Key.Y + newGaps[i].Value.Y) * 0.5);
                if (mid.GetDistanceTo(gMid) < 35.0) return true;
            }

            for (int i = 0; i < existingSegs.Count; i++)
            {
                WallSegment2D es = existingSegs[i];
                if (Math.Abs(targetDir.DotProduct(es.Dir)) < 0.96) continue;
                Point2d sMid = new Point2d((es.Start.X + es.End.X) * 0.5, (es.Start.Y + es.End.Y) * 0.5);
                if (mid.GetDistanceTo(sMid) < 35.0 && Math.Abs(es.Length - targetLen) < 60.0)
                {
                    return true;
                }
                if (ArchitecturalKnowledgeService.DistancePointToSegment2D(mid, es.Start, es.End) <= 8.0 &&
                    es.Length >= targetLen * 0.85)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointInExtents(Point2d pt, Extents3d ext)
        {
            return pt.X >= ext.MinPoint.X - 50.0 && pt.X <= ext.MaxPoint.X + 50.0 &&
                   pt.Y >= ext.MinPoint.Y - 50.0 && pt.Y <= ext.MaxPoint.Y + 50.0;
        }

        private static void EnsureDiemNoiLayer(Database db, Transaction tr)
        {
            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForWrite) as LayerTable;
            if (!lt.Has(LAYER_DIEM_NOI))
            {
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = LAYER_DIEM_NOI;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6);
                ltr.LineWeight = LineWeight.LineWeight030;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            EnsureWallLayer(db, tr);
        }

        private static void EnsureWallLayer(Database db, Transaction tr)
        {
            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForWrite) as LayerTable;
            if (!lt.Has("AI_XD_TUONG"))
            {
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = "AI_XD_TUONG";
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);
                ltr.LineWeight = LineWeight.LineWeight030;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private class DoorOpeningZone
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
            public List<Point2d> ArcHingeCenters = new List<Point2d>();

            public Extents3d ToExtents()
            {
                return new Extents3d(new Point3d(MinX, MinY, 0), new Point3d(MaxX, MaxY, 0));
            }
        }

        /// <summary>
        /// QUY TẮC BẮN THẲNG QUA CỬA KHI BAO PHÒNG:
        /// Chỉ khi CẢ 2 mép ngoài của Block cửa / Layer cửa (AI_XD_CUA) đều chạm vào mép Tường (AI_XD_TUONG) hoặc Cột (AI_XD_COT),
        /// hệ thống mới bắn đoạn thẳng tạm xuyên thẳng từ mép bên này sang mép bên kia (bỏ qua 100% cánh cửa, khuôn nhô & cung mở cửa).
        /// </summary>
        public static List<ObjectId> CreateTemporaryStraightDoorBridges(Document doc, Extents3d? scopeBounds)
        {
            List<ObjectId> createdIds = new List<ObjectId>();
            if (doc == null) return createdIds;
            Database db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureWallLayer(db, tr);
                    BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    if (btr == null)
                    {
                        tr.Commit();
                        return createdIds;
                    }

                    bool hasAiTuong = false;
                    List<Entity> allEnts = new List<Entity>();
                    foreach (ObjectId id in btr)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || !ent.Visible) continue;
                        allEnts.Add(ent);
                        if (string.Equals(ent.Layer, "AI_XD_TUONG", StringComparison.OrdinalIgnoreCase))
                        {
                            hasAiTuong = true;
                        }
                    }

                    List<WallSegment2D> structSegs = new List<WallSegment2D>();
                    List<Extents3d> colBoxes = new List<Extents3d>();
                    List<DoorOpeningZone> rawDoorItems = new List<DoorOpeningZone>();

                    foreach (Entity ent in allEnts)
                    {
                        string lay = (ent.Layer ?? "").Trim().ToUpperInvariant();
                        if (lay == "AI_XD_KHUNG_BAO" || lay.StartsWith("DUONG_BAO")) continue;

                        bool isDoorEnt = (lay == "AI_XD_CUA") ||
                            ((lay.Contains("CUA") || lay.Contains("DOOR")) &&
                             !lay.Contains("CUA_SO") && !lay.Contains("CUASO") && !lay.Contains("WINDOW") && !lay.Contains("KINH"));

                        if (!isDoorEnt && ent is BlockReference)
                        {
                            BlockReference br = (BlockReference)ent;
                            string bName = (br.Name ?? "").ToUpperInvariant();
                            if ((bName.Contains("CUA") || bName.Contains("DOOR")) &&
                                !bName.Contains("CUA_SO") && !bName.Contains("CUASO") && !bName.Contains("WINDOW"))
                            {
                                isDoorEnt = true;
                            }
                        }

                        if (isDoorEnt)
                        {
                            try
                            {
                                Extents3d de = ent.GeometricExtents;
                                if (scopeBounds.HasValue &&
                                    (de.MaxPoint.X < scopeBounds.Value.MinPoint.X - 100.0 || de.MinPoint.X > scopeBounds.Value.MaxPoint.X + 100.0 ||
                                     de.MaxPoint.Y < scopeBounds.Value.MinPoint.Y - 100.0 || de.MinPoint.Y > scopeBounds.Value.MaxPoint.Y + 100.0))
                                {
                                    continue;
                                }

                                DoorOpeningZone dz = new DoorOpeningZone
                                {
                                    MinX = de.MinPoint.X,
                                    MinY = de.MinPoint.Y,
                                    MaxX = de.MaxPoint.X,
                                    MaxY = de.MaxPoint.Y
                                };
                                if (ent is Arc)
                                {
                                    Arc a = (Arc)ent;
                                    if (a.Radius >= 420.0 && a.Radius <= 1800.0)
                                    {
                                        dz.ArcHingeCenters.Add(new Point2d(a.Center.X, a.Center.Y));
                                    }
                                }
                                else if (ent is BlockReference)
                                {
                                    ExtractArcCentersFromDoorBlock((BlockReference)ent, tr, dz.ArcHingeCenters);
                                }
                                rawDoorItems.Add(dz);
                            }
                            catch { }
                            continue;
                        }

                        if (lay == "AI_XD_COT" || (!hasAiTuong && (lay.Contains("COT") || lay.Contains("COL"))))
                        {
                            try { colBoxes.Add(ent.GeometricExtents); } catch { }
                            ExtractShortAllowedSegments(ent, scopeBounds, 30.0, structSegs);
                            continue;
                        }

                        bool isWallOrBridge = (lay == "AI_XD_TUONG" || lay == LAYER_DIEM_NOI || lay == "AI_XD_VACH_KINH") ||
                            (!hasAiTuong && (lay.Contains("TUONG") || lay.Contains("WALL") || lay.Contains("VACH")) &&
                             !lay.Contains("NOI_THAT") && !lay.Contains("FURN") && !lay.Contains("HATCH"));

                        if (isWallOrBridge)
                        {
                            ExtractShortAllowedSegments(ent, scopeBounds, 30.0, structSegs);
                        }
                    }

                    if (rawDoorItems.Count == 0 || structSegs.Count == 0)
                    {
                        tr.Commit();
                        return createdIds;
                    }

                    // Gộp các nét cửa rời chạm nhau (<= 25mm) thành từng vùng cửa hoàn chỉnh
                    List<DoorOpeningZone> doorZones = MergeTouchingDoorZones(rawDoorItems, 25.0);
                    List<KeyValuePair<Point2d, Point2d>> bridgePairs = new List<KeyValuePair<Point2d, Point2d>>();

                    foreach (DoorOpeningZone dz in doorZones)
                    {
                        double w = dz.MaxX - dz.MinX;
                        double h = dz.MaxY - dz.MinY;
                        if (Math.Max(w, h) < 350.0 || Math.Max(w, h) > 4500.0) continue;

                        const double touchTol = 65.0;
                        List<int> nearSegIndices = new List<int>();
                        for (int idx = 0; idx < structSegs.Count; idx++)
                        {
                            WallSegment2D ws = structSegs[idx];
                            if (IsPointNearDoorZone(ws.Start, dz, touchTol) ||
                                IsPointNearDoorZone(ws.End, dz, touchTol) ||
                                SegmentIntersectsDoorZone(ws.Start, ws.End, dz, touchTol))
                            {
                                nearSegIndices.Add(idx);
                            }
                        }
                        if (nearSegIndices.Count < 2) continue;

                        int bridgesForThisDoor = 0;
                        HashSet<string> matchedEndpoints = new HashSet<string>();

                        // TRƯỜNG HỢP A: 2 đầu mép Tường/Cột cùng phương chạm 2 mép ngoài đối diện của Cửa -> Bắn thẳng pA -> pB
                        for (int ii = 0; ii < nearSegIndices.Count; ii++)
                        {
                            WallSegment2D s1 = structSegs[nearSegIndices[ii]];
                            Point2d[] ends1 = new Point2d[] { s1.Start, s1.End };
                            Point2d[] others1 = new Point2d[] { s1.End, s1.Start };

                            for (int e1 = 0; e1 < 2; e1++)
                            {
                                Point2d pA = ends1[e1];
                                if (!IsPointNearDoorZone(pA, dz, touchTol)) continue;
                                Vector2d away1 = pA - others1[e1];
                                if (away1.Length < 1e-4) continue;
                                away1 = away1 / away1.Length;

                                int bestJ = -1;
                                Point2d bestPB = Point2d.Origin;
                                double bestSpan = double.MaxValue;

                                for (int jj = ii + 1; jj < nearSegIndices.Count; jj++)
                                {
                                    WallSegment2D s2 = structSegs[nearSegIndices[jj]];
                                    if (Math.Abs(s1.Dir.DotProduct(s2.Dir)) < 0.992) continue;

                                    Point2d[] ends2 = new Point2d[] { s2.Start, s2.End };
                                    Point2d[] others2 = new Point2d[] { s2.End, s2.Start };
                                    for (int e2 = 0; e2 < 2; e2++)
                                    {
                                        Point2d pB = ends2[e2];
                                        if (!IsPointNearDoorZone(pB, dz, touchTol)) continue;
                                        Vector2d away2 = pB - others2[e2];
                                        if (away2.Length < 1e-4) continue;
                                        away2 = away2 / away2.Length;

                                        Vector2d vAB = pB - pA;
                                        double span = vAB.Length;
                                        if (span < 350.0 || span > 3200.0 || span >= bestSpan) continue;

                                        double proj = Math.Abs(vAB.DotProduct(s1.Dir));
                                        double perpSq = Math.Max(0.0, span * span - proj * proj);
                                        if (perpSq > 144.0) continue; // lệch ngang <= 12mm

                                        if (vAB.DotProduct(away1) <= 0.0 || (-vAB).DotProduct(away2) <= 0.0) continue;

                                        // Kiểm chứng pA và pB thực sự nằm ở 2 MÉP NGOÀI ĐỐI DIỆN của Block/Layer cửa
                                        Vector2d uOpen = vAB / span;
                                        if (!ArePointsAtOppositeOuterDoorSides(pA, pB, uOpen, dz, 130.0)) continue;
                                        if (IsMidpointInsideColumn(pA, pB, colBoxes)) continue;

                                        bestSpan = span;
                                        bestPB = pB;
                                        bestJ = jj;
                                    }
                                }

                                if (bestJ >= 0)
                                {
                                    matchedEndpoints.Add(FormatPtKey(pA));
                                    matchedEndpoints.Add(FormatPtKey(bestPB));
                                    if (!IsGapAlreadySpanned(pA, bestPB, structSegs, bridgePairs))
                                    {
                                        bridgePairs.Add(new KeyValuePair<Point2d, Point2d>(pA, bestPB));
                                        bridgesForThisDoor++;
                                    }
                                }
                            }
                        }

                        // TRƯỜNG HỢP B: 1 bên chạm đầu Tường/Cột, bên đối diện của Cửa chạm thẳng vào mặt cạnh Cột/Tường vuông góc -> Bắn thẳng tia tới mặt cạnh đó
                        for (int ii = 0; ii < nearSegIndices.Count; ii++)
                        {
                            WallSegment2D s1 = structSegs[nearSegIndices[ii]];
                            Point2d[] ends1 = new Point2d[] { s1.Start, s1.End };
                            Point2d[] others1 = new Point2d[] { s1.End, s1.Start };

                            for (int e1 = 0; e1 < 2; e1++)
                            {
                                Point2d pA = ends1[e1];
                                if (!IsPointNearDoorZone(pA, dz, touchTol)) continue;
                                if (matchedEndpoints.Contains(FormatPtKey(pA))) continue;

                                Vector2d ray = pA - others1[e1];
                                if (ray.Length < 1e-4) continue;
                                ray = ray / ray.Length;

                                double bestT = double.MaxValue;
                                Point2d bestHit = Point2d.Origin;
                                bool foundHit = false;

                                for (int jj = 0; jj < nearSegIndices.Count; jj++)
                                {
                                    if (ii == jj) continue;
                                    WallSegment2D target = structSegs[nearSegIndices[jj]];
                                    if (Math.Abs(ray.DotProduct(target.Dir)) > 0.20) continue; // mặt đối diện gần vuông góc

                                    Vector2d vt = target.End - target.Start;
                                    double denom = ray.X * (-vt.Y) - ray.Y * (-vt.X);
                                    if (Math.Abs(denom) < 1e-6) continue;

                                    Vector2d rhs = target.Start - pA;
                                    double t = (rhs.X * (-vt.Y) - rhs.Y * (-vt.X)) / denom;
                                    double u = (ray.X * rhs.Y - ray.Y * rhs.X) / denom;
                                    if (t < 350.0 || t > 3200.0 || u < -0.04 || u > 1.04 || t >= bestT) continue;

                                    Point2d hitPt = pA + ray * t;
                                    if (!IsPointNearDoorZone(hitPt, dz, touchTol)) continue;
                                    if (!ArePointsAtOppositeOuterDoorSides(pA, hitPt, ray, dz, 130.0)) continue;
                                    if (IsMidpointInsideColumn(pA, hitPt, colBoxes)) continue;

                                    bestT = t;
                                    bestHit = hitPt;
                                    foundHit = true;
                                }

                                if (foundHit && !IsGapAlreadySpanned(pA, bestHit, structSegs, bridgePairs))
                                {
                                    bridgePairs.Add(new KeyValuePair<Point2d, Point2d>(pA, bestHit));
                                    bridgesForThisDoor++;
                                }
                            }
                        }

                        // TRƯỜNG HỢP C: Cửa hành lang/hốc tường kẹp thẳng giữa 2 bức tường/cột chạy song song liên tục hai bên (không có gờ tường nhô ngang)
                        if (bridgesForThisDoor == 0 && dz.ArcHingeCenters.Count > 0)
                        {
                            TryShootCorridorDoorBetweenParallelWalls(dz, nearSegIndices, structSegs, colBoxes, bridgePairs);
                        }
                    }

                    foreach (var bp in bridgePairs)
                    {
                        if (bp.Key.GetDistanceTo(bp.Value) < 5.0) continue;
                        Line tempLn = new Line(new Point3d(bp.Key.X, bp.Key.Y, 0), new Point3d(bp.Value.X, bp.Value.Y, 0));
                        tempLn.SetDatabaseDefaults(db);
                        tempLn.Layer = "AI_XD_TUONG";
                        btr.AppendEntity(tempLn);
                        tr.AddNewlyCreatedDBObject(tempLn, true);
                        createdIds.Add(tempLn.ObjectId);
                    }

                    tr.Commit();
                }
            }
            catch { }

            return createdIds;
        }

        public static void CleanupTemporaryStraightDoorBridges(Document doc, List<ObjectId> tempIds)
        {
            if (doc == null || tempIds == null || tempIds.Count == 0) return;
            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in tempIds)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        try
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                            if (obj != null && !obj.IsErased) obj.Erase();
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        private static void TryShootCorridorDoorBetweenParallelWalls(
            DoorOpeningZone dz, List<int> nearSegIndices, List<WallSegment2D> structSegs,
            List<Extents3d> colBoxes, List<KeyValuePair<Point2d, Point2d>> bridgePairs)
        {
            Point2d hinge = dz.ArcHingeCenters[0];
            // Thử bắn ngang (theo trục X) giữa 2 tường/cột đứng chạm mép trái & mép phải cửa
            double? leftX = null, rightX = null;
            double? botY = null, topY = null;

            for (int ii = 0; ii < nearSegIndices.Count; ii++)
            {
                WallSegment2D ws = structSegs[nearSegIndices[ii]];
                if (Math.Abs(ws.Dir.X) <= 0.15 && Math.Abs(ws.Dir.Y) >= 0.98)
                {
                    double minY = Math.Min(ws.Start.Y, ws.End.Y) - 40.0;
                    double maxY = Math.Max(ws.Start.Y, ws.End.Y) + 40.0;
                    if (hinge.Y >= minY && hinge.Y <= maxY)
                    {
                        double x = 0.5 * (ws.Start.X + ws.End.X);
                        if (Math.Abs(x - dz.MinX) <= 65.0) leftX = x;
                        if (Math.Abs(x - dz.MaxX) <= 65.0) rightX = x;
                    }
                }
                else if (Math.Abs(ws.Dir.Y) <= 0.15 && Math.Abs(ws.Dir.X) >= 0.98)
                {
                    double minX = Math.Min(ws.Start.X, ws.End.X) - 40.0;
                    double maxX = Math.Max(ws.Start.X, ws.End.X) + 40.0;
                    if (hinge.X >= minX && hinge.X <= maxX)
                    {
                        double y = 0.5 * (ws.Start.Y + ws.End.Y);
                        if (Math.Abs(y - dz.MinY) <= 65.0) botY = y;
                        if (Math.Abs(y - dz.MaxY) <= 65.0) topY = y;
                    }
                }
            }

            if (leftX.HasValue && rightX.HasValue && Math.Abs(rightX.Value - leftX.Value) >= 350.0)
            {
                Point2d p1 = new Point2d(leftX.Value, hinge.Y);
                Point2d p2 = new Point2d(rightX.Value, hinge.Y);
                if (!IsMidpointInsideColumn(p1, p2, colBoxes) && !IsGapAlreadySpanned(p1, p2, structSegs, bridgePairs))
                {
                    bridgePairs.Add(new KeyValuePair<Point2d, Point2d>(p1, p2));
                }
            }
            else if (botY.HasValue && topY.HasValue && Math.Abs(topY.Value - botY.Value) >= 350.0)
            {
                Point2d p1 = new Point2d(hinge.X, botY.Value);
                Point2d p2 = new Point2d(hinge.X, topY.Value);
                if (!IsMidpointInsideColumn(p1, p2, colBoxes) && !IsGapAlreadySpanned(p1, p2, structSegs, bridgePairs))
                {
                    bridgePairs.Add(new KeyValuePair<Point2d, Point2d>(p1, p2));
                }
            }
        }

        private static void ExtractArcCentersFromDoorBlock(BlockReference br, Transaction tr, List<Point2d> centers)
        {
            try
            {
                BlockTableRecord btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null) return;
                Matrix3d mat = br.BlockTransform;
                foreach (ObjectId id in btr)
                {
                    Entity sub = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (sub is Arc)
                    {
                        Arc a = (Arc)sub;
                        Point3d wc = a.Center.TransformBy(mat);
                        centers.Add(new Point2d(wc.X, wc.Y));
                    }
                }
            }
            catch { }
        }

        private static void ExtractShortAllowedSegments(
            Entity ent, Extents3d? filterBounds, double minLen, List<WallSegment2D> target)
        {
            if (ent is Line)
            {
                Line ln = (Line)ent;
                Point2d p1 = new Point2d(ln.StartPoint.X, ln.StartPoint.Y);
                Point2d p2 = new Point2d(ln.EndPoint.X, ln.EndPoint.Y);
                if (p1.GetDistanceTo(p2) >= minLen &&
                    (!filterBounds.HasValue || IsPointInExtents(p1, filterBounds.Value) || IsPointInExtents(p2, filterBounds.Value)))
                {
                    target.Add(new WallSegment2D(p1, p2));
                }
            }
            else if (ent is Polyline)
            {
                Polyline pl = (Polyline)ent;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    int next = (i + 1) % pl.NumberOfVertices;
                    if (next == 0 && !pl.Closed) break;
                    Point2d p1 = pl.GetPoint2dAt(i);
                    Point2d p2 = pl.GetPoint2dAt(next);
                    if (p1.GetDistanceTo(p2) >= minLen &&
                        (!filterBounds.HasValue || IsPointInExtents(p1, filterBounds.Value) || IsPointInExtents(p2, filterBounds.Value)))
                    {
                        target.Add(new WallSegment2D(p1, p2));
                    }
                }
            }
            else if (ent is Mline)
            {
                Extract2DSegmentsFromEntity(ent, filterBounds, target);
            }
        }

        private static List<DoorOpeningZone> MergeTouchingDoorZones(List<DoorOpeningZone> items, double tol)
        {
            List<DoorOpeningZone> res = new List<DoorOpeningZone>();
            if (items == null || items.Count == 0) return res;

            bool[] used = new bool[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                DoorOpeningZone cur = new DoorOpeningZone
                {
                    MinX = items[i].MinX,
                    MinY = items[i].MinY,
                    MaxX = items[i].MaxX,
                    MaxY = items[i].MaxY
                };
                cur.ArcHingeCenters.AddRange(items[i].ArcHingeCenters);

                bool expanded = true;
                while (expanded)
                {
                    expanded = false;
                    for (int j = 0; j < items.Count; j++)
                    {
                        if (used[j]) continue;
                        var other = items[j];
                        if (other.MaxX >= cur.MinX - tol && other.MinX <= cur.MaxX + tol &&
                            other.MaxY >= cur.MinY - tol && other.MinY <= cur.MaxY + tol)
                        {
                            used[j] = true;
                            if (other.MinX < cur.MinX) cur.MinX = other.MinX;
                            if (other.MinY < cur.MinY) cur.MinY = other.MinY;
                            if (other.MaxX > cur.MaxX) cur.MaxX = other.MaxX;
                            if (other.MaxY > cur.MaxY) cur.MaxY = other.MaxY;
                            cur.ArcHingeCenters.AddRange(other.ArcHingeCenters);
                            expanded = true;
                        }
                    }
                }
                res.Add(cur);
            }
            return res;
        }

        private static bool IsPointNearDoorZone(Point2d pt, DoorOpeningZone dz, double tol)
        {
            return pt.X >= dz.MinX - tol && pt.X <= dz.MaxX + tol &&
                   pt.Y >= dz.MinY - tol && pt.Y <= dz.MaxY + tol;
        }

        private static bool SegmentIntersectsDoorZone(Point2d p1, Point2d p2, DoorOpeningZone dz, double tol)
        {
            double x0 = Math.Min(p1.X, p2.X), x1 = Math.Max(p1.X, p2.X);
            double y0 = Math.Min(p1.Y, p2.Y), y1 = Math.Max(p1.Y, p2.Y);
            return x1 >= dz.MinX - tol && x0 <= dz.MaxX + tol &&
                   y1 >= dz.MinY - tol && y0 <= dz.MaxY + tol;
        }

        private static bool ArePointsAtOppositeOuterDoorSides(
            Point2d pA, Point2d pB, Vector2d uDir, DoorOpeningZone dz, double sideTol)
        {
            Point2d[] corners = new Point2d[]
            {
                new Point2d(dz.MinX, dz.MinY),
                new Point2d(dz.MaxX, dz.MinY),
                new Point2d(dz.MaxX, dz.MaxY),
                new Point2d(dz.MinX, dz.MaxY)
            };
            double minP = double.MaxValue, maxP = double.MinValue;
            for (int k = 0; k < 4; k++)
            {
                double pr = corners[k].X * uDir.X + corners[k].Y * uDir.Y;
                if (pr < minP) minP = pr;
                if (pr > maxP) maxP = pr;
            }
            double prA = pA.X * uDir.X + pA.Y * uDir.Y;
            double prB = pB.X * uDir.X + pB.Y * uDir.Y;
            return Math.Abs(prA - minP) <= sideTol && Math.Abs(prB - maxP) <= sideTol && (prB - prA) >= (maxP - minP) * 0.55;
        }

        private static bool IsMidpointInsideColumn(Point2d p1, Point2d p2, List<Extents3d> colBoxes)
        {
            Point2d mid = new Point2d((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);
            return IsPointInsideAnyBox(mid, colBoxes, -5.0);
        }

        private static string FormatPtKey(Point2d p)
        {
            return string.Format("{0:F0}_{1:F0}", p.X, p.Y);
        }

        public static bool DrawSingleRoomOnCad(Document doc, CoPilotRoomItem rm)
        {
            if (doc == null || rm == null || rm.Vertices == null || rm.Vertices.Count < 3) return false;
            Database db = doc.Database;
            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    EnsureLayersAndAppId(db, tr);

                    // 1. Tạo LWPOLYLINE khép kín trên layer DUONG_BAO (Color 1 Red)
                    Polyline pline = new Polyline();
                    pline.SetDatabaseDefaults();
                    pline.Layer = LAYER_BOUNDARY;
                    pline.Closed = true;

                    for (int v = 0; v < rm.Vertices.Count; v++)
                    {
                        Point2d pt = new Point2d(rm.Vertices[v].X, rm.Vertices[v].Y);
                        double bulge = (rm.Bulges != null && v < rm.Bulges.Count) ? rm.Bulges[v] : 0.0;
                        pline.AddVertexAt(v, pt, bulge, 0, 0);
                    }

                    btr.AppendEntity(pline);
                    tr.AddNewlyCreatedDBObject(pline, true);

                    // Gán XData nhận diện phòng
                    try
                    {
                        ResultBuffer rb = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, XDATA_APP_NAME),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, rm.Code ?? ""),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, rm.RoomName ?? ""),
                            new TypedValue((int)DxfCode.ExtendedDataReal, rm.AreaM2)
                        );
                        pline.XData = rb;
                        rb.Dispose();
                    }
                    catch { }

                    // 2. Tạo nhãn MText tâm phòng trên layer DUONG_BAO_TEXT (H = 100/200mm, Color 3 Green)
                    double lx = (rm.LabelX != 0.0) ? rm.LabelX : ((rm.CenterX != 0.0) ? rm.CenterX : rm.Vertices[0].X);
                    double ly = (rm.LabelY != 0.0) ? rm.LabelY : ((rm.CenterY != 0.0) ? rm.CenterY : rm.Vertices[0].Y);

                    MText mTxt = new MText();
                    mTxt.SetDatabaseDefaults();
                    mTxt.Layer = LAYER_BOUNDARY_TEXT;
                    mTxt.Location = new Point3d(lx, ly, 0.0);
                    mTxt.Attachment = AttachmentPoint.MiddleCenter;
                    mTxt.TextStyleId = CadLayoutProcessorService.EnsureUnicodeTextStyle(db, tr);

                    string name = !string.IsNullOrEmpty(rm.RoomName) ? rm.RoomName : (!string.IsNullOrEmpty(rm.Code) ? rm.Code : "PHONG");
                    string nu = (name ?? "").ToUpperInvariant();
                    bool isKt = nu.Contains("KT") || nu.Contains("GEN");
                    bool isSmall = rm.IsWc || isKt || nu.Contains("BAN CÔNG") || nu.Contains("LOGIA") || nu.Contains("LÔ GIA");
                    mTxt.TextHeight = isSmall ? 100.0 : 200.0;

                    if (isKt)
                    {
                        mTxt.Contents = name;
                    }
                    else
                    {
                        mTxt.Contents = string.Format("{0}\\PS={1:F2} m²", name, rm.AreaM2);
                    }

                    btr.AppendEntity(mTxt);
                    tr.AddNewlyCreatedDBObject(mTxt, true);

                    tr.Commit();
                }

                // Cập nhật màn hình CAD ngay lập tức để người dùng nhìn thấy đường bao đỏ & text xanh
                doc.Editor.UpdateScreen();
                return true;
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[Live Draw Lỗi] " + ex.Message);
                return false;
            }
        }

        public static CoPilotResult DrawBoundariesOnCad(Document doc, List<CoPilotRoomItem> roomsToDraw = null)
        {
            return DrawBoundariesOnCad(doc, roomsToDraw, null, null);
        }

        public static CoPilotResult DrawBoundariesOnCad(Document doc, List<CoPilotRoomItem> roomsToDraw, TextFormatConfig textConfig, Extents3d? scopeBounds)
        {
            CoPilotResult result = new CoPilotResult();
            if (doc == null)
            {
                result.Message = "Tài liệu CAD hiện hành không hợp lệ.";
                return result;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (textConfig == null) textConfig = new TextFormatConfig();

            List<CoPilotRoomItem> targetRooms = (roomsToDraw != null && roomsToDraw.Count > 0)
                ? roomsToDraw
                : LoadMultiAgentRoomPolygons();

            if (targetRooms == null || targetRooms.Count == 0)
            {
                result.Message = "Chưa có dữ liệu phòng chuẩn xác từ Multi-Agent. Vui lòng chờ AI xử lý xong.";
                ed.WriteMessage("\n[CHỈ HUY TRƯỞNG AI] Chưa có dữ liệu phòng chuẩn xác từ Multi-Agent.");
                return result;
            }

            // KHÓA CỨNG PHẠM VI KHUNG BAO (CORE SPEC): Chỉ giữ các phòng nằm trọn vẹn trong khung bao
            if (scopeBounds.HasValue)
            {
                Extents3d kb = scopeBounds.Value;
                targetRooms = targetRooms.Where(rm => {
                    if (rm.Vertices == null || rm.Vertices.Count == 0) return false;
                    double minX = rm.Vertices.Min(v => v.X);
                    double maxX = rm.Vertices.Max(v => v.X);
                    double minY = rm.Vertices.Min(v => v.Y);
                    double maxY = rm.Vertices.Max(v => v.Y);
                    return minX >= kb.MinPoint.X - 10.0 && maxX <= kb.MaxPoint.X + 10.0 &&
                           minY >= kb.MinPoint.Y - 10.0 && maxY <= kb.MaxPoint.Y + 10.0;
                }).ToList();

                CadLayoutProcessorService.PurgeBoundariesOutsideScope(doc, kb);
            }

            if (targetRooms.Count == 0)
            {
                result.Message = "Không có phòng nào nằm trong phạm vi Khung Bao đã chọn.";
                ed.WriteMessage("\n[CHỈ HUY TRƯỞNG AI] ⚠️ Không có phòng nào nằm trong phạm vi Khung Bao đã chọn.");
                return result;
            }

            // 1. Xóa các đường bao cũ trên CAD



            ClearAiBoundaries(doc);



            // 2. Vẽ trực tiếp các phòng lên CAD



            using (DocumentLock docLock = doc.LockDocument())



            using (Transaction tr = db.TransactionManager.StartTransaction())



            {



                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;



                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;



                EnsureLayersAndAppId(db, tr);
                ObjectId unicodeStyleId = CadLayoutProcessorService.EnsureUnicodeTextStyle(db, tr);

                int drawnCount = 0;

                for (int i = 0; i < targetRooms.Count; i++)

                {



                    CoPilotRoomItem rm = targetRooms[i];



                    if (rm.Vertices == null || rm.Vertices.Count < 3) continue;



                    // A. Tạo LWPOLYLINE khép kín



                    Polyline pline = new Polyline();



                    pline.SetDatabaseDefaults();



                    pline.Layer = LAYER_BOUNDARY;



                    pline.Closed = true;



                    for (int v = 0; v < rm.Vertices.Count; v++)



                    {



                        Point2d pt = new Point2d(rm.Vertices[v].X, rm.Vertices[v].Y);



                        double bulge = (rm.Bulges != null && v < rm.Bulges.Count) ? rm.Bulges[v] : 0.0;



                        pline.AddVertexAt(v, pt, bulge, 0, 0);



                    }



                    btr.AppendEntity(pline);



                    tr.AddNewlyCreatedDBObject(pline, true);



                    // XData



                    ResultBuffer rb = new ResultBuffer(



                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, XDATA_APP_NAME),



                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, rm.Code ?? ""),



                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, rm.RoomName ?? ""),



                        new TypedValue((int)DxfCode.ExtendedDataReal, rm.AreaM2)



                    );



                    pline.XData = rb;



                    rb.Dispose();



                    // B. Tạo nhãn MText
                    double lx = (rm.LabelX != 0.0) ? rm.LabelX : rm.Vertices[0].X;
                    double ly = (rm.LabelY != 0.0) ? rm.LabelY : rm.Vertices[0].Y;

                    MText mTxt = new MText();
                    mTxt.SetDatabaseDefaults();
                    mTxt.Layer = LAYER_BOUNDARY_TEXT;
                    mTxt.Location = new Point3d(lx, ly, 0.0);
                    mTxt.TextStyleId = unicodeStyleId;
                    string roomDisplayName = !string.IsNullOrEmpty(rm.RoomName) ? rm.RoomName : rm.Code;
                    if (string.IsNullOrEmpty(roomDisplayName) || roomDisplayName.StartsWith("PH-", StringComparison.OrdinalIgnoreCase) || roomDisplayName.StartsWith("P-", StringComparison.OrdinalIgnoreCase))
                    {
                        string inferred = CadLayoutProcessorService.InferRoomNameFromCad(db, tr, pline, rm.AreaM2);
                        if (!string.IsNullOrEmpty(inferred))
                        {
                            roomDisplayName = inferred;
                            rm.RoomName = inferred;
                        }
                    }
                    if (roomDisplayName.IndexOf("WC", StringComparison.OrdinalIgnoreCase) >= 0) rm.IsWc = true;

                    mTxt.TextHeight = textConfig.GetHeightForRoom(roomDisplayName, rm.IsWc);
                    mTxt.Attachment = AttachmentPoint.MiddleCenter;
                    mTxt.Contents = textConfig.BuildMTextContent(roomDisplayName, rm.AreaM2, rm.IsWc);

                    btr.AppendEntity(mTxt);
                    tr.AddNewlyCreatedDBObject(mTxt, true);

                    drawnCount++;



                }



                tr.Commit();



            }



            ed.WriteMessage(string.Format("\n⚡ [CHỈ HUY TRƯỞNG AI] Đã vẽ thành công {0} đường bao phòng thông thủy lên CAD!\n", targetRooms.Count));



            result.Success = true;



            result.Rooms = targetRooms;



            result.RoomCount = targetRooms.Count;



            return result;



        }



        private static void EnsureLayersAndAppId(Database db, Transaction tr)



        {



            RegAppTable rat = tr.GetObject(db.RegAppTableId, OpenMode.ForWrite) as RegAppTable;



            if (!rat.Has(XDATA_APP_NAME))



            {



                RegAppTableRecord ratr = new RegAppTableRecord();



                ratr.Name = XDATA_APP_NAME;



                rat.Add(ratr);



                tr.AddNewlyCreatedDBObject(ratr, true);



            }



            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForWrite) as LayerTable;

            if (!lt.Has(LAYER_BOUNDARY))
            {
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = LAYER_BOUNDARY;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                ltr.LineWeight = LineWeight.LineWeight035;
                ltr.IsOff = false;
                ltr.IsLocked = false;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                LayerTableRecord ltr = tr.GetObject(lt[LAYER_BOUNDARY], OpenMode.ForWrite) as LayerTableRecord;
                if (ltr != null)
                {
                    ltr.IsOff = false;
                    ltr.IsLocked = false;
                }
            }

            if (!lt.Has(LAYER_BOUNDARY_TEXT))
            {
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = LAYER_BOUNDARY_TEXT;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                ltr.IsOff = false;
                ltr.IsLocked = false;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                LayerTableRecord ltr = tr.GetObject(lt[LAYER_BOUNDARY_TEXT], OpenMode.ForWrite) as LayerTableRecord;
                if (ltr != null)
                {
                    ltr.IsOff = false;
                    ltr.IsLocked = false;
                }
            }



        }



        public static List<CoPilotRoomItem> LoadMultiAgentRoomPolygons()



        {



            string[] jsonCandidates = new string[]



            {



                @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT\true_room_polygons.json",



                @"D:\01. NGUYEN TAN TAI\MULTI AGENT\true_room_polygons.json",



                @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\true_room_polygons.json"



            };



            string jsonPath = "";
            DateTime latestTime = DateTime.MinValue;
            for (int i = 0; i < jsonCandidates.Length; i++)
            {
                if (File.Exists(jsonCandidates[i]))
                {
                    try
                    {
                        DateTime wt = File.GetLastWriteTime(jsonCandidates[i]);
                        if (wt > latestTime) { latestTime = wt; jsonPath = jsonCandidates[i]; }
                    }
                    catch { if (string.IsNullOrEmpty(jsonPath)) jsonPath = jsonCandidates[i]; }
                }
            }



            if (!string.IsNullOrEmpty(jsonPath))



            {



                try



                {



                    string jsonText = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);



                    JavaScriptSerializer serializer = new JavaScriptSerializer();



                    serializer.MaxJsonLength = int.MaxValue;



                    object[] rawList = serializer.Deserialize<object[]>(jsonText);



                    if (rawList != null && rawList.Length > 0)



                    {



                        List<CoPilotRoomItem> rooms = new List<CoPilotRoomItem>();



                        for (int i = 0; i < rawList.Length; i++)



                        {



                            Dictionary<string, object> dict = rawList[i] as Dictionary<string, object>;



                            if (dict == null) continue;



                            CoPilotRoomItem item = new CoPilotRoomItem();



                            if (dict.ContainsKey("code") && dict["code"] != null && !string.IsNullOrEmpty(dict["code"].ToString()))



                                item.Code = dict["code"].ToString();



                            else



                                item.Code = string.Format("P-{0:D2}", i + 1);



                            if (dict.ContainsKey("name") && dict["name"] != null)



                                item.RoomName = dict["name"].ToString();



                            else



                                item.RoomName = "Phòng " + item.Code;



                            if (dict.ContainsKey("floor") && dict["floor"] != null)



                                item.Floor = dict["floor"].ToString();



                            if (dict.ContainsKey("area_m2") && dict["area_m2"] != null)



                                item.AreaM2 = Convert.ToDouble(dict["area_m2"]);



                            if (dict.ContainsKey("perimeter_m") && dict["perimeter_m"] != null)



                                item.PerimeterM = Convert.ToDouble(dict["perimeter_m"]);



                            if (dict.ContainsKey("is_wc") && dict["is_wc"] != null)



                                item.IsWc = Convert.ToBoolean(dict["is_wc"]);



                            if (dict.ContainsKey("label_x") && dict["label_x"] != null)



                                item.LabelX = Convert.ToDouble(dict["label_x"]);



                            else if (dict.ContainsKey("seed_x") && dict["seed_x"] != null)



                                item.LabelX = Convert.ToDouble(dict["seed_x"]);



                            if (dict.ContainsKey("label_y") && dict["label_y"] != null)



                                item.LabelY = Convert.ToDouble(dict["label_y"]);



                            else if (dict.ContainsKey("seed_y") && dict["seed_y"] != null)



                                item.LabelY = Convert.ToDouble(dict["seed_y"]);



                            if (dict.ContainsKey("vertices") && dict["vertices"] != null)



                            {



                                System.Collections.IEnumerable vList = dict["vertices"] as System.Collections.IEnumerable;



                                if (vList != null)



                                {



                                    foreach (object vObj in vList)



                                    {



                                        Dictionary<string, object> ptDict = vObj as Dictionary<string, object>;

                                        if (ptDict != null)

                                        {

                                            double vx = 0, vy = 0;

                                            bool hasX = false, hasY = false;

                                            if (ptDict.ContainsKey("X")) { vx = Convert.ToDouble(ptDict["X"]); hasX = true; }

                                            else if (ptDict.ContainsKey("x")) { vx = Convert.ToDouble(ptDict["x"]); hasX = true; }



                                            if (ptDict.ContainsKey("Y")) { vy = Convert.ToDouble(ptDict["Y"]); hasY = true; }

                                            else if (ptDict.ContainsKey("y")) { vy = Convert.ToDouble(ptDict["y"]); hasY = true; }



                                            if (hasX && hasY)

                                            {

                                                item.Vertices.Add(new CoPilotPoint2D(vx, vy));

                                            }

                                        }



                                    }



                                }



                            }



                            else if (dict.ContainsKey("coords") && dict["coords"] != null)



                            {



                                System.Collections.IEnumerable coordsList = dict["coords"] as System.Collections.IEnumerable;



                                if (coordsList != null)



                                {



                                    foreach (object ptObj in coordsList)



                                    {



                                        System.Collections.IList ptArr = ptObj as System.Collections.IList;



                                        if (ptArr != null && ptArr.Count >= 2)



                                        {



                                            double x = Convert.ToDouble(ptArr[0]);



                                            double y = Convert.ToDouble(ptArr[1]);



                                            item.Vertices.Add(new CoPilotPoint2D(x, y));



                                        }



                                    }



                                }



                            }



                            if (dict.ContainsKey("bulges") && dict["bulges"] != null)



                            {



                                System.Collections.IEnumerable bList = dict["bulges"] as System.Collections.IEnumerable;



                                if (bList != null)



                                {



                                    foreach (object bObj in bList)



                                    {



                                        double b = Convert.ToDouble(bObj);



                                        item.Bulges.Add(b);



                                        if (Math.Abs(b) > 1e-4) item.HasCurvedWall = true;



                                    }



                                }



                            }



                            if (dict.ContainsKey("center_x") && dict["center_x"] != null)
                                item.CenterX = Convert.ToDouble(dict["center_x"]);
                            if (dict.ContainsKey("center_y") && dict["center_y"] != null)
                                item.CenterY = Convert.ToDouble(dict["center_y"]);

                            if (item.Vertices.Count > 0)
                            {
                                double vMinX = double.MaxValue, vMaxX = double.MinValue;
                                double vMinY = double.MaxValue, vMaxY = double.MinValue;
                                foreach (var pt in item.Vertices)
                                {
                                    if (pt.X < vMinX) vMinX = pt.X;
                                    if (pt.X > vMaxX) vMaxX = pt.X;
                                    if (pt.Y < vMinY) vMinY = pt.Y;
                                    if (pt.Y > vMaxY) vMaxY = pt.Y;
                                }
                                if (Math.Abs(item.CenterX) < 0.001 && Math.Abs(item.CenterY) < 0.001)
                                {
                                    item.CenterX = (vMinX + vMaxX) / 2.0;
                                    item.CenterY = (vMinY + vMaxY) / 2.0;
                                }
                            }

                            if (Math.Abs(item.CenterX) < 0.001 && Math.Abs(item.CenterY) < 0.001)
                            {
                                if (Math.Abs(item.LabelX) > 0.001 || Math.Abs(item.LabelY) > 0.001)
                                {
                                    item.CenterX = item.LabelX;
                                    item.CenterY = item.LabelY;
                                }
                            }

                            string lowerName = (item.RoomName ?? "").ToLowerInvariant();



                            if (lowerName.Contains("wc") || lowerName.Contains("vệ sinh") || lowerName.Contains("toilet") || lowerName.Contains("tắm"))



                            {



                                item.IsWc = true;



                            }



                            if (item.Vertices.Count >= 3)



                            {



                                rooms.Add(item);



                            }



                        }



                        if (rooms.Count > 0) return rooms;



                    }



                }



                catch { }



            }



            string[] lspCandidates = new string[]



            {



                @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT\ai_smart_boundary_live.lsp",



                @"D:\01. NGUYEN TAN TAI\MULTI AGENT\ai_smart_boundary_live.lsp",



                @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT\smart_boundary.lsp",



                @"D:\01. NGUYEN TAN TAI\MULTI AGENT\smart_boundary.lsp"



            };



            for (int l = 0; l < lspCandidates.Length; l++)



            {



                if (File.Exists(lspCandidates[l]))



                {



                    List<CoPilotRoomItem> lspRooms = ParseRoomsFromLsp(lspCandidates[l]);



                    if (lspRooms != null && lspRooms.Count > 0) return lspRooms;



                }



            }



            return new List<CoPilotRoomItem>();



        }



        public static void InsertCadRoomTable(Document doc, List<CoPilotRoomItem> rooms)



        {



            if (doc == null || rooms == null || rooms.Count == 0) return;



            Database db = doc.Database;



            Editor ed = doc.Editor;



            PromptPointResult ppr = ed.GetPoint("\nChọn điểm đặt Bảng Thống Kê Diện Tích Phòng: ");



            if (ppr.Status != PromptStatus.OK) return;



            Point3d insPt = ppr.Value;



            using (DocumentLock docLock = doc.LockDocument())



            using (Transaction tr = db.TransactionManager.StartTransaction())



            {



                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;



                Table tbl = new Table();



                tbl.SetDatabaseDefaults();



                tbl.TableStyle = db.Tablestyle;



                tbl.SetSize(rooms.Count + 3, 5);



                tbl.Position = insPt;



                tbl.Cells[0, 0].TextString = "BẢNG THỐNG KÊ DIỆN TÍCH PHÒNG THÔNG THỦY (AI MULTI-AGENT)";



                tbl.Cells[0, 0].TextHeight = 400.0;



                string[] headers = new string[] { "STT", "Mã Phòng", "Tên Phòng / Khu Vực", "Diện Tích (m²)", "Chu Vi (m)" };



                for (int c = 0; c < 5; c++)



                {



                    tbl.Cells[1, c].TextString = headers[c];



                    tbl.Cells[1, c].TextHeight = 250.0;



                }



                double totArea = 0.0;



                double totPerim = 0.0;



                for (int r = 0; r < rooms.Count; r++)



                {



                    int rowIdx = r + 2;



                    CoPilotRoomItem rm = rooms[r];



                    totArea += rm.AreaM2;



                    totPerim += rm.PerimeterM;



                    tbl.Cells[rowIdx, 0].TextString = (r + 1).ToString();



                    tbl.Cells[rowIdx, 1].TextString = rm.Code ?? "";



                    tbl.Cells[rowIdx, 2].TextString = rm.RoomName ?? "";



                    tbl.Cells[rowIdx, 3].TextString = string.Format("{0:F2}", rm.AreaM2);



                    tbl.Cells[rowIdx, 4].TextString = string.Format("{0:F2}", rm.PerimeterM);



                    for (int c = 0; c < 5; c++)



                    {



                        tbl.Cells[rowIdx, c].TextHeight = 220.0;



                    }



                }



                int summaryRow = rooms.Count + 2;



                tbl.Cells[summaryRow, 0].TextString = "TỔNG CỘNG";



                tbl.Cells[summaryRow, 3].TextString = string.Format("{0:F2}", totArea);



                tbl.Cells[summaryRow, 4].TextString = string.Format("{0:F2}", totPerim);



                for (int c = 0; c < 5; c++)



                {



                    tbl.Cells[summaryRow, c].TextHeight = 250.0;



                }



                tbl.GenerateLayout();



                btr.AppendEntity(tbl);



                tr.AddNewlyCreatedDBObject(tbl, true);



                tr.Commit();



            }



            ed.WriteMessage(string.Format("\n⚡ [CHỈ HUY TRƯỞNG AI] Đã chèn thành công Bảng Thống Kê ({0} phòng, Tổng S = {1:F2} m²)!\n", rooms.Count, rooms.Sum(x => x.AreaM2)));



        }



        private static FileSystemWatcher _multiAgentWatcher1 = null;



        private static FileSystemWatcher _multiAgentWatcher2 = null;



        private static System.Threading.Timer _debounceTimer = null;



        public static event Action<List<CoPilotRoomItem>> OnMultiAgentDataRefreshed;



        public static void StartMultiAgentWatcher()



        {



            if (_multiAgentWatcher1 != null) return;



            string dir1 = @"D:\01. NGUYEN TAN TAI\CODING AI\MULTI AGENT";



            if (Directory.Exists(dir1))



            {



                try



                {



                    _multiAgentWatcher1 = new FileSystemWatcher(dir1);



                    _multiAgentWatcher1.Filter = "*.*";



                    _multiAgentWatcher1.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;



                    _multiAgentWatcher1.Changed += OnMultiAgentFileChanged;



                    _multiAgentWatcher1.Created += OnMultiAgentFileChanged;



                    _multiAgentWatcher1.EnableRaisingEvents = true;



                }



                catch { }



            }



            string dir2 = @"D:\01. NGUYEN TAN TAI\MULTI AGENT";



            if (Directory.Exists(dir2))



            {



                try



                {



                    _multiAgentWatcher2 = new FileSystemWatcher(dir2);



                    _multiAgentWatcher2.Filter = "*.*";



                    _multiAgentWatcher2.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;



                    _multiAgentWatcher2.Changed += OnMultiAgentFileChanged;



                    _multiAgentWatcher2.Created += OnMultiAgentFileChanged;



                    _multiAgentWatcher2.EnableRaisingEvents = true;



                }



                catch { }



            }



        }



        private static void OnMultiAgentFileChanged(object sender, FileSystemEventArgs e)



        {



            string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();



            if (ext != ".json" && ext != ".lsp" && ext != ".jsonl") return;



            if (_debounceTimer != null)



            {



                _debounceTimer.Dispose();



                _debounceTimer = null;



            }



            _debounceTimer = new System.Threading.Timer(state =>



            {



                try



                {



                    List<CoPilotRoomItem> freshRooms = LoadMultiAgentRoomPolygons();



                    if (freshRooms != null && freshRooms.Count > 0)



                    {



                        var handler = OnMultiAgentDataRefreshed;



                        if (handler != null) handler(freshRooms);



                    }



                }



                catch { }



            }, null, 300, System.Threading.Timeout.Infinite);



        }



        #endregion



    }



}




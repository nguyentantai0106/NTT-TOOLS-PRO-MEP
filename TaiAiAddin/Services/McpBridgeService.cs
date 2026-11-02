using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Dịch vụ Cầu Nối MCP Server <-> AutoCAD Add-in (Local REST API Server).
    /// Chạy HttpListener trên cổng 5005, chuyển tiếp các lệnh của AI vào Main STA Thread của AutoCAD an toàn tuyệt đối.
    /// Có cơ chế khóa bảo mật: Mặc định KHÓA, chỉ mở khi người dùng ra lệnh "khởi động MCP server" hoặc click Bật trên Palette.
    /// </summary>
    public static class McpBridgeService
    {
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static bool _isRunning = false;
        private static int _port = 5005;

        // Cờ bảo mật: Mặc định KHÓA (false)
        private static bool _isAuthorized = false;

        public static bool IsAuthorized
        {
            get { return _isAuthorized; }
            set
            {
                if (_isAuthorized != value)
                {
                    _isAuthorized = value;
                    try
                    {
                        if (OnAuthorizationChanged != null)
                        {
                            OnAuthorizationChanged(_isAuthorized);
                        }
                    }
                    catch { }
                }
            }
        }

        public static bool IsRunning
        {
            get { return _isRunning; }
        }

        public static int Port
        {
            get { return _port; }
        }

        public static event Action<bool> OnAuthorizationChanged;

        private class QueuedTask
        {
            public Func<object> Action;
            public TaskCompletionSource<object> Tcs;
        }

        private static readonly ConcurrentQueue<QueuedTask> _taskQueue = new ConcurrentQueue<QueuedTask>();
        private static readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer();

        static McpBridgeService()
        {
            _jsonSerializer.MaxJsonLength = int.MaxValue;
        }

        /// <summary>
        /// Khởi chạy HTTP Server lắng nghe lệnh từ MCP Server
        /// </summary>
        public static void StartServer(int port = 5005)
        {
            if (_isRunning) return;

            try
            {
                _port = port;
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://localhost:{0}/api/", _port));
                _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/api/", _port));
                _listener.Start();

                _isRunning = true;
                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "TaiAi_McpListenerThread"
                };
                _listenerThread.Start();

                // Đăng ký sự kiện Idle của AutoCAD để tiêu thụ hàng đợi trên Main STA Thread
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnAutoCadIdle;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                try
                {
                    Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (doc != null && doc.Editor != null)
                    {
                        doc.Editor.WriteMessage(string.Format("\n[NTT MCP] Không thể khởi động HTTP Server trên port {0}: {1}", _port, ex.Message));
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Dừng HTTP Server khi Addin unload
        /// </summary>
        public static void StopServer()
        {
            _isRunning = false;
            _isAuthorized = false;

            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnAutoCadIdle;
            }
            catch { }

            try
            {
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }

            _listener = null;
        }

        private static void ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(state => HandleRequestAsync((HttpListenerContext)state));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Tiếp tục vòng lặp
                }
            }
        }

        private static async void HandleRequestAsync(HttpListenerContext context)
        {
            if (context == null) return;

            try
            {
                HttpListenerRequest req = context.Request;
                HttpListenerResponse res = context.Response;

                if (req == null || res == null) return;

                // Header CORS để các client web/scripts gọi mượt mà
                try
                {
                    res.Headers.Add("Access-Control-Allow-Origin", "*");
                    res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
                }
                catch { }

                if (req.HttpMethod == "OPTIONS")
                {
                    res.StatusCode = 200;
                    res.Close();
                    return;
                }

                string rawUrl = req.RawUrl != null ? req.RawUrl.ToLowerInvariant() : "";

                // 1. ENDPOINT: GET /api/status (Kiểm tra trạng thái)
                if (req.HttpMethod == "GET" && (rawUrl == "/api/status" || rawUrl == "/api/status/"))
                {
                    string docName = null;
                    try
                    {
                        Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                        if (doc != null) docName = doc.Name;
                    }
                    catch { }

                    Dictionary<string, object> statusObj = new Dictionary<string, object>();
                    statusObj.Add("success", true);
                    statusObj.Add("isRunning", _isRunning);
                    statusObj.Add("isAuthorized", _isAuthorized);
                    statusObj.Add("port", _port);
                    statusObj.Add("activeDocument", docName);
                    statusObj.Add("message", _isAuthorized ? "Sẵn sàng nhận lệnh từ AI." : "Đang KHÓA. Cần ra lệnh 'khởi động MCP server' để cấp quyền.");

                    SendJsonResponse(res, 200, statusObj);
                    return;
                }

                // 2. ENDPOINT: POST /api/auth/start (Cấp quyền kết nối khi người dùng ra lệnh)
                if (req.HttpMethod == "POST" && rawUrl.StartsWith("/api/auth/start"))
                {
                    IsAuthorized = true;
                    WriteCadMessage("\n[NTT MCP] >>> ĐÃ CẤP QUYỀN ĐIỀU KHIỂN AI (MCP Remote: ACTIVE) <<<\n");

                    Dictionary<string, object> authObj = new Dictionary<string, object>();
                    authObj.Add("success", true);
                    authObj.Add("isAuthorized", true);
                    authObj.Add("message", "Đã khởi động phiên MCP và cấp quyền điều khiển AutoCAD cho AI.");
                    SendJsonResponse(res, 200, authObj);
                    return;
                }

                // 3. ENDPOINT: POST /api/auth/stop (Thu hồi quyền kết nối)
                if (req.HttpMethod == "POST" && rawUrl.StartsWith("/api/auth/stop"))
                {
                    IsAuthorized = false;
                    WriteCadMessage("\n[NTT MCP] >>> ĐÃ KHÓA QUYỀN ĐIỀU KHIỂN AI (MCP Remote: LOCKED) <<<\n");

                    Dictionary<string, object> authObj = new Dictionary<string, object>();
                    authObj.Add("success", true);
                    authObj.Add("isAuthorized", false);
                    authObj.Add("message", "Đã khóa quyền điều khiển MCP. AI không thể can thiệp vào bản vẽ.");
                    SendJsonResponse(res, 200, authObj);
                    return;
                }

                // 4. KIỂM TRA RÀNG BUỘC BẢO MẬT: Bắt buộc đã Authorized mới cho chạy các Action
                if (!_isAuthorized)
                {
                    Dictionary<string, object> deniedObj = new Dictionary<string, object>();
                    deniedObj.Add("success", false);
                    deniedObj.Add("isAuthorized", false);
                    deniedObj.Add("error", "TRUY CẬP BỊ TỪ CHỐI: Remote Control chưa được cấp quyền. Vui lòng yêu cầu người dùng ra lệnh 'khởi động MCP server' trước khi thực thi.");
                    SendJsonResponse(res, 403, deniedObj);
                    return;
                }

                // 5. ENDPOINT: POST /api/action (Thực thi các lệnh CAD)
                if (req.HttpMethod == "POST" && (rawUrl == "/api/action" || rawUrl == "/api/action/"))
                {
                    string body = "";
                    using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    {
                        body = await reader.ReadToEndAsync();
                    }

                    Dictionary<string, object> requestDict = null;
                    try
                    {
                        requestDict = _jsonSerializer.Deserialize<Dictionary<string, object>>(body);
                    }
                    catch { }

                    string action = requestDict != null && requestDict.ContainsKey("action") ? requestDict["action"].ToString() : "";
                    Dictionary<string, object> parameters = null;
                    if (requestDict != null && requestDict.ContainsKey("params"))
                    {
                        parameters = requestDict["params"] as Dictionary<string, object>;
                    }

                    // Đẩy task vào hàng đợi Main STA Thread của AutoCAD
                    TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                    _taskQueue.Enqueue(new QueuedTask
                    {
                        Action = () => ExecuteCadAction(action, parameters),
                        Tcs = tcs
                    });

                    // Chờ hoàn thành với timeout an toàn 45s
                    Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(45000));
                    if (completedTask == tcs.Task)
                    {
                        object result = await tcs.Task;
                        SendJsonResponse(res, 200, result);
                    }
                    else
                    {
                        Dictionary<string, object> err = new Dictionary<string, object>();
                        err.Add("success", false);
                        err.Add("error", "AutoCAD phản hồi quá lâu (Timeout 45s). Có thể AutoCAD đang mở hộp thoại Modal hoặc đang trong lệnh Jig.");
                        SendJsonResponse(res, 504, err);
                    }
                    return;
                }

                // Endpoint không hợp lệ
                Dictionary<string, object> notFound = new Dictionary<string, object>();
                notFound.Add("success", false);
                notFound.Add("error", "Endpoint không tồn tại.");
                SendJsonResponse(res, 404, notFound);
            }
            catch (Exception ex)
            {
                try
                {
                    if (context != null && context.Response != null)
                    {
                        Dictionary<string, object> err500 = new Dictionary<string, object>();
                        err500.Add("success", false);
                        err500.Add("error", ex.Message);
                        SendJsonResponse(context.Response, 500, err500);
                    }
                }
                catch { }
            }
        }

        private static void OnAutoCadIdle(object sender, EventArgs e)
        {
            // Xử lý các task tồn đọng trên Main STA Thread
            try
            {
                QueuedTask task;
                while (_taskQueue != null && _taskQueue.TryDequeue(out task))
                {
                    if (task == null || task.Action == null) continue;

                    try
                    {
                        Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                        {
                            using (doc.LockDocument())
                            {
                                object res = task.Action();
                                if (task.Tcs != null) task.Tcs.TrySetResult(res);
                            }
                        }
                        else
                        {
                            object res = task.Action();
                            if (task.Tcs != null) task.Tcs.TrySetResult(res);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (task.Tcs != null) task.Tcs.TrySetException(ex);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Thực thi các Action cụ thể trong luồng Main STA Thread với DocumentLock
        /// </summary>
        private static object ExecuteCadAction(string action, Dictionary<string, object> p)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Dictionary<string, object> noDoc = new Dictionary<string, object>();
                noDoc.Add("success", false);
                noDoc.Add("error", "Không có bản vẽ nào đang mở.");
                return noDoc;
            }

            Editor ed = doc.Editor;

            switch (action.ToLowerInvariant())
            {
                case "get_drawing_info":
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                        LayerTable lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);

                        int layerCount = 0;
                        foreach (ObjectId lid in lt) layerCount++;

                        int blockCount = 0;
                        foreach (ObjectId bid in bt) blockCount++;

                        Dictionary<string, object> info = new Dictionary<string, object>();
                        info.Add("success", true);
                        info.Add("drawingName", doc.Name);
                        info.Add("layerCount", layerCount);
                        info.Add("blockCount", blockCount);
                        info.Add("ucsOrigin", string.Format("{0:F2}, {1:F2}, {2:F2}", ed.CurrentUserCoordinateSystem.Translation.X, ed.CurrentUserCoordinateSystem.Translation.Y, ed.CurrentUserCoordinateSystem.Translation.Z));
                        return info;
                    }
                }

                case "analyze_dwg":
                {
                    DwgAnalyzerService.AnalysisResult analysis = DwgAnalyzerService.AnalyzeDatabase(doc.Database, doc.Name);
                    Dictionary<string, object> res = new Dictionary<string, object>();
                    res.Add("success", true);
                    res.Add("drawingName", doc.Name);
                    res.Add("totalEntities", analysis.TotalEntities);
                    res.Add("layerCount", analysis.Layers != null ? analysis.Layers.Count : 0);
                    res.Add("blockCount", analysis.BlockDefinitions != null ? analysis.BlockDefinitions.Count : 0);
                    res.Add("blockRefCount", analysis.BlockReferences != null ? analysis.BlockReferences.Count : 0);
                    res.Add("textCount", analysis.Texts != null ? analysis.Texts.Count : 0);
                    return res;
                }

                case "draw_sample_sld":
                {
                    ElectricalPanel panel = new ElectricalPanel
                    {
                        PanelName = (p != null && p.ContainsKey("panelName")) ? p["panelName"].ToString() : "MSB-MCP-AI",
                        DefaultCableType = "Cu/XLPE/PVC",
                        Incomer = IncomerType.DualATS,
                        HasCapacitorBank = true,
                        MainBreakerSpec = "MCCB 3P-36kA 400A"
                    };

                    panel.Loads.Add(new ElectricalLoad { Stt = 1, LoadName = "TỦ PHÂN PHỐI TẦNG 1", PowerKW = 45.0, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, BreakerSpec = "MCCB 3P-25kA 100A", CableLength = 30.0 });
                    panel.Loads.Add(new ElectricalLoad { Stt = 2, LoadName = "TỦ ĐIỀU HÒA CHILLER", PowerKW = 75.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, BreakerSpec = "MCCB 3P-36kA 160A", CableLength = 25.0 });
                    panel.Loads.Add(new ElectricalLoad { Stt = 3, LoadName = "BƠM NƯỚC SINH HOẠT", PowerKW = 15.0, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, BreakerSpec = "MCCB 3P-25kA 40A", CableLength = 20.0 });
                    panel.Loads.Add(new ElectricalLoad { Stt = 4, LoadName = "CHIẾU SÁNG & KHẨN CẤP", PowerKW = 6.0, Phase = PhaseType.SinglePhase, AssignedPhase = "R", BreakerSpec = "MCB 1P-6kA 32A", CableLength = 15.0 });

                    ElectricalCalculator calc = new ElectricalCalculator();
                    calc.CalculatePanel(panel);

                    DrawingParameters dp = new DrawingParameters
                    {
                        AutoFitSpacing = true,
                        IncludeTitleBlock = true,
                        IncludeLegend = true,
                        DrawFrontView = true,
                        ProjectName = "DỰ ÁN ĐIỀU KHIỂN TỰ ĐỘNG QUA AI (MCP)",
                        DrawingTitle = "SƠ ĐỒ NGUYÊN LÝ TỦ " + panel.PanelName,
                        DrawnBy = "AI ANTIGRAVITY",
                        CheckedBy = "NGUYỄN TẤN TÀI",
                        DrawingNumber = "MCP-EL-01"
                    };
                    dp.AutoFitForPanel(panel);

                    Point3d insertPt = new Point3d(0, 0, 0);
                    if (p != null && p.ContainsKey("insertX") && p.ContainsKey("insertY"))
                    {
                        insertPt = new Point3d(Convert.ToDouble(p["insertX"]), Convert.ToDouble(p["insertY"]), 0);
                    }

                    ElectricalDrawingService drawingService = new ElectricalDrawingService();
                    drawingService.DrawSingleLineDiagram(doc, panel, insertPt, dp);

                    ed.WriteMessage(string.Format("\n[NTT MCP] Đã vẽ thành công sơ đồ tủ {0} tại tọa độ ({1:F0}, {2:F0})!\n", panel.PanelName, insertPt.X, insertPt.Y));

                    Dictionary<string, object> sldRes = new Dictionary<string, object>();
                    sldRes.Add("success", true);
                    sldRes.Add("panelName", panel.PanelName);
                    sldRes.Add("totalLoadCount", panel.Loads.Count);
                    sldRes.Add("totalPowerKw", panel.TotalInstalledPowerKW);
                    sldRes.Add("mainBreaker", panel.MainBreakerSpec);
                    sldRes.Add("insertPoint", string.Format("{0:F0}, {1:F0}", insertPt.X, insertPt.Y));
                    return sldRes;
                }

                case "draw_cable_tray":
                {
                    double width = 300;
                    double height = 100;
                    string typeStr = "Trunking";
                    bool insertSupports = true;

                    if (p != null)
                    {
                        if (p.ContainsKey("width")) width = Convert.ToDouble(p["width"]);
                        if (p.ContainsKey("height")) height = Convert.ToDouble(p["height"]);
                        if (p.ContainsKey("type")) typeStr = p["type"].ToString();
                        if (p.ContainsKey("insertSupports")) insertSupports = Convert.ToBoolean(p["insertSupports"]);
                    }

                    CableTrayType trayType = CableTrayType.Trunking;
                    if (typeStr.Equals("Ladder", StringComparison.OrdinalIgnoreCase)) trayType = CableTrayType.Ladder;
                    else if (typeStr.Equals("Tray", StringComparison.OrdinalIgnoreCase)) trayType = CableTrayType.Tray;

                    List<Point3d> pts = new List<Point3d>();
                    if (p != null && p.ContainsKey("points"))
                    {
                        ArrayList ptList = p["points"] as ArrayList;
                        if (ptList != null)
                        {
                            foreach (object item in ptList)
                            {
                                ArrayList coords = item as ArrayList;
                                if (coords != null && coords.Count >= 2)
                                {
                                    double x = Convert.ToDouble(coords[0]);
                                    double y = Convert.ToDouble(coords[1]);
                                    double z = coords.Count >= 3 ? Convert.ToDouble(coords[2]) : 0.0;
                                    pts.Add(new Point3d(x, y, z));
                                }
                            }
                        }
                    }

                    if (pts.Count < 2)
                    {
                        pts.Add(new Point3d(0, 0, 0));
                        pts.Add(new Point3d(3000, 0, 0));
                        pts.Add(new Point3d(4500, 1500, 0));
                        pts.Add(new Point3d(4500, 4000, 0));
                    }

                    CableTrayParams ctp = new CableTrayParams
                    {
                        Type = trayType,
                        WidthMm = width,
                        HeightMm = height,
                        SupportSpacingM = 1.5,
                        InsertSupports = insertSupports,
                        AddAnnotations = true
                    };

                    CableTrayRouteResult res = CableTrayService.RouteCableTrayAlongPoints(doc, pts, ctp);
                    if (res != null)
                    {
                        Dictionary<string, object> ctRes = new Dictionary<string, object>();
                        ctRes.Add("success", true);
                        ctRes.Add("totalLengthM", res.TotalLengthM);
                        ctRes.Add("elbowCount", res.ElbowCount);
                        ctRes.Add("segmentCount", res.SegmentCount);
                        ctRes.Add("supportCount", res.SupportCount);
                        ctRes.Add("weightKg", res.SheetMetalWeightKg);
                        return ctRes;
                    }
                    else
                    {
                        Dictionary<string, object> errRes = new Dictionary<string, object>();
                        errRes.Add("success", false);
                        errRes.Add("error", "Không thể sinh tuyến thang máng cáp.");
                        return errRes;
                    }
                }

                case "smart_boundary":
                {
                    RoomBoundaryOptions opts = new RoomBoundaryOptions();
                    RoomBoundaryResult res = SmartRoomBoundaryService.ExecuteRoomBoundary(doc, opts);
                    if (res != null && res.Success)
                    {
                        Dictionary<string, object> bRes = new Dictionary<string, object>();
                        bRes.Add("success", true);
                        bRes.Add("roomCount", res.RoomCount);
                        bRes.Add("totalAreaM2", res.TotalAreaM2);
                        bRes.Add("message", "Đã quét và bo viền phòng thành công!");
                        return bRes;
                    }
                    else
                    {
                        Dictionary<string, object> errBRes = new Dictionary<string, object>();
                        errBRes.Add("success", false);
                        errBRes.Add("error", res != null ? res.Message : "Lỗi khi xử lý Smart Boundary.");
                        return errBRes;
                    }
                }

                case "run_command":
                {
                    if (p != null && p.ContainsKey("command"))
                    {
                        string cmd = p["command"].ToString();
                        doc.SendStringToExecute(cmd + "\n", true, false, false);

                        Dictionary<string, object> cmdRes = new Dictionary<string, object>();
                        cmdRes.Add("success", true);
                        cmdRes.Add("commandSent", cmd);
                        cmdRes.Add("message", "Đã gửi lệnh vào dòng lệnh AutoCAD.");
                        return cmdRes;
                    }
                    Dictionary<string, object> missCmd = new Dictionary<string, object>();
                    missCmd.Add("success", false);
                    missCmd.Add("error", "Thiếu tham số 'command'.");
                    return missCmd;
                }

                default:
                {
                    Dictionary<string, object> unk = new Dictionary<string, object>();
                    unk.Add("success", false);
                    unk.Add("error", string.Format("Action '{0}' chưa được hỗ trợ.", action));
                    return unk;
                }
            }
        }

        private static void SendJsonResponse(HttpListenerResponse res, int statusCode, object data)
        {
            try
            {
                if (res == null) return;
                res.StatusCode = statusCode;
                res.ContentType = "application/json; charset=utf-8";

                string json = _jsonSerializer.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.Close();
            }
            catch { }
        }

        private static void WriteCadMessage(string msg)
        {
            try
            {
                _taskQueue.Enqueue(new QueuedTask
                {
                    Action = () =>
                    {
                        try
                        {
                            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                            if (doc != null && doc.Editor != null)
                            {
                                doc.Editor.WriteMessage(msg);
                            }
                        }
                        catch { }
                        return null;
                    },
                    Tcs = new TaskCompletionSource<object>()
                });
            }
            catch { }
        }
    }
}

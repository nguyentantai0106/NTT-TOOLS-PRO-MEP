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
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;

namespace TaiAiAddin.Services
{
    /// <summary>
    /// Cầu nối HTTP Local Bridge giữa SLD Studio Web và AutoCAD Add-in.
    /// Lắng nghe trên cổng 19850 (http://127.0.0.1:19850/sld/), điều phối lệnh vẽ
    /// Sơ đồ nguyên lý về Main STA Thread của AutoCAD an toàn tuyệt đối.
    /// </summary>
    public static class SldLocalBridgeService
    {
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static volatile bool _isRunning;
        private static int _port = 19850;
        private static volatile string _cachedDocName = "Chưa mở bản vẽ";

        private class QueuedTask
        {
            public Func<object> Action;
            public TaskCompletionSource<object> Tcs;
        }

        private static readonly ConcurrentQueue<QueuedTask> _taskQueue = new ConcurrentQueue<QueuedTask>();
        private static readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer();

        static SldLocalBridgeService()
        {
            _jsonSerializer.MaxJsonLength = int.MaxValue;
        }

        public static bool IsRunning { get { return _isRunning; } }

        public static void StartServer(int port = 19850)
        {
            if (_isRunning) return;

            try
            {
                _port = port;
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/sld/", _port));
                _listener.Prefixes.Add(string.Format("http://localhost:{0}/sld/", _port));
                _listener.Start();

                _isRunning = true;
                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "TaiAi_SldBridgeThread"
                };
                _listenerThread.Start();

                try
                {
                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc != null) _cachedDocName = doc.Name;
                    Application.DocumentManager.DocumentActivated += OnDocumentActivated;
                }
                catch { }

                Application.Idle += OnAutoCadIdle;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SldLocalBridge] StartServer error: " + ex.Message);
            }
        }

        public static void StopServer()
        {
            _isRunning = false;
            try { Application.Idle -= OnAutoCadIdle; } catch { }
            try { Application.DocumentManager.DocumentActivated -= OnDocumentActivated; } catch { }
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                    _listener = null;
                }
            }
            catch { }
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            try
            {
                _cachedDocName = (e.Document != null) ? e.Document.Name : "Chưa mở bản vẽ";
            }
            catch { }
        }

        private static void OnAutoCadIdle(object sender, EventArgs e)
        {
            QueuedTask task;
            while (_taskQueue.TryDequeue(out task))
            {
                try
                {
                    object result = task.Action();
                    task.Tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    task.Tcs.TrySetException(ex);
                }
            }
        }

        private static object RunOnMainThread(Func<object> func, int timeoutMs = 120000)
        {
            QueuedTask task = new QueuedTask
            {
                Action = func,
                Tcs = new TaskCompletionSource<object>()
            };
            _taskQueue.Enqueue(task);

            if (!task.Tcs.Task.Wait(timeoutMs))
            {
                throw new TimeoutException("Hết thời gian chờ AutoCAD phản hồi (AutoCAD có thể đang mở hộp thoại hoặc bận lệnh khác).");
            }
            return task.Tcs.Task.Result;
        }

        private static void ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;

            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            try
            {
                string path = (req.Url != null ? req.Url.AbsolutePath : "").TrimEnd('/').ToLowerInvariant();
                if (req.HttpMethod == "GET" && path == "/sld/health")
                {
                    WriteJson(res, 200, new
                    {
                        connected = true,
                        docName = _cachedDocName,
                        port = _port,
                        version = "1.0"
                    });
                    return;
                }

                if (req.HttpMethod == "POST" && path == "/sld/draw")
                {
                    string body = "";
                    using (StreamReader reader = new StreamReader(req.InputStream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }

                    Dictionary<string, object> payload = _jsonSerializer.Deserialize<Dictionary<string, object>>(body);
                    object result = RunOnMainThread(() => ExecuteDrawSld(payload));
                    WriteJson(res, 200, result);
                    return;
                }

                WriteJson(res, 404, new { success = false, message = "Endpoint không tồn tại." });
            }
            catch (Exception ex)
            {
                Exception inner = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                WriteJson(res, 500, new { success = false, message = inner.Message });
            }
        }

        private static object ExecuteDrawSld(Dictionary<string, object> payload)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new { success = false, message = "Không tìm thấy bản vẽ AutoCAD nào đang mở!" };
            }

            Dictionary<string, object> panelDict = GetDict(payload, "panel");
            Dictionary<string, object> paramsDict = GetDict(payload, "params");
            bool pickPoint = GetBool(payload, "pickPoint", true);
            Dictionary<string, object> originDict = GetDict(payload, "origin");

            ElectricalPanel panel = ParsePanel(panelDict);
            DrawingParameters dp = ParseDrawingParams(paramsDict);

            Point3d origin = new Point3d(
                GetDouble(originDict, "x", 0.0),
                GetDouble(originDict, "y", 0.0),
                0.0);

            Editor ed = doc.Editor;
            if (pickPoint)
            {
                try
                {
                    Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                }
                catch { }

                PromptPointOptions opts = new PromptPointOptions(
                    string.Format("\n[SLD STUDIO] Chọn điểm đặt Sơ đồ nguyên lý tủ '{0}': ", panel.PanelName));
                PromptPointResult ppr = ed.GetPoint(opts);
                if (ppr.Status != PromptStatus.OK)
                {
                    return new { success = false, cancelled = true, message = "Kỹ sư đã hủy chọn điểm đặt trên CAD." };
                }
                origin = ppr.Value;
            }

            ElectricalDrawingService svc = new ElectricalDrawingService();
            bool ok = svc.DrawSingleLineDiagram(doc, panel, origin, dp);
            if (ok)
            {
                panel.DrawingParams = dp.Clone();
                PanelStorageService.AddOrUpdatePanel(panel);
                return new
                {
                    success = true,
                    message = string.Format("Đã vẽ sơ đồ tủ '{0}' vào bản vẽ '{1}' thành công!", panel.PanelName, Path.GetFileName(doc.Name))
                };
            }

            return new { success = false, message = "Không thể vẽ sơ đồ vào AutoCAD. Vui lòng kiểm tra dòng lệnh CAD." };
        }

        private static ElectricalPanel ParsePanel(Dictionary<string, object> d)
        {
            ElectricalPanel panel = new ElectricalPanel();
            if (d == null) return panel;

            panel.PanelName = GetString(d, "PanelName", "DB-01");
            panel.ParentPanelName = GetString(d, "ParentPanelName", "MSB");
            panel.VoltageV = GetDouble(d, "VoltageV", 380.0);
            panel.PowerFactor = GetDouble(d, "PowerFactor", 0.85);
            panel.SimultaneousFactor = GetDouble(d, "SimultaneousFactor", 0.85);
            panel.UsageFactor = GetDouble(d, "UsageFactor", 1.0);
            panel.TotalInstalledPowerKW = GetDouble(d, "TotalInstalledPowerKW", 0.0);
            panel.TotalCalculatedPowerKW = GetDouble(d, "TotalCalculatedPowerKW", 0.0);
            panel.TotalCurrentA = GetDouble(d, "TotalCurrentA", 0.0);
            panel.MainBreakerSpec = GetString(d, "MainBreakerSpec", "MCCB 3P-100A-36kA");
            panel.MainCableSpec = GetString(d, "MainCableSpec", "");
            panel.DefaultCableType = GetString(d, "DefaultCableType", "Cu/XLPE/PVC");
            panel.PhasePowerR = GetDouble(d, "PhasePowerR", 0.0);
            panel.PhasePowerY = GetDouble(d, "PhasePowerY", 0.0);
            panel.PhasePowerB = GetDouble(d, "PhasePowerB", 0.0);
            panel.Incomer = ParseEnum(d, "Incomer", IncomerType.SingleNormal);
            panel.BackupSourceTitle = GetString(d, "BackupSourceTitle", "MÁY PHÁT");
            panel.HasCapacitorBank = GetBool(d, "HasCapacitorBank", false);
            panel.TargetCosPhi = GetDouble(d, "TargetCosPhi", 0.95);
            panel.CapacitorBankKvar = GetDouble(d, "CapacitorBankKvar", 0.0);
            panel.CapacitorSteps = GetInt(d, "CapacitorSteps", 4);
            panel.TransformerKVA = GetDouble(d, "TransformerKVA", 0.0);
            panel.ShortCircuitKA = GetDouble(d, "ShortCircuitKA", 10.0);

            panel.Loads = new List<ElectricalLoad>();
            object loadsObj;
            if (d.TryGetValue("Loads", out loadsObj) && loadsObj is IEnumerable)
            {
                int idx = 1;
                foreach (object item in (IEnumerable)loadsObj)
                {
                    Dictionary<string, object> ld = item as Dictionary<string, object>;
                    if (ld == null) continue;

                    ElectricalLoad load = new ElectricalLoad();
                    load.Stt = GetInt(ld, "Stt", idx);
                    load.CircuitNumber = GetString(ld, "CircuitNumber", "C" + idx);
                    load.LoadName = GetString(ld, "LoadName", "Phụ tải " + idx);
                    load.PowerKW = GetDouble(ld, "PowerKW", 1.0);
                    load.Phase = ParseEnum(ld, "Phase", PhaseType.ThreePhase);
                    load.AssignedPhase = GetString(ld, "AssignedPhase", "R-Y-B");
                    load.PowerR = GetDouble(ld, "PowerR", 0.0);
                    load.PowerY = GetDouble(ld, "PowerY", 0.0);
                    load.PowerB = GetDouble(ld, "PowerB", 0.0);
                    load.CurrentA = GetDouble(ld, "CurrentA", 0.0);
                    load.BreakerType = GetString(ld, "BreakerType", "MCB");
                    load.BreakerSpec = GetString(ld, "BreakerSpec", "");
                    load.CableSpec = GetString(ld, "CableSpec", "");
                    load.GroundWireSpec = GetString(ld, "GroundWireSpec", "");
                    load.CableType = GetString(ld, "CableType", "Cu/XLPE/PVC");
                    load.ConductorSize = GetString(ld, "ConductorSize", "2.5");
                    load.GroundSize = GetString(ld, "GroundSize", "2.5");
                    load.Starter = ParseEnum(ld, "Starter", StarterType.Direct);
                    load.Control = ParseEnum(ld, "Control", ControlMode.None);
                    load.IsControlGrouped = GetBool(ld, "IsControlGrouped", false);
                    load.CableLength = GetDouble(ld, "CableLength", 25.0);
                    load.VoltageDropPercent = GetDouble(ld, "VoltageDropPercent", 0.0);
                    load.IsVoltageDropExceeded = GetBool(ld, "IsVoltageDropExceeded", false);
                    load.LoadType = GetString(ld, "LoadType", "Động cơ");
                    load.IsSpare = GetBool(ld, "IsSpare", false);
                    load.DrawOutgoingWire = GetBool(ld, "DrawOutgoingWire", true);
                    panel.Loads.Add(load);
                    idx++;
                }
            }

            return panel;
        }

        private static DrawingParameters ParseDrawingParams(Dictionary<string, object> d)
        {
            DrawingParameters dp = new DrawingParameters();
            if (d == null) return dp;

            dp.LayoutScheme = ParseEnum(d, "LayoutScheme", LayoutScheme.LayoutBasic);
            dp.TreeOrientation = ParseEnum(d, "TreeOrientation", TreeOrientation.VerticalTopDown);
            dp.SymbolRegion = ParseEnum(d, "SymbolRegion", SymbolRegion.South_Iec);
            dp.DiagramType = (dp.LayoutScheme == LayoutScheme.LayoutBasic)
                ? SldDiagramType.StandardSchedule
                : SldDiagramType.TreeSinglePanel;

            dp.IncludeFuse = GetBool(d, "IncludeFuse", true);
            dp.IncludeLamps = GetBool(d, "IncludeLamps", true);
            dp.IncludeRelay = GetBool(d, "IncludeRelay", true);
            dp.IncludeMct = GetBool(d, "IncludeMct", true);
            dp.IncludeSpd = GetBool(d, "IncludeSpd", true);
            dp.IncludeSpare = GetBool(d, "IncludeSpare", false);
            dp.SpareCount3P = GetInt(d, "SpareCount3P", 1);
            dp.SpareCount1P = GetInt(d, "SpareCount1P", 0);
            dp.SpareCount = dp.SpareCount3P + dp.SpareCount1P;
            dp.DrawSpareOutgoingWire = GetBool(d, "DrawSpareOutgoingWire", false);

            dp.ColWidth = GetDouble(d, "ColWidth", 26.0);
            dp.RowCableHeight = GetDouble(d, "RowCableHeight", 90.0);
            dp.RowNameHeight = GetDouble(d, "RowNameHeight", 65.0);
            dp.RowPowerHeight = GetDouble(d, "RowPowerHeight", 36.0);
            dp.SplitCableTwoLines = GetBool(d, "SplitCableTwoLines", true);
            dp.AutoFitSpacing = GetBool(d, "AutoFitSpacing", true);
            dp.IncludeTitleBlock = GetBool(d, "IncludeTitleBlock", false);
            dp.IncludeLegend = GetBool(d, "IncludeLegend", false);
            dp.DrawFrontView = GetBool(d, "DrawFrontView", false);
            dp.PaperSize = GetString(d, "PaperSize", "A3");
            dp.ProjectName = GetString(d, "ProjectName", "");
            dp.DesignerName = GetString(d, "DesignerName", "");
            dp.DrawingTitle = GetString(d, "DrawingTitle", "");
            dp.DrawingNumber = GetString(d, "DrawingNumber", "EL-01");

            return dp;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            object val;
            return (d != null && d.TryGetValue(key, out val)) ? val as Dictionary<string, object> : null;
        }

        private static string GetString(Dictionary<string, object> d, string key, string def)
        {
            object val;
            if (d != null && d.TryGetValue(key, out val) && val != null) return val.ToString();
            return def;
        }

        private static double GetDouble(Dictionary<string, object> d, string key, double def)
        {
            object val;
            if (d != null && d.TryGetValue(key, out val) && val != null)
            {
                double r;
                if (double.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out r))
                    return r;
            }
            return def;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            object val;
            if (d != null && d.TryGetValue(key, out val) && val != null)
            {
                int r;
                if (int.TryParse(val.ToString(), out r)) return r;
            }
            return def;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            object val;
            if (d != null && d.TryGetValue(key, out val) && val != null)
            {
                bool r;
                if (bool.TryParse(val.ToString(), out r)) return r;
            }
            return def;
        }

        private static T ParseEnum<T>(Dictionary<string, object> d, string key, T def) where T : struct
        {
            object val;
            if (d != null && d.TryGetValue(key, out val) && val != null)
            {
                T parsed;
                if (Enum.TryParse<T>(val.ToString(), true, out parsed)) return parsed;
            }
            return def;
        }

        private static void WriteJson(HttpListenerResponse res, int statusCode, object obj)
        {
            try
            {
                string json = _jsonSerializer.Serialize(obj);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                res.StatusCode = statusCode;
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
            }
            catch { }
        }
    }
}

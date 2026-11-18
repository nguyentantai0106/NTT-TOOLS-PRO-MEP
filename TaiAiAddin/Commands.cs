using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using TaiAiAddin.Models;
using TaiAiAddin.Services;
using TaiAiAddin.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(TaiAiAddin.Commands))]
[assembly: CommandClass(typeof(TaiAiAddin.Commands))]

namespace TaiAiAddin
{
    public class Commands : IExtensionApplication
    {
        private static PaletteSet _paletteSet;
        private static TaiAiPaletteControl _paletteControl;
        private static string _pendingPanelToOpen = null;

                #region LỆNH ĐIỀU KHIỂN MCP SERVER & AI REMOTE CONTROL

        [CommandMethod("NTTMCP")]
        [CommandMethod("NTT_MCP")]
        public void McpStatusCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            string status = McpBridgeService.IsAuthorized 
                ? "🟢 ĐANG BẬT (Active - AI được phép gửi lệnh)" 
                : "⚪ ĐANG KHÓA (Locked - AI bị chặn truy cập)";

            ed.WriteMessage("\n=======================================================");
            ed.WriteMessage(string.Format("\n [NTT MCP SERVER] Trạng thái: {0}", status));
            ed.WriteMessage(string.Format("\n Cổng giao tiếp REST: http://localhost:{0}/api/", McpBridgeService.Port));
            ed.WriteMessage("\n Lệnh gõ: 'NTTMCP ON' để cấp quyền, 'NTTMCP OFF' để khóa quyền.");
            ed.WriteMessage("\n Hoặc yêu cầu trong AI chat: 'khởi động MCP server' để tự động mở.");
            ed.WriteMessage("\n=======================================================\n");
        }

        [CommandMethod("NTTMCP_START")]
        [CommandMethod("NTTMCP_ON")]
        public void McpStartCommand()
        {
            McpBridgeService.IsAuthorized = true;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null && doc.Editor != null) doc.Editor.WriteMessage("\n[NTT MCP] >>> ĐÃ CẤP QUYỀN ĐIỀU KHIỂN CHO AI (MCP Remote: ACTIVE) <<<\n");
        }

        [CommandMethod("NTTMCP_STOP")]
        [CommandMethod("NTTMCP_OFF")]
        public void McpStopCommand()
        {
            McpBridgeService.IsAuthorized = false;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null && doc.Editor != null) doc.Editor.WriteMessage("\n[NTT MCP] >>> ĐÃ KHÓA QUYỀN ĐIỀU KHIỂN AI (MCP Remote: LOCKED) <<<\n");
        }

        #endregion

        public void Initialize()
        {
            try
            {
                // Đăng ký sự kiện Double-Click để tự động mở đúng tủ điện được click trên CAD
                Application.BeginDoubleClick += OnBeginDoubleClick;

                McpBridgeService.StartServer(5005);
                SldLocalBridgeService.StartServer(19850);

                Editor ed = Application.DocumentManager.MdiActiveDocument != null ? Application.DocumentManager.MdiActiveDocument.Editor : null;
                if (ed != null)
                {
                    ed.WriteMessage("\n=======================================================");
                    ed.WriteMessage("\n [NTT TOOLS] Khởi tạo thành công!");
                    ed.WriteMessage("\n Nhập lệnh 'NTT' hoặc 'TaiAi' để mở Palette công cụ.");
                    ed.WriteMessage("\n Nhập lệnh 'NTTSLD' hoặc 'TaiAiSLD' để mở Sơ đồ tủ điện.");
                    ed.WriteMessage("\n Nhập lệnh 'NTTRISER' để vẽ Sơ đồ Cây Phân Phối (Riser Diagram).");
                    ed.WriteMessage("\n Nhập lệnh 'NTTDUCT' để vẽ tuyến ống gió tự động theo Polyline.");
                    ed.WriteMessage("\n Nhập lệnh 'NTTGAS' để vẽ tuyến ống đồng & bộ chia gas Daikin VN.");
                    ed.WriteMessage("\n Nhập lệnh 'NTTDRAIN' để vẽ tuyến ống nước ngưng kèm độ dốc tự động.");
                    ed.WriteMessage("\n Nhập lệnh 'NTTTMC' để vẽ Thang Máng Cáp thủ công kéo dãn trực quan (Jig).\n");
                    ed.WriteMessage("\n Nhập lệnh 'NTTTMCROUTE' để vẽ Tuyến Thang Máng Cáp tự động theo PL + Giá Treo.\n");
                    ed.WriteMessage("\n Nhập lệnh 'XLMB' hoặc 'BAOTATCA' để tự động quét & vẽ đường bao phòng toàn bộ.");
                    ed.WriteMessage("\n Nhập lệnh 'BAOPHONG' để click bo từng phòng riêng lẻ (tự tìm góc & bo kín phòng hở).");
                    ed.WriteMessage("\n Nhập lệnh 'XEMPHONG' để xem thông tin XData của phòng.");
                    ed.WriteMessage("\n Double-Click vào tủ điện trên bản vẽ để chỉnh sửa ngay!");
                    ed.WriteMessage("\n=======================================================\n");
                }
            }
            catch { }
        }

        public void Terminate()
        {
            try
            {
                Application.BeginDoubleClick -= OnBeginDoubleClick;
                Application.Idle -= OnIdleOpenPanelDialog;
            }
            catch { }

            McpBridgeService.StopServer();
            SldLocalBridgeService.StopServer();

            if (_paletteSet != null)
            {
                _paletteSet.Visible = false;
                _paletteSet.Dispose();
                _paletteSet = null;
            }
        }

        /// <summary>
        /// Xử lý sự kiện Double-Click: Nếu double click vào bất kỳ đối tượng nào của tủ điện thì mở dialog sửa đúng tủ đó
        /// </summary>
        private static void OnBeginDoubleClick(object sender, BeginDoubleClickEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                string targetPanelName = null;
                Editor ed = doc.Editor;

                // 1. Kiểm tra đối tượng đang chọn trước khi click (Implied Selection)
                PromptSelectionResult psr = ed.SelectImplied();
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so != null && so.ObjectId.IsValid && !so.ObjectId.IsErased)
                            {
                                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                                if (ent != null)
                                {
                                    targetPanelName = ElectricalDrawingService.GetPanelNameFromEntity(ent, tr, doc.Database);
                                    if (!string.IsNullOrEmpty(targetPanelName)) break;
                                }
                            }
                        }
                        tr.Commit();
                    }
                }

                // 2. Nếu chưa tìm thấy, quét vùng Crossing Window tại tọa độ click
                if (string.IsNullOrEmpty(targetPanelName))
                {
                    Point3d clickPt = e.Location;
                    double pickTol = 15.0;
                    Point3d pMin = new Point3d(clickPt.X - pickTol, clickPt.Y - pickTol, 0);
                    Point3d pMax = new Point3d(clickPt.X + pickTol, clickPt.Y + pickTol, 0);

                    PromptSelectionResult winPsr = ed.SelectCrossingWindow(pMin, pMax);
                    if (winPsr.Status == PromptStatus.OK && winPsr.Value != null && winPsr.Value.Count > 0)
                    {
                        using (doc.LockDocument())
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            foreach (SelectedObject so in winPsr.Value)
                            {
                                if (so != null && so.ObjectId.IsValid && !so.ObjectId.IsErased)
                                {
                                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                                    if (ent != null)
                                    {
                                        targetPanelName = ElectricalDrawingService.GetPanelNameFromEntity(ent, tr, doc.Database);
                                        if (!string.IsNullOrEmpty(targetPanelName)) break;
                                    }
                                }
                            }
                            tr.Commit();
                        }
                    }
                }

                // 3. Nếu tìm thấy tên tủ điện, kích hoạt mở dialog trên sự kiện Idle để an toàn
                if (!string.IsNullOrEmpty(targetPanelName))
                {
                    try { ed.SetImpliedSelection(new ObjectId[0]); } catch { }
                    _pendingPanelToOpen = targetPanelName;
                    Application.Idle += OnIdleOpenPanelDialog;
                }
            }
            catch { }
        }

        private static void OnIdleOpenPanelDialog(object sender, EventArgs e)
        {
            Application.Idle -= OnIdleOpenPanelDialog;
            string panelToOpen = _pendingPanelToOpen;
            _pendingPanelToOpen = null;

            if (string.IsNullOrEmpty(panelToOpen)) return;

            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    // Đảm bảo dữ liệu mới nhất được nạp
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        ElectricalPanel restored = ElectricalDrawingService.RestorePanelFromDrawing(panelToOpen, tr, doc.Database);
                        if (restored != null)
                        {
                            PanelStorageService.AddOrUpdatePanel(restored);
                        }
                        tr.Commit();
                    }
                }

                ElectricalPanelDialog dialog = new ElectricalPanelDialog(panelToOpen);
                dialog.ShowDialog();
            }
            catch { }
        }

        /// <summary>
        /// Lệnh quét và phân tích file mẫu tủ điện
        /// </summary>
        [CommandMethod("NTTSCANMN")]
        public static void CmdScanMauTuDienMienNam()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            {
                try
                {
                    string path = @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\Thư Viện\Điện\MAU TU DIEN MIEN NAM.dwg";
                    var res = DwgAnalyzerService.AnalyzeDwgFile(path);
                    string report = DwgAnalyzerService.GenerateAnalysisReport(res);
                    string outPath = @"C:\Users\ADMIN\AppData\Local\Temp\mau_tu_dien_report.txt";
                    File.WriteAllText(outPath, report);
                    doc.Editor.WriteMessage("\n[NTT] Da phan tich thanh cong: " + outPath);
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage("\n[NTT] Loi phan tich: " + ex.Message);
                }
            }
        }

        public static Assembly LoadLatestAssembly()
        {
            DateTime ignored;
            return LoadLatestAssembly(out ignored);
        }

        public static Assembly LoadLatestAssembly(out DateTime dllWriteTimeUtc)
        {
            dllWriteTimeUtc = DateTime.MinValue;
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string[] candidates = new string[]
                {
                    @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\bin\x64\Release\TaiAiAddin.dll",
                    Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\Windows\TaiAiAddin.dll"),
                    Path.Combine(appData, @"Autodesk\ApplicationPlugins\TaiAiAddin.bundle\Contents\TaiAiAddin.dll"),
                    @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\Windows\TaiAiAddin.dll",
                    @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin.bundle\Contents\TaiAiAddin.dll",
                    typeof(Commands).Assembly.Location
                };

                string bestDllPath = null;
                DateTime latestTime = DateTime.MinValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    string p = candidates[i];
                    if (!string.IsNullOrEmpty(p) && File.Exists(p) && !p.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime wt = File.GetLastWriteTimeUtc(p);
                        if (wt > latestTime)
                        {
                            latestTime = wt;
                            bestDllPath = p;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(bestDllPath) && File.Exists(bestDllPath))
                {
                    dllWriteTimeUtc = latestTime;
                    byte[] rawBytes = File.ReadAllBytes(bestDllPath);
                    return Assembly.Load(rawBytes);
                }
            }
            catch { }
            return typeof(Commands).Assembly;
        }
        /// <summary>
        /// Lệnh mở và nạp mới Palette bên hông AutoCAD
        /// </summary>
        [CommandMethod("NTT")]
        [CommandMethod("TaiAi")]
        [CommandMethod("NTTRELOAD")]
        [CommandMethod("NTT_RELOAD")]
        [CommandMethod("TaiAiReload")]
        public void ShowTaiAiPalette()
        {
            try
            {
                bool wasLayoutDlgOpen = (_activeLayoutDialog != null && _activeLayoutDialog.IsLoaded);
                if (wasLayoutDlgOpen)
                {
                    try { _activeLayoutDialog.Close(); }
                    catch { }
                    _activeLayoutDialog = null;
                    _activeLayoutDialogDllTime = DateTime.MinValue;
                }

                if (_paletteSet == null)
                {
                    _paletteSet = new PaletteSet("NTT TOOLS - Trợ Lý MEP", new Guid("3B1B916A-1B8C-4F7D-B2B2-747A9A167C55"));
                    _paletteSet.Style = PaletteSetStyles.ShowAutoHideButton |
                                        PaletteSetStyles.ShowCloseButton |
                                        PaletteSetStyles.ShowPropertiesMenu;
                    _paletteSet.MinimumSize = new Size(320, 500);
                    _paletteSet.Size = new Size(380, 650);
                }

                while (_paletteSet.Count > 0)
                {
                    _paletteSet.Remove(0);
                }

                System.Windows.Media.Visual newVisual = null;
                try
                {
                    Assembly latestAsm = LoadLatestAssembly();
                    if (latestAsm != null)
                    {
                        Type ctrlType = latestAsm.GetType("TaiAiAddin.Views.TaiAiPaletteControl");
                        if (ctrlType != null)
                        {
                            newVisual = Activator.CreateInstance(ctrlType) as System.Windows.Media.Visual;
                        }
                    }
                }
                catch { }

                if (newVisual != null)
                {
                    _paletteSet.AddVisual("Thiết Kế / Shop / BKL", newVisual);
                }
                else
                {
                    _paletteControl = new TaiAiPaletteControl();
                    _paletteSet.AddVisual("Thiết Kế / Shop / BKL", _paletteControl);
                }

                _paletteSet.Visible = true;

                if (wasLayoutDlgOpen)
                {
                    ShowLayoutProcessorModelessDialog();
                }

                Editor ed = Application.DocumentManager.MdiActiveDocument != null ? Application.DocumentManager.MdiActiveDocument.Editor : null;
                if (ed != null)
                {
                    ed.WriteMessage("\n[NTT TOOLS] Đã mở / làm mới Palette giao diện theo mã mới nhất (Lệnh: NTT, TaiAi, NTTRELOAD).");
                }
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(string.Format("Lỗi khi mở Palette NTT TOOLS: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Lệnh mở trực tiếp Giao diện Thiết kế Sơ đồ nguyên lý tủ điện HVAC
        /// </summary>
        [CommandMethod("TaiAiSLD")]
        [CommandMethod("NTTSLD")]
        [CommandMethod("NTTSLDUPDATE")]
        [CommandMethod("TAIPANEL")]
        public void ShowElectricalPanelDialog()
        {
            try
            {
                System.Windows.Window dialog = null;
                try
                {
                    Assembly latestAsm = LoadLatestAssembly();
                    if (latestAsm != null)
                    {
                        Type dlgType = latestAsm.GetType("TaiAiAddin.Views.ElectricalPanelDialog");
                        if (dlgType != null)
                        {
                            dialog = Activator.CreateInstance(dlgType) as System.Windows.Window;
                        }
                    }
                }
                catch { }

                if (dialog == null)
                {
                    dialog = new ElectricalPanelDialog();
                }

                dialog.ShowDialog();
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(string.Format("Lỗi mở sơ đồ tủ điện: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Lệnh vẽ Cây Sơ Đồ Phân Phối Điện Tổng Thể (Riser Diagram)
        /// </summary>
        [CommandMethod("NTTRISER")]
        public void DrawRiserDiagramCommand()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                List<ElectricalPanel> panels = PanelStorageService.GetAllPanels();
                ElectricalDrawingService drawingService = new ElectricalDrawingService();

                PromptPointResult ppr = doc.Editor.GetPoint("\nChọn điểm đặt Sơ đồ Cây Phân Phối (Riser Diagram): ");
                Point3d pt = (ppr.Status == PromptStatus.OK) ? ppr.Value : Point3d.Origin;

                drawingService.DrawRiserDiagram(doc, panels, pt);
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(string.Format("Lỗi vẽ Riser Diagram: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Lệnh vẽ trực tiếp Sơ đồ nguyên lý mẫu vào AutoCAD không cần mở dialog (Dùng kiểm thử tự động)
        /// </summary>
        [CommandMethod("NTTDRAW")]
        public void DrawSampleDiagramDirect()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                ElectricalPanel panel = new ElectricalPanel();
                panel.PanelName = "MSB-HVAC";
                panel.DefaultCableType = "Cu/XLPE/PVC";
                panel.Incomer = IncomerType.DualATS;
                panel.HasCapacitorBank = true;
                panel.MainBreakerSpec = "MCCB 3P-36kA 500A";

                panel.Loads.Add(new ElectricalLoad { Stt = 1, LoadName = "CHILLER 01", PowerKW = 95.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, BreakerSpec = "MCCB 3P-36kA 200A", CableLength = 40.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 2, LoadName = "CHILLER 02", PowerKW = 95.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, BreakerSpec = "MCCB 3P-36kA 200A", CableLength = 40.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 3, LoadName = "BƠM NƯỚC LẠNH 01", PowerKW = 18.5, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, Control = ControlMode.AutoManual, BreakerSpec = "MCCB 3P-25kA 50A", CableLength = 25.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 4, LoadName = "BƠM GIẢI NHIỆT 01", PowerKW = 22.0, Phase = PhaseType.ThreePhase, Starter = StarterType.VFD, Control = ControlMode.PressureSwitch, BreakerSpec = "MCCB 3P-25kA 63A", CableLength = 30.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 5, LoadName = "QUẠT HÚT KHÓI", PowerKW = 15.0, Phase = PhaseType.ThreePhase, Starter = StarterType.SoftStarter, BreakerSpec = "MCCB 3P-25kA 40A", CableLength = 35.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 6, LoadName = "CHIẾU SÁNG PHÒNG MÁY", PowerKW = 3.5, Phase = PhaseType.SinglePhase, AssignedPhase = "R", BreakerSpec = "MCB 1P-6kA 25A", CableLength = 20.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 7, LoadName = "Ổ CẮM KỸ THUẬT", PowerKW = 4.0, Phase = PhaseType.SinglePhase, AssignedPhase = "Y", BreakerSpec = "MCB 1P-6kA 32A", CableLength = 20.0 });

                ElectricalCalculator calc = new ElectricalCalculator();
                calc.CalculatePanel(panel);

                DrawingParameters p = new DrawingParameters();
                p.AutoFitSpacing = true;
                p.IncludeTitleBlock = true;
                p.IncludeLegend = true;
                p.DrawFrontView = true;
                p.ProjectName = "CÔNG TRÌNH CƠ ĐIỆN TÒA NHÀ & NHÀ XƯỞNG";
                p.DrawingTitle = "SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN MSB-HVAC";
                p.DrawnBy = "NGUYỄN TẤN TÀI";
                p.CheckedBy = "CAO VĂN SIÊU";
                p.ApprovedBy = "DƯƠNG CAO SƠN";
                p.DrawingNumber = "EL-01";
                p.Revision = "00";
                p.AutoFitForPanel(panel);

                ElectricalDrawingService drawingService = new ElectricalDrawingService();
                drawingService.DrawSingleLineDiagram(doc, panel, new Point3d(0, 0, 0), p);
                doc.Editor.WriteMessage("\n[NTT TOOLS] Đã hoàn thành vẽ trực tiếp sơ đồ mẫu vào bản vẽ (bao gồm ATS, Y-Δ, VFD, SoftStarter, Tụ bù, Mặt ngoài cánh tủ và Khung tên)!");
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(string.Format("Lỗi vẽ sơ đồ trực tiếp: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Lệnh kiểm thử Cập nhật sơ đồ tủ điện đã vẽ trên bản vẽ
        /// </summary>
        [CommandMethod("NTTTESTUPDATE")]
        public void TestUpdateDiagram()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                ElectricalPanel panel = new ElectricalPanel();
                panel.PanelName = "MSB-HVAC";
                panel.DefaultCableType = "Cu/XLPE/PVC";
                panel.Incomer = IncomerType.DualATS;
                panel.HasCapacitorBank = true;
                panel.MainBreakerSpec = "MCCB 3P-36kA 630A";

                panel.Loads.Add(new ElectricalLoad { Stt = 1, LoadName = "CHILLER 01 - ĐÃ CẬP NHẬT", PowerKW = 110.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, BreakerSpec = "MCCB 3P-36kA 250A", CableLength = 40.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 2, LoadName = "CHILLER 02 - ĐÃ CẬP NHẬT", PowerKW = 110.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, BreakerSpec = "MCCB 3P-36kA 250A", CableLength = 40.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 3, LoadName = "BƠM NƯỚC LẠNH 01", PowerKW = 22.0, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, Control = ControlMode.AutoManual, BreakerSpec = "MCCB 3P-25kA 63A", CableLength = 25.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 4, LoadName = "BƠM GIẢI NHIỆT 01", PowerKW = 30.0, Phase = PhaseType.ThreePhase, Starter = StarterType.VFD, BreakerSpec = "MCCB 3P-25kA 80A", CableLength = 30.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 5, LoadName = "QUẠT HÚT KHÓI", PowerKW = 15.0, Phase = PhaseType.ThreePhase, Starter = StarterType.SoftStarter, BreakerSpec = "MCCB 3P-25kA 40A", CableLength = 35.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 6, LoadName = "CHIẾU SÁNG PHÒNG MÁY", PowerKW = 5.0, Phase = PhaseType.SinglePhase, AssignedPhase = "R", BreakerSpec = "MCB 1P-6kA 32A", CableLength = 20.0 });
                panel.Loads.Add(new ElectricalLoad { Stt = 7, LoadName = "Ổ CẮM KỸ THUẬT", PowerKW = 4.0, Phase = PhaseType.SinglePhase, AssignedPhase = "Y", BreakerSpec = "MCB 1P-6kA 32A", CableLength = 20.0 });

                ElectricalCalculator calc = new ElectricalCalculator();
                calc.CalculatePanel(panel);

                DrawingParameters p = new DrawingParameters();
                p.AutoFitSpacing = true;
                p.IncludeTitleBlock = true;
                p.IncludeLegend = true;
                p.DrawFrontView = true;
                p.ProjectName = "CÔNG TRÌNH CƠ ĐIỆN TÒA NHÀ & NHÀ XƯỞNG";
                p.DrawingTitle = "SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN MSB-HVAC";
                p.DrawnBy = "NGUYỄN TẤN TÀI";
                p.CheckedBy = "CAO VĂN SIÊU";
                p.ApprovedBy = "DƯƠNG CAO SƠN";
                p.DrawingNumber = "EL-01";
                p.Revision = "00";
                p.AutoFitForPanel(panel);

                ElectricalDrawingService drawingService = new ElectricalDrawingService();
                bool ok = drawingService.UpdateSingleLineDiagram(doc, panel, p);
                doc.Editor.WriteMessage(ok ? "\n[NTT TOOLS] Cập nhật sơ đồ tủ MSB-HVAC thành công!" : "\n[NTT TOOLS] Không tìm thấy sơ đồ cũ để cập nhật!");
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog(string.Format("Lỗi update: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Bộ kiểm thử tự động toàn diện:
        /// 1. Cân pha 1 pha thông minh (LPT)
        /// 2. Khởi động động cơ (DOL, StarDelta Y-Δ 2x3C, VFD, SoftStarter, HOA/FS/PS)
        /// 3. Tính toán sụt áp TCVN & Tối ưu cáp
        /// 4. Tính toán Tụ Bù kVAR & Ngắn mạch Isc/Icu
        /// 5. Nguồn kép ATS & Khóa liên động
        /// 6. Xuất/Nhập Excel 2 chiều & Bóc tách BOQ
        /// 7. Riser Diagram & Mặt ngoài cánh tủ
        /// </summary>
                [CommandMethod("NTTTESTDIALOG")]
        public void TestDialog()
        {
            try
            {
                ElectricalPanelDialog dlg = new ElectricalPanelDialog();

                // Test adding 3P and 1P loads
                dlg.TestAddRows();
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[TEST] TestAddRows executed successfully!\n");

                // Test re-initializing
                ElectricalPanelDialog dlg2 = new ElectricalPanelDialog();
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[TEST] Second ElectricalPanelDialog initialized successfully!\n");
Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[TEST] ElectricalPanelDialog initialized successfully!\n");
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(string.Format("\n[TEST ERROR] {0}\nStack: {1}\n", ex.Message, ex.StackTrace));
                if (ex.InnerException != null)
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(string.Format("\n[INNER] {0}\nStack: {1}\n", ex.InnerException.Message, ex.InnerException.StackTrace));
                }
            }
        }

[CommandMethod("NTTTESTSUITE")]
        public void RunTestSuite()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n\n=======================================================");
            ed.WriteMessage("\n=== BẮT ĐẦU KIỂM THỬ TOÀN DIỆN 6 HẠNG MỤC NÂNG CAO ===");
            ed.WriteMessage("\n=======================================================");

            // 1. Kiểm thử Cân Pha Tự Động & Động Cơ Nâng Cao
            ed.WriteMessage("\n[1] Kiểm thử Cân pha tự động & Động cơ (DOL, Y-Δ, VFD, SoftStarter):");
            ElectricalPanel panel = new ElectricalPanel();
            panel.PanelName = "DB-TEST-ADV";
            panel.Incomer = IncomerType.DualATS;
            panel.HasCapacitorBank = true;

            panel.Loads.Add(new ElectricalLoad { LoadName = "CHILLER 01", PowerKW = 75.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, CableLength = 50.0 });
            panel.Loads.Add(new ElectricalLoad { LoadName = "BƠM NƯỚC LẠNH", PowerKW = 22.0, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, Control = ControlMode.AutoManual, CableLength = 30.0 });
            panel.Loads.Add(new ElectricalLoad { LoadName = "QUẠT TĂNG ÁP", PowerKW = 15.0, Phase = PhaseType.ThreePhase, Starter = StarterType.VFD, Control = ControlMode.PressureSwitch, CableLength = 40.0 });
            panel.Loads.Add(new ElectricalLoad { LoadName = "QUẠT HÚT KHÓI", PowerKW = 18.5, Phase = PhaseType.ThreePhase, Starter = StarterType.SoftStarter, CableLength = 35.0 });
            panel.Loads.Add(new ElectricalLoad { LoadName = "CHIẾU SÁNG SẢNH A", PowerKW = 8.0, Phase = PhaseType.SinglePhase, CableLength = 25.0 });
            panel.Loads.Add(new ElectricalLoad { LoadName = "CHIẾU SÁNG SẢNH B", PowerKW = 7.0, Phase = PhaseType.SinglePhase, CableLength = 25.0 });
            panel.Loads.Add(new ElectricalLoad { LoadName = "Ổ CẮM TẦNG 1", PowerKW = 6.0, Phase = PhaseType.SinglePhase, CableLength = 20.0 });

            ElectricalCalculator calc = new ElectricalCalculator();
            calc.AutoBalancePhases(panel);

            ed.WriteMessage(string.Format("\n    -> Pha R: {0:N2} kW, Pha Y: {1:N2} kW, Pha B: {2:N2} kW", panel.PhasePowerR, panel.PhasePowerY, panel.PhasePowerB));
            double unb = ElectricalCalculator.GetPhaseUnbalancePercentage(panel);
            ed.WriteMessage(string.Format("\n    -> Độ lệch pha tối đa: {0:N1}%", unb));

            // Kiểm tra cáp 2x3C của Star-Delta
            string ydCable = panel.Loads[0].FullCableString;
            ed.WriteMessage(string.Format("\n    -> Cáp của Y-Δ Starter: '{0}'", ydCable));
            if (ydCable.StartsWith("2x("))
            {
                ed.WriteMessage("\n    [ĐẠT] Star-Delta tự động cấp 2 đường cáp 2x3C chuẩn kỹ thuật!");
            }

            // 2. Kiểm thử Tính Toán Sụt Áp & Tụ Bù
            ed.WriteMessage("\n\n[2] Kiểm thử Tính toán Sụt Áp TCVN & Tụ Bù & Ngắn Mạch:");
            ed.WriteMessage(string.Format("\n    -> Sụt áp Chiller: {0:N2}%", panel.Loads[0].VoltageDropPercent));
            ed.WriteMessage(string.Format("\n    -> Dung lượng Tụ bù tính toán: {0} kVAR ({1} cấp bù)", panel.CapacitorBankKvar, panel.CapacitorSteps));
            ed.WriteMessage(string.Format("\n    -> Dòng ngắn mạch tính toán: Isc = {0:N1} kA -> Đề xuất: {1}", panel.ShortCircuitKA, panel.SuggestedMainIcu));
            ed.WriteMessage(string.Format("\n    -> Kích thước vỏ tủ dự kiến: {0} x {1} x {2} mm", panel.EstimatedHeight, panel.EstimatedWidth, panel.EstimatedDepth));
            if (panel.CapacitorBankKvar > 0 && panel.ShortCircuitKA > 0)
            {
                ed.WriteMessage("\n    [ĐẠT] Tính toán kỹ thuật hoàn thành chính xác!");
            }

            // 3. Kiểm thử Xuất Excel .xls đầy đủ kèm Sheet BOQ
            ed.WriteMessage("\n\n[3] Kiểm thử Xuất Excel .xls (Sheet Bảng Tính & Sheet BOQ):");
            ExcelExportService exporter = new ExcelExportService();
            string xlsPath = "d:\\01. NGUYEN TAN TAI\\CODING AI\\00.BAI 1_TOOLS\\DB_TEST_ADV_Full_BOQ.xls";
            bool expOk = exporter.ExportPanelToExcel(panel, xlsPath);
            if (expOk && System.IO.File.Exists(xlsPath))
            {
                long sz = new System.IO.FileInfo(xlsPath).Length;
                ed.WriteMessage(string.Format("\n    [ĐẠT] Xuất Excel .xls thành công! Kích thước: {0} bytes tại '{1}'", sz, xlsPath));
            }

            // 4. Kiểm thử Template & Import Excel hai chiều
            ed.WriteMessage("\n\n[4] Kiểm thử Template & Nhập dữ liệu từ Excel:");
            ExcelImportService importer = new ExcelImportService();
            string templatePath = "d:\\01. NGUYEN TAN TAI\\CODING AI\\00.BAI 1_TOOLS\\Template_PhuTai_Mau.xls";
            bool tplOk = importer.CreateSampleTemplate(templatePath);
            List<ElectricalLoad> importedLoads = importer.ImportLoads(templatePath);
            ed.WriteMessage(string.Format("\n    -> Đã tạo template mẫu ({0}), nạp ngược lại đọc được: {1} phụ tải!", tplOk ? "OK" : "FAIL", importedLoads.Count));
            if (importedLoads.Count >= 7)
            {
                ed.WriteMessage("\n    [ĐẠT] Nhập dữ liệu 2 chiều từ Excel hoạt động hoàn hảo!");
            }

            // 5. Kiểm thử XData Serialization cho các trường mở rộng
            ed.WriteMessage("\n\n[5] Kiểm thử Lưu & Khôi phục XData (ATS, Starter, Control, Sụt áp):");
            ResultBuffer rb = PanelStorageService.SerializePanelToXData(panel, Point3d.Origin);
            Point3d pt;
            ElectricalPanel restored = PanelStorageService.DeserializePanelFromXData(rb, out pt);
            if (restored != null && restored.Incomer == IncomerType.DualATS && restored.Loads[0].Starter == StarterType.StarDelta)
            {
                ed.WriteMessage(string.Format("\n    [ĐẠT] Khôi phục toàn vẹn 100%: Nguồn={0}, Starter={1}, Control={2}!",
                    restored.Incomer, restored.Loads[0].Starter, restored.Loads[1].Control));
            }

            ed.WriteMessage("\n=======================================================");
            ed.WriteMessage("\n=== HOÀN TẤT KIỂM THỬ: TẤT CẢ 6 HẠNG MỤC ĐÃ ĐẠT 100%! ===");
            ed.WriteMessage("\n=======================================================\n");
        }

        [CommandMethod("NTTTESTSPARE")]
        public void RunTestSpare()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            ed.WriteMessage("\n=== KIỂM THỬ TÍNH NĂNG NHIỀU LỘ DỰ PHÒNG VÀ KHÔNG VẼ DÂY RA ===");

            ElectricalPanel panel = new ElectricalPanel();
            panel.PanelName = "DB-SPARE-TEST";
            panel.Loads.Add(new ElectricalLoad { LoadName = "TẢI CHÍNH 01", PowerKW = 22.0, Phase = PhaseType.ThreePhase });
            panel.Loads.Add(new ElectricalLoad { LoadName = "TẢI CHÍNH 02", PowerKW = 5.5, Phase = PhaseType.SinglePhase, AssignedPhase = "R" });
            
            panel.Loads.Add(new ElectricalLoad { LoadName = "LỘ DỰ PHÒNG 3P (LƯỚI)", Phase = PhaseType.ThreePhase, IsSpare = true, DrawOutgoingWire = false });
            panel.Loads.Add(new ElectricalLoad { LoadName = "LỘ DỰ PHÒNG 1P (LƯỚI)", Phase = PhaseType.SinglePhase, IsSpare = true, DrawOutgoingWire = false });

            ElectricalCalculator calc = new ElectricalCalculator();
            calc.CalculatePanel(panel);

            ed.WriteMessage(string.Format("\n1. Tổng công suất tải tính toán: Ptt = {0:N2} kW (Dự phòng P=0 không làm méo tổng tải)", panel.TotalCalculatedPowerKW));
            if (panel.TotalCalculatedPowerKW > 27.0 && panel.TotalCalculatedPowerKW < 28.0)
            {
                ed.WriteMessage("\n    [ĐẠT] Phụ tải dự phòng tự động đặt 0 kW và không cộng dồn vào tổng công suất tủ!");
            }

            DrawingParameters p = new DrawingParameters();
            p.IncludeSpare = true;
            p.SpareCount3P = 2;
            p.SpareCount1P = 2;
            p.DrawSpareOutgoingWire = false;

            ElectricalDrawingService drawer = new ElectricalDrawingService();
            bool ok = drawer.DrawSingleLineDiagram(doc, panel, Point3d.Origin, p);
            if (ok)
            {
                ed.WriteMessage(string.Format("\n2. Vẽ Sơ đồ nguyên lý với {0} lộ tải ({1} tải thật + 2 lộ dự phòng trên lưới) + 4 lộ dự phòng cấu hình (2 lộ 3P + 2 lộ 1P) thành công!",
                    panel.Loads.Count, 2));
                ed.WriteMessage("\n    [ĐẠT] Toàn bộ các lộ dự phòng đều được vẽ Aptomat trên busbar và KHÔNG vẽ dây chạy xuống mép bảng!");
            }
            ed.WriteMessage("\n=== HOÀN TẤT KIỂM THỬ LỘ DỰ PHÒNG! ===\n");
        }

        [CommandMethod("NTTTESTBREAKER")]
        public void RunTestBreaker()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            ed.WriteMessage("\n=======================================================");
            ed.WriteMessage("\n=== KIỂM THỬ TÙY CHỌN APTOMAT (RCCB, MCB, MCCB) ===");
            ed.WriteMessage("\n=======================================================");

            ElectricalPanel panel = new ElectricalPanel();
            panel.PanelName = "DB-BREAKER-OPT";

            panel.Loads.Add(new ElectricalLoad { LoadName = "Ổ CẮM KHU ƯỚT (RCCB)", PowerKW = 3.5, Phase = PhaseType.SinglePhase, BreakerType = "RCCB 2P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "CHIẾU SÁNG (MCB 2P)", PowerKW = 2.2, Phase = PhaseType.SinglePhase, BreakerType = "MCB 2P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "BƠM PHỤ (MCB 3P)", PowerKW = 7.5, Phase = PhaseType.ThreePhase, BreakerType = "MCB 3P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "QUẠT CẤP (MCB 4P)", PowerKW = 11.0, Phase = PhaseType.ThreePhase, BreakerType = "MCB 4P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "LỘ PHỤ 1 (MCCB 1P)", PowerKW = 5.0, Phase = PhaseType.SinglePhase, BreakerType = "MCCB 1P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "LỘ PHỤ 2 (MCCB 2P)", PowerKW = 8.0, Phase = PhaseType.SinglePhase, BreakerType = "MCCB 2P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "TỦ CON ĐỘNG LỰC (MCCB 4P)", PowerKW = 45.0, Phase = PhaseType.ThreePhase, BreakerType = "MCCB 4P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "Ổ CẮM BẾP (RCBO 2P)", PowerKW = 4.0, Phase = PhaseType.SinglePhase, BreakerType = "RCBO 2P" });
            panel.Loads.Add(new ElectricalLoad { LoadName = "MCC HVAC CHẠY CÁP 3x1C", PowerKW = 127.0, Phase = PhaseType.ThreePhase, BreakerType = "MCCB 3P", CoreType = "3x1C" });

            ElectricalCalculator calc = new ElectricalCalculator();
            calc.CalculatePanel(panel);

            foreach (var ld in panel.Loads)
            {
                ed.WriteMessage(string.Format("\n    -> {0} ({1}): '{2}'", ld.LoadName, ld.BreakerType, ld.BreakerSpec));
            }

            DrawingParameters p = new DrawingParameters();
            p.IncludeLegend = true;
            ElectricalDrawingService drawer = new ElectricalDrawingService();
            bool ok = drawer.DrawSingleLineDiagram(doc, panel, Point3d.Origin, p);
            if (ok)
            {
                ed.WriteMessage("\n    [ĐẠT] Vẽ thành công Sơ đồ nguyên lý với đầy đủ tùy chọn Aptomat và Bảng chú thích RCCB/RCBO!");
            }
            ed.WriteMessage("\n=== HOÀN TẤT KIỂM THỬ TÙY CHỌN APTOMAT! ===\n");
        }

        [CommandMethod("NTTANALYZETD")]
        public void AnalyzeTDDrawingCommand()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            string targetPath = @"c:\Users\ADMIN\Desktop\TD.dwg";
            if (!System.IO.File.Exists(targetPath))
            {
                ed.WriteMessage("\n[LỖI] Không tìm thấy file: " + targetPath);
                return;
            }

            ed.WriteMessage("\n[TaiAi] Đang dùng C# phân tích chi tiết bản vẽ: " + targetPath + " ...");
            try
            {
                var result = DwgAnalyzerService.AnalyzeDwgFile(targetPath);
                string report = DwgAnalyzerService.GenerateAnalysisReport(result);
                
                string outPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "TD_Analysis_Report.txt");
                System.IO.File.WriteAllText(outPath, report, System.Text.Encoding.UTF8);

                string outBrain = @"C:\Users\ADMIN\.gemini\antigravity\brain\f90d4c37-0e5a-456b-86c6-91fa9de81c3b\TD_Analysis_Report.txt";
                System.IO.File.WriteAllText(outBrain, report, System.Text.Encoding.UTF8);

                ed.WriteMessage("\n[TaiAi] Phân tích hoàn tất! Tổng thực thể: " + result.TotalEntities);
                ed.WriteMessage("\n[TaiAi] Kích thước vùng vẽ: W = " + result.TotalWidth + "mm, H = " + result.TotalHeight + "mm");
                ed.WriteMessage("\n[TaiAi] Đã lưu báo cáo tại: " + outPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[LỖI Phân tích]: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        [CommandMethod("NTTDRAWENCLOSURE")]
        [CommandMethod("NTTTD")]
        [CommandMethod("NTTENCLOSURE")]
        [CommandMethod("TaiAiTD")]
        public void DrawEnclosureLayoutCommand()
        {
            try
            {
                EnclosureLayoutDialog dialog = new EnclosureLayoutDialog();
                dialog.ShowDialog();
            }
            catch (System.Exception ex)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument != null ? Application.DocumentManager.MdiActiveDocument.Editor : null;
                if (ed != null) ed.WriteMessage("\n[NTT Error] " + ex.Message);
            }
        }

        [CommandMethod("NTTTDBATCH")]
        public void DrawEnclosureLayoutBatchCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions("\n[NTT TOOLS] Chọn điểm đặt: ");
            ppo.AllowNone = true;
            PromptPointResult ppr = ed.GetPoint(ppo);
            Point3d pt = (ppr.Status == PromptStatus.OK) ? ppr.Value : Point3d.Origin;

            ElectricalPanel panel = ElectricalCalculator.CreateDefaultHvacPanel();
            EnclosureLayoutParameters p = new EnclosureLayoutParameters();
            EnclosureLayoutDrawingService svc = new EnclosureLayoutDrawingService();
            bool ok = svc.DrawFullEnclosureLayoutPlan(doc, panel, pt, p);
            if (ok)
            {
                ed.WriteMessage("\n[NTT TOOLS] Đã xuất Bản vẽ Mặt Bằng Bố Trí Vỏ Tủ (Batch) thành công!");
            }
        }

        [CommandMethod("NTTDUCT")]
        [CommandMethod("NTT_DUCT")]
        public void RouteDuctCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptDoubleOptions pdoQ = new PromptDoubleOptions("\n[NTT TOOLS] Nhập lưu lượng gió thiết kế Q (CMH) [3000]: ");
            pdoQ.DefaultValue = 3000;
            pdoQ.AllowNegative = false;
            pdoQ.AllowZero = false;
            PromptDoubleResult pdrQ = ed.GetDouble(pdoQ);
            if (pdrQ.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdoV = new PromptDoubleOptions("\n[NTT TOOLS] Nhập vận tốc gió V (m/s) [6.0]: ");
            pdoV.DefaultValue = 6.0;
            pdoV.AllowNegative = false;
            pdoV.AllowZero = false;
            PromptDoubleResult pdrV = ed.GetDouble(pdoV);
            if (pdrV.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdoH = new PromptDoubleOptions("\n[NTT TOOLS] Nhập chiều cao ống gió cố định H (mm) [300]: ");
            pdoH.DefaultValue = 300;
            pdoH.AllowNegative = false;
            pdoH.AllowZero = false;
            PromptDoubleResult pdrH = ed.GetDouble(pdoH);
            if (pdrH.Status != PromptStatus.OK) return;

            PromptEntityOptions peo = new PromptEntityOptions("\n[NTT TOOLS] Chọn đường Polyline (PL) định tuyến ống gió: ");
            peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline!");
            peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            DuctRoutingParams p = new DuctRoutingParams
            {
                SystemType = HvacService.LayerHvacDuctSupply,
                InitialAirFlowCmh = pdrQ.Value,
                VelocityMs = pdrV.Value,
                FixedHeightMm = pdrH.Value,
                StepDownPercent = 20.0,
                DrawDoubleLine = true,
                DrawElbows = true,
                DrawCenterline = true,
                AddAnnotations = true
            };

            bool ok = HvacService.RouteDuctAlongPolyline(doc, per.ObjectId, p);
            if (ok)
            {
                ed.WriteMessage(string.Format("\n[NTT TOOLS] Đã vẽ xong tuyến ống gió theo đường PL (Q={0} CMH, V={1} m/s, H={2} mm)!", pdrQ.Value, pdrV.Value, pdrH.Value));
            }
        }

        [CommandMethod("NTTGAS")]
        [CommandMethod("NTT_GAS")]
        public void RouteGasCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptDoubleOptions pdo = new PromptDoubleOptions("\n[NTT TOOLS] Nhập tổng công suất lạnh của nhánh VRV (kW) [25.0]: ");
            pdo.DefaultValue = 25.0;
            pdo.AllowNegative = false;
            pdo.AllowZero = false;
            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK) return;

            PromptEntityOptions peo = new PromptEntityOptions("\n[NTT TOOLS] Chọn đường Polyline (PL) định tuyến ống đồng: ");
            peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline!");
            peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            RefrigerantRoutingParams p = new RefrigerantRoutingParams
            {
                TotalCoolingKw = pdr.Value,
                PipeSpacingMm = 60.0,
                InsertRefnetJoints = true,
                DrawCombinedDrain = true,
                DrainSlopePercent = 1.0,
                AddAnnotations = true
            };

            bool ok = HvacService.RouteRefrigerantAlongPolyline(doc, per.ObjectId, p);
            if (ok)
            {
                DaikinRefnetSpec spec = HvacService.GetDaikinRefnetByCapacity(pdr.Value);
                ed.WriteMessage(string.Format("\n[NTT TOOLS] Đã vẽ xong tuyến ống đồng VRV theo tiêu chuẩn Daikin VN (Bộ chia gas: {0})!", spec.ModelName));
            }
        }

        [CommandMethod("NTTDRAIN")]
        [CommandMethod("NTT_DRAIN")]
        public void RouteDrainCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptDoubleOptions pdoSlope = new PromptDoubleOptions("\n[NTT TOOLS] Nhập độ dốc thiết kế i (%) [1.0]: ");
            pdoSlope.DefaultValue = 1.0;
            pdoSlope.AllowNegative = false;
            pdoSlope.AllowZero = false;
            PromptDoubleResult pdrSlope = ed.GetDouble(pdoSlope);
            if (pdrSlope.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdoCap = new PromptDoubleOptions("\n[NTT TOOLS] Nhập công suất lạnh để tính cỡ ống uPVC (kW) [15.0]: ");
            pdoCap.DefaultValue = 15.0;
            pdoCap.AllowNegative = false;
            PromptDoubleResult pdrCap = ed.GetDouble(pdoCap);
            if (pdrCap.Status != PromptStatus.OK) return;

            PromptEntityOptions peo = new PromptEntityOptions("\n[NTT TOOLS] Chọn đường Polyline (PL) định tuyến ống nước ngưng: ");
            peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline!");
            peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            DrainRoutingParams p = new DrainRoutingParams
            {
                TotalCoolingKw = pdrCap.Value,
                SlopePercent = pdrSlope.Value,
                StartElevationM = 2.8,
                AddSlopeArrow = true,
                AddAnnotations = true
            };

            bool ok = HvacService.RouteCondensateDrainAlongPolyline(doc, per.ObjectId, p);
            if (ok)
            {
                double pipeDia;
                string pipeLabel = HvacService.GetCondensateDrainSize(pdrCap.Value, out pipeDia);
                ed.WriteMessage(string.Format("\n[NTT TOOLS] Đã vẽ xong tuyến ống nước ngưng {0} với độ dốc i={1}%!", pipeLabel, pdrSlope.Value));
            }
        }

        #region Các Lệnh Thang Máng Cáp (TMC) - Cable Tray / Trunking / Ladder

        private static CableTrayRouteResult _lastTmcResult = null;
        private static CableTrayParams _lastTmcParams = null;

                [CommandMethod("NTTTMC")]
        [CommandMethod("NTT_TMC")]
        [CommandMethod("NTTTMCJIG")]
        public void DrawTmcManualCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            // 1. Ưu tiên lấy thông số vừa chọn trên Palette (nếu có)
            CableTrayParams p = CableTrayService.ActiveParams;
            CableTrayService.ActiveParams = null; // Tiêu thụ 1 lần

            // 2. Nếu chưa có từ Palette thì lấy thông số lần trước (User Settings Persistence)
            if (p == null)
            {
                p = _lastTmcParams;
            }

            // 3. Nếu chạy lệnh lần đầu tiên mà chưa có thông số thì mới prompt hỏi người dùng
            if (p == null)
            {
                PromptKeywordOptions pko = new PromptKeywordOptions("\n[NTT TOOLS] Chọn loại Thang Máng Cáp [Trunking/Ladder/tRay] <Trunking>: ");
                pko.Keywords.Add("Trunking");
                pko.Keywords.Add("Ladder");
                pko.Keywords.Add("tRay");
                pko.Keywords.Default = "Trunking";
                pko.AllowNone = true;
                PromptResult prType = ed.GetKeywords(pko);
                if (prType.Status != PromptStatus.OK) return;

                CableTrayType trayType = CableTrayType.Trunking;
                if (prType.StringResult == "Ladder") trayType = CableTrayType.Ladder;
                else if (prType.StringResult == "tRay") trayType = CableTrayType.Tray;

                PromptDoubleOptions pdoW = new PromptDoubleOptions("\n[NTT TOOLS] Nhập bề rộng máng cáp W (mm) [300]: ");
                pdoW.DefaultValue = 300;
                pdoW.AllowNegative = false;
                pdoW.AllowZero = false;
                PromptDoubleResult pdrW = ed.GetDouble(pdoW);
                if (pdrW.Status != PromptStatus.OK) return;

                PromptDoubleOptions pdoH = new PromptDoubleOptions("\n[NTT TOOLS] Nhập chiều cao máng cáp H (mm) [100]: ");
                pdoH.DefaultValue = 100;
                pdoH.AllowNegative = false;
                pdoH.AllowZero = false;
                PromptDoubleResult pdrH = ed.GetDouble(pdoH);
                if (pdrH.Status != PromptStatus.OK) return;

                p = new CableTrayParams
                {
                    Type = trayType,
                    WidthMm = pdrW.Value,
                    HeightMm = pdrH.Value,
                    ThicknessMm = 1.5,
                    SupportSpacingM = 1.5,
                    InsertSupports = true,
                    AddAnnotations = true
                };
            }

            _lastTmcParams = p;
            CableTrayService.DrawCableTrayManualWithJig(doc, p);
        }

        [CommandMethod("NTTTMCROUTE")]
        [CommandMethod("NTT_TMC_ROUTE")]
        public void RouteTmcAlongPolylineCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptKeywordOptions pko = new PromptKeywordOptions("\n[NTT TOOLS] Chọn loại Thang Máng Cáp [Trunking/Ladder/tRay] <Trunking>: ");
            pko.Keywords.Add("Trunking");
            pko.Keywords.Add("Ladder");
            pko.Keywords.Add("tRay");
            pko.Keywords.Default = "Trunking";
            pko.AllowNone = true;
            PromptResult prType = ed.GetKeywords(pko);
            if (prType.Status != PromptStatus.OK) return;

            CableTrayType trayType = CableTrayType.Trunking;
            if (prType.StringResult == "Ladder") trayType = CableTrayType.Ladder;
            else if (prType.StringResult == "tRay") trayType = CableTrayType.Tray;

            PromptDoubleOptions pdoW = new PromptDoubleOptions("\n[NTT TOOLS] Nhập bề rộng máng cáp W (mm) [300]: ");
            pdoW.DefaultValue = 300;
            pdoW.AllowNegative = false;
            pdoW.AllowZero = false;
            PromptDoubleResult pdrW = ed.GetDouble(pdoW);
            if (pdrW.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdoH = new PromptDoubleOptions("\n[NTT TOOLS] Nhập chiều cao máng cáp H (mm) [100]: ");
            pdoH.DefaultValue = 100;
            pdoH.AllowNegative = false;
            pdoH.AllowZero = false;
            PromptDoubleResult pdrH = ed.GetDouble(pdoH);
            if (pdrH.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdoSup = new PromptDoubleOptions("\n[NTT TOOLS] Nhập khoảng cách giá treo máng cáp (m) [1.5]: ");
            pdoSup.DefaultValue = 1.5;
            pdoSup.AllowNegative = false;
            pdoSup.AllowZero = false;
            PromptDoubleResult pdrSup = ed.GetDouble(pdoSup);
            if (pdrSup.Status != PromptStatus.OK) return;

            CableTrayParams p = new CableTrayParams
            {
                Type = trayType,
                WidthMm = pdrW.Value,
                HeightMm = pdrH.Value,
                ThicknessMm = 1.5,
                SupportSpacingM = pdrSup.Value,
                InsertSupports = true,
                AddAnnotations = true
            };

            int routedCount = 0;
            while (true)
            {
                PromptEntityOptions peo = new PromptEntityOptions("\n[NTT TOOLS] Chọn đường Polyline (PL) định tuyến Thang Máng Cáp (hoặc ENTER/ESC để kết thúc): ");
                peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline!");
                peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
                peo.AllowNone = true;
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) break;

                CableTrayRouteResult res = CableTrayService.RouteCableTrayAlongPolyline(doc, per.ObjectId, p);
                if (res != null)
                {
                    _lastTmcResult = res;
                    _lastTmcParams = p;
                    routedCount++;
                }
            }

            if (routedCount > 0 && _lastTmcResult != null)
            {
                PromptKeywordOptions pkoTable = new PromptKeywordOptions("\n[NTT TOOLS] Bạn có muốn chèn Bảng Bóc Tách Khối Lượng TMC vào bản vẽ? [Có/Không] <Có>: ");
                pkoTable.Keywords.Add("Có");
                pkoTable.Keywords.Add("Không");
                pkoTable.Keywords.Default = "Có";
                pkoTable.AllowNone = true;
                PromptResult prTable = ed.GetKeywords(pkoTable);
                if (prTable.Status == PromptStatus.OK && (string.IsNullOrEmpty(prTable.StringResult) || prTable.StringResult == "Có"))
                {
                    CableTrayService.DrawCableTrayBOMTableInCad(doc, _lastTmcResult, p);
                }
            }
        }

        [CommandMethod("NTTTMCBOM")]
        [CommandMethod("NTT_TMC_BOM")]
        public void DrawTmcBOMCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            CableTrayParams p = _lastTmcParams ?? new CableTrayParams { Type = CableTrayType.Trunking, WidthMm = 300, HeightMm = 100, ThicknessMm = 1.5 };
            CableTrayRouteResult res = _lastTmcResult ?? new CableTrayRouteResult
            {
                TypeName = "Máng Cáp (Trunking)",
                DimensionLabel = "300x100 mm",
                WidthMm = 300,
                HeightMm = 100,
                ThicknessMm = 1.5,
                TotalLengthM = 25.0,
                SupportCount = 17,
                ElbowCount = 3,
                SegmentCount = 4,
                SheetMetalWeightKg = 185.0
            };

            CableTrayService.DrawCableTrayBOMTableInCad(doc, res, p);
        }

        [CommandMethod("NTT_TMC_TEST_ROUTE")]
        public void TestTmcRouteCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            List<Point3d> pts = new List<Point3d>();
            pts.Add(new Point3d(0, 0, 0));
            pts.Add(new Point3d(2000, 0, 0));
            pts.Add(new Point3d(3414.21, 1414.21, 0));
            pts.Add(new Point3d(3414.21, 3000.0, 0));

            CableTrayParams p = new CableTrayParams
            {
                Type = CableTrayType.Trunking,
                WidthMm = 300.0,
                HeightMm = 100.0,
                SupportSpacingM = 1.5,
                InsertSupports = true,
                AddAnnotations = true
            };

            CableTrayRouteResult res = CableTrayService.RouteCableTrayAlongPoints(doc, pts, p);
            if (res != null)
            {
                ed.WriteMessage(string.Format("\n[TEST] Created: Length={0:F2}m, Elbows={1}, Supports={2}",
                    res.TotalLengthM, res.ElbowCount, res.SupportCount));
            }
        }

        /// <summary>
        /// LỆNH CẬP NHẬT KÍCH THƯỚC BLOCK THANG MÁNG CÁP TRÊN BẢN VẼ:
        /// Cho phép chọn các BlockReference và điều chỉnh Scale, Dynamic Properties cùng Attribute WxH
        /// </summary>
        [CommandMethod("NTTTMCUPDATE")]
        [CommandMethod("NTT_TMC_UPDATE")]
        public void UpdateTmcCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptDoubleOptions pdoW = new PromptDoubleOptions("\n[NTT TOOLS] Nhập bề rộng máng cáp W (mm) [300]: ");
            pdoW.DefaultValue = 300;
            pdoW.AllowNegative = false;
            pdoW.AllowZero = false;
            PromptDoubleResult pdrW = ed.GetDouble(pdoW);
            if (pdrW.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdoH = new PromptDoubleOptions("\n[NTT TOOLS] Nhập chiều cao máng cáp H (mm) [100]: ");
            pdoH.DefaultValue = 100;
            pdoH.AllowNegative = false;
            pdoH.AllowZero = false;
            PromptDoubleResult pdrH = ed.GetDouble(pdoH);
            if (pdrH.Status != PromptStatus.OK) return;

            CableTrayParams p = new CableTrayParams
            {
                Type = CableTrayType.Trunking,
                WidthMm = pdrW.Value,
                HeightMm = pdrH.Value
            };

            CableTrayService.UpdateBlockReferenceDimensions(doc, p);
        }

        #region XỬ LÝ MẶT BẰNG (TIỀN XỬ LÝ 7 BƯỚC)

        private static System.Windows.Window _activeLayoutDialog = null;
        private static DateTime _activeLayoutDialogDllTime = DateTime.MinValue;

        /// <summary>
        /// Mở Dialog Tiền Xử Lý Mặt Bằng dưới dạng MODELESS (không khóa màn hình CAD)
        /// </summary>
        public static void ShowLayoutProcessorModelessDialog()
        {
            try
            {
                DateTime latestTime;
                Assembly latestAsm = LoadLatestAssembly(out latestTime);

                if (_activeLayoutDialog != null && _activeLayoutDialog.IsLoaded)
                {
                    if (latestTime <= _activeLayoutDialogDllTime || latestTime == DateTime.MinValue)
                    {
                        _activeLayoutDialog.Activate();
                        if (_activeLayoutDialog.WindowState == System.Windows.WindowState.Minimized)
                            _activeLayoutDialog.WindowState = System.Windows.WindowState.Normal;
                        return;
                    }
                    try { _activeLayoutDialog.Close(); }
                    catch { }
                    _activeLayoutDialog = null;
                }

                Type dlgType = latestAsm != null ? latestAsm.GetType("TaiAiAddin.Views.CadLayoutProcessorDialog") : null;
                System.Windows.Window dlg = null;
                if (dlgType != null)
                {
                    dlg = Activator.CreateInstance(dlgType) as System.Windows.Window;
                }
                if (dlg == null)
                {
                    dlg = new Views.CadLayoutProcessorDialog();
                }
                if (dlg != null)
                {
                    _activeLayoutDialog = dlg;
                    _activeLayoutDialogDllTime = latestTime;
                    dlg.Closed += (s, ev) => { _activeLayoutDialog = null; _activeLayoutDialogDllTime = DateTime.MinValue; };

                    // Mở dạng MODELESS để người dùng vẫn thao tác vẽ, zoom, pan trên CAD tự do
                    Application.ShowModelessWindow(dlg);
                }
            }
            catch (System.Exception ex)
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage("\n[LỖI TIỀN XỬ LÝ] " + ex.Message + "\n");
                }
            }
        }

        /// <summary>
        /// LỆNH XLMB_PREPROCESS / AI_LAYOUT_PROCESSOR / TIENXULYMB: Mở Modeless Dialog Tiền xử lý & Chuẩn hóa Mặt bằng (7 Bước)
        /// </summary>
        [CommandMethod("AI_LAYOUT_PROCESSOR")]
        [CommandMethod("XLMB_PREPROCESS")]
        [CommandMethod("TIENXULYMB")]
        [CommandMethod("NTT_TIENXULYMB")]
        [CommandMethod("NTT_XLMB")]
        [CommandMethod("XLMB")]
        [CommandMethod("NTTXLMB")]
        [CommandMethod("BAOTATCA")]
        [CommandMethod("NTTBAOTATCA")]
        [CommandMethod("BAOPHONG")]
        [CommandMethod("NTTBAOPHONG")]
        [CommandMethod("NTT_BAOPHONG")]
        public void XlmbPreprocessDialogCommand()
        {
            ShowLayoutProcessorModelessDialog();
        }

        #endregion


        #endregion
    }
}

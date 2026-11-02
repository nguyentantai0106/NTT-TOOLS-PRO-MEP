using System;

using System.Collections.Generic;

using System.Globalization;

using Autodesk.AutoCAD.DatabaseServices;

using Autodesk.AutoCAD.Geometry;

using TaiAiAddin.Models;

namespace TaiAiAddin.Services

{

    /// <summary>

    /// Quản lý danh sách nhiều tủ điện trong phiên làm việc và lưu trữ / khôi phục dữ liệu tủ từ bản vẽ CAD (XData).

    /// Hỗ trợ chuyển đổi qua lại giữa các tủ và tự động mở đúng tủ khi double-click trên AutoCAD.

    /// </summary>

    public static class PanelStorageService

    {

        public const string RegAppName = "TAIAI_PANEL";

        private static readonly List<ElectricalPanel> _panels = new List<ElectricalPanel>();

        static PanelStorageService()

        {

            InitDefaultPanels();

        }

        private static void InitDefaultPanels()

        {

            if (_panels.Count == 0)

            {

                ElectricalPanel p1 = ElectricalCalculator.CreateDefaultHvacPanel();

                _panels.Add(p1);

                ElectricalPanel p2 = new ElectricalPanel();

                p2.PanelName = "DB-HVAC-02";

                p2.ParentPanelName = "DB-HVAC-01";

                p2.VoltageV = 380;

                p2.SimultaneousFactor = 0.85;

                p2.PowerFactor = 0.85;

                p2.DefaultCableType = "Cu/XLPE/PVC";

                p2.Incomer = IncomerType.SingleNormal;

                p2.HasCapacitorBank = false;

                p2.Loads.Add(new ElectricalLoad { LoadName = "PAU-01", PowerKW = 22.0, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, CableLength = 30.0 });

                p2.Loads.Add(new ElectricalLoad { LoadName = "AHU-01", PowerKW = 18.5, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, Control = ControlMode.AutoManual, CableLength = 25.0 });

                p2.Loads.Add(new ElectricalLoad { LoadName = "FCU-L02-01", PowerKW = 2.2, Phase = PhaseType.SinglePhase, CableLength = 20.0 });

                p2.Loads.Add(new ElectricalLoad { LoadName = "FCU-L02-02", PowerKW = 2.2, Phase = PhaseType.SinglePhase, CableLength = 20.0 });

                p2.Loads.Add(new ElectricalLoad { LoadName = "FCU-L02-03", PowerKW = 1.5, Phase = PhaseType.SinglePhase, CableLength = 20.0 });

                ElectricalCalculator calc = new ElectricalCalculator();

                calc.CalculatePanel(p2);

                _panels.Add(p2);

            }

        }

        public static List<ElectricalPanel> GetAllPanels()

        {

            if (_panels.Count == 0) InitDefaultPanels();

            foreach (ElectricalPanel p in _panels)

            {

                if (p.Loads != null)

                {

                    foreach (var ld in p.Loads)

                    {

                        if (string.IsNullOrEmpty(ld.BreakerSpec))

                        {

                            ld.BreakerSpec = ld.Phase == PhaseType.ThreePhase ? "MCCB 3P-25kA 40A" : "MCB 1P-6kA 16A";

                        }

                        if (string.IsNullOrEmpty(ld.CableSpec))

                        {

                            ld.UpdateCableSpec();

                        }

                    }

                }

            }

            return _panels;

        }

        public static ElectricalPanel GetPanelByName(string name)

        {

            if (string.IsNullOrEmpty(name)) return null;

            foreach (ElectricalPanel p in _panels)

            {

                if (string.Equals(p.PanelName, name, StringComparison.OrdinalIgnoreCase))

                    return p;

            }

            return null;

        }

        public static void AddOrUpdatePanel(ElectricalPanel panel)

        {

            if (panel == null || string.IsNullOrEmpty(panel.PanelName)) return;

            for (int i = 0; i < _panels.Count; i++)

            {

                if (string.Equals(_panels[i].PanelName, panel.PanelName, StringComparison.OrdinalIgnoreCase))

                {

                    _panels[i] = panel;

                    return;

                }

            }

            _panels.Add(panel);

        }

        public static bool DeletePanel(string name)

        {

            if (string.IsNullOrEmpty(name) || _panels.Count <= 1) return false;

            for (int i = 0; i < _panels.Count; i++)

            {

                if (string.Equals(_panels[i].PanelName, name, StringComparison.OrdinalIgnoreCase))

                {

                    _panels.RemoveAt(i);

                    return true;

                }

            }

            return false;

        }

        /// <summary>

        /// Tạo ResultBuffer chứa toàn bộ thông số cấu hình và danh sách phụ tải của tủ để lưu vào XData của đối tượng CAD

        /// </summary>

        public static ResultBuffer SerializePanelToXData(ElectricalPanel panel, Point3d origin, DrawingParameters p = null)

        {

            if (panel == null) return null;

            ResultBuffer rb = new ResultBuffer();

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName));

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, panel.PanelName ?? ""));

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataXCoordinate, origin));

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataReal, panel.VoltageV));

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataReal, panel.SimultaneousFactor));

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataReal, panel.PowerFactor));

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, panel.DefaultCableType ?? "Cu/XLPE/PVC"));

            // Lưu thông tin mở rộng: Incomer|ParentPanel|HasCapacitor|CapKvar|CapSteps

            string panelExt = string.Format(CultureInfo.InvariantCulture,

                "CFG:{0}|{1}|{2}|{3:0.###}|{4}",

                (int)panel.Incomer,

                panel.ParentPanelName ?? "",

                panel.HasCapacitorBank ? "1" : "0",

                panel.CapacitorBankKvar,

                panel.CapacitorSteps

            );

            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, panelExt));

            if (panel.Loads != null)

            {

                foreach (ElectricalLoad l in panel.Loads)

                {

                    // Định dạng gói phụ tải: STT|Name|Phase|PowerKW|PowerR|PowerY|PowerB|Breaker|Cable|Dol|Starter|Control|Length

                    string loadStr = string.Format(CultureInfo.InvariantCulture,

                        "{0}|{1}|{2}|{3:0.###}|{4:0.###}|{5:0.###}|{6:0.###}|{7}|{8}|{9}|{10}|{11}|{12:0.###}|{13}",

                        l.Stt,

                        l.LoadName ?? "",

                        l.Phase == PhaseType.ThreePhase ? "3" : "1",

                        l.PowerKW,

                        l.PowerR,

                        l.PowerY,

                        l.PowerB,

                        l.BreakerSpec ?? "",

                        l.FullCableString ?? "",

                        l.HasDol ? "1" : "0",

                        (int)l.Starter,

                        (int)l.Control,

                        l.CableLength

                    );

                    rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, "LOAD:" + loadStr));

                }

            }

                        if (p == null && panel.DrawingParams != null) p = panel.DrawingParams;
            if (p != null)
            {
                panel.DrawingParams = p;
                string pStr = string.Format(CultureInfo.InvariantCulture,
                    "PARAMS:{0}|{1}|{2}|{3}|{4:0.###}|{5:0.###}|{6:0.###}|{7:0.###}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}",
                    (int)p.LayoutScheme,
                    (int)p.TreeOrientation,
                    (int)p.SymbolRegion,
                    (int)p.DiagramType,
                    p.ColWidth,
                    p.RowCableHeight,
                    p.RowNameHeight,
                    p.RowPowerHeight,
                    p.SplitCableTwoLines ? "1" : "0",
                    p.IncludeFuse ? "1" : "0",
                    p.IncludeLamps ? "1" : "0",
                    p.IncludeRelay ? "1" : "0",
                    p.IncludeMct ? "1" : "0",
                    p.IncludeSpd ? "1" : "0",
                    p.IncludeSpare ? "1" : "0",
                    p.SpareCount3P,
                    p.SpareCount1P,
                    p.IncludeTitleBlock ? "1" : "0",
                    p.IncludeLegend ? "1" : "0",
                    p.DrawFrontView ? "1" : "0"
                );
                rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, pStr));

                if (p.SldCustomItems != null)
                {
                    foreach (var it in p.SldCustomItems)
                    {
                        string itStr = string.Format(CultureInfo.InvariantCulture,
                            "SLDITEM:{0}|{1:0.###}|{2:0.###}|{3:0.###}|{4:0.###}|{5}|{6}|{7}|{8}|{9}|{10:0.###}|{11:0.###}",
                            it.Id ?? "",
                            it.OffsetX,
                            it.OffsetY,
                            it.RotationAngle,
                            it.Scale,
                            it.IsDeleted ? "1" : "0",
                            it.IsCopied ? "1" : "0",
                            it.DisplayName ?? "",
                            it.ItemType ?? "",
                            it.LoadIndex,
                            it.Width,
                            it.Height
                        );
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, itStr));
                    }
                }
            }

            return rb;

        }

        /// <summary>

        /// Đọc XData từ đối tượng CAD và khôi phục thành đối tượng ElectricalPanel đầy đủ

        /// </summary>

        public static ElectricalPanel DeserializePanelFromXData(ResultBuffer rb, out Point3d origin)

        {

            origin = Point3d.Origin;

            if (rb == null) return null;

            TypedValue[] tvs = rb.AsArray();

            if (tvs == null || tvs.Length < 2) return null;

            ElectricalPanel panel = new ElectricalPanel();

            panel.Loads.Clear();

            int state = 0;

            foreach (TypedValue tv in tvs)

            {

                if (tv.TypeCode == (short)DxfCode.ExtendedDataRegAppName)

                {

                    continue;

                }

                if (state == 0 && tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString)

                {

                    panel.PanelName = tv.Value.ToString();

                    state++;

                }

                else if (state == 1 && tv.TypeCode == (short)DxfCode.ExtendedDataXCoordinate)

                {

                    origin = (Point3d)tv.Value;

                    state++;

                }

                else if (state == 2 && tv.TypeCode == (short)DxfCode.ExtendedDataReal)

                {

                    panel.VoltageV = Convert.ToDouble(tv.Value);

                    state++;

                }

                else if (state == 3 && tv.TypeCode == (short)DxfCode.ExtendedDataReal)

                {

                    panel.SimultaneousFactor = Convert.ToDouble(tv.Value);

                    state++;

                }

                else if (state == 4 && tv.TypeCode == (short)DxfCode.ExtendedDataReal)

                {

                    panel.PowerFactor = Convert.ToDouble(tv.Value);

                    state++;

                }

                else if (state == 5 && tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString && !tv.Value.ToString().StartsWith("LOAD:") && !tv.Value.ToString().StartsWith("CFG:"))

                {

                    panel.DefaultCableType = tv.Value.ToString();

                    state++;

                }

                else if (tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString)

                {

                    string str = tv.Value.ToString();

                    if (str.StartsWith("CFG:"))

                    {

                        string data = str.Substring(4);

                        string[] parts = data.Split('|');

                        if (parts.Length >= 5)

                        {

                            int incVal;

                            if (int.TryParse(parts[0], out incVal)) panel.Incomer = (IncomerType)incVal;

                            panel.ParentPanelName = parts[1];

                            panel.HasCapacitorBank = parts[2] == "1";

                            double ck;

                            if (double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out ck)) panel.CapacitorBankKvar = ck;

                            int cs;

                            if (int.TryParse(parts[4], out cs)) panel.CapacitorSteps = cs;

                        }

                    }

                    else if (str.StartsWith("LOAD:"))

                    {

                        string data = str.Substring(5);

                        string[] parts = data.Split('|');

                        if (parts.Length >= 10)

                        {

                            ElectricalLoad load = new ElectricalLoad();

                            int stt;

                            int.TryParse(parts[0], out stt);

                            load.Stt = stt;

                            load.LoadName = parts[1];

                            load.Phase = parts[2] == "3" ? PhaseType.ThreePhase : PhaseType.SinglePhase;

                            double p, pr, py, pb;

                            double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out p);

                            double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out pr);

                            double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out py);

                            double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out pb);

                            load.PowerKW = p;

                            load.PowerR = pr;

                            load.PowerY = py;

                            load.PowerB = pb;

                            load.BreakerSpec = parts[7];

                            load.FullCableString = parts[8];

                            load.HasDol = parts[9] == "1";

                            // Mở rộng Starter, Control, Length nếu có

                            if (parts.Length >= 13)

                            {

                                int stVal, ctrlVal;

                                if (int.TryParse(parts[10], out stVal)) load.Starter = (StarterType)stVal;

                                if (int.TryParse(parts[11], out ctrlVal)) load.Control = (ControlMode)ctrlVal;

                                double len;

                                if (double.TryParse(parts[12], NumberStyles.Any, CultureInfo.InvariantCulture, out len)) load.CableLength = len;

                            }

                            panel.Loads.Add(load);
                        }
                    }
                    else if (str.StartsWith("PARAMS:"))
                        {
                            try
                            {
                                string[] pParts = str.Substring(7).Split('|');
                                if (panel.DrawingParams == null) panel.DrawingParams = new DrawingParameters();
                                DrawingParameters p = panel.DrawingParams;
                                if (pParts.Length >= 4)
                                {
                                    int ls, to, sr, dt;
                                    if (int.TryParse(pParts[0], out ls)) p.LayoutScheme = (LayoutScheme)ls;
                                    if (int.TryParse(pParts[1], out to)) p.TreeOrientation = (TreeOrientation)to;
                                    if (int.TryParse(pParts[2], out sr)) p.SymbolRegion = (SymbolRegion)sr;
                                    if (int.TryParse(pParts[3], out dt)) p.DiagramType = (SldDiagramType)dt;
                                }
                                if (pParts.Length >= 9)
                                {
                                    double cw, rch, rnh, rph;
                                    if (double.TryParse(pParts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out cw)) p.ColWidth = cw;
                                    if (double.TryParse(pParts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out rch)) p.RowCableHeight = rch;
                                    if (double.TryParse(pParts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out rnh)) p.RowNameHeight = rnh;
                                    if (double.TryParse(pParts[7], NumberStyles.Any, CultureInfo.InvariantCulture, out rph)) p.RowPowerHeight = rph;
                                    p.SplitCableTwoLines = (pParts[8] == "1");
                                }
                                if (pParts.Length >= 20)
                                {
                                    p.IncludeFuse = (pParts[9] == "1");
                                    p.IncludeLamps = (pParts[10] == "1");
                                    p.IncludeRelay = (pParts[11] == "1");
                                    p.IncludeMct = (pParts[12] == "1");
                                    p.IncludeSpd = (pParts[13] == "1");
                                    p.IncludeSpare = (pParts[14] == "1");
                                    int sp3, sp1;
                                    if (int.TryParse(pParts[15], out sp3)) p.SpareCount3P = sp3;
                                    if (int.TryParse(pParts[16], out sp1)) p.SpareCount1P = sp1;
                                    p.IncludeTitleBlock = (pParts[17] == "1");
                                    p.IncludeLegend = (pParts[18] == "1");
                                    p.DrawFrontView = (pParts[19] == "1");
                                }
                            }
                            catch { }
                        }
                        else if (str.StartsWith("SLDITEM:"))
                        {
                            try
                            {
                                string[] itParts = str.Substring(8).Split('|');
                                if (panel.DrawingParams == null) panel.DrawingParams = new DrawingParameters();
                                if (panel.DrawingParams.SldCustomItems == null) panel.DrawingParams.SldCustomItems = new System.Collections.Generic.List<SldInteractiveItem>();
                                if (itParts.Length >= 7)
                                {
                                    string id = itParts[0];
                                    double offX, offY, rot, scale;
                                    double.TryParse(itParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out offX);
                                    double.TryParse(itParts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out offY);
                                    double.TryParse(itParts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out rot);
                                    double.TryParse(itParts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out scale);
                                    bool isDel = (itParts[5] == "1");
                                    bool isCopied = (itParts[6] == "1");
                                    string disp = (itParts.Length > 7) ? itParts[7] : id;
                                    string itType = (itParts.Length > 8) ? itParts[8] : "BranchBreaker";
                                    int ldIdx = -1;
                                    if (itParts.Length > 9) int.TryParse(itParts[9], out ldIdx);
                                    double w = 30.0, h = 30.0;
                                    if (itParts.Length > 10) double.TryParse(itParts[10], NumberStyles.Any, CultureInfo.InvariantCulture, out w);
                                    if (itParts.Length > 11) double.TryParse(itParts[11], NumberStyles.Any, CultureInfo.InvariantCulture, out h);

                                    SldInteractiveItem item = new SldInteractiveItem(id, disp, itType, w, h)
                                    {
                                        OffsetX = offX,
                                        OffsetY = offY,
                                        RotationAngle = rot,
                                        Scale = (scale > 0) ? scale : 1.0,
                                        IsDeleted = isDel,
                                        IsCopied = isCopied,
                                        LoadIndex = ldIdx
                                    };
                                    panel.DrawingParams.SldCustomItems.Add(item);
                                }
                            }
                            catch { }
                        }
                }
            }

            ElectricalCalculator calc = new ElectricalCalculator();

            calc.CalculatePanel(panel);

            return panel;

        }

    }

}

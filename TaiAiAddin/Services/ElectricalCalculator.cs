using System;

using System.Collections.Generic;

using System.Linq;

using TaiAiAddin.Models;

namespace TaiAiAddin.Services

{

    public class ElectricalCalculator

    {

        private static readonly int[] StandardBreakerRatings = new int[]

        {

            16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 300, 350, 400, 500, 630, 800, 1000, 1250, 1600

        };

        private static readonly string[] StandardCableSizes = new string[]

        {

            "1.5", "2.5", "4", "6", "10", "16", "25", "35", "50", "70", "95", "120", "150", "185", "240", "300"

        };

        // Bảng điện trở một chiều R (Ohm/km) của ruột đồng ở 75°C theo TCVN / IEC 60228

        private static readonly Dictionary<string, double> CopperResistanceTable = new Dictionary<string, double>

        {

            { "1.5", 14.8 },

            { "2.5", 8.91 },

            { "4",   5.57 },

            { "6",   3.71 },

            { "10",  2.24 },

            { "16",  1.41 },

            { "25",  0.889 },

            { "35",  0.641 },

            { "50",  0.473 },

            { "70",  0.328 },

            { "95",  0.236 },

            { "120", 0.188 },

            { "150", 0.153 },

            { "185", 0.123 },

            { "240", 0.094 },

            { "300", 0.077 }

        };

        private const double StandardReactanceX = 0.08; // Cảm kháng X xấp xỉ 0.08 Ohm/km

        /// <summary>

        /// Thuật toán Cân Pha Tự Động Thông Minh cho tất cả các phụ tải 1 pha (LPT Heuristic)

        /// </summary>

        public void AutoBalancePhases(ElectricalPanel panel)

        {

            if (panel == null || panel.Loads == null || panel.Loads.Count == 0) return;

            // 1. Tính tổng công suất cơ sở từ các phụ tải 3 pha

            double sumR = 0;

            double sumY = 0;

            double sumB = 0;

            foreach (ElectricalLoad load in panel.Loads)

            {

                if (load.IsSpare || (!string.IsNullOrEmpty(load.LoadName) && (load.LoadName.ToUpper().Contains("SPARE") || load.LoadName.ToUpper().Contains("DỰ PHÒNG"))))

                {

                    continue;

                }

                if (load.Phase == PhaseType.ThreePhase)

                {

                    double p3 = load.PowerKW / 3.0;

                    load.PowerR = p3;

                    load.PowerY = p3;

                    load.PowerB = p3;

                    load.AssignedPhase = "3P";

                    sumR += p3;

                    sumY += p3;

                    sumB += p3;

                }

            }

            // 2. Lấy danh sách phụ tải 1 pha và sắp xếp giảm dần theo công suất (Greedy LPT)

            List<ElectricalLoad> singlePhaseLoads = new List<ElectricalLoad>();

            foreach (ElectricalLoad load in panel.Loads)

            {

                if (load.IsSpare || (!string.IsNullOrEmpty(load.LoadName) && (load.LoadName.ToUpper().Contains("SPARE") || load.LoadName.ToUpper().Contains("DỰ PHÒNG"))))

                {

                    continue;

                }

                if (load.Phase == PhaseType.SinglePhase)

                {

                    singlePhaseLoads.Add(load);

                }

            }

            singlePhaseLoads.Sort(delegate(ElectricalLoad a, ElectricalLoad b)

            {

                return b.PowerKW.CompareTo(a.PowerKW);

            });

            // 3. Phân bổ thông minh vào pha có tổng tải nhỏ nhất

            foreach (ElectricalLoad load in singlePhaseLoads)

            {

                double pVal = load.PowerKW;

                if (pVal <= 0.0001) continue;

                if (sumR <= sumY && sumR <= sumB)

                {

                    load.AssignedPhase = "R";

                    load.PowerR = pVal;

                    load.PowerY = 0;

                    load.PowerB = 0;

                    sumR += pVal;

                }

                else if (sumY <= sumR && sumY <= sumB)

                {

                    load.AssignedPhase = "Y";

                    load.PowerY = pVal;

                    load.PowerR = 0;

                    load.PowerB = 0;

                    sumY += pVal;

                }

                else

                {

                    load.AssignedPhase = "B";

                    load.PowerB = pVal;

                    load.PowerR = 0;

                    load.PowerY = 0;

                    sumB += pVal;

                }

                load.PowerKW = pVal;

            }

            // 4. Tính toán lại toàn bộ thông số chọn Aptomat, Cáp, Sụt áp và Vỏ tủ

            CalculatePanel(panel);

        }

        public static double GetPhaseUnbalancePercentage(ElectricalPanel panel)

        {

            if (panel == null) return 0;

            double avg = (panel.PhasePowerR + panel.PhasePowerY + panel.PhasePowerB) / 3.0;

            if (avg <= 0.001) return 0;

            double maxDiff = Math.Max(Math.Abs(panel.PhasePowerR - avg),

                Math.Max(Math.Abs(panel.PhasePowerY - avg), Math.Abs(panel.PhasePowerB - avg)));

            return (maxDiff / avg) * 100.0;

        }

        /// <summary>

        /// Tính toán toàn bộ thông số kỹ thuật cho tủ điện:

        /// - Dòng điện Itt từng lộ & tổng tủ

        /// - Chọn MCB/MCCB & Cáp điện

        /// - Tính toán sụt áp ΔU% theo chiều dài L

        /// - Tính toán Tụ bù công suất phản kháng

        /// - Tính toán dòng ngắn mạch Isc & gợi ý Icu

        /// - Ước tính kích thước vỏ tủ điện HxWxD

        /// </summary>

        public void CalculatePanel(ElectricalPanel panel)

        {

            if (panel == null || panel.Loads == null || panel.Loads.Count == 0) return;

            double sumR = 0;

            double sumY = 0;

            double sumB = 0;

            double totalInstalled = 0;

            int circuitIdx = 1;

            // Duyệt qua từng phụ tải

            foreach (ElectricalLoad load in panel.Loads)

            {

                load.Stt = circuitIdx++;

                bool isSpare = load.IsSpare || (!string.IsNullOrEmpty(load.LoadName) && (load.LoadName.ToUpper().Contains("SPARE") || load.LoadName.ToUpper().Contains("DỰ PHÒNG")));

                if (isSpare)

                {

                    load.IsSpare = true;

                    load.PowerKW = 0.0;

                    load.PowerR = 0.0;

                    load.PowerY = 0.0;

                    load.PowerB = 0.0;

                    load.CurrentA = 0.0;

                    if (string.IsNullOrEmpty(load.CircuitNumber) || load.CircuitNumber.StartsWith("C"))

                    {

                        load.CircuitNumber = "SPARE";

                    }

                    if (string.IsNullOrEmpty(load.BreakerSpec))

                    {

                        load.BreakerSpec = load.Phase == PhaseType.ThreePhase ? "MCCB 3P-25kA 40A" : "MCB 1P-6kA 16A";

                    }

                    load.CableSpec = "-";

                    if (!load.DrawOutgoingWire)

                    {

                        load.FullCableString = "";

                    }

                    continue;

                }

                load.CircuitNumber = string.Format("C{0:D2}", load.Stt);

                if (string.IsNullOrEmpty(load.CableType))

                {

                    load.CableType = string.IsNullOrEmpty(panel.DefaultCableType) ? "Cu/XLPE/PVC" : panel.DefaultCableType;

                }

                if (load.Phase == PhaseType.ThreePhase)

                {

                    load.AssignedPhase = "3P";

                    if (Math.Abs(load.PowerR + load.PowerY + load.PowerB - load.PowerKW) > 0.01)

                    {

                        double p3 = load.PowerKW / 3.0;

                        load.PowerR = p3;

                        load.PowerY = p3;

                        load.PowerB = p3;

                    }

                    sumR += load.PowerR;

                    sumY += load.PowerY;

                    sumB += load.PowerB;

                    totalInstalled += load.PowerKW;

                    // Dòng điện tính toán 3 pha: I = P / (sqrt(3) * U * cosPhi)

                    double maxPhaseP = Math.Max(load.PowerR, Math.Max(load.PowerY, load.PowerB));

                    double equivThreePhaseP = maxPhaseP * 3.0;

                    double current = (equivThreePhaseP * 1000.0) / (Math.Sqrt(3.0) * panel.VoltageV * panel.PowerFactor);

                    load.CurrentA = current;

                    // Chọn MCCB và Cáp 3 pha

                    SelectBreakerAndCable(load, true);

                    // Tính toán sụt áp ΔU%

                    CalculateVoltageDrop(load, panel.VoltageV, panel.PowerFactor);

                }

                else // Phụ tải 1 pha

                {

                    totalInstalled += load.PowerKW;

                    if (load.PowerR > 0.001)

                    {

                        load.AssignedPhase = "R";

                        load.PowerKW = load.PowerR;

                        load.PowerY = 0;

                        load.PowerB = 0;

                        sumR += load.PowerKW;

                    }

                    else if (load.PowerY > 0.001)

                    {

                        load.AssignedPhase = "Y";

                        load.PowerKW = load.PowerY;

                        load.PowerR = 0;

                        load.PowerB = 0;

                        sumY += load.PowerKW;

                    }

                    else if (load.PowerB > 0.001)

                    {

                        load.AssignedPhase = "B";

                        load.PowerKW = load.PowerB;

                        load.PowerR = 0;

                        load.PowerY = 0;

                        sumB += load.PowerKW;

                    }

                    else

                    {

                        // Phân bổ tự động

                        if (sumR <= sumY && sumR <= sumB)

                        {

                            load.AssignedPhase = "R";

                            load.PowerR = load.PowerKW;

                            load.PowerY = 0;

                            load.PowerB = 0;

                            sumR += load.PowerKW;

                        }

                        else if (sumY <= sumR && sumY <= sumB)

                        {

                            load.AssignedPhase = "Y";

                            load.PowerR = 0;

                            load.PowerY = load.PowerKW;

                            load.PowerB = 0;

                            sumY += load.PowerKW;

                        }

                        else

                        {

                            load.AssignedPhase = "B";

                            load.PowerR = 0;

                            load.PowerY = 0;

                            load.PowerB = load.PowerKW;

                            sumB += load.PowerKW;

                        }

                    }

                    // Dòng điện tính toán 1 pha: I = P / (220 * cosPhi)

                    double current = (load.PowerKW * 1000.0) / (220.0 * panel.PowerFactor);

                    load.CurrentA = current;

                    // Chọn MCB và Cáp 1 pha

                    SelectBreakerAndCable(load, false);

                    // Tính toán sụt áp ΔU%

                    CalculateVoltageDrop(load, 220.0, panel.PowerFactor);

                }

            }

            // Tổng hợp công suất toàn tủ

            panel.TotalInstalledPowerKW = totalInstalled;

            panel.TotalCalculatedPowerKW = totalInstalled * panel.SimultaneousFactor * panel.UsageFactor;

            panel.PhasePowerR = sumR;

            panel.PhasePowerY = sumY;

            panel.PhasePowerB = sumB;

            // Dòng điện tổng tủ: Itt = Ptt / (sqrt(3) * 380 * cosPhi)

            panel.TotalCurrentA = (panel.TotalCalculatedPowerKW * 1000.0) / (Math.Sqrt(3.0) * panel.VoltageV * panel.PowerFactor);

            // Chọn Main MCCB & Cáp nguồn tổng

            int mainCbRating;

            panel.MainBreakerSpec = SelectMainBreaker(panel.TotalCurrentA, out mainCbRating);

            panel.MainCableSpec = SelectMainCable(mainCbRating, panel.DefaultCableType);

            // Tính toán tụ bù công suất phản kháng (Capacitor Bank)

            CalculateCapacitorBank(panel);

            // Tính toán dòng ngắn mạch và đề xuất Icu

            CalculateShortCircuit(panel);

            // Ước tính kích thước vỏ tủ HxWxD

            EstimateEnclosureDimensions(panel);

        }

        /// <summary>

        /// Tính sụt áp ΔU% theo chuẩn TCVN 7447 / IEC 60364:

        /// 3P: ΔU = sqrt(3) * Itt * (L / 1000) * (R * cosPhi + X * sinPhi)

        /// 1P: ΔU = 2 * Itt * (L / 1000) * (R * cosPhi + X * sinPhi)

        /// </summary>

        public static void CalculateVoltageDrop(ElectricalLoad load, double voltageV, double cosPhi)

        {

            if (load == null) return;

            if (load.CurrentA <= 0.001 || load.CableLength <= 0.001)

            {

                load.VoltageDropPercent = 0.0;

                load.IsVoltageDropExceeded = false;

                return;

            }

            string size = string.IsNullOrEmpty(load.ConductorSize) ? "2.5" : load.ConductorSize;

            double r = 8.91;

            if (CopperResistanceTable.ContainsKey(size))

            {

                r = CopperResistanceTable[size];

            }

            double x = StandardReactanceX;

            cosPhi = Math.Max(0.1, Math.Min(1.0, cosPhi));

            double sinPhi = Math.Sqrt(1.0 - cosPhi * cosPhi);

            double lKm = load.CableLength / 1000.0;

            double deltaU = 0.0;

            if (load.Phase == PhaseType.ThreePhase)

            {

                // 3 Pha

                deltaU = Math.Sqrt(3.0) * load.CurrentA * lKm * (r * cosPhi + x * sinPhi);

                load.VoltageDropPercent = (deltaU / (voltageV > 0 ? voltageV : 380.0)) * 100.0;

            }

            else

            {

                // 1 Pha

                deltaU = 2.0 * load.CurrentA * lKm * (r * cosPhi + x * sinPhi);

                load.VoltageDropPercent = (deltaU / 220.0) * 100.0;

            }

            // Ngưỡng cho phép: Chiếu sáng 3%, Động lực 5%

            double threshold = (load.LoadType == "Lighting") ? 3.0 : 5.0;

            load.IsVoltageDropExceeded = (load.VoltageDropPercent > threshold);

        }

        /// <summary>

        /// Tự động tăng tiết diện dây cáp nếu sụt áp vượt quá ngưỡng cho phép

        /// </summary>

        public bool AutoUpsizeCableForVoltageDrop(ElectricalLoad load, double voltageV, double cosPhi)

        {

            if (load == null) return false;

            double threshold = (load.LoadType == "Lighting") ? 3.0 : 5.0;

            CalculateVoltageDrop(load, voltageV, cosPhi);

            if (!load.IsVoltageDropExceeded) return false;

            int currentIdx = Array.IndexOf(StandardCableSizes, load.ConductorSize);

            if (currentIdx < 0) currentIdx = 1; // Mặc định 2.5

            bool upgraded = false;

            while (load.VoltageDropPercent > threshold && currentIdx < StandardCableSizes.Length - 1)

            {

                currentIdx++;

                load.ConductorSize = StandardCableSizes[currentIdx];

                load.GroundSize = load.ConductorSize;

                load.UpdateCableSpec();

                CalculateVoltageDrop(load, voltageV, cosPhi);

                upgraded = true;

            }

            return upgraded;

        }

        /// <summary>

        /// Tính toán dung lượng tụ bù công suất phản kháng:

        /// Q = Ptt * (tan(phi1) - tan(phi2))

        /// </summary>

        public static void CalculateCapacitorBank(ElectricalPanel panel)

        {

            if (panel == null) return;

            if (panel.TotalCalculatedPowerKW <= 0.001)

            {

                panel.CapacitorBankKvar = 0;

                return;

            }

            double cos1 = Math.Max(0.5, Math.Min(0.99, panel.PowerFactor));

            double cos2 = Math.Max(cos1, Math.Min(0.99, panel.TargetCosPhi > 0 ? panel.TargetCosPhi : 0.95));

            double tan1 = Math.Tan(Math.Acos(cos1));

            double tan2 = Math.Tan(Math.Acos(cos2));

            double qReq = panel.TotalCalculatedPowerKW * (tan1 - tan2);

            if (qReq <= 0)

            {

                panel.CapacitorBankKvar = 0;

                panel.CapacitorSteps = 0;

                return;

            }

            // Làm tròn đến bậc 5 kVAR hoặc 10 kVAR chuẩn công nghiệp

            double qRounded = Math.Ceiling(qReq / 5.0) * 5.0;

            panel.CapacitorBankKvar = qRounded;

            if (qRounded <= 20) panel.CapacitorSteps = 2;

            else if (qRounded <= 40) panel.CapacitorSteps = 4;

            else if (qRounded <= 80) panel.CapacitorSteps = 6;

            else if (qRounded <= 150) panel.CapacitorSteps = 8;

            else panel.CapacitorSteps = 12;

        }

        /// <summary>

        /// Tính toán dòng ngắn mạch cực đại tại đầu cực tủ điện và đề xuất Icu của MCCB Tổng

        /// Isc = Strafo / (sqrt(3) * Ud * (uk / 100))

        /// </summary>

        public static void CalculateShortCircuit(ElectricalPanel panel)

        {

            if (panel == null) return;

            double sTrafo = panel.TransformerKVA > 0 ? panel.TransformerKVA : 1000.0;

            double ukPercent = 5.0; // Máy biến áp phân phối tiêu chuẩn uk = 4% - 5%

            double iSc = sTrafo / (Math.Sqrt(3.0) * 0.4 * (ukPercent / 100.0));

            panel.ShortCircuitKA = Math.Round(iSc / 1000.0, 1);

            if (panel.ShortCircuitKA <= 18.0) panel.SuggestedMainIcu = "25kA";

            else if (panel.ShortCircuitKA <= 25.0) panel.SuggestedMainIcu = "36kA";

            else if (panel.ShortCircuitKA <= 36.0) panel.SuggestedMainIcu = "50kA";

            else if (panel.ShortCircuitKA <= 50.0) panel.SuggestedMainIcu = "65kA";

            else panel.SuggestedMainIcu = "85kA";

        }

        /// <summary>

        /// Dự toán kích thước vỏ tủ điện (Cao x Rộng x Sâu) mm

        /// </summary>

        public static void EstimateEnclosureDimensions(ElectricalPanel panel)

        {

            if (panel == null) return;

            int loadCount = panel.Loads != null ? panel.Loads.Count : 6;

            // Bề rộng W: Mỗi lộ chiếm ~70mm + khoang tổng 400mm + ATS (nếu có 300mm)

            double w = 400.0 + (loadCount * 70.0);

            if (panel.Incomer == IncomerType.DualATS || panel.Incomer == IncomerType.DualInterlock)

            {

                w += 350.0;

            }

            if (panel.HasCapacitorBank)

            {

                w += 300.0;

            }

            w = Math.Max(700.0, Math.Ceiling(w / 100.0) * 100.0);

            // Chiều cao H: Theo số lộ và dòng định mức

            double h = 1200.0;

            if (panel.TotalCurrentA > 630 || loadCount > 12) h = 1800.0;

            else if (panel.TotalCurrentA > 250 || loadCount > 8) h = 1500.0;

            // Chiều sâu D: Tùy theo dòng định mức MCCB

            double d = 300.0;

            if (panel.TotalCurrentA > 630) d = 500.0;

            else if (panel.TotalCurrentA > 250) d = 400.0;

            panel.EstimatedWidth = w;

            panel.EstimatedHeight = h;

            panel.EstimatedDepth = d;

        }

        private void SelectBreakerAndCable(ElectricalLoad load, bool isThreePhase)

        {

            double targetCurrent = load.CurrentA * 1.25;

            int cbRating = StandardBreakerRatings[StandardBreakerRatings.Length - 1];

            foreach (int rating in StandardBreakerRatings)

            {

                if (rating >= targetCurrent)

                {

                    cbRating = rating;

                    break;

                }

            }

            if (load.IsBreakerUserDefined && !string.IsNullOrEmpty(load.BreakerSpec))
            {
                var m = System.Text.RegularExpressions.Regex.Match(load.BreakerSpec, @"(\d+)\s*A");
                if (m.Success)
                {
                    int parsedR;
                    if (int.TryParse(m.Groups[1].Value, out parsedR) && parsedR > 0) cbRating = parsedR;
                }
            }

            string icu = "10kA";

            if (cbRating >= 300) icu = "36kA";

            else if (cbRating >= 100) icu = "25kA";

            string conductorSize;

            string groundSize;

            if (isThreePhase)

            {

                if (!load.IsBreakerUserDefined)
                {
                    load.BreakerType = "MCCB 3P";
                    load.UpdateBreakerSpecFromType(cbRating);
                }

                if (cbRating <= 20) { conductorSize = "2.5"; groundSize = "2.5"; }

                else if (cbRating <= 25) { conductorSize = "4"; groundSize = "4"; }

                else if (cbRating <= 32) { conductorSize = "6"; groundSize = "6"; }

                else if (cbRating <= 50) { conductorSize = "10"; groundSize = "10"; }

                else if (cbRating <= 63) { conductorSize = "16"; groundSize = "16"; }

                else if (cbRating <= 100) { conductorSize = "35"; groundSize = "16"; }

                else if (cbRating <= 125) { conductorSize = "50"; groundSize = "25"; }

                else if (cbRating <= 160) { conductorSize = "70"; groundSize = "35"; }

                else if (cbRating <= 200) { conductorSize = "95"; groundSize = "50"; }

                else if (cbRating <= 250) { conductorSize = "120"; groundSize = "70"; }

                else if (cbRating <= 300) { conductorSize = "185"; groundSize = "95"; }

                else if (cbRating <= 400) { conductorSize = "240"; groundSize = "120"; }

                else { conductorSize = "300"; groundSize = "150"; }

            }

            else

            {

                // 1 Pha

                if (!load.IsBreakerUserDefined)
                {
                    load.BreakerType = (cbRating <= 63) ? "MCB 1P" : "MCB 2P";
                    load.UpdateBreakerSpecFromType(cbRating);
                }

                if (cbRating <= 20) { conductorSize = "2.5"; groundSize = "2.5"; }

                else if (cbRating <= 32) { conductorSize = "4"; groundSize = "4"; }

                else if (cbRating <= 50) { conductorSize = "6"; groundSize = "6"; }

                else if (cbRating <= 63) { conductorSize = "10"; groundSize = "10"; }

                else { conductorSize = "16"; groundSize = "16"; }

            }

            load.ConductorSize = conductorSize;

            load.GroundSize = groundSize;

            load.UpdateCableSpec();

        }

        private string SelectMainBreaker(double totalCurrent, out int cbRating)

        {

            double target = totalCurrent * 1.25;

            cbRating = 800;

            foreach (int rating in StandardBreakerRatings)

            {

                if (rating >= target)

                {

                    cbRating = rating;

                    break;

                }

            }

            string icu = cbRating >= 630 ? "50kA" : (cbRating >= 250 ? "36kA" : "25kA");

            return string.Format("MCCB 3P-{0} {1}A", icu, cbRating);

        }

        public string SelectMainCable(int cbRating, string cableType)

        {

            if (string.IsNullOrEmpty(cableType)) cableType = "Cu/XLPE/PVC";

            string spec = "";

            if (cbRating <= 25) spec = "4Cx4mm2 + E4mm2";

            else if (cbRating <= 32) spec = "4Cx6mm2 + E6mm2";

            else if (cbRating <= 50) spec = "4Cx10mm2 + E10mm2";

            else if (cbRating <= 63) spec = "4Cx16mm2 + E16mm2";

            else if (cbRating <= 100) spec = "4Cx35mm2 + E16mm2";

            else if (cbRating <= 125) spec = "4Cx50mm2 + E25mm2";

            else if (cbRating <= 160) spec = "4Cx70mm2 + E35mm2";

            else if (cbRating <= 200) spec = "4Cx95mm2 + E50mm2";

            else if (cbRating <= 250) spec = "4Cx120mm2 + E70mm2";

            else if (cbRating <= 300) spec = "4Cx185mm2 + E95mm2";

            else if (cbRating <= 350) spec = "4Cx240mm2 + E120mm2";

            else if (cbRating <= 400) spec = "4x1Cx300mm2 + E150mm2";

            else if (cbRating <= 500) spec = "2x(4x1Cx150mm2) + E95mm2";

            else if (cbRating <= 630) spec = "2x(4x1Cx185mm2) + E95mm2";

            else if (cbRating <= 800) spec = "2x(4x1Cx240mm2) + E120mm2";

            else if (cbRating <= 1000) spec = "3x(4x1Cx240mm2) + E150mm2";

            else if (cbRating <= 1250) spec = "4x(4x1Cx240mm2) + E150mm2";

            else spec = "4x(4x1Cx300mm2) + E185mm2";

            return string.Format("{0} {1}", cableType, spec);

        }

        public static ElectricalPanel CreateDefaultHvacPanel()

        {

            ElectricalPanel panel = new ElectricalPanel();

            panel.PanelName = "DB-HVAC-01";

            panel.VoltageV = 380;

            panel.SimultaneousFactor = 0.85;

            panel.PowerFactor = 0.85;

            panel.Incomer = IncomerType.SingleNormal;

            panel.HasCapacitorBank = false;

            panel.Loads.Add(new ElectricalLoad { LoadName = "MCC-HVAC-SAY-01", PowerKW = 42.8, Phase = PhaseType.ThreePhase, Starter = StarterType.StarDelta, CableLength = 35.0 });

            panel.Loads.Add(new ElectricalLoad { LoadName = "MCC-FAN-CB-01", PowerKW = 15.0, Phase = PhaseType.ThreePhase, Starter = StarterType.DOL, Control = ControlMode.AutoManual, CableLength = 20.0 });

            panel.Loads.Add(new ElectricalLoad { LoadName = "MCC-HVAC-ROT DC-01", PowerKW = 127.452, Phase = PhaseType.ThreePhase, Starter = StarterType.VFD, CableLength = 45.0 });

            panel.Loads.Add(new ElectricalLoad { LoadName = "FCU-L01-01", PowerKW = 2.2, Phase = PhaseType.SinglePhase, CableLength = 25.0 });

            panel.Loads.Add(new ElectricalLoad { LoadName = "FCU-L01-02", PowerKW = 1.5, Phase = PhaseType.SinglePhase, CableLength = 30.0 });

            panel.Loads.Add(new ElectricalLoad { LoadName = "MCC-FAN-NL-01", PowerKW = 25.8, Phase = PhaseType.ThreePhase, Starter = StarterType.SoftStarter, CableLength = 40.0 });

            ElectricalCalculator calc = new ElectricalCalculator();

            calc.CalculatePanel(panel);

            return panel;

        }

    }

}

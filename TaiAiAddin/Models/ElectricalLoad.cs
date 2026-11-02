﻿﻿using System;

using System.ComponentModel;

using System.Windows.Media;

namespace TaiAiAddin.Models

{

    public enum PhaseType

    {

        ThreePhase,

        SinglePhase

    }

    /// <summary>

    /// Các phương pháp khởi động động cơ

    /// </summary>

    public enum StarterType

    {

        Direct,      // Trực tiếp (không có bộ khởi động đặc biệt)

        DOL,         // Khởi động trực tiếp Direct On Line

        StarDelta,   // Sao - Tam giác (Y-Δ Starter, cấp 2 đường cáp 2x3C)

        VFD,         // Biến tần (Variable Frequency Drive / Inverter)

        SoftStarter  // Khởi động mềm (Soft Starter)

    }

    /// <summary>

    /// Chế độ điều khiển lộ ra

    /// </summary>

    public enum ControlMode
    {
        None,           // Tiêu chuẩn / Không hiển thị
        AutoManual,     // Công tắc Hand-Off-Auto (H-O-A)
        FloatSwitch,    // Phao báo mức nước (FS)
        PressureSwitch, // Rơ le công tắc áp suất (PS)
        BMS,            // Hệ thống điều khiển tòa nhà BMS
        PLC,            // Bộ điều khiển lập trình PLC
        Timer           // Bộ định thời gian TIMER
    }

    /// <summary>

    /// Cấu hình ngõ vào nguồn tổng tủ điện

    /// </summary>

    public enum IncomerType

    {

        SingleNormal,   // 1 Nguồn cấp đơn (Lưới EVN)

        DualATS,        // Nguồn kép với bộ chuyển nguồn tự động ATS (EVN + Máy phát GEN)

        DualInterlock,  // Nguồn kép với 2 MCCB khóa liên động cơ điện

        BusCoupler      // Hệ thống 2 nguồn có máy cắt phân đoạn thanh cái (Bus Coupler)

    }

    /// <summary>

    /// Thông tin chi tiết một lộ phụ tải điện (Hỗ trợ 1P/3P, Starter DOL/Y-Δ/VFD/SS, Sụt áp & Điều khiển)

    /// </summary>

    public class ElectricalLoad : INotifyPropertyChanged

    {

        private int _stt;

        private string _loadName;

        private double _powerKw;

        private PhaseType _phase;

        private string _circuitNumber;

        private string _assignedPhase;

        private double _powerR;

        private double _powerY;

        private double _powerB;

        private double _currentA;

        private string _breakerSpec;

        private string _cableSpec;

        private string _groundWireSpec;

        private string _cableType;

        private string _coreType;

        private string _conductorSize;

        private string _groundSize;

        private bool _hasDol;

        private StarterType _starter;

        private ControlMode _control;

        private double _cableLength;

        private double _voltageDropPercent;

        private bool _isVoltageDropExceeded;

        private string _loadType;

        private bool _isSpare;

        private bool _drawOutgoingWire = true;

        public ElectricalLoad()

        {

            _cableType = "Cu/XLPE/PVC";

            _coreType = "Auto";

            _conductorSize = "2.5";

            _groundSize = "2.5";

            _starter = StarterType.Direct;

            _control = ControlMode.None;

            _cableLength = 25.0; // Chiều dài mặc định 25 mét

            _loadType = "Động cơ";

        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propName)

        {

            if (PropertyChanged != null)

            {

                PropertyChanged(this, new PropertyChangedEventArgs(propName));

            }

        }

        public int Stt

        {

            get { return _stt; }

            set { _stt = value; OnPropertyChanged("Stt"); }

        }

        public string LoadName

        {

            get { return _loadName; }

            set { _loadName = value; OnPropertyChanged("LoadName"); }

        }

        /// <summary>

        /// Tổng công suất P (kW) của phụ tải

        /// </summary>

        public double PowerKW

        {

            get { return _powerKw; }

            set

            {

                _powerKw = value;

                OnPropertyChanged("PowerKW");

                // Nếu là 3 pha và không có chỉnh tay lẻ, tự động chia đều 3 pha

                if (_phase == PhaseType.ThreePhase)

                {

                    double p3 = _powerKw / 3.0;

                    _powerR = p3;

                    _powerY = p3;

                    _powerB = p3;

                    OnPropertyChanged("PowerR");

                    OnPropertyChanged("PowerY");

                    OnPropertyChanged("PowerB");

                }

                else if (_phase == PhaseType.SinglePhase)

                {

                    // 1 pha: gán vào pha đang chọn

                    if (_assignedPhase == "Y") { _powerY = _powerKw; _powerR = 0; _powerB = 0; }

                    else if (_assignedPhase == "B") { _powerB = _powerKw; _powerR = 0; _powerY = 0; }

                    else { _assignedPhase = "R"; _powerR = _powerKw; _powerY = 0; _powerB = 0; }

                    OnPropertyChanged("PowerR");

                    OnPropertyChanged("PowerY");

                    OnPropertyChanged("PowerB");

                }

            }

        }

        public PhaseType Phase

        {

            get { return _phase; }

            set

            {

                _phase = value;

                OnPropertyChanged("Phase");

                OnPropertyChanged("PhaseDisplay");

                OnPropertyChanged("PhaseColorBrush");

                OnPropertyChanged("IsThreePhase");

                // Cập nhật lại phân bổ pha

                PowerKW = _powerKw;

            }

        }

        public string PhaseDisplay

        {

            get { return _phase == PhaseType.ThreePhase ? "3 Pha" : "1 Pha"; }

            set

            {

                if (value == "1 Pha")

                    Phase = PhaseType.SinglePhase;

                else

                    Phase = PhaseType.ThreePhase;

            }

        }

        public bool IsThreePhase

        {

            get { return _phase == PhaseType.ThreePhase; }

        }

        public Brush PhaseColorBrush

        {

            get

            {

                return _phase == PhaseType.ThreePhase

                    ? new SolidColorBrush(Color.FromRgb(211, 47, 47))   // Đỏ

                    : new SolidColorBrush(Color.FromRgb(46, 125, 50));  // Xanh lá

            }

        }

        public string CircuitNumber

        {

            get { return _circuitNumber; }

            set { _circuitNumber = value; OnPropertyChanged("CircuitNumber"); }

        }

        public string AssignedPhase

        {

            get { return _assignedPhase; }

            set { _assignedPhase = value; OnPropertyChanged("AssignedPhase"); }

        }

        public double PowerR

        {

            get { return _powerR; }

            set

            {

                _powerR = value;

                if (_phase == PhaseType.SinglePhase && value > 0.0001)

                {

                    _powerY = 0;

                    _powerB = 0;

                    _assignedPhase = "R";

                    OnPropertyChanged("PowerY");

                    OnPropertyChanged("PowerB");

                    OnPropertyChanged("AssignedPhase");

                }

                OnPropertyChanged("PowerR");

                OnPropertyChanged("DisplayPowerR");

                UpdateTotalFromPhases();

            }

        }

        public double PowerY

        {

            get { return _powerY; }

            set

            {

                _powerY = value;

                if (_phase == PhaseType.SinglePhase && value > 0.0001)

                {

                    _powerR = 0;

                    _powerB = 0;

                    _assignedPhase = "Y";

                    OnPropertyChanged("PowerR");

                    OnPropertyChanged("PowerB");

                    OnPropertyChanged("AssignedPhase");

                }

                OnPropertyChanged("PowerY");

                OnPropertyChanged("DisplayPowerY");

                UpdateTotalFromPhases();

            }

        }

        public double PowerB

        {

            get { return _powerB; }

            set

            {

                _powerB = value;

                if (_phase == PhaseType.SinglePhase && value > 0.0001)

                {

                    _powerR = 0;

                    _powerY = 0;

                    _assignedPhase = "B";

                    OnPropertyChanged("PowerR");

                    OnPropertyChanged("PowerY");

                    OnPropertyChanged("AssignedPhase");

                }

                OnPropertyChanged("PowerB");

                OnPropertyChanged("DisplayPowerB");

                UpdateTotalFromPhases();

            }

        }

        private void UpdateTotalFromPhases()

        {

            _powerKw = _powerR + _powerY + _powerB;

            OnPropertyChanged("PowerKW");

            OnPropertyChanged("DisplayPowerR");

            OnPropertyChanged("DisplayPowerY");

            OnPropertyChanged("DisplayPowerB");

        }

        public double CurrentA

        {

            get { return _currentA; }

            set { _currentA = value; OnPropertyChanged("CurrentA"); }

        }

        private string _breakerType;
        private bool _isBreakerUserDefined;

        public string BreakerSpec

        {

            get { return _breakerSpec; }

            set { _breakerSpec = value; OnPropertyChanged("BreakerSpec"); }

        }

        /// <summary>
        /// Loại Aptomat: MCB 1P/2P/3P/4P, MCCB 1P/2P/3P/4P, RCCB 2P/4P (30mA), RCBO 2P/4P
        /// </summary>
        public string BreakerType
        {
            get
            {
                if (!string.IsNullOrEmpty(_breakerType)) return _breakerType;
                if (!string.IsNullOrEmpty(_breakerSpec))
                {
                    string upper = _breakerSpec.ToUpper();
                    if (upper.StartsWith("RCCB 2P")) return "RCCB 2P";
                    if (upper.StartsWith("RCCB 4P")) return "RCCB 4P";
                    if (upper.StartsWith("RCBO 2P") || upper.StartsWith("RCBO 1P+N")) return "RCBO 2P";
                    if (upper.StartsWith("RCBO 4P")) return "RCBO 4P";
                    if (upper.StartsWith("MCCB 4P")) return "MCCB 4P";
                    if (upper.StartsWith("MCCB 3P")) return "MCCB 3P";
                    if (upper.StartsWith("MCCB 2P")) return "MCCB 2P";
                    if (upper.StartsWith("MCCB 1P")) return "MCCB 1P";
                    if (upper.StartsWith("MCB 4P")) return "MCB 4P";
                    if (upper.StartsWith("MCB 3P")) return "MCB 3P";
                    if (upper.StartsWith("MCB 2P")) return "MCB 2P";
                    if (upper.StartsWith("MCB 1P")) return "MCB 1P";
                }
                return Phase == PhaseType.ThreePhase ? "MCCB 3P" : "MCB 1P";
            }
            set
            {
                _breakerType = value;
                _isBreakerUserDefined = true;
                OnPropertyChanged("BreakerType");
                UpdateBreakerSpecFromType();
            }
        }

        public bool IsBreakerUserDefined
        {
            get { return _isBreakerUserDefined; }
            set { _isBreakerUserDefined = value; OnPropertyChanged("IsBreakerUserDefined"); }
        }

        public void UpdateBreakerSpecFromType(int? forcedRating = null)
        {
            int rating = 16;
            if (forcedRating.HasValue && forcedRating.Value > 0)
            {
                rating = forcedRating.Value;
            }
            else if (CurrentA > 0.001)
            {
                double target = CurrentA * 1.25;
                int[] std = new int[] { 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 300, 350, 400, 500, 630 };
                rating = std[std.Length - 1];
                for (int i = 0; i < std.Length; i++)
                {
                    if (std[i] >= target) { rating = std[i]; break; }
                }
            }
            else if (!string.IsNullOrEmpty(_breakerSpec))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(_breakerSpec, @"(\d+)\s*A");
                if (m.Success)
                {
                    int.TryParse(m.Groups[1].Value, out rating);
                }
            }

            if (rating <= 0) rating = (Phase == PhaseType.ThreePhase ? 40 : 16);

            string bType = BreakerType;
            string icu = "10kA";
            if (rating >= 300) icu = "36kA";
            else if (rating >= 100) icu = "25kA";

            switch (bType)
            {
                case "MCCB 1P":
                    BreakerSpec = string.Format("MCCB 1P-10kA {0}A", rating);
                    break;
                case "MCCB 2P":
                    BreakerSpec = string.Format("MCCB 2P-10kA {0}A", rating);
                    break;
                case "MCCB 3P":
                    BreakerSpec = string.Format("MCCB 3P-{0} {1}A", icu, rating);
                    break;
                case "MCCB 4P":
                    BreakerSpec = string.Format("MCCB 4P-{0} {1}A", icu, rating);
                    break;
                case "MCB 1P":
                    BreakerSpec = string.Format("MCB 1P-6kA {0}A", rating);
                    break;
                case "MCB 2P":
                    BreakerSpec = string.Format("MCB 2P-6kA {0}A", rating);
                    break;
                case "MCB 3P":
                    BreakerSpec = string.Format("MCB 3P-6kA {0}A", rating);
                    break;
                case "MCB 4P":
                    BreakerSpec = string.Format("MCB 4P-6kA {0}A", rating);
                    break;
                case "RCCB 2P":
                    int rccbRating = rating <= 25 ? 25 : (rating <= 40 ? 40 : (rating <= 63 ? 63 : rating));
                    BreakerSpec = string.Format("RCCB 2P-30mA {0}A", rccbRating);
                    break;
                case "RCCB 4P":
                    int rccb4Rating = rating <= 40 ? 40 : (rating <= 63 ? 63 : (rating <= 100 ? 100 : rating));
                    BreakerSpec = string.Format("RCCB 4P-30mA {0}A", rccb4Rating);
                    break;
                case "RCBO 2P":
                    BreakerSpec = string.Format("RCBO 2P-30mA {0}A", rating);
                    break;
                case "RCBO 4P":
                    BreakerSpec = string.Format("RCBO 4P-30mA {0}A", rating);
                    break;
                default:
                    if (bType.Contains("RCCB"))
                        BreakerSpec = string.Format("{0}-30mA {1}A", bType, rating);
                    else if (bType.Contains("MCB"))
                        BreakerSpec = string.Format("{0}-6kA {1}A", bType, rating);
                    else
                        BreakerSpec = string.Format("{0}-{1} {2}A", bType, icu, rating);
                    break;
            }
        }

        public string CableType

        {

            get { return string.IsNullOrEmpty(_cableType) ? "Cu/XLPE/PVC" : _cableType; }

            set

            {

                _cableType = value;

                OnPropertyChanged("CableType");

                UpdateCableSpec();

            }

        }

        public string CoreType

        {

            get { return string.IsNullOrEmpty(_coreType) ? "Auto" : _coreType; }

            set

            {

                _coreType = value;

                OnPropertyChanged("CoreType");

                UpdateCableSpec();

            }

        }

        public string ConductorSize

        {

            get { return _conductorSize; }

            set

            {

                _conductorSize = value;

                OnPropertyChanged("ConductorSize");

                UpdateCableSpec();

            }

        }

        public string GroundSize

        {

            get { return _groundSize; }

            set

            {

                _groundSize = value;

                OnPropertyChanged("GroundSize");

                UpdateCableSpec();

            }

        }

        public string CableSpec

        {

            get { return _cableSpec; }

            set

            {

                _cableSpec = value;

                OnPropertyChanged("CableSpec");

                OnPropertyChanged("FullCableString");

            }

        }

        public string GroundWireSpec

        {

            get { return _groundWireSpec; }

            set

            {

                _groundWireSpec = value;

                OnPropertyChanged("GroundWireSpec");

                OnPropertyChanged("FullCableString");

            }

        }

        public void UpdateCableSpec()

        {

            string cType = string.IsNullOrEmpty(_cableType) ? "Cu/XLPE/PVC" : _cableType;

            string core = string.IsNullOrEmpty(_coreType) ? "Auto" : _coreType;

            string size = string.IsNullOrEmpty(_conductorSize) ? "2.5" : _conductorSize;

            string gSize = string.IsNullOrEmpty(_groundSize) ? size : _groundSize;

            double sVal = 2.5;

            double.TryParse(size, out sVal);

            string cable;

            // Nếu là khởi động Sao - Tam giác (Y-Δ), cần 2 đường cáp 3 pha riêng biệt ra động cơ (2x3C)

            if (_starter == StarterType.StarDelta)

            {

                cable = string.Format("2x({0} 3C-{1}mm2)", cType, size);

            }

            else if (core == "4C")

            {

                cable = string.Format("{0} 4C-{1}mm2", cType, size);

            }

            else if (core == "3C")

            {

                cable = string.Format("{0} 3C-{1}mm2", cType, size);

            }

            else if (core == "2C")

            {

                cable = string.Format("{0} 2C-{1}mm2", cType, size);

            }

            else if (core == "1C")

            {

                cable = string.Format("{0} 1C-{1}mm2", cType, size);

            }

            else if (core == "4x1C" || string.Equals(core, "4x1c", StringComparison.OrdinalIgnoreCase))

            {

                cable = string.Format("4x{0} 1C-{1}mm2", cType, size);

            }

            else if (core == "3x1C" || string.Equals(core, "3x1c", StringComparison.OrdinalIgnoreCase))

            {

                cable = string.Format("3x{0} 1C-{1}mm2", cType, size);

            }

            else if (core == "2x1C" || string.Equals(core, "2x1c", StringComparison.OrdinalIgnoreCase))

            {

                cable = string.Format("2x{0} 1C-{1}mm2", cType, size);

            }

            else // "Auto"

            {

                if (_phase == PhaseType.ThreePhase)

                {

                    if (sVal >= 50.0)

                    {

                        cable = string.Format("4x{0} 1C-{1}mm2", cType, size);

                    }

                    else

                    {

                        cable = string.Format("{0} 4C-{1}mm2", cType, size);

                    }

                }

                else

                {

                    cable = string.Format("{0} 2C-{1}mm2", cType, size);

                }

            }

            _cableSpec = cable;

            _groundWireSpec = string.Format("(E) Cu/PVC 1C-{0}mm2", gSize);

            OnPropertyChanged("CableSpec");

            OnPropertyChanged("GroundWireSpec");

            OnPropertyChanged("FullCableString");

        }

        public string DisplayPowerR

        {

            get { return PowerR > 0.0001 ? PowerR.ToString("0.###") : "-"; }

        }

        public string DisplayPowerY

        {

            get { return PowerY > 0.0001 ? PowerY.ToString("0.###") : "-"; }

        }

        public string DisplayPowerB

        {

            get { return PowerB > 0.0001 ? PowerB.ToString("0.###") : "-"; }

        }

        public string FullCableString

        {

            get

            {

                if (!string.IsNullOrEmpty(GroundWireSpec) && !string.IsNullOrEmpty(CableSpec) && !CableSpec.Contains("(E)"))

                {

                    return string.Format("{0} + {1}", CableSpec, GroundWireSpec);

                }

                return CableSpec;

            }

            set

            {

                _cableSpec = value;

                _groundWireSpec = string.Empty;

                if (!string.IsNullOrEmpty(value))

                {

                    string upper = value.ToUpperInvariant();

                    if (upper.Contains("3X1C") || (upper.Contains("3X") && upper.Contains("1C")))

                    {

                        _coreType = "3x1C";

                    }

                    else if (upper.Contains("4X1C") || (upper.Contains("4X") && upper.Contains("1C")))

                    {

                        _coreType = "4x1C";

                    }

                    else if (upper.Contains("2X1C") || (upper.Contains("2X") && upper.Contains("1C")))

                    {

                        _coreType = "2x1C";

                    }

                    else if (upper.Contains("4C"))

                    {

                        _coreType = "4C";

                    }

                    else if (upper.Contains("3C"))

                    {

                        _coreType = "3C";

                    }

                    else if (upper.Contains("2C"))

                    {

                        _coreType = "2C";

                    }

                    else if (upper.Contains("1C"))

                    {

                        _coreType = "1C";

                    }

                    OnPropertyChanged("CoreType");

                }

                OnPropertyChanged("CableSpec");

                OnPropertyChanged("GroundWireSpec");

                OnPropertyChanged("FullCableString");

            }

        }

        // Tương thích ngược với HasDol

        public bool HasDol

        {

            get { return _starter == StarterType.DOL; }

            set

            {

                _hasDol = value;

                if (value) _starter = StarterType.DOL;

                else if (_starter == StarterType.DOL) _starter = StarterType.Direct;

                OnPropertyChanged("HasDol");

                OnPropertyChanged("DolDisplay");

                OnPropertyChanged("Starter");

                OnPropertyChanged("StarterDisplay");

            }

        }

        public string DolDisplay

        {

            get { return _starter == StarterType.DOL ? "DOL" : "-"; }

        }

        /// <summary>

        /// Bộ khởi động động cơ

        /// </summary>

        public StarterType Starter
        {
            get { return _starter; }
            set
            {
                _starter = value;
                _hasDol = (value == StarterType.DOL);
                OnPropertyChanged("Starter");
                OnPropertyChanged("StarterDisplay");
                OnPropertyChanged("HasDol");
                OnPropertyChanged("DolDisplay");
                UpdateCableSpec();

                if (value != StarterType.Direct)
                {
                    string norm = string.IsNullOrEmpty(_loadType) ? "" : _loadType.ToUpperInvariant();
                    if (!norm.Contains("DONG CO") && !norm.Contains("ĐỘNG CƠ") && !norm.Contains("ĐỘNG") && !norm.Contains("MOTOR"))
                    {
                        _loadType = "Động cơ";
                        OnPropertyChanged("LoadType");
                        OnPropertyChanged("LoadTypeBrush");
                    }
                    if (_control == ControlMode.None)
                    {
                        Control = ControlMode.AutoManual;
                    }
                }
                else
                {
                    if (_control == ControlMode.AutoManual)
                    {
                        Control = ControlMode.None;
                    }
                }
            }
        }

        public string StarterDisplay
        {
            get
            {
                switch (_starter)
                {
                    case StarterType.DOL: return "DOL";
                    case StarterType.StarDelta: return "Y - \u0394";
                    case StarterType.VFD: return "VFD";
                    case StarterType.SoftStarter: return "Soft Starter";
                    default: return "-";
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value) || value == "-") Starter = StarterType.Direct;
                else if (value == "DOL") Starter = StarterType.DOL;
                else if (value.Contains("Y") || value.Contains("\u0394") || value.Contains("Δ") || value.Contains("Star")) Starter = StarterType.StarDelta;
                else if (value == "VFD") Starter = StarterType.VFD;
                else if (value.Contains("Soft") || value == "SS") Starter = StarterType.SoftStarter;
                else Starter = StarterType.Direct;
            }
        }

        /// <summary>

        /// Chế độ điều khiển mạch

        /// </summary>

        private bool _isControlGrouped = true;
        public bool IsControlGrouped
        {
            get { return _isControlGrouped; }
            set
            {
                if (_isControlGrouped != value)
                {
                    _isControlGrouped = value;
                    OnPropertyChanged("IsControlGrouped");
                }
            }
        }

        public bool CanGroupControl
        {
            get
            {
                return _control == ControlMode.BMS || _control == ControlMode.PLC || _control == ControlMode.Timer;
            }
        }

        public ControlMode Control
        {
            get { return _control; }
            set
            {
                _control = value;
                OnPropertyChanged("Control");
                OnPropertyChanged("ControlDisplay");
                OnPropertyChanged("CanGroupControl");
            }
        }

        public string ControlDisplay
        {
            get
            {
                switch (_control)
                {
                    case ControlMode.AutoManual: return "A-O-M";
                    case ControlMode.FloatSwitch: return "Phao (FS)";
                    case ControlMode.PressureSwitch: return "Áp suất (PS)";
                    case ControlMode.BMS: return "BMS";
                    case ControlMode.PLC: return "PLC";
                    case ControlMode.Timer: return "TIMER";
                    default: return "-";
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value) || value == "-") Control = ControlMode.None;
                else if (value.Contains("A-O-M") || value.Contains("AOM") || value.Contains("HOA") || value.Contains("Auto")) Control = ControlMode.AutoManual;
                else if (value.Contains("Phao") || value.Contains("FS")) Control = ControlMode.FloatSwitch;
                else if (value.Contains("Áp") || value.Contains("PS")) Control = ControlMode.PressureSwitch;
                else if (value.Contains("BMS")) Control = ControlMode.BMS;
                else if (value.Contains("PLC")) Control = ControlMode.PLC;
                else if (value.Contains("TIMER") || value.Contains("Timer")) Control = ControlMode.Timer;
                else Control = ControlMode.None;
            }
        }

        public double CableLength

        {

            get { return _cableLength; }

            set

            {

                _cableLength = Math.Max(1.0, value);

                OnPropertyChanged("CableLength");

            }

        }

        /// <summary>

        /// Độ sụt áp tính toán (ΔU %)

        /// </summary>

        public double VoltageDropPercent

        {

            get { return _voltageDropPercent; }

            set

            {

                _voltageDropPercent = value;

                OnPropertyChanged("VoltageDropPercent");

                OnPropertyChanged("VoltageDropDisplay");

                OnPropertyChanged("VoltageDropColorBrush");

            }

        }

        public string VoltageDropDisplay

        {

            get { return string.Format("{0:N2}%", _voltageDropPercent); }

        }

        public bool IsVoltageDropExceeded

        {

            get { return _isVoltageDropExceeded; }

            set

            {

                _isVoltageDropExceeded = value;

                OnPropertyChanged("IsVoltageDropExceeded");

                OnPropertyChanged("VoltageDropColorBrush");

            }

        }

        public Brush VoltageDropColorBrush

        {

            get

            {

                return _isVoltageDropExceeded

                    ? new SolidColorBrush(Color.FromRgb(211, 47, 47))   // Đỏ cảnh báo vượt sụt áp

                    : new SolidColorBrush(Color.FromRgb(46, 125, 50));  // Xanh lá an toàn

            }

        }

        /// <summary>

        /// Loại tải: "Power" (Động lực, sụt áp cho phép 5%) hoặc "Lighting" (Chiếu sáng, sụt áp cho phép 3%)

        /// </summary>

        public string LoadType
        {
            get
            {
                if (string.IsNullOrEmpty(_loadType) || _loadType == "Power" || _loadType == "Motor") return "Động cơ";
                if (_loadType == "SubPanel" || _loadType == "Feeder" || _loadType == "Panel Feeder") return "Cấp nguồn tủ";
                if (_loadType == "Lighting") return "Chiếu sáng";
                if (_loadType == "Socket") return "Ổ cắm";
                if (_loadType == "Other" || _loadType == "Other Load") return "Tải khác";
                return _loadType;
            }
            set
            {
                string mapped = value;
                if (!string.IsNullOrEmpty(value))
                {
                    string norm = value.Trim().ToUpperInvariant();
                    if (norm.Contains("DONG CO") || norm.Contains("ĐỘNG CƠ") || norm.Contains("ĐỘNG") || norm.Contains("MOTOR") || norm == "POWER")
                        mapped = "Động cơ";
                    else if (norm.Contains("CAP NGUON") || norm.Contains("CẤP NGUỒN") || norm.Contains("FEEDER") || norm.Contains("PANEL") || norm.Contains("SUBPANEL"))
                        mapped = "Cấp nguồn tủ";
                    else if (norm.Contains("CHIEU SANG") || norm.Contains("CHIẾU SÁNG") || norm.Contains("LIGHT"))
                        mapped = "Chiếu sáng";
                    else if (norm.Contains("O CAM") || norm.Contains("Ổ CẮM") || norm.Contains("SOCKET"))
                        mapped = "Ổ cắm";
                    else if (norm.Contains("OTHER") || norm.Contains("KHAC") || norm.Contains("KHÁC"))
                        mapped = "Tải khác";
                }

                if (_loadType != mapped)
                {
                    _loadType = mapped;
                    OnPropertyChanged("LoadType");
                    OnPropertyChanged("LoadTypeBrush");

                    if (mapped == "Động cơ")
                    {
                        if (_starter == StarterType.Direct)
                        {
                            Starter = _powerKw > 7.5 ? StarterType.StarDelta : StarterType.DOL;
                        }
                        if (_control == ControlMode.None)
                        {
                            Control = ControlMode.AutoManual;
                        }
                    }
                    else
                    {
                        Starter = StarterType.Direct;
                        Control = ControlMode.None;
                    }
                }
            }
        }

        public Brush LoadTypeBrush
        {
            get
            {
                string lt = string.IsNullOrEmpty(_loadType) ? "" : _loadType.ToUpperInvariant();
                if (lt.Contains("DONG CO") || lt.Contains("ĐỘNG CƠ") || lt.Contains("ĐỘNG") || lt.Contains("MOTOR"))
                    return new SolidColorBrush(Color.FromRgb(2, 136, 209));
                if (lt.Contains("CAP NGUON") || lt.Contains("CẤP NGUỒN") || lt.Contains("FEEDER") || lt.Contains("PANEL"))
                    return new SolidColorBrush(Color.FromRgb(123, 31, 162));
                if (lt.Contains("CHIEU SANG") || lt.Contains("CHIẾU SÁNG") || lt.Contains("LIGHT"))
                    return new SolidColorBrush(Color.FromRgb(245, 124, 0));
                if (lt.Contains("O CAM") || lt.Contains("Ổ CẮM") || lt.Contains("SOCKET"))
                    return new SolidColorBrush(Color.FromRgb(56, 142, 60));
                return new SolidColorBrush(Color.FromRgb(84, 110, 122));
            }
        }

        public bool IsSpare
        {
            get { return _isSpare; }
            set
            {
                _isSpare = value;
                if (value)
                {
                    _drawOutgoingWire = false;
                }
                OnPropertyChanged("IsSpare");
                OnPropertyChanged("DrawOutgoingWire");
            }
        }

        public bool DrawOutgoingWire
        {
            get { return _drawOutgoingWire; }
            set
            {
                _drawOutgoingWire = value;
                OnPropertyChanged("DrawOutgoingWire");
            }
        }

        public ElectricalLoad Clone()

        {

            return new ElectricalLoad

            {

                Stt = this.Stt,

                LoadName = this.LoadName,

                PowerKW = this.PowerKW,

                Phase = this.Phase,

                CircuitNumber = this.CircuitNumber,

                AssignedPhase = this.AssignedPhase,

                PowerR = this.PowerR,

                PowerY = this.PowerY,

                PowerB = this.PowerB,

                CurrentA = this.CurrentA,

                BreakerSpec = this.BreakerSpec,

                CableType = this.CableType,

                CoreType = this.CoreType,

                ConductorSize = this.ConductorSize,

                GroundSize = this.GroundSize,

                CableSpec = this.CableSpec,

                GroundWireSpec = this.GroundWireSpec,

                HasDol = this.HasDol,

                Starter = this.Starter,

                Control = this.Control,
                IsControlGrouped = this.IsControlGrouped,

                CableLength = this.CableLength,

                VoltageDropPercent = this.VoltageDropPercent,

                IsVoltageDropExceeded = this.IsVoltageDropExceeded,

                LoadType = this.LoadType,

                IsSpare = this.IsSpare,

                DrawOutgoingWire = this.DrawOutgoingWire,

                BreakerType = this.BreakerType,

                IsBreakerUserDefined = this.IsBreakerUserDefined

            };

        }

    }

    /// <summary>

    /// Thông tin toàn bộ Tủ điện (Hỗ trợ Nguồn kép ATS, Tụ bù, Cây Riser, Ngắn mạch)

    /// </summary>

    public class ElectricalPanel

    {

        public string PanelName { get; set; }

        public override string ToString()
        {
            return !string.IsNullOrEmpty(PanelName) ? PanelName : "Tủ điện";
        }

        public double VoltageV { get; set; } // 380V

        public double PowerFactor { get; set; } // cos(phi) = 0.85

        public double SimultaneousFactor { get; set; } // Kdt = 0.85

        public double UsageFactor { get; set; } // Ksd = 1.0

        public double TotalInstalledPowerKW { get; set; } // Pdat

        public double TotalCalculatedPowerKW { get; set; } // Ptt

        public double TotalCurrentA { get; set; } // Itt

        public string MainBreakerSpec { get; set; } // VD: "MCCB 3P-36kA 800A"

        public string MainCableSpec { get; set; }   // VD: "Cu/XLPE/PVC 2x(4x1Cx150mm2) + E95mm2"

        public double PhasePowerR { get; set; }

        public double PhasePowerY { get; set; }

        public double PhasePowerB { get; set; }

        public string DefaultCableType { get; set; }

        // Mở rộng Nguồn Kép & ATS

        public IncomerType Incomer { get; set; }

        public string BackupSourceTitle { get; set; }

        // Mở rộng Cây Riser Diagram (Tủ cha)

        public string ParentPanelName { get; set; }

        // Mở rộng Tính toán Tụ Bù

        public bool HasCapacitorBank { get; set; }

        public double TargetCosPhi { get; set; }

        public double CapacitorBankKvar { get; set; }

        public int CapacitorSteps { get; set; }

        // Mở rộng Ngắn mạch & MBA

        public double TransformerKVA { get; set; }

        public double ShortCircuitKA { get; set; }

        public string SuggestedMainIcu { get; set; }

        // Kích thước vỏ tủ dự kiến (mm)

        public double EstimatedHeight { get; set; }

        public double EstimatedWidth { get; set; }

        public double EstimatedDepth { get; set; }

        public DrawingParameters DrawingParams { get; set; }

        public System.Collections.Generic.List<ElectricalLoad> Loads { get; set; }

        public ElectricalPanel()

        {

            PanelName = "DB-HVAC-01";

            DefaultCableType = "Cu/XLPE/PVC";

            VoltageV = 380;

            PowerFactor = 0.85;

            SimultaneousFactor = 0.85;

            UsageFactor = 1.0;

            Incomer = IncomerType.SingleNormal;

            BackupSourceTitle = "CẤP NGUỒN TỪ MÁY PHÁT ĐIỆN DỰ PHÒNG";

            ParentPanelName = "MSB";

            HasCapacitorBank = false;

            TargetCosPhi = 0.95;

            CapacitorBankKvar = 0;

            CapacitorSteps = 4;

            TransformerKVA = 1000.0;

            ShortCircuitKA = 28.8;

            SuggestedMainIcu = "36kA";

            EstimatedHeight = 1400;

            EstimatedWidth = 800;

            EstimatedDepth = 350;

            Loads = new System.Collections.Generic.List<ElectricalLoad>();

        }

        public ElectricalPanel Clone(string newName = null)

        {

            ElectricalPanel copy = new ElectricalPanel();

            copy.PanelName = !string.IsNullOrEmpty(newName) ? newName : this.PanelName + " - Copy";

            copy.VoltageV = this.VoltageV;

            copy.PowerFactor = this.PowerFactor;

            copy.SimultaneousFactor = this.SimultaneousFactor;

            copy.UsageFactor = this.UsageFactor;

            copy.TotalInstalledPowerKW = this.TotalInstalledPowerKW;

            copy.TotalCalculatedPowerKW = this.TotalCalculatedPowerKW;

            copy.TotalCurrentA = this.TotalCurrentA;

            copy.MainBreakerSpec = this.MainBreakerSpec;

            copy.MainCableSpec = this.MainCableSpec;

            copy.PhasePowerR = this.PhasePowerR;

            copy.PhasePowerY = this.PhasePowerY;

            copy.PhasePowerB = this.PhasePowerB;

            copy.DefaultCableType = this.DefaultCableType;
            copy.DrawingParams = this.DrawingParams != null ? this.DrawingParams.Clone() : null;

            copy.Incomer = this.Incomer;

            copy.BackupSourceTitle = this.BackupSourceTitle;

            copy.ParentPanelName = this.ParentPanelName;

            copy.HasCapacitorBank = this.HasCapacitorBank;

            copy.TargetCosPhi = this.TargetCosPhi;

            copy.CapacitorBankKvar = this.CapacitorBankKvar;

            copy.CapacitorSteps = this.CapacitorSteps;

            copy.TransformerKVA = this.TransformerKVA;

            copy.ShortCircuitKA = this.ShortCircuitKA;

            copy.SuggestedMainIcu = this.SuggestedMainIcu;

            copy.EstimatedHeight = this.EstimatedHeight;

            copy.EstimatedWidth = this.EstimatedWidth;

            copy.EstimatedDepth = this.EstimatedDepth;

            if (this.Loads != null)

            {

                foreach (ElectricalLoad load in this.Loads)

                {

                    copy.Loads.Add(load.Clone());

                }

            }

            return copy;

        }

    }

}

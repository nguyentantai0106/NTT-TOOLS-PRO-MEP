using System;

using System.Collections.Generic;

using System.IO;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Input;

using Autodesk.AutoCAD.ApplicationServices;

using Autodesk.AutoCAD.DatabaseServices;

using Autodesk.AutoCAD.EditorInput;

using TaiAiAddin.Models;

using TaiAiAddin.Services;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;



namespace TaiAiAddin.Views

{

    public class ThemeDefinition

    {

        public string Name { get; set; }

        public string BgPrimary { get; set; }

        public string BgSurface { get; set; }

        public string BgSubCard { get; set; }

        public string BgHover { get; set; }

        public string BorderDivider { get; set; }

        public string AccentCyan { get; set; }

        public string AccentAmber { get; set; }

        public string TextPrimary { get; set; }

        public string TextSecondary { get; set; }

        public string Grad1 { get; set; }

        public string Grad2 { get; set; }

        public string LogoTextBg { get; set; }

    }



    public partial class TaiAiPaletteControl : UserControl

    {

        private readonly LengthCalculatorService _calculatorService;

        private List<LayerLengthStat> _currentStats;

        private SummaryResult _currentSummary;
        private static CableTrayRouteResult _paletteLastTmcResult = null;
        private static CableTrayParams _paletteLastTmcParams = null;

        private FrameworkElement[] _allPanels;

        private ThemeDefinition[] _themes;

        private int _currentThemeIndex = 1;

        private string _activePanelName = "pnlModuleElectrical";

        private bool _isSettingsLoaded = false;



        public TaiAiPaletteControl()

        {

            InitializeComponent();

            _calculatorService = new LengthCalculatorService();

            _currentStats = new List<LayerLengthStat>();

            _currentSummary = new SummaryResult();



            InitThemeDefinitions();



            // Mảng gom tất cả các panel (Bao gồm 5 chủ đề chuyên ngành mới)

            _allPanels = new FrameworkElement[]

            {

                pnlConfigUi,

                pnlConfigLayout,

                pnlSpecElectrical,

                pnlSpecHvac,

                pnlSpecPlumbing,

                pnlSpecEnclosure,

                pnlSpecFire,

                pnlSpecElv,

                pnlSpecEarthing,

                pnlSpecSubstation,

                pnlSpecLighting,

                pnlSpecSolar,

                pnlMepHvac,

                pnlMepPlumbing,

                pnlMepFire,

                pnlModuleElectrical

            };



            // 1. Nạp theme đã lưu trước đó của người dùng (Mặc định Theme 7: Tết Giáp Thìn)
            int savedTheme = LoadThemeSetting();
            if (savedTheme < 1 || (_themes != null && savedTheme > _themes.Length)) savedTheme = 7;
            ApplyTheme(savedTheme, true);

            // 2. Nạp cấu hình thông số và thao tác từ lần sử dụng trước (User Settings Persistence)
            LoadPaletteSettings();

            // 3. Đảm bảo phần Spec cho toàn dự án khi mở lên luôn ở trạng thái thu lại
            if (expanderProjectSpec != null)
            {
                expanderProjectSpec.IsExpanded = false;
            }

            _isSettingsLoaded = true;

            // 4. Kích hoạt hiển thị phân hệ đã lưu từ lần trước (hoặc mặc định Hệ Điện)
            ShowPanelByName(_activePanelName);

            // 5. Đăng ký tự động ghi nhớ thao tác khi người dùng thay đổi dữ liệu
            HookSettingsPersistence();

            Loaded += (s, e) => { try { InitMcpStatusListener(); } catch { } };
        }



        #region Khởi Tạo & Áp Dụng 5 Bộ Theme Màu Sắc



        private void InitThemeDefinitions()

        {

            _themes = new ThemeDefinition[]

            {

                // Theme 1: Dark Deep Space (Cyan Accent)

                new ThemeDefinition

                {

                    Name = "Dark Deep Space (Chuẩn AutoCAD)",

                    BgPrimary = "#12161F",

                    BgSurface = "#1E2433",

                    BgSubCard = "#161B26",

                    BgHover = "#2A3246",

                    BorderDivider = "#333D54",

                    AccentCyan = "#00E5FF",

                    AccentAmber = "#FF9100",

                    TextPrimary = "#F8FAFC",

                    TextSecondary = "#94A3B8",

                    Grad1 = "#00E5FF",

                    Grad2 = "#007ACC",

                    LogoTextBg = "#12161F"

                },

                // Theme 2: Cyberpunk Neon Gold (Amber Accent)

                new ThemeDefinition

                {

                    Name = "Cyberpunk Neon Gold",

                    BgPrimary = "#131722",

                    BgSurface = "#1C2336",

                    BgSubCard = "#151A2A",

                    BgHover = "#2B3550",

                    BorderDivider = "#3C4764",

                    AccentCyan = "#FF9100",

                    AccentAmber = "#00E5FF",

                    TextPrimary = "#F8FAFC",

                    TextSecondary = "#94A3B8",

                    Grad1 = "#FFD600",

                    Grad2 = "#FF9100",

                    LogoTextBg = "#131722"

                },

                // Theme 3: Emerald Green Energy (Emerald Accent)

                new ThemeDefinition

                {

                    Name = "Emerald Green Energy",

                    BgPrimary = "#0E1915",

                    BgSurface = "#162721",

                    BgSubCard = "#11201B",

                    BgHover = "#213B32",

                    BorderDivider = "#2C4F43",

                    AccentCyan = "#00E676",

                    AccentAmber = "#FFD600",

                    TextPrimary = "#F8FAFC",

                    TextSecondary = "#A5D6A7",

                    Grad1 = "#69F0AE",

                    Grad2 = "#00897B",

                    LogoTextBg = "#0E1915"

                },

                // Theme 4: Electric Violet High-Tech (Violet Accent)

                new ThemeDefinition

                {

                    Name = "Electric Violet High-Tech",

                    BgPrimary = "#161324",

                    BgSurface = "#221C38",

                    BgSubCard = "#1B162D",

                    BgHover = "#342A55",

                    BorderDivider = "#463870",

                    AccentCyan = "#B388FF",

                    AccentAmber = "#00E5FF",

                    TextPrimary = "#F8FAFC",

                    TextSecondary = "#D1C4E9",

                    Grad1 = "#E040FB",

                    Grad2 = "#651FFF",

                    LogoTextBg = "#161324"

                },

                // Theme 5: AutoCAD Titanium Gray (SkyBlue Accent)

                new ThemeDefinition

                {

                    Name = "AutoCAD Titanium Gray",

                    BgPrimary = "#1F2228",

                    BgSurface = "#2B303A",

                    BgSubCard = "#242830",

                    BgHover = "#3A414E",

                    BorderDivider = "#444C5C",

                    AccentCyan = "#40C4FF",

                    AccentAmber = "#FF9100",

                    TextPrimary = "#F8FAFC",

                    TextSecondary = "#B0BEC5",

                    Grad1 = "#40C4FF",

                    Grad2 = "#0288D1",

                    LogoTextBg = "#1F2228"

                },

                // Theme 6: Giáng Sinh An Lành (Noel Christmas Magic)
                new ThemeDefinition
                {
                    Name = "Giáng Sinh An Lành (Noel)",
                    BgPrimary = "#180D10",
                    BgSurface = "#25141A",
                    BgSubCard = "#1E0F14",
                    BgHover = "#381923",
                    BorderDivider = "#4A222F",
                    AccentCyan = "#FF334B",
                    AccentAmber = "#FFD700",
                    TextPrimary = "#FFF5F5",
                    TextSecondary = "#D4A5B0",
                    Grad1 = "#FF334B",
                    Grad2 = "#00E676",
                    LogoTextBg = "#180D10"
                },

                // Theme 7: Tết Giáp Thìn (Xuân May Mắn)
                new ThemeDefinition
                {
                    Name = "Tết Giáp Thìn (Xuân May Mắn)",
                    BgPrimary = "#1A0A0C",
                    BgSurface = "#261014",
                    BgSubCard = "#1F0C10",
                    BgHover = "#38171E",
                    BorderDivider = "#52232B",
                    AccentCyan = "#FFD700",
                    AccentAmber = "#FF2A4B",
                    TextPrimary = "#FFF8F0",
                    TextSecondary = "#D4B8B2",
                    Grad1 = "#FFD700",
                    Grad2 = "#FF2A4B",
                    LogoTextBg = "#1A0A0C"
                }

            };

        }



                private void SetThemeBrush(string key, string hexColor)

        {

            try

            {

                Color color = (Color)ColorConverter.ConvertFromString(hexColor);

                if (this.Resources.Contains(key))

                {

                    SolidColorBrush brush = this.Resources[key] as SolidColorBrush;

                    if (brush != null && !brush.IsFrozen)

                    {

                        brush.Color = color;

                    }

                }

                this.Resources[key] = new SolidColorBrush(color);

            }

            catch { }

        }



        public void ApplyTheme(int themeIndex, bool saveSettings = true)

        {

            int maxTheme = (_themes != null && _themes.Length > 0) ? _themes.Length : 7;
            if (themeIndex < 1 || themeIndex > maxTheme) themeIndex = 1;

            _currentThemeIndex = themeIndex;

            ThemeDefinition t = _themes[themeIndex - 1];



            // 1. Cập nhật bảng DynamicResources trên UserControl (đổi màu tức thì)

            SetThemeBrush("BgPrimary", t.BgPrimary);

            SetThemeBrush("BgSurface", t.BgSurface);

            SetThemeBrush("BgSubCard", t.BgSubCard);

            SetThemeBrush("BgHover", t.BgHover);

            SetThemeBrush("BorderDivider", t.BorderDivider);

            SetThemeBrush("AccentCyan", t.AccentCyan);

            SetThemeBrush("AccentAmber", t.AccentAmber);

            SetThemeBrush("TextPrimary", t.TextPrimary);

            SetThemeBrush("TextSecondary", t.TextSecondary);



            // 2. Cập nhật nền & chữ chính UserControl

            this.Background = (SolidColorBrush)this.Resources["BgPrimary"];

            this.Foreground = (SolidColorBrush)this.Resources["TextPrimary"];



            // 3. Cập nhật Gradient Logo NTT

            if (gradStop1 != null && gradStop2 != null)

            {

                gradStop1.Color = (Color)ColorConverter.ConvertFromString(t.Grad1);

                gradStop2.Color = (Color)ColorConverter.ConvertFromString(t.Grad2);

            }

            if (txtLogoText != null)

            {

                txtLogoText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.LogoTextBg));

            }



            // 4. Cập nhật giao diện 5 thẻ theme (Card borders & badges)

            UpdateThemeCardsUI(themeIndex);



            if (txtStatus != null)

            {

                txtStatus.Text = string.Format("✓ Đã áp dụng thành công Theme: {0}", t.Name);

                txtStatus.Foreground = (SolidColorBrush)this.Resources["AccentCyan"];

            }



            if (saveSettings)

            {

                SaveThemeSetting(themeIndex);

            }

        }



        private void UpdateThemeCardsUI(int activeIndex)

        {

            Border[] cards = new Border[] { cardTheme1, cardTheme2, cardTheme3, cardTheme4, cardTheme5, cardTheme6, cardTheme7 };

            Border[] badges = new Border[] { badgeTheme1, badgeTheme2, badgeTheme3, badgeTheme4, badgeTheme5, badgeTheme6, badgeTheme7 };

            Button[] buttons = new Button[] { btnApplyTheme1, btnApplyTheme2, btnApplyTheme3, btnApplyTheme4, btnApplyTheme5, btnApplyTheme6, btnApplyTheme7 };



            SolidColorBrush normalBorder = (SolidColorBrush)this.Resources["BorderDivider"];

            SolidColorBrush activeBorder = (SolidColorBrush)this.Resources["AccentCyan"];



            for (int i = 0; i < cards.Length; i++)

            {

                bool isActive = (i + 1 == activeIndex);

                if (cards[i] != null)

                {

                    cards[i].BorderBrush = isActive ? activeBorder : normalBorder;

                    cards[i].BorderThickness = new Thickness(isActive ? 1.5 : 1.0);

                }

                if (badges[i] != null)

                {

                    badges[i].Visibility = isActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

                }

                if (buttons[i] != null)

                {

                    buttons[i].Visibility = isActive ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

                }

            }

        }



        private void BtnApplyTheme1_Click(object sender, RoutedEventArgs e) { ApplyTheme(1); }

        private void BtnApplyTheme2_Click(object sender, RoutedEventArgs e) { ApplyTheme(2); }

        private void BtnApplyTheme3_Click(object sender, RoutedEventArgs e) { ApplyTheme(3); }

        private void BtnApplyTheme4_Click(object sender, RoutedEventArgs e) { ApplyTheme(4); }

        private void BtnApplyTheme5_Click(object sender, RoutedEventArgs e) { ApplyTheme(5); }

        private void BtnApplyTheme6_Click(object sender, RoutedEventArgs e) { ApplyTheme(6); }

        private void BtnApplyTheme7_Click(object sender, RoutedEventArgs e) { ApplyTheme(7); }



        private void CardTheme1_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(1); }

        private void CardTheme2_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(2); }

        private void CardTheme3_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(3); }

        private void CardTheme4_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(4); }

        private void CardTheme5_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(5); }

        private void CardTheme6_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(6); }

        private void CardTheme7_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(7); }



        private void CbFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (cbFontSize == null) return;

            int idx = cbFontSize.SelectedIndex;

            double newSize = 12.0;

            if (idx == 0) newSize = 12.0;

            else if (idx == 1) newSize = 13.5;

            else if (idx == 2) newSize = 10.5;



            this.FontSize = newSize;

            if (txtStatus != null)

            {

                txtStatus.Text = string.Format("Đã đổi cỡ chữ giao diện sang: {0:F1}pt", newSize);

            }

        }



        private void SaveThemeSetting(int themeIndex)

        {

            try

            {

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaiAiAddin");

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string f = Path.Combine(dir, "theme_settings.txt");

                File.WriteAllText(f, themeIndex.ToString());

            }

            catch { }

        }



        private int LoadThemeSetting()

        {

            try

            {

                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaiAiAddin", "theme_settings.txt");

                if (File.Exists(f))

                {

                    string s = File.ReadAllText(f).Trim();

                    int v;

                    int maxTheme = (_themes != null && _themes.Length > 0) ? _themes.Length : 7;
                    if (int.TryParse(s, out v) && v >= 1 && v <= maxTheme) return v;

                }

            }

            catch { }

            return 1;

        }



        #endregion



        #region Chuyển Đổi Giao Diện Panel & Highlight Nút



        /// <summary>

        /// Kích hoạt hiển thị panel được chỉ định và ẩn tất cả các panel còn lại

        /// </summary>

        private void ShowPanel(FrameworkElement targetPanel, Button activeMepButton, string statusMessage)
        {
            if (targetPanel != null && !string.IsNullOrEmpty(targetPanel.Name))
            {
                _activePanelName = targetPanel.Name;
            }

            if (_allPanels != null)
            {
                foreach (FrameworkElement p in _allPanels)
                {
                    if (p != null)
                    {
                        p.Visibility = (p == targetPanel) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                    }
                }
            }

            HighlightMepButton(activeMepButton);

            if (!string.IsNullOrEmpty(statusMessage) && txtStatus != null)
            {
                txtStatus.Text = statusMessage;
            }

            SavePaletteSettings();
        }

        private void ShowPanelByName(string panelName)
        {
            if (string.IsNullOrEmpty(panelName) || panelName == "pnlModuleElectrical")
            {
                ShowPanel(pnlModuleElectrical, btnMepElectrical, "Đang chọn phân hệ: Hệ Điện - Sơ đồ nguyên lý, Vỏ tủ & Bóc tách khối lượng.");
                return;
            }

            if (panelName == "pnlMepHvac") ShowPanel(pnlMepHvac, btnMepHvac, "Phân hệ Điều hòa không khí (ĐHKK & Thông gió tòa nhà).");
            else if (panelName == "pnlMepPlumbing") ShowPanel(pnlMepPlumbing, btnMepPlumbing, "Phân hệ Cấp thoát nước (Plumbing & Xử lý nước).");
            else if (panelName == "pnlMepFire") ShowPanel(pnlMepFire, btnMepFire, "Phân hệ Phòng Cháy Chữa Cháy (PCCC).");
            else if (panelName == "pnlMepXlmb") ShowPanel(pnlMepXlmb, btnMepXlmb, "Phân hệ Xử lý mặt bằng (XLMB - Smart Room Boundary).");
            else if (panelName == "pnlConfigUi") ShowPanel(pnlConfigUi, null, "Cấu hình giao diện & Bảng màu.");
            else if (panelName == "pnlConfigLayout") ShowPanel(pnlConfigLayout, null, "Cấu hình khung tên & Khởi tạo Layout.");
            else if (panelName == "pnlSpecElectrical") ShowPanel(pnlSpecElectrical, null, "Spec Tiêu chuẩn Kỹ thuật: Hệ Điện.");
            else if (panelName == "pnlSpecHvac") ShowPanel(pnlSpecHvac, null, "Spec Tiêu chuẩn Kỹ thuật: ĐHKK.");
            else if (panelName == "pnlSpecPlumbing") ShowPanel(pnlSpecPlumbing, null, "Spec Tiêu chuẩn Kỹ thuật: Cấp Thoát Nước.");
            else if (panelName == "pnlSpecEnclosure") ShowPanel(pnlSpecEnclosure, null, "Spec Tiêu chuẩn Kỹ thuật: Vỏ Tủ Điện.");
            else if (panelName == "pnlSpecFire") ShowPanel(pnlSpecFire, null, "Spec Tiêu chuẩn Kỹ thuật: PCCC.");
            else if (panelName == "pnlSpecElv") ShowPanel(pnlSpecElv, null, "Spec Tiêu chuẩn Kỹ thuật: Điện Nhẹ (ELV).");
            else if (panelName == "pnlSpecEarthing") ShowPanel(pnlSpecEarthing, null, "Spec Tiêu chuẩn Kỹ thuật: Tiếp Địa & Chống Sét.");
            else if (panelName == "pnlSpecSubstation") ShowPanel(pnlSpecSubstation, null, "Spec Tiêu chuẩn Kỹ thuật: Trạm Biến Áp.");
            else if (panelName == "pnlSpecLighting") ShowPanel(pnlSpecLighting, null, "Spec Tiêu chuẩn Kỹ thuật: Chiếu Sáng.");
            else if (panelName == "pnlSpecSolar") ShowPanel(pnlSpecSolar, null, "Spec Tiêu chuẩn Kỹ thuật: Điện Mặt Trời.");
            else ShowPanel(pnlModuleElectrical, btnMepElectrical, "Đang chọn phân hệ: Hệ Điện - Sơ đồ nguyên lý, Vỏ tủ & Bóc tách khối lượng.");
        }

        #region Lưu Trữ Cấu Hình Thông Minh & Ghi Nhớ Thao Tác (User Settings Persistence - Rule 4.4)
        private void HookSettingsPersistence()
        {
            try
            {
                this.Unloaded += (s, e) => SavePaletteSettings();

                // TMC Inputs
                if (cbTmcWidth != null)
                {
                    cbTmcWidth.SelectionChanged += (s, e) => SavePaletteSettings();
                    cbTmcWidth.LostFocus += (s, e) => SavePaletteSettings();
                }
                if (cbTmcHeight != null)
                {
                    cbTmcHeight.SelectionChanged += (s, e) => SavePaletteSettings();
                    cbTmcHeight.LostFocus += (s, e) => SavePaletteSettings();
                }
                if (txtTmcSupportSpacing != null) txtTmcSupportSpacing.LostFocus += (s, e) => SavePaletteSettings();
                if (txtTmcThickness != null) txtTmcThickness.LostFocus += (s, e) => SavePaletteSettings();
                if (chkTmcInsertSupports != null) chkTmcInsertSupports.Click += (s, e) => SavePaletteSettings();
                if (chkTmcAddAnnotations != null) chkTmcAddAnnotations.Click += (s, e) => SavePaletteSettings();

                // HVAC Inputs
                if (txtDuctFlowCmh != null) txtDuctFlowCmh.LostFocus += (s, e) => SavePaletteSettings();
                if (txtDuctVelocity != null) txtDuctVelocity.LostFocus += (s, e) => SavePaletteSettings();
                if (txtDuctHeight != null) txtDuctHeight.LostFocus += (s, e) => SavePaletteSettings();
                if (chkDuctDoubleLine != null) chkDuctDoubleLine.Click += (s, e) => SavePaletteSettings();

                // Plumbing Inputs
                if (txtWsfuResidents != null) txtWsfuResidents.LostFocus += (s, e) => SavePaletteSettings();
                if (txtWsfuFloors != null) txtWsfuFloors.LostFocus += (s, e) => SavePaletteSettings();

                // Fire Inputs
                if (txtSprinklerCoverageR != null) txtSprinklerCoverageR.LostFocus += (s, e) => SavePaletteSettings();
            }
            catch { }
        }

        private void SavePaletteSettings()
        {
            if (!_isSettingsLoaded) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaiAiAddin");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, "settings.json");

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"ActivePanel\": \"" + (_activePanelName ?? "pnlModuleElectrical") + "\",");

                // TMC
                string wVal = "";
                if (cbTmcWidth != null)
                {
                    wVal = cbTmcWidth.Text;
                    if (string.IsNullOrEmpty(wVal) && cbTmcWidth.SelectedItem != null)
                    {
                        ComboBoxItem cbi = cbTmcWidth.SelectedItem as ComboBoxItem;
                        if (cbi != null && cbi.Content != null) wVal = cbi.Content.ToString();
                    }
                }
                sb.AppendLine("  \"TmcWidth\": \"" + wVal + "\",");

                string hVal = "";
                if (cbTmcHeight != null)
                {
                    hVal = cbTmcHeight.Text;
                    if (string.IsNullOrEmpty(hVal) && cbTmcHeight.SelectedItem != null)
                    {
                        ComboBoxItem cbi = cbTmcHeight.SelectedItem as ComboBoxItem;
                        if (cbi != null && cbi.Content != null) hVal = cbi.Content.ToString();
                    }
                }
                sb.AppendLine("  \"TmcHeight\": \"" + hVal + "\",");

                if (txtTmcSupportSpacing != null) sb.AppendLine("  \"TmcSupportSpacing\": \"" + txtTmcSupportSpacing.Text + "\",");
                if (txtTmcThickness != null) sb.AppendLine("  \"TmcThickness\": \"" + txtTmcThickness.Text + "\",");
                if (chkTmcInsertSupports != null) sb.AppendLine("  \"TmcInsertSupports\": \"" + (chkTmcInsertSupports.IsChecked == true) + "\",");
                if (chkTmcAddAnnotations != null) sb.AppendLine("  \"TmcAddAnnotations\": \"" + (chkTmcAddAnnotations.IsChecked == true) + "\",");

                // HVAC
                if (txtDuctFlowCmh != null) sb.AppendLine("  \"DuctFlowCmh\": \"" + txtDuctFlowCmh.Text + "\",");
                if (txtDuctVelocity != null) sb.AppendLine("  \"DuctVelocity\": \"" + txtDuctVelocity.Text + "\",");
                if (txtDuctHeight != null) sb.AppendLine("  \"DuctHeight\": \"" + txtDuctHeight.Text + "\",");
                if (chkDuctDoubleLine != null) sb.AppendLine("  \"DuctDoubleLine\": \"" + (chkDuctDoubleLine.IsChecked == true) + "\",");

                // Plumbing
                if (txtWsfuResidents != null) sb.AppendLine("  \"WsfuResidents\": \"" + txtWsfuResidents.Text + "\",");
                if (txtWsfuFloors != null) sb.AppendLine("  \"WsfuFloors\": \"" + txtWsfuFloors.Text + "\",");

                // Fire
                if (txtSprinklerCoverageR != null) sb.AppendLine("  \"SprinklerCoverageR\": \"" + txtSprinklerCoverageR.Text + "\",");

                // Theme
                sb.AppendLine("  \"ThemeIndex\": \"" + _currentThemeIndex + "\"");
                sb.AppendLine("}");

                File.WriteAllText(f, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private void LoadPaletteSettings()
        {
            try
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaiAiAddin", "settings.json");
                if (!File.Exists(f)) return;

                Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(f))
                {
                    string t = line.Trim().TrimEnd(',');
                    int colonIdx = t.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string key = t.Substring(0, colonIdx).Trim().Trim('"');
                        string val = t.Substring(colonIdx + 1).Trim().Trim('"');
                        dict[key] = val;
                    }
                }

                string valStr;
                // TMC
                if (dict.TryGetValue("TmcWidth", out valStr) && cbTmcWidth != null && !string.IsNullOrEmpty(valStr))
                {
                    cbTmcWidth.Text = valStr;
                    foreach (object item in cbTmcWidth.Items)
                    {
                        ComboBoxItem cbi = item as ComboBoxItem;
                        if (cbi != null && cbi.Content != null && cbi.Content.ToString() == valStr)
                        {
                            cbTmcWidth.SelectedItem = cbi;
                            break;
                        }
                    }
                }
                if (dict.TryGetValue("TmcHeight", out valStr) && cbTmcHeight != null && !string.IsNullOrEmpty(valStr))
                {
                    cbTmcHeight.Text = valStr;
                    foreach (object item in cbTmcHeight.Items)
                    {
                        ComboBoxItem cbi = item as ComboBoxItem;
                        if (cbi != null && cbi.Content != null && cbi.Content.ToString() == valStr)
                        {
                            cbTmcHeight.SelectedItem = cbi;
                            break;
                        }
                    }
                }
                if (dict.TryGetValue("TmcSupportSpacing", out valStr) && txtTmcSupportSpacing != null && !string.IsNullOrEmpty(valStr))
                {
                    txtTmcSupportSpacing.Text = valStr;
                }
                if (dict.TryGetValue("TmcThickness", out valStr) && txtTmcThickness != null && !string.IsNullOrEmpty(valStr))
                {
                    txtTmcThickness.Text = valStr;
                }
                if (dict.TryGetValue("TmcInsertSupports", out valStr) && chkTmcInsertSupports != null)
                {
                    bool b;
                    if (bool.TryParse(valStr, out b)) chkTmcInsertSupports.IsChecked = b;
                }
                if (dict.TryGetValue("TmcAddAnnotations", out valStr) && chkTmcAddAnnotations != null)
                {
                    bool b;
                    if (bool.TryParse(valStr, out b)) chkTmcAddAnnotations.IsChecked = b;
                }

                // HVAC
                if (dict.TryGetValue("DuctFlowCmh", out valStr) && txtDuctFlowCmh != null) txtDuctFlowCmh.Text = valStr;
                if (dict.TryGetValue("DuctVelocity", out valStr) && txtDuctVelocity != null) txtDuctVelocity.Text = valStr;
                if (dict.TryGetValue("DuctHeight", out valStr) && txtDuctHeight != null) txtDuctHeight.Text = valStr;
                if (dict.TryGetValue("DuctDoubleLine", out valStr) && chkDuctDoubleLine != null)
                {
                    bool b;
                    if (bool.TryParse(valStr, out b)) chkDuctDoubleLine.IsChecked = b;
                }

                // Plumbing
                if (dict.TryGetValue("WsfuResidents", out valStr) && txtWsfuResidents != null) txtWsfuResidents.Text = valStr;
                if (dict.TryGetValue("WsfuFloors", out valStr) && txtWsfuFloors != null) txtWsfuFloors.Text = valStr;

                // Fire
                if (dict.TryGetValue("SprinklerCoverageR", out valStr) && txtSprinklerCoverageR != null) txtSprinklerCoverageR.Text = valStr;

                // Active Panel
                if (dict.TryGetValue("ActivePanel", out valStr) && !string.IsNullOrEmpty(valStr))
                {
                    _activePanelName = valStr;
                }
            }
            catch { }
        }
        #endregion



        private void HighlightMepButton(Button activeButton)

        {

            SolidColorBrush normalBg = (SolidColorBrush)this.Resources["BgSurface"];

            SolidColorBrush normalBorder = (SolidColorBrush)this.Resources["BorderDivider"];



            SolidColorBrush activeBg = (SolidColorBrush)this.Resources["BgHover"];

            SolidColorBrush activeBorder = (SolidColorBrush)this.Resources["AccentCyan"];



            Button[] mepButtons = new Button[] { btnMepHvac, btnMepPlumbing, btnMepElectrical, btnMepFire, btnMepXlmb };

            foreach (Button btn in mepButtons)

            {

                if (btn == null) continue;

                if (btn == activeButton)

                {

                    btn.Background = activeBg;

                    btn.BorderBrush = activeBorder;

                    btn.BorderThickness = new Thickness(1.5);

                }

                else

                {

                    btn.Background = normalBg;

                    btn.BorderBrush = normalBorder;

                    btn.BorderThickness = new Thickness(1.2);

                }

            }

        }



        private void BtnBackToElectrical_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlModuleElectrical, btnMepElectrical, "Đang chọn phân hệ: Hệ Điện - Sơ đồ nguyên lý, Vỏ tủ & Bóc tách khối lượng.");

        }



        private void BtnSaveConfigMock_Click(object sender, RoutedEventArgs e)

        {

            if (txtStatus != null)

            {

                txtStatus.Text = "Đã lưu thiết lập cấu hình & Theme thành công vào bộ nhớ hệ thống!";

            }

        }



        #endregion



        #region Các Button Cấu Hình Tools & 10 Spec Toàn Dự Án



        private void BtnConfigUi_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlConfigUi, null, "Cấu hình giao diện (UI/UX 5 Themes màu sắc & Bảng màu tương phản chuẩn).");

        }



        private void BtnConfigLayout_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlConfigLayout, null, "Khởi tạo Layout, khung tên bản vẽ và tỷ lệ Viewport.");

        }



        private void BtnSpecElectrical_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecElectrical, null, "Spec Điện: Kích thước giá treo thang máng cáp, cao độ công tắc, ổ cắm, tủ điện.");

        }



        private void BtnSpecHvac_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecHvac, null, "Spec ĐHKK: Quy cách ống gió, cùm treo, cao độ dàn lạnh FCU, miệng gió.");

        }



        private void BtnSpecPlumbing_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecPlumbing, null, "Spec Cấp Thoát Nước: Độ dốc thoát, cùm treo ống và cao độ thiết bị vệ sinh.");

        }



        private void BtnSpecEnclosure_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecEnclosure, null, "Spec Cho Phần Vẽ Tủ Điện: Kích thước vỏ tủ, độ dày tôn, thanh cái, khoảng cách thiết bị.");

        }



        private void BtnSpecFire_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecFire, null, "Spec Cho PCCC: Cao độ đầu phun Sprinkler, van báo động, hộp chữa cháy vách tường.");

        }



        // --- 5 CHỦ ĐỀ SPEC MỚI ---



        private void BtnSpecElv_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecElv, null, "Spec Điện Nhẹ (ELV): Camera CCTV, mạng Data/Tel, âm thanh thông báo PA, SmartHome & BMS.");

        }



        private void BtnSpecEarthing_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecEarthing, null, "Spec Chống Sét & Tiếp Địa: Kim thu sét ESE, cọc đồng D16, băng đồng 25x3, R <= 10/4/1 Ohm.");

        }



        private void BtnSpecSubstation_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecSubstation, null, "Spec Trạm Biến Áp & Máy Phát: Máy biến áp Kiosk/Dry, phòng máy phát tiêu âm, rãnh cáp.");

        }



        private void BtnSpecLighting_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecLighting, null, "Spec Chiếu Sáng & Điều Khiển: Tiêu chuẩn độ rọi Lux TCVN 7114, cảm biến hiện diện PIR, DALI.");

        }



        private void BtnSpecSolar_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlSpecSolar, null, "Spec Năng Lượng Mặt Trời: Tấm pin PV 550-670Wp, Inverter hòa lưới, hệ thống Zero Export.");

        }



        #endregion



        #region Các Button Truy Cập Từng Hạng Mục MEP



        private void BtnMepHvac_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlMepHvac, btnMepHvac, "Phân hệ Điều hòa không khí (ĐHKK & Thông gió tòa nhà).");

        }



        private void BtnMepPlumbing_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlMepPlumbing, btnMepPlumbing, "Phân hệ Cấp thoát nước (Plumbing & Xử lý nước).");

        }



        private void BtnMepElectrical_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlModuleElectrical, btnMepElectrical, "Đang chọn phân hệ: Hệ Điện - Sơ đồ nguyên lý, Vỏ tủ & Bóc tách khối lượng.");

        }



        private void BtnMepFire_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlMepFire, btnMepFire, "Phân hệ Phòng cháy chữa cháy (PCCC & Báo cháy tự động).");

        }

        private void BtnMepXlmb_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(pnlMepXlmb, btnMepXlmb, "Phân hệ Xử lý mặt bằng (XLMB - Tự động nhận diện tường, cột, tự tìm điểm góc & bo kín phòng hở).");
        }

        private void BtnExecuteXlmb_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.SendStringToExecute("_.XLMB \n", true, false, false);
            }
        }

        private void BtnBaoPhong_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.SendStringToExecute("_.BAOPHONG \n", true, false, false);
            }
        }

        public RoomBoundaryOptions GetXlmbOptions()
        {
            double textHeight = 250.0;
            double th;
            if (txtXlmbTextHeight != null && double.TryParse(txtXlmbTextHeight.Text, out th) && th > 0)
            {
                textHeight = th;
            }

            double snapTol = 20.0;
            double tol;
            if (txtXlmbTolerance != null && double.TryParse(txtXlmbTolerance.Text, out tol) && tol > 0)
            {
                snapTol = tol;
            }

            return new RoomBoundaryOptions
            {
                TextHeight = textHeight,
                SnapToleranceMm = snapTol,
                BoundaryLayer = (txtXlmbLayerBoundary != null && !string.IsNullOrWhiteSpace(txtXlmbLayerBoundary.Text)) ? txtXlmbLayerBoundary.Text.Trim() : "DUONG_BAO",
                TextLayer = (txtXlmbLayerText != null && !string.IsNullOrWhiteSpace(txtXlmbLayerText.Text)) ? txtXlmbLayerText.Text.Trim() : "DUONG_BAO_TEXT",
                RoundCurvedWalls = (chkXlmbCurvedWalls != null && chkXlmbCurvedWalls.IsChecked.HasValue) ? chkXlmbCurvedWalls.IsChecked.Value : true,
                CreateMText = (chkXlmbCreateText != null && chkXlmbCreateText.IsChecked.HasValue) ? chkXlmbCreateText.IsChecked.Value : true,
                AttachXData = (chkXlmbAttachXData != null && chkXlmbAttachXData.IsChecked.HasValue) ? chkXlmbAttachXData.IsChecked.Value : true,
                ExcludeFurniture = (chkXlmbExcludeFurniture != null && chkXlmbExcludeFurniture.IsChecked.HasValue) ? chkXlmbExcludeFurniture.IsChecked.Value : true,
                AutoCloseCornerGaps = (chkXlmbAutoCornerGap != null && chkXlmbAutoCornerGap.IsChecked.HasValue) ? chkXlmbAutoCornerGap.IsChecked.Value : true,
                ExcludeGrids = true
            };
        }

        public void UpdateXlmbResults(RoomBoundaryResult res)
        {
            if (res == null) return;

            if (txtXlmbCount != null)
            {
                txtXlmbCount.Text = string.Format("{0} phòng", res.RoomCount);
            }

            if (txtXlmbSummary != null)
            {
                txtXlmbSummary.Text = res.Success
                    ? string.Format("Đã vẽ {0} phòng. Tổng diện tích: {1:F2} m²", res.RoomCount, res.TotalAreaM2)
                    : (res.Message ?? "Không phát hiện được phòng hợp lệ.");
            }

            if (lstXlmbRooms != null)
            {
                lstXlmbRooms.ItemsSource = res.Rooms;
            }
        }

        private void BtnViewRoomXData_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.SendStringToExecute("_.XEMPHONG \n", true, false, false);
        }




        #endregion



        #region Các Button Thao Tác Nhanh & Điều Hướng Spec



        private void BtnQuickRouteDuct_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlMepHvac, btnMepHvac, "Đang mở: Phân hệ ĐHKK - Định tuyến Ống Gió theo Polyline.");

            Dispatcher.BeginInvoke(new Action(() =>

            {

                BtnRouteDuctAlongPl_Click(sender, e);

            }), System.Windows.Threading.DispatcherPriority.Background);

        }



        private void BtnQuickRouteGas_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlMepHvac, btnMepHvac, "Đang mở: Phân hệ ĐHKK - Tuyến Ống Đồng & Bộ Chia Gas Daikin VN.");

            Dispatcher.BeginInvoke(new Action(() =>

            {

                BtnRouteRefrigerantAlongPl_Click(sender, e);

            }), System.Windows.Threading.DispatcherPriority.Background);

        }



        private void BtnQuickRouteDrain_Click(object sender, RoutedEventArgs e)

        {

            ShowPanel(pnlMepHvac, btnMepHvac, "Đang mở: Phân hệ ĐHKK - Tuyến Ống Nước Ngưng & Đánh Độ Dốc.");

            Dispatcher.BeginInvoke(new Action(() =>

            {

                BtnRouteDrainAlongPl_Click(sender, e);

            }), System.Windows.Threading.DispatcherPriority.Background);

        }



        private void BtnQuickRouteTmc_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(pnlModuleElectrical, btnMepElectrical, "Đang mở: Phân hệ Hệ Điện - Tuyến Thang Máng Cáp (Block Động).");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BtnDrawTmcManual_Click(sender, e);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

                #region Các Button Chuyển Phân Hệ & Điều Hướng
        private void BtnReloadPalette_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cbTmcWidth != null) cbTmcWidth.Text = "300";
                if (cbTmcHeight != null) cbTmcHeight.Text = "100";
                if (txtStatus != null) txtStatus.Text = "Đang làm mới giao diện Palette theo mã nguồn mới nhất...";

                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.SendStringToExecute("NTTRELOAD\n", true, false, false);
                }
            }
            catch { }
        }

        private void BtnGoToHvacTools_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(pnlMepHvac, btnMepHvac, "Đang mở: Phân hệ ĐHKK - Sẵn sàng thực hiện các chức năng thiết kế.");
        }

        private void BtnGoToPlumbingTools_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(pnlMepPlumbing, btnMepPlumbing, "Đang mở: Phân hệ Cấp Thoát Nước (Plumbing).");
        }

        private void BtnGoToFireTools_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(pnlMepFire, btnMepFire, "Phân hệ Phòng Cháy Chữa Cháy (PCCC).");
        }
        #endregion

        #region Các Button Thiết Kế Tủ Điện & Bóc Tách Khối Lượng Dây Ống
        private void BtnOpenElectricalDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Window dialog = null;
                try
                {
                    Type dlgType = typeof(TaiAiPaletteControl).Assembly.GetType("TaiAiAddin.Views.ElectricalPanelDialog");
                    if (dlgType != null)
                    {
                        dialog = Activator.CreateInstance(dlgType) as Window;
                    }
                }
                catch { }

                if (dialog == null)
                {
                    dialog = new ElectricalPanelDialog();
                }

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Lỗi mở giao diện Tủ điện: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenEnclosureDialog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnclosureLayoutDialog dialog = new EnclosureLayoutDialog();
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Lỗi mở giao diện Mặt Bằng Bố Trí Vỏ Tủ: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSelectAndCalculate_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    txtStatus.Text = "Đang chờ người dùng quét chọn đối tượng trên bản vẽ...";

                    _currentStats = _calculatorService.CollectAndCalculateLengths(doc, out _currentSummary);

                    if (_currentStats == null || _currentStats.Count == 0)
                    {
                        txtStatus.Text = "Không có đối tượng Line/Pline nào được chọn.";
                        return;
                    }

                    bool isMeters = rbMeters.IsChecked == true;
                    dgResults.ItemsSource = null;
                    dgResults.ItemsSource = _currentStats;

                    txtTotalCount.Text = string.Format("{0} đối tượng", _currentSummary.GrandTotalCount);
                    txtTotalLength.Text = isMeters
                        ? string.Format("{0} m", _currentSummary.FormattedGrandTotalM)
                        : string.Format("{0:N2} mm", _currentSummary.GrandTotalLength);

                    btnCreateTableOnly.IsEnabled = true;
                    txtStatus.Text = string.Format("Đã quét thành công {0} đối tượng qua {1} Layer.", _currentSummary.GrandTotalCount, _currentStats.Count);

                    if (chkAutoCreateTable.IsChecked == true)
                    {
                        txtStatus.Text = "Vui lòng click chọn điểm đặt Bảng trên bản vẽ...";
                        bool created = _calculatorService.CreateSummaryTable(doc, _currentStats, _currentSummary, isMeters);
                        if (created)
                        {
                            txtStatus.Text = "Đã tạo bảng thống kê thành công trên AutoCAD!";
                        }
                        else
                        {
                            txtStatus.Text = "Đã hoàn thành thống kê (Chưa chèn bảng).";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);
                if (doc.Editor != null)
                {
                    doc.Editor.WriteMessage(string.Format("\n[TaiAi Error] {0}\n{1}", ex.Message, ex.StackTrace));
                }
            }
        }

        private void BtnCreateTableOnly_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (_currentStats == null || _currentStats.Count == 0)
            {
                MessageBox.Show("Chưa có dữ liệu thống kê. Vui lòng quét chọn đối tượng trước!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    txtStatus.Text = "Vui lòng chọn điểm đặt Bảng trên bản vẽ...";
                    bool isMeters = rbMeters.IsChecked == true;
                    bool created = _calculatorService.CreateSummaryTable(doc, _currentStats, _currentSummary, isMeters);

                    if (created)
                    {
                        txtStatus.Text = "Đã chèn lại bảng thành công!";
                    }
                    else
                    {
                        txtStatus.Text = "Hủy thao tác chèn bảng.";
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);
                if (doc.Editor != null)
                {
                    doc.Editor.WriteMessage(string.Format("\n[TaiAi Error] {0}", ex.Message));
                }
            }
        }
        #endregion

        #region Nghiệp Vụ Thang Máng Cáp (TMC)
        private CableTrayParams GetCableTrayParamsFromUi()
        {
            CableTrayType trayType = CableTrayType.Trunking;
            if (cbTmcType != null && cbTmcType.SelectedIndex == 1)
                trayType = CableTrayType.Ladder;
            else if (cbTmcType != null && cbTmcType.SelectedIndex == 2)
                trayType = CableTrayType.Tray;

            double widthMm = 300;
            if (cbTmcWidth != null)
            {
                string wText = cbTmcWidth.Text;
                ComboBoxItem itemW = cbTmcWidth.SelectedItem as ComboBoxItem;
                if (string.IsNullOrEmpty(wText) && itemW != null && itemW.Content != null)
                    wText = itemW.Content.ToString();
                if (!string.IsNullOrEmpty(wText))
                {
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(wText, @"\d+");
                    if (match.Success) double.TryParse(match.Value, out widthMm);
                }
                if (widthMm <= 0) widthMm = 300;
            }

            double heightMm = 100;
            if (cbTmcHeight != null)
            {
                string hText = cbTmcHeight.Text;
                ComboBoxItem itemH = cbTmcHeight.SelectedItem as ComboBoxItem;
                if (string.IsNullOrEmpty(hText) && itemH != null && itemH.Content != null)
                    hText = itemH.Content.ToString();
                if (!string.IsNullOrEmpty(hText))
                {
                    System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(hText, @"\d+");
                    if (match.Success) double.TryParse(match.Value, out heightMm);
                }
                if (heightMm <= 0) heightMm = 100;
            }

            double supportSpacingM = 1.5;
            if (txtTmcSupportSpacing != null)
            {
                double.TryParse(txtTmcSupportSpacing.Text, out supportSpacingM);
                if (supportSpacingM <= 0) supportSpacingM = 1.5;
            }

            double thicknessMm = 1.5;
            if (txtTmcThickness != null)
            {
                double.TryParse(txtTmcThickness.Text, out thicknessMm);
                if (thicknessMm <= 0) thicknessMm = 1.5;
            }

            bool insertSupports = (chkTmcInsertSupports != null && chkTmcInsertSupports.IsChecked == true);
            bool addAnnotations = (chkTmcAddAnnotations != null && chkTmcAddAnnotations.IsChecked == true);

            return new CableTrayParams
            {
                Type = trayType,
                WidthMm = widthMm,
                HeightMm = heightMm,
                ThicknessMm = thicknessMm,
                SupportSpacingM = supportSpacingM,
                InsertSupports = insertSupports,
                AddAnnotations = addAnnotations
            };
        }

        public void OnTmcDimensionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OnTmcDimensionChanged(sender, e);
        }

        public void OnTmcDimensionChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;
                PromptSelectionResult psr = ed.SelectImplied();
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    CableTrayParams p = GetCableTrayParamsFromUi();
                    int count = CableTrayService.UpdateBlockReferenceDimensions(doc, p, psr.Value);
                    if (count > 0 && txtStatus != null)
                    {
                        txtStatus.Text = string.Format("Đã tự động điều chỉnh {0} Block theo thông số W{1:F0}xH{2:F0}!",
                            count, p.WidthMm, p.HeightMm);
                    }
                }
            }
            catch { }
        }

        private void BtnUpdateSelectedTmc_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                CableTrayParams p = GetCableTrayParamsFromUi();
                Application.MainWindow.Focus();
                int count = CableTrayService.UpdateBlockReferenceDimensions(doc, p);
                if (count > 0 && txtStatus != null)
                {
                    txtStatus.Text = string.Format("Đã cập nhật thành công {0} Block Thang Máng Cáp theo kích thước W{1:F0}xH{2:F0}!",
                        count, p.WidthMm, p.HeightMm);
                }
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi cập nhật: {0}", ex.Message);
            }
        }

        private void StartDrawTmcWithType(CableTrayType type, string typeName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Đồng bộ ngầm ComboBox
                if (cbTmcType != null)
                {
                    if (type == CableTrayType.Trunking) cbTmcType.SelectedIndex = 0;
                    else if (type == CableTrayType.Ladder) cbTmcType.SelectedIndex = 1;
                    else if (type == CableTrayType.Tray) cbTmcType.SelectedIndex = 2;
                }

                CableTrayParams p = GetCableTrayParamsFromUi();
                p.Type = type; // Gán đúng loại của nút bấm
                CableTrayService.ActiveParams = p;

                if (txtStatus != null)
                {
                    txtStatus.Text = string.Format("⚡ Live Jig {0} (W{1}xH{2}): Click điểm đầu trên CAD để bắt đầu kéo dãn...", typeName, p.WidthMm, p.HeightMm);
                }

                // Gửi lệnh NTTTMC vào AutoCAD để chạy trong Native Command Context: chuột kéo Jig mượt mà 60 FPS, bắt điểm OSNAP chuẩn xác
                doc.SendStringToExecute("NTTTMC\n", true, false, false);
            }
            catch (Exception ex)
            {
                if (txtStatus != null) txtStatus.Text = "Lỗi kích hoạt Live Jig: " + ex.Message;
            }
        }

        private void BtnDrawTray_Click(object sender, RoutedEventArgs e)
        {
            StartDrawTmcWithType(CableTrayType.Tray, "Khay Cáp (Tray)");
        }

        private void BtnDrawTrunking_Click(object sender, RoutedEventArgs e)
        {
            StartDrawTmcWithType(CableTrayType.Trunking, "Trunking (Máng Cáp)");
        }

        private void BtnDrawLadder_Click(object sender, RoutedEventArgs e)
        {
            StartDrawTmcWithType(CableTrayType.Ladder, "Thang Điện (Ladder)");
        }

        private void BtnDrawTmcManual_Click(object sender, RoutedEventArgs e)
        {
            BtnDrawTrunking_Click(sender, e);
        }
        #endregion

        private void BtnRouteTmcAlongPl_Click(object sender, RoutedEventArgs e)

        {

            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)

            {

                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }

            Editor ed = doc.Editor;



            try

            {

                CableTrayParams p = GetCableTrayParamsFromUi();

                Application.MainWindow.Focus();



                int routedCount = 0;

                while (true)

                {

                    PromptEntityOptions peo = new PromptEntityOptions("\n[NTT TOOLS] Chọn đường Polyline (PL) định tuyến Thang Máng Cáp (hoặc ENTER/ESC để kết thúc): ");

                    peo.SetRejectMessage("\nChỉ được chọn đối tượng Polyline!");

                    peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

                    peo.AllowNone = true;

                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK) break;



                    using (DocumentLock docLock = doc.LockDocument())

                    {

                        CableTrayRouteResult res = CableTrayService.RouteCableTrayAlongPolyline(doc, per.ObjectId, p);

                        if (res != null)

                        {

                            _paletteLastTmcResult = res;

                            _paletteLastTmcParams = p;

                            routedCount++;

                            txtStatus.Text = string.Format("Đã vẽ xong tuyến {0} {1}: {2:F2}m, {3} Co 90°, {4} Co 45°, {5} Giá đỡ.",

                                res.TypeName, res.DimensionLabel, res.TotalLengthM, res.Elbow90Count, res.Elbow45Count, res.SupportCount);

                        }

                    }

                }

            }

            catch (Exception ex)

            {

                txtStatus.Text = string.Format("Lỗi định tuyến TMC: {0}", ex.Message);

                if (ed != null)

                {

                    ed.WriteMessage(string.Format("\n[NTT Error] {0}", ex.Message));

                }

            }

        }



        private void BtnDrawTmcBOMTable_Click(object sender, RoutedEventArgs e)

        {

            Document doc = Application.DocumentManager.MdiActiveDocument;

            if (doc == null)

            {

                MessageBox.Show("Không tìm thấy bản vẽ nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }



            try

            {

                CableTrayParams p = _paletteLastTmcParams ?? GetCableTrayParamsFromUi();

                CableTrayRouteResult res = _paletteLastTmcResult;



                if (res == null)

                {

                    res = new CableTrayRouteResult

                    {

                        TypeName = p.Type == CableTrayType.Ladder ? "Thang Cáp (Cable Ladder)" : (p.Type == CableTrayType.Tray ? "Khay Cáp (Perforated Cable Tray)" : "Máng Cáp (Cable Trunking)"),

                        DimensionLabel = string.Format("{0}x{1} mm", p.WidthMm, p.HeightMm),

                        WidthMm = p.WidthMm,

                        HeightMm = p.HeightMm,

                        ThicknessMm = p.ThicknessMm,

                        TotalLengthM = 15.0,

                        SupportCount = 11,

                        ElbowCount = 2,

                        SegmentCount = 3,

                        SheetMetalWeightKg = 110.0

                    };

                }



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    txtStatus.Text = "Click chọn điểm đặt Bảng Bóc Tách Khối Lượng TMC trên bản vẽ...";

                    CableTrayService.DrawCableTrayBOMTableInCad(doc, res, p);

                    txtStatus.Text = "Đã xuất Bảng Bóc Tách Khối Lượng TMC vào bản vẽ CAD!";

                }

            }

            catch (Exception ex)

            {

                txtStatus.Text = string.Format("Lỗi xuất bảng BOM: {0}", ex.Message);

                if (doc.Editor != null)

                {

                    doc.Editor.WriteMessage(string.Format("\n[NTT Error] {0}", ex.Message));

                }

            }

        }



        private void BtnOpenTmcFolder_Click(object sender, RoutedEventArgs e)

        {

            CableTrayService.OpenLibraryFolder();

            if (txtStatus != null) txtStatus.Text = "Đã mở thư mục Thư Viện Thang Máng Cáp trong File Explorer.";

        }



        #endregion



        #region Phân Hệ Phòng Cháy Chữa Cháy (PCCC)



        private void BtnOpenPcccFolder_Click(object sender, RoutedEventArgs e)

        {

            FireProtectionService.OpenLibraryFolder();

            if (txtStatus != null) txtStatus.Text = "Đã mở thư mục Thư Viện PCCC trong File Explorer.";

        }



        private void BtnReloadPcccCatalog_Click(object sender, RoutedEventArgs e)

        {

            if (txtStatus != null) txtStatus.Text = "Đã làm mới danh mục 13 block thư viện PCCC tiêu chuẩn!";

        }



        private void BtnInsertPcccBlock_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                int idx = cbPcccCatalog != null ? cbPcccCatalog.SelectedIndex : 0;

                var catalog = FireProtectionService.GetCatalog();

                if (idx < 0 || idx >= catalog.Count) idx = 0;

                var item = catalog[idx];



                bool drawCov = chkDrawBlockCoverage != null && chkDrawBlockCoverage.IsChecked == true;

                double covR = drawCov ? item.DefaultCoverageRadius : 0;



                Application.MainWindow.Focus();

                FireProtectionService.InsertSingleBlockWithPrompt(doc, Path.GetFileNameWithoutExtension(item.FileName), item.FileName, covR);



                if (txtStatus != null) txtStatus.Text = string.Format("Đã chèn {0} thành công!", item.Name);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi chèn block: {0}", ex.Message);

            }

        }



        private void CbPcccHazard_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (cbPcccHazard == null || txtSprinklerDx == null) return;

            int idx = cbPcccHazard.SelectedIndex;

            if (idx == 0) // Thấp

            {

                txtSprinklerDx.Text = "3.6";

                txtSprinklerDy.Text = "3.6";

                txtSprinklerWallX.Text = "1.8";

                txtSprinklerWallY.Text = "1.8";

                txtSprinklerCoverageR.Text = "2.1";

            }

            else if (idx == 1) // TB1

            {

                txtSprinklerDx.Text = "3.2";

                txtSprinklerDy.Text = "3.2";

                txtSprinklerWallX.Text = "1.6";

                txtSprinklerWallY.Text = "1.6";

                txtSprinklerCoverageR.Text = "2.0";

            }

            else if (idx == 2) // TB2

            {

                txtSprinklerDx.Text = "3.0";

                txtSprinklerDy.Text = "3.0";

                txtSprinklerWallX.Text = "1.5";

                txtSprinklerWallY.Text = "1.5";

                txtSprinklerCoverageR.Text = "1.9";

            }

            else if (idx == 3) // Cao

            {

                txtSprinklerDx.Text = "2.4";

                txtSprinklerDy.Text = "2.4";

                txtSprinklerWallX.Text = "1.2";

                txtSprinklerWallY.Text = "1.2";

                txtSprinklerCoverageR.Text = "1.5";

            }

        }



        private void BtnAutoSprinkler_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                double dx = 3.0, dy = 3.0, wx = 1.5, wy = 1.5, covR = 2.1;

                double.TryParse(txtSprinklerDx != null ? txtSprinklerDx.Text : "3.0", out dx);

                double.TryParse(txtSprinklerDy != null ? txtSprinklerDy.Text : "3.0", out dy);

                double.TryParse(txtSprinklerWallX != null ? txtSprinklerWallX.Text : "1.5", out wx);

                double.TryParse(txtSprinklerWallY != null ? txtSprinklerWallY.Text : "1.5", out wy);

                double.TryParse(txtSprinklerCoverageR != null ? txtSprinklerCoverageR.Text : "2.1", out covR);



                string blkFile = "Sprinkler_Pendant.dwg";

                int spkTypeIdx = cbSprinklerType != null ? cbSprinklerType.SelectedIndex : 0;

                if (spkTypeIdx == 1) blkFile = "Sprinkler_Upright.dwg";

                else if (spkTypeIdx == 2) blkFile = "Sprinkler_Sidewall.dwg";



                SprinklerLayoutParams cfg = new SprinklerLayoutParams

                {

                    BlockFileName = blkFile,

                    SpacingX = dx,

                    SpacingY = dy,

                    WallOffsetX = wx,

                    WallOffsetY = wy,

                    DrawCoverageCircle = chkCoverageCircle != null && chkCoverageCircle.IsChecked == true,

                    CoverageRadius = covR,

                    ConnectPipes = chkConnectPipes != null && chkConnectPipes.IsChecked == true

                };



                Application.MainWindow.Focus();

                double roomArea = 0;

                int count = FireProtectionService.AutoLayoutSprinklersInCad(doc, cfg, out roomArea);



                if (count > 0)

                {

                    string msg = string.Format("✓ Đã rải thành công {0} đầu phun Sprinkler. S phòng = {1:F1} m²", count, roomArea);

                    if (txtSprinklerResult != null) txtSprinklerResult.Text = msg;

                    if (txtStatus != null) txtStatus.Text = msg;

                }

                else

                {

                    if (txtSprinklerResult != null) txtSprinklerResult.Text = "Đã hủy hoặc không bố trí được đầu phun.";

                }

            }

            catch (Exception ex)

            {

                if (txtSprinklerResult != null) txtSprinklerResult.Text = string.Format("Lỗi: {0}", ex.Message);

            }

        }



        private PcccHydraulicInput GetHydraulicInputFromUi()

        {

            int nHeads = 12, nWall = 2;

            double qSpk = 1.2, qWall = 2.5, hStat = 35.0, lPipe = 120.0, pMin = 10.0, cVal = 120.0;



            int.TryParse(txtHydDesignHeads != null ? txtHydDesignHeads.Text : "12", out nHeads);

            double.TryParse(txtHydSpkHeadFlow != null ? txtHydSpkHeadFlow.Text : "1.2", out qSpk);

            int.TryParse(txtHydWallCount != null ? txtHydWallCount.Text : "2", out nWall);

            double.TryParse(txtHydWallFlow != null ? txtHydWallFlow.Text : "2.5", out qWall);

            double.TryParse(txtHydStaticHeight != null ? txtHydStaticHeight.Text : "35.0", out hStat);

            double.TryParse(txtHydPipeLength != null ? txtHydPipeLength.Text : "120.0", out lPipe);

            double.TryParse(txtHydResidualP != null ? txtHydResidualP.Text : "10.0", out pMin);

            double.TryParse(txtHydC != null ? txtHydC.Text : "120", out cVal);



            return new PcccHydraulicInput

            {

                DesignHeadCount = nHeads,

                SprinklerFlowPerHead = qSpk,

                WallHydrantCount = nWall,

                WallHydrantFlow = qWall,

                StaticHeight = hStat,

                EquivalentPipeLength = lPipe,

                ResidualPressure = pMin,

                HazenWilliamsC = cVal

            };

        }



        private void BtnCalcHydraulics_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                PcccHydraulicInput inp = GetHydraulicInputFromUi();

                PcccHydraulicResult res = FireProtectionService.CalculateHydraulics(inp);



                if (txtResFlow != null) txtResFlow.Text = string.Format("{0:F1} l/s ({1:F1} m³/h)", res.TotalFlowLs, res.TotalFlowM3h);

                if (txtResHead != null) txtResHead.Text = string.Format("{0:F1} m ({1:F1} bar)", res.TotalHeadH, res.TotalHeadH / 10.0);

                if (txtResPipe != null) txtResPipe.Text = string.Format("{0} (v = {1:F2} m/s)", res.PipeDiameter, res.VelocityMs);

                if (txtResPower != null) txtResPower.Text = string.Format("{0:F1} kW ({1:F1} HP)", res.PumpPowerKw, res.PumpPowerHp);

                if (txtResJockey != null) txtResJockey.Text = string.Format("{0:F1} m³/h - {1:F0} m ({2:F1} kW)", res.JockeyFlowM3h, res.JockeyHeadM, res.JockeyPowerKw);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong: Q={0:F1} l/s, H={1:F1}m, Bơm={2:F1} kW", res.TotalFlowLs, res.TotalHeadH, res.PumpPowerKw);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính thủy lực: {0}", ex.Message);

            }

        }



        private void BtnDrawHydTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                PcccHydraulicInput inp = GetHydraulicInputFromUi();

                PcccHydraulicResult res = FireProtectionService.CalculateHydraulics(inp);



                Application.MainWindow.Focus();

                FireProtectionService.DrawHydraulicTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Toán Thủy Lực PCCC vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void BtnQuickPlaceHoseCabinet_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Tu_Chua_Chay_Vach_Tuong", "Tu_Chua_Chay_Vach_Tuong.dwg", 25.0);

        }



        private void BtnQuickPlaceAlarmValve_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Van_Bao_Dong", "Van_Bao_Dong.dwg", 0);

        }



        private void BtnQuickPlaceMfz4_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Binh_MFZ4_Bot", "Binh_MFZ4_Bot.dwg", 0);

        }



        private void BtnQuickPlaceMt3_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPcccDevice("Binh_MT3_CO2", "Binh_MT3_CO2.dwg", 0);

        }



        private void PlaceQuickPcccDevice(string blkName, string dwgFile, double covR)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null) return;

                Application.MainWindow.Focus();

                FireProtectionService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile, covR);

                if (txtStatus != null) txtStatus.Text = string.Format("Đã bố trí {0} thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi bố trí: {0}", ex.Message);

            }

        }



        #endregion



        #region Nghiệp Vụ Phân Hệ Điều Hòa Không Khí (ĐHKK & Thông Gió)



        private void BtnOpenHvacFolder_Click(object sender, RoutedEventArgs e)

        {

            HvacService.OpenLibraryFolder();

        }



        private void BtnReloadHvacCatalog_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                if (txtHvacLibPath != null) txtHvacLibPath.Text = HvacService.GetPrimaryLibraryPath();

                if (txtStatus != null) txtStatus.Text = "Đã cập nhật lại thư mục Block ĐHKK!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);

            }

        }



                private void BtnRouteDuctAlongPl_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                // Đọc thông số UI trước khi chuyển focus sang AutoCAD

                string sysLayer = "Gió cấp";

                if (cbDuctSystemLayer != null)

                {

                    ComboBoxItem item = cbDuctSystemLayer.SelectedItem as ComboBoxItem;

                    if (item != null && item.Content != null) sysLayer = item.Content.ToString();

                }



                double q0 = 3000.0, v = 6.0, h = 300.0, stepDown = 20.0;

                double.TryParse(txtDuctFlowCmh != null ? txtDuctFlowCmh.Text : "3000", out q0);

                double.TryParse(txtDuctVelocity != null ? txtDuctVelocity.Text : "6.0", out v);

                double.TryParse(txtDuctHeight != null ? txtDuctHeight.Text : "300", out h);



                if (cbDuctStepDown != null)

                {

                    switch (cbDuctStepDown.SelectedIndex)

                    {

                        case 0: stepDown = 0.0; break;

                        case 1: stepDown = 15.0; break;

                        case 2: stepDown = 20.0; break;

                        case 3: stepDown = 25.0; break;

                        case 4: stepDown = 30.0; break;

                    }

                }



                DuctRoutingParams p = new DuctRoutingParams

                {

                    SystemType = sysLayer,

                    InitialAirFlowCmh = q0,

                    VelocityMs = v,

                    FixedHeightMm = h,

                    StepDownPercent = stepDown,

                    DrawDoubleLine = chkDuctDoubleLine == null || chkDuctDoubleLine.IsChecked == true,

                    DrawElbows = true,

                    DrawCenterline = chkDuctCenterline != null && chkDuctCenterline.IsChecked == true,

                    AddAnnotations = chkDuctAnnotations == null || chkDuctAnnotations.IsChecked == true

                };



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    Editor ed = doc.Editor;

                    PromptEntityOptions peo = new PromptEntityOptions("\n[ĐHKK] Chọn đường Polyline (PL) định tuyến ống gió: ");

                    peo.SetRejectMessage("\nChỉ chọn được đường Polyline (LWPOLYLINE)!");

                    peo.AddAllowedClass(typeof(Polyline), false);

                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK) return;



                    bool ok = HvacService.RouteDuctAlongPolyline(doc, per.ObjectId, p);

                    if (ok && txtStatus != null)

                    {

                        txtStatus.Text = string.Format("✓ Đã vẽ tuyến ống gió ({0}, Q={1:F0} CMH) tự động theo Polyline!", sysLayer, q0);

                    }

                }

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống gió: {0}", ex.Message);

            }

        }



        private void TxtRefCoolingKw_TextChanged(object sender, TextChangedEventArgs e)

        {

            try

            {

                if (txtRefCoolingKw == null || txtResRefnetModel == null) return;

                double kw = 28.0;

                double.TryParse(txtRefCoolingKw.Text, out kw);

                if (kw <= 0) kw = 28.0;



                DaikinRefnetSpec spec = HvacService.GetDaikinRefnetByCapacity(kw);

                txtResRefnetModel.Text = string.Format("DAIKIN {0} (<={1:F0} kW)", spec.ModelName.Replace("Bo_Chia_Gas_Daikin_", ""), spec.MaxKw);

                if (txtResGasPipe != null) txtResGasPipe.Text = string.Format("Gas: {0} (MEP_HVAC_ONG_DONG_GAS)", spec.GasPipeStr);

                if (txtResLiqPipe != null) txtResLiqPipe.Text = string.Format("Liq: {0} (MEP_HVAC_ONG_DONG_LONG)", spec.LiqPipeStr);

            }

            catch { }

        }



        private void BtnRouteRefrigerantAlongPl_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                double kw = 28.0, spacing = 60.0;

                double.TryParse(txtRefCoolingKw != null ? txtRefCoolingKw.Text : "28.0", out kw);

                double.TryParse(txtRefPipeSpacing != null ? txtRefPipeSpacing.Text : "60", out spacing);



                RefrigerantRoutingParams p = new RefrigerantRoutingParams

                {

                    TotalCoolingKw = kw,

                    PipeSpacingMm = spacing,

                    InsertRefnetJoints = chkInsertRefnetJoints == null || chkInsertRefnetJoints.IsChecked == true,

                    DrawCombinedDrain = chkRefCombinedDrain != null && chkRefCombinedDrain.IsChecked == true,

                    DrainSlopePercent = 1.0,

                    AddAnnotations = chkRefAnnotations == null || chkRefAnnotations.IsChecked == true

                };



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    Editor ed = doc.Editor;

                    PromptEntityOptions peo = new PromptEntityOptions("\n[ĐHKK] Chọn đường Polyline (PL) định tuyến Cụm Ống Đồng & Bộ Chia Daikin: ");

                    peo.SetRejectMessage("\nChỉ chọn được đường Polyline (LWPOLYLINE)!");

                    peo.AddAllowedClass(typeof(Polyline), false);

                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK) return;



                    bool ok = HvacService.RouteRefrigerantAlongPolyline(doc, per.ObjectId, p);

                    if (ok && txtStatus != null)

                    {

                        DaikinRefnetSpec spec = HvacService.GetDaikinRefnetByCapacity(kw);

                        txtStatus.Text = string.Format("✓ Đã vẽ Cụm Ống Đồng (Gas {0} / Liq {1}) & Bộ chia Daikin {2}!",

                            spec.GasPipeStr, spec.LiqPipeStr, spec.ModelName);

                    }

                }

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống đồng: {0}", ex.Message);

            }

        }



        private void BtnRouteDrainAlongPl_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                double slope = 1.0, startElev = 2.8, kw = 10.0;

                if (cbDrainSlope != null)

                {

                    switch (cbDrainSlope.SelectedIndex)

                    {

                        case 0: slope = 1.0; break;

                        case 1: slope = 1.5; break;

                        case 2: slope = 2.0; break;

                    }

                }

                double.TryParse(txtDrainStartElev != null ? txtDrainStartElev.Text : "2.8", out startElev);



                if (cbDrainPipeDia != null)

                {

                    switch (cbDrainPipeDia.SelectedIndex)

                    {

                        case 0: kw = 4.0; break;  // D27

                        case 1: kw = 12.0; break; // D34

                        case 2: kw = 25.0; break; // D42

                        case 3: kw = 40.0; break; // D49

                    }

                }



                DrainRoutingParams p = new DrainRoutingParams

                {

                    TotalCoolingKw = kw,

                    SlopePercent = slope,

                    StartElevationM = startElev,

                    AddSlopeArrow = chkDrainSlopeArrow == null || chkDrainSlopeArrow.IsChecked == true,

                    AddAnnotations = true

                };



                Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())

                {

                    Editor ed = doc.Editor;

                    PromptEntityOptions peo = new PromptEntityOptions("\n[ĐHKK] Chọn đường Polyline (PL) định tuyến Ống Nước Ngưng: ");

                    peo.SetRejectMessage("\nChỉ chọn được đường Polyline (LWPOLYLINE)!");

                    peo.AddAllowedClass(typeof(Polyline), false);

                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK) return;



                    bool ok = HvacService.RouteCondensateDrainAlongPolyline(doc, per.ObjectId, p);

                    if (ok && txtStatus != null)

                    {

                        txtStatus.Text = string.Format("✓ Đã vẽ tuyến Ống Nước Ngưng (độ dốc i={0:F1}%, cao độ +{1:F2}m)!", slope, startElev);

                    }

                }

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ ống nước ngưng: {0}", ex.Message);

            }

        }



private void BtnInsertHvacBlock_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                string blkName = "Mieng_Gio_Khuech_Tan_Vuong";

                string dwgFile = "Mieng_Gio_Khuech_Tan_Vuong.dwg";



                int selIndex = cbHvacCatalog != null ? cbHvacCatalog.SelectedIndex : 0;

                List<HvacBlockItem> catalog = HvacService.GetCatalog();

                if (selIndex >= 0 && selIndex < catalog.Count)

                {

                    blkName = Path.GetFileNameWithoutExtension(catalog[selIndex].FileName);

                    dwgFile = catalog[selIndex].FileName;

                }



                Application.MainWindow.Focus();

                HvacService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã chèn {0} vào bản vẽ thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi chèn block: {0}", ex.Message);

            }

        }



        private HvacDuctCalcInput GetDuctInputFromUi()

        {

            double q = 1200.0, v = 5.5, h = 250.0, l = 15.0;

            double.TryParse(txtDuctFlowCmh != null ? txtDuctFlowCmh.Text : "1200", out q);

            double.TryParse(txtDuctVelocity != null ? txtDuctVelocity.Text : "5.5", out v);

            double.TryParse(txtDuctHeight != null ? txtDuctHeight.Text : "250", out h);

            double.TryParse(txtDuctLength != null ? txtDuctLength.Text : "15.0", out l);



            return new HvacDuctCalcInput

            {

                AirFlowCmh = q,

                VelocityMs = v,

                DuctHeightMm = h,

                DuctLengthM = l,

                DuctType = "Vuông"

            };

        }



        private void BtnCalcDuctSize_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                HvacDuctCalcInput inp = GetDuctInputFromUi();

                HvacDuctCalcResult res = HvacService.CalculateDuctSize(inp);



                if (txtResDuctRect != null) txtResDuctRect.Text = string.Format("{0:F0} x {1:F0} mm (W/H={2:F1})", res.RectWidthMm, res.RectHeightMm, res.RectAspectRatio);

                if (txtResDuctRound != null) txtResDuctRound.Text = string.Format("Ø{0:F0} mm (Tính toán: Ø{1:F0} mm)", res.RoundStandardMm, res.RoundDiameterMm);

                if (txtResDuctLoss != null) txtResDuctLoss.Text = string.Format("v = {0:F2} m/s | {1:F2} Pa/m ({2:F1} Pa)", res.ActualVelocityMs, res.UnitFrictionLossPam, res.TotalFrictionLossPa);

                if (txtResDuctWeight != null) txtResDuctWeight.Text = string.Format("Tôn {0:F2}mm | {1:F1} m² (~{2:F1} kg)", res.SheetThicknessMm, res.SheetMetalAreaM2, res.SheetMetalWeightKg);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong ống gió: {0:F0}x{1:F0}mm, V={2:F2}m/s, Tôn {3:F1}kg", res.RectWidthMm, res.RectHeightMm, res.ActualVelocityMs, res.SheetMetalWeightKg);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính ống gió: {0}", ex.Message);

            }

        }



        private void BtnDrawDuctTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                HvacDuctCalcInput inp = GetDuctInputFromUi();

                HvacDuctCalcResult res = HvacService.CalculateDuctSize(inp);



                Application.MainWindow.Focus();

                HvacService.DrawDuctSizingTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Kích Thước Ống Gió vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void CbCoolingSpaceType_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (cbCoolingSpaceType == null || txtCoolingSpecificLoad == null) return;

            switch (cbCoolingSpaceType.SelectedIndex)

            {

                case 0: txtCoolingSpecificLoad.Text = "200"; break; // Văn phòng

                case 1: txtCoolingSpecificLoad.Text = "160"; break; // Phòng ngủ / KS

                case 2: txtCoolingSpecificLoad.Text = "220"; break; // Phòng khách / Biệt thự

                case 3: txtCoolingSpecificLoad.Text = "250"; break; // TTTM

                case 4: txtCoolingSpecificLoad.Text = "320"; break; // Nhà hàng / Bếp

            }

        }



        private HvacCoolingCalcInput GetCoolingInputFromUi()

        {

            double area = 45.0, ceilingH = 3.2, specificLoad = 200.0, margin = 10.0;

            double.TryParse(txtCoolingArea != null ? txtCoolingArea.Text : "45.0", out area);

            double.TryParse(txtCoolingCeilingH != null ? txtCoolingCeilingH.Text : "3.2", out ceilingH);

            double.TryParse(txtCoolingSpecificLoad != null ? txtCoolingSpecificLoad.Text : "200", out specificLoad);

            double.TryParse(txtCoolingMargin != null ? txtCoolingMargin.Text : "10", out margin);



            string spaceType = "Văn phòng làm việc";

            if (cbCoolingSpaceType != null)

            {

                ComboBoxItem item = cbCoolingSpaceType.SelectedItem as ComboBoxItem;

                if (item != null && item.Content != null) spaceType = item.Content.ToString();

            }



            return new HvacCoolingCalcInput

            {

                SpaceType = spaceType,

                RoomAreaM2 = area,

                CeilingHeightM = ceilingH,

                CoolingIndexWPerM2 = specificLoad,

                SafetyMarginPercent = margin

            };

        }



        private void BtnCalcCooling_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                HvacCoolingCalcInput inp = GetCoolingInputFromUi();

                HvacCoolingCalcResult res = HvacService.CalculateCoolingLoad(inp);



                if (txtResCoolingLoad != null) txtResCoolingLoad.Text = string.Format("{0:F2} kW (~{1:F0} BTU/h - {2:F1} RT)", res.TotalLoadKw, res.TotalLoadBtu, res.TotalLoadRt);

                if (txtResFcuModel != null) txtResFcuModel.Text = res.SuggestedFcuModel;

                if (txtResVrvOutdoor != null) txtResVrvOutdoor.Text = res.SuggestedVrvOutdoor;



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính phụ tải lạnh: {0:F1} kW ({1:F0} BTU/h) cho diện tích {2:F1} m²", res.TotalLoadKw, res.TotalLoadBtu, res.RoomAreaM2);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính phụ tải: {0}", ex.Message);

            }

        }



        private void BtnDrawCoolingTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                HvacCoolingCalcInput inp = GetCoolingInputFromUi();

                HvacCoolingCalcResult res = HvacService.CalculateCoolingLoad(inp);



                Application.MainWindow.Focus();

                HvacService.DrawCoolingLoadTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Chọn FCU & VRV vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void BtnQuickPlaceDiffuser_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Mieng_Gio_Khuech_Tan_Vuong", "Mieng_Gio_Khuech_Tan_Vuong.dwg");

        }



        private void BtnQuickPlaceFcu_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Dan_Lanh_FCU_Giau_Tran", "Dan_Lanh_FCU_Giau_Tran.dwg");

        }



        private void BtnQuickPlaceVcd_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Van_Gio_Vuong_VCD", "Van_Gio_Vuong_VCD.dwg");

        }



        private void BtnQuickPlaceFireDamper_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickHvacDevice("Van_Dap_Lua_FD", "Van_Dap_Lua_FD.dwg");

        }



        private void PlaceQuickHvacDevice(string blkName, string dwgFile)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null) return;

                Application.MainWindow.Focus();

                HvacService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

                if (txtStatus != null) txtStatus.Text = string.Format("Đã bố trí {0} thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi bố trí: {0}", ex.Message);

            }

        }



        #endregion



        #region Nghiệp Vụ Phân Hệ Cấp Thoát Nước (Plumbing & Xử Lý Nước)



        private void BtnOpenPlumbingFolder_Click(object sender, RoutedEventArgs e)

        {

            PlumbingService.OpenLibraryFolder();

        }



        private void BtnReloadPlumbingCatalog_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                if (txtPlumbingLibPath != null) txtPlumbingLibPath.Text = PlumbingService.GetPrimaryLibraryPath();

                if (txtStatus != null) txtStatus.Text = "Đã cập nhật lại thư mục Block CTN!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi: {0}", ex.Message);

            }

        }



        private void BtnInsertPlumbingBlock_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                string blkName = "Ban_Cau_Ket_Hop";

                string dwgFile = "Ban_Cau_Ket_Hop.dwg";



                int selIndex = cbPlumbingCatalog != null ? cbPlumbingCatalog.SelectedIndex : 0;

                List<PlumbingBlockItem> catalog = PlumbingService.GetCatalog();

                if (selIndex >= 0 && selIndex < catalog.Count)

                {

                    blkName = Path.GetFileNameWithoutExtension(catalog[selIndex].FileName);

                    dwgFile = catalog[selIndex].FileName;

                }



                Application.MainWindow.Focus();

                PlumbingService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã chèn {0} vào bản vẽ thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi chèn block: {0}", ex.Message);

            }

        }



        private PlumbingWaterSupplyInput GetWaterSupplyInputFromUi()

        {

            int wc = 6, lav = 6, sink = 2, shower = 4, urinal = 2, wm = 2, residents = 25;

            double floors = 5.0;



            int.TryParse(txtWsfuWc != null ? txtWsfuWc.Text : "6", out wc);

            int.TryParse(txtWsfuLav != null ? txtWsfuLav.Text : "6", out lav);

            int.TryParse(txtWsfuSink != null ? txtWsfuSink.Text : "2", out sink);

            int.TryParse(txtWsfuShower != null ? txtWsfuShower.Text : "4", out shower);

            int.TryParse(txtWsfuUrinal != null ? txtWsfuUrinal.Text : "2", out urinal);

            int.TryParse(txtWsfuWm != null ? txtWsfuWm.Text : "2", out wm);

            int.TryParse(txtWsfuResidents != null ? txtWsfuResidents.Text : "25", out residents);

            double.TryParse(txtWsfuFloors != null ? txtWsfuFloors.Text : "5", out floors);



            string bldType = "Nhà ở gia đình";

            if (cbPlumbingBuildingType != null)

            {

                ComboBoxItem item = cbPlumbingBuildingType.SelectedItem as ComboBoxItem;

                if (item != null && item.Content != null) bldType = item.Content.ToString();

            }



            return new PlumbingWaterSupplyInput

            {

                BuildingType = bldType,

                CountWc = wc,

                CountLavabo = lav,

                CountKitchenSink = sink,

                CountShower = shower,

                CountUrinal = urinal,

                CountWashingMachine = wm,

                ResidentCount = residents,

                FloorCount = floors

            };

        }



        private void BtnCalcWaterSupply_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                PlumbingWaterSupplyInput inp = GetWaterSupplyInputFromUi();

                PlumbingWaterSupplyResult res = PlumbingService.CalculateWaterSupply(inp);



                if (txtResSupplyFlow != null) txtResSupplyFlow.Text = string.Format("{0:F1} WSFU | {1:F2} l/s ({2:F1} m³/h)", res.TotalWsfu, res.DesignFlowLs, res.DesignFlowM3h);

                if (txtResSupplyPipe != null) txtResSupplyPipe.Text = string.Format("{0} (v = {1:F2} m/s)", res.SuggestedPipeName, res.VelocityMs);

                if (txtResSupplyTanks != null) txtResSupplyTanks.Text = string.Format("{0:F1} m³/ngày | Bể ngầm {1:F1} m³ - Mái {2:F1} m³", res.DailyDemandM3, res.UndergroundTankM3, res.RoofTankM3);

                if (txtResBooster != null) txtResBooster.Text = string.Format("Cột áp H = {0:F1} m | Công suất P = {1:F1} kW", res.BoosterHeadM, res.BoosterPowerKw);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong nước cấp: {0:F1} WSFU, Q={1:F2} l/s, Ống {2}", res.TotalWsfu, res.DesignFlowLs, res.SuggestedPipeName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính nước cấp: {0}", ex.Message);

            }

        }



        private void BtnDrawWaterSupplyTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                PlumbingWaterSupplyInput inp = GetWaterSupplyInputFromUi();

                PlumbingWaterSupplyResult res = PlumbingService.CalculateWaterSupply(inp);



                Application.MainWindow.Focus();

                PlumbingService.DrawWaterSupplyTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Tính Nước Cấp & Bể Chứa vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private PlumbingDrainageInput GetDrainageInputFromUi()

        {

            int wc = 6, lav = 6, sink = 2, fd = 6, shower = 4, residents = 25;

            double slope = 2.0;



            int.TryParse(txtDfuWc != null ? txtDfuWc.Text : "6", out wc);

            int.TryParse(txtDfuLav != null ? txtDfuLav.Text : "6", out lav);

            int.TryParse(txtDfuSink != null ? txtDfuSink.Text : "2", out sink);

            int.TryParse(txtDfuFd != null ? txtDfuFd.Text : "6", out fd);

            int.TryParse(txtDfuShower != null ? txtDfuShower.Text : "4", out shower);

            double.TryParse(txtDfuSlope != null ? txtDfuSlope.Text : "2.0", out slope);

            int.TryParse(txtWsfuResidents != null ? txtWsfuResidents.Text : "25", out residents);



            return new PlumbingDrainageInput

            {

                CountWc = wc,

                CountLavabo = lav,

                CountKitchenSink = sink,

                CountFloorDrain = fd,

                CountShower = shower,

                DesignSlopePercent = slope,

                ResidentCount = residents

            };

        }



        private void BtnCalcDrainage_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                PlumbingDrainageInput inp = GetDrainageInputFromUi();

                PlumbingDrainageResult res = PlumbingService.CalculateDrainage(inp);



                if (txtResDfuTotal != null) txtResDfuTotal.Text = string.Format("{0:F1} DFU", res.TotalDfu);

                if (txtResSoilStack != null) txtResSoilStack.Text = string.Format("Phân {0} | Thải {1}", res.SoilStackDia, res.WasteStackDia);

                if (txtResDrainBranch != null) txtResDrainBranch.Text = string.Format("Nhánh {0} (i={1:F1}%) | Hơi {2}", res.HorizontalBranchDia, res.RecommendedSlopePercent, res.MainVentDia);

                if (txtResSepticTank != null) txtResSepticTank.Text = string.Format("V = {0:F1} m³ ({1})", res.SepticTankVolumeM3, res.SepticTankDimensions);



                if (txtStatus != null) txtStatus.Text = string.Format("✓ Đã tính xong thoát nước: {0:F1} DFU, Bể tự hoại {1:F1} m³", res.TotalDfu, res.SepticTankVolumeM3);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi tính thoát nước: {0}", ex.Message);

            }

        }



        private void BtnDrawDrainageTable_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null)

                {

                    if (txtStatus != null) txtStatus.Text = "Không có bản vẽ nào đang mở!";

                    return;

                }



                PlumbingDrainageInput inp = GetDrainageInputFromUi();

                PlumbingDrainageResult res = PlumbingService.CalculateDrainage(inp);



                Application.MainWindow.Focus();

                PlumbingService.DrawDrainageTableInCad(doc, res, inp);



                if (txtStatus != null) txtStatus.Text = "✓ Đã vẽ Bảng Chọn Cỡ Ống Thoát Nước vào CAD thành công!";

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi vẽ bảng: {0}", ex.Message);

            }

        }



        private void BtnQuickPlaceToilet_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Ban_Cau_Ket_Hop", "Ban_Cau_Ket_Hop.dwg");

        }



        private void BtnQuickPlaceLavabo_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Chau_Rua_Lavabo", "Chau_Rua_Lavabo.dwg");

        }



        private void BtnQuickPlaceFloorDrain_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Pheu_Thu_San_FloorDrain", "Pheu_Thu_San_FloorDrain.dwg");

        }



        private void BtnQuickPlaceBoosterPump_Click(object sender, RoutedEventArgs e)

        {

            PlaceQuickPlumbingDevice("Cum_Bom_Cap_Nuoc_Booster", "Cum_Bom_Cap_Nuoc_Booster.dwg");

        }



        private void PlaceQuickPlumbingDevice(string blkName, string dwgFile)

        {

            try

            {

                Document doc = Application.DocumentManager.MdiActiveDocument;

                if (doc == null) return;

                Application.MainWindow.Focus();

                PlumbingService.InsertSingleBlockWithPrompt(doc, blkName, dwgFile);

                if (txtStatus != null) txtStatus.Text = string.Format("Đã bố trí {0} thành công!", blkName);

            }

            catch (Exception ex)

            {

                if (txtStatus != null) txtStatus.Text = string.Format("Lỗi bố trí: {0}", ex.Message);

            }

        }



        #endregion






        #region AI MCP REMOTE CONTROL LOGIC

        private void InitMcpStatusListener()
        {
            try
            {
                UpdateMcpStatusUi(McpBridgeService.IsAuthorized);
                McpBridgeService.OnAuthorizationChanged += (isAuth) =>
                {
                    try
                    {
                        var disp = this.Dispatcher;
                        if (disp != null)
                        {
                            disp.BeginInvoke(new Action(() =>
                            {
                                try { UpdateMcpStatusUi(isAuth); } catch { }
                            }));
                        }
                    }
                    catch { }
                };
            }
            catch { }
        }

        private void BtnToggleMcpAuth_Click(object sender, RoutedEventArgs e)
        {
            McpBridgeService.IsAuthorized = !McpBridgeService.IsAuthorized;
            UpdateMcpStatusUi(McpBridgeService.IsAuthorized);
        }

        private void UpdateMcpStatusUi(bool isAuthorized)
        {
            try
            {
                if (dotMcpStatus == null || txtMcpStatus == null || btnToggleMcpAuth == null) return;

                if (isAuthorized)
                {
                    dotMcpStatus.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    txtMcpStatus.Text = "Sẵn sàng nhận lệnh AI (Port 5005)";
                    txtMcpStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    btnToggleMcpAuth.Content = "KHÓA LẠI";
                }
                else
                {
                    dotMcpStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                    txtMcpStatus.Text = "Đang khóa (Chờ khẩu lệnh AI)";
                    txtMcpStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    btnToggleMcpAuth.Content = "BẬT QUYỀN";
                }
            }
            catch { }
        }

        #endregion
}

}


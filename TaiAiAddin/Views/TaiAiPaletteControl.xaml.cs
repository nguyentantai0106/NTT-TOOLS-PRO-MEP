﻿﻿﻿using System.Reflection;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
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
        public static TaiAiPaletteControl Instance { get; private set; }

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



        static TaiAiPaletteControl()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                try
                {
                    if (e == null || string.IsNullOrEmpty(e.Name)) return null;
                    if (e.Name.IndexOf(".resources", StringComparison.OrdinalIgnoreCase) >= 0) return null;
                    System.Reflection.AssemblyName an = new System.Reflection.AssemblyName(e.Name);
                    if (string.Equals(an.Name, "TaiAiAddin", StringComparison.OrdinalIgnoreCase))
                    {
                        return typeof(TaiAiPaletteControl).Assembly;
                    }
                }
                catch { }
                return null;
            };
        }

        public TaiAiPaletteControl()
        {
            Instance = this;
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

                pnlMepXlmb,

                pnlModuleElectrical

            };



            // 1. Nạp theme đã lưu trước đó của người dùng (Mặc định Theme 9: Gemini Dark Glow)
            int savedTheme = LoadThemeSetting();
            if (savedTheme < 1 || (_themes != null && savedTheme > _themes.Length)) savedTheme = 9;
            ApplyTheme(savedTheme, true);

            // 2. Nạp cấu hình thông số và thao tác từ lần sử dụng trước (User Settings Persistence)
            LoadPaletteSettings();

            _isSettingsLoaded = true;

            // 4. Kích hoạt hiển thị phân hệ đã lưu từ lần trước (hoặc mặc định Hệ Điện)
            ShowPanelByName(_activePanelName);

            // 5. Đăng ký tự động ghi nhớ thao tác khi người dùng thay đổi dữ liệu
            HookSettingsPersistence();

            Loaded += (s, e) => {
                try { InitMcpStatusListener(); } catch { }
                try { ReloadHvacCatalog(); } catch { }
            };
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
                },

                // Theme 8: Andromeda Galaxy (Dải Ngân Hà Xanh Vũ Trụ)
                new ThemeDefinition
                {
                    Name = "Andromeda Galaxy (Dải Ngân Hà)",
                    BgPrimary = "#030712",
                    BgSurface = "#0B132B",
                    BgSubCard = "#070E22",
                    BgHover = "#16254A",
                    BorderDivider = "#1C2E5E",
                    AccentCyan = "#2979FF",
                    AccentAmber = "#D946EF",
                    TextPrimary = "#F1F5F9",
                    TextSecondary = "#94A3B8",
                    Grad1 = "#2979FF",
                    Grad2 = "#D946EF",
                    LogoTextBg = "#030712"
                },

                // Theme 9: Gemini Dark Glow (Nền Đen Tuyền Tuyệt Đối - True OLED Black)
                new ThemeDefinition
                {
                    Name = "Gemini Dark Glow (Nền Đen Tuyền)",
                    BgPrimary = "#000000",
                    BgSurface = "#000000",
                    BgSubCard = "#000000",
                    BgHover = "#141414",
                    BorderDivider = "#1E1E1E",
                    AccentCyan = "#00E5FF",
                    AccentAmber = "#38BDF8",
                    TextPrimary = "#FFFFFF",
                    TextSecondary = "#A0A0A0",
                    Grad1 = "#00E5FF",
                    Grad2 = "#3B82F6",
                    LogoTextBg = "#000000"
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

            // 4. Kích hoạt hiệu ứng nền ảnh Galaxy (Theme 8)
            UpdateGalaxyVisual(themeIndex);

            // 5. Kích hoạt hiệu ứng ánh sáng hào quang phát quang (Theme 9: Gemini Dark Glow)
            UpdateGlowVisual(themeIndex);

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
            Border[] cards = new Border[] { cardTheme1, cardTheme2, cardTheme3, cardTheme4, cardTheme5, cardTheme6, cardTheme7, cardTheme8, cardTheme9 };
            Border[] badges = new Border[] { badgeTheme1, badgeTheme2, badgeTheme3, badgeTheme4, badgeTheme5, badgeTheme6, badgeTheme7, badgeTheme8, badgeTheme9 };
            Button[] buttons = new Button[] { btnApplyTheme1, btnApplyTheme2, btnApplyTheme3, btnApplyTheme4, btnApplyTheme5, btnApplyTheme6, btnApplyTheme7, btnApplyTheme8, btnApplyTheme9 };



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



        private BitmapImage _cachedGalaxyBitmap = null;
        private void UpdateGalaxyVisual(int themeIndex)
        {
            try
            {
                if (themeIndex == 8)
                {
                    if (_cachedGalaxyBitmap == null)
                    {
                        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        List<string> candidatePaths = new List<string>
                        {
                            Path.Combine(appData, "Autodesk", "ApplicationPlugins", "TaiAiAddin.bundle", "Contents", "Resources", "galaxy_bg.png"),
                            Path.Combine(appData, "TaiAiAddin", "galaxy_bg.png"),
                            @"D:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Resources\galaxy_bg.png"
                        };

                        try
                        {
                            string asmLoc = Assembly.GetExecutingAssembly().Location;
                            if (!string.IsNullOrEmpty(asmLoc))
                            {
                                string asmDir = Path.GetDirectoryName(asmLoc);
                                if (!string.IsNullOrEmpty(asmDir))
                                {
                                    candidatePaths.Add(Path.Combine(asmDir, "Resources", "galaxy_bg.png"));
                                }
                            }
                        }
                        catch { }

                        foreach (string p in candidatePaths)
                        {
                            if (!string.IsNullOrEmpty(p) && File.Exists(p))
                            {
                                BitmapImage bi = new BitmapImage();
                                bi.BeginInit();
                                bi.UriSource = new Uri(p, UriKind.Absolute);
                                bi.CacheOption = BitmapCacheOption.OnLoad;
                                bi.EndInit();
                                bi.Freeze();
                                _cachedGalaxyBitmap = bi;
                                break;
                            }
                        }
                    }

                    if (_cachedGalaxyBitmap != null)
                    {
                        if (imgGalaxyBg != null) { imgGalaxyBg.Source = _cachedGalaxyBitmap; imgGalaxyBg.Visibility = System.Windows.Visibility.Visible; }
                        if (imgGalaxyHeader != null) { imgGalaxyHeader.Source = _cachedGalaxyBitmap; imgGalaxyHeader.Visibility = System.Windows.Visibility.Visible; }
                    }
                }
                else
                {
                    if (imgGalaxyBg != null) imgGalaxyBg.Visibility = System.Windows.Visibility.Collapsed;
                    if (imgGalaxyHeader != null) imgGalaxyHeader.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void UpdateGlowVisual(int themeIndex)
        {
            try
            {
                if (themeIndex == 9)
                {
                    // Hào quang Neon Blue phong cách Gemini Dark Mode
                    this.Resources["CardGlowEffect"] = new DropShadowEffect
                    {
                        Color = (Color)ColorConverter.ConvertFromString("#1D4ED8"),
                        BlurRadius = 18,
                        Direction = 270,
                        ShadowDepth = 3.5,
                        Opacity = 0.65
                    };
                    this.Resources["HeaderGlowEffect"] = new DropShadowEffect
                    {
                        Color = (Color)ColorConverter.ConvertFromString("#1D4ED8"),
                        BlurRadius = 22,
                        Direction = 270,
                        ShadowDepth = 4,
                        Opacity = 0.75
                    };
                    this.Resources["FocusedGlowEffect"] = new DropShadowEffect
                    {
                        Color = (Color)ColorConverter.ConvertFromString("#00E5FF"),
                        BlurRadius = 16,
                        Direction = 0,
                        ShadowDepth = 0,
                        Opacity = 0.75
                    };
                }
                else
                {
                    // Đổ bóng nhẹ tiêu chuẩn cho các theme khác
                    this.Resources["CardGlowEffect"] = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 6,
                        Direction = 270,
                        ShadowDepth = 2,
                        Opacity = 0.25
                    };
                    this.Resources["HeaderGlowEffect"] = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 6,
                        Direction = 270,
                        ShadowDepth = 2,
                        Opacity = 0.25
                    };
                    this.Resources["FocusedGlowEffect"] = new DropShadowEffect
                    {
                        Color = (Color)ColorConverter.ConvertFromString("#00E5FF"),
                        BlurRadius = 8,
                        Direction = 0,
                        ShadowDepth = 0,
                        Opacity = 0.4
                    };
                }
            }
            catch { }
        }

        private void BtnApplyTheme1_Click(object sender, RoutedEventArgs e) { ApplyTheme(1); }

        private void BtnApplyTheme2_Click(object sender, RoutedEventArgs e) { ApplyTheme(2); }

        private void BtnApplyTheme3_Click(object sender, RoutedEventArgs e) { ApplyTheme(3); }

        private void BtnApplyTheme4_Click(object sender, RoutedEventArgs e) { ApplyTheme(4); }

        private void BtnApplyTheme5_Click(object sender, RoutedEventArgs e) { ApplyTheme(5); }

        private void BtnApplyTheme6_Click(object sender, RoutedEventArgs e) { ApplyTheme(6); }

        private void BtnApplyTheme7_Click(object sender, RoutedEventArgs e) { ApplyTheme(7); }

        private void BtnApplyTheme8_Click(object sender, RoutedEventArgs e) { ApplyTheme(8); }

        private void BtnApplyTheme9_Click(object sender, RoutedEventArgs e) { ApplyTheme(9); }



        private void CardTheme1_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(1); }

        private void CardTheme2_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(2); }

        private void CardTheme3_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(3); }

        private void CardTheme4_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(4); }

        private void CardTheme5_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(5); }

        private void CardTheme6_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(6); }

        private void CardTheme7_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(7); }

        private void CardTheme8_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(8); }

        private void CardTheme9_MouseDown(object sender, MouseButtonEventArgs e) { ApplyTheme(9); }



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

            if (panelName == "pnlMepHvac")
            {
                ShowPanel(pnlMepHvac, btnMepHvac, "Phân hệ Điều hòa không khí (ĐHKK & Thông gió tòa nhà).");
                try { if (cbHvacCatalog != null && cbHvacCatalog.Items.Count == 0) ReloadHvacCatalog(); } catch { }
            }
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



        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsDialog.ShowOrActivate();
        }

        public void OpenConfigUi()
        {
            ShowPanel(pnlConfigUi, null, "Cấu hình giao diện (UI/UX Themes màu sắc & Bảng màu tương phản chuẩn).");
        }

        public void OpenConfigLayout()
        {
            ShowPanel(pnlConfigLayout, null, "Khởi tạo Layout, khung tên bản vẽ và tỷ lệ Viewport.");
        }

        public void OpenSpecElectrical()
        {
            ShowPanel(pnlSpecElectrical, null, "Spec Điện: Kích thước giá treo thang máng cáp, cao độ công tắc, ổ cắm, tủ điện.");
        }

        public void OpenSpecHvac()
        {
            ShowPanel(pnlSpecHvac, null, "Spec ĐHKK: Quy cách ống gió, cùm treo, cao độ dàn lạnh FCU, miệng gió.");
        }

        public void OpenSpecPlumbing()
        {
            ShowPanel(pnlSpecPlumbing, null, "Spec Cấp Thoát Nước: Độ dốc thoát, cùm treo ống và cao độ thiết bị vệ sinh.");
        }

        public void OpenSpecEnclosure()
        {
            ShowPanel(pnlSpecEnclosure, null, "Spec Cho Phần Vẽ Tủ Điện: Kích thước vỏ tủ, độ dày tôn, thanh cái, khoảng cách thiết bị.");
        }

        public void OpenSpecFire()
        {
            ShowPanel(pnlSpecFire, null, "Spec Cho PCCC: Cao độ đầu phun Sprinkler, van báo động, hộp chữa cháy vách tường.");
        }

        public void OpenSpecElv()
        {
            ShowPanel(pnlSpecElv, null, "Spec Điện Nhẹ (ELV): Camera CCTV, mạng Data/Tel, âm thanh thông báo PA, SmartHome & BMS.");
        }

        public void OpenSpecEarthing()
        {
            ShowPanel(pnlSpecEarthing, null, "Spec Chống Sét & Tiếp Địa: Kim thu sét ESE, cọc đồng D16, băng đồng 25x3, R <= 10/4/1 Ohm.");
        }

        public void OpenSpecSubstation()
        {
            ShowPanel(pnlSpecSubstation, null, "Spec Trạm Biến Áp & Máy Phát: Máy biến áp Kiosk/Dry, phòng máy phát tiêu âm, rãnh cáp.");
        }

        public void OpenSpecLighting()
        {
            ShowPanel(pnlSpecLighting, null, "Spec Chiếu Sáng & Điều Khiển: Tiêu chuẩn độ rọi Lux TCVN 7114, cảm biến hiện diện PIR, DALI.");
        }

        public void OpenSpecSolar()
        {
            ShowPanel(pnlSpecSolar, null, "Spec Năng Lượng Mặt Trời: Tấm pin PV 550-670Wp, Inverter hòa lưới, hệ thống Zero Export.");
        }

        private void BtnConfigUi_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigUi();
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
            ShowPanel(pnlMepXlmb, btnMepXlmb, "Phân hệ Tiền xử lý & Chuẩn hóa mặt bằng AI (7 bước).");
        }

        // [MODULE HÓA NHÁNH 5] Toàn bộ logic và nghiệp vụ Xử Lý Mặt Bằng (XLMB) đã được tách sang file: Views/PaletteParts/TaiAiPaletteControl.Xlmb.cs

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


        // [MODULE HÓA NHÁNH 1] Toàn bộ logic Thiết kế Tủ điện & Thang máng cáp (TMC) đã được tách sang file: Views/PaletteParts/TaiAiPaletteControl.Electrical.cs
        // [MODULE HÓA NHÁNH 4] Toàn bộ logic Phân hệ Phòng cháy chữa cháy (PCCC) đã được tách sang file: Views/PaletteParts/TaiAiPaletteControl.FireProtection.cs
        // [MODULE HÓA NHÁNH 2] Toàn bộ logic Phân hệ Điều hòa không khí (ĐHKK/HVAC) đã được tách sang file: Views/PaletteParts/TaiAiPaletteControl.Hvac.cs
        // [MODULE HÓA NHÁNH 3] Toàn bộ logic Phân hệ Cấp thoát nước (Plumbing) đã được tách sang file: Views/PaletteParts/TaiAiPaletteControl.Plumbing.cs

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

        #endregion
        #endregion
}


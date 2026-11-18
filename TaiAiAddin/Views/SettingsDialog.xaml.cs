using System;
using System.Windows;
using System.Windows.Input;

namespace TaiAiAddin.Views
{
    /// <summary>
    /// Cửa sổ Pop-Up Cài Đặt Tools &amp; Spec Dự Án chuẩn Dark Mode OLED Black (WCAG AAA)
    /// </summary>
    public partial class SettingsDialog : Window
    {
        private static SettingsDialog _currentInstance;

        public SettingsDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Mở hoặc kích hoạt lại cửa sổ Pop-Up Cài Đặt (chống trùng lặp tiến trình)
        /// </summary>
        public static void ShowOrActivate()
        {
            try
            {
                if (_currentInstance != null && _currentInstance.IsLoaded)
                {
                    if (_currentInstance.WindowState == WindowState.Minimized)
                        _currentInstance.WindowState = WindowState.Normal;

                    _currentInstance.Activate();
                    _currentInstance.Focus();
                    return;
                }

                _currentInstance = new SettingsDialog();
                _currentInstance.Closed += (s, e) => { _currentInstance = null; };
                _currentInstance.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở cửa sổ Cài Đặt: " + ex.Message, "Lỗi Giao Diện", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #region Các Nút Cấu Hình & Spec Toàn Dự Án

        private void BtnConfigUi_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenConfigUi();
            }
        }

        private void BtnConfigLayout_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenConfigLayout();
            }
        }

        private void BtnSpecElectrical_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecElectrical();
            }
        }

        private void BtnSpecHvac_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecHvac();
            }
        }

        private void BtnSpecPlumbing_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecPlumbing();
            }
        }

        private void BtnSpecEnclosure_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecEnclosure();
            }
        }

        private void BtnSpecFire_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecFire();
            }
        }

        private void BtnSpecElv_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecElv();
            }
        }

        private void BtnSpecEarthing_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecEarthing();
            }
        }

        private void BtnSpecSubstation_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecSubstation();
            }
        }

        private void BtnSpecLighting_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecLighting();
            }
        }

        private void BtnSpecSolar_Click(object sender, RoutedEventArgs e)
        {
            if (TaiAiPaletteControl.Instance != null)
            {
                TaiAiPaletteControl.Instance.OpenSpecSolar();
            }
        }

        #endregion
    }
}

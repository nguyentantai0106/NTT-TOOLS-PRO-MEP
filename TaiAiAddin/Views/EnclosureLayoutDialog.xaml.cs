using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;
using TaiAiAddin.Services;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TaiAiAddin.Views
{
    public partial class EnclosureLayoutDialog : Window
    {
        private ElectricalPanel _panel;
        private EnclosureLayoutParameters _params;
        private bool _isInitializing = true;

        // Trạng thái tương tác kéo thả chuột (Drag & Drop)
        private EnclosureCanvasItem _selectedItem = null;
        private FrameworkElement _selectedContainer = null;
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _itemStartLeft;
        private double _itemStartTop;
        private double _itemStartOffsetX;
        private double _itemStartOffsetY;

        public EnclosureLayoutDialog() : this(null)
        {
        }

        public EnclosureLayoutDialog(ElectricalPanel panel)
        {
            InitializeComponent();

            List<ElectricalPanel> allPanels = PanelStorageService.GetAllPanels();
            if (panel != null)
            {
                _panel = panel;
            }
            else if (allPanels != null && allPanels.Count > 0)
            {
                _panel = allPanels[0];
            }
            else
            {
                _panel = ElectricalCalculator.CreateDefaultHvacPanel();
            }

            _params = new EnclosureLayoutParameters();
            if (_panel != null)
            {
                _params.PanelName = _panel.PanelName;
            }

            LoadPanelsCombo(allPanels);
            LoadParametersToUi();

            _isInitializing = false;
            UpdatePreview();
        }

        private void LoadPanelsCombo(List<ElectricalPanel> allPanels)
        {
            if (allPanels == null) allPanels = PanelStorageService.GetAllPanels();
            cboPanels.ItemsSource = null;
            cboPanels.ItemsSource = allPanels;

            if (_panel != null)
            {
                for (int i = 0; i < allPanels.Count; i++)
                {
                    if (string.Equals(allPanels[i].PanelName, _panel.PanelName, StringComparison.OrdinalIgnoreCase))
                    {
                        cboPanels.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (cboPanels.SelectedIndex < 0 && allPanels.Count > 0)
            {
                cboPanels.SelectedIndex = 0;
            }
        }

        private void LoadParametersToUi()
        {
            if (_params == null) _params = new EnclosureLayoutParameters();

            txtWidth.Text = _params.Width.ToString("0", CultureInfo.InvariantCulture);
            txtHeight.Text = _params.Height.ToString("0", CultureInfo.InvariantCulture);
            txtDepth.Text = _params.Depth.ToString("0", CultureInfo.InvariantCulture);
            txtPlinth.Text = _params.PlinthHeight.ToString("0", CultureInfo.InvariantCulture);
            txtCanopy.Text = _params.CanopyHeight.ToString("0", CultureInfo.InvariantCulture);
            txtCadTextHeight.Text = _params.CadTextHeight.ToString("0", CultureInfo.InvariantCulture);

            txtPanelName.Text = _params.PanelName;
            txtVoltage.Text = _params.VoltageSpec;
            txtCableHole.Text = _params.CableHoleSpec;
            txtProtection.Text = _params.ProtectionRating;
            txtColor.Text = _params.ColorSpec;
            txtMaterial.Text = _params.MaterialSpec;

            chkFrontView.IsChecked = _params.IncludeFrontView;
            chkInnerDoorView.IsChecked = _params.IncludeInnerDoorView;
            chkInternalView.IsChecked = _params.IncludeInternalView;
            chkSideView.IsChecked = _params.IncludeSideView;
            chkBottomView.IsChecked = _params.IncludeBottomView;

            chkNameplate.IsChecked = _params.IncludeNameplate;
            chkPhaseLamps.IsChecked = _params.IncludePhaseLampsAndMeter;
            chkControlStations.IsChecked = _params.IncludeControlStations;
            chkEmergencyStop.IsChecked = _params.IncludeEmergencyStop;
            chkDoorLock.IsChecked = _params.IncludeDoorLock;
            chkVentFan.IsChecked = _params.IncludeVentilationFan;

            chkBusbar.IsChecked = _params.IncludeCopperBusbar;
            chkMainMccb.IsChecked = _params.IncludeMainMccb;
            chkBranchMcbs.IsChecked = _params.IncludeBranchMcbs;
            chkLargeFrameMcb.IsChecked = _params.UseLargeFrameMcb;
            chkStarters.IsChecked = _params.IncludeMotorStarters;
            chkRelays.IsChecked = _params.IncludeControlRelays;
            chkTerminals.IsChecked = _params.IncludeTerminalBlocks;
            chkWiringDucts.IsChecked = _params.IncludeWiringDucts;
            chkDimensions.IsChecked = _params.IncludeDimensions;
            chkSpecTable.IsChecked = _params.IncludeSpecTable;

            UpdateTotalHeightBadge();
        }

        private void SyncUiToParameters()
        {
            if (_params == null) _params = new EnclosureLayoutParameters();

            double val;
            if (double.TryParse(txtWidth.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out val) && val > 0)
                _params.Width = val;
            if (double.TryParse(txtHeight.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out val) && val > 0)
                _params.Height = val;
            if (double.TryParse(txtDepth.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out val) && val > 0)
                _params.Depth = val;
            if (double.TryParse(txtPlinth.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out val) && val >= 0)
                _params.PlinthHeight = val;
            if (double.TryParse(txtCanopy.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out val) && val >= 0)
                _params.CanopyHeight = val;
            if (double.TryParse(txtCadTextHeight.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out val) && val > 0)
                _params.CadTextHeight = val;

            _params.PanelName = txtPanelName.Text.Trim();
            _params.VoltageSpec = txtVoltage.Text.Trim();
            _params.CableHoleSpec = txtCableHole.Text.Trim();
            _params.ProtectionRating = txtProtection.Text.Trim();
            _params.ColorSpec = txtColor.Text.Trim();
            _params.MaterialSpec = txtMaterial.Text.Trim();

            _params.IncludeFrontView = chkFrontView.IsChecked == true;
            _params.IncludeInnerDoorView = chkInnerDoorView.IsChecked == true;
            _params.IncludeInternalView = chkInternalView.IsChecked == true;
            _params.IncludeSideView = chkSideView.IsChecked == true;
            _params.IncludeBottomView = chkBottomView.IsChecked == true;

            _params.IncludeNameplate = chkNameplate.IsChecked == true;
            _params.IncludePhaseLampsAndMeter = chkPhaseLamps.IsChecked == true;
            _params.IncludeControlStations = chkControlStations.IsChecked == true;
            _params.IncludeEmergencyStop = chkEmergencyStop.IsChecked == true;
            _params.IncludeDoorLock = chkDoorLock.IsChecked == true;
            _params.IncludeVentilationFan = chkVentFan.IsChecked == true;

            _params.IncludeCopperBusbar = chkBusbar.IsChecked == true;
            _params.IncludeMainMccb = chkMainMccb.IsChecked == true;
            _params.IncludeBranchMcbs = chkBranchMcbs.IsChecked == true;
            _params.UseLargeFrameMcb = chkLargeFrameMcb.IsChecked == true;
            _params.IncludeMotorStarters = chkStarters.IsChecked == true;
            _params.IncludeControlRelays = chkRelays.IsChecked == true;
            _params.IncludeTerminalBlocks = chkTerminals.IsChecked == true;
            _params.IncludeWiringDucts = chkWiringDucts.IsChecked == true;
            _params.IncludeDimensions = chkDimensions.IsChecked == true;
            _params.IncludeSpecTable = chkSpecTable.IsChecked == true;

            UpdateTotalHeightBadge();
        }

        private void UpdateTotalHeightBadge()
        {
            if (txtTotalHeightBadge == null || _params == null) return;
            double totalH = _params.Height + _params.PlinthHeight + _params.CanopyHeight;
            txtTotalHeightBadge.Text = string.Format("Tổng H: {0:0} mm", totalH);
        }

        // =========================================================================
        // PREVIEW RENDERING & INTERACTIVE DRAG HOOKS
        // =========================================================================

        private void UpdatePreview()
        {
            if (_isInitializing || previewCanvas == null) return;
            try
            {
                DeselectAll();
                EnclosureLayoutDrawingService.RenderPreviewToCanvas(previewCanvas, _panel, _params, OnCanvasItemCreated);
                RefreshTreeList();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Lỗi hiển thị xem trước: " + ex.Message;
            }
        }

        private void RefreshTreeList()
        {
            if (lstItemsTree == null || _params == null || _params.CustomItems == null) return;
            List<EnclosureCanvasItem> items = new List<EnclosureCanvasItem>();
            foreach (var kv in _params.CustomItems)
            {
                if (!kv.Value.IsDeleted) items.Add(kv.Value);
            }
            lstItemsTree.ItemsSource = null;
            lstItemsTree.ItemsSource = items;
        }

        private void OnCanvasItemCreated(EnclosureCanvasItem item, FrameworkElement container)
        {
            if (container == null || item == null) return;

            container.PreviewMouseLeftButtonDown += (s, e) =>
            {
                SelectItem(item, container);
                if (item.IsLocked) return;

                _isDragging = true;
                _dragStartPoint = e.GetPosition(previewCanvas);
                _itemStartLeft = Canvas.GetLeft(container);
                _itemStartTop = Canvas.GetTop(container);
                _itemStartOffsetX = item.OffsetX;
                _itemStartOffsetY = item.OffsetY;

                container.CaptureMouse();
                e.Handled = true;
            };

            container.MouseMove += (s, e) =>
            {
                if (!_isDragging || _selectedContainer != container || _selectedItem != item) return;

                Point curPt = e.GetPosition(previewCanvas);
                double dx = curPt.X - _dragStartPoint.X;
                double dy = curPt.Y - _dragStartPoint.Y;

                double sScale = 0.22;
                double modelDx = dx / sScale;
                double modelDy = -dy / sScale; // CAD Y goes upwards

                if (chkSnapGrid != null && chkSnapGrid.IsChecked == true)
                {
                    double snap = 10.0;
                    modelDx = Math.Round(modelDx / snap) * snap;
                    modelDy = Math.Round(modelDy / snap) * snap;
                    dx = modelDx * sScale;
                    dy = -modelDy * sScale;
                }

                Canvas.SetLeft(container, _itemStartLeft + dx);
                Canvas.SetTop(container, _itemStartTop + dy);

                item.OffsetX = _itemStartOffsetX + modelDx;
                item.OffsetY = _itemStartOffsetY + modelDy;

                txtItemOffX.Text = item.OffsetX.ToString("0.0", CultureInfo.InvariantCulture);
                txtItemOffY.Text = item.OffsetY.ToString("0.0", CultureInfo.InvariantCulture);
                txtStatus.Text = string.Format("🎯 Di chuyển '{0}' -> ΔX: {1:+0.0;-0.0;0} mm, ΔY: {2:+0.0;-0.0;0} mm", item.DisplayName, item.OffsetX, item.OffsetY);
            };

            container.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging && _selectedContainer == container)
                {
                    container.ReleaseMouseCapture();
                    _isDragging = false;
                    txtStatus.Text = string.Format("✅ Đã đặt '{0}' tại tọa độ dịch chuyển (ΔX: {1:+0.0;-0.0;0} mm, ΔY: {2:+0.0;-0.0;0} mm)", item.DisplayName, item.OffsetX, item.OffsetY);
                    RefreshTreeList();
                }
            };

            container.MouseEnter += (s, e) =>
            {
                if (_selectedContainer != container)
                {
                    Border b = container as Border;
                    if (b != null)
                    {
                        b.BorderBrush = Brushes.DeepSkyBlue;
                        b.BorderThickness = new Thickness(1.0);
                    }
                }
            };

            container.MouseLeave += (s, e) =>
            {
                if (_selectedContainer != container)
                {
                    Border b = container as Border;
                    if (b != null)
                    {
                        b.BorderBrush = Brushes.Transparent;
                        b.BorderThickness = new Thickness(0);
                    }
                }
            };
        }

        private void SelectItem(EnclosureCanvasItem item, FrameworkElement container)
        {
            if (_selectedContainer != null)
            {
                Border prevBdr = _selectedContainer as Border;
                if (prevBdr != null)
                {
                    prevBdr.BorderBrush = Brushes.Transparent;
                    prevBdr.BorderThickness = new Thickness(0);
                    prevBdr.Background = Brushes.Transparent;
                }
            }

            if (_selectedItem != null) _selectedItem.IsSelected = false;

            _selectedItem = item;
            _selectedContainer = container;

            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = true;
                Border bdr = _selectedContainer as Border;
                if (bdr != null)
                {
                    bdr.BorderBrush = Brushes.DodgerBlue;
                    bdr.BorderThickness = new Thickness(1.8);
                    bdr.Background = new SolidColorBrush(Color.FromArgb(35, 0, 122, 204));
                }

                txtSelectedName.Text = "⚡ " + item.DisplayName;
                txtItemOffX.Text = item.OffsetX.ToString("0.0", CultureInfo.InvariantCulture);
                txtItemOffY.Text = item.OffsetY.ToString("0.0", CultureInfo.InvariantCulture);
                txtItemText.Text = item.CustomText ?? "";
                txtItemText.IsEnabled = true;
                btnLockItem.Content = item.IsLocked ? "🔒 Đã khóa" : "🔓 Khóa";
                btnDeleteItem.IsEnabled = true;
                btnResetItemPos.IsEnabled = true;
            }
        }

        private void DeselectAll()
        {
            if (_selectedContainer != null)
            {
                Border prevBdr = _selectedContainer as Border;
                if (prevBdr != null)
                {
                    prevBdr.BorderBrush = Brushes.Transparent;
                    prevBdr.BorderThickness = new Thickness(0);
                    prevBdr.Background = Brushes.Transparent;
                }
            }

            if (_selectedItem != null) _selectedItem.IsSelected = false;
            _selectedItem = null;
            _selectedContainer = null;

            txtSelectedName.Text = "Chưa chọn đối tượng nào (Click vào hình vẽ để chọn)";
            txtItemOffX.Text = "0";
            txtItemOffY.Text = "0";
            txtItemText.Text = "";
            txtItemText.IsEnabled = false;
            btnLockItem.Content = "🔓 Khóa";
            btnDeleteItem.IsEnabled = false;
            btnResetItemPos.IsEnabled = false;
        }

        private void PreviewCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == previewCanvas)
            {
                DeselectAll();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_selectedItem == null) return;

            if (e.Key == Key.Delete)
            {
                BtnDeleteItem_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DeselectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                NudgeItem(-GetNudgeStep(), 0);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                NudgeItem(GetNudgeStep(), 0);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                NudgeItem(0, GetNudgeStep());
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                NudgeItem(0, -GetNudgeStep());
                e.Handled = true;
            }
        }

        private double GetNudgeStep()
        {
            if (cboNudgeStep == null || cboNudgeStep.SelectedIndex < 0) return 10.0;
            switch (cboNudgeStep.SelectedIndex)
            {
                case 0: return 1.0;
                case 1: return 5.0;
                case 2: return 10.0;
                case 3: return 50.0;
                default: return 10.0;
            }
        }

        private void NudgeItem(double dxMm, double dyMm)
        {
            if (_selectedItem == null || _selectedContainer == null) return;
            double sScale = 0.22;
            _selectedItem.OffsetX += dxMm;
            _selectedItem.OffsetY += dyMm;

            Canvas.SetLeft(_selectedContainer, Canvas.GetLeft(_selectedContainer) + dxMm * sScale);
            Canvas.SetTop(_selectedContainer, Canvas.GetTop(_selectedContainer) - dyMm * sScale);

            txtItemOffX.Text = _selectedItem.OffsetX.ToString("0.0", CultureInfo.InvariantCulture);
            txtItemOffY.Text = _selectedItem.OffsetY.ToString("0.0", CultureInfo.InvariantCulture);
            txtStatus.Text = string.Format("🎯 Dịch chuyển '{0}' -> ΔX: {1:+0.0;-0.0;0} mm, ΔY: {2:+0.0;-0.0;0} mm", _selectedItem.DisplayName, _selectedItem.OffsetX, _selectedItem.OffsetY);
        }

        private void BtnNudge_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null || _selectedItem == null) return;

            double step = GetNudgeStep();
            if (btn == btnNudgeLeft) NudgeItem(-step, 0);
            else if (btn == btnNudgeRight) NudgeItem(step, 0);
            else if (btn == btnNudgeUp) NudgeItem(0, step);
            else if (btn == btnNudgeDown) NudgeItem(0, -step);
        }

        private void TxtItemOffset_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _selectedItem == null || _selectedContainer == null) return;
            double ox, oy;
            if (double.TryParse(txtItemOffX.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out ox) &&
                double.TryParse(txtItemOffY.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out oy))
            {
                _selectedItem.OffsetX = ox;
                _selectedItem.OffsetY = oy;
                UpdatePreview();
                txtStatus.Text = string.Format("Đã cập nhật tọa độ '{0}' (ΔX: {1:+0.0;-0.0;0} mm, ΔY: {2:+0.0;-0.0;0} mm)", _selectedItem.DisplayName, ox, oy);
            }
        }

        private void TxtItemText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedItem == null) return;
            _selectedItem.CustomText = txtItemText.Text;
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedContainer == null) return;
            _selectedItem.IsDeleted = true;
            _selectedContainer.Visibility = Visibility.Collapsed;
            txtStatus.Text = string.Format("🗑️ Đã xóa/ẩn đối tượng '{0}' khỏi bản vẽ.", _selectedItem.DisplayName);
            DeselectAll();
            RefreshTreeList();
        }

        private void BtnResetItemPos_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            _selectedItem.ResetPosition();
            UpdatePreview();
            txtStatus.Text = string.Format("🔄 Đã khôi phục '{0}' về vị trí chuẩn ban đầu.", _selectedItem.DisplayName);
        }

        private void BtnLockItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            _selectedItem.IsLocked = !_selectedItem.IsLocked;
            btnLockItem.Content = _selectedItem.IsLocked ? "🔒 Đã khóa" : "🔓 Khóa";
            if (_selectedContainer != null)
                _selectedContainer.Cursor = _selectedItem.IsLocked ? Cursors.No : Cursors.SizeAll;
            txtStatus.Text = _selectedItem.IsLocked ? string.Format("🔒 Đã khóa vị trí '{0}'.", _selectedItem.DisplayName) : string.Format("🔓 Đã mở khóa vị trí '{0}'.", _selectedItem.DisplayName);
        }

        private void BtnResetAllItems_Click(object sender, RoutedEventArgs e)
        {
            if (_params == null) return;
            _params.ResetAllOffsets();
            UpdatePreview();
            txtStatus.Text = "🔄 Đã khôi phục toàn bộ các đối tượng và hình chiếu về vị trí chuẩn ban đầu!";
        }

        private void LstItemsTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EnclosureCanvasItem it = lstItemsTree.SelectedItem as EnclosureCanvasItem;
            if (it == null) return;

            foreach (UIElement child in previewCanvas.Children)
            {
                FrameworkElement fe = child as FrameworkElement;
                if (fe != null && fe.Tag == it)
                {
                    SelectItem(it, fe);
                    break;
                }
            }
        }

        // =========================================================================
        // GENERAL CONTROLS & AUTOCAD EXPORT
        // =========================================================================

        private void InputControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SyncUiToParameters();
            UpdatePreview();
        }

        private void CboPanels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            ElectricalPanel selected = cboPanels.SelectedItem as ElectricalPanel;
            if (selected != null)
            {
                _panel = selected;
                txtPanelName.Text = selected.PanelName;
                SyncUiToParameters();
                UpdatePreview();
            }
        }

        private void BtnReloadPanels_Click(object sender, RoutedEventArgs e)
        {
            LoadPanelsCombo(null);
            txtStatus.Text = "Đã làm mới danh sách tủ điện từ hệ thống!";
        }

        private void BtnAutoFitPanel_Click(object sender, RoutedEventArgs e)
        {
            if (_panel == null) return;

            int nLoads = _panel.Loads != null ? _panel.Loads.Count : 0;
            int nStarters = 0;
            if (_panel.Loads != null)
            {
                foreach (ElectricalLoad l in _panel.Loads)
                {
                    if (l.Starter != StarterType.Direct) nStarters++;
                }
            }

            if (nLoads > 10 || nStarters > 4)
            {
                txtWidth.Text = "800";
                txtHeight.Text = "1800";
                txtDepth.Text = "500";
            }
            else if (nLoads > 6 || nStarters > 2)
            {
                txtWidth.Text = "700";
                txtHeight.Text = "1625";
                txtDepth.Text = "400";
            }
            else
            {
                txtWidth.Text = "600";
                txtHeight.Text = "1400";
                txtDepth.Text = "350";
            }

            SyncUiToParameters();
            UpdatePreview();
            txtStatus.Text = string.Format("Đã tự động tính toán kích thước vỏ tủ ({0}x{1}x{2}) mm theo {3} phụ tải ({4} bộ khởi động)!",
                _params.Width, _params.Height, _params.Depth, nLoads, nStarters);
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            _params = new EnclosureLayoutParameters();
            if (_panel != null) _params.PanelName = _panel.PanelName;

            _isInitializing = true;
            LoadParametersToUi();
            _isInitializing = false;

            UpdatePreview();
            txtStatus.Text = "Đã khôi phục kích thước chuẩn công nghiệp Form 2B (700x1625x400 mm)!";
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (canvasScaleTransform == null || txtZoomPercent == null) return;
            double z = sliderZoom.Value;
            canvasScaleTransform.ScaleX = z;
            canvasScaleTransform.ScaleY = z;
            txtZoomPercent.Text = string.Format("{0:0}%", z * 100);
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            sliderZoom.Value = Math.Min(sliderZoom.Maximum, sliderZoom.Value + 0.15);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            sliderZoom.Value = Math.Max(sliderZoom.Minimum, sliderZoom.Value - 0.15);
        }

        private void BtnZoomFit_Click(object sender, RoutedEventArgs e)
        {
            if (scrollPreview == null || previewCanvas == null) return;
            double viewW = scrollPreview.ActualWidth - 60.0;
            double viewH = scrollPreview.ActualHeight - 60.0;
            if (viewW > 50 && viewH > 50 && previewCanvas.Width > 50 && previewCanvas.Height > 50)
            {
                double zx = viewW / previewCanvas.Width;
                double zy = viewH / previewCanvas.Height;
                double fitZ = Math.Max(sliderZoom.Minimum, Math.Min(sliderZoom.Maximum, Math.Min(zx, zy)));
                sliderZoom.Value = fitZ;
                txtStatus.Text = string.Format("Đã tự động căn chỉnh tỷ lệ vừa khung: {0:0}%", fitZ * 100);
            }
        }

        private void BtnRefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            SyncUiToParameters();
            UpdatePreview();
            txtStatus.Text = "Đã làm mới khung xem trước bản vẽ!";
        }

        private void BtnDrawToCad_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Không tìm thấy bản vẽ AutoCAD nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SyncUiToParameters();

            try
            {
                Editor ed = doc.Editor;
                Point3d insertPt = Point3d.Origin;
                bool hasPt = false;

                using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
                {
                    PromptPointOptions ppo = new PromptPointOptions(string.Format("\n[NTT TOOLS] Chọn điểm đặt Bản vẽ Mặt Bằng Bố Trí Vỏ Tủ '{0}': ", _params.PanelName));
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.OK)
                    {
                        insertPt = ppr.Value;
                        hasPt = true;
                    }
                }

                if (hasPt)
                {
                    EnclosureLayoutDrawingService svc = new EnclosureLayoutDrawingService();
                    bool success = svc.DrawFullEnclosureLayoutPlan(doc, _panel, insertPt, _params);
                    if (success)
                    {
                        ed.WriteMessage(string.Format("\n[NTT TOOLS] Đã xuất Bản vẽ Mặt Bằng Bố Trí Vỏ Tủ '{0}' ({1}x{2}x{3} mm) vào AutoCAD thành công!",
                            _params.PanelName, _params.Width, _params.Height, _params.Depth));
                        txtStatus.Text = string.Format("✅ Đã xuất thành công Bản vẽ Mặt Bằng Vỏ Tủ '{0}' vào AutoCAD!", _params.PanelName);
                        MessageBox.Show(string.Format("Đã xuất hoàn tất Bản vẽ Mặt Bằng Bố Trí Vỏ Tủ '{0}' vào AutoCAD!\nKích thước: {1} x {2} x {3} mm.",
                            _params.PanelName, _params.Width, _params.Height, _params.Depth),
                            "Xuất Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        txtStatus.Text = "❌ Xuất bản vẽ không thành công! Vui lòng kiểm tra dòng lệnh AutoCAD.";
                        MessageBox.Show("Không thể xuất bản vẽ vào AutoCAD! Vui lòng kiểm tra thông báo lỗi trên cửa sổ dòng lệnh AutoCAD.", "Lỗi Xuất Bản Vẽ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    txtStatus.Text = "Đã hủy thao tác chọn điểm đặt bản vẽ.";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Lỗi xuất bản vẽ: " + ex.Message;
                MessageBox.Show("Lỗi khi vẽ Mặt bằng bố trí vỏ tủ: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

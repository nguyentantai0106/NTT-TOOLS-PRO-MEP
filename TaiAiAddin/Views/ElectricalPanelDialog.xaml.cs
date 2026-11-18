﻿﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Microsoft.Win32;
using TaiAiAddin.Models;
using TaiAiAddin.Services;
using System.Linq;
using TaiAiAddin.Models;
using TaiAiAddin.Services;
namespace TaiAiAddin.Views
{
public partial class ElectricalPanelDialog : Window
{
		private enum PreviewHostLocation
		{
			Tab2,
			SidePanel,
			Lightbox
		}

		private ElectricalPanel _panel;

		private ObservableCollection<ElectricalPanel> _panelsList;

		private readonly ElectricalCalculator _calculator;

		private readonly ElectricalDrawingService _drawingService;

		private DrawingParameters _params;

		private bool _isInitializing;
		private bool _isSwitchingPanel = false;

		private bool _isCalculating;

		private PreviewHostLocation _currentHostLocation;

		private PreviewHostLocation _previousHostBeforeLightbox;

		private double _currentZoom = 1.0;

		private Point _panStartPoint;

		private double _panStartHOffset;

		private double _panStartVOffset;

		private bool _isPanning;

		private bool _isPanModeActive;

		private bool _isDarkCadBackground = true;

		private DispatcherTimer _toolbarFadeTimer;

		private SldInteractiveItem _selectedSldItem;

		private Canvas _selectedSldContainer;

		private Border _sldSelectionBox;

		private Border _sldGizmoToolbar;

		private bool _isDraggingSldItem;

		private Point _dragStartMouse;

		private double _dragStartOffsetX;

		private double _dragStartOffsetY;

		private bool _isSyncingLayoutControls;

		private TextBlock _txtGizmoInfo;







































































































		static ElectricalPanelDialog()
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
						return typeof(ElectricalPanelDialog).Assembly;
					}
				}
				catch { }
				return null;
			};
		}

		public ElectricalPanelDialog() : this(null)
		{
		}

		public ElectricalPanelDialog(string initialPanelName = null)
		{
			_isInitializing = true;
			InitializeComponent();
			_calculator = new ElectricalCalculator();
			_drawingService = new ElectricalDrawingService();
			_params = new DrawingParameters();
			this.Closing += OnDialogClosing;
			InitPanels(initialPanelName);
			if (_panel != null && _panel.DrawingParams != null)
			{
				_params = _panel.DrawingParams;
			}
			else if (_panel != null)
			{
				_panel.DrawingParams = _params;
			}
			SyncParamsToUi();
			InitToolbarFadeTimer();
			UpdatePanelCounter();
			if (!string.IsNullOrEmpty(initialPanelName))
			{
				EnsureSidePanelOpen();
			}
			_isInitializing = false;
		}

		private void OnDialogClosing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			try
			{
				CleanCollectionView(_panelsList);
				CleanCollectionView(_panel != null ? _panel.Loads : null);
				try { if (dgLoads != null) { dgLoads.CommitEdit(DataGridEditingUnit.Row, true); dgLoads.CancelEdit(); } } catch { }
				if (_panel != null)
				{
					_panel.DrawingParams = (_params != null ? _params.Clone() : null);
					PanelStorageService.AddOrUpdatePanel(_panel);
				}
				var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager != null ? Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument : null;
				if (doc != null)
				{
					using (doc.LockDocument())
					{
						PanelStorageService.SavePanelsToDwg(doc.Database);
					}
				}
			}
			catch { }
		}

		private void InitPanels(string initialPanelName)
		{
			var docActive = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager != null ? Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument : null;
			if (docActive != null)
			{
				try
				{
					using (docActive.LockDocument())
					{
						PanelStorageService.LoadPanelsFromDwg(docActive.Database);
					}
				}
				catch { }
			}

			List<ElectricalPanel> allPanels = PanelStorageService.GetAllPanels();
			_panelsList = new ObservableCollection<ElectricalPanel>(allPanels);
			cboPanels.ItemsSource = _panelsList;
			ElectricalPanel electricalPanel = null;
			if (!string.IsNullOrEmpty(initialPanelName))
			{
				Document mdiActiveDocument = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
				if (mdiActiveDocument != null)
				{
					try
					{
						using (mdiActiveDocument.LockDocument())
						{
							using (Transaction transaction = mdiActiveDocument.Database.TransactionManager.StartTransaction())
							{
								ElectricalPanel electricalPanel2 = ElectricalDrawingService.RestorePanelFromDrawing(initialPanelName, transaction, mdiActiveDocument.Database);
								if (electricalPanel2 != null)
								{
									electricalPanel = electricalPanel2;
									PanelStorageService.AddOrUpdatePanel(electricalPanel);
									int num = -1;
									for (int i = 0; i < _panelsList.Count; i++)
									{
										if (string.Equals(_panelsList[i].PanelName, initialPanelName, StringComparison.OrdinalIgnoreCase))
										{
											num = i;
											break;
										}
									}
									if (num >= 0)
									{
										_panelsList[num] = electricalPanel;
									}
									else
									{
										_panelsList.Add(electricalPanel);
									}
								}
								transaction.Commit();
							}
						}
					}
					catch
					{
					}
				}
				if (electricalPanel == null)
				{
					foreach (ElectricalPanel panels in _panelsList)
					{
						if (string.Equals(panels.PanelName, initialPanelName, StringComparison.OrdinalIgnoreCase))
						{
							electricalPanel = panels;
							break;
						}
					}
				}
			}
			if (electricalPanel == null && _panelsList.Count > 0)
			{
				electricalPanel = _panelsList[0];
			}
			cboPanels.SelectedItem = electricalPanel;
			SwitchToPanel(electricalPanel);
		}

		public static void CleanCollectionView(object collection)
		{
			if (collection == null) return;
			try
			{
				System.ComponentModel.IEditableCollectionView ecv = System.Windows.Data.CollectionViewSource.GetDefaultView(collection) as System.ComponentModel.IEditableCollectionView;
				if (ecv != null)
				{
					if (ecv.IsAddingNew) { try { ecv.CommitNew(); } catch { try { ecv.CancelNew(); } catch { } } }
					if (ecv.IsEditingItem) { try { ecv.CommitEdit(); } catch { try { ecv.CancelEdit(); } catch { } } }
				}
			}
			catch { }
		}

		private void SafeRefreshDataGrid()
		{
			if (dgLoads == null || _panel == null || _panel.Loads == null) return;
			try
			{
				CleanCollectionView(_panel.Loads);
				try { dgLoads.CommitEdit(DataGridEditingUnit.Cell, true); } catch { }
				try { dgLoads.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
				try { dgLoads.CancelEdit(); } catch { }
				dgLoads.Items.Refresh();
				if (dgMccStarters != null && dgMccStarters.ItemsSource != null)
				{
					try { dgMccStarters.CommitEdit(DataGridEditingUnit.Cell, true); } catch { }
					try { dgMccStarters.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
					try { dgMccStarters.CancelEdit(); } catch { }
					dgMccStarters.Items.Refresh();
				}
			}
			catch
			{
				try
				{
					CleanCollectionView(_panel.Loads);
					dgLoads.ItemsSource = null;
					dgLoads.ItemsSource = _panel.Loads;
					if (dgMccStarters != null)
					{
						dgMccStarters.ItemsSource = null;
						dgMccStarters.ItemsSource = _panel.Loads;
					}
				}
				catch { }
			}
		}

		private void SafeRefreshPanelsCombo()
		{
			// Không gọi Items.Refresh() để tránh lỗi 'Refresh is not allowed during AddNew/EditItem'
		}

		private void SwitchToPanel(ElectricalPanel panel)
		{
			if (panel != null)
			{
				_isSwitchingPanel = true;
				try
				{
					CleanCollectionView(_panelsList);
					CleanCollectionView(panel.Loads);
					PanelStorageService.SanitizePanel(panel);
					_panel = panel;
				if (_panel.DrawingParams != null)
				{
					_params = _panel.DrawingParams;
				}
				else
				{
					_panel.DrawingParams = _params;
				}
				txtPanelName.Text = _panel.PanelName;
				txtKdt.Text = _panel.SimultaneousFactor.ToString("0.##");
				txtCosPhi.Text = _panel.PowerFactor.ToString("0.##");
				if (cmbDefaultCableType != null)
				{
					cmbDefaultCableType.SelectedValue = _panel.DefaultCableType;
				}
				if (cboIncomer != null)
				{
					cboIncomer.SelectedIndex = (int)_panel.Incomer;
				}
				if (chkHasCapacitor != null)
				{
					chkHasCapacitor.IsChecked = _panel.HasCapacitorBank;
				}
				if (chkIncludeLegend != null)
				{
					chkIncludeLegend.IsChecked = _params.IncludeLegend;
				}
				if (chkIncludeLegendTab != null)
				{
					chkIncludeLegendTab.IsChecked = _params.IncludeLegend;
				}
				if (chkIncludeTitleBlock != null)
				{
					chkIncludeTitleBlock.IsChecked = _params.IncludeTitleBlock;
				}
				if (chkDrawFrontView != null)
				{
					chkDrawFrontView.IsChecked = _params.DrawFrontView;
				}
				if (txtDrawingTitle != null)
				{
					txtDrawingTitle.Text = _params.DrawingTitle ?? ("SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN " + _panel.PanelName);
				}
				AttachLoadEvents();
				RefreshUi();
				DrawCanvasPreview();
				UpdatePanelCounter();
				}
				finally
				{
					_isSwitchingPanel = false;
				}
			}
		}

		private void AttachPreviewTo(PreviewHostLocation target)
		{
			if (previewCanvasHost == null)
			{
				return;
			}
			ContentControl contentControl = previewCanvasHost.Parent as ContentControl;
			if (contentControl != null)
			{
				contentControl.Content = null;
			}
			switch (target)
			{
			case PreviewHostLocation.Lightbox:
				if (lightboxScrollViewer != null)
				{
					lightboxScrollViewer.Content = previewCanvasHost;
					_currentHostLocation = PreviewHostLocation.Lightbox;
				}
				break;
			case PreviewHostLocation.SidePanel:
				if (sidePreviewScrollViewer != null)
				{
					sidePreviewScrollViewer.Content = previewCanvasHost;
					_currentHostLocation = PreviewHostLocation.SidePanel;
				}
				break;
			default:
				if (tab2ScrollViewer != null)
				{
					tab2ScrollViewer.Content = previewCanvasHost;
					_currentHostLocation = PreviewHostLocation.Tab2;
				}
				break;
			}
		}

		private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.Source != mainTabControl)
			{
				return;
			}
			if (mainTabControl.SelectedIndex == 1)
			{
				AttachPreviewTo(PreviewHostLocation.Tab2);
				ZoomFitActivePreview();
			}
			else if (mainTabControl.SelectedIndex == 0)
			{
				if (sidePreviewPanel != null && sidePreviewPanel.Visibility == System.Windows.Visibility.Visible)
				{
					AttachPreviewTo(PreviewHostLocation.SidePanel);
					ZoomFitActivePreview();
				}
				else
				{
					AttachPreviewTo(PreviewHostLocation.Tab2);
				}
			}
		}

		private void BtnToggleSidePreview_Click(object sender, RoutedEventArgs e)
		{
			ToggleSidePanel();
		}

		public void EnsureSidePanelOpen()
		{
			if (sidePreviewPanel != null && sidePreviewPanel.Visibility != 0)
			{
				sidePreviewPanel.Visibility = System.Windows.Visibility.Visible;
				if (sidePreviewSplitter != null)
				{
					sidePreviewSplitter.Visibility = System.Windows.Visibility.Visible;
				}
				if (colSplitter != null)
				{
					colSplitter.Width = GridLength.Auto;
				}
				if (colSidePreview != null)
				{
					colSidePreview.Width = new GridLength(1.8, GridUnitType.Star);
				}
				if (colDataGrid != null)
				{
					colDataGrid.Width = new GridLength(2.2, GridUnitType.Star);
				}
				if (btnToggleSidePreview != null)
				{
					btnToggleSidePreview.Content = "◀ Ẩn Side-Panel";
					btnToggleSidePreview.Background = new SolidColorBrush(Color.FromRgb(69, 90, 100));
				}
				AttachPreviewTo(PreviewHostLocation.SidePanel);
				UpdatePanelCounter();
				SafeDrawCanvasPreview();
				ZoomFitActivePreview();
			}
		}

		private void ToggleSidePanel()
		{
			if (sidePreviewPanel == null)
			{
				return;
			}
			if (sidePreviewPanel.Visibility != 0)
			{
				sidePreviewPanel.Visibility = System.Windows.Visibility.Visible;
				if (sidePreviewSplitter != null)
				{
					sidePreviewSplitter.Visibility = System.Windows.Visibility.Visible;
				}
				if (colSplitter != null)
				{
					colSplitter.Width = GridLength.Auto;
				}
				if (colSidePreview != null)
				{
					colSidePreview.Width = new GridLength(1.8, GridUnitType.Star);
				}
				if (colDataGrid != null)
				{
					colDataGrid.Width = new GridLength(2.2, GridUnitType.Star);
				}
				if (btnToggleSidePreview != null)
				{
					btnToggleSidePreview.Content = "◀ Ẩn Side-Panel";
					btnToggleSidePreview.Background = new SolidColorBrush(Color.FromRgb(69, 90, 100));
				}
				AttachPreviewTo(PreviewHostLocation.SidePanel);
				UpdatePanelCounter();
				SafeDrawCanvasPreview();
				ZoomFitActivePreview();
			}
			else
			{
				CloseSidePanel();
			}
		}

		private void CloseSidePanel()
		{
			if (sidePreviewPanel != null)
			{
				sidePreviewPanel.Visibility = System.Windows.Visibility.Collapsed;
				if (sidePreviewSplitter != null)
				{
					sidePreviewSplitter.Visibility = System.Windows.Visibility.Collapsed;
				}
				if (colSplitter != null)
				{
					colSplitter.Width = new GridLength(0.0);
				}
				if (colSidePreview != null)
				{
					colSidePreview.Width = new GridLength(0.0);
				}
				if (colDataGrid != null)
				{
					colDataGrid.Width = new GridLength(1.0, GridUnitType.Star);
				}
				if (btnToggleSidePreview != null)
				{
					btnToggleSidePreview.Content = "\ud83d\udc41\ufe0f Xem Nhanh Side-Panel";
					btnToggleSidePreview.Background = new SolidColorBrush(Color.FromRgb(0, 131, 143));
				}
				if (_currentHostLocation == PreviewHostLocation.SidePanel)
				{
					AttachPreviewTo(PreviewHostLocation.Tab2);
				}
			}
		}

		private void BtnOpenLightbox_Click(object sender, RoutedEventArgs e)
		{
			OpenLightbox();
		}

		private void OpenLightbox()
		{
			if (lightboxOverlay != null)
			{
				_previousHostBeforeLightbox = _currentHostLocation;
				AttachPreviewTo(PreviewHostLocation.Lightbox);
				lightboxOverlay.Visibility = System.Windows.Visibility.Visible;
				UpdatePanelCounter();
				SafeDrawCanvasPreview();
				ZoomFitActivePreview();
				StartToolbarFadeTimer();
				lightboxOverlay.Focus();
			}
		}

		private void CloseLightbox()
		{
			if (lightboxOverlay != null)
			{
				lightboxOverlay.Visibility = System.Windows.Visibility.Collapsed;
				StopToolbarFadeTimer();
				if (_previousHostBeforeLightbox == PreviewHostLocation.SidePanel && sidePreviewPanel != null && sidePreviewPanel.Visibility == System.Windows.Visibility.Visible)
				{
					AttachPreviewTo(PreviewHostLocation.SidePanel);
				}
				else
				{
					AttachPreviewTo(PreviewHostLocation.Tab2);
				}
				ZoomFitActivePreview();
				if (dgLoads != null)
				{
					dgLoads.Focus();
				}
			}
		}

		private void BtnExitLightbox_Click(object sender, RoutedEventArgs e)
		{
			CloseLightbox();
		}

		private void LightboxBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			CloseLightbox();
		}

		private void ApplyZoom(double newZoom)
		{
			_currentZoom = Math.Max(0.15, Math.Min(5.0, newZoom));
			if (previewScaleTransform != null)
			{
				previewScaleTransform.ScaleX = _currentZoom;
				previewScaleTransform.ScaleY = _currentZoom;
			}
			string text = string.Format("{0:0}%", _currentZoom * 100.0);
			if (txtLightboxZoom != null)
			{
				txtLightboxZoom.Text = text;
			}
			if (txtTab2Zoom != null)
			{
				txtTab2Zoom.Text = text;
			}
		}

		private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
		{
			ApplyZoom(_currentZoom * 1.25);
		}

		private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
		{
			ApplyZoom(_currentZoom / 1.25);
		}

		private void BtnZoom100_Click(object sender, RoutedEventArgs e)
		{
			ApplyZoom(1.0);
		}

		private void BtnZoomFit_Click(object sender, RoutedEventArgs e)
		{
			ZoomFitActivePreview();
		}

		private void ZoomFitActivePreview()
		{
			ScrollViewer activeSv = null;
			if (lightboxOverlay != null && lightboxOverlay.Visibility == System.Windows.Visibility.Visible)
			{
				activeSv = lightboxScrollViewer;
			}
			else if (sidePreviewPanel != null && sidePreviewPanel.Visibility == System.Windows.Visibility.Visible)
			{
				activeSv = sidePreviewScrollViewer;
			}
			else
			{
				activeSv = tab2ScrollViewer;
			}
			if (activeSv == null || previewCanvas == null)
			{
				return;
			}
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				double num = activeSv.ActualWidth - 60.0;
				double num2 = activeSv.ActualHeight - 60.0;
				if (num > 50.0 && num2 > 50.0 && previewCanvas.Width > 50.0 && previewCanvas.Height > 50.0)
				{
					double val = num / previewCanvas.Width;
					double val2 = num2 / previewCanvas.Height;
					double newZoom = Math.Max(0.15, Math.Min(3.0, Math.Min(val, val2)));
					ApplyZoom(newZoom);
				}
				else
				{
					ApplyZoom(0.75);
				}
			}, DispatcherPriority.Loaded);
		}

		private void BtnTogglePanMode_Click(object sender, RoutedEventArgs e)
		{
			_isPanModeActive = !_isPanModeActive;
			if (btnTogglePanMode != null)
			{
				btnTogglePanMode.Background = (_isPanModeActive ? new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue)) : new SolidColorBrush(Color.FromRgb(42, 50, 70)));
				btnTogglePanMode.Foreground = (_isPanModeActive ? new SolidColorBrush(Color.FromRgb(18, 22, 31)) : new SolidColorBrush(Color.FromRgb(248, 250, 252)));
			}
		}

		private void PreviewScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.MiddleButton == MouseButtonState.Pressed || (e.LeftButton == MouseButtonState.Pressed && _isPanModeActive))
			{
				ScrollViewer scrollViewer = sender as ScrollViewer;
				if (scrollViewer != null)
				{
					_isPanning = true;
					_panStartPoint = e.GetPosition(scrollViewer);
					_panStartHOffset = scrollViewer.HorizontalOffset;
					_panStartVOffset = scrollViewer.VerticalOffset;
					scrollViewer.Cursor = Cursors.SizeAll;
					scrollViewer.CaptureMouse();
					e.Handled = true;
				}
			}
		}

		private void PreviewScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (_isPanning)
			{
				ScrollViewer scrollViewer = sender as ScrollViewer;
				if (scrollViewer != null)
				{
					Point position = e.GetPosition(scrollViewer);
					double num = position.X - _panStartPoint.X;
					double num2 = position.Y - _panStartPoint.Y;
					scrollViewer.ScrollToHorizontalOffset(_panStartHOffset - num);
					scrollViewer.ScrollToVerticalOffset(_panStartVOffset - num2);
					e.Handled = true;
				}
			}
		}

		private void PreviewScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
		{
			if (_isPanning)
			{
				ScrollViewer scrollViewer = sender as ScrollViewer;
				if (scrollViewer != null)
				{
					_isPanning = false;
					scrollViewer.Cursor = Cursors.Arrow;
					scrollViewer.ReleaseMouseCapture();
					e.Handled = true;
				}
			}
		}

		private void PreviewScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (Keyboard.Modifiers == ModifierKeys.Control || (lightboxOverlay != null && lightboxOverlay.Visibility == System.Windows.Visibility.Visible))
			{
				double num = ((e.Delta > 0) ? 1.15 : 0.85);
				ApplyZoom(_currentZoom * num);
				e.Handled = true;
			}
		}

		private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			DependencyObject dependencyObject = FocusManager.GetFocusedElement(this) as DependencyObject;
			bool flag = dependencyObject is TextBox || dependencyObject is ComboBox || dependencyObject is ComboBoxItem;
			if (_selectedSldItem != null && !flag)
			{
				if (e.Key == Key.Escape)
				{
					DeselectSldItem();
					e.Handled = true;
					return;
				}
				if (e.Key == Key.R)
				{
					RotateSelectedSldItem();
					e.Handled = true;
					return;
				}
				if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
				{
					CopySelectedSldItem();
					e.Handled = true;
					return;
				}
				if (e.Key == Key.OemPlus || e.Key == Key.Add)
				{
					ScaleSelectedSldItem(1.1);
					e.Handled = true;
					return;
				}
				if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
				{
					ScaleSelectedSldItem(0.9);
					e.Handled = true;
					return;
				}
				if (e.Key == Key.Delete || e.Key == Key.Back)
				{
					DeleteSelectedSldItem();
					e.Handled = true;
					return;
				}
				double num = (((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) ? 20.0 : (((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ? 1.0 : 5.0));
				if (e.Key == Key.Up)
				{
					NudgeSelectedSldItem(0.0, 0.0 - num);
					e.Handled = true;
					return;
				}
				if (e.Key == Key.Down)
				{
					NudgeSelectedSldItem(0.0, num);
					e.Handled = true;
					return;
				}
				if (e.Key == Key.Left)
				{
					NudgeSelectedSldItem(0.0 - num, 0.0);
					e.Handled = true;
					return;
				}
				if (e.Key == Key.Right)
				{
					NudgeSelectedSldItem(num, 0.0);
					e.Handled = true;
					return;
				}
			}
			if (lightboxOverlay != null && lightboxOverlay.Visibility == System.Windows.Visibility.Visible)
			{
				if (e.Key == Key.Escape)
				{
					CloseLightbox();
					e.Handled = true;
				}
				else if (e.Key == Key.Left || e.Key == Key.Prior)
				{
					NavigatePreviousPanel();
					e.Handled = true;
				}
				else if (e.Key == Key.Right || e.Key == Key.Next)
				{
					NavigateNextPanel();
					e.Handled = true;
				}
				else if (e.Key == Key.OemPlus || e.Key == Key.Add)
				{
					ApplyZoom(_currentZoom * 1.25);
					e.Handled = true;
				}
				else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
				{
					ApplyZoom(_currentZoom / 1.25);
					e.Handled = true;
				}
				else if (e.Key == Key.D0 || e.Key == Key.NumPad0 || e.Key == Key.F)
				{
					ZoomFitActivePreview();
					e.Handled = true;
				}
				else if (e.Key == Key.D1 || e.Key == Key.NumPad1)
				{
					ApplyZoom(1.0);
					e.Handled = true;
				}
			}
			else if (e.Key == Key.Escape && sidePreviewPanel != null && sidePreviewPanel.Visibility == System.Windows.Visibility.Visible && !flag)
			{
				CloseSidePanel();
				e.Handled = true;
			}
		}

		private void BtnLightboxPrev_Click(object sender, RoutedEventArgs e)
		{
			NavigatePreviousPanel();
		}

		private void BtnLightboxNext_Click(object sender, RoutedEventArgs e)
		{
			NavigateNextPanel();
		}

		private void NavigatePreviousPanel()
		{
			if (_panelsList != null && _panelsList.Count > 1)
			{
				int num = _panelsList.IndexOf(_panel);
				int index = ((num > 0) ? (num - 1) : (_panelsList.Count - 1));
				cboPanels.SelectedItem = _panelsList[index];
			}
		}

		private void NavigateNextPanel()
		{
			if (_panelsList != null && _panelsList.Count > 1)
			{
				int num = _panelsList.IndexOf(_panel);
				int index = ((num < _panelsList.Count - 1) ? (num + 1) : 0);
				cboPanels.SelectedItem = _panelsList[index];
			}
		}

		private void UpdatePanelCounter()
		{
			if (_panelsList != null && _panel != null)
			{
				int num = _panelsList.IndexOf(_panel);
				string text = string.Format("Tủ {0}/{1}: {2}", (num < 0) ? 1 : (num + 1), _panelsList.Count, _panel.PanelName ?? "Không tên");
				if (txtLightboxPanelCounter != null)
				{
					txtLightboxPanelCounter.Text = text;
				}
				if (txtSidePanelTitle != null)
				{
					txtSidePanelTitle.Text = text;
				}
			}
		}

		private void InitToolbarFadeTimer()
		{
			_toolbarFadeTimer = new DispatcherTimer();
			_toolbarFadeTimer.Interval = TimeSpan.FromSeconds(2.5);
			_toolbarFadeTimer.Tick += delegate
			{
				if (pnlLightboxToolbar != null && lightboxOverlay != null && lightboxOverlay.Visibility == System.Windows.Visibility.Visible)
				{
					DoubleAnimation animation = new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(400.0));
					pnlLightboxToolbar.BeginAnimation(UIElement.OpacityProperty, animation);
				}
				_toolbarFadeTimer.Stop();
			};
		}

		private void StartToolbarFadeTimer()
		{
			if (_toolbarFadeTimer == null)
			{
				InitToolbarFadeTimer();
			}
			if (pnlLightboxToolbar != null)
			{
				pnlLightboxToolbar.BeginAnimation(UIElement.OpacityProperty, null);
				pnlLightboxToolbar.Opacity = 1.0;
			}
			_toolbarFadeTimer.Stop();
			_toolbarFadeTimer.Start();
		}

		private void StopToolbarFadeTimer()
		{
			if (_toolbarFadeTimer != null)
			{
				_toolbarFadeTimer.Stop();
			}
			if (pnlLightboxToolbar != null)
			{
				pnlLightboxToolbar.BeginAnimation(UIElement.OpacityProperty, null);
				pnlLightboxToolbar.Opacity = 1.0;
			}
		}

		private void Lightbox_MouseMove(object sender, MouseEventArgs e)
		{
			if (pnlLightboxToolbar != null && lightboxOverlay != null && lightboxOverlay.Visibility == System.Windows.Visibility.Visible)
			{
				if (pnlLightboxToolbar.Opacity < 0.95)
				{
					DoubleAnimation animation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180.0));
					pnlLightboxToolbar.BeginAnimation(UIElement.OpacityProperty, animation);
				}
				_toolbarFadeTimer.Stop();
				_toolbarFadeTimer.Start();
			}
		}

		private void BtnToggleCadTheme_Click(object sender, RoutedEventArgs e)
		{
			_isDarkCadBackground = !_isDarkCadBackground;
			UpdatePreviewBackgroundTheme();
		}

		private void UpdatePreviewBackgroundTheme()
		{
			if (previewCanvasHost != null && previewCanvas != null)
			{
				SolidColorBrush background = (_isDarkCadBackground ? new SolidColorBrush(Color.FromRgb(18, 22, 31)) : new SolidColorBrush(Color.FromRgb(241, 245, 249)));
				if (lightboxScrollViewer != null)
				{
					lightboxScrollViewer.Background = background;
				}
				if (sidePreviewScrollViewer != null)
				{
					sidePreviewScrollViewer.Background = background;
				}
				if (tab2ScrollViewer != null)
				{
					tab2ScrollViewer.Background = background;
				}
				if (btnToggleCadTheme != null)
				{
					btnToggleCadTheme.Content = (_isDarkCadBackground ? "\ud83c\udf13 Đổi Nền (Tối)" : "\ud83c\udf13 Đổi Nền (Sáng)");
				}
			}
		}

		private void SafeDrawCanvasPreview()
		{
			if (pnlSideLoading != null && sidePreviewPanel != null && sidePreviewPanel.Visibility == System.Windows.Visibility.Visible)
			{
				pnlSideLoading.Visibility = System.Windows.Visibility.Visible;
			}
			if (pnlLightboxLoading != null && lightboxOverlay != null && lightboxOverlay.Visibility == System.Windows.Visibility.Visible)
			{
				pnlLightboxLoading.Visibility = System.Windows.Visibility.Visible;
			}
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				try
				{
					DrawCanvasPreview();
					UpdatePanelCounter();
				}
				finally
				{
					if (pnlSideLoading != null)
					{
						pnlSideLoading.Visibility = System.Windows.Visibility.Collapsed;
					}
					if (pnlLightboxLoading != null)
					{
						pnlLightboxLoading.Visibility = System.Windows.Visibility.Collapsed;
					}
				}
			}, DispatcherPriority.Render);
		}

		private DispatcherTimer _previewDebounceTimer;

		private void RequestPreviewRedraw()
		{
			if (_previewDebounceTimer == null)
			{
				_previewDebounceTimer = new DispatcherTimer();
				_previewDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
				_previewDebounceTimer.Tick += delegate
				{
					_previewDebounceTimer.Stop();
					DrawCanvasPreview();
				};
			}
			_previewDebounceTimer.Stop();
			_previewDebounceTimer.Start();
		}

		private void AttachLoadEvents()
		{
			if (_panel == null || _panel.Loads == null)
			{
				return;
			}
			foreach (ElectricalLoad load in _panel.Loads)
			{
				load.PropertyChanged -= Load_PropertyChanged;
				load.PropertyChanged += Load_PropertyChanged;
			}
		}

		private void Load_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (!_isInitializing && !_isCalculating && (e.PropertyName == "CableType" || e.PropertyName == "CoreType" || e.PropertyName == "FullCableString" || e.PropertyName == "HasDol" || e.PropertyName == "Starter" || e.PropertyName == "Control" || e.PropertyName == "LoadType" || e.PropertyName == "CableLength" || e.PropertyName == "PowerKW" || e.PropertyName == "Phase"))
			{
				ElectricalLoad electricalLoad = sender as ElectricalLoad;
				if (electricalLoad != null && (e.PropertyName == "CableLength" || e.PropertyName == "PowerKW" || e.PropertyName == "Phase"))
				{
					double voltageV = ((electricalLoad.Phase == PhaseType.ThreePhase) ? 380.0 : 220.0);
					ElectricalCalculator.CalculateVoltageDrop(electricalLoad, voltageV, (_panel != null) ? _panel.PowerFactor : 0.85);
				}
				RequestPreviewRedraw();
			}
		}

		private void CboPanels_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (_isInitializing || _isSwitchingPanel)
			{
				return;
			}
			ElectricalPanel electricalPanel = cboPanels.SelectedItem as ElectricalPanel;
			if (electricalPanel != null && electricalPanel != _panel)
			{
				if (_panel != null)
				{
					_panel.DrawingParams = (_params != null ? _params.Clone() : null);
					PanelStorageService.AddOrUpdatePanel(_panel);
				}
				SwitchToPanel(electricalPanel);
			}
		}

		private void BtnAddPanel_Click(object sender, RoutedEventArgs e)
		{
			string text = string.Format("DB-HVAC-{0:D2}", _panelsList.Count + 1);
			ElectricalPanel electricalPanel = new ElectricalPanel();
			electricalPanel.PanelName = text;
			electricalPanel.DefaultCableType = ((!string.IsNullOrEmpty(_panel.DefaultCableType)) ? _panel.DefaultCableType : "Cu/XLPE/PVC");
			electricalPanel.Loads.Add(new ElectricalLoad
			{
				Stt = 1,
				LoadName = "Phụ tải 1",
				PowerKW = 2.2,
				Phase = PhaseType.SinglePhase,
				CircuitNumber = "C01",
				BreakerType = "MCB 1P",
				BreakerSpec = "MCB 1P-6kA 16A",
				CableLength = 20.0,
				ConductorSize = "2.5",
				AssignedPhase = "R"
			});
			_calculator.CalculatePanel(electricalPanel);
			_panelsList.Add(electricalPanel);
			PanelStorageService.AddOrUpdatePanel(electricalPanel);
			cboPanels.SelectedItem = electricalPanel;
			txtDialogStatus.Text = string.Format("Đã tạo mới tủ điện '{0}' thành công!", text);
		}

		private void BtnClonePanel_Click(object sender, RoutedEventArgs e)
		{
			if (_panel != null)
			{
				string text = string.Format("{0}-Copy", _panel.PanelName);
				ElectricalPanel electricalPanel = _panel.Clone(text);
				_calculator.CalculatePanel(electricalPanel);
				_panelsList.Add(electricalPanel);
				PanelStorageService.AddOrUpdatePanel(electricalPanel);
				cboPanels.SelectedItem = electricalPanel;
				txtDialogStatus.Text = string.Format("Đã nhân bản tủ '{0}' thành '{1}' thành công!", _panel.PanelName, text);
			}
		}

		private void BtnDeletePanel_Click(object sender, RoutedEventArgs e)
		{
			if (_panelsList.Count <= 1)
			{
				MessageBox.Show("Dự án phải có ít nhất 1 tủ điện!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}
			MessageBoxResult messageBoxResult = MessageBox.Show(string.Format("Bạn có chắc chắn muốn xóa tủ điện '{0}'?", _panel.PanelName), "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (messageBoxResult == MessageBoxResult.Yes)
			{
				string panelName = _panel.PanelName;
				int val = _panelsList.IndexOf(_panel);
				_panelsList.Remove(_panel);
				PanelStorageService.DeletePanel(panelName);
				int index = Math.Min(val, _panelsList.Count - 1);
				cboPanels.SelectedItem = _panelsList[index];
				txtDialogStatus.Text = string.Format("Đã xóa tủ điện '{0}' thành công!", panelName);
			}
		}

		private void TxtPanelName_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!_isInitializing && !_isSwitchingPanel && _panel != null && txtPanelName != null)
			{
				string text = txtPanelName.Text.Trim();
				if (!string.IsNullOrEmpty(text) && text != _panel.PanelName)
				{
					string oldName = _panel.PanelName;
					_panel.PanelName = text;
					PanelStorageService.RenamePanel(oldName, text);
					SafeRefreshPanelsCombo();
				}
			}
		}

		private void CmbDefaultCableType_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (_isInitializing || _panel == null || cmbDefaultCableType == null)
			{
				return;
			}
			ComboBoxItem comboBoxItem = cmbDefaultCableType.SelectedItem as ComboBoxItem;
			if (comboBoxItem == null || comboBoxItem.Tag == null)
			{
				return;
			}
			string text = comboBoxItem.Tag.ToString();
			_panel.DefaultCableType = text;
			foreach (ElectricalLoad load in _panel.Loads)
			{
				load.CableType = text;
			}
			RefreshUi();
			DrawCanvasPreview();
		}

		private void DolCheck_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing)
			{
				DrawCanvasPreview();
			}
		}

		private void RefreshUi()
		{
			try
			{
				CleanCollectionView(_panel != null ? _panel.Loads : null);
				if (dgLoads != null && dgLoads.ItemsSource != _panel.Loads)
				{
					dgLoads.ItemsSource = _panel.Loads;
				}
				if (dgMccStarters != null && dgMccStarters.ItemsSource != _panel.Loads)
				{
					dgMccStarters.ItemsSource = _panel.Loads;
				}
			}
			catch { }
			if (txtMccParentPanel != null && !string.IsNullOrEmpty(_panel.ParentPanelName))
			{
				txtMccParentPanel.Text = _panel.ParentPanelName;
			}
			lblInstalledPower.Text = string.Format("{0:N2} kW", _panel.TotalInstalledPowerKW);
			lblCalculatedPower.Text = string.Format("{0:N3} kW", _panel.TotalCalculatedPowerKW);
			lblTotalCurrent.Text = string.Format("{0:N1} A", _panel.TotalCurrentA);
			lblMainBreaker.Text = _panel.MainBreakerSpec;
			if (lblMainCable != null)
			{
				lblMainCable.Text = ((!string.IsNullOrEmpty(_panel.MainCableSpec)) ? _panel.MainCableSpec : "-");
			}
			if (lblPhaseDetail != null)
			{
				lblPhaseDetail.Text = string.Format("R: {0:N1} kW | Y: {1:N1} kW | B: {2:N1} kW", _panel.PhasePowerR, _panel.PhasePowerY, _panel.PhasePowerB);
			}
			if (lblUnbalanceDetail != null)
			{
				double phaseUnbalancePercentage = ElectricalCalculator.GetPhaseUnbalancePercentage(_panel);
				lblUnbalanceDetail.Text = string.Format("Độ lệch pha: {0:N1}%", phaseUnbalancePercentage);
			}
		}

		private void BtnLoadSample_Click(object sender, RoutedEventArgs e)
		{
			_panel = ElectricalCalculator.CreateDefaultHvacPanel();
			PanelStorageService.AddOrUpdatePanel(_panel);
			int num = -1;
			for (int i = 0; i < _panelsList.Count; i++)
			{
				if (string.Equals(_panelsList[i].PanelName, _panel.PanelName, StringComparison.OrdinalIgnoreCase))
				{
					num = i;
					break;
				}
			}
			if (num >= 0)
			{
				_panelsList[num] = _panel;
			}
			else
			{
				_panelsList.Add(_panel);
			}
			cboPanels.SelectedItem = _panel;
			SwitchToPanel(_panel);
			txtDialogStatus.Text = "Đã nạp bộ thông số HVAC mẫu thành công!";
		}

		private void BtnCalculate_Click(object sender, RoutedEventArgs e)
		{
			_panel.PanelName = (string.IsNullOrEmpty(txtPanelName.Text) ? "DB-HVAC-01" : txtPanelName.Text.Trim());
			double result = 0.85;
			double result2 = 0.85;
			double.TryParse(txtKdt.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
			double.TryParse(txtCosPhi.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out result2);
			_panel.SimultaneousFactor = ((result > 0.0) ? result : 0.85);
			_panel.PowerFactor = ((result2 > 0.0) ? result2 : 0.85);
			_calculator.AutoBalancePhases(_panel);
			SafeRefreshDataGrid();
			AttachLoadEvents();
			RefreshUi();
			DrawCanvasPreview();
			double phaseUnbalancePercentage = ElectricalCalculator.GetPhaseUnbalancePercentage(_panel);
			txtDialogStatus.Text = string.Format("Đã tự động cân pha thông minh (R={0:N1}kW, Y={1:N1}kW, B={2:N1}kW - Lệch {3:N1}%) và tính toán Aptomat / Dây cáp thành công!", _panel.PhasePowerR, _panel.PhasePowerY, _panel.PhasePowerB, phaseUnbalancePercentage);
		}

		private void ExecuteCalculation()
		{
			if (_panel == null || _isCalculating)
			{
				return;
			}
			try
			{
				_isCalculating = true;
				_panel.PanelName = (string.IsNullOrEmpty(txtPanelName.Text) ? "DB-HVAC-01" : txtPanelName.Text.Trim());
				double result = 0.85;
				double result2 = 0.85;
				double.TryParse(txtKdt.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
				double.TryParse(txtCosPhi.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out result2);
				_panel.SimultaneousFactor = ((result > 0.0) ? result : 0.85);
				_panel.PowerFactor = ((result2 > 0.0) ? result2 : 0.85);
				_calculator.CalculatePanel(_panel);
				AttachLoadEvents();
				RefreshUi();
				DrawCanvasPreview();
			}
			catch (Exception)
			{
				// Bảo vệ an toàn tuyệt đối, ngăn mọi ngoại lệ làm sập tiến trình AutoCAD
			}
			finally
			{
				_isCalculating = false;
			}
		}

		public void TestAddRows()
		{
			BtnAddRow3P_Click(null, null);
			BtnAddRow1P_Click(null, null);
			BtnAddSpare3P_Click(null, null);
			BtnAddSpare1P_Click(null, null);
		}

		private void BtnAddRow3P_Click(object sender, RoutedEventArgs e)
		{
			ElectricalLoad electricalLoad = new ElectricalLoad();
			electricalLoad.LoadName = string.Format("MCC-HVAC-NEW-{0:D2}", _panel.Loads.Count + 1);
			electricalLoad.Phase = PhaseType.ThreePhase;
			electricalLoad.PowerKW = 15.0;
			electricalLoad.CableType = (string.IsNullOrEmpty(_panel.DefaultCableType) ? "Cu/XLPE/PVC" : _panel.DefaultCableType);
			electricalLoad.BreakerSpec = "MCCB 3P-25kA 40A";
			electricalLoad.CableSpec = "4C-10mm2";
			electricalLoad.GroundWireSpec = "(E) Cu/PVC 1C-10mm2";
			electricalLoad.ConductorSize = "10";
			electricalLoad.AssignedPhase = "3P";
			electricalLoad.UpdateCableSpec();
			int insertIdx3P = _panel.Loads.Count;
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				if (_panel.Loads[i].IsSpare || (!string.IsNullOrEmpty(_panel.Loads[i].CircuitNumber) && _panel.Loads[i].CircuitNumber.StartsWith("SPARE")))
				{
					insertIdx3P = i;
					break;
				}
			}
			_panel.Loads.Insert(insertIdx3P, electricalLoad);
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}
			SafeRefreshDataGrid();
			ExecuteCalculation();
		}

		private void BtnAddRow1P_Click(object sender, RoutedEventArgs e)
		{
			ElectricalLoad electricalLoad = new ElectricalLoad();
			electricalLoad.LoadName = string.Format("FCU-NEW-{0:D2}", _panel.Loads.Count + 1);
			electricalLoad.Phase = PhaseType.SinglePhase;
			electricalLoad.PowerKW = 2.2;
			electricalLoad.CableType = (string.IsNullOrEmpty(_panel.DefaultCableType) ? "Cu/XLPE/PVC" : _panel.DefaultCableType);
			electricalLoad.BreakerSpec = "MCB 1P-6kA 16A";
			electricalLoad.CableSpec = "1Cx2.5mm2";
			electricalLoad.GroundWireSpec = "+E2.5mm2";
			electricalLoad.ConductorSize = "2.5";
			electricalLoad.AssignedPhase = "R";
			electricalLoad.UpdateCableSpec();
			int insertIdx1P = _panel.Loads.Count;
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				if (_panel.Loads[i].IsSpare || (!string.IsNullOrEmpty(_panel.Loads[i].CircuitNumber) && _panel.Loads[i].CircuitNumber.StartsWith("SPARE")))
				{
					insertIdx1P = i;
					break;
				}
			}
			_panel.Loads.Insert(insertIdx1P, electricalLoad);
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}
			SafeRefreshDataGrid();
			ExecuteCalculation();
		}

		private void BtnAddSpare3P_Click(object sender, RoutedEventArgs e)
		{
			ElectricalLoad electricalLoad = new ElectricalLoad();
			electricalLoad.LoadName = string.Format("LỘ DỰ PHÒNG 3P-{0:D2}", _panel.Loads.Count + 1);
			electricalLoad.Phase = PhaseType.ThreePhase;
			electricalLoad.PowerKW = 0.0;
			electricalLoad.IsSpare = true;
			electricalLoad.DrawOutgoingWire = false;
			electricalLoad.CircuitNumber = "SPARE";
			electricalLoad.BreakerSpec = "MCCB 3P-25kA 40A";
			electricalLoad.CableSpec = "-";
			electricalLoad.GroundWireSpec = "";
			electricalLoad.ConductorSize = "";
			electricalLoad.AssignedPhase = "3P";
			electricalLoad.UpdateCableSpec();
			_panel.Loads.Add(electricalLoad);
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}
			SafeRefreshDataGrid();
			ExecuteCalculation();
		}

		private void BtnAddSpare1P_Click(object sender, RoutedEventArgs e)
		{
			ElectricalLoad electricalLoad = new ElectricalLoad();
			electricalLoad.LoadName = string.Format("LỘ DỰ PHÒNG 1P-{0:D2}", _panel.Loads.Count + 1);
			electricalLoad.Phase = PhaseType.SinglePhase;
			electricalLoad.PowerKW = 0.0;
			electricalLoad.IsSpare = true;
			electricalLoad.DrawOutgoingWire = false;
			electricalLoad.CircuitNumber = "SPARE";
			electricalLoad.BreakerSpec = "MCB 1P-6kA 16A";
			electricalLoad.CableSpec = "-";
			electricalLoad.GroundWireSpec = "";
			electricalLoad.ConductorSize = "";
			electricalLoad.AssignedPhase = "R";
			electricalLoad.UpdateCableSpec();
			_panel.Loads.Add(electricalLoad);
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}
			SafeRefreshDataGrid();
			ExecuteCalculation();
		}

		private void DgLoads_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
		{
			if (_isCalculating) return;
			ElectricalLoad electricalLoad = e.Row.Item as ElectricalLoad;
			if (electricalLoad != null && e.Column != null)
			{
				string text = e.Column.Header as string;
				if (text != null && text.Contains("Thông Số CB"))
				{
					electricalLoad.IsBreakerUserDefined = true;
				}
				else if (text != null && (text.Contains("Tổng P") || text.Contains("P (kW)") || text.Contains("Pha") || text.Contains("Loại CB") || text.Contains("Loại Tải")))
				{
					electricalLoad.IsBreakerUserDefined = false;
				}
			}
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				ExecuteCalculation();
			}, DispatcherPriority.Background);
		}

		private Point _dragStartPoint;
		private bool _isDragging = false;
		private DependencyObject _dragSourceElement;

		private void DgLoads_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			_dragStartPoint = e.GetPosition(null);
			_dragSourceElement = e.OriginalSource as DependencyObject;
		}

		private void DgLoads_MouseMove(object sender, MouseEventArgs e)
		{
			try
			{
				if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
				{
					if (Keyboard.Modifiers == ModifierKeys.Shift || Keyboard.Modifiers == ModifierKeys.Control) return;

					// Xác định ô được click một cách an toàn (tránh lỗi Run không phải Visual)
					DependencyObject dep = _dragSourceElement;
					DataGridCell cell = null;
					while (dep != null && dep != dgLoads)
					{
						if (dep is DataGridCell) { cell = (DataGridCell)dep; break; }
						if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
						{
							dep = VisualTreeHelper.GetParent(dep);
						}
						else if (dep is FrameworkContentElement)
						{
							dep = ((FrameworkContentElement)dep).Parent;
						}
						else
						{
							break;
						}
					}

					// Chỉ kích hoạt kéo thả khi click vào cột STT (DisplayIndex == 0), tránh xung đột khi chọn/sửa ô khác
					if (cell == null || cell.Column == null || cell.Column.DisplayIndex != 0) return;

					Point currentPosition = e.GetPosition(null);
					Vector diff = _dragStartPoint - currentPosition;
					if (Math.Abs(diff.X) > 10 || Math.Abs(diff.Y) > 10)
					{
						if (dgLoads.SelectedItems != null && dgLoads.SelectedItems.Count > 0)
						{
							_isDragging = true;
							var selected = dgLoads.SelectedItems.Cast<ElectricalLoad>().ToList();
							DragDrop.DoDragDrop(dgLoads, selected, DragDropEffects.Move);
							_isDragging = false;
						}
					}
				}
			}
			catch
			{
				_isDragging = false;
			}
		}

		private void DgLoads_Drop(object sender, DragEventArgs e)
		{
			try
			{
				if (e.Data.GetDataPresent(typeof(List<ElectricalLoad>)))
				{
					var droppedLoads = e.Data.GetData(typeof(List<ElectricalLoad>)) as List<ElectricalLoad>;
					if (droppedLoads != null && droppedLoads.Count > 0)
					{
						DependencyObject dep = e.OriginalSource as DependencyObject;
						DataGridRow row = null;
						while (dep != null && dep != dgLoads)
						{
							if (dep is DataGridRow) { row = (DataGridRow)dep; break; }
							if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
							{
								dep = VisualTreeHelper.GetParent(dep);
							}
							else if (dep is FrameworkContentElement)
							{
								dep = ((FrameworkContentElement)dep).Parent;
							}
							else
							{
								break;
							}
						}

						int targetIndex = _panel.Loads.Count;
						if (row != null)
						{
							ElectricalLoad targetLoad = row.Item as ElectricalLoad;
							if (targetLoad != null)
							{
								targetIndex = _panel.Loads.IndexOf(targetLoad);
							}
						}

						foreach (var ld in droppedLoads)
						{
							int oldIdx = _panel.Loads.IndexOf(ld);
							if (oldIdx >= 0)
							{
								_panel.Loads.RemoveAt(oldIdx);
								if (targetIndex > oldIdx) targetIndex--;
								if (targetIndex < 0) targetIndex = 0;
								if (targetIndex > _panel.Loads.Count) targetIndex = _panel.Loads.Count;
								_panel.Loads.Insert(targetIndex, ld);
								targetIndex++;
							}
						}

						for (int i = 0; i < _panel.Loads.Count; i++)
						{
							_panel.Loads[i].Stt = i + 1;
						}
						SafeRefreshDataGrid();
						DrawCanvasPreview();
					}
				}
			}
			catch { }
		}

		private void DgLoads_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Delete)
			{
				BtnDeleteRow_Click(sender, e);
				e.Handled = true;
			}
			else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
			{
				BtnDuplicateRow_Click(sender, e);
				e.Handled = true;
			}
			else if (e.Key == Key.Up && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
			{
				BtnMoveUp_Click(sender, e);
				e.Handled = true;
			}
			else if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
			{
				BtnMoveDown_Click(sender, e);
				e.Handled = true;
			}
		}

		private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
		{
			if (dgLoads.SelectedItems == null || dgLoads.SelectedItems.Count == 0) return;
			var selected = dgLoads.SelectedItems.Cast<ElectricalLoad>().OrderBy(x => _panel.Loads.IndexOf(x)).ToList();
			if (selected.Count == 0) return;

			int firstIdx = _panel.Loads.IndexOf(selected[0]);
			if (firstIdx <= 0) return;

			foreach (var item in selected)
			{
				int idx = _panel.Loads.IndexOf(item);
				if (idx > 0)
				{
					_panel.Loads.RemoveAt(idx);
					_panel.Loads.Insert(idx - 1, item);
				}
			}

			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}
			SafeRefreshDataGrid();
			DrawCanvasPreview();
		}

		private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
		{
			if (dgLoads.SelectedItems == null || dgLoads.SelectedItems.Count == 0) return;
			var selected = dgLoads.SelectedItems.Cast<ElectricalLoad>().OrderByDescending(x => _panel.Loads.IndexOf(x)).ToList();
			if (selected.Count == 0) return;

			int lastIdx = _panel.Loads.IndexOf(selected[0]);
			if (lastIdx >= _panel.Loads.Count - 1) return;

			bool anyRegular = selected.Any(x => !x.IsSpare);
			if (anyRegular && lastIdx + 1 < _panel.Loads.Count && _panel.Loads[lastIdx + 1].IsSpare) return;

			foreach (var item in selected)
			{
				int idx = _panel.Loads.IndexOf(item);
				if (idx < _panel.Loads.Count - 1)
				{
					_panel.Loads.RemoveAt(idx);
					_panel.Loads.Insert(idx + 1, item);
				}
			}

			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}
			SafeRefreshDataGrid();
			DrawCanvasPreview();
		}

		private void BtnDuplicateRow_Click(object sender, RoutedEventArgs e)
		{
			if (dgLoads.SelectedItems == null || dgLoads.SelectedItems.Count == 0)
			{
				MessageBox.Show("Vui lòng chọn phụ tải cần nhân bản.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}

			var selected = dgLoads.SelectedItems.Cast<ElectricalLoad>().ToList();
			int insertIdx = _panel.Loads.Count;
			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				if (_panel.Loads[i].IsSpare || (!string.IsNullOrEmpty(_panel.Loads[i].LoadName) && _panel.Loads[i].LoadName.ToUpper().Contains("SPARE")))
				{
					insertIdx = i;
					break;
				}
			}

			foreach (var ld in selected)
			{
				ElectricalLoad clone = ld.Clone();
				clone.LoadName = ld.LoadName + " (Copy)";
				_panel.Loads.Insert(insertIdx++, clone);
			}

			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}

			SafeRefreshDataGrid();
			ExecuteCalculation();
		}

		private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
		{
			if (dgLoads.SelectedItems == null || dgLoads.SelectedItems.Count == 0)
			{
				MessageBox.Show("Vui lòng chọn phụ tải cần xóa (Bảng phải có ít nhất 1 phụ tải).", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}

			var toRemove = dgLoads.SelectedItems.Cast<ElectricalLoad>().ToList();
			if (_panel.Loads.Count - toRemove.Count < 1)
			{
				MessageBox.Show("Bảng phải giữ lại ít nhất 1 phụ tải!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}

			foreach (var ld in toRemove)
			{
				_panel.Loads.Remove(ld);
			}

			for (int i = 0; i < _panel.Loads.Count; i++)
			{
				_panel.Loads[i].Stt = i + 1;
			}

			SafeRefreshDataGrid();
			ExecuteCalculation();
		}

		private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
		{
			if (_panel == null)
			{
				return;
			}
			SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.Filter = "Bảng tính Excel 97-2003 (*.xls)|*.xls";
			saveFileDialog.FileName = string.Format("{0}_BangTinhTai.xls", _panel.PanelName);
			saveFileDialog.Title = "Xuất bảng tính phụ tải tủ điện ra file Excel (.xls)";
			if (saveFileDialog.ShowDialog() != true)
			{
				return;
			}
			try
			{
				ExcelExportService excelExportService = new ExcelExportService();
				if (excelExportService.ExportPanelToExcel(_panel, saveFileDialog.FileName))
				{
					txtDialogStatus.Text = string.Format("Đã xuất file Excel '{0}' thành công!", System.IO.Path.GetFileName(saveFileDialog.FileName));
					MessageBoxResult messageBoxResult = MessageBox.Show(string.Format("Đã xuất Bảng tính toán phụ tải tủ điện '{0}' thành công!\nFile lưu tại: {1}\n\nBạn có muốn mở file Excel ngay bây giờ?", _panel.PanelName, saveFileDialog.FileName), "Thành công", MessageBoxButton.YesNo, MessageBoxImage.Asterisk);
					if (messageBoxResult == MessageBoxResult.Yes)
					{
						Process.Start(saveFileDialog.FileName);
					}
				}
				else
				{
					MessageBox.Show("Có lỗi xảy ra trong quá trình xuất file Excel!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format("Lỗi xuất Excel: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void SyncParamsToUi()
		{
			if (txtColWidth != null)
			{
				txtColWidth.Text = _params.ColWidth.ToString("0");
			}
			if (txtRowCable != null)
			{
				txtRowCable.Text = _params.RowCableHeight.ToString("0");
			}
			if (txtRowName != null)
			{
				txtRowName.Text = _params.RowNameHeight.ToString("0");
			}
			if (txtRowPower != null)
			{
				txtRowPower.Text = _params.RowPowerHeight.ToString("0");
			}
			if (chkSplitCable != null)
			{
				chkSplitCable.IsChecked = _params.SplitCableTwoLines;
			}
			if (chkIncludeFuse != null)
			{
				chkIncludeFuse.IsChecked = _params.IncludeFuse;
			}
			if (chkIncludeLamps != null)
			{
				chkIncludeLamps.IsChecked = _params.IncludeLamps;
			}
			if (chkIncludeRelay != null)
			{
				chkIncludeRelay.IsChecked = _params.IncludeRelay;
			}
			if (chkIncludeMct != null)
			{
				chkIncludeMct.IsChecked = _params.IncludeMct;
			}
			if (chkIncludeSpd != null)
			{
				chkIncludeSpd.IsChecked = _params.IncludeSpd;
			}
			if (chkIncludeSpare != null)
			{
				chkIncludeSpare.IsChecked = _params.IncludeSpare;
			}
			if (cboSpare3P != null)
			{
				cboSpare3P.SelectedIndex = Math.Max(0, Math.Min(5, _params.SpareCount3P));
			}
			if (cboSpare1P != null)
			{
				cboSpare1P.SelectedIndex = Math.Max(0, Math.Min(5, _params.SpareCount1P));
			}
			if (chkDrawSpareWire != null)
			{
				chkDrawSpareWire.IsChecked = _params.DrawSpareOutgoingWire;
			}
			if (pnlSpareOptions != null)
			{
				pnlSpareOptions.Visibility = ((!_params.IncludeSpare) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible);
			}
			if (chkIncludeLegend != null)
			{
				chkIncludeLegend.IsChecked = _params.IncludeLegend;
			}
			if (chkIncludeLegendTab != null)
			{
				chkIncludeLegendTab.IsChecked = _params.IncludeLegend;
			}
			if (chkIncludeTitleBlock != null)
			{
				chkIncludeTitleBlock.IsChecked = _params.IncludeTitleBlock;
			}
			if (chkDrawFrontView != null)
			{
				chkDrawFrontView.IsChecked = _params.DrawFrontView;
			}
			if (txtProjectName != null)
			{
				txtProjectName.Text = _params.ProjectName ?? "CÔNG TRÌNH CƠ ĐIỆN TÒA NHÀ & NHÀ XƯỞNG";
			}
			if (txtDrawingTitle != null)
			{
				txtDrawingTitle.Text = _params.DrawingTitle ?? ("SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN " + ((_panel != null) ? _panel.PanelName : ""));
			}
			if (txtDrawingNo != null)
			{
				txtDrawingNo.Text = _params.DrawingNumber ?? "EL-01";
			}
			if (txtDrawingScale != null)
			{
				txtDrawingScale.Text = _params.DrawingScale ?? "1:25";
			}
			if (txtRevision != null)
			{
				txtRevision.Text = _params.Revision ?? "00";
			}
			if (txtDesignerName != null)
			{
				txtDesignerName.Text = _params.DesignerName ?? "NTT TOOLS";
			}
			if (txtDrawnBy != null)
			{
				txtDrawnBy.Text = _params.DrawnBy ?? "NGUYỄN TẤN TÀI";
			}
			if (txtCheckedBy != null)
			{
				txtCheckedBy.Text = _params.CheckedBy ?? "CAO VĂN SIÊU";
			}
			if (txtApprovedBy != null)
			{
				txtApprovedBy.Text = _params.ApprovedBy ?? "DƯƠNG CAO SƠN";
			}
			string text = ElectricalDrawingService.FindTitleBlockDwgPath();
			if (txtTitleBlockPath != null && !string.IsNullOrEmpty(text))
			{
				txtTitleBlockPath.Text = text;
				if (lblTitleBlockStatus != null)
				{
					lblTitleBlockStatus.Text = "✓ Đã tìm thấy file mẫu khung tên A3: " + System.IO.Path.GetFileName(text);
					lblTitleBlockStatus.Foreground = Brushes.ForestGreen;
				}
			}
			int selectedIndex = ((_params.LayoutScheme == LayoutScheme.SouthRegion) ? 1 : ((_params.LayoutScheme == LayoutScheme.NorthRegion) ? 2 : ((_params.LayoutScheme == LayoutScheme.SldV2Mcc) ? 3 : 0)));
			int selectedIndex2 = ((_params.TreeOrientation == TreeOrientation.HorizontalLeftRight) ? 1 : 0);
			_isSyncingLayoutControls = true;
			try
			{
				if (cboLayoutScheme != null)
				{
					cboLayoutScheme.SelectedIndex = selectedIndex;
				}
				if (cboSideLayoutScheme != null)
				{
					cboSideLayoutScheme.SelectedIndex = selectedIndex;
				}
				if (cboLightboxLayoutScheme != null)
				{
					cboLightboxLayoutScheme.SelectedIndex = selectedIndex;
				}
				if (cboTreeOrientation != null)
				{
					cboTreeOrientation.SelectedIndex = selectedIndex2;
				}
				if (cboSideTreeOrientation != null)
				{
					cboSideTreeOrientation.SelectedIndex = selectedIndex2;
				}
				if (cboLightboxTreeOrientation != null)
				{
					cboLightboxTreeOrientation.SelectedIndex = selectedIndex2;
				}
			}
			finally
			{
				_isSyncingLayoutControls = false;
			}
		}

		private void ChkAccessories_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _params != null)
			{
				if (chkIncludeFuse != null)
				{
					_params.IncludeFuse = chkIncludeFuse.IsChecked == true;
				}
				if (chkIncludeLamps != null)
				{
					_params.IncludeLamps = chkIncludeLamps.IsChecked == true;
				}
				if (chkIncludeRelay != null)
				{
					_params.IncludeRelay = chkIncludeRelay.IsChecked == true;
				}
				if (chkIncludeMct != null)
				{
					_params.IncludeMct = chkIncludeMct.IsChecked == true;
				}
				if (chkIncludeSpd != null)
				{
					_params.IncludeSpd = chkIncludeSpd.IsChecked == true;
				}
				if (chkIncludeSpare != null)
				{
					_params.IncludeSpare = chkIncludeSpare.IsChecked == true;
				}
				DrawCanvasPreview();
			}
		}

		private void SetLayoutSchemeSelection(int index)
		{
			if (_isSyncingLayoutControls || _isInitializing || _params == null)
			{
				return;
			}
			_isSyncingLayoutControls = true;
			try
			{
				if (cboLayoutScheme != null && cboLayoutScheme.SelectedIndex != index)
				{
					cboLayoutScheme.SelectedIndex = index;
				}
				if (cboSideLayoutScheme != null && cboSideLayoutScheme.SelectedIndex != index)
				{
					cboSideLayoutScheme.SelectedIndex = index;
				}
				if (cboLightboxLayoutScheme != null && cboLightboxLayoutScheme.SelectedIndex != index)
				{
					cboLightboxLayoutScheme.SelectedIndex = index;
				}
				switch (index)
				{
				case 0:
					_params.LayoutScheme = LayoutScheme.LayoutBasic;
					_params.DiagramType = SldDiagramType.StandardSchedule;
					if (pnlTreeOrientation != null)
					{
						pnlTreeOrientation.Visibility = System.Windows.Visibility.Collapsed;
					}
					if (txtTemplateHint != null)
					{
						txtTemplateHint.Text = "(Mặc định Layout gốc đã thiết kế)";
					}
					break;
				case 1:
					_params.LayoutScheme = LayoutScheme.SouthRegion;
					_params.DiagramType = SldDiagramType.TreeSinglePanel;
					_params.SymbolRegion = SymbolRegion.South_Iec;
					if (pnlTreeOrientation != null)
					{
						pnlTreeOrientation.Visibility = System.Windows.Visibility.Visible;
					}
					_params.TreeOrientation = ((cboSideTreeOrientation != null && cboSideTreeOrientation.SelectedIndex == 1) ? TreeOrientation.HorizontalLeftRight : TreeOrientation.VerticalTopDown);
					if (txtTemplateHint != null)
					{
						txtTemplateHint.Text = (string.IsNullOrEmpty(_params.ReferenceTemplateDwgPath) ? "(Chuẩn IEC / EVN SPC - Yêu cầu bản vẽ đối chiếu)" : ("(Đối chiếu: " + System.IO.Path.GetFileName(_params.ReferenceTemplateDwgPath) + ")"));
					}
					break;
				case 2:
					_params.LayoutScheme = LayoutScheme.NorthRegion;
					_params.DiagramType = SldDiagramType.TreeSinglePanel;
					_params.SymbolRegion = SymbolRegion.North_Tcvn;
					if (pnlTreeOrientation != null)
					{
						pnlTreeOrientation.Visibility = System.Windows.Visibility.Visible;
					}
					_params.TreeOrientation = ((cboSideTreeOrientation != null && cboSideTreeOrientation.SelectedIndex == 1) ? TreeOrientation.HorizontalLeftRight : TreeOrientation.VerticalTopDown);
					if (txtTemplateHint != null)
					{
						txtTemplateHint.Text = (string.IsNullOrEmpty(_params.ReferenceTemplateDwgPath) ? "(Chuẩn TCVN / EVN NPC - Yêu cầu bản vẽ đối chiếu)" : ("(Đối chiếu: " + System.IO.Path.GetFileName(_params.ReferenceTemplateDwgPath) + ")"));
					}
					break;
				case 3:
					_params.LayoutScheme = LayoutScheme.SldV2Mcc;
					_params.DiagramType = SldDiagramType.TreeSinglePanel;
					_params.SymbolRegion = SymbolRegion.South_Iec;
					if (pnlTreeOrientation != null)
					{
						pnlTreeOrientation.Visibility = System.Windows.Visibility.Collapsed;
					}
					if (txtTemplateHint != null)
					{
						txtTemplateHint.Text = "⚡ SDNL V2: Tủ Động Lực MCC / PCCC (Cấp nguồn từ dưới lên)";
					}
					break;
				}
				if (_panel != null)
				{
					_panel.DrawingParams = _params;
				}
				DrawCanvasPreview();
			}
			finally
			{
				_isSyncingLayoutControls = false;
			}
		}

		private void SetTreeOrientationSelection(int index)
		{
			if (_isSyncingLayoutControls || _isInitializing || _params == null)
			{
				return;
			}
			_isSyncingLayoutControls = true;
			try
			{
				if (cboTreeOrientation != null && cboTreeOrientation.SelectedIndex != index)
				{
					cboTreeOrientation.SelectedIndex = index;
				}
				if (cboSideTreeOrientation != null && cboSideTreeOrientation.SelectedIndex != index)
				{
					cboSideTreeOrientation.SelectedIndex = index;
				}
				if (cboLightboxTreeOrientation != null && cboLightboxTreeOrientation.SelectedIndex != index)
				{
					cboLightboxTreeOrientation.SelectedIndex = index;
				}
				_params.TreeOrientation = ((index == 1) ? TreeOrientation.HorizontalLeftRight : TreeOrientation.VerticalTopDown);
				if (_panel != null)
				{
					_panel.DrawingParams = _params;
				}
				if (index == 1 && _params.LayoutScheme == LayoutScheme.LayoutBasic)
				{
					_params.LayoutScheme = LayoutScheme.SouthRegion;
					_params.DiagramType = SldDiagramType.TreeSinglePanel;
					_params.SymbolRegion = SymbolRegion.South_Iec;
					if (cboLayoutScheme != null)
					{
						cboLayoutScheme.SelectedIndex = 1;
					}
					if (cboSideLayoutScheme != null)
					{
						cboSideLayoutScheme.SelectedIndex = 1;
					}
					if (cboLightboxLayoutScheme != null)
					{
						cboLightboxLayoutScheme.SelectedIndex = 1;
					}
				}
				DrawCanvasPreview();
			}
			finally
			{
				_isSyncingLayoutControls = false;
			}
		}

		private void CboLayoutScheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (cboLayoutScheme != null && cboLayoutScheme.SelectedIndex >= 0)
			{
				SetLayoutSchemeSelection(cboLayoutScheme.SelectedIndex);
			}
		}

		private void CboTreeOrientation_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (cboTreeOrientation != null && cboTreeOrientation.SelectedIndex >= 0)
			{
				SetTreeOrientationSelection(cboTreeOrientation.SelectedIndex);
			}
		}

		private void CboSideLayoutScheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (cboSideLayoutScheme != null && cboSideLayoutScheme.SelectedIndex >= 0)
			{
				SetLayoutSchemeSelection(cboSideLayoutScheme.SelectedIndex);
			}
		}

		private void CboSideTreeOrientation_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (cboSideTreeOrientation != null && cboSideTreeOrientation.SelectedIndex >= 0)
			{
				SetTreeOrientationSelection(cboSideTreeOrientation.SelectedIndex);
			}
		}

		private void CboLightboxLayoutScheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (cboLightboxLayoutScheme != null && cboLightboxLayoutScheme.SelectedIndex >= 0)
			{
				SetLayoutSchemeSelection(cboLightboxLayoutScheme.SelectedIndex);
			}
		}

		private void CboLightboxTreeOrientation_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (cboLightboxTreeOrientation != null && cboLightboxTreeOrientation.SelectedIndex >= 0)
			{
				SetTreeOrientationSelection(cboLightboxTreeOrientation.SelectedIndex);
			}
		}

		private void BtnBrowseTemplate_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				OpenFileDialog openFileDialog = new OpenFileDialog();
				openFileDialog.Filter = "Bản vẽ AutoCAD (*.dwg)|*.dwg|Tất cả tệp (*.*)|*.*";
				openFileDialog.Title = "Chọn Bản Vẽ Mẫu Đối Chiếu Cho Sơ Đồ Nguyên Lý";
				if (openFileDialog.ShowDialog() == true)
				{
					if (_params != null)
					{
						_params.ReferenceTemplateDwgPath = openFileDialog.FileName;
					}
					if (txtTemplateHint != null)
					{
						txtTemplateHint.Text = "(Đã nạp file đối chiếu: " + System.IO.Path.GetFileName(openFileDialog.FileName) + ")";
					}
					MessageBox.Show("Đã nạp bản vẽ mẫu đối chiếu:\n" + openFileDialog.FileName + "\n\nThuật toán sẽ tự động áp dụng form chuẩn từ bản vẽ đối chiếu này!", "Đã Chọn File Đối Chiếu", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					DrawCanvasPreview();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Lỗi chọn file mẫu đối chiếu: " + ex.Message, "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}

		private void ChkSpare_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _params != null)
			{
				if (chkIncludeSpare != null)
				{
					_params.IncludeSpare = chkIncludeSpare.IsChecked == true;
				}
				if (pnlSpareOptions != null)
				{
					pnlSpareOptions.Visibility = ((!_params.IncludeSpare) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible);
				}
				DrawCanvasPreview();
			}
		}

		private void CboSpareCount_Changed(object sender, SelectionChangedEventArgs e)
		{
			if (!_isInitializing && _params != null)
			{
				if (cboSpare3P != null && cboSpare3P.SelectedIndex >= 0)
				{
					_params.SpareCount3P = cboSpare3P.SelectedIndex;
				}
				if (cboSpare1P != null && cboSpare1P.SelectedIndex >= 0)
				{
					_params.SpareCount1P = cboSpare1P.SelectedIndex;
				}
				_params.SpareCount = _params.SpareCount3P + _params.SpareCount1P;
				DrawCanvasPreview();
			}
		}

		private void ChkSpareWire_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _params != null)
			{
				if (chkDrawSpareWire != null)
				{
					_params.DrawSpareOutgoingWire = chkDrawSpareWire.IsChecked == true;
				}
				DrawCanvasPreview();
			}
		}

		private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
		{
			if (_params != null && _panel != null)
			{
				_isInitializing = true;
				_params.AutoFitForPanel(_panel);
				SyncParamsToUi();
				_isInitializing = false;
				DrawCanvasPreview();
				txtDialogStatus.Text = "Đã tự động căn chỉnh và co dãn kích thước tối ưu chống đè chữ/block!";
			}
		}

		private void UpdateParamsFromUi()
		{
			if (!_isInitializing)
			{
				double result;
				if (double.TryParse(txtColWidth.Text, out result) && result >= 15.0 && result <= 60.0)
				{
					_params.ColWidth = result;
				}
				if (double.TryParse(txtRowCable.Text, out result) && result >= 40.0 && result <= 180.0)
				{
					_params.RowCableHeight = result;
				}
				if (double.TryParse(txtRowName.Text, out result) && result >= 30.0 && result <= 150.0)
				{
					_params.RowNameHeight = result;
				}
				if (double.TryParse(txtRowPower.Text, out result) && result >= 20.0 && result <= 80.0)
				{
					_params.RowPowerHeight = result;
				}
				_params.SplitCableTwoLines = chkSplitCable.IsChecked == true;
				DrawCanvasPreview();
			}
		}

		private void ParamChanged_TextChanged(object sender, TextChangedEventArgs e)
		{
			UpdateParamsFromUi();
		}

		private void ChkSplitCable_Changed(object sender, RoutedEventArgs e)
		{
			UpdateParamsFromUi();
		}

		private void BtnResetParams_Click(object sender, RoutedEventArgs e)
		{
			_isInitializing = true;
			_params.ColWidth = 26.0;
			_params.RowCableHeight = 90.0;
			_params.RowNameHeight = 65.0;
			_params.RowPowerHeight = 36.0;
			_params.SplitCableTwoLines = true;
			SyncParamsToUi();
			_isInitializing = false;
			DrawCanvasPreview();
		}

		private void CboIncomer_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (!_isInitializing && _panel != null && cboIncomer != null)
			{
				_panel.Incomer = (IncomerType)cboIncomer.SelectedIndex;
				_params.AutoFitForPanel(_panel);
				SyncParamsToUi();
				DrawCanvasPreview();
			}
		}

		private void ChkCapacitor_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _panel != null && chkHasCapacitor != null)
			{
				_panel.HasCapacitorBank = chkHasCapacitor.IsChecked == true;
				ElectricalCalculator.CalculateCapacitorBank(_panel);
				DrawCanvasPreview();
			}
		}

		private void ChkTitleBlock_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _params != null && chkIncludeTitleBlock != null)
			{
				_params.IncludeTitleBlock = chkIncludeTitleBlock.IsChecked == true;
				DrawCanvasPreview();
			}
		}

		private void ChkFrontView_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _params != null && chkDrawFrontView != null)
			{
				_params.DrawFrontView = chkDrawFrontView.IsChecked == true;
				DrawCanvasPreview();
			}
		}

		private void ChkLegend_Changed(object sender, RoutedEventArgs e)
		{
			if (!_isInitializing && _params != null)
			{
				bool flag = false;
				if (sender == chkIncludeLegend && chkIncludeLegend != null)
				{
					flag = chkIncludeLegend.IsChecked == true;
				}
				else if (sender == chkIncludeLegendTab && chkIncludeLegendTab != null)
				{
					flag = chkIncludeLegendTab.IsChecked == true;
				}
				else if (chkIncludeLegend != null)
				{
					flag = chkIncludeLegend.IsChecked == true;
				}
				_params.IncludeLegend = flag;
				if (chkIncludeLegend != null && chkIncludeLegend.IsChecked != flag)
				{
					chkIncludeLegend.IsChecked = flag;
				}
				if (chkIncludeLegendTab != null && chkIncludeLegendTab.IsChecked != flag)
				{
					chkIncludeLegendTab.IsChecked = flag;
				}
				DrawCanvasPreview();
			}
		}

		private void TitleBlock_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!_isInitializing && _params != null)
			{
				if (txtProjectName != null)
				{
					_params.ProjectName = txtProjectName.Text;
				}
				if (txtDrawingTitle != null)
				{
					_params.DrawingTitle = txtDrawingTitle.Text;
				}
				if (txtDrawingNo != null)
				{
					_params.DrawingNumber = txtDrawingNo.Text;
				}
				if (txtDrawingScale != null)
				{
					_params.DrawingScale = txtDrawingScale.Text;
				}
				if (txtRevision != null)
				{
					_params.Revision = txtRevision.Text;
				}
				if (txtDesignerName != null)
				{
					_params.DesignerName = txtDesignerName.Text;
				}
				if (txtDrawnBy != null)
				{
					_params.DrawnBy = txtDrawnBy.Text;
				}
				if (txtCheckedBy != null)
				{
					_params.CheckedBy = txtCheckedBy.Text;
				}
				if (txtApprovedBy != null)
				{
					_params.ApprovedBy = txtApprovedBy.Text;
				}
				DrawCanvasPreview();
			}
		}

		private void BtnBrowseTitleBlock_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				OpenFileDialog openFileDialog = new OpenFileDialog();
				openFileDialog.Filter = "AutoCAD Drawing (*.dwg)|*.dwg|All files (*.*)|*.*";
				openFileDialog.Title = "Chọn file mẫu khung tên A3";
				if (openFileDialog.ShowDialog() == true)
				{
					txtTitleBlockPath.Text = openFileDialog.FileName;
					lblTitleBlockStatus.Text = "✓ Đã chọn file khung tên: " + System.IO.Path.GetFileName(openFileDialog.FileName);
					lblTitleBlockStatus.Foreground = Brushes.ForestGreen;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Lỗi chọn file: " + ex.Message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}

		private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				OpenFileDialog openFileDialog = new OpenFileDialog();
				openFileDialog.Filter = "Tệp Excel (*.xlsx;*.xls;*.csv;*.txt)|*.xlsx;*.xls;*.csv;*.txt|Tệp Excel Hiện Đại (*.xlsx)|*.xlsx|Tệp Excel 2003 (*.xls)|*.xls|Tệp CSV (*.csv)|*.csv|Tất cả tệp (*.*)|*.*";
				openFileDialog.Title = "Chọn file Excel (.xlsx / .xls) hoặc CSV chứa bảng tính phụ tải tủ điện";
				if (openFileDialog.ShowDialog() != true)
				{
					return;
				}
				ExcelImportService excelImportService = new ExcelImportService();
				List<ElectricalPanel> list = excelImportService.ImportPanelsFromExcel(openFileDialog.FileName);
				if (list != null && list.Count > 0)
				{
					if (list.Count == 1)
					{
						ElectricalPanel electricalPanel = list[0];
						_panel.PanelName = electricalPanel.PanelName;
						txtPanelName.Text = electricalPanel.PanelName;
						if (electricalPanel.SimultaneousFactor > 0.0)
						{
							txtKdt.Text = electricalPanel.SimultaneousFactor.ToString("0.##");
						}
						if (electricalPanel.PowerFactor > 0.0)
						{
							txtCosPhi.Text = electricalPanel.PowerFactor.ToString("0.##");
						}
						if (electricalPanel.Incomer != 0)
						{
							_panel.Incomer = electricalPanel.Incomer;
							cboIncomer.SelectedIndex = (int)electricalPanel.Incomer;
						}
						if (electricalPanel.HasCapacitorBank)
						{
							chkHasCapacitor.IsChecked = true;
						}
						_panel.Loads.Clear();
						foreach (ElectricalLoad load in electricalPanel.Loads)
						{
							_panel.Loads.Add(load);
						}
						_calculator.AutoBalancePhases(_panel);
						SafeRefreshDataGrid();
						ExecuteCalculation();
						txtDialogStatus.Text = string.Format("Đã nhập thành công tủ '{0}' ({1} phụ tải) từ file Excel!", _panel.PanelName, _panel.Loads.Count);
						MessageBox.Show(string.Format("Đã nhập thành công tủ '{0}' với {1} phụ tải từ Excel!\nĐã tự động tính toán dòng tải, cân pha và chọn thiết bị bảo vệ / cỡ cáp theo tiêu chuẩn.", _panel.PanelName, _panel.Loads.Count), "Nhập Excel Thành Công", MessageBoxButton.OK, MessageBoxImage.Asterisk);
						return;
					}
					int num = 0;
					List<string> list2 = new List<string>();
					foreach (ElectricalPanel item in list)
					{
						_calculator.AutoBalancePhases(item);
						_calculator.CalculatePanel(item);
						num += item.Loads.Count;
						list2.Add(item.PanelName);
						ElectricalPanel electricalPanel2 = null;
						foreach (ElectricalPanel panels in _panelsList)
						{
							if (panels.PanelName != null && panels.PanelName.Equals(item.PanelName, StringComparison.OrdinalIgnoreCase))
							{
								electricalPanel2 = panels;
								break;
							}
						}
						if (electricalPanel2 != null)
						{
							int index = _panelsList.IndexOf(electricalPanel2);
							_panelsList[index] = item;
						}
						else
						{
							_panelsList.Add(item);
						}
					}
					cboPanels.SelectedItem = list[0];
					SwitchToPanel(list[0]);
					txtDialogStatus.Text = string.Format("Đã nhập thành công {0} tủ điện ({1} phụ tải) từ file Excel!", list.Count, num);
					string messageBoxText = string.Format("Đã nhập thành công {0} tủ điện ({1} phụ tải) từ file Excel:\n{2}\n\nĐã tự động tính toán toàn bộ dòng tải, cân pha và chọn thiết bị bảo vệ / cỡ cáp theo tiêu chuẩn!", list.Count, num, string.Join(", ", list2.GetRange(0, Math.Min(6, list2.Count)).ToArray()) + ((list2.Count > 6) ? string.Format(" và {0} tủ khác...", list2.Count - 6) : ""));
					MessageBox.Show(messageBoxText, "Nhập Excel Đa Tủ Thành Công", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				}
				else
				{
					MessageBox.Show("Không tìm thấy dòng dữ liệu phụ tải hợp lệ trong file!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format("Lỗi khi nhập Excel: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void BtnExportSampleTemplate_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				SaveFileDialog saveFileDialog = new SaveFileDialog();
				saveFileDialog.Filter = "Tệp Excel Hiện Đại (*.xlsx)|*.xlsx|Tệp Excel 2003 (*.xls)|*.xls";
				saveFileDialog.FileName = "Template_DanhSachPhuTai.xlsx";
				saveFileDialog.Title = "Lưu file Excel mẫu phụ tải (Đầy đủ ATS, DOL, Y-Δ, VFD, SoftStarter, RCBO)";
				if (saveFileDialog.ShowDialog() != true)
				{
					return;
				}
				ExcelImportService excelImportService = new ExcelImportService();
				if (excelImportService.CreateSampleTemplate(saveFileDialog.FileName))
				{
					txtDialogStatus.Text = string.Format("Đã tạo file mẫu '{0}' thành công!", System.IO.Path.GetFileName(saveFileDialog.FileName));
					MessageBoxResult messageBoxResult = MessageBox.Show(string.Format("Đã tạo file mẫu thành công tại:\n{0}\n\nBạn có muốn mở ngay bây giờ?", saveFileDialog.FileName), "Tạo Mẫu Excel Thành Công", MessageBoxButton.YesNo, MessageBoxImage.Asterisk);
					if (messageBoxResult == MessageBoxResult.Yes)
					{
						Process.Start(saveFileDialog.FileName);
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format("Lỗi khi tạo template mẫu: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void BtnDrawRiser_Click(object sender, RoutedEventArgs e)
		{
			Document mdiActiveDocument = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
			if (mdiActiveDocument == null)
			{
				MessageBox.Show("Không tìm thấy bản vẽ AutoCAD nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			try
			{
				Editor editor = mdiActiveDocument.Editor;
				Point3d origin = Point3d.Origin;
				bool flag = false;
				using (editor.StartUserInteraction(this))
				{
					PromptPointOptions options = new PromptPointOptions("\n[NTT TOOLS] Chọn điểm đặt Sơ đồ Cây Phân Phối (Riser Diagram): ");
					PromptPointResult point = editor.GetPoint(options);
					if (point.Status == PromptStatus.OK)
					{
						origin = point.Value;
						flag = true;
					}
				}
				if (flag && _drawingService.DrawRiserDiagram(mdiActiveDocument, PanelStorageService.GetAllPanels(), origin))
				{
					txtDialogStatus.Text = "Đã vẽ Sơ đồ Cây Phân Phối (Riser Diagram) thành công!";
					MessageBox.Show("Đã vẽ Sơ đồ Cây Phân Phối toàn công trình (Riser Diagram) thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format("Lỗi khi vẽ Riser: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void BtnOptimizeVoltageDrop_Click(object sender, RoutedEventArgs e)
		{
			if (_panel == null || _panel.Loads == null || _panel.Loads.Count == 0)
			{
				return;
			}
			int num = 0;
			double cosPhi = ((_panel.PowerFactor > 0.0) ? _panel.PowerFactor : 0.85);
			foreach (ElectricalLoad load in _panel.Loads)
			{
				double voltageV = ((load.Phase == PhaseType.ThreePhase) ? 380.0 : 220.0);
				if (_calculator.AutoUpsizeCableForVoltageDrop(load, voltageV, cosPhi))
				{
					num++;
				}
			}
			SafeRefreshDataGrid();
			DrawCanvasPreview();
			txtDialogStatus.Text = string.Format("Đã tự động tối ưu & tăng tiết diện cho {0} lộ tải vượt sụt áp TCVN!", num);
			MessageBox.Show(string.Format("Đã kiểm tra sụt áp TCVN cho tất cả phụ tải:\n- Số lộ được nâng cỡ cáp: {0}", num), "Tối Ưu Sụt Áp", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}

		private void DrawCanvasPreview()
		{
			if (previewCanvas == null)
			{
				return;
			}
			previewCanvas.Children.Clear();
			if (_panel == null || _panel.Loads == null || _panel.Loads.Count == 0)
			{
				return;
			}
			if (_params != null && _params.LayoutScheme != 0)
			{
				DrawTreePreview(previewCanvas, _panel, _params);
				return;
			}
			int count = _panel.Loads.Count;
			int num = (_params.IncludeSpare ? ((_params.SpareCount3P + _params.SpareCount1P > 0) ? (_params.SpareCount3P + _params.SpareCount1P) : Math.Max(1, _params.SpareCount)) : 0);
			int num2 = num + (_params.IncludeSpd ? 1 : 0) + (_panel.HasCapacitorBank ? 1 : 0);
			int num3 = count + num2;
			double num4 = 2.8;
			double num5 = _params.ColWidth * num4;
			double num6 = _params.TotalHeaderWidth * num4;
			double num7 = num6 + (double)num3 * num5;
			double num8 = (_params.IncludeTitleBlock ? 20.0 : 10.0);
			double num9 = num8 + 20.0;
			bool flag = _panel.Incomer == IncomerType.DualATS || _panel.Incomer == IncomerType.DualInterlock;
			double num10 = num8 + (flag ? 45.0 : 25.0);
			double num11 = ((_params.CabinetHeight > 0.0) ? _params.CabinetHeight : (flag ? 175.0 : 145.0)) * (num4 / 2.8);
			double num12 = num10 + num11;
			double num13 = ((_params.RowCableHeight > 0.0) ? _params.RowCableHeight : 85.0) * (num4 * 0.7);
			double num14 = num12 + num13;
			double num15 = _params.RowPowerHeight * (num4 * 0.85);
			double num16 = _params.RowCircuitHeight * (num4 * 0.85);
			double num17 = _params.RowNameHeight * (num4 * 0.85);
			double num18 = num15 + num16 + num17;
			double num19 = Math.Max(840.0, num9 + num7 + 40.0);
			if (_params.IncludeLegend)
			{
				num19 = Math.Max(num19, 1080.0);
			}
			double num20 = Math.Max(660.0, num14 + num18 + 65.0);
			double num21 = (_params.DrawFrontView ? 520.0 : 0.0);
			double num22 = (_params.DrawFrontView ? 30.0 : 0.0);
			double val = num8 + num19 + num22 + num21 + num8;
			double val2 = Math.Max(num20, 700.0) + num8 * 2.0;
			previewCanvas.Width = Math.Max(1120.0, val);
			previewCanvas.Height = Math.Max(720.0, val2);
			if (_params.IncludeTitleBlock)
			{
				Rectangle rectangle = new Rectangle();
				rectangle.Width = num19;
				rectangle.Height = num20;
				rectangle.Stroke = Brushes.Black;
				rectangle.StrokeThickness = 2.0;
				Rectangle rectangle2 = rectangle;
				Canvas.SetLeft(rectangle2, num8);
				Canvas.SetTop(rectangle2, num8);
				previewCanvas.Children.Add(rectangle2);
				Rectangle rectangle3 = new Rectangle();
				rectangle3.Width = rectangle2.Width - 8.0;
				rectangle3.Height = rectangle2.Height - 8.0;
				rectangle3.Stroke = Brushes.DarkSlateGray;
				rectangle3.StrokeThickness = 0.8;
				Rectangle element = rectangle3;
				Canvas.SetLeft(element, num8 + 4.0);
				Canvas.SetTop(element, num8 + 4.0);
				previewCanvas.Children.Add(element);
				double num23 = 230.0;
				double num24 = 50.0;
				double num25 = num8 + num19 - 4.0 - num23;
				double num26 = num8 + num20 - 4.0 - num24;
				Rectangle rectangle4 = new Rectangle();
				rectangle4.Width = num23;
				rectangle4.Height = num24;
				rectangle4.Stroke = Brushes.Black;
				rectangle4.StrokeThickness = 1.2;
				rectangle4.Fill = Brushes.White;
				Rectangle element2 = rectangle4;
				Canvas.SetLeft(element2, num25);
				Canvas.SetTop(element2, num26);
				previewCanvas.Children.Add(element2);
				DrawCenteredText(num25 + num23 / 2.0, num26 + 7.0, (!string.IsNullOrEmpty(_params.ProjectName)) ? _params.ProjectName : "CÔNG TRÌNH CƠ ĐIỆN TÒA NHÀ & NHÀ XƯỞNG", 7.0, FontWeights.Bold, Brushes.Navy);
				DrawCenteredText(num25 + num23 / 2.0, num26 + 18.0, string.Format("BẢN VẼ: {0} ({1})", (!string.IsNullOrEmpty(_params.DrawingTitle)) ? _params.DrawingTitle : "SƠ ĐỒ NGUYÊN LÝ", _panel.PanelName), 7.5, FontWeights.Bold, Brushes.DarkRed);
				DrawCenteredText(num25 + num23 / 2.0, num26 + 29.0, string.Format("VẼ: {0}  |  KT: {1}  |  DUYỆT: {2}", _params.DrawnBy ?? "NGUYỄN TẤN TÀI", _params.CheckedBy ?? "CAO VĂN SIÊU", _params.ApprovedBy ?? "DƯƠNG CAO SƠN"), 6.5, FontWeights.Normal, Brushes.DarkSlateGray);
				DrawCenteredText(num25 + num23 / 2.0, num26 + 40.0, string.Format("SỐ BV: {0}  |  TỶ LỆ: {1}  |  REV: {2}", _params.DrawingNumber ?? "EL-01", _params.DrawingScale ?? "1:25", _params.Revision ?? "00"), 7.0, FontWeights.SemiBold, Brushes.Black);
			}
			if (_params.IncludeLegend)
			{
				double num27 = 225.0;
				double num28 = num8 + num19 - 4.0 - num27 - 6.0;
				double num29 = num8 + 12.0;
				double legX = num28 + _params.LegendOffsetX;
				double legY = num29 + _params.LegendOffsetY;
				DrawCanvasLegendTable(legX, legY, num27);
			}
			double num30 = num9 - 10.0;
			double num31 = num9 + num7 + 35.0;
			Rectangle rectangle5 = new Rectangle();
			rectangle5.Width = num31 - num30;
			rectangle5.Height = num11;
			rectangle5.Stroke = Brushes.DarkSlateGray;
			rectangle5.StrokeThickness = 1.5;
			rectangle5.Fill = new SolidColorBrush(Color.FromRgb(252, 253, byte.MaxValue));
			Rectangle element3 = rectangle5;
			Canvas.SetLeft(element3, num30);
			Canvas.SetTop(element3, num10);
			previewCanvas.Children.Add(element3);
			TextBlock textBlock = new TextBlock();
			textBlock.Text = string.Format("T? DI?N: {0}  (Ptt= {1:N3} KW, Itt= {2:N1} A)", _panel.PanelName, _panel.TotalCalculatedPowerKW, _panel.TotalCurrentA);
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.FontSize = 11.5;
			textBlock.Foreground = Brushes.Navy;
			Canvas.SetLeft(textBlock, num30 + 15.0);
			Canvas.SetTop(textBlock, num10 + 8.0);
			previewCanvas.Children.Add(textBlock);
			double num32 = num12 - 45.0;
			System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
			line.X1 = num9 + num6 - 8.0;
			line.Y1 = num32;
			line.X2 = num9 + num7 - 8.0;
			line.Y2 = num32;
			line.Stroke = Brushes.Black;
			line.StrokeThickness = 3.0;
			System.Windows.Shapes.Line element4 = line;
			previewCanvas.Children.Add(element4);
			double num33 = num9 + num6 + Math.Min(2.0, count) * num5;
			double num34 = num10 + (flag ? 40.0 : 16.0);
			double num35 = (_params.IncludeMct ? (num10 + (flag ? 56.0 : 38.0)) : ((num34 + num32) / 2.0));
			double num36 = num32 - 18.0;
			if (_panel.Incomer == IncomerType.DualATS)
			{
				double num37 = 75.0;
				double num38 = 22.0;
				double num39 = num10 + 12.0;
				double num40 = num33 - 32.0;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num40,
					Y1 = num10 - 20.0,
					X2 = num40,
					Y2 = num39,
					Stroke = Brushes.Navy,
					StrokeThickness = 1.5
				});
				DrawCenteredText(num40, num10 - 26.0, "NGUON 1 (EVN)", 7.5, FontWeights.Bold, Brushes.Navy);
				double num41 = num33 + 32.0;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num41,
					Y1 = num10 - 20.0,
					X2 = num41,
					Y2 = num39,
					Stroke = Brushes.DarkRed,
					StrokeThickness = 1.5
				});
				DrawCenteredText(num41, num10 - 26.0, "NGUON 2 (GEN)", 7.5, FontWeights.Bold, Brushes.DarkRed);
				Rectangle rectangle6 = new Rectangle();
				rectangle6.Width = num37;
				rectangle6.Height = num38;
				rectangle6.Stroke = Brushes.DarkBlue;
				rectangle6.StrokeThickness = 1.5;
				rectangle6.Fill = new SolidColorBrush(Color.FromRgb(227, 242, 253));
				Rectangle element5 = rectangle6;
				Canvas.SetLeft(element5, num33 - num37 / 2.0);
				Canvas.SetTop(element5, num39);
				previewCanvas.Children.Add(element5);
				DrawCenteredText(num33, num39 + num38 / 2.0, "BO DOI NGUON ATS", 8.0, FontWeights.Bold, Brushes.DarkBlue);
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num33,
					Y1 = num39 + num38,
					X2 = num33,
					Y2 = num34,
					Stroke = Brushes.Black,
					StrokeThickness = 1.5
				});
			}
			else if (_panel.Incomer == IncomerType.DualInterlock)
			{
				double num42 = num33 - 25.0;
				double num43 = num33 + 25.0;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num42,
					Y1 = num10 - 20.0,
					X2 = num42,
					Y2 = num34,
					Stroke = Brushes.Navy,
					StrokeThickness = 1.2
				});
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num43,
					Y1 = num10 - 20.0,
					X2 = num43,
					Y2 = num34,
					Stroke = Brushes.DarkRed,
					StrokeThickness = 1.2
				});
				DrawCenteredText(num42, num10 - 26.0, "NGUỒN 1: EVN", 7.5, FontWeights.Bold, Brushes.Navy);
				DrawCenteredText(num43, num10 - 26.0, "NGUỒN 2: GEN", 7.5, FontWeights.Bold, Brushes.DarkRed);
				SldInteractiveItem orCreateCustomItem = _params.GetOrCreateCustomItem("INTERLOCK_CB_EVN", "MCCB EVN 3P", "MainBreaker", 36.0, 36.0);
				orCreateCustomItem.BaseX = num42;
				orCreateCustomItem.BaseY = num35;
				if (!orCreateCustomItem.IsDeleted)
				{
					double num44 = orCreateCustomItem.BaseX + orCreateCustomItem.OffsetX;
					double num45 = orCreateCustomItem.BaseY + orCreateCustomItem.OffsetY;
					DrawOrthogonalWire(num42, num34, num44, num45 - 12.0 * orCreateCustomItem.Scale, Brushes.Navy, 1.2);
					DrawInteractiveBreaker(orCreateCustomItem, "MCCB 3P", true, Brushes.Navy);
					DrawOrthogonalWire(num44, num45 + 12.0 * orCreateCustomItem.Scale, num33, num32, Brushes.Navy, 1.2);
				}
				SldInteractiveItem orCreateCustomItem2 = _params.GetOrCreateCustomItem("INTERLOCK_CB_GEN", "MCCB GEN 3P", "MainBreaker", 36.0, 36.0);
				orCreateCustomItem2.BaseX = num43;
				orCreateCustomItem2.BaseY = num35;
				if (!orCreateCustomItem2.IsDeleted)
				{
					double num46 = orCreateCustomItem2.BaseX + orCreateCustomItem2.OffsetX;
					double num47 = orCreateCustomItem2.BaseY + orCreateCustomItem2.OffsetY;
					DrawOrthogonalWire(num43, num34, num46, num47 - 12.0 * orCreateCustomItem2.Scale, Brushes.DarkRed, 1.2);
					DrawInteractiveBreaker(orCreateCustomItem2, "MCCB 3P", true, Brushes.DarkRed);
					DrawOrthogonalWire(num46, num47 + 12.0 * orCreateCustomItem2.Scale, num33, num32, Brushes.DarkRed, 1.2);
				}
				System.Windows.Shapes.Line line2 = new System.Windows.Shapes.Line();
				line2.X1 = num42 + 8.0;
				line2.Y1 = num35;
				line2.X2 = num43 - 8.0;
				line2.Y2 = num35;
				line2.Stroke = Brushes.DarkOrange;
				line2.StrokeThickness = 2.0;
				line2.StrokeDashArray = new System.Windows.Media.DoubleCollection(new double[2] { 2.0, 2.0 });
				System.Windows.Shapes.Line element6 = line2;
				previewCanvas.Children.Add(element6);
				DrawCenteredText(num33, num35 - 7.0, "INTERLOCK", 7.0, FontWeights.Bold, Brushes.DarkOrange);
			}
			else
			{
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num33,
					Y1 = num10 - 15.0,
					X2 = num33,
					Y2 = num34,
					Stroke = Brushes.Black,
					StrokeThickness = 1.5
				});
				TextBlock textBlock2 = new TextBlock();
				textBlock2.Text = "CAP NGUON TU DIEN TONG";
				textBlock2.FontSize = 9.0;
				textBlock2.FontWeight = FontWeights.Bold;
				textBlock2.Foreground = Brushes.DarkSlateGray;
				TextBlock element7 = textBlock2;
				Canvas.SetLeft(element7, num33 - 60.0);
				Canvas.SetTop(element7, num10 - 30.0);
				previewCanvas.Children.Add(element7);
			}
			if (_panel.Incomer != IncomerType.DualInterlock)
			{
				SldInteractiveItem orCreateCustomItem3 = _params.GetOrCreateCustomItem("MAIN_BREAKER", "MCCB Tổng", "MainBreaker", 36.0, 36.0);
				orCreateCustomItem3.BaseX = num33;
				orCreateCustomItem3.BaseY = num35;
				if (!orCreateCustomItem3.IsDeleted)
				{
					double num48 = orCreateCustomItem3.BaseX + orCreateCustomItem3.OffsetX;
					double num49 = orCreateCustomItem3.BaseY + orCreateCustomItem3.OffsetY;
					DrawOrthogonalWire(num33, num34, num48, num49 - 12.0 * orCreateCustomItem3.Scale, Brushes.Black, 1.2);
					DrawInteractiveBreaker(orCreateCustomItem3, _panel.MainBreakerSpec, true);
					if (_params.IncludeMct)
					{
						DrawOrthogonalWire(num48, num49 + 12.0 * orCreateCustomItem3.Scale, num33, num36 - 7.0, Brushes.Black, 1.2);
						for (int i = -1; i <= 1; i++)
						{
							System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
							ellipse.Width = 7.0;
							ellipse.Height = 7.0;
							ellipse.Stroke = Brushes.Black;
							ellipse.StrokeThickness = 1.2;
							System.Windows.Shapes.Ellipse element8 = ellipse;
							Canvas.SetLeft(element8, num33 - 3.5);
							Canvas.SetTop(element8, num36 + (double)i * 5.0 - 3.5);
							previewCanvas.Children.Add(element8);
						}
						TextBlock textBlock3 = new TextBlock();
						textBlock3.Text = "3xMCT";
						textBlock3.FontSize = 8.0;
						textBlock3.FontWeight = FontWeights.SemiBold;
						textBlock3.Foreground = Brushes.Black;
						TextBlock element9 = textBlock3;
						Canvas.SetLeft(element9, num33 - 32.0);
						Canvas.SetTop(element9, num36 - 6.0);
						previewCanvas.Children.Add(element9);
						previewCanvas.Children.Add(new System.Windows.Shapes.Line
						{
							X1 = num33,
							Y1 = num36 + 7.0,
							X2 = num33,
							Y2 = num32,
							Stroke = Brushes.Black,
							StrokeThickness = 1.2
						});
					}
					else
					{
						DrawOrthogonalWire(num48, num49 + 12.0 * orCreateCustomItem3.Scale, num33, num32, Brushes.Black, 1.2);
					}
					System.Windows.Shapes.Ellipse ellipse2 = new System.Windows.Shapes.Ellipse();
					ellipse2.Width = 4.0;
					ellipse2.Height = 4.0;
					ellipse2.Fill = Brushes.Black;
					System.Windows.Shapes.Ellipse element10 = ellipse2;
					Canvas.SetLeft(element10, num33 - 2.0);
					Canvas.SetTop(element10, num32 - 2.0);
					previewCanvas.Children.Add(element10);
				}
			}
			if (_params.IncludeFuse || _params.IncludeLamps || _params.IncludeRelay || _params.IncludeMct)
			{
				System.Windows.Shapes.Ellipse ellipse3 = new System.Windows.Shapes.Ellipse();
				ellipse3.Width = 3.5;
				ellipse3.Height = 3.5;
				ellipse3.Fill = Brushes.Black;
				System.Windows.Shapes.Ellipse element11 = ellipse3;
				Canvas.SetLeft(element11, num33 - 1.75);
				Canvas.SetTop(element11, num34 - 1.75);
				previewCanvas.Children.Add(element11);
				double num50 = num33 + 45.0;
				if (_params.IncludeFuse)
				{
					double num51 = num33 + 10.0;
					double num52 = num51 + 24.0;
					double baseX = (num51 + num52) / 2.0;
					SldInteractiveItem orCreateCustomItem4 = _params.GetOrCreateCustomItem("ACCESSORY_FUSE", "Cầu Chì Đo Lường", "Fuse", 32.0, 20.0);
					orCreateCustomItem4.BaseX = baseX;
					orCreateCustomItem4.BaseY = num34;
					if (!orCreateCustomItem4.IsDeleted)
					{
						double num53 = orCreateCustomItem4.BaseX + orCreateCustomItem4.OffsetX;
						double num54 = orCreateCustomItem4.BaseY + orCreateCustomItem4.OffsetY;
						DrawOrthogonalWire(num33, num34, num53 - 14.0 * orCreateCustomItem4.Scale, num54, Brushes.Black, 1.0);
						DrawInteractiveFuse(orCreateCustomItem4);
						DrawOrthogonalWire(num53 + 14.0 * orCreateCustomItem4.Scale, num54, num50, num34, Brushes.Black, 1.0);
					}
				}
				else
				{
					previewCanvas.Children.Add(new System.Windows.Shapes.Line
					{
						X1 = num33,
						Y1 = num34,
						X2 = num50,
						Y2 = num34,
						Stroke = Brushes.Black,
						StrokeThickness = 1.0
					});
				}
				if (_params.IncludeLamps)
				{
					double num55 = num50 + 6.0;
					double baseX2 = num55 + 18.0;
					SldInteractiveItem orCreateCustomItem5 = _params.GetOrCreateCustomItem("ACCESSORY_LAMPS", "Đèn Báo Pha (R-Y-B)", "Lamps", 54.0, 24.0);
					orCreateCustomItem5.BaseX = baseX2;
					orCreateCustomItem5.BaseY = num34;
					if (!orCreateCustomItem5.IsDeleted)
					{
						double num56 = orCreateCustomItem5.BaseX + orCreateCustomItem5.OffsetX;
						double y = orCreateCustomItem5.BaseY + orCreateCustomItem5.OffsetY;
						DrawOrthogonalWire(num50, num34, num56 - 25.0 * orCreateCustomItem5.Scale, y, Brushes.Black, 1.0);
						DrawInteractiveLamps(orCreateCustomItem5);
					}
				}
				if (_params.IncludeRelay || _params.IncludeMct)
				{
					double num57 = num34 + 28.0;
					double num58 = num36;
					double y2 = (_params.IncludeMct ? num58 : num57);
					previewCanvas.Children.Add(new System.Windows.Shapes.Line
					{
						X1 = num50,
						Y1 = num34,
						X2 = num50,
						Y2 = y2,
						Stroke = Brushes.Green,
						StrokeThickness = 1.2
					});
					double num59 = num50 + 16.0;
					double baseX3 = num59 + 13.0;
					if (_params.IncludeRelay)
					{
						SldInteractiveItem orCreateCustomItem6 = _params.GetOrCreateCustomItem("ACCESSORY_RELAY", "Rơ Le Bảo Vệ", "Relay", 34.0, 24.0);
						orCreateCustomItem6.BaseX = baseX3;
						orCreateCustomItem6.BaseY = num57;
						if (!orCreateCustomItem6.IsDeleted)
						{
							double num60 = orCreateCustomItem6.BaseX + orCreateCustomItem6.OffsetX;
							double y3 = orCreateCustomItem6.BaseY + orCreateCustomItem6.OffsetY;
							DrawOrthogonalWire(num50, num57, num60 - 15.0 * orCreateCustomItem6.Scale, y3, Brushes.Green, 1.2);
							DrawInteractiveRelay(orCreateCustomItem6);
						}
					}
					if (_params.IncludeMct)
					{
						SldInteractiveItem orCreateCustomItem7 = _params.GetOrCreateCustomItem("ACCESSORY_PM", "Đồng Hồ Đa Năng PM", "PM", 34.0, 28.0);
						orCreateCustomItem7.BaseX = baseX3;
						orCreateCustomItem7.BaseY = num58;
						if (!orCreateCustomItem7.IsDeleted)
						{
							double num61 = orCreateCustomItem7.BaseX + orCreateCustomItem7.OffsetX;
							double num62 = orCreateCustomItem7.BaseY + orCreateCustomItem7.OffsetY;
							DrawOrthogonalWire(num50, num58, num61 - 15.0 * orCreateCustomItem7.Scale, num62, Brushes.Green, 1.2);
							DrawOrthogonalWire(num33 + 4.0, num36, num61 - 15.0 * orCreateCustomItem7.Scale, num62 + 5.0 * orCreateCustomItem7.Scale, Brushes.Green, 1.2);
							System.Windows.Shapes.Ellipse ellipse4 = new System.Windows.Shapes.Ellipse();
							ellipse4.Width = 3.0;
							ellipse4.Height = 3.0;
							ellipse4.Fill = Brushes.Green;
							System.Windows.Shapes.Ellipse element12 = ellipse4;
							Canvas.SetLeft(element12, num33 + 3.0);
							Canvas.SetTop(element12, num36 - 1.5);
							previewCanvas.Children.Add(element12);
							DrawInteractivePM(orCreateCustomItem7);
						}
					}
				}
			}
			double num63 = num12 - 22.0;
			for (int j = 0; j < count; j++)
			{
				ElectricalLoad electricalLoad = _panel.Loads[j];
				double num64 = num9 + num6 + ((double)j + 0.5) * num5;
				Brush brush = ((electricalLoad.Phase == PhaseType.ThreePhase) ? Brushes.Red : Brushes.Green);
				SldInteractiveItem orCreateCustomItem8 = _params.GetOrCreateCustomItem("LOAD_CB_" + j, "CB Lộ " + (j + 1) + " (" + electricalLoad.LoadName + ")", "BranchBreaker", 36.0, 36.0);
				orCreateCustomItem8.LoadIndex = j;
				orCreateCustomItem8.BaseX = num64;
				orCreateCustomItem8.BaseY = num63;
				double num65 = orCreateCustomItem8.BaseX + orCreateCustomItem8.OffsetX;
				double num66 = orCreateCustomItem8.BaseY + orCreateCustomItem8.OffsetY;
				DrawOrthogonalWire(num64, num32, num65, num66 - 12.0 * orCreateCustomItem8.Scale, Brushes.Black, 1.2);
				System.Windows.Shapes.Ellipse ellipse5 = new System.Windows.Shapes.Ellipse();
				ellipse5.Width = 3.5;
				ellipse5.Height = 3.5;
				ellipse5.Fill = Brushes.Black;
				System.Windows.Shapes.Ellipse element13 = ellipse5;
				Canvas.SetLeft(element13, num64 - 1.75);
				Canvas.SetTop(element13, num32 - 1.75);
				previewCanvas.Children.Add(element13);
				DrawInteractiveBreaker(orCreateCustomItem8, electricalLoad.BreakerSpec, false, brush);
				if (electricalLoad.IsSpare || (!string.IsNullOrEmpty(electricalLoad.LoadName) && (electricalLoad.LoadName.ToUpper().Contains("SPARE") || electricalLoad.LoadName.ToUpper().Contains("DỰ PHÒNG"))) || !electricalLoad.DrawOutgoingWire)
				{
					continue;
				}
				StarterType starterType = electricalLoad.Starter;
				if (starterType == StarterType.Direct && electricalLoad.HasDol)
				{
					starterType = StarterType.DOL;
				}
				if (starterType != 0)
				{
					double num67 = num63 + 14.0;
					double num68 = 16.0;
					double y4 = num67 + num68;
					DrawOrthogonalWire(num65, num66 + 12.0 * orCreateCustomItem8.Scale, num64, num67, brush, 1.2);
					string text = "DOL";
					Brush brush2 = brush;
					Brush fill = Brushes.White;
					switch (starterType)
					{
					case StarterType.StarDelta:
						text = "Y - Δ";
						brush2 = Brushes.DarkRed;
						fill = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 235, 238));
						break;
					case StarterType.VFD:
						text = "VFD";
						brush2 = Brushes.DarkMagenta;
						fill = new SolidColorBrush(Color.FromRgb(243, 229, 245));
						break;
					case StarterType.SoftStarter:
						text = "SS";
						brush2 = Brushes.DarkOrange;
						fill = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 243, 224));
						break;
					}
					Rectangle rectangle7 = new Rectangle();
					rectangle7.Width = 26.0;
					rectangle7.Height = num68;
					rectangle7.Stroke = brush2;
					rectangle7.StrokeThickness = 1.2;
					rectangle7.Fill = fill;
					Rectangle element14 = rectangle7;
					Canvas.SetLeft(element14, num64 - 13.0);
					Canvas.SetTop(element14, num67);
					previewCanvas.Children.Add(element14);
					DrawCenteredText(num64, num67 + num68 / 2.0, text, 7.5, FontWeights.Bold, brush2);
					if (starterType == StarterType.StarDelta)
					{
						double num69 = 2.5;
						previewCanvas.Children.Add(new System.Windows.Shapes.Line
						{
							X1 = num64 - num69,
							Y1 = y4,
							X2 = num64 - num69,
							Y2 = num14,
							Stroke = brush,
							StrokeThickness = 1.2
						});
						previewCanvas.Children.Add(new System.Windows.Shapes.Line
						{
							X1 = num64 + num69,
							Y1 = y4,
							X2 = num64 + num69,
							Y2 = num14,
							Stroke = brush,
							StrokeThickness = 1.2
						});
					}
					else
					{
						previewCanvas.Children.Add(new System.Windows.Shapes.Line
						{
							X1 = num64,
							Y1 = y4,
							X2 = num64,
							Y2 = num14,
							Stroke = brush,
							StrokeThickness = 1.2
						});
					}
				}
				else
				{
					DrawOrthogonalWire(num65, num66 + 12.0 * orCreateCustomItem8.Scale, num64, num14, brush, 1.2);
				}
				if (electricalLoad.Control != 0)
				{
					string text2 = ((electricalLoad.Control == ControlMode.AutoManual) ? "A-O-M" : ((electricalLoad.Control == ControlMode.FloatSwitch) ? "FS" : "PS"));
					double num70 = (num63 + 10.0 + num14) / 2.0;
					Rectangle rectangle8 = new Rectangle();
					rectangle8.Width = 22.0;
					rectangle8.Height = 10.0;
					rectangle8.Stroke = Brushes.DarkCyan;
					rectangle8.StrokeThickness = 0.8;
					rectangle8.Fill = Brushes.White;
					Rectangle element15 = rectangle8;
					Canvas.SetLeft(element15, num64 + 5.0);
					Canvas.SetTop(element15, num70 - 5.0);
					previewCanvas.Children.Add(element15);
					DrawCenteredText(num64 + 16.0, num70, text2, 6.5, FontWeights.Bold, Brushes.DarkCyan);
				}
				DrawVerticalFeederCableText(num64 - 4.0, num14 - 6.0, electricalLoad.FullCableString, Brushes.Black);
			}
			int num71 = count;
			if (_params.IncludeSpare)
			{
				int num72 = _params.SpareCount3P;
				int spareCount1P = _params.SpareCount1P;
				if (num72 + spareCount1P == 0)
				{
					num72 = Math.Max(1, _params.SpareCount);
				}
				for (int k = 0; k < num72; k++)
				{
					double num73 = num9 + num6 + ((double)num71 + 0.5) * num5;
					previewCanvas.Children.Add(new System.Windows.Shapes.Line
					{
						X1 = num73,
						Y1 = num32,
						X2 = num73,
						Y2 = num63 - 10.0,
						Stroke = Brushes.Black,
						StrokeThickness = 1.2
					});
					System.Windows.Shapes.Ellipse ellipse6 = new System.Windows.Shapes.Ellipse();
					ellipse6.Width = 3.5;
					ellipse6.Height = 3.5;
					ellipse6.Fill = Brushes.Black;
					System.Windows.Shapes.Ellipse element16 = ellipse6;
					Canvas.SetLeft(element16, num73 - 1.75);
					Canvas.SetTop(element16, num32 - 1.75);
					previewCanvas.Children.Add(element16);
					DrawBreakerOnCanvas(num73, num63, "MCCB 3P 40A", false, Brushes.Red);
					if (_params.DrawSpareOutgoingWire)
					{
						previewCanvas.Children.Add(new System.Windows.Shapes.Line
						{
							X1 = num73,
							Y1 = num63 + 10.0,
							X2 = num73,
							Y2 = num14,
							Stroke = Brushes.Red,
							StrokeThickness = 1.2
						});
						DrawVerticalFeederCableText(num73 - 4.0, num14 - 6.0, "Cu/XLPE/PVC 4C-10mm2", Brushes.Black);
					}
					num71++;
				}
				for (int l = 0; l < spareCount1P; l++)
				{
					double num74 = num9 + num6 + ((double)num71 + 0.5) * num5;
					previewCanvas.Children.Add(new System.Windows.Shapes.Line
					{
						X1 = num74,
						Y1 = num32,
						X2 = num74,
						Y2 = num63 - 10.0,
						Stroke = Brushes.Black,
						StrokeThickness = 1.2
					});
					System.Windows.Shapes.Ellipse ellipse7 = new System.Windows.Shapes.Ellipse();
					ellipse7.Width = 3.5;
					ellipse7.Height = 3.5;
					ellipse7.Fill = Brushes.Black;
					System.Windows.Shapes.Ellipse element17 = ellipse7;
					Canvas.SetLeft(element17, num74 - 1.75);
					Canvas.SetTop(element17, num32 - 1.75);
					previewCanvas.Children.Add(element17);
					DrawBreakerOnCanvas(num74, num63, "MCB 1P 16A", false, Brushes.Green);
					if (_params.DrawSpareOutgoingWire)
					{
						previewCanvas.Children.Add(new System.Windows.Shapes.Line
						{
							X1 = num74,
							Y1 = num63 + 10.0,
							X2 = num74,
							Y2 = num14,
							Stroke = Brushes.Green,
							StrokeThickness = 1.2
						});
						DrawVerticalFeederCableText(num74 - 4.0, num14 - 6.0, "Cu/PVC/PVC 2Cx2.5mm2", Brushes.Black);
					}
					num71++;
				}
			}
			if (_params.IncludeSpd)
			{
				double num75 = num9 + num6 + ((double)num71 + 0.5) * num5;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num75,
					Y1 = num32,
					X2 = num75,
					Y2 = num63 - 10.0,
					Stroke = Brushes.Black,
					StrokeThickness = 1.2
				});
				System.Windows.Shapes.Ellipse ellipse8 = new System.Windows.Shapes.Ellipse();
				ellipse8.Width = 3.5;
				ellipse8.Height = 3.5;
				ellipse8.Fill = Brushes.Black;
				System.Windows.Shapes.Ellipse element18 = ellipse8;
				Canvas.SetLeft(element18, num75 - 1.75);
				Canvas.SetTop(element18, num32 - 1.75);
				previewCanvas.Children.Add(element18);
				DrawBreakerOnCanvas(num75, num63, "MCB 3P-6kA 32A", false, Brushes.DarkMagenta);
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num75,
					Y1 = num63 + 10.0,
					X2 = num75,
					Y2 = num14,
					Stroke = Brushes.DarkMagenta,
					StrokeThickness = 1.2
				});
				Rectangle rectangle9 = new Rectangle();
				rectangle9.Width = 22.0;
				rectangle9.Height = 14.0;
				rectangle9.Stroke = Brushes.DarkMagenta;
				rectangle9.StrokeThickness = 1.2;
				rectangle9.Fill = Brushes.White;
				Rectangle element19 = rectangle9;
				Canvas.SetLeft(element19, num75 - 11.0);
				Canvas.SetTop(element19, (num63 + 10.0 + num14) / 2.0 - 7.0);
				previewCanvas.Children.Add(element19);
				DrawCenteredText(num75, (num63 + 10.0 + num14) / 2.0, "SPD", 7.0, FontWeights.Bold, Brushes.DarkMagenta);
				DrawVerticalFeederCableText(num75 - 4.0, num14 - 6.0, "Cu/PVC 4x1C-10mm2 + Cu/PVC 1C-10mm2(E)", Brushes.Black);
				num71++;
			}
			if (_panel.HasCapacitorBank)
			{
				double num76 = num9 + num6 + ((double)num71 + 0.5) * num5;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num76,
					Y1 = num32,
					X2 = num76,
					Y2 = num63 - 10.0,
					Stroke = Brushes.Black,
					StrokeThickness = 1.2
				});
				System.Windows.Shapes.Ellipse ellipse9 = new System.Windows.Shapes.Ellipse();
				ellipse9.Width = 3.5;
				ellipse9.Height = 3.5;
				ellipse9.Fill = Brushes.Black;
				System.Windows.Shapes.Ellipse element20 = ellipse9;
				Canvas.SetLeft(element20, num76 - 1.75);
				Canvas.SetTop(element20, num32 - 1.75);
				previewCanvas.Children.Add(element20);
				DrawBreakerOnCanvas(num76, num63, "MCCB 3P-25kA 63A", false, Brushes.DarkBlue);
				double num77 = (num63 + 10.0 + num14) / 2.0;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num76,
					Y1 = num63 + 10.0,
					X2 = num76,
					Y2 = num77 - 10.0,
					Stroke = Brushes.DarkBlue,
					StrokeThickness = 1.2
				});
				Rectangle rectangle10 = new Rectangle();
				rectangle10.Width = 26.0;
				rectangle10.Height = 18.0;
				rectangle10.Stroke = Brushes.DarkBlue;
				rectangle10.StrokeThickness = 1.2;
				rectangle10.Fill = new SolidColorBrush(Color.FromRgb(227, 242, 253));
				Rectangle element21 = rectangle10;
				Canvas.SetLeft(element21, num76 - 13.0);
				Canvas.SetTop(element21, num77 - 9.0);
				previewCanvas.Children.Add(element21);
				DrawCenteredText(num76, num77, "CAP BANK", 6.5, FontWeights.Bold, Brushes.DarkBlue);
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num76,
					Y1 = num77 + 9.0,
					X2 = num76,
					Y2 = num14,
					Stroke = Brushes.DarkBlue,
					StrokeThickness = 1.2
				});
				DrawVerticalFeederCableText(num76 - 4.0, num14 - 6.0, "Cu/XLPE/PVC 4C-16mm2", Brushes.Black);
				num71++;
			}
			Rectangle rectangle11 = new Rectangle();
			rectangle11.Width = num7;
			rectangle11.Height = num18;
			rectangle11.Stroke = Brushes.Black;
			rectangle11.StrokeThickness = 1.5;
			rectangle11.Fill = Brushes.White;
			Rectangle element22 = rectangle11;
			Canvas.SetLeft(element22, num9);
			Canvas.SetTop(element22, num14);
			previewCanvas.Children.Add(element22);
			double num78 = num14 + num15;
			double num79 = num78 + num16;
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num9,
				Y1 = num78,
				X2 = num9 + num7,
				Y2 = num78,
				Stroke = Brushes.Black,
				StrokeThickness = 1.2
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num9,
				Y1 = num79,
				X2 = num9 + num7,
				Y2 = num79,
				Stroke = Brushes.Black,
				StrokeThickness = 1.2
			});
			double num80 = num15 / 3.0;
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num9,
				Y1 = num14 + num80,
				X2 = num9 + num7,
				Y2 = num14 + num80,
				Stroke = Brushes.DarkGray,
				StrokeThickness = 0.8
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num9,
				Y1 = num14 + 2.0 * num80,
				X2 = num9 + num7,
				Y2 = num14 + 2.0 * num80,
				Stroke = Brushes.DarkGray,
				StrokeThickness = 0.8
			});
			double num81 = _params.HeaderMainWidth * num4;
			double num82 = _params.HeaderSubWidth * num4;
			double num83 = num9 + num81;
			double num84 = num9 + num6;
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num83,
				Y1 = num14,
				X2 = num83,
				Y2 = num78,
				Stroke = Brushes.Black,
				StrokeThickness = 1.0
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num84,
				Y1 = num14,
				X2 = num84,
				Y2 = num14 + num18,
				Stroke = Brushes.Black,
				StrokeThickness = 1.5
			});
			for (int m = 1; m < num3; m++)
			{
				double num85 = num9 + num6 + (double)m * num5;
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = num85,
					Y1 = num14,
					X2 = num85,
					Y2 = num14 + num18,
					Stroke = Brushes.Black,
					StrokeThickness = 0.8
				});
			}
			DrawWrappedText(num9 + num81 / 2.0, num14 + num15 / 2.0, "Cng su?t\ntnh ton\n(kW)", num81 - 4.0, 8.5, FontWeights.Bold, Brushes.Black);
			DrawWrappedText(num9 + num6 / 2.0, num78 + num16 / 2.0, "K hi?u l?", num6 - 6.0, 9.0, FontWeights.Bold, Brushes.Black);
			DrawWrappedText(num9 + num6 / 2.0, num79 + num17 / 2.0, "Tn ph? t?i", num6 - 6.0, 9.0, FontWeights.Bold, Brushes.Black);
			double cx = num83 + num82 / 2.0;
			DrawCenteredText(cx, num14 + 0.5 * num80, "R", 8.5, FontWeights.Bold, Brushes.DarkRed);
			DrawCenteredText(cx, num14 + 1.5 * num80, "Y", 8.5, FontWeights.Bold, Brushes.DarkGoldenrod);
			DrawCenteredText(cx, num14 + 2.5 * num80, "B", 8.5, FontWeights.Bold, Brushes.DarkBlue);
			for (int n = 0; n < count; n++)
			{
				ElectricalLoad electricalLoad2 = _panel.Loads[n];
				double cx2 = num9 + num6 + ((double)n + 0.5) * num5;
				Brush brush3 = ((electricalLoad2.Phase == PhaseType.ThreePhase) ? new SolidColorBrush(Color.FromRgb(211, 47, 47)) : new SolidColorBrush(Color.FromRgb(46, 125, 50)));
				bool flag2 = electricalLoad2.IsSpare || (!string.IsNullOrEmpty(electricalLoad2.LoadName) && (electricalLoad2.LoadName.ToUpper().Contains("SPARE") || electricalLoad2.LoadName.ToUpper().Contains("DỰ PHÒNG")));
				string text3 = (flag2 ? "-" : electricalLoad2.DisplayPowerR);
				string text4 = (flag2 ? "-" : electricalLoad2.DisplayPowerY);
				string text5 = (flag2 ? "-" : electricalLoad2.DisplayPowerB);
				string text6 = ((flag2 && (string.IsNullOrEmpty(electricalLoad2.CircuitNumber) || electricalLoad2.CircuitNumber.StartsWith("C"))) ? "SPARE" : electricalLoad2.CircuitNumber);
				DrawCenteredText(cx2, num14 + 0.5 * num80, text3, 8.0, FontWeights.Normal, brush3);
				DrawCenteredText(cx2, num14 + 1.5 * num80, text4, 8.0, FontWeights.Normal, brush3);
				DrawCenteredText(cx2, num14 + 2.5 * num80, text5, 8.0, FontWeights.Normal, brush3);
				DrawCenteredText(cx2, num78 + num16 / 2.0, text6, 8.5, FontWeights.Bold, brush3);
				DrawWrappedText(cx2, num79 + num17 / 2.0, electricalLoad2.LoadName, num5 - 6.0, 8.0, FontWeights.SemiBold, brush3);
			}
			int num86 = count;
			if (_params.IncludeSpare)
			{
				int num87 = _params.SpareCount3P;
				int spareCount1P2 = _params.SpareCount1P;
				if (num87 + spareCount1P2 == 0)
				{
					num87 = Math.Max(1, _params.SpareCount);
				}
				for (int num88 = 0; num88 < num87; num88++)
				{
					double cx3 = num9 + num6 + ((double)num86 + 0.5) * num5;
					DrawCenteredText(cx3, num14 + 0.5 * num80, "-", 8.0, FontWeights.Normal, Brushes.Gray);
					DrawCenteredText(cx3, num14 + 1.5 * num80, "-", 8.0, FontWeights.Normal, Brushes.Gray);
					DrawCenteredText(cx3, num14 + 2.5 * num80, "-", 8.0, FontWeights.Normal, Brushes.Gray);
					DrawCenteredText(cx3, num78 + num16 / 2.0, "SPARE", 8.5, FontWeights.Bold, Brushes.DarkSlateGray);
					DrawWrappedText(cx3, num79 + num17 / 2.0, "LỘ DỰ PHÒNG 3P\nSPARE", num5 - 6.0, 8.0, FontWeights.SemiBold, Brushes.DarkSlateGray);
					num86++;
				}
				for (int num89 = 0; num89 < spareCount1P2; num89++)
				{
					double cx4 = num9 + num6 + ((double)num86 + 0.5) * num5;
					DrawCenteredText(cx4, num14 + 0.5 * num80, "-", 8.0, FontWeights.Normal, Brushes.Gray);
					DrawCenteredText(cx4, num14 + 1.5 * num80, "-", 8.0, FontWeights.Normal, Brushes.Gray);
					DrawCenteredText(cx4, num14 + 2.5 * num80, "-", 8.0, FontWeights.Normal, Brushes.Gray);
					DrawCenteredText(cx4, num78 + num16 / 2.0, "SPARE", 8.5, FontWeights.Bold, Brushes.DarkSlateGray);
					DrawWrappedText(cx4, num79 + num17 / 2.0, "LỘ DỰ PHÒNG 1P\nSPARE", num5 - 6.0, 8.0, FontWeights.SemiBold, Brushes.DarkSlateGray);
					num86++;
				}
			}
			if (_params.IncludeSpd)
			{
				double cx5 = num9 + num6 + ((double)num86 + 0.5) * num5;
				DrawCenteredText(cx5, num78 + num16 / 2.0, "SPD", 8.5, FontWeights.Bold, Brushes.DarkSlateGray);
				DrawWrappedText(cx5, num79 + num17 / 2.0, "CHONG SET\nSPD", num5 - 6.0, 8.0, FontWeights.SemiBold, Brushes.DarkSlateGray);
				num86++;
			}
			if (_panel.HasCapacitorBank)
			{
				double cx6 = num9 + num6 + ((double)num86 + 0.5) * num5;
				DrawCenteredText(cx6, num14 + 0.5 * num80, string.Format("{0:N0}", _panel.CapacitorBankKvar / 3.0), 8.0, FontWeights.Normal, Brushes.DarkBlue);
				DrawCenteredText(cx6, num14 + 1.5 * num80, string.Format("{0:N0}", _panel.CapacitorBankKvar / 3.0), 8.0, FontWeights.Normal, Brushes.DarkBlue);
				DrawCenteredText(cx6, num14 + 2.5 * num80, string.Format("{0:N0}", _panel.CapacitorBankKvar / 3.0), 8.0, FontWeights.Normal, Brushes.DarkBlue);
				DrawCenteredText(cx6, num78 + num16 / 2.0, "CAP", 8.5, FontWeights.Bold, Brushes.DarkBlue);
				DrawWrappedText(cx6, num79 + num17 / 2.0, string.Format("TU BU COSPHI\n{0:N0} kVAR", _panel.CapacitorBankKvar), num5 - 6.0, 8.0, FontWeights.SemiBold, Brushes.DarkBlue);
				num86++;
			}
			if (_params.DrawFrontView)
			{
				double num90 = num8 + num19 + num22;
				double num91 = num8;
				double num92 = num21;
				double num93 = num20;
				if (_params.IncludeTitleBlock)
				{
					Rectangle rectangle12 = new Rectangle();
					rectangle12.Width = num92;
					rectangle12.Height = num93;
					rectangle12.Stroke = Brushes.Black;
					rectangle12.StrokeThickness = 2.0;
					Rectangle element23 = rectangle12;
					Canvas.SetLeft(element23, num90);
					Canvas.SetTop(element23, num91);
					previewCanvas.Children.Add(element23);
					Rectangle rectangle13 = new Rectangle();
					rectangle13.Width = num92 - 8.0;
					rectangle13.Height = num93 - 8.0;
					rectangle13.Stroke = Brushes.DarkSlateGray;
					rectangle13.StrokeThickness = 0.8;
					Rectangle element24 = rectangle13;
					Canvas.SetLeft(element24, num90 + 4.0);
					Canvas.SetTop(element24, num91 + 4.0);
					previewCanvas.Children.Add(element24);
					double num94 = 230.0;
					double num95 = 50.0;
					double num96 = num90 + num92 - 4.0 - num94;
					double num97 = num91 + num93 - 4.0 - num95;
					Rectangle rectangle14 = new Rectangle();
					rectangle14.Width = num94;
					rectangle14.Height = num95;
					rectangle14.Stroke = Brushes.Black;
					rectangle14.StrokeThickness = 1.2;
					rectangle14.Fill = Brushes.White;
					Rectangle element25 = rectangle14;
					Canvas.SetLeft(element25, num96);
					Canvas.SetTop(element25, num97);
					previewCanvas.Children.Add(element25);
					DrawCenteredText(num96 + num94 / 2.0, num97 + 7.0, (!string.IsNullOrEmpty(_params.ProjectName)) ? _params.ProjectName : "CÔNG TRÌNH CƠ ĐIỆN TÒA NHÀ & NHÀ XƯỞNG", 7.0, FontWeights.Bold, Brushes.Navy);
					DrawCenteredText(num96 + num94 / 2.0, num97 + 18.0, string.Format("BẢN VẼ: MẶT NGOÀI & KÍCH THƯỚC VỎ TỦ {0}", _panel.PanelName), 7.5, FontWeights.Bold, Brushes.DarkMagenta);
					DrawCenteredText(num96 + num94 / 2.0, num97 + 29.0, string.Format("VẼ: {0}  |  KT: {1}  |  DUYỆT: {2}", _params.DrawnBy ?? "NGUYỄN TẤN TÀI", _params.CheckedBy ?? "CAO VĂN SIÊU", _params.ApprovedBy ?? "DƯƠNG CAO SƠN"), 6.5, FontWeights.Normal, Brushes.DarkSlateGray);
					DrawCenteredText(num96 + num94 / 2.0, num97 + 40.0, string.Format("SỐ BV: {0}  |  TỶ LỆ: {1}  |  REV: {2}", (!string.IsNullOrEmpty(_params.DrawingNumber)) ? (_params.DrawingNumber + "-02") : "EL-02", _params.DrawingScale ?? "1:25", _params.Revision ?? "00"), 7.0, FontWeights.SemiBold, Brushes.Black);
				}
				DrawCenteredText(num90 + num92 / 2.0, num91 + 16.0, "BẢN VẼ 02: MẶT NGOÀI & KÍCH THƯỚC VỎ TỦ ĐIỆN", 10.0, FontWeights.Bold, Brushes.DarkMagenta);
				ElectricalCalculator.EstimateEnclosureDimensions(_panel);
				double num98 = ((_panel.EstimatedHeight > 0.0) ? _panel.EstimatedHeight : 1400.0);
				double num99 = ((_panel.EstimatedWidth > 0.0) ? _panel.EstimatedWidth : 800.0);
				double num100 = ((_panel.EstimatedDepth > 0.0) ? _panel.EstimatedDepth : 350.0);
				double num101 = 340.0;
				double num102 = Math.Max(160.0, num101 * (num99 / num98));
				double num103 = num90 + 25.0;
				double num104 = num91 + 45.0;
				double height = 22.0;
				Rectangle rectangle15 = new Rectangle();
				rectangle15.Width = num102 - 4.0;
				rectangle15.Height = height;
				rectangle15.Stroke = Brushes.Black;
				rectangle15.StrokeThickness = 1.2;
				rectangle15.Fill = Brushes.DarkGray;
				Rectangle element26 = rectangle15;
				Canvas.SetLeft(element26, num103 + 2.0);
				Canvas.SetTop(element26, num104 + num101);
				previewCanvas.Children.Add(element26);
				DrawCenteredText(num103 + num102 / 2.0, num104 + num101 + 11.0, "CHÂN ĐẾ PLINTH 100mm", 7.0, FontWeights.Bold, Brushes.White);
				Rectangle rectangle16 = new Rectangle();
				rectangle16.Width = num102;
				rectangle16.Height = num101;
				rectangle16.Stroke = Brushes.DarkSlateGray;
				rectangle16.StrokeThickness = 2.0;
				rectangle16.Fill = new SolidColorBrush(Color.FromRgb(245, 247, 250));
				Rectangle element27 = rectangle16;
				Canvas.SetLeft(element27, num103);
				Canvas.SetTop(element27, num104);
				previewCanvas.Children.Add(element27);
				Rectangle rectangle17 = new Rectangle();
				rectangle17.Width = num102 - 14.0;
				rectangle17.Height = num101 - 14.0;
				rectangle17.Stroke = Brushes.SteelBlue;
				rectangle17.StrokeThickness = 1.0;
				rectangle17.Fill = new SolidColorBrush(Color.FromRgb(250, 252, byte.MaxValue));
				Rectangle element28 = rectangle17;
				Canvas.SetLeft(element28, num103 + 7.0);
				Canvas.SetTop(element28, num104 + 7.0);
				previewCanvas.Children.Add(element28);
				for (int num105 = 0; num105 < 3; num105++)
				{
					double num106 = num104 + 25.0 + (double)num105 * ((num101 - 50.0) / 2.0);
					Rectangle rectangle18 = new Rectangle();
					rectangle18.Width = 5.0;
					rectangle18.Height = 14.0;
					rectangle18.Stroke = Brushes.Black;
					rectangle18.StrokeThickness = 0.8;
					rectangle18.Fill = Brushes.DarkSlateGray;
					Rectangle element29 = rectangle18;
					Canvas.SetLeft(element29, num103 + 2.0);
					Canvas.SetTop(element29, num106 - 7.0);
					previewCanvas.Children.Add(element29);
				}
				double num107 = num104 + 32.0;
				double num108 = num103 + num102 / 2.0;
				Brush[] array = new Brush[3]
				{
					Brushes.Red,
					Brushes.Gold,
					Brushes.Blue
				};
				string[] array2 = new string[3] { "R", "Y", "B" };
				for (int num109 = 0; num109 < 3; num109++)
				{
					double num110 = num108 + (double)(num109 - 1) * 36.0;
					System.Windows.Shapes.Ellipse ellipse10 = new System.Windows.Shapes.Ellipse();
					ellipse10.Width = 14.0;
					ellipse10.Height = 14.0;
					ellipse10.Stroke = Brushes.Black;
					ellipse10.StrokeThickness = 1.0;
					ellipse10.Fill = array[num109];
					System.Windows.Shapes.Ellipse element30 = ellipse10;
					Canvas.SetLeft(element30, num110 - 7.0);
					Canvas.SetTop(element30, num107 - 7.0);
					previewCanvas.Children.Add(element30);
					DrawCenteredText(num110, num107 + 14.0, array2[num109], 7.5, FontWeights.Bold, array[num109]);
				}
				double num111 = 56.0;
				double height2 = 46.0;
				double num112 = num108 - num111 / 2.0;
				double num113 = num107 + 36.0;
				Rectangle rectangle19 = new Rectangle();
				rectangle19.Width = num111;
				rectangle19.Height = height2;
				rectangle19.Stroke = Brushes.Black;
				rectangle19.StrokeThickness = 1.2;
				rectangle19.Fill = new SolidColorBrush(Color.FromRgb(224, 242, 241));
				Rectangle element31 = rectangle19;
				Canvas.SetLeft(element31, num112);
				Canvas.SetTop(element31, num113);
				previewCanvas.Children.Add(element31);
				DrawCenteredText(num112 + num111 / 2.0, num113 + 14.0, "DIGITAL PM", 7.5, FontWeights.Bold, Brushes.DarkSlateGray);
				DrawCenteredText(num112 + num111 / 2.0, num113 + 30.0, "MULTIMETER", 7.0, FontWeights.Bold, Brushes.Navy);
				double num114 = num103 + num102 - 14.0;
				double num115 = num104 + num101 / 2.0;
				Rectangle rectangle20 = new Rectangle();
				rectangle20.Width = 6.0;
				rectangle20.Height = 32.0;
				rectangle20.Stroke = Brushes.Black;
				rectangle20.StrokeThickness = 1.0;
				rectangle20.Fill = Brushes.Silver;
				Rectangle element32 = rectangle20;
				Canvas.SetLeft(element32, num114 - 3.0);
				Canvas.SetTop(element32, num115 - 16.0);
				previewCanvas.Children.Add(element32);
				double num116 = num104 + num101 - 55.0;
				for (int num117 = -2; num117 <= 2; num117++)
				{
					System.Windows.Shapes.Line line3 = new System.Windows.Shapes.Line();
					line3.X1 = num108 - 35.0;
					line3.Y1 = num116 + (double)num117 * 5.0;
					line3.X2 = num108 + 35.0;
					line3.Y2 = num116 + (double)num117 * 5.0;
					line3.Stroke = Brushes.Gray;
					line3.StrokeThickness = 1.2;
					System.Windows.Shapes.Line element33 = line3;
					previewCanvas.Children.Add(element33);
				}
				DrawCenteredText(num108, num116 + 18.0, "CHỚP THÔNG GIÓ", 6.8, FontWeights.Normal, Brushes.DarkSlateGray);
				DrawCenteredText(num103 + num102 / 2.0, num104 - 10.0, string.Format("◄──  W = {0} mm  ──►", (int)num99), 8.0, FontWeights.Bold, Brushes.DarkRed);
				DrawCenteredText(num103 + num102 + 16.0, num104 + num101 / 2.0, string.Format("▲\n│\nH\n=\n{0}\nmm\n│\n▼", (int)num98), 7.5, FontWeights.Bold, Brushes.DarkRed);
				double num118 = num103 + num102 + 42.0;
				double num119 = num104;
				double num120 = num90 + num92 - num118 - 12.0;
				double height3 = 200.0;
				Rectangle rectangle21 = new Rectangle();
				rectangle21.Width = num120;
				rectangle21.Height = height3;
				rectangle21.Stroke = Brushes.SteelBlue;
				rectangle21.StrokeThickness = 1.0;
				rectangle21.Fill = Brushes.White;
				Rectangle element34 = rectangle21;
				Canvas.SetLeft(element34, num118);
				Canvas.SetTop(element34, num119);
				previewCanvas.Children.Add(element34);
				Rectangle rectangle22 = new Rectangle();
				rectangle22.Width = num120;
				rectangle22.Height = 22.0;
				rectangle22.Fill = new SolidColorBrush(Color.FromRgb(227, 242, 253));
				Rectangle element35 = rectangle22;
				Canvas.SetLeft(element35, num118);
				Canvas.SetTop(element35, num119);
				previewCanvas.Children.Add(element35);
				DrawCenteredText(num118 + num120 / 2.0, num119 + 11.0, "THÔNG SỐ KỸ THUẬT VỎ TỦ", 7.5, FontWeights.Bold, Brushes.Navy);
				string[] array3 = new string[8]
				{
					string.Format("• Kích thước: H{0} x W{1} x D{2} mm", (int)num98, (int)num99, (int)num100),
					"• Thép cán nguội: Dày 1.5 - 2.0 mm",
					"• Sơn tĩnh điện: RAL 7035 nhăn",
					"• Cấp bảo vệ: IP43 / IP54",
					"• Dạng ngăn cách: Form 2B / Form 3B",
					"• Chân đế: U100 sơn đen chống rỉ",
					"• Bản lề 110° có khóa an toàn",
					"• Tiếp địa vỏ tủ: Thanh đồng PE"
				};
				for (int num121 = 0; num121 < array3.Length; num121++)
				{
					DrawCenteredText(num118 + num120 / 2.0, num119 + 34.0 + (double)num121 * 20.0, array3[num121], 7.0, FontWeights.Normal, Brushes.Black);
				}
			}
		}

		private void DrawCanvasLegendTable(double legX, double legY, double legW)
		{
			double num = 18.0;
			double num2 = 20.0;
			double num3 = 22.0;
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			bool flag4 = false;
			bool flag5 = false;
			bool flag6 = false;
			bool flag7 = false;
			if (_panel.Loads != null)
			{
				foreach (ElectricalLoad load in _panel.Loads)
				{
					if (load.Phase == PhaseType.SinglePhase)
					{
						flag = true;
					}
					if (load.Starter == StarterType.DOL || load.HasDol)
					{
						flag2 = true;
					}
					if (load.Starter == StarterType.StarDelta)
					{
						flag3 = true;
					}
					if (load.Starter == StarterType.VFD)
					{
						flag4 = true;
					}
					if (load.Starter == StarterType.SoftStarter)
					{
						flag5 = true;
					}
					if (load.Control == ControlMode.AutoManual)
					{
						flag6 = true;
					}
					if (load.Control == ControlMode.FloatSwitch || load.Control == ControlMode.PressureSwitch)
					{
						flag7 = true;
					}
				}
			}
			List<Tuple<string, string, Action<Canvas, double, double>>> list = new List<Tuple<string, string, Action<Canvas, double, double>>>();
			list.Add(new Tuple<string, string, Action<Canvas, double, double>>("MCCB", "Aptomat khối đóng cắt nguồn tổng", delegate(Canvas cv, double cx, double cy)
			{
				System.Windows.Shapes.Ellipse element29 = new System.Windows.Shapes.Ellipse
				{
					Width = 6.0,
					Height = 6.0,
					Stroke = Brushes.Black,
					StrokeThickness = 1.0
				};
				Canvas.SetLeft(element29, cx - 8.0);
				Canvas.SetTop(element29, cy - 3.0);
				cv.Children.Add(element29);
				System.Windows.Shapes.Ellipse element30 = new System.Windows.Shapes.Ellipse
				{
					Width = 6.0,
					Height = 6.0,
					Stroke = Brushes.Black,
					StrokeThickness = 1.0
				};
				Canvas.SetLeft(element30, cx + 2.0);
				Canvas.SetTop(element30, cy - 3.0);
				cv.Children.Add(element30);
				System.Windows.Shapes.Line element31 = new System.Windows.Shapes.Line
				{
					X1 = cx - 4.0,
					Y1 = cy + 4.0,
					X2 = cx + 4.0,
					Y2 = cy - 4.0,
					Stroke = Brushes.Black,
					StrokeThickness = 1.2
				};
				cv.Children.Add(element31);
			}));
			if (flag)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("MCB", "Aptomat tép bảo vệ lộ nhánh 1P/3P", delegate(Canvas cv, double cx, double cy)
				{
					System.Windows.Shapes.Ellipse element27 = new System.Windows.Shapes.Ellipse
					{
						Width = 5.0,
						Height = 5.0,
						Stroke = Brushes.ForestGreen,
						StrokeThickness = 1.0
					};
					Canvas.SetLeft(element27, cx - 6.0);
					Canvas.SetTop(element27, cy - 2.5);
					cv.Children.Add(element27);
					System.Windows.Shapes.Ellipse element28 = new System.Windows.Shapes.Ellipse
					{
						Width = 5.0,
						Height = 5.0,
						Stroke = Brushes.ForestGreen,
						StrokeThickness = 1.0
					};
					Canvas.SetLeft(element28, cx + 2.0);
					Canvas.SetTop(element28, cy - 2.5);
					cv.Children.Add(element28);
				}));
			}
			if (flag2)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("DOL", "Khởi động trực tiếp Contactor+TOR", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element26 = new Rectangle
					{
						Width = 20.0,
						Height = 11.0,
						Stroke = Brushes.Red,
						StrokeThickness = 1.0,
						Fill = Brushes.MistyRose
					};
					Canvas.SetLeft(element26, cx - 10.0);
					Canvas.SetTop(element26, cy - 5.5);
					cv.Children.Add(element26);
					DrawCenteredText(cv, cx, cy, "DOL", 5.5, FontWeights.Bold, Brushes.DarkRed);
				}));
			}
			if (flag3)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("Y - Δ", "Khởi động Sao - Tam giác (3 Con)", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element25 = new Rectangle
					{
						Width = 22.0,
						Height = 11.0,
						Stroke = Brushes.Red,
						StrokeThickness = 1.0,
						Fill = Brushes.MistyRose
					};
					Canvas.SetLeft(element25, cx - 11.0);
					Canvas.SetTop(element25, cy - 5.5);
					cv.Children.Add(element25);
					DrawCenteredText(cv, cx, cy, "Y-Δ", 5.5, FontWeights.Bold, Brushes.DarkRed);
				}));
			}
			if (flag4)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("VFD", "Biến tần điều khiển tốc độ động cơ", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element24 = new Rectangle
					{
						Width = 20.0,
						Height = 11.0,
						Stroke = Brushes.Purple,
						StrokeThickness = 1.0,
						Fill = Brushes.Lavender
					};
					Canvas.SetLeft(element24, cx - 10.0);
					Canvas.SetTop(element24, cy - 5.5);
					cv.Children.Add(element24);
					DrawCenteredText(cv, cx, cy, "VFD", 5.5, FontWeights.Bold, Brushes.Purple);
				}));
			}
			if (flag5)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("SS", "Khởi động mềm giảm dòng khởi động", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element23 = new Rectangle
					{
						Width = 18.0,
						Height = 11.0,
						Stroke = Brushes.Chocolate,
						StrokeThickness = 1.0,
						Fill = Brushes.Wheat
					};
					Canvas.SetLeft(element23, cx - 9.0);
					Canvas.SetTop(element23, cy - 5.5);
					cv.Children.Add(element23);
					DrawCenteredText(cv, cx, cy, "SS", 5.5, FontWeights.Bold, Brushes.SaddleBrown);
				}));
			}
			if (_params.IncludeFuse)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("FUSE", "Cầu chì bảo vệ đo lường (3x2A)", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element22 = new Rectangle
					{
						Width = 16.0,
						Height = 8.0,
						Stroke = Brushes.DarkCyan,
						StrokeThickness = 1.0
					};
					Canvas.SetLeft(element22, cx - 8.0);
					Canvas.SetTop(element22, cy - 4.0);
					cv.Children.Add(element22);
				}));
			}
			if (_params.IncludeLamps)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("ĐÈN BÁO", "Đèn LED báo pha R-Y-B thanh cái", delegate(Canvas cv, double cx, double cy)
				{
					System.Windows.Shapes.Ellipse element19 = new System.Windows.Shapes.Ellipse
					{
						Width = 6.0,
						Height = 6.0,
						Fill = Brushes.Red
					};
					Canvas.SetLeft(element19, cx - 9.0);
					Canvas.SetTop(element19, cy - 3.0);
					cv.Children.Add(element19);
					System.Windows.Shapes.Ellipse element20 = new System.Windows.Shapes.Ellipse
					{
						Width = 6.0,
						Height = 6.0,
						Fill = Brushes.Gold
					};
					Canvas.SetLeft(element20, cx - 3.0);
					Canvas.SetTop(element20, cy - 3.0);
					cv.Children.Add(element20);
					System.Windows.Shapes.Ellipse element21 = new System.Windows.Shapes.Ellipse
					{
						Width = 6.0,
						Height = 6.0,
						Fill = Brushes.Blue
					};
					Canvas.SetLeft(element21, cx + 3.0);
					Canvas.SetTop(element21, cy - 3.0);
					cv.Children.Add(element21);
				}));
			}
			if (_params.IncludeMct)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("3xMCT", "Biến dòng đo lường tỷ số .../5A", delegate(Canvas cv, double cx, double cy)
				{
					System.Windows.Shapes.Ellipse element17 = new System.Windows.Shapes.Ellipse
					{
						Width = 6.0,
						Height = 6.0,
						Stroke = Brushes.Black
					};
					Canvas.SetLeft(element17, cx - 7.0);
					Canvas.SetTop(element17, cy - 3.0);
					cv.Children.Add(element17);
					System.Windows.Shapes.Ellipse element18 = new System.Windows.Shapes.Ellipse
					{
						Width = 6.0,
						Height = 6.0,
						Stroke = Brushes.Black
					};
					Canvas.SetLeft(element18, cx);
					Canvas.SetTop(element18, cy - 3.0);
					cv.Children.Add(element18);
				}));
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("PM", "Đồng hồ đa năng kỹ thuật số", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element16 = new Rectangle
					{
						Width = 16.0,
						Height = 10.0,
						Stroke = Brushes.DarkCyan,
						StrokeThickness = 1.0,
						Fill = Brushes.Azure
					};
					Canvas.SetLeft(element16, cx - 8.0);
					Canvas.SetTop(element16, cy - 5.0);
					cv.Children.Add(element16);
					DrawCenteredText(cv, cx, cy, "PM", 5.5, FontWeights.Bold, Brushes.DarkCyan);
				}));
			}
			if (_params.IncludeRelay)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("RELAY", "Rơ-le bảo vệ điện tử OV/UV/EF", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element15 = new Rectangle
					{
						Width = 18.0,
						Height = 10.0,
						Stroke = Brushes.DarkGreen,
						StrokeThickness = 1.0,
						Fill = Brushes.Honeydew
					};
					Canvas.SetLeft(element15, cx - 9.0);
					Canvas.SetTop(element15, cy - 5.0);
					cv.Children.Add(element15);
					DrawCenteredText(cv, cx, cy, "RL", 5.5, FontWeights.Bold, Brushes.DarkGreen);
				}));
			}
			if (_params.IncludeSpd)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("SPD", "Chống sét lan truyền cắt xung sét", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element14 = new Rectangle
					{
						Width = 16.0,
						Height = 10.0,
						Stroke = Brushes.DarkOrchid,
						StrokeThickness = 1.0,
						Fill = Brushes.LavenderBlush
					};
					Canvas.SetLeft(element14, cx - 8.0);
					Canvas.SetTop(element14, cy - 5.0);
					cv.Children.Add(element14);
					DrawCenteredText(cv, cx, cy, "SPD", 5.0, FontWeights.Bold, Brushes.DarkOrchid);
				}));
			}
			if (_panel.HasCapacitorBank)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("TỤ BÙ", "Tụ bù công suất phản kháng cosφ>=0.95", delegate(Canvas cv, double cx, double cy)
				{
					System.Windows.Shapes.Line element10 = new System.Windows.Shapes.Line
					{
						X1 = cx - 5.0,
						Y1 = cy - 4.0,
						X2 = cx - 5.0,
						Y2 = cy + 4.0,
						Stroke = Brushes.DarkMagenta,
						StrokeThickness = 1.2
					};
					System.Windows.Shapes.Line element11 = new System.Windows.Shapes.Line
					{
						X1 = cx - 2.0,
						Y1 = cy - 4.0,
						X2 = cx - 2.0,
						Y2 = cy + 4.0,
						Stroke = Brushes.DarkMagenta,
						StrokeThickness = 1.2
					};
					System.Windows.Shapes.Line element12 = new System.Windows.Shapes.Line
					{
						X1 = cx + 2.0,
						Y1 = cy - 4.0,
						X2 = cx + 2.0,
						Y2 = cy + 4.0,
						Stroke = Brushes.DarkMagenta,
						StrokeThickness = 1.2
					};
					System.Windows.Shapes.Line element13 = new System.Windows.Shapes.Line
					{
						X1 = cx + 5.0,
						Y1 = cy - 4.0,
						X2 = cx + 5.0,
						Y2 = cy + 4.0,
						Stroke = Brushes.DarkMagenta,
						StrokeThickness = 1.2
					};
					cv.Children.Add(element10);
					cv.Children.Add(element11);
					cv.Children.Add(element12);
					cv.Children.Add(element13);
				}));
			}
			if (flag6)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("A-O-M", "Công tắc 3 vị trí Auto-Off-Manual (A-O-M)", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element9 = new Rectangle
					{
						Width = 18.0,
						Height = 10.0,
						Stroke = Brushes.DarkSlateGray,
						StrokeThickness = 1.0
					};
					Canvas.SetLeft(element9, cx - 9.0);
					Canvas.SetTop(element9, cy - 5.0);
					cv.Children.Add(element9);
					DrawCenteredText(cv, cx, cy, "A-O-M", 4.5, FontWeights.Bold, Brushes.Black);
				}));
			}
			if (flag7)
			{
				list.Add(new Tuple<string, string, Action<Canvas, double, double>>("FS / PS", "Phao mức / Rơ-le áp suất tự động", delegate(Canvas cv, double cx, double cy)
				{
					Rectangle element8 = new Rectangle
					{
						Width = 18.0,
						Height = 10.0,
						Stroke = Brushes.DarkSlateGray,
						StrokeThickness = 1.0
					};
					Canvas.SetLeft(element8, cx - 9.0);
					Canvas.SetTop(element8, cy - 5.0);
					cv.Children.Add(element8);
					DrawCenteredText(cv, cx, cy, "FS", 5.0, FontWeights.Bold, Brushes.Black);
				}));
			}
			int count = list.Count;
			double num4 = num3 + num2 + (double)count * num;
			Canvas legendContainer = new Canvas();
			legendContainer.Width = legW;
			legendContainer.Height = num4;
			legendContainer.Background = Brushes.Transparent;
			legendContainer.Cursor = Cursors.SizeAll;
			legendContainer.ToolTip = "Bấm giữ chuột trái để kéo di chuyển bảng chú thích tự do trong bản vẽ\nNhấp đúp chuột để đặt lại vị trí ban đầu";
			Canvas.SetLeft(legendContainer, legX);
			Canvas.SetTop(legendContainer, legY);
			Point dragStart = default(Point);
			bool isDragging = false;
			legendContainer.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				if (e.ClickCount == 2)
				{
					_params.LegendOffsetX = 0.0;
					_params.LegendOffsetY = 0.0;
					DrawCanvasPreview();
				}
				else
				{
					isDragging = true;
					dragStart = e.GetPosition(previewCanvas);
					legendContainer.CaptureMouse();
					e.Handled = true;
				}
			};
			legendContainer.MouseMove += delegate(object s, MouseEventArgs e)
			{
				if (isDragging)
				{
					Point position = e.GetPosition(previewCanvas);
					double num8 = position.X - dragStart.X;
					double num9 = position.Y - dragStart.Y;
					dragStart = position;
					_params.LegendOffsetX += num8;
					_params.LegendOffsetY += num9;
					double left = Canvas.GetLeft(legendContainer);
					double top = Canvas.GetTop(legendContainer);
					Canvas.SetLeft(legendContainer, left + num8);
					Canvas.SetTop(legendContainer, top + num9);
					e.Handled = true;
				}
			};
			legendContainer.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
			{
				if (isDragging)
				{
					isDragging = false;
					legendContainer.ReleaseMouseCapture();
					e.Handled = true;
				}
			};
			Rectangle rectangle = new Rectangle();
			rectangle.Width = legW;
			rectangle.Height = num4;
			rectangle.Stroke = Brushes.DarkSlateGray;
			rectangle.StrokeThickness = 1.2;
			rectangle.Fill = Brushes.White;
			Rectangle element = rectangle;
			Canvas.SetLeft(element, 0.0);
			Canvas.SetTop(element, 0.0);
			legendContainer.Children.Add(element);
			Rectangle rectangle2 = new Rectangle();
			rectangle2.Width = legW;
			rectangle2.Height = num3;
			rectangle2.Fill = new SolidColorBrush(Color.FromRgb(254, 237, 222));
			Rectangle element2 = rectangle2;
			Canvas.SetLeft(element2, 0.0);
			Canvas.SetTop(element2, 0.0);
			legendContainer.Children.Add(element2);
			DrawCenteredText(legendContainer, legW / 2.0, num3 / 2.0, "BẢNG CHÚ THÍCH KÝ HIỆU (LEGEND)", 7.5, FontWeights.Bold, Brushes.DarkRed);
			double num5 = num3;
			Rectangle rectangle3 = new Rectangle();
			rectangle3.Width = legW;
			rectangle3.Height = num2;
			rectangle3.Fill = new SolidColorBrush(Color.FromRgb(245, 245, 245));
			Rectangle element3 = rectangle3;
			Canvas.SetLeft(element3, 0.0);
			Canvas.SetTop(element3, num5);
			legendContainer.Children.Add(element3);
			double num6 = 50.0;
			DrawCenteredText(legendContainer, num6 / 2.0, num5 + num2 / 2.0, "KÝ HIỆU", 6.8, FontWeights.Bold, Brushes.Black);
			DrawCenteredText(legendContainer, num6 + (legW - num6) / 2.0, num5 + num2 / 2.0, "THIẾT BỊ & DIỄN GIẢI", 6.8, FontWeights.Bold, Brushes.Black);
			System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
			line.X1 = num6;
			line.Y1 = num5;
			line.X2 = num6;
			line.Y2 = num4;
			line.Stroke = Brushes.LightGray;
			line.StrokeThickness = 1.0;
			System.Windows.Shapes.Line element4 = line;
			legendContainer.Children.Add(element4);
			for (int i = 0; i < count; i++)
			{
				double num7 = num5 + num2 + (double)i * num;
				if (i % 2 == 1)
				{
					Rectangle rectangle4 = new Rectangle();
					rectangle4.Width = legW;
					rectangle4.Height = num;
					rectangle4.Fill = new SolidColorBrush(Color.FromRgb(250, 250, 252));
					Rectangle element5 = rectangle4;
					Canvas.SetLeft(element5, 0.0);
					Canvas.SetTop(element5, num7);
					legendContainer.Children.Add(element5);
				}
				System.Windows.Shapes.Line line2 = new System.Windows.Shapes.Line();
				line2.X1 = 0.0;
				line2.Y1 = num7;
				line2.X2 = legW;
				line2.Y2 = num7;
				line2.Stroke = Brushes.LightGray;
				line2.StrokeThickness = 0.5;
				System.Windows.Shapes.Line element6 = line2;
				legendContainer.Children.Add(element6);
				Tuple<string, string, Action<Canvas, double, double>> tuple = list[i];
				tuple.Item3(legendContainer, 16.0, num7 + num / 2.0);
				DrawCenteredText(legendContainer, 38.0, num7 + num / 2.0, tuple.Item1, 6.5, FontWeights.Bold, Brushes.DarkSlateGray);
				TextBlock textBlock = new TextBlock();
				textBlock.Text = tuple.Item2;
				textBlock.FontSize = 9.0;
				textBlock.Foreground = Brushes.Black;
				textBlock.VerticalAlignment = VerticalAlignment.Center;
				TextBlock element7 = textBlock;
				Canvas.SetLeft(element7, num6 + 6.0);
				Canvas.SetTop(element7, num7 + 2.0);
				legendContainer.Children.Add(element7);
			}
			previewCanvas.Children.Add(legendContainer);
			if (_params.SldCustomItems != null)
			{
				foreach (SldInteractiveItem sldCustomItem in _params.SldCustomItems)
				{
					if (sldCustomItem.IsCopied && !sldCustomItem.IsDeleted)
					{
						DrawCopiedInteractiveItem(sldCustomItem);
					}
				}
			}
			UpdateGizmoPosition();
		}

		private void DrawVerticalFeederCableText(double x, double yBottom, string text, Brush brush)
		{
			if (!string.IsNullOrEmpty(text))
			{
				TextBlock textBlock = new TextBlock();
				textBlock.Text = text;
				textBlock.FontSize = 8.0;
				textBlock.FontWeight = FontWeights.Normal;
				textBlock.Foreground = ((brush != null) ? brush : Brushes.Black);
				textBlock.LayoutTransform = new RotateTransform(-90.0);
				textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				Size desiredSize = textBlock.DesiredSize;
				Canvas.SetLeft(textBlock, x - desiredSize.Width);
				Canvas.SetTop(textBlock, yBottom - desiredSize.Height);
				previewCanvas.Children.Add(textBlock);
			}
		}

		private void DrawBreakerOnCanvas(double cx, double cy, string spec, bool isMain, Brush textBrush = null)
		{
			System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
			ellipse.Width = 5.0;
			ellipse.Height = 5.0;
			ellipse.Stroke = Brushes.Black;
			ellipse.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element = ellipse;
			System.Windows.Shapes.Ellipse ellipse2 = new System.Windows.Shapes.Ellipse();
			ellipse2.Width = 5.0;
			ellipse2.Height = 5.0;
			ellipse2.Stroke = Brushes.Black;
			ellipse2.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element2 = ellipse2;
			Canvas.SetLeft(element, cx - 2.5);
			Canvas.SetTop(element, cy - 11.0);
			Canvas.SetLeft(element2, cx - 2.5);
			Canvas.SetTop(element2, cy + 6.0);
			previewCanvas.Children.Add(element);
			previewCanvas.Children.Add(element2);
			System.Windows.Shapes.Path path = new System.Windows.Shapes.Path();
			path.Stroke = Brushes.Black;
			path.StrokeThickness = 1.2;
			PathGeometry pathGeometry = new PathGeometry();
			PathFigure pathFigure = new PathFigure();
			pathFigure.StartPoint = new Point(cx, cy - 8.5);
			PathFigure pathFigure2 = pathFigure;
			pathFigure2.Segments.Add(new ArcSegment(new Point(cx, cy + 8.5), new Size(7.0, 10.0), 0.0, false, SweepDirection.Counterclockwise, true));
			pathGeometry.Figures.Add(pathFigure2);
			path.Data = pathGeometry;
			previewCanvas.Children.Add(path);
			if (string.IsNullOrEmpty(spec))
			{
				spec = (isMain ? "MCCB 3P" : "MCB 1P");
			}
			bool flag = !string.IsNullOrEmpty(spec) && (spec.ToUpper().Contains("RCCB") || spec.ToUpper().Contains("RCBO"));
			if (flag && textBrush == null)
			{
				textBrush = Brushes.DarkOrange;
			}
			string[] array = spec.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string arg = ((array.Length > 0) ? array[0] : (isMain ? "MCCB" : "MCB"));
			string arg2 = "";
			string text = "";
			if (array.Length == 2)
			{
				arg2 = array[1];
			}
			else if (array.Length == 3)
			{
				arg2 = array[1];
				text = array[2];
			}
			else if (array.Length >= 4)
			{
				StringBuilder stringBuilder = new StringBuilder();
				for (int i = 1; i < array.Length - 1; i++)
				{
					if (stringBuilder.Length > 0)
					{
						stringBuilder.Append(" ");
					}
					stringBuilder.Append(array[i]);
				}
				arg2 = stringBuilder.ToString();
				text = array[array.Length - 1];
			}
			TextBlock textBlock = new TextBlock();
			textBlock.Text = (string.IsNullOrEmpty(text) ? string.Format("{0}\n{1}", arg, arg2) : string.Format("{0}\n{1}\n{2}", arg, arg2, text));
			textBlock.FontSize = 7.5;
			textBlock.Foreground = ((textBrush != null) ? textBrush : (flag ? Brushes.DarkOrange : Brushes.DarkRed));
			textBlock.FontWeight = FontWeights.SemiBold;
			textBlock.TextAlignment = System.Windows.TextAlignment.Right;
			Canvas.SetLeft(textBlock, cx - 42.0);
			Canvas.SetTop(textBlock, cy - 14.0);
			previewCanvas.Children.Add(textBlock);
			if (flag)
			{
				TextBlock textBlock2 = new TextBlock();
				textBlock2.Text = "ΔI";
				textBlock2.FontSize = 6.5;
				textBlock2.FontWeight = FontWeights.Bold;
				textBlock2.Foreground = Brushes.DarkOrange;
				TextBlock element3 = textBlock2;
				Canvas.SetLeft(element3, cx + 5.0);
				Canvas.SetTop(element3, cy - 6.0);
				previewCanvas.Children.Add(element3);
			}
		}

		private void DrawCenteredText(double cx, double cy, string text, double fontSize, FontWeight weight, Brush brush)
		{
			DrawCenteredText(previewCanvas, cx, cy, text, fontSize, weight, brush);
		}

		private void DrawCenteredText(Canvas targetCanvas, double cx, double cy, string text, double fontSize, FontWeight weight, Brush brush)
		{
			if (!string.IsNullOrEmpty(text) && targetCanvas != null)
			{
				TextBlock textBlock = new TextBlock();
				textBlock.Text = text.Replace("\\n", "\n");
				textBlock.FontSize = fontSize;
				textBlock.FontWeight = weight;
				textBlock.Foreground = ((brush != null) ? brush : Brushes.Black);
				textBlock.TextAlignment = System.Windows.TextAlignment.Center;
				textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				Size desiredSize = textBlock.DesiredSize;
				Canvas.SetLeft(textBlock, cx - desiredSize.Width / 2.0);
				Canvas.SetTop(textBlock, cy - desiredSize.Height / 2.0);
				targetCanvas.Children.Add(textBlock);
			}
		}

		private void DrawWrappedText(double cx, double cy, string text, double maxWidth, double fontSize, FontWeight weight, Brush brush)
		{
			if (!string.IsNullOrEmpty(text))
			{
				TextBlock textBlock = new TextBlock();
				textBlock.Text = text.Replace("\\n", "\n");
				textBlock.FontSize = fontSize;
				textBlock.FontWeight = weight;
				textBlock.Foreground = ((brush != null) ? brush : Brushes.Black);
				textBlock.TextWrapping = TextWrapping.Wrap;
				textBlock.TextAlignment = System.Windows.TextAlignment.Center;
				if (maxWidth > 10.0)
				{
					textBlock.Width = maxWidth;
				}
				textBlock.Measure(new Size((maxWidth > 10.0) ? maxWidth : double.PositiveInfinity, double.PositiveInfinity));
				Size desiredSize = textBlock.DesiredSize;
				Canvas.SetLeft(textBlock, cx - ((maxWidth > 10.0) ? maxWidth : desiredSize.Width) / 2.0);
				Canvas.SetTop(textBlock, cy - desiredSize.Height / 2.0);
				previewCanvas.Children.Add(textBlock);
			}
		}

		private void BtnDrawEnclosureLayout_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				EnclosureLayoutDialog enclosureLayoutDialog = new EnclosureLayoutDialog(_panel);
				enclosureLayoutDialog.ShowDialog();
			}
			catch (Exception ex)
			{
				MessageBox.Show("Lỗi khi mở giao diện Mặt Bằng Bố Trí Vỏ Tủ: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void BtnDrawToCad_Click(object sender, RoutedEventArgs e)
		{
			Document mdiActiveDocument = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
			if (mdiActiveDocument == null)
			{
				MessageBox.Show("Không tìm thấy bản vẽ AutoCAD nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			try
			{
				Editor editor = mdiActiveDocument.Editor;
				Point3d origin = Point3d.Origin;
				bool flag = false;
				using (editor.StartUserInteraction(this))
				{
					PromptPointOptions options = new PromptPointOptions(string.Format("\n[NTT TOOLS] Chọn điểm đặt Sơ đồ nguyên lý tủ điện '{0}': ", _panel.PanelName));
					PromptPointResult point = editor.GetPoint(options);
					if (point.Status == PromptStatus.OK)
					{
						origin = point.Value;
						flag = true;
					}
				}
				if (flag)
				{
					if (_drawingService.DrawSingleLineDiagram(mdiActiveDocument, _panel, origin, _params))
					{
						_panel.DrawingParams = _params.Clone();
						PanelStorageService.AddOrUpdatePanel(_panel);
						txtDialogStatus.Text = string.Format("Đã vẽ sơ đồ tủ '{0}' thành công vào bản vẽ!", _panel.PanelName);
						MessageBox.Show(string.Format("Đã vẽ Sơ đồ nguyên lý tủ điện '{0}' thành công với kích thước đã tinh chỉnh!", _panel.PanelName), "Thành công", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					}
					else
					{
						txtDialogStatus.Text = string.Format("Lỗi khi vẽ sơ đồ tủ '{0}' vào AutoCAD!", _panel.PanelName);
						MessageBox.Show(string.Format("Không thể vẽ sơ đồ nguyên lý tủ điện '{0}' vào AutoCAD! Vui lòng kiểm tra dòng lệnh AutoCAD.", _panel.PanelName), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
					}
				}
				else
				{
					txtDialogStatus.Text = "Hủy thao tác chọn điểm đặt sơ đồ.";
				}
			}
			catch (Exception ex)
			{
				txtDialogStatus.Text = string.Format("Lỗi: {0}", ex.Message);
				MessageBox.Show(string.Format("Lỗi: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void BtnUpdateInCad_Click(object sender, RoutedEventArgs e)
		{
			Document mdiActiveDocument = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
			if (mdiActiveDocument == null)
			{
				MessageBox.Show("Không tìm thấy bản vẽ AutoCAD nào đang mở!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			try
			{
				bool flag = false;
				Editor editor = mdiActiveDocument.Editor;
				using (editor.StartUserInteraction(this))
				{
					flag = _drawingService.UpdateSingleLineDiagram(mdiActiveDocument, _panel, _params);
				}
				if (flag)
				{
					_panel.DrawingParams = _params.Clone();
					PanelStorageService.AddOrUpdatePanel(_panel);
					txtDialogStatus.Text = string.Format("Đã cập nhật tủ điện '{0}' thành công!", _panel.PanelName);
					MessageBox.Show(string.Format("Đã cập nhật Sơ đồ nguyên lý tủ điện '{0}' thành công tại vị trí đã vẽ!", _panel.PanelName), "Thành công", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				}
				else
				{
					txtDialogStatus.Text = "Không tìm thấy sơ đồ cũ để cập nhật.";
					MessageBox.Show(string.Format("Không tìm thấy sơ đồ tủ điện '{0}' cũ trên bản vẽ để cập nhật!\nVui lòng chọn 'Vẽ Sơ Đồ Nguyên Lý Vào AutoCAD'.", _panel.PanelName), "Thông báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}
			catch (Exception ex)
			{
				txtDialogStatus.Text = string.Format("Lỗi: {0}", ex.Message);
				MessageBox.Show(string.Format("Lỗi: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}

		private void DrawTreePreview(Canvas canvas, ElectricalPanel panel, DrawingParameters p)
		{
			if (canvas == null || panel == null)
			{
				return;
			}
			canvas.Children.Clear();
			int val = ((panel.Loads != null) ? panel.Loads.Count : 0);
			int num = Math.Max(1, val);
			if (p.IncludeSpare)
			{
				num += Math.Max(1, p.SpareCount3P + p.SpareCount1P);
			}
						if (p.LayoutScheme == LayoutScheme.SldV2Mcc)
			{
				RenderSldV2MccPreview(canvas, panel, p, 20.0, 0, 0, num);
				return;
			}
			bool flag = p.TreeOrientation == TreeOrientation.HorizontalLeftRight;
			bool flag2 = p.SymbolRegion == SymbolRegion.South_Iec || p.LayoutScheme == LayoutScheme.SouthRegion;
			double num2 = 20.0;
			double num4;
			double num5;
			if (flag)
			{
				if (flag2)
				{
					double num3 = 26.0;
					num4 = Math.Max(820.0, num2 * 2.0 + 130.0 + (double)num * num3 + 200.0);
					num5 = 1200.0;
				}
				else
				{
					double num6 = 48.0;
					num4 = Math.Max(720.0, num2 * 2.0 + 110.0 + (double)num * num6);
					num5 = 1150.0;
				}
			}
			else
			{
				double num7 = 120.0;
				num5 = Math.Max(1120.0, num2 * 2.0 + 100.0 + (double)num * num7);
				num4 = 750.0;
			}
			canvas.Width = num5;
			canvas.Height = num4;
			Rectangle rectangle = new Rectangle();
			rectangle.Width = num5 - num2 * 2.0;
			rectangle.Height = num4 - num2 * 2.0;
			rectangle.Stroke = new SolidColorBrush(Color.FromRgb(30, 36, 51));
			rectangle.StrokeThickness = 2.0;
			rectangle.Fill = new SolidColorBrush(Color.FromRgb(250, 252, byte.MaxValue));
			Rectangle element = rectangle;
			Canvas.SetLeft(element, num2);
			Canvas.SetTop(element, num2);
			canvas.Children.Add(element);
			Rectangle rectangle2 = new Rectangle();
			rectangle2.Width = num5 - num2 * 2.0 - 12.0;
			rectangle2.Height = num4 - num2 * 2.0 - 12.0;
			rectangle2.Stroke = new SolidColorBrush(Color.FromRgb(100, 116, 139));
			rectangle2.StrokeThickness = 0.8;
			Rectangle element2 = rectangle2;
			Canvas.SetLeft(element2, num2 + 6.0);
			Canvas.SetTop(element2, num2 + 6.0);
			canvas.Children.Add(element2);
			Border border = new Border();
			border.Background = new SolidColorBrush(Color.FromRgb(18, 22, 31));
			border.CornerRadius = new CornerRadius(4.0);
			border.Padding = new Thickness(14.0, 8.0, 14.0, 8.0);
			Border border2 = border;
			StackPanel stackPanel = new StackPanel();
			stackPanel.Orientation = Orientation.Horizontal;
			StackPanel stackPanel2 = stackPanel;
			stackPanel2.Children.Add(new TextBlock
			{
				Text = ((p.DiagramType == SldDiagramType.TreeSystemRiser) ? "\ud83c\udfe2 SƠ ĐỒ NGUYÊN LÝ DẠNG CÂY: TOÀN HỆ THỐNG (RISER DIAGRAM)" : ("\ud83c\udf33 SƠ ĐỒ NGUYÊN LÝ DẠNG CÂY: TỦ " + panel.PanelName)),
				FontWeight = FontWeights.Bold,
				FontSize = 13.0,
				Foreground = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue))
			});
			stackPanel2.Children.Add(new TextBlock
			{
				Text = "  |  " + ((p.SymbolRegion == SymbolRegion.North_Tcvn) ? "⭐ CHUẨN MIỀN BẮC (TCVN / EVN NPC)" : "⚡ CHUẨN MIỀN NAM (IEC / EVN SPC)"),
				FontWeight = FontWeights.SemiBold,
				FontSize = 12.0,
				Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 145, 0))
			});
			stackPanel2.Children.Add(new TextBlock
			{
				Text = "  |  " + (flag ? "➡\ufe0f BỐ CỤC NGANG / DỌC" : "⬇\ufe0f BỐ CỤC CÂY ĐỨNG"),
				FontSize = 11.5,
				Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
			});
			border2.Child = stackPanel2;
			Canvas.SetLeft(border2, num2 + 16.0);
			Canvas.SetTop(border2, num2 + 14.0);
			canvas.Children.Add(border2);
			if (flag)
			{
				RenderHorizontalTreePreview(canvas, panel, p, num2, num5, num4, num);
			}
			else
			{
				RenderVerticalTreePreview(canvas, panel, p, num2, num5, num4, num);
			}
		}

		private void RenderVerticalTreePreview(Canvas canvas, ElectricalPanel panel, DrawingParameters p, double pad, double canvasW, double canvasH, int branchCount)
		{
			int num = ((panel.Loads != null) ? panel.Loads.Count : 0);
			double num2 = pad + 45.0;
			double num3 = Math.Max(110.0, (canvasW - pad * 2.0 - 90.0) / (double)branchCount);
			double num4 = pad + 190.0;
			double num5 = num2 + (double)(branchCount - 1) * num3;
			double num6 = (num2 + num5) / 2.0;
			Border border = new Border();
			border.Width = 190.0;
			border.Height = 65.0;
			border.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
			border.BorderBrush = new SolidColorBrush(Color.FromRgb(14, 116, 144));
			border.BorderThickness = new Thickness(1.5);
			border.CornerRadius = new CornerRadius(5.0);
			border.Padding = new Thickness(8.0, 6.0, 8.0, 6.0);
			Border border2 = border;
			StackPanel stackPanel = new StackPanel();
			stackPanel.Children.Add(new TextBlock
			{
				Text = "⚡ NGUỒN VÀO INCOMER",
				FontWeight = FontWeights.Bold,
				FontSize = 11.0,
				Foreground = new SolidColorBrush(Color.FromRgb(14, 116, 144))
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = string.Format("Ptt = {0:0.##} kW  |  Itt = {1:0.#} A", panel.TotalCalculatedPowerKW, panel.TotalCurrentA),
				FontWeight = FontWeights.SemiBold,
				FontSize = 10.0,
				Foreground = Brushes.Black,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = ((!string.IsNullOrEmpty(panel.MainCableSpec)) ? panel.MainCableSpec : "Cu/XLPE/PVC 4x25mm2"),
				FontSize = 9.5,
				Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
			});
			border2.Child = stackPanel;
			Canvas.SetLeft(border2, num6 - 95.0);
			Canvas.SetTop(border2, pad + 55.0);
			canvas.Children.Add(border2);
			SldInteractiveItem orCreateCustomItem = p.GetOrCreateCustomItem("MAIN_BREAKER", "MCCB Tổng (" + panel.PanelName + ")", "MainBreaker", 36.0, 42.0);
			orCreateCustomItem.BaseX = num6;
			orCreateCustomItem.BaseY = pad + 150.0;
			if (!orCreateCustomItem.IsDeleted)
			{
				double num7 = orCreateCustomItem.BaseX + orCreateCustomItem.OffsetX;
				double num8 = orCreateCustomItem.BaseY + orCreateCustomItem.OffsetY;
				DrawOrthogonalWire(num6, pad + 120.0, num7, num8 - 18.0 * orCreateCustomItem.Scale, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.0);
				DrawInteractiveTreeBreaker(canvas, orCreateCustomItem, true, 3, p.SymbolRegion, false);
				DrawOrthogonalWire(num7, num8 + 18.0 * orCreateCustomItem.Scale, num6, num4, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.0);
			}
			System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
			line.X1 = num2 - 25.0;
			line.Y1 = num4;
			line.X2 = num5 + 25.0;
			line.Y2 = num4;
			line.Stroke = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			line.StrokeThickness = 6.0;
			System.Windows.Shapes.Line element = line;
			canvas.Children.Add(element);
			TextBlock textBlock = new TextBlock();
			textBlock.Text = string.Format("THANH CÁI BUSBAR CHÍNH ({0}A Cu)", Math.Ceiling(panel.TotalCurrentA * 1.25 / 10.0) * 10.0);
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.FontSize = 10.0;
			textBlock.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			TextBlock element2 = textBlock;
			Canvas.SetLeft(element2, num2 - 25.0);
			Canvas.SetTop(element2, num4 - 18.0);
			canvas.Children.Add(element2);
			for (int i = 0; i < num; i++)
			{
				ElectricalLoad electricalLoad = panel.Loads[i];
				double num9 = num2 + (double)i * num3;
				SldInteractiveItem orCreateCustomItem2 = p.GetOrCreateCustomItem("LOAD_CB_" + i, "CB Lộ " + (i + 1) + " (" + electricalLoad.LoadName + ")", "BranchBreaker", 36.0, 42.0);
				orCreateCustomItem2.LoadIndex = i;
				orCreateCustomItem2.BaseX = num9;
				orCreateCustomItem2.BaseY = num4 + 65.0;
				if (!orCreateCustomItem2.IsDeleted)
				{
					double num10 = orCreateCustomItem2.BaseX + orCreateCustomItem2.OffsetX;
					double num11 = orCreateCustomItem2.BaseY + orCreateCustomItem2.OffsetY;
					DrawOrthogonalWire(num9, num4, num10, num11 - 18.0 * orCreateCustomItem2.Scale, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 1.6);
					DrawInteractiveTreeBreaker(canvas, orCreateCustomItem2, false, (electricalLoad.Phase != 0) ? 1 : 3, p.SymbolRegion, false);
					DrawOrthogonalWire(num10, num11 + 18.0 * orCreateCustomItem2.Scale, num9, num4 + 140.0, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 1.6);
				}
				Border border3 = new Border();
				border3.Width = 100.0;
				border3.Height = 110.0;
				border3.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
				border3.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
				border3.BorderThickness = new Thickness(1.2);
				border3.CornerRadius = new CornerRadius(4.0);
				border3.Padding = new Thickness(6.0, 6.0, 6.0, 6.0);
				Border border4 = border3;
				StackPanel stackPanel2 = new StackPanel();
				stackPanel2.Children.Add(new TextBlock
				{
					Text = ((!string.IsNullOrEmpty(electricalLoad.CircuitNumber)) ? electricalLoad.CircuitNumber : ("C" + (i + 1))),
					FontWeight = FontWeights.Bold,
					FontSize = 11.0,
					Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
				});
				stackPanel2.Children.Add(new TextBlock
				{
					Text = ((!string.IsNullOrEmpty(electricalLoad.LoadName)) ? electricalLoad.LoadName : "Phụ tải"),
					FontSize = 9.5,
					FontWeight = FontWeights.SemiBold,
					Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
				});
				stackPanel2.Children.Add(new TextBlock
				{
					Text = string.Format("P = {0:0.##} kW ({1}P)", electricalLoad.PowerKW, (electricalLoad.Phase != 0) ? 1 : 3),
					FontSize = 9.0,
					Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
					Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
				});
				stackPanel2.Children.Add(new TextBlock
				{
					Text = ((!string.IsNullOrEmpty(electricalLoad.CableSpec)) ? electricalLoad.CableSpec : "2x2.5mm2"),
					FontSize = 8.5,
					Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
				});
				border4.Child = stackPanel2;
				Canvas.SetLeft(border4, num9 - 50.0);
				Canvas.SetTop(border4, num4 + 140.0);
				canvas.Children.Add(border4);
			}
			if (p.IncludeSpare)
			{
				int num12 = Math.Max(1, p.SpareCount3P + p.SpareCount1P);
				for (int j = 0; j < num12; j++)
				{
					int num13 = num + j;
					double num14 = num2 + (double)num13 * num3;
					int num15 = ((j >= p.SpareCount3P) ? 1 : 3);
					SldInteractiveItem orCreateCustomItem3 = p.GetOrCreateCustomItem("SPARE_CB_" + j, "CB Spare " + (j + 1), "BranchBreaker", 36.0, 42.0);
					orCreateCustomItem3.BaseX = num14;
					orCreateCustomItem3.BaseY = num4 + 65.0;
					if (!orCreateCustomItem3.IsDeleted)
					{
						double num16 = orCreateCustomItem3.BaseX + orCreateCustomItem3.OffsetX;
						double num17 = orCreateCustomItem3.BaseY + orCreateCustomItem3.OffsetY;
						DrawOrthogonalWire(num14, num4, num16, num17 - 18.0 * orCreateCustomItem3.Scale, new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.4);
						DrawInteractiveTreeBreaker(canvas, orCreateCustomItem3, false, num15, p.SymbolRegion, false);
						DrawOrthogonalWire(num16, num17 + 18.0 * orCreateCustomItem3.Scale, num14, num4 + 140.0, new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.4);
					}
					Border border5 = new Border();
					border5.Width = 95.0;
					border5.Height = 70.0;
					border5.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
					border5.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
					border5.BorderThickness = new Thickness(1.0);
					border5.CornerRadius = new CornerRadius(4.0);
					border5.Padding = new Thickness(4.0);
					Border border6 = border5;
					StackPanel stackPanel3 = new StackPanel();
					stackPanel3.Children.Add(new TextBlock
					{
						Text = string.Format("SPARE {0}", j + 1),
						FontWeight = FontWeights.Bold,
						FontSize = 10.0,
						Foreground = Brushes.Gray
					});
					stackPanel3.Children.Add(new TextBlock
					{
						Text = string.Format("Dự phòng {0}P", num15),
						FontSize = 9.0,
						Foreground = Brushes.DimGray
					});
					border6.Child = stackPanel3;
					Canvas.SetLeft(border6, num14 - 47.5);
					Canvas.SetTop(border6, num4 + 140.0);
					canvas.Children.Add(border6);
				}
			}
			if (p.SldCustomItems != null)
			{
				foreach (SldInteractiveItem sldCustomItem in p.SldCustomItems)
				{
					if (sldCustomItem.IsCopied && !sldCustomItem.IsDeleted)
					{
						DrawCopiedInteractiveItem(sldCustomItem);
					}
				}
			}
			UpdateGizmoPosition();
		}

		private void RenderHorizontalTreePreview(Canvas canvas, ElectricalPanel panel, DrawingParameters p, double pad, double canvasW, double canvasH, int branchCount)
		{
			if (p.SymbolRegion == SymbolRegion.South_Iec || p.LayoutScheme == LayoutScheme.SouthRegion)
			{
				RenderSouthHorizontalSchedulePreview(canvas, panel, p, pad, canvasW, canvasH, branchCount);
				return;
			}
			int num = ((panel.Loads != null) ? panel.Loads.Count : 0);
			double num2 = pad + 105.0;
			double num3 = Math.Max(42.0, (canvasH - pad * 2.0 - 130.0) / (double)branchCount);
			double num4 = pad + 250.0;
			double num5 = num2 + (double)(branchCount - 1) * num3;
			double num6 = (num2 + num5) / 2.0;
			Border border = new Border();
			border.Width = 175.0;
			border.Height = 65.0;
			border.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
			border.BorderBrush = new SolidColorBrush(Color.FromRgb(14, 116, 144));
			border.BorderThickness = new Thickness(1.5);
			border.CornerRadius = new CornerRadius(5.0);
			border.Padding = new Thickness(8.0, 6.0, 8.0, 6.0);
			Border border2 = border;
			StackPanel stackPanel = new StackPanel();
			stackPanel.Children.Add(new TextBlock
			{
				Text = "⚡ NGUỒN VÀO INCOMER",
				FontWeight = FontWeights.Bold,
				FontSize = 10.5,
				Foreground = new SolidColorBrush(Color.FromRgb(14, 116, 144))
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = string.Format("P = {0:0.##}kW | {1:0.#}A", panel.TotalCalculatedPowerKW, panel.TotalCurrentA),
				FontWeight = FontWeights.SemiBold,
				FontSize = 9.5,
				Foreground = Brushes.Black
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = ((!string.IsNullOrEmpty(panel.MainCableSpec)) ? panel.MainCableSpec : "Cu/XLPE 4x25"),
				FontSize = 9.0,
				Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
			});
			border2.Child = stackPanel;
			Canvas.SetLeft(border2, pad + 20.0);
			Canvas.SetTop(border2, num6 - 32.5);
			canvas.Children.Add(border2);
			SldInteractiveItem orCreateCustomItem = p.GetOrCreateCustomItem("MAIN_BREAKER", "MCCB Tổng (" + panel.PanelName + ")", "MainBreaker", 42.0, 36.0);
			orCreateCustomItem.BaseX = pad + 215.0;
			orCreateCustomItem.BaseY = num6;
			if (!orCreateCustomItem.IsDeleted)
			{
				double num7 = orCreateCustomItem.BaseX + orCreateCustomItem.OffsetX;
				double num8 = orCreateCustomItem.BaseY + orCreateCustomItem.OffsetY;
				DrawOrthogonalWireH(pad + 195.0, num6, num7 - 18.0 * orCreateCustomItem.Scale, num8, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.0);
				DrawInteractiveTreeBreaker(canvas, orCreateCustomItem, true, 3, p.SymbolRegion, true);
				DrawOrthogonalWireH(num7 + 18.0 * orCreateCustomItem.Scale, num8, num4, num6, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.0);
			}
			System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
			line.X1 = num4;
			line.Y1 = num2 - 20.0;
			line.X2 = num4;
			line.Y2 = num5 + 20.0;
			line.Stroke = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			line.StrokeThickness = 6.0;
			System.Windows.Shapes.Line element = line;
			canvas.Children.Add(element);
			TextBlock textBlock = new TextBlock();
			textBlock.Text = "BUSBAR TRỤC ĐỨNG";
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.FontSize = 9.5;
			textBlock.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			TextBlock element2 = textBlock;
			Canvas.SetLeft(element2, num4 - 10.0);
			Canvas.SetTop(element2, num2 - 36.0);
			canvas.Children.Add(element2);
			for (int i = 0; i < num; i++)
			{
				ElectricalLoad electricalLoad = panel.Loads[i];
				double num9 = num2 + (double)i * num3;
				SldInteractiveItem orCreateCustomItem2 = p.GetOrCreateCustomItem("LOAD_CB_" + i, "CB Lộ " + (i + 1) + " (" + electricalLoad.LoadName + ")", "BranchBreaker", 42.0, 36.0);
				orCreateCustomItem2.LoadIndex = i;
				orCreateCustomItem2.BaseX = num4 + 50.0;
				orCreateCustomItem2.BaseY = num9;
				if (!orCreateCustomItem2.IsDeleted)
				{
					double num10 = orCreateCustomItem2.BaseX + orCreateCustomItem2.OffsetX;
					double num11 = orCreateCustomItem2.BaseY + orCreateCustomItem2.OffsetY;
					DrawOrthogonalWireH(num4, num9, num10 - 18.0 * orCreateCustomItem2.Scale, num11, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 1.4);
					DrawInteractiveTreeBreaker(canvas, orCreateCustomItem2, false, (electricalLoad.Phase != 0) ? 1 : 3, p.SymbolRegion, true);
					DrawOrthogonalWireH(num10 + 18.0 * orCreateCustomItem2.Scale, num11, num4 + 110.0, num9, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 1.4);
				}
				Border border3 = new Border();
				border3.Width = 420.0;
				border3.Height = num3 - 6.0;
				border3.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
				border3.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
				border3.BorderThickness = new Thickness(1.0);
				border3.CornerRadius = new CornerRadius(3.0);
				border3.Padding = new Thickness(8.0, 2.0, 8.0, 2.0);
				Border border4 = border3;
				StackPanel stackPanel2 = new StackPanel();
				stackPanel2.Orientation = Orientation.Horizontal;
				stackPanel2.VerticalAlignment = VerticalAlignment.Center;
				StackPanel stackPanel3 = stackPanel2;
				stackPanel3.Children.Add(new TextBlock
				{
					Text = ((!string.IsNullOrEmpty(electricalLoad.CircuitNumber)) ? electricalLoad.CircuitNumber : ("C" + (i + 1))) + ": ",
					FontWeight = FontWeights.Bold,
					FontSize = 10.0,
					Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
					Width = 40.0
				});
				stackPanel3.Children.Add(new TextBlock
				{
					Text = ((!string.IsNullOrEmpty(electricalLoad.LoadName)) ? electricalLoad.LoadName : "Tải") + " ",
					FontWeight = FontWeights.SemiBold,
					FontSize = 9.5,
					Foreground = Brushes.Black,
					Width = 140.0
				});
				stackPanel3.Children.Add(new TextBlock
				{
					Text = string.Format("({0:0.##}kW, {1}P)  ", electricalLoad.PowerKW, (electricalLoad.Phase != 0) ? 1 : 3),
					FontSize = 9.0,
					Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
					Width = 85.0
				});
				stackPanel3.Children.Add(new TextBlock
				{
					Text = ((!string.IsNullOrEmpty(electricalLoad.CableSpec)) ? electricalLoad.CableSpec : "2x2.5"),
					FontSize = 8.5,
					Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
				});
				border4.Child = stackPanel3;
				Canvas.SetLeft(border4, num4 + 110.0);
				Canvas.SetTop(border4, num9 - (num3 - 6.0) / 2.0);
				canvas.Children.Add(border4);
			}
			if (p.IncludeSpare)
			{
				int num12 = Math.Max(1, p.SpareCount3P + p.SpareCount1P);
				for (int j = 0; j < num12; j++)
				{
					int num13 = num + j;
					double num14 = num2 + (double)num13 * num3;
					int num15 = ((j >= p.SpareCount3P) ? 1 : 3);
					SldInteractiveItem orCreateCustomItem3 = p.GetOrCreateCustomItem("SPARE_CB_" + j, "CB Spare " + (j + 1), "BranchBreaker", 42.0, 36.0);
					orCreateCustomItem3.BaseX = num4 + 50.0;
					orCreateCustomItem3.BaseY = num14;
					if (!orCreateCustomItem3.IsDeleted)
					{
						double num16 = orCreateCustomItem3.BaseX + orCreateCustomItem3.OffsetX;
						double num17 = orCreateCustomItem3.BaseY + orCreateCustomItem3.OffsetY;
						DrawOrthogonalWireH(num4, num14, num16 - 18.0 * orCreateCustomItem3.Scale, num17, new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.2);
						DrawInteractiveTreeBreaker(canvas, orCreateCustomItem3, false, num15, p.SymbolRegion, true);
						DrawOrthogonalWireH(num16 + 18.0 * orCreateCustomItem3.Scale, num17, num4 + 110.0, num14, new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.2);
					}
					TextBlock textBlock2 = new TextBlock();
					textBlock2.Text = string.Format("SPARE {0}: LỘ DỰ PHÒNG ({1}P)", j + 1, num15);
					textBlock2.FontSize = 9.0;
					textBlock2.FontWeight = FontWeights.SemiBold;
					textBlock2.Foreground = Brushes.Gray;
					TextBlock element3 = textBlock2;
					Canvas.SetLeft(element3, num4 + 120.0);
					Canvas.SetTop(element3, num14 - 7.0);
					canvas.Children.Add(element3);
				}
			}
			if (p.SldCustomItems != null)
			{
				foreach (SldInteractiveItem sldCustomItem in p.SldCustomItems)
				{
					if (sldCustomItem.IsCopied && !sldCustomItem.IsDeleted)
					{
						DrawCopiedInteractiveItem(sldCustomItem);
					}
				}
			}
			UpdateGizmoPosition();
		}

		private void RenderSouthHorizontalSchedulePreview(Canvas canvas, ElectricalPanel panel, DrawingParameters p, double pad, double canvasW, double canvasH, int branchCount)
		{
			int num = ((panel.Loads != null) ? panel.Loads.Count : 0);
			double num2 = pad + 60.0;
			double num3 = 22.0;
			double num4 = 34.0;
			double num5 = 26.0;
			double num6 = pad + 20.0;
			double tableX = num6 + 100.0;
			double num7 = tableX - 56.0;
			double num8 = 125.0;
			double wStarter = 50.0;
			double num9 = 35.0;
			double num10 = 190.0;
			double wPhaseA = 35.0;
			double wPhaseB = 35.0;
			double wPhaseC = 35.0;
			double wPhaseABC = 35.0;
			double num11 = wPhaseA + wPhaseB + wPhaseC + wPhaseABC;
			double num12 = 160.0;
			double num13 = 50.0;
			double tableW = num8 + wStarter + num9 + num10 + num11 + num12 + num13;
			double colProtectX = tableX;
			double colStarterX = colProtectX + num8;
			double num14 = colStarterX + wStarter;
			double num15 = num14 + num9;
			double colPhaseAX = num15 + num10;
			double colPhaseBX = colPhaseAX + wPhaseA;
			double colPhaseCX = colPhaseBX + wPhaseB;
			double colPhaseABCX = colPhaseCX + wPhaseC;
			double colDescX = colPhaseABCX + wPhaseABC;
			double num16 = colDescX + num12;
			double num17 = num16 + num13;
			double curY = num2;
			Border border = new Border();
			border.Width = tableW;
			border.Height = num3;
			border.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
			border.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
			border.BorderThickness = new Thickness(1.5);
			border.CornerRadius = new CornerRadius(3.0, 3.0, 0.0, 0.0);
			Border border2 = border;
			border2.Child = new TextBlock
			{
				Text = "SƠ ĐỒ NGUYÊN LÝ TỦ ĐIỆN",
				FontWeight = FontWeights.Bold,
				FontSize = 11.5,
				Foreground = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue)),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
			};
			Canvas.SetLeft(border2, tableX);
			Canvas.SetTop(border2, curY);
			canvas.Children.Add(border2);
			curY += num3;
			Border border3 = new Border();
			border3.Width = tableW;
			border3.Height = num3;
			border3.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
			border3.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
			border3.BorderThickness = new Thickness(1.5, 0.0, 1.5, 1.0);
			Border element = border3;
			Canvas.SetLeft(element, tableX);
			Canvas.SetTop(element, curY);
			canvas.Children.Add(element);
			TextBlock textBlock = new TextBlock();
			textBlock.Text = "TÊN TỦ ĐIỆN:";
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.FontSize = 9.5;
			textBlock.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
			TextBlock element2 = textBlock;
			Canvas.SetLeft(element2, colProtectX + 6.0);
			Canvas.SetTop(element2, curY + 3.0);
			canvas.Children.Add(element2);
			TextBlock textBlock2 = new TextBlock();
			textBlock2.Text = panel.PanelName.ToUpper();
			textBlock2.FontWeight = FontWeights.Bold;
			textBlock2.FontSize = 10.0;
			textBlock2.Foreground = Brushes.White;
			textBlock2.Width = wStarter - 8.0;
			textBlock2.TextTrimming = TextTrimming.CharacterEllipsis;
			TextBlock element3 = textBlock2;
			Canvas.SetLeft(element3, colStarterX + 4.0);
			Canvas.SetTop(element3, curY + 3.0);
			canvas.Children.Add(element3);
			string text2 = "TẦNG ĐIỂN HÌNH";
			TextBlock textBlock3 = new TextBlock();
			textBlock3.Text = "VỊ TRÍ ĐẶT TỦ: " + text2;
			textBlock3.FontWeight = FontWeights.Bold;
			textBlock3.FontSize = 9.5;
			textBlock3.Foreground = Brushes.White;
			TextBlock element4 = textBlock3;
			Canvas.SetLeft(element4, colPhaseAX + 6.0);
			Canvas.SetTop(element4, curY + 3.0);
			canvas.Children.Add(element4);
			curY += num3;
			string text3 = ((!string.IsNullOrEmpty(panel.ParentPanelName)) ? panel.ParentPanelName : "MSB");
			Border border4 = new Border();
			border4.Width = tableW;
			border4.Height = num3;
			border4.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
			border4.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
			border4.BorderThickness = new Thickness(1.5, 0.0, 1.5, 1.0);
			Border element5 = border4;
			Canvas.SetLeft(element5, tableX);
			Canvas.SetTop(element5, curY);
			canvas.Children.Add(element5);
			TextBlock textBlock4 = new TextBlock();
			textBlock4.Text = "TỪ TỦ:";
			textBlock4.FontWeight = FontWeights.Bold;
			textBlock4.FontSize = 9.5;
			textBlock4.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
			TextBlock element6 = textBlock4;
			Canvas.SetLeft(element6, colProtectX + 6.0);
			Canvas.SetTop(element6, curY + 3.0);
			canvas.Children.Add(element6);
			TextBlock textBlock5 = new TextBlock();
			textBlock5.Text = text3;
			textBlock5.FontWeight = FontWeights.Bold;
			textBlock5.FontSize = 10.0;
			textBlock5.Foreground = Brushes.White;
			textBlock5.Width = wStarter - 8.0;
			textBlock5.TextTrimming = TextTrimming.CharacterEllipsis;
			TextBlock element7 = textBlock5;
			Canvas.SetLeft(element7, colStarterX + 4.0);
			Canvas.SetTop(element7, curY + 3.0);
			canvas.Children.Add(element7);
			string text4 = "800x600x250";
			TextBlock textBlock6 = new TextBlock();
			textBlock6.Text = "KÍCH THƯỚC: " + text4;
			textBlock6.FontWeight = FontWeights.Bold;
			textBlock6.FontSize = 9.5;
			textBlock6.Foreground = Brushes.White;
			TextBlock element8 = textBlock6;
			Canvas.SetLeft(element8, colPhaseAX + 6.0);
			Canvas.SetTop(element8, curY + 3.0);
			canvas.Children.Add(element8);
			curY += num3;
			Border border5 = new Border();
			border5.Width = tableW;
			border5.Height = num4;
			border5.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
			border5.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
			border5.BorderThickness = new Thickness(1.5, 0.0, 1.5, 1.0);
			Border element9 = border5;
			Canvas.SetLeft(element9, tableX);
			Canvas.SetTop(element9, curY);
			canvas.Children.Add(element9);
			Action<string, double, double, double, double, bool> action = delegate(string text, double x, double yOffset, double w, double h, bool isBold)
			{
				TextBlock element66 = new TextBlock
				{
					Text = text,
					FontWeight = (isBold ? FontWeights.Bold : FontWeights.SemiBold),
					FontSize = 9.0,
					Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
					TextAlignment = System.Windows.TextAlignment.Center,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					Width = w - 4.0
				};
				Canvas.SetLeft(element66, x + 2.0);
				Canvas.SetTop(element66, curY + yOffset);
				canvas.Children.Add(element66);
			};
			action("THIẾT BỊ BẢO VỆ", colProtectX, 10.0, num8, num4, true);
			action("KHỞI ĐỘNG", colStarterX, 10.0, wStarter, num4, true);
			action("LỘ", num14, 10.0, num9, num4, true);
			action("CÁP / DÂY", num15, 10.0, num10, num4, true);
			action("PHA, CÔNG SUẤT (W)", colPhaseAX, 2.0, num11, 14.0, true);
			System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
			line.X1 = colPhaseAX;
			line.Y1 = curY + 16.0;
			line.X2 = colDescX;
			line.Y2 = curY + 16.0;
			line.Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85));
			line.StrokeThickness = 1.0;
			System.Windows.Shapes.Line element10 = line;
			canvas.Children.Add(element10);
			action("A", colPhaseAX, 18.0, wPhaseA, 14.0, false);
			action("B", colPhaseBX, 18.0, wPhaseB, 14.0, false);
			action("C", colPhaseCX, 18.0, wPhaseC, 14.0, false);
			action("ABC", colPhaseABCX, 18.0, wPhaseABC, 14.0, false);
			action("PHẠM VI CẤP ĐIỆN", colDescX, 10.0, num12, num4, true);
			action("GHI CHÚ", num16, 10.0, num13, num4, true);
			curY += num4;
			double num18 = curY;
			double num19 = 0.0;
			double num20 = 0.0;
			double num21 = 0.0;
			double num22 = 0.0;
			for (int i = 0; i < num; i++)
			{
				ElectricalLoad electricalLoad = panel.Loads[i];
				double rowMidY = curY + num5 / 2.0;
				Rectangle rectangle = new Rectangle();
				rectangle.Width = tableW;
				rectangle.Height = num5;
				rectangle.Fill = ((i % 2 == 0) ? new SolidColorBrush(Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue)) : new SolidColorBrush(Color.FromRgb(248, 250, 252)));
				Rectangle element11 = rectangle;
				Canvas.SetLeft(element11, tableX);
				Canvas.SetTop(element11, curY);
				canvas.Children.Add(element11);
				System.Windows.Shapes.Line line2 = new System.Windows.Shapes.Line();
				line2.X1 = tableX;
				line2.Y1 = curY + num5;
				line2.X2 = num17;
				line2.Y2 = curY + num5;
				line2.Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240));
				line2.StrokeThickness = 1.0;
				System.Windows.Shapes.Line element12 = line2;
				canvas.Children.Add(element12);
				System.Windows.Shapes.Line line3 = new System.Windows.Shapes.Line();
				line3.X1 = num6;
				line3.Y1 = rowMidY;
				line3.X2 = num7 - 8.0;
				line3.Y2 = rowMidY;
				line3.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
				line3.StrokeThickness = 1.5;
				System.Windows.Shapes.Line element13 = line3;
				canvas.Children.Add(element13);
				System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
				ellipse.Width = 4.0;
				ellipse.Height = 4.0;
				ellipse.Fill = new SolidColorBrush(Color.FromRgb(180, 83, 9));
				System.Windows.Shapes.Ellipse element14 = ellipse;
				Canvas.SetLeft(element14, num6 - 2.0);
				Canvas.SetTop(element14, rowMidY - 2.0);
				canvas.Children.Add(element14);
				SldInteractiveItem orCreateCustomItem = p.GetOrCreateCustomItem("LOAD_CB_" + i, "Tiếp Điểm Lộ " + (i + 1) + " (" + electricalLoad.LoadName + ")", "BranchBreaker", 36.0, 24.0);
				orCreateCustomItem.LoadIndex = i;
				orCreateCustomItem.BaseX = num7;
				orCreateCustomItem.BaseY = rowMidY;
				if (!orCreateCustomItem.IsDeleted)
				{
					double num23 = orCreateCustomItem.BaseX + orCreateCustomItem.OffsetX;
					double num24 = orCreateCustomItem.BaseY + orCreateCustomItem.OffsetY;
					DrawOrthogonalWireH(num6, rowMidY, num23 - 14.0 * orCreateCustomItem.Scale, num24, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 1.5);
					DrawInteractiveSouthKnifeSwitch(canvas, orCreateCustomItem, false);
					DrawOrthogonalWireH(num23 + 14.0 * orCreateCustomItem.Scale, num24, tableX, rowMidY, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 1.5);
				}
				string text5;
				if (!string.IsNullOrEmpty(electricalLoad.BreakerSpec))
				{
					text5 = electricalLoad.BreakerSpec;
				}
				else if (!string.IsNullOrEmpty(electricalLoad.BreakerType))
				{
					text5 = string.Format("{0} {1}A", electricalLoad.BreakerType, Math.Max(16.0, Math.Ceiling(electricalLoad.CurrentA * 1.25)));
				}
				else
				{
					text5 = ((electricalLoad.Phase == PhaseType.ThreePhase) ? string.Format("MCB-3P-{0}A-10kA", Math.Max(16.0, Math.Ceiling(electricalLoad.CurrentA * 1.25))) : string.Format("MCB-1P-{0}A-6kA", Math.Max(10.0, Math.Ceiling(electricalLoad.CurrentA * 1.25))));
				}
				TextBlock textBlock7 = new TextBlock();
				textBlock7.Text = text5;
				textBlock7.FontSize = 8.5;
				textBlock7.FontWeight = FontWeights.SemiBold;
				textBlock7.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				textBlock7.VerticalAlignment = VerticalAlignment.Center;
				TextBlock element15 = textBlock7;
				Canvas.SetLeft(element15, colProtectX + 6.0);
				Canvas.SetTop(element15, rowMidY - 7.0);
				canvas.Children.Add(element15);
				string text6 = ((electricalLoad.Starter != 0 && !string.IsNullOrEmpty(electricalLoad.StarterDisplay)) ? electricalLoad.StarterDisplay : ((electricalLoad.LoadName != null && (electricalLoad.LoadName.IndexOf("quạt", StringComparison.OrdinalIgnoreCase) >= 0 || electricalLoad.LoadName.IndexOf("bơm", StringComparison.OrdinalIgnoreCase) >= 0 || electricalLoad.LoadName.IndexOf("fcu", StringComparison.OrdinalIgnoreCase) >= 0)) ? "DOL" : "-"));
				TextBlock textBlock8 = new TextBlock();
				textBlock8.Text = text6;
				textBlock8.FontSize = 9.0;
				textBlock8.FontWeight = ((text6 == "DOL") ? FontWeights.Bold : FontWeights.Normal);
				textBlock8.Foreground = ((text6 == "DOL") ? new SolidColorBrush(Color.FromRgb(14, 116, 144)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)));
				textBlock8.TextAlignment = System.Windows.TextAlignment.Center;
				textBlock8.Width = wStarter;
				TextBlock element16 = textBlock8;
				Canvas.SetLeft(element16, colStarterX);
				Canvas.SetTop(element16, rowMidY - 7.0);
				canvas.Children.Add(element16);
				string text7 = ((!string.IsNullOrEmpty(electricalLoad.CircuitNumber)) ? electricalLoad.CircuitNumber : ("C" + (i + 1)));
				TextBlock textBlock9 = new TextBlock();
				textBlock9.Text = text7;
				textBlock9.FontSize = 9.5;
				textBlock9.FontWeight = FontWeights.Bold;
				textBlock9.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
				textBlock9.TextAlignment = System.Windows.TextAlignment.Center;
				textBlock9.Width = num9;
				TextBlock element17 = textBlock9;
				Canvas.SetLeft(element17, num14);
				Canvas.SetTop(element17, rowMidY - 7.0);
				canvas.Children.Add(element17);
				string text8 = ((!string.IsNullOrEmpty(electricalLoad.CableSpec)) ? electricalLoad.CableSpec : ((electricalLoad.Phase == PhaseType.ThreePhase) ? "4Cu/PVC(1x4)+(1x4)E PVC D25" : "2Cu/PVC(1x2.5)+(1x2.5)E PVC D20"));
				TextBlock textBlock10 = new TextBlock();
				textBlock10.Text = text8;
				textBlock10.FontSize = 8.5;
				textBlock10.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
				textBlock10.Width = num10 - 8.0;
				TextBlock element18 = textBlock10;
				Canvas.SetLeft(element18, num15 + 4.0);
				Canvas.SetTop(element18, rowMidY - 7.0);
				canvas.Children.Add(element18);
				double num25 = electricalLoad.PowerKW * 1000.0;
				string arg = "-";
				string arg2 = "-";
				string arg3 = "-";
				string arg4 = "-";
				if (electricalLoad.Phase == PhaseType.ThreePhase)
				{
					double num26 = num25 / 3.0;
					num19 += num26;
					num20 += num26;
					num21 += num26;
					num22 += num25;
					arg = string.Format("{0:0}", num26);
					arg2 = string.Format("{0:0}", num26);
					arg3 = string.Format("{0:0}", num26);
					arg4 = string.Format("{0:0}", num25);
				}
				else
				{
					int num27;
					if (electricalLoad.PowerR > 0.0001 && electricalLoad.PowerY <= 0.0001 && electricalLoad.PowerB <= 0.0001)
					{
						num27 = 0;
					}
					else if (electricalLoad.PowerY > 0.0001 && electricalLoad.PowerR <= 0.0001 && electricalLoad.PowerB <= 0.0001)
					{
						num27 = 1;
					}
					else if (electricalLoad.PowerB > 0.0001 && electricalLoad.PowerR <= 0.0001 && electricalLoad.PowerY <= 0.0001)
					{
						num27 = 2;
					}
					else
					{
						string ap = (electricalLoad.AssignedPhase ?? "").Trim().ToUpper();
						if (ap == "R" || ap == "A" || ap == "1") num27 = 0;
						else if (ap == "Y" || ap == "PHA B" || ap == "2") num27 = 1;
						else if (ap == "B" || ap == "C" || ap == "PHA C" || ap == "3") num27 = 2;
						else num27 = i % 3;
					}
					switch (num27)
					{
					case 0:
						num19 += num25;
						arg = string.Format("{0:0}", num25);
						break;
					case 1:
						num20 += num25;
						arg2 = string.Format("{0:0}", num25);
						break;
					default:
						num21 += num25;
						arg3 = string.Format("{0:0}", num25);
						break;
					}
				}
				Action<string, double, double, Color> action2 = delegate(string v, double x, double w, Color clr)
				{
					TextBlock element65 = new TextBlock
					{
						Text = v,
						FontSize = 9.0,
						FontWeight = ((v != "-") ? FontWeights.SemiBold : FontWeights.Normal),
						Foreground = ((v != "-") ? new SolidColorBrush(clr) : new SolidColorBrush(Color.FromRgb(203, 213, 225))),
						TextAlignment = System.Windows.TextAlignment.Center,
						Width = w
					};
					Canvas.SetLeft(element65, x);
					Canvas.SetTop(element65, rowMidY - 7.0);
					canvas.Children.Add(element65);
				};
				action2(arg, colPhaseAX, wPhaseA, Color.FromRgb(220, 38, 38));
				action2(arg2, colPhaseBX, wPhaseB, Color.FromRgb(217, 119, 6));
				action2(arg3, colPhaseCX, wPhaseC, Color.FromRgb(37, 99, 235));
				action2(arg4, colPhaseABCX, wPhaseABC, Color.FromRgb(15, 23, 42));
				string text9 = ((!string.IsNullOrEmpty(electricalLoad.LoadName)) ? electricalLoad.LoadName : "Cấp nguồn phụ tải");
				TextBlock textBlock11 = new TextBlock();
				textBlock11.Text = text9;
				textBlock11.FontSize = 9.0;
				textBlock11.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
				textBlock11.Width = num12 - 8.0;
				textBlock11.TextTrimming = TextTrimming.CharacterEllipsis;
				TextBlock element19 = textBlock11;
				Canvas.SetLeft(element19, colDescX + 4.0);
				Canvas.SetTop(element19, rowMidY - 7.0);
				canvas.Children.Add(element19);
				TextBlock textBlock12 = new TextBlock();
				textBlock12.Text = (i + 1).ToString();
				textBlock12.FontSize = 9.0;
				textBlock12.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
				textBlock12.TextAlignment = System.Windows.TextAlignment.Center;
				textBlock12.Width = num13;
				TextBlock element20 = textBlock12;
				Canvas.SetLeft(element20, num16);
				Canvas.SetTop(element20, rowMidY - 7.0);
				canvas.Children.Add(element20);
				curY += num5;
			}
			curY = num18 + (double)num * num5;
			ControlMode[] array = new ControlMode[3]
			{
				ControlMode.BMS,
				ControlMode.PLC,
				ControlMode.Timer
			};
			int num28 = 0;
			ControlMode[] array2 = array;
			foreach (ControlMode controlMode in array2)
			{
				List<Tuple<int, ElectricalLoad, double>> list = new List<Tuple<int, ElectricalLoad, double>>();
				for (int k = 0; k < num; k++)
				{
					ElectricalLoad electricalLoad2 = panel.Loads[k];
					if (electricalLoad2.Control == controlMode && electricalLoad2.IsControlGrouped)
					{
						double item = num18 + (double)k * num5 + num5 / 2.0;
						list.Add(new Tuple<int, ElectricalLoad, double>(k, electricalLoad2, item));
					}
				}
				if (list.Count > 0)
				{
					double num29 = colStarterX - 12.0 - (double)num28 * 12.0;
					double num30 = num18 + 4.0 + (double)num28 * 6.0;
					double num31 = num7 - 18.0 - (double)num28 * 18.0;
					SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue));
					object obj;
					switch (controlMode)
					{
					default:
						obj = "TIMER";
						break;
					case ControlMode.PLC:
						obj = "PLC";
						break;
					case ControlMode.BMS:
						obj = "BMS";
						break;
					}
					string text10 = (string)obj;
					double num32 = double.MinValue;
					foreach (Tuple<int, ElectricalLoad, double> item3 in list)
					{
						double item2 = item3.Item3;
						double num33 = item2 - 9.0;
						num32 = Math.Max(num32, num33);
						System.Windows.Shapes.Line line4 = new System.Windows.Shapes.Line();
						line4.X1 = colStarterX + 8.0;
						line4.Y1 = item2;
						line4.X2 = num29;
						line4.Y2 = num33;
						line4.Stroke = solidColorBrush;
						line4.StrokeThickness = 1.6;
						line4.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4.0, 3.0 };
						System.Windows.Shapes.Line element21 = line4;
						canvas.Children.Add(element21);
					}
					System.Windows.Shapes.Line line5 = new System.Windows.Shapes.Line();
					line5.X1 = num29;
					line5.Y1 = num32;
					line5.X2 = num29;
					line5.Y2 = num30;
					line5.Stroke = solidColorBrush;
					line5.StrokeThickness = 1.6;
					line5.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4.0, 3.0 };
					System.Windows.Shapes.Line element22 = line5;
					canvas.Children.Add(element22);
					System.Windows.Shapes.Line line6 = new System.Windows.Shapes.Line();
					line6.X1 = num29;
					line6.Y1 = num30;
					line6.X2 = num31;
					line6.Y2 = num30;
					line6.Stroke = solidColorBrush;
					line6.StrokeThickness = 1.6;
					line6.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4.0, 3.0 };
					System.Windows.Shapes.Line element23 = line6;
					canvas.Children.Add(element23);
					Polygon polygon = new Polygon();
					polygon.Fill = solidColorBrush;
					polygon.Points = new PointCollection
					{
						new Point(num31, num30),
						new Point(num31 + 7.0, num30 - 3.8),
						new Point(num31 + 7.0, num30 + 3.8)
					};
					Polygon element24 = polygon;
					canvas.Children.Add(element24);
					TextBlock textBlock13 = new TextBlock();
					textBlock13.Text = text10;
					textBlock13.FontWeight = FontWeights.Bold;
					textBlock13.FontSize = 10.5;
					textBlock13.Foreground = solidColorBrush;
					TextBlock element25 = textBlock13;
					Canvas.SetLeft(element25, num31 + 9.0);
					Canvas.SetTop(element25, num30 - 14.0);
					canvas.Children.Add(element25);
					num28++;
				}
				for (int l = 0; l < num; l++)
				{
					ElectricalLoad electricalLoad3 = panel.Loads[l];
					if (electricalLoad3.Control == controlMode && !electricalLoad3.IsControlGrouped)
					{
						double num34 = num18 + (double)l * num5 + num5 / 2.0;
						double num35 = num34 - 7.0;
						double num36 = tableX - 35.0;
						SolidColorBrush solidColorBrush2 = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue));
						object obj2;
						switch (controlMode)
						{
						default:
							obj2 = "TIMER";
							break;
						case ControlMode.PLC:
							obj2 = "PLC";
							break;
						case ControlMode.BMS:
							obj2 = "BMS";
							break;
						}
						string text11 = (string)obj2;
						System.Windows.Shapes.Line line7 = new System.Windows.Shapes.Line();
						line7.X1 = colStarterX + 8.0;
						line7.Y1 = num35;
						line7.X2 = num36;
						line7.Y2 = num35;
						line7.Stroke = solidColorBrush2;
						line7.StrokeThickness = 1.6;
						line7.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4.0, 3.0 };
						System.Windows.Shapes.Line element26 = line7;
						canvas.Children.Add(element26);
						Polygon polygon2 = new Polygon();
						polygon2.Fill = solidColorBrush2;
						polygon2.Points = new PointCollection
						{
							new Point(num36, num35),
							new Point(num36 + 6.0, num35 - 3.2),
							new Point(num36 + 6.0, num35 + 3.2)
						};
						Polygon element27 = polygon2;
						canvas.Children.Add(element27);
						TextBlock textBlock14 = new TextBlock();
						textBlock14.Text = text11;
						textBlock14.FontWeight = FontWeights.Bold;
						textBlock14.FontSize = 9.5;
						textBlock14.Foreground = solidColorBrush2;
						TextBlock element28 = textBlock14;
						Canvas.SetLeft(element28, num36 + 8.0);
						Canvas.SetTop(element28, num35 - 13.0);
						canvas.Children.Add(element28);
					}
				}
			}
			if (p.IncludeSpare)
			{
				int num37 = Math.Max(1, p.SpareCount3P + p.SpareCount1P);
				for (int m = 0; m < num37; m++)
				{
					double num38 = curY + num5 / 2.0;
					int num39 = ((m >= p.SpareCount3P) ? 1 : 3);
					Rectangle rectangle2 = new Rectangle();
					rectangle2.Width = tableW;
					rectangle2.Height = num5;
					rectangle2.Fill = new SolidColorBrush(Color.FromRgb(248, 250, 252));
					Rectangle element29 = rectangle2;
					Canvas.SetLeft(element29, tableX);
					Canvas.SetTop(element29, curY);
					canvas.Children.Add(element29);
					System.Windows.Shapes.Line line8 = new System.Windows.Shapes.Line();
					line8.X1 = tableX;
					line8.Y1 = curY + num5;
					line8.X2 = num17;
					line8.Y2 = curY + num5;
					line8.Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240));
					line8.StrokeThickness = 1.0;
					System.Windows.Shapes.Line element30 = line8;
					canvas.Children.Add(element30);
					SldInteractiveItem orCreateCustomItem2 = p.GetOrCreateCustomItem("SPARE_CB_" + m, "Tiếp Điểm Spare " + (m + 1), "BranchBreaker", 36.0, 24.0);
					orCreateCustomItem2.BaseX = num7;
					orCreateCustomItem2.BaseY = num38;
					if (!orCreateCustomItem2.IsDeleted)
					{
						double num40 = orCreateCustomItem2.BaseX + orCreateCustomItem2.OffsetX;
						double num41 = orCreateCustomItem2.BaseY + orCreateCustomItem2.OffsetY;
						DrawOrthogonalWireH(num6, num38, num40 - 14.0 * orCreateCustomItem2.Scale, num41, new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.2);
						DrawInteractiveSouthKnifeSwitch(canvas, orCreateCustomItem2, false);
						DrawOrthogonalWireH(num40 + 14.0 * orCreateCustomItem2.Scale, num41, tableX, num38, new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1.2);
					}
					TextBlock textBlock15 = new TextBlock();
					textBlock15.Text = string.Format("MCB-{0}P-16A-6kA", num39);
					textBlock15.FontSize = 8.5;
					textBlock15.Foreground = Brushes.Gray;
					TextBlock element31 = textBlock15;
					Canvas.SetLeft(element31, colProtectX + 6.0);
					Canvas.SetTop(element31, num38 - 7.0);
					canvas.Children.Add(element31);
					TextBlock textBlock16 = new TextBlock();
					textBlock16.Text = "-";
					textBlock16.FontSize = 9.0;
					textBlock16.Foreground = Brushes.Gray;
					textBlock16.TextAlignment = System.Windows.TextAlignment.Center;
					textBlock16.Width = wStarter;
					TextBlock element32 = textBlock16;
					Canvas.SetLeft(element32, colStarterX);
					Canvas.SetTop(element32, num38 - 7.0);
					canvas.Children.Add(element32);
					TextBlock textBlock17 = new TextBlock();
					textBlock17.Text = string.Format("SP{0}", m + 1);
					textBlock17.FontSize = 9.5;
					textBlock17.FontWeight = FontWeights.Bold;
					textBlock17.Foreground = Brushes.DimGray;
					textBlock17.TextAlignment = System.Windows.TextAlignment.Center;
					textBlock17.Width = num9;
					TextBlock element33 = textBlock17;
					Canvas.SetLeft(element33, num14);
					Canvas.SetTop(element33, num38 - 7.0);
					canvas.Children.Add(element33);
					TextBlock textBlock18 = new TextBlock();
					textBlock18.Text = "-";
					textBlock18.FontSize = 8.5;
					textBlock18.Foreground = Brushes.Gray;
					textBlock18.Width = num10 - 8.0;
					TextBlock element34 = textBlock18;
					Canvas.SetLeft(element34, num15 + 4.0);
					Canvas.SetTop(element34, num38 - 7.0);
					canvas.Children.Add(element34);
					TextBlock textBlock19 = new TextBlock();
					textBlock19.Text = "DỰ PHÒNG";
					textBlock19.FontSize = 9.0;
					textBlock19.FontWeight = FontWeights.SemiBold;
					textBlock19.Foreground = Brushes.Gray;
					textBlock19.Width = num12 - 8.0;
					TextBlock element35 = textBlock19;
					Canvas.SetLeft(element35, colDescX + 4.0);
					Canvas.SetTop(element35, num38 - 7.0);
					canvas.Children.Add(element35);
					TextBlock textBlock20 = new TextBlock();
					textBlock20.Text = (num + m + 1).ToString();
					textBlock20.FontSize = 9.0;
					textBlock20.Foreground = Brushes.Gray;
					textBlock20.TextAlignment = System.Windows.TextAlignment.Center;
					textBlock20.Width = num13;
					TextBlock element36 = textBlock20;
					Canvas.SetLeft(element36, num16);
					Canvas.SetTop(element36, num38 - 7.0);
					canvas.Children.Add(element36);
					curY += num5;
				}
			}
			double num42 = curY;
			double num43 = num42 + 88.0;
			double[] array3 = new double[11]
			{
				colProtectX, colStarterX, num14, num15, colPhaseAX, colPhaseBX, colPhaseCX, colPhaseABCX, colDescX, num16,
				num17
			};
			double[] array4 = array3;
			foreach (double num44 in array4)
			{
				double y;
				double y2;
				if (num44 == colProtectX || num44 == num17)
				{
					y = num2;
					y2 = num43;
				}
				else if (num44 == colStarterX || num44 == num14 || num44 == colPhaseAX)
				{
					y = num2 + num3;
					y2 = num43;
				}
				else if (num44 == colDescX)
				{
					y = num2 + 3.0 * num3;
					y2 = num43;
				}
				else if (num44 == colPhaseBX || num44 == colPhaseCX || num44 == colPhaseABCX)
				{
					y = num2 + 3.0 * num3 + 16.0;
					y2 = num42 + 22.0;
				}
				else
				{
					y = num2 + 3.0 * num3;
					y2 = num42;
				}
				System.Windows.Shapes.Line line9 = new System.Windows.Shapes.Line();
				line9.X1 = num44;
				line9.Y1 = y;
				line9.X2 = num44;
				line9.Y2 = y2;
				line9.Stroke = new SolidColorBrush(Color.FromRgb(203, 213, 225));
				line9.StrokeThickness = ((num44 == colProtectX || num44 == num17) ? 1.5 : 0.8);
				System.Windows.Shapes.Line element37 = line9;
				canvas.Children.Add(element37);
			}
			double num45 = num18;
			double y3 = num42 + 77.0;
			System.Windows.Shapes.Line line10 = new System.Windows.Shapes.Line();
			line10.X1 = num6;
			line10.Y1 = num45;
			line10.X2 = num6;
			line10.Y2 = y3;
			line10.Stroke = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			line10.StrokeThickness = 6.0;
			System.Windows.Shapes.Line element38 = line10;
			canvas.Children.Add(element38);
			TextBlock textBlock21 = new TextBlock();
			textBlock21.Text = "BUSBAR";
			textBlock21.FontWeight = FontWeights.Bold;
			textBlock21.FontSize = 9.0;
			textBlock21.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			TextBlock element39 = textBlock21;
			Canvas.SetLeft(element39, num6 - 16.0);
			Canvas.SetTop(element39, num45 - 16.0);
			canvas.Children.Add(element39);
			double curSumY = num42;
			double sRowH = 22.0;
			Action<string, string, string, string, string, string, string> action3 = delegate(string protectTxt, string valA, string valB, string valC, string valABC, string descTxt, string starterTxt)
			{
				Border element60 = new Border
				{
					Width = tableW,
					Height = sRowH,
					Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
					BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
					BorderThickness = new Thickness(1.5, 0.0, 1.5, 1.0)
				};
				Canvas.SetLeft(element60, tableX);
				Canvas.SetTop(element60, curSumY);
				canvas.Children.Add(element60);
				TextBlock element61 = new TextBlock
				{
					Text = protectTxt,
					FontWeight = FontWeights.Bold,
					FontSize = 8.5,
					Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
					VerticalAlignment = VerticalAlignment.Center
				};
				Canvas.SetLeft(element61, colProtectX + 6.0);
				Canvas.SetTop(element61, curSumY + 4.0);
				canvas.Children.Add(element61);
				if (!string.IsNullOrEmpty(starterTxt))
				{
					TextBlock element62 = new TextBlock
					{
						Text = starterTxt,
						FontWeight = FontWeights.Bold,
						FontSize = 8.5,
						Foreground = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
						TextAlignment = System.Windows.TextAlignment.Center,
						Width = wStarter
					};
					Canvas.SetLeft(element62, colStarterX);
					Canvas.SetTop(element62, curSumY + 4.0);
					canvas.Children.Add(element62);
				}
				Action<string, double, double, Color> action5 = delegate(string v, double x, double w, Color clr)
				{
					if (!string.IsNullOrEmpty(v))
					{
						TextBlock element64 = new TextBlock
						{
							Text = v,
							FontSize = 9.0,
							FontWeight = FontWeights.Bold,
							Foreground = new SolidColorBrush(clr),
							TextAlignment = System.Windows.TextAlignment.Center,
							Width = w
						};
						Canvas.SetLeft(element64, x);
						Canvas.SetTop(element64, curSumY + 4.0);
						canvas.Children.Add(element64);
					}
				};
				action5(valA, colPhaseAX, wPhaseA, Color.FromRgb(220, 38, 38));
				action5(valB, colPhaseBX, wPhaseB, Color.FromRgb(217, 119, 6));
				action5(valC, colPhaseCX, wPhaseC, Color.FromRgb(37, 99, 235));
				action5(valABC, colPhaseABCX, wPhaseABC, Color.FromRgb(15, 23, 42));
				TextBlock element63 = new TextBlock
				{
					Text = descTxt,
					FontWeight = FontWeights.Bold,
					FontSize = 8.5,
					Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
					VerticalAlignment = VerticalAlignment.Center
				};
				Canvas.SetLeft(element63, colDescX + 4.0);
				Canvas.SetTop(element63, curSumY + 4.0);
				canvas.Children.Add(element63);
				curSumY += sRowH;
			};
			action3("ĐÈN BÁO PHA 3x2W", string.Format("{0:0}", num19), string.Format("{0:0}", num20), string.Format("{0:0}", num21), string.Format("{0:0}", num22), "TỔNG (W)", "");
			action3("ĐỒNG HỒ PM", "", string.Format("{0:0}", num19 + num20 + num21), "", "", "CÔNG SUẤT THIẾT KẾ (W)", "");
			double num46 = ((panel.SimultaneousFactor > 0.0) ? panel.SimultaneousFactor : 0.8);
			action3("CT 50/5A", "", string.Format("{0:0.##}", num46), "", "", "HỆ SỐ ĐỒNG THỜI", "");
			string arg5 = ((!string.IsNullOrEmpty(panel.MainBreakerSpec)) ? panel.MainBreakerSpec : "MCCB-3P-32A-18kA");
			action3(arg5, "", string.Format("{0:0}", panel.TotalCalculatedPowerKW * 1000.0), "", "", string.Format("CÔNG SUẤT TÍNH TOÁN (W) [{0:0.#} A]", panel.TotalCurrentA), "ĐẦU VÀO");
			double num47 = num42 + 77.0;
			double num48 = tableX - 76.0;
			double nodeX = tableX - 52.0;
			double baseX = tableX - 26.0;
			System.Windows.Shapes.Line line11 = new System.Windows.Shapes.Line();
			line11.X1 = num6;
			line11.Y1 = num47;
			line11.X2 = nodeX;
			line11.Y2 = num47;
			line11.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line11.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element40 = line11;
			canvas.Children.Add(element40);
			System.Windows.Shapes.Ellipse ellipse2 = new System.Windows.Shapes.Ellipse();
			ellipse2.Width = 6.0;
			ellipse2.Height = 6.0;
			ellipse2.Stroke = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			ellipse2.StrokeThickness = 1.2;
			ellipse2.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element41 = ellipse2;
			Canvas.SetLeft(element41, num48 - 3.0);
			Canvas.SetTop(element41, num47 - 3.0);
			canvas.Children.Add(element41);
			System.Windows.Shapes.Line line12 = new System.Windows.Shapes.Line();
			line12.X1 = num48;
			line12.Y1 = num47;
			line12.X2 = num48;
			line12.Y2 = num42 + 41.0;
			line12.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line12.StrokeThickness = 1.2;
			System.Windows.Shapes.Line element42 = line12;
			canvas.Children.Add(element42);
			Border border6 = new Border();
			border6.Width = 18.0;
			border6.Height = 16.0;
			border6.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
			border6.BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
			border6.BorderThickness = new Thickness(1.2);
			border6.CornerRadius = new CornerRadius(2.0);
			Border border7 = border6;
			border7.Child = new TextBlock
			{
				Text = "PM",
				FontWeight = FontWeights.Bold,
				FontSize = 8.0,
				Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			Canvas.SetLeft(border7, num48 - 9.0);
			Canvas.SetTop(border7, num42 + 25.0);
			canvas.Children.Add(border7);
			System.Windows.Shapes.Ellipse ellipse3 = new System.Windows.Shapes.Ellipse();
			ellipse3.Width = 4.0;
			ellipse3.Height = 4.0;
			ellipse3.Fill = new SolidColorBrush(Color.FromRgb(234, 179, 8));
			System.Windows.Shapes.Ellipse element43 = ellipse3;
			Canvas.SetLeft(element43, nodeX - 2.0);
			Canvas.SetTop(element43, num47 - 2.0);
			canvas.Children.Add(element43);
			SldInteractiveItem orCreateCustomItem3 = p.GetOrCreateCustomItem("MAIN_BREAKER", "Dao Nguồn Tổng (" + panel.PanelName + ")", "MainBreaker", 36.0, 24.0);
			orCreateCustomItem3.BaseX = baseX;
			orCreateCustomItem3.BaseY = num47;
			if (!orCreateCustomItem3.IsDeleted)
			{
				double num49 = orCreateCustomItem3.BaseX + orCreateCustomItem3.OffsetX;
				double num50 = orCreateCustomItem3.BaseY + orCreateCustomItem3.OffsetY;
				DrawOrthogonalWireH(nodeX, num47, num49 - 14.0 * orCreateCustomItem3.Scale, num50, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 1.5);
				DrawInteractiveSouthKnifeSwitch(canvas, orCreateCustomItem3, true);
				DrawOrthogonalWireH(num49 + 14.0 * orCreateCustomItem3.Scale, num50, tableX, num47, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 1.5);
			}
			Polygon polygon3 = new Polygon();
			polygon3.Points = new PointCollection
			{
				new Point(tableX, num47 - 3.5),
				new Point(tableX - 5.0, num47),
				new Point(tableX, num47 + 3.5)
			};
			polygon3.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			polygon3.StrokeThickness = 1.2;
			Polygon element44 = polygon3;
			canvas.Children.Add(element44);
			System.Windows.Shapes.Line line13 = new System.Windows.Shapes.Line();
			line13.X1 = nodeX;
			line13.Y1 = num47;
			line13.X2 = nodeX;
			line13.Y2 = num42 + 62.0;
			line13.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line13.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element45 = line13;
			canvas.Children.Add(element45);
			Border border8 = new Border();
			border8.Width = 7.0;
			border8.Height = 14.0;
			border8.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
			border8.BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
			border8.BorderThickness = new Thickness(1.2);
			Border element46 = border8;
			Canvas.SetLeft(element46, nodeX - 3.5);
			Canvas.SetTop(element46, num42 + 48.0);
			canvas.Children.Add(element46);
			TextBlock textBlock22 = new TextBlock();
			textBlock22.Text = "FUSE";
			textBlock22.FontSize = 6.5;
			textBlock22.FontWeight = FontWeights.Bold;
			textBlock22.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
			textBlock22.LayoutTransform = new RotateTransform(-90.0);
			TextBlock element47 = textBlock22;
			Canvas.SetLeft(element47, nodeX - 12.0);
			Canvas.SetTop(element47, num42 + 49.0);
			canvas.Children.Add(element47);
			System.Windows.Shapes.Line line14 = new System.Windows.Shapes.Line();
			line14.X1 = nodeX;
			line14.Y1 = num42 + 48.0;
			line14.X2 = nodeX;
			line14.Y2 = num42 + 37.0;
			line14.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line14.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element48 = line14;
			canvas.Children.Add(element48);
			Action<double, Color, string> action4 = delegate(double yPos, Color clr, string lbl)
			{
				System.Windows.Shapes.Ellipse element56 = new System.Windows.Shapes.Ellipse
				{
					Width = 8.0,
					Height = 8.0,
					Stroke = new SolidColorBrush(clr),
					StrokeThickness = 1.2,
					Fill = new SolidColorBrush(Color.FromArgb(50, clr.R, clr.G, clr.B))
				};
				Canvas.SetLeft(element56, nodeX - 4.0);
				Canvas.SetTop(element56, yPos - 4.0);
				canvas.Children.Add(element56);
				System.Windows.Shapes.Line element57 = new System.Windows.Shapes.Line
				{
					X1 = nodeX - 2.5,
					Y1 = yPos - 2.5,
					X2 = nodeX + 2.5,
					Y2 = yPos + 2.5,
					Stroke = new SolidColorBrush(clr),
					StrokeThickness = 1.0
				};
				System.Windows.Shapes.Line element58 = new System.Windows.Shapes.Line
				{
					X1 = nodeX - 2.5,
					Y1 = yPos + 2.5,
					X2 = nodeX + 2.5,
					Y2 = yPos - 2.5,
					Stroke = new SolidColorBrush(clr),
					StrokeThickness = 1.0
				};
				canvas.Children.Add(element57);
				canvas.Children.Add(element58);
				TextBlock element59 = new TextBlock
				{
					Text = lbl,
					FontWeight = FontWeights.Bold,
					FontSize = 7.5,
					Foreground = new SolidColorBrush(clr)
				};
				Canvas.SetLeft(element59, nodeX - 11.0);
				Canvas.SetTop(element59, yPos - 5.5);
				canvas.Children.Add(element59);
			};
			System.Windows.Shapes.Line line15 = new System.Windows.Shapes.Line();
			line15.X1 = nodeX;
			line15.Y1 = num42 + 29.0;
			line15.X2 = nodeX;
			line15.Y2 = num42 + 15.0;
			line15.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line15.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element49 = line15;
			canvas.Children.Add(element49);
			System.Windows.Shapes.Line line16 = new System.Windows.Shapes.Line();
			line16.X1 = nodeX;
			line16.Y1 = num42 + 7.0;
			line16.X2 = nodeX;
			line16.Y2 = num42 - 1.0;
			line16.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line16.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element50 = line16;
			canvas.Children.Add(element50);
			action4(num42 + 33.0, Color.FromRgb(220, 38, 38), "R");
			action4(num42 + 11.0, Color.FromRgb(234, 179, 8), "Y");
			action4(num42 - 5.0, Color.FromRgb(37, 99, 235), "B");
			curSumY += 4.0;
			System.Windows.Shapes.Line line17 = new System.Windows.Shapes.Line();
			line17.X1 = colStarterX + 25.0;
			line17.Y1 = curSumY;
			line17.X2 = colStarterX + 25.0;
			line17.Y2 = curSumY + 8.0;
			line17.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line17.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element51 = line17;
			canvas.Children.Add(element51);
			System.Windows.Shapes.Line line18 = new System.Windows.Shapes.Line();
			line18.X1 = colStarterX + 15.0;
			line18.Y1 = curSumY + 8.0;
			line18.X2 = colStarterX + 35.0;
			line18.Y2 = curSumY + 8.0;
			line18.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line18.StrokeThickness = 2.0;
			System.Windows.Shapes.Line element52 = line18;
			canvas.Children.Add(element52);
			System.Windows.Shapes.Line line19 = new System.Windows.Shapes.Line();
			line19.X1 = colStarterX + 18.0;
			line19.Y1 = curSumY + 11.0;
			line19.X2 = colStarterX + 32.0;
			line19.Y2 = curSumY + 11.0;
			line19.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line19.StrokeThickness = 1.5;
			System.Windows.Shapes.Line element53 = line19;
			canvas.Children.Add(element53);
			System.Windows.Shapes.Line line20 = new System.Windows.Shapes.Line();
			line20.X1 = colStarterX + 21.0;
			line20.Y1 = curSumY + 14.0;
			line20.X2 = colStarterX + 29.0;
			line20.Y2 = curSumY + 14.0;
			line20.Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			line20.StrokeThickness = 1.0;
			System.Windows.Shapes.Line element54 = line20;
			canvas.Children.Add(element54);
			TextBlock textBlock23 = new TextBlock();
			textBlock23.Text = "⏚ PE";
			textBlock23.FontWeight = FontWeights.Bold;
			textBlock23.FontSize = 9.0;
			textBlock23.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
			TextBlock element55 = textBlock23;
			Canvas.SetLeft(element55, colStarterX + 40.0);
			Canvas.SetTop(element55, curSumY + 4.0);
			canvas.Children.Add(element55);
			if (p.SldCustomItems != null)
			{
				foreach (SldInteractiveItem sldCustomItem in p.SldCustomItems)
				{
					if (sldCustomItem.IsCopied && !sldCustomItem.IsDeleted)
					{
						DrawCopiedInteractiveItem(sldCustomItem);
					}
				}
			}
			UpdateGizmoPosition();
		}

		private void DrawCanvasBreakerSymbol(Canvas canvas, double cx, double cy, bool isMain, int phases, SymbolRegion region, bool isHorizontal)
		{
			double num = (isMain ? 12.0 : 8.0);
			if (region == SymbolRegion.North_Tcvn)
			{
				double num2 = (isHorizontal ? (num * 1.5) : num);
				double num3 = (isHorizontal ? num : (num * 1.5));
				Rectangle rectangle = new Rectangle();
				rectangle.Width = num2;
				rectangle.Height = num3;
				rectangle.Stroke = new SolidColorBrush(Color.FromRgb(22, 101, 52));
				rectangle.StrokeThickness = 1.4;
				rectangle.Fill = new SolidColorBrush(Color.FromRgb(240, 253, 244));
				Rectangle element = rectangle;
				Canvas.SetLeft(element, cx - num2 / 2.0);
				Canvas.SetTop(element, cy - num3 / 2.0);
				canvas.Children.Add(element);
				System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
				line.X1 = cx - num2 / 2.0;
				line.Y1 = cy - num3 / 2.0;
				line.X2 = cx + num2 / 2.0;
				line.Y2 = cy + num3 / 2.0;
				line.Stroke = new SolidColorBrush(Color.FromRgb(22, 101, 52));
				line.StrokeThickness = 1.2;
				System.Windows.Shapes.Line element2 = line;
				canvas.Children.Add(element2);
				string text = (isMain ? string.Format("Ap {0}P", phases) : string.Format("MCB {0}P", phases));
				TextBlock textBlock = new TextBlock();
				textBlock.Text = text;
				textBlock.FontSize = 8.0;
				textBlock.FontWeight = FontWeights.Bold;
				textBlock.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
				TextBlock element3 = textBlock;
				if (isHorizontal)
				{
					Canvas.SetLeft(element3, cx - 12.0);
					Canvas.SetTop(element3, cy - num3 / 2.0 - 12.0);
				}
				else
				{
					Canvas.SetLeft(element3, cx + num2 / 2.0 + 3.0);
					Canvas.SetTop(element3, cy - 6.0);
				}
				canvas.Children.Add(element3);
			}
			else if (!isHorizontal)
			{
				System.Windows.Shapes.Line line2 = new System.Windows.Shapes.Line();
				line2.X1 = cx;
				line2.Y1 = cy + num * 0.5;
				line2.X2 = cx + num * 0.7;
				line2.Y2 = cy - num * 0.4;
				line2.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line2.StrokeThickness = 1.5;
				System.Windows.Shapes.Line element4 = line2;
				canvas.Children.Add(element4);
				System.Windows.Shapes.Line line3 = new System.Windows.Shapes.Line();
				line3.X1 = cx + num * 0.2;
				line3.Y1 = cy - num * 0.1;
				line3.X2 = cx + num * 0.6;
				line3.Y2 = cy - num * 0.5;
				line3.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line3.StrokeThickness = 1.2;
				System.Windows.Shapes.Line element5 = line3;
				canvas.Children.Add(element5);
				string text2 = (isMain ? string.Format("MCCB {0}P\nI>", phases) : string.Format("MCB {0}P\nI>", phases));
				TextBlock textBlock2 = new TextBlock();
				textBlock2.Text = text2;
				textBlock2.FontSize = 7.5;
				textBlock2.FontWeight = FontWeights.Bold;
				textBlock2.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				TextBlock element6 = textBlock2;
				Canvas.SetLeft(element6, cx + num * 0.8 + 2.0);
				Canvas.SetTop(element6, cy - 8.0);
				canvas.Children.Add(element6);
			}
			else
			{
				System.Windows.Shapes.Line line4 = new System.Windows.Shapes.Line();
				line4.X1 = cx - num * 0.4;
				line4.Y1 = cy;
				line4.X2 = cx + num * 0.5;
				line4.Y2 = cy - num * 0.7;
				line4.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line4.StrokeThickness = 1.5;
				System.Windows.Shapes.Line element7 = line4;
				canvas.Children.Add(element7);
				string text3 = (isMain ? string.Format("MCCB {0}P (I>)", phases) : string.Format("MCB {0}P (I>)", phases));
				TextBlock textBlock3 = new TextBlock();
				textBlock3.Text = text3;
				textBlock3.FontSize = 7.5;
				textBlock3.FontWeight = FontWeights.Bold;
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				TextBlock element8 = textBlock3;
				Canvas.SetLeft(element8, cx - 15.0);
				Canvas.SetTop(element8, cy - num * 0.7 - 12.0);
				canvas.Children.Add(element8);
			}
		}

		private void DrawOrthogonalWire(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
		{
			if (Math.Abs(x1 - x2) < 1.0)
			{
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = x1,
					Y1 = y1,
					X2 = x2,
					Y2 = y2,
					Stroke = stroke,
					StrokeThickness = thickness
				});
				return;
			}
			if (Math.Abs(y1 - y2) < 1.0)
			{
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = x1,
					Y1 = y1,
					X2 = x2,
					Y2 = y1,
					Stroke = stroke,
					StrokeThickness = thickness
				});
				return;
			}
			double num = (y1 + y2) / 2.0;
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = x1,
				Y1 = y1,
				X2 = x1,
				Y2 = num,
				Stroke = stroke,
				StrokeThickness = thickness
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = x1,
				Y1 = num,
				X2 = x2,
				Y2 = num,
				Stroke = stroke,
				StrokeThickness = thickness
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = x2,
				Y1 = num,
				X2 = x2,
				Y2 = y2,
				Stroke = stroke,
				StrokeThickness = thickness
			});
		}

		private void DrawOrthogonalWireH(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
		{
			if (Math.Abs(x1 - x2) < 1.0)
			{
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = x1,
					Y1 = y1,
					X2 = x2,
					Y2 = y2,
					Stroke = stroke,
					StrokeThickness = thickness
				});
				return;
			}
			if (Math.Abs(y1 - y2) < 1.0)
			{
				previewCanvas.Children.Add(new System.Windows.Shapes.Line
				{
					X1 = x1,
					Y1 = y1,
					X2 = x2,
					Y2 = y1,
					Stroke = stroke,
					StrokeThickness = thickness
				});
				return;
			}
			double num = (x1 + x2) / 2.0;
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = x1,
				Y1 = y1,
				X2 = num,
				Y2 = y1,
				Stroke = stroke,
				StrokeThickness = thickness
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num,
				Y1 = y1,
				X2 = num,
				Y2 = y2,
				Stroke = stroke,
				StrokeThickness = thickness
			});
			previewCanvas.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num,
				Y1 = y2,
				X2 = x2,
				Y2 = y2,
				Stroke = stroke,
				StrokeThickness = thickness
			});
		}

		private void DrawInteractiveTreeBreaker(Canvas canvas, SldInteractiveItem item, bool isMain, int phases, SymbolRegion region, bool isHorizontal)
		{
			double num = (isHorizontal ? 42.0 : 36.0);
			double num2 = (isHorizontal ? 36.0 : 42.0);
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas canvas2 = CreateInteractiveItemContainer(item, num, num2);
			double num5 = (isMain ? 12.0 : 8.0);
			if (region == SymbolRegion.North_Tcvn)
			{
				double num6 = (isHorizontal ? (num5 * 1.5) : num5);
				double num7 = (isHorizontal ? num5 : (num5 * 1.5));
				Rectangle rectangle = new Rectangle();
				rectangle.Width = num6;
				rectangle.Height = num7;
				rectangle.Stroke = new SolidColorBrush(Color.FromRgb(22, 101, 52));
				rectangle.StrokeThickness = 1.4;
				rectangle.Fill = new SolidColorBrush(Color.FromRgb(240, 253, 244));
				Rectangle element = rectangle;
				Canvas.SetLeft(element, num3 - num6 / 2.0);
				Canvas.SetTop(element, num4 - num7 / 2.0);
				canvas2.Children.Add(element);
				System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
				line.X1 = num3 - num6 / 2.0;
				line.Y1 = num4 - num7 / 2.0;
				line.X2 = num3 + num6 / 2.0;
				line.Y2 = num4 + num7 / 2.0;
				line.Stroke = new SolidColorBrush(Color.FromRgb(22, 101, 52));
				line.StrokeThickness = 1.2;
				System.Windows.Shapes.Line element2 = line;
				canvas2.Children.Add(element2);
				string text = (isMain ? string.Format("Ap {0}P", phases) : string.Format("MCB {0}P", phases));
				TextBlock textBlock = new TextBlock();
				textBlock.Text = text;
				textBlock.FontSize = 7.5;
				textBlock.FontWeight = FontWeights.Bold;
				textBlock.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
				TextBlock element3 = textBlock;
				if (isHorizontal)
				{
					Canvas.SetLeft(element3, num3 - 12.0);
					Canvas.SetTop(element3, num4 - num7 / 2.0 - 12.0);
				}
				else
				{
					Canvas.SetLeft(element3, num3 + num6 / 2.0 + 2.0);
					Canvas.SetTop(element3, num4 - 6.0);
				}
				canvas2.Children.Add(element3);
			}
			else if (!isHorizontal)
			{
				System.Windows.Shapes.Line line2 = new System.Windows.Shapes.Line();
				line2.X1 = num3;
				line2.Y1 = num4 + num5 * 0.5;
				line2.X2 = num3 + num5 * 0.7;
				line2.Y2 = num4 - num5 * 0.4;
				line2.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line2.StrokeThickness = 1.5;
				System.Windows.Shapes.Line element4 = line2;
				canvas2.Children.Add(element4);
				System.Windows.Shapes.Line line3 = new System.Windows.Shapes.Line();
				line3.X1 = num3 + num5 * 0.2;
				line3.Y1 = num4 - num5 * 0.1;
				line3.X2 = num3 + num5 * 0.6;
				line3.Y2 = num4 - num5 * 0.5;
				line3.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line3.StrokeThickness = 1.2;
				System.Windows.Shapes.Line element5 = line3;
				canvas2.Children.Add(element5);
				System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
				ellipse.Width = 3.0;
				ellipse.Height = 3.0;
				ellipse.Fill = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				System.Windows.Shapes.Ellipse element6 = ellipse;
				Canvas.SetLeft(element6, num3 - 1.5);
				Canvas.SetTop(element6, num4 + num5 * 0.5 - 1.5);
				canvas2.Children.Add(element6);
				string text2 = (isMain ? string.Format("MCCB {0}P", phases) : string.Format("MCB {0}P", phases));
				TextBlock textBlock2 = new TextBlock();
				textBlock2.Text = text2;
				textBlock2.FontSize = 7.5;
				textBlock2.FontWeight = FontWeights.Bold;
				textBlock2.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				TextBlock element7 = textBlock2;
				Canvas.SetLeft(element7, num3 + num5 * 0.8);
				Canvas.SetTop(element7, num4 - 5.0);
				canvas2.Children.Add(element7);
			}
			else
			{
				System.Windows.Shapes.Line line4 = new System.Windows.Shapes.Line();
				line4.X1 = num3 - num5 * 0.5;
				line4.Y1 = num4;
				line4.X2 = num3 + num5 * 0.4;
				line4.Y2 = num4 - num5 * 0.7;
				line4.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line4.StrokeThickness = 1.5;
				System.Windows.Shapes.Line element8 = line4;
				canvas2.Children.Add(element8);
				System.Windows.Shapes.Line line5 = new System.Windows.Shapes.Line();
				line5.X1 = num3 - num5 * 0.1;
				line5.Y1 = num4 - num5 * 0.2;
				line5.X2 = num3 - num5 * 0.5;
				line5.Y2 = num4 - num5 * 0.6;
				line5.Stroke = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				line5.StrokeThickness = 1.2;
				System.Windows.Shapes.Line element9 = line5;
				canvas2.Children.Add(element9);
				System.Windows.Shapes.Ellipse ellipse2 = new System.Windows.Shapes.Ellipse();
				ellipse2.Width = 3.0;
				ellipse2.Height = 3.0;
				ellipse2.Fill = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				System.Windows.Shapes.Ellipse element10 = ellipse2;
				Canvas.SetLeft(element10, num3 - num5 * 0.5 - 1.5);
				Canvas.SetTop(element10, num4 - 1.5);
				canvas2.Children.Add(element10);
				string text3 = (isMain ? string.Format("MCCB {0}P", phases) : string.Format("MCB {0}P", phases));
				TextBlock textBlock3 = new TextBlock();
				textBlock3.Text = text3;
				textBlock3.FontSize = 7.5;
				textBlock3.FontWeight = FontWeights.Bold;
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
				TextBlock element11 = textBlock3;
				Canvas.SetLeft(element11, num3 - 12.0);
				Canvas.SetTop(element11, num4 - num5 * 0.8 - 10.0);
				canvas2.Children.Add(element11);
			}
			if (item.IsSelected)
			{
				_selectedSldContainer = canvas2;
			}
			canvas.Children.Add(canvas2);
		}

		private void DrawInteractiveSouthKnifeSwitch(Canvas canvas, SldInteractiveItem item, bool flipped)
		{
			double num = 36.0;
			double num2 = 24.0;
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas canvas2 = CreateInteractiveItemContainer(item, num, num2);
			System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
			ellipse.Width = 3.5;
			ellipse.Height = 3.5;
			ellipse.Stroke = new SolidColorBrush(Color.FromRgb(217, 119, 6));
			ellipse.StrokeThickness = 1.0;
			ellipse.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element = ellipse;
			Canvas.SetLeft(element, num3 - 4.5);
			Canvas.SetTop(element, num4 - 1.75);
			canvas2.Children.Add(element);
			System.Windows.Shapes.Ellipse ellipse2 = new System.Windows.Shapes.Ellipse();
			ellipse2.Width = 3.5;
			ellipse2.Height = 3.5;
			ellipse2.Stroke = new SolidColorBrush(Color.FromRgb(217, 119, 6));
			ellipse2.StrokeThickness = 1.0;
			ellipse2.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element2 = ellipse2;
			Canvas.SetLeft(element2, num3 + 4.5);
			Canvas.SetTop(element2, num4 - 1.75);
			canvas2.Children.Add(element2);
			if (!flipped)
			{
				System.Windows.Shapes.Line line = new System.Windows.Shapes.Line();
				line.X1 = num3 + 4.5;
				line.Y1 = num4;
				line.X2 = num3 - 3.5;
				line.Y2 = num4 - 4.5;
				line.Stroke = new SolidColorBrush(Color.FromRgb(234, 179, 8));
				line.StrokeThickness = 2.0;
				System.Windows.Shapes.Line element3 = line;
				canvas2.Children.Add(element3);
			}
			else
			{
				System.Windows.Shapes.Line line2 = new System.Windows.Shapes.Line();
				line2.X1 = num3 - 4.5;
				line2.Y1 = num4;
				line2.X2 = num3 + 3.5;
				line2.Y2 = num4 - 4.5;
				line2.Stroke = new SolidColorBrush(Color.FromRgb(234, 179, 8));
				line2.StrokeThickness = 2.0;
				System.Windows.Shapes.Line element4 = line2;
				canvas2.Children.Add(element4);
			}
			if (item.IsSelected)
			{
				_selectedSldContainer = canvas2;
			}
			canvas.Children.Add(canvas2);
		}

		private Canvas CreateInteractiveItemContainer(SldInteractiveItem item, double w, double h)
		{
			Canvas container = new Canvas
			{
				Width = w,
				Height = h,
				Background = Brushes.Transparent,
				Cursor = Cursors.SizeAll,
				ClipToBounds = false,
				Tag = item
			};
			Canvas.SetLeft(container, item.BaseX + item.OffsetX - w / 2.0);
			Canvas.SetTop(container, item.BaseY + item.OffsetY - h / 2.0);
			TransformGroup transformGroup = new TransformGroup();
			transformGroup.Children.Add(new RotateTransform(item.RotationAngle, w / 2.0, h / 2.0));
			transformGroup.Children.Add(new ScaleTransform(item.Scale, item.Scale, w / 2.0, h / 2.0));
			container.RenderTransform = transformGroup;
			container.ToolTip = string.Format("[{0}]\n• Kéo chuột: Di chuyển (Move)\n• Phím R: Xoay 90° (Rotate)\n• Phím Ctrl+C: Sao chép (Copy)\n• Phím +/-: Thu phóng (Scale)\n• Phím Del: Đặt lại / Xóa\n• Phím Mũi tên: Vi chỉnh 5px", item.DisplayName);
			container.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				SelectSldItem(item, container);
				_isDraggingSldItem = true;
				_dragStartMouse = e.GetPosition(previewCanvas);
				_dragStartOffsetX = item.OffsetX;
				_dragStartOffsetY = item.OffsetY;
				container.CaptureMouse();
				e.Handled = true;
			};
			container.MouseMove += delegate(object s, MouseEventArgs e)
			{
				if (_isDraggingSldItem && container.IsMouseCaptured)
				{
					Point position = e.GetPosition(previewCanvas);
					item.OffsetX = _dragStartOffsetX + (position.X - _dragStartMouse.X);
					item.OffsetY = _dragStartOffsetY + (position.Y - _dragStartMouse.Y);
					Canvas.SetLeft(container, item.BaseX + item.OffsetX - w / 2.0);
					Canvas.SetTop(container, item.BaseY + item.OffsetY - h / 2.0);
					UpdateGizmoPosition();
				}
			};
			container.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
			{
				if (_isDraggingSldItem)
				{
					_isDraggingSldItem = false;
					container.ReleaseMouseCapture();
					DrawCanvasPreview();
					e.Handled = true;
				}
			};
			return container;
		}

		private void DrawInteractiveBreaker(SldInteractiveItem item, string spec, bool isMain, Brush textBrush = null)
		{
			double num = 36.0;
			double num2 = 36.0;
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas canvas = CreateInteractiveItemContainer(item, num, num2);
			System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
			ellipse.Width = 5.0;
			ellipse.Height = 5.0;
			ellipse.Stroke = Brushes.Black;
			ellipse.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element = ellipse;
			System.Windows.Shapes.Ellipse ellipse2 = new System.Windows.Shapes.Ellipse();
			ellipse2.Width = 5.0;
			ellipse2.Height = 5.0;
			ellipse2.Stroke = Brushes.Black;
			ellipse2.Fill = Brushes.White;
			System.Windows.Shapes.Ellipse element2 = ellipse2;
			Canvas.SetLeft(element, num3 - 2.5);
			Canvas.SetTop(element, num4 - 11.0);
			Canvas.SetLeft(element2, num3 - 2.5);
			Canvas.SetTop(element2, num4 + 6.0);
			canvas.Children.Add(element);
			canvas.Children.Add(element2);
			System.Windows.Shapes.Path path = new System.Windows.Shapes.Path();
			path.Stroke = Brushes.Black;
			path.StrokeThickness = 1.2;
			System.Windows.Shapes.Path path2 = path;
			PathGeometry pathGeometry = new PathGeometry();
			PathFigure pathFigure = new PathFigure();
			pathFigure.StartPoint = new Point(num3, num4 - 8.5);
			PathFigure pathFigure2 = pathFigure;
			pathFigure2.Segments.Add(new ArcSegment(new Point(num3, num4 + 8.5), new Size(7.0, 10.0), 0.0, false, SweepDirection.Counterclockwise, true));
			pathGeometry.Figures.Add(pathFigure2);
			path2.Data = pathGeometry;
			canvas.Children.Add(path2);
			if (string.IsNullOrEmpty(spec))
			{
				spec = (isMain ? "MCCB 3P" : "MCB 1P");
			}
			if (!string.IsNullOrEmpty(spec) && (spec.ToUpper().Contains("RCCB") || spec.ToUpper().Contains("RCBO")) && textBrush == null)
			{
				textBrush = Brushes.DarkOrange;
			}
			string[] array = spec.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string text = ((array.Length > 0) ? array[0] : (isMain ? "MCCB" : "MCB"));
			string text2 = ((array.Length > 1) ? array[1] : "");
			string text3 = ((array.Length > 2) ? array[2] : "");
			double length = (isMain ? (num3 - 24.0) : (num3 + 10.0));
			TextBlock textBlock = new TextBlock();
			textBlock.Text = text;
			textBlock.FontSize = 7.5;
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.Foreground = textBrush ?? Brushes.Black;
			TextBlock element3 = textBlock;
			Canvas.SetLeft(element3, length);
			Canvas.SetTop(element3, num4 - 14.0);
			canvas.Children.Add(element3);
			if (!string.IsNullOrEmpty(text2))
			{
				TextBlock textBlock2 = new TextBlock();
				textBlock2.Text = text2;
				textBlock2.FontSize = 7.5;
				textBlock2.FontWeight = FontWeights.Bold;
				textBlock2.Foreground = textBrush ?? Brushes.Black;
				TextBlock element4 = textBlock2;
				Canvas.SetLeft(element4, length);
				Canvas.SetTop(element4, num4 - 4.0);
				canvas.Children.Add(element4);
			}
			if (!string.IsNullOrEmpty(text3))
			{
				TextBlock textBlock3 = new TextBlock();
				textBlock3.Text = text3;
				textBlock3.FontSize = 7.5;
				textBlock3.FontWeight = FontWeights.Bold;
				textBlock3.Foreground = textBrush ?? Brushes.Black;
				TextBlock element5 = textBlock3;
				Canvas.SetLeft(element5, length);
				Canvas.SetTop(element5, num4 + 6.0);
				canvas.Children.Add(element5);
			}
			if (item.IsSelected)
			{
				_selectedSldContainer = canvas;
			}
			previewCanvas.Children.Add(canvas);
		}

		private void DrawInteractiveFuse(SldInteractiveItem item)
		{
			double num = 34.0;
			double num2 = 22.0;
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas canvas = CreateInteractiveItemContainer(item, num, num2);
			Rectangle rectangle = new Rectangle();
			rectangle.Width = 24.0;
			rectangle.Height = 8.0;
			rectangle.Stroke = Brushes.DarkCyan;
			rectangle.StrokeThickness = 1.2;
			rectangle.Fill = Brushes.White;
			Rectangle element = rectangle;
			Canvas.SetLeft(element, num3 - 12.0);
			Canvas.SetTop(element, num4 - 4.0);
			canvas.Children.Add(element);
			TextBlock textBlock = new TextBlock();
			textBlock.Text = "3x2A FUSE";
			textBlock.FontSize = 7.5;
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.Foreground = Brushes.DarkCyan;
			TextBlock element2 = textBlock;
			Canvas.SetLeft(element2, num3 - 18.0);
			Canvas.SetTop(element2, num4 - 14.0);
			canvas.Children.Add(element2);
			if (item.IsSelected)
			{
				_selectedSldContainer = canvas;
			}
			previewCanvas.Children.Add(canvas);
		}

		private void DrawInteractiveLamps(SldInteractiveItem item)
		{
			double num = 56.0;
			double num2 = 26.0;
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas canvas = CreateInteractiveItemContainer(item, num, num2);
			Brush[] array = new Brush[3]
			{
				Brushes.Red,
				Brushes.Gold,
				Brushes.Blue
			};
			string[] array2 = new string[3] { "R", "Y", "B" };
			for (int i = 0; i < 3; i++)
			{
				double num5 = num3 - 22.0 + (double)i * 18.0;
				System.Windows.Shapes.Ellipse ellipse = new System.Windows.Shapes.Ellipse();
				ellipse.Width = 8.0;
				ellipse.Height = 8.0;
				ellipse.Stroke = Brushes.Black;
				ellipse.StrokeThickness = 1.0;
				ellipse.Fill = array[i];
				System.Windows.Shapes.Ellipse element = ellipse;
				Canvas.SetLeft(element, num5);
				Canvas.SetTop(element, num4 - 4.0);
				canvas.Children.Add(element);
				TextBlock textBlock = new TextBlock();
				textBlock.Text = array2[i];
				textBlock.FontSize = 7.5;
				textBlock.FontWeight = FontWeights.Bold;
				textBlock.Foreground = array[i];
				TextBlock element2 = textBlock;
				Canvas.SetLeft(element2, num5 + 1.5);
				Canvas.SetTop(element2, num4 - 14.0);
				canvas.Children.Add(element2);
				if (i < 2)
				{
					canvas.Children.Add(new System.Windows.Shapes.Line
					{
						X1 = num5 + 8.0,
						Y1 = num4,
						X2 = num5 + 18.0,
						Y2 = num4,
						Stroke = Brushes.Black,
						StrokeThickness = 1.0
					});
				}
			}
			if (item.IsSelected)
			{
				_selectedSldContainer = canvas;
			}
			previewCanvas.Children.Add(canvas);
		}

		private void DrawInteractiveRelay(SldInteractiveItem item)
		{
			double num = 34.0;
			double num2 = 24.0;
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas container = CreateInteractiveItemContainer(item, num, num2);
			Rectangle rectangle = new Rectangle();
			rectangle.Width = 26.0;
			rectangle.Height = 16.0;
			rectangle.Stroke = Brushes.Green;
			rectangle.StrokeThickness = 1.2;
			rectangle.Fill = Brushes.White;
			Rectangle element = rectangle;
			Canvas.SetLeft(element, num3 - 13.0);
			Canvas.SetTop(element, num4 - 8.0);
			container.Children.Add(element);
			container.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num3,
				Y1 = num4 - 8.0,
				X2 = num3,
				Y2 = num4 + 8.0,
				Stroke = Brushes.Green,
				StrokeThickness = 0.8
			});
			container.Children.Add(new System.Windows.Shapes.Line
			{
				X1 = num3 - 13.0,
				Y1 = num4,
				X2 = num3 + 13.0,
				Y2 = num4,
				Stroke = Brushes.Green,
				StrokeThickness = 0.8
			});
			Action<double, double, string> action = delegate(double tx, double ty, string str)
			{
				TextBlock element2 = new TextBlock
				{
					Text = str,
					FontSize = 6.5,
					FontWeight = FontWeights.Bold,
					Foreground = Brushes.Green
				};
				Canvas.SetLeft(element2, tx - 4.5);
				Canvas.SetTop(element2, ty - 4.5);
				container.Children.Add(element2);
			};
			action(num3 - 6.5, num4 - 4.0, "OV");
			action(num3 + 6.5, num4 - 4.0, "UV");
			action(num3 - 6.5, num4 + 4.0, "EF");
			action(num3 + 6.5, num4 + 4.0, "OC");
			if (item.IsSelected)
			{
				_selectedSldContainer = container;
			}
			previewCanvas.Children.Add(container);
		}

		private void DrawInteractivePM(SldInteractiveItem item)
		{
			double num = 34.0;
			double num2 = 28.0;
			double num3 = num / 2.0;
			double num4 = num2 / 2.0;
			Canvas canvas = CreateInteractiveItemContainer(item, num, num2);
			Rectangle rectangle = new Rectangle();
			rectangle.Width = 26.0;
			rectangle.Height = 20.0;
			rectangle.Stroke = Brushes.DarkCyan;
			rectangle.StrokeThickness = 1.2;
			rectangle.Fill = Brushes.White;
			Rectangle element = rectangle;
			Canvas.SetLeft(element, num3 - 13.0);
			Canvas.SetTop(element, num4 - 10.0);
			canvas.Children.Add(element);
			TextBlock textBlock = new TextBlock();
			textBlock.Text = "PM";
			textBlock.FontSize = 8.5;
			textBlock.FontWeight = FontWeights.Bold;
			textBlock.Foreground = Brushes.DarkCyan;
			TextBlock element2 = textBlock;
			Canvas.SetLeft(element2, num3 - 7.5);
			Canvas.SetTop(element2, num4 - 6.0);
			canvas.Children.Add(element2);
			if (item.IsSelected)
			{
				_selectedSldContainer = canvas;
			}
			previewCanvas.Children.Add(canvas);
		}

		private void DrawCopiedInteractiveItem(SldInteractiveItem cItem)
		{
			if (cItem.ItemType == "MainBreaker" || cItem.ItemType == "BranchBreaker")
			{
				if (_params != null && _params.LayoutScheme != 0)
				{
					if (_params.TreeOrientation == TreeOrientation.HorizontalLeftRight && (_params.SymbolRegion == SymbolRegion.South_Iec || _params.LayoutScheme == LayoutScheme.SouthRegion))
					{
						DrawInteractiveSouthKnifeSwitch(previewCanvas, cItem, false);
					}
					else
					{
						DrawInteractiveTreeBreaker(previewCanvas, cItem, false, 3, _params.SymbolRegion, _params.TreeOrientation == TreeOrientation.HorizontalLeftRight);
					}
				}
				else
				{
					DrawInteractiveBreaker(cItem, "MCB SAO CHÉP", false, Brushes.DarkMagenta);
				}
			}
			else if (cItem.ItemType == "Fuse")
			{
				DrawInteractiveFuse(cItem);
			}
			else if (cItem.ItemType == "Lamps")
			{
				DrawInteractiveLamps(cItem);
			}
			else if (cItem.ItemType == "Relay")
			{
				DrawInteractiveRelay(cItem);
			}
			else if (cItem.ItemType == "PM")
			{
				DrawInteractivePM(cItem);
			}
		}

		private void SelectSldItem(SldInteractiveItem item, Canvas container)
		{
			if (_selectedSldItem != null)
			{
				_selectedSldItem.IsSelected = false;
			}
			_selectedSldItem = item;
			_selectedSldItem.IsSelected = true;
			_selectedSldContainer = container;
			UpdateGizmoPosition();
		}

		private void DeselectSldItem()
		{
			if (_selectedSldItem != null)
			{
				_selectedSldItem.IsSelected = false;
				_selectedSldItem = null;
			}
			_selectedSldContainer = null;
			if (_sldSelectionBox != null)
			{
				_sldSelectionBox.Visibility = System.Windows.Visibility.Collapsed;
			}
			if (_sldGizmoToolbar != null)
			{
				_sldGizmoToolbar.Visibility = System.Windows.Visibility.Collapsed;
			}
		}

		private void UpdateGizmoPosition()
		{
			if (_selectedSldItem == null || _selectedSldContainer == null)
			{
				if (_sldSelectionBox != null)
				{
					_sldSelectionBox.Visibility = System.Windows.Visibility.Collapsed;
				}
				if (_sldGizmoToolbar != null)
				{
					_sldGizmoToolbar.Visibility = System.Windows.Visibility.Collapsed;
				}
				return;
			}
			double num = _selectedSldContainer.Width * _selectedSldItem.Scale;
			double num2 = _selectedSldContainer.Height * _selectedSldItem.Scale;
			double num3 = _selectedSldItem.BaseX + _selectedSldItem.OffsetX;
			double num4 = _selectedSldItem.BaseY + _selectedSldItem.OffsetY;
			double length = num3 - num / 2.0 - 4.0;
			double num5 = num4 - num2 / 2.0 - 4.0;
			if (_sldSelectionBox == null || !previewCanvas.Children.Contains(_sldSelectionBox))
			{
				if (_sldSelectionBox == null)
				{
					_sldSelectionBox = new Border
					{
						BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue)),
						BorderThickness = new Thickness(1.5),
						CornerRadius = new CornerRadius(4.0),
						Background = new SolidColorBrush(Color.FromArgb(24, 0, 229, byte.MaxValue)),
						IsHitTestVisible = false
					};
				}
				if (!previewCanvas.Children.Contains(_sldSelectionBox))
				{
					previewCanvas.Children.Add(_sldSelectionBox);
				}
			}
			_sldSelectionBox.Visibility = System.Windows.Visibility.Visible;
			_sldSelectionBox.Width = num + 8.0;
			_sldSelectionBox.Height = num2 + 8.0;
			Canvas.SetLeft(_sldSelectionBox, length);
			Canvas.SetTop(_sldSelectionBox, num5);
			_sldSelectionBox.RenderTransform = new RotateTransform(_selectedSldItem.RotationAngle, (num + 8.0) / 2.0, (num2 + 8.0) / 2.0);
			Panel.SetZIndex(_sldSelectionBox, 900);
			if (_sldGizmoToolbar == null || !previewCanvas.Children.Contains(_sldGizmoToolbar))
			{
				if (_sldGizmoToolbar == null)
				{
					_sldGizmoToolbar = CreateGizmoToolbarElement();
				}
				if (!previewCanvas.Children.Contains(_sldGizmoToolbar))
				{
					previewCanvas.Children.Add(_sldGizmoToolbar);
				}
			}
			_sldGizmoToolbar.Visibility = System.Windows.Visibility.Visible;
			Canvas.SetLeft(_sldGizmoToolbar, Math.Max(10.0, num3 - 180.0));
			Canvas.SetTop(_sldGizmoToolbar, Math.Max(5.0, num5 - 42.0));
			Panel.SetZIndex(_sldGizmoToolbar, 999);
			UpdateGizmoToolbarText();
		}

		private Border CreateGizmoToolbarElement()
		{
			Border border = new Border();
			border.Background = new SolidColorBrush(Color.FromRgb(30, 36, 51));
			border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue));
			border.BorderThickness = new Thickness(1.2);
			border.CornerRadius = new CornerRadius(6.0);
			border.Padding = new Thickness(4.0, 2.0, 4.0, 2.0);
			border.Effect = new DropShadowEffect
			{
				Color = Colors.Black,
				BlurRadius = 10.0,
				ShadowDepth = 2.0,
				Opacity = 0.5
			};
			Border border2 = border;
			StackPanel stackPanel = new StackPanel();
			stackPanel.Orientation = Orientation.Horizontal;
			StackPanel stackPanel2 = stackPanel;
			_txtGizmoInfo = new TextBlock
			{
				FontSize = 10.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Color.FromRgb(0, 229, byte.MaxValue)),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4.0, 0.0, 8.0, 0.0),
				FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif")
			};
			stackPanel2.Children.Add(_txtGizmoInfo);
			Func<string, string, RoutedEventHandler, Button> func = delegate(string content, string tip, RoutedEventHandler handler)
			{
				Button button = new Button
				{
					Content = content,
					ToolTip = tip,
					Margin = new Thickness(2.0, 0.0, 2.0, 0.0),
					Padding = new Thickness(5.0, 2.0, 5.0, 2.0),
					Background = new SolidColorBrush(Color.FromRgb(42, 50, 70)),
					Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
					BorderBrush = new SolidColorBrush(Color.FromRgb(51, 61, 84)),
					BorderThickness = new Thickness(1.0),
					FontSize = 10.5,
					FontWeight = FontWeights.Medium,
					Cursor = Cursors.Hand
				};
				button.Click += handler;
				return button;
			};
			stackPanel2.Children.Add(func("\ud83d\udd04 90°", "Xoay thiết bị 90 độ (Phím: R)", delegate
			{
				RotateSelectedSldItem();
			}));
			stackPanel2.Children.Add(func("\ud83d\udd0d +", "Phóng to thiết bị 10% (Phím: +)", delegate
			{
				ScaleSelectedSldItem(1.1);
			}));
			stackPanel2.Children.Add(func("\ud83d\udd0d -", "Thu nhỏ thiết bị 10% (Phím: -)", delegate
			{
				ScaleSelectedSldItem(0.9);
			}));
			stackPanel2.Children.Add(func("\ud83d\udccb Sao Chép", "Nhân bản thiết bị ra bản vẽ (Phím: Ctrl+C)", delegate
			{
				CopySelectedSldItem();
			}));
			stackPanel2.Children.Add(func("↺ Đặt Lại", "Khôi phục vị trí và kích thước gốc", delegate
			{
				ResetSelectedSldItem();
			}));
			stackPanel2.Children.Add(func("\ud83d\uddd1\ufe0f Xóa", "Xóa thiết bị (Phím: Del)", delegate
			{
				DeleteSelectedSldItem();
			}));
			stackPanel2.Children.Add(func("✕", "Bỏ chọn (Phím: ESC)", delegate
			{
				DeselectSldItem();
			}));
			border2.Child = stackPanel2;
			return border2;
		}

		private void UpdateGizmoToolbarText()
		{
			if (_txtGizmoInfo != null && _selectedSldItem != null)
			{
				_txtGizmoInfo.Text = string.Format("{0} | X:{1:+0;-0;0} Y:{2:+0;-0;0} | {3:0}° | {4:P0}", _selectedSldItem.DisplayName, _selectedSldItem.OffsetX, _selectedSldItem.OffsetY, _selectedSldItem.RotationAngle, _selectedSldItem.Scale);
			}
		}

		private void RotateSelectedSldItem()
		{
			if (_selectedSldItem != null)
			{
				_selectedSldItem.RotationAngle = (_selectedSldItem.RotationAngle + 90.0) % 360.0;
				DrawCanvasPreview();
			}
		}

		private void ScaleSelectedSldItem(double factor)
		{
			if (_selectedSldItem != null)
			{
				_selectedSldItem.Scale = Math.Max(0.5, Math.Min(3.0, _selectedSldItem.Scale * factor));
				DrawCanvasPreview();
			}
		}

		private void CopySelectedSldItem()
		{
			if (_selectedSldItem != null)
			{
				SldInteractiveItem sldInteractiveItem = _selectedSldItem.Clone();
				_params.SldCustomItems.Add(sldInteractiveItem);
				_selectedSldItem.IsSelected = false;
				_selectedSldItem = sldInteractiveItem;
				DrawCanvasPreview();
			}
		}

		private void ResetSelectedSldItem()
		{
			if (_selectedSldItem != null)
			{
				_selectedSldItem.Reset();
				DrawCanvasPreview();
			}
		}

		private void DeleteSelectedSldItem()
		{
			if (_selectedSldItem != null)
			{
				if (_selectedSldItem.IsCopied)
				{
					_params.SldCustomItems.Remove(_selectedSldItem);
				}
				else
				{
					_selectedSldItem.IsDeleted = true;
				}
				DeselectSldItem();
				DrawCanvasPreview();
			}
		}

		private void NudgeSelectedSldItem(double dx, double dy)
		{
			if (_selectedSldItem != null)
			{
				_selectedSldItem.OffsetX += dx;
				_selectedSldItem.OffsetY += dy;
				DrawCanvasPreview();
			}
		}

		private void BtnResetAllCustomItems_Click(object sender, RoutedEventArgs e)
		{
			if (_params != null)
			{
				_params.ResetAllCustomItems();
			}
			DeselectSldItem();
			DrawCanvasPreview();
		}

		private void PreviewCanvas_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.OriginalSource == previewCanvas || e.OriginalSource is Rectangle || e.OriginalSource is System.Windows.Shapes.Line)
			{
				DeselectSldItem();
			}
		}


		#region SDL V2 MCC Event Handlers
		public void BtnQuickSldV2_Click(object sender, RoutedEventArgs e)
		{
			if (tabSldV2Mcc != null)
			{
				tabSldV2Mcc.IsSelected = true;
			}
		}

		public void BtnApplySldV2Preset_Click(object sender, RoutedEventArgs e)
		{
			if (_params != null)
			{
				_params.LayoutScheme = LayoutScheme.SldV2Mcc;
				_params.DiagramType = SldDiagramType.TreeSinglePanel;
				_params.SymbolRegion = SymbolRegion.South_Iec;
				SetLayoutSchemeSelection(3);
			}
			BtnAutoAssignStarters_Click(sender, e);
			if (mainTabControl != null)
			{
				mainTabControl.SelectedIndex = 1; // Tab Preview
			}
			SafeDrawCanvasPreview();
		}

		public void BtnSwitchToSldV2Preview_Click(object sender, RoutedEventArgs e)
		{
			if (_params != null)
			{
				SetLayoutSchemeSelection(3);
			}
			if (mainTabControl != null)
			{
				mainTabControl.SelectedIndex = 1; // Tab Preview
			}
			SafeDrawCanvasPreview();
		}

		public void CboMccMainBreaker_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (_panel == null || cboMccMainBreaker == null || cboMccMainBreaker.SelectedItem == null) return;
			ComboBoxItem item = cboMccMainBreaker.SelectedItem as ComboBoxItem;
			if (item != null)
			{
				_panel.MainBreakerSpec = item.Content.ToString();
				SafeDrawCanvasPreview();
			}
		}

		public void MccParam_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (_panel == null) return;
			if (txtMccParentPanel != null && !string.IsNullOrEmpty(txtMccParentPanel.Text))
			{
				_panel.ParentPanelName = txtMccParentPanel.Text.Trim();
			}
			SafeDrawCanvasPreview();
		}

		public void MccOption_Changed(object sender, RoutedEventArgs e)
		{
			SafeDrawCanvasPreview();
		}

		public void BtnAutoAssignStarters_Click(object sender, RoutedEventArgs e)
		{
			if (_panel == null || _panel.Loads == null) return;
			foreach (var load in _panel.Loads)
			{
				if (load.PowerKW >= 15.0)
				{
					load.Starter = StarterType.StarDelta;
				}
				else if (load.LoadName != null && (load.LoadName.ToUpper().Contains("VFD") || load.LoadName.ToUpper().Contains("BIẾN TẦN") || load.LoadName.ToUpper().Contains("BIEN TAN")))
				{
					load.Starter = StarterType.VFD;
				}
				else
				{
					load.Starter = StarterType.DOL;
				}
			}
			if (dgMccStarters != null)
			{
				dgMccStarters.ItemsSource = null;
				dgMccStarters.ItemsSource = _panel.Loads;
			}
			if (dgLoads != null)
			{
				SafeRefreshDataGrid();
			}
			SafeDrawCanvasPreview();
		}

		public void BtnSetAllDol_Click(object sender, RoutedEventArgs e)
		{
			if (_panel == null || _panel.Loads == null) return;
			foreach (var load in _panel.Loads)
			{
				load.Starter = StarterType.DOL;
			}
			if (dgMccStarters != null)
			{
				dgMccStarters.ItemsSource = null;
				dgMccStarters.ItemsSource = _panel.Loads;
			}
			if (dgLoads != null)
			{
				SafeRefreshDataGrid();
			}
			SafeDrawCanvasPreview();
		}

		public void BtnSetAllSd_Click(object sender, RoutedEventArgs e)
		{
			if (_panel == null || _panel.Loads == null) return;
			foreach (var load in _panel.Loads)
			{
				load.Starter = StarterType.StarDelta;
			}
			if (dgMccStarters != null)
			{
				dgMccStarters.ItemsSource = null;
				dgMccStarters.ItemsSource = _panel.Loads;
			}
			if (dgLoads != null)
			{
				SafeRefreshDataGrid();
			}
			SafeDrawCanvasPreview();
		}

		public void DgMccStarterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			SafeDrawCanvasPreview();
		}
		#endregion

		private void BtnClose_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

	#region Preview Sơ Đồ Nguyên Lý SDNL V2 (Tủ Động Lực MCC / PCCC)
	private void RenderSldV2MccPreview(Canvas canvas, ElectricalPanel panel, DrawingParameters p, double pad, double canvasW, double canvasH, int branchCount)
	{
		int loadCount = (panel.Loads != null) ? panel.Loads.Count : 0;
		double actualW = Math.Max(1200.0, pad * 2.0 + 80.0 + branchCount * 110.0 + 160.0);
		double actualH = 880.0;
		canvas.Width = actualW;
		canvas.Height = actualH;

		System.Windows.Shapes.Rectangle borderOuter = new System.Windows.Shapes.Rectangle
		{
			Width = actualW - pad * 2.0,
			Height = actualH - pad * 2.0,
			Stroke = new SolidColorBrush(Color.FromRgb(30, 36, 51)),
			StrokeThickness = 2.0,
			Fill = new SolidColorBrush(Color.FromRgb(250, 252, 255))
		};
		Canvas.SetLeft(borderOuter, pad);
		Canvas.SetTop(borderOuter, pad);
		canvas.Children.Add(borderOuter);

		Border headerBadge = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(18, 22, 31)),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(14, 8, 14, 8)
		};
		StackPanel spHeader = new StackPanel { Orientation = Orientation.Horizontal };
		spHeader.Children.Add(new TextBlock
		{
			Text = "⚡ SDNL V2: TỦ ĐỘNG LỰC MCC / PCCC - TỦ " + panel.PanelName,
			FontWeight = FontWeights.Bold,
			FontSize = 13,
			Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255))
		});
		spHeader.Children.Add(new TextBlock
		{
			Text = "  |  CẤP NGUỒN TỪ DƯỚI LÊN (BOTTOM-TO-TOP)  |  S/D, DOL, VFD & LIÊN ĐỘNG",
			FontWeight = FontWeights.SemiBold,
			FontSize = 12,
			Foreground = new SolidColorBrush(Color.FromRgb(255, 145, 0))
		});
		headerBadge.Child = spHeader;
		Canvas.SetLeft(headerBadge, pad + 16.0);
		Canvas.SetTop(headerBadge, pad + 14.0);
		canvas.Children.Add(headerBadge);

		double leftMargin = 160.0;
		double branchPitch = 105.0;
		double busbarStartX = pad + leftMargin;
		double busbarEndX = busbarStartX + branchCount * branchPitch + 120.0;
		double busbarY = actualH - 280.0;

		System.Windows.Shapes.Line busbarLine = new System.Windows.Shapes.Line
		{
			X1 = busbarStartX - 10.0, Y1 = busbarY,
			X2 = busbarEndX, Y2 = busbarY,
			Stroke = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
			StrokeThickness = 6.0
		};
		canvas.Children.Add(busbarLine);

		string busbarRatingStr = (panel.TotalCurrentA > 0)
			? string.Format("4x CU BUSBAR {0:0}A", Math.Max(100, Math.Ceiling(panel.TotalCurrentA * 1.25 / 25.0) * 25.0))
			: "4x CU BUSBAR 125A";
		TextBlock txtBusbar = new TextBlock
		{
			Text = busbarRatingStr,
			FontWeight = FontWeights.Bold,
			FontSize = 10.5,
			Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9))
		};
		Canvas.SetLeft(txtBusbar, busbarStartX);
		Canvas.SetTop(txtBusbar, busbarY + 6.0);
		canvas.Children.Add(txtBusbar);

		int incomerIdx = Math.Min(3, Math.Max(1, branchCount / 2));
		double incomerX = busbarStartX + incomerIdx * branchPitch;
		double incomerBottomY = actualH - pad - 60.0;
		double mainBreakerY = incomerBottomY - 70.0;
		double meterY = mainBreakerY - 70.0;

		Border incBadge = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
			BorderThickness = new Thickness(1.5),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(8, 4, 8, 4)
		};
		string parentPanelStr = !string.IsNullOrEmpty(panel.ParentPanelName) ? panel.ParentPanelName : "MSB-COM";
		incBadge.Child = new TextBlock
		{
			Text = "▲ TỪ TỦ CHÍNH " + parentPanelStr + "\nFROM SWITCH BOARD " + parentPanelStr,
			FontSize = 9.5, FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(14, 116, 144)),
			TextAlignment = System.Windows.TextAlignment.Center
		};
		Canvas.SetLeft(incBadge, incomerX - 90.0);
		Canvas.SetTop(incBadge, incomerBottomY);
		canvas.Children.Add(incBadge);

		DrawOrthogonalWireH(incomerX, incomerBottomY, incomerX, mainBreakerY, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.5);

		Border mainCbBox = new Border
		{
			Width = 90.0, Height = 40.0,
			Background = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
			BorderThickness = new Thickness(1.5),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(4)
		};
		mainCbBox.Child = new TextBlock
		{
			Text = string.Format("MCCB 3P {0:0}A\n36kA", panel.TotalCurrentA > 0 ? Math.Ceiling(panel.TotalCurrentA * 1.25) : 125),
			FontSize = 9.5, FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
			TextAlignment = System.Windows.TextAlignment.Center
		};
		Canvas.SetLeft(mainCbBox, incomerX - 45.0);
		Canvas.SetTop(mainCbBox, mainBreakerY - 20.0);
		canvas.Children.Add(mainCbBox);

		DrawOrthogonalWireH(incomerX, mainBreakerY - 20.0, incomerX, meterY, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.5);

		Border meterBadge = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(240, 253, 250)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
			BorderThickness = new Thickness(1.2),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(6, 4, 6, 4)
		};
		meterBadge.Child = new TextBlock
		{
			Text = "3xMCT 125/5A  |  A/S  |  V/S 500V\nĐO LƯỜNG & BẢO VỆ",
			FontSize = 9, FontWeight = FontWeights.SemiBold,
			Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
			TextAlignment = System.Windows.TextAlignment.Center
		};
		Canvas.SetLeft(meterBadge, incomerX - 90.0);
		Canvas.SetTop(meterBadge, meterY - 18.0);
		canvas.Children.Add(meterBadge);

		DrawOrthogonalWireH(incomerX, meterY - 18.0, incomerX, busbarY, new SolidColorBrush(Color.FromRgb(2, 132, 199)), 2.5);

		List<double> sdStarterYList = new List<double>();
		List<double> sdBranchXList = new List<double>();

		for (int i = 0; i < loadCount; i++)
		{
			ElectricalLoad load = panel.Loads[i];
			double bX = busbarStartX + i * branchPitch + 20.0;
			string tag = !string.IsNullOrEmpty(load.CircuitNumber) ? load.CircuitNumber : ("P" + (i + 1));
			string loadType = DetectMccLoadTypePreview(load);

			System.Windows.Shapes.Ellipse dot = new System.Windows.Shapes.Ellipse
			{
				Width = 6.0, Height = 6.0, Fill = new SolidColorBrush(Color.FromRgb(180, 83, 9))
			};
			Canvas.SetLeft(dot, bX - 3.0); Canvas.SetTop(dot, busbarY - 3.0);
			canvas.Children.Add(dot);

			TextBlock txtTag = new TextBlock
			{
				Text = tag, FontSize = 10, FontWeight = FontWeights.Bold,
				Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
			};
			Canvas.SetLeft(txtTag, bX - 16.0); Canvas.SetTop(txtTag, busbarY - 22.0);
			canvas.Children.Add(txtTag);

			double cbY = busbarY - 60.0;
			DrawOrthogonalWireH(bX, busbarY, bX, cbY, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 2.0);

			int poles = (load.Phase == PhaseType.ThreePhase ? 3 : 1);
			double ratingA = Math.Max(16.0, Math.Ceiling(load.CurrentA * 1.25));
			Border cbBox = new Border
			{
				Width = 65.0, Height = 32.0,
				Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
				BorderThickness = new Thickness(1.2),
				CornerRadius = new CornerRadius(3),
				Padding = new Thickness(2)
			};
			cbBox.Child = new TextBlock
			{
				Text = string.Format("MCB {0}P\n{1:0}A", poles, ratingA),
				FontSize = 8.5, FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
				TextAlignment = System.Windows.TextAlignment.Center
			};
			Canvas.SetLeft(cbBox, bX - 32.5); Canvas.SetTop(cbBox, cbY - 16.0);
			canvas.Children.Add(cbBox);

			double starterY = cbY - 70.0;
			double motorY = starterY - 90.0;
			DrawOrthogonalWireH(bX, cbY - 16.0, bX, starterY, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 2.0);

			string starterLabel = (loadType == "STAR_DELTA") ? "S/D\n(x2)" :
								  (loadType == "DOL") ? "DOL" :
								  (loadType == "VFD") ? "VFD" :
								  (loadType == "DAMPER") ? "VAN\nMFD" :
								  (loadType == "SENSOR") ? "CO\nx5" : "DK";
			Border starterBox = new Border
			{
				Width = 50.0, Height = 32.0,
				Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
				BorderThickness = new Thickness(1.2),
				CornerRadius = new CornerRadius(3),
				Padding = new Thickness(2)
			};
			starterBox.Child = new TextBlock
			{
				Text = starterLabel, FontSize = 9, FontWeight = FontWeights.Bold,
				Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
				TextAlignment = System.Windows.TextAlignment.Center
			};
			Canvas.SetLeft(starterBox, bX - 25.0); Canvas.SetTop(starterBox, starterY - 16.0);
			canvas.Children.Add(starterBox);

			if (loadType == "STAR_DELTA")
			{
				sdStarterYList.Add(starterY);
				sdBranchXList.Add(bX);
			}

			DrawOrthogonalWireH(bX, starterY - 16.0, bX, motorY, new SolidColorBrush(Color.FromRgb(22, 163, 74)), 2.0);

			Border motorCircle = new Border
			{
				Width = 36.0, Height = 36.0,
				CornerRadius = new CornerRadius(18),
				Background = new SolidColorBrush(Color.FromRgb(240, 253, 244)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
				BorderThickness = new Thickness(1.5)
			};
			motorCircle.Child = new TextBlock
			{
				Text = (loadType == "DAMPER") ? "V" : ((loadType == "SENSOR") ? "CO" : "M"),
				FontWeight = FontWeights.Bold, FontSize = 13,
				Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
				HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
			};
			Canvas.SetLeft(motorCircle, bX - 18.0); Canvas.SetTop(motorCircle, motorY - 18.0);
			canvas.Children.Add(motorCircle);

			string cableStr = !string.IsNullOrEmpty(load.CableSpec) ? load.CableSpec : (load.Phase == PhaseType.ThreePhase ? "4x3C x 25mm2 Cu/XLPE" : "2x2.5mm2 Cu/PVC");
			string routeStr = "ĐI TRONG MÁNG CÁP / ỐNG GI";
			string devStr = string.Format("{0} ({1:0.##}kW)", load.LoadName, load.PowerKW);
			TextBlock txtVertical = new TextBlock
			{
				Text = string.Format("{0}\n{1}\n{2}", cableStr, routeStr, devStr),
				FontSize = 8.5, FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
				LayoutTransform = new RotateTransform(-90)
			};
			Canvas.SetLeft(txtVertical, bX + 6.0);
			Canvas.SetTop(txtVertical, motorY - 160.0);
			canvas.Children.Add(txtVertical);
		}

		if (sdBranchXList.Count > 0)
		{
			double signalLeftX = pad + 15.0;
			double maxSdX = sdBranchXList[sdBranchXList.Count - 1] + 25.0;
			double ySig = sdStarterYList[0];

			Border sigBox = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(254, 243, 199)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(3),
				Padding = new Thickness(4)
			};
			sigBox.Child = new TextBlock
			{
				Text = "• TỪ MIMIC PANEL\n• TÍN HIỆU FACP BÁO CHÁY\n• CẢM BIẾN CO ĐẾN QUẠT",
				FontSize = 8, FontWeight = FontWeights.Bold,
				Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9))
			};
			Canvas.SetLeft(sigBox, signalLeftX); Canvas.SetTop(sigBox, ySig - 25.0);
			canvas.Children.Add(sigBox);

			System.Windows.Shapes.Line sigLine = new System.Windows.Shapes.Line
			{
				X1 = signalLeftX + 115.0, Y1 = ySig,
				X2 = maxSdX, Y2 = ySig,
				Stroke = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
				StrokeThickness = 1.5,
				StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 3 }
			};
			canvas.Children.Add(sigLine);
		}

		double spareX = busbarEndX - 100.0;
		Border spareBox = new Border
		{
			Width = 90.0, Height = 40.0,
			Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Padding = new Thickness(2)
		};
		spareBox.Child = new TextBlock
		{
			Text = "20% SPARE SPACE\nDỰ PHÒNG TỦ",
			FontSize = 8.5, FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.Gray, TextAlignment = System.Windows.TextAlignment.Center
		};
		Canvas.SetLeft(spareBox, spareX); Canvas.SetTop(spareBox, busbarY - 50.0);
		canvas.Children.Add(spareBox);

		TextBlock txtPE = new TextBlock
		{
			Text = "⏚ E (TIẾP ĐỊA)", FontSize = 10, FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74))
		};
		Canvas.SetLeft(txtPE, busbarEndX - 80.0); Canvas.SetTop(txtPE, busbarY + 25.0);
		canvas.Children.Add(txtPE);

		Border bottomCard = new Border
		{
			Width = actualW - pad * 2.0 - 40.0, Height = 36.0,
			Background = new SolidColorBrush(Color.FromRgb(18, 22, 31)),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(12, 6, 12, 6)
		};
		DockPanel dpBottom = new DockPanel();
		TextBlock txtPanelInfo = new TextBlock
		{
			Text = "TỦ: " + panel.PanelName + "  |  ĐẶT TẠI: PHÒNG QUẠT THẢI GIÓ - KHOANG 2 - TẦNG HẦM 2 (BASEMENT 2)",
			FontWeight = FontWeights.Bold, FontSize = 11,
			Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock txtCableInfo = new TextBlock
		{
			Text = "CÁP ( XEM BẢN VẼ HỆ THỐNG ĐIỆN ) / CABLE ( REFER DRAWING )",
			FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
			VerticalAlignment = VerticalAlignment.Center
		};
		DockPanel.SetDock(txtCableInfo, Dock.Right);
		dpBottom.Children.Add(txtCableInfo);
		dpBottom.Children.Add(txtPanelInfo);
		bottomCard.Child = dpBottom;

		Canvas.SetLeft(bottomCard, pad + 20.0);
		Canvas.SetTop(bottomCard, actualH - pad - 42.0);
		canvas.Children.Add(bottomCard);
	}

	private static string DetectMccLoadTypePreview(ElectricalLoad load)
	{
		if (load == null) return "NORMAL";
		string name = (load.LoadName ?? "").ToUpper();
		if (load.Starter == StarterType.StarDelta || name.Contains("S/D") || name.Contains("SAO") || (load.Phase == PhaseType.ThreePhase && load.PowerKW >= 15.0))
			return "STAR_DELTA";
		if (load.Starter == StarterType.DOL || name.Contains("DOL") || name.Contains("QUẠT") || name.Contains("QUAT") || name.Contains("FAN") || name.Contains("BƠM") || name.Contains("PUMP"))
			return "DOL";
		if (load.Starter == StarterType.VFD || load.Starter == StarterType.SoftStarter || name.Contains("VFD") || name.Contains("BIẾN TẦN") || name.Contains("BIEN TAN"))
			return "VFD";
		if (name.Contains("VAN") || name.Contains("MFD") || name.Contains("DAMPER"))
			return "DAMPER";
		if (name.Contains("CẢM BIẾN") || name.Contains("CAM BIEN") || name.Contains("SENSOR") || name.Contains("CO"))
			return "SENSOR";
		if (name.Contains("ĐIỀU KHIỂN") || name.Contains("DIEU KHIEN") || name.Contains("CONTROL") || name.Contains("DK"))
			return "CONTROL";
		if (name.Contains("DỰ PHÒNG") || name.Contains("DU PHONG") || name.Contains("SPARE") || name.Contains("SPACE"))
			return "SPARE";
		return "NORMAL";
	}
	#endregion
}
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TaiAiAddin.Models;
using TaiAiAddin.Services;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TaiAiAddin.Views
{
    public partial class CadLayoutProcessorDialog : Window
    {
        private Extents3d? _currentBounds = null;
        private ObservableCollection<LayerMappingItem> _layerMappings = new ObservableCollection<LayerMappingItem>();
        private ObservableCollection<SuspiciousEntityItem> _suspiciousItems = new ObservableCollection<SuspiciousEntityItem>();
        private ObservableCollection<CoPilotRoomItem> _roomItems = new ObservableCollection<CoPilotRoomItem>();
        private Dictionary<ObjectId, bool> _savedLayerStates = null;
        private Document _activeDoc = null;
        private List<ObjectId> _currentLiveSelectedIds = new List<ObjectId>();
        private LiveSelectionData _lastSelectionData = null;
        private TextFormatConfig _textConfig = new TextFormatConfig();

        public CadLayoutProcessorDialog()
        {
            InitializeComponent();
            dgLayerMapping.ItemsSource = _layerMappings;
            dgSuspicious.ItemsSource = _suspiciousItems;
            dgRoomResults.ItemsSource = _roomItems;
            if (dgAiRoomResults != null)
            {
                dgAiRoomResults.ItemsSource = _roomItems;
            }

            cboQuickBatchLayer.ItemsSource = new List<string>
            {
                "AI_XD_TUONG",
                "AI_XD_COT",
                "AI_XD_CUA",
                "AI_XD_VACH_KINH",
                "AI_XD_NOI_THAT",
                "AI_XD_DIM_TEXT",
                "AI_XD_GRID",
                "AI_XD_RAC",
                "-- Bỏ Qua (Không Đổi) --"
            };
            cboQuickBatchLayer.SelectedIndex = 0;

            var targetLayers = new List<string>
            {
                "AI_XD_CUA",
                "AI_XD_TUONG",
                "AI_XD_COT",
                "AI_XD_VACH_KINH",
                "AI_XD_NOI_THAT",
                "AI_XD_DIM_TEXT",
                "AI_XD_GRID",
                "AI_XD_RAC"
            };

            if (cboLiveTargetLayer != null)
            {
                cboLiveTargetLayer.ItemsSource = targetLayers;
                cboLiveTargetLayer.SelectedIndex = 0;
            }

            if (cboAiLiveTargetLayer != null)
            {
                cboAiLiveTargetLayer.ItemsSource = targetLayers;
                cboAiLiveTargetLayer.SelectedIndex = 0;
            }

            // Sync 2 combobox giữa Tab 1 và Tab 4
            if (cboLiveTargetLayer != null && cboAiLiveTargetLayer != null)
            {
                cboLiveTargetLayer.SelectionChanged += delegate(object s, SelectionChangedEventArgs e)
                {
                    if (cboLiveTargetLayer.SelectedIndex >= 0 && cboAiLiveTargetLayer.SelectedIndex != cboLiveTargetLayer.SelectedIndex)
                    {
                        cboAiLiveTargetLayer.SelectedIndex = cboLiveTargetLayer.SelectedIndex;
                    }
                };

                cboAiLiveTargetLayer.SelectionChanged += delegate(object s, SelectionChangedEventArgs e)
                {
                    if (cboAiLiveTargetLayer.SelectedIndex >= 0 && cboLiveTargetLayer.SelectedIndex != cboAiLiveTargetLayer.SelectedIndex)
                    {
                        cboLiveTargetLayer.SelectedIndex = cboAiLiveTargetLayer.SelectedIndex;
                    }
                };
            }

            // Kích hoạt Zoom/Pan trực tiếp trên cả 2 màn hình Canvas
            if (cvsLiveSelectionPreview != null && stLivePreview != null && ttLivePreview != null)
            {
                SetupCanvasZoomPan(cvsLiveSelectionPreview, stLivePreview, ttLivePreview);
                cvsLiveSelectionPreview.SizeChanged += delegate(object s, SizeChangedEventArgs e)
                {
                    if (_lastSelectionData != null)
                    {
                        RenderLiveSelectionPreview(_lastSelectionData);
                    }
                };
            }

            if (cvsAiLiveSelectionPreview != null && stAiLivePreview != null && ttAiLivePreview != null)
            {
                SetupCanvasZoomPan(cvsAiLiveSelectionPreview, stAiLivePreview, ttAiLivePreview);
                cvsAiLiveSelectionPreview.SizeChanged += delegate(object s, SizeChangedEventArgs e)
                {
                    if (_lastSelectionData != null)
                    {
                        RenderLiveSelectionPreview(_lastSelectionData);
                    }
                };
            }

            Loaded += CadLayoutProcessorDialog_Loaded;
        }

        private void CadLayoutProcessorDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadWindowSettings();
            ReloadLayers();
            RegisterSelectionEvent();
        }

        private void ReloadLayers()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                // BẢO TỒN 100% HIỂN THỊ: Mở khóa và bật sáng toàn bộ layer bị ẩn
                CoPilotRoomBoundaryService.UnhideAllLayers(doc.Database);

                if (!_currentBounds.HasValue)
                {
                    _currentBounds = CadLayoutProcessorService.DetectExistingKhungBao(doc);
                }

                var modeInfo = LayerLearningService.AnalyzeDrawingLayerMode(doc, _currentBounds);
                var list = CadLayoutProcessorService.ScanLayers(doc, _currentBounds);
                _layerMappings.Clear();
                foreach (var item in list)
                {
                    _layerMappings.Add(item);
                }

                if (_currentBounds.HasValue)
                {
                    txtKhungBaoStatus.Text = string.Format("🔒 Phạm vi Khung Bao: X[{0:F1} → {1:F1}], Y[{2:F1} → {3:F1}] | {4}",
                        _currentBounds.Value.MinPoint.X, _currentBounds.Value.MaxPoint.X,
                        _currentBounds.Value.MinPoint.Y, _currentBounds.Value.MaxPoint.Y,
                        modeInfo.Description);
                }
                else
                {
                    txtKhungBaoStatus.Text = string.Format("⚠️ Chưa có Khung Bao | {0} ({1} layer)", modeInfo.Description, _layerMappings.Count);
                }

                int dictCnt = LayerLearningService.GetLearnedMappingCount();
                txtDialogStatus.Text = string.Format("Đã quét {0} layer lúc {1:HH:mm:ss} | {2} | Từ điển đã nhớ: {3} layer.",
                    _layerMappings.Count, DateTime.Now, modeInfo.Description, dictCnt);
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi khi quét layer: " + ex.Message;
            }
        }

        private void BtnPickKhungBao_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            this.Hide();

            try
            {
                Editor ed = doc.Editor;
                PromptPointOptions ppo1 = new PromptPointOptions("\n[AI] Chọn điểm góc thứ nhất của khung bao mặt bằng: ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status == PromptStatus.OK)
                {
                    PromptCornerOptions pco = new PromptCornerOptions("\n[AI] Chọn điểm góc đối diện của khung bao: ", ppr1.Value);
                    PromptPointResult ppr2 = ed.GetCorner(pco);
                    if (ppr2.Status == PromptStatus.OK)
                    {
                        _currentBounds = CadLayoutProcessorService.CreateKhungBao(doc, ppr1.Value, ppr2.Value);
                        ed.WriteMessage("\n[AI] Đã tạo khung bao mặt bằng thành công trên layer AI_XD_KHUNG_BAO (Color 6 Magenta)!\n");
                    }
                }
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[AI Lỗi] " + ex.Message);
            }
            finally
            {
                this.Show();
                this.Activate();
                ReloadLayers();
            }
        }

        private void BtnClearKhungBao_Click(object sender, RoutedEventArgs e)
        {
            _currentBounds = null;
            ReloadLayers();
            txtDialogStatus.Text = "Đã xóa khung bao. Phạm vi quét hiện là toàn bộ bản vẽ.";
        }

        private void BtnSelectAllLayers_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnselected = _layerMappings.Any(x => !x.IsSelected);
            foreach (var item in _layerMappings)
            {
                item.IsSelected = anyUnselected;
            }
        }

        private void BtnAutoMapLayers_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var modeInfo = LayerLearningService.AnalyzeDrawingLayerMode(doc, _currentBounds);
            if (!modeInfo.IsLayerZeroDrawing)
            {
                txtDialogStatus.Text = "🧠 Đang phân tích hình học & thống kê từng Layer có sẵn...";
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
                var geomItems = CadLayoutProcessorService.ScanSmartGeometryAndPatterns(doc, _currentBounds, false);
                LayerLearningService.RefineAndLearnLayers(doc, _currentBounds, geomItems, _layerMappings);
                int homogeneous = _layerMappings.Count(x => x.IsSelected && !x.IsMixedLayer);
                int mixed = _layerMappings.Count(x => x.IsMixedLayer);
                txtDialogStatus.Text = string.Format("🧠 Đã học xong theo Layer có sẵn: {0} Layer đồng nhất (≥75%), {1} Layer hỗn hợp (<75% sẽ tách từng nét).", homogeneous, mixed);
            }
            else
            {
                foreach (var item in _layerMappings)
                {
                    item.TargetLayer = CadLayoutProcessorService.SuggestAiLayer(item.SourceLayer);
                    item.IsSelected = !item.TargetLayer.StartsWith("--");
                }
                txtDialogStatus.Text = "📐 Bản vẽ chủ yếu là Layer 0 → hãy bấm '⚡ 1-Click' để chẩn đoán thuần hình học từng đối tượng.";
            }
        }

        private void BtnClearLearnedLayers_Click(object sender, RoutedEventArgs e)
        {
            int beforeLayers = LayerLearningService.GetLearnedMappingCount();
            int beforeShapes = ShapePatternLearningService.GetPatternCount();
            LayerLearningService.ClearLearnedMappings();
            ShapePatternLearningService.ClearPatterns();
            SetStep2OnlyMode(false);
            ReloadLayers();
            txtDialogStatus.Text = string.Format(
                "🧹 Đã xóa bộ nhớ học: {0} quy tắc Layer và {1} mẫu hình học phòng ví dụ.",
                beforeLayers, beforeShapes);
        }

        private bool _isSyncingStep2Check = false;
        private void SetStep2OnlyMode(bool enabled)
        {
            _isSyncingStep2Check = true;
            try
            {
                if (chkStep2PatternOnly != null) chkStep2PatternOnly.IsChecked = enabled;
                if (chkAiStep2PatternOnly != null) chkAiStep2PatternOnly.IsChecked = enabled;
            }
            finally
            {
                _isSyncingStep2Check = false;
            }
        }

        private void ChkStep2PatternOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncingStep2Check) return;
            var cb = sender as System.Windows.Controls.CheckBox;
            bool val = cb != null && cb.IsChecked == true;
            SetStep2OnlyMode(val);
        }

        private void BtnApplyLayerStandardization_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var itemsToMap = _layerMappings.Where(x => x.IsSelected && !x.IsMixedLayer && !string.IsNullOrEmpty(x.TargetLayer) && !x.TargetLayer.StartsWith("--")).ToList();
            if (itemsToMap.Count == 0)
            {
                MessageBox.Show("Không có Layer đồng nhất nào được chọn (Layer hỗn hợp < 75% hoặc Layer 0 cần dùng nút '⚡ 1-Click' để tách từng nét).", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int savedLearned = LayerLearningService.SaveConfirmedLayerMappings(itemsToMap);
            int purgedCount = 0;
            int count = CadLayoutProcessorService.ApplyLayerStandardization(doc, itemsToMap, _currentBounds, out purgedCount);
            MessageBox.Show(string.Format("Đã chuẩn hóa thành công {0} thực thể về hệ layer AI_*, Purge sạch {1} layer cũ và ghi nhớ {2} quy tắc Layer!", count, purgedCount, savedLearned), "Hoàn Tất", MessageBoxButton.OK, MessageBoxImage.Information);
            txtDialogStatus.Text = string.Format("Đã chuẩn hóa {0} thực thể, Purge {1} layer và lưu {2} quy tắc học Layer.", count, purgedCount, savedLearned);

            ReloadLayers();
        }

        private void RegisterSelectionEvent()
        {
            try
            {
                Application.DocumentManager.DocumentActivated += DocumentManager_DocumentActivated;
            }
            catch { }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                HookDocument(doc);
            }
        }

        private void DocumentManager_DocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != null)
            {
                HookDocument(e.Document);
                this.Dispatcher.BeginInvoke(new Action(delegate()
                {
                    ReloadLayers();
                    ClearLiveSelectionUI();
                }));
            }
        }

        private void HookDocument(Document doc)
        {
            if (_activeDoc != null && _activeDoc != doc)
            {
                try
                {
                    _activeDoc.ImpliedSelectionChanged -= Doc_ImpliedSelectionChanged;
                }
                catch { }
            }

            _activeDoc = doc;
            try
            {
                _activeDoc.ImpliedSelectionChanged -= Doc_ImpliedSelectionChanged;
                _activeDoc.ImpliedSelectionChanged += Doc_ImpliedSelectionChanged;
            }
            catch { }
        }

        private void UnregisterSelectionEvent()
        {
            try
            {
                Application.DocumentManager.DocumentActivated -= DocumentManager_DocumentActivated;
            }
            catch { }

            if (_activeDoc != null)
            {
                try
                {
                    _activeDoc.ImpliedSelectionChanged -= Doc_ImpliedSelectionChanged;
                }
                catch { }
                _activeDoc = null;
            }
        }

        private void Doc_ImpliedSelectionChanged(object sender, EventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;
                PromptSelectionResult psr = ed.SelectImplied();
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    List<ObjectId> selIds = new List<ObjectId>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so != null && !so.ObjectId.IsNull)
                        {
                            selIds.Add(so.ObjectId);
                        }
                    }
                    ProcessSelectedObjects(doc, selIds);
                }
                else
                {
                    ProcessSelectedObjects(doc, new List<ObjectId>());
                }
            }
            catch { }
        }

        private void ProcessSelectedObjects(Document doc, List<ObjectId> selIds)
        {
            if (selIds != null && selIds.Count > 0)
            {
                _currentLiveSelectedIds = selIds;
                LiveSelectionData data = ExtractLiveSelectionData(doc, selIds);
                _lastSelectionData = data;

                string firstLayer = data.Layers.FirstOrDefault();

                this.Dispatcher.BeginInvoke(new Action(delegate()
                {
                    RenderLiveSelectionPreview(data);
                    if (!string.IsNullOrEmpty(firstLayer))
                    {
                        NavigateToLayer(firstLayer);
                    }
                }));
            }
            else
            {
                _currentLiveSelectedIds.Clear();
                _lastSelectionData = null;
                this.Dispatcher.BeginInvoke(new Action(delegate()
                {
                    ClearLiveSelectionUI();
                }));
            }
        }

        private void BtnLivePickSelection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                Editor ed = doc.Editor;
                using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\n[NTT TOOLS] Quét chọn các đối tượng trên CAD để xem Live Preview / Can thiệp thủ công: ";
                    PromptSelectionResult psr = ed.GetSelection(pso);
                    if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                    {
                        List<ObjectId> selIds = new List<ObjectId>();
                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so != null && !so.ObjectId.IsNull)
                            {
                                selIds.Add(so.ObjectId);
                            }
                        }
                        ed.SetImpliedSelection(selIds.ToArray());
                        ProcessSelectedObjects(doc, selIds);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Lỗi khi quét đối tượng CAD: " + ex.Message, "NTT TOOLS", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearLiveSelectionUI()
        {
            if (cvsLiveSelectionPreview != null) cvsLiveSelectionPreview.Children.Clear();
            if (txtLivePreviewPlaceholder != null) txtLivePreviewPlaceholder.Visibility = System.Windows.Visibility.Visible;
            if (txtLivePreviewDimInfo != null) txtLivePreviewDimInfo.Text = "";
            if (txtLiveSelectedStatus != null) txtLiveSelectedStatus.Text = "Chưa chọn đối tượng nào trên CAD";
            if (txtLiveEntitySummary != null) txtLiveEntitySummary.Text = "0 đối tượng";
            if (txtLiveCurrentLayer != null) txtLiveCurrentLayer.Text = "Chưa có";
            if (txtLiveAiSuggestedLayer != null) txtLiveAiSuggestedLayer.Text = "--";
            if (txtLiveAiDiagnosisReason != null) txtLiveAiDiagnosisReason.Text = "Chọn đối tượng trên CAD để AI kiểm tra hình học & tri thức";

            if (cvsAiLiveSelectionPreview != null) cvsAiLiveSelectionPreview.Children.Clear();
            if (txtAiLivePreviewPlaceholder != null) txtAiLivePreviewPlaceholder.Visibility = System.Windows.Visibility.Visible;
            if (txtAiLivePreviewDimInfo != null) txtAiLivePreviewDimInfo.Text = "";
            if (txtAiLiveSelectedStatus != null) txtAiLiveSelectedStatus.Text = "Chưa chọn đối tượng nào trên CAD";
            if (txtAiLiveEntitySummary != null) txtAiLiveEntitySummary.Text = "0 đối tượng";
            if (txtAiLiveCurrentLayer != null) txtAiLiveCurrentLayer.Text = "Chưa có";
            if (txtAiLiveAiSuggestedLayer != null) txtAiLiveAiSuggestedLayer.Text = "--";
            if (txtAiLiveAiDiagnosisReason != null) txtAiLiveAiDiagnosisReason.Text = "Chọn đối tượng trên CAD để AI kiểm tra hình học & tri thức";
        }

        private LiveSelectionData ExtractLiveSelectionData(Document doc, List<ObjectId> ids)
        {
            LiveSelectionData data = new LiveSelectionData();
            data.TotalCount = ids.Count;
            if (ids.Count == 0) return data;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            try
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string typeName = ent.GetType().Name;
                        if (data.TypeCounts.ContainsKey(typeName))
                            data.TypeCounts[typeName]++;
                        else
                            data.TypeCounts[typeName] = 1;

                        if (!string.IsNullOrEmpty(ent.Layer))
                            data.Layers.Add(ent.Layer);

                        System.Windows.Media.Color drawColor = System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF);
                        if (ent.Layer != null)
                        {
                            if (ent.Layer.IndexOf("TUONG", StringComparison.OrdinalIgnoreCase) >= 0)
                                drawColor = System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC);
                            else if (ent.Layer.IndexOf("COT", StringComparison.OrdinalIgnoreCase) >= 0)
                                drawColor = System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81);
                            else if (ent.Layer.IndexOf("CUA", StringComparison.OrdinalIgnoreCase) >= 0)
                                drawColor = System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B);
                            else if (ent.Layer.IndexOf("VACH_KINH", StringComparison.OrdinalIgnoreCase) >= 0)
                                drawColor = System.Windows.Media.Color.FromRgb(0x06, 0xB6, 0xD4);
                            else if (ent.Layer.IndexOf("RAC", StringComparison.OrdinalIgnoreCase) >= 0)
                                drawColor = System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B);
                        }

                        ExtractEntitySegments(ent, tr, data, drawColor, ref minX, ref minY, ref maxX, ref maxY);
                    }

                    tr.Commit();
                }

                data.Diagnosis = ArchitecturalKnowledgeService.DiagnoseSelection(doc, ids);
            }
            catch { }

            if (minX <= maxX && minY <= maxY)
            {
                data.MinX = minX;
                data.MinY = minY;
                data.MaxX = maxX;
                data.MaxY = maxY;
                data.HasExtents = true;
            }

            return data;
        }

        private static void UpdateExtents(ref double minX, ref double minY, ref double maxX, ref double maxY, double x, double y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        private static void ExtractEntitySegments(Entity ent, Transaction tr, LiveSelectionData data, System.Windows.Media.Color drawColor, ref double minX, ref double minY, ref double maxX, ref double maxY, int depth = 0)
        {
            if (ent == null || depth > 4) return;

            Line line = ent as Line;
            if (line != null)
            {
                Point2d p1 = new Point2d(line.StartPoint.X, line.StartPoint.Y);
                Point2d p2 = new Point2d(line.EndPoint.X, line.EndPoint.Y);
                data.Segments.Add(new LivePreviewSegment(p1, p2, drawColor));
                UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, p1.X, p1.Y);
                UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, p2.X, p2.Y);
                return;
            }

            Polyline pline = ent as Polyline;
            if (pline != null)
            {
                int numVerts = pline.NumberOfVertices;
                int segCount = pline.Closed ? numVerts : numVerts - 1;
                for (int i = 0; i < segCount; i++)
                {
                    int nextIdx = (i + 1) % numVerts;
                    Point2d pt1 = pline.GetPoint2dAt(i);
                    Point2d pt2 = pline.GetPoint2dAt(nextIdx);
                    double bulge = pline.GetBulgeAt(i);

                    if (Math.Abs(bulge) < 1e-6)
                    {
                        data.Segments.Add(new LivePreviewSegment(pt1, pt2, drawColor));
                        UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, pt1.X, pt1.Y);
                        UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, pt2.X, pt2.Y);
                    }
                    else
                    {
                        List<Point2d> arcPts = SampleBulgeArc(pt1, pt2, bulge, 12);
                        for (int a = 0; a < arcPts.Count - 1; a++)
                        {
                            data.Segments.Add(new LivePreviewSegment(arcPts[a], arcPts[a + 1], drawColor));
                            UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, arcPts[a].X, arcPts[a].Y);
                            UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, arcPts[a + 1].X, arcPts[a + 1].Y);
                        }
                    }
                }
                return;
            }

            Arc arc = ent as Arc;
            if (arc != null)
            {
                double startAngle = arc.StartAngle;
                double endAngle = arc.EndAngle;
                double totalSweep = endAngle - startAngle;
                if (totalSweep < 0) totalSweep += 2.0 * Math.PI;

                int numSteps = 16;
                Point2d prevPt = new Point2d(arc.Center.X + arc.Radius * Math.Cos(startAngle), arc.Center.Y + arc.Radius * Math.Sin(startAngle));
                UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, prevPt.X, prevPt.Y);

                for (int s = 1; s <= numSteps; s++)
                {
                    double ang = startAngle + (totalSweep * s) / numSteps;
                    Point2d curPt = new Point2d(arc.Center.X + arc.Radius * Math.Cos(ang), arc.Center.Y + arc.Radius * Math.Sin(ang));
                    data.Segments.Add(new LivePreviewSegment(prevPt, curPt, drawColor));
                    UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, curPt.X, curPt.Y);
                    prevPt = curPt;
                }
                return;
            }

            Circle circ = ent as Circle;
            if (circ != null)
            {
                int numSteps = 24;
                Point2d prevPt = new Point2d(circ.Center.X + circ.Radius, circ.Center.Y);
                UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, prevPt.X, prevPt.Y);

                for (int s = 1; s <= numSteps; s++)
                {
                    double ang = (2.0 * Math.PI * s) / numSteps;
                    Point2d curPt = new Point2d(circ.Center.X + circ.Radius * Math.Cos(ang), circ.Center.Y + circ.Radius * Math.Sin(ang));
                    data.Segments.Add(new LivePreviewSegment(prevPt, curPt, drawColor));
                    UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, curPt.X, curPt.Y);
                    prevPt = curPt;
                }
                return;
            }

            BlockReference br = ent as BlockReference;
            if (br != null)
            {
                DBObjectCollection exploded = new DBObjectCollection();
                try
                {
                    br.Explode(exploded);
                    foreach (DBObject obj in exploded)
                    {
                        Entity subEnt = obj as Entity;
                        if (subEnt != null)
                        {
                            ExtractEntitySegments(subEnt, tr, data, drawColor, ref minX, ref minY, ref maxX, ref maxY, depth + 1);
                            subEnt.Dispose();
                        }
                    }
                    return;
                }
                catch
                {
                    try
                    {
                        Extents3d ext = br.GeometricExtents;
                        AddBoxSegments(ext, data, drawColor, ref minX, ref minY, ref maxX, ref maxY);
                        return;
                    }
                    catch { }
                }
            }

            // Fallback for Polyline2d, Polyline3d, Ellipse, Spline, etc.
            DBObjectCollection otherExploded = new DBObjectCollection();
            bool explodedSuccess = false;
            try
            {
                ent.Explode(otherExploded);
                if (otherExploded.Count > 0)
                {
                    explodedSuccess = true;
                    foreach (DBObject obj in otherExploded)
                    {
                        Entity subEnt = obj as Entity;
                        if (subEnt != null)
                        {
                            ExtractEntitySegments(subEnt, tr, data, drawColor, ref minX, ref minY, ref maxX, ref maxY, depth + 1);
                            subEnt.Dispose();
                        }
                    }
                }
            }
            catch { }

            if (!explodedSuccess)
            {
                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    AddBoxSegments(ext, data, drawColor, ref minX, ref minY, ref maxX, ref maxY);
                }
                catch { }
            }
        }

        private static void AddBoxSegments(Extents3d ext, LiveSelectionData data, System.Windows.Media.Color drawColor, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            Point2d p1 = new Point2d(ext.MinPoint.X, ext.MinPoint.Y);
            Point2d p2 = new Point2d(ext.MaxPoint.X, ext.MinPoint.Y);
            Point2d p3 = new Point2d(ext.MaxPoint.X, ext.MaxPoint.Y);
            Point2d p4 = new Point2d(ext.MinPoint.X, ext.MaxPoint.Y);

            data.Segments.Add(new LivePreviewSegment(p1, p2, drawColor));
            data.Segments.Add(new LivePreviewSegment(p2, p3, drawColor));
            data.Segments.Add(new LivePreviewSegment(p3, p4, drawColor));
            data.Segments.Add(new LivePreviewSegment(p4, p1, drawColor));

            UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, p1.X, p1.Y);
            UpdateExtents(ref minX, ref minY, ref maxX, ref maxY, p3.X, p3.Y);
        }

        private static List<Point2d> SampleBulgeArc(Point2d p1, Point2d p2, double b, int steps)
        {
            List<Point2d> pts = new List<Point2d>();
            if (Math.Abs(b) < 1e-6 || steps < 2)
            {
                pts.Add(p1);
                pts.Add(p2);
                return pts;
            }

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-6)
            {
                pts.Add(p1);
                pts.Add(p2);
                return pts;
            }

            double theta = 4.0 * Math.Atan(b);
            double d = (L * (1.0 - b * b)) / (4.0 * b);
            double ux = -dy / L;
            double uy = dx / L;

            double cx = (p1.X + p2.X) / 2.0 + ux * d;
            double cy = (p1.Y + p2.Y) / 2.0 + uy * d;
            double r = (L * (1.0 + b * b)) / (4.0 * Math.Abs(b));

            double theta1 = Math.Atan2(p1.Y - cy, p1.X - cx);

            for (int k = 0; k <= steps; k++)
            {
                double t = (double)k / steps;
                double ang = theta1 + t * theta;
                pts.Add(new Point2d(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang)));
            }
            return pts;
        }

        private void RenderLiveSelectionPreview(LiveSelectionData data)
        {
            // Reset zoom/pan về ban đầu mỗi khi có vùng chọn mới
            if (stLivePreview != null) { stLivePreview.ScaleX = 1.0; stLivePreview.ScaleY = 1.0; }
            if (ttLivePreview != null) { ttLivePreview.X = 0; ttLivePreview.Y = 0; }
            if (stAiLivePreview != null) { stAiLivePreview.ScaleX = 1.0; stAiLivePreview.ScaleY = 1.0; }
            if (ttAiLivePreview != null) { ttAiLivePreview.X = 0; ttAiLivePreview.Y = 0; }

            if (data == null || data.TotalCount == 0 || !data.HasExtents || data.Segments.Count == 0)
            {
                ClearLiveSelectionUI();
                return;
            }

            if (txtLivePreviewPlaceholder != null) txtLivePreviewPlaceholder.Visibility = System.Windows.Visibility.Collapsed;
            if (txtAiLivePreviewPlaceholder != null) txtAiLivePreviewPlaceholder.Visibility = System.Windows.Visibility.Collapsed;

            double wMm = Math.Max(0, data.MaxX - data.MinX);
            double hMm = Math.Max(0, data.MaxY - data.MinY);
            string dimText = string.Format("Kích thước: {0:F0} × {1:F0} mm", wMm, hMm);
            if (txtLivePreviewDimInfo != null) txtLivePreviewDimInfo.Text = dimText;
            if (txtAiLivePreviewDimInfo != null) txtAiLivePreviewDimInfo.Text = dimText;

            List<string> typeList = new List<string>();
            foreach (var kvp in data.TypeCounts)
            {
                typeList.Add(string.Format("{0}: {1}", kvp.Key, kvp.Value));
            }
            string summaryText = string.Format("{0} đối tượng ({1})", data.TotalCount, string.Join(", ", typeList.ToArray()));
            if (txtLiveEntitySummary != null) txtLiveEntitySummary.Text = summaryText;
            if (txtAiLiveEntitySummary != null) txtAiLiveEntitySummary.Text = summaryText;

            string layerText = data.Layers.Count == 1 ? data.Layers.First() : (data.Layers.Count > 1 ? string.Format("Đa layer ({0})", data.Layers.Count) : "Không xác định");
            if (txtLiveCurrentLayer != null) txtLiveCurrentLayer.Text = layerText;
            if (txtAiLiveCurrentLayer != null) txtAiLiveCurrentLayer.Text = layerText;

            string statusText = string.Format("Đang chọn {0} đối tượng trên CAD (Live)", data.TotalCount);
            if (txtLiveSelectedStatus != null) txtLiveSelectedStatus.Text = statusText;
            if (txtAiLiveSelectedStatus != null) txtAiLiveSelectedStatus.Text = statusText;

            // Cập nhật Chẩn đoán AI Tức Thời (Dữ liệu đã học Cửa & Tường tiếp giáp Cột)
            UpdateLiveDiagnosisUI(data.Diagnosis);

            // Render vector lên cả 2 Canvas (Tab 1 & Tab 4)
            DrawSegmentsToCanvas(cvsLiveSelectionPreview, data, wMm, hMm);
            DrawSegmentsToCanvas(cvsAiLiveSelectionPreview, data, wMm, hMm);
        }

        private void UpdateLiveDiagnosisUI(LiveSelectionDiagnosis diag)
        {
            string suggestedText = (diag != null && !string.IsNullOrEmpty(diag.SuggestedLayer))
                ? (diag.IsLayerMismatch ? diag.SuggestedLayer + " (Sai Layer!)" : diag.SuggestedLayer + " (Đúng chuẩn)")
                : "--";
            string reasonText = (diag != null && !string.IsNullOrEmpty(diag.Reason))
                ? string.Format("[{0}] {1}", diag.EntityType, diag.Reason)
                : "Chọn đối tượng trên CAD để AI kiểm tra hình học & tri thức";

            var layerBrush = new System.Windows.Media.SolidColorBrush(
                (diag != null && diag.IsLayerMismatch)
                    ? System.Windows.Media.Color.FromRgb(0xFF, 0x91, 0x00)
                    : System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));

            if (txtLiveAiSuggestedLayer != null)
            {
                txtLiveAiSuggestedLayer.Text = suggestedText;
                txtLiveAiSuggestedLayer.Foreground = layerBrush;
            }
            if (txtLiveAiDiagnosisReason != null) txtLiveAiDiagnosisReason.Text = reasonText;

            if (txtAiLiveAiSuggestedLayer != null)
            {
                txtAiLiveAiSuggestedLayer.Text = suggestedText;
                txtAiLiveAiSuggestedLayer.Foreground = layerBrush;
            }
            if (txtAiLiveAiDiagnosisReason != null) txtAiLiveAiDiagnosisReason.Text = reasonText;

            if (diag != null && !string.IsNullOrEmpty(diag.SuggestedLayer))
            {
                if (cboLiveTargetLayer != null && cboLiveTargetLayer.Items.Contains(diag.SuggestedLayer))
                {
                    cboLiveTargetLayer.SelectedItem = diag.SuggestedLayer;
                }
                if (cboAiLiveTargetLayer != null && cboAiLiveTargetLayer.Items.Contains(diag.SuggestedLayer))
                {
                    cboAiLiveTargetLayer.SelectedItem = diag.SuggestedLayer;
                }
            }
        }

        private void DrawSegmentsToCanvas(Canvas canvas, LiveSelectionData data, double wMm, double hMm)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            double canvasW = canvas.ActualWidth;
            double canvasH = canvas.ActualHeight;
            if (canvasW <= 10) canvasW = 380;
            if (canvasH <= 10) canvasH = 180;

            double pad = 16.0;
            double availW = canvasW - pad * 2.0;
            double availH = canvasH - pad * 2.0;
            if (availW <= 10) availW = 100;
            if (availH <= 10) availH = 100;

            double scaleX = wMm > 0.1 ? availW / wMm : 1.0;
            double scaleY = hMm > 0.1 ? availH / hMm : 1.0;
            double scale = Math.Min(scaleX, scaleY);
            if (scale <= 0.00001) scale = 1.0;

            double midCadX = (data.MinX + data.MaxX) / 2.0;
            double midCadY = (data.MinY + data.MaxY) / 2.0;
            double midCanvasX = canvasW / 2.0;
            double midCanvasY = canvasH / 2.0;

            foreach (var seg in data.Segments)
            {
                double x1 = midCanvasX + (seg.Start.X - midCadX) * scale;
                double y1 = midCanvasY - (seg.Start.Y - midCadY) * scale;
                double x2 = midCanvasX + (seg.End.X - midCadX) * scale;
                double y2 = midCanvasY - (seg.End.Y - midCadY) * scale;

                System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = new System.Windows.Media.SolidColorBrush(seg.Color),
                    StrokeThickness = 1.6
                };
                canvas.Children.Add(line);
            }
        }

        private void SetupCanvasZoomPan(Canvas canvas, ScaleTransform st, TranslateTransform tt)
        {
            System.Windows.Point lastMousePos = new System.Windows.Point();
            bool isPanning = false;

            canvas.MouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                double zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
                System.Windows.Point mousePos = e.GetPosition(canvas);

                double newScaleX = st.ScaleX * zoomFactor;
                double newScaleY = st.ScaleY * zoomFactor;

                if (newScaleX < 0.1 || newScaleX > 80.0) return;

                tt.X = mousePos.X - (mousePos.X - tt.X) * zoomFactor;
                tt.Y = mousePos.Y - (mousePos.Y - tt.Y) * zoomFactor;

                st.ScaleX = newScaleX;
                st.ScaleY = newScaleY;
                e.Handled = true;
            };

            canvas.MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle)
                {
                    if (e.ClickCount == 2)
                    {
                        st.ScaleX = 1.0;
                        st.ScaleY = 1.0;
                        tt.X = 0;
                        tt.Y = 0;
                        e.Handled = true;
                        return;
                    }

                    isPanning = true;
                    lastMousePos = e.GetPosition(canvas.Parent as IInputElement ?? canvas);
                    canvas.CaptureMouse();
                    canvas.Cursor = Cursors.SizeAll;
                    e.Handled = true;
                }
            };

            canvas.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (isPanning)
                {
                    System.Windows.Point curPos = e.GetPosition(canvas.Parent as IInputElement ?? canvas);
                    tt.X += curPos.X - lastMousePos.X;
                    tt.Y += curPos.Y - lastMousePos.Y;
                    lastMousePos = curPos;
                    e.Handled = true;
                }
            };

            canvas.MouseUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (isPanning && (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle))
                {
                    isPanning = false;
                    canvas.ReleaseMouseCapture();
                    canvas.Cursor = Cursors.Cross;
                    e.Handled = true;
                }
            };
        }

        private void BtnLiveChangeLayer_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (_currentLiveSelectedIds == null || _currentLiveSelectedIds.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn các đối tượng trên AutoCAD trước khi đổi layer!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string targetLayer = null;
            if (mainTabControl != null && mainTabControl.SelectedItem == tabAiProcessing && cboAiLiveTargetLayer != null)
            {
                targetLayer = cboAiLiveTargetLayer.SelectedItem as string;
            }
            if (string.IsNullOrEmpty(targetLayer) && cboLiveTargetLayer != null)
            {
                targetLayer = cboLiveTargetLayer.SelectedItem as string;
            }

            if (string.IsNullOrEmpty(targetLayer))
            {
                MessageBox.Show("Vui lòng chọn layer mục tiêu trong danh sách!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Ghi nhận layer nguồn của các đối tượng đang chọn trước khi đổi (Bước 2 -> Bước 3)
                HashSet<string> sourceLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (doc.LockDocument())
                using (Transaction trSrc = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in _currentLiveSelectedIds)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        Entity ent = trSrc.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && !string.IsNullOrEmpty(ent.Layer)) sourceLayers.Add(ent.Layer);
                    }
                    trSrc.Commit();
                }

                int changed = CadLayoutProcessorService.ChangeEntitiesLayer(doc, _currentLiveSelectedIds, targetLayer);
                var learnedStep2 = ShapePatternLearningService.LearnStep2PatternsFromSelection(
                    doc, _currentLiveSelectedIds, sourceLayers, targetLayer);

                if (learnedStep2.Count > 0)
                {
                    SetStep2OnlyMode(true);
                    string patMsg = string.Format(
                        "⚡ Đã đổi {0} nét → {1} & 🧠 TỰ HỌC {2} mẫu Bước 2! Bấm '1-Click' để chuẩn hóa đúng mẫu này (không đụng layer khác).",
                        changed, targetLayer, learnedStep2.Count);
                    txtDialogStatus.Text = patMsg;
                    if (txtLiveAiDiagnosisReason != null) txtLiveAiDiagnosisReason.Text = patMsg;
                    if (txtAiLiveAiDiagnosisReason != null) txtAiLiveAiDiagnosisReason.Text = patMsg;
                }
                else
                {
                    txtDialogStatus.Text = string.Format("⚡ Đã chuyển {0} đối tượng sang layer {1} thành công!", changed, targetLayer);
                }

                if (txtLiveCurrentLayer != null) txtLiveCurrentLayer.Text = targetLayer;
                if (txtAiLiveCurrentLayer != null) txtAiLiveCurrentLayer.Text = targetLayer;
                doc.Editor.WriteMessage(string.Format(
                    "\n[AI] ⚡ Đã chuyển {0} đối tượng sang layer '{1}' (Đã học {2} mẫu Bước 2).\n",
                    changed, targetLayer, learnedStep2.Count));

                ReloadLayers();
                NavigateToLayer(targetLayer);

                var data = ExtractLiveSelectionData(doc, _currentLiveSelectedIds);
                _lastSelectionData = data;
                RenderLiveSelectionPreview(data);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đổi layer: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLivePackageDoorBlock_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (_currentLiveSelectedIds == null || _currentLiveSelectedIds.Count == 0)
            {
                MessageBox.Show("Vui lòng quét chọn các nét của cửa trên AutoCAD trước khi đóng gói Block!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string blkName = string.Format("AI_DOOR_{0:yyyyMMdd_HHmmss}", DateTime.Now);
                ObjectId brId = CadLayoutProcessorService.PackageSelectedEntitiesToBlock(doc, _currentLiveSelectedIds, CadLayoutProcessorService.LayerCua, blkName);

                if (!brId.IsNull)
                {
                    txtDialogStatus.Text = string.Format("🚪 Đã gom và đóng gói Block Cửa '{0}' trên layer AI_XD_CUA!", blkName);
                    doc.Editor.WriteMessage(string.Format("\n[AI] 🚪 Đã gom và đóng gói Block Cửa '{0}' trên layer AI_XD_CUA thành công!\n", blkName));

                    _currentLiveSelectedIds = new List<ObjectId> { brId };
                    doc.Editor.SetImpliedSelection(new ObjectId[] { brId });

                    ReloadLayers();
                    NavigateToLayer(CadLayoutProcessorService.LayerCua);

                    var data = ExtractLiveSelectionData(doc, _currentLiveSelectedIds);
                    _lastSelectionData = data;
                    RenderLiveSelectionPreview(data);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đóng gói block cửa: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLivePackageCustomBlock_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (_currentLiveSelectedIds == null || _currentLiveSelectedIds.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn các đối tượng trên AutoCAD trước khi đóng gói Block!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string targetLayer = null;
            if (mainTabControl != null && mainTabControl.SelectedItem == tabAiProcessing && cboAiLiveTargetLayer != null)
            {
                targetLayer = cboAiLiveTargetLayer.SelectedItem as string;
            }
            if (string.IsNullOrEmpty(targetLayer) && cboLiveTargetLayer != null)
            {
                targetLayer = cboLiveTargetLayer.SelectedItem as string;
            }
            if (string.IsNullOrEmpty(targetLayer))
            {
                targetLayer = "AI_XD_CUA";
            }

            // Đo kích thước thực tế của vùng chọn CAD (W, D) và xác định Pickpoint góc dưới trái
            double measuredW = 1000, measuredD = 100;
            Point3d minPt = new Point3d(double.MaxValue, double.MaxValue, double.MaxValue);
            Point3d maxPt = new Point3d(double.MinValue, double.MinValue, double.MinValue);
            bool hasBounds = false;

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in _currentLiveSelectedIds)
                {
                    if (id.IsNull || id.IsErased) continue;
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        try
                        {
                            Extents3d box = ent.GeometricExtents;
                            if (box.MinPoint.X < minPt.X) minPt = new Point3d(box.MinPoint.X, minPt.Y, minPt.Z);
                            if (box.MinPoint.Y < minPt.Y) minPt = new Point3d(minPt.X, box.MinPoint.Y, minPt.Z);
                            if (box.MaxPoint.X > maxPt.X) maxPt = new Point3d(box.MaxPoint.X, maxPt.Y, maxPt.Z);
                            if (box.MaxPoint.Y > maxPt.Y) maxPt = new Point3d(maxPt.X, box.MaxPoint.Y, maxPt.Z);
                            hasBounds = true;
                        }
                        catch { }
                    }
                }
                tr.Commit();
            }

            if (hasBounds)
            {
                double dx = Math.Abs(maxPt.X - minPt.X);
                double dy = Math.Abs(maxPt.Y - minPt.Y);
                measuredW = Math.Max(dx, dy);
                measuredD = Math.Min(dx, dy);
            }

            // Bật bảng thông số đóng gói Block tùy chọn
            CustomBlockDialog dlg = new CustomBlockDialog(measuredW, measuredD, targetLayer);
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;

            string blkName = dlg.FinalBlockName;
            string chosenLayer = dlg.FinalTargetLayer;
            Point3d pickPoint = hasBounds ? new Point3d(minPt.X, minPt.Y, 0) : Point3d.Origin;

            try
            {
                ObjectId brId = CadLayoutProcessorService.PackageSelectedEntitiesToBlock(doc, _currentLiveSelectedIds, chosenLayer, blkName, pickPoint);

                if (!brId.IsNull)
                {
                    txtDialogStatus.Text = string.Format("📦 Đã gom và đóng gói Block '{0}' trên layer {1}!", blkName, chosenLayer);
                    doc.Editor.WriteMessage(string.Format("\n[AI] 📦 Đã gom và đóng gói Block '{0}' trên layer '{1}' thành công!\n", blkName, chosenLayer));

                    _currentLiveSelectedIds = new List<ObjectId> { brId };
                    doc.Editor.SetImpliedSelection(new ObjectId[] { brId });

                    ReloadLayers();
                    NavigateToLayer(chosenLayer);

                    var data = ExtractLiveSelectionData(doc, _currentLiveSelectedIds);
                    _lastSelectionData = data;
                    RenderLiveSelectionPreview(data);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đóng gói block: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetSelectedLiveTargetLayer()
        {
            string targetLayer = null;
            if (mainTabControl != null && mainTabControl.SelectedItem == tabAiProcessing && cboAiLiveTargetLayer != null)
            {
                targetLayer = cboAiLiveTargetLayer.SelectedItem as string;
            }
            if (string.IsNullOrEmpty(targetLayer) && cboLiveTargetLayer != null)
            {
                targetLayer = cboLiveTargetLayer.SelectedItem as string;
            }
            return string.IsNullOrEmpty(targetLayer) ? CadLayoutProcessorService.LayerNoiThat : targetLayer;
        }

        private void BtnLiveLearnShapePattern_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            string activeTargetLayer = GetSelectedLiveTargetLayer();
            List<LearnedShapePattern> learnedThisRun = new List<LearnedShapePattern>();
            bool useSavedOnly = false;

            if (_currentLiveSelectedIds != null && _currentLiveSelectedIds.Count >= 3)
            {
                learnedThisRun = ShapePatternLearningService.LearnPatternsFromSelectedIds(doc, _currentLiveSelectedIds, activeTargetLayer);
                if (learnedThisRun.Count == 0)
                {
                    MessageBox.Show(
                        "Cụm đang chọn chưa đủ phức tạp để làm mẫu (cần ít nhất 3 nét/đoạn liên tiếp và kích thước >= 100mm).",
                        "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else
            {
                this.Hide();
                try
                {
                    PromptKeywordOptions pko = new PromptKeywordOptions(
                        "\nHọc mẫu hình học từ 1 phòng ví dụ hay chọn 1 cụm mẫu ");
                    pko.Keywords.Add("PhongMau", "PhongMau", "Quét 1 Phòng Mẫu (W)");
                    pko.Keywords.Add("CumMau", "CumMau", "Chọn 1 Cụm Mẫu (S)");
                    pko.Keywords.Add("QuetLai", "QuetLai", "Quét lại mẫu đã học (R)");
                    pko.Keywords.Default = "PhongMau";
                    pko.AllowNone = true;

                    PromptResult kr = ed.GetKeywords(pko);
                    if (kr.Status == PromptStatus.Cancel) return;
                    string kw = (kr.Status == PromptStatus.Keyword || kr.Status == PromptStatus.OK)
                        ? (kr.StringResult ?? "PhongMau") : "PhongMau";

                    if (kw == "PhongMau")
                    {
                        PromptPointResult p1 = ed.GetPoint("\nPick góc thứ 1 bao vùng 1 phòng mẫu (đã gán sẵn layer chuẩn): ");
                        if (p1.Status != PromptStatus.OK) return;
                        PromptCornerOptions pco = new PromptCornerOptions("\nPick góc đối diện của phòng mẫu: ", p1.Value);
                        PromptPointResult p2 = ed.GetCorner(pco);
                        if (p2.Status != PromptStatus.OK) return;

                        Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;
                        Point3d w1 = p1.Value.TransformBy(ucsToWcs);
                        Point3d w2 = p2.Value.TransformBy(ucsToWcs);
                        Extents3d sampleBox = new Extents3d(
                            new Point3d(Math.Min(w1.X, w2.X), Math.Min(w1.Y, w2.Y), -1000),
                            new Point3d(Math.Max(w1.X, w2.X), Math.Max(w1.Y, w2.Y), 1000));
                        learnedThisRun = ShapePatternLearningService.LearnPatternsFromSampleRegion(doc, sampleBox);
                        if (learnedThisRun.Count == 0)
                        {
                            MessageBox.Show(
                                "Không tìm thấy cụm nét nào (>= 3 nét) trên layer AI_XD_NOI_THAT / CUA / VACH_KINH / COT bên trong vùng phòng vừa quét!",
                                "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                    }
                    else if (kw == "CumMau")
                    {
                        PromptSelectionOptions pso = new PromptSelectionOptions();
                        pso.MessageForAdding = "\nQuét chọn 1 cụm đối tượng mẫu trên CAD (VD: giàn móc áo, bàn ghế, sofa): ";
                        PromptSelectionResult psr = ed.GetSelection(pso);
                        if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0) return;

                        var ids = psr.Value.GetObjectIds();
                        _currentLiveSelectedIds = new List<ObjectId>(ids);
                        learnedThisRun = ShapePatternLearningService.LearnPatternsFromSelectedIds(doc, ids, activeTargetLayer);
                        if (learnedThisRun.Count == 0)
                        {
                            MessageBox.Show(
                                "Cụm vừa chọn cần ít nhất 3 đoạn nét liên tiếp và kích thước >= 100mm để tạo chữ ký hình học!",
                                "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                    }
                    else
                    {
                        useSavedOnly = true;
                        if (ShapePatternLearningService.GetPatternCount() == 0)
                        {
                            MessageBox.Show("Chưa có mẫu hình học nào được lưu trước đó!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                    }
                }
                finally
                {
                    this.Show();
                    this.Activate();
                }
            }

            try
            {
                txtDialogStatus.Text = "🎓 Đang quét toàn bộ Khung Bao tìm các cụm giống mẫu (hỗ trợ xoay & đối xứng gương)...";
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));

                List<LearnedShapePattern> patternsToScan = useSavedOnly
                    ? ShapePatternLearningService.LoadPatterns()
                    : learnedThisRun;

                if (!useSavedOnly && learnedThisRun.Count > 0)
                {
                    ShapePatternLearningService.RegisterStep2SessionRule(learnedThisRun, null);
                    SetStep2OnlyMode(true);
                }

                var matches = ShapePatternLearningService.FindMatchingClustersInScope(doc, _currentBounds, patternsToScan);
                int totalEntsToMove = 0;
                foreach (var m in matches)
                {
                    if (m.MatchedPattern != null)
                    {
                        foreach (var id in m.EntityIds)
                        {
                            totalEntsToMove++;
                        }
                    }
                }

                // Chuyển trước các nét vừa pick sang activeTargetLayer nếu kỹ sư học trực tiếp từ cụm chọn mà cụm gốc chưa đổi layer
                int changedSample = 0;
                if (_currentLiveSelectedIds != null && _currentLiveSelectedIds.Count > 0 && learnedThisRun.Count == 1 && !useSavedOnly)
                {
                    changedSample = CadLayoutProcessorService.ChangeEntitiesLayer(doc, _currentLiveSelectedIds, activeTargetLayer);
                }

                if (matches.Count == 0)
                {
                    ReloadLayers();
                    string infoNoMatch = string.Format(
                        "Đã học & lưu {0} mẫu hình học ({1}). Hiện không còn cụm nào khác nằm sai layer trong phạm vi Khung Bao!",
                        learnedThisRun.Count, string.Join(", ", patternsToScan.Select(p => p.Name).Take(3).ToArray()));
                    txtDialogStatus.Text = infoNoMatch;
                    MessageBox.Show(infoNoMatch, "Hoàn Tất Học Mẫu", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var groupedSummary = matches
                    .GroupBy(m => m.MatchedPattern.TargetLayer)
                    .Select(g => string.Format("• {0}: {1} cụm ({2} đối tượng)", g.Key, g.Count(), g.Sum(x => x.EntityIds.Count)))
                    .ToArray();

                string askMsg = string.Format(
                    "Đã học {0} mẫu mới (Tổng {1} mẫu đã lưu).\n\nTìm thấy {2} cụm giống hệt trong Khung Bao ({3} đối tượng đang ở layer khác, hỗ trợ xoay + đối xứng):\n{4}\n\nBạn có muốn chuyển toàn bộ sang đúng Layer chuẩn ngay không?",
                    learnedThisRun.Count,
                    ShapePatternLearningService.GetPatternCount(),
                    matches.Count,
                    totalEntsToMove,
                    string.Join("\n", groupedSummary));

                if (MessageBox.Show(askMsg, "Xác Nhận Gom Cụm Theo Mẫu Phòng", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    int changedMatched = ShapePatternLearningService.ApplyMatchedClusters(doc, matches);
                    ReloadLayers();
                    try { doc.Editor.UpdateScreen(); } catch { }
                    txtDialogStatus.Text = string.Format(
                        "🎓 Đã học {0} mẫu & gom đồng loạt {1} cụm ({2} đối tượng) về đúng Layer chuẩn!",
                        learnedThisRun.Count, matches.Count, changedMatched + changedSample);
                    ed.WriteMessage(string.Format(
                        "\n[AI HỌC MẪU PHÒNG] 🎓 Đã học {0} mẫu và chuyển {1} cụm ({2} đối tượng) sang layer chuẩn.\n",
                        learnedThisRun.Count, matches.Count, changedMatched + changedSample));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi học mẫu phòng / quét khung bao: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class LivePreviewSegment
        {
            public System.Windows.Point Start;
            public System.Windows.Point End;
            public System.Windows.Media.Color Color;

            public LivePreviewSegment() { }
            public LivePreviewSegment(Point2d p1, Point2d p2, System.Windows.Media.Color col)
            {
                Start = new System.Windows.Point(p1.X, p1.Y);
                End = new System.Windows.Point(p2.X, p2.Y);
                Color = col;
            }
        }

        private class LiveSelectionData
        {
            public int TotalCount;
            public Dictionary<string, int> TypeCounts = new Dictionary<string, int>();
            public HashSet<string> Layers = new HashSet<string>();
            public double MinX, MinY, MaxX, MaxY;
            public bool HasExtents;
            public List<LivePreviewSegment> Segments = new List<LivePreviewSegment>();
            public LiveSelectionDiagnosis Diagnosis;
        }

        private void NavigateToLayer(string layName)
        {
            if (string.IsNullOrEmpty(layName)) return;

            var match = _layerMappings.FirstOrDefault(x => string.Equals(x.SourceLayer, layName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                if (mainTabControl != null && mainTabControl.SelectedIndex != 0)
                {
                    mainTabControl.SelectedIndex = 0;
                }

                dgLayerMapping.SelectedItem = match;
                dgLayerMapping.ScrollIntoView(match);
                txtDialogStatus.Text = string.Format("Đã định vị Layer: {0} ({1} thực thể) từ nét chọn trên CAD.", match.SourceLayer, match.EntityCount);
            }
        }

        private void BtnPickLayerFromCad_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            this.Hide();
            try
            {
                Editor ed = doc.Editor;
                PromptEntityOptions peo = new PromptEntityOptions("\n[AI Định Vị Layer] Click chọn 1 nét bất kỳ trên AutoCAD: ");
                peo.AllowNone = true;
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK)
                {
                    string layName = null;
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent != null)
                        {
                            layName = ent.Layer;
                        }
                        tr.Commit();
                    }

                    if (!string.IsNullOrEmpty(layName))
                    {
                        NavigateToLayer(layName);
                        ed.WriteMessage(string.Format("\n✅ Đã định vị Layer: '{0}' trên bảng NTT TOOLS (Không zoom CAD).\n", layName));
                    }
                }
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[AI Lỗi Pick Nét] " + ex.Message);
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        
        private bool _cancelScanRequested = false;
        private bool _isScanning = false;

        private void BtnCancelScan_Click(object sender, RoutedEventArgs e)
        {
            _cancelScanRequested = true;
            if (txtScanProgressText != null) txtScanProgressText.Text = "Đang hủy...";
        }

        /// <summary>
        /// NÚT 1-CLICK TRONG KHUNG LIVE CAD INSPECTOR:
        /// Chạy bộ AI Chẩn đoán đồng bộ 100% (Tường tiếp giáp cột, Cửa theo tri thức v27/v35/v36, Lọc nan lam >= 3 thanh, Bảo toàn Cột L/T)
        /// và tự động áp dụng đổi Layer ngay lập tức cho toàn bộ thực thể đạt ngưỡng tin cậy >= 85%.
        /// </summary>
        private void BtnLiveAutoStandardizeAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            _isScanning = true;
            _cancelScanRequested = false;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            if (btnLiveAutoStandardizeAll != null) btnLiveAutoStandardizeAll.IsEnabled = false;
            if (btnAiLiveAutoStandardizeAll != null) btnAiLiveAutoStandardizeAll.IsEnabled = false;

            Action<int, string> progress = delegate(int pct, string msg)
            {
                string stepText = string.Format("⏳ [{0}%] {1} ({2:F1}s)", pct, msg, sw.Elapsed.TotalSeconds);
                if (txtLiveAiDiagnosisReason != null) txtLiveAiDiagnosisReason.Text = stepText;
                if (txtAiLiveAiDiagnosisReason != null) txtAiLiveAiDiagnosisReason.Text = stepText;
                txtDialogStatus.Text = stepText;
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
            };

            try
            {
                bool isStep2Only = (chkStep2PatternOnly != null && chkStep2PatternOnly.IsChecked == true) ||
                                   (chkAiStep2PatternOnly != null && chkAiStep2PatternOnly.IsChecked == true);

                if (isStep2Only)
                {
                    var activeStep2Patterns = ShapePatternLearningService.GetActiveStep2Patterns();
                    if (activeStep2Patterns.Count == 0)
                    {
                        MessageBox.Show(
                            "Chưa có mẫu nào được dạy ở Bước 2! Hãy chọn các nét mẫu trên CAD rồi bấm '⚡ Đổi Layer' trước.",
                            "Thông Báo Bước 2 → Bước 3", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    progress(25, string.Format("Chế độ Bước 3: Chỉ tìm đúng {0} mẫu đã dạy ở Bước 2 (Không đụng Layer khác)...", activeStep2Patterns.Count));
                    var step2SrcLayers = ShapePatternLearningService.GetActiveStep2SourceLayers();
                    var step2Matches = ShapePatternLearningService.FindMatchingClustersInScope(
                        doc, _currentBounds, activeStep2Patterns, step2SrcLayers);

                    progress(80, string.Format("Tìm thấy {0} cụm khớp mẫu Bước 2, đang chuyển layer...", step2Matches.Count));
                    int movedStep2 = ShapePatternLearningService.ApplyMatchedClusters(doc, step2Matches);
                    sw.Stop();

                    ReloadLayers();
                    try { doc.Editor.UpdateScreen(); } catch { }

                    string step2Ok = string.Format("✅ Bước 3: Đã chuẩn hóa {0} cụm ({1} đối tượng) theo đúng Bước 2 ({2:F1}s)",
                        step2Matches.Count, movedStep2, sw.Elapsed.TotalSeconds);
                    string patNames = string.Join(", ", activeStep2Patterns.Select(p => p.Name).Take(3).ToArray());
                    string step2Detail = string.Format("Mẫu áp dụng ({0}): {1} | Giữ nguyên 100% layer khác.",
                        activeStep2Patterns.Count, patNames);
                    var step2Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));
                    if (txtLiveAiSuggestedLayer != null) { txtLiveAiSuggestedLayer.Text = step2Ok; txtLiveAiSuggestedLayer.Foreground = step2Brush; }
                    if (txtLiveAiDiagnosisReason != null) txtLiveAiDiagnosisReason.Text = step2Detail;
                    if (txtAiLiveAiSuggestedLayer != null) { txtAiLiveAiSuggestedLayer.Text = step2Ok; txtAiLiveAiSuggestedLayer.Foreground = step2Brush; }
                    if (txtAiLiveAiDiagnosisReason != null) txtAiLiveAiDiagnosisReason.Text = step2Detail;
                    txtDialogStatus.Text = step2Ok + " — " + step2Detail;
                    return;
                }

                var modeInfo = LayerLearningService.AnalyzeDrawingLayerMode(doc, _currentBounds);
                progress(1, modeInfo.Description);

                // BƯỚC TIỀN KIỂM (KHÔNG ĐỔI LAYER): Quét toàn bản vẽ bằng Hình học + Tri thức (Lũy đẳng 100%)
                var list = CadLayoutProcessorService.ScanSmartGeometryAndPatterns(
                    doc, _currentBounds, true, progress, delegate { return _cancelScanRequested; });

                int savedPatternsCnt = ShapePatternLearningService.GetPatternCount();
                if (savedPatternsCnt > 0)
                {
                    progress(94, string.Format("Đối chiếu {0} mẫu hình học phòng ví dụ đã học...", savedPatternsCnt));
                    ShapePatternLearningService.UpgradeClassificationsByLearnedShapes(doc, _currentBounds, list);
                }

                if (!modeInfo.IsLayerZeroDrawing)
                {
                    progress(96, "Học theo Layer có sẵn của bản vẽ (Ngưỡng đồng nhất >= 75%)...");
                    LayerLearningService.RefineAndLearnLayers(doc, _currentBounds, list, _layerMappings);
                }

                sw.Stop();
                _suspiciousItems.Clear();
                foreach (var it in list.OrderByDescending(x => x.NeedsReview).ThenBy(x => x.Confidence))
                {
                    _suspiciousItems.Add(it);
                }

                var unknownItems = list.Where(x => x.NeedsReview || x.IsOutsideKnowledge).ToList();
                int knownCount = list.Count - unknownItems.Count;

                if (unknownItems.Count > 0)
                {
                    var warnBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x91, 0x00));
                    string preMsg = string.Format("⚠️ Phát hiện {0} đối tượng mới ngoài vùng tri thức!", unknownItems.Count);
                    if (txtLiveAiSuggestedLayer != null)
                    {
                        txtLiveAiSuggestedLayer.Text = preMsg;
                        txtLiveAiSuggestedLayer.Foreground = warnBrush;
                    }
                    if (txtAiLiveAiSuggestedLayer != null)
                    {
                        txtAiLiveAiSuggestedLayer.Text = preMsg;
                        txtAiLiveAiSuggestedLayer.Foreground = warnBrush;
                    }
                    txtDialogStatus.Text = preMsg + " Đang chờ kỹ sư xác nhận...";

                    UnknownEntitiesReviewDialog reviewDlg = new UnknownEntitiesReviewDialog(doc, unknownItems, knownCount);
                    reviewDlg.Owner = this;
                    if (reviewDlg.ShowDialog() != true)
                    {
                        txtDialogStatus.Text = string.Format("⛔ Đã dừng trước khi đổi layer ({0} đối tượng ngoài tri thức đang chờ kiểm tra). Bản vẽ giữ nguyên 100%.", unknownItems.Count);
                        return;
                    }
                }

                // Tự động ghi nhớ các Layer đã học chuẩn khi bản vẽ có sẵn Layer & được xác nhận
                if (!modeInfo.IsLayerZeroDrawing)
                {
                    LayerLearningService.SaveConfirmedLayerMappings(_layerMappings);
                }

                // Áp dụng đổi layer cho phần đã khớp tri thức (>=85%) + các mục ngoài tri thức được kỹ sư tích chọn xác nhận
                var toChange = list.Where(x =>
                    x.IsSelected &&
                    !string.IsNullOrEmpty(x.SuggestedLayer) &&
                    !x.SuggestedLayer.StartsWith("--") &&
                    !string.Equals(x.CurrentLayer, x.SuggestedLayer, StringComparison.OrdinalIgnoreCase)).ToList();

                int changed = CadLayoutProcessorService.ApplySuspiciousClassifications(doc, toChange);

                ReloadLayers();
                try { doc.Editor.UpdateScreen(); } catch { }

                int wallCnt = list.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerTuong && !x.NeedsReview);
                int doorCnt = list.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerCua && !x.NeedsReview);
                int colCnt = list.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerCot && !x.NeedsReview);
                int gridCnt = list.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerGrid && !x.NeedsReview);
                int furnCnt = list.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerNoiThat && !x.NeedsReview);
                int racCnt = list.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerRac && !x.NeedsReview);

                string summary = string.Format("Tường: {0} | Cửa: {1} | Cột: {2} | Trục Grid: {3} | Nội thất: {4} | Rác 1 nét: {5}",
                    wallCnt, doorCnt, colCnt, gridCnt, furnCnt, racCnt);

                var okBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));
                if (txtLiveAiSuggestedLayer != null)
                {
                    txtLiveAiSuggestedLayer.Text = string.Format("✅ Đã chuẩn hóa {0} đối tượng ({1:F1}s)", changed, sw.Elapsed.TotalSeconds);
                    txtLiveAiSuggestedLayer.Foreground = okBrush;
                }
                if (txtLiveAiDiagnosisReason != null) txtLiveAiDiagnosisReason.Text = summary;

                if (txtAiLiveAiSuggestedLayer != null)
                {
                    txtAiLiveAiSuggestedLayer.Text = string.Format("✅ Đã chuẩn hóa {0} đối tượng ({1:F1}s)", changed, sw.Elapsed.TotalSeconds);
                    txtAiLiveAiSuggestedLayer.Foreground = okBrush;
                }
                if (txtAiLiveAiDiagnosisReason != null) txtAiLiveAiDiagnosisReason.Text = summary;

                txtDialogStatus.Text = string.Format("⚡ Hoàn tất trong {0:F1}s: Đã di dời chuẩn xác {1} đối tượng ({2}).",
                    sw.Elapsed.TotalSeconds, changed, summary);
            }
            catch (OperationCanceledException)
            {
                txtDialogStatus.Text = "⛔ Đã hủy. Bản vẽ không bị thay đổi.";
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi chuẩn hóa AI: " + ex.Message;
            }
            finally
            {
                _isScanning = false;
                if (btnLiveAutoStandardizeAll != null) btnLiveAutoStandardizeAll.IsEnabled = true;
                if (btnAiLiveAutoStandardizeAll != null) btnAiLiveAutoStandardizeAll.IsEnabled = true;
            }
        }

        /// <summary>
        /// 1 CLICK: AI quét & phân loại toàn bộ (trong khung bao hoặc toàn bản vẽ), có tiến trình + Hủy.
        /// Mục chắc chắn (>=85%) tích sẵn; mục nghi ngờ xếp đầu danh sách, bỏ tích để kỹ sư kiểm tra.
        /// </summary>
        private void BtnQuickScanAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            _isScanning = true;
            _cancelScanRequested = false;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            btnQuickScanAll.IsEnabled = false;
            pnlScanProgress.Visibility = System.Windows.Visibility.Visible;
            pbScan.Value = 0;
            txtScanProgressText.Text = "Khởi động AI...";

            Action<int, string> progress = delegate(int pct, string msg)
            {
                pbScan.Value = pct;
                txtScanProgressText.Text = msg;
                txtScanElapsed.Text = string.Format("{0:F1} s", sw.Elapsed.TotalSeconds);
                // Bơm message cho WPF vẽ lại & nhận click Hủy (cùng Main Thread với AutoCAD)
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
            };

            try
            {
                var list = CadLayoutProcessorService.ScanSmartGeometryAndPatterns(
                    doc, _currentBounds, true, progress, delegate { return _cancelScanRequested; });

                // Mục nghi ngờ lên đầu, sau đó nhóm theo layer đề xuất
                var ordered = list.OrderByDescending(x => x.NeedsReview)
                                  .ThenBy(x => x.Confidence)
                                  .ThenBy(x => x.SuggestedLayer).ToList();

                _suspiciousItems.Clear();
                foreach (var it in ordered) _suspiciousItems.Add(it);

                sw.Stop();
                int review = ordered.Count(x => x.NeedsReview);
                int wallCnt = ordered.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerTuong);
                int doorCnt = ordered.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerCua);
                int furnCnt = ordered.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerNoiThat);
                txtDialogStatus.Text = string.Format(
                    "⚡ Xong trong {0:F1}s: {1} đối tượng (Tường {2} | Cửa {3} | Nội thất {4}) — {5} mục cần kiểm tra (đầu danh sách, chưa tích).",
                    sw.Elapsed.TotalSeconds, ordered.Count, wallCnt, doorCnt, furnCnt, review);
            }
            catch (OperationCanceledException)
            {
                txtDialogStatus.Text = "⛔ Đã hủy quét. Bản vẽ không bị thay đổi.";
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi quét AI: " + ex.Message;
            }
            finally
            {
                _isScanning = false;
                btnQuickScanAll.IsEnabled = true;
                pnlScanProgress.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnScanSmartGeometry_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                var list = CadLayoutProcessorService.ScanSmartGeometryAndPatterns(doc, _currentBounds, true);
                _suspiciousItems.Clear();
                foreach (var item in list)
                {
                    _suspiciousItems.Add(item);
                }

                int wallCnt = _suspiciousItems.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerTuong);
                int doorCnt = _suspiciousItems.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerCua);
                int furnCnt = _suspiciousItems.Count(x => x.SuggestedLayer == CadLayoutProcessorService.LayerNoiThat);
                txtDialogStatus.Text = string.Format("🧠 Chẩn đoán {0} đối tượng (Tường giáp cột: {1} | Cửa: {2} | Nội thất/Nan lam: {3}).",
                    _suspiciousItems.Count, wallCnt, doorCnt, furnCnt);
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi khi quét chẩn đoán hình học: " + ex.Message;
            }
        }

        private void BtnScanUnpaired_Click(object sender, RoutedEventArgs e)
        {
            BtnScanSmartGeometry_Click(sender, e);
        }

        private void BtnScanFurnitureGnn_Click(object sender, RoutedEventArgs e)
        {
            BtnScanSmartGeometry_Click(sender, e);
        }

        private void BtnSelectAllSuspicious_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnselected = _suspiciousItems.Any(x => !x.IsSelected);
            foreach (var item in _suspiciousItems)
            {
                item.IsSelected = anyUnselected;
            }
        }

        private void BtnApplyQuickBatch_Click(object sender, RoutedEventArgs e)
        {
            string target = cboQuickBatchLayer.SelectedItem as string;
            if (string.IsNullOrEmpty(target)) return;

            int changed = 0;
            foreach (var item in _suspiciousItems)
            {
                if (item.IsSelected)
                {
                    item.SuggestedLayer = target;
                    changed++;
                }
            }
            txtDialogStatus.Text = string.Format("Đã gán nhanh '{0}' cho {1} dòng đang chọn.", target, changed);
        }

        private void DgSuspicious_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = dgSuspicious.SelectedItem as SuspiciousEntityItem;
            if (selected != null && !string.IsNullOrEmpty(selected.HandleStr))
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    CadLayoutProcessorService.ZoomToEntity(doc, selected.HandleStr);
                }
            }
        }
private void BtnApplyClassifyAll_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                int count = CadLayoutProcessorService.ApplySuspiciousClassifications(doc, _suspiciousItems.ToList());
                MessageBox.Show(string.Format("Đã phân loại và di dời thành công {0} đối tượng nội thất / vách kính về đúng Layer AI!", count),
                    "Phân Loại Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                txtDialogStatus.Text = string.Format("Đã đổi Layer thành công cho {0} đối tượng.", count);
                ReloadLayers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi phân loại đối tượng: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnScanSuspicious_Click(object sender, RoutedEventArgs e)
        {
            BtnScanFurnitureGnn_Click(sender, e);
        }

        private void BtnZoomCad_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgSuspicious.SelectedItem as SuspiciousEntityItem;
            if (selected == null)
            {
                MessageBox.Show("Vui lòng chọn một dòng đối tượng trong danh sách để Zoom!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                CadLayoutProcessorService.ZoomToEntity(doc, selected.HandleStr);
            }
        }

        private void DgSuspicious_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnZoomCad_Click(sender, e);
        }

        private void BtnPackageDoors_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                int blkCount = CadLayoutProcessorService.PackageDoorBlocks(doc, _currentBounds);
                MessageBox.Show(string.Format("Đã gom cụm và đóng gói thành công {0} Block cửa độc lập trên layer AI_XD_CUA!", blkCount), 
                    "Đóng Gói Block Cửa Hoàn Tất", MessageBoxButton.OK, MessageBoxImage.Information);
                txtDialogStatus.Text = string.Format("Đã tạo {0} Block cửa (AI_DOOR_xxx).", blkCount);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đóng gói block: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAutoPlaceText_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!_currentBounds.HasValue)
            {
                _currentBounds = CadLayoutProcessorService.DetectExistingKhungBao(doc);
            }

            try
            {
                List<CoPilotRoomItem> roomsToPlace = (_roomItems != null && _roomItems.Count > 0)
                    ? _roomItems.ToList()
                    : null;

                int textCount = CadLayoutProcessorService.AutoPlaceRoomTexts(doc, roomsToPlace, _textConfig, _currentBounds);
                if (textCount == 0)
                {
                    MessageBox.Show("Không tìm thấy đường bao phòng khép kín nào trên layer DUONG_BAO trong phạm vi khung bao!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show(string.Format("Đã tự động rải {0} nhãn MText (Tên phòng & Diện tích S) vào tâm phòng trên layer DUONG_BAO_TEXT!", textCount),
                    "Rải Text Hoàn Tất", MessageBoxButton.OK, MessageBoxImage.Information);
                txtDialogStatus.Text = string.Format("Đã rải {0} nhãn MText trên layer DUONG_BAO_TEXT theo cấu hình Tab 5.", textCount);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi rải text: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

                private void UpdateRoomKpi()
        {
            string totalRoomsStr = string.Format("{0} phòng", _roomItems.Count);
            string totalAreaStr = string.Format("{0:F2} m²", _roomItems.Sum(r => r.AreaM2));
            string totalPerimeterStr = string.Format("{0:F2} m", _roomItems.Sum(r => r.PerimeterM));
            string totalWcStr = string.Format("{0} WC (-50mm)", _roomItems.Count(r => r.IsWc));
            string totalHopKtStr = string.Format("{0} Hộp KT", _roomItems.Count(r => (r.RoomName ?? r.Name ?? "").IndexOf("KT", StringComparison.OrdinalIgnoreCase) >= 0));
            string totalBanCongStr = string.Format("{0} Ban Công", _roomItems.Count(r => (r.RoomName ?? r.Name ?? "").IndexOf("BAN CÔNG", StringComparison.OrdinalIgnoreCase) >= 0 || (r.RoomName ?? r.Name ?? "").IndexOf("LOGIA", StringComparison.OrdinalIgnoreCase) >= 0));

            if (txtKpiTotalRooms != null) txtKpiTotalRooms.Text = totalRoomsStr;
            if (txtKpiTotalArea != null) txtKpiTotalArea.Text = totalAreaStr;
            if (txtKpiTotalPerimeter != null) txtKpiTotalPerimeter.Text = totalPerimeterStr;
            if (txtKpiTotalWc != null) txtKpiTotalWc.Text = totalWcStr;
            if (txtKpiTotalHopKt != null) txtKpiTotalHopKt.Text = totalHopKtStr;
            if (txtKpiTotalBanCong != null) txtKpiTotalBanCong.Text = totalBanCongStr;

            if (txtAiKpiRooms != null) txtAiKpiRooms.Text = totalRoomsStr;
            if (txtAiKpiArea != null) txtAiKpiArea.Text = totalAreaStr;
            if (txtAiKpiPerimeter != null) txtAiKpiPerimeter.Text = totalPerimeterStr;
            if (txtAiKpiWc != null) txtAiKpiWc.Text = totalWcStr;
            if (txtAiKpiHopKt != null) txtAiKpiHopKt.Text = totalHopKtStr;
            if (txtAiKpiBanCong != null) txtAiKpiBanCong.Text = totalBanCongStr;
        }

        private void BtnTriggerMultiAgent_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // KHÓA CỨNG PHẠM VI (THE HARD 100% GATE): Tự động phát hiện hoặc chặn nếu không có khung bao
            if (!_currentBounds.HasValue)
            {
                _currentBounds = CadLayoutProcessorService.DetectExistingKhungBao(doc);
            }

            if (!_currentBounds.HasValue)
            {
                MessageBox.Show("⚠️ [CHẶN TUYỆT ĐỐI] Bản vẽ chưa có Khung Bao (layer AI_XD_KHUNG_BAO)!\n\nQuy chuẩn bắt buộc: Tính năng chỉ được phép hoạt động trong phạm vi Khung Bao đã xác lập để tránh làm ảnh hưởng đến các tầng khác của CĐT.\n\nVui lòng bấm 'Quét Khung Mặt Bằng (CAD Pick)' để chọn vùng trước khi chạy.", "Chặn Phạm Vi Xử Lý", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                txtDialogStatus.Text = "⚡ Chỉ Huy Trưởng đang điều phối Multi-Agent tính toán bao phòng thông thủy...";

                List<CoPilotRoomItem> rooms = CoPilotRoomBoundaryService.LoadMultiAgentRoomPolygons();
                if (rooms == null || rooms.Count == 0)
                {
                    var execRes = CoPilotRoomBoundaryService.ExecuteAllRooms(doc, true);
                    if (execRes.Success && execRes.Rooms != null)
                    {
                        rooms = execRes.Rooms;
                    }
                }

                _roomItems.Clear();
                if (rooms != null)
                {
                    Extents3d kb = _currentBounds.Value;

                    foreach (var r in rooms)
                    {
                        // Kiểm tra tọa độ: Chỉ nhận phòng nằm trọn vẹn trong khung bao (dung sai 10mm)
                        double rx = r.CenterX != 0 ? r.CenterX : (r.Vertices != null && r.Vertices.Count > 0 ? r.Vertices[0].X : 0);
                        double ry = r.CenterY != 0 ? r.CenterY : (r.Vertices != null && r.Vertices.Count > 0 ? r.Vertices[0].Y : 0);
                        if (rx < kb.MinPoint.X - 10.0 || rx > kb.MaxPoint.X + 10.0 ||
                            ry < kb.MinPoint.Y - 10.0 || ry > kb.MaxPoint.Y + 10.0)
                        {
                            continue;
                        }

                        string nu = (r.RoomName ?? r.Name ?? "").ToUpperInvariant();
                        bool isWc = r.IsWc;
                        bool isKt = nu.Contains("KT") || nu.Contains("GEN");
                        bool isBc = nu.Contains("BAN CÔNG") || nu.Contains("LOGIA") || nu.Contains("LÔ GIA");
                        bool isCorridor = nu.Contains("HÀNH LANG") || nu.Contains("SẢNH");
                        bool isStairs = nu.Contains("THANG");

                        if (isWc && chkRoomWc.IsChecked != true) continue;
                        if (isKt && chkRoomHopKt.IsChecked != true) continue;
                        if (isBc && chkRoomBanCong.IsChecked != true) continue;
                        if (isCorridor && chkRoomCorridor.IsChecked != true) continue;
                        if (isStairs && chkRoomStairs.IsChecked != true) continue;
                        if (!isWc && !isKt && !isBc && !isCorridor && !isStairs && chkRoomMain.IsChecked != true) continue;

                        _roomItems.Add(r);
                    }
                }

                UpdateRoomKpi();
                txtDialogStatus.Text = string.Format("✅ Chỉ Huy Trưởng hoàn tất: Tìm thấy {0} phòng thông thủy trong phạm vi Khung Bao.", _roomItems.Count);
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi bao phòng: " + ex.Message;
                MessageBox.Show("Lỗi bao phòng: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnQuickRunBoundary_Click(object sender, RoutedEventArgs e)
        {
            if (tabAiProcessing != null)
            {
                tabAiProcessing.IsSelected = true;
            }
            BtnTriggerMultiAgent_Click(sender, e);
            if (_roomItems != null && _roomItems.Count > 0)
            {
                BtnDrawBoundariesCad_Click(sender, e);
            }
        }

        /// <summary>
        /// QUÉT & BAO NHIỀU PHÒNG LIÊN TỤC TRÊN CAD (PICK LIÊN TỤC CHO ĐẾN KHI ENTER/ESC)
        /// Bắt buộc kiểm soát 100% trong phạm vi Khung Bao (AI_XD_KHUNG_BAO)
        /// </summary>
        private void BtnPickRoomCad_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // BẮT BUỘC PHẢI CÓ KHUNG BAO (CORE SPEC): KHÔNG CÓ -> YÊU CẦU QUÉT NGAY
            if (!_currentBounds.HasValue)
            {
                MessageBoxResult mbr = MessageBox.Show(
                    "Quy chuẩn bắt buộc: Bản vẽ cần được xác lập KHUNG BAO (layer AI_XD_KHUNG_BAO) để đảm bảo đường bao phòng không vượt khỏi giới hạn.\n\nBạn có muốn quét 2 điểm để tạo Khung Bao ngay bây giờ không?",
                    "Yêu Cầu Xác Lập Khung Bao", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (mbr == MessageBoxResult.Yes)
                {
                    this.Hide();
                    try
                    {
                        Editor ed = doc.Editor;
                        PromptPointOptions ppo1 = new PromptPointOptions("\n[AI Khung Bao] Chọn điểm góc thứ 1 của khung bao giới hạn: ");
                        PromptPointResult ppr1 = ed.GetPoint(ppo1);
                        if (ppr1.Status == PromptStatus.OK)
                        {
                            PromptCornerOptions pco = new PromptCornerOptions("\n[AI Khung Bao] Chọn điểm góc đối diện: ", ppr1.Value);
                            PromptPointResult ppr2 = ed.GetCorner(pco);
                            if (ppr2.Status == PromptStatus.OK)
                            {
                                _currentBounds = CadLayoutProcessorService.CreateKhungBao(doc, ppr1.Value, ppr2.Value);
                                ReloadLayers();
                                ed.WriteMessage("\n✅ [AI] Đã xác lập Khung Bao trên layer AI_XD_KHUNG_BAO!\n");
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        this.Show();
                        this.Activate();
                    }
                }

                if (!_currentBounds.HasValue)
                {
                    MessageBox.Show("Thao tác bị dừng: Không thể bao phòng khi chưa xác lập Khung Bao giới hạn!", "Chặn Tuyệt Đối", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }
            }

            // Quy tắc cốt lõi: Làm mới toàn bộ dữ liệu nền (xóa sạch đa giác và nhãn cũ trên CAD) và bộ nhớ đệm UI
            CoPilotRoomBoundaryService.ClearAiBoundaries(doc);
            _roomItems.Clear();
            UpdateRoomKpi();
            txtDialogStatus.Text = "Bắt đầu bao phòng trong phạm vi Khung Bao đã khóa...";

            this.Hide();
            try
            {
                var res = CoPilotRoomBoundaryService.ExecutePickMultipleRooms(doc, _currentBounds);
                if (res.Success && res.Rooms != null && res.Rooms.Count > 0)
                {
                    foreach (var r in res.Rooms)
                    {
                        _roomItems.Add(r);
                    }
                    UpdateRoomKpi();
                    txtDialogStatus.Text = string.Format("Đã quét và thêm thành công {0} phòng trong Khung Bao (Tổng S = {1:F2} m²).", res.Rooms.Count, res.TotalAreaM2);
                }
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[AI Lỗi Quét Nhiều Phòng] " + ex.Message);
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        /// <summary>
        /// QUÉT VÙNG CHỌN (WINDOW PICK) CHỨA NHIỀU PHÒNG TRÊN CAD
        /// </summary>
        private void BtnWindowSelectRooms_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Quy tắc cốt lõi: Làm mới toàn bộ dữ liệu nền (xóa sạch đa giác và nhãn cũ trên CAD) và bộ nhớ đệm UI
            CoPilotRoomBoundaryService.ClearAiBoundaries(doc);
            _roomItems.Clear();
            UpdateRoomKpi();
            txtDialogStatus.Text = "Bắt đầu quét vùng chọn mới (đã làm mới bộ nhớ đệm)...";

            this.Hide();
            try
            {
                Editor ed = doc.Editor;
                PromptPointOptions ppo1 = new PromptPointOptions("\n[AI] Chọn điểm góc thứ nhất của vùng nhiều phòng: ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status == PromptStatus.OK)
                {
                    PromptCornerOptions pco = new PromptCornerOptions("\n[AI] Chọn điểm góc đối diện: ", ppr1.Value);
                    PromptPointResult ppr2 = ed.GetCorner(pco);
                    if (ppr2.Status == PromptStatus.OK)
                    {
                        _currentBounds = CadLayoutProcessorService.CreateKhungBao(doc, ppr1.Value, ppr2.Value);
                        ReloadLayers();
                        ed.WriteMessage(string.Format("\n[AI] Đã khóa vùng X[{0:F0} -> {1:F0}], Y[{2:F0} -> {3:F0}]. Đang quét tất cả các phòng bên trong...\n",
                            _currentBounds.Value.MinPoint.X, _currentBounds.Value.MaxPoint.X,
                            _currentBounds.Value.MinPoint.Y, _currentBounds.Value.MaxPoint.Y));

                        // Tự động kích hoạt tính toán bao toàn bộ phòng trong khung
                        var execRes = CoPilotRoomBoundaryService.ExecutePickMultipleRooms(doc, _currentBounds);
                        if (execRes.Success && execRes.Rooms != null && execRes.Rooms.Count > 0)
                        {
                            foreach (var r in execRes.Rooms)
                            {
                                _roomItems.Add(r);
                            }
                            UpdateRoomKpi();
                            txtDialogStatus.Text = string.Format("Đã quét thành công {0} phòng trong vùng chọn (Tổng S = {1:F2} m²).", execRes.Rooms.Count, execRes.TotalAreaM2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[AI Lỗi Quét Vùng] " + ex.Message);
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        private void BtnDrawBoundariesCad_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!_currentBounds.HasValue)
            {
                _currentBounds = CadLayoutProcessorService.DetectExistingKhungBao(doc);
            }

            if (!_currentBounds.HasValue)
            {
                MessageBox.Show("⚠️ [CHẶN TUYỆT ĐỐI] Bản vẽ chưa có Khung Bao (layer AI_XD_KHUNG_BAO)!\n\nVui lòng bấm 'Quét Khung Mặt Bằng (CAD Pick)' để chọn vùng trước khi vẽ lên CAD.", "Chặn Phạm Vi Xử Lý", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedRooms = _roomItems.Where(r => r.IsSelected).ToList();
            if (selectedRooms.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một phòng để vẽ lên CAD!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // ĐẢM BẢO 100% CÁC ĐA GIÁC TRƯỚC KHI VẼ NẰM TRỌN TRONG KHUNG BAO
            if (_currentBounds.HasValue)
            {
                for (int i = 0; i < selectedRooms.Count; i++)
                {
                    var clipped = CoPilotRoomBoundaryService.ClipRoomToBoundary(selectedRooms[i], _currentBounds.Value);
                    if (clipped != null)
                    {
                        selectedRooms[i] = clipped;
                    }
                }
            }

            try
            {
                var res = CoPilotRoomBoundaryService.DrawBoundariesOnCad(doc, selectedRooms, _textConfig, _currentBounds);
                MessageBox.Show(string.Format("Đã xuất vẽ thành công {0} đường bao phòng trên layer DUONG_BAO và gán nhãn MText trên layer DUONG_BAO_TEXT!", selectedRooms.Count),
                    "Vẽ Lên CAD Hoàn Tất", MessageBoxButton.OK, MessageBoxImage.Information);
                txtDialogStatus.Text = string.Format("Đã vẽ {0} đường bao phòng lên AutoCAD theo cấu hình nhãn Tab 5.", selectedRooms.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi vẽ lên CAD: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnInsertCadTable_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var selectedRooms = _roomItems.Where(r => r.IsSelected).ToList();
            if (selectedRooms.Count == 0)
            {
                MessageBox.Show("Vui lòng kích hoạt bao phòng trước khi xuất bảng!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            this.Hide();
            try
            {
                CoPilotRoomBoundaryService.InsertCadRoomTable(doc, selectedRooms);
                txtDialogStatus.Text = string.Format("Đã chèn Bảng Thống Kê {0} phòng lên CAD.", selectedRooms.Count);
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[AI Lỗi Xuất Bảng] " + ex.Message);
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        private void BtnClearBoundaries_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                CoPilotRoomBoundaryService.ClearAiBoundaries(doc);
                _roomItems.Clear();
                UpdateRoomKpi();
                txtDialogStatus.Text = "Đã dọn sạch các đường bao và nhãn MText cũ trên AutoCAD.";
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi khi xóa đường bao: " + ex.Message;
            }
        }

        private void BtnToggleIsolateLayers_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                if (_savedLayerStates == null)
                {
                    // TỰ ĐỘNG DI DỜI TOÀN BỘ NÉT TƯỜNG & CỘT TỪ LAYER 0 SANG AI_XD_TUONG & AI_XD_COT
                    int migrated = CadLayoutProcessorService.MigrateWallEntitiesFromLayerZero(doc, _currentBounds);

                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        _savedLayerStates = CoPilotRoomBoundaryService.IsolateStructuralLayersForBoundary(db, tr);
                        tr.Commit();
                    }
                    ed.UpdateScreen();
                    btnToggleIsolateLayers.Content = "👁️ Hiện Toàn Bộ Layer";
                    btnToggleIsolateLayers.Style = (Style)FindResource("AmberBtnStyle");
                    txtDialogStatus.Text = string.Format("Đã chuyển {0} nét tường/cột từ Layer 0 sang AI_XD_TUONG/COT và cô lập nét cấu trúc.", migrated);
                    ed.WriteMessage(string.Format("\n[NTT TOOLS] 👁️ Đã chuyển {0} nét tường/cột từ Layer 0 sang AI_XD_TUONG và cô lập màn hình chỉ còn Tường, Cột, Cửa!\n", migrated));
                    ReloadLayers();
                }
                else
                {
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        CoPilotRoomBoundaryService.RestoreLayerStates(db, tr, _savedLayerStates);
                        tr.Commit();
                    }
                    CoPilotRoomBoundaryService.SetDiemNoiLayerVisibility(db, false);
                    _savedLayerStates = null;
                    ed.UpdateScreen();
                    btnToggleIsolateLayers.Content = "👁️ Ẩn Layer Phụ";
                    btnToggleIsolateLayers.Style = (Style)FindResource("DarkBtnStyle");
                    txtDialogStatus.Text = "Đã khôi phục hiển thị toàn bộ layer bản vẽ.";
                    ed.WriteMessage("\n[NTT TOOLS] 👁️ Đã khôi phục hiển thị toàn bộ layer bản vẽ.");
                    ReloadLayers();
                }
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi đổi layer: " + ex.Message;
                ed.WriteMessage("\n[NTT TOOLS Lỗi Layer] " + ex.Message);
            }
        }

        private void BtnShowAllLayers_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                int count = CoPilotRoomBoundaryService.UnhideAllLayers(doc.Database);
                doc.Editor.UpdateScreen();
                _savedLayerStates = null;
                if (btnToggleIsolateLayers != null)
                {
                    btnToggleIsolateLayers.Content = "👁️ Ẩn Layer Phụ";
                    btnToggleIsolateLayers.Style = (Style)FindResource("DarkBtnStyle");
                }
                ReloadLayers();
                txtDialogStatus.Text = string.Format("Đã bật sáng và phục hồi toàn bộ {0} layer trên AutoCAD.", count);
                doc.Editor.WriteMessage(string.Format("\n[NTT TOOLS] 👁️ Đã bật sáng và phục hồi toàn bộ {0} layer trên AutoCAD!\n", count));
            }
            catch (Exception ex)
            {
                txtDialogStatus.Text = "Lỗi phục hồi layer: " + ex.Message;
            }
        }

        private void BtnAutoCloseDoorGaps_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            this.Hide();
            try
            {
                int drawn = CoPilotRoomBoundaryService.CloseDoorGapsWithDoubleWallLines(doc, _currentBounds);
                ReloadLayers();
                if (drawn > 0)
                {
                    txtDialogStatus.Text = string.Format("Đã nối thành công {0} ngạch cửa MLINE (layer {1} - tự tắt khi không bao phòng).", drawn, CoPilotRoomBoundaryService.LAYER_DIEM_NOI);
                }
                else
                {
                    txtDialogStatus.Text = "Không có ngạch cửa nào được nối hoặc lệnh đã kết thúc.";
                }
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage("\n[AI Lỗi Nối Ngạch Cửa] " + ex.Message);
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        private void DgRoomResults_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = dgRoomResults.SelectedItem as CoPilotRoomItem;
            if (selected != null)
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    CoPilotRoomBoundaryService.ZoomToRoom(doc, selected);
                    try
                    {
                        doc.Editor.SetImpliedSelection(new ObjectId[0]);
                    }
                    catch { }
                }
            }
        }

        private void DgRoomResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DgRoomResults_SelectionChanged(sender, null);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            SaveWindowSettings();
            UnregisterSelectionEvent();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                CadLayoutProcessorService.UnhighlightAll(doc);

                if (_savedLayerStates != null)
                {
                    try
                    {
                        using (doc.LockDocument())
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            CoPilotRoomBoundaryService.RestoreLayerStates(doc.Database, tr, _savedLayerStates);
                            tr.Commit();
                        }
                        _savedLayerStates = null;
                        doc.Editor.UpdateScreen();
                    }
                    catch { }
                }
            }
        }

        private void LoadWindowSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\TaiAiAddin\CadLayoutDialog"))
                {
                    if (key != null)
                    {
                        object w = key.GetValue("Width");
                        object h = key.GetValue("Height");
                        object l = key.GetValue("Left");
                        object t = key.GetValue("Top");

                        if (w != null && h != null)
                        {
                            double width = Convert.ToDouble(w);
                            double height = Convert.ToDouble(h);
                            if (width >= this.MinWidth) this.Width = width;
                            if (height >= this.MinHeight) this.Height = height;
                        }

                        if (l != null && t != null)
                        {
                            double left = Convert.ToDouble(l);
                            double top = Convert.ToDouble(t);

                            if (left >= SystemParameters.VirtualScreenLeft - 20 &&
                                left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                                top >= SystemParameters.VirtualScreenTop - 20 &&
                                top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
                            {
                                this.WindowStartupLocation = WindowStartupLocation.Manual;
                                this.Left = left;
                                this.Top = top;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveWindowSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\TaiAiAddin\CadLayoutDialog"))
                {
                    if (key != null)
                    {
                        if (this.WindowState == WindowState.Normal)
                        {
                            key.SetValue("Width", this.ActualWidth);
                            key.SetValue("Height", this.ActualHeight);
                            key.SetValue("Left", this.Left);
                            key.SetValue("Top", this.Top);
                        }
                    }
                }
            }
            catch { }
        }

        private void RbTextHeight_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlManualTextHeight == null) return;
            bool isManual = rbManualTextHeight != null && rbManualTextHeight.IsChecked == true;
            pnlManualTextHeight.IsEnabled = isManual;
            pnlManualTextHeight.Opacity = isManual ? 1.0 : 0.5;

            _textConfig.IsAutoTextHeight = !isManual;
            UpdateSampleTextPreview();
        }

        private void TxtManualHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_textConfig == null) return;

            double hMain, hWc, hHopKt;
            if (txtHeightMainRoom != null && double.TryParse(txtHeightMainRoom.Text, out hMain) && hMain > 0)
                _textConfig.HeightMainRoom = hMain;
            if (txtHeightWcBalcony != null && double.TryParse(txtHeightWcBalcony.Text, out hWc) && hWc > 0)
                _textConfig.HeightWcBalcony = hWc;
            if (txtHeightHopKt != null && double.TryParse(txtHeightHopKt.Text, out hHopKt) && hHopKt > 0)
                _textConfig.HeightHopKt = hHopKt;

            UpdateSampleTextPreview();
        }

        private void RbLang_Checked(object sender, RoutedEventArgs e)
        {
            if (_textConfig == null) return;

            RadioButton rb = sender as RadioButton;
            if (rb == null) return;

            if (rb == rbLangVnOnly)
                _textConfig.LanguageMode = TextLanguageMode.VietnameseOnly;
            else if (rb == rbLangVnEn)
                _textConfig.LanguageMode = TextLanguageMode.VietnameseEnglish;
            else if (rb == rbLangVnCn)
                _textConfig.LanguageMode = TextLanguageMode.VietnameseChinese;
            else if (rb == rbLangTri)
                _textConfig.LanguageMode = TextLanguageMode.Trilingual;

            UpdateSampleTextPreview();
        }

        private void UpdateSampleTextPreview()
        {
            if (txtPreviewSampleText == null || _textConfig == null) return;
            string rawMText = _textConfig.BuildMTextContent("PHÒNG NGỦ", 24.50, false);
            txtPreviewSampleText.Text = rawMText.Replace(@"\P", "\n");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

import re

cs_path = r'd:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Views\ElectricalPanelDialog.xaml.cs'
with open(cs_path, 'r', encoding='utf-8') as f:
    code = f.read()

# 1. Update SwitchToPanel to sync Incomer, Capacitor, Title Block, Front View
old_switch_end = '''            AttachLoadEvents();
            RefreshUi();
            DrawCanvasPreview();
        }'''

new_switch_end = '''            if (cboIncomer != null)
            {
                cboIncomer.SelectedValue = _panel.Incomer.ToString();
            }
            if (chkHasCapacitor != null)
            {
                chkHasCapacitor.IsChecked = _panel.HasCapacitorBank;
            }
            if (chkIncludeTitleBlock != null)
            {
                chkIncludeTitleBlock.IsChecked = _params.IncludeTitleBlock;
            }
            if (chkDrawFrontView != null)
            {
                chkDrawFrontView.IsChecked = _params.DrawFrontView;
            }

            AttachLoadEvents();
            RefreshUi();
            DrawCanvasPreview();
        }'''

code = code.replace(old_switch_end, new_switch_end)

# 2. Update Load_PropertyChanged to re-draw canvas when Starter, Control, CableLength change
old_prop_check = 'e.PropertyName == "CableType" || e.PropertyName == "CoreType" || \n                e.PropertyName == "FullCableString" || e.PropertyName == "HasDol"'
new_prop_check = 'e.PropertyName == "CableType" || e.PropertyName == "CoreType" || \n                e.PropertyName == "FullCableString" || e.PropertyName == "HasDol" || \n                e.PropertyName == "Starter" || e.PropertyName == "Control" || e.PropertyName == "CableLength"'
code = code.replace(old_prop_check, new_prop_check)

# 3. Add the action button handlers right after BtnExportExcel_Click
old_export_end = '''                        if (res == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(sfd.FileName);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Có lỗi xảy ra trong quá trình xuất file Excel!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("Lỗi xuất Excel: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }'''

new_export_end = '''                        if (res == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(sfd.FileName);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Có lỗi xảy ra trong quá trình xuất file Excel!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("Lỗi xuất Excel: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_panel == null) return;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Bảng tính Excel hoặc CSV (*.xls;*.csv;*.txt)|*.xls;*.csv;*.txt|Tất cả tệp (*.*)|*.*";
            ofd.Title = "Chọn file danh sách phụ tải để nạp vào sơ đồ";

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    ExcelImportService importer = new ExcelImportService();
                    List<ElectricalLoad> imported = importer.ImportLoads(ofd.FileName);
                    if (imported != null && imported.Count > 0)
                    {
                        _panel.Loads.Clear();
                        foreach (ElectricalLoad l in imported)
                        {
                            _panel.Loads.Add(l);
                        }
                        ExecuteCalculation();
                        txtDialogStatus.Text = string.Format("Đã nạp thành công {0} phụ tải từ file '{1}'!", imported.Count, System.IO.Path.GetFileName(ofd.FileName));
                    }
                    else
                    {
                        MessageBox.Show("Không tìm thấy dữ liệu phụ tải hợp lệ trong file đã chọn!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("Lỗi đọc file: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportSampleTemplate_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "File Excel Mẫu (*.xls)|*.xls";
            sfd.FileName = "Template_DanhSachPhuTai.xls";
            sfd.Title = "Lưu file Excel mẫu chuẩn kỹ sư";

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    ExcelImportService importer = new ExcelImportService();
                    bool ok = importer.CreateSampleTemplate(sfd.FileName);
                    if (ok)
                    {
                        txtDialogStatus.Text = string.Format("Đã tạo file mẫu thành công tại '{0}'!", sfd.FileName);
                        MessageBoxResult res = MessageBox.Show(string.Format("Đã tạo file mẫu Excel chuẩn kỹ thuật thành công!\nFile lưu tại: {0}\n\nBạn có muốn mở file ngay để xem mẫu?", sfd.FileName),
                            "Thành công", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (res == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(sfd.FileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("Lỗi tạo file mẫu: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnOptimizeVoltageDrop_Click(object sender, RoutedEventArgs e)
        {
            if (_panel == null || _panel.Loads == null) return;

            int upgraded = 0;
            foreach (ElectricalLoad load in _panel.Loads)
            {
                if (_calculator.AutoUpsizeCableForVoltageDrop(load, _panel.VoltageV, _panel.PowerFactor))
                {
                    upgraded++;
                }
            }

            ExecuteCalculation();

            if (upgraded > 0)
            {
                txtDialogStatus.Text = string.Format("Đã tự động tối ưu và tăng tiết diện dây cáp cho {0} phụ tải để đạt tiêu chuẩn sụt áp TCVN!", upgraded);
                MessageBox.Show(string.Format("Đã tự động tối ưu tiết diện dây cho {0} lộ phụ tải bị vượt sụt áp!\nTất cả các lộ hiện đã an toàn theo chuẩn TCVN.", upgraded),
                    "Tối ưu sụt áp thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                txtDialogStatus.Text = "Tất cả các tuyến cáp hiện tại đều đã đạt tiêu chuẩn sụt áp cho phép (≤3% chiếu sáng, ≤5% động lực)!";
                MessageBox.Show("Tất cả các tuyến cáp đều đã đạt chuẩn sụt áp TCVN cho phép!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnDrawRiser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("Vui lòng mở một bản vẽ AutoCAD để thực hiện vẽ Cây Riser!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                this.Hide();
                PromptPointResult ppr = doc.Editor.GetPoint("\\nChọn điểm đặt Sơ đồ Cây Phân Phối (Riser Diagram) trên bản vẽ: ");
                Point3d pt = (ppr.Status == PromptStatus.OK) ? ppr.Value : Point3d.Origin;

                _drawingService.DrawRiserDiagram(doc, PanelStorageService.GetAllPanels(), pt);
                this.Show();
                txtDialogStatus.Text = "Đã hoàn thành vẽ Sơ đồ Cây Phân Phối (Riser Diagram) vào bản vẽ AutoCAD!";
            }
            catch (Exception ex)
            {
                this.Show();
                MessageBox.Show(string.Format("Lỗi vẽ Riser Diagram: {0}", ex.Message), "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CboIncomer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _panel == null || cboIncomer == null) return;
            ComboBoxItem item = cboIncomer.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
            {
                string tag = item.Tag.ToString();
                if (tag == "DualATS") _panel.Incomer = IncomerType.DualATS;
                else if (tag == "DualInterlock") _panel.Incomer = IncomerType.DualInterlock;
                else _panel.Incomer = IncomerType.SingleNormal;

                _params.AutoFitForPanel(_panel);
                DrawCanvasPreview();
            }
        }

        private void ChkCapacitor_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _panel == null) return;
            _panel.HasCapacitorBank = (chkHasCapacitor.IsChecked == true);
            ExecuteCalculation();
        }

        private void ChkTitleBlock_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _params == null) return;
            _params.IncludeTitleBlock = (chkIncludeTitleBlock.IsChecked == true);
            DrawCanvasPreview();
        }

        private void ChkFrontView_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _params == null) return;
            _params.DrawFrontView = (chkDrawFrontView.IsChecked == true);
            DrawCanvasPreview();
        }'''

assert old_export_end in code, "Old export end not found!"
code = code.replace(old_export_end, new_export_end)

with open(cs_path, 'w', encoding='utf-8') as f:
    f.write(code)

print("Patch dialog CS part 1 done!")

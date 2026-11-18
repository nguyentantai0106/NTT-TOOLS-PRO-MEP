using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using TaiAiAddin.Models;
using TaiAiAddin.Services;

namespace TaiAiAddin.Views
{
    public partial class UnknownEntitiesReviewDialog : Window
    {
        private readonly Document _doc;
        private readonly ObservableCollection<SuspiciousEntityItem> _unknownItems;

        public UnknownEntitiesReviewDialog(Document doc, List<SuspiciousEntityItem> unknownItems, int knownCount)
        {
            InitializeComponent();
            _doc = doc;
            _unknownItems = new ObservableCollection<SuspiciousEntityItem>(unknownItems ?? new List<SuspiciousEntityItem>());
            dgUnknownItems.ItemsSource = _unknownItems;

            cboBatchAssign.ItemsSource = new List<string>
            {
                CadLayoutProcessorService.LayerTuong,
                CadLayoutProcessorService.LayerCot,
                CadLayoutProcessorService.LayerCua,
                CadLayoutProcessorService.LayerVachKinh,
                CadLayoutProcessorService.LayerNoiThat,
                CadLayoutProcessorService.LayerDimText,
                CadLayoutProcessorService.LayerGrid,
                CadLayoutProcessorService.LayerRac,
                "-- Bỏ Qua (Không Đổi) --"
            };
            cboBatchAssign.SelectedIndex = 4; // AI_XD_NOI_THAT

            txtHeaderTitle.Text = string.Format("⚠️ PHÁT HIỆN {0} ĐỐI TƯỢNG MỚI NGOÀI VÙNG TRI THỨC", _unknownItems.Count);
            txtKnownBadge.Text = string.Format("Đã khớp tri thức: {0}", knownCount);
            txtUnknownBadge.Text = string.Format("Ngoài tri thức: {0}", _unknownItems.Count);
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _unknownItems) item.IsSelected = true;
        }

        private void BtnUnselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _unknownItems) item.IsSelected = false;
        }

        private void BtnBatchAssign_Click(object sender, RoutedEventArgs e)
        {
            string target = cboBatchAssign.SelectedItem as string;
            if (string.IsNullOrEmpty(target)) return;
            foreach (var item in _unknownItems)
            {
                if (item.IsSelected) item.SuggestedLayer = target;
            }
        }

        private void BtnZoomSelected_Click(object sender, RoutedEventArgs e)
        {
            var sel = dgUnknownItems.SelectedItem as SuspiciousEntityItem;
            if (sel != null && _doc != null && !string.IsNullOrEmpty(sel.HandleStr))
            {
                CadLayoutProcessorService.ZoomToEntity(_doc, sel.HandleStr);
            }
        }

        private void DgUnknownItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = dgUnknownItems.SelectedItem as SuspiciousEntityItem;
            if (sel != null && _doc != null && !string.IsNullOrEmpty(sel.HandleStr))
            {
                CadLayoutProcessorService.ZoomToEntity(_doc, sel.HandleStr);
            }
        }

        private void DgUnknownItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnZoomSelected_Click(sender, e);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnApplyKnownOnly_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _unknownItems) item.IsSelected = false;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnConfirmAllSelected_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }
    }
}

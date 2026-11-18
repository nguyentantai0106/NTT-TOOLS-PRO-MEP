using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using TaiAiAddin.Models;
using TaiAiAddin.Services;

namespace TaiAiAddin.Views.Tabs
{
    /// <summary>
    /// UserControl Phân hệ Xử Lý Mặt Bằng (XLMB Multi-Agent V2.6)
    /// Độc lập, module hóa chuẩn WPF MVVM/Clean Architecture
    /// </summary>
    public partial class XlmbTabControl : UserControl
    {
        private List<CoPilotRoomItem> _allCoPilotRooms = new List<CoPilotRoomItem>();

        /// <summary>
        /// Sự kiện yêu cầu chuyển hướng về Phân hệ Điện
        /// </summary>
        public event EventHandler BackToElectricalRequested;

        public XlmbTabControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// KÍCH HOẠT CHỈ HUY TRƯỞNG MULTI-AGENT (CHỌN 1 ĐẾN 4 KHU VỰC CÙNG LÚC)
        /// </summary>
        private void BtnExecuteXlmb_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                bool incMain = chkScopeMain != null && chkScopeMain.IsChecked == true;
                bool incCorridor = chkScopeCorridor != null && chkScopeCorridor.IsChecked == true;
                bool incStair = chkScopeStair != null && chkScopeStair.IsChecked == true;
                bool incWc = chkScopeWc != null && chkScopeWc.IsChecked == true;

                if (!incMain && !incCorridor && !incStair && !incWc)
                {
                    MessageBox.Show("Vui lòng tích chọn ít nhất một khu vực cần bao phòng!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                CoPilotResult res = CoPilotRoomBoundaryService.ExecuteMultiAgentWorkflow(doc, incMain, incCorridor, incStair, incWc);
                UpdateCoPilotResults(res);
            }
        }

        /// <summary>
        /// XUẤT BẢNG THỐNG KÊ PHÒNG VÀO CAD (TABLE)
        /// </summary>
        private void BtnExportCadTable_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                List<CoPilotRoomItem> roomsToExport = (_allCoPilotRooms != null && _allCoPilotRooms.Count > 0)
                    ? _allCoPilotRooms
                    : CoPilotRoomBoundaryService.LoadMultiAgentRoomPolygons();

                CoPilotRoomBoundaryService.InsertCadRoomTable(doc, roomsToExport);
            }
        }

        /// <summary>
        /// LÀM MỚI DỮ LIỆU (XLMB_REFRESH / LAMMOIXLMB): Xóa sạch rác cũ trên CAD, xóa file đệm, reset Palette trắng
        /// </summary>
        private void BtnRefreshXlmbData_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            string msg = CoPilotRoomBoundaryService.RefreshDataAndPurgeHardcode(doc);

            // Xóa trắng toàn bộ dữ liệu mặt bằng cũ trên UI Palette (0 phòng)
            _allCoPilotRooms = new List<CoPilotRoomItem>();

            CoPilotResult res = new CoPilotResult
            {
                Success = true,
                Message = msg,
                Rooms = _allCoPilotRooms,
                RoomCount = 0,
                TotalAreaM2 = 0.0,
                TotalPerimeterM = 0.0,
                WcCount = 0
            };

            UpdateCoPilotResults(res);
            if (lstXlmbRooms != null) lstXlmbRooms.ItemsSource = null;
        }

        /// <summary>
        /// XÓA TOÀN BỘ NÉT BAO VÀ TEXT TRÊN BẢN VẼ
        /// </summary>
        private void BtnClearBoundary_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                CoPilotRoomBoundaryService.ClearAiBoundaries(doc);
                _allCoPilotRooms.Clear();
                if (lstXlmbRooms != null) lstXlmbRooms.ItemsSource = null;
                if (txtXlmbCount != null) txtXlmbCount.Text = "0 phòng";
                if (txtXlmbTotalArea != null) txtXlmbTotalArea.Text = "0.00 m²";
                if (txtXlmbTotalPerim != null) txtXlmbTotalPerim.Text = "0.00 m";
                if (txtXlmbWcCount != null) txtXlmbWcCount.Text = "0 WC";
                if (txtXlmbCurvedCount != null) txtXlmbCurvedCount.Text = "0 Bulge";
                if (txtXlmbSummary != null) txtXlmbSummary.Text = "Đã dọn dẹp sạch toàn bộ các đường bao AI trên bản vẽ.";
            }
        }

        /// <summary>
        /// LỌC DANH SÁCH PHÒNG THEO TẦNG
        /// </summary>
        private void CbXlmbFloorFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyXlmbFloorFilter();
        }

        private void ApplyXlmbFloorFilter()
        {
            if (lstXlmbRooms == null || _allCoPilotRooms == null) return;
            if (cbXlmbFloorFilter == null || cbXlmbFloorFilter.SelectedIndex <= 0)
            {
                lstXlmbRooms.ItemsSource = _allCoPilotRooms;
                return;
            }

            ComboBoxItem cbi = cbXlmbFloorFilter.SelectedItem as ComboBoxItem;
            if (cbi != null)
            {
                string selectedFloor = cbi.Content.ToString();
                List<CoPilotRoomItem> filtered = _allCoPilotRooms.FindAll(r => r.Floor != null && r.Floor.IndexOf(selectedFloor, StringComparison.OrdinalIgnoreCase) >= 0);
                lstXlmbRooms.ItemsSource = filtered;
            }
        }

        /// <summary>
        /// ZOOM PHÓNG TO ĐẾN PHÒNG ĐƯỢC CHỌN
        /// </summary>
        private void LstXlmbRooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstXlmbRooms == null) return;
            CoPilotRoomItem selected = lstXlmbRooms.SelectedItem as CoPilotRoomItem;
            if (selected != null)
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    CoPilotRoomBoundaryService.ZoomToRoom(doc, selected);
                }
            }
        }

        /// <summary>
        /// CẬP NHẬT KẾT QUẢ TỪ COPILOT ROOM BOUNDARY LÊN GIAO DIỆN
        /// </summary>
        public void UpdateCoPilotResults(CoPilotResult res)
        {
            if (res == null) return;

            _allCoPilotRooms = res.Rooms ?? new List<CoPilotRoomItem>();
            ApplyXlmbFloorFilter();

            if (txtXlmbCount != null) txtXlmbCount.Text = string.Format("{0} phòng", res.RoomCount);
            if (txtXlmbTotalArea != null) txtXlmbTotalArea.Text = string.Format("{0:F2} m²", res.TotalAreaM2);
            if (txtXlmbTotalPerim != null) txtXlmbTotalPerim.Text = string.Format("{0:F2} m", res.TotalPerimeterM);
            if (txtXlmbWcCount != null) txtXlmbWcCount.Text = string.Format("{0} WC", res.WcCount);
            if (txtXlmbCurvedCount != null) txtXlmbCurvedCount.Text = string.Format("{0} Bulge", res.CurvedCount);
            if (txtXlmbSummary != null)
            {
                txtXlmbSummary.Text = !string.IsNullOrEmpty(res.Message) ? res.Message : "Đã vẽ hoàn tất các phòng trên CAD!";
            }
        }

        /// <summary>
        /// NÚT QUAY LẠI PHÂN HỆ ĐIỆN
        /// </summary>
        private void BtnBackToElectrical_Click(object sender, RoutedEventArgs e)
        {
            var handler = BackToElectricalRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}

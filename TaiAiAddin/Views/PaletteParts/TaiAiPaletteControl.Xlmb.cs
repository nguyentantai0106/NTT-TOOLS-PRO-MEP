using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TaiAiAddin.Views
{
    public partial class TaiAiPaletteControl : UserControl
    {
        /// <summary>
        /// MỞ MODAL DIALOG TIỀN XỬ LÝ & CHUẨN HÓA MẶT BẰNG AI (7 BƯỚC)
        /// </summary>
        private void BtnOpenLayoutProcessorDialog_Click(object sender, RoutedEventArgs e)
        {
            Commands.ShowLayoutProcessorModelessDialog();
        }
    }
}

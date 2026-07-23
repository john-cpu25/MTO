using System.Windows;
using Autodesk.Revit.UI;
using RincoMTO.Tools.PtReport.ViewModels;

namespace RincoMTO.Tools.PtReport.UI
{
    public partial class PtReportWindow : Window
    {
        public PtReportWindow(UIDocument uidoc)
        {
            InitializeComponent();
            this.DataContext = new PtReportViewModel(uidoc);
        }
    }
}

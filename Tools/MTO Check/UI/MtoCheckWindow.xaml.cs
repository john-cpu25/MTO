using System.Windows;
using Autodesk.Revit.UI;
using RincoMTO.Tools.MtoCheck.ViewModels;

namespace RincoMTO.Tools.MtoCheck.UI
{
    public partial class MtoCheckWindow : Window
    {
        public MtoCheckWindow(UIDocument uidoc)
        {
            InitializeComponent();
            this.DataContext = new MtoCheckViewModel(uidoc);
        }
    }
}

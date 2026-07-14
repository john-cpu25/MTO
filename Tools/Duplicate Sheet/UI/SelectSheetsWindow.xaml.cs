using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Windows.Data;

namespace RincoMTO.Tools.DuplicateSheet.UI
{
    public partial class SelectSheetsWindow : Window
    {
        public List<SheetWrapper> Sheets { get; set; }
        public List<ViewSheet> SelectedSheets { get; private set; }
        public string TargetSheetSeries { get; private set; }
        private ICollectionView _sheetsView;
        private DuplicateSheetEventHandler _handler;
        private Autodesk.Revit.UI.ExternalEvent _exEvent;

        public SelectSheetsWindow(IEnumerable<ViewSheet> availableSheets, List<string> availableSeries, DuplicateSheetEventHandler handler, Autodesk.Revit.UI.ExternalEvent exEvent)
        {
            InitializeComponent();
            
            _handler = handler;
            _exEvent = exEvent;

            Sheets = availableSheets
                .OrderBy(s => s.SheetNumber)
                .Select(s => new SheetWrapper(s))
                .ToList();

            _sheetsView = CollectionViewSource.GetDefaultView(Sheets);
            _sheetsView.Filter = SheetFilter;
            lbSheets.ItemsSource = _sheetsView;
            
            cboSheetSeries.ItemsSource = availableSeries;
            cboSheetSeries.Text = "S10000 SERIES - MTO";
        }

        private void BtnCheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (SheetWrapper sheet in _sheetsView)
            {
                sheet.IsSelected = true;
            }
        }

        private void BtnCheckNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (SheetWrapper sheet in _sheetsView)
            {
                sheet.IsSelected = false;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedSheets = Sheets.Where(s => s.IsSelected).Select(s => s.Sheet).ToList();
            if (SelectedSheets.Count == 0)
            {
                MessageBox.Show("Please select at least one sheet.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TargetSheetSeries = cboSheetSeries.Text;
            
            _handler.SelectedSheetIds = SelectedSheets.Select(s => s.Id).ToList();
            _handler.TargetSeries = TargetSheetSeries;
            _exEvent.Raise();
            
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool SheetFilter(object item)
        {
            if (string.IsNullOrEmpty(TxtSearch.Text))
                return true;

            var sheetWrapper = item as SheetWrapper;
            if (sheetWrapper == null)
                return false;

            return sheetWrapper.DisplayName.IndexOf(TxtSearch.Text, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _sheetsView?.Refresh();
        }
    }

    public class SheetWrapper : System.ComponentModel.INotifyPropertyChanged
    {
        public ViewSheet Sheet { get; }
        public string DisplayName => $"{Sheet.SheetNumber} - {Sheet.Name}";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public SheetWrapper(ViewSheet sheet)
        {
            Sheet = sheet;
        }
    }
}

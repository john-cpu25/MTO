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

        public SelectSheetsWindow(IEnumerable<ViewSheet> availableSheets, List<string> availableSeries, List<ElementType> viewportTypes, List<ViewFamilyType> viewTypes, DuplicateSheetEventHandler handler, Autodesk.Revit.UI.ExternalEvent exEvent)
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
            _sheetsView.GroupDescriptions.Add(new PropertyGroupDescription("Series"));
            lbSheets.ItemsSource = _sheetsView;
            
            cboSheetSeries.ItemsSource = availableSeries;
            cboSheetSeries.Text = "S10000 SERIES - MTO";

            cboViewportType.ItemsSource = viewportTypes;
            cboViewportType.DisplayMemberPath = "Name";
            cboViewportType.SelectedValuePath = "Id";
            
            var defaultVpType = viewportTypes.FirstOrDefault(v => v.Name.Equals("RINCO_Title_GA", System.StringComparison.OrdinalIgnoreCase));
            if (defaultVpType != null)
            {
                cboViewportType.SelectedValue = defaultVpType.Id;
            }
            else if (viewportTypes.Count > 0)
            {
                cboViewportType.SelectedIndex = 0;
            }

            var viewTypeWrappers = new List<ViewTypeWrapper>();
            viewTypeWrappers.Add(new ViewTypeWrapper { Id = ElementId.InvalidElementId, DisplayName = "<None>" });
            
            foreach (var vt in viewTypes.OrderBy(v => v.FamilyName).ThenBy(v => v.Name))
            {
                viewTypeWrappers.Add(new ViewTypeWrapper { Id = vt.Id, Name = vt.Name, DisplayName = $"{vt.FamilyName} : {vt.Name}" });
            }

            cboViewType.ItemsSource = viewTypeWrappers;
            cboViewType.DisplayMemberPath = "DisplayName";
            cboViewType.SelectedValuePath = "Id";
            
            // Look for an existing type named MTO or just set text to MTO
            var defaultMto = viewTypeWrappers.FirstOrDefault(v => v.Name != null && v.Name.Equals("MTO", System.StringComparison.OrdinalIgnoreCase));
            if (defaultMto != null)
            {
                cboViewType.SelectedValue = defaultMto.Id;
            }
            else
            {
                cboViewType.Text = "MTO";
            }
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

            _handler.SelectedSheetIds = SelectedSheets.Select(s => s.Id).ToList();
            
            if (tabControl.SelectedIndex == 2)
            {
                _handler.ActionType = "CopyID";
            }
            else
            {
                _handler.ActionType = "Duplicate";
                
                TargetSheetSeries = cboSheetSeries.Text;
                _handler.TargetSeries = TargetSheetSeries;

            
            if (cboViewportType.SelectedValue is ElementId vpTypeId)
            {
                _handler.TargetViewportTypeId = vpTypeId;
            }
            else
            {
                _handler.TargetViewportTypeId = ElementId.InvalidElementId;
            }

            if (cboViewType.SelectedValue is ElementId viewTypeId && viewTypeId != ElementId.InvalidElementId)
            {
                _handler.TargetViewTypeId = viewTypeId;
                _handler.TargetViewTypeName = string.Empty;
            }
            else
            {
                _handler.TargetViewTypeId = ElementId.InvalidElementId;
                string typedText = cboViewType.Text;
                if (!string.IsNullOrWhiteSpace(typedText) && typedText != "<None>")
                {
                    var wrappers = cboViewType.ItemsSource as List<ViewTypeWrapper>;
                    var match = wrappers?.FirstOrDefault(w => w.DisplayName.Equals(typedText, System.StringComparison.OrdinalIgnoreCase) || (w.Name != null && w.Name.Equals(typedText, System.StringComparison.OrdinalIgnoreCase)));
                    
                    if (match != null && match.Id != ElementId.InvalidElementId)
                    {
                        _handler.TargetViewTypeId = match.Id;
                        _handler.TargetViewTypeName = string.Empty;
                    }
                    else
                    {
                        _handler.TargetViewTypeName = typedText.Trim();
                    }
                }
                else
                {
                    _handler.TargetViewTypeName = string.Empty;
                }
            }
            } // End of else (Duplicate)

            _exEvent.Raise();
            
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCreateParams_Click(object sender, RoutedEventArgs e)
        {
            _handler.ActionType = "CreateParameters";
            _exEvent.Raise();
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

        private SheetWrapper _lastClickedItem;

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = sender as System.Windows.Controls.ListBoxItem;
            if (item == null) return;

            var wrapper = item.DataContext as SheetWrapper;
            if (wrapper == null) return;

            if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) && _lastClickedItem != null)
            {
                e.Handled = true; // Ngăn chặn ListBox tự chọn item (SelectionMode=Multiple) để mình tự xử lý mảng

                bool targetState = !_lastClickedItem.IsSelected; // Đảo trạng thái của cái click trước đó làm chuẩn
                // Hoặc lấy trạng thái của chính dòng đang click (chưa bị đổi) rồi đảo lại. Ở Multiple mode click thì nó sẽ toggle.
                targetState = !wrapper.IsSelected;

                var viewList = _sheetsView.Cast<SheetWrapper>().ToList();
                int startIndex = viewList.IndexOf(_lastClickedItem);
                int endIndex = viewList.IndexOf(wrapper);

                if (startIndex != -1 && endIndex != -1)
                {
                    int min = System.Math.Min(startIndex, endIndex);
                    int max = System.Math.Max(startIndex, endIndex);

                    for (int i = min; i <= max; i++)
                    {
                        viewList[i].IsSelected = targetState;
                    }
                }
            }
            
            _lastClickedItem = wrapper;
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

        public string Series
        {
            get
            {
                var param = Sheet.LookupParameter("RINCO_TB_SHEET SERIES");
                if (param != null && param.HasValue)
                {
                    return param.AsString() ?? "Unknown Series";
                }
                return "Unknown Series";
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public SheetWrapper(ViewSheet sheet)
        {
            Sheet = sheet;
        }
    }

    public class ViewTypeWrapper
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RincoMTO.Tools.MtoCheck.ViewModels
{
    /// <summary>
    /// ViewModel cho MtoCheckWindow.
    /// Hỗ trợ chọn nhiều Source sheets và tự động tìm Target theo quy tắc tên + "_MTO".
    /// </summary>
    public partial class MtoCheckViewModel : ObservableObject
    {
        private UIDocument _uidoc;
        private Document _doc;
        private MtoCheckHandler _handler;
        private ExternalEvent _externalEvent;
        private System.Windows.Threading.Dispatcher _dispatcher;

        /// <summary>Danh sách gốc tất cả sheets (không bị filter).</summary>
        private List<ViewItem> _allSheets = new List<ViewItem>();

        /// <summary>Danh sách sheets hiển thị (sau khi filter theo SearchText).</summary>
        [ObservableProperty]
        private ObservableCollection<ViewItem> _availableSheets = new ObservableCollection<ViewItem>();

        /// <summary>Từ khóa tìm kiếm để lọc danh sách sheets.</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>Kết quả kiểm tra hiển thị trong DataGrid.</summary>
        [ObservableProperty]
        private ObservableCollection<CheckResultItem> _results = new ObservableCollection<CheckResultItem>();

        /// <summary>Thông báo trạng thái cho người dùng.</summary>
        [ObservableProperty]
        private string _statusMessage = "Sẵn sàng kiểm tra. Chọn các sheet gốc và nhấn RUN CHECK.";

        /// <summary>Có thép bị thiếu hay không (để enable/disable nút FIX MISSING).</summary>
        [ObservableProperty]
        private bool _hasMissingItems = false;

        public MtoCheckViewModel(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            _handler = new MtoCheckHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            _handler.NotifyStatus = msg =>
            {
                _dispatcher.Invoke(() => StatusMessage = msg);
            };

            _handler.OnCheckCompleted = () =>
            {
                _dispatcher.Invoke(() =>
                {
                    Results = new ObservableCollection<CheckResultItem>(_handler.Discrepancies);
                    HasMissingItems = _handler.Discrepancies.Count > 0;
                });
            };

            _handler.OnFixCompleted = () =>
            {
                _dispatcher.Invoke(() =>
                {
                    HasMissingItems = false;
                    // Chạy lại check để cập nhật kết quả sau khi fix
                });
            };

            LoadSheets();
        }

        /// <summary>
        /// Khi SearchText thay đổi, tự động lọc lại danh sách sheets hiển thị.
        /// </summary>
        partial void OnSearchTextChanged(string value)
        {
            FilterSheets();
        }

        /// <summary>
        /// Tải danh sách tất cả sheets trong project.
        /// Hiển thị dạng "SheetNumber - SheetName", sắp xếp theo SheetNumber.
        /// </summary>
        private void LoadSheets()
        {
            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            _allSheets.Clear();
            foreach (var s in sheets)
            {
                _allSheets.Add(new ViewItem
                {
                    Id = s.Id,
                    SheetNumber = s.SheetNumber,
                    SheetName = s.Name,
                    DisplayName = $"{s.SheetNumber} - {s.Name}",
                    View = s,
                    IsSelected = false
                });
            }

            FilterSheets();
        }

        /// <summary>
        /// Lọc danh sách sheets theo SearchText (tìm trong cả SheetNumber và SheetName).
        /// </summary>
        private void FilterSheets()
        {
            AvailableSheets.Clear();

            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? _allSheets
                : _allSheets.Where(s =>
                    s.DisplayName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            foreach (var item in filtered)
            {
                AvailableSheets.Add(item);
            }
        }

        private List<ViewPair> GetSelectedViewPairs()
        {
            var selectedSheets = _allSheets.Where(s => s.IsSelected).ToList();
            if (selectedSheets.Count == 0)
            {
                StatusMessage = "Vui lòng chọn ít nhất 1 sheet MTO!";
                return null;
            }

            var allSheetsByName = new Dictionary<string, ViewSheet>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _allSheets)
            {
                if (item.View is ViewSheet vs && !allSheetsByName.ContainsKey(vs.Name))
                {
                    allSheetsByName[vs.Name] = vs;
                }
            }

            var viewPairs = new List<ViewPair>();
            var notFound = new List<string>();

            foreach (var mtoSheet in selectedSheets)
            {
                string sourceName = null;
                if (mtoSheet.SheetName.EndsWith("_MTO", StringComparison.OrdinalIgnoreCase))
                {
                    sourceName = mtoSheet.SheetName.Substring(0, mtoSheet.SheetName.Length - 4);
                }
                else if (mtoSheet.SheetName.EndsWith("MTO", StringComparison.OrdinalIgnoreCase))
                {
                    sourceName = mtoSheet.SheetName.Substring(0, mtoSheet.SheetName.Length - 3);
                }

                if (sourceName != null && allSheetsByName.TryGetValue(sourceName, out ViewSheet sourceSheet))
                {
                    viewPairs.Add(new ViewPair
                    {
                        Source = sourceSheet,
                        Target = mtoSheet.View,
                        SheetName = mtoSheet.DisplayName
                    });
                }
                else
                {
                    notFound.Add(mtoSheet.DisplayName);
                }
            }

            if (notFound.Count > 0)
            {
                string missing = string.Join(", ", notFound);
                StatusMessage = $"Không tìm thấy bản gốc cho: {missing}";
                if (viewPairs.Count == 0) return null;
            }

            return viewPairs;
        }

        /// <summary>
        /// Chạy kiểm tra cho tất cả sheets MTO được chọn.
        /// Với mỗi MTO sheet, tự động tìm sheet gốc bằng cách bỏ "_MTO" khỏi tên.
        /// Source = sheet gốc, Target = sheet MTO đã chọn.
        /// Nếu không tìm thấy sheet gốc → báo lỗi.
        /// </summary>
        [RelayCommand]
        private void RunCheck()
        {
            var viewPairs = GetSelectedViewPairs();
            if (viewPairs == null || viewPairs.Count == 0) return;

            StatusMessage = $"Đang kiểm tra {viewPairs.Count} sheet...";
            Results.Clear();

            _handler.ViewPairs = viewPairs;
            _handler.Action = "Check";
            _externalEvent.Raise();
        }
        
        /// <summary>
        /// Reset màu sắc của các thanh thép trong các sheet MTO đã chọn về mặc định.
        /// </summary>
        [RelayCommand]
        private void ResetColors()
        {
            var viewPairs = GetSelectedViewPairs();
            if (viewPairs == null || viewPairs.Count == 0) return;

            StatusMessage = "Đang reset màu...";
            _handler.ViewPairs = viewPairs;
            _handler.Action = "ResetColors";
            _externalEvent.Raise();
        }

        /// <summary>
        /// Chọn tất cả sheets đang hiển thị (sau filter).
        /// </summary>
        [RelayCommand]
        private void SelectAll()
        {
            foreach (var item in AvailableSheets)
                item.IsSelected = true;
        }

        /// <summary>
        /// Bỏ chọn tất cả sheets đang hiển thị (sau filter).
        /// </summary>
        [RelayCommand]
        private void DeselectAll()
        {
            foreach (var item in AvailableSheets)
                item.IsSelected = false;
        }

        /// <summary>
        /// Cập nhật Target MTO: copy thép thiếu + sửa parameter thay đổi.
        /// Chỉ enable khi có sai lệch sau RUN CHECK.
        /// </summary>
        [RelayCommand]
        private void FixMissing()
        {
            if (!HasMissingItems)
            {
                StatusMessage = "Không có sai lệch nào để cập nhật.";
                return;
            }

            StatusMessage = "Đang cập nhật Target MTO...";
            _handler.Action = "Update";
            _externalEvent.Raise();
        }
    }

    /// <summary>
    /// Item hiển thị trong danh sách sheet với checkbox.
    /// </summary>
    public partial class ViewItem : ObservableObject
    {
        public ElementId Id { get; set; }

        /// <summary>Số hiệu sheet (vd: "S-01").</summary>
        public string SheetNumber { get; set; }

        /// <summary>Tên sheet gốc (vd: "GROUND ZONE 1 - BOTTOM REINFORCEMENT PLAN").</summary>
        public string SheetName { get; set; }

        /// <summary>Tên hiển thị dạng "SheetNumber - SheetName".</summary>
        public string DisplayName { get; set; }

        public View View { get; set; }

        /// <summary>Trạng thái chọn (checkbox) của sheet.</summary>
        [ObservableProperty]
        private bool _isSelected;
    }
}

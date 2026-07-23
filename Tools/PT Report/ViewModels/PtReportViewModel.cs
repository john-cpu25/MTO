using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RincoMTO.Tools.PtReport.ViewModels
{
    /// <summary>
    /// ViewModel cho PtReportWindow.
    /// Quản lý checkbox options, chạy ExternalEvent, và hiển thị kết quả.
    /// </summary>
    public partial class PtReportViewModel : ObservableObject
    {
        private UIDocument _uidoc;
        private Document _doc;
        private PtReportHandler _handler;
        private ExternalEvent _externalEvent;
        private System.Windows.Threading.Dispatcher _dispatcher;

        // === Checkbox Options (giống form Python) ===

        [ObservableProperty]
        private bool _showNumberStrands = true;

        [ObservableProperty]
        private bool _showNumberSplitStrands = false;

        [ObservableProperty]
        private bool _showStrandType = true;

        [ObservableProperty]
        private bool _showLength = true;

        [ObservableProperty]
        private bool _showSplitStrandsLength = false;

        [ObservableProperty]
        private bool _showWeight = true;

        [ObservableProperty]
        private bool _showPanCount = true;

        [ObservableProperty]
        private bool _showAssociatedLevel = false;

        [ObservableProperty]
        private bool _showAssociatedBuilding = false;

        [ObservableProperty]
        private bool _showAssociatedZone = false;

        [ObservableProperty]
        private bool _showPourNumber = false;

        [ObservableProperty]
        private bool _selectAll = false;

        // === Column Visibility (để binding Visibility trong DataGrid) ===

        [ObservableProperty]
        private bool _colNumberStrandsVisible = true;

        [ObservableProperty]
        private bool _colNumberSplitStrandsVisible = false;

        [ObservableProperty]
        private bool _colStrandTypeVisible = true;

        [ObservableProperty]
        private bool _colLengthVisible = true;

        [ObservableProperty]
        private bool _colSplitStrandsLengthVisible = false;

        [ObservableProperty]
        private bool _colWeightVisible = true;

        [ObservableProperty]
        private bool _colPanCountVisible = true;

        [ObservableProperty]
        private bool _colAssociatedLevelVisible = false;

        [ObservableProperty]
        private bool _colAssociatedBuildingVisible = false;

        [ObservableProperty]
        private bool _colAssociatedZoneVisible = false;

        [ObservableProperty]
        private bool _colPourNumberVisible = false;

        // === Results ===

        [ObservableProperty]
        private ObservableCollection<PtReportItem> _reportItems = new ObservableCollection<PtReportItem>();

        [ObservableProperty]
        private string _statusMessage = "Sẵn sàng. Chọn các cột cần hiển thị và nhấn GENERATE REPORT.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
        private bool _hasResults = false;

        public PtReportViewModel(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            _handler = new PtReportHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            _handler.NotifyStatus = msg =>
            {
                _dispatcher.Invoke(() => StatusMessage = msg);
            };

            _handler.OnReportCompleted = () =>
            {
                _dispatcher.Invoke(() => BuildResultTable());
            };
        }

        /// <summary>
        /// Khi SelectAll thay đổi, cập nhật tất cả checkboxes.
        /// </summary>
        partial void OnSelectAllChanged(bool value)
        {
            if (value)
            {
                ShowNumberStrands = true;
                ShowNumberSplitStrands = true;
                ShowStrandType = true;
                ShowLength = true;
                ShowSplitStrandsLength = true;
                ShowWeight = true;
                ShowPanCount = true;
                ShowAssociatedLevel = true;
                ShowAssociatedBuilding = true;
                ShowAssociatedZone = true;
                ShowPourNumber = true;
            }
        }

        /// <summary>
        /// Chạy report: thu thập dữ liệu PT từ active view.
        /// </summary>
        [RelayCommand]
        private void GenerateReport()
        {
            StatusMessage = "Đang chạy report...";
            ReportItems.Clear();
            HasResults = false;

            // Truyền options cho handler
            _handler.Options = new PtReportOptions
            {
                ShowNumberStrands = ShowNumberStrands,
                ShowNumberSplitStrands = ShowNumberSplitStrands,
                ShowStrandType = ShowStrandType,
                ShowLength = ShowLength,
                ShowSplitStrandsLength = ShowSplitStrandsLength,
                ShowWeight = ShowWeight,
                ShowPanCount = ShowPanCount,
                ShowAssociatedLevel = ShowAssociatedLevel,
                ShowAssociatedBuilding = ShowAssociatedBuilding,
                ShowAssociatedZone = ShowAssociatedZone,
                ShowPourNumber = ShowPourNumber
            };

            _externalEvent.Raise();
        }

        /// <summary>
        /// Xây dựng bảng kết quả từ dữ liệu handler trả về.
        /// Thêm hàng Total ở cuối, cập nhật column visibility.
        /// </summary>
        private void BuildResultTable()
        {
            ReportItems.Clear();

            // Thêm tất cả items
            foreach (var item in _handler.ReportItems)
            {
                ReportItems.Add(item);
            }

            // Thêm hàng Total
            if (_handler.ReportItems.Count > 0)
            {
                var totals = _handler.Totals;
                var totalRow = new PtReportItem
                {
                    ElementId = "Total",
                    PtIdDisplay = totals.TendonCount.ToString(),
                    NumberStrands = totals.TotalStrands.ToString(),
                    NumberStrandsValue = totals.TotalStrands,
                    NumberSplitStrands = 0,
                    StrandType = "",
                    Length = $"{totals.TotalLength} mm",
                    LengthValue = totals.TotalLength,
                    SplitStrandLength = "",
                    Weight = totals.TotalWeight,
                    PanCount = totals.TotalPan,
                    AssociatedLevel = "",
                    AssociatedBuilding = "",
                    AssociatedZone = "",
                    PourNumber = "",
                    IsTotalRow = true
                };
                ReportItems.Add(totalRow);
            }

            // Cập nhật column visibility
            ColNumberStrandsVisible = ShowNumberStrands;
            ColNumberSplitStrandsVisible = ShowNumberSplitStrands;
            ColStrandTypeVisible = ShowStrandType;
            ColLengthVisible = ShowLength;
            ColSplitStrandsLengthVisible = ShowSplitStrandsLength;
            ColWeightVisible = ShowWeight;
            ColPanCountVisible = ShowPanCount;
            ColAssociatedLevelVisible = ShowAssociatedLevel;
            ColAssociatedBuildingVisible = ShowAssociatedBuilding;
            ColAssociatedZoneVisible = ShowAssociatedZone;
            ColPourNumberVisible = ShowPourNumber;

            HasResults = ReportItems.Count > 0;
        }

        /// <summary>
        /// Xuất dữ liệu report ra file CSV (chỉ các cột đang hiển thị).
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasResults))]
        private void ExportCsv()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"PT_Report_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                // Build column definitions: (header, value accessor)
                var columns = new List<(string Header, Func<PtReportItem, string> GetValue)>();
                columns.Add(("ID Tag", item => item.ElementId));
                columns.Add(("PT ID", item => item.PtIdDisplay));

                if (ShowNumberStrands)
                    columns.Add(("No. Strands", item => item.NumberStrands));
                if (ShowNumberSplitStrands)
                    columns.Add(("No. Strands Terminated Early", item => item.NumberSplitStrands.ToString()));
                if (ShowStrandType)
                    columns.Add(("Strand Type", item => item.StrandType));
                if (ShowLength)
                    columns.Add(("PT Length", item => item.Length));
                if (ShowSplitStrandsLength)
                    columns.Add(("Split Strands Length", item => item.SplitStrandLength));
                if (ShowWeight)
                    columns.Add(("PT Weight (kg)", item => item.Weight.ToString()));
                if (ShowPanCount)
                    columns.Add(("Pan Count", item => item.PanCount.ToString()));
                if (ShowAssociatedLevel)
                    columns.Add(("PT Associated Level", item => item.AssociatedLevel));
                if (ShowAssociatedBuilding)
                    columns.Add(("PT Associated Building", item => item.AssociatedBuilding));
                if (ShowAssociatedZone)
                    columns.Add(("PT Associated Zone", item => item.AssociatedZone));
                if (ShowPourNumber)
                    columns.Add(("Pour Number", item => item.PourNumber));

                var sb = new StringBuilder();

                // Header row
                sb.AppendLine(string.Join(",", columns.Select(c => EscapeCsv(c.Header))));

                // Data rows
                foreach (var item in ReportItems)
                {
                    sb.AppendLine(string.Join(",", columns.Select(c => EscapeCsv(c.GetValue(item)))));
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                StatusMessage = $"Đã xuất CSV: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Lỗi xuất CSV: {ex.Message}";
            }
        }

        /// <summary>
        /// Escape giá trị CSV: bọc trong dấu ngoặc kép nếu chứa dấu phẩy, xuống dòng hoặc dấu ngoặc kép.
        /// </summary>
        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
    }
}

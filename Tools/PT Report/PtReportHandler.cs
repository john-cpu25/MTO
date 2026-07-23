using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.PtReport
{
    /// <summary>
    /// External event handler để thu thập dữ liệu PT (Post-Tension tendons)
    /// từ active view và tạo bảng báo cáo.
    /// Chuyển đổi từ PT_Report_script.py (pyrevit).
    /// </summary>
    public class PtReportHandler : IExternalEventHandler
    {
        private const string WarningChar = " **";
        private const double FtToMm = 304.8;

        /// <summary>Cài đặt columns cần hiển thị từ ViewModel.</summary>
        public PtReportOptions Options { get; set; } = new PtReportOptions();

        /// <summary>Kết quả sau khi chạy report.</summary>
        public List<PtReportItem> ReportItems { get; private set; } = new List<PtReportItem>();

        /// <summary>Hàng tổng cộng.</summary>
        public PtReportTotals Totals { get; private set; } = new PtReportTotals();

        /// <summary>Callback thông báo trạng thái cho UI.</summary>
        public Action<string> NotifyStatus { get; set; }

        /// <summary>Callback khi report hoàn tất.</summary>
        public Action OnReportCompleted { get; set; }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var activeView = doc.ActiveView;

            try
            {
                RunReport(doc, activeView);
            }
            catch (Exception ex)
            {
                NotifyStatus?.Invoke($"Error: {ex.Message}");
            }
        }

        public string GetName() => "PtReportHandler";

        /// <summary>
        /// Thu thập dữ liệu PT từ active view và tạo bảng báo cáo.
        /// </summary>
        private void RunReport(Document doc, View activeView)
        {
            NotifyStatus?.Invoke("Đang thu thập dữ liệu PT...");

            ReportItems.Clear();

            // 1. Lấy tất cả PT tendons trong active view
            var tendonList = GetAllPT(doc, activeView);

            if (tendonList.Count == 0)
            {
                NotifyStatus?.Invoke("Không tìm thấy PT tendon nào trong active view.");
                OnReportCompleted?.Invoke();
                return;
            }

            // 2. Sắp xếp theo PT ID No.
            tendonList = SortTendonsByPTID(tendonList);

            // 3. Khởi tạo tổng
            int totalStrands = 0;
            int totalLength = 0;
            double totalWeight = 0;
            int totalPan = 0;
            int previousPtId = -1;

            // 4. Duyệt từng tendon và thu thập dữ liệu
            for (int index = 0; index < tendonList.Count; index++)
            {
                var tendon = tendonList[index];
                var item = new PtReportItem();

                // Đọc parameters
                int ptId = GetIntParam(tendon, "PT ID No.");
                int numberStrands = GetIntParam(tendon, "Number of Strands");
                int displaySplitStrands = GetIntParam(tendon, "Split Strands");

                int numberSplitStrands;
                string splitStrandLength;

                if (displaySplitStrands == 0)
                {
                    numberSplitStrands = 0;
                    splitStrandLength = "0 mm";
                }
                else
                {
                    numberSplitStrands = GetIntParam(tendon, "No. Strands Terminated Early");
                    double splitLengthFt = GetDoubleParam(tendon, "Split Strands Length");
                    splitStrandLength = $"{(int)(splitLengthFt * FtToMm)} mm";
                }

                string strandType = GetStringParam(tendon, "Strand Type");
                int length = (int)(GetDoubleParam(tendon, "PT Length") * FtToMm);
                double weight = Math.Round(GetDoubleParam(tendon, "PT Weight"), 1);
                string level = GetStringParam(tendon, "PT Associated Level");
                string building = "-"; // Hardcoded như Python
                string zone = "-";     // Hardcoded như Python
                string pourNumber = GetStringParam(tendon, "PT Pour Number");
                int panCount = GetIntParam(tendon, "Pan Count");

                // Cộng dồn tổng
                totalStrands += numberStrands;
                totalLength += length;
                totalWeight += weight;
                totalPan += panCount;

                // Xử lý warning cho PT ID (không liên tục)
                string ptIdDisplay;
                if (index > 0 && ptId != previousPtId + 1)
                {
                    ptIdDisplay = ptId.ToString() + WarningChar;
                }
                else
                {
                    ptIdDisplay = ptId.ToString();
                }

                // Warning cho Number of Strands
                string numberStrandsDisplay;
                if (numberStrands < 3 || numberStrands > 5)
                {
                    numberStrandsDisplay = numberStrands.ToString() + WarningChar;
                }
                else
                {
                    numberStrandsDisplay = numberStrands.ToString();
                }

                // Warning cho Length
                string lengthDisplay;
                if (length < 5000)
                {
                    lengthDisplay = $"{length} mm{WarningChar}";
                }
                else
                {
                    lengthDisplay = $"{length} mm";
                }

                // Thay thế empty strings
                if (string.IsNullOrEmpty(level)) level = "---";
                if (string.IsNullOrEmpty(building)) building = "---";
                if (string.IsNullOrEmpty(zone)) zone = "---";

                previousPtId = ptId;

                // Tạo item
#if REVIT2024_OR_GREATER
                item.ElementId = tendon.Id.Value.ToString();
#else
                item.ElementId = tendon.Id.IntegerValue.ToString();
#endif
                item.PtIdDisplay = ptIdDisplay;
                item.PtIdValue = ptId;
                item.NumberStrands = numberStrandsDisplay;
                item.NumberStrandsValue = numberStrands;
                item.NumberSplitStrands = numberSplitStrands;
                item.StrandType = strandType;
                item.Length = lengthDisplay;
                item.LengthValue = length;
                item.SplitStrandLength = splitStrandLength;
                item.Weight = weight;
                item.PanCount = panCount;
                item.AssociatedLevel = level;
                item.AssociatedBuilding = building;
                item.AssociatedZone = zone;
                item.PourNumber = pourNumber;
                item.IsWarning = ptIdDisplay.Contains(WarningChar)
                                 || numberStrandsDisplay.Contains(WarningChar)
                                 || lengthDisplay.Contains(WarningChar);

                ReportItems.Add(item);
            }

            // 5. Lưu tổng
            Totals = new PtReportTotals
            {
                TendonCount = tendonList.Count,
                TotalStrands = totalStrands,
                TotalLength = totalLength,
                TotalWeight = Math.Round(totalWeight, 1),
                TotalPan = totalPan
            };

            NotifyStatus?.Invoke($"Hoàn tất. Tìm thấy {tendonList.Count} PT tendons.");
            OnReportCompleted?.Invoke();
        }

        /// <summary>
        /// Lấy tất cả FamilyInstance có parameter "PT ID No." trong view.
        /// Đây là cách xác định PT tendon vì module __PT_display__ không có source code.
        /// </summary>
        private List<FamilyInstance> GetAllPT(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.LookupParameter("PT ID No.") != null)
                .ToList();
        }

        /// <summary>
        /// Sắp xếp tendons theo parameter "PT ID No." tăng dần.
        /// </summary>
        private List<FamilyInstance> SortTendonsByPTID(List<FamilyInstance> tendons)
        {
            return tendons
                .OrderBy(t => GetIntParam(t, "PT ID No."))
                .ToList();
        }

        private int GetIntParam(FamilyInstance fi, string paramName)
        {
            Parameter p = fi.LookupParameter(paramName);
            if (p == null) return 0;
            return p.AsInteger();
        }

        private double GetDoubleParam(FamilyInstance fi, string paramName)
        {
            Parameter p = fi.LookupParameter(paramName);
            if (p == null) return 0.0;
            return p.AsDouble();
        }

        private string GetStringParam(FamilyInstance fi, string paramName)
        {
            Parameter p = fi.LookupParameter(paramName);
            if (p == null) return "";
            return p.AsString() ?? "";
        }
    }

    /// <summary>
    /// Cài đặt columns cần hiển thị trong report.
    /// </summary>
    public class PtReportOptions
    {
        public bool ShowNumberStrands { get; set; } = true;
        public bool ShowNumberSplitStrands { get; set; } = false;
        public bool ShowStrandType { get; set; } = true;
        public bool ShowLength { get; set; } = true;
        public bool ShowSplitStrandsLength { get; set; } = false;
        public bool ShowWeight { get; set; } = true;
        public bool ShowPanCount { get; set; } = true;
        public bool ShowAssociatedLevel { get; set; } = false;
        public bool ShowAssociatedBuilding { get; set; } = false;
        public bool ShowAssociatedZone { get; set; } = false;
        public bool ShowPourNumber { get; set; } = false;
    }

    /// <summary>
    /// Dữ liệu một hàng trong bảng PT Report.
    /// </summary>
    public class PtReportItem
    {
        /// <summary>Element ID để link về Revit element.</summary>
        public string ElementId { get; set; }

        /// <summary>PT ID hiển thị (có thể kèm warning " **").</summary>
        public string PtIdDisplay { get; set; }

        /// <summary>PT ID giá trị số (để sort).</summary>
        public int PtIdValue { get; set; }

        /// <summary>Số strands hiển thị (có thể kèm warning " **").</summary>
        public string NumberStrands { get; set; }

        /// <summary>Số strands giá trị số.</summary>
        public int NumberStrandsValue { get; set; }

        /// <summary>Số strands bị split.</summary>
        public int NumberSplitStrands { get; set; }

        /// <summary>Loại strand.</summary>
        public string StrandType { get; set; }

        /// <summary>Chiều dài hiển thị (có thể kèm warning " **").</summary>
        public string Length { get; set; }

        /// <summary>Chiều dài giá trị số (mm).</summary>
        public int LengthValue { get; set; }

        /// <summary>Chiều dài strands bị split.</summary>
        public string SplitStrandLength { get; set; }

        /// <summary>Trọng lượng (kg).</summary>
        public double Weight { get; set; }

        /// <summary>Số pan.</summary>
        public int PanCount { get; set; }

        /// <summary>Level liên kết.</summary>
        public string AssociatedLevel { get; set; }

        /// <summary>Building liên kết.</summary>
        public string AssociatedBuilding { get; set; }

        /// <summary>Zone liên kết.</summary>
        public string AssociatedZone { get; set; }

        /// <summary>Số đổ bê tông.</summary>
        public string PourNumber { get; set; }

        /// <summary>Có warning hay không (để tô highlight).</summary>
        public bool IsWarning { get; set; }

        /// <summary>Đánh dấu hàng tổng.</summary>
        public bool IsTotalRow { get; set; } = false;
    }

    /// <summary>
    /// Dữ liệu tổng cộng cho bảng PT Report.
    /// </summary>
    public class PtReportTotals
    {
        public int TendonCount { get; set; }
        public int TotalStrands { get; set; }
        public int TotalLength { get; set; }
        public double TotalWeight { get; set; }
        public int TotalPan { get; set; }
    }
}

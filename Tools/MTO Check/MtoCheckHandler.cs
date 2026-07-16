using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.MtoCheck
{
    public class MtoCheckHandler : IExternalEventHandler
    {
        public string Action { get; set; } = string.Empty;

        // Inputs
        public View SourceView { get; set; }
        public View TargetView { get; set; }

        // Outputs
        public List<CheckResultItem> Discrepancies { get; private set; } = new List<CheckResultItem>();

        // Delegates
        public Action<string> NotifyStatus { get; set; }
        public Action OnCheckCompleted { get; set; }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                if (Action == "Check")
                {
                    RunCheck(doc);
                }
            }
            catch (Exception ex)
            {
                NotifyStatus?.Invoke($"Error: {ex.Message}");
            }
        }

        private void RunCheck(Document doc)
        {
            if (SourceView == null || TargetView == null)
            {
                NotifyStatus?.Invoke("Vui lòng chọn đủ Source View và Target View.");
                return;
            }

            Discrepancies.Clear();

            // Collect Detail Items in Source View
            var sourceItems = new FilteredElementCollector(doc, SourceView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // Collect Detail Items in Target View
            var targetItems = new FilteredElementCollector(doc, TargetView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // Dictionary for target items by Unique ID (parameter)
            var targetByUniqueIdParam = new Dictionary<string, FamilyInstance>();
            foreach (var item in targetItems)
            {
                var uniqueIdParam = item.LookupParameter("Unique ID");
                if (uniqueIdParam != null && uniqueIdParam.HasValue)
                {
                    string val = uniqueIdParam.AsString();
                    if (!string.IsNullOrEmpty(val) && !targetByUniqueIdParam.ContainsKey(val))
                    {
                        targetByUniqueIdParam[val] = item;
                    }
                }
            }

            // Dictionary for target items by Element ID (parameter)
            var targetByElementIdParam = new Dictionary<string, FamilyInstance>();
            foreach (var item in targetItems)
            {
                var elemIdParam = item.LookupParameter("Element ID");
                if (elemIdParam != null && elemIdParam.HasValue)
                {
                    string val = elemIdParam.AsString();
                    if (!string.IsNullOrEmpty(val) && !targetByElementIdParam.ContainsKey(val))
                    {
                        targetByElementIdParam[val] = item;
                    }
                }
            }

            // 1. Check for MISSING items in Target View (Exist in Source, not in Target)
            foreach (var sItem in sourceItems)
            {
                string sUniqueId = sItem.UniqueId;
                string sElemId = sItem.Id.ToString();

                bool found = targetByUniqueIdParam.ContainsKey(sUniqueId) || targetByElementIdParam.ContainsKey(sElemId);
                if (!found)
                {
                    Discrepancies.Add(new CheckResultItem
                    {
                        IssueType = "Missing in Target",
                        ElementId = sElemId,
                        FamilyName = sItem.Symbol.FamilyName,
                        Description = $"Thép bị thiếu ở bản sao (Không tìm thấy Element ID: {sElemId} hoặc Unique ID: {sUniqueId})"
                    });
                }
            }

            // 2. Check for EXTRA items in Target View (Exist in Target, but parameter points to a non-existent Source item)
            var sourceIds = new HashSet<string>(sourceItems.Select(s => s.Id.ToString()));
            var sourceUniqueIds = new HashSet<string>(sourceItems.Select(s => s.UniqueId));

            foreach (var tItem in targetItems)
            {
                var uniqueIdParam = tItem.LookupParameter("Unique ID");
                var elemIdParam = tItem.LookupParameter("Element ID");

                string tUniqueIdVal = uniqueIdParam?.AsString();
                string tElemIdVal = elemIdParam?.AsString();

                bool hasTrackingParams = !string.IsNullOrEmpty(tUniqueIdVal) || !string.IsNullOrEmpty(tElemIdVal);

                if (hasTrackingParams)
                {
                    bool sourceExists = false;
                    if (!string.IsNullOrEmpty(tUniqueIdVal) && sourceUniqueIds.Contains(tUniqueIdVal)) sourceExists = true;
                    if (!string.IsNullOrEmpty(tElemIdVal) && sourceIds.Contains(tElemIdVal)) sourceExists = true;

                    if (!sourceExists)
                    {
                        Discrepancies.Add(new CheckResultItem
                        {
                            IssueType = "Extra in Target (Orphaned)",
                            ElementId = tItem.Id.ToString(),
                            FamilyName = tItem.Symbol.FamilyName,
                            Description = $"Thép dư thừa ở bản sao (Bản gốc đã bị xoá Element ID: {tElemIdVal})"
                        });
                    }
                }
            }

            NotifyStatus?.Invoke($"Kiểm tra hoàn tất. Phát hiện {Discrepancies.Count} điểm sai lệch.");
            OnCheckCompleted?.Invoke();
        }

        public string GetName() => "MtoCheckHandler";
    }

    public class CheckResultItem
    {
        public string IssueType { get; set; }
        public string ElementId { get; set; }
        public string FamilyName { get; set; }
        public string Description { get; set; }
    }
}

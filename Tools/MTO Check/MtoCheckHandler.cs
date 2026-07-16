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
            var sourceItems = GetDetailItems(doc, SourceView);

            // Collect Detail Items in Target View
            var targetItems = GetDetailItems(doc, TargetView);

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
                    else
                    {
                        // Compare parameters if it exists in source
                        FamilyInstance sItem = null;
                        if (!string.IsNullOrEmpty(tUniqueIdVal)) 
                            sItem = sourceItems.FirstOrDefault(s => s.UniqueId == tUniqueIdVal);
                        else if (!string.IsNullOrEmpty(tElemIdVal)) 
                            sItem = sourceItems.FirstOrDefault(s => s.Id.ToString() == tElemIdVal);

                        if (sItem != null)
                        {
                            List<string> changedParams = new List<string>();
                            foreach (Parameter tParam in tItem.Parameters)
                            {
                                if (tParam.IsReadOnly) continue;
                                string pName = tParam.Definition.Name;
                                if (pName == "Unique ID" || pName == "Element ID") continue;

                                Parameter sParam = sItem.LookupParameter(pName);
                                if (sParam != null && sParam.StorageType == tParam.StorageType)
                                {
                                    bool isDiff = false;
                                    switch (tParam.StorageType)
                                    {
                                        case StorageType.String:
                                            isDiff = tParam.AsString() != sParam.AsString();
                                            break;
                                        case StorageType.Integer:
                                            isDiff = tParam.AsInteger() != sParam.AsInteger();
                                            break;
                                        case StorageType.Double:
                                            isDiff = Math.Abs(tParam.AsDouble() - sParam.AsDouble()) > 0.0001;
                                            break;
                                        case StorageType.ElementId:
                                            isDiff = tParam.AsElementId() != sParam.AsElementId();
                                            break;
                                    }
                                    if (isDiff) changedParams.Add(pName);
                                }
                            }

                            if (changedParams.Count > 0)
                            {
                                Discrepancies.Add(new CheckResultItem
                                {
                                    IssueType = "Modified",
                                    ElementId = tItem.Id.ToString(),
                                    FamilyName = tItem.Symbol.FamilyName,
                                    Description = $"Bị thay đổi thông số: {string.Join(", ", changedParams)}"
                                });
                            }
                        }
                    }
                }
            }

            NotifyStatus?.Invoke($"Kiểm tra hoàn tất. Phát hiện {Discrepancies.Count} điểm sai lệch.");
            OnCheckCompleted?.Invoke();
        }

        private List<FamilyInstance> GetDetailItems(Document doc, View view)
        {
            var items = new List<FamilyInstance>();
            if (view is ViewSheet sheet)
            {
                var placedViews = sheet.GetAllPlacedViews();
                foreach (var vId in placedViews)
                {
                    var placedView = doc.GetElement(vId) as View;
                    if (placedView != null && placedView.Name.ToUpper().Contains("OVER"))
                    {
                        items.AddRange(new FilteredElementCollector(doc, vId)
                            .OfClass(typeof(FamilyInstance))
                            .OfCategory(BuiltInCategory.OST_DetailComponents)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(fi => fi.Symbol != null && fi.Symbol.FamilyName.Contains("Reo__Reinforcement")));
                    }
                }
                
                // Collect items directly placed on sheet
                items.AddRange(new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol != null && fi.Symbol.FamilyName.Contains("Reo__Reinforcement")));
            }
            else
            {
                items.AddRange(new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol != null && fi.Symbol.FamilyName.Contains("Reo__Reinforcement")));
            }
            return items;
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

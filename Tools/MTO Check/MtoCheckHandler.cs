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

            string GetTrackingUniqueId(FamilyInstance fi)
            {
                string p = fi.LookupParameter("Unique ID")?.AsString();
                return !string.IsNullOrEmpty(p) ? p : fi.UniqueId;
            }

            string GetTrackingElementId(FamilyInstance fi)
            {
                string p = fi.LookupParameter("Element ID")?.AsString();
#if REVIT2024_OR_GREATER
                return !string.IsNullOrEmpty(p) ? p : fi.Id.Value.ToString();
#else
                return !string.IsNullOrEmpty(p) ? p : fi.Id.IntegerValue.ToString();
#endif
            }

            // Dictionary for target items
            var targetByUniqueId = new Dictionary<string, FamilyInstance>();
            var targetByElementId = new Dictionary<string, FamilyInstance>();
            foreach (var item in targetItems)
            {
                string uId = GetTrackingUniqueId(item);
                if (!targetByUniqueId.ContainsKey(uId)) targetByUniqueId[uId] = item;

                string eId = GetTrackingElementId(item);
                if (!targetByElementId.ContainsKey(eId)) targetByElementId[eId] = item;
            }

            // Dictionary for source items
            var sourceByUniqueId = new Dictionary<string, FamilyInstance>();
            var sourceByElementId = new Dictionary<string, FamilyInstance>();
            foreach (var item in sourceItems)
            {
                string uId = GetTrackingUniqueId(item);
                if (!sourceByUniqueId.ContainsKey(uId)) sourceByUniqueId[uId] = item;

                string eId = GetTrackingElementId(item);
                if (!sourceByElementId.ContainsKey(eId)) sourceByElementId[eId] = item;
            }

            // 1. Check for MISSING items in Target View (Exist in Source, not in Target)
            foreach (var sItem in sourceItems)
            {
                string sUniqueId = GetTrackingUniqueId(sItem);
                string sElemId = GetTrackingElementId(sItem);

                bool found = targetByUniqueId.ContainsKey(sUniqueId) || targetByElementId.ContainsKey(sElemId);
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
            foreach (var tItem in targetItems)
            {
                string tUniqueId = GetTrackingUniqueId(tItem);
                string tElemId = GetTrackingElementId(tItem);

                bool sourceExists = sourceByUniqueId.ContainsKey(tUniqueId) || sourceByElementId.ContainsKey(tElemId);

                if (!sourceExists)
                {
                    Discrepancies.Add(new CheckResultItem
                    {
                        IssueType = "Extra in Target (Orphaned)",
                        ElementId = tElemId,
                        FamilyName = tItem.Symbol.FamilyName,
                        Description = $"Thép dư thừa ở bản sao (Không có ở bản gốc Element ID: {tElemId})"
                    });
                }
                else
                {
                    // Compare parameters if it exists in source
                    FamilyInstance sItem = null;
                    if (sourceByUniqueId.ContainsKey(tUniqueId)) sItem = sourceByUniqueId[tUniqueId];
                    else if (sourceByElementId.ContainsKey(tElemId)) sItem = sourceByElementId[tElemId];

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

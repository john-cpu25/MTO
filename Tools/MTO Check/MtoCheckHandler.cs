using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.MtoCheck
{
    /// <summary>
    /// External event handler để kiểm tra sai lệch thép (reinforcement detail items)
    /// giữa Source View (bản gốc) và Target View (bản sao).
    /// Hỗ trợ so sánh nhiều cặp sheet cùng lúc.
    /// Phát hiện 3 loại vấn đề: Missing, Extra, và Parameter Changed.
    /// Hỗ trợ tự động copy thép bị thiếu + tag từ Source → Target.
    /// </summary>
    public class MtoCheckHandler : IExternalEventHandler
    {
        /// <summary>Hành động cần thực thi: "Check" hoặc "Update".</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Danh sách các cặp Source-Target cần kiểm tra.</summary>
        public List<ViewPair> ViewPairs { get; set; } = new List<ViewPair>();

        /// <summary>Danh sách kết quả kiểm tra - chứa các điểm sai lệch phát hiện được.</summary>
        public List<CheckResultItem> Discrepancies { get; private set; } = new List<CheckResultItem>();

        /// <summary>Danh sách thép bị thiếu (lưu lại sau Check để dùng cho Update).</summary>
        public List<MissingItemInfo> MissingItems { get; private set; } = new List<MissingItemInfo>();

        /// <summary>Danh sách thép có parameter thay đổi (lưu lại sau Check để dùng cho Update).</summary>
        public List<ChangedItemInfo> ChangedItems { get; private set; } = new List<ChangedItemInfo>();

        /// <summary>Danh sách ElementId thép dư ở Target (lưu lại sau Check để xóa khi Update).</summary>
        public List<ElementId> ExtraItemIds { get; private set; } = new List<ElementId>();

        /// <summary>Danh sách thép có vị trí thay đổi (lưu lại sau Check để dùng cho Update).</summary>
        public List<LocationChangedItemInfo> LocationChangedItems { get; private set; } = new List<LocationChangedItemInfo>();

        /// <summary>Danh sách view bị thiếu ở Target (lưu lại sau Check để copy khi Update).</summary>
        public List<MissingViewInfo> MissingViews { get; private set; } = new List<MissingViewInfo>();

        /// <summary>Callback thông báo trạng thái cho UI (status message).</summary>
        public Action<string> NotifyStatus { get; set; }

        /// <summary>Callback khi quá trình kiểm tra hoàn tất.</summary>
        public Action OnCheckCompleted { get; set; }

        /// <summary>Callback khi quá trình fix hoàn tất.</summary>
        public Action OnFixCompleted { get; set; }

        /// <summary>
        /// Entry point được gọi bởi Revit ExternalEvent.
        /// Dispatch hành động dựa trên property Action.
        /// </summary>
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
                else if (Action == "Update")
                {
                    RunUpdate(doc);
                }
                else if (Action == "ResetColors")
                {
                    RunResetColors(doc);
                }
            }
            catch (Exception ex)
            {
                NotifyStatus?.Invoke($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Thực hiện kiểm tra sai lệch cho tất cả các cặp View trong ViewPairs.
        /// </summary>
        private void RunCheck(Document doc)
        {
            if (ViewPairs == null || ViewPairs.Count == 0)
            {
                NotifyStatus?.Invoke("Không có cặp sheet nào để kiểm tra.");
                return;
            }

            Discrepancies.Clear();
            MissingItems.Clear();
            MissingViews.Clear();
            ChangedItems.Clear();
            ExtraItemIds.Clear();
            LocationChangedItems.Clear();

            using (Transaction t = new Transaction(doc, "MTO Check - Apply Colors"))
            {
                t.Start();
                try
                {
                    // Duyệt từng cặp Source-Target và kiểm tra
                    foreach (var pair in ViewPairs)
                    {
                        NotifyStatus?.Invoke($"Đang kiểm tra: {pair.SheetName}...");
                        RunCheckForPair(doc, pair);
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    NotifyStatus?.Invoke($"Lỗi khi kiểm tra: {ex.Message}");
                    return;
                }
            }

            NotifyStatus?.Invoke($"Kiểm tra hoàn tất {ViewPairs.Count} sheet. Phát hiện {Discrepancies.Count} điểm sai lệch.");
            OnCheckCompleted?.Invoke();
        }

        private void RunResetColors(Document doc)
        {
            if (ViewPairs == null || ViewPairs.Count == 0) return;

            using (Transaction t = new Transaction(doc, "MTO Check - Reset Colors"))
            {
                t.Start();
                try
                {
                    foreach (var pair in ViewPairs)
                    {
                        var targetItems = GetDetailItems(doc, pair.Target);
                        foreach (var item in targetItems)
                        {
                            View targetView = doc.GetElement(item.OwnerViewId) as View;
                            if (targetView != null)
                            {
                                targetView.SetElementOverrides(item.Id, new OverrideGraphicSettings());
                            }
                        }
                    }
                    t.Commit();
                    NotifyStatus?.Invoke("Đã reset màu về trạng thái ban đầu.");
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    NotifyStatus?.Invoke($"Lỗi khi reset màu: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Thực hiện kiểm tra sai lệch cho 1 cặp Source-Target.
        /// Quy trình:
        ///   1. Thu thập detail items (Reo__Reinforcement) từ cả 2 view
        ///   2. Xây dựng dictionary tra cứu theo Unique ID và Element ID
        ///   3. Kiểm tra Missing (có ở Source, thiếu ở Target) + lưu MissingItemInfo
        ///   4. Kiểm tra Extra (có ở Target, không có ở Source)
        ///   5. Kiểm tra Parameter Changed (tất cả parameters)
        /// </summary>
        private void RunCheckForPair(Document doc, ViewPair pair)
        {
            string sheetName = pair.SheetName;

            // Collect Detail Items in Source View
            var sourceItems = GetDetailItems(doc, pair.Source);

            // Collect Detail Items in Target View
            var targetItems = GetDetailItems(doc, pair.Target);

            // Lấy Unique ID từ parameter "Unique ID" (nếu có), fallback về fi.UniqueId
            string GetTrackingUniqueId(FamilyInstance fi)
            {
                string p = fi.LookupParameter("Unique ID")?.AsString();
                return !string.IsNullOrEmpty(p) ? p : fi.UniqueId;
            }

            // Lấy Element ID từ parameter "Element ID" (nếu có), fallback về fi.Id
            string GetTrackingElementId(FamilyInstance fi)
            {
                string p = fi.LookupParameter("Element ID")?.AsString();
#if REVIT2024_OR_GREATER
                return !string.IsNullOrEmpty(p) ? p : fi.Id.Value.ToString();
#else
                return !string.IsNullOrEmpty(p) ? p : fi.Id.IntegerValue.ToString();
#endif
            }

            // Xây dựng dictionary tra cứu nhanh cho Target items (theo Unique ID và Element ID)
            var targetByUniqueId = new Dictionary<string, FamilyInstance>();
            var targetByElementId = new Dictionary<string, FamilyInstance>();
            foreach (var item in targetItems)
            {
                string uId = GetTrackingUniqueId(item);
                if (!targetByUniqueId.ContainsKey(uId)) targetByUniqueId[uId] = item;

                string eId = GetTrackingElementId(item);
                if (!targetByElementId.ContainsKey(eId)) targetByElementId[eId] = item;
            }

            // Xây dựng dictionary tra cứu nhanh cho Source items (theo Unique ID và Element ID)
            var sourceByUniqueId = new Dictionary<string, FamilyInstance>();
            var sourceByElementId = new Dictionary<string, FamilyInstance>();
            foreach (var item in sourceItems)
            {
                string uId = GetTrackingUniqueId(item);
                if (!sourceByUniqueId.ContainsKey(uId)) sourceByUniqueId[uId] = item;

                string eId = GetTrackingElementId(item);
                if (!sourceByElementId.ContainsKey(eId)) sourceByElementId[eId] = item;
            }

            // 0. Check for Missing Views (Viewports)
            var sourceOverViews = GetOverViews(doc, pair.Source as ViewSheet);
            var targetOverViews = GetOverViews(doc, pair.Target as ViewSheet);
            
            var missingViewIds = new HashSet<ElementId>();
            foreach (var sv in sourceOverViews)
            {
                View correspondingTv = FindCorrespondingTargetView(doc, pair, sv.Id);
                if (correspondingTv == null)
                {
                    missingViewIds.Add(sv.Id);
                    
                    Discrepancies.Add(new CheckResultItem
                    {
                        SheetName = sheetName,
                        IssueType = "Missing View",
                        ElementId = sv.Id.ToString(),
                        FamilyName = "Viewport",
                        Description = $"View '{sv.Name}' chưa được copy sang sheet MTO."
                    });
                    
                    MissingViews.Add(new MissingViewInfo
                    {
                        SourceViewId = sv.Id,
                        Pair = pair
                    });
                }
            }

            // 1. Check for MISSING items in Target View (Exist in Source, not in Target)
            foreach (var sItem in sourceItems)
            {
                if (missingViewIds.Contains(sItem.OwnerViewId))
                {
                    // Bỏ qua các thép thuộc View đang bị thiếu hoàn toàn (vì sẽ copy nguyên View)
                    continue;
                }

                string sUniqueId = GetTrackingUniqueId(sItem);
                string sElemId = GetTrackingElementId(sItem);

                bool found = targetByUniqueId.ContainsKey(sUniqueId) || targetByElementId.ContainsKey(sElemId);
                if (!found)
                {
                    Discrepancies.Add(new CheckResultItem
                    {
                        SheetName = sheetName,
                        IssueType = "Missing in Target",
                        ElementId = sElemId,
                        FamilyName = sItem.Symbol.FamilyName,
                        Description = $"Thép bị thiếu ở bản sao (Không tìm thấy Element ID: {sElemId} hoặc Unique ID: {sUniqueId})"
                    });

                    // Lưu thông tin để FixMissing có thể copy sau
                    MissingItems.Add(new MissingItemInfo
                    {
                        SourceElementId = sItem.Id,
                        SourceViewId = sItem.OwnerViewId,
                        Pair = pair
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
                        SheetName = sheetName,
                        IssueType = "Extra in Target (Orphaned)",
                        ElementId = tElemId,
                        FamilyName = tItem.Symbol.FamilyName,
                        Description = $"Thép dư thừa ở bản sao (Không có ở bản gốc Element ID: {tElemId})"
                    });

                    // Lưu ElementId để xóa khi Update
                    ExtraItemIds.Add(tItem.Id);
                    
                    // Tô đỏ thép dư thừa (bị xóa ở bản gốc)
                    View targetView = doc.GetElement(tItem.OwnerViewId) as View;
                    if (targetView != null)
                        ApplyColorOverride(targetView, tItem.Id, new Color(255, 0, 0));
                }
            }

            // 3. Check for PARAMETER CHANGES on matched items (Exist in both Source and Target)
            // Duyệt TẤT CẢ parameters của thanh thép để phát hiện mọi thay đổi
            // Bỏ qua các parameter không cần so sánh giữa 2 bản vẽ
            var skipParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Unique ID", "IfcGUID", "Mark", "Workset", "Edited by"
            };

            foreach (var sItem in sourceItems)
            {
                string sUniqueId = GetTrackingUniqueId(sItem);
                string sElemId = GetTrackingElementId(sItem);

                // Tìm item tương ứng ở Target (ưu tiên match theo Unique ID, fallback Element ID)
                FamilyInstance tItem = null;
                if (targetByUniqueId.TryGetValue(sUniqueId, out tItem)
                    || targetByElementId.TryGetValue(sElemId, out tItem))
                {
                    // Duyệt tất cả parameters của Source item và so sánh với Target
                    foreach (Parameter sParam in sItem.Parameters)
                    {
                        if (sParam?.Definition == null) continue;

                        string paramName = sParam.Definition.Name;

                        // Bỏ qua parameter tracking (dùng để match, không cần so sánh)
                        if (skipParams.Contains(paramName)) continue;

                        CompareParameter(sParam, sItem, tItem, sElemId, sItem.Symbol.FamilyName, sheetName, pair);
                    }

                    // So sánh vị trí (LocationPoint) giữa Source và Target
                    CompareLocation(sItem, tItem, sElemId, sItem.Symbol.FamilyName, sheetName);
                }
            }
        }

        /// <summary>
        /// So sánh giá trị của một parameter (từ Source) với parameter cùng tên trên Target item.
        /// Sử dụng AsValueString() (giá trị có đơn vị, vd: "12 mm") với fallback AsString().
        /// Nếu giá trị khác nhau, thêm CheckResultItem và lưu ChangedItemInfo để Update sau.
        /// </summary>
        private void CompareParameter(Parameter sourceParam, FamilyInstance source, FamilyInstance target,
            string elementId, string familyName, string sheetName, ViewPair pair)
        {
            string paramName = sourceParam.Definition.Name;

            // Tìm parameter cùng tên trên Target item
            Parameter tParam = target.LookupParameter(paramName);

            // Đọc giá trị: ưu tiên AsValueString() (có đơn vị), fallback AsString(), default ""
            string sVal = sourceParam.AsValueString() ?? sourceParam.AsString() ?? "";
            string tVal = tParam != null
                ? (tParam.AsValueString() ?? tParam.AsString() ?? "")
                : "";

            // Chỉ báo lỗi khi giá trị khác nhau
            if (sVal != tVal)
            {
                Discrepancies.Add(new CheckResultItem
                {
                    SheetName = sheetName,
                    IssueType = "Parameter Changed",
                    ElementId = elementId,
                    FamilyName = familyName,
                    ParameterName = paramName,
                    SourceValue = sVal,
                    TargetValue = tVal,
                    Description = $"{paramName}: \"{sVal}\" → \"{tVal}\""
                });

                // Lưu thông tin để Update có thể sửa parameter sau
                ChangedItems.Add(new ChangedItemInfo
                {
                    SourceElementId = source.Id,
                    SourceViewId = source.OwnerViewId,
                    TargetElementId = target.Id,
                    ParameterName = paramName,
                    SourceParam = sourceParam,
                    Pair = pair
                });
                
                // Tô xanh lá cây thép bị thay đổi parameter
                Document doc = target.Document;
                View targetView = doc.GetElement(target.OwnerViewId) as View;
                if (targetView != null)
                    ApplyColorOverride(targetView, target.Id, new Color(0, 255, 0));
            }
        }

        /// <summary>
        /// So sánh vị trí (LocationPoint) giữa Source item và Target item.
        /// Nếu tọa độ XYZ khác nhau (tolerance 0.001 ft ≈ 0.3mm), báo "Location Changed".
        /// </summary>
        private void CompareLocation(FamilyInstance source, FamilyInstance target,
            string elementId, string familyName, string sheetName)
        {
            var sLoc = source.Location as LocationPoint;
            var tLoc = target.Location as LocationPoint;

            if (sLoc == null || tLoc == null) return;

            XYZ sPoint = sLoc.Point;
            XYZ tPoint = tLoc.Point;

            // Tolerance 0.001 ft ≈ 0.3mm
            const double tolerance = 0.001;
            double dx = Math.Abs(sPoint.X - tPoint.X);
            double dy = Math.Abs(sPoint.Y - tPoint.Y);
            double dz = Math.Abs(sPoint.Z - tPoint.Z);

            if (dx > tolerance || dy > tolerance || dz > tolerance)
            {
                // Chuyển sang mm để hiển thị dễ đọc (1 ft = 304.8 mm)
                string sPos = $"({sPoint.X * 304.8:F1}, {sPoint.Y * 304.8:F1})";
                string tPos = $"({tPoint.X * 304.8:F1}, {tPoint.Y * 304.8:F1})";

                Discrepancies.Add(new CheckResultItem
                {
                    SheetName = sheetName,
                    IssueType = "Location Changed",
                    ElementId = elementId,
                    FamilyName = familyName,
                    ParameterName = "Location (X, Y)",
                    SourceValue = sPos,
                    TargetValue = tPos,
                    Description = $"Vị trí thay đổi: {sPos} → {tPos} mm"
                });

                // Lưu thông tin để Update có thể move element sau
                LocationChangedItems.Add(new LocationChangedItemInfo
                {
                    SourceElementId = source.Id,
                    SourceViewId = source.OwnerViewId,
                    TargetElementId = target.Id,
                    SourcePoint = sPoint,
                    TargetPoint = tPoint
                });
                
                // Tô xanh lá cây thép bị thay đổi vị trí
                Document doc = target.Document;
                View targetView = doc.GetElement(target.OwnerViewId) as View;
                if (targetView != null)
                    ApplyColorOverride(targetView, target.Id, new Color(0, 255, 0));
            }
        }

        /// <summary>
        /// Update Target MTO sheet:
        ///   1. Copy thép bị thiếu (Missing in Target) từ Source → Target + tô đỏ
        ///   2. Cập nhật parameter thay đổi (Parameter Changed) từ Source → Target + tô đỏ
        /// </summary>
        private void RunUpdate(Document doc)
        {
            bool hasMissingView = MissingViews != null && MissingViews.Count > 0;
            bool hasMissing = MissingItems != null && MissingItems.Count > 0;
            bool hasChanged = ChangedItems != null && ChangedItems.Count > 0;
            bool hasExtra = ExtraItemIds != null && ExtraItemIds.Count > 0;
            bool hasLocationChanged = LocationChangedItems != null && LocationChangedItems.Count > 0;

            if (!hasMissingView && !hasMissing && !hasChanged && !hasExtra && !hasLocationChanged)
            {
                NotifyStatus?.Invoke("Không có sai lệch nào để cập nhật.");
                return;
            }

            int copiedItems = 0;
            int copiedTags = 0;
            int updatedParams = 0;
            int deletedItems = 0;
            int movedItems = 0;

            using (Transaction t = new Transaction(doc, "MTO Check - Fix All (Update)"))
            {
                t.Start();
                try
                {
                    // === PHẦN 0: Duplicate View thiếu ===
                    if (MissingViews.Count > 0)
                    {
                        foreach (var mv in MissingViews)
                        {
                            View sourceView = doc.GetElement(mv.SourceViewId) as View;
                            ViewSheet sourceSheet = mv.Pair.Source as ViewSheet;
                            ViewSheet targetSheet = mv.Pair.Target as ViewSheet;
                            if (sourceView == null || sourceSheet == null || targetSheet == null) continue;

                            Viewport sourceVp = null;
                            foreach (ElementId vpId in sourceSheet.GetAllViewports())
                            {
                                Viewport vp = doc.GetElement(vpId) as Viewport;
                                if (vp != null && vp.ViewId == sourceView.Id)
                                {
                                    sourceVp = vp;
                                    break;
                                }
                            }
                            if (sourceVp == null) continue;

                            if (sourceView.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                            {
                                try
                                {
                                    ElementId newViewId = sourceView.Duplicate(ViewDuplicateOption.WithDetailing);
                                    View newView = doc.GetElement(newViewId) as View;

                                    string newViewName = sourceView.Name + "_MTO";
                                    var allViewNames = new HashSet<string>(
                                        new FilteredElementCollector(doc)
                                        .OfClass(typeof(View))
                                        .Cast<View>()
                                        .Select(v => v.Name));
                                    
                                    if (allViewNames.Contains(newViewName))
                                    {
                                        int i = 1;
                                        while (allViewNames.Contains($"{newViewName}_{i}")) i++;
                                        newViewName = $"{newViewName}_{i}";
                                    }
                                    newView.Name = newViewName;

                                    Viewport newVp = Viewport.Create(doc, targetSheet.Id, newView.Id, sourceVp.GetBoxCenter());
                                    
                                    if (sourceVp.GetTypeId() != ElementId.InvalidElementId)
                                    {
                                        try { newVp.ChangeTypeId(sourceVp.GetTypeId()); } catch { }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    NotifyStatus?.Invoke($"Lỗi khi copy view {sourceView.Name}: {ex.Message}");
                                }
                            }
                        }
                    }

                    // === PHẦN 1: Copy thép bị thiếu + tag cùng lúc ===
                    if (hasMissing)
                    {
                        var groups = MissingItems
                            .GroupBy(m => new { PairSheet = m.Pair.SheetName, SourceViewId = m.SourceViewId.ToString() })
                            .ToList();

                        foreach (var group in groups)
                        {
                            var firstItem = group.First();
                            View sourceView = doc.GetElement(firstItem.SourceViewId) as View;
                            if (sourceView == null) continue;

                            View targetView = FindCorrespondingTargetView(doc, firstItem.Pair, firstItem.SourceViewId);
                            if (targetView == null) continue;

                            var elementIds = group.Select(m => m.SourceElementId).ToList();

                            // Lưu vị trí gốc của từng element trước khi copy
                            var sourceLocations = new Dictionary<int, XYZ>();
                            for (int i = 0; i < elementIds.Count; i++)
                            {
                                var srcElem = doc.GetElement(elementIds[i]) as FamilyInstance;
                                if (srcElem?.Location is LocationPoint srcLoc)
                                {
                                    sourceLocations[i] = srcLoc.Point;
                                }
                            }

                            // Tìm tags gắn với các element cần copy
                            var tagIds = FindTagsForElements(doc, sourceView, elementIds);

                            // Gộp element + tag vào một danh sách để copy cùng lúc
                            // Revit sẽ tự động remap tag → element mới khi copy chung
                            var allIds = new List<ElementId>(elementIds);
                            allIds.AddRange(tagIds);

                            var opts = new CopyPasteOptions();
                            opts.SetDuplicateTypeNamesHandler(new OverwriteDuplicateHandler());

                            ICollection<ElementId> allCopiedIds;
                            try
                            {
                                allCopiedIds = ElementTransformUtils.CopyElements(
                                    sourceView, allIds, targetView, null, opts);
                            }
                            catch { continue; }

                            // Tách kết quả: rebar (đầu) và tags (cuối)
                            var allCopiedList = allCopiedIds.ToList();
                            int rebarCount = Math.Min(elementIds.Count, allCopiedList.Count);
                            var copiedRebarIds = allCopiedList.GetRange(0, rebarCount);

                            copiedItems += copiedRebarIds.Count;
                            copiedTags += Math.Max(0, allCopiedList.Count - rebarCount);

                            // Sửa vị trí: đảm bảo element mới copy đúng vị trí với Source
                            for (int i = 0; i < copiedRebarIds.Count; i++)
                            {
                                if (sourceLocations.TryGetValue(i, out XYZ srcPoint))
                                {
                                    var newItem = doc.GetElement(copiedRebarIds[i]) as FamilyInstance;
                                    if (newItem?.Location is LocationPoint newLoc)
                                    {
                                        XYZ delta = srcPoint - newLoc.Point;
                                        if (delta.GetLength() > 0.001)
                                        {
                                            ElementTransformUtils.MoveElement(doc, copiedRebarIds[i], delta);
                                        }
                                    }
                                }
                            }

                            // Ghi Element ID + Unique ID + tô xanh dương cho rebar mới copy
                            foreach (var newId in copiedRebarIds)
                            {
                                var newItem = doc.GetElement(newId) as FamilyInstance;
                                if (newItem == null) continue;

                                Parameter elemIdParam = newItem.LookupParameter("Element ID");
                                if (elemIdParam != null && !elemIdParam.IsReadOnly)
                                {
#if REVIT2024_OR_GREATER
                                    elemIdParam.Set(newItem.Id.Value.ToString());
#else
                                    elemIdParam.Set(newItem.Id.IntegerValue.ToString());
#endif
                                }

                                Parameter uniqueIdParam = newItem.LookupParameter("Unique ID");
                                if (uniqueIdParam != null && !uniqueIdParam.IsReadOnly)
                                {
                                    uniqueIdParam.Set(newItem.UniqueId);
                                }

                                // Tô màu xanh dương cho item mới copy
                                ApplyColorOverride(targetView, newItem.Id, new Color(0, 0, 255));
                            }
                        }
                    }

                    // === PHẦN 2: Cập nhật parameter thay đổi ===
                    if (hasChanged)
                    {
                        // Nhóm theo TargetElementId để tránh set override nhiều lần
                        var changedByTarget = ChangedItems
                            .GroupBy(c => c.TargetElementId.ToString())
                            .ToList();

                        foreach (var group in changedByTarget)
                        {
                            var firstChanged = group.First();
                            var targetElem = doc.GetElement(firstChanged.TargetElementId);
                            View ownerView = targetElem != null ? doc.GetElement(targetElem.OwnerViewId) as View : null;

                            // 1. Lưu vị trí Reo Tag trước khi thay đổi parameter
                            var reoTagSavedPositions = new Dictionary<ElementId, XYZ>();
                            if (ownerView != null)
                            {
                                var targetTags = FindTagsForElements(doc, ownerView, new List<ElementId> { firstChanged.TargetElementId });
                                foreach (var tagId in targetTags)
                                {
                                    var tag = doc.GetElement(tagId) as IndependentTag;
                                    if (tag != null && IsReoTag(doc, tag))
                                    {
                                        reoTagSavedPositions[tagId] = tag.TagHeadPosition;
                                    }
                                }
                            }

                            // 2. Cập nhật parameters
                            foreach (var changed in group)
                            {
                                var targetItem = doc.GetElement(changed.TargetElementId) as FamilyInstance;
                                if (targetItem == null) continue;

                                Parameter tParam = targetItem.LookupParameter(changed.ParameterName);
                                if (tParam == null || tParam.IsReadOnly) continue;

                                // Copy giá trị từ Source parameter sang Target parameter
                                try
                                {
                                    switch (changed.SourceParam.StorageType)
                                    {
                                        case StorageType.Double:
                                            tParam.Set(changed.SourceParam.AsDouble());
                                            break;
                                        case StorageType.Integer:
                                            tParam.Set(changed.SourceParam.AsInteger());
                                            break;
                                        case StorageType.String:
                                            tParam.Set(changed.SourceParam.AsString() ?? "");
                                            break;
                                        case StorageType.ElementId:
                                            tParam.Set(changed.SourceParam.AsElementId());
                                            break;
                                    }
                                    updatedParams++;
                                }
                                catch { }
                            }

                            // 3. Khôi phục vị trí Reo Tag (tránh bị nhảy sau khi đổi parameter)
                            foreach (var kvp in reoTagSavedPositions)
                            {
                                var tag = doc.GetElement(kvp.Key) as IndependentTag;
                                if (tag != null)
                                {
                                    try { tag.TagHeadPosition = kvp.Value; } catch { }
                                }
                            }

                            // 4. Tô màu + sync non-Reo tags từ Source
                            if (ownerView != null && targetElem != null)
                            {
                                ApplyColorOverride(ownerView, firstChanged.TargetElementId, new Color(0, 255, 0));
                                View srcView = doc.GetElement(firstChanged.SourceViewId) as View;
                                if (srcView != null)
                                    SyncTagsFromSourceToTarget(doc, firstChanged.SourceElementId, firstChanged.TargetElementId, srcView, ownerView, true, true);
                            }
                        }
                    }

                    // === PHẦN 3: Xóa thép dư thừa ===
                    if (hasExtra)
                    {
                        foreach (var extraId in ExtraItemIds)
                        {
                            try
                            {
                                doc.Delete(extraId);
                                deletedItems++;
                            }
                            catch { }
                        }
                    }

                    // === PHẦN 4: Di chuyển thép thay đổi vị trí ===
                    if (hasLocationChanged)
                    {
                        foreach (var locItem in LocationChangedItems)
                        {
                            try
                            {
                                XYZ delta = locItem.SourcePoint - locItem.TargetPoint;
                                ElementTransformUtils.MoveElement(doc, locItem.TargetElementId, delta);
                                movedItems++;

                                // Giữ màu xanh lá cho element đã di chuyển
                                var elem = doc.GetElement(locItem.TargetElementId);
                                if (elem != null)
                                {
                                    View ownerView = doc.GetElement(elem.OwnerViewId) as View;
                                    if (ownerView != null)
                                    {
                                        ApplyColorOverride(ownerView, locItem.TargetElementId, new Color(0, 255, 0));
                                        View srcView2 = doc.GetElement(locItem.SourceViewId) as View;
                                        if (srcView2 != null)
                                            SyncTagsFromSourceToTarget(doc, locItem.SourceElementId, locItem.TargetElementId, srcView2, ownerView, true, true);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    NotifyStatus?.Invoke($"Lỗi: {ex.Message}");
                    return;
                }
            }

            MissingItems.Clear();
            ChangedItems.Clear();
            ExtraItemIds.Clear();
            LocationChangedItems.Clear();

            // Báo kết quả
            var parts = new List<string>();
            if (copiedItems > 0) parts.Add($"copy {copiedItems} thép");
            if (copiedTags > 0) parts.Add($"{copiedTags} tag");
            if (updatedParams > 0) parts.Add($"cập nhật {updatedParams} parameter");
            if (deletedItems > 0) parts.Add($"xóa {deletedItems} thép dư");
            if (movedItems > 0) parts.Add($"di chuyển {movedItems} thép");
            NotifyStatus?.Invoke($"Đã {string.Join(", ", parts)}. Thép sai lệch được tô đỏ.");
            OnFixCompleted?.Invoke();
        }

        /// <summary>
        /// Tô màu cho element trong view để dễ phát hiện sai lệch.
        /// </summary>
        private void ApplyColorOverride(View view, ElementId elementId, Color color)
        {
            var overrideSettings = new OverrideGraphicSettings();
            overrideSettings.SetProjectionLineColor(color);
            overrideSettings.SetSurfaceForegroundPatternColor(color);
            overrideSettings.SetCutLineColor(color);
            view.SetElementOverrides(elementId, overrideSettings);
        }

        /// <summary>
        /// Tìm target view tương ứng với source view trong target sheet.
        /// Cách match: Ưu tiên theo tên, sau đó fallback theo tọa độ Viewport.
        /// </summary>
        private View FindCorrespondingTargetView(Document doc, ViewPair pair, ElementId sourceViewId)
        {
            if (sourceViewId == ElementId.InvalidElementId || !(pair.Source is ViewSheet sourceSheet) || !(pair.Target is ViewSheet targetSheet))
                return null;

            View sourceView = doc.GetElement(sourceViewId) as View;
            if (sourceView == null) return null;

            var targetOverViews = GetOverViews(doc, targetSheet);

            // 1. Match by EXACT name + "_MTO"
            string expectedMtoName = sourceView.Name + "_MTO";
            var exactMatch = targetOverViews.FirstOrDefault(v => v.Name.Equals(expectedMtoName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null) return exactMatch;

            // 2. Match by Prefix (e.g. source is "DAM 1 OVER", target is "DAM 1 OVER Copy 1")
            var prefixMatch = targetOverViews.FirstOrDefault(v => v.Name.StartsWith(sourceView.Name, StringComparison.OrdinalIgnoreCase));
            if (prefixMatch != null) return prefixMatch;

            // 3. Match by Viewport coordinates
            Viewport sourceVp = GetViewportForView(doc, sourceSheet, sourceViewId);
            if (sourceVp != null)
            {
                XYZ sourceCenter = sourceVp.GetBoxCenter();
                foreach (var tv in targetOverViews)
                {
                    Viewport targetVp = GetViewportForView(doc, targetSheet, tv.Id);
                    if (targetVp != null && targetVp.GetBoxCenter().IsAlmostEqualTo(sourceCenter, 0.01))
                    {
                        return tv;
                    }
                }
            }

            return null;
        }

        private Viewport GetViewportForView(Document doc, ViewSheet sheet, ElementId viewId)
        {
            foreach (var vpId in sheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is Viewport vp && vp.ViewId == viewId)
                    return vp;
            }
            return null;
        }

        /// <summary>
        /// Lấy danh sách OVER views trong một sheet, sắp xếp theo ViewportId.
        /// </summary>
        private List<View> GetOverViews(Document doc, ViewSheet sheet)
        {
            var overViews = new List<View>();
            foreach (var vId in sheet.GetAllPlacedViews())
            {
                var v = doc.GetElement(vId) as View;
                if (v != null && v.Name.ToUpper().Contains("OVER"))
                {
                    overViews.Add(v);
                }
            }
            return overViews;
        }

        /// <summary>
        /// Tìm tất cả IndependentTag trong source view gắn với các element ids cho trước.
        /// </summary>
        private List<ElementId> FindTagsForElements(Document doc, View sourceView, List<ElementId> elementIds)
        {
            var elementIdSet = new HashSet<string>(elementIds.Select(id => id.ToString()));
            var tagIds = new List<ElementId>();

            var tags = new FilteredElementCollector(doc, sourceView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in tags)
            {
                try
                {
                    // Kiểm tra xem tag có gắn với element nào trong danh sách không
                    var taggedIds = tag.GetTaggedLocalElementIds();
                    foreach (var taggedId in taggedIds)
                    {
                        if (elementIdSet.Contains(taggedId.ToString()))
                        {
                            tagIds.Add(tag.Id);
                            break;
                        }
                    }
                }
                catch { }
            }

            return tagIds;
        }


        /// <summary>
        /// Copy tất cả tag từ Source element sang Target element.
        /// Xóa tag cũ ở Target (nếu yêu cầu), tạo tag mới có vị trí và leader giống hệt Source.
        /// </summary>
        /// <returns>Số tag đã tạo</returns>
        private int SyncTagsFromSourceToTarget(Document doc,
            ElementId sourceElementId, ElementId targetElementId,
            View sourceView, View targetView,
            bool deleteExistingTargetTags = true,
            bool excludeReoTag = false)
        {
            int createdCount = 0;

            // 1. Xóa tag cũ ở Target (nếu yêu cầu) - skip Reo Tag nếu excludeReoTag
            if (deleteExistingTargetTags)
            {
                var existingTargetTags = FindTagsForElements(doc, targetView, new List<ElementId> { targetElementId });
                foreach (var tagId in existingTargetTags)
                {
                    if (excludeReoTag)
                    {
                        var tag = doc.GetElement(tagId) as IndependentTag;
                        if (tag != null && IsReoTag(doc, tag)) continue;
                    }
                    try { doc.Delete(tagId); } catch { }
                }
            }

            // 2. Tìm tất cả source tags - skip Reo Tag nếu excludeReoTag
            var sourceTagIds = FindTagsForElements(doc, sourceView, new List<ElementId> { sourceElementId });
            if (excludeReoTag)
            {
                sourceTagIds = sourceTagIds.Where(id =>
                {
                    var tag = doc.GetElement(id) as IndependentTag;
                    return tag == null || !IsReoTag(doc, tag);
                }).ToList();
            }
            if (sourceTagIds.Count == 0) return 0;

            // 3. Tạo tag mới trong target view, gắn với target element, giống hệt source tag
            var targetElem = doc.GetElement(targetElementId);
            if (targetElem == null) return 0;
            Reference targetRef = new Reference(targetElem);

            // Lưu thông tin elbow để set sau khi Regenerate
            var elbowInfos = new List<(IndependentTag newTag, XYZ elbowPos, Reference tRef)>();

            foreach (var sTagId in sourceTagIds)
            {
                var sourceTag = doc.GetElement(sTagId) as IndependentTag;
                if (sourceTag == null) continue;

                try
                {
                    var newTag = IndependentTag.Create(doc,
                        sourceTag.GetTypeId(),
                        targetView.Id,
                        targetRef,
                        sourceTag.HasLeader,
                        sourceTag.TagOrientation,
                        sourceTag.TagHeadPosition);

                    if (newTag != null)
                    {
                        createdCount++;

                        if (sourceTag.HasLeader)
                        {
                            try
                            {
                                newTag.LeaderEndCondition = sourceTag.LeaderEndCondition;

                                var taggedRefs = sourceTag.GetTaggedReferences();
                                if (taggedRefs.Count > 0)
                                {
                                    var srcRef = taggedRefs.First();

                                    // Set LeaderEnd ngay (cho Free mode)
                                    if (sourceTag.LeaderEndCondition == LeaderEndCondition.Free)
                                    {
                                        try
                                        {
                                            XYZ srcLeaderEnd = sourceTag.GetLeaderEnd(srcRef);
                                            newTag.SetLeaderEnd(targetRef, srcLeaderEnd);
                                        }
                                        catch { }
                                    }

                                    // Lưu elbow position để set sau Regenerate
                                    try
                                    {
                                        XYZ srcElbow = sourceTag.GetLeaderElbow(srcRef);
                                        elbowInfos.Add((newTag, srcElbow, targetRef));
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // 4. Regenerate để Revit thiết lập geometry leader
            if (elbowInfos.Count > 0)
            {
                doc.Regenerate();

                // Set elbow position sau khi leader geometry đã sẵn sàng
                foreach (var info in elbowInfos)
                {
                    try
                    {
                        info.newTag.SetLeaderElbow(info.tRef, info.elbowPos);
                    }
                    catch { }
                }
            }

            return createdCount;
        }

        /// <summary>
        /// Kiểm tra xem tag có phải là "Reo Tag" family không.
        /// Matches: RINCO_TAG_Reo, Reo Tag_Mark, v.v.
        /// </summary>
        private bool IsReoTag(Document doc, IndependentTag tag)
        {
            try
            {
                var tagType = doc.GetElement(tag.GetTypeId()) as ElementType;
                if (tagType != null)
                {
                    // Check FamilyName chứa "Reo" (bắt RINCO_TAG_Reo)
                    // hoặc Name chứa "Reo Tag" (bắt Reo Tag_Mark)
                    if (tagType.FamilyName != null && tagType.FamilyName.IndexOf("Reo", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    if (tagType.Name != null && tagType.Name.IndexOf("Reo Tag", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Thu thập tất cả Detail Component (FamilyInstance) thuộc family "Reo__Reinforcement" từ một View.
        /// Nếu View là Sheet: lấy từ các placed views có tên chứa "OVER" + items trực tiếp trên sheet.
        /// Nếu View thường: lấy trực tiếp từ view đó.
        /// </summary>
        /// <param name="doc">Document hiện tại</param>
        /// <param name="view">View hoặc Sheet cần thu thập detail items</param>
        /// <returns>Danh sách FamilyInstance thuộc family Reo__Reinforcement</returns>
        private List<FamilyInstance> GetDetailItems(Document doc, View view)
        {
            var items = new List<FamilyInstance>();
            if (view is ViewSheet sheet)
            {
                // Duyệt qua các view được đặt trên Sheet, chỉ lấy view có tên chứa "OVER"
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
                
                // Thu thập items đặt trực tiếp trên Sheet (không nằm trong placed view nào)
                items.AddRange(new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol != null && fi.Symbol.FamilyName.Contains("Reo__Reinforcement")));
            }
            else
            {
                // View thường: lấy trực tiếp detail items trong view
                items.AddRange(new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol != null && fi.Symbol.FamilyName.Contains("Reo__Reinforcement")));
            }
            return items;
        }

        /// <summary>Tên handler để Revit hiển thị trong log.</summary>
        public string GetName() => "MtoCheckHandler";
    }

    /// <summary>
    /// Handler để tự động overwrite khi gặp duplicate type names trong CopyElements.
    /// </summary>
    public class OverwriteDuplicateHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }

    /// <summary>
    /// Thông tin một view bị thiếu ở Target, lưu lại để dùng cho Update.
    /// </summary>
    public class MissingViewInfo
    {
        public ElementId SourceViewId { get; set; }
        public ViewPair Pair { get; set; }
    }

    /// <summary>
    /// Thông tin một thép bị thiếu ở Target, lưu lại để dùng cho Update.
    /// </summary>
    public class MissingItemInfo
    {
        /// <summary>ElementId của thép ở Source view.</summary>
        public ElementId SourceElementId { get; set; }

        /// <summary>View chứa thép ở Source (OVER view).</summary>
        public ElementId SourceViewId { get; set; }

        /// <summary>Cặp sheet Source-Target.</summary>
        public ViewPair Pair { get; set; }
    }

    /// <summary>
    /// Thông bộ một parameter thay đổi, lưu lại để dùng cho Update.
    /// </summary>
    public class ChangedItemInfo
    {
        public ElementId SourceElementId { get; set; }
        public ElementId SourceViewId { get; set; }
        public ElementId TargetElementId { get; set; }
        public string ParameterName { get; set; }
        public Parameter SourceParam { get; set; }
        public ViewPair Pair { get; set; }
    }

    /// <summary>
    /// Thông tin một thép có vị trí thay đổi, lưu lại để dùng cho Update.
    /// </summary>
    public class LocationChangedItemInfo
    {
        public ElementId SourceElementId { get; set; }
        public ElementId SourceViewId { get; set; }
        public ElementId TargetElementId { get; set; }
        public XYZ SourcePoint { get; set; }
        public XYZ TargetPoint { get; set; }
    }

    /// <summary>
    /// Model chứa thông tin một điểm sai lệch phát hiện được.
    /// Được hiển thị trong DataGrid của MtoCheckWindow.
    /// </summary>
    public class CheckResultItem
    {
        /// <summary>Tên sheet gốc (Source) để phân biệt kết quả từ nhiều sheet.</summary>
        public string SheetName { get; set; }

        /// <summary>Loại vấn đề: "Missing in Target", "Extra in Target (Orphaned)", hoặc "Parameter Changed".</summary>
        public string IssueType { get; set; }

        /// <summary>Element ID của cây thép (từ Source hoặc Target tùy loại issue).</summary>
        public string ElementId { get; set; }

        /// <summary>Tên Family của detail component (vd: Reo__Reinforcement_L).</summary>
        public string FamilyName { get; set; }

        /// <summary>Tên parameter bị thay đổi (chỉ có khi IssueType = "Parameter Changed").</summary>
        public string ParameterName { get; set; }

        /// <summary>Giá trị parameter ở Source View (bản gốc).</summary>
        public string SourceValue { get; set; }

        /// <summary>Giá trị parameter ở Target View (bản sao).</summary>
        public string TargetValue { get; set; }

        /// <summary>Mô tả chi tiết vấn đề (hiển thị trong cột Description).</summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// Cặp Source-Target View để kiểm tra sai lệch.
    /// </summary>
    public class ViewPair
    {
        /// <summary>View bản gốc (Source).</summary>
        public View Source { get; set; }

        /// <summary>View bản sao (Target), tự động tìm theo quy tắc tên + "_MTO".</summary>
        public View Target { get; set; }

        /// <summary>Tên sheet gốc để hiển thị trong kết quả.</summary>
        public string SheetName { get; set; }
    }
}

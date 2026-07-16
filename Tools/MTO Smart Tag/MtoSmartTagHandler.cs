using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.MtoSmartTag
{
    /// <summary>
    /// Offset direction for tag placement relative to the insertion point (circle).
    /// </summary>
    public enum OffsetDirection
    {
        Top,
        Bottom,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public class MtoSmartTagHandler : IExternalEventHandler
    {
        public string Action { get; set; }
        public List<string> TargetFamilyNames { get; set; } = new List<string>();
        public OffsetDirection Direction { get; set; } = OffsetDirection.TopLeft;
        public double OffsetDistanceMm { get; set; } = 300; // mm
        public double OffsetXMm { get; set; } = 0; // Direct X offset in mm
        public double OffsetYMm { get; set; } = 0; // Direct Y offset in mm
        public bool UseDirectOffset { get; set; } = false; // Use X/Y instead of direction
        public bool AddLeader { get; set; } = false;
        public bool OnlyAlreadyTagged { get; set; } = false;
        public bool OnlyUntagged { get; set; } = false;
        public bool ApplyColorOverride { get; set; } = false;
        public bool OverrideRebarColor { get; set; } = true;
        public byte ColorR { get; set; } = 255;
        public byte ColorG { get; set; } = 0;
        public byte ColorB { get; set; } = 0;
        public string LayerDirection { get; set; } // "X" or "Y"
        public ElementId SelectedTagTypeId { get; set; }
        public Action<string> NotifyStatus { get; set; }
        public Action<Document, View> OnReloadData { get; set; }

        // Untagged Items Settings
        public bool CenterDotAdjustable { get; set; } = true;
        public bool HideDotAdjustable { get; set; } = false;
        public bool ShowDotAdjustable { get; set; } = false;
        public bool CenterDotZBar { get; set; } = true;
        public bool HideDotZBar { get; set; } = false;
        public bool ShowDotZBar { get; set; } = false;
        public bool ColorUntaggedItems { get; set; } = true;
        public byte UntaggedColorR { get; set; } = 255;
        public byte UntaggedColorG { get; set; } = 0;
        public byte UntaggedColorB { get; set; } = 255;
        public bool IgnoreTagCheck { get; set; } = false;

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            View view = doc.ActiveView;

            using (Transaction trans = new Transaction(doc, "MTO Smart Tag"))
            {
                trans.Start();

                try
                {
                    if (Action == "ReloadData")
                    {
                        OnReloadData?.Invoke(doc, view);
                    }
                    else if (Action == "TagAll")
                    {
                        TagAllReinforcementDistributions(doc, view);
                    }
                    else if (Action == "ResetColor")
                    {
                        ResetColorOverrides(doc, view);
                    }
                    else if (Action == "CheckDot")
                    {
                        CheckDots(doc, view);
                    }
                    else if (Action == "HideTaggedReo")
                    {
                        HideTaggedReo(doc, view);
                    }
                    else if (Action == "HideLayer")
                    {
                        SetLayerVisibility(doc, view, LayerDirection, hide: true);
                    }
                    else if (Action == "ShowLayer")
                    {
                        SetLayerVisibility(doc, view, LayerDirection, hide: false);
                    }
                    else if (Action == "ShowAll")
                    {
                        ShowAllLayers(doc, view);
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    NotifyStatus?.Invoke("Error: " + ex.Message);
                }
            }
        }

        private void CheckDots(Document doc, View view)
        {
            var detailItems = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = doc.GetElement(typeId) as FamilySymbol;
                    if (type == null || type.FamilyName == null) return false;
                    return type.FamilyName.Contains("Reinforcement_Distribution") || type.FamilyName.Contains("ZBar");
                })
                .ToList();

            if (!detailItems.Any())
            {
                NotifyStatus?.Invoke($"No Reo_Reinforcement_Distribution or Reo_ZBar items found in current view.");
                return;
            }

            // Get already tagged element IDs
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var taggedIds = new HashSet<ElementId>();
            foreach (var tag in existingTags)
            {
#if REVIT2022_OR_GREATER
                foreach (var id in tag.GetTaggedLocalElementIds())
#else
                foreach (var id in new List<ElementId> { tag.TaggedLocalElementId })
#endif
                {
                    taggedIds.Add(id);
                }
            }

            int processedCount = 0;
            foreach (var item in detailItems)
            {
                bool alreadyTagged = taggedIds.Contains(item.Id);

                if (IgnoreTagCheck || !alreadyTagged)
                {
                    ProcessUntaggedItem(item, doc, view);
                    
                    if (ColorUntaggedItems)
                    {
                        try
                        {
                            var overrideSettings = new OverrideGraphicSettings();
                            var color = new Color(UntaggedColorR, UntaggedColorG, UntaggedColorB);
                            overrideSettings.SetProjectionLineColor(color);
                            view.SetElementOverrides(item.Id, overrideSettings);
                        }
                        catch { }
                    }

                    processedCount++;
                }
            }

            NotifyStatus?.Invoke($"Checked and processed dots for {processedCount} untagged items.");
        }

        private void TagAllReinforcementDistributions(Document doc, View view)
        {
            // 1. Find all Detail Items matching selected families
            var detailItems = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = doc.GetElement(typeId) as FamilySymbol;
                    return type?.FamilyName != null && TargetFamilyNames.Contains(type.FamilyName);
                })
                .ToList();

            if (!detailItems.Any())
            {
                NotifyStatus?.Invoke($"No matching detail items found in current view for the selected families.");
                return;
            }

            // 2. Find tag type
            FamilySymbol tagType = null;
            if (SelectedTagTypeId != null && SelectedTagTypeId != ElementId.InvalidElementId)
            {
                tagType = doc.GetElement(SelectedTagTypeId) as FamilySymbol;
            }

            if (tagType == null)
            {
                // Try to find tag family automatically (RINCO_TAG_Reo or Reo Tag_Mark)
                tagType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs =>
                        fs.Category != null &&
                        fs.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_DetailComponentTags &&
                        (fs.FamilyName.Contains("RINCO_TAG_Reo") || fs.FamilyName.Contains("Reo Tag_Mark")));
            }

            if (tagType == null)
            {
                NotifyStatus?.Invoke("Cannot find Detail Item Tag family. Please ensure a tag family (e.g. 'Reo Tag_Mark') is loaded.");
                return;
            }

            // 3. Get already tagged element IDs
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var taggedIds = new HashSet<ElementId>();
            foreach (var tag in existingTags)
            {
#if REVIT2022_OR_GREATER
                foreach (var id in tag.GetTaggedLocalElementIds())
#else
                foreach (var id in new List<ElementId> { tag.TaggedLocalElementId })
#endif
                {
                    taggedIds.Add(id);
                }
            }

            // 4. Calculate offset vector
            XYZ offsetVector;
            if (UseDirectOffset)
            {
                double ox = OffsetXMm / 304.8;
                double oy = OffsetYMm / 304.8;
                offsetVector = new XYZ(ox, oy, 0);
            }
            else
            {
                double offsetFeet = OffsetDistanceMm / 304.8;
                offsetVector = GetOffsetVector(Direction, offsetFeet);
            }

            // 5. Tag each detail item
            int taggedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            string debugInfo = "";

            foreach (var item in detailItems)
            {
                bool alreadyTagged = taggedIds.Contains(item.Id);

                // OnlyAlreadyTagged: skip items that DON'T have a tag
                if (OnlyAlreadyTagged && !alreadyTagged)
                {
                    skippedCount++;
                    continue;
                }

                // OnlyUntagged: skip items that ALREADY have a tag
                if (OnlyUntagged && alreadyTagged)
                {
                    skippedCount++;
                    continue;
                }


                try
                {
                    // Only modify family parameters if NOT in OnlyAlreadyTagged mode
                    if (!OnlyAlreadyTagged)
                    {
                        // Process dot settings (hide, show, center) BEFORE getting its position
                        string dotDebug = ProcessUntaggedItem(item, doc, view);
                        if (!string.IsNullOrEmpty(dotDebug) && string.IsNullOrEmpty(debugInfo))
                        {
                            debugInfo += dotDebug;
                        }

                        // For ZBar: set Arrow Location to 15211.1mm
                        if (item is FamilyInstance fiZBar)
                        {
                            var zType = doc.GetElement(fiZBar.GetTypeId()) as FamilySymbol;
                            if (zType != null && zType.FamilyName.Contains("ZBar"))
                            {
                                Parameter pArrowLoc = fiZBar.LookupParameter("Arrow Location");
                                if (pArrowLoc != null && !pArrowLoc.IsReadOnly)
                                {
                                    pArrowLoc.Set(15211.1 / 304.8); // mm to feet
                                }
                            }
                        }
                    
                        // Force Revit to update geometry so GetDotPosition gets the NEW center location
                        doc.Regenerate();
                    }

                    // Get dot position (the circle on the distribution symbol)
                    XYZ dotPos = GetDotPosition(item, doc, view);
                    if (dotPos == null)
                    {
                        failedCount++;
                        if (string.IsNullOrEmpty(debugInfo))
                        {
                            debugInfo += $"\nâŒ Element {item.Id} has no location or bounding box.";
                        }
                        continue;
                    }

                    // Build debug info for first item
                    if (string.IsNullOrEmpty(debugInfo))
                    {
                        // Get all positions for comparison
                        XYZ locPt = (item.Location is LocationPoint lp) ? lp.Point : null;
                        BoundingBoxXYZ bbox = item.get_BoundingBox(view);
                        XYZ bboxCenter = bbox != null ? (bbox.Min + bbox.Max) / 2 : null;

                        double toMm = 304.8;
                        debugInfo = $"\n🔴 Dot: ({dotPos.X * toMm:F0}, {dotPos.Y * toMm:F0})";
                        if (locPt != null)
                            debugInfo += $"\n📌 LocPt: ({locPt.X * toMm:F0}, {locPt.Y * toMm:F0})";
                        if (bboxCenter != null)
                            debugInfo += $"\n📦 BBox: ({bboxCenter.X * toMm:F0}, {bboxCenter.Y * toMm:F0})";
                        if (bbox != null)
                            debugInfo += $"\n   Min:({bbox.Min.X * toMm:F0},{bbox.Min.Y * toMm:F0}) Max:({bbox.Max.X * toMm:F0},{bbox.Max.Y * toMm:F0})";
                    }

                    // Tag position = dot position + offset
                    XYZ tagPosition = dotPos + offsetVector;

                    // Create the tag — ALWAYS with leader first so TagHeadPosition works
                    // (Without leader, Revit ignores position and snaps to element center)
                    Reference hostRef = new Reference(item);
                    IndependentTag tag = IndependentTag.Create(
                        doc,
                        tagType.Id,
                        view.Id,
                        hostRef,
                        true, // Always create with leader first
                        TagOrientation.Horizontal,
                        tagPosition);

                    if (tag != null)
                    {
                        // Toggle leader based on user preference BEFORE setting position
                        if (AddLeader)
                        {
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Free;
                            try
                            {
                                tag.SetLeaderEnd(hostRef, dotPos);
                            }
                            catch { }
                        }
                        else
                        {
                            tag.HasLeader = false;
                        }

                        // Force the tag head to the exact desired position AFTER modifying the leader state
                        tag.TagHeadPosition = tagPosition;

                        taggedCount++;

                        // Tự động tắt chấm tròn sau khi đã tag xong (chỉ khi KHÔNG ở chế độ OnlyAlreadyTagged)
                        if (!OnlyAlreadyTagged && item is FamilyInstance fi)
                        {
                            bool isAdj = fi.Symbol.FamilyName.Contains("Reinforcement_Distribution");
                            string pName = isAdj ? "Dot Visibility" : "Arrow & Dot Visibility";
                            Parameter visParam = fi.LookupParameter(pName);
                            if (visParam != null && !visParam.IsReadOnly)
                            {
                                visParam.Set(0); // Tắt chấm tròn
                            }
                        }

                        // Apply color override to the detail item AND the tag
                        if (ApplyColorOverride)
                        {
                            var color = new Color(ColorR, ColorG, ColorB);
                            
                            if (OverrideRebarColor)
                            {
                                // Override cho thép
                                var itemOverride = new OverrideGraphicSettings();
                                itemOverride.SetProjectionLineColor(color);
                                itemOverride.SetSurfaceForegroundPatternColor(color);
                                itemOverride.SetCutLineColor(color);
                                view.SetElementOverrides(item.Id, itemOverride);

                                // Override cho các subcomponents (ví dụ: dot)
                                if (item is FamilyInstance fiColor)
                                {
                                    var subIds = fiColor.GetSubComponentIds();
                                    if (subIds != null)
                                    {
                                        foreach (var subId in subIds)
                                        {
                                            try { view.SetElementOverrides(subId, itemOverride); }
                                            catch { }
                                        }
                                    }
                                }
                            }
                            
                            // Override cho tag (annotation)
                            var tagOverride = new OverrideGraphicSettings();
                            tagOverride.SetProjectionLineColor(color);
                            tagOverride.SetSurfaceForegroundPatternColor(color);
                            tagOverride.SetCutLineColor(color);
                            // Tìm Solid Fill pattern để tô text/annotation
                            var solidFill = new FilteredElementCollector(doc)
                                .OfClass(typeof(FillPatternElement))
                                .Cast<FillPatternElement>()
                                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                            if (solidFill != null)
                            {
                                tagOverride.SetSurfaceForegroundPatternId(solidFill.Id);
                                tagOverride.SetSurfaceForegroundPatternColor(color);
                            }
                            view.SetElementOverrides(tag.Id, tagOverride);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    if (string.IsNullOrEmpty(debugInfo)) debugInfo += $"\nâŒ Error on {item.Id}: {ex.Message}";
                }
            }

            string msg = $"Tagged {taggedCount} items.";
            if (skippedCount > 0) msg += $" Skipped {skippedCount}.";
            if (failedCount > 0) msg += $" Failed {failedCount}.";
            msg += $" (Total: {detailItems.Count})";
            msg += debugInfo;
            NotifyStatus?.Invoke(msg);
        }

        private string ProcessUntaggedItem(Element item, Document doc, View view)
        {
            if (!(item is FamilyInstance fi)) return "";
            
            // Bá» qua cÃ¡c Ä‘á»‘i tÆ°á»£ng náº±m trong Group vÃ¬ Revit khÃ´ng cho phÃ©p sá»­a tham sá»‘
            if (fi.GroupId != ElementId.InvalidElementId) return "\nâš ï¸ Element is in a Group (Skipped modifying parameters)";
            
            var typeId = fi.GetTypeId();
            var type = doc.GetElement(typeId) as FamilySymbol;
            if (type == null) return "";
            
            var familyName = type.FamilyName;

            bool isAdjustable = familyName.Contains("Reinforcement_Distribution");
            bool isZBar = familyName.Contains("ZBar");

            if (!isAdjustable && !isZBar) return "";

            bool centerDot = isAdjustable ? CenterDotAdjustable : CenterDotZBar;
            bool hideDot = isAdjustable ? HideDotAdjustable : HideDotZBar;
            bool showDot = isAdjustable ? ShowDotAdjustable : ShowDotZBar;

            string paramName = isAdjustable ? "Dot Visibility" : "Arrow & Dot Visibility";
            Parameter pVis = fi.LookupParameter(paramName);
            string debugMsg = "";
            if (pVis == null) debugMsg += $"\nâš ï¸ Param '{paramName}' NOT FOUND!";

            if (hideDot)
            {
                // Hide dot via parameter
                if (pVis != null && !pVis.IsReadOnly)
                {
                    pVis.Set(0); // false
                }
            }
            else if (showDot || centerDot)
            {
                // Ensure dot is visible if explicitly shown or centered
                if (pVis != null && !pVis.IsReadOnly)
                {
                    pVis.Set(1); // true
                }
            }

            // Always process centering if center is requested (even if dot is hidden)
            if (centerDot)
            {
                if (isAdjustable)
                {
                    Parameter pArrow = fi.LookupParameter("Arrow 1 From Bar");
                    if (pArrow != null && !pArrow.IsReadOnly)
                    {
                        Parameter pLength = fi.LookupParameter("Distribution_Length");
                        if (pLength != null)
                        {
                            pArrow.Set(pLength.AsDouble() / 2.0);
                        }
                        else
                        {
                            pArrow.Set(15000.0 / 304.8);
                        }
                    }
                    else
                    {
                        debugMsg += "\nâš ï¸ Param 'Arrow 1 From Bar' NOT FOUND!";
                    }
                }
                else if (isZBar)
                {
                    Parameter pArrow = fi.LookupParameter("Arrow & Dot 1 From Bar");
                    if (pArrow != null && !pArrow.IsReadOnly)
                    {
                        Parameter pHeight = fi.LookupParameter("Height");
                        Parameter pBarD = fi.LookupParameter("Bar_Diameter");
                        if (pHeight != null && pBarD != null)
                        {
                            double h = pHeight.AsDouble();
                            double d = pBarD.AsDouble();
                            double offset = (h / 2.0) + d;
                            pArrow.Set(offset);
                        }
                    }
                }
                
                // Find dot position and move it to center
                bool moved = false;
                var subIds = fi.GetSubComponentIds();
                if (subIds != null && subIds.Any())
                {
                    foreach (var subId in subIds)
                    {
                        Element subElem = doc.GetElement(subId);
                        if (subElem != null)
                        {
                            string subName = subElem.Name.ToLower();
                            if (subName.Contains("dot") || subName.Contains("point") || subName.Contains("circle") || 
                                subName.Contains("chấm") || subName.Contains("nút") || subName.Contains("node") || 
                                subName.Contains("sym") || subName.Contains("mark"))
                            {
                                if (subElem.Location is LocationPoint dotLoc)
                                {
                                    XYZ currentPos = dotLoc.Point;
                                    XYZ targetPos = null;

                                    if (fi.Location is LocationCurve locCurve)
                                    {
                                        targetPos = locCurve.Curve.Evaluate(0.5, true);
                                    }
                                    else
                                    {
                                        BoundingBoxXYZ bbox = fi.get_BoundingBox(view);
                                        if (bbox != null)
                                        {
                                            targetPos = (bbox.Min + bbox.Max) / 2;
                                        }
                                    }

                                    if (targetPos != null)
                                    {
                                        XYZ translation = targetPos - currentPos;
                                        translation = new XYZ(translation.X, translation.Y, 0);

                                        if (!translation.IsAlmostEqualTo(XYZ.Zero))
                                        {
                                            try
                                            {
                                                ElementTransformUtils.MoveElement(doc, subId, translation);
                                                moved = true;
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // If not moved as subcomponent, try parameters
                if (!moved)
                {
                    // Find length parameter
                    double length = 0;
                    Parameter lenParam = fi.LookupParameter("Length") ?? fi.LookupParameter("repp") ?? fi.LookupParameter("L");
                    if (lenParam != null) length = lenParam.AsDouble();

                    if (length > 0)
                    {
                        string[] possibleParams = { "Dot Position", "Dot Offset", "Dot Offset X", "Offset_Dot", "DotDistance", "Dot_Distance", "Khoáº£ng cÃ¡ch chấm", "Vá»‹ trÃ­ chấm", "Dot Pos" };
                        foreach (string pn in possibleParams)
                        {
                            Parameter pDot = fi.LookupParameter(pn);
                            if (pDot != null && !pDot.IsReadOnly)
                            {
                                pDot.Set(length / 2.0);
                                break;
                            }
                        }
                        fi.Document.Regenerate();
                    }
                }
            }
            
            return debugMsg;
        }

        /// <summary>
        /// Resets color overrides for target family items in the current view.
        /// </summary>
        private void ResetColorOverrides(Document doc, View view)
        {
            // Unhide all hidden detail components in this view before resetting their colors
            var hiddenDetailItems = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId == view.Id && e.IsHidden(view))
                .ToList();

            var elementsToUnhide = new List<ElementId>();
            foreach (var item in hiddenDetailItems)
            {
                elementsToUnhide.Add(item.Id);
                if (item is FamilyInstance fi)
                {
                    var subIds = fi.GetSubComponentIds();
                    if (subIds != null)
                    {
                        foreach (var subId in subIds)
                        {
                            var subElem = doc.GetElement(subId);
                            if (subElem != null && subElem.IsHidden(view))
                                elementsToUnhide.Add(subId);
                        }
                    }
                }
            }

            if (elementsToUnhide.Count > 0)
            {
                try
                {
                    view.UnhideElements(elementsToUnhide);
                }
                catch { }
            }
            var detailItems = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = doc.GetElement(typeId) as FamilySymbol;
                    return type?.FamilyName != null && TargetFamilyNames.Contains(type.FamilyName);
                })
                .ToList();

            int resetCount = 0;
            var detailItemIds = new HashSet<ElementId>(detailItems.Select(d => d.Id));

            foreach (var item in detailItems)
            {
                // Reset detail item color
                view.SetElementOverrides(item.Id, new OverrideGraphicSettings());
                
                if (item is FamilyInstance fiColor)
                {
                    var subIds = fiColor.GetSubComponentIds();
                    if (subIds != null)
                    {
                        foreach (var subId in subIds)
                        {
                            try { view.SetElementOverrides(subId, new OverrideGraphicSettings()); }
                            catch { }
                        }
                    }
                }
                
                resetCount++;
            }

            // Reset color cho táº¥t cáº£ tags trá» Ä‘áº¿n cÃ¡c detail items
            var allTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            int tagResetCount = 0;
            foreach (var tag in allTags)
            {
                try
                {
                    var refs = tag.GetTaggedLocalElements();
                    if (refs != null && refs.Any(r => detailItemIds.Contains(r.Id)))
                    {
                        view.SetElementOverrides(tag.Id, new OverrideGraphicSettings());
                        tagResetCount++;
                    }
                }
                catch { }
            }

            NotifyStatus?.Invoke($"Reset color for {resetCount} items + {tagResetCount} tags.");
        }

        /// <summary>
        /// Hides or shows detail items of the target family based on their rotation angle.
        /// X = horizontal (0Â° or 180Â°), Y = vertical (90Â° or 270Â°).
        /// </summary>
        private void SetLayerVisibility(Document doc, View view, string direction, bool hide)
        {
            var detailItems = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = doc.GetElement(typeId) as FamilySymbol;
                    return type?.FamilyName != null && TargetFamilyNames.Contains(type.FamilyName);
                })
                .ToList();

            var targetIds = new List<ElementId>();

            foreach (var item in detailItems)
            {
                if (item is FamilyInstance fi)
                {
                    double angleDeg = GetRotationDegrees(fi);

                    bool isX = IsHorizontal(angleDeg);
                    bool isY = IsVertical(angleDeg);

                    if ((direction == "X" && isX) || (direction == "Y" && isY))
                    {
                        targetIds.Add(item.Id);

                        // Also include sub-components (the nested dot annotation)
                        var subIds = fi.GetSubComponentIds();
                        if (subIds != null)
                        {
                            foreach (var subId in subIds)
                                targetIds.Add(subId);
                        }
                    }
                }
            }

            if (targetIds.Any())
            {
                if (hide)
                {
                    view.HideElements(targetIds);
                    NotifyStatus?.Invoke($"Hidden {targetIds.Count} Layer {direction} items (incl. dots).");
                }
                else
                {
                    view.UnhideElements(targetIds);
                    NotifyStatus?.Invoke($"Shown {targetIds.Count} Layer {direction} items.");
                }
            }
            else
            {
                NotifyStatus?.Invoke($"No Layer {direction} items found for selected families.");
            }
        }

        /// <summary>
        /// Shows all hidden target family items in the view.
        /// </summary>
        private void ShowAllLayers(Document doc, View view)
        {
            // Collect ALL instances of target family from the document (includes hidden ones)
            var allItems = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = doc.GetElement(typeId) as FamilySymbol;
                    return type?.FamilyName != null && TargetFamilyNames.Contains(type.FamilyName);
                })
                .ToList();

            var allIds = new List<ElementId>();
            foreach (var item in allItems)
            {
                allIds.Add(item.Id);
                // Also include sub-components (nested dots)
                if (item is FamilyInstance fi)
                {
                    var subIds = fi.GetSubComponentIds();
                    if (subIds != null)
                    {
                        foreach (var subId in subIds)
                            allIds.Add(subId);
                    }
                }
            }

            if (allIds.Any())
            {
                try
                {
                    view.UnhideElements(allIds);
                }
                catch { /* Some elements may not be hidden */ }
                NotifyStatus?.Invoke($"Shown all {allIds.Count} items (incl. dots) for selected families.");
            }
            else
            {
                NotifyStatus?.Invoke($"No items found for selected families.");
            }
        }

        /// <summary>
        /// Gets the rotation angle in degrees from a FamilyInstance.
        /// </summary>
        private double GetRotationDegrees(FamilyInstance fi)
        {
            // Try to get rotation from the transform
            Transform transform = fi.GetTransform();
            // BasisX shows the direction the family's X axis points after placement
            XYZ basisX = transform.BasisX;

            // Angle of BasisX relative to world X axis
            double angleRad = Math.Atan2(basisX.Y, basisX.X);
            double angleDeg = angleRad * 180.0 / Math.PI;

            // Normalize to 0-360
            if (angleDeg < 0) angleDeg += 360;
            return angleDeg;
        }

        private bool IsHorizontal(double angleDeg)
        {
            // 0° ± 15° or 180° ± 15°
            double tolerance = 15;
            return (angleDeg <= tolerance || angleDeg >= 360 - tolerance) ||
                   (Math.Abs(angleDeg - 180) <= tolerance);
        }

        private bool IsVertical(double angleDeg)
        {
            // 90° ± 15° or 270° ± 15°
            double tolerance = 15;
            return (Math.Abs(angleDeg - 90) <= tolerance) ||
                   (Math.Abs(angleDeg - 270) <= tolerance);
        }

        /// <summary>
        /// Gets the position of the circle/dot on the Reinforcement Distribution symbol.
        /// The dot is a NESTED Generic Annotation family (e.g. Rincovitch_G_Anno_Dot)
        /// inside the main Reinforcement_Distribution family.
        /// Multiple strategies are used to locate it.
        /// </summary>
        private XYZ GetDotPosition(Element item, Document doc, View view)
        {
            // Strategy 1: Find nested sub-component via GetSubComponentIds()
            if (item is FamilyInstance fi)
            {
                var subIds = fi.GetSubComponentIds();
                if (subIds != null && subIds.Any())
                {
                    foreach (var subId in subIds)
                    {
                        Element subElem = doc.GetElement(subId);
                        if (subElem == null) continue;

                        if (subElem.Location is LocationPoint dotLoc)
                        {
                            return dotLoc.Point;
                        }
                    }
                }
            }

            // Strategy 2: Find the nearest Generic Annotation (dot) in the view
            // that is spatially close to this detail item
            XYZ dotFromView = FindNearestDotAnnotation(item, doc, view);
            if (dotFromView != null) return dotFromView;

            // Strategy 3: Scan the geometry for circle/arc shapes
            XYZ dotFromGeometry = FindDotFromGeometry(item, view);
            if (dotFromGeometry != null) return dotFromGeometry;

            // Fallback: LocationPoint of the main element
            if (item.Location is LocationPoint locPt)
            {
                return locPt.Point;
            }

            // Fallback for line-based elements
            if (item.Location is LocationCurve locCurve)
            {
                return locCurve.Curve.Evaluate(0.5, true); // Midpoint
            }

            // Last fallback: BoundingBox center
            BoundingBoxXYZ bbox = item.get_BoundingBox(view);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2;
            }

            return null;
        }

        /// <summary>
        /// Searches the view for Generic Annotation instances (dots) that are
        /// spatially within the bounding box of the given detail item.
        /// </summary>
        private XYZ FindNearestDotAnnotation(Element item, Document doc, View view)
        {
            BoundingBoxXYZ itemBbox = item.get_BoundingBox(view);
            if (itemBbox == null) return null;

            // Expand search area slightly
            double tolerance = 1.0; // 1 foot
            XYZ searchMin = itemBbox.Min - new XYZ(tolerance, tolerance, tolerance);
            XYZ searchMax = itemBbox.Max + new XYZ(tolerance, tolerance, tolerance);

            var dotAnnotations = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    // Check if it's a dot family
                    if (e is FamilyInstance annFi)
                    {
                        var annType = doc.GetElement(annFi.GetTypeId()) as FamilySymbol;
                        if (annType != null &&
                            (annType.FamilyName.Contains("Dot") ||
                             annType.FamilyName.Contains("Anno_Dot") ||
                             annType.FamilyName.Contains("Rincovitch")))
                        {
                            return true;
                        }
                    }
                    return false;
                })
                .ToList();

            // Find the dot annotation whose location is within the detail item's bounding box
            XYZ bestDot = null;
            double bestDist = double.MaxValue;
            XYZ itemCenter = (itemBbox.Min + itemBbox.Max) / 2;

            foreach (var dot in dotAnnotations)
            {
                if (dot.Location is LocationPoint dotLoc)
                {
                    XYZ dotPt = dotLoc.Point;

                    // Check if the dot is within the expanded bounding box
                    if (dotPt.X >= searchMin.X && dotPt.X <= searchMax.X &&
                        dotPt.Y >= searchMin.Y && dotPt.Y <= searchMax.Y)
                    {
                        double dist = itemCenter.DistanceTo(dotPt);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestDot = dotPt;
                        }
                    }
                }
            }

            return bestDot;
        }

        /// <summary>
        /// Scans the geometry of the element to find circle/arc shapes.
        /// The center of the largest arc is assumed to be the dot position.
        /// </summary>
        private XYZ FindDotFromGeometry(Element item, View view)
        {
            Options opt = new Options { View = view, ComputeReferences = true };
            GeometryElement geo = item.get_Geometry(opt);
            if (geo == null) return null;

            XYZ bestCenter = null;
            double bestRadius = 0;

            foreach (GeometryObject obj in geo)
            {
                SearchGeometryForArcs(obj, ref bestCenter, ref bestRadius);
            }

            return bestCenter;
        }

        private void SearchGeometryForArcs(GeometryObject obj, ref XYZ bestCenter, ref double bestRadius)
        {
            if (obj is Arc arc)
            {
                // Full circle or arc
                if (arc.Radius > bestRadius)
                {
                    bestRadius = arc.Radius;
                    bestCenter = arc.Center;
                }
            }
            else if (obj is GeometryInstance geoInst)
            {
                GeometryElement instGeo = geoInst.GetInstanceGeometry();
                if (instGeo != null)
                {
                    foreach (GeometryObject subObj in instGeo)
                    {
                        SearchGeometryForArcs(subObj, ref bestCenter, ref bestRadius);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the offset vector for the given direction.
        /// </summary>
        private XYZ GetOffsetVector(OffsetDirection direction, double distance)
        {
            double diag = distance / Math.Sqrt(2);

            switch (direction)
            {
                case OffsetDirection.Top:
                    return new XYZ(0, distance, 0);
                case OffsetDirection.Bottom:
                    return new XYZ(0, -distance, 0);
                case OffsetDirection.Left:
                    return new XYZ(-distance, 0, 0);
                case OffsetDirection.Right:
                    return new XYZ(distance, 0, 0);
                case OffsetDirection.TopLeft:
                    return new XYZ(-diag, diag, 0);
                case OffsetDirection.TopRight:
                    return new XYZ(diag, diag, 0);
                case OffsetDirection.BottomLeft:
                    return new XYZ(-diag, -diag, 0);
                case OffsetDirection.BottomRight:
                    return new XYZ(diag, -diag, 0);
                default:
                    return new XYZ(0, distance, 0);
            }
        }

        private void HideTaggedReo(Document doc, View view)
        {
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var elementsToHide = new HashSet<ElementId>();

            foreach (var tag in existingTags)
            {
                var type = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                if (type != null && ((type.FamilyName != null && type.FamilyName.Contains("Reo")) || (type.Name != null && type.Name.Contains("Reo Tag"))))
                {
#if REVIT2022_OR_GREATER
                    foreach (var id in tag.GetTaggedLocalElementIds())
#else
                    foreach (var id in new List<ElementId> { tag.TaggedLocalElementId })
#endif
                    {
                        elementsToHide.Add(id);
                        
                        var elem = doc.GetElement(id);
                        if (elem is FamilyInstance fi)
                        {
                            var subIds = fi.GetSubComponentIds();
                            if (subIds != null)
                            {
                                foreach (var subId in subIds)
                                {
                                    elementsToHide.Add(subId);
                                }
                            }
                        }
                    }
                }
            }

            if (elementsToHide.Count > 0)
            {
                try
                {
                    view.HideElements(elementsToHide);
                    NotifyStatus?.Invoke($"Hidden {elementsToHide.Count} elements tagged with 'Reo Tag'.");
                }
                catch (Exception ex)
                {
                    NotifyStatus?.Invoke($"Error hiding elements: {ex.Message}");
                }
            }
            else
            {
                NotifyStatus?.Invoke("No elements tagged with 'Reo Tag' found.");
            }
        }

        public string GetName() => "MtoSmartTagHandler";
    }
}

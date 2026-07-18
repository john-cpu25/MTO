using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace RincoMTO.Tools.MtoGroupBar
{
    [Transaction(TransactionMode.Manual)]
    public class AlignDistributionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                ElementId detailCatId = null;
                try { detailCatId = Category.GetCategory(doc, BuiltInCategory.OST_DetailComponents)?.Id; } catch { }
#if REVIT2024_OR_GREATER
                if (detailCatId == null) detailCatId = new ElementId((long)BuiltInCategory.OST_DetailComponents);
#else
                if (detailCatId == null) detailCatId = new ElementId((int)BuiltInCategory.OST_DetailComponents);
#endif

                // 1. Pick Main Element
                Reference mainRef = null;
                try
                {
                    mainRef = uidoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element, 
                        new DetailItemFilter(detailCatId, true), 
                        "Chá»n 1 cÃ¢y thÃ©p chÃ­nh (Lap/ZBar)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (mainRef == null) return Result.Cancelled;
                Element mainElem = doc.GetElement(mainRef);

                // 2. Pick Target Elements
                IList<Reference> targetRefs = null;
                try
                {
                    targetRefs = uidoc.Selection.PickObjects(
                        Autodesk.Revit.UI.Selection.ObjectType.Element, 
                        new DetailItemFilter(detailCatId, false), 
                        "QuÃ©t chá»n cÃ¡c mÅ©i tÃªn thÃ©p phÃ¢n bá»‘"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (targetRefs == null || targetRefs.Count == 0) return Result.Cancelled;

                XYZ mainPt = GetLocationPoint(mainElem);
                if (mainPt == null)
                {
                    TaskDialog.Show("Align Group", "KhÃ´ng thá»ƒ xÃ¡c Ä‘á»‹nh vá»‹ trÃ­ cá»§a cÃ¢y thÃ©p chÃ­nh.");
                    return Result.Succeeded;
                }

                using (Transaction tx = new Transaction(doc, "Align Distribution Symbols"))
                {
                    tx.Start();

                    int count = 0;
                    foreach (var tRef in targetRefs)
                    {
                        FamilyInstance target = doc.GetElement(tRef) as FamilyInstance;
                        if (target != null)
                        {
                            XYZ targetPt = GetLocationPoint(target);
                            if (targetPt != null)
                            {
                                XYZ translation = mainPt - targetPt;
                                if (!translation.IsAlmostEqualTo(XYZ.Zero))
                                {
                                    ElementTransformUtils.MoveElement(doc, target.Id, translation);
                                    count++;
                                }
                            }
                        }
                    }

                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private XYZ GetLocationPoint(Element elem)
        {
            if (elem.Location is LocationPoint lp)
            {
                return lp.Point;
            }
            else if (elem.Location is LocationCurve lc && lc.Curve != null)
            {
                return lc.Curve.GetEndPoint(0); 
            }
            else
            {
                // Fallback to bounding box center
                BoundingBoxXYZ bb = elem.get_BoundingBox(null);
                if (bb != null)
                {
                    return (bb.Min + bb.Max) / 2.0;
                }
            }
            return null;
        }
    }

    public class DetailItemFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        private ElementId _detailCatId;
        private bool _isMain;

        public DetailItemFilter(ElementId detailCatId, bool isMain)
        {
            _detailCatId = detailCatId;
            _isMain = isMain;
        }

        public bool AllowElement(Element elem)
        {
            if (!_isMain)
            {
                if (elem is FamilyInstance fi && fi.Category != null && fi.Category.Id == _detailCatId)
                {
                    if (fi.Symbol != null && fi.Symbol.Family != null)
                    {
                        string famName = fi.Symbol.Family.Name;
                        return famName.Contains("Reinforcement_Distribution") || famName.Contains("DistributionAdjustable");
                    }
                }
                return false;
            }
            else
            {
                // Don't allow picking the distribution arrow as the main element
                if (elem is FamilyInstance fi && fi.Category != null && fi.Category.Id == _detailCatId)
                {
                    if (fi.Symbol != null && fi.Symbol.Family != null)
                    {
                        string famName = fi.Symbol.Family.Name;
                        if (famName.Contains("Reinforcement_Distribution") || famName.Contains("DistributionAdjustable"))
                        {
                            return false;
                        }
                    }
                }
                // Allow anything else
                return true;
            }
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.DuplicateSheet
{
    public class DuplicateSheetEventHandler : IExternalEventHandler
    {
        public List<ElementId> SelectedSheetIds { get; set; } = new List<ElementId>();
        public string TargetSeries { get; set; } = string.Empty;
        public bool WithDetailing { get; set; } = false;

        public void Execute(UIApplication uiapp)
        {
            if (SelectedSheetIds == null || SelectedSheetIds.Count == 0) return;

            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;

            List<ViewSheet> selectedSheets = new List<ViewSheet>();
            foreach (var id in SelectedSheetIds)
            {
                if (doc.GetElement(id) is ViewSheet sheet)
                {
                    selectedSheets.Add(sheet);
                }
            }

            if (selectedSheets.Count == 0) return;

            HashSet<string> allSheetNames = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.Name));
            HashSet<string> allSheetNumbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber));
            HashSet<string> allViewNames = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name));

            List<ViewSheet> copiedSheets = new List<ViewSheet>();
            int copiedViewsCount = 0;

            using (Transaction t = new Transaction(doc, WithDetailing ? "Duplicate Sheets with Detailing" : "Duplicate Empty Sheets"))
            {
                t.Start();
                try
                {
                    foreach (var sheet in selectedSheets)
                    {
                        var titleblocks = new FilteredElementCollector(doc, sheet.Id)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .WhereElementIsNotElementType()
                            .ToElements();

                        ElementId tbTypeId = ElementId.InvalidElementId;
                        if (titleblocks.Count > 0)
                        {
                            tbTypeId = titleblocks[0].GetTypeId();
                        }

                        string baseName = sheet.Name + "_MTO";
                        string newName = MakeUniqueName(baseName, allSheetNames);
                        allSheetNames.Add(newName);

                        string baseNumber = sheet.SheetNumber + "_MTO";
                        string newNumber = MakeUniqueName(baseNumber, allSheetNumbers);
                        allSheetNumbers.Add(newNumber);

                        ViewSheet newSheet = ViewSheet.Create(doc, tbTypeId);
                        newSheet.Name = newName;
                        newSheet.SheetNumber = newNumber;
                        
                        if (!string.IsNullOrEmpty(TargetSeries))
                        {
                            Parameter seriesParam = newSheet.LookupParameter("RINCO_TB_SHEET SERIES");
                            if (seriesParam != null && !seriesParam.IsReadOnly)
                            {
                                seriesParam.Set(TargetSeries);
                            }
                        }
                        
                        copiedSheets.Add(newSheet);

                        if (WithDetailing)
                        {
                            foreach (ElementId vpId in sheet.GetAllViewports())
                            {
                                Viewport vp = doc.GetElement(vpId) as Viewport;
                                if (vp == null) continue;

                                View view = doc.GetElement(vp.ViewId) as View;
                                if (view == null) continue;

                                if (view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
                                {
                                    ElementId newViewId = view.Duplicate(ViewDuplicateOption.WithDetailing);
                                    View newView = doc.GetElement(newViewId) as View;

                                    if (newView != null)
                                    {
                                        string newViewName = newView.Name.Replace("Copy", "MTO");
                                        newViewName = MakeUniqueName(newViewName, allViewNames);
                                        newView.Name = newViewName;
                                        allViewNames.Add(newViewName);

                                        Viewport.Create(doc, newSheet.Id, newView.Id, vp.GetBoxCenter());
                                        copiedViewsCount++;

                                        var detailItems = new FilteredElementCollector(doc, newView.Id)
                                            .OfCategory(BuiltInCategory.OST_DetailComponents)
                                            .WhereElementIsNotElementType()
                                            .ToElements();

                                        foreach (var di in detailItems)
                                        {
                                            Parameter elementParam = di.LookupParameter("Element");
                                            if (elementParam != null && !elementParam.IsReadOnly)
                                            {
                                                elementParam.Set(di.UniqueId);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    TaskDialog.Show("Error", $"An error occurred:\n{ex.Message}");
                    return;
                }
            }

            string msg = $"Duplicated {copiedSheets.Count} sheet(s)";
            if (WithDetailing)
            {
                msg += $" and {copiedViewsCount} view(s).";
            }
            else
            {
                msg += ".";
            }
            TaskDialog.Show("Success", msg);
        }

        public string GetName()
        {
            return "Duplicate Sheet Event Handler";
        }

        private static string MakeUniqueName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName))
            {
                return baseName;
            }

            int i = 1;
            while (true)
            {
                string newName = $"{baseName}_{i}";
                if (!existingNames.Contains(newName))
                {
                    return newName;
                }
                i++;
            }
        }
    }
}

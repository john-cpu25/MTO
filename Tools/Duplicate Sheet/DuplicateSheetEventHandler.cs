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
        public ElementId TargetViewportTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId TargetViewTypeId { get; set; } = ElementId.InvalidElementId;
        public string TargetViewTypeName { get; set; } = string.Empty;
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
            Dictionary<ElementId, ElementId> createdViewTypes = new Dictionary<ElementId, ElementId>();

            using (Transaction t = new Transaction(doc, WithDetailing ? "Duplicate Sheets with Detailing" : "Duplicate Empty Sheets"))
            {
                t.Start();
                try
                {
                    if (WithDetailing)
                    {
                        CreateDetailItemParameters(doc, uiapp);
                    }

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

                                        if (TargetViewTypeId != ElementId.InvalidElementId)
                                        {
                                            try
                                            {
                                                newView.ChangeTypeId(TargetViewTypeId);
                                            }
                                            catch { }
                                        }
                                        else if (!string.IsNullOrEmpty(TargetViewTypeName))
                                        {
                                            ElementId originalViewTypeId = view.GetTypeId();
                                            if (originalViewTypeId != ElementId.InvalidElementId)
                                            {
                                                if (createdViewTypes.TryGetValue(originalViewTypeId, out ElementId mappedId))
                                                {
                                                    try { newView.ChangeTypeId(mappedId); } catch { }
                                                }
                                                else
                                                {
                                                    ViewFamilyType originalType = doc.GetElement(originalViewTypeId) as ViewFamilyType;
                                                    if (originalType != null)
                                                    {
                                                        var existingType = new FilteredElementCollector(doc)
                                                            .OfClass(typeof(ViewFamilyType))
                                                            .Cast<ViewFamilyType>()
                                                            .FirstOrDefault(v => v.ViewFamily == originalType.ViewFamily && v.Name.Equals(TargetViewTypeName, StringComparison.OrdinalIgnoreCase));

                                                        if (existingType != null)
                                                        {
                                                            createdViewTypes[originalViewTypeId] = existingType.Id;
                                                            try { newView.ChangeTypeId(existingType.Id); } catch { }
                                                        }
                                                        else
                                                        {
                                                            try 
                                                            {
                                                                ElementType newType = originalType.Duplicate(TargetViewTypeName);
                                                                createdViewTypes[originalViewTypeId] = newType.Id;
                                                                newView.ChangeTypeId(newType.Id);
                                                            }
                                                            catch { }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        Viewport newVp = Viewport.Create(doc, newSheet.Id, newView.Id, vp.GetBoxCenter());
                                        
                                        ElementId originalVpTypeId = vp.GetTypeId();
                                        
                                        if (TargetViewportTypeId != ElementId.InvalidElementId && newView.Name.IndexOf("OVER", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            newVp.ChangeTypeId(TargetViewportTypeId);
                                        }
                                        else if (originalVpTypeId != ElementId.InvalidElementId)
                                        {
                                            newVp.ChangeTypeId(originalVpTypeId);
                                        }

                                        try
                                        {
                                            newVp.LabelOffset = vp.LabelOffset;
                                            newVp.LabelLineLength = vp.LabelLineLength;
                                        }
                                        catch { }
                                        
                                        copiedViewsCount++;

                                        var detailItems = new FilteredElementCollector(doc, newView.Id)
                                            .OfCategory(BuiltInCategory.OST_DetailComponents)
                                            .WhereElementIsNotElementType()
                                            .ToElements();

                                        foreach (var di in detailItems)
                                        {
                                            Parameter elementIdParam = di.LookupParameter("Element ID");
                                            if (elementIdParam != null && !elementIdParam.IsReadOnly)
                                            {
#if REVIT2024_OR_GREATER
                                                elementIdParam.Set(di.Id.Value.ToString());
#else
                                                elementIdParam.Set(di.Id.IntegerValue.ToString());
#endif
                                            }
                                            
                                            Parameter uniqueIdParam = di.LookupParameter("Unique ID");
                                            if (uniqueIdParam != null && !uniqueIdParam.IsReadOnly)
                                            {
                                                uniqueIdParam.Set(di.UniqueId);
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

        private void CreateDetailItemParameters(Document doc, UIApplication uiapp)
        {
            Category detailItemCat = Category.GetCategory(doc, BuiltInCategory.OST_DetailComponents);
            if (detailItemCat == null) return;

            BindingMap bindingMap = doc.ParameterBindings;
            DefinitionBindingMapIterator it = bindingMap.ForwardIterator();
            bool hasElementId = false;
            bool hasUniqueId = false;
            it.Reset();
            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def != null)
                {
                    if (def.Name.Equals("Element ID", StringComparison.OrdinalIgnoreCase))
                    {
                        ElementBinding binding = it.Current as ElementBinding;
                        if (binding != null && binding.Categories.Contains(detailItemCat))
                        {
                            hasElementId = true;
                        }
                    }
                    else if (def.Name.Equals("Unique ID", StringComparison.OrdinalIgnoreCase))
                    {
                        ElementBinding binding = it.Current as ElementBinding;
                        if (binding != null && binding.Categories.Contains(detailItemCat))
                        {
                            hasUniqueId = true;
                        }
                    }
                }
            }

            if (hasElementId && hasUniqueId) return;

            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            string originalSharedParamFile = app.SharedParametersFilename;
            string tempSharedParamFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TempSharedParams.txt");

            try
            {
                System.IO.File.WriteAllText(tempSharedParamFile, "");
                app.SharedParametersFilename = tempSharedParamFile;

                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile != null)
                {
                    DefinitionGroup group = defFile.Groups.get_Item("MTO Parameters");
                    if (group == null) group = defFile.Groups.Create("MTO Parameters");
                    
                    CategorySet catSet = app.Create.NewCategorySet();
                    catSet.Insert(detailItemCat);

                    if (!hasElementId)
                    {
                        ExternalDefinitionCreationOptions opt = new ExternalDefinitionCreationOptions("Element ID", SpecTypeId.String.Text);
                        opt.Visible = true;
                        opt.UserModifiable = true;
                        Definition externalDef = group.Definitions.Create(opt);
                        InstanceBinding instanceBinding = app.Create.NewInstanceBinding(catSet);
#if REVIT2024_OR_GREATER
                        doc.ParameterBindings.Insert(externalDef, instanceBinding, GroupTypeId.Text);
#else
                        doc.ParameterBindings.Insert(externalDef, instanceBinding, BuiltInParameterGroup.PG_TEXT);
#endif
                    }

                    if (!hasUniqueId)
                    {
                        ExternalDefinitionCreationOptions opt = new ExternalDefinitionCreationOptions("Unique ID", SpecTypeId.String.Text);
                        opt.Visible = true;
                        opt.UserModifiable = true;
                        Definition externalDef = group.Definitions.Create(opt);
                        InstanceBinding instanceBinding = app.Create.NewInstanceBinding(catSet);
#if REVIT2024_OR_GREATER
                        doc.ParameterBindings.Insert(externalDef, instanceBinding, GroupTypeId.Text);
#else
                        doc.ParameterBindings.Insert(externalDef, instanceBinding, BuiltInParameterGroup.PG_TEXT);
#endif
                    }

                    // Set allow vary between groups
                    DefinitionBindingMapIterator itAfter = doc.ParameterBindings.ForwardIterator();
                    itAfter.Reset();
                    while (itAfter.MoveNext())
                    {
                        InternalDefinition intDef = itAfter.Key as InternalDefinition;
                        if (intDef != null && (intDef.Name.Equals("Element ID", StringComparison.OrdinalIgnoreCase) || intDef.Name.Equals("Unique ID", StringComparison.OrdinalIgnoreCase)))
                        {
                            try
                            {
                                intDef.SetAllowVaryBetweenGroups(doc, true);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    app.SharedParametersFilename = originalSharedParamFile;
                    if (System.IO.File.Exists(tempSharedParamFile))
                    {
                        System.IO.File.Delete(tempSharedParamFile);
                    }
                }
                catch { }
            }
        }
    }
}

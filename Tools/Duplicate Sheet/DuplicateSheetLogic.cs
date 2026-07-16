using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RincoMTO.Tools.DuplicateSheet.UI;

namespace RincoMTO.Tools.DuplicateSheet
{
    public static class DuplicateSheetLogic
    {
        private static SelectSheetsWindow _window;
        private static DuplicateSheetEventHandler _handler;
        private static ExternalEvent _exEvent;

        public static void Execute(ExternalCommandData commandData, bool withDetailing)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (_handler == null)
            {
                _handler = new DuplicateSheetEventHandler();
                _exEvent = ExternalEvent.Create(_handler);
            }

            _handler.WithDetailing = withDetailing;

            // Step 1: Try to get pre-selected sheets
            List<ViewSheet> selectedSheets = new List<ViewSheet>();
            var selectionIds = uidoc.Selection.GetElementIds();
            foreach (var id in selectionIds)
            {
                if (doc.GetElement(id) is ViewSheet sheet)
                {
                    selectedSheets.Add(sheet);
                }
            }

            // Step 2: If no sheets selected, show UI
            if (selectedSheets.Count == 0)
            {
                if (_window != null && _window.IsLoaded)
                {
                    _window.Focus();
                    return;
                }

                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsTemplate)
                    .ToList();

                if (allSheets.Count == 0)
                {
                    TaskDialog.Show("Error", "No sheets found in the document.");
                    return;
                }

                var availableSeries = allSheets
                    .Select(s => s.LookupParameter("RINCO_TB_SHEET SERIES")?.AsString())
                    .Where(val => !string.IsNullOrEmpty(val))
                    .Distinct()
                    .OrderBy(val => val)
                    .ToList();

                var viewportTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType))
                    .Cast<ElementType>()
                    .Where(e => e.FamilyName == "Viewport" || (e.Category != null && e.Category.Name == "Viewports"))
                    .OrderBy(e => e.Name)
                    .ToList();

                var viewTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .ToList();

                _window = new SelectSheetsWindow(allSheets, availableSeries, viewportTypes, viewTypes, _handler, _exEvent);
                // Set Revit as parent window
                var hndl = uiapp.MainWindowHandle;
                var helper = new WindowInteropHelper(_window);
                helper.Owner = hndl;

                _window.Show();
            }
            else
            {
                // If pre-selected, inherit the series from the first sheet
                _handler.SelectedSheetIds = selectedSheets.Select(s => s.Id).ToList();
                _handler.TargetSeries = selectedSheets.FirstOrDefault()?.LookupParameter("RINCO_TB_SHEET SERIES")?.AsString() ?? string.Empty;
                
                // Execute synchronously since we have context
                _handler.Execute(uiapp);
            }
        }
    }
}

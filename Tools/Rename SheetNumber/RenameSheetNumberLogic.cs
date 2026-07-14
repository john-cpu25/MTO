using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RincoMTO.Tools.RenameSheetNumber.UI;

namespace RincoMTO.Tools.RenameSheetNumber
{
    public static class RenameSheetNumberLogic
    {
        private static RenameSheetNumberWindow _window;
        private static RenameSheetNumberEventHandler _handler;
        private static ExternalEvent _exEvent;

        public static void Execute(ExternalCommandData commandData)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (_handler == null)
            {
                _handler = new RenameSheetNumberEventHandler();
                _exEvent = ExternalEvent.Create(_handler);
            }

            if (_window != null && _window.IsLoaded)
            {
                _window.Focus();
                return;
            }

            // Get all sheets
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

            // Get pre-selected sheets
            List<ElementId> selectedSheetIds = new List<ElementId>();
            var selectionIds = uidoc.Selection.GetElementIds();
            foreach (var id in selectionIds)
            {
                if (doc.GetElement(id) is ViewSheet sheet)
                {
                    selectedSheetIds.Add(sheet.Id);
                }
            }

            var sheetItems = new List<SheetRenameItem>();
            foreach (var sheet in allSheets)
            {
                sheetItems.Add(new SheetRenameItem
                {
                    SheetId = sheet.Id,
                    SheetName = sheet.Name,
                    OldNumber = sheet.SheetNumber,
                    NewNumber = sheet.SheetNumber,
                    IsSelected = selectedSheetIds.Count == 0 || selectedSheetIds.Contains(sheet.Id)
                });
            }

            _window = new RenameSheetNumberWindow(sheetItems, _handler, _exEvent);
            // Set Revit as parent window
            var hndl = uiapp.MainWindowHandle;
            var helper = new WindowInteropHelper(_window);
            helper.Owner = hndl;

            _window.Show();
        }
    }
}

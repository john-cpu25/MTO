using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.DuplicateSheet
{
    [Transaction(TransactionMode.Manual)]
    public class DuplicateEmptySheetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DuplicateSheetLogic.Execute(commandData, withDetailing: false);
            return Result.Succeeded;
        }
    }
}

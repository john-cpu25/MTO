using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.DuplicateSheet
{
    [Transaction(TransactionMode.Manual)]
    public class DuplicateWithDetailingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DuplicateSheetLogic.Execute(commandData, withDetailing: true);
            return Result.Succeeded;
        }
    }
}

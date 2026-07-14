using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.RenameSheetNumber
{
    [Transaction(TransactionMode.Manual)]
    public class RenameSheetNumberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RenameSheetNumberLogic.Execute(commandData);
            return Result.Succeeded;
        }
    }
}

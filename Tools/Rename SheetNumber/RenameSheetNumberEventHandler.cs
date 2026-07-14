using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RincoMTO.Tools.RenameSheetNumber.UI;

namespace RincoMTO.Tools.RenameSheetNumber
{
    public class RenameSheetNumberEventHandler : IExternalEventHandler
    {
        public List<SheetRenameItem> ItemsToRename { get; set; } = new List<SheetRenameItem>();

        public void Execute(UIApplication app)
        {
            if (ItemsToRename == null || ItemsToRename.Count == 0) return;

            Document doc = app.ActiveUIDocument.Document;
            
            using (Transaction t = new Transaction(doc, "Rename Sheet Numbers"))
            {
                t.Start();
                
                int successCount = 0;
                int failCount = 0;

                // Step 1: Temporarily append a suffix to avoid duplicate sheet number errors during the swap
                string tempSuffix = "_" + Guid.NewGuid().ToString().Substring(0, 5);
                foreach (var item in ItemsToRename)
                {
                    if (item.OldNumber == item.NewNumber) continue;
                    
                    if (doc.GetElement(item.SheetId) is ViewSheet sheet)
                    {
                        try
                        {
                            sheet.SheetNumber = item.NewNumber + tempSuffix;
                        }
                        catch
                        {
                            // Ignore temp rename failures, might just be read-only or some other constraint
                        }
                    }
                }

                // Step 2: Apply the actual new numbers
                foreach (var item in ItemsToRename)
                {
                    if (item.OldNumber == item.NewNumber) continue;

                    if (doc.GetElement(item.SheetId) is ViewSheet sheet)
                    {
                        try
                        {
                            sheet.SheetNumber = item.NewNumber;
                            successCount++;
                        }
                        catch (Exception)
                        {
                            failCount++;
                            // Attempt to revert temp suffix if actual rename failed
                            try
                            {
                                sheet.SheetNumber = item.OldNumber;
                            }
                            catch { }
                        }
                    }
                }
                
                t.Commit();

                if (failCount > 0)
                {
                    TaskDialog.Show("Rename Sheets", $"Successfully renamed {successCount} sheets.\nFailed to rename {failCount} sheets (possibly duplicate sheet numbers or read-only).");
                }
            }
        }

        public string GetName()
        {
            return "Rename Sheet Numbers Event";
        }
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RincoMTO.Tools.MtoGroupBar.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RincoMTO.Tools.MtoGroupBar
{
    public class MtoGroupBarEventHandler : IExternalEventHandler
    {
        public Action<UIApplication> Action { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                Action?.Invoke(app);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        public string GetName()
        {
            return "MtoGroupBarEventHandler";
        }
    }
}

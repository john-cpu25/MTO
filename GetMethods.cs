using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace TestApp
{
    class Program
    {
        static void Main()
        {
            var m = typeof(CADLinkType).GetMethods()
                .Where(x => x.Name.Contains("Load") || x.Name.Contains("Reload"))
                .Select(x => x.Name + "(" + string.Join(", ", x.GetParameters().Select(p => p.ParameterType.Name)) + ")");
            File.WriteAllText("methods.txt", string.Join(Environment.NewLine, m));
        }
    }
}

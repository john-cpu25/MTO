using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        try
        {
            var assembly = Assembly.LoadFrom(@"C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll");
            var type = assembly.GetType("Autodesk.Revit.DB.CADLinkType");
            var methods = type.GetMethods()
                .Where(m => m.Name.Contains("Load"))
                .Select(m => m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ") -> " + m.ReturnType.Name);
            File.WriteAllText("methods.txt", string.Join(Environment.NewLine, methods));
        }
        catch (Exception ex)
        {
            File.WriteAllText("methods.txt", ex.ToString());
        }
    }
}

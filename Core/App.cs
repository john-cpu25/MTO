using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RincoMTO
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // Register Assembly Resolver to handle library conflicts (System.Text.Json, etc.)
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            // KhÃ¡Â»Å¸i tÃ¡ÂºÂ¡o Ribbon UI thÃƒÂ´ng qua RibbonManager
            RincoMTO.Core.RibbonManager.SetupRibbon(application);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
            return Result.Succeeded;
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                // If it's one of our complex dependencies, try to find it in our folder
                if (args.Name.Contains("System.Text.Json") || 
                    args.Name.Contains("System.Runtime.CompilerServices.Unsafe") ||
                    args.Name.Contains("Microsoft.Bcl.AsyncInterfaces") ||
                    args.Name.Contains("System.Memory") ||
                    args.Name.Contains("System.Buffers"))
                {
                    string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                    string targetPath = Path.Combine(assemblyDir, assemblyName);

                    if (File.Exists(targetPath))
                    {
                        return Assembly.LoadFrom(targetPath);
                    }
                }
            }
            catch
            {
                // Silently fail to let other resolvers try
            }
            return null;
        }
    }
}

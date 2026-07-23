using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RincoMTO.Core
{
    public static class RibbonManager
    {
        private static string _assemblyPath = Assembly.GetExecutingAssembly().Location;
        private static string _assemblyDir = Path.GetDirectoryName(_assemblyPath);
        private static string _resourcesPath = Path.Combine(Directory.GetParent(_assemblyDir).FullName, "Resources");

        public static void SetupRibbon(UIControlledApplication application)
        {
            string tabName = "RincoMTO";

            // Create Ribbon Tab
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Exception)
            {
                // Ignore if tab already exists
            }

            // Define Panels
            RibbonPanel generalPanel = GetOrCreatePanel(application, tabName, "General");

            // Add Buttons to General Panel
            AddDuplicateSheetButton(generalPanel);
            AddMtoCheckButton(generalPanel);
            AddMtoSmartTagButton(generalPanel);
            AddPtReportButton(generalPanel);
            AddRenameSheetNumberButton(generalPanel);
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(tabName))
            {
                if (panel.Name == panelName) return panel;
            }
            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static BitmapImage LoadIcon(string iconName, int size = 32)
        {
            string iconPath = Path.Combine(_resourcesPath, iconName);
            if (!File.Exists(iconPath)) return null;

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(iconPath, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (size > 0)
                {
                    image.DecodePixelWidth = size;
                    image.DecodePixelHeight = size;
                }
                image.EndInit();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static void AddDuplicateSheetButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdDuplicateSheet",
                "Duplicate\nSheet",
                _assemblyPath,
                "RincoMTO.Tools.DuplicateSheet.DuplicateWithDetailingCommand"
            );
            btnData.ToolTip = "Duplicate selected sheets and all their views.";
            btnData.LargeImage = LoadIcon("DefaultIcon.png");
            btnData.Image = LoadIcon("DefaultIcon.png", 16);

            panel.AddItem(btnData);
        }

        private static void AddMtoCheckButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdMtoCheck",
                "MTO\nCheck",
                _assemblyPath,
                "RincoMTO.Tools.MtoCheck.Command"
            );
            btnData.ToolTip = "Check and compare rebar between drawings using Element ID and Unique ID.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("DefaultIcon.png"); 
            pb.Image = LoadIcon("DefaultIcon.png", 16);
        }



        private static void AddMtoSmartTagButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdMtoSmartTag",
                "MTO Smart\nTag",
                _assemblyPath,
                "RincoMTO.Tools.MtoSmartTag.Command"
            );
            btnData.ToolTip = "MTO Smart Tag tool.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            try { pb.LargeImage = LoadIcon("DefaultIcon.png"); pb.Image = LoadIcon("DefaultIcon.png", 16); } catch { }
        }

        private static void AddPtReportButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdPtReport",
                "PT\nReport",
                _assemblyPath,
                "RincoMTO.Tools.PtReport.Command"
            );
            btnData.ToolTip = "Create a table to review all PTs in active view.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            try { pb.LargeImage = LoadIcon("DefaultIcon.png"); pb.Image = LoadIcon("DefaultIcon.png", 16); } catch { }
        }

        private static void AddRenameSheetNumberButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdRenameSheetNumber",
                "Rename\nSheetNumber",
                _assemblyPath,
                "RincoMTO.Tools.RenameSheetNumber.RenameSheetNumberCommand"
            );
            btnData.ToolTip = "Rename Sheet Numbers.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            try { pb.LargeImage = LoadIcon("DefaultIcon.png"); pb.Image = LoadIcon("DefaultIcon.png", 16); } catch { }
        }
    }
}

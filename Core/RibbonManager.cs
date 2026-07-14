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
            // RibbonPanel modelingPanel = GetOrCreatePanel(application, tabName, "Modeling");

            // Add Buttons to General Panel
            AddFilterButton(generalPanel);
            AddReloadCADButton(generalPanel);
            AddJoinElementsButton(generalPanel);
            AddElementsTagsButton(generalPanel);
            AddAlignTagsButton(generalPanel);
            AddImportEXtoLegendButton(generalPanel);
            AddWallDivideButton(generalPanel);
            AddElevationViewButton(generalPanel);
            AddCreateSectionWallButton(generalPanel);
            AddViewRefButton(generalPanel);
            AddLayerXYButton(generalPanel);
            AddDuplicateSheetButton(generalPanel);
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(tabName))
            {
                if (panel.Name == panelName) return panel;
            }
            return application.CreateRibbonPanel(tabName, panelName);
        }

        private static void AddFilterButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdFilter",
                "Advanced\nFilter",
                _assemblyPath,
                "RincoMTO.Tools.Filter.Command"
            );
            btnData.ToolTip = "Advanced object filter for project elements.";
            
            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("Filter.png");
            pb.Image = LoadIcon("Filter.png", 16);
        }

        private static void AddReloadCADButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdReloadCADLinks",
                "Reload\nCAD Links",
                _assemblyPath,
                "RincoMTO.Tools.ReloadCADLinks.ReloadCADCommand"
            );
            btnData.ToolTip = "Batch reload CAD links with path overriding.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("ReloadCAD.png");
            pb.Image = LoadIcon("ReloadCAD.png", 16);
        }

        private static void AddJoinElementsButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdJoinElements",
                "Join\nElements",
                _assemblyPath,
                "RincoMTO.Tools.JoinElements.Command"
            );
            btnData.ToolTip = "Batch Join, Unjoin, or Switch geometry of elements.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("JoinElements.png");
            pb.Image = LoadIcon("JoinElements.png", 16);
        }

        private static void AddElementsTagsButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdElementsTags",
                "Elements\nTags",
                _assemblyPath,
                "RincoMTO.Tools.ElementsTags.Command"
            );
            btnData.ToolTip = "Batch tag elements and fix misplaced tags.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("ElementsTags.png");
            pb.Image = LoadIcon("ElementsTags.png", 16);
        }

        private static void AddAlignTagsButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdAlignTags",
                "Align\nTags",
                _assemblyPath,
                "RincoMTO.Tools.AlignTags.AlignTagsCommand"
            );
            btnData.ToolTip = "Align heads of multiple tags based on a reference tag coordinate.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("AlignTags.png");
            pb.Image = LoadIcon("AlignTags.png", 16);
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

        private static void AddImportEXtoLegendButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdImportEXtoLegend",
                "Import\nExcel",
                _assemblyPath,
                "RincoMTO.Tools.ImportEXtoLegend.Command"
            );
            btnData.ToolTip = "Import and update Excel tables in Legend views.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("ImportExcel.png");
            pb.Image = LoadIcon("ImportExcel.png", 16);
        }

        private static void AddWallDivideButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdWallDivide",
                "Wall\nDivide",
                _assemblyPath,
                "RincoMTO.Tools.WallDivide.Command"
            );
            btnData.ToolTip = "Divide wall panels based on weight and dimension constraints.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("WallDivide.png");
            pb.Image = LoadIcon("WallDivide.png", 16);
        }

        private static void AddElevationViewButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdElevationView",
                "Elevation\nView",
                _assemblyPath,
                "RincoMTO.Tools.ElevationView.Command"
            );
            btnData.ToolTip = "Optimize elevation view crops and level lines.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
        }

        private static void AddCreateSectionWallButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdCreateSectionWall",
                "Section\nWall",
                _assemblyPath,
                "RincoMTO.Tools.CreateSectionWall.Command"
            );
            btnData.ToolTip = "Generate section views parallel to selected walls.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
        }

        private static void AddViewRefButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdViewRef",
                "View\nRef",
                _assemblyPath,
                "RincoMTO.Tools.ViewRef.Command"
            );
            btnData.ToolTip = "Place View Reference tags on walls.";

            // Add icon if it exists, otherwise it will just show text
            PushButton pb = panel.AddItem(btnData) as PushButton;
            pb.LargeImage = LoadIcon("ViewRef.png");
            pb.Image = LoadIcon("ViewRef.png", 16);
        }

        private static void AddLayerXYButton(RibbonPanel panel)
        {
            PushButtonData btnData = new PushButtonData(
                "cmdLayerXY",
                "Layer\nXY",
                _assemblyPath,
                "RincoMTO.Tools.MtoLayerXy.Command"
            );
            btnData.ToolTip = "Toggle visibility of family instances by X/Y direction.";

            PushButton pb = panel.AddItem(btnData) as PushButton;
        }

        private static void AddDuplicateSheetButton(RibbonPanel panel)
        {
            PulldownButtonData pdData = new PulldownButtonData(
                "cmdDuplicateSheetSplit",
                "Duplicate\nSheet"
            );
            pdData.ToolTip = "Duplicate sheets with or without detailing.";

            PulldownButton pdBtn = panel.AddItem(pdData) as PulldownButton;
            pdBtn.LargeImage = LoadIcon("DuplicateSheet.png");
            pdBtn.Image = LoadIcon("DuplicateSheet.png", 16);

            PushButtonData btnWithDetailing = new PushButtonData(
                "cmdDuplicateWithDetailing",
                "With Detailing",
                _assemblyPath,
                "RincoMTO.Tools.DuplicateSheet.DuplicateWithDetailingCommand"
            );
            btnWithDetailing.ToolTip = "Duplicate selected sheets and all their views with detailing.";
            btnWithDetailing.LargeImage = LoadIcon("DuplicateSheet.png");
            btnWithDetailing.Image = LoadIcon("DuplicateSheet.png", 16);
            pdBtn.AddPushButton(btnWithDetailing);

            PushButtonData btnEmpty = new PushButtonData(
                "cmdDuplicateEmptySheet",
                "Empty Sheet",
                _assemblyPath,
                "RincoMTO.Tools.DuplicateSheet.DuplicateEmptySheetCommand"
            );
            btnEmpty.ToolTip = "Duplicate selected sheets without any views.";
            btnEmpty.LargeImage = LoadIcon("DuplicateSheet.png");
            btnEmpty.Image = LoadIcon("DuplicateSheet.png", 16);
            pdBtn.AddPushButton(btnEmpty);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RincoMTO.Tools.MtoSmartTag.ViewModels
{
    public partial class TagTypeItem : ObservableObject
    {
        public FamilySymbol Symbol { get; }
        public string DisplayName => $"{Symbol.FamilyName} : {Symbol.Name}";
        public ElementId Id => Symbol.Id;

        public TagTypeItem(FamilySymbol symbol)
        {
            Symbol = symbol;
        }
    }

    public partial class DirectionItem : ObservableObject
    {
        public OffsetDirection Direction { get; }
        public string DisplayName { get; }
        public double Angle { get; }

        public DirectionItem(OffsetDirection direction, string displayName, double angle)
        {
            Direction = direction;
            DisplayName = displayName;
            Angle = angle;
        }
    }

    public partial class TargetFamilyItem : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty]
        private bool _isSelected;

        public TargetFamilyItem(string name, bool isSelected = false)
        {
            Name = name;
            _isSelected = isSelected;
        }
    }

    public partial class MtoSmartTagViewModel : ObservableObject
    {
        private MtoSmartTagHandler _handler;
        private ExternalEvent _externalEvent;

        // Tag Types
        public ObservableCollection<TagTypeItem> TagTypes { get; set; }

        [ObservableProperty]
        private TagTypeItem _selectedTagType;

        // Target Families (which families to tag/layer)
        public ObservableCollection<TargetFamilyItem> TargetFamilies { get; set; }

        // Direction Options
        public ObservableCollection<DirectionItem> Directions { get; set; }

        [ObservableProperty]
        private DirectionItem _selectedDirection;

        // Offset Distance (mm)
        [ObservableProperty]
        private double _offsetDistance = 300;

        // Direct X/Y offset (mm)
        [ObservableProperty]
        private double _offsetX = 0;

        [ObservableProperty]
        private double _offsetY = 0;

        [ObservableProperty]
        private bool _useDirectXY = false;

        // Add Leader
        [ObservableProperty]
        private bool _addLeader = false;

        // Only tag items that already have a tag
        [ObservableProperty]
        private bool _onlyAlreadyTagged = false;
        
        [ObservableProperty]
        private bool _onlyUntagged = false;

        // Color Override
        [ObservableProperty]
        private bool _applyColorOverride = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewBrush))]
        private byte _colorR = 255;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewBrush))]
        private byte _colorG = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PreviewBrush))]
        private byte _colorB = 0;

        public System.Windows.Media.SolidColorBrush PreviewBrush =>
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(ColorR, ColorG, ColorB));

        // Untagged Items Settings
        [ObservableProperty]
        private bool _centerDotAdjustable = true;

        [ObservableProperty]
        private bool _hideDotAdjustable = false;

        [ObservableProperty]
        private bool _showDotAdjustable = false;

        [ObservableProperty]
        private bool _centerDotZBar = true;

        [ObservableProperty]
        private bool _hideDotZBar = false;

        [ObservableProperty]
        private bool _showDotZBar = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UntaggedPreviewBrush))]
        private byte _untaggedColorR = 255;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UntaggedPreviewBrush))]
        private byte _untaggedColorG = 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UntaggedPreviewBrush))]
        private byte _untaggedColorB = 255;

        public System.Windows.Media.SolidColorBrush UntaggedPreviewBrush =>
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(UntaggedColorR, UntaggedColorG, UntaggedColorB));

        [ObservableProperty]
        private bool _colorUntaggedItems = true;

        [ObservableProperty]
        private bool _ignoreTagCheck = false;

        // Status
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusVisible))]
        private string _statusMessage;

        public bool StatusVisible => !string.IsNullOrEmpty(StatusMessage);

        // Count info
        [ObservableProperty]
        private string _itemCountInfo;

        private readonly Document _doc;
        private readonly View _activeView;

        public MtoSmartTagViewModel(Document doc, View activeView, MtoSmartTagHandler handler)
        {
            _doc = doc;
            _activeView = activeView;
            _handler = handler;
            _externalEvent = ExternalEvent.Create(_handler);

            // Initialize directions
            Directions = new ObservableCollection<DirectionItem>
            {
                new DirectionItem(OffsetDirection.TopLeft, "Top-Left", -45),
                new DirectionItem(OffsetDirection.Top, "Top", 0),
                new DirectionItem(OffsetDirection.TopRight, "Top-Right", 45),
                new DirectionItem(OffsetDirection.Left, "Left", -90),
                new DirectionItem(OffsetDirection.Right, "Right", 90),
                new DirectionItem(OffsetDirection.BottomLeft, "Bottom-Left", -135),
                new DirectionItem(OffsetDirection.Bottom, "Bottom", 180),
                new DirectionItem(OffsetDirection.BottomRight, "Bottom-Right", 135),
            };
            SelectedDirection = Directions.First(); // Default: TopLeft

            LoadData();
        }

        private void LoadData()
        {
            // Find Detail Item Tag families
            var tagFamilies = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             fs.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_DetailComponentTags &&
                             fs.FamilyName != null && fs.FamilyName.Contains("RINCO_TAG_Reo") &&
                             fs.Name != null && fs.Name.Contains("Reo Tag"))
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            // Preserve selected tag type if possible
            ElementId currentSelectedTagId = SelectedTagType?.Id;

            TagTypes = new ObservableCollection<TagTypeItem>(tagFamilies.Select(fs => new TagTypeItem(fs)));
            
            if (currentSelectedTagId != null)
                SelectedTagType = TagTypes.FirstOrDefault(t => t.Id == currentSelectedTagId) ?? TagTypes.FirstOrDefault();
            else
                SelectedTagType = TagTypes.FirstOrDefault();

            // Find distinct Detail Item families in the view (for target family selection)
            var detailFamilies = new FilteredElementCollector(_doc, _activeView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Select(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = _doc.GetElement(typeId) as FamilySymbol;
                    return type?.FamilyName;
                })
                .Where(name => !string.IsNullOrEmpty(name) && name.ToUpper().StartsWith("REO"))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            TargetFamilies = new ObservableCollection<TargetFamilyItem>(
                detailFamilies.Select(f => new TargetFamilyItem(f, true))
            );

            // Count items in view (for selected families)
            UpdateItemCount();
        }

        [RelayCommand]
        private void ReloadView()
        {
            // Reset state to mimic reopening the tool
            SelectedTagType = null;
            TargetFamilies = null;
            if (Directions != null && Directions.Any())
            {
                SelectedDirection = Directions.First();
            }

            OffsetDistance = 300;
            OffsetX = 0;
            OffsetY = 0;
            UseDirectXY = false;
            AddLeader = false;
            OnlyAlreadyTagged = false;
            OnlyUntagged = false;

            ApplyColorOverride = true;
            ColorR = 255;
            ColorG = 0;
            ColorB = 0;

            CenterDotAdjustable = true;
            HideDotAdjustable = false;
            ShowDotAdjustable = false;

            CenterDotZBar = true;
            HideDotZBar = false;
            ShowDotZBar = false;

            UntaggedColorR = 255;
            UntaggedColorG = 0;
            UntaggedColorB = 255;
            
            ColorUntaggedItems = true;
            IgnoreTagCheck = false;

            LoadData();
            StatusMessage = "View reloaded. Data refreshed.";
        }

        private void UpdateItemCount()
        {
            var selectedFamilies = TargetFamilies?.Where(f => f.IsSelected).Select(f => f.Name).ToList() ?? new List<string>();

            if (!selectedFamilies.Any())
            {
                ItemCountInfo = "No target families selected";
                return;
            }

            int count = new FilteredElementCollector(_doc, _activeView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var typeId = e.GetTypeId();
                    var type = _doc.GetElement(typeId) as FamilySymbol;
                    return type?.FamilyName != null && selectedFamilies.Contains(type.FamilyName);
                })
                .Count();

            ItemCountInfo = $"Found {count} items for selected families in current view";
        }

        private Action<string> GetStatusNotifier()
        {
            return msg =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = msg;
                });
            };
        }

        [RelayCommand]
        private void TagAll()
        {
            if (SelectedTagType == null)
            {
                StatusMessage = "Please select a Tag Type.";
                return;
            }

            if (SelectedDirection == null)
            {
                StatusMessage = "Please select an offset direction.";
                return;
            }

            _handler.Action = "TagAll";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.SelectedTagTypeId = SelectedTagType.Id;
            _handler.Direction = SelectedDirection.Direction;
            _handler.OffsetDistanceMm = OffsetDistance;
            _handler.UseDirectOffset = UseDirectXY;
            _handler.OffsetXMm = OffsetX;
            _handler.OffsetYMm = OffsetY;
            _handler.AddLeader = AddLeader;
            _handler.OnlyAlreadyTagged = OnlyAlreadyTagged;
            _handler.OnlyUntagged = OnlyUntagged;
            _handler.ApplyColorOverride = ApplyColorOverride;
            _handler.ColorR = ColorR;
            _handler.ColorG = ColorG;
            _handler.ColorB = ColorB;
            
            _handler.CenterDotAdjustable = CenterDotAdjustable;
            _handler.HideDotAdjustable = HideDotAdjustable;
            _handler.ShowDotAdjustable = ShowDotAdjustable;
            _handler.CenterDotZBar = CenterDotZBar;
            _handler.HideDotZBar = HideDotZBar;
            _handler.ShowDotZBar = ShowDotZBar;

            _handler.NotifyStatus = GetStatusNotifier();

            _externalEvent.Raise();
        }

        [RelayCommand]
        private void ResetColor()
        {
            _handler.Action = "ResetColor";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void HideTaggedReo()
        {
            _handler.Action = "HideTaggedReo";
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void CheckDot()
        {
            _handler.Action = "CheckDot";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();

            _handler.CenterDotAdjustable = CenterDotAdjustable;
            _handler.HideDotAdjustable = HideDotAdjustable;
            _handler.ShowDotAdjustable = ShowDotAdjustable;
            _handler.CenterDotZBar = CenterDotZBar;
            _handler.HideDotZBar = HideDotZBar;
            _handler.ShowDotZBar = ShowDotZBar;
            _handler.ColorUntaggedItems = ColorUntaggedItems;
            _handler.UntaggedColorR = UntaggedColorR;
            _handler.UntaggedColorG = UntaggedColorG;
            _handler.UntaggedColorB = UntaggedColorB;
            _handler.IgnoreTagCheck = IgnoreTagCheck;

            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        // ===== Layer X/Y Commands =====

        [RelayCommand]
        private void ShowLayerX()
        {
            _handler.Action = "ShowLayer";
            _handler.LayerDirection = "X";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void HideLayerX()
        {
            _handler.Action = "HideLayer";
            _handler.LayerDirection = "X";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void ShowLayerY()
        {
            _handler.Action = "ShowLayer";
            _handler.LayerDirection = "Y";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void HideLayerY()
        {
            _handler.Action = "HideLayer";
            _handler.LayerDirection = "Y";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void ShowAllLayers()
        {
            _handler.Action = "ShowAll";
            _handler.TargetFamilyNames = TargetFamilies.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            _handler.NotifyStatus = GetStatusNotifier();
            _externalEvent.Raise();
        }
    }
}

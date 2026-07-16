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

        public void Dispose()
        {
            if (_externalEvent != null)
            {
                _externalEvent.Dispose();
                _externalEvent = null;
            }
        }

        // Target Families (which families to tag/layer)
        [ObservableProperty]
        private ObservableCollection<TargetFamilyItem> _targetFamilies;

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
        private bool _onlyAlreadyTagged = true;

        partial void OnOnlyAlreadyTaggedChanged(bool value)
        {
            if (value && OnlyUntagged)
            {
                OnlyUntagged = false;
            }
        }
        
        [ObservableProperty]
        private bool _onlyUntagged = false;

        partial void OnOnlyUntaggedChanged(bool value)
        {
            if (value && OnlyAlreadyTagged)
            {
                OnlyAlreadyTagged = false;
            }
        }

        // Color Override
        [ObservableProperty]
        private bool _applyColorOverride = true;

        [ObservableProperty]
        private bool _overrideRebarColor = true;

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

        private Document _doc;
        private View _activeView;
        private Dictionary<string, int> _familyCounts = new Dictionary<string, int>();

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

        public void UpdateDocumentAndView(Document doc, View activeView)
        {
            _doc = doc;
            _activeView = activeView;
            // Removed LoadData() to disable auto-reload on view switch
        }

        private void LoadData()
        {
            // Execute the load on Revit API thread via ExternalEvent
            _handler.Action = "ReloadData";
            _handler.OnReloadData = (doc, view) =>
            {
                _doc = doc;
                _activeView = view;
                
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

                var newTagTypes = new ObservableCollection<TagTypeItem>(tagFamilies.Select(fs => new TagTypeItem(fs)));
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ElementId currentSelectedTagId = SelectedTagType?.Id;
                    TagTypes = newTagTypes;
                    if (currentSelectedTagId != null)
                        SelectedTagType = TagTypes.FirstOrDefault(t => t.Id == currentSelectedTagId) ?? TagTypes.FirstOrDefault();
                    else
                        SelectedTagType = TagTypes.FirstOrDefault();
                });

                try
                {
                    var detailInstances = new FilteredElementCollector(_doc, _activeView.Id)
                        .OfCategory(BuiltInCategory.OST_DetailComponents)
                        .WhereElementIsNotElementType()
                        .Select(e =>
                        {
                            var typeId = e.GetTypeId();
                            var type = _doc.GetElement(typeId) as FamilySymbol;
                            return type?.FamilyName;
                        })
                        .Where(name => !string.IsNullOrEmpty(name) && name.ToUpper().StartsWith("REO"))
                        .ToList();

                    var grouped = detailInstances.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
                    _familyCounts = grouped;
                    
                    var distinctNames = grouped.Keys.OrderBy(n => n).ToList();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var previousSelections = TargetFamilies?.ToDictionary(f => f.Name, f => f.IsSelected) ?? new Dictionary<string, bool>();
                        TargetFamilies = new ObservableCollection<TargetFamilyItem>(
                            distinctNames.Select(f => new TargetFamilyItem(f, previousSelections.ContainsKey(f) ? previousSelections[f] : true))
                        );
                        UpdateItemCount();
                    });
                }
                catch
                {
                    _familyCounts = new Dictionary<string, int>();
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        TargetFamilies = new ObservableCollection<TargetFamilyItem>();
                        UpdateItemCount();
                    });
                }
            };
            _externalEvent.Raise();
        }

        [RelayCommand]
        private void ReloadView()
        {
            LoadData();
            StatusMessage = "View reloaded. Data refreshed.";
        }

        [RelayCommand]
        private void UpdateItemCount()
        {
            var selectedFamilies = TargetFamilies?.Where(f => f.IsSelected).Select(f => f.Name).ToList() ?? new List<string>();

            if (!selectedFamilies.Any())
            {
                ItemCountInfo = "No target families selected";
                return;
            }

            int count = 0;
            foreach (var fam in selectedFamilies)
            {
                if (_familyCounts.TryGetValue(fam, out int c))
                {
                    count += c;
                }
            }

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

            if (TargetFamilies == null || !TargetFamilies.Any(f => f.IsSelected))
            {
                StatusMessage = "No target families found in current view.";
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
            _handler.OverrideRebarColor = OverrideRebarColor;
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

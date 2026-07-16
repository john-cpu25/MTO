using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RincoMTO.Tools.MtoCheck.ViewModels
{
    public partial class MtoCheckViewModel : ObservableObject
    {
        private UIDocument _uidoc;
        private Document _doc;
        private MtoCheckHandler _handler;
        private ExternalEvent _externalEvent;
        private System.Windows.Threading.Dispatcher _dispatcher;

        [ObservableProperty]
        private ObservableCollection<ViewItem> _availableViews = new ObservableCollection<ViewItem>();

        [ObservableProperty]
        private ViewItem _selectedSourceView;

        [ObservableProperty]
        private string _targetViewName;

        [ObservableProperty]
        private ObservableCollection<CheckResultItem> _results = new ObservableCollection<CheckResultItem>();

        [ObservableProperty]
        private string _statusMessage = "Sẵn sàng kiểm tra.";

        public MtoCheckViewModel(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            _handler = new MtoCheckHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            _handler.NotifyStatus = msg =>
            {
                _dispatcher.Invoke(() => StatusMessage = msg);
            };

            _handler.OnCheckCompleted = () =>
            {
                _dispatcher.Invoke(() =>
                {
                    Results = new ObservableCollection<CheckResultItem>(_handler.Discrepancies);
                });
            };

            TargetViewName = _uidoc.ActiveView.Name;
            LoadViews();
        }

        private void LoadViews()
        {
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser)
                .OrderBy(v => v.ViewType == ViewType.DrawingSheet ? 0 : 1)
                .ThenBy(v => v.Name)
                .ToList();

            AvailableViews.Clear();
            foreach (var v in views)
            {
                string prefix = v.ViewType == ViewType.DrawingSheet ? "[Sheet]" : "[View]";
                AvailableViews.Add(new ViewItem { Id = v.Id, Name = $"{prefix} {v.Name}", View = v });
            }
        }

        [RelayCommand]
        private void RunCheck()
        {
            if (SelectedSourceView == null)
            {
                StatusMessage = "Vui lòng chọn Source View!";
                return;
            }

            StatusMessage = "Đang kiểm tra...";
            Results.Clear();

            _handler.SourceView = SelectedSourceView.View;
            _handler.TargetView = _uidoc.ActiveView;
            _handler.Action = "Check";
            _externalEvent.Raise();
        }
    }

    public class ViewItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public View View { get; set; }
    }
}

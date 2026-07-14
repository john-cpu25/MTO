using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RincoMTO.Tools.MtoGroupBar.Models
{
    public class MtoGroupItem : INotifyPropertyChanged
    {
        private string _groupName;
        public string GroupName
        {
            get => _groupName;
            set { _groupName = value; OnPropertyChanged(); }
        }

        private int _count;
        public int Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(); }
        }

        private string _remarks;
        public string Remarks
        {
            get => _remarks;
            set { _remarks = value; OnPropertyChanged(); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _displayColor = new SolidColorBrush(Colors.Transparent);
        public SolidColorBrush DisplayColor
        {
            get => _displayColor;
            set { _displayColor = value; OnPropertyChanged(); }
        }

        public List<ElementId> ElementIds { get; set; } = new List<ElementId>();

        // Store Revit Color
        public Autodesk.Revit.DB.Color RevitColor { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

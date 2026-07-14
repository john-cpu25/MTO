using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace RincoMTO.Tools.RenameSheetNumber.UI
{
    public class SheetRenameItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public ElementId SheetId { get; set; }
        
        public string SheetName { get; set; }
        
        public string OldNumber { get; set; }
        
        private string _newNumber;
        public string NewNumber
        {
            get => _newNumber;
            set
            {
                _newNumber = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

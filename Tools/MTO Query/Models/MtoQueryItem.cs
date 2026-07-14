using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RincoMTO.Tools.MTOQuery.Models
{
    public class MtoQueryItem : INotifyPropertyChanged
    {
        private string _familyName;
        public string FamilyName
        {
            get => _familyName;
            set { _familyName = value; OnPropertyChanged(); }
        }

        private string _typeName;
        public string TypeName
        {
            get => _typeName;
            set { _typeName = value; OnPropertyChanged(); }
        }

        private string _tagText;
        public string TagText
        {
            get => _tagText;
            set { _tagText = value; OnPropertyChanged(); }
        }

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set { _quantity = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

using System.Windows;

namespace RincoMTO.Core
{
    /// <summary>
    /// BindingProxy cho phép binding DataContext vào các element nằm ngoài Visual Tree
    /// (ví dụ: DataGridColumn.Visibility).
    /// Kế thừa Freezable vì Freezable tự động inherit DataContext từ parent.
    /// </summary>
    public class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data),
                typeof(object),
                typeof(BindingProxy),
                new UIPropertyMetadata(null));

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}

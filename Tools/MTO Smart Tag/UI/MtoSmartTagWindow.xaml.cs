using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.UI;
using RincoMTO.Tools.MtoSmartTag.ViewModels;

namespace RincoMTO.Tools.MtoSmartTag.UI
{
    /// <summary>
    /// Inverts a boolean value (true → false, false → true).
    /// Used to disable direction controls when UseDirectXY is checked.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }
    }
    public partial class MtoSmartTagWindow : Window
    {
        private MtoSmartTagViewModel _viewModel;
        private UIApplication _uiApp;

        public MtoSmartTagWindow(UIDocument uidoc)
        {
            // Ensure WPF Application exists (required for XAML resource loading in Revit)
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application();
            }
            
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            InitializeComponent();

            _uiApp = new UIApplication(uidoc.Document.Application);
            _uiApp.ViewActivated += UiApp_ViewActivated;
            
            this.Closed += (s, e) => 
            {
                try
                {
                    _uiApp.ViewActivated -= UiApp_ViewActivated;
                    _viewModel?.Dispose();
                }
                catch { }
            };

            var handler = new MtoSmartTagHandler();
            _viewModel = new MtoSmartTagViewModel(uidoc.Document, uidoc.Document.ActiveView, handler);

            DataContext = _viewModel;
        }

        private void UiApp_ViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            if (e.CurrentActiveView != null && _viewModel != null)
            {
                _viewModel.UpdateDocumentAndView(e.Document, e.CurrentActiveView);
            }
        }

        /// <summary>
        /// Handles RadioButton checked events for the direction picker grid.
        /// Updates the ViewModel's SelectedDirection based on the DataContext of the checked RadioButton.
        /// </summary>
        private void Direction_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.DataContext is DirectionItem dirItem)
            {
                if (_viewModel != null)
                {
                    _viewModel.SelectedDirection = dirItem;
                }
            }
        }

        /// <summary>
        /// Opens the standard Windows Color Dialog for picking override color.
        /// </summary>
        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(_viewModel.ColorR, _viewModel.ColorG, _viewModel.ColorB)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _viewModel.ColorR = dialog.Color.R;
                _viewModel.ColorG = dialog.Color.G;
                _viewModel.ColorB = dialog.Color.B;
            }
        }

        private void PickUntaggedColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(_viewModel.UntaggedColorR, _viewModel.UntaggedColorG, _viewModel.UntaggedColorB)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _viewModel.UntaggedColorR = dialog.Color.R;
                _viewModel.UntaggedColorG = dialog.Color.G;
                _viewModel.UntaggedColorB = dialog.Color.B;
            }
        }
    }
}

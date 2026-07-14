using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RincoMTO.Tools.MtoGroupBar.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Data;

namespace RincoMTO.Tools.MtoGroupBar.UI
{
    public partial class MtoGroupBarWindow : Window
    {
        private ObservableCollection<MtoGroupItem> _groups;
        private ExternalEvent _exEvent;
        private MtoGroupBarEventHandler _handler;
        private ICollectionView _groupsView;

        public MtoGroupBarWindow(ObservableCollection<MtoGroupItem> groups, ExternalEvent exEvent, MtoGroupBarEventHandler handler)
        {
            // Ensure WPF Application exists (required for XAML resource loading in Revit)

            if (System.Windows.Application.Current == null)

            {

                new System.Windows.Application();

            }


            InitializeComponent();
            _groups = groups;
            _exEvent = exEvent;
            _handler = handler;

            _groupsView = CollectionViewSource.GetDefaultView(_groups);
            _groupsView.Filter = GroupFilter;
            DataGridGroups.ItemsSource = _groupsView;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void DataGridGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataGridGroups.SelectedItem is MtoGroupItem selectedGroup)
            {
                _handler.Action = app =>
                {
                    app.ActiveUIDocument.Selection.SetElementIds(selectedGroup.ElementIds);
                };
                _exEvent.Raise();
            }
        }

        private void BtnRandomColors_Click(object sender, RoutedEventArgs e)
        {
            // Generate random colors and update UI
            Random rnd = new Random();
            foreach (var group in _groups)
            {
                byte r = (byte)rnd.Next(0, 200); // avoiding pure white/very light colors
                byte g = (byte)rnd.Next(0, 200);
                byte b = (byte)rnd.Next(0, 200);

                group.DisplayColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
                group.RevitColor = new Autodesk.Revit.DB.Color(r, g, b);
            }

            // Apply to Revit
            _handler.Action = app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                View activeView = app.ActiveUIDocument.ActiveView;

                using (Transaction tx = new Transaction(doc, "Override Group Colors"))
                {
                    tx.Start();
                    
                    foreach (var group in _groups)
                    {
                        OverrideGraphicSettings ogs = activeView.GetElementOverrides(group.ElementIds[0]);
                        if (ogs == null) ogs = new OverrideGraphicSettings();

                        ogs.SetProjectionLineColor(group.RevitColor);
                        // Optionally set line weight or other graphics

                        foreach (var id in group.ElementIds)
                        {
                            activeView.SetElementOverrides(id, ogs);
                        }
                    }

                    tx.Commit();
                }
            };
            _exEvent.Raise();
        }

        private bool GroupFilter(object item)
        {
            if (string.IsNullOrEmpty(TxtSearch.Text))
                return true;

            var group = item as MtoGroupItem;
            if (group == null)
                return false;

            bool matchName = group.GroupName != null && group.GroupName.IndexOf(TxtSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchRemarks = group.Remarks != null && group.Remarks.IndexOf(TxtSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0;

            return matchName || matchRemarks;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _groupsView?.Refresh();
        }
    }
}

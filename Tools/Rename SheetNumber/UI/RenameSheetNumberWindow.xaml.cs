using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.UI;

namespace RincoMTO.Tools.RenameSheetNumber.UI
{
    public partial class RenameSheetNumberWindow : Window
    {
        private List<SheetRenameItem> _sheets;
        private RenameSheetNumberEventHandler _handler;
        private ExternalEvent _exEvent;

        public RenameSheetNumberWindow(List<SheetRenameItem> sheets, RenameSheetNumberEventHandler handler, ExternalEvent exEvent)
        {
            InitializeComponent();
            _sheets = sheets;
            _handler = handler;
            _exEvent = exEvent;
            
            // Sort by OldNumber so Auto Numbering makes sense
            _sheets = _sheets.OrderBy(s => s.OldNumber).ToList();
            dgSheets.ItemsSource = _sheets;
        }

        private void BtnCheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _sheets)
            {
                item.IsSelected = true;
            }
        }

        private void BtnUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _sheets)
            {
                item.IsSelected = false;
            }
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            GeneratePreview();
        }

        private void GeneratePreview()
        {
            string find = txtFind.Text;
            string replace = txtReplace.Text;
            string prefix = txtPrefix.Text;
            string suffix = txtSuffix.Text;
            bool useAutoNumber = chkAutoNumber.IsChecked == true;
            string startNumStr = txtStartNumber.Text;

            int currentNum = 1;
            int padding = 1;
            
            if (useAutoNumber)
            {
                if (int.TryParse(startNumStr, out int parsed))
                {
                    currentNum = parsed;
                }
                padding = startNumStr.Length; // e.g. "01" -> 2
            }

            foreach (var item in _sheets)
            {
                if (!item.IsSelected)
                {
                    item.NewNumber = item.OldNumber;
                    continue;
                }

                string baseNum = item.OldNumber;

                if (useAutoNumber)
                {
                    baseNum = currentNum.ToString().PadLeft(padding, '0');
                    currentNum++;
                }
                else
                {
                    if (!string.IsNullOrEmpty(find))
                    {
                        baseNum = baseNum.Replace(find, replace);
                    }
                }

                item.NewNumber = prefix + baseNum + suffix;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            GeneratePreview(); // Ensure latest rules are applied

            var toRename = _sheets.Where(s => s.IsSelected).ToList();
            if (toRename.Count == 0)
            {
                MessageBox.Show("Please select at least one sheet to rename.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check for duplicate NewNumbers within the selected ones
            var duplicates = toRename.GroupBy(x => x.NewNumber).Where(g => g.Count() > 1).ToList();
            if (duplicates.Count > 0)
            {
                MessageBox.Show("There are duplicate target sheet numbers. Please check your rules.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _handler.ItemsToRename = toRename;
            _exEvent.Raise();
            
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

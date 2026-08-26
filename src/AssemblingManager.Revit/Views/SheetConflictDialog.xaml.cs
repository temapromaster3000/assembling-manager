using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class SheetConflictDialog : Window
    {
        public List<SheetConflictItem> ConflictItems { get; }

        public SheetConflictDialog(List<SheetConflictItem> conflictItems)
        {
            ConflictItems = conflictItems;

            InitializeComponent();
            ConflictListView.ItemsSource = ConflictItems;

            UpdateSelectAllButtonText();
            UpdateCounter();
        }

        private void ButtonSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool newState = true;

            foreach (SheetConflictItem item in ConflictItems)
            {
                if (item.Replace)
                {
                    newState = false;
                    break;
                }
            }

            foreach (SheetConflictItem item in ConflictItems)
            {
                item.Replace = newState;
            }

            ConflictListView.Items.Refresh();

            UpdateSelectAllButtonText();
            UpdateCounter();
        }

        private void UpdateSelectAllButtonText()
        {
            bool allSelected = true;

            foreach (SheetConflictItem item in ConflictItems)
            {
                if (!item.Replace)
                {
                    allSelected = false;
                    break;
                }
            }

            ButtonSelectAll.Content = allSelected ? "Снять всё" : "Выбрать всё";
        }

        private void UpdateCounter()
        {
            int selected = 0;

            foreach (SheetConflictItem item in ConflictItems)
            {
                if (item.Replace)
                {
                    selected++;
                }
            }

            TextBlockSelectedCount.Text = $"Заменяют: {selected} из {ConflictItems.Count}";
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is SheetConflictItem clickedItem)
            {
                bool newState = checkBox.IsChecked == true;

                if (ConflictListView.SelectedItems.Contains(clickedItem) && ConflictListView.SelectedItems.Count > 1)
                {
                    foreach (SheetConflictItem item in ConflictListView.SelectedItems)
                    {
                        item.Replace = newState;
                    }
                }
            }

            UpdateSelectAllButtonText();
            UpdateCounter();
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

using System.Collections.Generic;
using System.Windows;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class ConflictDialog : Window
    {
        public List<ViewConflictItem> ConflictItems { get; private set; }

        public ConflictDialog(List<ViewConflictItem> conflictItems)
        {
            ConflictItems = conflictItems;
            InitializeComponent();
            ConflictListView.ItemsSource = ConflictItems;
        }

        private void ButtonSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool newState = true;

            foreach (ViewConflictItem item in ConflictItems)
            {
                if (item.Replace)
                {
                    newState = false;
                    break;
                }
            }

            foreach (ViewConflictItem item in ConflictItems)
            {
                item.Replace = newState;
            }

            ConflictListView.Items.Refresh();
            UpdateSelectAllButtonText();
        }

        private void UpdateSelectAllButtonText()
        {
            bool allSelected = true;

            foreach (ViewConflictItem item in ConflictItems)
            {
                if (!item.Replace)
                {
                    allSelected = false;
                    break;
                }
            }

            ButtonSelectAll.Content = allSelected ? "Снять всё" : "Выбрать всё";
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

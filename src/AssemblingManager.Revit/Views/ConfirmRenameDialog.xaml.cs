using System.Collections.Generic;
using System.Windows;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class ConfirmRenameDialog : Window
    {
        public ConfirmRenameDialog(int renameCount, IReadOnlyList<RenameReportItem> items)
        {
            InitializeComponent();
            BuildSummary(renameCount, items);
        }

        private void BuildSummary(int renameCount, IReadOnlyList<RenameReportItem> items)
        {
            SummaryText.Text = $"Будет переименовано сборок: {renameCount}";

            if (items == null || items.Count == 0)
            {
                return;
            }

            string text = string.Empty;
            foreach (RenameReportItem item in items)
            {
                text += $"{item.OldName}  →  {item.NewName}\n";
            }

            RenameListText.Text = text.TrimEnd();
        }

        private void ButtonYes_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

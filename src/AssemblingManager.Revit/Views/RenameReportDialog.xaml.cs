using System.Collections.Generic;
using System.Windows;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class RenameReportDialog : Window
    {
        public RenameReportDialog(int renamedCount, IReadOnlyList<RenameReportItem> items)
        {
            InitializeComponent();
            BuildSummary(renamedCount, items);
        }

        private void BuildSummary(int renamedCount, IReadOnlyList<RenameReportItem> items)
        {
            SummaryText.Text = $"Переименовано сборок: {renamedCount}";

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

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

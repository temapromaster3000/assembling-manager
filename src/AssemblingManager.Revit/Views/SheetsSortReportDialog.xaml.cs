using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace AssemblingManager.Revit.Views
{
    public partial class SheetsSortReportDialog : Window
    {
        public SheetsSortReportDialog(
            int renamedCount,
            int deletedCount,
            int renumberedCount,
            IReadOnlyList<string> renameLog,
            IReadOnlyList<string> warnings)
        {
            InitializeComponent();
            BuildSummary(renamedCount, deletedCount, renumberedCount, renameLog, warnings);
        }

        private void BuildSummary(
            int renamedCount,
            int deletedCount,
            int renumberedCount,
            IReadOnlyList<string> renameLog,
            IReadOnlyList<string> warnings)
        {
            List<string> statistics = new List<string>();

            if (renamedCount > 0)
            {
                statistics.Add($"Переименовано листов: {renamedCount}");
            }

            if (deletedCount > 0)
            {
                statistics.Add($"Удалено пустых листов: {deletedCount}");
            }

            if (renumberedCount > 0)
            {
                statistics.Add($"Перенумеровано листов: {renumberedCount}");
            }

            if (statistics.Count == 0)
            {
                SummaryText.Text = "Изменений не потребовалось.";
                return;
            }

            SummaryText.Text = string.Join("\n", statistics);

            if (renameLog != null && renameLog.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("Переименованные листы:");
                foreach (string renameEntry in renameLog)
                {
                    builder.AppendLine($"  {renameEntry}");
                }

                RenameListText.Text = builder.ToString().TrimEnd();
            }

            if (warnings != null && warnings.Count > 0)
            {
                WarningText.Text = "Предупреждения:\n" + string.Join("\n", warnings);
            }
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

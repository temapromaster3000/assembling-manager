using System;
using System.Windows;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class ReportDialog : Window
    {
        public ReportDialog(ViewCreationResult result)
        {
            InitializeComponent();
            ReportTextBlock.Text = BuildReportText(result);
        }

        private string BuildReportText(ViewCreationResult result)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("Формирование видов завершено.");
            builder.AppendLine();

            if (result.CreatedCount > 0)
            {
                builder.AppendLine($"Создано видов: {result.CreatedCount}");
            }

            if (result.ReplacedCount > 0)
            {
                builder.AppendLine($"Заменено видов: {result.ReplacedCount}");
            }

            if (result.SkippedCount > 0)
            {
                builder.AppendLine($"Пропущено видов: {result.SkippedCount}");
            }

            builder.AppendLine();
            builder.AppendLine($"Время работы: {result.Elapsed.TotalSeconds:F2} с");

            return builder.ToString();
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

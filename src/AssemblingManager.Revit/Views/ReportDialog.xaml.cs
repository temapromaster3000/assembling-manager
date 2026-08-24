using System;
using System.Windows;
using System.Windows.Controls;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class ReportDialog : Window
    {
        public ReportDialog(ViewCreationResult result)
        {
            InitializeComponent();
            BuildStatistics(result);
        }

        private void BuildStatistics(ViewCreationResult result)
        {
            if (result.CreatedCount > 0)
            {
                AddStatistic("Создано видов:", result.CreatedCount.ToString());
            }

            if (result.ReplacedCount > 0)
            {
                AddStatistic("Заменено видов:", result.ReplacedCount.ToString());
            }

            if (result.SkippedCount > 0)
            {
                AddStatistic("Пропущено видов:", result.SkippedCount.ToString());
            }

            AddStatistic("Время работы:", $"{result.Elapsed.TotalSeconds:F2} с");
        }

        private void AddStatistic(string label, string value)
        {
            int rowIndex = StatisticsGrid.RowDefinitions.Count;
            StatisticsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 12, 4)
            };

            TextBlock valueBlock = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 0, 4)
            };

            Grid.SetRow(labelBlock, rowIndex);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetRow(valueBlock, rowIndex);
            Grid.SetColumn(valueBlock, 1);

            StatisticsGrid.Children.Add(labelBlock);
            StatisticsGrid.Children.Add(valueBlock);
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
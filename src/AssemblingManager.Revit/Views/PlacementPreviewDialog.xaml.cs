using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class PlacementPreviewDialog : Window
    {
        public PlacementPreviewDialog(SheetService.SheetPlacementPlan plan)
        {
            InitializeComponent();

            string signalText = plan.Sheets.Count(s => s.IsSignal) > 0
                ? $", сигнальных: {plan.Sheets.Count(s => s.IsSignal)}"
                : string.Empty;

            SummaryText.Text = $"Будет создано листов: {plan.Sheets.Count}{signalText}.";
            if (plan.FullyPlacedGroupsCount > 0)
            {
                SummaryText.Text += $"\nПолностью размещено групп: {plan.FullyPlacedGroupsCount}.";
            }

            SheetsList.ItemsSource = plan.Sheets;
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

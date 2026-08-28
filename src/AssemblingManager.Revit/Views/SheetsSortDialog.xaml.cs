using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Common;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class SheetsSortDialog : Window
    {
        private readonly Document _document;
        private readonly SheetService _sheetService;
        private HashSet<ElementId> _sheetsWithContent;

        public SheetGroupNode SelectedGroupNode { get; private set; }
        public int StartNumber { get; private set; }

        public SheetsSortDialog(Document document, List<SheetGroupNode> sheetRoots, SheetService sheetService)
        {
            _document = document;
            _sheetService = sheetService;

            InitializeComponent();
            GroupTreeView.ItemsSource = sheetRoots;

            HintTextBlock.Text = "Выберите группу листов и укажите номер первого листа.";
            UpdateSummary();
        }

        private void GroupTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SheetGroupNode node = e.NewValue as SheetGroupNode;
            SelectedGroupNode = node == null ? null : (node.IsSheet ? node.Parent : node);

            UpdateSummary();
            UpdateOkState();
        }

        private void StartNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOkState();
        }

        private void UpdateSummary()
        {
            if (SelectedGroupNode == null)
            {
                SummaryTextBlock.Text = "Группа не выбрана.";
                return;
            }

            List<ViewSheet> sheets = SelectedGroupNode.GetAllSheets();
            int emptyCount = 0;

            foreach (ViewSheet sheet in sheets)
            {
                if (_sheetService.IsSheetEmpty(sheet, GetSheetsWithContent()))
                {
                    emptyCount++;
                }
            }

            SummaryTextBlock.Text = $"Группа «{SelectedGroupNode.Name}»: листов — {sheets.Count}, пустых — {emptyCount}.";

            NaturalStringComparer comparer = new NaturalStringComparer();
            ViewSheet firstSheet = sheets
                .OrderBy(s => s.SheetNumber ?? string.Empty, comparer)
                .FirstOrDefault();

            if (firstSheet != null && firstSheet.SheetNumber != null)
            {
                StartNumberTextBox.Text = firstSheet.SheetNumber.Trim();
            }
        }

        private HashSet<ElementId> GetSheetsWithContent()
        {
            if (_sheetsWithContent == null)
            {
                _sheetsWithContent = _sheetService.GetSheetIdsWithMeaningfulContent(
                    _document,
                    _sheetService.GetAssemblyNames(_document));
            }

            return _sheetsWithContent;
        }

        private void UpdateOkState()
        {
            if (ButtonOK == null || HintTextBlock == null)
            {
                return;
            }

            bool validNumber = int.TryParse(StartNumberTextBox.Text, out int number) && number >= 0;
            StartNumber = validNumber ? number : 0;

            ButtonOK.IsEnabled = SelectedGroupNode != null && validNumber;

            if (ButtonOK.IsEnabled)
            {
                HintTextBlock.Text = string.Empty;
            }
            else
            {
                HintTextBlock.Text = "Выберите группу листов и укажите номер первого листа.";
            }
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

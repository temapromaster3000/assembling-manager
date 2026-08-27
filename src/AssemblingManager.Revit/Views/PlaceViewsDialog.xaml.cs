using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class PlaceViewsDialog : Window
    {
        private readonly Document _document;
        private readonly SheetService _sheetService;
        private List<ObjectViewGroup> _currentObjects;

        public ViewGroupNode SelectedGroupNode { get; private set; }
        public ViewSheet SelectedMasterSheet { get; private set; }
        public SheetGroupNode SelectedSheetGroupNode { get; private set; }

        public PlaceViewsDialog(Document document, List<ViewGroupNode> groupRoots, List<SheetGroupNode> sheetRoots, SheetService sheetService)
        {
            _document = document;
            _sheetService = sheetService;

            InitializeComponent();

            GroupTreeView.ItemsSource = groupRoots;
            SheetsTreeView.ItemsSource = sheetRoots;

            HintTextBlock.Text = "Выберите группу видов и лист-образец.";
            UpdateSummary();
        }

        private void GroupTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ViewGroupNode node = e.NewValue as ViewGroupNode;
            SelectedGroupNode = node != null && node.IsLeaf && node.ViewIds.Count > 0 ? node : null;

            _currentObjects = SelectedGroupNode != null
                ? _sheetService.GroupViewsByObject(_document, SelectedGroupNode.ViewIds)
                : null;

            UpdateSummary();
            UpdateOkState();
        }

        private void SheetsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SheetGroupNode node = e.NewValue as SheetGroupNode;
            SelectedMasterSheet = node != null && node.IsSheet ? node.Sheet : null;
            SelectedSheetGroupNode = node != null && node.IsSheet ? node.Parent : null;

            UpdateOkState();
        }

        private void UpdateSummary()
        {
            if (SelectedGroupNode == null)
            {
                SummaryTextBlock.Text = "Группа не выбрана.";
                return;
            }

            int objects = _currentObjects != null ? _currentObjects.Count : 0;
            SummaryTextBlock.Text = $"Группа «{SelectedGroupNode.Name}»: объектов — {objects}, будет создано листов — {objects}.";
        }

        private void UpdateOkState()
        {
            ButtonOK.IsEnabled = SelectedGroupNode != null && SelectedMasterSheet != null;

            if (ButtonOK.IsEnabled)
            {
                HintTextBlock.Text = string.Empty;
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

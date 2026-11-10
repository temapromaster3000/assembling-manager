using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class ElevationCheckDialog : Window
    {
        private static readonly SolidColorBrush ErrorBrush = CreateFrozenBrush(0xB0, 0x0C, 0x00);
        private static readonly SolidColorBrush SuccessBrush = CreateFrozenBrush(0x10, 0x7C, 0x10);
        private static readonly SolidColorBrush NormalBrush = CreateFrozenBrush(0x5A, 0x6B, 0x7B);

        private readonly UIApplication _uiApplication;
        private readonly Document _document;
        private readonly ElevationCheckEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly IntPtr _revitWindowHandle;

        private ElevationSheetTreeItem _selectedGroup;
        private bool _isChecking;
        private bool _isClosed;

        public ElevationCheckDialog(
            UIApplication uiApplication,
            Document document,
            List<ElevationSheetTreeItem> tree,
            ElevationCheckEventHandler handler,
            ExternalEvent externalEvent)
        {
            _uiApplication = uiApplication ?? throw new ArgumentNullException(nameof(uiApplication));
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));
            _revitWindowHandle = uiApplication.MainWindowHandle;

            InitializeComponent();

            Title = string.IsNullOrWhiteSpace(_document.Title)
                ? "Проверка отметок"
                : $"Проверка отметок — {_document.Title}";

            GroupTreeView.ItemsSource = tree;

            _handler.CheckCompleted = OnCheckCompleted;
            _handler.CheckFailed = OnCheckFailed;
            _handler.Navigated = OnNavigated;
            _handler.NavigationFailed = OnNavigationFailed;

            _uiApplication.Application.DocumentClosing += OnDocumentClosing;

            UpdateSelectionState();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                if (_revitWindowHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(this).Owner = _revitWindowHandle;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"ElevationCheckDialog: could not set Revit window owner: {ex.Message}");
            }
        }

        private void GroupTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ElevationSheetTreeItem item = e.NewValue as ElevationSheetTreeItem;

            _selectedGroup = item != null && item.IsSheet && item.Parent != null
                ? item.Parent
                : item;

            ClearResults();
            UpdateSelectionState();
        }

        private void ButtonCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_isChecking || _selectedGroup == null)
            {
                return;
            }

            if (_selectedGroup.SheetIds == null || _selectedGroup.SheetIds.Count == 0)
            {
                ShowStatus("В выбранном комплекте нет листов.", true);
                return;
            }

            _isChecking = true;
            ButtonCheck.Content = "Проверка…";
            ResultsGrid.ItemsSource = null;
            SummaryTextBlock.Text = string.Empty;
            EmptyStateTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            UpdateSelectionState();
            ShowStatus("Проверяю виды на листах…", false);

            _handler.RequestCheck(_selectedGroup.SheetIds);
            ExternalEventRequest request = _externalEvent.Raise();

            if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
            {
                _handler.ClearRequests();
                FinishChecking();
                ShowStatus($"Не удалось запустить проверку ({DescribeRequest(request)}). Попробуйте ещё раз.", true);
            }
        }

        private void OnCheckCompleted(ElevationCheckResult result)
        {
            if (_isClosed)
            {
                return;
            }

            FinishChecking();

            ElevationCheckResult data = result ?? new ElevationCheckResult();
            data.ProblemSheets = data.ProblemSheets ?? new List<SheetElevationCheckItem>();

            ResultsGrid.ItemsSource = data.ProblemSheets;

            bool hasProblems = data.ProblemSheets.Count > 0;
            if (hasProblems)
            {
                EmptyStateTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                EmptyStateTextBlock.Text = data.CheckedSheetCount == 0
                    ? "В выбранном комплекте нет листов."
                    : "Все листы комплекта содержат высотные отметки.";
                EmptyStateTextBlock.Visibility = System.Windows.Visibility.Visible;
            }

            SummaryTextBlock.Text = BuildSummary(data);
            SummaryTextBlock.Foreground = hasProblems ? ErrorBrush : SuccessBrush;

            ShowStatus($"Проверка завершена за {data.Elapsed.TotalSeconds:F1} с.", false);
        }

        private void OnCheckFailed(string error)
        {
            if (_isClosed)
            {
                return;
            }

            FinishChecking();
            SummaryTextBlock.Text = "Проверка не выполнена.";
            SummaryTextBlock.Foreground = ErrorBrush;
            EmptyStateTextBlock.Text = "Не удалось выполнить проверку.";
            EmptyStateTextBlock.Visibility = System.Windows.Visibility.Visible;
            ShowStatus($"Ошибка проверки: {error}", true);
        }

        private void OnNavigated(string message)
        {
            if (_isClosed)
            {
                return;
            }

            ShowStatus(message, false);
        }

        private void OnNavigationFailed(string message)
        {
            if (_isClosed)
            {
                return;
            }

            ShowStatus(message, true);
        }

        private void MenuItemGoToSheet_Click(object sender, RoutedEventArgs e)
        {
            GoToSelectedSheet();
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (source == null || ItemsControl.ContainerFromElement(ResultsGrid, source) as DataGridRow == null)
            {
                return;
            }

            GoToSelectedSheet();
        }

        private void ResultsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            DataGridRow row = source == null
                ? null
                : ItemsControl.ContainerFromElement(ResultsGrid, source) as DataGridRow;

            if (row != null)
            {
                row.IsSelected = true;
                ResultsGrid.SelectedItem = row.Item;
                row.Focus();
            }
        }

        private void GoToSelectedSheet()
        {
            SheetElevationCheckItem item = ResultsGrid.SelectedItem as SheetElevationCheckItem;
            if (item == null)
            {
                return;
            }

            if (_isChecking)
            {
                ShowStatus("Дождитесь окончания проверки и повторите.", true);
                return;
            }

            ShowStatus($"Открываю лист № {item.SheetNumber}…", false);

            _handler.RequestNavigate(item.SheetId);
            ExternalEventRequest request = _externalEvent.Raise();

            if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
            {
                _handler.ClearRequests();
                ShowStatus($"Не удалось открыть лист ({DescribeRequest(request)}).", true);
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            if (e.Document == null || _document == null)
            {
                return;
            }

            if (ElevationCheckEventHandler.SameDocument(e.Document, _document) && !_isClosed)
            {
                Logger.Info("ElevationCheckDialog: source document is closing, closing window.");
                Close();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _isClosed = true;

            try
            {
                _uiApplication.Application.DocumentClosing -= OnDocumentClosing;
            }
            catch (Exception ex)
            {
                Logger.Warn($"ElevationCheckDialog: could not unsubscribe DocumentClosing: {ex.Message}");
            }

            _handler.CheckCompleted = null;
            _handler.CheckFailed = null;
            _handler.Navigated = null;
            _handler.NavigationFailed = null;

            try
            {
                _externalEvent.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn($"ElevationCheckDialog: could not dispose external event: {ex.Message}");
            }

            Logger.Info("ElevationCheckDialog closed.");
        }

        private void UpdateSelectionState()
        {
            GroupTreeView.IsEnabled = !_isChecking;
            ResultsGrid.IsEnabled = !_isChecking;

            if (_selectedGroup == null)
            {
                SelectionTextBlock.Text = "Выберите комплект листов.";
                ButtonCheck.IsEnabled = false;
                return;
            }

            int sheetCount = _selectedGroup.SheetIds?.Count ?? 0;

            SelectionTextBlock.Text = sheetCount == 0
                ? $"В комплекте «{_selectedGroup.Name}» нет листов."
                : $"Выбран комплект «{_selectedGroup.Name}»: листов — {sheetCount}.";

            ButtonCheck.IsEnabled = !_isChecking && sheetCount > 0;
        }

        private void FinishChecking()
        {
            _isChecking = false;
            ButtonCheck.Content = "Проверить";
            UpdateSelectionState();
        }

        private void ClearResults()
        {
            ResultsGrid.ItemsSource = null;
            SummaryTextBlock.Text = string.Empty;
            SummaryTextBlock.Foreground = NormalBrush;
            EmptyStateTextBlock.Text = "Выберите комплект и нажмите «Проверить».";
            EmptyStateTextBlock.Visibility = System.Windows.Visibility.Visible;
            ShowStatus(string.Empty, false);
        }

        private void ShowStatus(string text, bool isError)
        {
            StatusTextBlock.Text = text ?? string.Empty;
            StatusTextBlock.Foreground = isError ? ErrorBrush : NormalBrush;
        }

        private static string BuildSummary(ElevationCheckResult result)
        {
            string summary =
                $"Проверено листов: {result.CheckedSheetCount}. " +
                $"С отметками: {result.SheetsWithElevationsCount}. " +
                $"Без отметок: {result.ProblemSheets.Count}.";

            if (result.SheetsWithoutAnyViewsCount > 0)
            {
                summary += $" Из них без размещённых видов: {result.SheetsWithoutAnyViewsCount}.";
            }

            if (result.SkippedSheetCount > 0)
            {
                summary += $" Пропущено недоступных листов: {result.SkippedSheetCount}.";
            }

            return summary;
        }

        private static string DescribeRequest(ExternalEventRequest request)
        {
            switch (request)
            {
                case ExternalEventRequest.Pending:
                    return "предыдущая операция ещё выполняется";
                case ExternalEventRequest.Denied:
                    return "Revit отклонил запрос";
                case ExternalEventRequest.TimedOut:
                    return "истекло время ожидания";
                default:
                    return request.ToString();
            }
        }

        private static SolidColorBrush CreateFrozenBrush(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class SortamentsDialog : Window
    {
        private readonly Document _document;
        private readonly SortamentService _service;
        private readonly ForgeTypeId _unitId;
        private readonly string _unitName;
        private readonly ObservableCollection<SortamentSizeRow> _rows = new ObservableCollection<SortamentSizeRow>();
        private readonly Dictionary<int, List<SortamentSizeRow>> _originalRows = new Dictionary<int, List<SortamentSizeRow>>();
        private readonly Dictionary<int, List<SortamentSizeRow>> _drafts = new Dictionary<int, List<SortamentSizeRow>>();
        private int _currentSegmentId;
        private bool _isLoading;
        private bool _isUpdatingSizeConstraints;
        private double _lastMinimumWidth;

        public int SelectedSegmentId { get; private set; }
        public List<SortamentSizeRow> Rows { get; private set; }
        public List<SortamentEdit> Edits { get; private set; } = new List<SortamentEdit>();

        public SortamentsDialog(
            Document document,
            SortamentService service,
            List<SortamentSegmentItem> segments,
            ForgeTypeId unitId)
        {
            InitializeComponent();

            _document = document;
            _service = service;
            _unitId = unitId;
            _unitName = service.GetUnitName(unitId);
            if (string.IsNullOrWhiteSpace(_unitName))
            {
                _unitName = "единицы проекта";
            }

            SegmentComboBox.ItemsSource = segments;
            _isLoading = true;
            SegmentComboBox.SelectedIndex = 0;
            _isLoading = false;

            SegmentDetailsTextBlock.Text = "Единицы отображения: " + _unitName;
            LoadSelectedSegment();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSizeConstraints();
        }

        private void SortamentSizeRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SortamentSizeRow.NominalDiameter) &&
                e.PropertyName != nameof(SortamentSizeRow.OuterDiameter) &&
                e.PropertyName != nameof(SortamentSizeRow.WallThickness) &&
                e.PropertyName != nameof(SortamentSizeRow.InnerDiameter))
            {
                return;
            }

            if (IsLoaded)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(UpdateSizeConstraints));
            }
        }

        private void UpdateSizeConstraints()
        {
            if (_isUpdatingSizeConstraints || SizesDataGrid.Columns.Count < 4)
            {
                return;
            }

            _isUpdatingSizeConstraints = true;
            try
            {
                UpdateColumnMinimumWidth(
                    SizesDataGrid.Columns[0],
                    "Номинальный диаметр",
                    _rows.Select(row => FormatDisplayValue(row.NominalDiameter)));
                UpdateColumnMinimumWidth(
                    SizesDataGrid.Columns[1],
                    "Наружный диаметр",
                    _rows.Select(row => FormatDisplayValue(row.OuterDiameter)));
                UpdateColumnMinimumWidth(
                    SizesDataGrid.Columns[2],
                    "Толщина стенки",
                    _rows.Select(row => FormatDisplayValue(row.WallThickness)));
                UpdateColumnMinimumWidth(
                    SizesDataGrid.Columns[3],
                    "Внутренний диаметр",
                    _rows.Select(row => FormatDisplayValue(row.InnerDiameter)));

                double columnsMinimumWidth = SizesDataGrid.Columns.Sum(column => column.MinWidth);
                double minimumWidth = Math.Ceiling(
                    columnsMinimumWidth +
                    SystemParameters.VerticalScrollBarWidth +
                    40 +
                    16);
                if (Math.Abs(minimumWidth - _lastMinimumWidth) > 0.5)
                {
                    MinWidth = minimumWidth;
                    _lastMinimumWidth = minimumWidth;
                }

                MinHeight = 420;
            }
            finally
            {
                _isUpdatingSizeConstraints = false;
            }
        }

        private void UpdateColumnMinimumWidth(
            DataGridColumn column,
            string header,
            IEnumerable<string> values)
        {
            double requiredWidth = MeasureTextWidth(header, true);
            foreach (string value in values)
            {
                requiredWidth = Math.Max(requiredWidth, MeasureTextWidth(value, false));
            }

            column.MinWidth = Math.Max(80, Math.Ceiling(requiredWidth));
        }

        private double MeasureTextWidth(string text, bool header)
        {
            TextBlock measureBlock = new TextBlock
            {
                Text = text ?? string.Empty,
                FontFamily = SizesDataGrid.FontFamily,
                FontSize = SizesDataGrid.FontSize,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
            };
            measureBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return measureBlock.DesiredSize.Width + (header ? 32 : 24);
        }

        private static string FormatDisplayValue(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private SortamentSegmentItem SelectedSegment =>
            SegmentComboBox.SelectedItem as SortamentSegmentItem;

        private void SegmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            SaveCurrentDraft();
            LoadSelectedSegment();
        }

        private void SaveCurrentDraft()
        {
            if (_currentSegmentId == 0)
            {
                return;
            }

            SizesDataGrid.CommitEdit();
            _drafts[_currentSegmentId] = _rows.Select(row => row.Clone()).ToList();
        }

        private void LoadSelectedSegment()
        {
            SortamentSegmentItem segment = SelectedSegment;
            foreach (SortamentSizeRow row in _rows)
            {
                row.PropertyChanged -= SortamentSizeRow_PropertyChanged;
            }

            _rows.Clear();
            SizesDataGrid.ItemsSource = null;
            SetEditingEnabled(false);

            if (segment == null)
            {
                _currentSegmentId = 0;
                return;
            }

            try
            {
                if (!_originalRows.TryGetValue(segment.SegmentId, out List<SortamentSizeRow> originalRows))
                {
                    originalRows = _service
                        .GetSizeRows(_document, segment.SegmentId, _unitId)
                        .Select(row => row.Clone())
                        .ToList();
                    _originalRows[segment.SegmentId] = originalRows;
                }

                List<SortamentSizeRow> rows = _drafts.TryGetValue(segment.SegmentId, out List<SortamentSizeRow> draftRows)
                    ? draftRows.Select(row => row.Clone()).ToList()
                    : originalRows.Select(row => row.Clone()).ToList();
                foreach (SortamentSizeRow row in rows)
                {
                    row.PropertyChanged += SortamentSizeRow_PropertyChanged;
                    _rows.Add(row);
                }

                SizesDataGrid.ItemsSource = _rows;
                SelectedSegmentId = segment.SegmentId;
                _currentSegmentId = segment.SegmentId;
                SegmentDetailsTextBlock.Text =
                    "Единицы: " + _unitName +
                    "; материал: " + (segment.MaterialName ?? "не указан") +
                    "; Спецификация/Тип: " + (segment.NameWithoutMaterialPrefix ?? "не указано") +
                    "; используется труб: " + segment.UsedPipeCount;
                SetEditingEnabled(true);
                UpdateSizeConstraints();
            }
            catch (Exception ex)
            {
                SegmentDetailsTextBlock.Text = "Не удалось загрузить выбранный каталог размеров.";
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Сортаменты труб",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ButtonAddSize_Click(object sender, RoutedEventArgs e)
        {
            AddDiameterDialog dialog = new AddDiameterDialog(
                _rows.Select(row => row.NominalDiameter),
                _unitName)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || !dialog.Diameter.HasValue)
            {
                return;
            }

            SortamentSizeRow row = new SortamentSizeRow
            {
                IsExisting = false,
                NominalDiameter = dialog.Diameter.Value,
                UsedInSizeLists = true,
                UsedInSizing = true
            };

            row.PropertyChanged += SortamentSizeRow_PropertyChanged;
            _rows.Add(row);
            SizesDataGrid.SelectedIndex = _rows.Count - 1;
            SizesDataGrid.ScrollIntoView(row);
            SizesDataGrid.Focus();
            UpdateSizeConstraints();
        }

        private void SizesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeleteButtonState();
        }

        private void ButtonDeleteSize_Click(object sender, RoutedEventArgs e)
        {
            List<SortamentSizeRow> selectedRows = SizesDataGrid.SelectedItems
                .Cast<SortamentSizeRow>()
                .ToList();

            if (selectedRows.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Выберите строки для удаления.",
                    "Сортаменты труб",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (selectedRows.Any(row => row.IsUsedByPipes))
            {
                MessageBox.Show(
                    this,
                    "Выбранные размеры нельзя удалить: они уже используются трубами в проекте.",
                    "Сортаменты труб",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            foreach (SortamentSizeRow row in selectedRows)
            {
                row.PropertyChanged -= SortamentSizeRow_PropertyChanged;
                _rows.Remove(row);
            }

            UpdateDeleteButtonState();
            UpdateSizeConstraints();
        }

        private void ButtonApply_Click(object sender, RoutedEventArgs e)
        {
            SortamentSegmentItem segment = SelectedSegment;
            if (segment == null)
            {
                MessageBox.Show(
                    this,
                    "Выберите PipeSegment.",
                    "Сортаменты труб",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SizesDataGrid.CommitEdit();
            if (HasValidationErrors(SizesDataGrid))
            {
                MessageBox.Show(
                    this,
                    "Проверьте числовые значения: в таблице есть ошибка ввода.",
                    "Сортаменты труб",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SaveCurrentDraft();
            List<SortamentEdit> edits = BuildEdits();
            if (edits.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Изменений не обнаружено.",
                    "Сортаменты труб",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            foreach (SortamentEdit edit in edits)
            {
                if (!TryValidateEditRows(edit.SegmentId, edit.Rows, out string validationMessage))
                {
                    MessageBox.Show(
                        this,
                        validationMessage,
                        "Сортаменты труб",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            SelectedSegmentId = segment.SegmentId;
            Rows = _rows.Select(row => row.Clone()).ToList();
            Edits = edits;
            DialogResult = true;
            Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private List<SortamentEdit> BuildEdits()
        {
            List<SortamentEdit> result = new List<SortamentEdit>();
            foreach (KeyValuePair<int, List<SortamentSizeRow>> draft in _drafts)
            {
                if (!_originalRows.TryGetValue(draft.Key, out List<SortamentSizeRow> originalRows) ||
                    !RowsDiffer(draft.Value, originalRows))
                {
                    continue;
                }

                result.Add(new SortamentEdit
                {
                    SegmentId = draft.Key,
                    Rows = draft.Value.Select(row => row.Clone()).ToList()
                });
            }

            return result.OrderBy(edit => edit.SegmentId).ToList();
        }

        private bool TryValidateEditRows(int segmentId, List<SortamentSizeRow> rows, out string message)
        {
            message = null;
            if (rows.Count == 0)
            {
                message = "В каталоге должен остаться хотя бы один размер.";
                return false;
            }

            if (HasDuplicateNominalDiameters(rows))
            {
                message = "Номинальные диаметры должны быть уникальными.";
                return false;
            }

            if (rows.Any(row => !IsValidRow(row)))
            {
                message = "Проверьте значения: номинальный диаметр, наружный диаметр и толщина стенки должны быть положительными.";
                return false;
            }

            if (_originalRows.TryGetValue(segmentId, out List<SortamentSizeRow> originalRows))
            {
                foreach (SortamentSizeRow originalRow in originalRows.Where(row => row.IsUsedByPipes))
                {
                    if (FindRow(rows, originalRow.NominalDiameter) == null)
                    {
                        message = "Нельзя удалить размер, который уже используется трубами в проекте.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool RowsDiffer(
            List<SortamentSizeRow> currentRows,
            List<SortamentSizeRow> originalRows)
        {
            if (currentRows.Count != originalRows.Count)
            {
                return true;
            }

            foreach (SortamentSizeRow currentRow in currentRows)
            {
                SortamentSizeRow originalRow = FindRow(originalRows, currentRow.NominalDiameter);
                if (originalRow == null || !RowsEqual(currentRow, originalRow))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RowsEqual(SortamentSizeRow first, SortamentSizeRow second)
        {
            const double tolerance = 0.000001;
            return Math.Abs(first.NominalDiameter - second.NominalDiameter) <= tolerance &&
                   Math.Abs(first.OuterDiameter - second.OuterDiameter) <= tolerance &&
                   Math.Abs(first.WallThickness - second.WallThickness) <= tolerance &&
                   first.UsedInSizeLists == second.UsedInSizeLists &&
                   first.UsedInSizing == second.UsedInSizing;
        }

        private static SortamentSizeRow FindRow(IEnumerable<SortamentSizeRow> rows, double nominalDiameter)
        {
            const double tolerance = 0.000001;
            return rows.FirstOrDefault(row => Math.Abs(row.NominalDiameter - nominalDiameter) <= tolerance);
        }

        private static bool HasDuplicateNominalDiameters(IEnumerable<SortamentSizeRow> rows)
        {
            const double tolerance = 0.000001;
            List<SortamentSizeRow> rowList = rows.ToList();
            for (int i = 0; i < rowList.Count; i++)
            {
                for (int j = i + 1; j < rowList.Count; j++)
                {
                    if (Math.Abs(rowList[i].NominalDiameter - rowList[j].NominalDiameter) <= tolerance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SetEditingEnabled(bool enabled)
        {
            ButtonAddSize.IsEnabled = enabled;
            ButtonApply.IsEnabled = enabled;
            UpdateDeleteButtonState();
        }

        private void UpdateDeleteButtonState()
        {
            if (!ButtonAddSize.IsEnabled)
            {
                ButtonDeleteSize.IsEnabled = false;
                return;
            }

            List<SortamentSizeRow> selectedRows = SizesDataGrid.SelectedItems
                .Cast<SortamentSizeRow>()
                .ToList();
            ButtonDeleteSize.IsEnabled = selectedRows.Count > 0 &&
                                          selectedRows.All(row => !row.IsUsedByPipes);
            ButtonDeleteSize.ToolTip = selectedRows.Any(row => row.IsUsedByPipes)
                ? "Данный размер не может быть удален, так как уже используется в проекте"
                : null;
        }

        private static bool HasValidationErrors(DependencyObject root)
        {
            if (Validation.GetHasError(root))
            {
                return true;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                if (HasValidationErrors(VisualTreeHelper.GetChild(root, i)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidRow(SortamentSizeRow row)
        {
            return row != null &&
                   IsPositiveFinite(row.NominalDiameter) &&
                   IsPositiveFinite(row.OuterDiameter) &&
                   IsPositiveFinite(row.WallThickness) &&
                   IsPositiveFinite(row.InnerDiameter);
        }

        private static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }
    }
}

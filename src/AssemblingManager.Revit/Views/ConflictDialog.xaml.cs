using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class ConflictDialog : Window
    {
        private ScrollViewer _listScrollViewer;
        private string _currentSortProperty;
        private ListSortDirection _currentSortDirection;
        private GridViewColumn _currentSortColumn;

        public List<ViewConflictItem> ConflictItems { get; private set; }

        public ConflictDialog(List<ViewConflictItem> conflictItems)
        {
            ConflictItems = conflictItems;
            InitializeComponent();
            ConflictListView.ItemsSource = ConflictItems;

            ICollectionView view = CollectionViewSource.GetDefaultView(ConflictListView.ItemsSource);
            view.Filter = FilterConflicts;

            UpdateCounter();
        }

        private void ButtonSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool newState = true;

            foreach (ViewConflictItem item in ConflictItems)
            {
                if (item.Replace)
                {
                    newState = false;
                    break;
                }
            }

            foreach (ViewConflictItem item in ConflictItems)
            {
                item.Replace = newState;
            }

            UpdateSelectAllButtonText();
            UpdateCounter();
        }

        private void UpdateSelectAllButtonText()
        {
            bool allSelected = true;

            foreach (ViewConflictItem item in ConflictItems)
            {
                if (!item.Replace)
                {
                    allSelected = false;
                    break;
                }
            }

            ButtonSelectAll.Content = allSelected ? "Снять всё" : "Выбрать всё";
        }

        private void UpdateCounter()
        {
            int selected = 0;

            foreach (ViewConflictItem item in ConflictItems)
            {
                if (item.Replace)
                {
                    selected++;
                }
            }

            TextBlockSelectedCount.Text = $"Выбрано: {selected} из {ConflictItems.Count}";
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ViewConflictItem clickedItem)
            {
                bool newState = checkBox.IsChecked == true;

                if (ConflictListView.SelectedItems.Contains(clickedItem) && ConflictListView.SelectedItems.Count > 1)
                {
                    foreach (ViewConflictItem item in ConflictListView.SelectedItems)
                    {
                        item.Replace = newState;
                    }
                }
            }

            UpdateCounter();
        }

        private bool FilterConflicts(object obj)
        {
            if (!(obj is ViewConflictItem conflict))
            {
                return true;
            }

            string searchText = SearchTextBox.Text;

            if (string.IsNullOrEmpty(searchText))
            {
                return true;
            }

            bool matchesAssembly = conflict.AssemblyName != null
                && conflict.AssemblyName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;

            bool matchesViewType = conflict.ViewTypeDisplayName != null
                && conflict.ViewTypeDisplayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;

            return matchesAssembly || matchesViewType;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(ConflictListView.ItemsSource)?.Refresh();
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchTextBox.Text = string.Empty;
            }
        }

        private void ButtonClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader header) || header.Column == null)
            {
                return;
            }

            string headerText = StripSortArrow(header.Column.Header as string);
            string propertyName = null;

            switch (headerText)
            {
                case "Сборка":
                    propertyName = "AssemblyName";
                    break;
                case "Тип вида":
                    propertyName = "ViewTypeDisplayName";
                    break;
                default:
                    return;
            }

            ListSortDirection direction;
            if (_currentSortProperty == propertyName)
            {
                direction = _currentSortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            else
            {
                direction = ListSortDirection.Ascending;
            }

            if (_currentSortColumn != null)
            {
                _currentSortColumn.Header = StripSortArrow(_currentSortColumn.Header as string);
            }

            header.Column.Header = headerText + (direction == ListSortDirection.Ascending ? " ▲" : " ▼");
            _currentSortColumn = header.Column;
            _currentSortProperty = propertyName;
            _currentSortDirection = direction;

            ICollectionView view = CollectionViewSource.GetDefaultView(ConflictListView.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(propertyName, direction));
        }

        private static string StripSortArrow(string headerText)
        {
            if (headerText == null)
            {
                return null;
            }

            return headerText.Replace(" ▲", "").Replace(" ▼", "");
        }

        private void AssemblyName_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            TextBlock textBlock = sender as TextBlock;
            if (textBlock == null || string.IsNullOrEmpty(textBlock.Text))
            {
                return;
            }

            Typeface typeface = new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch);

            FormattedText formattedText = new FormattedText(
                textBlock.Text,
                CultureInfo.CurrentCulture,
                textBlock.FlowDirection,
                typeface,
                textBlock.FontSize,
                textBlock.Foreground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            bool isTrimmed = formattedText.Width > textBlock.ActualWidth;

            if (!isTrimmed)
            {
                e.Handled = true;
            }
        }

        private void ConflictDialog_ContentRendered(object sender, EventArgs e)
        {
            if (_listScrollViewer == null)
            {
                _listScrollViewer = FindScrollViewer(ConflictListView);
                if (_listScrollViewer != null)
                {
                    _listScrollViewer.ScrollChanged += ListScrollViewer_ScrollChanged;
                }
            }

            CollapseFillerHeader();
            UpdateLastColumnWidth();
        }

        private void ListScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateLastColumnWidth();
        }

        private void CollapseFillerHeader()
        {
            GridViewColumnHeader filler = FindFillerHeader(ConflictListView);
            if (filler != null)
            {
                filler.Visibility = Visibility.Collapsed;
            }
        }

        private static GridViewColumnHeader FindFillerHeader(DependencyObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            if (parent is GridViewColumnHeader header && header.Column == null)
            {
                return header;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                GridViewColumnHeader result = FindFillerHeader(VisualTreeHelper.GetChild(parent, i));
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            if (parent is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                ScrollViewer result = FindScrollViewer(VisualTreeHelper.GetChild(parent, i));
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void UpdateLastColumnWidth()
        {
            if (_listScrollViewer == null)
            {
                return;
            }

            GridView gridView = ConflictListView.View as GridView;
            if (gridView == null || gridView.Columns.Count < 3)
            {
                return;
            }

            double fixedColumnsWidth = gridView.Columns[0].ActualWidth + gridView.Columns[1].ActualWidth;
            double remaining = _listScrollViewer.ViewportWidth - fixedColumnsWidth;

            if (remaining > 100 && Math.Abs(gridView.Columns[2].Width - remaining) > 1)
            {
                gridView.Columns[2].Width = remaining;
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
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class PipeAxesDialog : Window
    {
        private readonly List<AssemblyItem> _items;
        private AssemblyItem _lastClickedItem;

        public PipeAxisSettings Settings { get; private set; }

        public List<AssemblyInstance> SelectedAssemblies { get; private set; }

        public GraphicsStyle SelectedLineStyle { get; private set; }

        public string Mode { get; private set; }

        public PipeAxesDialog(List<AssemblyInstance> assemblies, PipeAxisSettings settings, List<GraphicsStyle> lineStyles)
        {
            InitializeComponent();

            _items = assemblies
                .Select(a => new AssemblyItem { Assembly = a, Name = a.Name })
                .OrderBy(i => i.Name, new NaturalStringComparer())
                .ToList();

            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Num = i + 1;
            }

            AssembliesListView.ItemsSource = _items;
            CollectionViewSource.GetDefaultView(AssembliesListView.ItemsSource).Filter = FilterItems;

            BuildStyleItems(settings, lineStyles ?? new List<GraphicsStyle>());

            SkipOccludedCheckBox.IsChecked = settings?.SkipOccludedSegments ?? PipeAxisSettings.DefaultSkipOccludedSegments;

            UpdateCounter();
        }

        public class AssemblyItem : INotifyPropertyChanged
        {
            private bool _isSelected;

            public int Num { get; set; }

            public AssemblyInstance Assembly { get; set; }

            public string Name { get; set; }

            public bool IsSelected
            {
                get
                {
                    return _isSelected;
                }

                set
                {
                    if (_isSelected == value)
                    {
                        return;
                    }

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private void BuildStyleItems(PipeAxisSettings settings, List<GraphicsStyle> lineStyles)
        {
            foreach (GraphicsStyle style in lineStyles)
            {
                string name = style.GraphicsStyleCategory?.Name ?? style.Name;
                StyleComboBox.Items.Add(new ComboBoxItem { Content = name, Tag = style });
            }

            if (StyleComboBox.Items.Count == 0)
            {
                StyleComboBox.IsEnabled = false;
                StyleComboBox.ToolTip = "В проекте нет стилей линий — создайте их в Управление → Дополнительные настройки → Стили линий.";
                return;
            }

            int selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(settings?.LineStyleName))
            {
                foreach (ComboBoxItem item in StyleComboBox.Items)
                {
                    if (string.Equals(item.Content as string, settings.LineStyleName, StringComparison.Ordinal))
                    {
                        selectedIndex = StyleComboBox.Items.IndexOf(item);
                        break;
                    }
                }
            }

            StyleComboBox.SelectedIndex = selectedIndex;
        }

        private void AssembliesListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ListViewItem container = FindItemContainer(e.OriginalSource as DependencyObject);
            if (container == null)
            {
                return;
            }

            AssemblyItem item = container.Content as AssemblyItem;
            if (item == null)
            {
                return;
            }

            bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (shiftPressed && _lastClickedItem != null && !ReferenceEquals(_lastClickedItem, item))
            {
                bool rangeState = _lastClickedItem.IsSelected;
                int startIndex = _items.IndexOf(_lastClickedItem);
                int endIndex = _items.IndexOf(item);

                if (startIndex >= 0 && endIndex >= 0)
                {
                    int from = Math.Min(startIndex, endIndex);
                    int to = Math.Max(startIndex, endIndex);

                    for (int i = from; i <= to; i++)
                    {
                        _items[i].IsSelected = rangeState;
                    }
                }
            }
            else
            {
                item.IsSelected = !item.IsSelected;
            }

            _lastClickedItem = item;
            e.Handled = true;
            UpdateCounter();
        }

        private static ListViewItem FindItemContainer(DependencyObject source)
        {
            DependencyObject current = source;

            while (current != null && !(current is ListViewItem))
            {
                current = VisualTreeHelper.GetParent(current);
            }

            return current as ListViewItem;
        }

        private void ButtonSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool newState = true;

            foreach (AssemblyItem item in _items)
            {
                if (item.IsSelected)
                {
                    newState = false;
                    break;
                }
            }

            foreach (AssemblyItem item in _items)
            {
                item.IsSelected = newState;
            }

            ButtonSelectAll.Content = newState ? "Снять всё" : "Выбрать всё";
            UpdateCounter();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is AssemblyItem item)
            {
                _lastClickedItem = item;
            }

            UpdateCounter();
        }

        private void UpdateCounter()
        {
            int selected = _items.Count(i => i.IsSelected);
            TextBlockSelectedCount.Text = $"Выбрано: {selected} из {_items.Count}";
        }

        private bool FilterItems(object obj)
        {
            AssemblyItem item = obj as AssemblyItem;
            if (item == null)
            {
                return true;
            }

            string searchText = SearchTextBox.Text;

            if (string.IsNullOrEmpty(searchText))
            {
                return true;
            }

            return item.Name != null
                && item.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(AssembliesListView.ItemsSource)?.Refresh();
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

        private void ButtonApply_Click(object sender, RoutedEventArgs e)
        {
            CloseWithMode("Apply");
        }

        private void ButtonDelete_Click(object sender, RoutedEventArgs e)
        {
            CloseWithMode("Delete");
        }

        private void CloseWithMode(string mode)
        {
            if (StyleComboBox.SelectedItem is ComboBoxItem styleItem && styleItem.Tag is GraphicsStyle style)
            {
                SelectedLineStyle = style;
            }
            else
            {
                MessageBox.Show(
                    this,
                    "Выберите стиль линии для осей.",
                    "Оси трубопроводов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<AssemblyInstance> selected = _items
                .Where(i => i.IsSelected)
                .Select(i => i.Assembly)
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Не выбрано ни одной сборки.",
                    "Оси трубопроводов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Settings = new PipeAxisSettings
            {
                LineStyleName = style.GraphicsStyleCategory?.Name ?? style.Name,
                SkipOccludedSegments = SkipOccludedCheckBox.IsChecked == true
            };

            Mode = mode;
            SelectedAssemblies = selected;
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

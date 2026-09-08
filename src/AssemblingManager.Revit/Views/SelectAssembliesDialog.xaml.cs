using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
    public partial class SelectAssembliesDialog : Window
    {
        private readonly List<AssemblyItem> _items;
        private readonly List<string> _worksetNames;
        private AssemblyItem _lastClickedItem;

        public NeighborAxesSettings Settings { get; private set; }

        public List<AssemblyInstance> SelectedAssemblies { get; private set; }

        public SelectAssembliesDialog(List<AssemblyInstance> assemblies, NeighborAxesSettings settings, List<string> worksetNames)
        {
            InitializeComponent();

            _worksetNames = worksetNames ?? new List<string>();

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

            RadiusTextBox.Text = settings.RadiusMm.ToString(CultureInfo.InvariantCulture);
            BuildWorksetItems(settings);

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

        private void BuildWorksetItems(NeighborAxesSettings settings)
        {
            WorksetComboBox.Items.Add(new ComboBoxItem { Content = "Все рабочие наборы" });

            foreach (string worksetName in _worksetNames)
            {
                WorksetComboBox.Items.Add(new ComboBoxItem { Content = worksetName });
            }

            int selectedIndex = 0;
            if (!string.IsNullOrEmpty(settings.GridWorksetName))
            {
                int worksetIndex = _worksetNames.IndexOf(settings.GridWorksetName);
                if (worksetIndex >= 0)
                {
                    selectedIndex = worksetIndex + 1;
                }
            }

            WorksetComboBox.SelectedIndex = selectedIndex;

            if (_worksetNames.Count == 0)
            {
                WorksetComboBox.IsEnabled = false;
                WorksetComboBox.ToolTip = "Модель не в совместной работе — рабочие наборы отсутствуют.";
            }
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

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            string radiusText = RadiusTextBox.Text.Trim().Replace(',', '.');
            bool radiusParsed = double.TryParse(
                radiusText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double radius);

            if (!radiusParsed || radius <= 0 || radius > 10000)
            {
                MessageBox.Show(
                    this,
                    "Введите радиус кружка в миллиметрах (положительное число).",
                    "Ближайшие оси",
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
                    "Ближайшие оси",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string gridWorksetName = null;
            if (WorksetComboBox.IsEnabled && WorksetComboBox.SelectedIndex > 0)
            {
                gridWorksetName = _worksetNames[WorksetComboBox.SelectedIndex - 1];
            }

            Settings = new NeighborAxesSettings
            {
                RadiusMm = radius,
                GridWorksetName = gridWorksetName
            };

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

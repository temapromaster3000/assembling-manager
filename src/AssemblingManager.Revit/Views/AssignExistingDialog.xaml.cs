using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Common;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class AssignExistingDialog : Window
    {
        private const string PluginParameterName = "AssemblyParameter";
        private const string GroupingParameterName = "ADSK_Группирование";

        private readonly Document _document;
        private readonly ParameterService _parameterService;
        private readonly List<AssemblyItem> _items;
        private readonly bool _groupingParameterFound;

        public AssemblyInstance SelectedAssembly { get; private set; }

        public bool UseGroupingParameter { get; private set; }

        public AssignExistingDialog(Document document, List<AssemblyInstance> assemblies, int elementCount, int nestedCount)
        {
            InitializeComponent();

            _document = document;
            _parameterService = new ParameterService();

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

            bool pluginParameterFound = _parameterService.GetParameterByName(_document, PluginParameterName) != null;
            _groupingParameterFound = _parameterService.GetParameterByName(_document, GroupingParameterName) != null;

            TextBlockPluginParameterStatus.Text = pluginParameterFound
                ? "найден в проекте"
                : "не найден — будет создан плагином";

            TextBlockGroupingParameterStatus.Text = _groupingParameterFound
                ? "найден в проекте"
                : "не найден в проекте";

            if (!_groupingParameterFound)
            {
                RadioButtonUseGrouping.IsEnabled = false;
            }

            RadioButtonCreateNew.IsChecked = true;

            TextBlockElementCount.Text = string.Format(
                "Выбрано элементов: {0} (в т.ч. вложенных: {1})",
                elementCount,
                nestedCount);

            TextBlockAssemblyCount.Text = $"Сборок: {_items.Count}";
        }

        public class AssemblyItem
        {
            public int Num { get; set; }

            public AssemblyInstance Assembly { get; set; }

            public string Name { get; set; }
        }

        private void AssembliesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ButtonOK.IsEnabled = AssembliesListView.SelectedItem != null;
        }

        private void AssembliesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AssembliesListView.SelectedItem != null)
            {
                ButtonOK_Click(ButtonOK, new RoutedEventArgs());
            }
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
            AssemblyItem selectedItem = AssembliesListView.SelectedItem as AssemblyItem;

            if (selectedItem == null)
            {
                MessageBox.Show(
                    this,
                    "Выберите сборку из списка.",
                    "Добавить существующее",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (RadioButtonUseGrouping.IsChecked == true)
            {
                if (!_groupingParameterFound)
                {
                    MessageBox.Show(
                        this,
                        $"Параметр '{GroupingParameterName}' не найден в проекте.",
                        "Добавить существующее",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                UseGroupingParameter = true;
            }
            else
            {
                UseGroupingParameter = false;
            }

            SelectedAssembly = selectedItem.Assembly;
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

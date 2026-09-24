using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace AssemblingManager.Revit.Views
{
    public partial class AddDiameterDialog : Window
    {
        private const double DuplicateTolerance = 0.000001;
        private readonly List<double> _existingValues;

        public double? Diameter { get; private set; }

        public AddDiameterDialog(IEnumerable<double> existingValues, string unitName)
        {
            InitializeComponent();
            _existingValues = new List<double>(existingValues ?? Array.Empty<double>());
            UnitTextBlock.Text = "Единицы: " + (string.IsNullOrWhiteSpace(unitName) ? "проекта" : unitName);
            Loaded += AddDiameterDialog_Loaded;
        }

        private void AddDiameterDialog_Loaded(object sender, RoutedEventArgs e)
        {
            DiameterTextBox.Focus();
            DiameterTextBox.SelectAll();
        }

        private void ButtonAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(
                    DiameterTextBox.Text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.CurrentCulture,
                    out double value) ||
                double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value <= 0.0)
            {
                MessageBox.Show(
                    this,
                    "Введите положительное числовое значение номинального диаметра.",
                    "Добавление размера",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DiameterTextBox.Focus();
                DiameterTextBox.SelectAll();
                return;
            }

            if (_existingValues.Any(existing => Math.Abs(existing - value) <= DuplicateTolerance))
            {
                MessageBox.Show(
                    this,
                    "Такой номинальный диаметр уже существует в текущем каталоге.",
                    "Добавление размера",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DiameterTextBox.Focus();
                DiameterTextBox.SelectAll();
                return;
            }

            Diameter = value;
            DialogResult = true;
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public class TagOptionItem
    {
        public string Name { get; }
        public FamilySymbol Symbol { get; }

        public TagOptionItem(string name, FamilySymbol symbol)
        {
            Name = name;
            Symbol = symbol;
        }
    }

    public class TagCategoryItem : INotifyPropertyChanged
    {
        private TagOptionItem _selected;
        private readonly bool _hasOptions;

        public Category Category { get; }
        public string CategoryName { get; }
        public List<TagOptionItem> Options { get; }
        public bool HasOptions { get { return _hasOptions; } }

        public TagOptionItem Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public TagCategoryItem(Category category, List<TagOptionItem> options)
        {
            Category = category;
            CategoryName = category != null ? category.Name : "?";
            Options = options ?? new List<TagOptionItem>();

            if (Options.Count == 0)
            {
                Options.Add(new TagOptionItem(TagService.SkipOptionName + " (нет марок)", null));
                _hasOptions = false;
            }
            else
            {
                _hasOptions = true;
            }

            _selected = Options[0];
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class AssignTagsDialog : Window
    {
        private readonly List<TagCategoryItem> _categories;

        private static readonly int MinOffsetRangeMin = 200;
        private static readonly int MinOffsetRangeMax = 3000;
        private static readonly int ZoneHeightRangeMin = 50;
        private static readonly int ZoneHeightRangeMax = 2000;

        public Dictionary<ElementId, FamilySymbol> SelectedSymbolsByCategoryId { get; private set; }

        public int MinOffsetMm
        {
            get
            {
                int parsed;
                if (!int.TryParse(MinOffsetTextBox.Text.Trim(), out parsed))
                {
                    return TagPresetStorage.DefaultMinOffsetMm;
                }

                return Math.Max(MinOffsetRangeMin, Math.Min(MinOffsetRangeMax, parsed));
            }
        }

        public int ZoneHeightMm
        {
            get
            {
                int parsed;
                if (!int.TryParse(ZoneHeightTextBox.Text.Trim(), out parsed))
                {
                    return TagPresetStorage.DefaultZoneHeightMm;
                }

                return Math.Max(ZoneHeightRangeMin, Math.Min(ZoneHeightRangeMax, parsed));
            }
        }

        public bool TextBelowShelf
        {
            get { return RadioTextBelow.IsChecked == true; }
        }

        public AssignTagsDialog(
            IReadOnlyList<TagCategoryItem> categories,
            IReadOnlyDictionary<string, string> preset,
            int initialMinOffsetMm,
            int initialZoneHeightMm,
            bool initialTextBelowShelf)
        {
            InitializeComponent();

            _categories = categories.ToList();

            Logger.Info($"AssignTagsDialog opened: {_categories.Count} categories, preset entries {preset?.Count ?? 0}.");

            foreach (TagCategoryItem item in _categories)
            {
                TagOptionItem saved = null;
                bool found = preset != null && TryGetSavedOption(item, preset, out saved);

                Logger.Info(
                    $"  Category '{item.CategoryName}': preset {found} {(found ? "-> '" + saved.Name + "'" : string.Empty)}.");

                if (found)
                {
                    item.Selected = saved;
                }

                item.PropertyChanged += (s, e) => UpdateSummary();
            }

            MinOffsetTextBox.Text = Math.Max(
                MinOffsetRangeMin,
                Math.Min(MinOffsetRangeMax, initialMinOffsetMm)).ToString();

            ZoneHeightTextBox.Text = Math.Max(
                ZoneHeightRangeMin,
                Math.Min(ZoneHeightRangeMax, initialZoneHeightMm)).ToString();

            if (initialTextBelowShelf)
            {
                RadioTextBelow.IsChecked = true;
            }

            CategoriesList.ItemsSource = _categories;
            UpdateSummary();
            UpdateMinOffsetValidation();
            UpdateZoneHeightValidation();
        }

        private void MinOffsetTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateMinOffsetValidation();
        }

        private void ZoneHeightTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateZoneHeightValidation();
        }

        private void UpdateMinOffsetValidation()
        {
            if (ButtonOK == null || MinOffsetTextBox == null || MinOffsetErrorText == null)
            {
                return;
            }

            int parsed;
            bool valid = int.TryParse(MinOffsetTextBox.Text.Trim(), out parsed)
                && parsed >= MinOffsetRangeMin
                && parsed <= MinOffsetRangeMax;

            MinOffsetErrorText.Visibility = valid
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            UpdateOkButton(valid);
        }

        private void UpdateZoneHeightValidation()
        {
            if (ButtonOK == null || ZoneHeightTextBox == null || ZoneHeightErrorText == null)
            {
                return;
            }

            int parsed;
            bool valid = int.TryParse(ZoneHeightTextBox.Text.Trim(), out parsed)
                && parsed >= ZoneHeightRangeMin
                && parsed <= ZoneHeightRangeMax;

            ZoneHeightErrorText.Visibility = valid
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            UpdateOkButton(valid);
        }

        private void UpdateOkButton(bool minOffsetValid)
        {
            if (ButtonOK == null || MinOffsetTextBox == null || ZoneHeightTextBox == null)
            {
                return;
            }

            int minOffsetParsed;
            int zoneHeightParsed;
            bool allValid = minOffsetValid
                && int.TryParse(MinOffsetTextBox.Text.Trim(), out minOffsetParsed)
                && minOffsetParsed >= MinOffsetRangeMin
                && minOffsetParsed <= MinOffsetRangeMax
                && int.TryParse(ZoneHeightTextBox.Text.Trim(), out zoneHeightParsed)
                && zoneHeightParsed >= ZoneHeightRangeMin
                && zoneHeightParsed <= ZoneHeightRangeMax;

            ButtonOK.IsEnabled = allValid;
        }

        private bool TryGetSavedOption(
            TagCategoryItem item,
            IReadOnlyDictionary<string, string> preset,
            out TagOptionItem saved)
        {
            saved = null;
            if (item.Options == null || item.Options.Count == 0)
            {
                return false;
            }

            string entry;
            if (!preset.TryGetValue(item.CategoryName, out entry) || string.IsNullOrEmpty(entry))
            {
                return false;
            }

            string[] parts = entry.Split(new[] { TagPresetStorage.EntrySeparator[0] }, 2);
            if (parts.Length != 2)
            {
                return false;
            }

            saved = item.Options.FirstOrDefault(o =>
                o.Symbol != null
                && string.Equals(
                    o.Symbol.Family.Name.Trim(), parts[0].Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    o.Symbol.Name.Trim(), parts[1].Trim(), StringComparison.OrdinalIgnoreCase));

            return saved != null;
        }

        private void UpdateSummary()
        {
            if (ButtonOK == null)
            {
                return;
            }

            int selectedCount = _categories.Count(c => c.HasOptions && c.Selected != null && c.Selected.Symbol != null);

            SummaryTextBlock.Text = $"Выбрано марок: {selectedCount} из {_categories.Count} категорий.";
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<ElementId, FamilySymbol> result = new Dictionary<ElementId, FamilySymbol>();

            foreach (TagCategoryItem item in _categories)
            {
                if (!item.HasOptions || item.Selected == null || item.Selected.Symbol == null)
                {
                    continue;
                }

                result[item.Category.Id] = item.Selected.Symbol;
            }

            SelectedSymbolsByCategoryId = result;
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

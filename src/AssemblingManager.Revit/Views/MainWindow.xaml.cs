using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public partial class MainWindow : Window
    {
        public ViewCreationOptions Options { get; private set; }

        private const string GroupingParameterName = "ADSK_Группирование";
        private const string NewParameterName = "AssemblyParameter";

        private readonly int _assemblyCount;
        private readonly Document _document;
        private readonly IReadOnlyList<Category> _assemblyCategories;
        private readonly IReadOnlyList<ElementId> _assemblyElementIds;
        private readonly ParameterService _parameterService;
        private readonly ViewTemplateService _viewTemplateService;
        private bool _isUpdatingSectionsState;
        private bool _wasPopupOpen;
        private bool _groupingParameterMissing;
        private bool _groupingParameterTypeBinding;
        private int _missingCategoriesCount;

        public MainWindow(
            int assemblyCount,
            IReadOnlyList<Category> assemblyCategories,
            IReadOnlyList<ElementId> assemblyElementIds,
            Document document,
            ViewCreationOptions initialOptions = null)
        {
            _assemblyCount = assemblyCount;
            _document = document;
            _assemblyCategories = assemblyCategories;
            _assemblyElementIds = assemblyElementIds;
            _parameterService = new ParameterService();
            _viewTemplateService = new ViewTemplateService();

            InitializeComponent();

            PopupSectionOptions.PlacementTarget = ButtonSectionOptions;

            InitializeParameterMode(initialOptions);
            InitializeCheckBoxes(initialOptions);
            UpdateCounter();
            InitializeTemplateComboBoxes(initialOptions);
        }

        private void InitializeParameterMode(ViewCreationOptions initialOptions)
        {
            if (initialOptions != null)
            {
                if (initialOptions.UseExistingGroupingParameter)
                {
                    RadioButtonUseGrouping.IsChecked = true;
                }
                else if (initialOptions.CreateNewParameter)
                {
                    RadioButtonCreateNew.IsChecked = true;
                }
            }
            else
            {
                RadioButtonUseGrouping.IsChecked = true;
            }
        }

        private void InitializeCheckBoxes(ViewCreationOptions initialOptions)
        {
            if (initialOptions != null)
            {
                CheckBoxPlan.IsChecked = initialOptions.CreatePlan;
                CheckBox3D.IsChecked = initialOptions.Create3D;
                CheckBoxFrontView.IsChecked = initialOptions.CreateFrontView;
                CheckBoxBackView.IsChecked = initialOptions.CreateBackView;
                CheckBoxRightView.IsChecked = initialOptions.CreateRightView;
                CheckBoxLeftView.IsChecked = initialOptions.CreateLeftView;
            }
            else
            {
                CheckBoxPlan.IsChecked = true;
                CheckBox3D.IsChecked = true;

                CheckBoxFrontView.IsChecked = true;
                CheckBoxBackView.IsChecked = true;
                CheckBoxRightView.IsChecked = true;
                CheckBoxLeftView.IsChecked = true;
            }

            UpdateSectionsCheckBoxState();
        }

        private void InitializeTemplateComboBoxes(ViewCreationOptions initialOptions)
        {
            List<ViewTemplateItem> planTemplates = _viewTemplateService.GetPlanTemplates(_document);
            ComboBoxPlanTemplate.ItemsSource = planTemplates;
            ComboBoxPlanTemplate.DisplayMemberPath = "Name";
            ComboBoxPlanTemplate.SelectedItem = SelectTemplateById(planTemplates, initialOptions?.PlanTemplateId);

            List<ViewTemplateItem> sectionTemplates = _viewTemplateService.GetSectionTemplates(_document);
            ComboBoxSectionTemplate.ItemsSource = sectionTemplates;
            ComboBoxSectionTemplate.DisplayMemberPath = "Name";
            ComboBoxSectionTemplate.SelectedItem = SelectTemplateById(sectionTemplates, initialOptions?.SectionTemplateId);

            List<ViewTemplateItem> view3DTemplates = _viewTemplateService.GetView3DTemplates(_document);
            ComboBoxView3DTemplate.ItemsSource = view3DTemplates;
            ComboBoxView3DTemplate.DisplayMemberPath = "Name";
            ComboBoxView3DTemplate.SelectedItem = SelectTemplateById(view3DTemplates, initialOptions?.View3DTemplateId);
        }

        private ViewTemplateItem SelectTemplateById(List<ViewTemplateItem> templates, int? templateId)
        {
            if (!templateId.HasValue)
            {
                return templates.FirstOrDefault();
            }

            return templates.FirstOrDefault(t => t.Id == templateId) ?? templates.FirstOrDefault();
        }

        private int? GetSelectedTemplateId(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ViewTemplateItem)?.Id;
        }

        private void ParameterMode_Checked(object sender, RoutedEventArgs e)
        {
            UpdateParameterStatus();
        }

        private void ParameterMode_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateParameterStatus();
        }

        private void UpdateParameterStatus()
        {
            ClearParameterStatus();

            if (RadioButtonUseGrouping.IsChecked == true)
            {
                ValidateGroupingParameter();
            }
            else if (RadioButtonCreateNew.IsChecked == true)
            {
                ShowCreateNewParameterInfo();
            }
            else
            {
                TextBlockLine1.Text = "Выберите способ работы с параметром.";
                TextBlockLine1.Foreground = Brushes.Black;
                ButtonCreate.IsEnabled = false;
            }
        }

        private void ClearParameterStatus()
        {
            TextBlockLine1.Text = string.Empty;
            TextBlockLine2.Text = string.Empty;
            TextBlockLine1.Foreground = Brushes.Black;
            TextBlockLine2.Foreground = Brushes.Black;
            _groupingParameterMissing = false;
            _groupingParameterTypeBinding = false;
        }

        private void SetStatusLine(TextBlock textBlock, string prefix, int count)
        {
            textBlock.Text = $"{prefix}: {count}";
            textBlock.Foreground = count > 0 ? Brushes.Orange : Brushes.Black;
        }

        private void ValidateGroupingParameter()
        {
            ElementId parameterId = _parameterService.GetParameterByName(_document, GroupingParameterName);

            if (parameterId == null)
            {
                TextBlockLine1.Text = "Параметр не найден";
                TextBlockLine1.Foreground = Brushes.Red;
                TextBlockLine2.Text = string.Empty;
                _groupingParameterMissing = true;
                _groupingParameterTypeBinding = false;
                return;
            }

            ParameterValidationResult categoriesResult = _parameterService.ValidateParameterCategories(
                _document,
                parameterId,
                _assemblyCategories);

            if (!categoriesResult.IsValid)
            {
                TextBlockLine1.Text = categoriesResult.IsTypeBinding
                    ? "Параметр создан по типу"
                    : "Параметр не найден";
                TextBlockLine1.Foreground = Brushes.Red;
                TextBlockLine2.Text = string.Empty;
                _groupingParameterMissing = !categoriesResult.IsTypeBinding;
                _groupingParameterTypeBinding = categoriesResult.IsTypeBinding;
                return;
            }

            int missingCategoriesCount = categoriesResult.MissingCategories.Count;
            int existingValuesCount = _parameterService.CountExistingValues(
                _document,
                GroupingParameterName,
                _assemblyElementIds);

            _missingCategoriesCount = missingCategoriesCount;

            SetStatusLine(TextBlockLine1, "Будет привязано категорий", missingCategoriesCount);
            SetStatusLine(TextBlockLine2, "Будет перезаписано значений", existingValuesCount);
            TextBlockLine1.Foreground = Brushes.Orange;
            TextBlockLine2.Foreground = Brushes.Orange;
            _groupingParameterMissing = false;
            _groupingParameterTypeBinding = false;
        }

        private void ShowCreateNewParameterInfo()
        {
            TextBlockLine1.Text = $"Будет создан общий параметр с именем '{NewParameterName}'.";
            TextBlockLine1.Foreground = Brushes.Orange;
            TextBlockLine2.Text = string.Empty;
            _groupingParameterMissing = false;
            _groupingParameterTypeBinding = false;
        }

        private void ButtonSectionOptions_Click(object sender, RoutedEventArgs e)
        {
            PopupSectionOptions.IsOpen = !_wasPopupOpen;
        }

        private void ButtonSectionOptions_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _wasPopupOpen = PopupSectionOptions.IsOpen;
        }

        private void CheckBoxSections_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSectionsState) return;

            _isUpdatingSectionsState = true;

            bool? newState = CheckBoxSections.IsChecked;

            if (newState == true)
            {
                CheckBoxFrontView.IsChecked = true;
                CheckBoxBackView.IsChecked = true;
                CheckBoxRightView.IsChecked = true;
                CheckBoxLeftView.IsChecked = true;
            }
            else
            {
                CheckBoxFrontView.IsChecked = false;
                CheckBoxBackView.IsChecked = false;
                CheckBoxRightView.IsChecked = false;
                CheckBoxLeftView.IsChecked = false;
            }

            _isUpdatingSectionsState = false;

            UpdateSectionsCheckBoxState();
            UpdateCounter();
        }

        private void IndividualCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender != CheckBoxPlan && sender != CheckBox3D)
            {
                UpdateSectionsCheckBoxState();
            }

            UpdateCounter();
        }

        private void UpdateSectionsCheckBoxState()
        {
            _isUpdatingSectionsState = true;

            bool? front = CheckBoxFrontView.IsChecked;
            bool? back = CheckBoxBackView.IsChecked;
            bool? right = CheckBoxRightView.IsChecked;
            bool? left = CheckBoxLeftView.IsChecked;

            if (front == true && back == true && right == true && left == true)
            {
                CheckBoxSections.IsChecked = true;
            }
            else if (front == false && back == false && right == false && left == false)
            {
                CheckBoxSections.IsChecked = false;
            }
            else
            {
                CheckBoxSections.IsChecked = null;
            }

            _isUpdatingSectionsState = false;
        }

        private void UpdateCounter()
        {
            TextBlockAssemblyCount.Text = $"{_assemblyCount}";

            int selectedViewCount = 0;

            if (CheckBoxPlan.IsChecked == true) selectedViewCount++;
            if (CheckBox3D.IsChecked == true) selectedViewCount++;
            if (CheckBoxFrontView.IsChecked == true) selectedViewCount++;
            if (CheckBoxBackView.IsChecked == true) selectedViewCount++;
            if (CheckBoxRightView.IsChecked == true) selectedViewCount++;
            if (CheckBoxLeftView.IsChecked == true) selectedViewCount++;

            int totalViews = _assemblyCount * selectedViewCount;
            TextBlockViewCount.Text = $"{totalViews}";
        }

        private void ButtonCreate_Click(object sender, RoutedEventArgs e)
        {
            bool useGrouping = RadioButtonUseGrouping.IsChecked == true;
            bool createNew = RadioButtonCreateNew.IsChecked == true;

            if (!useGrouping && !createNew)
            {
                MessageBox.Show("Выберите способ работы с параметром.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (useGrouping && _groupingParameterMissing)
            {
                MessageBox.Show("Параметр ADSK_Группирование не создан в данном проекте.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (useGrouping && _groupingParameterTypeBinding)
            {
                MessageBox.Show("Параметр ADSK_Группирование создан для типов, плагин должен записывать значения в экземпляры.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? planTemplateId = GetSelectedTemplateId(ComboBoxPlanTemplate);
            int? sectionTemplateId = GetSelectedTemplateId(ComboBoxSectionTemplate);
            int? view3DTemplateId = GetSelectedTemplateId(ComboBoxView3DTemplate);

            bool createPlan = CheckBoxPlan.IsChecked == true;
            bool createSections = CheckBoxFrontView.IsChecked == true ||
                                  CheckBoxBackView.IsChecked == true ||
                                  CheckBoxRightView.IsChecked == true ||
                                  CheckBoxLeftView.IsChecked == true;
            bool create3D = CheckBox3D.IsChecked == true;

            List<string> blockedViewTypes = new List<string>();

            if (createPlan && planTemplateId.HasValue && _viewTemplateService.IsTemplateLockingFilters(_document, planTemplateId.Value))
            {
                blockedViewTypes.Add("План");
            }

            if (createSections && sectionTemplateId.HasValue && _viewTemplateService.IsTemplateLockingFilters(_document, sectionTemplateId.Value))
            {
                blockedViewTypes.Add("Разрезы");
            }

            if (create3D && view3DTemplateId.HasValue && _viewTemplateService.IsTemplateLockingFilters(_document, view3DTemplateId.Value))
            {
                blockedViewTypes.Add("3D вид");
            }

            int selectedViewTypesCount = 0;
            if (createPlan) selectedViewTypesCount++;
            if (createSections) selectedViewTypesCount++;
            if (create3D) selectedViewTypesCount++;

            if (blockedViewTypes.Count > 0 && blockedViewTypes.Count == selectedViewTypesCount)
            {
                MessageBox.Show(
                    "Все выбранные шаблоны видов блокируют применение фильтров.\n" +
                    "Без фильтров плагин не имеет смысла.\n" +
                    "Пожалуйста, выберите другие шаблоны или отключите шаблоны для этих видов.",
                    "Assembling Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (blockedViewTypes.Count > 0)
            {
                string message = "Следующие шаблоны блокируют применение фильтров:\n" +
                    string.Join("\n", blockedViewTypes.Select(v => $"• {v}")) +
                    "\n\nВиды этих типов созданы не будут. Продолжить создание оставшихся видов?";

                MessageBoxResult result = MessageBox.Show(
                    message,
                    "Assembling Manager",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            Options = new ViewCreationOptions
            {
                UseExistingGroupingParameter = useGrouping,
                CreateNewParameter = createNew,
                MissingCategoriesCount = _missingCategoriesCount,
                CreatePlan = createPlan,
                CreateFrontView = CheckBoxFrontView.IsChecked ?? false,
                CreateBackView = CheckBoxBackView.IsChecked ?? false,
                CreateRightView = CheckBoxRightView.IsChecked ?? false,
                CreateLeftView = CheckBoxLeftView.IsChecked ?? false,
                Create3D = create3D,
                PlanTemplateId = planTemplateId,
                SectionTemplateId = sectionTemplateId,
                View3DTemplateId = view3DTemplateId
            };

            if (blockedViewTypes.Contains("План"))
            {
                Options.CreatePlan = false;
            }

            if (blockedViewTypes.Contains("Разрезы"))
            {
                Options.CreateFrontView = false;
                Options.CreateBackView = false;
                Options.CreateRightView = false;
                Options.CreateLeftView = false;
            }

            if (blockedViewTypes.Contains("3D вид"))
            {
                Options.Create3D = false;
            }

            if (!Options.CreatePlan &&
                !Options.CreateFrontView &&
                !Options.CreateBackView &&
                !Options.CreateRightView &&
                !Options.CreateLeftView &&
                !Options.Create3D)
            {
                MessageBox.Show("Выберите хотя бы один вид.", "Assembling Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

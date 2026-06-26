using System.Windows;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class MainWindow : Window
    {
        public ViewCreationOptions Options { get; private set; }

        private readonly int _assemblyCount;
        private bool _isUpdatingSectionsState;

        public MainWindow(int assemblyCount, ViewCreationOptions initialOptions = null)
        {
            _assemblyCount = assemblyCount;
            InitializeComponent();

            InitializeCheckBoxes(initialOptions);
            UpdateCounter();
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
            TextBlockAssemblyCount.Text = $"Сборок в проекте: {_assemblyCount}";

            int selectedViewCount = 0;

            if (CheckBoxPlan.IsChecked == true) selectedViewCount++;
            if (CheckBox3D.IsChecked == true) selectedViewCount++;
            if (CheckBoxFrontView.IsChecked == true) selectedViewCount++;
            if (CheckBoxBackView.IsChecked == true) selectedViewCount++;
            if (CheckBoxRightView.IsChecked == true) selectedViewCount++;
            if (CheckBoxLeftView.IsChecked == true) selectedViewCount++;

            int totalViews = _assemblyCount * selectedViewCount;
            TextBlockViewCount.Text = $"Будет создано видов: {totalViews}";
        }

        private void ButtonCreate_Click(object sender, RoutedEventArgs e)
        {
            Options = new ViewCreationOptions
            {
                CreatePlan = CheckBoxPlan.IsChecked ?? false,
                CreateFrontView = CheckBoxFrontView.IsChecked ?? false,
                CreateBackView = CheckBoxBackView.IsChecked ?? false,
                CreateRightView = CheckBoxRightView.IsChecked ?? false,
                CreateLeftView = CheckBoxLeftView.IsChecked ?? false,
                Create3D = CheckBox3D.IsChecked ?? false
            };

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

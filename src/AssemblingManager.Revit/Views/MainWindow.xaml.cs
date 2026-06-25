using System.Windows;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public partial class MainWindow : Window
    {
        public ViewCreationOptions Options { get; private set; }

        public MainWindow()
        {
            InitializeComponent();

            CheckBoxPlan.IsChecked = true;
            CheckBoxFrontView.IsChecked = true;
            CheckBoxBackView.IsChecked = true;
            CheckBoxRightView.IsChecked = true;
            CheckBoxLeftView.IsChecked = true;
            CheckBox3D.IsChecked = true;
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

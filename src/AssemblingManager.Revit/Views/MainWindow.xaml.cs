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
            CheckBoxSection.IsChecked = true;
            CheckBox3D.IsChecked = true;
        }

        private void ButtonCreate_Click(object sender, RoutedEventArgs e)
        {
            Options = new ViewCreationOptions
            {
                CreatePlan = CheckBoxPlan.IsChecked ?? false,
                CreateSection = CheckBoxSection.IsChecked ?? false,
                Create3D = CheckBox3D.IsChecked ?? false
            };

            if (!Options.CreatePlan && !Options.CreateSection && !Options.Create3D)
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

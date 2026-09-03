using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    public class ScheduleTreeNode : INotifyPropertyChanged
    {
        private readonly string _name;
        private bool _isSelected;
        private ObservableCollection<ScheduleTreeNode> _visibleChildren;

        public ViewSchedule Schedule { get; }
        public List<ScheduleTreeNode> Children { get; }
        public ObservableCollection<ScheduleTreeNode> VisibleChildren
        {
            get { return _visibleChildren; }
            set
            {
                _visibleChildren = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleChildren)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }

        public bool IsLeaf
        {
            get { return Schedule != null; }
        }

        public string Name
        {
            get { return _name; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
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

        public string DisplayName
        {
            get
            {
                if (IsLeaf)
                {
                    return _name;
                }

                int count = VisibleChildren != null ? CountLeaves() : 0;
                return $"{_name} ({count})";
            }
        }

        private int CountLeaves()
        {
            int result = 0;
            foreach (ScheduleTreeNode child in Children)
            {
                result += child.IsLeaf ? 1 : child.CountLeaves();
            }
            return result;
        }

        public void CollectLeaves(List<ScheduleTreeNode> leaves)
        {
            if (IsLeaf)
            {
                leaves.Add(this);
                return;
            }

            foreach (ScheduleTreeNode child in Children)
            {
                child.CollectLeaves(leaves);
            }
        }

        public ScheduleTreeNode(string name, List<ScheduleTreeNode> children)
        {
            _name = name;
            Children = children;
            VisibleChildren = new ObservableCollection<ScheduleTreeNode>(children);
        }

        public ScheduleTreeNode(ViewSchedule schedule)
        {
            _name = schedule.Name;
            Schedule = schedule;
            Children = new List<ScheduleTreeNode>();
            VisibleChildren = new ObservableCollection<ScheduleTreeNode>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class AssignPositionsDialog : Window
    {
        private readonly List<ScheduleTreeNode> _roots;
        private readonly List<TextBox> _keywordBoxes = new List<TextBox>();
        private readonly List<ScheduleTreeNode> _flatLeaves = new List<ScheduleTreeNode>();
        private ScheduleTreeNode _lastLeaf;

        public List<string> Keywords
        {
            get
            {
                return _keywordBoxes
                    .Select(b => b.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .ToList();
            }
        }

        public List<ViewSchedule> SelectedSchedules { get; private set; }

        public bool MergeSchedules
        {
            get { return MergeSchedulesCheckBox != null && MergeSchedulesCheckBox.IsChecked == true; }
        }

        public bool OnlyPositions
        {
            get { return OnlyPositionsCheckBox != null && OnlyPositionsCheckBox.IsChecked == true; }
        }

        public AssignPositionsDialog(List<ScheduleGroupNode> scheduleRoots, IReadOnlyList<string> initialKeywords)
        {
            InitializeComponent();

            _roots = BuildTree(scheduleRoots);
            _roots.ForEach(r => r.CollectLeaves(_flatLeaves));

            SchedulesTreeView.ItemsSource = _roots;

            if (initialKeywords == null || initialKeywords.Count == 0)
            {
                AddKeywordBox();
            }
            else
            {
                foreach (string keyword in initialKeywords)
                {
                    AddKeywordBox(keyword);
                }
            }

            UpdateSummary();
        }

        private static List<ScheduleTreeNode> BuildTree(List<ScheduleGroupNode> roots)
        {
            List<ScheduleTreeNode> result = new List<ScheduleTreeNode>();

            foreach (ScheduleGroupNode root in roots)
            {
                if (root.IsSchedule)
                {
                    result.Add(new ScheduleTreeNode(root.Schedule));
                }
                else
                {
                    result.Add(new ScheduleTreeNode(root.Name, BuildTree(root.Children.ToList())));
                }
            }

            return result;
        }

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem item = sender as TreeViewItem;
            ScheduleTreeNode node = item?.DataContext as ScheduleTreeNode;
            if (node == null)
            {
                return;
            }

            if (node.IsLeaf)
            {
                ModifierKeys modifiers = Keyboard.Modifiers;

                if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    ToggleSelection(node);
                }
                else if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && _lastLeaf != null)
                {
                    SelectRangeTo(node);
                }
                else
                {
                    SelectSingle(node);
                }

                e.Handled = true;
                UpdateSummary();
            }
        }

        private void SelectSingle(ScheduleTreeNode node)
        {
            foreach (ScheduleTreeNode leaf in _flatLeaves)
            {
                leaf.IsSelected = false;
            }

            node.IsSelected = true;
            _lastLeaf = node;
        }

        private void ToggleSelection(ScheduleTreeNode node)
        {
            node.IsSelected = !node.IsSelected;
            _lastLeaf = node;
        }

        private void SelectRangeTo(ScheduleTreeNode node)
        {
            int fromIndex = _flatLeaves.IndexOf(_lastLeaf);
            int toIndex = _flatLeaves.IndexOf(node);

            if (fromIndex < 0 || toIndex < 0)
            {
                SelectSingle(node);
                return;
            }

            int start = System.Math.Min(fromIndex, toIndex);
            int end = System.Math.Max(fromIndex, toIndex);

            for (int i = start; i <= end; i++)
            {
                _flatLeaves[i].IsSelected = true;
            }

            _lastLeaf = node;
        }

        private void AddKeywordBox(string initialText = null)
        {
            TextBox textBox = new TextBox
            {
                Height = 26,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(4, 3, 4, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (initialText != null)
            {
                textBox.Text = initialText;
            }

            _keywordBoxes.Add(textBox);
            KeywordsPanel.Children.Add(textBox);
            textBox.Focus();
            UpdateSummary();
        }

        private void ButtonAddKeyword_Click(object sender, RoutedEventArgs e)
        {
            AddKeywordBox();
        }

        private void ButtonRemoveKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (_keywordBoxes.Count == 0)
            {
                return;
            }

            TextBox activeBox = _keywordBoxes.FirstOrDefault(b => b.IsKeyboardFocused);

            if (activeBox == null)
            {
                activeBox = _keywordBoxes[_keywordBoxes.Count - 1];
            }

            _keywordBoxes.Remove(activeBox);
            KeywordsPanel.Children.Remove(activeBox);
            UpdateSummary();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearch(SearchTextBox.Text);
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

        private void ApplySearch(string searchText)
        {
            _roots.ForEach(r => ApplySearchNode(r, searchText));
        }

        private static bool ApplySearchNode(ScheduleTreeNode node, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                node.VisibleChildren = new ObservableCollection<ScheduleTreeNode>(node.Children);
                return true;
            }

            if (node.IsLeaf)
            {
                bool visible = node.Name.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
                node.VisibleChildren = new ObservableCollection<ScheduleTreeNode>();
                return visible;
            }

            List<ScheduleTreeNode> visibleChildren = node.Children
                .Where(c => ApplySearchNode(c, searchText))
                .ToList();

            node.VisibleChildren = new ObservableCollection<ScheduleTreeNode>(visibleChildren);
            return visibleChildren.Count > 0;
        }

        private void UpdateSummary()
        {
            if (ButtonOK == null)
            {
                return;
            }

            int selectedCount = _flatLeaves.Count(l => l.IsSelected);
            SelectedSchedules = _flatLeaves.Where(l => l.IsSelected).Select(l => l.Schedule).ToList();

            SummaryTextBlock.Text = $"Выбрано спецификаций: {selectedCount}. Ключевых слов: {Keywords.Count}.";
            ButtonOK.IsEnabled = selectedCount > 0;

            bool mergeEnabled = selectedCount > 1;
            MergeSchedulesCheckBox.IsEnabled = mergeEnabled;

            if (!mergeEnabled)
            {
                MergeSchedulesCheckBox.IsChecked = false;
            }

            string mergeNote = MergeSchedulesCheckBox.IsChecked == true ? " Объединение включено." : string.Empty;
            SummaryTextBlock.Text += mergeNote;
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

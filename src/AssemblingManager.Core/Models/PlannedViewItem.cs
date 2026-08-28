using System.ComponentModel;

namespace AssemblingManager.Core.Models
{
    public class PlannedViewItem : INotifyPropertyChanged
    {
        public string AssemblyName { get; set; }
        public string ViewName { get; set; }
        public string ViewTypeDisplayName { get; set; }
        public string ViewKind { get; set; }

        private bool _create;

        public bool Create
        {
            get { return _create; }
            set
            {
                if (_create == value)
                {
                    return;
                }

                _create = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Create)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

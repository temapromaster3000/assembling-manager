using System.ComponentModel;

namespace AssemblingManager.Core.Models
{
    public class ViewConflictItem : INotifyPropertyChanged
    {
        public string AssemblyName { get; set; }
        public string ViewName { get; set; }
        public string ViewTypeDisplayName { get; set; }
        public string ViewKind { get; set; }

        private ConflictAction _action;

        public ConflictAction Action
        {
            get { return _action; }
            set
            {
                if (_action == value)
                {
                    return;
                }

                _action = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Action)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

using System.ComponentModel;

namespace AssemblingManager.Core.Models
{
    public class ViewConflictItem : INotifyPropertyChanged
    {
        public string AssemblyName { get; set; }
        public string ViewName { get; set; }
        public string ViewTypeDisplayName { get; set; }
        public string ViewKind { get; set; }

        private bool _replace;

        public bool Replace
        {
            get { return _replace; }
            set
            {
                if (_replace == value)
                {
                    return;
                }

                _replace = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Replace)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
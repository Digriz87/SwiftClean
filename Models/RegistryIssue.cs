using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>A problematic (leftover) registry key listed on the Registry page.</summary>
    public class RegistryIssue : ViewModelBase
    {
        private bool _isFixed;
        private bool _isSelected;

        public RegistryIssue(string path, string key, string value)
        {
            Path = path;
            Key = key;
            Value = value;
        }

        public string Path { get; }
        public string Key { get; }
        public string Value { get; }

        public string Issue => Loc.Instance["reg.issue"];

        /// <summary>Backing data for the service (the <c>RegLeftover</c>).</summary>
        public object? Tag { get; set; }

        public bool IsFixed
        {
            get => _isFixed;
            set
            {
                if (SetProperty(ref _isFixed, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string StatusText => Loc.Instance[IsFixed ? "reg.statusDeleted" : "reg.statusObsolete"];
    }
}

using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>A step in the scan modal's checklist (pending → active → done).</summary>
    public class ScanStage : ViewModelBase
    {
        private bool _isActive;
        private bool _isDone;

        public ScanStage(string nameKey, string key)
        {
            NameKey = nameKey;
            Key = key;
            Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Name));
        }

        public string NameKey { get; }
        /// <summary>The scanner's (English) category name this stage maps to.</summary>
        public string Key { get; }

        public string Name => Loc.Instance[NameKey];

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                    OnPropertyChanged(nameof(IsPending));
            }
        }

        public bool IsDone
        {
            get => _isDone;
            set
            {
                if (SetProperty(ref _isDone, value))
                    OnPropertyChanged(nameof(IsPending));
            }
        }

        public bool IsPending => !IsActive && !IsDone;
    }
}

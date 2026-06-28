using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>A program that runs at startup, listed on the Startup page.</summary>
    public class StartupApp : ViewModelBase
    {
        private bool _isEnabled;

        public StartupApp(string id, string name, string publisher, string impactKey,
                          string impactColorHex, int startupMs, bool isEnabled)
        {
            Id = id;
            Name = name;
            Publisher = publisher;
            ImpactKey = impactKey;
            ImpactColorHex = impactColorHex;
            StartupMs = startupMs;
            _isEnabled = isEnabled;
            Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Impact));
        }

        public string Id { get; }
        public string Name { get; }
        public string Publisher { get; }
        public string ImpactKey { get; }
        public string ImpactColorHex { get; }
        public int StartupMs { get; }

        public string Impact => Loc.Instance[ImpactKey];

        /// <summary>Backing data for the service (the <c>StartupEntry</c>).</summary>
        public object? Tag { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }
}

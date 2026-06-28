using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>A single entry in the sidebar navigation menu (label/section localized).</summary>
    public class NavigationItem : ViewModelBase
    {
        private bool _isSelected;
        private string? _badge;

        public NavigationItem(string id, string titleKey, string icon, string sectionKey)
        {
            Id = id;
            TitleKey = titleKey;
            Icon = icon;
            SectionKey = sectionKey;
            Loc.Instance.LanguageChanged += () =>
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Section));
            };
        }

        public string Id { get; }
        public string TitleKey { get; }
        public string SectionKey { get; }

        /// <summary>Segoe MDL2 Assets glyph shown before the title.</summary>
        public string Icon { get; }

        public string Title => Loc.Instance[TitleKey];
        public string Section => Loc.Instance[SectionKey];

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string? Badge
        {
            get => _badge;
            set
            {
                if (SetProperty(ref _badge, value))
                    OnPropertyChanged(nameof(HasBadge));
            }
        }

        public bool HasBadge => !string.IsNullOrEmpty(Badge);
    }
}

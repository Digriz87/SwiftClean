using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>A folder shown in the Disk page; size is filled in asynchronously.</summary>
    public class DiskFolder : ViewModelBase
    {
        private string _size = "…";

        public DiskFolder(string nameKey, string path, string iconColorHex)
        {
            NameKey = nameKey;
            Path = path;
            IconColorHex = iconColorHex;
            Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Name));
        }

        public string NameKey { get; }
        public string Path { get; }
        public string IconColorHex { get; }

        public string Name => Loc.Instance[NameKey];

        public string Size
        {
            get => _size;
            set => SetProperty(ref _size, value);
        }
    }
}

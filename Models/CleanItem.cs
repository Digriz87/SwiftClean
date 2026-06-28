using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>A cleanable category row (Dashboard / Cleanup list). Name/desc are localized.</summary>
    public class CleanItem : ViewModelBase
    {
        private static Loc L => Loc.Instance;

        private readonly long _bytes;
        private readonly string _displaySize;
        private bool _isSelected;
        private bool _isExpanded;
        private bool _syncing;

        public CleanItem(string id, string nameKey, string displaySize, long bytes,
                         string icon, string iconColorHex, string iconBgHex, bool isSelected,
                         IReadOnlyList<CleanFile>? files = null, int fileTotal = 0,
                         IEnumerable<BrowserCache>? browsers = null)
        {
            Id = id;
            NameKey = nameKey;
            _displaySize = displaySize;
            _bytes = bytes;
            Icon = icon;
            IconColorHex = iconColorHex;
            IconBgHex = iconBgHex;
            _isSelected = isSelected;
            Files = files ?? new List<CleanFile>();
            FileTotal = fileTotal;
            Browsers = new ObservableCollection<BrowserCache>(browsers ?? Enumerable.Empty<BrowserCache>());

            foreach (var b in Browsers)
                b.PropertyChanged += OnBrowserChanged;
        }

        public string Id { get; }
        public string NameKey { get; }
        public string Icon { get; }
        public string IconColorHex { get; }
        public string IconBgHex { get; }

        public string Name => L[NameKey];

        /// <summary>Sample of files that will be removed (capped during scanning).</summary>
        public IReadOnlyList<CleanFile> Files { get; }
        public int FileTotal { get; }

        /// <summary>Per-browser breakdown (Browser Cache / Cookies categories only).</summary>
        public ObservableCollection<BrowserCache> Browsers { get; }
        public bool HasBrowsers => Browsers.Count > 0;

        public bool HasFiles => Files.Count > 0;
        public bool ShowFiles => HasFiles && !HasBrowsers;
        public bool HasDetails => HasFiles || HasBrowsers;
        public bool HasMore => FileTotal > Files.Count;
        public string MoreText => HasMore
            ? string.Format(CultureInfo.CurrentCulture, L["fmt.more"], FileTotal - Files.Count)
            : string.Empty;

        /// <summary>Size in bytes; for browser categories it tracks the selected browsers.</summary>
        public long Bytes => HasBrowsers ? Browsers.Where(b => b.IsSelected).Sum(b => b.Bytes) : _bytes;

        public string DisplaySize => HasBrowsers ? SizeFormatter.Format(Bytes) : _displaySize;

        public string Desc => HasBrowsers
            ? string.Format(CultureInfo.CurrentCulture, L["fmt.browsers"], Browsers.Count(b => b.IsSelected), Browsers.Count)
            : string.Format(CultureInfo.CurrentCulture, L["fmt.files"], FileTotal);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value) && HasBrowsers && !_syncing)
                {
                    _syncing = true;
                    foreach (var b in Browsers)
                        b.IsSelected = value;
                    _syncing = false;
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private void OnBrowserChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(BrowserCache.IsSelected) or nameof(BrowserCache.Bytes)))
                return;

            if (e.PropertyName == nameof(BrowserCache.IsSelected) && !_syncing)
            {
                _syncing = true;
                IsSelected = Browsers.Any(b => b.IsSelected);
                _syncing = false;
            }

            OnPropertyChanged(nameof(Bytes));
            OnPropertyChanged(nameof(DisplaySize));
            OnPropertyChanged(nameof(Desc));
        }
    }
}

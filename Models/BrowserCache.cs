using System.Collections.Generic;
using SwiftClean.Helpers;
using SwiftClean.ViewModels;

namespace SwiftClean.Models
{
    /// <summary>One installed browser's cache, selectable inside the Browser Cache row.</summary>
    public class BrowserCache : ViewModelBase
    {
        private long _bytes;
        private bool _isSelected;

        public BrowserCache(string name, long bytes, IReadOnlyList<string> paths, bool isSelected = true)
        {
            Name = name;
            _bytes = bytes;
            Paths = paths;
            _isSelected = isSelected;
        }

        public string Name { get; }
        public IReadOnlyList<string> Paths { get; }

        public long Bytes
        {
            get => _bytes;
            set
            {
                if (SetProperty(ref _bytes, value))
                    OnPropertyChanged(nameof(DisplaySize));
            }
        }

        public string DisplaySize => SizeFormatter.Format(Bytes);

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}

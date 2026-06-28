using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftClean.Helpers;
using SwiftClean.Models;
using SwiftClean.Services;
using ScanProgressReport = SwiftClean.Services.ScanProgress;

namespace SwiftClean.ViewModels
{
    /// <summary>
    /// Root view model for <c>MainWindow</c>. Holds all application state — a direct
    /// port of the single-component SwiftClean.dc.html design: navigation, the scan
    /// flow/modal, and the data for every page (Dashboard, Cleaning, Registry,
    /// Startup, Apps, Disk, Scheduler, Settings).
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private static Loc L => Loc.Instance;

        // The scan bar animates smoothly over at least this long (matches the design's
        // "~3 seconds" feel), even when the real filesystem scan finishes sooner.
        private const double ScanMinMs = 2500;

        // Maps a ScannerService category to its Russian label / icon / colors.
        private static readonly Dictionary<string, (string Name, string Icon, string Color, string Bg)> CategoryStyles = new()
        {
            ["Temp Files"]           = ("Временные файлы", "", "#e05c5c", "#1f1414"),
            ["Recycle Bin"]          = ("Корзина",         "", "#e05c5c", "#1f1414"),
            ["Browser Cache"]        = ("Кэш браузеров",   "", "#d4924a", "#1f1a10"),
            ["Browser Cookies"]      = ("Cookies браузеров", "", "#5b7fff", "#101828"),
            ["Windows Update Cache"] = ("Кэш обновлений",  "", "#d4924a", "#1f1a10"),
        };

        private readonly ScannerService _scanner = new();
        private readonly CleanerService _cleaner = new();
        private readonly StartupService _startup = new();
        private readonly AppsService _apps = new();
        private readonly RegistryService _registry = new();
        private readonly DriverService _driverService = new();
        private readonly DispatcherTimer _scanTimer;
        private readonly DispatcherTimer _cpuTimer;
        private readonly Stopwatch _scanWatch = new();
        private readonly Random _rng = new();

        private ScanResult? _scanResult;
        private CancellationTokenSource? _scanCts;
        private bool _isCleaning;
        private string _junkDisplay = "—";

        private string _activePage = "dashboard";
        private bool _isScanning;
        private int _scanProgress;
        private string _scanFile = "Инициализация...";
        private bool _hasScanData;
        private bool _cleanDone;
        private RegistryIssue _selectedReg = null!;
        private string _appsSearch = string.Empty;
        private bool _schedulerEnabled = true;
        private string _schedulerFreq = "weekly";
        private TimeSpan _scheduleTime = new(3, 0, 0);
        private bool _schedCleanTemp = true;
        private bool _schedCleanRecycle = true;
        private bool _schedCleanCache;
        private bool _driversLoaded;
        private bool _driverScanning;
        private int _driverScanProgress;
        private string _driverScanDevice = string.Empty;
        private bool _driverScanDone;
        private bool _wuSearching;
        private bool _notificationsEnabled = true;
        private bool _autostartEnabled;
        private double _cpu = 12;
        private double _ram = 6.2;

        public MainViewModel()
        {
            BuildNavigation();
            BuildCleanItems();
            BuildRegistry();
            BuildStartup();
            BuildApps();
            BuildDisk();

            NavigateCommand = new RelayCommand(p => Navigate(p as NavigationItem));
            ScanCommand = new RelayCommand(_ => StartScan(), _ => !IsScanning);
            ToggleCleanCommand = new RelayCommand(p => ToggleClean(p as CleanItem));
            CleanAllCommand = new RelayCommand(async _ => await CleanSelectedAsync(), _ => CanClean());
            SelectRegCommand = new RelayCommand(p => SelectReg(p as RegistryIssue));
            FixSelectedCommand = new RelayCommand(async _ => await FixSelectedAsync(), _ => NotSelFixed);
            ToggleStartupCommand = new RelayCommand(p => ToggleStartup(p as StartupApp));
            FreqCommand = new RelayCommand(p => SchedulerFreq = (string)p!);
            SaveScheduleCommand = new RelayCommand(_ => SaveSchedule());
            TimeUpCommand = new RelayCommand(_ => StepTime(30));
            TimeDownCommand = new RelayCommand(_ => StepTime(-30));
            ToggleAmCommand = new RelayCommand(_ =>
            {
                if (SchedTimeIsPm)
                    _scheduleTime = _scheduleTime.Subtract(TimeSpan.FromHours(12));
                RaiseTimeProps();
            });
            TogglePmCommand = new RelayCommand(_ =>
            {
                if (SchedTimeIsAm)
                    _scheduleTime = _scheduleTime.Add(TimeSpan.FromHours(12));
                RaiseTimeProps();
            });
            ScanDriversCommand = new RelayCommand(_ => _ = ScanDriversAsync(), _ => !_driverScanning && !_wuSearching);
            UpdateDriverCommand = new RelayCommand(
                p => { if (p is DriverInfo d) _ = UpdateOneDriverAsync(d); },
                p => p is DriverInfo { IsOutdated: true, IsInstalling: false });
            UpdateAllDriversCommand = new RelayCommand(
                _ => _ = UpdateAllDriversAsync(),
                _ => Drivers.Any(d => d.IsOutdated && !d.IsInstalling));
            DialogConfirmCommand = new RelayCommand(_ => ResolveDialog(true));
            DialogCancelCommand = new RelayCommand(_ => ResolveDialog(false));
            UninstallAppCommand = new RelayCommand(async p => await UninstallAppAsync(p as InstalledApp));
            SortAppsCommand = new RelayCommand(p => SortApps(p as string));
            RefreshAppsCommand = new RelayCommand(_ => BuildApps());
            ChangeLanguageCommand = new RelayCommand(p => { L.SetLanguage((string)p!); SaveSettings(); });
            SupportCommand = new RelayCommand(_ => IsSupportVisible = true);
            CloseSupportCommand = new RelayCommand(_ => IsSupportVisible = false);
            DonateCommand = new RelayCommand(_ => OpenDonate());
            L.LanguageChanged += OnLanguageChanged;

            // Restore saved preferences (autostart's source of truth is the registry).
            var saved = SettingsStore.Load();
            _notificationsEnabled = saved.Notifications;
            _autostartEnabled = AutostartManager.IsEnabled();
            _schedulerEnabled = saved.SchedulerEnabled;
            _schedulerFreq = saved.SchedulerFreq;
            _scheduleTime = TimeSpan.TryParse(saved.SchedulerTime, out var t) ? t : new TimeSpan(3, 0, 0);
            _schedCleanTemp = saved.SchedulerCleanTemp;
            _schedCleanRecycle = saved.SchedulerCleanRecycle;
            _schedCleanCache = saved.SchedulerCleanCache;
            if (saved.Language == "ru")
                L.SetLanguage("ru");

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _scanTimer.Tick += OnScanTick;

            _cpuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            _cpuTimer.Tick += OnCpuTick;
            _cpuTimer.Start();
        }

        // ===================== Navigation =====================

        public ObservableCollection<NavigationItem> NavigationItems { get; private set; } = new();
        public ICollectionView NavigationView { get; private set; } = null!;
        public ICommand NavigateCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand SupportCommand { get; }
        public ICommand CloseSupportCommand { get; }
        public ICommand DonateCommand { get; }

        private bool _isSupportVisible;
        /// <summary>Whether the "Support the developer" popup is on screen.</summary>
        public bool IsSupportVisible
        {
            get => _isSupportVisible;
            private set => SetProperty(ref _isSupportVisible, value);
        }

        private void OpenDonate()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://buymeacoffee.com/viacheslavprotsenko") { UseShellExecute = true });
            }
            catch (Exception) { }
            IsSupportVisible = false;
        }

        // Re-evaluates every localized binding and rebuilds the language-bearing lists.
        private void OnLanguageChanged()
        {
            OnPropertyChanged(string.Empty); // refresh all computed VM strings
            NavigationView.Refresh();
            if (_scanResult is not null)
            {
                ApplyScanResult(_scanResult);
                RebuildRegistry();
            }
            if (_diskSnapshot is not null)
                RebuildDiskBreakdown();
            BuildAppTiles();
        }

        private void BuildNavigation()
        {
            NavigationItems = new ObservableCollection<NavigationItem>
            {
                new("dashboard", "nav.dashboard", "", "sec.main")    { IsSelected = true },
                new("cleaning", "nav.cleaning", "", "sec.tools"),
                new("registry", "nav.registry", "", "sec.tools"),
                new("startup", "nav.startup", "", "sec.tools"),
                new("apps", "nav.apps", "", "sec.tools"),
                new("disk", "nav.disk", "", "sec.tools"),
                new("drivers", "nav.drivers", "", "sec.tools"),
                new("scheduler", "nav.scheduler", "", "sec.other"),
                new("settings", "nav.settings", "", "sec.other"),
            };

            NavigationView = CollectionViewSource.GetDefaultView(NavigationItems);
            NavigationView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavigationItem.Section)));
        }

        private void Navigate(NavigationItem? item)
        {
            if (item is null || item.Id == _activePage)
                return;

            foreach (var nav in NavigationItems)
                nav.IsSelected = nav.Id == item.Id;

            _activePage = item.Id;
            OnPropertyChanged(nameof(PageTitle));
            RaisePageFlags();

            if (item.Id == "disk")
            {
                foreach (var p in new[] { nameof(DiskTotalText), nameof(DiskFreeText), nameof(DiskUsedText),
                    nameof(UsedPercent), nameof(UsedPercentText), nameof(FreePercentText), nameof(DriveLabelText),
                    nameof(DiskOccupiedLine), nameof(DiskFreeOfLine) })
                    OnPropertyChanged(p);
                _ = ComputeFolderSizesAsync();
                StartDiskTypeScan();
            }
            else if (item.Id == "drivers" && !_driversLoaded)
            {
                _ = ScanDriversAsync();
            }
        }

        private void RaisePageFlags()
        {
            OnPropertyChanged(nameof(IsDashboard));
            OnPropertyChanged(nameof(IsCleaning));
            OnPropertyChanged(nameof(IsRegistry));
            OnPropertyChanged(nameof(IsStartup));
            OnPropertyChanged(nameof(IsApps));
            OnPropertyChanged(nameof(IsDisk));
            OnPropertyChanged(nameof(IsDrivers));
            OnPropertyChanged(nameof(IsScheduler));
            OnPropertyChanged(nameof(IsSettings));
        }

        public bool IsDashboard => _activePage == "dashboard";
        public bool IsCleaning => _activePage == "cleaning";
        public bool IsRegistry => _activePage == "registry";
        public bool IsStartup => _activePage == "startup";
        public bool IsApps => _activePage == "apps";
        public bool IsDisk => _activePage == "disk";
        public bool IsDrivers => _activePage == "drivers";
        public bool IsScheduler => _activePage == "scheduler";
        public bool IsSettings => _activePage == "settings";

        public string PageTitle => L["title." + _activePage];

        public string DateStr => DateTime.Now.ToString("d MMMM yyyy",
            L.IsRussian ? Ru : CultureInfo.GetCultureInfo("en-US"));

        // ===================== Scan flow / modal =====================

        public ICommand ScanCommand { get; }

        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnPropertyChanged(nameof(NotScanning));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool NotScanning => !IsScanning;

        public int ScanProgress
        {
            get => _scanProgress;
            private set => SetProperty(ref _scanProgress, value);
        }

        public string ScanFile
        {
            get => _scanFile;
            private set => SetProperty(ref _scanFile, value);
        }

        public bool HasScanData
        {
            get => _hasScanData;
            private set
            {
                if (SetProperty(ref _hasScanData, value))
                {
                    OnPropertyChanged(nameof(NoScanData));
                    OnPropertyChanged(nameof(DashJunk));
                    OnPropertyChanged(nameof(DashReg));
                    OnPropertyChanged(nameof(DashStartupDelay));
                    UpdateBadges();
                }
            }
        }

        public bool NoScanData => !HasScanData;

        /// <summary>The scan modal checklist (real stages, updated from scan progress).</summary>
        public ObservableCollection<ScanStage> ScanStages { get; } = new()
        {
            new("stage.temp", "Temp Files"),
            new("stage.recycle", "Recycle Bin"),
            new("stage.cache", "Browser Cache"),
            new("stage.cookies", "Browser Cookies"),
            new("stage.updates", "Windows Update Cache"),
        };

        private void StartScan()
        {
            if (IsScanning)
                return;

            ScanProgress = 0;
            ScanFile = "Инициализация...";
            HasScanData = false;
            CleanDone = false;
            _scanResult = null;
            foreach (var reg in RegItems)
                reg.IsFixed = false;
            foreach (var stage in ScanStages)
            {
                stage.IsActive = false;
                stage.IsDone = false;
            }
            UpdateBadges();

            IsScanning = true;
            _scanWatch.Restart();
            _scanTimer.Start();
            _ = RunScanAsync();
        }

        // Marks the stage for the reported category active and the earlier stages done.
        private void UpdateStages(string category)
        {
            var idx = -1;
            for (var i = 0; i < ScanStages.Count; i++)
                if (ScanStages[i].Key == category) { idx = i; break; }
            if (idx < 0)
                return;

            for (var i = 0; i < ScanStages.Count; i++)
            {
                ScanStages[i].IsDone = i < idx;
                ScanStages[i].IsActive = i == idx;
            }
        }

        // Runs the real filesystem scan in the background, reporting the path being
        // measured. The bar in OnScanTick waits for this to finish before hitting 100%.
        private async Task RunScanAsync()
        {
            _scanCts = new CancellationTokenSource();
            var progress = new Progress<ScanProgressReport>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentPath))
                    ScanFile = p.CurrentPath;
                UpdateStages(p.Category);
            });

            try
            {
                _scanResult = await _scanner.ScanAsync(progress, _scanCts.Token);
            }
            catch (OperationCanceledException)
            {
                _scanResult = new ScanResult(new List<ScanCategory>(), 0, 0, TimeSpan.Zero);
            }
            finally
            {
                _scanCts.Dispose();
                _scanCts = null;
            }
        }

        private void OnScanTick(object? sender, EventArgs e)
        {
            // Animate smoothly toward 100, but hold at 99 until the real scan returns.
            var target = _scanResult is null ? 99.0 : 100.0;
            var animated = _scanWatch.Elapsed.TotalMilliseconds / ScanMinMs * 100;
            ScanProgress = (int)Math.Min(target, animated);

            if (ScanProgress >= 100)
                CompleteScan();
        }

        private void CompleteScan()
        {
            _scanTimer.Stop();
            _scanWatch.Stop();
            ScanProgress = 100;
            foreach (var stage in ScanStages)
            {
                stage.IsActive = false;
                stage.IsDone = true;
            }

            if (_scanResult is not null)
                ApplyScanResult(_scanResult);

            RebuildRegistry();
            IsScanning = false;
            HasScanData = true;
            OnPropertyChanged(nameof(DashReg));
        }

        // Rebuilds the clean list and the dashboard junk total from real scan results.
        private void ApplyScanResult(ScanResult result)
        {
            foreach (var old in CleanItems)
                old.PropertyChanged -= OnCleanItemChanged;
            CleanItems.Clear();

            foreach (var category in result.Categories)
            {
                var style = CategoryStyles.TryGetValue(category.Name, out var s)
                    ? s
                    : (category.Name, "", "#5b7fff", "#101828");

                var files = category.Files
                    .Select(f => new CleanFile(Path.GetFileName(f.Path), f.Path, SizeFormatter.Format(f.Size)))
                    .ToList();

                var browsers = category.Browsers
                    .Select(b => new BrowserCache(b.Name, b.SizeBytes, b.Paths))
                    .ToList();

                var item = new CleanItem(
                    category.Name,
                    CategoryKey(category.Name),
                    SizeFormatter.Format(category.SizeBytes),
                    category.SizeBytes,
                    style.Item2, style.Item3, style.Item4,
                    category.IsSafeToDelete,
                    files, category.FileCount, browsers);

                item.PropertyChanged += OnCleanItemChanged;
                CleanItems.Add(item);
            }

            _junkDisplay = SizeFormatter.Format(result.TotalBytes);
            OnPropertyChanged(nameof(CleanItems));
            OnPropertyChanged(nameof(TotalDisplay));
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(CleanButtonText));
            UpdateBadges();
        }

        private static string CategoryKey(string englishName) => englishName switch
        {
            "Temp Files" => "cat.temp",
            "Recycle Bin" => "cat.recycle",
            "Browser Cache" => "cat.cache",
            "Browser Cookies" => "cat.cookies",
            "Windows Update Cache" => "cat.updates",
            _ => englishName,
        };

        // ===================== Dashboard stat cards =====================

        public string DashJunk => HasScanData ? _junkDisplay : "—";
        public string DashReg => HasScanData ? RegItems.Count(r => !r.IsFixed).ToString() : "—";
        public string DashStartupDelay => HasScanData ? $"+{BootSec} {L["unit.sec"]}" : "—";

        // ===================== Clean items (Dashboard + Cleaning) =====================

        public ObservableCollection<CleanItem> CleanItems { get; private set; } = new();
        public ICommand ToggleCleanCommand { get; }
        public ICommand CleanAllCommand { get; }

        public bool CleanDone
        {
            get => _cleanDone;
            private set
            {
                if (SetProperty(ref _cleanDone, value))
                {
                    OnPropertyChanged(nameof(NotCleanDone));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool NotCleanDone => !CleanDone;

        /// <summary>True while a clean is running (disables the button).</summary>
        public bool IsCleanRunning
        {
            get => _isCleaning;
            private set
            {
                if (SetProperty(ref _isCleaning, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string TotalDisplay
            => SizeFormatter.Format(CleanItems.Where(i => i.IsSelected).Sum(i => i.Bytes));

        private bool CanClean()
            => HasScanData && !CleanDone && !IsCleanRunning && CleanItems.Any(i => i.IsSelected);

        // ===================== In-app confirm dialog =====================

        private TaskCompletionSource<bool>? _dialogTcs;
        private bool _isDialogVisible;
        private string _dialogTitle = string.Empty;
        private string _dialogMessage = string.Empty;
        private string _dialogConfirmText = "Подтвердить";
        private string _dialogCancelText = "Отмена";

        public ICommand DialogConfirmCommand { get; }
        public ICommand DialogCancelCommand { get; }

        public bool IsDialogVisible
        {
            get => _isDialogVisible;
            private set => SetProperty(ref _isDialogVisible, value);
        }

        public string DialogTitle
        {
            get => _dialogTitle;
            private set => SetProperty(ref _dialogTitle, value);
        }

        public string DialogMessage
        {
            get => _dialogMessage;
            private set => SetProperty(ref _dialogMessage, value);
        }

        public string DialogConfirmText
        {
            get => _dialogConfirmText;
            private set => SetProperty(ref _dialogConfirmText, value);
        }

        public string DialogCancelText
        {
            get => _dialogCancelText;
            private set => SetProperty(ref _dialogCancelText, value);
        }

        /// <summary>Shows the in-app confirm dialog and awaits the user's choice.</summary>
        private Task<bool> ShowConfirmAsync(string title, string message, string confirmText, string cancelText)
        {
            DialogTitle = title;
            DialogMessage = message;
            DialogConfirmText = confirmText;
            DialogCancelText = cancelText;
            _dialogTcs = new TaskCompletionSource<bool>();
            IsDialogVisible = true;
            return _dialogTcs.Task;
        }

        private void ResolveDialog(bool result)
        {
            IsDialogVisible = false;
            var tcs = _dialogTcs;
            _dialogTcs = null;
            tcs?.TrySetResult(result);
        }

        private string _cleanedSummary = string.Empty;
        /// <summary>Size freed by the last clean, shown on the "cleaned" plate.</summary>
        public string CleanedSummary
        {
            get => _cleanedSummary;
            private set
            {
                if (SetProperty(ref _cleanedSummary, value))
                    OnPropertyChanged(nameof(CleanedText));
            }
        }

        // Localized composite strings for the clean panels.
        public string SelectedText => string.Format(L["dash.selectedFmt"], TotalDisplay);
        public string CleanButtonText => string.Format(L["dash.cleanFmt"], TotalDisplay);
        public string CleanedText => string.Format(L["clean.cleanedFmt"], CleanedSummary);

        // Confirms (in-app dialog), moves the selected categories' contents to the
        // Recycle Bin, refreshes the list, then offers to open the Recycle Bin.
        private async Task CleanSelectedAsync()
        {
            if (_scanResult is null)
                return;

            // Build the clean targets. For Browser Cache, restrict to the chosen browsers.
            var targets = new List<ScanCategory>();
            foreach (var item in CleanItems.Where(i => i.IsSelected))
            {
                var cat = _scanResult.Categories.FirstOrDefault(c => c.Name == item.Id);
                if (cat is null)
                    continue;

                if (item.HasBrowsers)
                {
                    var paths = item.Browsers.Where(b => b.IsSelected).SelectMany(b => b.Paths).ToList();
                    if (paths.Count == 0)
                        continue; // category checked but no browser chosen
                    targets.Add(cat with { Paths = paths });
                }
                else
                {
                    targets.Add(cat);
                }
            }
            if (targets.Count == 0)
                return;

            var cleanedIds = targets.Select(t => t.Name).ToHashSet();
            var names = string.Join("\n", CleanItems.Where(i => cleanedIds.Contains(i.Id)).Select(i => "•  " + i.Name));
            var summary = SizeFormatter.Format(CleanItems.Where(i => cleanedIds.Contains(i.Id)).Sum(i => i.Bytes));
            var onlyRecycle = cleanedIds.Count == 1 && cleanedIds.Contains("Recycle Bin");

            var message = onlyRecycle
                ? string.Format(L["dlg.recycleFmt"], summary)
                : string.Format(L["dlg.cleanFmt"], names, summary);

            if (!await ShowConfirmAsync(L["dlg.cleanTitle"], message,
                    onlyRecycle ? L["dlg.cleanRecycle"] : L["dlg.clean"], L["dlg.cancel"]))
                return;

            IsCleanRunning = true;
            var cts = new CancellationTokenSource();
            try
            {
                await _cleaner.CleanAsync(targets, null, cts.Token);
            }
            finally
            {
                cts.Dispose();
                IsCleanRunning = false;
            }

            ApplyCleanResult(cleanedIds);
            CleanedSummary = summary;
            CleanDone = true;

            // Cleaning the Recycle Bin empties it — no point offering to open it.
            if (!onlyRecycle && await ShowConfirmAsync(L["dlg.doneTitle"],
                    string.Format(L["dlg.doneFmt"], summary), L["dlg.openRecycle"], L["dlg.close"]))
            {
                OpenRecycleBin();
            }
        }

        // Zeroes out the cleaned categories so the list reflects the freed space.
        private void ApplyCleanResult(HashSet<string> cleanedIds)
        {
            for (var i = 0; i < CleanItems.Count; i++)
            {
                var it = CleanItems[i];
                if (!cleanedIds.Contains(it.Id))
                    continue;

                if (it.HasBrowsers)
                {
                    // Only the cleaned (selected) browsers are emptied; others keep their size.
                    foreach (var b in it.Browsers.Where(b => b.IsSelected))
                        b.Bytes = 0;
                    continue;
                }

                it.PropertyChanged -= OnCleanItemChanged;
                var empty = new CleanItem(it.Id, it.NameKey, "0 B", 0,
                    it.Icon, it.IconColorHex, it.IconBgHex, isSelected: false);
                empty.PropertyChanged += OnCleanItemChanged;
                CleanItems[i] = empty;
            }

            _junkDisplay = SizeFormatter.Format(CleanItems.Sum(i => i.Bytes));
            OnPropertyChanged(nameof(DashJunk));
            OnPropertyChanged(nameof(TotalDisplay));
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(CleanButtonText));
            UpdateBadges();
        }

        private static void OpenRecycleBin()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
            }
            catch (Exception) { /* best-effort */ }
        }

        private void BuildCleanItems() => CleanItems = new ObservableCollection<CleanItem>();

        private bool _collapsingOthers;

        private void OnCleanItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CleanItem.IsSelected) or nameof(CleanItem.Bytes))
            {
                OnPropertyChanged(nameof(TotalDisplay));
                OnPropertyChanged(nameof(SelectedText));
                OnPropertyChanged(nameof(CleanButtonText));
                UpdateBadges();
                // Changing the selection after a clean brings the action button back.
                if (CleanDone)
                    CleanDone = false;
            }
            else if (e.PropertyName == nameof(CleanItem.IsExpanded) && sender is CleanItem item && item.IsExpanded)
            {
                // Accordion: opening one row collapses the others.
                if (_collapsingOthers)
                    return;
                _collapsingOthers = true;
                foreach (var other in CleanItems)
                    if (!ReferenceEquals(other, item))
                        other.IsExpanded = false;
                _collapsingOthers = false;
            }
        }

        private void ToggleClean(CleanItem? item)
        {
            if (item is not null)
                item.IsSelected = !item.IsSelected;
        }

        // ===================== Registry =====================

        public ObservableCollection<RegistryIssue> RegItems { get; private set; } = new();
        public ICommand SelectRegCommand { get; }
        public ICommand FixSelectedCommand { get; }

        public RegistryIssue SelectedReg
        {
            get => _selectedReg;
            private set
            {
                if (SetProperty(ref _selectedReg, value))
                {
                    OnPropertyChanged(nameof(RegSelPath));
                    OnPropertyChanged(nameof(RegSelKey));
                    OnPropertyChanged(nameof(RegSelValue));
                    OnPropertyChanged(nameof(RegSelStatus));
                    OnPropertyChanged(nameof(IsSelFixed));
                    OnPropertyChanged(nameof(NotSelFixed));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string RegSelPath => SelectedReg.Path;
        public string RegSelKey => SelectedReg.Key;
        public string RegSelValue => SelectedReg.Value;
        public string RegSelStatus => SelectedReg.IsFixed ? L["reg.removed"] : SelectedReg.Issue;
        public bool IsSelFixed => SelectedReg.IsFixed;
        public bool NotSelFixed => SelectedReg.Tag is not null && !SelectedReg.IsFixed;

        public bool HasRegIssues => RegItems.Count > 0;
        public bool RegClean => HasScanData && RegItems.Count == 0;
        public string RegFoundText => string.Format(L["reg.foundFmt"], RegItems.Count);

        // Empty until a scan populates it (see ApplyScanResult).
        private void BuildRegistry()
        {
            RegItems = new ObservableCollection<RegistryIssue>();
            _selectedReg = new RegistryIssue(string.Empty, string.Empty, string.Empty);
        }

        // Rebuilds the registry-leftovers list from the real scan.
        private void RebuildRegistry()
        {
            RegItems = new ObservableCollection<RegistryIssue>(
                _registry.ScanOrphans().Select(o => new RegistryIssue(
                    o.RegistryPath, o.DisplayName, o.MissingPath) { Tag = o }));

            OnPropertyChanged(nameof(RegItems));
            OnPropertyChanged(nameof(HasRegIssues));
            OnPropertyChanged(nameof(RegClean));
            OnPropertyChanged(nameof(RegFoundText));

            _selectedReg = RegItems.Count > 0 ? RegItems[0] : new RegistryIssue("", "", "");
            if (RegItems.Count > 0)
                _selectedReg.IsSelected = true;
            SelectedReg = _selectedReg;
        }

        private void SelectReg(RegistryIssue? item)
        {
            if (item is null || ReferenceEquals(item, SelectedReg))
                return;

            SelectedReg.IsSelected = false;
            item.IsSelected = true;
            SelectedReg = item;
        }

        // Confirms, then deletes the leftover registry key for real.
        private async Task FixSelectedAsync()
        {
            if (SelectedReg.Tag is not RegLeftover leftover || SelectedReg.IsFixed)
                return;

            if (!await ShowConfirmAsync(L["reg.confirmTitle"],
                    string.Format(L["reg.confirmFmt"], SelectedReg.Key, SelectedReg.Path),
                    L["reg.deleteBtn"], L["dlg.cancel"]))
                return;

            if (!_registry.Delete(leftover))
            {
                _ = ShowConfirmAsync(L["reg.adminTitle"], L["reg.adminMsg"], L["btn.understand"], L["dlg.close"]);
                return;
            }

            SelectedReg.IsFixed = true;
            OnPropertyChanged(nameof(IsSelFixed));
            OnPropertyChanged(nameof(NotSelFixed));
            OnPropertyChanged(nameof(RegSelStatus));
            OnPropertyChanged(nameof(DashReg));
            UpdateBadges();
            CommandManager.InvalidateRequerySuggested();
        }

        // ===================== Startup =====================

        public ObservableCollection<StartupApp> StartupItems { get; private set; } = new();
        public ICommand ToggleStartupCommand { get; }

        public int EnabledCount => StartupItems.Count(i => i.IsEnabled);
        public int StartupTotal => StartupItems.Count;
        public int HighImpactCount => StartupItems.Count(i => i.ImpactKey == "impact.high");
        public string EnabledSummary => string.Format(L["st.enabledFmt"], EnabledCount, StartupTotal);
        public string HighImpactText => string.Format(L["st.highFmt"], HighImpactCount);
        public string BootSec => (StartupItems.Where(i => i.IsEnabled).Sum(i => i.StartupMs) / 1000.0)
            .ToString("0.0", CultureInfo.InvariantCulture);
        public string BootDelayText => $"+{BootSec} {L["unit.sec"]}";

        // Real startup programs from the registry Run keys + Startup folders.
        private void BuildStartup()
        {
            StartupItems = new ObservableCollection<StartupApp>();
            foreach (var entry in _startup.List())
            {
                var (impact, color) = ImpactFor(entry.ExeSizeBytes);
                var publisher = string.IsNullOrWhiteSpace(entry.Company) ? "—" : entry.Company;
                var app = new StartupApp(entry.Source + ":" + entry.Name, entry.Name, publisher,
                    impact, color, EstimateStartupMs(entry.ExeSizeBytes), entry.IsEnabled) { Tag = entry };
                app.PropertyChanged += OnStartupChanged;
                StartupItems.Add(app);
            }
        }

        // Heuristic impact/boot-delay from the executable size (real impact isn't exposed by Windows).
        private static (string ImpactKey, string Color) ImpactFor(long bytes) => bytes switch
        {
            >= 10L * 1024 * 1024 => ("impact.high", "#e05c5c"),
            >= 1L * 1024 * 1024 => ("impact.med", "#d4924a"),
            _ => ("impact.low", "#4ab87d"),
        };

        private static int EstimateStartupMs(long bytes)
            => (int)Math.Clamp(bytes / (1024 * 1024) * 120, 50, 3000);

        private bool _togglingStartup;

        private void OnStartupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(StartupApp.IsEnabled))
                return;

            OnPropertyChanged(nameof(EnabledCount));
            OnPropertyChanged(nameof(EnabledSummary));
            OnPropertyChanged(nameof(BootSec));
            OnPropertyChanged(nameof(BootDelayText));
            OnPropertyChanged(nameof(DashStartupDelay));

            if (_togglingStartup || sender is not StartupApp app || app.Tag is not StartupEntry entry)
                return;

            if (_startup.SetEnabled(entry, app.IsEnabled))
                return;

            // Couldn't write (usually HKLM without admin) — revert and tell the user.
            _togglingStartup = true;
            app.IsEnabled = !app.IsEnabled;
            _togglingStartup = false;
            _ = ShowConfirmAsync(L["st.adminTitle"], L["st.adminMsg"], L["btn.understand"], L["dlg.close"]);
        }

        private void ToggleStartup(StartupApp? item)
        {
            if (item is not null)
                item.IsEnabled = !item.IsEnabled;
        }

        // ===================== Apps =====================

        private List<InstalledApp> _allApps = new();
        private string _sortColumn = "name";
        private bool _sortAsc = true;
        private bool _appsRefreshPending;

        public ObservableCollection<InstalledApp> FilteredApps { get; } = new();
        public ICommand UninstallAppCommand { get; }
        public ICommand SortAppsCommand { get; }
        public ICommand RefreshAppsCommand { get; }

        public string AppsSearch
        {
            get => _appsSearch;
            set
            {
                if (SetProperty(ref _appsSearch, value))
                    RebuildApps();
            }
        }

        // ----- header sort indicators -----
        public bool IsSortName => _sortColumn == "name";
        public bool IsSortSize => _sortColumn == "size";
        public bool IsSortDate => _sortColumn == "date";
        public bool IsSortUsed => _sortColumn == "used";
        public string SortArrow => _sortAsc ? "" : ""; // chevron up / down

        // ----- stats panel -----
        public string AppsTotalSize => SizeFormatter.Format(_allApps.Sum(a => a.SizeBytes));
        public string AppsCountText => string.Format(L["apps.installedFmt"], _allApps.Count);
        public string FreeSpaceText
        {
            get
            {
                try { return SizeFormatter.Format(new DriveInfo("C").AvailableFreeSpace); }
                catch (Exception) { return "—"; }
            }
        }

        // Real installed programs from the Uninstall registry keys.
        private void BuildApps()
        {
            _allApps = _apps.List()
                .Select(a => new InstalledApp(
                    a.Name,
                    string.IsNullOrWhiteSpace(a.Publisher) ? "—" : a.Publisher,
                    a.SizeBytes > 0 ? SizeFormatter.Format(a.SizeBytes) : "—",
                    a.Installed?.ToString("dd.MM.yyyy", Ru) ?? "—",
                    a.LastUsed?.ToString("dd.MM.yyyy", Ru) ?? "—",
                    a.UninstallCommand,
                    a.SizeBytes, a.Installed, a.LastUsed))
                .ToList();
            RebuildApps();
            BuildAppTiles();
            OnPropertyChanged(nameof(AppsTotalSize));
            OnPropertyChanged(nameof(AppsCountText));
            OnPropertyChanged(nameof(FreeSpaceText));
        }

        // Applies the search filter + current sort into FilteredApps.
        private void RebuildApps()
        {
            var q = _allApps.Where(a =>
                a.Name.Contains(AppsSearch, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(AppsSearch, StringComparison.OrdinalIgnoreCase));

            q = _sortColumn switch
            {
                "size" => _sortAsc ? q.OrderBy(a => a.SizeBytes) : q.OrderByDescending(a => a.SizeBytes),
                "date" => _sortAsc ? q.OrderBy(a => a.InstalledDate ?? DateTime.MinValue)
                                   : q.OrderByDescending(a => a.InstalledDate ?? DateTime.MinValue),
                "used" => _sortAsc ? q.OrderBy(a => a.LastUsedDate ?? DateTime.MinValue)
                                   : q.OrderByDescending(a => a.LastUsedDate ?? DateTime.MinValue),
                _ => _sortAsc ? q.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                              : q.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            };

            FilteredApps.Clear();
            foreach (var app in q)
                FilteredApps.Add(app);
        }

        private void SortApps(string? column)
        {
            if (string.IsNullOrEmpty(column))
                return;
            if (_sortColumn == column)
                _sortAsc = !_sortAsc;
            else { _sortColumn = column; _sortAsc = true; }

            RebuildApps();
            OnPropertyChanged(nameof(IsSortName));
            OnPropertyChanged(nameof(IsSortSize));
            OnPropertyChanged(nameof(IsSortDate));
            OnPropertyChanged(nameof(IsSortUsed));
            OnPropertyChanged(nameof(SortArrow));
        }

        // Confirms, launches the program's own uninstaller, then refreshes the list/stats.
        private async Task UninstallAppAsync(InstalledApp? app)
        {
            if (app is null || string.IsNullOrWhiteSpace(app.UninstallCommand))
                return;

            if (!await ShowConfirmAsync(L["apps.uninstallTitle"],
                    string.Format(L["apps.uninstallFmt"], app.Name), L["apps.removeBtn"], L["apps.cancel"]))
                return;

            var (exe, args) = SplitCommand(app.UninstallCommand);
            try
            {
                // UseShellExecute lets the uninstaller elevate (UAC) and handle its own UI.
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true,
                });

                // Uninstallers often hand off to another (elevated) process, so waiting isn't
                // reliable — refresh both now and again when the user returns to the app.
                _appsRefreshPending = true;
                if (proc is not null)
                {
                    await proc.WaitForExitAsync();
                    BuildApps();
                }
            }
            catch (Exception)
            {
                _ = ShowConfirmAsync(L["apps.errTitle"], L["apps.errMsg"], L["btn.understand"], L["dlg.close"]);
            }
        }

        /// <summary>Called when the window regains focus — refreshes the app list after an uninstall.</summary>
        public void OnWindowActivated()
        {
            if (!_appsRefreshPending)
                return;
            _appsRefreshPending = false;
            BuildApps();
        }

        // Splits an uninstall string into executable + arguments.
        private static (string Exe, string Args) SplitCommand(string command)
        {
            var cmd = command.Trim();
            if (cmd.StartsWith('"'))
            {
                var end = cmd.IndexOf('"', 1);
                if (end > 0)
                    return (cmd[1..end], end + 1 < cmd.Length ? cmd[(end + 1)..].Trim() : string.Empty);
                return (cmd.Trim('"'), string.Empty);
            }

            var space = cmd.IndexOf(' ');
            return space > 0 ? (cmd[..space], cmd[(space + 1)..].Trim()) : (cmd, string.Empty);
        }

        // ===================== Disk =====================

        public ObservableCollection<DiskSegment> DiskSegments { get; } = new();
        public ObservableCollection<DiskFolder> DiskFolders { get; private set; } = new();
        public ObservableCollection<DiskTile> DiskTiles { get; } = new();

        public ICommand OpenFolderCommand { get; private set; } = null!;
        private bool _diskSizesStarted;
        private bool _diskTypeStarted;
        private DiskTypeSnapshot? _diskSnapshot;

        // Soft bg / accent text / border per file-type category (index-aligned to DiskTypeScanner.Keys).
        private static readonly (string Bg, string Text, string Border)[] TypeColors =
        {
            ("#101828", "#5b7fff", "#243a6e"), // video      (blue)
            ("#0f1f17", "#4ab87d", "#264f3a"), // photo      (green)
            ("#160f3a", "#8b6fff", "#312a6e"), // audio      (purple)
            ("#1f1a10", "#d4924a", "#4f3a1e"), // documents  (amber)
            ("#1f1414", "#e05c5c", "#4f2a2a"), // archives   (red)
            ("#14122e", "#7c6fff", "#2d2650"), // apps       (accent)
            ("#1a1a20", "#8a8a94", "#2a2a35"), // other      (grey)
        };
        private static readonly (string Bg, string Text, string Border) SystemColor = ("#141418", "#6b6b78", "#1e1e22");

        // Tile bg / text / border by how recently an app was used (finviz-style green→red heat).
        private static readonly (string Bg, string Text, string Border) RecencyRecent = ("#13241b", "#5ed99a", "#2f6b4c"); // ≤30d
        private static readonly (string Bg, string Text, string Border) RecencyMonths = ("#241f12", "#e0a85c", "#5f4a26"); // ≤180d
        private static readonly (string Bg, string Text, string Border) RecencyStale  = ("#241414", "#ec6b6b", "#5f2d2d"); // >180d
        private static readonly (string Bg, string Text, string Border) RecencyUnknown = ("#191920", "#8a8a94", "#2a2a35"); // never/unknown

        // How many of the biggest apps to show as tiles (keeps the map readable).
        private const int AppTileCap = 45;
        private int _appTileCount;
        public bool HasAppTiles => _appTileCount > 0;
        public string AppMapCaption => _appTileCount > 0 ? string.Format(L["disk.appMapFmt"], _appTileCount) : "";

        private bool _diskAnalyzing;
        public bool DiskAnalyzing
        {
            get => _diskAnalyzing;
            private set
            {
                if (SetProperty(ref _diskAnalyzing, value))
                    OnPropertyChanged(nameof(DiskNotAnalyzed));
            }
        }

        public bool DiskAnalyzed => _diskSnapshot is not null;
        public bool DiskNotAnalyzed => _diskSnapshot is null && !_diskAnalyzing;

        private string _diskScanStatus = "";
        public string DiskScanStatus
        {
            get => _diskScanStatus;
            private set => SetProperty(ref _diskScanStatus, value);
        }

        // ----- real system-drive figures -----
        private static DriveInfo? SystemDrive()
        {
            try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"); }
            catch (Exception) { return null; }
        }

        public string DiskTotalText => SizeFormatter.Format(SystemDrive()?.TotalSize ?? 0);
        public string DiskFreeText => SizeFormatter.Format(SystemDrive()?.AvailableFreeSpace ?? 0);
        public string DiskUsedText
        {
            get { var d = SystemDrive(); return d is null ? "—" : SizeFormatter.Format(d.TotalSize - d.AvailableFreeSpace); }
        }

        public double UsedPercent
        {
            get
            {
                var d = SystemDrive();
                return d is null || d.TotalSize == 0 ? 0 : (d.TotalSize - d.AvailableFreeSpace) / (double)d.TotalSize * 100;
            }
        }

        public string UsedPercentText => string.Format(L["disk.occupiedPctFmt"], UsedPercent);
        public string FreePercentText => string.Format(L["disk.freePctFmt"], 100 - UsedPercent);
        public string DiskOccupiedLine => string.Format(L["disk.occupiedFmt"], DiskUsedText);
        public string DiskFreeOfLine => string.Format(L["disk.freeOfFmt"], DiskTotalText);
        public string DriveLabelText => $"{L["disk.drive"]} {Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:"}";

        private void BuildDisk()
        {
            OpenFolderCommand = new RelayCommand(p => OpenFolder(p as DiskFolder));

            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            DiskFolders = new ObservableCollection<DiskFolder>();
            AddFolder("folder.videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "#4ab87d");
            AddFolder("folder.pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "#5b7fff");
            AddFolder("folder.music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "#8b6fff");
            AddFolder("folder.documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "#6b6b78");
            AddFolder("folder.downloads", Path.Combine(user, "Downloads"), "#d4924a");
            AddFolder("folder.desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "#38383f");
            AddFolder("Program Files", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "#d4924a");
            AddFolder("Program Files (x86)", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "#d4924a");
        }

        private void AddFolder(string nameKey, string path, string color)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                DiskFolders.Add(new DiskFolder(nameKey, path, color));
        }

        // Lazily measures the folders' sizes the first time the Disk page is opened.
        private async Task ComputeFolderSizesAsync()
        {
            if (_diskSizesStarted)
                return;
            _diskSizesStarted = true;

            foreach (var folder in DiskFolders)
            {
                var (bytes, _) = await ScannerService.GetDirectorySizeAsync(folder.Path, CancellationToken.None);
                folder.Size = SizeFormatter.Format(bytes);
            }
        }

        // Kicks off the one-time disk type analysis (full system-drive walk) on first Disk visit.
        private void StartDiskTypeScan()
        {
            if (_diskTypeStarted)
                return;
            _diskTypeStarted = true;

            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            DiskAnalyzing = true;
            DiskScanStatus = L["disk.analyzing"];

            var progress = new Progress<DiskTypeSnapshot>(OnDiskTypeProgress);
            _ = DiskTypeScanner.ScanAsync(root, progress, CancellationToken.None);
        }

        // Marshaled to the UI thread by the Progress<T>; refreshes the treemap and segments live.
        private void OnDiskTypeProgress(DiskTypeSnapshot snap)
        {
            _diskSnapshot = snap;
            RebuildDiskBreakdown();

            if (snap.Done)
            {
                DiskAnalyzing = false;
                DiskScanStatus = "";
                OnPropertyChanged(nameof(DiskAnalyzed));
                OnPropertyChanged(nameof(DiskNotAnalyzed));
            }
            else
            {
                DiskScanStatus = string.Format(L["disk.analyzingFmt"], SizeFormatter.Format(snap.ScannedBytes));
            }
        }

        // Builds the treemap tiles and Dashboard segments from the latest snapshot + live drive figures.
        private void RebuildDiskBreakdown()
        {
            if (_diskSnapshot is null)
                return;

            var drive = SystemDrive();
            long total = drive?.TotalSize ?? 0;
            long free = drive?.AvailableFreeSpace ?? 0;
            long used = total - free;

            var buckets = _diskSnapshot.Buckets;
            long typed = buckets.Sum(b => b.Bytes);
            long systemBytes = Math.Max(0, used - typed); // protected/system files we couldn't classify

            // ----- Dashboard segments (% of total disk, used categories only, top 6 desc) -----
            var segs = new List<DiskSegment>();
            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Bytes <= 0 || total <= 0)
                    continue;
                segs.Add(new DiskSegment(L["disktype." + buckets[i].Key],
                    Math.Round(buckets[i].Bytes / (double)total * 100), TypeColors[i].Text));
            }
            if (systemBytes > 0 && total > 0)
                segs.Add(new DiskSegment(L["disktype.system"], Math.Round(systemBytes / (double)total * 100), SystemColor.Text));

            DiskSegments.Clear();
            foreach (var s in segs.OrderByDescending(s => s.Percent).Take(6))
                DiskSegments.Add(s);
        }

        // Builds the finviz-style application treemap: one tile per installed program with a known
        // size, area ∝ size on disk, colored by how recently it was used (green→amber→red→grey).
        public bool NoAppTiles => _appTileCount == 0;

        private void BuildAppTiles()
        {
            var apps = _allApps
                .Where(a => a.SizeBytes > 0)
                .OrderByDescending(a => a.SizeBytes)
                .Take(AppTileCap)
                .ToList();

            DiskTiles.Clear();
            foreach (var a in apps)
            {
                var (color, caption) = Recency(a.LastUsedDate);
                DiskTiles.Add(new DiskTile(a.Name, a.SizeBytes, caption, color.Bg, color.Text, color.Border));
            }

            _appTileCount = apps.Count;
            OnPropertyChanged(nameof(HasAppTiles));
            OnPropertyChanged(nameof(NoAppTiles));
            OnPropertyChanged(nameof(AppMapCaption));
        }

        // Maps an app's last-used date to a heat color + a short "used …" caption.
        private static ((string Bg, string Text, string Border) Color, string Caption) Recency(DateTime? lastUsed)
        {
            if (lastUsed is null)
                return (RecencyUnknown, L["disk.usedNever"]);

            int days = (int)Math.Max(0, (DateTime.Now - lastUsed.Value).TotalDays);
            if (days == 0) return (RecencyRecent, L["disk.usedToday"]);
            if (days <= 30) return (RecencyRecent, string.Format(L["disk.usedDaysFmt"], days));
            int months = Math.Max(1, days / 30);
            if (days <= 180) return (RecencyMonths, string.Format(L["disk.usedMonthsFmt"], months));
            return (RecencyStale, string.Format(L["disk.usedMonthsFmt"], months));
        }

        private static void OpenFolder(DiskFolder? folder)
        {
            if (folder is null || !Directory.Exists(folder.Path))
                return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.Path}\"") { UseShellExecute = true });
            }
            catch (Exception) { /* best-effort */ }
        }

        // ===================== Drivers =====================

        public ObservableCollection<DriverInfo> Drivers { get; } = new();

        public ICommand ScanDriversCommand { get; }
        public ICommand UpdateDriverCommand { get; }
        public ICommand UpdateAllDriversCommand { get; }

        public bool DriverScanning
        {
            get => _driverScanning;
            private set
            {
                if (SetProperty(ref _driverScanning, value))
                {
                    OnPropertyChanged(nameof(DriverShowEmpty));
                    OnPropertyChanged(nameof(DriverBusy));
                    OnPropertyChanged(nameof(DriverBusyText));
                    OnPropertyChanged(nameof(DriverCheckComplete));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Empty-state placeholder: shown before the first scan and when nothing was found.</summary>
        public bool DriverShowEmpty => !HasDrivers && !DriverScanning && !WuSearching;

        /// <summary>True while either enumerating devices or checking them against Windows Update.</summary>
        public bool DriverBusy => DriverScanning || WuSearching;

        /// <summary>Status line for the progress panel — reflects which phase is running.</summary>
        public string DriverBusyText => WuSearching ? L["drv.wuSearching"] : L["drv.scanning"];

        /// <summary>The "scan complete" footer shows only after both phases finish.</summary>
        public bool DriverCheckComplete => DriverScanDone && !DriverBusy;

        public int DriverScanProgress
        {
            get => _driverScanProgress;
            private set => SetProperty(ref _driverScanProgress, value);
        }

        public string DriverScanDevice
        {
            get => _driverScanDevice;
            private set { if (SetProperty(ref _driverScanDevice, value)) OnPropertyChanged(nameof(DriverCheckingText)); }
        }

        /// <summary>True once a scan has completed at least once (drives the "scan complete" footer).</summary>
        public bool DriverScanDone
        {
            get => _driverScanDone;
            private set => SetProperty(ref _driverScanDone, value);
        }

        public bool HasDrivers => Drivers.Count > 0;
        public int DriverTotalCount => Drivers.Count;
        // Counts reflect real per-device WU results — 0/0 until the user runs a Windows Update search.
        public int DriverOutdatedCount => Drivers.Count(d => d.IsOutdated);
        public int DriverUptodateCount => Drivers.Count(d => d.IsUpToDate);
        public string DriverTotalText => string.Format(L["drv.totalFmt"], Drivers.Count);
        public string DriverCheckingText => string.Format(L["drv.checkingFmt"], DriverScanDevice);

        private async Task ScanDriversAsync()
        {
            if (DriverScanning)
                return;

            _driversLoaded = true;
            DriverScanning = true;
            DriverScanDone = false;
            DriverScanProgress = 0;
            Drivers.Clear();
            RaiseDriverCounts();

            var progress = new Progress<DriverScanProgress>(p =>
            {
                DriverScanProgress = p.Percent;
                DriverScanDevice = p.Device;
            });

            try
            {
                var found = await _driverService.ScanAsync(progress);
                Drivers.Clear();
                foreach (var d in found)
                    Drivers.Add(d);
            }
            catch (Exception)
            {
                // Best-effort: an empty list shows the empty state.
            }
            finally
            {
                DriverScanning = false;
                DriverScanDone = true;
                RaiseDriverCounts();
                UpdateBadges();
            }

            // One scan does the full job: after listing devices, check them against Windows Update
            // so each row resolves to "up to date" / "update available" instead of staying "not checked".
            if (Drivers.Count > 0)
                await WuSearchAsync();
        }

        private void RaiseDriverCounts()
        {
            OnPropertyChanged(nameof(HasDrivers));
            OnPropertyChanged(nameof(DriverTotalCount));
            OnPropertyChanged(nameof(DriverOutdatedCount));
            OnPropertyChanged(nameof(DriverUptodateCount));
            OnPropertyChanged(nameof(DriverTotalText));
            OnPropertyChanged(nameof(DriverShowEmpty));
        }

        // ── Windows Update driver search ──────────────────────────────────────

        public ObservableCollection<WuDriverUpdate> WuUpdates { get; } = new();

        public bool WuSearching
        {
            get => _wuSearching;
            private set
            {
                if (SetProperty(ref _wuSearching, value))
                {
                    OnPropertyChanged(nameof(DriverShowEmpty));
                    OnPropertyChanged(nameof(DriverBusy));
                    OnPropertyChanged(nameof(DriverBusyText));
                    OnPropertyChanged(nameof(DriverCheckComplete));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private async Task WuSearchAsync()
        {
            if (_wuSearching) return;
            WuSearching = true;
            WuUpdates.Clear();
            // Re-arm every device to "not checked" for the duration of the search.
            foreach (var d in Drivers) d.SetWuResult(null);

            try
            {
                var found = await DriverService.WuSearchAsync();
                foreach (var u in found) WuUpdates.Add(u);
                // Mark each device: update available if WU returned a matching driver, else up to date.
                foreach (var d in Drivers) d.SetWuResult(WuUpdates.Any(d.MatchesWu));
            }
            catch
            {
                // WU unavailable — leave devices "not checked" rather than claiming a result.
            }
            finally
            {
                WuSearching = false;
                RaiseDriverCounts();
                UpdateBadges();
            }
        }

        /// <summary>Installs the WU update matching a single device, with the row showing a spinner.</summary>
        private async Task UpdateOneDriverAsync(DriverInfo driver)
        {
            if (driver.IsInstalling) return;
            var match = WuUpdates.FirstOrDefault(driver.MatchesWu);
            if (match == null) return;

            driver.IsInstalling = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                if (await DriverService.WuInstallAsync(match.Title))
                {
                    driver.SetWuResult(false);     // now up to date
                    WuUpdates.Remove(match);
                }
            }
            finally
            {
                driver.IsInstalling = false;
                RaiseDriverCounts();
                UpdateBadges();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Installs every pending driver update in one elevated pass; all flagged rows spin.</summary>
        private async Task UpdateAllDriversAsync()
        {
            var targets = Drivers.Where(d => d.IsOutdated && !d.IsInstalling).ToList();
            if (targets.Count == 0) return;

            foreach (var d in targets) d.IsInstalling = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                if (await DriverService.WuInstallAsync(null))
                {
                    foreach (var d in targets) d.SetWuResult(false);
                    WuUpdates.Clear();
                }
            }
            finally
            {
                foreach (var d in targets) d.IsInstalling = false;
                RaiseDriverCounts();
                UpdateBadges();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // ===================== Scheduler =====================

        public ICommand FreqCommand { get; }
        public ICommand SaveScheduleCommand { get; }
        public ICommand TimeUpCommand { get; }
        public ICommand TimeDownCommand { get; }
        public ICommand ToggleAmCommand { get; }
        public ICommand TogglePmCommand { get; }

        // Creates/removes the Windows scheduled task and persists the choice.
        private void SaveSchedule()
        {
            // schtasks /ST always expects 24-hour HH:MM regardless of display language.
            var time24 = $"{_scheduleTime.Hours:D2}:{_scheduleTime.Minutes:D2}";
            var ok = SchedulerEnabled
                ? SchedulerService.Create(SchedulerFreq, time24)
                : SchedulerService.Remove();
            SaveSettings();

            _ = ok
                ? ShowConfirmAsync(L["sch.savedTitle"], L["sch.savedMsg"], L["btn.understand"], L["dlg.close"])
                : ShowConfirmAsync(L["sch.errTitle"], L["sch.errMsg"], L["btn.understand"], L["dlg.close"]);
        }

        public bool SchedulerEnabled
        {
            get => _schedulerEnabled;
            set => SetProperty(ref _schedulerEnabled, value);
        }

        public string SchedulerFreq
        {
            get => _schedulerFreq;
            set
            {
                if (SetProperty(ref _schedulerFreq, value))
                {
                    OnPropertyChanged(nameof(IsFreqDaily));
                    OnPropertyChanged(nameof(IsFreqWeekly));
                    OnPropertyChanged(nameof(IsFreqMonthly));
                }
            }
        }

        public bool IsFreqDaily => SchedulerFreq == "daily";
        public bool IsFreqWeekly => SchedulerFreq == "weekly";
        public bool IsFreqMonthly => SchedulerFreq == "monthly";

        public bool SchedTimeIsAm => _scheduleTime.Hours < 12;
        public bool SchedTimeIsPm => _scheduleTime.Hours >= 12;

        private void RaiseTimeProps()
        {
            OnPropertyChanged(nameof(ScheduleTimeText));
            OnPropertyChanged(nameof(SchedTimeIsAm));
            OnPropertyChanged(nameof(SchedTimeIsPm));
        }

        /// <summary>
        /// EN: 12-hour "h:mm" (no suffix — AM/PM chosen via toggle buttons).
        /// RU: 24-hour "HH:mm". Setter accepts both; invalid input keeps the current value.
        /// </summary>
        public string ScheduleTimeText
        {
            get
            {
                if (Loc.Instance.IsEnglish)
                {
                    var h = _scheduleTime.Hours % 12;
                    if (h == 0) h = 12;
                    return $"{h}:{_scheduleTime.Minutes:D2}";
                }
                return $"{_scheduleTime.Hours:D2}:{_scheduleTime.Minutes:D2}";
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { RaiseTimeProps(); return; }
                var input = value.Trim();
                if (TimeSpan.TryParseExact(input, new[] { @"h\:mm", @"hh\:mm" },
                        CultureInfo.InvariantCulture, out var ts))
                {
                    if (Loc.Instance.IsEnglish && ts.Hours <= 12)
                    {
                        var h = ts.Hours % 12;
                        if (SchedTimeIsPm) h += 12;
                        _scheduleTime = new TimeSpan(h, ts.Minutes, 0);
                    }
                    else
                    {
                        _scheduleTime = new TimeSpan(ts.Hours, ts.Minutes, 0);
                    }
                }
                else if (DateTime.TryParse(input, CultureInfo.CurrentCulture,
                             DateTimeStyles.NoCurrentDateDefault, out var dt))
                {
                    _scheduleTime = new TimeSpan(dt.Hour, dt.Minute, 0);
                }
                RaiseTimeProps();
            }
        }

        private void StepTime(int minutes)
        {
            var total = ((int)_scheduleTime.TotalMinutes + minutes + 1440) % 1440;
            _scheduleTime = TimeSpan.FromMinutes(total);
            RaiseTimeProps();
        }

        // "What to clean" — the categories the scheduled auto-clean removes.
        public bool SchedCleanTemp { get => _schedCleanTemp; set => SetProperty(ref _schedCleanTemp, value); }
        public bool SchedCleanRecycle { get => _schedCleanRecycle; set => SetProperty(ref _schedCleanRecycle, value); }
        public bool SchedCleanCache { get => _schedCleanCache; set => SetProperty(ref _schedCleanCache, value); }

        // ===================== Settings =====================

        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set
            {
                if (SetProperty(ref _notificationsEnabled, value))
                    SaveSettings();
            }
        }

        public bool AutostartEnabled
        {
            get => _autostartEnabled;
            set
            {
                if (SetProperty(ref _autostartEnabled, value))
                {
                    AutostartManager.Set(value); // write/remove the HKCU Run entry
                    SaveSettings();
                }
            }
        }

        private void SaveSettings() => SettingsStore.Save(new AppSettings
        {
            Language = L.Language,
            Notifications = _notificationsEnabled,
            Autostart = _autostartEnabled,
            SchedulerEnabled = _schedulerEnabled,
            SchedulerFreq = _schedulerFreq,
            SchedulerTime = $"{_scheduleTime.Hours:D2}:{_scheduleTime.Minutes:D2}",
            SchedulerCleanTemp = _schedCleanTemp,
            SchedulerCleanRecycle = _schedCleanRecycle,
            SchedulerCleanCache = _schedCleanCache,
        });

        // ===================== Status bar (CPU / RAM) =====================

        public string CpuVal => $"{Math.Round(_cpu)}%";
        public string RamVal => _ram.ToString("0.0", CultureInfo.InvariantCulture) + " GB";

        private void OnCpuTick(object? sender, EventArgs e)
        {
            _cpu = Math.Max(4, Math.Min(42, _cpu + (_rng.NextDouble() * 6 - 3)));
            _ram = Math.Max(5, Math.Min(12, _ram + (_rng.NextDouble() * 0.4 - 0.2)));
            OnPropertyChanged(nameof(CpuVal));
            OnPropertyChanged(nameof(RamVal));
        }

        // ===================== Badges =====================

        private void UpdateBadges()
        {
            var cleaning = NavigationItems.FirstOrDefault(n => n.Id == "cleaning");
            var registry = NavigationItems.FirstOrDefault(n => n.Id == "registry");
            var drivers = NavigationItems.FirstOrDefault(n => n.Id == "drivers");

            if (cleaning is not null)
                cleaning.Badge = HasScanData ? CleanItems.Count(i => i.IsSelected).ToString() : null;

            if (drivers is not null)
            {
                var outdated = Drivers.Count(d => d.IsOutdated);
                drivers.Badge = outdated > 0 ? outdated.ToString() : null;
            }

            if (registry is not null)
            {
                var remaining = RegItems.Count(r => !r.IsFixed);
                registry.Badge = HasScanData && remaining > 0 ? remaining.ToString() : null;
            }
        }
    }
}

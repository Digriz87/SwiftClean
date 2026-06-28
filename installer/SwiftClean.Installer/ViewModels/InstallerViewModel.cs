using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using SwiftClean.Installer.Helpers;
using SwiftClean.Installer.Models;
using SwiftClean.Installer.Services;

namespace SwiftClean.Installer.ViewModels;

/// <summary>
/// Drives the whole installer window: the install wizard (welcome → license → path →
/// installing → done) and the standalone uninstall flow. Mirrors the imported
/// <c>SwiftClean Installer.dc.html</c> design.
/// </summary>
public sealed class InstallerViewModel : ObservableObject
{
    // Install wizard pages.
    private const int Welcome = 0, License = 1, PathPage = 2, Installing = 3, Done = 4;
    private const double RingCirc = 2 * Math.PI * 34;

    private readonly InstallerService _service = new();
    private CancellationTokenSource? _cts;

    /// <summary>Raised when the window should close. <c>true</c> = launch the app afterwards.</summary>
    public event Action<bool>? RequestClose;

    public InstallerViewModel(bool uninstallMode = false, bool silent = false)
    {
        IsUninstallMode = uninstallMode;

        NextCommand = new RelayCommand(_ => Next(), _ => NextEnabled);
        BackCommand = new RelayCommand(_ => Back(), _ => ShowBack);
        CancelCommand = new RelayCommand(_ => Cancel());
        BrowseCommand = new RelayCommand(_ => Browse());
        UninstallCommand = new RelayCommand(_ => _ = StartUninstallAsync());

        Steps = new ObservableCollection<WizardStep>
        {
            new("Welcome",   "Program overview",     false),
            new("License",   "Terms of use",         false),
            new("Location",  "Installation folder",  false),
            new("Install",   "Copying files",        false),
            new("Done",      "Installation complete", true),
        };

        UninstallSteps = new ObservableCollection<WizardStep>
        {
            new("Confirm",   "Review before removing", false),
            new("Uninstall", "Removing files",         false),
            new("Done",      "Uninstall complete",     true),
        };

        // In uninstall mode point at the real install location (falls back to the default).
        TargetDir = (uninstallMode ? InstallerService.InstalledLocation() : null)
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                InstallerService.AppName);

        RefreshDiskInfo();
        RequiredText = ComputeRequiredText();
        FileCountText = ComputeFileCount().ToString("N0", CultureInfo.CurrentCulture);
        RefreshSteps();

        BuildRemoveItems();
        RefreshUSteps();

        if (uninstallMode && silent)
            _ = StartUninstallAsync();
    }

    // ───────────────────────── Wizard state ─────────────────────────

    private int _page = Welcome;
    public int Page
    {
        get => _page;
        private set
        {
            if (!SetProperty(ref _page, value)) return;
            OnPropertyChanged(nameof(IsWelcome));
            OnPropertyChanged(nameof(IsLicense));
            OnPropertyChanged(nameof(IsPath));
            OnPropertyChanged(nameof(IsInstalling));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(ShowBack));
            OnPropertyChanged(nameof(ShowCancel));
            OnPropertyChanged(nameof(NextLabel));
            OnPropertyChanged(nameof(NextEnabled));
            OnPropertyChanged(nameof(ShowNextArrow));
            OnPropertyChanged(nameof(ShowSpinner));
            RefreshSteps();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsWelcome => Page == Welcome;
    public bool IsLicense => Page == License;
    public bool IsPath => Page == PathPage;
    public bool IsInstalling => Page == Installing;
    public bool IsDone => Page == Done;

    public bool ShowBack => Page is > Welcome and < Installing;
    public bool ShowCancel => Page < Done && !IsInstalling;
    public bool ShowNextArrow => Page < PathPage;
    public bool ShowSpinner => Page == Installing;

    public string NextLabel => Page switch
    {
        Welcome => "Next",
        License => "Accept",
        PathPage => "Install",
        Installing => "Installing...",
        _ => "Finish",
    };

    public bool NextEnabled => Page switch
    {
        Installing => false,
        License => LicenseAccepted,
        _ => true,
    };

    public ObservableCollection<WizardStep> Steps { get; }

    private void RefreshSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Done = i < Page;
            Steps[i].Active = i == Page;
            Steps[i].LineLit = i < Page;
        }
    }

    // ───────────────────────── Options ─────────────────────────

    private bool _licenseAccepted;
    public bool LicenseAccepted
    {
        get => _licenseAccepted;
        set
        {
            if (SetProperty(ref _licenseAccepted, value))
            {
                OnPropertyChanged(nameof(NextEnabled));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _targetDir = "";
    public string TargetDir { get => _targetDir; set => SetProperty(ref _targetDir, value); }

    private bool _desktopShortcut = true;
    public bool DesktopShortcut { get => _desktopShortcut; set => SetProperty(ref _desktopShortcut, value); }

    private bool _startMenuShortcut = true;
    public bool StartMenuShortcut { get => _startMenuShortcut; set => SetProperty(ref _startMenuShortcut, value); }

    private bool _launchAfter = true;
    public bool LaunchAfter { get => _launchAfter; set => SetProperty(ref _launchAfter, value); }

    // Disk info shown on the path page.
    public string RequiredText { get; }

    /// <summary>File count + elapsed time shown on the completion page.</summary>
    public string FileCountText { get; }

    private string _elapsedText = "—";
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }

    private string _freeSpaceText = "";
    public string FreeSpaceText { get => _freeSpaceText; private set => SetProperty(ref _freeSpaceText, value); }

    private string _diskUsageText = "";
    public string DiskUsageText { get => _diskUsageText; private set => SetProperty(ref _diskUsageText, value); }

    private double _diskUsedPercent = 47;
    public double DiskUsedPercent { get => _diskUsedPercent; private set => SetProperty(ref _diskUsedPercent, value); }

    private void RefreshDiskInfo()
    {
        try
        {
            var root = Path.GetPathRoot(TargetDir) ?? "C:\\";
            var d = new DriveInfo(root);
            var total = d.TotalSize;
            var free = d.AvailableFreeSpace;
            var used = total - free;
            FreeSpaceText = Gb(free);
            DiskUsageText = $"{Gb(used)} / {Gb(total)}";
            DiskUsedPercent = total > 0 ? Math.Round(100.0 * used / total) : 0;
        }
        catch
        {
            FreeSpaceText = "—";
            DiskUsageText = "—";
            DiskUsedPercent = 0;
        }
    }

    private static string ComputeRequiredText()
    {
        try
        {
            var bytes = InstallerService.HasPayload ? PayloadUncompressedBytes() : 50L * 1024 * 1024;
            return Mb(bytes);
        }
        catch { return "≈ 150 MB"; }
    }

    private static long PayloadUncompressedBytes()
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("payload.zip");
        if (stream is null) return 0;
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        return zip.Entries.Sum(e => e.Length);
    }

    private static int ComputeFileCount()
    {
        try
        {
            if (!InstallerService.HasPayload) return 0;
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("payload.zip");
            if (stream is null) return 0;
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            return zip.Entries.Count(e => !e.FullName.EndsWith('/'));
        }
        catch { return 0; }
    }

    private static string Gb(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.#} GB";
    private static string Mb(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d / 1024d:0.#} GB"
        : $"{bytes / 1024d / 1024d:0} MB";

    // ───────────────────────── Install progress ─────────────────────────

    private int _installPct;
    public int InstallPct
    {
        get => _installPct;
        private set { if (SetProperty(ref _installPct, value)) OnPropertyChanged(nameof(ProgressArcData)); }
    }

    /// <summary>Path mini-language for the progress ring arc (centre 40,40, r 34, clockwise from top).</summary>
    public string ProgressArcData
    {
        get
        {
            var pct = Math.Clamp(InstallPct, 0, 100);
            if (pct <= 0) return "";
            if (pct >= 100) // full circle as two arcs (a single 360° arc is degenerate)
                return "M40,6 A34,34 0 1 1 39.99,6 Z";
            var theta = pct / 100.0 * 2 * Math.PI;
            var x = 40 + 34 * Math.Sin(theta);
            var y = 40 - 34 * Math.Cos(theta);
            var large = pct > 50 ? 1 : 0;
            return string.Format(CultureInfo.InvariantCulture,
                "M40,6 A34,34 0 {0} 1 {1:0.##},{2:0.##}", large, x, y);
        }
    }

    private string _installPhase = "Preparing...";
    public string InstallPhase { get => _installPhase; private set => SetProperty(ref _installPhase, value); }

    private string _installFile = "";
    public string InstallFile { get => _installFile; private set => SetProperty(ref _installFile, value); }

    public ObservableCollection<InstallLogLine> InstallLog { get; } = new();

    // ───────────────────────── Uninstall state ─────────────────────────

    public bool IsUninstallMode { get; }
    public bool IsInstallMode => !IsUninstallMode;

    /// <summary>Title-bar caption — differs between install and uninstall mode.</summary>
    public string TitleText => IsUninstallMode
        ? $"{InstallerService.AppName} {InstallerService.AppVersion} — Uninstall"
        : $"{InstallerService.AppName} {InstallerService.AppVersion} — Setup";

    private int _uPage; // 0 confirm, 1 removing, 2 done
    public int UPage
    {
        get => _uPage;
        private set
        {
            if (!SetProperty(ref _uPage, value)) return;
            OnPropertyChanged(nameof(IsUConfirm));
            OnPropertyChanged(nameof(IsURemoving));
            OnPropertyChanged(nameof(IsUDone));
            RefreshUSteps();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsUConfirm => UPage == 0;
    public bool IsURemoving => UPage == 1;
    public bool IsUDone => UPage == 2;

    /// <summary>The three uninstall stepper entries (Confirm → Uninstall → Done).</summary>
    public ObservableCollection<WizardStep> UninstallSteps { get; }

    private void RefreshUSteps()
    {
        for (var i = 0; i < UninstallSteps.Count; i++)
        {
            UninstallSteps[i].Done = i < UPage;
            UninstallSteps[i].Active = i == UPage;
            UninstallSteps[i].LineLit = i < UPage;
        }
    }

    /// <summary>Static list shown on the confirm page.</summary>
    public ObservableCollection<RemoveItem> RemoveItems { get; } = new();

    private void BuildRemoveItems()
    {
        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            InstallerService.AppName);

        RemoveItems.Clear();
        RemoveItems.Add(new RemoveItem("", "Program files", $"{TargetDir}\\", RequiredText));
        RemoveItems.Add(new RemoveItem("", "User data", $"{roaming}\\", "settings + cache"));
        RemoveItems.Add(new RemoveItem("", "Registry entries",
            "HKLM\\…\\Uninstall\\SwiftClean", "—"));
        RemoveItems.Add(new RemoveItem("", "Shortcuts", "Desktop · Start Menu", "—"));
    }

    private bool _keepSettings;
    public bool KeepSettings { get => _keepSettings; set => SetProperty(ref _keepSettings, value); }

    // ── Uninstall completion stats (Done page) ──
    private string _uFreedText = "—";
    public string UFreedText { get => _uFreedText; private set => SetProperty(ref _uFreedText, value); }

    private string _uFileCountText = "—";
    public string UFileCountText { get => _uFileCountText; private set => SetProperty(ref _uFileCountText, value); }

    // ───────────────────────── Commands ─────────────────────────

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand UninstallCommand { get; }

    private void Next()
    {
        switch (Page)
        {
            case License when !LicenseAccepted:
                return;
            case PathPage:
                Page = Installing;
                _ = StartInstallAsync();
                return;
            case Installing:
                return;
            case Done:
                RequestClose?.Invoke(LaunchAfter);
                return;
            default:
                Page++;
                break;
        }
    }

    private void Back()
    {
        if (ShowBack) Page--;
    }

    private void Cancel()
    {
        _cts?.Cancel();
        RequestClose?.Invoke(false);
    }

    private void Browse()
    {
        // OpenFolderDialog (WPF, .NET 8) — pick the parent, append the app folder.
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose installation folder",
            InitialDirectory = Directory.Exists(TargetDir) ? TargetDir
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dlg.ShowDialog() == true)
        {
            var picked = dlg.FolderName;
            // If the user picked a parent (not already our folder), nest under it.
            TargetDir = string.Equals(Path.GetFileName(picked), InstallerService.AppName,
                            StringComparison.OrdinalIgnoreCase)
                ? picked
                : Path.Combine(picked, InstallerService.AppName);
            RefreshDiskInfo();
        }
    }

    private async Task StartInstallAsync()
    {
        _cts = new CancellationTokenSource();
        InstallLog.Clear();
        var progress = new Progress<InstallProgress>(OnProgress);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var options = new InstallOptions(TargetDir, DesktopShortcut, StartMenuShortcut);
            await _service.InstallAsync(options, progress, _cts.Token);
            await Task.Delay(500, _cts.Token);
            sw.Stop();
            ElapsedText = $"{sw.Elapsed.TotalSeconds:0.0} sec";
            Page = Done;
        }
        catch (OperationCanceledException)
        {
            // user cancelled — window is closing anyway.
        }
        catch (Exception ex)
        {
            InstallPhase = "Installation error";
            InstallLog.Add(new InstallLogLine($"[ERR] {ex.Message}", LogKind.Error));
            OnPropertyChanged(nameof(ShowCancel)); // allow closing
        }
    }

    private async Task StartUninstallAsync()
    {
        UPage = 1;
        _cts = new CancellationTokenSource();
        InstallLog.Clear();

        // Measure what's about to be freed, for the Done page stats.
        var (bytes, files) = MeasureInstall();
        UFreedText = Mb(bytes);
        UFileCountText = files.ToString("N0", CultureInfo.CurrentCulture);

        var progress = new Progress<InstallProgress>(OnProgress);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _service.UninstallAsync(KeepSettings, progress, _cts.Token);
            await Task.Delay(400);
            sw.Stop();
            ElapsedText = $"{sw.Elapsed.TotalSeconds:0.0} sec";
            UPage = 2;
        }
        catch (Exception ex)
        {
            InstallPhase = "Uninstall error";
            InstallLog.Add(new InstallLogLine($"[ERR] {ex.Message}", LogKind.Error));
        }
    }

    /// <summary>Total size + file count of the install folder, before it's removed.</summary>
    private (long Bytes, int Files) MeasureInstall()
    {
        try
        {
            var dir = new DirectoryInfo(TargetDir);
            if (!dir.Exists) return (0, 0);
            var all = dir.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            return (all.Sum(f => f.Length), all.Count);
        }
        catch { return (0, 0); }
    }

    private void OnProgress(InstallProgress p)
    {
        InstallPct = p.Percent;
        InstallPhase = p.Phase;
        InstallFile = p.Detail;

        var kind = p.IsError ? LogKind.Error
                 : p.Detail.StartsWith("[INFO]") ? LogKind.Info
                 : p.Detail.StartsWith("Extracting") || p.Detail.StartsWith("Creating C:") ? LogKind.Highlight
                 : LogKind.Dim;
        var prefix = p.Detail.StartsWith('[') ? "" : "[OK]  ";
        InstallLog.Add(new InstallLogLine(prefix + p.Detail, kind));

        // Keep the console bounded.
        while (InstallLog.Count > 200)
            InstallLog.RemoveAt(0);
    }
}

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace SwiftClean.Installer.Services;

/// <summary>Options chosen on the install-path page.</summary>
public sealed record InstallOptions(string TargetDir, bool DesktopShortcut, bool StartMenuShortcut);

/// <summary>A single progress beat reported during installation.</summary>
public sealed record InstallProgress(int Percent, string Phase, string Detail, bool IsError = false);

/// <summary>
/// Performs the real install/uninstall: extracts the embedded app payload, creates shortcuts,
/// and writes the Windows uninstall registry entry. All file/registry work touches
/// Program Files + HKLM, so the process is manifested <c>requireAdministrator</c>.
/// </summary>
public sealed class InstallerService
{
    public const string AppName = "SwiftClean";
    public const string AppVersion = "1.0.0";
    public const string Publisher = "Vyacheslav Protsenko";
    public const string ExeName = "SwiftClean.exe";
    public const string UninstallExeName = "uninstall.exe";

    private const string UninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SwiftClean";
    private const string PayloadResourceName = "payload.zip";

    /// <summary>True when an app payload is embedded (a real, runnable installer).</summary>
    public static bool HasPayload =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(PayloadResourceName);

    /// <summary>The exe path of the installed application once <paramref name="targetDir"/> is populated.</summary>
    public static string AppExePath(string targetDir) => Path.Combine(targetDir, ExeName);

    /// <summary>
    /// Runs the full installation, reporting progress. Designed to be awaited off the UI thread.
    /// Throws on a fatal failure (caller surfaces it); partial work is best-effort cleaned where safe.
    /// </summary>
    public async Task InstallAsync(InstallOptions options, IProgress<InstallProgress> progress,
                                   CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var target = options.TargetDir;

            progress.Report(new(3, "Preparing...", $"[INFO] {AppName} Setup started"));
            Directory.CreateDirectory(target);
            progress.Report(new(8, "Creating folders...", $"Creating {target}\\"));

            // 1. Extract the embedded payload zip into the target directory.
            ExtractPayload(target, progress, ct);

            // 2. Drop a copy of this setup binary as the uninstaller.
            progress.Report(new(80, "Registering components...", "Installing uninstaller..."));
            var setupSelf = Environment.ProcessPath!;
            var uninstallExe = Path.Combine(target, UninstallExeName);
            CopyFileReplacing(setupSelf, uninstallExe);

            // 3. Shortcuts.
            progress.Report(new(86, "Creating shortcuts...", "Creating shortcuts..."));
            CreateShortcuts(options);

            // 4. Uninstall registry entry + version stamp.
            progress.Report(new(93, "Configuring registry...", $"Writing HKLM\\{UninstallKeyPath}"));
            WriteUninstallEntry(options, uninstallExe);
            File.WriteAllText(Path.Combine(target, "version.dat"), AppVersion);

            progress.Report(new(100, "Finishing...", "Installation complete!"));
        }, ct).ConfigureAwait(false);
    }

    private static void ExtractPayload(string target, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                "Installer was built without its payload (payload.zip). Run build-installer.ps1.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')).ToList();
        var total = Math.Max(entries.Count, 1);
        var done = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var destPath = Path.GetFullPath(Path.Combine(target, entry.FullName));
            // Zip-slip guard: never write outside the target directory.
            if (!destPath.StartsWith(Path.GetFullPath(target + Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);

            done++;
            // Extraction spans 8%..78% of the bar.
            var pct = 8 + (int)(70.0 * done / total);
            var phase = pct < 30 ? "Extracting files..."
                      : pct < 55 ? "Copying resources..."
                      : "Configuring components...";
            progress.Report(new(pct, phase, $"Extracting {entry.FullName}..."));
        }
    }

    private static void CreateShortcuts(InstallOptions options)
    {
        var exe = AppExePath(options.TargetDir);

        if (options.DesktopShortcut)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            Helpers.ShellLink.Create(Path.Combine(desktop, $"{AppName}.lnk"), exe,
                description: AppName, workingDirectory: options.TargetDir);
        }

        if (options.StartMenuShortcut)
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var folder = Path.Combine(programs, AppName);
            Helpers.ShellLink.Create(Path.Combine(folder, $"{AppName}.lnk"), exe,
                description: AppName, workingDirectory: options.TargetDir);
        }
    }

    private static void WriteUninstallEntry(InstallOptions options, string uninstallExe)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to write the uninstall entry to the registry.");

        var exe = AppExePath(options.TargetDir);
        key.SetValue("DisplayName", $"{AppName} {AppVersion}");
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", options.TargetDir);
        // Only UninstallString (no QuietUninstallString): both Control Panel and Windows 11 Settings
        // then launch the windowed uninstaller (confirm + progress) instead of removing silently.
        key.SetValue("UninstallString", $"\"{uninstallExe}\" /uninstall");
        // Clear any QuietUninstallString left by an earlier build, so upgrading also gets the window.
        key.DeleteValue("QuietUninstallString", throwOnMissingValue: false);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", DirectorySizeKb(options.TargetDir), RegistryValueKind.DWord);
        key.SetValue("URLInfoAbout", "https://swiftclean.app");
    }

    private static int DirectorySizeKb(string dir)
    {
        try
        {
            long bytes = new DirectoryInfo(dir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            return (int)Math.Min(bytes / 1024, int.MaxValue);
        }
        catch { return 0; }
    }

    private static void CopyFileReplacing(string source, string dest)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return;
        File.Copy(source, dest, overwrite: true);
    }

    /// <summary>Launches the freshly-installed application (used by the "launch after" option).</summary>
    public static void LaunchApp(string targetDir)
    {
        var exe = AppExePath(targetDir);
        if (File.Exists(exe))
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = targetDir });
    }

    // ───────────────────────────── Uninstall ─────────────────────────────

    /// <summary>Reads the install location from the uninstall registry key, if present.</summary>
    public static string? InstalledLocation()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKeyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    /// <summary>
    /// Removes shortcuts and the registry entry, then schedules deletion of the install folder.
    /// The running uninstaller lives inside that folder, so the folder is removed by a detached
    /// shell command after this process exits.
    /// </summary>
    public async Task UninstallAsync(bool keepSettings, IProgress<InstallProgress> progress,
                                     CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var target = InstalledLocation() ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

            progress.Report(new(15, "Removing shortcuts...", "Removing shortcuts..."));
            DeleteShortcuts();

            progress.Report(new(45, "Cleaning registry...", $"Removing HKLM\\{UninstallKeyPath}"));
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false); }
            catch { /* best effort */ }

            if (!keepSettings)
            {
                progress.Report(new(65, "Removing user data...", "Removing user settings + cache..."));
                DeleteUserData();
            }

            progress.Report(new(80, "Deleting files...", $"Deleting {target}\\"));
            ScheduleFolderDeletion(target);

            progress.Report(new(100, "Finishing...", "Uninstall complete!"));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Removes the app's per-user settings/cache folders (Roaming + Local).</summary>
    private static void DeleteUserData()
    {
        foreach (var special in new[] { Environment.SpecialFolder.ApplicationData,
                                        Environment.SpecialFolder.LocalApplicationData })
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(special), AppName);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { /* best effort */ }
        }
    }

    private static void DeleteShortcuts()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        TryDelete(Path.Combine(desktop, $"{AppName}.lnk"));

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var folder = Path.Combine(programs, AppName);
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>
    /// Spawns a detached cmd that waits for this process to exit, then recursively deletes the
    /// install folder (including this running uninstaller). Standard self-deletion technique.
    /// </summary>
    private static void ScheduleFolderDeletion(string target)
    {
        if (!Directory.Exists(target))
            return;

        // ping for a short delay (no console flash), then rmdir the whole folder.
        var cmd = $"/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"{target}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}

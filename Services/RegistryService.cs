using System.IO;
using Microsoft.Win32;

namespace SwiftClean.Services
{
    public enum RegHive { CurrentUser, LocalMachine, LocalMachine32 }

    /// <summary>A leftover Uninstall entry whose program is already gone from disk.</summary>
    public record RegLeftover(string DisplayName, string MissingPath, string RegistryPath, RegHive Hive, string SubKeyName);

    /// <summary>
    /// Finds (and removes) registry leftovers — Uninstall entries that still point at an
    /// InstallLocation folder which no longer exists, i.e. apps removed without cleaning up.
    /// Conservative: only flags entries with a rooted InstallLocation that is missing.
    /// </summary>
    public class RegistryService
    {
        private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string UninstallPath32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        public List<RegLeftover> ScanOrphans()
        {
            var list = new List<RegLeftover>();
            Scan(list, Registry.CurrentUser, UninstallPath, RegHive.CurrentUser, "HKCU");
            Scan(list, Registry.LocalMachine, UninstallPath, RegHive.LocalMachine, "HKLM");
            Scan(list, Registry.LocalMachine, UninstallPath32, RegHive.LocalMachine32, @"HKLM\WOW6432Node");
            return list;
        }

        public bool Delete(RegLeftover entry)
        {
            try
            {
                var (hive, basePath) = HiveFor(entry.Hive);
                hive.DeleteSubKeyTree($@"{basePath}\{entry.SubKeyName}", throwOnMissingSubKey: false);
                return true;
            }
            catch (Exception)
            {
                return false; // usually HKLM without admin
            }
        }

        private static void Scan(List<RegLeftover> list, RegistryKey hive, string basePath, RegHive regHive, string label)
        {
            try
            {
                using var root = hive.OpenSubKey(basePath);
                if (root is null)
                    return;

                foreach (var sub in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(sub);
                        if (key is null)
                            continue;

                        var name = (key.GetValue("DisplayName") as string)?.Trim();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        if (key.GetValue("SystemComponent") is int sc && sc == 1)
                            continue;

                        // Orphaned if its install folder OR its uninstaller executable is gone.
                        var missing = MissingInstallLocation(key) ?? MissingUninstaller(key);
                        if (missing is null)
                            continue;

                        list.Add(new RegLeftover(name, missing, $@"{label}\…\Uninstall\{sub}", regHive, sub));
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        // Returns the InstallLocation if it's a rooted folder that no longer exists.
        private static string? MissingInstallLocation(RegistryKey key)
        {
            var loc = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(loc))
                return null;

            loc = Environment.ExpandEnvironmentVariables(loc);
            return loc.Length >= 3 && Path.IsPathRooted(loc) && !Directory.Exists(loc) ? loc : null;
        }

        // Returns the uninstaller .exe if it's a rooted file that no longer exists (skips MSI/rundll).
        private static string? MissingUninstaller(RegistryKey key)
        {
            var us = (key.GetValue("UninstallString") as string) ?? (key.GetValue("QuietUninstallString") as string);
            if (string.IsNullOrWhiteSpace(us))
                return null;

            var cmd = us.Trim();
            string exe = cmd.StartsWith('"')
                ? (cmd.IndexOf('"', 1) is var e && e > 0 ? cmd[1..e] : cmd.Trim('"'))
                : (cmd.IndexOf(' ') is var s && s > 0 ? cmd[..s] : cmd);

            try { exe = Environment.ExpandEnvironmentVariables(exe); }
            catch (Exception) { return null; }

            var fileName = Path.GetFileName(exe).ToLowerInvariant();
            if (fileName is "msiexec.exe" or "rundll32.exe" || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.IsPathRooted(exe) && !File.Exists(exe) ? exe : null;
        }

        private static (RegistryKey Hive, string Base) HiveFor(RegHive hive) => hive switch
        {
            RegHive.CurrentUser => (Registry.CurrentUser, UninstallPath),
            RegHive.LocalMachine => (Registry.LocalMachine, UninstallPath),
            RegHive.LocalMachine32 => (Registry.LocalMachine, UninstallPath32),
            _ => (Registry.CurrentUser, UninstallPath),
        };
    }
}

using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace SwiftClean.Services
{
    /// <summary>An installed program read from the Windows Uninstall registry keys.</summary>
    public record AppInfo(string Name, string Publisher, long SizeBytes, DateTime? Installed,
                          DateTime? LastUsed, string UninstallCommand);

    /// <summary>Lists installed programs (the "Programs and Features" source) and exposes their uninstallers.</summary>
    public class AppsService
    {
        public List<AppInfo> List()
        {
            var lastUsed = ReadUserAssist();
            var map = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
            Read(map, lastUsed, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            Read(map, lastUsed, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            Read(map, lastUsed, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            return map.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void Read(Dictionary<string, AppInfo> map, Dictionary<string, DateTime> lastUsed,
                                 RegistryKey hive, string path)
        {
            try
            {
                using var root = hive.OpenSubKey(path);
                if (root is null)
                    return;

                foreach (var subName in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(subName);
                        if (key is null)
                            continue;

                        var name = (key.GetValue("DisplayName") as string)?.Trim();
                        if (string.IsNullOrWhiteSpace(name) || map.ContainsKey(name))
                            continue;

                        if (key.GetValue("SystemComponent") is int sc && sc == 1)
                            continue;
                        if (key.GetValue("ParentKeyName") is not null)
                            continue;
                        if (key.GetValue("ReleaseType") is "Update" or "Hotfix" or "Security Update")
                            continue;

                        var uninstall = (key.GetValue("QuietUninstallString") as string)
                                        ?? (key.GetValue("UninstallString") as string);
                        if (string.IsNullOrWhiteSpace(uninstall))
                            continue;

                        var publisher = (key.GetValue("Publisher") as string)?.Trim() ?? string.Empty;
                        long sizeBytes = key.GetValue("EstimatedSize") is int kb && kb > 0 ? (long)kb * 1024 : 0;
                        var installed = ParseDate(key.GetValue("InstallDate") as string);
                        var location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        var used = MatchLastUsed(lastUsed, location);

                        map[name] = new AppInfo(name, publisher, sizeBytes, installed, used, uninstall);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        private static DateTime? ParseDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : null;
        }

        // Newest UserAssist execution under the app's install folder, if any.
        private static DateTime? MatchLastUsed(Dictionary<string, DateTime> lastUsed, string? installLocation)
        {
            if (string.IsNullOrWhiteSpace(installLocation))
                return null;

            DateTime? best = null;
            foreach (var (exePath, when) in lastUsed)
            {
                if (exePath.StartsWith(installLocation, StringComparison.OrdinalIgnoreCase) &&
                    (best is null || when > best))
                {
                    best = when;
                }
            }
            return best;
        }

        // UserAssist holds last-run times (ROT13-encoded paths, FILETIME at offset 60).
        private static Dictionary<string, DateTime> ReadUserAssist()
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
                if (root is null)
                    return map;

                foreach (var guid in root.GetSubKeyNames())
                {
                    using var count = root.OpenSubKey($@"{guid}\Count");
                    if (count is null)
                        continue;

                    foreach (var valueName in count.GetValueNames())
                    {
                        if (count.GetValue(valueName) is not byte[] data || data.Length < 68)
                            continue;
                        var ft = BitConverter.ToInt64(data, 60);
                        if (ft <= 0)
                            continue;
                        try
                        {
                            map[Rot13(valueName)] = DateTime.FromFileTimeUtc(ft);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            return map;
        }

        private static string Rot13(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c is >= 'a' and <= 'z') sb.Append((char)('a' + (c - 'a' + 13) % 26));
                else if (c is >= 'A' and <= 'Z') sb.Append((char)('A' + (c - 'A' + 13) % 26));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

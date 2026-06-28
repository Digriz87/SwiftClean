using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SwiftClean.Services
{
    /// <summary>Persisted user preferences.</summary>
    public class AppSettings
    {
        public string Language { get; set; } = "en";
        public bool Notifications { get; set; } = true;
        public bool Autostart { get; set; }
        public bool SchedulerEnabled { get; set; }
        public string SchedulerFreq { get; set; } = "weekly";

        /// <summary>Run time of the scheduled auto-clean, "HH:mm" (24h).</summary>
        public string SchedulerTime { get; set; } = "03:00";

        // Which categories the scheduled auto-clean removes (matches the Scheduler page checkboxes).
        public bool SchedulerCleanTemp { get; set; } = true;
        public bool SchedulerCleanRecycle { get; set; } = true;
        public bool SchedulerCleanCache { get; set; }
    }

    /// <summary>Loads/saves <see cref="AppSettings"/> to %AppData%\SwiftClean\settings.json (best-effort).</summary>
    public static class SettingsStore
    {
        private static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwiftClean");
        private static string FilePath => Path.Combine(Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception) { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { }
        }
    }

    /// <summary>Registers/removes SwiftClean in the per-user startup (HKCU\...\Run).</summary>
    public static class AutostartManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SwiftClean";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (key is null)
                    return;

                if (enabled)
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(ValueName, $"\"{exe}\"");
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception) { }
        }
    }
}

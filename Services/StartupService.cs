using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SwiftClean.Services
{
    public enum StartupSource { UserRun, MachineRun, MachineRun32, UserStartupFolder, CommonStartupFolder }

    /// <summary>A program that runs at login, from a registry Run key or a Startup folder.</summary>
    public record StartupEntry(string Name, string ApprovedName, string Command, string ExePath,
                               StartupSource Source, bool IsEnabled, long ExeSizeBytes, string Company);

    /// <summary>
    /// Reads startup programs and toggles them the way Task Manager does — via the
    /// <c>StartupApproved</c> keys, without deleting the underlying Run value / shortcut.
    /// HKLM writes need admin; failures are reported back so the UI can revert.
    /// </summary>
    public class StartupService
    {
        private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public List<StartupEntry> List()
        {
            var list = new List<StartupEntry>();
            ReadRun(list, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupSource.UserRun, "Run");
            ReadRun(list, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupSource.MachineRun, "Run");
            ReadRun(list, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupSource.MachineRun32, "Run32");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserStartupFolder);
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonStartupFolder);
            return list;
        }

        /// <summary>Enables/disables an entry via StartupApproved. Returns false on failure (e.g. no admin for HKLM).</summary>
        public bool SetEnabled(StartupEntry entry, bool enabled)
        {
            var (hive, sub) = ApprovedLocation(entry.Source);
            try
            {
                using var key = hive.CreateSubKey(ApprovedRoot + sub, writable: true);
                if (key is null)
                    return false;

                var data = new byte[12];
                if (enabled)
                {
                    data[0] = 0x02;
                }
                else
                {
                    data[0] = 0x03;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
                }
                key.SetValue(entry.ApprovedName, data, RegistryValueKind.Binary);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ReadRun(List<StartupEntry> list, RegistryKey hive, string subKey, StartupSource source, string approvedSub)
        {
            try
            {
                using var key = hive.OpenSubKey(subKey);
                if (key is null)
                    return;

                foreach (var name in key.GetValueNames())
                {
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var command = key.GetValue(name)?.ToString() ?? string.Empty;
                    var exe = ResolveExe(command);
                    var (size, company) = ExeInfo(exe);
                    var enabled = IsApprovedEnabled(source, approvedSub, name);
                    list.Add(new StartupEntry(name, name, command, exe, source, enabled, size, company));
                }
            }
            catch (Exception) { }
        }

        private void ReadFolder(List<StartupEntry> list, string folder, StartupSource source)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    return;

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ini")
                        continue;

                    var fileName = Path.GetFileName(file);
                    var exe = ext == ".lnk" ? string.Empty : file; // resolving .lnk targets needs the shell
                    var (size, company) = ExeInfo(exe);
                    var enabled = IsApprovedEnabled(source, "StartupFolder", fileName);
                    list.Add(new StartupEntry(Path.GetFileNameWithoutExtension(file), fileName, file, exe, source, enabled, size, company));
                }
            }
            catch (Exception) { }
        }

        private static bool IsApprovedEnabled(StartupSource source, string approvedSub, string valueName)
        {
            var (hive, _) = ApprovedLocation(source);
            try
            {
                using var key = hive.OpenSubKey(ApprovedRoot + approvedSub);
                if (key?.GetValue(valueName) is byte[] data && data.Length > 0)
                    return (data[0] & 1) == 0; // odd first byte => disabled
            }
            catch (Exception) { }
            return true; // no record => enabled
        }

        private static (RegistryKey Hive, string Sub) ApprovedLocation(StartupSource source) => source switch
        {
            StartupSource.UserRun => (Registry.CurrentUser, "Run"),
            StartupSource.MachineRun => (Registry.LocalMachine, "Run"),
            StartupSource.MachineRun32 => (Registry.LocalMachine, "Run32"),
            StartupSource.UserStartupFolder => (Registry.CurrentUser, "StartupFolder"),
            StartupSource.CommonStartupFolder => (Registry.LocalMachine, "StartupFolder"),
            _ => (Registry.CurrentUser, "Run"),
        };

        private static string ResolveExe(string command)
        {
            var cmd = command.Trim();
            if (cmd.Length == 0)
                return string.Empty;

            string path;
            if (cmd.StartsWith('"'))
            {
                var end = cmd.IndexOf('"', 1);
                path = end > 0 ? cmd[1..end] : cmd.Trim('"');
            }
            else
            {
                var space = cmd.IndexOf(' ');
                path = space > 0 ? cmd[..space] : cmd;
            }

            try { return Environment.ExpandEnvironmentVariables(path); }
            catch (Exception) { return path; }
        }

        private static (long Size, string Company) ExeInfo(string exe)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return (0, string.Empty);
            try
            {
                var size = new FileInfo(exe).Length;
                var company = FileVersionInfo.GetVersionInfo(exe).CompanyName ?? string.Empty;
                return (size, company.Trim());
            }
            catch (Exception)
            {
                return (0, string.Empty);
            }
        }
    }
}

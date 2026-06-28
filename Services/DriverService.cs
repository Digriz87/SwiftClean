using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using SwiftClean.Models;

namespace SwiftClean.Services
{
    /// <summary>Progress beat emitted during a driver scan.</summary>
    public record DriverScanProgress(int Percent, string Device);

    /// <summary>
    /// Enumerates installed device drivers via WMI <c>Win32_PnPSignedDriver</c> and checks/updates
    /// them entirely through the Windows Update Agent (WUA) COM API — search is read-only (no admin),
    /// install runs an elevated PowerShell host that downloads and installs the driver package.
    /// </summary>
    public class DriverService
    {
        private static readonly HashSet<string> RelevantClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "DISPLAY", "NET", "MEDIA", "BLUETOOTH", "IMAGE", "USB", "SYSTEM", "AUDIOENDPOINT", "MONITOR",
        };

        // ── Scan ─────────────────────────────────────────────────────────────

        public async Task<List<DriverInfo>> ScanAsync(IProgress<DriverScanProgress>? progress,
                                                      CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var result = new List<DriverInfo>();
                var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                ManagementObject[] rows;
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT DeviceName, Manufacturer, DriverVersion, DriverDate, DeviceClass, DeviceID " +
                        "FROM Win32_PnPSignedDriver");
                    rows = searcher.Get().Cast<ManagementObject>().ToArray();
                }
                catch { return result; }

                for (var i = 0; i < rows.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    using var mo = rows[i];

                    var name        = (mo["DeviceName"]   as string)?.Trim();
                    var deviceClass = (mo["DeviceClass"]  as string)?.Trim();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(deviceClass)) continue;
                    if (!RelevantClasses.Contains(deviceClass) || !seen.Add(name))       continue;

                    var manufacturer = (mo["Manufacturer"]  as string)?.Trim() ?? "";
                    var version      = (mo["DriverVersion"] as string)?.Trim() ?? "";
                    var date         = ParseDate(mo["DriverDate"] as string);
                    var deviceId     = (mo["DeviceID"]      as string)?.Trim() ?? "";

                    result.Add(new DriverInfo(name, manufacturer, deviceClass, version, date, "", deviceId));

                    progress?.Report(new DriverScanProgress(
                        (int)((i + 1) / (double)rows.Length * 100), name));
                }

                return result
                    .OrderBy(d => d.DeviceClass)
                    .ThenBy(d => d.DeviceName)
                    .ToList();

            }, ct).ConfigureAwait(false);
        }

        private static DateTime? ParseDate(string? dmtf)
        {
            if (string.IsNullOrWhiteSpace(dmtf)) return null;
            try { return ManagementDateTimeConverter.ToDateTime(dmtf); }
            catch { return null; }
        }

        // ── Windows Update Agent (WUA) ────────────────────────────────────────

        /// <summary>
        /// Uses the Windows Update Agent COM API to search for pending driver updates.
        /// Does NOT require admin — search is a read-only operation.
        /// Returns an empty list if WUA is unavailable or the search fails.
        /// </summary>
        public static async Task<List<WuDriverUpdate>> WuSearchAsync(
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var updates = new List<WuDriverUpdate>();
                try
                {
                    var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    if (sessionType == null) return updates;

                    dynamic session  = Activator.CreateInstance(sessionType)!;
                    dynamic searcher = session.CreateUpdateSearcher();

                    progress?.Report("searching");
                    dynamic result = searcher.Search("IsInstalled=0 And Type='Driver'");

                    int count = (int)result.Updates.Count;
                    for (int i = 0; i < count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        dynamic u = result.Updates.Item(i);

                        // Driver-specific fields (IWindowsDriverUpdate) — read defensively; absent on some objects.
                        string model = "", cls = "", mfg = "";
                        try { model = (string)u.DriverModel        ?? ""; } catch { }
                        try { cls   = (string)u.DriverClass        ?? ""; } catch { }
                        try { mfg   = (string)u.DriverManufacturer ?? ""; } catch { }

                        updates.Add(new WuDriverUpdate((string)u.Title, (long)u.MaxDownloadSize, model, cls, mfg));
                    }
                }
                catch { /* WUA unavailable or search failed — return empty list */ }
                return updates;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads and installs driver updates through the Windows Update Agent COM API, hosted in an
        /// elevated PowerShell process (install requires admin → one UAC prompt). When <paramref name="title"/>
        /// is given, only the update with that exact title is installed; otherwise all pending driver updates.
        /// Awaits completion and returns <c>true</c> only when the install succeeded (WUA result code 2).
        /// Exit codes: 0 = success, 2 = nothing matched, 1/other = failure or the user declined elevation.
        /// </summary>
        public static async Task<bool> WuInstallAsync(string? title = null, CancellationToken ct = default)
        {
            // Title is passed as a -File argument, so the script reads it from $args rather than being
            // interpolated into the script body (avoids quoting/injection issues with driver titles).
            const string script = @"param([string]$Title = '')
$ErrorActionPreference = 'Stop'
try {
    $s = New-Object -ComObject 'Microsoft.Update.Session'
    $q = $s.CreateUpdateSearcher()
    $r = $q.Search(""IsInstalled=0 And Type='Driver'"")
    $c = New-Object -ComObject 'Microsoft.Update.UpdateColl'
    foreach ($u in $r.Updates) {
        if ($Title -eq '' -or $u.Title -eq $Title) { try { $u.AcceptEula() } catch {}; [void]$c.Add($u) }
    }
    if ($c.Count -eq 0) { exit 2 }
    $dl = $s.CreateUpdateDownloader(); $dl.Updates = $c; [void]$dl.Download()
    $i = $s.CreateUpdateInstaller(); $i.Updates = $c
    $res = $i.Install()
    if ($res.ResultCode -eq 2) { exit 0 } else { exit 1 }
} catch { exit 1 }";
            try
            {
                var tmp = Path.Combine(Path.GetTempPath(), "swiftclean_wudrv_install.ps1");
                await File.WriteAllTextAsync(tmp, script, Encoding.UTF8, ct).ConfigureAwait(false);

                var safeTitle = (title ?? string.Empty).Replace("\"", "\\\"");
                var psi = new ProcessStartInfo("powershell.exe")
                {
                    Arguments       = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{tmp}\" -Title \"{safeTitle}\"",
                    UseShellExecute = true,
                    Verb            = "runas",                // elevation prompt — install needs admin
                    WindowStyle     = ProcessWindowStyle.Hidden,
                };

                var proc = Process.Start(psi);
                if (proc == null) return false;
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                return proc.ExitCode == 0;
            }
            catch
            {
                // User declined the UAC prompt, or WUA/PowerShell is unavailable.
                return false;
            }
        }
    }
}

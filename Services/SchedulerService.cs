using System.Diagnostics;

namespace SwiftClean.Services
{
    /// <summary>
    /// Manages a Windows Task Scheduler entry that runs SwiftClean with <c>--autoclean</c>
    /// on a schedule. Uses the <c>schtasks</c> CLI (per-user task, no admin needed).
    /// </summary>
    public static class SchedulerService
    {
        private const string TaskName = "SwiftClean Auto-Clean";

        public static bool Exists() => Run("/Query", "/TN", TaskName) == 0;

        /// <summary>Creates or replaces the scheduled task for the given frequency and run time ("HH:mm").</summary>
        public static bool Create(string freq, string time = "03:00")
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return false;

            var sc = freq switch
            {
                "daily" => "DAILY",
                "monthly" => "MONTHLY",
                _ => "WEEKLY",
            };

            // ArgumentList handles quoting; /TR carries the quoted exe path + flag.
            return Run("/Create", "/TN", TaskName, "/TR", $"\"{exe}\" --autoclean",
                       "/SC", sc, "/ST", time, "/F") == 0;
        }

        public static bool Remove() => Run("/Delete", "/TN", TaskName, "/F") == 0;

        public static void Apply(bool enabled, string freq, string time = "03:00")
        {
            if (enabled)
                Create(freq, time);
            else
                Remove();
        }

        private static int Run(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in args)
                    psi.ArgumentList.Add(a);

                using var p = Process.Start(psi);
                if (p is null)
                    return -1;
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}

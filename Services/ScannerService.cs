using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SwiftClean.Services
{
    /// <summary>Reported as a scan progresses; surfaces the path being measured.</summary>
    public record ScanProgress(string CurrentPath, int PercentComplete, string Category);

    /// <summary>A single file found in a category (used to preview what will be removed).</summary>
    public record FileEntry(string Path, long Size);

    /// <summary>One installed browser's cache (a sub-item of the Browser Cache category).</summary>
    public record BrowserCacheInfo(string Name, long SizeBytes, IReadOnlyList<string> Paths);

    /// <summary>One cleanable category with its measured size, the folders it covers, a sample of its files,
    /// and (for Browser Cache) the per-browser breakdown.</summary>
    public record ScanCategory(string Name, long SizeBytes, int FileCount, bool IsSafeToDelete, string Icon,
                               IReadOnlyList<string> Paths, IReadOnlyList<FileEntry> Files,
                               IReadOnlyList<BrowserCacheInfo> Browsers);

    /// <summary>Aggregate result of a full filesystem scan.</summary>
    public record ScanResult(List<ScanCategory> Categories, long TotalBytes, int TotalFiles, TimeSpan Duration);

    /// <summary>
    /// Measures (does not delete) the size of well-known junk/cache locations on disk.
    /// All filesystem access is best-effort: inaccessible files and folders are skipped.
    /// </summary>
    public class ScannerService
    {
        // Segoe MDL2 Assets glyphs (unicode escapes keep the source ASCII-safe).
        private const string IconTemp = "";    // Page
        private const string IconRecycle = ""; // Delete
        private const string IconBrowser = ""; // Globe
        private const string IconUpdate = "";  // UpdateRestore

        /// <summary>
        /// Scans the known junk locations, reporting progress, and returns the aggregate result.
        /// </summary>
        // How many file paths to keep per category for the "what will be removed" preview.
        private const int SampleCap = 500;

        public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            var specs = BuildCategorySpecs();

            var categories = new List<ScanCategory>();
            long totalBytes = 0;
            int totalFiles = 0;

            for (var i = 0; i < specs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var spec = specs[i];
                var percent = (int)(i / (double)specs.Count * 100);

                long categoryBytes;
                int categoryFiles;
                var sample = new List<FileEntry>();
                IReadOnlyList<BrowserCacheInfo> categoryBrowsers = Array.Empty<BrowserCacheInfo>();

                if (spec.Name == "Recycle Bin")
                {
                    // The Recycle Bin folder always holds per-user desktop.ini files, so a raw
                    // directory walk over-counts. Ask the shell for the real size/count instead.
                    progress?.Report(new ScanProgress("Корзина", percent, spec.Name));
                    (categoryBytes, categoryFiles) = QueryRecycleBin();
                }
                else if (spec.Name == "Browser Cache")
                {
                    (categoryBytes, categoryFiles, categoryBrowsers) =
                        await ScanBrowsersAsync(BrowserSpecs(), sample, percent, progress, spec.Name, ct);
                }
                else if (spec.Name == "Browser Cookies")
                {
                    (categoryBytes, categoryFiles, categoryBrowsers) =
                        await ScanBrowsersAsync(BrowserCookieSpecs(), sample, percent, progress, spec.Name, ct);
                }
                else
                {
                    categoryBytes = 0;
                    categoryFiles = 0;
                    foreach (var path in spec.Paths)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new ScanProgress(path, percent, spec.Name));

                        var (bytes, count) = await MeasureAsync(path, sample, SampleCap, ct);
                        categoryBytes += bytes;
                        categoryFiles += count;
                    }
                }

                categories.Add(new ScanCategory(spec.Name, categoryBytes, categoryFiles,
                    spec.SafeToDelete, spec.Icon, spec.Paths, sample, categoryBrowsers));
                totalBytes += categoryBytes;
                totalFiles += categoryFiles;

                var donePercent = (int)((i + 1) / (double)specs.Count * 100);
                progress?.Report(new ScanProgress(spec.Name, donePercent, spec.Name));
            }

            stopwatch.Stop();
            return new ScanResult(categories, totalBytes, totalFiles, stopwatch.Elapsed);
        }

        /// <summary>Recursively sums sizes under <paramref name="path"/>, collecting up to
        /// <paramref name="sampleCap"/> file entries into <paramref name="sample"/>. Errors are skipped.</summary>
        private static Task<(long Bytes, int Files)> MeasureAsync(string path, List<FileEntry> sample, int sampleCap, CancellationToken ct)
            => Task.Run(() =>
            {
                long bytes = 0;
                var files = 0;

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return (bytes, files);

                var pending = new Stack<string>();
                pending.Push(path);

                while (pending.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var dir = pending.Pop();

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            pending.Push(sub);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            try
                            {
                                var len = new FileInfo(file).Length;
                                bytes += len;
                                files++;
                                if (sample.Count < sampleCap)
                                    sample.Add(new FileEntry(file, len));
                            }
                            catch (UnauthorizedAccessException) { }
                            catch (IOException) { }
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }

                return (bytes, files);
            }, ct);

        /// <summary>Measures each installed browser's paths (cache folders or cookie files) into a per-browser breakdown.</summary>
        private static async Task<(long Bytes, int Files, List<BrowserCacheInfo> Browsers)> ScanBrowsersAsync(
            List<(string Name, List<string> Paths)> specs, List<FileEntry> sample, int percent,
            IProgress<ScanProgress>? progress, string category, CancellationToken ct)
        {
            long total = 0;
            int files = 0;
            var browsers = new List<BrowserCacheInfo>();

            foreach (var browser in specs)
            {
                ct.ThrowIfCancellationRequested();
                var existing = browser.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                if (existing.Count == 0)
                    continue; // not installed

                progress?.Report(new ScanProgress(browser.Name, percent, category));
                long bytes = 0;
                foreach (var path in existing)
                {
                    var (b, f) = await MeasurePathAsync(path, sample, SampleCap, ct);
                    bytes += b;
                    files += f;
                }
                browsers.Add(new BrowserCacheInfo(browser.Name, bytes, existing));
                total += bytes;
            }

            return (total, files, browsers);
        }

        /// <summary>Measures a path that may be a single file (cookie db) or a folder (cache).</summary>
        private static Task<(long Bytes, int Files)> MeasurePathAsync(string path, List<FileEntry> sample, int cap, CancellationToken ct)
        {
            if (File.Exists(path))
            {
                try
                {
                    var len = new FileInfo(path).Length;
                    if (sample.Count < cap)
                        sample.Add(new FileEntry(path, len));
                    return Task.FromResult((len, 1));
                }
                catch (Exception)
                {
                    return Task.FromResult((0L, 0));
                }
            }
            return MeasureAsync(path, sample, cap, ct);
        }

        /// <summary>Asks the shell for the real Recycle Bin size and item count (across all drives).</summary>
        private static (long Bytes, int Files) QueryRecycleBin()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = SHQueryRecycleBin(null, ref info);
            return hr == 0 ? (info.i64Size, (int)info.i64NumItems) : (0L, 0);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        /// <summary>Known browsers and their cache folders. A browser is "installed" if any path exists.</summary>
        private static List<(string Name, List<string> Paths)> BrowserSpecs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return new List<(string, List<string>)>
            {
                ("Google Chrome", new List<string> { Path.Combine(local, @"Google\Chrome\User Data\Default\Cache") }),
                ("Microsoft Edge", new List<string> { Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache") }),
                ("Brave", new List<string> { Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data\Default\Cache") }),
                ("Opera", new List<string> { Path.Combine(local, @"Opera Software\Opera Stable\Cache") }),
                ("Yandex", new List<string> { Path.Combine(local, @"Yandex\YandexBrowser\User Data\Default\Cache") }),
                ("Vivaldi", new List<string> { Path.Combine(local, @"Vivaldi\User Data\Default\Cache") }),
                ("Mozilla Firefox", FirefoxCachePaths(local).ToList()),
            };
        }

        /// <summary>
        /// Recursively sums the file sizes under <paramref name="path"/>.
        /// <see cref="UnauthorizedAccessException"/> and <see cref="IOException"/> are skipped silently.
        /// </summary>
        public static Task<(long Bytes, int Files)> GetDirectorySizeAsync(string path, CancellationToken ct)
            => Task.Run(() =>
            {
                long bytes = 0;
                var files = 0;

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return (bytes, files);

                var pending = new Stack<string>();
                pending.Push(path);

                while (pending.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var dir = pending.Pop();

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            pending.Push(sub);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            try
                            {
                                bytes += new FileInfo(file).Length;
                                files++;
                            }
                            catch (UnauthorizedAccessException) { }
                            catch (IOException) { }
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }

                return (bytes, files);
            }, ct);

        /// <summary>Builds the list of categories and the real paths each one covers.</summary>
        private static List<(string Name, string Icon, bool SafeToDelete, List<string> Paths)> BuildCategorySpecs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            var tempPaths = Distinct(new[]
            {
                Path.GetTempPath(),
                Path.Combine(windows, "Temp"),
                Path.Combine(local, "Temp"),
            });

            var browserPaths = new List<string>
            {
                Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"),
            };
            browserPaths.AddRange(FirefoxCachePaths(local));

            var cookiePaths = Distinct(BrowserCookieSpecs().SelectMany(b => b.Paths));

            return new List<(string, string, bool, List<string>)>
            {
                ("Temp Files",           IconTemp,    true, tempPaths),
                ("Recycle Bin",          IconRecycle, true, new List<string> { @"C:\$Recycle.Bin" }),
                ("Browser Cache",        IconBrowser, true, Distinct(browserPaths)),
                ("Browser Cookies",      IconBrowser, true, cookiePaths),
                ("Windows Update Cache", IconUpdate,  true, new List<string> { Path.Combine(windows, @"SoftwareDistribution\Download") }),
            };
        }

        /// <summary>Per-installed-browser cookie databases (a sub-item per browser).</summary>
        private static List<(string Name, List<string> Paths)> BrowserCookieSpecs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            return new List<(string, List<string>)>
            {
                ("Google Chrome", ChromiumCookies(local, @"Google\Chrome")),
                ("Microsoft Edge", ChromiumCookies(local, @"Microsoft\Edge")),
                ("Brave", ChromiumCookies(local, @"BraveSoftware\Brave-Browser")),
                ("Opera", new List<string> { Path.Combine(roaming, @"Opera Software\Opera Stable\Network\Cookies"), Path.Combine(roaming, @"Opera Software\Opera Stable\Cookies") }),
                ("Yandex", ChromiumCookies(local, @"Yandex\YandexBrowser")),
                ("Vivaldi", ChromiumCookies(local, "Vivaldi")),
                // Firefox keeps cookies.sqlite in the Roaming profile (only the cache is in Local).
                ("Mozilla Firefox", FirefoxProfileItems(roaming, "cookies.sqlite").ToList()),
            };
        }

        // Chromium-based cookie DB lives under either Default\Network\Cookies (newer) or Default\Cookies.
        private static List<string> ChromiumCookies(string local, string vendor) => new()
        {
            Path.Combine(local, vendor, @"User Data\Default\Network\Cookies"),
            Path.Combine(local, vendor, @"User Data\Default\Cookies"),
        };

        /// <summary>Cache folders of every Firefox profile.</summary>
        private static IEnumerable<string> FirefoxCachePaths(string local) => FirefoxProfileItems(local, "cache2");

        /// <summary>Enumerates <paramref name="item"/> inside every Firefox profile.
        /// On Windows the cache/cookies live under LocalAppData (the Roaming profile only holds settings).</summary>
        private static IEnumerable<string> FirefoxProfileItems(string local, string item)
        {
            var profilesRoot = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (!Directory.Exists(profilesRoot))
                yield break;

            string[] profiles;
            try
            {
                profiles = Directory.GetDirectories(profilesRoot);
            }
            catch (UnauthorizedAccessException) { yield break; }
            catch (IOException) { yield break; }

            foreach (var profile in profiles)
                yield return Path.Combine(profile, item);
        }

        // De-duplicates paths that resolve to the same folder (e.g. %TEMP% == LocalAppData\Temp).
        private static List<string> Distinct(IEnumerable<string> paths)
            => paths
                .Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}

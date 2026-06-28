using System.Diagnostics;
using System.IO;

namespace SwiftClean.Services
{
    /// <summary>Bytes attributed to one file-type category.</summary>
    public record DiskTypeBucket(string Key, long Bytes);

    /// <summary>A progress snapshot of the disk type analysis (raised repeatedly as the walk proceeds).</summary>
    public record DiskTypeSnapshot(IReadOnlyList<DiskTypeBucket> Buckets, long ScannedBytes, bool Done);

    /// <summary>
    /// Walks the system drive once and groups every file's size by its type (derived from the
    /// extension): video, photo, audio, documents, archives, apps, other. Best-effort — inaccessible
    /// folders/files and reparse points (junctions/symlinks) are skipped, so the walk can't loop or throw.
    /// </summary>
    public class DiskTypeScanner
    {
        /// <summary>Category keys, in display order. Loc keys are "disktype.&lt;key&gt;".</summary>
        public static readonly string[] Keys = { "video", "photo", "audio", "documents", "archives", "apps", "other" };
        private const int OtherIndex = 6;

        private static readonly Dictionary<string, int> ExtIndex = BuildExtIndex();

        private static Dictionary<string, int> BuildExtIndex()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            void Add(int i, params string[] exts) { foreach (var e in exts) map[e] = i; }
            Add(0, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts", ".m2ts", ".vob", ".mts");
            Add(1, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".heic", ".heif", ".raw", ".cr2", ".nef", ".arw", ".dng", ".svg", ".ico", ".psd");
            Add(2, ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus", ".aiff", ".mid", ".midi");
            Add(3, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp", ".csv", ".md", ".epub", ".mobi", ".one");
            Add(4, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".tgz", ".lz", ".arj");
            Add(5, ".exe", ".dll", ".msi", ".sys", ".bin", ".dat", ".bat", ".cmd", ".com", ".scr", ".ocx", ".drv", ".cpl", ".efi", ".appx", ".msix", ".cat", ".mui", ".node", ".pak", ".vdi", ".vmdk");
            return map;
        }

        /// <summary>
        /// Scans <paramref name="root"/> on a background thread, reporting throttled snapshots
        /// (~every 400ms) and a final <c>Done</c> snapshot.
        /// </summary>
        public static Task ScanAsync(string root, IProgress<DiskTypeSnapshot> progress, CancellationToken ct)
            => Task.Run(() =>
            {
                var totals = new long[Keys.Length];
                long scanned = 0;
                var sw = Stopwatch.StartNew();
                long lastReport = 0;

                var pending = new Stack<string>();
                pending.Push(root);

                while (pending.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var dir = pending.Pop();

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            try
                            {
                                // Skip junctions/symlinks so the walk neither loops nor double-counts.
                                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                                    continue;
                            }
                            catch (UnauthorizedAccessException) { continue; }
                            catch (IOException) { continue; }
                            pending.Push(sub);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            try
                            {
                                long len = new FileInfo(file).Length;
                                var ext = Path.GetExtension(file);
                                int idx = ext.Length > 0 && ExtIndex.TryGetValue(ext, out var i) ? i : OtherIndex;
                                totals[idx] += len;
                                scanned += len;
                            }
                            catch (UnauthorizedAccessException) { }
                            catch (IOException) { }
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }

                    if (progress is not null && sw.ElapsedMilliseconds - lastReport > 400)
                    {
                        lastReport = sw.ElapsedMilliseconds;
                        progress.Report(Snapshot(totals, scanned, false));
                    }
                }

                progress?.Report(Snapshot(totals, scanned, true));
            }, ct);

        private static DiskTypeSnapshot Snapshot(long[] totals, long scanned, bool done)
        {
            var list = new List<DiskTypeBucket>(Keys.Length);
            for (int i = 0; i < Keys.Length; i++)
                list.Add(new DiskTypeBucket(Keys[i], totals[i]));
            return new DiskTypeSnapshot(list, scanned, done);
        }
    }
}

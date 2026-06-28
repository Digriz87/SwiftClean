using System.IO;
using System.Runtime.InteropServices;

namespace SwiftClean.Services
{
    /// <summary>Reported as cleaning progresses.</summary>
    public record CleanProgress(string Category, int PercentComplete);

    /// <summary>Outcome of a cleaning run.</summary>
    public record CleanResult(long FreedBytes, int CategoriesCleaned, int Failures);

    /// <summary>
    /// Removes the contents of scanned categories. Everything goes to the Recycle Bin
    /// (via the shell, silently) so the user can recover it; the "Recycle Bin" category
    /// itself is emptied. All operations are best-effort — locked/denied items are skipped.
    /// </summary>
    public class CleanerService
    {
        private const string RecycleBinCategory = "Recycle Bin";

        public Task<CleanResult> CleanAsync(IReadOnlyList<ScanCategory> categories,
                                            IProgress<CleanProgress>? progress, CancellationToken ct)
            => Task.Run(() =>
            {
                long freed = 0;
                int cleaned = 0;
                int failures = 0;

                for (var i = 0; i < categories.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var category = categories[i];
                    progress?.Report(new CleanProgress(category.Name, (int)(i / (double)categories.Count * 100)));

                    bool ok;
                    if (string.Equals(category.Name, RecycleBinCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        ok = EmptyRecycleBin();
                    }
                    else
                    {
                        ok = true;
                        foreach (var path in category.Paths)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (!RecycleContents(path))
                                ok = false;
                        }
                    }

                    if (ok)
                    {
                        freed += category.SizeBytes;
                        cleaned++;
                    }
                    else
                    {
                        failures++;
                    }
                }

                progress?.Report(new CleanProgress(string.Empty, 100));
                return new CleanResult(freed, cleaned, failures);
            }, ct);

        /// <summary>Recycles a single file (e.g. a cookie DB), or every top-level entry of a folder (keeping the folder).</summary>
        private static bool RecycleContents(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            if (File.Exists(path))
                return RecyclePaths(new[] { path });

            if (!Directory.Exists(path))
                return true; // nothing to do

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(path);
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }

            return entries.Length == 0 || RecyclePaths(entries);
        }

        private static bool RecyclePaths(string[] paths)
        {
            // Double-null-terminated list of paths for SHFileOperation.
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = string.Join('\0', paths) + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR,
            };

            var result = SHFileOperation(ref op);
            return result == 0 && op.fAnyOperationsAborted == 0;
        }

        private static bool EmptyRecycleBin()
        {
            var result = SHEmptyRecycleBin(IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            // S_OK (0) = emptied; E_UNEXPECTED when already empty — treat both as success.
            return result == 0 || (uint)result == 0x8000FFFF;
        }

        // ===== Shell interop =====

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_NOCONFIRMMKDIR = 0x0200;

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string? pFrom;
            public string? pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    }
}

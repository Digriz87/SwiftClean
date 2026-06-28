using System.Globalization;

namespace SwiftClean.Helpers
{
    /// <summary>Formats byte counts into human-readable sizes.</summary>
    public static class SizeFormatter
    {
        private const double Kb = 1024;
        private const double Mb = Kb * 1024;
        private const double Gb = Mb * 1024;

        /// <summary>Formats bytes as e.g. "2.1 GB", "876 MB", "92 KB".</summary>
        public static string Format(long bytes)
        {
            if (bytes >= Gb) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / Gb);
            if (bytes >= Mb) return string.Format(CultureInfo.InvariantCulture, "{0:0} MB", bytes / Mb);
            if (bytes >= Kb) return string.Format(CultureInfo.InvariantCulture, "{0:0} KB", bytes / Kb);
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }

        public static double ToGigabytes(long bytes) => bytes / Gb;
    }
}

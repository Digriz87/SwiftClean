namespace SwiftClean.Models
{
    /// <summary>A labeled slice of disk usage rendered as a progress bar.</summary>
    public class DiskSegment
    {
        public DiskSegment(string name, double percent, string colorHex)
        {
            Name = name;
            Percent = percent;
            ColorHex = colorHex;
        }

        public string Name { get; }
        /// <summary>Share of the disk, 0-100.</summary>
        public double Percent { get; }
        public string ColorHex { get; }
    }
}

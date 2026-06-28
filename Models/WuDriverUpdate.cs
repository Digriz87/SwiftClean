namespace SwiftClean.Models
{
    /// <summary>A single driver update found by the Windows Update Agent search.</summary>
    public sealed class WuDriverUpdate
    {
        public WuDriverUpdate(string title, long sizeBytes,
                              string driverModel = "", string driverClass = "", string driverManufacturer = "")
        {
            Title              = title;
            SizeBytes          = sizeBytes;
            DriverModel        = driverModel;
            DriverClass        = driverClass;
            DriverManufacturer = driverManufacturer;
        }

        public string Title     { get; }
        public long   SizeBytes { get; }

        // Populated for true driver updates (IWindowsDriverUpdate) — used to match a found update
        // back to a specific device row in the table.
        public string DriverModel        { get; }
        public string DriverClass        { get; }
        public string DriverManufacturer { get; }

        public string SizeText => SizeBytes switch
        {
            > 1_048_576 => $"{SizeBytes / 1_048_576.0:F1} MB",
            > 1_024     => $"{SizeBytes / 1024} KB",
            _           => $"{SizeBytes} B"
        };
    }
}

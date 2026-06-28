namespace SwiftClean.Models
{
    /// <summary>An installed program listed on the Apps page (display + sort keys).</summary>
    public record InstalledApp(
        string Name,
        string Publisher,
        string Size,
        string Date,
        string LastUsed,
        string UninstallCommand,
        long SizeBytes,
        System.DateTime? InstalledDate,
        System.DateTime? LastUsedDate);
}

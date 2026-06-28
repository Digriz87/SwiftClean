namespace SwiftClean.Models
{
    /// <summary>A single file shown in a clean category's "what will be removed" list.</summary>
    public record CleanFile(string Name, string FullPath, string DisplaySize);
}

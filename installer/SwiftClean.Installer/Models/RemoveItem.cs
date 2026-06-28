namespace SwiftClean.Installer.Models;

/// <summary>One row in the uninstaller's "will be removed" list.</summary>
public sealed class RemoveItem
{
    public RemoveItem(string icon, string label, string path, string size)
    {
        Icon = icon;
        Label = label;
        Path = path;
        Size = size;
    }

    /// <summary>Segoe MDL2 Assets glyph.</summary>
    public string Icon { get; }
    public string Label { get; }
    public string Path { get; }
    public string Size { get; }
}

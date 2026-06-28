namespace SwiftClean.Installer.Models;

/// <summary>A single line in the install log console.</summary>
/// <param name="Text">The line text.</param>
/// <param name="Kind">Drives the line colour (see the converter / styles).</param>
public sealed record InstallLogLine(string Text, LogKind Kind = LogKind.Dim);

public enum LogKind
{
    Dim,        // #38383f — routine
    Info,       // #45454f — info
    Highlight,  // #5b7fff — primary action of a phase
    Error,      // #e05c5c
}

using SwiftClean.Installer.Helpers;

namespace SwiftClean.Installer.Models;

/// <summary>One entry in the left-hand vertical stepper.</summary>
public sealed class WizardStep : ObservableObject
{
    public WizardStep(string label, string sub, bool isLast)
    {
        Label = label;
        Sub = sub;
        IsLast = isLast;
    }

    public string Label { get; }
    public string Sub { get; }
    public bool IsLast { get; }

    private bool _done;
    public bool Done { get => _done; set => SetProperty(ref _done, value); }

    private bool _active;
    public bool Active { get => _active; set => SetProperty(ref _active, value); }

    /// <summary>The connector line below this step is lit once the step is completed.</summary>
    private bool _lineLit;
    public bool LineLit { get => _lineLit; set => SetProperty(ref _lineLit, value); }
}

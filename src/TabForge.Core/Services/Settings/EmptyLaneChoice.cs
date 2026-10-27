namespace TabForge.Services;

// Owns: the stored values of EditingSettings.EmptyLaneClick (what a click on an empty take lane of a tab track shows) and their Preferences labels.
// Does not own: the click itself (TimelineInputController) or the pane switch it asks for (EditorPaneSwitch).
// Tests: TabForge.Avalonia.Tests.EmptyLaneClickTests.
public static class EmptyLaneChoice
{
    public const string Tab = "Tab", PianoRoll = "Piano roll";

    /// <summary>The Preferences labels and the take-lane menu's rows, in the order of <see cref="Values"/>.</summary>
    public static string[] Labels => new[] { "Tab", "Piano roll" };
    public static string[] Values => new[] { Tab, PianoRoll };

    /// <summary>The stored value for any text: an unknown or missing value shows the tab.</summary>
    public static string Normalize(string? stored) => stored == PianoRoll ? PianoRoll : Tab;
}

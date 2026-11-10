namespace TabForge.Services;

// Owns: the stored values of EditingSettings.ComposerDrumDropLane (where a Composer drum sample lands: ask each time, the tab lane, or the MIDI lane) and their Preferences labels.
// Does not own: the lane prompt (DrumSampleLanePromptWindow) or the drop itself (DrumSampleDrop).
// Tests: TabForge.Avalonia.Tests.DrumSampleDropTests.
public static class DrumSampleDropChoice
{
    public const string Ask = "Ask", Tab = "Tab", Midi = "Midi";

    /// <summary>The Preferences labels, in the order of <see cref="Values"/>.</summary>
    public static string[] Labels => new[] { "Ask each time", "Tab lane", "MIDI lane" };
    public static string[] Values => new[] { Ask, Tab, Midi };

    /// <summary>The stored value for any text: an unknown or missing value asks.</summary>
    public static string Normalize(string? stored) => stored is Tab or Midi ? stored : Ask;

    public static string ToLabel(string? stored) => Labels[Array.IndexOf(Values, Normalize(stored))];

    public static string FromLabel(string label) { var i = Array.IndexOf(Labels, label); return i < 0 ? Ask : Values[i]; }

    /// <summary>The value Remember stores for a lane answer.</summary>
    public static string Store(DrumSampleLaneAnswer answer) => answer.Lane == DrumSampleLane.Tab ? Tab : Midi;
}

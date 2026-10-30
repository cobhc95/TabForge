using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>
/// The "Root Scale" highlight text the fretboard and keyboard scale highlight stores, and the root and scale choices of the fretboard's Scale menu.
/// Owns: the highlight format and its split. Does not own: the scale popup (ScalePanel), the detection (SectionScaleDetector) or the scale tables (MusicTheoryService).
/// Tests: TabForge.Avalonia.Tests.ScaleSearchTests.
/// </summary>
public static class ScaleFinderPresenter
{
    /// <summary>Highlight value that clears the fretboard highlight.</summary>
    public const string OffHighlight = "Off";

    public static IReadOnlyList<string> RootChoices => MusicTheoryService.NoteNames;

    public static IReadOnlyList<string> ScaleChoices => MusicTheoryService.Scales.Keys.ToList();

    /// <summary>Splits a saved "Root Scale" highlight; "Off", blank text or text without a space gives neither part.</summary>
    public static (string? Root, string? Scale) Split(string? highlight)
    {
        if (string.IsNullOrWhiteSpace(highlight) || highlight == OffHighlight) return (null, null);
        var space = highlight.IndexOf(' ');
        return space <= 0 ? (null, null) : (highlight[..space], highlight[(space + 1)..]);
    }
}

namespace TabForge.Services;

// Owns: the words of the bar-range Delete prompt and of its Settings row, and the mapping between them and BarRangeAction.
// Does not own: the prompt window (Views/BarRangePrompt) or the edits (BarRangeGaps).
// Tests: TestBarRangeGaps.
public static class BarRangePromptText
{
    public const string AskLabel = "Ask each time";

    /// <summary>The hotkey command that runs each action directly.</summary>
    public static string CommandOf(BarRangeAction action) => action switch
    {
        BarRangeAction.Clear => "Range.Clear",
        BarRangeAction.Remove => "Range.Remove",
        BarRangeAction.InsertBefore => "Range.InsertBefore",
        _ => "Range.InsertAfter",
    };

    public static string Label(BarRangeAction action) => action switch
    {
        BarRangeAction.Clear => "Delete the selected bars – leave a gap",
        BarRangeAction.Remove => "Delete the selected bars – close the gap",
        BarRangeAction.InsertBefore => "Insert a gap before the selection",
        _ => "Insert a gap after the selection",
    };

    private const string TrackSuffix = ":ThisTrack";
    private const string TrackLabel = " (this track)";

    /// <summary>The stored form of a remembered answer: the action name, plus ":ThisTrack" when it applies to the selected track only.</summary>
    public static string Store(BarRangeAction action, bool allTracks) => action + (allTracks ? "" : TrackSuffix);

    private static IEnumerable<(BarRangeAction Action, bool AllTracks)> Answers() =>
        Enum.GetValues<BarRangeAction>().SelectMany(a => new[] { (a, true), (a, false) });

    private static string ChoiceLabel(BarRangeAction action, bool allTracks) => Label(action) + (allTracks ? "" : TrackLabel);

    public static string[] Choices { get; } = new[] { AskLabel }.Concat(Answers().Select(a => ChoiceLabel(a.Action, a.AllTracks))).ToArray();

    public static string ToChoice(string stored) => Remembered(stored) is { } r ? ChoiceLabel(r.Action, r.AllTracks) : AskLabel;

    public static string FromChoice(string label) =>
        Answers().Where(a => ChoiceLabel(a.Action, a.AllTracks) == label).Select(a => Store(a.Action, a.AllTracks)).FirstOrDefault() ?? "Ask";

    /// <summary>The one-line status tip while bars are selected, built from the live key text of each command.</summary>
    public static string Tip(int start, int end, Func<string, string> key)
    {
        string K(string id, string what) => key(id) is { Length: > 0 } k ? $"{k} {what}" : "";
        var parts = new[] { K("Range.Delete", "asks what to do"), K("Range.Remove", "removes and closes the gap"), "right-click for more" }.Where(p => p.Length > 0);
        var label = start == end ? $"Bar {start + 1}" : $"Bars {start + 1}-{end + 1}";
        return $"{label} selected · {string.Join(" · ", parts)}";
    }

    /// <summary>The remembered action and scope, or null when Delete asks.</summary>
    public static (BarRangeAction Action, bool AllTracks)? Remembered(string stored)
    {
        var allTracks = !stored.EndsWith(TrackSuffix, StringComparison.Ordinal);
        var name = allTracks ? stored : stored[..^TrackSuffix.Length];
        return Enum.TryParse<BarRangeAction>(name, out var action) && Enum.IsDefined(action) ? (action, allTracks) : null;
    }
}

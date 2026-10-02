using System.Linq;

namespace TabForge.Services;

// Owns: the text of the Keyboard shortcuts message: a short list of common commands with their live keys.
// Does not own: the bindings (HotkeySettings) or the full list (Settings > Shortcuts).
// Tests: TestNoHardWiredKeyText.
/// <summary>The Keyboard shortcuts help text, built from the user's current bindings so it never shows a stale key.</summary>
public static class ShortcutHelp
{
    private static readonly (string Group, string[] Ids)[] Groups =
    {
        ("File", new[] { "File.New", "File.Open", "File.Save", "File.SaveAs", "File.Print", "Tab.New" }),
        ("Edit", new[] { "Edit.Undo", "Edit.Redo", "Edit.Copy", "Edit.Paste", "Edit.InsertBeat", "Edit.DeleteBeats" }),
        ("Bar", new[] { "Bar.Insert", "Bar.Delete", "Bar.TimeSignature", "Bar.KeySignature", "Bar.RepeatOpen", "Bar.RepeatClose", "Bar.Check" }),
        ("Note", new[] { "Note.Longer", "Note.Shorter", "Note.Dot", "Note.Triplet", "Note.Rest", "Note.Tie", "Note.Chord", "Note.Text" }),
        ("Tracks", new[] { "Track.Add", "Track.Properties", "Bar.ScoreInfo" }),
        ("Sound", new[] { "Transport.PlayPause", "Transport.PlayFromStart", "Transport.Loop", "Transport.Record" }),
        ("View", new[] { "View.ZoomIn", "View.ZoomOut", "View.Multitrack", "View.Fullscreen" }),
    };

    /// <summary>The ids the help text lists (so a test can check each is a catalogue command).</summary>
    public static IEnumerable<string> Ids => Groups.SelectMany(g => g.Ids);

    public static string Build(HotkeySettings settings)
    {
        var lines = new List<string>();
        foreach (var (group, ids) in Groups)
        {
            var entries = ids.Select(id => (Action: HotkeyCatalog.ById(id), Keys: HotkeyCatalog.DisplayAll(settings, id)))
                .Where(x => x.Action is not null && x.Keys.Length > 0)
                .Select(x => $"{x.Action!.Name} {x.Keys}");
            var text = string.Join(", ", entries);
            if (text.Length > 0) lines.Add($"{group}: {text}");
        }
        lines.Add("Every command can have two keys (Hotkey 1 and Hotkey 2): see Settings > Shortcuts.");
        return string.Join("\n", lines);
    }
}

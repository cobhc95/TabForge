using System.Linq;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the menu consolidation changed no default key: every catalogue command that had a default Hotkey 1 / Hotkey 2 before it still has exactly that pair
/// (a removed or moved menu row never unbinds its key), and the commands the menus dropped or moved are still catalogued, so the command palette and the shortcut lists keep them.
/// Does not own: the menu tree (MainWindowMenuTests, InsertMenuTests) or the key routing (HotkeySweepTests).
/// Tests: this class.
/// </summary>
public sealed class MenuHotkeysUnchangedTests
{
    /// <summary>The default keys before the consolidation: id, Hotkey 1, Hotkey 2 ("|" separated).</summary>
    private static readonly string[] Before = new[]
    {
            "File.New|Ctrl+N|",
            "File.Open|Ctrl+O|",
            "File.OpenInNewTab|Ctrl+Shift+O|",
            "File.Save|Ctrl+S|",
            "File.SaveAs|Ctrl+Shift+S|",
            "File.Print|Ctrl+P|",
            "File.PrintPreview|Ctrl+Shift+P|",
            "App.CommandPalette|Ctrl+Shift+A|",
            "File.Render|Ctrl+Alt+R|",
            "Tab.New|Ctrl+Alt+T|",
            "Tab.Close|Ctrl+W|",
            "Tab.Duplicate|Ctrl+Shift+D|",
            "App.Preferences|F12|",
            "App.Shortcuts|F1|",
            "Edit.Undo|Ctrl+Z|",
            "Edit.Redo|Ctrl+Y|",
            "Edit.Copy|Ctrl+C|",
            "Edit.Cut|Ctrl+X|",
            "Edit.Paste|Ctrl+V|",
            "Edit.PasteSpecial|Ctrl+Shift+V|",
            "Edit.SelectAll|Ctrl+A|",
            "Edit.InvertSelection|Ctrl+I|",
            "Edit.InsertBeat|Insert|",
            "Edit.RepeatSelection|Ctrl+Shift+R|",
            "Transport.PlayPause|Space|",
            "Transport.PlayFromStart|Shift+Space|",
            "Playback.SpeedUp|Ctrl+Alt+Up|",
            "Playback.SpeedDown|Ctrl+Alt+Down|",
            "Playback.SpeedReset|Ctrl+Alt+D0|",
            "Transport.Stop|Ctrl+OemPeriod|",
            "Transport.Loop|F9|",
            "Bar.Insert|Ctrl+Insert|",
            "Bar.Delete|Ctrl+Delete|",
            "Bar.TimeSignature|Ctrl+Shift+T|",
            "Beat.MixTable|F10|",
            "Bar.KeySignature|Ctrl+K|",
            "Bar.Clef|K|",
            "Bar.Directions|D|",
            "Bar.GoTo|Ctrl+G|",
            "Reader.ReadBar|Ctrl+Alt+B|",
            "Reader.ReadPosition|Ctrl+Alt+P|",
            "Bar.First|Ctrl+Home|",
            "Bar.Last|Ctrl+End|",
            "Bar.Check|F4|",
            "Bar.ScoreInfo|F5|",
            "Section.Add|M|",
            "Section.Edit|Shift+Insert|",
            "Section.Previous|Alt+Shift+Left|",
            "Section.Next|Alt+Shift+Right|",
            "Note.Chord|A|",
            "Note.Text|T|",
            "Track.Add|Ctrl+T|Ctrl+Shift+Insert",
            "Track.Delete|Ctrl+Shift+Delete|",
            "Track.Properties|F6|",
            "Track.Next|Ctrl+Shift+Down|",
            "Track.Previous|Ctrl+Shift+Up|",
            "View.Multitrack|F3|",
            "View.Global|F8|",
            "View.Fullscreen|F11|",
            "Transport.Record|Ctrl+R|",
            "Clip.Delete|Delete|",
            "Clip.Deselect|Escape|",
            "Clip.NudgeLeft|Left|",
            "Clip.NudgeRight|Right|",
            "Clip.NudgeLeftFine|Shift+Left|",
            "Clip.NudgeRightFine|Shift+Right|",
            "Clip.LaneUp|Up|",
            "Clip.LaneDown|Down|",
            "Clip.Copy|Ctrl+C|",
            "Clip.Cut|Ctrl+X|",
            "Clip.Paste|Ctrl+V|",
            "Clip.Duplicate|Ctrl+D|",
            "Clip.Mute|Ctrl+M|",
            "Clip.Split|S|",
            "Clip.Glue|Ctrl+Shift+G|",
            "Clip.Properties|F2|",
            "TrackRow.Copy|Ctrl+C|",
            "TrackRow.Cut|Ctrl+X|",
            "TrackRow.Paste|Ctrl+V|",
            "TrackRow.Duplicate|Ctrl+D|",
            "TrackRow.Delete|Delete|",
            "Range.Copy|Ctrl+C|",
            "Range.Cut|Ctrl+X|",
            "Range.Paste|Ctrl+V|",
            "Range.PasteSpecial|Ctrl+Shift+V|",
            "Range.Delete|Delete|",
            "Range.Remove|Ctrl+Delete|",
            "Range.InsertBefore|Ctrl+Shift+Space|",
            "Highlight.Copy|Ctrl+C|",
            "Highlight.Cut|Ctrl+X|",
            "Highlight.Paste|Ctrl+V|",
            "Highlight.Delete|Delete|",
            "Timeline.ZoomIn|Ctrl+Alt+Shift+OemPlus|",
            "Timeline.ZoomOut|Ctrl+Alt+Shift+OemMinus|",
            "Timeline.Snap|Alt+S|",
            "Track.MoveUp|Alt+Shift+Up|",
            "Track.MoveDown|Alt+Shift+Down|",
            "View.FollowPlayback|Ctrl+Shift+F|",
            "View.ZoomIn|Ctrl+OemPlus|Ctrl+Add",
            "View.ZoomOut|Ctrl+OemMinus|Ctrl+Subtract",
            "Note.RepeatBeat|C|",
            "Note.Longer|OemMinus|",
            "Note.Shorter|OemPlus|",
            "Note.PitchUp|Shift+Up|",
            "Note.PitchDown|Shift+Down|",
            "Note.MoveStringUp|Alt+Up|",
            "Note.MoveStringDown|Alt+Down|",
            "Note.Rest|R|",
            "Note.Tie|L|",
            "Note.Fermata|F|",
            "Note.Accent|Oem1|",
            "Note.Staccato|Shift+D1|",
            "Note.Tenuto|Shift+OemMinus|",
            "Note.Bend|B|",
            "Note.HammerPull|H|",
            "Note.Vibrato|V|",
            "Note.Slide|S|",
            "Note.LetRing|I|",
            "Note.Dead|X|",
            "Note.Ghost|O|",
            "Note.Harmonic|Y|",
            "Note.Trill|N|",
            "Note.TremoloBar|W|",
            "Note.Grace|G|",
            "Note.PalmMute|P|",
            "Note.FadeIn|Shift+OemComma|",
            "Note.FadeOut|Shift+OemPeriod|",
            "Note.Dot|OemPeriod|Decimal",
            "Note.Triplet|OemQuestion|",
            "Bar.RepeatOpen|OemOpenBrackets|",
            "Bar.RepeatClose|OemCloseBrackets|",
            "Transport.RecordVideo|Ctrl+Alt+V|",
            "View.LearnMode|Ctrl+Alt+L|",
            "Learn.ToggleWait|Ctrl+Alt+W|",
            "Learn.WaitSkip|Ctrl+Alt+Q|",
            "Learn.LookAheadLonger|Ctrl+Alt+OemPlus|",
            "Learn.LookAheadShorter|Ctrl+Alt+OemMinus|",
            "View.LearnPopout|Ctrl+Alt+O|",
            "View.CompactMode|Ctrl+Alt+C|",
    };

    [Fact]
    public void EveryDefaultKeyIsUnchanged()
    {
        var problems = Before.Select(line => line.Split('|')).Where(parts =>
            HotkeyCatalog.ById(parts[0]) is not { } action || action.DefaultGesture != parts[1] || action.DefaultGesture2 != parts[2]).Select(parts => parts[0]).ToList();
        Assert.True(problems.Count == 0, "Changed or missing: " + string.Join(", ", problems));
    }

    [Fact]
    public void NoKeyWasAddedToACommandThatHadNone()
    {
        var had = Before.Select(line => line.Split('|')[0]).ToHashSet();
        var added = HotkeyCatalog.All.Where(a => !HotkeyCatalog.IsFxChainAction(a.Id) && !HotkeyCatalog.IsPianoRollAction(a.Id) && (a.DefaultGesture.Length > 0 || a.DefaultGesture2.Length > 0) && !had.Contains(a.Id)).Select(a => a.Id).ToList();
        Assert.True(added.Count == 0, "New default keys: " + string.Join(", ", added));
    }

    [Fact]
    public void RemovedAndMovedMenuCommandsStayInTheCatalogueForThePalette()
    {
        var kept = new[]
        {
            "Transport.PlayPause", "Transport.PlayFromStart", "Transport.Metronome", "Transport.CountIn", "Options.Stylesheet", "Sound.AudioSetup", "Sound.TestOutput",
            "File.CollectMedia", "File.FindMissingMedia", "File.Render", "Transport.RecordVideo", "App.CommandPalette", "Bar.EmptyBar", "Edit.SelectAll", "Tool.composition:tempo",
            "Track.AddAudio", "Bar.InsertAfter", "Bar.InsertSeveral", "Bar.Append", "Insert.Lyrics", "Insert.AudioFile", "Insert.MidiFile", "Insert.EmptyMidiClip",
        };
        Assert.All(kept, id => Assert.NotNull(HotkeyCatalog.ById(id)));
        Assert.Equal("Clear bar", HotkeyCatalog.ById("Bar.EmptyBar")!.Name);
    }
}

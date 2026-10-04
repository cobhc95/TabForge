using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The timeline's bar-range commands: clear, remove (close the gap) and insert a gap, for every track and for one track, with clips shifted, cut and removed;
/// one undo step each; the prompt texts, settings and key routing (the window scenarios are in SelfTestBarRangeWindow.cs).
/// </summary>
public static partial class SelfTest
{
    private static AudioClip BrClip(double start, double length) => new() { File = "x.wav", Name = "c", StartSec = start, SourceLengthSec = length, FileLengthSec = length + 10 };

    private static SongProject BrSong(out (double S, double E) span)
    {
        var song = DoSong(2, 8);
        song.Tracks[0].Measures[2].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        song.Tracks[1].Measures[5].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 69 });
        song.Tracks[1].Measures[7].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 7, MidiValue = 71 });
        var s = BarRangeGaps.Span(song, 2, 3)!.Value;
        span = (s.From, s.To);
        return song;
    }

    private static void TestBarRangeGaps()
    {
        // Clear: every track / one track; the bar count, sections and clips stay.
        var song = BrSong(out var span);
        song.Tracks[0].AudioClips.Add(BrClip(span.S + 0.1, 0.5));
        Check("clear all tracks: notes gone, bars and clips stay", BarRangeGaps.Clear(song, 2, 3, -1, true) && BarRangeEditor.MaxMeasures(song) == 8
            && song.Tracks[0].Measures[2].Cells.All(c => c.Notes.Count == 0) && song.Tracks[1].Measures[5].Cells[0].Notes.Count == 1 && song.Tracks[0].AudioClips.Count == 1 && song.Markers[1].MeasureIndex == 4);
        song = BrSong(out span);
        BarRangeGaps.Clear(song, 2, 5, 1, false);
        Check("clear one track: only that track is emptied", song.Tracks[0].Measures[2].Cells[0].Notes.Count == 1 && song.Tracks[1].Measures[5].Cells[0].Notes.Count == 0);

        // Remove, all tracks: later bars, markers and clips move earlier; inside clips go; edge clips are cut.
        song = BrSong(out span);
        var d = span.E - span.S;
        var t0 = song.Tracks[0].AudioClips;
        var before = BrClip(0, Math.Min(1, span.S)); var inside = BrClip(span.S + 0.1, 0.5); var left = BrClip(span.S - 0.5, 1);
        var right = BrClip(span.E - 0.5, 1); var after = BrClip(span.E + 0.5, 1); var across = BrClip(span.S - 0.5, d + 1);
        t0.AddRange(new[] { before, inside, left, right, after, across });
        var removed = BarRangeGaps.Remove(song, 2, 3, -1);
        Check("remove all tracks: bars, markers follow", removed is { Map: not null } && BarRangeEditor.MaxMeasures(song) == 6 && song.Markers[1].MeasureIndex == 2
            && song.Tracks[1].Measures[3].Cells[0].Notes.Count == 1 && song.Tracks[0].Measures.All(m => m.Number > 0));
        Check("remove all tracks: a clip before is untouched, one inside is removed", before.StartSec == 0 && !t0.Contains(inside));
        Check("remove all tracks: a clip across the left edge is cut at it", Math.Abs(left.SourceLengthSec - 0.5) < 1e-6 && Math.Abs(left.StartSec - (span.S - 0.5)) < 1e-6);
        Check("remove all tracks: a clip across the right edge starts at the cut, trimmed", Math.Abs(right.StartSec - span.S) < 1e-6 && Math.Abs(right.OffsetSec - 0.5) < 1e-6 && Math.Abs(right.SourceLengthSec - 0.5) < 1e-6);
        Check("remove all tracks: a later clip moves earlier by the range", Math.Abs(after.StartSec - (span.E + 0.5 - d)) < 1e-6);
        Check("remove all tracks: a clip across the whole range keeps both ends, joined", t0.Count == 6 && t0.Count(c => c != before && c != left && c != right && c != after && c != inside) == 2, $"{t0.Count}");

        // Remove, one track: that track shifts left, empty bars fill its end, the bar count stays equal, markers stay, its clips close up.
        song = BrSong(out span);
        song.Tracks[1].AudioClips.Add(BrClip(span.E + 0.5, 1));
        song.Tracks[0].AudioClips.Add(BrClip(span.E + 0.5, 1));
        BarRangeGaps.Remove(song, 2, 3, 1);
        Check("remove one track: counts stay equal, later bars shift left, the end is empty",
            song.Tracks[0].Measures.Count == 8 && song.Tracks[1].Measures.Count == 8 && song.Tracks[1].Measures[3].Cells[0].Notes.Count == 1
            && song.Tracks[1].Measures[7].Cells.All(c => c.Notes.Count == 0) && song.Markers[1].MeasureIndex == 4 && song.Tracks[0].Measures[2].Cells[0].Notes.Count == 1);
        Check("remove one track: its clips move earlier, other tracks' clips stay",
            Math.Abs(song.Tracks[1].AudioClips[0].StartSec - (span.E + 0.5 - (span.E - span.S))) < 1e-6 && Math.Abs(song.Tracks[0].AudioClips[0].StartSec - (span.E + 0.5)) < 1e-6);

        // Insert a gap: before / after, every track / one track.
        song = BrSong(out span);
        song.Tracks[0].AudioClips.Add(BrClip(span.E + 0.5, 1));
        var before2 = BrClip(0, 1); song.Tracks[0].AudioClips.Add(before2);
        var gap = BarRangeGaps.InsertGap(song, 2, 3, before: true, -1, true);
        Check("insert gap before: both tracks grow, the selection and markers move after the gap", gap is { Count: 2 } && BarRangeEditor.MaxMeasures(song) == 10 && song.Tracks[0].Measures[4].Cells[0].Notes.Count == 1
            && song.Tracks[0].Measures[2].Cells.All(c => c.Notes.Count == 0) && song.Markers[1].MeasureIndex == 6);
        Check("insert gap before: clips after move later by the gap, earlier ones stay", Math.Abs(song.Tracks[0].AudioClips[0].StartSec - (span.E + 0.5 + d)) < 1e-6 && before2.StartSec == 0);
        song = BrSong(out span);
        BarRangeGaps.InsertGap(song, 2, 3, before: false, -1, false);
        Check("insert gap after: the selection stays, later bars move", song.Tracks[0].Measures[2].Cells[0].Notes.Count == 1 && song.Tracks[1].Measures[7].Cells[0].Notes.Count == 1 && BarRangeEditor.MaxMeasures(song) == 10);
        song = BrSong(out span);
        BarRangeGaps.InsertGap(song, 2, 3, before: false, 0, false);
        Check("insert gap on one track whose end is empty: it shifts right, nothing grows", song.Tracks.All(t => t.Measures.Count == 8) && song.Tracks[0].Measures[4].Cells[0].Notes.Count == 0 && song.Tracks[0].Measures[2].Cells[0].Notes.Count == 1);
        BarRangeGaps.InsertGap(song, 5, 5, before: true, 1, false);
        Check("insert gap on one track whose end has notes: every track grows to stay equal", song.Tracks.All(t => t.Measures.Count == 9) && song.Tracks[1].Measures[6].Cells[0].Notes.Count == 1 && song.Tracks[1].Measures[8].Cells[0].Notes.Count == 1);

        // Prompt text, settings and key routing.
        var keys = new Dictionary<string, string> { ["Range.Delete"] = "Delete", ["Range.Remove"] = "Ctrl+Delete" };
        Check("tip names the live keys", BarRangePromptText.Tip(144, 148, id => keys.GetValueOrDefault(id, "")) == "Bars 145-149 selected · Delete asks what to do · Ctrl+Delete removes and closes the gap · right-click for more");
        Check("tip skips unbound keys", !BarRangePromptText.Tip(0, 0, _ => "").Contains("Ctrl"));
        Check("settings choice round-trips, with the scope", BarRangePromptText.FromChoice(BarRangePromptText.Label(BarRangeAction.Remove)) == "Remove" && BarRangePromptText.ToChoice("Ask") == BarRangePromptText.AskLabel
            && BarRangePromptText.Remembered("Remove") == (BarRangeAction.Remove, true) && BarRangePromptText.Remembered("Remove:ThisTrack") == (BarRangeAction.Remove, false)
            && BarRangePromptText.FromChoice(BarRangePromptText.ToChoice("InsertAfter:ThisTrack")) == "InsertAfter:ThisTrack" && BarRangePromptText.Remembered("Ask") is null);
        var hotkeys = new HotkeySettings();
        var range = HotkeyCatalog.BuildMap(hotkeys, rangeContext: true);
        var global = HotkeyCatalog.BuildMap(hotkeys);
        Check("range keys: Delete, Ctrl+Delete, Ctrl+Shift+Space; insert-after is unbound", range["Delete"] == "Range.Delete" && range["Ctrl+Delete"] == "Range.Remove" && range["Ctrl+Shift+Space"] == "Range.InsertBefore"
            && HotkeyCatalog.GestureFor(hotkeys, "Range.InsertAfter") == "" && global["Ctrl+Delete"] == "Bar.Delete" && !global.ContainsKey("Ctrl+Shift+Space"));
        Check("routing: only with the timeline focused, bars selected and no clip context",
            BarRangeFlow.Route(true, true, false, range, "Ctrl+Delete") == "Range.Remove" && BarRangeFlow.Route(false, true, false, range, "Ctrl+Delete") is null
            && BarRangeFlow.Route(true, false, false, range, "Delete") == "Range.Delete" && BarRangeFlow.Route(true, true, true, range, "Delete") is null);

        TestBarRangeGapsInWindow();
    }
}

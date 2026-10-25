using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Views;
using TabForge.Views.Score;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the S0 proof for score engraving Job C: the beam groups and stems the real engraver derives from the cells the real editor
//   commands write. Each scenario dumps every cell (index, duration, dots, tuplet, onset, note count) and every beat before asserting.
// Does not own: the engraver (StaffNotationLayoutBuilder), the editor commands (ScoreEditCommands) or any product behaviour.
// Tests: ScoreLayoutEngine.StaffLayoutFor -> StaffNotationLayoutBuilder.CreateLayout/BuildBeamGroups (the renderer path).
public class ScoreEntryBeamTests(ITestOutputHelper output)
{
    // DemoSongFactory bar 5 (0-based) is empty and 4/4; the same bar ScoreEditKeyBehaviourTests writes into.
    private const int Bar = 5;
    private const int HighString = 0;

    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor, TrackModel Track) Make()
    {
        var song = DemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, TabForge.Avalonia.Views.PreferencesCatalog.DefaultHotkeys()), song.Tracks[0]);
    }

    private static bool Press(ScorePreviewEditor editor, Key key, bool ctrl = false, bool shift = false) =>
        editor.Key(AvaloniaEditorKeys.ToEditorKey(key), key.ToString(), ctrl, shift, false);

    private static List<TabCell> Cells(TrackModel track) => track.Measures[Bar].CellsForVoice(0);

    // The keyboard flow: enter fret 0, press Right, repeated. Default settings keep the cursor on the note just written.
    private static void TypeNotes(ScorePreviewEditor editor, ScoreGeometryProbe probe, int count, int denominator)
    {
        probe.Cursor = (Bar, 0, HighString);
        editor.Effects.SetDuration(denominator);
        for (var i = 0; i < count; i++)
        {
            editor.Effects.EnterFret(0);
            Press(editor, Key.Right);
        }
    }

    private static void WriteAtCursor(ScorePreviewEditor editor)
    {
        editor.Effects.EnterFret(0);
        Press(editor, Key.Right);
    }

    /// <summary>The renderer's own entry: ScoreRenderer.DrawMeasure calls this, and it runs StaffNotationLayoutBuilder.CreateLayout.</summary>
    private static StaffNotationMeasureLayout Build(ScoreGeometryProbe probe, TrackModel track)
    {
        var measure = track.Measures[Bar];
        var slots = probe.SlotsFor(Bar);
        var numerator = measure.TimeSigNum ?? probe.Project!.TimeSignatureNumerator;
        var denominator = measure.TimeSigDenom ?? probe.Project!.TimeSignatureDenominator;
        var key = measure.KeySignature ?? probe.Project!.KeySignature;
        var layout = probe.Layout.StaffLayoutFor(track, measure, Bar, slots, 0, 100, 24, numerator, denominator, key, measure.CellsForVoice(0));
        // The same pure grouping the builder ran, called directly so a disagreement is caught here.
        var covered = layout.Beats.Count == 0 ? slots : Math.Max(slots, (int)Math.Ceiling(layout.Beats.Max(beat => beat.StartSlots + beat.DurationSlots)));
        var rebuilt = StaffNotationLayoutBuilder.BuildBeamGroups(layout.Beats, StaffNotationLayoutBuilder.BuildMetricGroups(numerator, denominator, covered));
        var fromLayout = string.Join("|", layout.BeamGroups.Select(group => string.Join(",", group.Beats.Select(beat => beat.CellIndex))));
        var fromBuilder = string.Join("|", rebuilt.Select(group => string.Join(",", group.Beats.Select(beat => beat.CellIndex))));
        Assert.True(fromLayout == fromBuilder, $"BuildBeamGroups disagrees with the layout: [{fromLayout}] vs [{fromBuilder}]");
        return layout;
    }

    private static string Dump(MeasureModel measure, StaffNotationMeasureLayout layout)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"bar {Bar}: {measure.Cells.Count} cells, {layout.Beats.Count} beats, {layout.BeamGroups.Count} beam groups");
        for (var i = 0; i < measure.Cells.Count; i++)
        {
            var cell = measure.Cells[i];
            var position = cell.RhythmicPosition?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  cell {i,2}: DurationDenominator={cell.DurationDenominator} Dots={cell.Dots} Tuplet={cell.Tuplet.Numerator}:{cell.Tuplet.Denominator} RhythmicPosition={position} notes={cell.Notes.Count} rest={cell.IsRest}");
        }
        foreach (var beat in layout.Beats.OrderBy(b => b.StartSlots).ThenBy(b => b.CellIndex))
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  beat cell={beat.CellIndex,2} start={beat.StartSlots:0.###} duration={beat.DurationSlots:0.###} flags={beat.Flags} beamGroup={beat.BeamGroupIndex} stem={beat.HasStem} stemUp={beat.StemUp} notes={beat.Notes.Count}");
        return text.ToString();
    }

    private static string[] GroupCells(StaffNotationMeasureLayout layout) =>
        layout.BeamGroups.Select(group => string.Join(",", group.Beats.Select(beat => beat.CellIndex))).ToArray();

    private static void AssertBeamGroups(StaffNotationMeasureLayout layout, string[] expected, string dump)
    {
        var actual = GroupCells(layout);
        Assert.True(actual.SequenceEqual(expected),
            $"beam groups: expected [{string.Join(" | ", expected)}] but found [{string.Join(" | ", actual)}]\n{dump}");
    }

    private static void AssertStems(StaffNotationMeasureLayout layout, int expected, string dump)
    {
        var stems = layout.Beats.Where(beat => beat.HasStem).ToList();
        Assert.True(stems.Count == expected,
            $"stems: expected {expected} but found {stems.Count} at cells [{string.Join(",", stems.Select(beat => beat.CellIndex))}]\n{dump}");
    }

    /// <summary>Every beat touches the previous one: no overlapping onsets and no gapped onsets.</summary>
    private static void AssertContiguous(StaffNotationMeasureLayout layout, string dump)
    {
        var beats = layout.Beats.OrderBy(beat => beat.StartSlots).ThenBy(beat => beat.CellIndex).ToList();
        Assert.True(beats.Count > 0, $"no beats were laid out\n{dump}");
        Assert.True(Math.Abs(beats[0].StartSlots) < 1e-6,
            $"the first beat starts at {beats[0].StartSlots:0.###}, not 0\n{dump}");
        for (var i = 1; i < beats.Count; i++)
        {
            var previousEnd = beats[i - 1].StartSlots + beats[i - 1].DurationSlots;
            Assert.True(Math.Abs(previousEnd - beats[i].StartSlots) < 1e-6,
                $"onset overlap/gap between cell {beats[i - 1].CellIndex} (ends {previousEnd:0.###}) and cell {beats[i].CellIndex} (starts {beats[i].StartSlots:0.###})\n{dump}");
        }
    }

    /// <summary>S-a: eight typed eighths are four beamed pairs, one stem each.</summary>
    [AvaloniaFact]
    public void FreshBarTypedEighthsBeamInPairs()
    {
        var (probe, editor, track) = Make();
        Assert.False(editor.Tools.AutoAdvanceAfterEntry, "the default cursor must stay on the note after writing");
        TypeNotes(editor, probe, 8, 8);

        var layout = Build(probe, track);
        var dump = Dump(track.Measures[Bar], layout);
        output.WriteLine(dump);

        AssertBeamGroups(layout, new[] { "0,2", "4,6", "8,10", "12,14" }, dump);
        AssertStems(layout, 8, dump);
        AssertContiguous(layout, dump);
        Assert.All(layout.Beats, beat => Assert.True(beat.Flags == 1, $"cell {beat.CellIndex} is not an eighth (flags {beat.Flags})\n{dump}"));
    }

    /// <summary>S-b: sixteen typed sixteenths are four beamed fours, one stem each.</summary>
    [AvaloniaFact]
    public void FreshBarTypedSixteenthsBeamInFours()
    {
        var (probe, editor, track) = Make();
        TypeNotes(editor, probe, 16, 16);

        var layout = Build(probe, track);
        var dump = Dump(track.Measures[Bar], layout);
        output.WriteLine(dump);

        AssertBeamGroups(layout, new[] { "0,1,2,3", "4,5,6,7", "8,9,10,11", "12,13,14,15" }, dump);
        AssertStems(layout, 16, dump);
        AssertContiguous(layout, dump);
    }

    /// <summary>S-c: a two-note chord in one cell is one beat, one stem spanning both heads.</summary>
    [AvaloniaFact]
    public void TypedChordSharesOneStem()
    {
        var (probe, editor, track) = Make();
        probe.Cursor = (Bar, 0, 0);
        editor.Effects.SetDuration(8);
        editor.Effects.EnterFret(0);
        Assert.True(editor.Effects.ToggleFretAtPosition(2, 2), "the second fret must join the current cell");

        var layout = Build(probe, track);
        var dump = Dump(track.Measures[Bar], layout);
        output.WriteLine(dump);

        var beat = Assert.Single(layout.Beats);
        Assert.True(layout.BeamGroups.Count == 0, $"a single beat cannot beam, but found {layout.BeamGroups.Count} groups\n{dump}");
        Assert.True(beat.Notes.Count == 2, $"expected 2 notes in the chord, found {beat.Notes.Count}\n{dump}");
        Assert.True(beat.Notes.Select(note => note.Source.StringIndex).Distinct().Count() == 2, $"the chord lost a string\n{dump}");
        AssertStems(layout, 1, dump);
        var stemTop = Math.Min(beat.StemStartY, beat.StemEndY);
        var stemBottom = Math.Max(beat.StemStartY, beat.StemEndY);
        Assert.True(stemTop <= beat.Notes.Min(note => note.Y) + 1e-6 && stemBottom >= beat.Notes.Max(note => note.Y) - 1e-6,
            $"the one stem {stemTop:0.###}..{stemBottom:0.###} does not span every head\n{dump}");
    }

    /// <summary>S-d: quarter, eighth, eighth, quarter, four sixteenths: the two eighths beam together and the four sixteenths beam together.</summary>
    [AvaloniaFact]
    public void MixedBarBeamsByBeat()
    {
        var (probe, editor, track) = Make();
        probe.Cursor = (Bar, 0, HighString);

        editor.Effects.SetDuration(4);
        WriteAtCursor(editor);                 // quarter at 0
        editor.Effects.SetDuration(8);
        WriteAtCursor(editor);                 // eighth at 4
        WriteAtCursor(editor);                 // eighth at 6
        editor.Effects.SetDuration(4);
        WriteAtCursor(editor);                 // quarter at 8
        editor.Effects.SetDuration(16);
        WriteAtCursor(editor);                 // sixteenths at 12
        WriteAtCursor(editor);                 // 13
        WriteAtCursor(editor);                 // 14
        WriteAtCursor(editor);                 // 15

        var layout = Build(probe, track);
        var dump = Dump(track.Measures[Bar], layout);
        output.WriteLine(dump);

        AssertBeamGroups(layout, new[] { "4,6", "12,13,14,15" }, dump);
        AssertStems(layout, 8, dump);
        AssertContiguous(layout, dump);
    }

    /// <summary>S-e: shortening the second quarter to an eighth, then writing an eighth in the freed half-beat, beams the two eighths.</summary>
    [AvaloniaFact]
    public void ShortenedQuarterAndFollowingEighthBeamTogether()
    {
        var (probe, editor, track) = Make();
        probe.Cursor = (Bar, 0, HighString);
        editor.Effects.SetDuration(4);
        for (var i = 0; i < 4; i++)
        {
            editor.Effects.EnterFret(0);
            if (i < 3) Press(editor, Key.Right);
        }
        var quarters = track.Measures[Bar].Cells.Count(cell => cell.Notes.Count > 0);
        Assert.True(quarters == 4, $"expected 4 written quarters before the edit, found {quarters}");

        // The second quarter is at cell 4: Home, Right; then "+" shortens it by default.
        Press(editor, Key.Home);
        Press(editor, Key.Right);
        Assert.True(editor.Cursor is { } cursor && cursor.Measure == Bar && cursor.Cell == 4, $"the cursor is not on the second quarter: {editor.Cursor}");
        Assert.True(Press(editor, Key.OemPlus, shift: true), "the + key must shorten note values by default");
        Assert.True(Cells(track)[4].DurationDenominator == 8, "the second quarter was not shortened to an eighth");

        // Right skips the freed half-beat at slot 6 because the gap has no cell of its own; the user's next eighth goes there.
        Press(editor, Key.Right);
        var rightCell = editor.Cursor!.Value.Cell;
        editor.SetPosition(Bar, 6, HighString);   // the freed slot right after the shortened eighth
        editor.Effects.EnterFret(0);

        var layout = Build(probe, track);
        var dump = Dump(track.Measures[Bar], layout) + $"  (Right after the shortened cell {4} landed on cell {rightCell}; the freed slot 6 is not a cursor stop)\n";
        output.WriteLine(dump);

        AssertBeamGroups(layout, new[] { "4,6" }, dump);
        AssertStems(layout, 5, dump);
        AssertContiguous(layout, dump);
        Assert.All(layout.Beats, beat => Assert.True(
            beat.StartSlots is 0 or 4 or 6 or 8 or 12,
            $"unexpected onset {beat.StartSlots:0.###} at cell {beat.CellIndex}\n{dump}"));
    }

    /// <summary>S-f control: the same eight eighths stored the way an import stores them (explicit RhythmicPosition) beam identically to S-a.</summary>
    [AvaloniaFact]
    public void StoredEighthBarBeamsLikeTheTypedBar()
    {
        var (probe, _, track) = Make();
        var cells = Cells(track);
        foreach (var index in new[] { 0, 2, 4, 6, 8, 10, 12, 14 })
        {
            var cell = cells[index];
            cell.DurationDenominator = 8;
            cell.RhythmicPosition = index;   // an import always stores the exact onset, even when it equals the cell index
            cell.Notes.Add(new TabNote { StringIndex = HighString, Fret = 0, MidiValue = track.NoteMidi(HighString, 0), Velocity = 100 });
        }

        var layout = Build(probe, track);
        var dump = Dump(track.Measures[Bar], layout);
        output.WriteLine(dump);

        AssertBeamGroups(layout, new[] { "0,2", "4,6", "8,10", "12,14" }, dump);   // S-a's groups
        AssertStems(layout, 8, dump);
        AssertContiguous(layout, dump);
    }
}

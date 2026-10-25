using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score note-edit gaps (tie pitch/no-op/round-trip, effect round-trip as one undo
/// step each, and rest-fill delete keeping the bar length).
/// Tests: ScoreEditCommands (Marks, Ties), ScorePreviewEditor, BarTiming.
/// Possible bug: ScoreEditCommands.ToggleTie (Ties.cs:16) writes a fret-0 tied note when
/// nothing comes before; the tests use TieSelectedNote (Marks.cs:90) which no-ops per the rule.
/// </summary>
public class ScoreNoteEditTests
{
    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor) TwoQuartersSamePitch()
    {
        var song = new SongProject { Tempo = 120 };
        var track = new TrackModel
        {
            Name = "G",
            Kind = TrackKind.Guitar,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
            Measures = new List<MeasureModel> { new MeasureModel { Number = 1 } }
        };
        foreach (var i in new[] { 0, 4 })
        {
            var cell = track.Measures[0].Cells[i];
            cell.DurationDenominator = 4; // MusicTime.cs:36
            cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) }); // SongProject.cs:151
        }
        song.Tracks.Add(track);
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, Views.PreferencesCatalog.DefaultHotkeys()));
    }

    private static TabCell Beat(ScoreGeometryProbe probe, int m, int c) =>
        probe.Project!.Tracks[0].Measures[m].CellsForVoice(0)[c]; // SongProject.cs:254

    [AvaloniaFact]
    public void TiedNote_ContinuesPreviousPitchSameString()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        probe.Cursor = (0, 4, 0);
        Assert.True(editor.Effects.CanTieSelectedNote()); // Marks.cs:77
        editor.Effects.TieSelectedNote(); // Marks.cs:90
        var before = Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0);
        var tied = Beat(probe, 0, 4).Notes.Single(n => n.StringIndex == 0);
        Assert.True(tied.Tied);
        Assert.Equal(before.Fret, tied.Fret);
        Assert.Equal(before.MidiValue, tied.MidiValue);
    }

    [AvaloniaFact]
    public void ToggleTieTwice_ReturnsToUntied()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        probe.Cursor = (0, 4, 0);
        editor.Effects.TieSelectedNote();
        Assert.True(Beat(probe, 0, 4).Notes.Single(n => n.StringIndex == 0).Tied);
        editor.Effects.TieSelectedNote();
        var note = Beat(probe, 0, 4).Notes.Single(n => n.StringIndex == 0);
        Assert.False(note.Tied);
        Assert.Equal(5, note.Fret);
    }

    [AvaloniaFact]
    public void TieOnFirstNote_IsNoOp()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        probe.Cursor = (0, 0, 0);
        Assert.False(editor.Effects.CanTieSelectedNote()); // Marks.cs:77 nothing compatible before
        editor.Effects.TieSelectedNote();
        var note = Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0);
        Assert.False(note.Tied);
        Assert.Single(Beat(probe, 0, 0).Notes);
    }

    [AvaloniaFact]
    public void EffectToggleOnOff_RestoresOriginalNoteExactly()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        probe.Cursor = (0, 0, 0);
        var note = Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0);
        var (fret, midi, vel, techs) = (note.Fret, note.MidiValue, note.Velocity, note.Techniques.ToArray());
        editor.Effects.ToggleTechnique(TechniqueNames.PalmMute); // Marks.cs:222 -> EditCommands.Notes.cs:84
        Assert.Contains(TechniqueNames.PalmMute, Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0).Techniques);
        editor.Effects.ToggleTechnique(TechniqueNames.PalmMute);
        var restored = Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0);
        Assert.Equal(techs, restored.Techniques.ToArray());
        Assert.Equal((fret, midi, vel), (restored.Fret, restored.MidiValue, restored.Velocity));
    }

    [AvaloniaFact]
    public void EachEffectToggle_IsOneUndoStep()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        probe.Cursor = (0, 0, 0);
        editor.Effects.ToggleTechnique(TechniqueNames.PalmMute);
        editor.Effects.ToggleTechnique(TechniqueNames.PalmMute); // two edits
        Assert.True(editor.Undo()); // ScorePreviewEditor.cs:331 undoes the second toggle only
        Assert.Contains(TechniqueNames.PalmMute, Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0).Techniques);
        Assert.True(editor.Undo()); // undoes the first toggle
        Assert.DoesNotContain(TechniqueNames.PalmMute, Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0).Techniques);
        Assert.False(editor.Undo());
    }

    [AvaloniaFact]
    public void VibratoAndLetRing_RoundTrip()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        probe.Cursor = (0, 0, 0);
        foreach (var fx in new[] { TechniqueNames.Vibrato, TechniqueNames.LetRing }) // TechniqueNames.cs:16,23
        {
            editor.Effects.ToggleTechnique(fx);
            Assert.Contains(fx, Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0).Techniques);
            editor.Effects.ToggleTechnique(fx);
            Assert.DoesNotContain(fx, Beat(probe, 0, 0).Notes.Single(n => n.StringIndex == 0).Techniques);
        }
    }

    [AvaloniaFact]
    public void DeleteNoteWithRestFill_KeepsBarLength()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        editor.Tools.FillBarsWithRests = true; // ScoreEditToolState.cs:30
        editor.Tools.MergeRestsOnDelete = false; // ScoreEditToolState.cs:32 keep the same-length rest
        probe.Cursor = (0, 0, 0);
        var measure = probe.Project!.Tracks[0].Measures[0];
        var usedBefore = BarTiming.VoiceUsedTicks(measure, 1); // BarTiming.cs:98
        var denomBefore = Beat(probe, 0, 0).DurationDenominator;
        editor.Effects.DeleteNote(); // Marks.cs:282
        var cell = Beat(probe, 0, 0);
        Assert.Empty(cell.Notes);
        Assert.True(cell.IsRest);
        Assert.Equal(denomBefore, cell.DurationDenominator);
        Assert.Equal(usedBefore, BarTiming.VoiceUsedTicks(measure, 1));
    }

    [AvaloniaFact]
    public void DeleteNote_UndoRestoresNote()
    {
        var (probe, editor) = TwoQuartersSamePitch();
        editor.Tools.FillBarsWithRests = true;
        editor.Tools.MergeRestsOnDelete = false;
        probe.Cursor = (0, 4, 0);
        editor.Effects.DeleteNote();
        Assert.Empty(Beat(probe, 0, 4).Notes);
        Assert.True(editor.Undo());
        var note = Beat(probe, 0, 4).Notes.Single(n => n.StringIndex == 0);
        Assert.Equal(5, note.Fret);
        Assert.False(Beat(probe, 0, 4).IsRest);
    }
}

using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Avalonia trill, grace-note and harmonic editors on the cursor note: OK applies the note's own starting values as one undoable edit.
/// Tests: OrnamentEditorFlow (the three Open methods, Undo through the score).
/// </summary>
public class OrnamentEditorFlowTests
{
    /// <summary>A score with one note on string 1 at fret 3 in beat 0 of bar 0, the cursor on it.</summary>
    private static ScorePreviewView ScoreWithNote(out TabNote note)
    {
        var view = new ScorePreviewView();
        var track = view.Editor.Project!.Tracks[0];
        note = new TabNote { StringIndex = 1, Fret = 3, MidiValue = track.PitchOf(1, 3) };
        track.Measures[0].CellsForVoice(0)[0].Notes.Add(note);
        view.Editor.SetPosition(0, 0, 1);
        return view;
    }

    private static Task<EffectValuesAnswer?> AnswerOk(EffectValuesWindow _) => Task.FromResult<EffectValuesAnswer?>(EffectValuesAnswer.Ok);

    [AvaloniaFact]
    public async Task TrillOkAddsTheTrillTwoFretsUpAndUndoRemovesIt()
    {
        var view = ScoreWithNote(out var note);
        var flow = new OrnamentEditorFlow(view, AnswerOk);
        Assert.True(await flow.OpenTrill());
        Assert.Contains(TechniqueNames.Trill, note.Techniques);
        Assert.Equal(view.Editor.Project!.Tracks[0].PitchOf(1, 5), note.TrillTargetMidi);
        Assert.Equal(16, note.TrillDurationDenominator);

        Assert.True(view.Run("Edit.Undo"));
        var undone = view.Editor.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0].Notes.Single();   // undo restores the song's own copy of the note
        Assert.DoesNotContain(TechniqueNames.Trill, undone.Techniques);
    }

    [AvaloniaFact]
    public async Task GraceOkAddsAGraceNoteOnTheNoteStringAndUndoRemovesIt()
    {
        var view = ScoreWithNote(out var note);
        var flow = new OrnamentEditorFlow(view, AnswerOk);
        Assert.True(await flow.OpenGrace());
        var grace = view.Editor.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0].Notes.Single(n => n.IsGraceNote);
        Assert.Equal(note.StringIndex, grace.StringIndex);
        Assert.Equal(1, grace.Fret);   // the editor's default step of -2 from fret 3

        Assert.True(view.Run("Edit.Undo"));
        Assert.DoesNotContain(view.Editor.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0].Notes, n => n.IsGraceNote);
    }

    [AvaloniaFact]
    public async Task HarmonicOkAddsTheNaturalHarmonicAndHotkeyRemovesIt()
    {
        var view = ScoreWithNote(out var note);
        var flow = new OrnamentEditorFlow(view, AnswerOk);
        Assert.True(await flow.OpenHarmonic());
        Assert.Contains(TechniqueNames.Harmonic, note.Techniques);

        Assert.True(await flow.OpenOrRemoveHarmonic());
        Assert.DoesNotContain(TechniqueNames.Harmonic, note.Techniques);
    }

    [AvaloniaFact]
    public async Task GraceWithNoNoteOnTheCursorSaysSoAndOpensNothing()
    {
        var view = new ScorePreviewView();
        view.Editor.SetPosition(0, 0, 1);   // an empty cell: no principal note
        string? status = null;
        var flow = new OrnamentEditorFlow(view, AnswerOk, text => status = text);
        Assert.False(await flow.OpenGrace());
        Assert.Equal("Select a note to edit its grace note.", status);
    }

    private static void Click(EffectValuesWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task OwnPresetsAreSavedInSettingsReloadedAndDeletedForGood()
    {
        var view = ScoreWithNote(out _);
        var settings = new TabForge.Services.AppSettings();
        var writes = 0;
        int firstRows = 0, secondRows = 0;
        var save = new OrnamentEditorFlow(view, window => { firstRows = window.PresetRows.Count; Click(window, "SavePresetButton"); return Task.FromResult<EffectValuesAnswer?>(null); },
            settings: () => settings, saveSettings: () => writes++);
        await save.OpenTrill();
        Assert.Single(settings.EffectPresets);
        Assert.Equal(1, writes);

        var reopen = new OrnamentEditorFlow(view, window =>
        {
            secondRows = window.PresetRows.Count;
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Name == "PresetList");
            list.SelectedIndex = list.ItemCount - 1;
            Click(window, "DeletePresetButton");
            return Task.FromResult<EffectValuesAnswer?>(null);
        }, settings: () => settings, saveSettings: () => writes++);
        await reopen.OpenTrill();
        Assert.Equal(firstRows + 1, secondRows);
        Assert.Empty(settings.EffectPresets);
        Assert.Equal(2, writes);
    }
}

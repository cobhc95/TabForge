using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Views.EffectEditors;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the vibrato settings window's Remove button on the cursor note: it takes the vibrato and its own depth and speed off as one undoable edit,
/// and the window shows Remove (not Clean) with the Remove answer wired to it.
/// Does not own: the window fields (VibratoEditorWindow) or the model writes (Core VibratoEdits).
/// Tests: VibratoEditorFlow (Open with Remove, Undo through the score).
/// </summary>
public class VibratoEditorFlowTests
{
    /// <summary>A score with one note on string 1 at fret 3 in beat 0 of bar 0, with vibrato of its own (depth 0.6, speed 7), the cursor on it.</summary>
    private static ScorePreviewView ScoreWithVibratoNote()
    {
        var view = new ScorePreviewView();
        var track = view.Editor.Project!.Tracks[0];
        var note = new TabNote { StringIndex = 1, Fret = 3, MidiValue = track.PitchOf(1, 3), VibratoDepth = 0.6, VibratoSpeed = 7 };
        note.Techniques.Add(TechniqueNames.Vibrato);
        track.Measures[0].CellsForVoice(0)[0].Notes.Add(note);
        view.Editor.SetPosition(0, 0, 1);
        return view;
    }

    private static TabNote NoteOnCursor(ScorePreviewView view) => view.Editor.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0].Notes.Single();

    [AvaloniaFact]
    public async Task RemoveClearsTheNoteVibratoAndUndoRestoresIt()
    {
        var view = ScoreWithVibratoNote();
        var flow = new VibratoEditorFlow(view, window =>
        {
            Assert.Single(window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "RemoveButton");
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "CleanButton");
            return Task.FromResult<EffectValuesAnswer?>(EffectValuesAnswer.Clean);
        });
        Assert.True(await flow.Open());
        var removed = NoteOnCursor(view);
        Assert.False(VibratoEdits.Has(removed));
        Assert.Null(removed.VibratoDepth);
        Assert.Null(removed.VibratoSpeed);

        Assert.True(view.Run("Edit.Undo"));
        var restored = NoteOnCursor(view);
        Assert.True(VibratoEdits.Has(restored));
        Assert.Equal((0.6, 7.0), (restored.VibratoDepth, restored.VibratoSpeed));
    }
}

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.EffectEditors;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Avalonia bend and tremolo-bar curve editor: a point drag changes the curve, and OK applies the curve to the cursor note as one undo step.
/// Tests: CurveEditorView, CurveEffectWindow, EffectCurveFlow (with EffectCurve and EffectEdits in Core).
/// </summary>
public class EffectCurveEditorTests
{
    private const double PlotCheckOffset = 30, DragOffset = 40, LowValue = 2, HighValue = 4;

    /// <summary>Lays the editor out at its own size, so its pixel mapping matches what a pointer sees.</summary>
    private static CurveEditorView Laid(EffectCurve curve)
    {
        var view = new CurveEditorView(curve);
        view.Measure(new Size(view.Width, view.Height));
        view.Arrange(new Rect(0, 0, view.Width, view.Height));
        return view;
    }

    [AvaloniaFact]
    public void DraggingAPointMovesItAndTheCurveFollows()
    {
        var curve = new EffectCurve(0, 6);
        var view = Laid(curve);
        var plot = view.PlotRect(view.Bounds.Size);
        Assert.True(view.PressLeft(view.ToPixel(plot, PlotCheckOffset, LowValue), 1));
        Assert.Equal(3, curve.Points.Count);
        Assert.Equal(1, view.Selected);

        view.DragTo(view.ToPixel(plot, DragOffset, HighValue));
        view.Release();

        Assert.Equal(DragOffset, curve.Points[1].Offset);
        Assert.Equal(HighValue, curve.Points[1].Value);
    }

    [AvaloniaFact]
    public void HoverOutlinesThePointUnderThePointer()
    {
        var curve = new EffectCurve(0, 6);
        var view = Laid(curve);
        var plot = view.PlotRect(view.Bounds.Size);
        var handle = view.ToPixel(plot, 0, 0);

        view.HoverAt(handle);
        Assert.Equal(0, view.Hovered);
        view.HoverAt(view.ToPixel(plot, PlotCheckOffset, HighValue));
        Assert.Equal(-1, view.Hovered);
    }

    /// <summary>A score with one plain note (string 1, fret 3) alone in beat 0 of bar 0, the cursor on it: the demo song's own notes may already carry effects.</summary>
    private static ScorePreviewView ScoreWithPlainNote()
    {
        var view = new ScorePreviewView();
        var track = view.Editor.Project!.Tracks[0];
        var cell = track.Measures[0].CellsForVoice(0)[0];
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 1, Fret = 3, MidiValue = track.PitchOf(1, 3) });
        view.Editor.SetPosition(0, 0, 1);
        return view;
    }

    [AvaloniaFact]
    public async Task OkAppliesTheCurveToTheCursorNoteAsOneUndoStep()
    {
        var score = ScoreWithPlainNote();
        var owner = new Window { Content = score };
        var settings = new AppSettings();
        var host = new AvaloniaDialogHost
        {
            Capture = window =>
            {
                var dialog = (CurveEffectWindow)window;
                dialog.Curve.SetPoints(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = EffectCurve.Span, Value = HighValue } });
                return CurveEffectAnswer.Ok;
            },
        };

        var changed = await EffectCurveFlow.Open(score, owner, host, () => settings, () => { }, EffectEditorKind.Bend);

        Assert.True(changed);
        var note = Assert.Single(score.Editor.Effects.EffectNotes());
        Assert.Contains(TechniqueNames.Bend, note.Techniques);
        Assert.Equal(2, note.BendPoints.Count);
        Assert.Equal(HighValue, note.BendPoints[1].Value);

        Assert.True(score.Run("Edit.Undo"));
        Assert.Empty(Assert.Single(score.Editor.Effects.EffectNotes()).BendPoints);
    }

    [AvaloniaFact]
    public async Task CancelChangesNothing()
    {
        var score = ScoreWithPlainNote();
        var owner = new Window { Content = score };
        var host = new AvaloniaDialogHost { Capture = _ => CurveEffectAnswer.Cancel };

        var changed = await EffectCurveFlow.Open(score, owner, host, () => new AppSettings(), () => { }, EffectEditorKind.Bend);

        Assert.False(changed);
        Assert.Empty(Assert.Single(score.Editor.Effects.EffectNotes()).BendPoints);
    }
}

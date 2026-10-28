using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presets;
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

    [Fact]
    public void ImportedPointsKeepTheirTimeAndValueAndTheAxisWidens()
    {
        var curve = new EffectCurve(0, EffectCurve.BendMaxValue);
        curve.SetPoints(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 33, Value = 18 }, new BendPointModel { Offset = 60, Value = 18 } });

        Assert.Equal(33, curve.Points[1].Offset);
        Assert.Equal(18, curve.Points[1].Value);
        Assert.Equal(20, curve.MaxValue);   // whole tones: 18 quarter tones shows on a 5 tone axis
        var tremolo = new EffectCurve(EffectCurve.TremoloMinValue, EffectCurve.TremoloMaxValue);
        tremolo.SetPoints(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 60, Value = -30 } });
        Assert.Equal(-30, tremolo.Points[1].Value);
        Assert.True(tremolo.MinValue <= -30);
    }

    [Fact]
    public void PlacedPointsSnapToATwelfthUnlessAskedNotTo()
    {
        var curve = new EffectCurve(0, EffectCurve.BendMaxValue);
        Assert.Equal(55, curve.Points[curve.Add(57, 2)].Offset);
        Assert.Equal(57, curve.Points[curve.Add(57, 2, snap: false)].Offset);
    }

    [Fact]
    public void ToneLabelsReadOnTheToneScale()
    {
        Assert.Equal(new[] { "0", "¼", "½", "¾", "Full", "1¼", "1½", "3" }, new[] { 0.0, 1, 2, 3, 4, 5, 6, 12 }.Select(v => EffectCurveShape.ToneLabel(v, bend: true)));
        Assert.Equal(new[] { "-3", "-½", "0", "+1", "+1½" }, new[] { -12.0, -2, 0, 4, 6 }.Select(v => EffectCurveShape.ToneLabel(v, bend: false)));
    }

    [Fact]
    public void TheEaseRunsThroughTheEndsAndTheMiddle()
    {
        Assert.Equal(0, EffectCurveShape.Between(0, 4, 0));
        Assert.Equal(2, EffectCurveShape.Between(0, 4, 0.5));
        Assert.Equal(4, EffectCurveShape.Between(0, 4, 1));
        Assert.True(EffectCurveShape.Between(0, 4, 0.25) < 1);   // slow start: below the straight line
    }

    [Fact]
    public void TypesThatStartFromTheNoteBeforeNeedABendThere()
    {
        var release = EffectCurveTypes.Bend.Single(t => t.Name == "Release");
        Assert.False(EffectCurveTypes.Available(release, 0));
        Assert.True(EffectCurveTypes.Available(release, 4));
        Assert.Equal(6, EffectCurveTypes.Points(release, 6)[0].Value);
        Assert.Equal(new[] { "Bend", "Release", "Bend/Release", "Hold", "Prebend", "Prebend/Bend", "Prebend/Release" }, EffectCurveTypes.Bend.Select(t => t.Name));
        Assert.Equal(new[] { "Dive", "Dip", "Hold", "Predive", "Predive/Dive" }, EffectCurveTypes.Tremolo.Take(5).Select(t => t.Name));
    }

    [AvaloniaFact]
    public void ChoosingATypeSetsTheCurveAndResetPutsTheStartBack()
    {
        var start = new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = EffectCurve.Span, Value = 2 } };
        var window = new CurveEffectWindow(EffectEditorKind.Bend, "Bend editor", 0, EffectCurve.BendMaxValue, start, new EffectPresetStore(new AppSettings()), false, () => { });

        var rows = window.Side.TypeRows;
        Assert.False(rows.Single(r => r.Name == "Release").Enabled);
        Assert.False(rows.Single(r => r.Name == "Hold").Enabled);
        Assert.True(rows.Single(r => r.Name == "Prebend").Enabled);
        window.Side.PickType(EffectCurveTypes.Bend.Single(t => t.Name == "Release"));
        Assert.Equal(2, window.Curve.Points[^1].Value);   // unavailable: nothing changed

        window.Side.PickType(EffectCurveTypes.Bend.Single(t => t.Name == "Prebend"));
        Assert.Equal(4, window.Curve.Points[0].Value);
        window.Reset();
        Assert.Equal(0, window.Curve.Points[0].Value);
        Assert.Equal(2, window.Curve.Points[^1].Value);
    }

    [AvaloniaFact]
    public void ReleaseAndHoldAreAvailableAfterABend()
    {
        var start = new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = EffectCurve.Span, Value = 0 } };
        var window = new CurveEffectWindow(EffectEditorKind.Bend, "Bend editor", 0, EffectCurve.BendMaxValue, start, new EffectPresetStore(new AppSettings()), false, () => { }, previous: 6);

        Assert.True(window.Side.TypeRows.Single(r => r.Name == "Hold").Enabled);
        window.Side.PickType(EffectCurveTypes.Bend.Single(t => t.Name == "Hold"));
        Assert.All(window.Curve.Points, p => Assert.Equal(6, p.Value));
    }

    [AvaloniaFact]
    public void EveryTypeRowCarriesAShapeIconAndGreyedRowsStayListed()
    {
        var start = new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = EffectCurve.Span, Value = 0 } };
        foreach (var kind in new[] { EffectEditorKind.Bend, EffectEditorKind.TremoloBar })
        {
            var window = new CurveEffectWindow(kind, "Editor", 0, EffectCurve.BendMaxValue, start, new EffectPresetStore(new AppSettings()), false, () => { });
            Assert.Equal(EffectCurveTypes.Of(kind).Count, window.Side.TypeRows.Count);
            Assert.Equal(EffectCurveTypes.Of(kind).Count, window.Side.TypeIconCount);
        }
    }

    [Fact]
    public void ATremoloBarPlaysInStraightLinesAndABendEases()
    {
        var wheelAt120 = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var tremolo in new[] { true, false })
        {
            var song = new SongProject { Title = "T", Tempo = 120 };
            var track = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiChannel = 0, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
            song.Tracks.Add(track);
            var cell = track.Measures[0].Cells[0];
            cell.DurationDenominator = 4;
            var note = new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) };
            var points = new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 60, Value = -8 } };
            if (tremolo) { cell.WhammyPoints = points.ToList(); note.Techniques.Add("TremBar"); }
            else { note.BendPoints = points.ToList(); note.BendTypeName = "Custom"; note.Techniques.Add("Bend"); }
            cell.Notes.Add(note);
            var timeline = TabForge.Playback.MidiTimelineBuilder.Build(song, new TabForge.Playback.PlaybackOptions { StartBar = 0 });
            var wheel = timeline.Events.Where(e => (e.Status & 0xF0) == 0xE0 && Math.Abs(e.TimeMs - 120) < 1).Select(e => e.Data2 << 7 | e.Data1).Single();
            wheelAt120[tremolo ? "tremolo" : "bend"] = wheel;
        }
        Assert.InRange(wheelAt120["tremolo"], 7500, 7560);   // 24 % of a 4 semitone dive, straight: about -0.96 semitone
        Assert.InRange(wheelAt120["bend"], 7760, 7830);       // the same stretch eased: about -0.58 semitone
    }
}

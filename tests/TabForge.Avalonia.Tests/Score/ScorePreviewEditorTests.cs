using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the preview score's editing: click to cursor through the Core hit rule, arrow and Home/End moves through ScoreCursorNavigator,
/// a typed digit writing the fret on the song model, Ctrl+Z / Ctrl+Y undo and redo, delete and duration keys, catalogue note keys,
/// and the selection (Shift+arrows, drag) and hover the renderer sees.
/// Tests: ScorePreviewView, ScorePreviewEditor, AvaloniaEditorKeys.
/// </summary>
public class ScorePreviewEditorTests
{
    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor, Models.TrackModel Track) Make()
    {
        var song = DemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, Views.PreferencesCatalog.DefaultHotkeys()), song.Tracks[0]);
    }

    private static bool Press(ScorePreviewEditor editor, Key key, bool ctrl = false, bool shift = false) =>
        editor.Key(AvaloniaEditorKeys.ToEditorKey(key), key.ToString(), ctrl, shift, false);

    private static Models.TabCell FirstBeat(ScoreGeometryProbe probe) => probe.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0];

    [AvaloniaFact]
    public void CtrlZThenCtrlYRedoesTheFret()
    {
        var (probe, editor, track) = Make();
        var beat = track.Measures[0].CellsForVoice(0)[0];
        var stringIndex = Enumerable.Range(0, track.StringTunings.Count).First(s => beat.Notes.All(n => n.StringIndex != s));
        probe.Cursor = (0, 0, stringIndex);
        Press(editor, Key.D7);
        Assert.True(Press(editor, Key.Z, ctrl: true));
        Assert.DoesNotContain(FirstBeat(probe).Notes, n => n.StringIndex == stringIndex);
        Assert.True(Press(editor, Key.Y, ctrl: true));
        Assert.Equal(7, FirstBeat(probe).Notes.Single(n => n.StringIndex == stringIndex).Fret);
        Assert.False(Press(editor, Key.Y, ctrl: true));
    }

    [AvaloniaFact]
    public void NoteValueCommandsSetTheBeatAndBarCommandsEditEveryTrackAsOneUndoStep()
    {
        var (probe, editor, track) = Make();
        probe.Cursor = (1, 0, 0);
        Assert.True(editor.RunCommand("Note.Eighth"));
        Assert.True(editor.RunCommand("Note.SixtyFourth"));
        var song = probe.Project!;
        int Bars() => probe.Project!.Tracks.Max(t => t.Measures.Count);
        var bars = Bars();
        var before = song.Markers.Select(m => m.MeasureIndex).ToArray();

        Assert.True(editor.RunCommand("Bar.Insert"));
        Assert.Equal(bars + 1, Bars());
        Assert.All(song.Tracks, t => Assert.Equal(bars + 1, t.Measures.Count));
        Assert.Equal((1, 0), (editor.Cursor!.Value.Measure, editor.Cursor!.Value.Cell));
        Assert.True(editor.RunCommand("Edit.Undo"));
        Assert.Equal(bars, Bars());

        Assert.True(editor.RunCommand("Bar.Duplicate"));
        Assert.Equal(bars + 1, Bars());
        Assert.Equal(2, editor.Cursor!.Value.Measure);
        Assert.True(editor.RunCommand("Edit.Undo"));

        probe.Cursor = (1, 0, 0);
        Assert.True(editor.RepeatSelection(2));
        Assert.Equal(bars + 2, Bars());
        Assert.True(editor.RunCommand("Edit.Undo"));
        Assert.Equal(bars, Bars());

        Assert.True(editor.RunCommand("Bar.Delete"));
        Assert.Equal(bars - 1, Bars());
        Assert.Equal(0, editor.Cursor!.Value.Measure);
        Assert.True(editor.RunCommand("Edit.Undo"));
        Assert.Equal(bars, Bars());
        Assert.Equal(before, probe.Project!.Markers.Select(m => m.MeasureIndex).ToArray());
    }

    [AvaloniaFact]
    public void BarDeleteAsksFirstWhenTheConfirmSettingIsOn()
    {
        var (probe, editor, _) = Make();
        int Bars() => probe.Project!.Tracks.Max(t => t.Measures.Count);
        var bars = Bars();
        int? asked = null;
        editor.ConfirmDeleteBarSetting = () => true;
        editor.AskDeleteBar = bar => asked = bar;
        probe.Cursor = (2, 0, 0);

        Assert.True(editor.RunCommand("Bar.Delete"));
        Assert.Equal(2, asked);
        Assert.Equal(bars, Bars());   // nothing is deleted until the host confirms

        Assert.True(editor.DeleteBarConfirmed(2));
        Assert.Equal(bars - 1, Bars());
        Assert.True(editor.RunCommand("Edit.Undo"));
        Assert.Equal(bars, Bars());
    }

    [AvaloniaFact]
    public void DeleteDurationAndCatalogueKeysEditTheBeat()
    {
        var (probe, editor, track) = Make();
        var stringIndex = Enumerable.Range(0, track.StringTunings.Count).First(s => FirstBeat(probe).Notes.All(n => n.StringIndex != s));
        probe.Cursor = (0, 0, stringIndex);
        Press(editor, Key.D3);   // a note to work on; by default the cursor stays on it
        probe.Cursor = (0, 0, stringIndex);
        var denominator = FirstBeat(probe).DurationDenominator;
        Assert.True(Press(editor, Key.OemMinus));   // Note.Longer
        Assert.True(FirstBeat(probe).DurationDenominator < denominator);
        Assert.True(Press(editor, Key.OemPeriod));   // Note.Dot
        Assert.Equal(1, FirstBeat(probe).Dots);
        Assert.True(Press(editor, Key.H));   // Note.HammerPull
        Assert.Contains(Models.TechniqueNames.Hopo, FirstBeat(probe).Notes.Single(n => n.StringIndex == stringIndex).Techniques);
        var notes = FirstBeat(probe).Notes.Count;
        Assert.True(Press(editor, Key.Back));   // DeleteNote
        Assert.Equal(notes - 1, FirstBeat(probe).Notes.Count);
        Assert.True(Press(editor, Key.R));   // Note.Rest
        Assert.True(FirstBeat(probe).IsRest);
        Assert.False(Press(editor, Key.Q));
    }

    [AvaloniaFact]
    public void ShiftArrowsAndDragSelectAndHoverIsShown()
    {
        var (probe, editor, track) = Make();
        probe.Cursor = (0, 0, 0);
        Assert.True(Press(editor, Key.Right, shift: true));
        Assert.True(Press(editor, Key.Right, shift: true));
        Assert.True(probe.HasSelection && editor.HasSelection);
        Assert.Equal((0, 0), (probe.SelectionRange().m1, probe.SelectionRange().c1));
        Assert.Equal((probe.Selection.EndMeasure, probe.Selection.EndCell), (editor.Cursor!.Value.Measure, editor.Cursor!.Value.Cell));
        Press(editor, Key.Right);
        Assert.False(probe.HasSelection);

        var system = probe.Layout.GetLayout(track).Systems[0];
        var y = probe.TabTop(0);
        editor.Click(system.Measures[0].X + 2, y);
        Assert.True(editor.DragTo(system.Measures[1].X + system.Measures[1].Width * 0.5, y));
        Assert.True(probe.HasSelection);
        Assert.Equal(1, probe.SelectionRange().m2);
        Assert.True(editor.HoverAt(system.Measures[2].X + 2, y));
        Assert.Equal(system.Measures[2].MeasureIndex, probe.HoverMeasure);
        Assert.True(editor.HoverEnd());
        Assert.Equal(-1, probe.HoverMeasure);
    }

    [AvaloniaFact]
    public void PlainClickWithJitterSelectsNothingAndDragSelectsAndClickElsewhereClears()
    {
        var (probe, editor, track) = Make();
        var system = probe.Layout.GetLayout(track).Systems[0];
        var y = probe.TabTop(0);
        var x = system.Measures[0].X + 2;
        foreach (var jitter in new[] { 0.0, 1, 2, 3 })
        {
            editor.Click(x, y);
            var cursor = editor.Cursor;
            editor.DragTo(x + jitter, y + jitter);
            editor.Release();
            Assert.False(editor.HasSelection);
            Assert.Equal(cursor, editor.Cursor);
        }
        editor.Click(x, y);
        Assert.True(editor.DragTo(x + 20, y));
        editor.Release();
        Assert.True(editor.HasSelection);
        editor.Click(system.Measures[1].X + 2, y);
        Assert.False(editor.HasSelection);
        Assert.False(editor.DragTo(system.Measures[1].X + 3, y));
        editor.Release();
        Assert.False(editor.DragTo(x + 40, y));
    }

    [AvaloniaFact]
    public void ClickPlacesCursorWhereTheEditorRuleChooses()
    {
        var (probe, editor, track) = Make();
        var system = probe.Layout.GetLayout(track).Systems[0];
        var bar = system.Measures[1];
        var x = bar.X + bar.Width * 0.6;
        var y = probe.TabTop(0) + 2 * probe.StringGap;
        editor.Click(x, y);
        var slot = probe.Layout.WarpFor(track, bar.MeasureIndex).SlotAt((x - bar.X) / bar.Width);
        var expected = CursorPositions.Resolve(track.Measures[bar.MeasureIndex].CellsForVoice(0), slot, probe.SlotsFor(bar.MeasureIndex));
        Assert.Equal((bar.MeasureIndex, expected, 2), editor.Cursor);
    }

    [AvaloniaFact]
    public void ArrowsAndHomeEndMoveLikeTheNavigator()
    {
        var (probe, editor, track) = Make();
        var stops = new ScoreCursorStops(track, m => m.CellsForVoice(0), probe.SlotsFor);
        probe.Cursor = (0, 0, 0);
        Assert.True(Press(editor, Key.Right));
        var next = ScoreCursorNavigator.ForwardBeat(stops, 0, 0)!.Value;
        Assert.Equal((next.Measure, next.Cell, 0), editor.Cursor);
        Press(editor, Key.Left);
        Assert.Equal((0, 0, 0), editor.Cursor);
        Press(editor, Key.Down);
        Assert.Equal(1, editor.Cursor!.Value.String);
        Press(editor, Key.Up); Press(editor, Key.Up);
        Assert.Equal(ScoreCursorNavigator.WrapString(0, -1, track.StringTunings.Count), editor.Cursor!.Value.String);
        Press(editor, Key.End);
        Assert.Equal(ScoreCursorNavigator.BarEnd(stops, 0, 0), editor.Cursor!.Value.Cell);
        Press(editor, Key.Home);
        Assert.Equal(stops.Snap(0, 0), editor.Cursor!.Value.Cell);
    }

    [AvaloniaFact]
    public void TypedDigitWritesFretAndUndoRestoresIt()
    {
        var (probe, editor, track) = Make();
        var beat = track.Measures[0].CellsForVoice(0)[0];
        var stringIndex = Enumerable.Range(0, track.StringTunings.Count).First(s => beat.Notes.All(n => n.StringIndex != s));
        probe.Cursor = (0, 0, stringIndex);
        var before = beat.Notes.Count;
        Assert.True(Press(editor, Key.D5));
        var note = beat.Notes.Single(n => n.StringIndex == stringIndex);
        Assert.Equal(5, note.Fret);
        Assert.Equal(track.NoteMidi(stringIndex, 5), note.MidiValue);
        Assert.True(editor.Undo());
        var restored = probe.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0];
        Assert.Equal(before, restored.Notes.Count);
        Assert.DoesNotContain(restored.Notes, n => n.StringIndex == stringIndex);
        Assert.False(editor.Undo());
    }

    [AvaloniaFact]
    public void UndoKeepsTheSelectionTheStepRestored()
    {
        var (probe, editor, _) = Make();
        var document = new TabForge.Documents.SongDocument(() => null) { Project = DemoSongFactory.Create() };
        probe.ReplaceSong(document.Project);
        editor.ShowDocument(document);
        var sync = new TabForge.Controllers.SelectionSync(document.Selection, editor, _ => { });
        editor.SelectionChanged += (_, _) => sync.PushFromEditor();
        editor.SelectRange(1, 0, 2, -1);
        Assert.Equal((1, 2), document.Selection.BarRange);
        var track = document.Project.Tracks[0];
        var stringIndex = Enumerable.Range(0, track.StringTunings.Count).First(s => track.Measures[1].CellsForVoice(0)[0].Notes.All(n => n.StringIndex != s));
        TabForge.Documents.DocumentEdits.Run(document, p => { p.Tracks[0].Measures[1].CellsForVoice(0)[0].Notes.Clear(); return true; });
        document.Selection.Clear(TabForge.Models.SelectionOrigin.Command);
        Assert.False(document.Selection.HasRange);
        Assert.True(editor.Undo());
        Assert.Equal((1, 2), document.Selection.BarRange);
        Assert.True(editor.HasSelection);
        _ = stringIndex;
    }

    [AvaloniaFact]
    public void TwoQuickDigitsWriteOneFretInOneUndoStep()
    {
        var (probe, editor, track) = Make();
        var beat = track.Measures[0].CellsForVoice(0)[0];
        var stringIndex = Enumerable.Range(0, track.StringTunings.Count).First(s => beat.Notes.All(n => n.StringIndex != s));
        probe.Cursor = (0, 0, stringIndex);
        Press(editor, Key.D1);
        Press(editor, Key.D2);
        Assert.Equal(12, beat.Notes.Single(n => n.StringIndex == stringIndex).Fret);
        Assert.True(editor.Undo());
        Assert.DoesNotContain(probe.Project!.Tracks[0].Measures[0].CellsForVoice(0)[0].Notes, n => n.StringIndex == stringIndex);
        Assert.False(editor.Undo());
    }

    [AvaloniaFact]
    public void RestFillOnLeavesTheBarGapFreeAfterAnEighthAndOneUndoRemovesIt()
    {
        var (probe, editor, track) = Make();
        editor.Tools.FillBarsWithRests = true;
        editor.Tools.DurationDenominator = 8;
        var measure = track.Measures[5];
        var slots = probe.SlotsFor(5);
        var before = measure.CellsForVoice(0).Select(cell => (cell.Notes.Count, cell.IsRest)).ToArray();
        probe.Cursor = (5, 0, 0);

        editor.Effects.EnterFret(5, autoAdvance: false);

        var cells = measure.CellsForVoice(0);
        Assert.Single(cells[0].Notes);
        Assert.Equal(5, cells[0].Notes[0].Fret);
        Assert.Single(cells.SelectMany(cell => cell.Notes));
        var pos = 0;
        var first = true;
        Assert.True(cells.Count >= slots, "bar is shorter than its slot count");
        while (pos < slots)
        {
            var at = cells[pos];
            Assert.True(at.Notes.Count > 0 || at.IsRest, $"slot {pos} is a gap");
            var len = TabForge.Services.MusicTime.CellSlotsRounded(at);
            if (first) Assert.True(at.Notes.Count > 0, "first event is not the written eighth");
            else Assert.True(at.IsRest, $"event at slot {pos} is not a rest");
            Assert.True(len >= 1 && pos + len <= slots, $"event at slot {pos} overruns the bar");
            pos += len;
            first = false;
        }
        Assert.Equal(slots, pos);

        Assert.True(editor.Undo());
        var restored = probe.Project!.Tracks[0].Measures[5].CellsForVoice(0);
        Assert.Equal(before, restored.Select(cell => (cell.Notes.Count, cell.IsRest)).ToArray());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionAndHoverRenderInBothThemes(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var view = new ScorePreviewView();
        var (probe, editor) = (view.Probe, view.Editor);
        var window = new global::Avalonia.Controls.Window { Width = 1280, Height = 800, Content = view };
        probe.Cursor = (0, 0, 0);
        Press(editor, Key.Right, shift: true);
        Press(editor, Key.Right, shift: true);
        var bar = probe.Layout.GetLayout(probe.Track!).Systems[0].Measures[2];
        Assert.True(editor.HoverAt(bar.X + 4, probe.TabTop(0)));
        var path = DialogTestKit.SavePng(window, nameof(ScorePreviewEditorTests), $"score-preview-editor-{(light ? "light" : "dark")}.png");
        Assert.True(System.IO.File.Exists(path));
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize(1280, 800), new global::Avalonia.Vector(96, 96));
        bitmap.Render(view);
        var paper = AvaloniaDrawContextTests.Pixel(bitmap, 2, 2);
        // The window's paper, measured on its default light and dark captures.
        Assert.Equal(light ? new UiColor(0xFF, 0xFA, 0xFA, 0xF7) : new UiColor(0xFF, 0x15, 0x18, 0x1D), paper);
        window.Close();
    }

    /// <summary>The preview's paper, ink and staff lines are the editor's with default settings (AppearanceSettings), in both papers.</summary>
    [AvaloniaFact]
    public void ScorePaperColoursMatchTheWpfEditorSettings()
    {
        var wpf = new TabForge.Services.AppearanceSettings();
        UiColor Hex(string hex) => TabForge.Platform.Theme.Palette.Parse(hex);
        var appearance = new ScorePreviewView().Probe.Appearance;
        Assert.Equal(Hex(wpf.LightScorePaperColour), appearance.LightPaperColor);
        Assert.Equal(Hex(wpf.DarkScorePaperColour), appearance.DarkPaperColor);
        Assert.Equal(Hex(wpf.LightScoreInkColour), appearance.LightInkColor);
        Assert.Equal(Hex(wpf.DarkScoreInkColour), appearance.DarkInkColor);
        Assert.Equal(Hex(wpf.LightScoreLinesColour), appearance.LightStaffLineColor);
        Assert.Equal(Hex(wpf.DarkScoreLinesColour), appearance.DarkStaffLineColor);
    }
}

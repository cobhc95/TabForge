using System.IO;
using System.Linq;
using System.Windows;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.EffectEditors;
using TabForge.Views.Score;

namespace TabForge;

public static partial class SelfTest
{
    // REGION:effect-editors
    private static EffectPresetEntry EePreset(EffectEditorKind kind, string name) => EffectPresetStore.BuiltIn(kind).First(p => p.Name == name);
    private static string EeText(IEnumerable<BendPointModel> p) => string.Join(" ", p.Select(x => $"{x.Offset:0.##}:{x.Value:0.##}"));

    /// <summary>The curve rules, the geometry of the curve control and the preset lists, without a window.</summary>
    private static void TestEffectCurveMath()
    {
        var curve = new EffectCurve(0, 6);
        Check("curve: a new curve is flat from the start to the end of the note", EeText(curve.Points) == "0:0 60:0");
        var i = curve.Add(31.9, 4.4);
        Check("curve: a click adds a point snapped to the grid, between its neighbours", i == 1 && EeText(curve.Points) == "0:0 30:4 60:0", EeText(curve.Points));
        curve.Add(30, 99);
        Check("curve: a point at an occupied time sets that point's value (clamped to the range) instead of adding another", EeText(curve.Points) == "0:0 30:6 60:0", EeText(curve.Points));
        curve.Move(1, 100, 2);
        Check("curve: a dragged point stays between its neighbours in time", EeText(curve.Points) == "0:0 55:2 60:0", EeText(curve.Points));
        curve.Move(0, 20, 3);
        curve.Move(2, 20, -5);
        Check("curve: the end points only move up and down, within the range", EeText(curve.Points) == "0:3 55:2 60:0", EeText(curve.Points));
        Check("curve: the end points cannot be removed", !curve.RemoveAt(0) && !curve.RemoveAt(2) && curve.Points.Count == 3);
        Check("curve: a middle point can be removed", curve.RemoveAt(1) && EeText(curve.Points) == "0:3 60:0");
        curve.SetPoints(new[] { new BendPointModel { Offset = 45, Value = 4 }, new BendPointModel { Offset = 15, Value = 4 } });
        Check("curve: an imported curve is ordered and gets end points at both ends", EeText(curve.Points) == "0:4 15:4 45:4 60:4", EeText(curve.Points));
        curve.SetPoints(new[] { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 0.5, Value = 2 }, new BendPointModel { Offset = 1, Value = 1 } });
        Check("curve: offsets given as a fraction of the note (0..1) are read as 0..60", EeText(curve.Points) == "0:0 30:2 60:1", EeText(curve.Points));

        foreach (var kind in new[] { EffectEditorKind.Bend, EffectEditorKind.TremoloBar })
        {
            var c = kind == EffectEditorKind.Bend ? new EffectCurve(0, 6) : new EffectCurve(-12, 12);
            foreach (var preset in EffectPresetStore.BuiltIn(kind))
            {
                c.SetPoints(preset.Points);
                Check($"preset {kind} / {preset.Name}: choosing it fills the curve with exactly its points (all on the grid and in range)", EeText(c.Points) == EeText(preset.Points), $"{EeText(c.Points)} vs {EeText(preset.Points)}");
            }
        }
        Check("presets: bend has the five built-in names in order", string.Join("|", EffectPresetStore.BuiltIn(EffectEditorKind.Bend).Select(p => p.Name)) == "Bend|Bend/Release|Bend/Release/Bend|Pre-Bend|Pre-Bend/Release");
        Check("presets: tremolo bar has the six built-in names in order", string.Join("|", EffectPresetStore.BuiltIn(EffectEditorKind.TremoloBar).Select(p => p.Name)) == "Dip|Dive|Release up|Inverted Dip|Return|Release down");

        // geometry of the control: pixel <-> (offset, value), and which point a click hits
        var editor = new EffectCurveEditor(new EffectCurve(0, 6));
        var plot = editor.PlotRect(new Size(560, 240));
        var px = editor.ToPixel(plot, 30, 4);
        var (offset, value) = editor.FromPixel(plot, px);
        Check("curve control: a point's pixel position converts back to the same offset and value", Math.Abs(offset - 30) < 1e-9 && Math.Abs(value - 4) < 1e-9);
        editor.Curve.SetPoints(EffectPresetStore.BuiltIn(EffectEditorKind.Bend)[0].Points);
        Check("curve control: a click on a point hits it; a click on empty grid does not", editor.HitPoint(plot, new Point(px.X + 3, px.Y - 3)) == 1 && editor.HitPoint(plot, new Point(px.X + 40, px.Y - 40)) == -1);
        Check("curve control: the axis reads in semitones", EffectCurveEditor.Label(2) == "+1" && EffectCurveEditor.Label(-4) == "-2" && EffectCurveEditor.Label(0) == "0");

        // user presets: stored in the settings keyed by editor kind, survive a settings save and load
        var settings = new AppSettings();
        var store = new EffectPresetStore(settings);
        Check("presets: a user preset is saved and listed after the built-in ones", store.Save(EffectEditorKind.Bend, "  My hook ", EePreset(EffectEditorKind.Bend, "Bend/Release").Points)
            && store.List(EffectEditorKind.Bend).Last() is { BuiltIn: false, Preset.Name: "My hook" } && store.List(EffectEditorKind.TremoloBar).All(p => p.BuiltIn));
        Check("presets: saving the same name again replaces it; a built-in name, an empty name and a full list are refused",
            store.Save(EffectEditorKind.Bend, "my HOOK", EePreset(EffectEditorKind.Bend, "Pre-Bend").Points) && settings.EffectPresets.Count == 1 && EeText(settings.EffectPresets[0].Points) == "0:4 60:4"
            && !store.Save(EffectEditorKind.Bend, "Bend", new List<BendPointModel>()) && !store.Save(EffectEditorKind.Bend, " ", new List<BendPointModel>())
            && Enumerable.Range(0, EffectPresetStore.MaxPerKind).Count(n => store.Save(EffectEditorKind.Bend, "p" + n, new List<BendPointModel>())) == EffectPresetStore.MaxPerKind - 1);
        var path = Path.Combine(Path.GetTempPath(), $"tabforge-effect-presets-{Environment.ProcessId}.json");
        try
        {
            SettingsFileService.SaveAtomic(path, settings);
            var back = SettingsFileService.Load(path);
            Check("presets: user presets survive saving and loading the settings", back.EffectPresets.Count == settings.EffectPresets.Count && back.EffectPresets.Any(p => p.Name == "my HOOK" || p.Name == "My hook") );
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
        Check("presets: a user preset can be deleted, a built-in one cannot", store.Delete(EffectEditorKind.Bend, "p0") && !store.Delete(EffectEditorKind.Bend, "Bend"));
    }

    private sealed class EePipeline : IScoreEditHost
    {
        public EePipeline(SongProject project) => Session = DocumentSession.FromProject(project, null);
        public DocumentSession Session { get; }
        public bool Run(Func<SongProject, bool> edit, bool invalidatesTimeline = true) => DocumentEdits.Run(Session, edit, invalidatesTimeline: invalidatesTimeline).Changed;
    }

    private sealed class EeHost : IEffectEditorHost
    {
        public EeHost(ScoreEditCommands edits) => Edits = edits;
        public Window Owner => null!;
        public ScoreEditCommands? Edits { get; }
        public AppSettings Settings { get; } = new();
        public void SaveSettings() { }
        public void Say(string text) { }
    }

    /// <summary>OK / Clean / Cancel of the bend and tremolo-bar editors: apply, remove and no-op, one undo step each, selection and chords.</summary>
    private static void TestEffectEditors()
    {
        var project = TemplateFactory.Create("Rock Band");
        var pipeline = new EePipeline(project);
        var editor = new TabForge.Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, EditHost = pipeline };
        var track = project.Tracks[0];
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = track.PitchOf(1, 5), Velocity = 95 });
        cell.Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = track.PitchOf(2, 5), Velocity = 95 });
        editor.SetPosition(0, 0, 1, false);
        var flow = new EffectEditorFlow(new EeHost(editor.Effects));
        var note = cell.Notes[0];
        var undo = pipeline.Session.Undo;

        editor.SetPosition(0, 0, 4, false);
        var none = flow.Open(EffectEditorKind.Bend, d => EditorAnswer.Ok);
        editor.SetPosition(0, 0, 1, false);
        Check("bend editor: with no note under the cursor nothing opens and nothing changes", !none && undo.UndoCount == 0 && note.BendPoints.Count == 0);

        var cancelled = flow.Open(EffectEditorKind.Bend, d => { d.Pick(EePreset(EffectEditorKind.Bend, "Bend/Release")); return EditorAnswer.Cancel; });
        Check("bend editor: Cancel changes nothing and stores no undo step", !cancelled && note.BendPoints.Count == 0 && !note.Techniques.Contains(TechniqueNames.Bend) && undo.UndoCount == 0);

        var ok = flow.Open(EffectEditorKind.Bend, d =>
        {
            Check("bend editor: opened on a note without a bend, it shows the first preset", EeText(d.Curve.Points) == EeText(EePreset(EffectEditorKind.Bend, "Bend").Points));
            d.Pick(EePreset(EffectEditorKind.Bend, "Bend/Release"));
            return EditorAnswer.Ok;
        });
        Check("bend editor: OK sets the bend curve and the Bend technique on that note only, in one undo step",
            ok && EeText(note.BendPoints) == "0:0 20:4 40:4 60:0" && note.Techniques.Contains(TechniqueNames.Bend) && cell.Notes[1].BendPoints.Count == 0 && undo.UndoCount == 1);
        flow.Open(EffectEditorKind.Bend, d =>
        {
            Check("bend editor: reopened, it shows the note's own curve", EeText(d.Curve.Points) == "0:0 20:4 40:4 60:0");
            return EditorAnswer.Ok;
        });
        Check("bend editor: OK with the curve unchanged is a no-op (no undo step)", undo.UndoCount == 1);

        var compiled = ScoreToMidiCompiler.BendCurve(note);
        Check("bend editor: playback uses the drawn points (up a whole tone by a third of the note, held, back to pitch at the end)",
            compiled.Length == 4 && Math.Abs(compiled[1].Fraction - 1.0 / 3) < 1e-9 && Math.Abs(compiled[1].Semitones - 2) < 1e-9 && Math.Abs(compiled[2].Fraction - 2.0 / 3) < 1e-9 && compiled[3].Semitones == 0,
            string.Join(" ", compiled.Select(p => $"{p.Fraction:0.###}:{p.Semitones}")));
        var peak = (Func<SongProject, int>)(p => MidiTimelineBuilder.Build(p, new PlaybackOptions()).Events.Where(e => (e.Status & 0xF0) == 0xE0).Select(e => e.Data1 | (e.Data2 << 7)).DefaultIfEmpty(8192).Max());
        var customPeak = peak(project);
        note.BendPoints[1].Value = 6; note.BendPoints[2].Value = 6;
        Check("bend editor: a higher drawn peak sounds higher in the compiled timeline", peak(project) > customPeak, $"{customPeak} -> {peak(project)}");
        note.BendPoints[1].Value = 4; note.BendPoints[2].Value = 4;

        flow.Open(EffectEditorKind.Bend, d => { d.Curve.Add(30, 2); d.Editor.Refresh(); return EditorAnswer.Ok; });
        Check("bend editor: a point added by hand is stored (and counts as one more undo step)", EeText(note.BendPoints) == "0:0 20:4 30:2 40:4 60:0" && undo.UndoCount == 2, EeText(note.BendPoints));

        var cleaned = flow.Open(EffectEditorKind.Bend, d => EditorAnswer.Clean);
        Check("bend editor: Clean removes the bend and its curve, one undo step", cleaned && note.BendPoints.Count == 0 && !note.Techniques.Contains(TechniqueNames.Bend) && undo.UndoCount == 3);
        Check("bend editor: Clean on a note without a bend is a no-op", !flow.Open(EffectEditorKind.Bend, d => EditorAnswer.Clean) && undo.UndoCount == 3);

        // tremolo bar: a beat-level curve plus the technique on every note of the chord
        var tremolo = flow.Open(EffectEditorKind.TremoloBar, d =>
        {
            Check("tremolo bar editor: the first preset (Dip) is shown for a beat without a curve", EeText(d.Curve.Points) == "0:0 30:-4 60:0");
            d.Pick(EePreset(EffectEditorKind.TremoloBar, "Dive"));
            return EditorAnswer.Ok;
        });
        Check("tremolo bar editor: OK sets the beat's whammy curve and the technique on both notes of the chord, one undo step",
            tremolo && EeText(cell.WhammyPoints) == "0:0 60:-8" && cell.Notes.All(n => n.Techniques.Contains(TechniqueNames.TremoloBar)) && undo.UndoCount == 4);
        Check("tremolo bar editor: the compiled timeline bends down for the Dive", MidiTimelineBuilder.Build(project, new PlaybackOptions()).Events.Any(e => (e.Status & 0xF0) == 0xE0 && (e.Data1 | (e.Data2 << 7)) < 8192 - 1000));
        cell.Notes[0].Techniques.Add("TremBarDip");
        flow.Open(EffectEditorKind.TremoloBar, d => EditorAnswer.Ok);
        Check("tremolo bar editor: OK drops a stale shape tag so the drawn curve is the only one that plays", !cell.Notes[0].Techniques.Contains("TremBarDip") && undo.UndoCount == 5);
        Check("tremolo bar editor: Clean removes the curve and the technique", flow.Open(EffectEditorKind.TremoloBar, d => EditorAnswer.Clean)
            && cell.WhammyPoints.Count == 0 && cell.Notes.All(n => !n.Techniques.Any(t => t.StartsWith("TremBar", StringComparison.Ordinal))) && undo.UndoCount == 6);

        // the dialog frame: OK is the default button, Cancel answers Esc, Clean is disabled when there is nothing to remove
        var dialog = new CurveEffectDialog(EffectEditorKind.Bend, "Bend editor", 0, 6, EffectPresetStore.BuiltIn(EffectEditorKind.Bend)[0].Points, new EffectPresetStore(new AppSettings()), canClean: false);
        Check("editor dialog: OK is the default button, Clean is disabled with no effect to remove, the tab order runs curve, presets, save, delete, OK, Clean, Cancel",
            dialog.Dialog.OkButton.IsDefault && !dialog.Dialog.CleanButton.IsEnabled && dialog.Dialog.Result == EditorAnswer.Cancel
            && System.Windows.Input.KeyboardNavigation.GetTabIndex(dialog.Editor) == 0 && dialog.Dialog.OkButton.TabIndex < dialog.Dialog.CleanButton.TabIndex);
        Check("editor dialog: the bend list shows the five built-in presets", dialog.ListedPresets.Count == 5 && dialog.ListedPresets.All(p => p.BuiltIn));
        dialog.Dialog.Close();
    }

    /// <summary>The bend curve and the tremolo-bar curve survive .tforge and .gp saving and reopening.</summary>
    private static void TestEffectEditorFiles()
    {
        var folder = RtFolder();
        try
        {
            var kinds = new (string Name, string Curve, string GpBack)[]
            {
                ("Bend", "0:0 30:4 60:4", ""), ("Bend/Release", "0:0 20:4 40:4 60:0", ""), ("Pre-Bend", "0:4 60:4", ""), ("Pre-Bend/Release", "0:4 30:4 60:0", ""),
            };
            var song = GfSong(2, t =>
            {
                GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95));
                GfPut(t, 0, 4, 4, RtNote(t, 1, 5, 95));
                GfPut(t, 0, 8, 4, RtNote(t, 1, 7, 95));
                GfPut(t, 0, 12, 4, RtNote(t, 1, 8, 95));
                GfPut(t, 1, 0, 4, RtNote(t, 2, 3, 95));
            });
            var beats = song.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
            for (var n = 0; n < kinds.Length; n++) EffectEdits.ApplyBend(beats[n].Notes, EffectPresetStore.BuiltIn(EffectEditorKind.Bend).First(p => p.Name == kinds[n].Name).Points);
            var tremoloCell = song.Tracks[0].Measures[1].Cells.First(c => c.Notes.Count > 0);
            EffectEdits.ApplyTremolo(new[] { tremoloCell }, EffectPresetStore.BuiltIn(EffectEditorKind.TremoloBar).First(p => p.Name == "Dip").Points);

            var tforge = RtCopy(song);
            var tBeats = tforge.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
            Check(".tforge: all four bend curves and their Bend technique come back exactly",
                Enumerable.Range(0, 4).All(n => EeText(tBeats[n].Notes[0].BendPoints) == kinds[n].Curve && tBeats[n].Notes[0].Techniques.Contains(TechniqueNames.Bend)),
                string.Join(" | ", tBeats.Select(c => EeText(c.Notes[0].BendPoints))));
            var tTrem = tforge.Tracks[0].Measures[1].Cells.First(c => c.Notes.Count > 0);
            Check(".tforge: the tremolo-bar curve and technique come back exactly", EeText(tTrem.WhammyPoints) == "0:0 30:-4 60:0" && tTrem.Notes[0].Techniques.Contains(TechniqueNames.TremoloBar));

            var back = GfClean(song, folder, "effect-editors");
            var gBeats = GfBeats(back, 0).Where(c => c.Notes.Count > 0).ToList();
            var gpCurves = string.Join(" | ", gBeats.Select(c => GfBend(c.Notes[0].BendPoints)));
            Check(".gp: a bend, a bend/release, a pre-bend and a pre-bend/release come back as drawn (the file holds a hold at the end as its last point)",
                gBeats.Count == 4 && GfBend(gBeats[0].Notes[0].BendPoints) == "0:0 30:4" && GfBend(gBeats[1].Notes[0].BendPoints) == "0:0 20:4 40:4 60:0"
                && gBeats[2].Notes[0].BendPoints.All(p => p.Value == 4) && gBeats[3].Notes[0].BendPoints.Count >= 2 && gBeats[3].Notes[0].BendPoints[0].Value == 4 && gBeats[3].Notes[0].BendPoints[^1].Value == 0, gpCurves);
            var gTrem = GfBeats(back, 1).First(c => c.Notes.Count > 0);
            Check(".gp: the tremolo-bar curve (down and back up) comes back", gTrem.WhammyPoints.Count >= 3 && gTrem.WhammyPoints.Min(p => p.Value) < 0 && gTrem.WhammyPoints[0].Value == 0 && gTrem.WhammyPoints[^1].Value == 0,
                string.Join(" ", gTrem.WhammyPoints.Select(p => $"{p.Offset}:{p.Value}")));
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }
    // ENDREGION
}

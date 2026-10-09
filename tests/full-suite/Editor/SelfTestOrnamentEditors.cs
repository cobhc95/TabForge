using System.IO;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge;

public static partial class SelfTest
{
    // REGION:ornament-editors
    private static EffectPresetEntry OePreset(EffectEditorKind kind, string name) => EffectPresetStore.BuiltIn(kind).First(p => p.Name == name);
    private static EffectPresetEntry OeValues(string values) => new() { Values = values.Split(' ').Select(x => x.Split('=')).ToDictionary(a => a[0], a => double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture)) };

    /// <summary>Trill, grace note and harmonic editors: OK / Clean / Cancel, one undo step each, presets, and what playback compiles.</summary>
    private static void TestOrnamentEditors()
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
        var host = new EeHost(editor.Effects);
        var flow = new EffectEditorFlow(host);
        var note = cell.Notes[0];
        var undo = pipeline.Session.Undo;
        var noteOns = (Func<ScoreTimeline>)(() => MidiTimelineBuilder.Build(project, new PlaybackOptions()));

        // trill
        editor.SetPosition(0, 0, 5, false);
        Check("trill editor: with no note under the cursor nothing opens", !flow.OpenOrnament(EffectEditorKind.Trill, d => EditorAnswer.Ok) && undo.UndoCount == 0);
        editor.SetPosition(0, 0, 1, false);
        Check("trill editor: Cancel changes nothing and stores no undo step", !flow.OpenOrnament(EffectEditorKind.Trill, d => { d.Pick(OePreset(EffectEditorKind.Trill, "Whole step, 32nd")); return EditorAnswer.Cancel; })
            && !note.Techniques.Contains(TechniqueNames.Trill) && undo.UndoCount == 0);
        var trillOk = flow.OpenOrnament(EffectEditorKind.Trill, d =>
        {
            Check("trill editor: a note without a trill opens at two frets up, 16th", d.Values["Step"] == 2 && d.Values["Speed"] == 16 && d.ListedPresets.Count == 6 && d.ListedPresets.All(p => p.BuiltIn));
            d.Pick(OePreset(EffectEditorKind.Trill, "Whole step, 32nd"));
            Check("trill editor: choosing a preset fills the fret and speed", d.Values["Step"] == 2 && d.Values["Speed"] == 32);
            return EditorAnswer.Ok;
        });
        Check("trill editor: OK stores the target pitch (fret 7 on that string, from the string tuning) and the 32nd speed on that note only, in one undo step",
            trillOk && note.Techniques.Contains(TechniqueNames.Trill) && note.TrillTargetMidi == track.PitchOf(1, 7) && note.TrillDurationDenominator == 32
            && !cell.Notes[1].Techniques.Contains(TechniqueNames.Trill) && undo.UndoCount == 1);
        flow.OpenOrnament(EffectEditorKind.Trill, d => { Check("trill editor: reopened, it shows the note's own fret step and speed", d.Values["Step"] == 2 && d.Values["Speed"] == 32); return EditorAnswer.Ok; });
        Check("trill editor: OK with nothing changed is a no-op (no undo step)", undo.UndoCount == 1);
        flow.OpenOrnament(EffectEditorKind.Trill, d => { d.Pick(OeValues("Step=-3 Speed=64")); return EditorAnswer.Ok; });
        Check("trill editor: a lower trill fret (fret 2) and 64th speed are stored, one more undo step", note.TrillTargetMidi == track.PitchOf(1, 2) && note.TrillDurationDenominator == 64 && undo.UndoCount == 2);
        var sounded = noteOns().Events.Where(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0).Select(e => e.Data1).ToList();
        Check("trill editor: playback alternates the written pitch with the trill pitch many times", sounded.Count(p => p == track.PitchOf(1, 2)) >= 4 && sounded.Count(p => p == track.PitchOf(1, 5)) >= 4,
            string.Join(",", sounded.Take(12)));
        Check("trill editor: Clean removes the trill, one undo step; again it is a no-op", flow.OpenOrnament(EffectEditorKind.Trill, d => EditorAnswer.Clean) && !note.Techniques.Contains(TechniqueNames.Trill)
            && note.TrillTargetMidi == 0 && note.TrillDurationDenominator == 0 && undo.UndoCount == 3 && !flow.OpenOrnament(EffectEditorKind.Trill, d => EditorAnswer.Clean) && undo.UndoCount == 3);

        // grace note
        Check("grace editor: Cancel changes nothing", !flow.OpenOrnament(EffectEditorKind.Grace, d => EditorAnswer.Cancel) && cell.Notes.Count == 2 && undo.UndoCount == 3);
        var graceOk = flow.OpenOrnament(EffectEditorKind.Grace, d =>
        {
            Check("grace editor: a beat without a grace note opens at the default settings and lists the eight built-in presets", d.Values["Step"] == -2 && d.Values["Before"] == 1 && d.Values["Speed"] == 32 && d.Values["Dynamic"] == Dynamics.NearestIndex(95) && d.ListedPresets.Count == 8);
            d.Pick(OePreset(EffectEditorKind.Grace, "Hammer-on from below"));
            return EditorAnswer.Ok;
        });
        var grace = cell.Notes.FirstOrDefault(n => n.IsGraceNote);
        Check("grace editor: OK adds one grace note (fret 3 beside the fret 5 principal, before the beat, 32nd, the note's dynamic, hammer transition), in one undo step",
            graceOk && grace is not null && cell.Notes.Count == 3 && grace.StringIndex == 1 && grace.Fret == 3 && grace.MidiValue == track.PitchOf(1, 3) && grace.GraceBeforeBeat && Math.Abs(grace.GraceDurationSlots - 0.5) < 1e-9
            && grace.Velocity == Dynamics.Velocities[Dynamics.NearestIndex(95)] && grace.Techniques.Contains(TechniqueNames.Hopo) && grace.Techniques.Contains("GraceBefore") && !grace.Dead && undo.UndoCount == 4);
        flow.OpenOrnament(EffectEditorKind.Grace, d => { Check("grace editor: reopened, it shows the grace note's own settings", d.Values["Step"] == -2 && d.Values["Transition"] == 3 && d.Values["Before"] == 1); return EditorAnswer.Ok; });
        Check("grace editor: OK with nothing changed is a no-op", undo.UndoCount == 4 && cell.Notes.Count == 3);
        flow.OpenOrnament(EffectEditorKind.Grace, d => { d.Pick(OeValues("Step=1 Dead=1 Before=0 Speed=64 Dynamic=2 Transition=1")); return EditorAnswer.Ok; });
        Check("grace editor: changing it edits the same grace note (dead, on the beat, 64th, p, bend), one undo step",
            cell.Notes.Count == 3 && grace!.Fret == 6 && grace.Dead && !grace.GraceBeforeBeat && Math.Abs(grace.GraceDurationSlots - 0.25) < 1e-9 && grace.Velocity == Dynamics.Velocities[2]
            && grace.Techniques.Contains("GraceBend") && grace.Techniques.Contains("GraceOnBeat") && !grace.Techniques.Contains(TechniqueNames.Hopo) && undo.UndoCount == 5);
        flow.OpenOrnament(EffectEditorKind.Grace, d => { d.Pick(OeValues("Step=-2 Dead=0 Before=1 Speed=16 Dynamic=5 Transition=2")); return EditorAnswer.Ok; });
        Check("grace editor: slide transition tag and 16th length", grace!.Techniques.Contains(TechniqueNames.LegatoSlide) && !grace.Techniques.Contains("GraceBend") && Math.Abs(grace.GraceDurationSlots - 1) < 1e-9 && undo.UndoCount == 6);
        var shortGraceMs = 0.0;
        foreach (var slots in new[] { 0.25, 1.0 })
        {
            grace.GraceDurationSlots = slots;
            var ms = noteOns().Notes.Where(n => n.Bar == 0 && n.Cell == 0 && n.Midi == track.PitchOf(1, 3)).Select(n => n.DurationMs).DefaultIfEmpty(0).Max();
            if (slots < 1) shortGraceMs = ms;
            else Check("grace editor: playback plays every grace note at the standard short length (GP files cannot keep a written length)", Math.Abs(ms - shortGraceMs) < 0.5 && shortGraceMs > 0, $"{shortGraceMs} vs {ms}");
        }
        Check("grace editor: Clean removes the grace note and keeps the principal notes, one undo step",
            flow.OpenOrnament(EffectEditorKind.Grace, d => EditorAnswer.Clean) && cell.Notes.Count == 2 && cell.Notes.All(n => !n.IsGraceNote) && undo.UndoCount == 7
            && !flow.OpenOrnament(EffectEditorKind.Grace, d => EditorAnswer.Clean));

        // harmonic
        var hOk = flow.OpenOrnament(EffectEditorKind.Harmonic, d =>
        {
            Check("harmonic editor: a note without a harmonic opens as Natural and lists the seven presets", d.Values["Type"] == 0 && d.ListedPresets.Count == 7);
            d.Pick(OePreset(EffectEditorKind.Harmonic, "Artificial, fifth (+7)"));
            return EditorAnswer.Ok;
        });
        Check("harmonic editor: OK sets the artificial harmonic tag and its fret 7 on that note only, one undo step",
            hOk && note.Techniques.Contains(TechniqueNames.ArtificialHarmonic) && note.HarmonicFret == 7 && cell.Notes[1].Techniques.Count == 0 && undo.UndoCount == 8);
        flow.OpenOrnament(EffectEditorKind.Harmonic, d => { Check("harmonic editor: reopened, it shows type and fret", d.Values["Type"] == 1 && d.Values["Fret"] == 7); return EditorAnswer.Ok; });
        Check("harmonic editor: OK with nothing changed is a no-op", undo.UndoCount == 8);
        flow.OpenOrnament(EffectEditorKind.Harmonic, d => { d.Pick(OePreset(EffectEditorKind.Harmonic, "Tapped, octave (12)")); return EditorAnswer.Ok; });
        Check("harmonic editor: switching to tapped replaces the old tag", note.Techniques.Contains("TapHarmonic") && !note.Techniques.Contains(TechniqueNames.ArtificialHarmonic) && note.HarmonicFret == 12 && undo.UndoCount == 9);
        flow.OpenOrnament(EffectEditorKind.Harmonic, d => { d.Pick(OePreset(EffectEditorKind.Harmonic, "Natural")); return EditorAnswer.Ok; });
        Check("harmonic editor: natural has no harmonic fret", note.Techniques.Contains(TechniqueNames.Harmonic) && !note.Techniques.Contains("TapHarmonic") && note.HarmonicFret is null && undo.UndoCount == 10);
        Check("harmonic editor: the harmonic note still compiles into the timeline", noteOns().Notes.Any(n => n.Bar == 0 && n.Cell == 0 && n.StringIndex == 1));
        Check("harmonic editor: Clean removes the harmonic, one undo step", flow.OpenOrnament(EffectEditorKind.Harmonic, d => EditorAnswer.Clean) && !note.Techniques.Contains(TechniqueNames.Harmonic) && undo.UndoCount == 11);

        // user presets keep the values
        var store = new EffectPresetStore(host.Settings);
        Check("ornament presets: a user preset keeps its values and is listed after the built-in ones",
            store.Save(EffectEditorKind.Grace, "Mine", Array.Empty<BendPointModel>(), new Dictionary<string, double> { ["Step"] = -4, ["Speed"] = 64 })
            && store.List(EffectEditorKind.Grace).Last() is { BuiltIn: false, Preset.Name: "Mine" } l && l.Preset.Values["Step"] == -4 && store.List(EffectEditorKind.Trill).All(p => p.BuiltIn));
        flow.OpenOrnament(EffectEditorKind.Grace, d => { Check("ornament presets: the dialog lists it as the user's own", d.ListedPresets.Count == 9 && !d.ListedPresets[8].BuiltIn); return EditorAnswer.Cancel; });

        // the frame
        var dialog = OrnamentEditors.Harmonic(note, new EffectPresetStore(new AppSettings()), canClean: false, presetsChanged: null);
        Check("ornament dialog: OK is the default button and Clean is disabled with nothing to remove", dialog.Dialog.OkButton.IsDefault && !dialog.Dialog.CleanButton.IsEnabled && dialog.Dialog.Result == EditorAnswer.Cancel);
        dialog.Dialog.Close();
    }

    /// <summary>What the three editors set survives .tforge and .gp saving and reopening.</summary>
    private static void TestOrnamentEditorFiles()
    {
        var folder = RtFolder();
        try
        {
            var song = GfSong(1, t =>
            {
                GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95));
                GfPut(t, 0, 4, 4, RtNote(t, 1, 5, 95));
                GfPut(t, 0, 8, 4, RtNote(t, 1, 7, 95));
                GfPut(t, 0, 12, 4, RtNote(t, 1, 5, 95));
            });
            var t0 = song.Tracks[0];
            var beats = t0.Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
            Func<int, int, int> pitchOf = t0.PitchOf;
            OrnamentEdits.ApplyTrill(beats[0].Notes, 3, 32, pitchOf);
            OrnamentEdits.ApplyGrace(beats[1], new GraceSettings(-2, false, true, 32, 5, GraceTransition.Hammer), pitchOf);
            OrnamentEdits.ApplyHarmonic(beats[2].Notes, 1, 7);
            OrnamentEdits.ApplyHarmonic(beats[3].Notes, 2, 12);

            void Verify(SongProject s, string label, bool gp)
            {
                var b = s.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
                var trill = b[0].Notes[0];
                Check($"{label}: the trill fret (target pitch) and its 32nd speed come back", trill.Techniques.Contains(TechniqueNames.Trill) && trill.TrillTargetMidi == pitchOf(1, 6) && trill.TrillDurationDenominator == 32,
                    $"{trill.TrillTargetMidi} vs {pitchOf(1, 6)}, 1/{trill.TrillDurationDenominator}");
                var g = b[1].Notes.FirstOrDefault(n => n.IsGraceNote);
                Check($"{label}: the grace note's fret, position, length and dynamic come back", g is not null && g.Fret == 3 && g.GraceBeforeBeat && (gp || Math.Abs(g.GraceDurationSlots - 0.5) < 1e-6)
                    && Dynamics.NearestIndex(g.Velocity) == 5 && !g.Dead && b[1].Notes.Any(n => !n.IsGraceNote && n.Fret == 5), g is null ? "no grace" : $"fret {g.Fret} before {g.GraceBeforeBeat} slots {g.GraceDurationSlots} vel {g.Velocity}");
                Check($"{label}: the grace note's hammer transition comes back", g is not null && (g.Techniques.Contains(TechniqueNames.Hopo) || g.Techniques.Contains(TechniqueNames.HopoOrigin)), g is null ? "no grace" : string.Join(",", g.Techniques));
                var art = b[2].Notes[0];
                Check($"{label}: the artificial harmonic and its fret come back", art.Techniques.Contains(TechniqueNames.ArtificialHarmonic) && art.HarmonicFret == 7, string.Join(",", art.Techniques) + " " + art.HarmonicFret);
                var tap = b[3].Notes[0];
                Check($"{label}: the tapped harmonic and its fret come back", tap.Techniques.Contains("TapHarmonic") && tap.HarmonicFret == 12, string.Join(",", tap.Techniques) + " " + tap.HarmonicFret);
            }
            Verify(RtCopy(song), ".tforge", false);

            var dead = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 5, 95)));
            var deadCell = dead.Tracks[0].Measures[0].Cells.First(c => c.Notes.Count > 0);
            OrnamentEdits.ApplyGrace(deadCell, new GraceSettings(0, true, false, 64, 2, GraceTransition.Slide), dead.Tracks[0].PitchOf);
            var deadBack = RtCopy(dead).Tracks[0].Measures[0].Cells.First(c => c.Notes.Count > 0).Notes.First(n => n.IsGraceNote);
            Check(".tforge: a dead grace note on the beat, 64th, with a slide comes back", deadBack.Dead && !deadBack.GraceBeforeBeat && Math.Abs(deadBack.GraceDurationSlots - 0.25) < 1e-9 && deadBack.Techniques.Contains(TechniqueNames.LegatoSlide) && deadBack.Velocity == Dynamics.Velocities[2]);

            Verify(GfClean(song, folder, "ornament-editors"), ".gp", true);

            var capo = GfSong(1, t => { t.Capo = 2; GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95)); });
            var capoCell = capo.Tracks[0].Measures[0].Cells.First(c => c.Notes.Count > 0);
            OrnamentEdits.ApplyTrill(capoCell.Notes, 4, 64, capo.Tracks[0].PitchOf);
            var capoTrill = GfClean(capo, folder, "ornament-capo").Tracks[0].Measures[0].Cells.First(c => c.Notes.Count > 0).Notes[0];
            Check(".gp: a trill on a capo track keeps its fret (target pitch) and 64th speed", capoTrill.TrillTargetMidi == capo.Tracks[0].PitchOf(1, 7) && capoTrill.TrillDurationDenominator == 64,
                $"{capoTrill.TrillTargetMidi} vs {capo.Tracks[0].PitchOf(1, 7)}, 1/{capoTrill.TrillDurationDenominator}");
            var gpDead = GfClean(dead, folder, "ornament-dead").Tracks[0].Measures[0].Cells.First(c => c.Notes.Count > 0).Notes.FirstOrDefault(n => n.IsGraceNote);
            Check(".gp: a dead grace note on the beat with a slide and its dynamic comes back (the file holds no grace length: every grace reads back as an eighth)", gpDead is not null && gpDead.Dead && !gpDead.GraceBeforeBeat && gpDead.Techniques.Contains(TechniqueNames.LegatoSlide) && Dynamics.NearestIndex(gpDead.Velocity) == 2,
                gpDead is null ? "no grace" : $"dead {gpDead.Dead} before {gpDead.GraceBeforeBeat} slots {gpDead.GraceDurationSlots} vel {gpDead.Velocity} {string.Join(",", gpDead.Techniques)}");
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }
    // ENDREGION
}

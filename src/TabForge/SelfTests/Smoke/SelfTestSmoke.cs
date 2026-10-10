using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Views.Score;
using TabForge.Visualization;

namespace TabForge;

/// <summary>Smoke tests of the basic set (model round trip, project save/load, one editor entry) and the helpers the full suite shares with them.</summary>
public static partial class SelfTest
{
    private static SongProject TwoBarSong(int bpm = 120)
    {
        var p = new SongProject { Tempo = bpm, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var t = new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(2) };
        for (var m = 0; m < 2; m++)
            for (var i = 0; i < 4; i++)
            {
                var cell = t.Measures[m].Cells[i * 4];
                cell.DurationDenominator = 4;
                cell.Notes.Add(new TabNote { StringIndex = 0, Fret = i, MidiValue = 64 + i });
            }
        p.Tracks.Add(t);
        return p;
    }
    private static TabEditorControl NewEditor(out SongProject project, out TrackModel track)
    {
        var editor = new TabEditorControl();
        project = new SongProject { Tempo = 120 };
        track = new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(4) };
        project.Tracks.Add(track);
        editor.Project = project;
        editor.SelectedTrackIndex = 0;
        return editor;
    }

    private static void TestEditorEntry()
    {
        var editor = NewEditor(out var project, out var track);
        editor.AutoAdvanceAfterEntry = true;   // the advance option (off by default) is what this entry checks
        editor.SetPosition(0, 0, 0);
        editor.Effects.SetDuration(4);
        editor.Effects.EnterFret(5);

        var cell = track.Measures[0].Cells[0];
        Eq("fret written", 5, cell.Notes[0].Fret);
        Eq("midi computed from tuning", 64 + 5, cell.Notes[0].MidiValue);
        Eq("duration applied", 4, cell.DurationDenominator);
        Eq("cursor auto-advanced by a quarter", 4, editor.SelectedCell);
        Check("project flagged dirty", project.IsDirty);

        // Editing an existing note must keep the cursor on that note.
        var editorExisting = NewEditor(out _, out var trackExisting);
        trackExisting.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 });
        editorExisting.SetPosition(0, 0, 0);
        editorExisting.Effects.EnterFret(7);
        Eq("existing note fret edited", 7, trackExisting.Measures[0].Cells[0].Notes[0].Fret);
        Eq("cursor stays on edited note", 0, editorExisting.SelectedCell);

        // Two digits typed quickly on the same beat make a two-digit fret.
        var editor2 = NewEditor(out _, out var track2);
        editor2.SetPosition(0, 0, 0);
        editor2.Effects.EnterFret(1, autoAdvance: false);
        editor2.Effects.EnterFret(2, autoAdvance: false);
        Eq("two-digit fret 12", 12, track2.Measures[0].Cells[0].Notes[0].Fret);

        // A second string on the same beat builds a chord.
        var editor3 = NewEditor(out _, out var track3);
        editor3.SetPosition(0, 0, 0);
        editor3.Effects.EnterFret(3);
        editor3.SetPosition(0, 0, 2);
        editor3.Effects.EnterFret(5, autoAdvance: false);
        Eq("chord has two notes", 2, track3.Measures[0].Cells[0].Notes.Count);
    }

    private static void TestProjectRoundtrip()
    {
        var p = TwoBarSong(140);
        p.Title = "Roundtrip";
        p.Markers.Add(new MarkerModel { MeasureIndex = 1, Title = "1. Chorus" });
        p.Tracks[0].Measures[0].RepeatStart = true;
        p.Tracks[0].Measures[0].Cells[0].ChordName = "Am";
        p.Tracks[0].Measures[0].Cells[0].IsTriplet = true;
        p.Tracks[0].Measures[0].Cells[0].Dots = 1;
        p.Tracks[0].Measures[0].Cells[1].TupletNumerator = 5;
        p.Tracks[0].Measures[0].Cells[1].TupletDenominator = 4;
        var json = ProjectService.Snapshot(p);
        var back = ProjectService.Restore(json);
        Eq("title survives", "Roundtrip", back.Title);
        Eq("tempo survives", 140, back.Tempo);
        Eq("markers survive", 1, back.Markers.Count);
        Check("repeat survives", back.Tracks[0].Measures[0].RepeatStart);
        Eq("chord survives", "Am", back.Tracks[0].Measures[0].Cells[0].ChordName);
        Check("triplet survives", back.Tracks[0].Measures[0].Cells[0].IsTriplet);
        Eq("dots survive", 1, back.Tracks[0].Measures[0].Cells[0].Dots);
        Eq("quintuplet numerator survives", 5, back.Tracks[0].Measures[0].Cells[1].TupletNumerator);
        Eq("quintuplet denominator survives", 4, back.Tracks[0].Measures[0].Cells[1].TupletDenominator);
    }

    /// <summary>
    /// The persisted model must be complete: every public settable property of every stored type has to
    /// be serialized (no accidental [JsonIgnore]) and come back with the same value. A field that is
    /// silently dropped here is data the user loses on every save.
    /// </summary>
    private static void TestModelRoundTrip()
    {
        var types = new[]
        {
            typeof(SongProject), typeof(MarkerModel), typeof(TrackModel), typeof(MeasureModel),
            typeof(TabCell), typeof(TabNote), typeof(RigPreset), typeof(PluginSlot), typeof(BendPointModel)
        };

        // Properties that are deliberately not persisted (computed, transient or UI state).
        var transient = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsDirty", "HasAnnotation", "Tuplet"
        };

        var modelProps = new List<(Type Type, string Name)>();
        foreach (var type in types)
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0) continue;
                if (transient.Contains(prop.Name)) continue;
                modelProps.Add((type, prop.Name));
                Check($"{type.Name}.{prop.Name} is persistable", prop.CanWrite, "no public setter");
            }

        var rich = BuildRichProject();
        var json = ProjectService.Snapshot(rich);
        var back = ProjectService.Restore(json);
        // Saved files omit values equal to a new object's initial value (lossless compaction), so
        // "is every property serializable" is checked on a plain serialization; the round-trip
        // comparison below proves the compact form restores every value.
        var plainJson = System.Text.Json.JsonSerializer.Serialize(rich);
        foreach (var (type, name) in modelProps)
            Check($"{type.Name}.{name} is serialized", plainJson.Contains($"\"{name}\""), name);

        var diffs = new List<string>();
        CompareModel(rich, back, "project", diffs, new HashSet<object>(ReferenceEqualityComparer.Instance));
        Check("the whole model survives a save/load round-trip", diffs.Count == 0,
            string.Join("; ", diffs.Take(6)) + (diffs.Count > 6 ? $" (+{diffs.Count - 6} more)" : ""));

        var originalCell = rich.Tracks[0].Measures[0].Cells[0];
        var clonedCell = ScoreEditCommands.CloneCell(originalCell);
        Check("cell cloning keeps overlapping curves and trill/picking data",
            clonedCell.WhammyPoints.Select(point => (point.Offset, point.Value))
                .SequenceEqual(originalCell.WhammyPoints.Select(point => (point.Offset, point.Value))) &&
            clonedCell.TremoloPickDenominator == originalCell.TremoloPickDenominator &&
            clonedCell.Notes[0].BendPoints.Select(point => (point.Offset, point.Value))
                .SequenceEqual(originalCell.Notes[0].BendPoints.Select(point => (point.Offset, point.Value))) &&
            clonedCell.Notes[0].BendTypeName == originalCell.Notes[0].BendTypeName &&
            clonedCell.Notes[0].BendStyleName == originalCell.Notes[0].BendStyleName &&
            clonedCell.Notes[0].TrillTargetMidi == originalCell.Notes[0].TrillTargetMidi &&
            clonedCell.Notes[0].TrillDurationDenominator == originalCell.Notes[0].TrillDurationDenominator);

        // And a real file write/read, not just an in-memory snapshot.
        var path = Path.Combine(Path.GetTempPath(), "tabforge-selftest-roundtrip.tforge");
        ProjectService.Save(path, rich);
        var fromDisk = ProjectService.Load(path);
        Check("save then load keeps every note",
            fromDisk.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count))) ==
            rich.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count))));
        try { File.Delete(path); } catch { } // Not logged: test cleanup of a temporary file.
    }

    /// <summary>Builds a project that sets every field a save would have to preserve.</summary>
    private static SongProject BuildRichProject()
    {
        var project = TwoBarSong(137);
        project.Title = "Round trip";
        project.Subtitle = "sub";
        project.Artist = "artist";
        project.Album = "album";
        project.MusicAuthor = "music";
        project.LyricsAuthor = "words";
        project.Copyright = "copy";
        project.TabAuthor = "tabber";
        project.Instructions = "instructions";
        project.Notice = "notice";
        project.Lyrics = "la la la";
        project.KeySignature = -3;
        project.KeySignatureMinor = true;
        project.GrayInactiveVoice = true;
        project.TimeSignatureNumerator = 6;
        project.TimeSignatureDenominator = 8;
        project.ImportedFrom = "somewhere.gp5";
        project.Markers.Add(new MarkerModel { MeasureIndex = 1, Title = "Chorus", ColorHex = "#112233" });

        var measure = project.Tracks[0].Measures[0];
        measure.RepeatStart = true;
        measure.RepeatEnd = true;
        measure.RepeatCount = 3;
        measure.AlternateEnding = 2;
        measure.IsDoubleBar = true;
        measure.SimileOneBar = true;
        measure.SimileTwoBar = true;
        measure.SectionName = "Intro";
        measure.TempoChange = 155;
        measure.TripletFeel = true;
        measure.TripletFeelKind = "Triplet16th";
        measure.FreeTime = true;
        measure.ForceLineBreak = true;
        measure.PreventLineBreak = true;
        measure.Anacrusis = true;
        measure.Directions = "DaCapo,Fine";
        measure.KeySignature = 4;
        measure.KeySignatureMinor = true;
        measure.TimeSigNum = 3;
        measure.TimeSigDenom = 4;
        measure.Clef = "F";

        var cell = measure.Cells[0];
        cell.DurationDenominator = 8;
        cell.Dots = 1;
        cell.IsTriplet = true;
        cell.TupletNumerator = 3;
        cell.TupletDenominator = 2;
        cell.SoundDurationPercent = 65;
        cell.OctaveShiftSemitones = 12;
        cell.BeamMode = BeamMode.Force;
        cell.BreakSecondaryBeamBefore = true;
        cell.StemDirection = StemDirection.Invert;
        cell.IsTied = true;
        cell.IsGrace = true;
        cell.GraceBeforeBeat = false;
        cell.Fermata = true;
        cell.Accent = 2;
        cell.Staccato = true;
        cell.Tenuto = true;
        cell.TremoloPickDenominator = 32;
        cell.WhammyPoints.AddRange(new[] { new BendPointModel { Offset = 0, Value = -2 }, new BendPointModel { Offset = 60, Value = 0 } });
        cell.ChordName = "C#m7";
        cell.Text = "note";
        cell.Lyrics = "line one\nline two";
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote
        {
            StringIndex = 3, Fret = 12, MidiValue = 64, Velocity = 111,
            Ghost = true, Dead = true, Tied = true, SlideTargetMidi = 67,
            TrillTargetMidi = 66, TrillDurationDenominator = 32,
            BendTypeName = "PrebendRelease", BendStyleName = "Gradual",
            Techniques = new HashSet<string>(new[] { "Vibrato", "Bend", "LetRing" }, StringComparer.OrdinalIgnoreCase),
            BendPoints = new List<BendPointModel> { new() { Offset = 10, Value = 2 }, new() { Offset = 60, Value = 4 } }
        });
        measure.Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        measure.Voice2Cells[4].Notes.Add(new TabNote { StringIndex = 2, Fret = 4, MidiValue = 59, Tied = true });
        measure.Voice2Cells[4].DurationDenominator = 16;

        var track = project.Tracks[0];
        track.Name = "Lead";
        track.Kind = TrackKind.Guitar;
        track.Capo = 3;
        track.ColorHex = "#ABCDEF";
        track.Mute = true;
        track.Solo = true;
        track.Volume = 88;
        track.Pan = 40;
        track.Chorus = 12;
        track.Reverb = 34;
        track.Transpose = -2;
        track.MidiChannel = 5;
        track.MidiProgram = 30;
        track.MidiOutputDeviceId = 2;
        track.InstrumentName = "Distortion";
        track.NumberOfFrets = 22;
        track.Rig.Name = "Modern";
        track.Rig.ArticulationMap = "Generic Guitar";
        track.Rig.Plugins.Add(new PluginSlot { Name = "Amp", Path = "C:/amp.vst3", Type = PluginSlotType.Instrument, Enabled = false });
        track.StringTunings.Clear();
        track.StringTunings.AddRange(new[] { 64, 59, 55, 50, 45, 40 });
        return project;
    }

    /// <summary>Deep value comparison of the persisted model (JsonIgnore'd properties excluded).</summary>
    private static void CompareModel(object? before, object? after, string path, List<string> diffs, HashSet<object> visited)
    {
        if (before is null || after is null)
        {
            if (!ReferenceEquals(before, after)) diffs.Add($"{path}: null mismatch");
            return;
        }
        var type = before.GetType();
        if (type != after.GetType()) { diffs.Add($"{path}: type {type.Name} vs {after.GetType().Name}"); return; }
        if (type.IsPrimitive || type.IsEnum || before is string || before is decimal || before is DateTime)
        {
            if (!before.Equals(after)) diffs.Add($"{path}: '{before}' vs '{after}'");
            return;
        }
        if (before is System.Collections.IEnumerable listBefore && after is System.Collections.IEnumerable listAfter)
        {
            var a = listBefore.Cast<object>().ToList();
            var b = listAfter.Cast<object>().ToList();
            if (a.Count != b.Count) { diffs.Add($"{path}: count {a.Count} vs {b.Count}"); return; }
            for (var i = 0; i < a.Count; i++) CompareModel(a[i], b[i], $"{path}[{i}]", diffs, visited);
            return;
        }
        if (!visited.Add(before)) return;
        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0) continue;
            CompareModel(prop.GetValue(before), prop.GetValue(after), $"{path}.{prop.Name}", diffs, visited);
        }
    }

    private static void PumpUi() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));

    /// <summary>Set TABFORGE_NO_LOCAL_SONGS=1 to run as on a clean checkout (no Tabs folder): every local-song check must skip, never fail.</summary>
    internal static bool LocalSongsDisabled => Environment.GetEnvironmentVariable("TABFORGE_NO_LOCAL_SONGS") == "1";
}

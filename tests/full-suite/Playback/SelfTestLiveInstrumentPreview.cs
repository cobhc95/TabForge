using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Plugins;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// The instrument picker auditions a sound in the running playback: the channel's program changes without touching the document,
// a loop restart / seek chase keeps it, and ending the preview puts the track's own program back.
public static partial class SelfTest
{
    private sealed class ProgramLogOutput : IMidiOutput
    {
        public readonly List<(int Status, int Data1)> Sent = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { lock (Sent) Sent.Add((status, data1)); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
        public int LastProgram(int channel) { lock (Sent) return Sent.LastOrDefault(s => s.Status == (0xC0 | channel)).Data1; }
    }

    private static void TestLiveInstrumentPreview()
    {
        var song = TemplateFactory.Blank();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var entry = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program != track.MidiProgram);
        var original = track.MidiProgram;
        var name = track.InstrumentName;
        song.IsDirty = false;
        var output = new ProgramLogOutput();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions { Loop = true }, _ => { }, () => { });
        try
        {
            for (var i = 0; i < 40 && !engine.IsPlaying; i++) Thread.Sleep(50);
            Check("live preview: engine is playing", engine.IsPlaying);
            InstrumentLivePreview.Apply(engine, track, entry.Name);
            Check("live preview: program change reaches the output", output.LastProgram(track.MidiChannel) == entry.Program && engine.ProgramPreviewOf(track.MidiChannel) == entry.Program);
            Check("live preview: the document is untouched (program, name, dirty flag)", track.MidiProgram == original && track.InstrumentName == name && !song.IsDirty);

            var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions());
            engine.RestoreChannelStateAt(timeline, 0, 0);
            Check("live preview: a seek or loop chase keeps the previewed program", output.LastProgram(track.MidiChannel) == entry.Program);

            InstrumentLivePreview.Apply(engine, track, null);
            Check("live preview: cancel restores the original program exactly", output.LastProgram(track.MidiChannel) == original && engine.ProgramPreviewOf(track.MidiChannel) == -1 && !song.IsDirty);

            InstrumentLivePreview.Apply(engine, track, entry.Name);
            new TrackController().ApplyEdit(song, new TrackEditRequest(song.Tracks.IndexOf(track), TrackEditKind.SelectInstrument, entry.Name));
            InstrumentLivePreview.Apply(engine, track, null);
            Check("live preview: commit keeps the chosen program after the preview ends", track.MidiProgram == entry.Program && output.LastProgram(track.MidiChannel) == entry.Program);
        }
        finally { engine.Stop(); }
    }

    private static void TestDrumToPitchedConversion()
    {
        var song = TemplateFactory.Blank();
        var drums = new TrackModel { Name = "Percussion", Kind = TrackKind.Drums, InstrumentName = "Drum Kit", MidiChannel = 9, MidiProgram = 0,
            StringTunings = new() { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(2), Volume = 103, Rig = new RigPreset { Name = "GM Drum Kit", ArticulationMap = "GM Drums" } };
        var cell = drums.Measures[0].Cells[0];
        cell.IsRest = false;
        cell.Notes.Add(new TabNote { StringIndex = 3, MidiValue = 62, Fret = 0 });
        song.Tracks.Add(drums);
        var index = song.Tracks.Count - 1;
        var viola = InstrumentCatalog.All.First(e => e.Name.Contains("Viola", StringComparison.OrdinalIgnoreCase));
        var doc = new DocumentSession(new PlaybackEngine(new ProgramLogOutput())) { Project = song };

        Check("drum conversion: notes make the change need a confirmation", TrackSetup.ConversionRemapsNotes(drums, toDrums: false) && !TrackSetup.ConversionRemapsNotes(drums, toDrums: true));
        var result = DocumentEdits.Run(doc, p => new TrackController().ApplyEdit(p, new TrackEditRequest(index, TrackEditKind.SelectInstrument, viola.Name)));
        var t = doc.Project.Tracks[index];
        Check("drum conversion: the track becomes a pitched track (kind, channel off 9)", result.Changed && t.Kind != TrackKind.Drums && t.MidiChannel != 9 && t.MidiProgram == viola.Program);
        Check("drum conversion: default tuning for the new kind", t.StringTunings.SequenceEqual(TrackSetup.DefaultStrings(t.Kind)!));
        var n = t.Measures[0].Cells[0].Notes[0];
        Check("drum conversion: the note keeps its MIDI pitch and is re-fingered on the lowest fret", n.MidiValue == 62 && t.StringTunings[n.StringIndex] + n.Fret == 62 && n.Fret >= 0 && n.Fret <= 24);
        Check("drum conversion: volume untouched", t.Volume == 103);
        Check("drum conversion: one undo step", doc.Undo.UndoCount == 1);
        var current = doc.Undo.Snapshot(doc.Project);
        Check("drum conversion: undo restores the drum track", doc.Undo.TryUndo(current, out var target) && doc.Undo.Restore(target).Tracks[index] is { Kind: TrackKind.Drums, MidiChannel: 9 } r && r.Measures[0].Cells[0].Notes[0].StringIndex == 3);

        TrackSetup.ConvertFamily(t, toDrums: true, TrackKind.Drums);
        Check("drum conversion: pitched to drums keeps the pitch as the drum key", t.Kind == TrackKind.Drums && t.Measures[0].Cells[0].Notes[0].MidiValue == 62);
    }
}

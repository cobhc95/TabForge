using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Diagnostics;

/// <summary>
/// Compares an exported MIDI file with the playback timeline it was written from: every performed bar's start,
/// every note-on, and the end of the song. The file's own tempo events are read back, so the numbers are what another sequencer would play.
/// </summary>
internal static class MidiTimingAudit
{
    internal sealed record Result(int Bars, double MaxBarStartErrMs, int WorstBar, double MaxNoteErrMs, int NoteCountPlayback, int NoteCountFile,
        double PlaybackEndMs, double FileEndMs, double PlaybackLastEventMs, double FileLastEventMs)
    {
        /// <summary>Where the largest note-on drift is (track, source bar, playback time), for the round-trip diff report.</summary>
        public string WorstNoteAt { get; init; } = "";
        public double EndErrMs => FileEndMs - PlaybackEndMs;
        public double LastEventErrMs => FileLastEventMs - PlaybackLastEventMs;
        public override string ToString() =>
            $"{Bars} bars: bar starts off by up to {MaxBarStartErrMs:0.000} ms (bar {WorstBar}), note-ons by up to {MaxNoteErrMs:0.000} ms ({NoteCountPlayback} vs {NoteCountFile} notes); " +
            $"song end {PlaybackEndMs:0.0} ms vs file {FileEndMs:0.0} ms ({EndErrMs:+0.0;-0.0;0.0}), last event {PlaybackLastEventMs:0.0} vs {FileLastEventMs:0.0} ms ({LastEventErrMs:+0.0;-0.0;0.0})";
    }

    /// <summary>Exports <paramref name="project"/> to <paramref name="midiPath"/> and measures the file against the playback timeline.</summary>
    public static Result Measure(SongProject project, string midiPath)
    {
        var playback = MidiTimelineBuilder.Build(project, new PlaybackOptions { RespectMuteSolo = false, Metronome = false, CountIn = false });
        MidiExportService.Export(project, midiPath);
        var midi = AudioAudit.ReadMidi(midiPath);

        double maxBar = 0; var worstBar = 0;
        var ticks = new MidiExportService.TickMap(playback, project);   // bar start ticks follow each bar's performed length (a short bar is not a whole number of slots)
        for (var i = 0; i < playback.Bars.Count; i++)
        {
            var bar = playback.Bars[i];
            var err = Math.Abs(midi.TickToMs(ticks.TickOfBar(i)) - bar.StartMs);
            if (err > maxBar) { maxBar = err; worstBar = i + 1; }
        }

        double maxNote = 0; var notesPlayback = 0; var notesFile = 0; var worstNoteAt = "";
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var pb = playback.Events.Where(e => e.TrackIndex == i && e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
            var file = i + 1 < midi.Tracks.Count ? midi.Tracks[i + 1].Where(n => n.On).OrderBy(n => n.Tick).ToList() : new();
            notesPlayback += pb.Count; notesFile += file.Count;
            if (pb.Count != file.Count) continue;
            for (var k = 0; k < pb.Count; k++)
            {
                var err = Math.Abs(midi.TickToMs(file[k].Tick) - pb[k].TimeMs);
                if (err > maxNote) { maxNote = err; worstNoteAt = $"track {i} bar {playback.BarAt(pb[k].TimeMs).Bar + 1} at {pb[k].TimeMs:0} ms (midi {pb[k].Data1}, file {midi.TickToMs(file[k].Tick) - pb[k].TimeMs:+0.0;-0.0} ms); around it, playback vs file: " + string.Join(", ", Enumerable.Range(Math.Max(0, k - 2), Math.Min(5, pb.Count - Math.Max(0, k - 2))).Select(j => $"{pb[j].Data1}@{pb[j].TimeMs:0.0}/{midi.TickToMs(file[j].Tick):0.0}[{file[j].Note}]")); }
            }
        }

        var playbackEnd = Math.Max(playback.TotalMs, playback.Bars.Count > 0 ? playback.Bars[^1].EndMs : 0);
        var lastEvent = playback.Events.Where(e => e.TrackIndex >= 0 && !e.IsMetronome && (e.IsNoteOn || e.IsNoteOff)).Select(e => e.TimeMs).DefaultIfEmpty(0).Max();
        playbackEnd = Math.Max(playbackEnd, lastEvent);   // the file lasts until its last note has ended
        var fileLast = midi.TickToMs(midi.Tracks.SelectMany(t => t).Select(n => n.Tick).DefaultIfEmpty(0).Max());
        return new Result(playback.Bars.Count, maxBar, worstBar, maxNote, notesPlayback, notesFile, playbackEnd, midi.TickToMs(midi.LastTick), lastEvent, fileLast) { WorstNoteAt = worstNoteAt };
    }
}

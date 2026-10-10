using System.IO;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// MIDI export timing: reading the exported file's own tempo events back must give the playback timeline's time at every
/// bar start and every note, and the file must last as long as the song, including notes that ring on past the last bar.
/// The tempo map must not use whole-BPM steps sampled per slot (ramps would drift), and notes after the last bar must not be clipped to its end.
/// </summary>
public static partial class SelfTest
{
    private static void TestMidiExportTiming()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-miditiming-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            // Awkward tempo (133 BPM is not a whole number of microseconds per quarter), a ramp, a mid-bar step, a fermata and a ringing last note.
            var p = SingleTrack(6, 133);
            for (var bar = 0; bar < 6; bar++)
                for (var beat = 0; beat < 4; beat++) Beat(p, 0, bar, beat * 4, 4, 60 + beat);
            p.Tracks[0].Measures[1].MidBarTempos = new List<TempoPoint> { new(0, 200, 16) };            // accelerando over the whole bar
            p.Tracks[0].Measures[2].MidBarTempos = new List<TempoPoint> { new(6, 90), new(10, 150, 4) };  // a step, then a short ramp
            p.Tracks[0].Measures[3].Cells[4].Fermata = true;
            p.Tracks[0].Measures[3].TempoChange = 77;
            p.Tracks[0].Measures[5].Cells[12].Notes[0].Techniques.Add("LetRing");
            var r = MidiTimingAudit.Measure(p, path);
            Log.Add($"  info  MIDI export timing, synthetic: {r}");
            Check("MIDI export: every bar starts within 1 ms of playback (ramp, step, fermata, awkward tempo)", r.MaxBarStartErrMs <= 1, $"{r.MaxBarStartErrMs:0.000} ms at bar {r.WorstBar}");
            Check("MIDI export: every note-on is within 1 ms of playback", r.MaxNoteErrMs <= 1 && r.NoteCountFile == r.NoteCountPlayback, $"{r.MaxNoteErrMs:0.000} ms, {r.NoteCountPlayback} vs {r.NoteCountFile} notes");
            Check("MIDI export: the file ends with the song and keeps the last note's release (within 1 ms)", Math.Abs(r.EndErrMs) <= 1 && Math.Abs(r.LastEventErrMs) <= 1, $"end {r.EndErrMs:+0.0;-0.0} ms, last event {r.LastEventErrMs:+0.0;-0.0} ms");

            // The built-in demo song: 154 performed bars with ramps, fermatas and a ringing ending.
            var demo = FullDemoSongFactory.Create();
            var d = MidiTimingAudit.Measure(demo, path);
            Log.Add($"  info  MIDI export timing, demo song: {d}");
            Check("MIDI export (demo song): bar starts and note-ons within 1 ms of playback", d.MaxBarStartErrMs <= 1 && d.MaxNoteErrMs <= 1 && d.NoteCountFile == d.NoteCountPlayback,
                $"bars {d.MaxBarStartErrMs:0.000} ms (bar {d.WorstBar}), notes {d.MaxNoteErrMs:0.000} ms");
            Check("MIDI export (demo song): the file ends with the song (within 1 ms, last release kept)", Math.Abs(d.EndErrMs) <= 1 && Math.Abs(d.LastEventErrMs) <= 1, $"end {d.EndErrMs:+0.0;-0.0} ms, last event {d.LastEventErrMs:+0.0;-0.0} ms");
        }
        finally { try { File.Delete(path); } catch { } }
    }
}

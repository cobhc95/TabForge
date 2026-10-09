using System.Diagnostics;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

/// <summary>
/// Typed-note preview: every typed note sounds once for the set length at the song tempo, also when the same pitch is typed
/// again quickly, after an edit (recompile notification) and with the rest fill on. An older note's timer never cuts a newer note.
/// </summary>
public static partial class SelfTest
{
    private sealed class PreviewRecorder : IMidiOutput
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public readonly List<(double Ms, int Status, int Note)> Log = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) is 0x90 or 0x80) lock (Log) Log.Add((_clock.Elapsed.TotalMilliseconds, status & 0xF0, data1));
        }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static void TestTypedNotePreview()
    {
        // 1. Engine: 20 rapid previews, pitches repeating (same pitch twice in a row, and again a few notes later).
        var output = new PreviewRecorder();
        using (var engine = new PlaybackEngine(output))
        {
            var pitches = new[] { 52, 52, 55, 57, 52, 60, 60, 60, 55, 52, 57, 57, 64, 52, 55, 55, 60, 52, 52, 64 };
            var tasks = new List<Task>();
            foreach (var p in pitches) { tasks.Add(engine.PreviewNoteAsync(0, 0, 24, p, 500)); Thread.Sleep(45); }
            Task.WaitAll(tasks.ToArray(), 8000);
            List<(double Ms, int Status, int Note)> log;
            lock (output.Log) log = output.Log.ToList();
            var ons = log.Where(e => e.Status == 0x90).ToList();
            var offs = log.Where(e => e.Status == 0x80).ToList();
            Check("typed preview: 20 rapid entries give 20 note-ons", ons.Count == 20, $"{ons.Count}");
            var bad = new List<string>();
            foreach (var off in offs)
            {
                var on = ons.LastOrDefault(o => o.Note == off.Note && o.Ms <= off.Ms);
                var retriggered = ons.Any(o => o.Note == off.Note && o.Ms >= off.Ms && o.Ms - off.Ms < 5);
                if (!retriggered && off.Ms - on.Ms < 470) bad.Add($"{off.Note}@{off.Ms - on.Ms:0}");
            }
            Check("typed preview: no note is cut before its own 500 ms (a newer same-pitch note only retriggers it)", bad.Count == 0, string.Join(",", bad.Take(5)));
            var stuck = pitches.Distinct().Where(n => true && ons.Count(o => o.Note == n) != offs.Count(o => o.Note == n)).ToList();
            Check("typed preview: every note-on gets exactly one note-off", stuck.Count == 0, string.Join(",", stuck));
            var lastOnOfPitch = pitches.Distinct().Select(n => (n, on: ons.Last(o => o.Note == n).Ms, off: offs.Last(o => o.Note == n).Ms));
            Check("typed preview: the last note of each pitch rings for the full length", lastOnOfPitch.All(x => x.off - x.on >= 470 && x.off - x.on < 700));
        }

        // 2. Editor: the event carries the set length (quarter at 120 bpm = 500 ms), also after an edit and with the rest fill on.
        foreach (var fill in new[] { false, true })
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            project.Tempo = 120;
            var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = fill, AutoAdvanceAfterEntry = true, CurrentDurationDenominator = 4 };
            var lengths = new List<int>();
            editor.NotePreview += (_, e) => lengths.Add(e.LengthMs);
            editor.SetPosition(0, 0, 1, false);
            for (var i = 0; i < 6; i++)
            {
                editor.Effects.EnterFret(3);
                if (i == 2) project.MarkTimelineChanged();   // an edit's recompile / live-splice notification
            }
            Check($"typed preview: six typed quarter notes each report 500 ms at 120 bpm (rest fill {(fill ? "on" : "off")})",
                lengths.Count == 6 && lengths.All(l => Math.Abs(l - 500) <= 2), string.Join(",", lengths));
            editor.CurrentDurationDenominator = 8;
            lengths.Clear();
            editor.Effects.EnterFret(5);
            Check($"typed preview: an eighth note reports 250 ms (rest fill {(fill ? "on" : "off")})", lengths.Count == 1 && Math.Abs(lengths[0] - 250) <= 2, string.Join(",", lengths));
        }
    }
}

using System.IO;
using System.Diagnostics;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

/// <summary>Drums and melodic notes of the same beat reach the output together, on the Windows-MIDI path and in mixed engine routing (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private sealed class StampingMidiOutput : IMidiOutput
    {
        public readonly List<(long Ticks, int Status, int Data1)> Sent = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices => Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { lock (Sent) Sent.Add((Stopwatch.GetTimestamp(), status, data1)); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static void TestDrumAndMelodicDispatchTogether()
    {
        var project = new SongProject { Tempo = 120 };
        TrackModel Make(string name, TrackKind kind, int channel, int midi)
        {
            var t = new TrackModel { Name = name, Kind = kind, MidiChannel = channel, Measures = TemplateFactory.Measures(1) };
            foreach (var cell in new[] { 0, 4, 8 })
            {
                t.Measures[0].Cells[cell].DurationDenominator = 4;
                t.Measures[0].Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = midi });
            }
            return t;
        }
        project.Tracks.Add(Make("Guitar", TrackKind.Guitar, 0, 64));
        project.Tracks.Add(Make("Drums", TrackKind.Drums, 9, 38));

        // 1. Compiled: the guitar and the drum note of each beat are the same time.
        var tl = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var guitarOn = tl.Events.Where(e => e.IsNoteOn && e.TrackIndex == 0).Select(e => e.TimeMs).ToList();
        var drumOn = tl.Events.Where(e => e.IsNoteOn && e.TrackIndex == 1).Select(e => e.TimeMs).ToList();
        Check("sync: drum and guitar note-ons of the same beats are compiled at the same times",
            guitarOn.Count == 3 && drumOn.Count == 3 && guitarOn.Zip(drumOn).All(p => Math.Abs(p.First - p.Second) < 0.001), $"{string.Join(",", guitarOn)} vs {string.Join(",", drumOn)}");

        // 2. Dispatched by the real scheduler (engine off, straight to the output): the same beat's drum and guitar reach it within 2 ms.
        var port = new StampingMidiOutput();
        var routed = new RoutedMidiOutput(port, AudioEngineClient.Instance);
        routed.SetRoutes(Enumerable.Repeat(-1, 16).ToArray());
        using (var playback = new PlaybackEngine(routed))
        {
            playback.StartDiagnostics();
            playback.Start(project, new PlaybackOptions { RepeatExpansion = true }, _ => { }, () => { });
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 1.4 && playback.IsPlaying) Thread.Sleep(20);
            playback.Stop();
        }
        List<(long Ticks, int Status, int Data1)> sent; lock (port.Sent) sent = port.Sent.ToList();
        var guitarSent = sent.Where(s => (s.Status & 0xF0) == 0x90 && (s.Status & 0x0F) == 0 && s.Data1 == 64).Select(s => s.Ticks).ToList();
        var drumSent = sent.Where(s => (s.Status & 0xF0) == 0x90 && (s.Status & 0x0F) == 9 && s.Data1 == 38).Select(s => s.Ticks).ToList();
        var pairs = Math.Min(guitarSent.Count, drumSent.Count);
        var worstMs = pairs == 0 ? double.MaxValue : Enumerable.Range(0, pairs).Max(i => Math.Abs(guitarSent[i] - drumSent[i]) * 1000.0 / Stopwatch.Frequency);
        Check("sync: engine off, the drum and guitar of the same beat reach the MIDI output together (within 2 ms)", pairs >= 2 && worstMs < 2, $"{pairs} beats, worst {worstMs:0.00} ms");

        // 3. Mixed routing (plug-in drums on the engine, guitar on Windows MIDI): each path is held back so both are heard together,
        //    for a fast engine over a slow Windows synth (ASIO), the reverse, and equal latencies.
        var f = Stopwatch.Frequency;
        var okAll = true; var detail = "";
        foreach (var (engineMs, windowsMs) in new[] { (12, 200), (12, 60), (250, 60), (60, 60), (0, 200) })
        {
            long engine = engineMs * f / 1000, windows = windowsMs * f / 1000;
            var (engineHold, windowsHold) = RoutedMidiOutput.Compensation(engine, windows, playAll: false);
            var heardEngine = engineHold + engine; var heardWindows = windowsHold + windows;
            if (engineHold < 0 || windowsHold < 0 || Math.Abs(heardEngine - heardWindows) > 1) { okAll = false; detail += $" engine {engineMs} / windows {windowsMs}: {heardEngine} vs {heardWindows};"; }
        }
        Check("sync: plug-in (engine) and Windows MIDI notes for the same beat are heard together for any pair of latencies", okAll, detail);
        Check("sync: everything on the engine holds nothing back", RoutedMidiOutput.Compensation(500, 100, playAll: true) == (0, 0));

        // 4. Defaults and migration: the measured 200 ms replaces the old guessed 60 ms once; any other stored value is the user's own.
        var dir = Path.Combine(Path.GetTempPath(), $"tf-winlat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string Load(string json) { var p = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(p, json); return p; }
            Check("Windows MIDI latency: the default is the measured 200 ms", new Services.PluginSettings().WindowsMidiLatencyMs == 200);
            Check("Windows MIDI latency: a stored 60 (the old guess) becomes 200 once",
                Services.SettingsFileService.Load(Load("{\"Plugins\":{\"WindowsMidiLatencyMs\":60}}")).Plugins.WindowsMidiLatencyMs == 200);
            Check("Windows MIDI latency: any other stored value stays (the user's own)",
                Services.SettingsFileService.Load(Load("{\"Plugins\":{\"WindowsMidiLatencyMs\":90}}")).Plugins.WindowsMidiLatencyMs == 90
                && Services.SettingsFileService.Load(Load("{\"Plugins\":{\"WindowsMidiLatencyMs\":250}}")).Plugins.WindowsMidiLatencyMs == 250);
            Check("Windows MIDI latency: after the migration a 60 typed again is kept",
                Services.SettingsFileService.Load(Load("{\"Plugins\":{\"WindowsMidiLatencyMs\":60,\"WindowsMidiLatencyVersion\":1}}")).Plugins.WindowsMidiLatencyMs == 60);
            Check("Windows MIDI latency: a file without the setting gets the default", Services.SettingsFileService.Load(Load("{}")).Plugins.WindowsMidiLatencyMs == 200);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}

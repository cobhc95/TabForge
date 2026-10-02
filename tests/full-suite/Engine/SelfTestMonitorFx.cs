using System.IO;
using TabForge.Models;
using EMix = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// Monitor FX (speaker / room calibration): the chain is heard on the live output, after the master, and a render never includes it
/// (neither the app-wide chain nor a song's own). The per-project opt-out round-trips and old files load with the defaults.
/// </summary>
public static partial class SelfTest
{
    private sealed class ConstInstrument : EP.IPluginInstance
    {
        public string Path => "selftest-const";
        public bool IsInstrument => true;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<EP.BlockMidi> midi, in EP.TransportInfo transport)
        {
            output[0].AsSpan(0, frames).Fill(0.25f); output[1].AsSpan(0, frames).Fill(0.25f);
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }

    private sealed class HalfGainEffect : EP.IPluginInstance
    {
        public string Path => "selftest-half";
        public bool IsInstrument => false;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<EP.BlockMidi> midi, in EP.TransportInfo transport)
        {
            for (var i = 0; i < frames; i++) { output[0][i] = input[0][i] * 0.5f; output[1][i] = input[1][i] * 0.5f; }
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }

    /// <summary>One headless mixer with a constant-level track; optionally a half-gain effect in the monitor slot. Returns the live peak and the rendered master file bytes.</summary>
    private static (float LivePeak, byte[] Render) MonitorFxRun(bool withMonitor)
    {
        using var shared = TabForge.Audio.Contracts.SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var mix = new EMix.MixEngine(shared, 48000, 64);
        mix.SetChain(0, new EMix.TrackChain(0, 0, null, new[] { new EMix.TrackChain.Effect(new ConstInstrument(), 1f, 0, IsInstrument: true) }, 64));
        mix.SetGraph(new EMix.MixEngine.RenderGraph(new[] { 0 }, EMix.MixEngine.RenderGraph.NewDest(), Array.Empty<int>()));
        if (withMonitor)
            mix.SetChain(EMix.MixEngine.MonitorSlot, new EMix.TrackChain(EMix.MixEngine.MonitorSlot, -1, null, new[] { new EMix.TrackChain.Effect(new HalfGainEffect(), 1f, 0) }, 64));
        var buffer = new float[128];
        var peak = 0f;
        // The live reads run on their own thread, as a device callback does: MixEngine.Read marks its caller as an audio thread
        // (flush-to-zero, MMCSS), and the self-test thread must stay unmarked (new threads inherit its SSE flags; RT-02 checks one).
        var device = new Thread(() =>
        {
            for (var b = 0; b < 40; b++)
            {
                mix.Read(buffer, 0, buffer.Length);
                if (b >= 30) foreach (var v in buffer) peak = MathF.Max(peak, MathF.Abs(v));
            }
        }) { IsBackground = true, Name = "selftest monitor fx device" };
        device.Start();
        device.Join();
        var dir = Directory.CreateTempSubdirectory("tf-monfx-").FullName;
        try
        {
            var events = System.IO.Path.Combine(dir, "e.events");
            TabForge.Audio.Contracts.RenderEventFile.Write(events, new List<TabForge.Audio.Contracts.RenderEvent>());
            var master = System.IO.Path.Combine(dir, "m.wav");
            var spec = new TabForge.Audio.Contracts.RenderSpec
            {
                StartFrame = 0, EndFrame = 4800, TailMode = TabForge.Audio.Contracts.RenderTailMode.Fixed, TailMs = 100, Channels = 2,
                Format = TabForge.Audio.Contracts.RenderFormat.Pcm24, MasterPath = master, EventFile = events, Threads = TabForge.Audio.Contracts.RenderThreads.One,
                Slots = { new TabForge.Audio.Contracts.RenderSlot { Slot = 0 } },
                Tempo = { new TabForge.Audio.Contracts.RenderTempoPoint(0, 120, 0) },
            };
            var renderer = new EMix.OfflineRenderer(spec, mix, shared, 48000, 64, () => false, _ => { });
            renderer.Prepare();
            try { renderer.Run(); } finally { renderer.Restore(); }
            return (peak, File.ReadAllBytes(master));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static void TestMonitorFx()
    {
        Section("Monitor FX");
        var plain = MonitorFxRun(false);
        var monitored = MonitorFxRun(true);
        Check("Monitor FX: the live output passes through the monitor chain (half-gain stand-in halves the level)",
            plain.LivePeak > 0.01f && monitored.LivePeak > 0 && Math.Abs(monitored.LivePeak / plain.LivePeak - 0.5f) < 0.02f, $"plain {plain.LivePeak:0.0000}, monitored {monitored.LivePeak:0.0000}");
        Check("Monitor FX: a render with a monitor chain loaded is byte-identical to one without (the monitor chain is never rendered)",
            plain.Render.Length > 1000 && plain.Render.AsSpan().SequenceEqual(monitored.Render), $"{plain.Render.Length} / {monitored.Render.Length} bytes");

        // Model: which chain is in effect, opt-out round trip, old files.
        var global = new BusChain();
        global.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "EQ", Path = @"C:\x\eq.dll" });
        var song = new SongProject();
        var useGlobal = MixerBuses.MonitorChain(song, global);
        Check("Monitor FX: a song uses the app-wide chain by default", ReferenceEquals(useGlobal, global) && song.Mixer.MonitorUseGlobal && MixerBuses.ActiveMonitor(song, global) is not null);
        song.Mixer.MonitorUseGlobal = false;
        Check("Monitor FX: after the opt-out the song's own (empty) chain is used, and nothing is loaded for it",
            ReferenceEquals(MixerBuses.MonitorChain(song, global), song.Mixer.MonitorFx) && MixerBuses.ActiveMonitor(song, global) is null);
        song.Mixer.MonitorFx.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Room", Path = @"C:\x\room.dll" });
        var json = System.Text.Json.JsonSerializer.Serialize(song.Mixer);
        var back = System.Text.Json.JsonSerializer.Deserialize<MixerSettings>(json)!;
        Check("Monitor FX: the per-project opt-out and its own chain round-trip through the project data",
            !back.MonitorUseGlobal && back.MonitorFx.Rig.Plugins.Count == 1 && back.MonitorFx.Rig.Plugins[0].Name == "Room" && !back.IsDefault);
        var old = System.Text.Json.JsonSerializer.Deserialize<MixerSettings>("{\"Grouping\":\"Compact\"}")!;
        Check("Monitor FX: an older project loads with the defaults (app-wide chain, own chain empty)", old.MonitorUseGlobal && old.MonitorFx.IsDefault && old.IsDefault);
        Check("Monitor FX: the app-wide chain lives in the settings and the 'Mixer.MonitorFx' command is bindable",
            new TabForge.Services.PluginSettings().MonitorFx.IsDefault && TabForge.Services.HotkeyCatalog.All.Any(c => c.Id == "Mixer.MonitorFx"));
    }
}

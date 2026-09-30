using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine;

public static partial class EngineHost
{
    /// <summary>
    /// Headless engine harness (T-01): drives the real engine code (Configure, chain building, clips, retirement) in the calling
    /// process, with no pipe and no sound card. Configure it with the <see cref="AudioOutputFactory.Null"/> driver; with device
    /// <see cref="AudioOutputFactory.NullManual"/> the test runs the audio callback itself through <see cref="Pump"/>. Events the
    /// engine would send to TabForge are dropped (there is no pipe). Call the methods from one thread (it plays the engine's
    /// main thread; <see cref="Attach"/> marks it); never inside a real engine process.
    /// </summary>
    public static class Headless
    {
        public static bool Attached => _session.PluginFactory is not null || (_shared is not null && _pipe is null);

        /// <param name="factory">Creates each plug-in of a chain (spec, sample rate, max block); the real VST loaders are not used.</param>
        public static void Attach(SharedBlock shared, Func<PluginSpec, double, int, IPluginInstance> factory)
        {
            if (_pipe is not null) throw new InvalidOperationException("The headless harness cannot run inside an engine process.");
            if (_shared is not null) throw new InvalidOperationException("The headless harness is already attached.");
            EngineThreads.MarkMainThread();
            _shared = shared;
            _session.PluginFactory = factory;
            _session.SampleRate = 48000;
            _session.Config = new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false);
        }

        public static void Configure(EngineConfig config) => _session.Configure(config);
        public static void LoadChain(int slot, string track, bool useMidiSynth, List<PluginSpec> specs) => _session.LoadChain(slot, track, useMidiSynth, specs);
        public static void RemoveChain(int slot) => _session.RemoveChain(slot);
        public static void SetClips(int slot, List<ClipSpec> clips) => _session.StoreClips(slot, clips);

        /// <summary>Song transport: playing from <paramref name="songSec"/> now.</summary>
        public static void SetPlaying(bool playing, double songSec)
        {
            _session.Transport.SetTempo(120);
            _session.Transport.SetPosition(playing, songSec, Stopwatch.GetTimestamp());
        }

        /// <summary>The engine main loop's housekeeping: posted work (and VST2 host notifications) and the retire queue.</summary>
        public static int Collect()
        {
            EngineThreads.RunPending();
            return _session.Retired.Collect();
        }

        /// <summary>The main loop's RT-02 check: sends (here: drops) one PluginMisbehaved per newly caught plug-in; returns how many.</summary>
        public static int ReportMisbehaving() => _session.ReportMisbehaving();

        /// <summary>The engine's song transport (position, tempo, bar map), as the command reader thread writes it.</summary>
        public static Mixing.SongTransport SongTransport => _session.Transport;

        /// <summary>Feeds one command frame through the engine's reader-side parser (S-03); false when it was dropped as malformed.</summary>
        public static bool Command(EngineCommand type, Action<BinaryWriter> payload)
        {
            using var body = new MemoryStream();
            using (var w = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true)) payload(w);
            body.Position = 0;
            using var r = new BinaryReader(body);
            return HandleFrame((byte)type, r);
        }

        /// <summary>Armed track slots (SetArm).</summary>
        public static int ArmedCount => _session.Arms.Count;

        public static NullOutput? Output => _session.Player as NullOutput;
        public static MixEngine? Mix => _session.Mix;
        public static int SampleRate => _session.SampleRate;
        public static TrackChain? ChainAt(int slot) => _session.Loaded.GetValueOrDefault(slot);
        public static int Retiring => _session.Retired.Count;

        /// <summary>Manual null output: runs <paramref name="blocks"/> audio callbacks on the calling thread.</summary>
        public static int Pump(int blocks) => Output?.Pump(blocks) ?? 0;

        /// <summary>Stops the output, disposes every chain and plug-in, and forgets all engine state (the SharedBlock stays the caller's).</summary>
        public static void Detach()
        {
            var s = _session;
            try { s.StopOutput(); } catch (Exception ex) { EngineLog.Write($"headless: stop: {ex.Message}"); }
            foreach (var chain in s.Loaded.Values) { try { chain.Dispose(); } catch (Exception ex) { EngineLog.Write($"dispose failed: {ex.Message}"); } }
            foreach (var parked in s.Parked.Values) foreach (var p in parked.Values) { try { p.Dispose(); } catch (Exception ex) { EngineLog.Write($"dispose failed: {ex.Message}"); } }
            s.Retired.DisposeAll();
            s.Loaded.Clear(); s.Parked.Clear(); s.Requested.Clear(); s.ChainGeneration.Clear(); s.Mixes.Clear(); s.ClipSpecs.Clear(); s.Arms.Clear();
            s.Unmonitored.Clear(); s.Routes.Clear(); s.MidiProcs.Clear(); s.GraphDest.Clear(); s.Sidechains.Clear(); s.Forwards.Clear(); s.Wirings.Clear(); s.LogWatch.Clear();
            s.Mix = null;
            s.Transport.SetPosition(false, 0, 0); s.Transport.SetTempo(120); s.Transport.SetMap(Array.Empty<TransportBar>());
            _shared = null;
            s.PluginFactory = null;
            s.SampleRate = 48000;
            s.Config = new EngineConfig(AudioOutputFactory.WasapiShared, "", 48000, 256, false);
        }
    }
}

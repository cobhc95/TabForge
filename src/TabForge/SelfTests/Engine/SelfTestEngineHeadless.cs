using System.Collections.Concurrent;
using System.IO;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using EA = TabForge.AudioEngine.Audio;
using EH = TabForge.AudioEngine.EngineHost.Headless;
using EO = TabForge.AudioEngine.Output;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// Engine lifecycle tests that need no device (audit 2026-09-29 T-01): the real engine code driven in process through the
/// headless harness and the null output. Device reconfigure (R-01), clip churn under streaming (R-02), several producers on
/// the MIDI ring (R-03) and engine-exit cleanup on the UI thread (R-04). Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    /// <summary>R-03: 4 producers x 100k sequence numbers through AudioEngineClient.Write against one consumer.</summary>
    private static void TestSharedRingProducers()
    {
        const int producers = 4, perProducer = 100_000;
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var client = new AudioEngineClient();
        client.AttachForTest(shared, new SynchronizationContext(), 0);
        var next = new long[producers];
        long lost = 0, duplicated = 0, foreign = 0, received = 0;
        var consumer = new Thread(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (received < producers * (long)perProducer && DateTime.UtcNow < deadline)
            {
                if (!shared.TryRead(out var e)) { Thread.SpinWait(50); continue; }
                received++;
                var p = e.Slot; var seq = e.Timestamp & 0xFFFFFFFF;
                if (p is < 0 or >= producers || (e.Timestamp >> 32) != p) { foreign++; continue; }
                if (seq > next[p]) { lost += seq - next[p]; next[p] = seq + 1; }
                else if (seq < next[p]) duplicated++;
                else next[p]++;
            }
        }) { IsBackground = true, Name = "selftest ring consumer" };
        consumer.Start();
        var threads = Enumerable.Range(0, producers).Select(p => new Thread(() =>
        {
            for (var i = 0; i < perProducer; i++)
            {
                var m = new TimedMidi { Timestamp = ((long)p << 32) | (uint)i, Slot = p, Status = 0x90 };
                while (!client.Write(m)) Thread.SpinWait(20);   // full: retry (the consumer drains)
            }
        }) { IsBackground = true, Name = $"selftest ring producer {p}" }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join(60_000));
        consumer.Join(60_000);
        for (var p = 0; p < producers; p++) lost += perProducer - next[p];
        Check("R-03: 4 producers x 100k writes through AudioEngineClient.Write: nothing lost, duplicated or torn, per-producer order kept",
            received == producers * (long)perProducer && lost == 0 && duplicated == 0 && foreign == 0,
            $"received {received}, lost {lost}, duplicated {duplicated}, torn {foreign}");
        client.Dispose();
        client.DisposeRetiredForTest();
    }

    /// <summary>Runs posted callbacks only when the test pumps it: the test thread plays the UI thread.</summary>
    private sealed class PumpedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
        public int Pump() { var n = 0; while (_queue.TryDequeue(out var item)) { item.Callback(item.State); n++; } return n; }
    }

    /// <summary>R-04: 200 engine exits while a producer writes in a loop; cleanup only on the UI thread, no use of an unmapped block.</summary>
    private static void TestEngineExitCleanupOnUi()
    {
        var ui = new PumpedContext();
        var client = new AudioEngineClient();
        var stop = false;
        long writes = 0, writerErrors = 0;
        string firstError = "";
        var writer = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                try { client.Write(new TimedMidi { Timestamp = writes, Slot = 0, Status = 0x80 }); writes++; }
                catch (Exception ex) { if (Interlocked.Increment(ref writerErrors) == 1) firstError = $"{ex.GetType().Name}: {ex.Message}"; }
            }
        }) { IsBackground = true, Name = "selftest scheduler writer" };
        var untouchedOffUi = true;
        var cleanedOnUi = true;
        var heldUntilRestart = true;
        writer.Start();
        try
        {
            for (var i = 0; i < 200; i++)
            {
                client.AttachForTest(SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}"), ui, 8);
                Thread.Sleep(i % 20 == 0 ? 2 : 0);
                Task.Run(client.SimulateEngineExitForTest).Wait();   // Process.Exited runs on a pool thread
                untouchedOffUi &= client.IsRunning && client.SentChainCountForTest == 8;
                ui.Pump();                                          // the UI thread's turn: Cleanup happens here
                cleanedOnUi &= !client.IsRunning && client.SentChainCountForTest == 0;
                heldUntilRestart &= client.HoldsRetiredBlockForTest;
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join(5000);
            client.DisposeRetiredForTest();
        }
        Check("R-04: engine exit (pool thread) leaves UI-owned state alone; Cleanup runs on the UI thread (200 exits)",
            untouchedOffUi && cleanedOnUi, $"untouched off UI {untouchedOffUi}, cleaned on UI {cleanedOnUi}");
        Check("R-04: a producer writing throughout never hits a disposed block; the old block is kept until the next start",
            writerErrors == 0 && writes > 0 && heldUntilRestart, $"writes {writes}, errors {writerErrors} {firstError}, held {heldUntilRestart}");
    }

    /// <summary>A thread that plays the device's callback thread for the manual null output (keeps the audio-thread marks off the test thread).</summary>
    private sealed class CallbackThread : IDisposable
    {
        private readonly BlockingCollection<(Func<int> Work, TaskCompletionSource<int> Done)> _work = new();
        private readonly Thread _thread;
        public CallbackThread()
        {
            _thread = new Thread(() =>
            {
                foreach (var (work, done) in _work.GetConsumingEnumerable())
                {
                    try { done.SetResult(work()); } catch (Exception ex) { done.SetException(ex); }
                }
            }) { IsBackground = true, Name = "selftest audio callback" };
            _thread.Start();
        }
        public int Pump(int blocks)
        {
            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add((() => EH.Pump(blocks), done));
            return done.Task.Wait(30_000) ? done.Task.Result : -1;
        }
        /// <summary>Runs <paramref name="work"/> on the callback thread (for reading the mixer's output directly).</summary>
        public int Run(Func<int> work)
        {
            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add((work, done));
            return done.Task.Wait(30_000) ? done.Task.Result : -1;
        }
        public void Dispose() { _work.CompleteAdding(); _thread.Join(5000); _work.Dispose(); }
    }

    private static string WriteTestWav(double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-selftest-{Guid.NewGuid():N}.wav");
        using var writer = new NAudio.Wave.WaveFileWriter(path, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var block = new float[4800 * 2];
        for (var done = 0; done < seconds * 48000; done += 4800)
        {
            for (var i = 0; i < 4800; i++) block[i * 2] = block[i * 2 + 1] = 0.1f * MathF.Sin((done + i) * 0.05f);
            writer.WriteSamples(block, 0, block.Length);
        }
        return path;
    }

    private static int HandleCount() { using var p = System.Diagnostics.Process.GetCurrentProcess(); return p.HandleCount; }

    /// <summary>Waits (bounded) until the disk thread has closed every handed-over player and done two more passes.</summary>
    private static void SettleDisk()
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until && (EA.DiskStreamer.PendingClose > 0 || EH.Collect() > 0 || EH.Retiring > 0)) Thread.Sleep(5);
        var passes = EA.DiskStreamer.Passes;
        while (DateTime.UtcNow < until && EA.DiskStreamer.Passes < passes + 2 && !EA.DiskStreamer.Parked) Thread.Sleep(5);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    private static EngineConfig NullConfig(int rate, int block, bool manual) =>
        new(EO.AudioOutputFactory.Null, manual ? EO.AudioOutputFactory.NullManual : "", rate, block, false);

    /// <summary>R-01 (and T-01): a device change reconfigures live plug-ins in place and retires the previous chains.</summary>
    private static void TestHeadlessDeviceReconfigure()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, rate, block) => spec.Path == EP.Vst2Plugin.TestEffect.PathName
            ? EP.Vst2Plugin.TestEffect.Create(rate, block) : throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        var wav = WriteTestWav(3);
        using var callback = new CallbackThread();
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: true));
            EH.LoadChain(0, "selftest", false, new List<PluginSpec> { new(EP.Vst2Plugin.TestEffect.PathName, "VST2", false, true, 100, null, Id: "fx") });
            var plugin = EH.ChainAt(0)?.Plugins.FirstOrDefault();
            EP.Vst2Plugin.TestEffect.ResetLog(256);
            var ran = callback.Pump(20);
            Check("T-01: the manual null output runs the real mixer in process (20 callbacks of 256 frames through a hosted VST2)",
                plugin is EP.Vst2Plugin && ran == 20 && EP.Vst2Plugin.TestEffect.Blocks == 20 && EP.Vst2Plugin.TestEffect.MaxFrames == 256
                && EP.Vst2Plugin.TestEffect.SampleRate == 48000 && EP.Vst2Plugin.TestEffect.BlockSize == 256,
                $"plug-in {plugin?.GetType().Name}, ran {ran}, blocks {EP.Vst2Plugin.TestEffect.Blocks}, max {EP.Vst2Plugin.TestEffect.MaxFrames}, rate {EP.Vst2Plugin.TestEffect.SampleRate}");

            EP.Vst2Plugin.TestEffect.ResetLog(1024);
            EH.Configure(NullConfig(44100, 1024, manual: true));
            var ops = EP.Vst2Plugin.TestEffect.Opcodes;
            var same = ReferenceEquals(EH.ChainAt(0)?.Plugins.FirstOrDefault(), plugin);
            ran = callback.Pump(20);
            Check("R-01: 48 kHz/256 -> 44.1 kHz/1024 keeps the live VST2 instance and runs stop, mains off, sample rate, block size, mains on, start",
                same && ops.SequenceEqual(EP.Vst2Plugin.TestEffect.ReconfigureSequence) && EP.Vst2Plugin.TestEffect.SampleRate == 44100 && EP.Vst2Plugin.TestEffect.BlockSize == 1024,
                $"same instance {same}, opcodes [{string.Join(",", ops)}], rate {EP.Vst2Plugin.TestEffect.SampleRate}, block {EP.Vst2Plugin.TestEffect.BlockSize}");
            Check("R-01: after the change every processed block is full length (1024 frames)",
                ran == 20 && EP.Vst2Plugin.TestEffect.Blocks == 20 && EP.Vst2Plugin.TestEffect.ShortBlocks == 0 && EP.Vst2Plugin.TestEffect.MinFrames == 1024,
                $"ran {ran}, blocks {EP.Vst2Plugin.TestEffect.Blocks}, short {EP.Vst2Plugin.TestEffect.ShortBlocks}, min {EP.Vst2Plugin.TestEffect.MinFrames}, max {EP.Vst2Plugin.TestEffect.MaxFrames}");

            // GM synth: follows the rate in place (when Windows' gm.dls is available).
            EH.LoadChain(1, "gm", true, new List<PluginSpec>());
            if (EH.ChainAt(1)?.MidiSynth is TabForge.AudioEngine.Synth.GmSynth synth)
            {
                EH.Configure(NullConfig(48000, 512, manual: true));
                Check("R-01: the GM synth follows the device rate (44.1 -> 48 kHz), same instance",
                    ReferenceEquals(EH.ChainAt(1)?.MidiSynth, synth) && synth.SampleRate == 48000, $"rate {synth.SampleRate}");
            }
            else Skip("R-01: the GM synth follows the device rate", "the General MIDI synth could not be created here (no gm.dls)");

            // 20 device changes with two streaming clips: no leaked chains, clip players or handles.
            var clip = new ClipSpec(wav, 0, 0, 3, 0, 0, 1);
            EH.SetClips(0, new List<ClipSpec> { clip, clip with { StartSec = 0.5 } });
            EH.SetPlaying(true, 0);
            callback.Pump(8);
            SettleDisk();
            var baseStreamed = EA.DiskStreamer.Count;
            var baseHandles = HandleCount();
            for (var i = 0; i < 20; i++)
            {
                EH.Configure(i % 2 == 0 ? NullConfig(44100, 1024, true) : NullConfig(48000, 256, true));
                EH.SetPlaying(true, 0);
                callback.Pump(4);
                EH.Collect();
            }
            SettleDisk();
            var streamed = EA.DiskStreamer.Count;
            var handles = HandleCount();
            Check("R-01: 20 device changes: the disk thread's player count stays flat (previous chains and their clips are retired)",
                streamed == baseStreamed && EA.DiskStreamer.PendingClose == 0, $"players {baseStreamed} -> {streamed}, pending close {EA.DiskStreamer.PendingClose}");
            Check("R-01: 20 device changes: the process handle count stays flat", handles <= baseHandles + 40, $"handles {baseHandles} -> {handles}");
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Per-song clip transport: two songs with audio clips play at the same time, each at its own position. A song that is stopped (or
    /// stops) does not silence the other, and a song that plays at 5 s hears its clip at 5 s while the other sits at 0.
    /// </summary>
    private static void TestEngineOwnerTransports()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        var wav = WriteTestWav(3);
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: false));
            EH.LoadChain(0, "song one", false, new List<PluginSpec>());
            EH.LoadChain(1, "song two", false, new List<PluginSpec>());
            // Song one's clip starts at 5 s, song two's at 0 s: each is heard only while its own song is inside the clip.
            EH.SetClips(0, new List<ClipSpec> { new(wav, 5, 0, 3, 0, 0, 1) }, owner: 1);
            EH.SetClips(1, new List<ClipSpec> { new(wav, 0, 0, 3, 0, 0, 1) }, owner: 2);
            foreach (var slot in new[] { 0, 1 }) EH.Command(EngineCommand.SetTrackMix, w => { w.Write(slot); w.Write(100); w.Write(64); });
            EH.Collect();

            // Waits (bounded) until the slot's meter shows signal; the disk thread can lag behind under load.
            float Heard(int slot, int ms = 5000)
            {
                var best = 0f;
                var until = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < until && best <= 0.01f) { EH.Collect(); best = Math.Max(best, shared.Peak(slot)); Thread.Sleep(2); }
                return best;
            }
            float Silent(int slot) { var peak = 0f; var until = DateTime.UtcNow.AddMilliseconds(250); while (DateTime.UtcNow < until) { EH.Collect(); peak = Math.Max(peak, shared.Peak(slot)); Thread.Sleep(2); } return peak; }

            EH.SetPlaying(1, true, 5.0);
            EH.SetPlaying(2, false, 0.0);
            var oneAt5 = Heard(0);
            var twoStopped = Silent(1);
            Check("owners: song one plays at 5 s and hears its clip while song two is stopped at 0 s (the stopped song stays silent)",
                oneAt5 > 0.01f && twoStopped < 0.002f, $"one {oneAt5:0.####}, two {twoStopped:0.####}");

            EH.SetPlaying(2, true, 0.0);
            var both0 = Heard(1); var both1 = Heard(0);
            Check("owners: with song two also playing (at 0 s) both songs are heard at once, each at its own position", both0 > 0.01f && both1 > 0.01f, $"two {both0:0.####}, one {both1:0.####}");

            EH.SetPlaying(2, false, 0.0);
            Thread.Sleep(700);   // the meter holds its last peak for a moment
            var oneAfter = Heard(0);
            var twoAfter = Silent(1);
            Check("owners: stopping song two leaves song one playing and silences only song two", oneAfter > 0.01f && twoAfter < 0.002f, $"one {oneAfter:0.####}, two {twoAfter:0.####}");
            Check("owners: SongPlaying is true while any song plays", EH.Mix?.SongPlaying == true);
            EH.SetPlaying(1, false, 5.0);
            Check("owners: SongPlaying is false once every song has stopped", EH.Mix?.SongPlaying == false);
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void DropOwnerWhilePlaying(AudioEngineClient client, out int id)
    {
        var dropped = new object();
        id = client.OwnerIdOf(dropped);
        client.SetPosition(true, 3.0, System.Diagnostics.Stopwatch.GetTimestamp(), id);
    }

    /// <summary>Transport ids: a dropped song's engine transport is stopped, renders take no id, and a full table never lets a song stop another.</summary>
    private static void TestEngineOwnerIdLifecycle()
    {
        var client = new AudioEngineClient();
        try
        {
            client.AttachFakeForTest();
            var sent = new List<EngineCommand>();
            client.SentForTest = c => sent.Add(c);

            var project = TabForge.Presets.TemplateFactory.Blank();
            var anonymous = client.OwnerIdOf(project);
            var first = new object();
            Check("owner ids: a render's bare project takes no id (the first document still gets 0)", anonymous == 0 && client.OwnerIdOf(first) == 0, $"anonymous {anonymous}");

            DropOwnerWhilePlaying(client, out var droppedId);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            sent.Clear();
            var next = new object();
            var nextId = client.OwnerIdOf(next);
            Check("owner ids: a song dropped while it played has its engine transport stopped and its id reused",
                nextId == droppedId && sent.Contains(EngineCommand.SetPosition) && client.PositionSentForTest(droppedId) is null, $"dropped {droppedId}, next {nextId}, sent {string.Join(",", sent)}");

            // Fill the table: the next song shares transport 0 and cannot stop the song that owns it.
            var holders = new List<object> { first, next };
            while (holders.Count < SongOwners.Max) { var o = new object(); client.OwnerIdOf(o); holders.Add(o); }
            var sharing = new object();
            var sharedId = client.OwnerIdOf(sharing);
            Check("owner ids: with every id taken a further song shares transport 0, and is known to share", sharedId == 0 && client.ExistingOwnerId(sharing) == 0 && client.SharesOwnerZero(sharing));
            Check("owner ids: a document without an id is not mistaken for a sharing one", client.ExistingOwnerId(new object()) == -1);
            var songA = TabForge.Presets.TemplateFactory.Blank();
            var clock = new SongClock(client) { OwnerKey = sharing };
            _ = clock.BarStartSec(songA, 0);
            clock.Report(songA, new TabForge.Playback.PlaybackPosition { Bar = 1, BarFraction = 0 }, playing: true);
            var before = client.PositionSentForTest(0);
            clock.Stopped();
            Check("owner ids: a song sharing transport 0 never stops it (its stop is not sent)", before is { Playing: true } && client.PositionSentForTest(0) is { Playing: true }, $"before {before}, after {client.PositionSentForTest(0)}");
            client.ReleaseOwner(sharing);
            PumpUi();
            Check("owner ids: releasing a sharing song frees nothing and stops nothing", client.PositionSentForTest(0) is { Playing: true } && client.ExistingOwnerId(sharing) == -1);
            GC.KeepAlive(holders);
        }
        finally { client.SentForTest = null; client.Stop(); }
    }

    /// <summary>Audit 2 C9: the disk thread parks with no players and wakes at the idle interval (not 1 ms) while clips sit stopped.</summary>
    private static void TestDiskStreamerIdle()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        var wav = WriteTestWav(1);
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: false));
            EH.LoadChain(0, "clips", false, new List<PluginSpec>());
            var clip = new ClipSpec(wav, 0, 0, 1, 0, 0, 1);
            EH.SetClips(0, new List<ClipSpec> { clip, clip with { StartSec = 0.5 }, clip with { StartSec = 5 } });
            EH.SetPlaying(false, 0);
            Thread.Sleep(50);
            var p0 = EA.DiskStreamer.Passes;
            Thread.Sleep(500);
            var stoppedRate = (EA.DiskStreamer.Passes - p0) * 2;
            EH.SetPlaying(true, 0);   // plays past the end of the 1 s clips: drained sources must not keep it hungry
            Thread.Sleep(1800);
            p0 = EA.DiskStreamer.Passes;
            Thread.Sleep(500);
            var drainedRate = (EA.DiskStreamer.Passes - p0) * 2;
            EH.SetClips(0, new List<ClipSpec>());
            SettleDisk();
            var parked = EA.DiskStreamer.Parked;
            p0 = EA.DiskStreamer.Passes;
            Thread.Sleep(300);
            var parkedPasses = EA.DiskStreamer.Passes - p0;
            Check("C9: disk thread wake-ups: <= 150/s with stopped clips and with clips played to their end (was up to 1000/s)",
                stoppedRate <= 150 && drainedRate <= 150, $"stopped {stoppedRate}/s, drained {drainedRate}/s");
            Check("C9: with no clips registered the disk thread parks (no wake-ups at all)",
                parked && EA.DiskStreamer.Count == 0 && parkedPasses == 0, $"parked {parked}, players {EA.DiskStreamer.Count}, passes in 300 ms {parkedPasses}");
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    /// <summary>R-02: 1,000 clip-list swaps while the clocked null output plays and the disk thread streams.</summary>
    private static void TestClipChurnWhileStreaming()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        var wavA = WriteTestWav(2);
        var wavB = WriteTestWav(2);
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: false));   // clocked: the audio callback runs on its own thread meanwhile
            EH.LoadChain(0, "clips", false, new List<PluginSpec>());
            EH.SetPlaying(true, 0);
            SettleDisk();
            var baseStreamed = EA.DiskStreamer.Count;
            var baseFaults = EA.DiskStreamer.Faults;
            var baseHandles = HandleCount();
            var baseBlocks = EH.Output?.Blocks ?? 0;
            for (var i = 0; i < 1000; i++)
            {
                var a = new ClipSpec(wavA, 0, 0, 2, 0, 0, 1);
                var b = new ClipSpec(wavB, 0.25, 0.1, 1.5, -3, i % 3 == 0 ? 2 : 0, i % 3 == 0 ? 1.25 : 1);   // some stretched (SoundTouch)
                EH.SetClips(0, i % 2 == 0 ? new List<ClipSpec> { a, b } : new List<ClipSpec> { b });
                if (i % 50 == 0) EH.SetPlaying(true, i % 100 == 0 ? 0 : 0.4);   // jumps: seeks on the disk thread
                EH.Collect();
                if (i % 5 == 0) Thread.Sleep(1);
            }
            EH.SetClips(0, new List<ClipSpec>());
            SettleDisk();
            var handles = HandleCount();
            var blocks = (EH.Output?.Blocks ?? 0) - baseBlocks;
            Check("R-02: 1,000 clip-list swaps while streaming: the disk thread survives with no exception",
                EA.DiskStreamer.Running && EA.DiskStreamer.Faults == baseFaults && blocks > 0, $"running {EA.DiskStreamer.Running}, faults {EA.DiskStreamer.Faults - baseFaults}, audio blocks {blocks}");
            Check("R-02: every swapped-out player's file is closed (no growth in streamed players or open handles)",
                EA.DiskStreamer.Count == baseStreamed && EA.DiskStreamer.PendingClose == 0 && handles <= baseHandles + 40,
                $"players {baseStreamed} -> {EA.DiskStreamer.Count}, pending {EA.DiskStreamer.PendingClose}, handles {baseHandles} -> {handles}");
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
            foreach (var f in new[] { wavA, wavB }) { try { File.Delete(f); } catch (IOException) { } }
        }
    }

    /// <summary>Adding an audio track (with and without a clip) during playback: no callback over budget, no deadline miss, no allocation.</summary>
    private static void TestAudioTrackAddDuringPlayback()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        var wav = WriteTestWav(2);
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: false));   // clocked: the callback runs on its own thread meanwhile
            EH.LoadChain(0, "song", true, new List<PluginSpec>());
            EH.SetPlaying(true, 0);
            SettleDisk();
            Thread.Sleep(300);
            var mix = EH.Mix!;
            mix.Metrics.TakeAndReset();
            var budgetMs = 256 * 1000.0 / 48000;
            for (var slot = 1; slot <= 8; slot++)
            {
                EH.LoadChain(slot, $"audio {slot}", false, new List<PluginSpec>());
                if (slot % 2 == 0) EH.SetClips(slot, new List<ClipSpec> { new(wav, 0, 0, 2, 0, 0, 1) });
                EH.Collect();
                Thread.Sleep(40);
            }
            Thread.Sleep(200);
            var m = mix.Metrics.TakeAndReset();
            Log.Add($"        track add: calls {m.Calls}, max {m.MaxMs:0.00} ms, p99 {m.P99Ms:0.0} ms, misses {m.DeadlineMisses}, late {m.LateCalls}, allocated {m.AllocatedBytes} B");
            Check("Adding audio tracks (with and without a clip) during playback: every callback within its block budget, no allocation",
                m.Calls > 20 && m.MaxMs < budgetMs && m.DeadlineMisses == 0 && m.AllocatedBytes == 0,
                $"calls {m.Calls}, max {m.MaxMs:0.00} ms (budget {budgetMs:0.00}), misses {m.DeadlineMisses}, late {m.LateCalls}, allocated {m.AllocatedBytes} B");
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
            try { File.Delete(wav); } catch (IOException) { }
        }
    }
}

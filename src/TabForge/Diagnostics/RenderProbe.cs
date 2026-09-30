using System.Diagnostics;
using System.IO;
using System.Text;
using NAudio.Wave;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Diagnostics;

// `--render-probe <out.wav>`: end-to-end check of the offline renderer through the real engine process. A generated
// General MIDI test (a chord arpeggio) is rendered twice (parallel workers, then one worker) to <out.wav> and
// <out>.2.wav, and checked for the exact file length, non-silence, a stem of the same length and bit-identical files.
// The report goes to <out>.txt. Needs no audio device (the engine renders with its device closed).
internal static partial class DiagnosticCommands
{
    private static int RunRenderProbe(string[] args)
    {
        if (args.Length < 2) return Usage("--render-probe <out.wav>");
        return Guard("Render probe", () =>
        {
            var outPath = Path.GetFullPath(args[1]);
            var second = Path.ChangeExtension(outPath, ".2.wav");
            var stem = Path.ChangeExtension(outPath, ".stem.wav");
            var report = new StringBuilder();
            var failures = new List<string>();
            var client = AudioEngineClient.Instance;
            client.WarmIdle = TimeSpan.Zero;   // the probe's last Sync stops the engine (no R-10 warm period headless)
            var settings = new PluginSettings();
            MixerGroups.PlayAllThroughEngine = true;   // the blank track has no plug-ins: it plays on the engine's General MIDI synth

            bool WaitFor(Func<bool> condition, int ms)
            {
                var sw = Stopwatch.StartNew();
                while (!condition() && sw.ElapsedMilliseconds < ms)
                {
                    Thread.Sleep(10);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                }
                return condition();
            }

            var song = TemplateFactory.Blank();
            var track = song.Tracks[0];
            var chainLoaded = false;
            client.ChainLoaded += () => chainLoaded = true;
            client.Sync(song.Tracks, settings);
            var slot = client.SlotOf(track);
            if (slot < 0) { Console.Error.WriteLine("The engine did not start or gave the test track no slot."); return CouldNotRun; }
            WaitFor(() => client.Output is not null, 8000);   // ready (device open); a machine without an output device still renders
            WaitFor(() => chainLoaded, 20000);
            var rate = client.Output?.Rate ?? settings.SampleRate;
            report.AppendLine($"engine rate {rate} Hz, slot {slot}, chain loaded {chainLoaded}");
            if (!chainLoaded) failures.Add("the General MIDI chain did not load");

            // 3 s of music, 0.5 s fixed tail.
            long Frames(double seconds) => (long)Math.Round(seconds * rate);
            var events = new List<RenderEvent> { new() { Frame = 0, Slot = slot, Status = 0xC0, Data1 = 0 } };
            int[] notes = { 60, 64, 67, 72, 67, 64 };
            for (var i = 0; i < notes.Length; i++)
            {
                events.Add(new RenderEvent { Frame = Frames(i * 0.5), Slot = slot, Status = 0x90, Data1 = (byte)notes[i], Data2 = 100 });
                events.Add(new RenderEvent { Frame = Frames(i * 0.5 + 0.45), Slot = slot, Status = 0x80, Data1 = (byte)notes[i], Data2 = 0 });
            }
            var eventFile = Path.ChangeExtension(outPath, ".events");
            RenderEventFile.Write(eventFile, events);
            var mainFrames = Frames(3.0); var tailFrames = Frames(0.5);

            RenderResult? Render(string master, string? stemPath, RenderThreads threads)
            {
                var spec = new RenderSpec
                {
                    StartFrame = 0, EndFrame = mainFrames, TailMode = RenderTailMode.Fixed, TailMs = 500, Channels = 2, Format = RenderFormat.Pcm16,
                    MasterPath = master, EventFile = eventFile, Threads = threads,
                    Slots = { new RenderSlot { Slot = slot, StemPath = stemPath ?? "" } },
                    Tempo = { new RenderTempoPoint(0, 120, 0) },
                };
                var task = client.RenderAsync(spec);
                if (!WaitFor(() => task.IsCompleted, 60000)) { failures.Add("render timed out"); return null; }
                if (task.IsFaulted) { failures.Add("render failed: " + task.Exception!.GetBaseException().Message); return null; }
                return task.Result;
            }

            var first = Render(outPath, stem, RenderThreads.Auto);
            var again = Render(second, null, RenderThreads.One);
            foreach (var (name, result) in new[] { ("run 1 (auto threads)", first), ("run 2 (one thread)", again) })
                if (result is not null) report.AppendLine($"{name}: {result.Seconds:0.00} s of audio in {result.ElapsedSeconds:0.000} s = {result.Seconds / Math.Max(0.001, result.ElapsedSeconds):0.0}x realtime, {result.Workers} worker(s), {result.Files.Count} file(s)");

            (long Frames, float Peak, int Channels) Inspect(string path)
            {
                using var reader = new WaveFileReader(path);
                var frames = reader.SampleCount;
                var provider = reader.ToSampleProvider();
                var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
                var peak = 0f; int n;
                while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) for (var i = 0; i < n; i++) peak = MathF.Max(peak, MathF.Abs(buffer[i]));
                return (frames, peak, reader.WaveFormat.Channels);
            }

            var expected = mainFrames + tailFrames;
            foreach (var (label, path) in new[] { ("master", outPath), ("master (1 thread)", second), ("stem", stem) })
            {
                if (!File.Exists(path)) { failures.Add($"{label} file is missing"); continue; }
                var (frames, peak, channels) = Inspect(path);
                report.AppendLine($"{label}: {frames} frames (expected {expected}), {channels} ch, peak {peak:0.000}");
                if (frames != expected) failures.Add($"{label} has {frames} frames, expected {expected}");
                if (peak < 0.01f) failures.Add($"{label} is silent");
            }
            var identical = File.Exists(outPath) && File.Exists(second) && File.ReadAllBytes(outPath).AsSpan().SequenceEqual(File.ReadAllBytes(second));
            report.AppendLine($"bit-identical across the two runs: {identical}");
            if (!identical) failures.Add("the two renders differ");

            // The device must be back: playing still works and a later render is possible.
            report.AppendLine(failures.Count == 0 ? "render probe passed" : "failures: " + string.Join("; ", failures));
            File.WriteAllText(Path.ChangeExtension(outPath, ".txt"), report.ToString());
            Console.WriteLine(report.ToString());
            try { File.Delete(eventFile); } catch (IOException) { }
            client.Sync(Array.Empty<TrackModel>(), settings);
            return failures.Count == 0 ? Ok : CheckFailed;
        });
    }
}

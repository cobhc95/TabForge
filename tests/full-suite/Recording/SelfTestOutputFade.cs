using System.Diagnostics;
using System.IO;
using TabForge.Audio.Contracts;
using EMix = TabForge.AudioEngine.Mixing;

namespace TabForge;

/// <summary>
/// The pop guard on fakes (no device): the output ramp, the bounded wait before the output stops, the config field, and the
/// pause / resume of playing songs around a device change.
/// </summary>
public static partial class SelfTest
{
    private static void TestOutputFade()
    {
        const int rate = 48000, frames = 256;
        var rampFrames = rate * EMix.OutputFade.RampMs / 1000;
        float[] Ones() { var b = new float[frames * 2]; Array.Fill(b, 1f); return b; }

        // At rest the block is untouched.
        var fade = new EMix.OutputFade(rate);
        var block = Ones();
        fade.Apply(block, 0, frames);
        Check("output fade: at rest the block is untouched", block.All(v => v == 1f) && !fade.IsSilent);

        // 1 -> 0: monotonic, ends at exactly 0, then whole blocks of zeros and the silence report.
        fade.FadeOut();
        var down = new List<float>();
        var blocks = 0;
        while (!fade.IsSilent && blocks++ < 100) { block = Ones(); fade.Apply(block, 0, frames); for (var i = 0; i < frames; i++) down.Add(block[2 * i]); }
        var firstZero = down.IndexOf(0f);
        var monotonic = true;
        for (var i = 1; i < down.Count; i++) if (down[i] > down[i - 1]) monotonic = false;
        Check($"output fade: the ramp down is monotonic and reaches exactly 0 after about {rampFrames} frames (measured {firstZero})",
            monotonic && firstZero >= rampFrames - 2 && firstZero <= rampFrames + 2 && down.Skip(firstZero).All(v => v == 0f));
        Check("output fade: silence is reported only after a whole silent callback", fade.IsSilent && down.Count >= firstZero + frames);
        block = Ones(); fade.Apply(block, 0, frames);
        Check("output fade: every block after the ramp down is all zeros", block.All(v => v == 0f));

        // 0 -> 1: the first callback of the new output is silent, the ramp ends at exactly 1, later blocks are untouched.
        fade.FadeIn();
        block = Ones(); fade.Apply(block, 0, frames);
        Check("output fade: the first block of a new output is silent", block.All(v => v == 0f));
        var up = new List<float>();
        for (var b = 0; b < rampFrames / frames + 2; b++) { block = Ones(); fade.Apply(block, 0, frames); for (var i = 0; i < frames; i++) up.Add(block[2 * i]); }
        var firstOne = up.IndexOf(1f);
        monotonic = true;
        for (var i = 1; i < up.Count; i++) if (up[i] < up[i - 1]) monotonic = false;
        Check($"output fade: the ramp up is monotonic and reaches exactly 1 after about {rampFrames} frames (measured {firstOne})",
            monotonic && firstOne >= rampFrames - 2 && firstOne <= rampFrames + 2 && up.Skip(firstOne).All(v => v == 1f));
        var noise = new float[frames * 2];
        for (var i = 0; i < noise.Length; i++) noise[i] = MathF.Sin(i * 0.37f);
        var copy = (float[])noise.Clone();
        fade.Apply(noise, 0, frames);
        Check("output fade: after the ramp up blocks are bit-identical to the input", noise.AsSpan().SequenceEqual(copy));

        // No allocation per block, at rest and while ramping.
        block = Ones();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var b = 0; b < 50; b++) { if (b == 10) fade.FadeOut(); if (b == 30) fade.FadeIn(); fade.Apply(block, 0, frames); }
        Check("output fade: no allocation per block", GC.GetAllocatedBytesForCurrentThread() - before == 0);

        // The stop sequence: with a running callback the wait returns only once silence was delivered; without one the wait gives up.
        var live = new EMix.OutputFade(rate);
        var stop = false;
        var pump = new Thread(() => { var b = new float[frames * 2]; while (!Volatile.Read(ref stop)) { live.Apply(b, 0, frames); Thread.Sleep(2); } }) { IsBackground = true };
        pump.Start();
        var sw = Stopwatch.StartNew();
        var silent = live.FadeOutAndWait();
        var took = sw.ElapsedMilliseconds;
        Volatile.Write(ref stop, true); pump.Join(1000);
        Check($"output fade: the stop waits for delivered silence and no longer than its cap ({took} ms)", silent && live.IsSilent && took <= EMix.OutputFade.WaitCapMs + 50);
        var dead = new EMix.OutputFade(rate);
        sw.Restart();
        var gaveUp = !dead.FadeOutAndWait();
        took = sw.ElapsedMilliseconds;
        Check($"output fade: with no callback running the wait gives up and the stop proceeds ({took} ms)", gaveUp && !dead.IsSilent && took >= EMix.OutputFade.StallMs - 5 && took <= EMix.OutputFade.WaitCapMs + 50);

        // The drain: after the silence the output keeps running for the device latency, then the stop may happen; no drain after a give-up.
        var drained = new EMix.OutputFade(rate);
        var silentCallbacks = 0;
        stop = false;
        pump = new Thread(() => { var b = new float[frames * 2]; while (!Volatile.Read(ref stop)) { drained.Apply(b, 0, frames); if (drained.IsSilent) silentCallbacks++; Thread.Sleep(2); } }) { IsBackground = true };
        pump.Start();
        Thread.Sleep(20);
        sw.Restart();
        var waited = new EMix.OutputFade(rate).FadeOutAndWait() ? -1 : sw.ElapsedMilliseconds;   // a dead output: the give-up time
        sw.Restart();
        silent = drained.FadeOutAndDrain(120);
        took = sw.ElapsedMilliseconds;
        var silentAtStop = silentCallbacks;
        Volatile.Write(ref stop, true); pump.Join(1000);
        Check($"output fade: the stop comes after the fade and the device latency of silence ({took} ms, {silentAtStop} silent callbacks)", silent && took >= 120 + EMix.OutputFade.RampMs && silentAtStop >= 2);
        sw.Restart();
        var noDrain = !new EMix.OutputFade(rate).FadeOutAndDrain(300);
        took = sw.ElapsedMilliseconds;
        Check($"output fade: no drain when the wait gave up ({took} ms; give-up alone {waited} ms)", noDrain && waited >= 0 && took < 200);
        Check("output fade: the drain is clamped", EMix.OutputFade.MaxDrainMs == 300);

        // A large device block (8192 frames, 170 ms): the wait does not give up between callbacks; the ramp and one silent block are delivered.
        const int big = 8192;
        var large = new EMix.OutputFade(rate);
        stop = false;
        pump = new Thread(() => { var b = new float[big * 2]; while (!Volatile.Read(ref stop)) { large.Apply(b, 0, big); Thread.Sleep(big * 1000 / rate); } }) { IsBackground = true };
        pump.Start();
        Thread.Sleep(30);   // the first callback is in; the next one is 170 ms away
        sw.Restart();
        silent = large.FadeOutAndWait();
        took = sw.ElapsedMilliseconds;
        Volatile.Write(ref stop, true); pump.Join(1000);
        Check($"output fade: a large device block still gets its ramp and a silent block ({took} ms)", silent && large.IsSilent && took > EMix.OutputFade.WaitCapMs);

        // The config field: travels at the tail; absent (an older sender) means on.
        var config = new EngineConfig("WASAPI", "out", 48000, 256, false, "in");
        static EngineConfig Trip(EngineConfig c, int cut = 0)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) w.Write(c);
            using var r = new BinaryReader(new MemoryStream(ms.ToArray(), 0, (int)ms.Length - cut));
            return r.ReadConfig();
        }
        Check("pop guard: on by default in the config and the settings", config.PopGuard && Trip(config).PopGuard && new TabForge.Services.PluginSettings().ProtectFromPops);
        Check("pop guard: off travels in the config; a config without the field means on",
            !Trip(config with { PopGuard = false }).PopGuard && Trip(config with { PopGuard = false }, cut: 1) == config
            && Trip(config with { PopGuard = false, AllInputs = false }, cut: 1) == config with { AllInputs = false });
    }

    private static void TestDeviceChangePause()
    {
        // Fake transports: the coordinator's decisions only (the real position keeping is PlaybackEngine.Pause / Resume).
        var playing = new FakeTransport { Playing = true, Position = 1234 };
        var stopped = new FakeTransport { Playing = false, Position = 77 };
        var userPaused = new FakeTransport { Playing = true, Paused = true, Position = 500 };
        var all = new[] { playing, stopped, userPaused };
        IEnumerable<(bool, Action, Action)> Rows() => all.Select(t => (t.Playing && !t.Paused, (Action)t.Pause, (Action)t.Resume));

        var pause = new TabForge.Audio.DeviceChangePause();
        pause.Changing(Rows());
        Check("device change: the playing song pauses, the stopped and the user-paused ones are left alone",
            playing.Paused && playing.Pauses == 1 && stopped.Pauses == 0 && userPaused.Pauses == 0 && pause.Waiting);
        pause.Ready();
        Check("device change: the song resumes by itself at the position it paused at", !playing.Paused && playing.Resumes == 1 && playing.Position == 1234 && !pause.Waiting);
        Check("device change: a stopped song stays stopped and a user-paused one stays paused", !stopped.Playing && stopped.Resumes == 0 && userPaused.Paused && userPaused.Resumes == 0);
        pause.Ready();
        Check("device change: a later ready resumes nothing again", playing.Resumes == 1);

        pause.Changing(Rows());
        pause.Failed();
        pause.Ready();
        Check("device change: when the device does not open the song stays paused with its position", playing.Paused && playing.Resumes == 1 && playing.Position == 1234);

        // Two changes in quick succession: the first answer resumes nothing, the last one resumes once.
        playing.Paused = false;
        pause.Changing(Rows());
        pause.Changing(Rows());   // the song is paused already: not paused twice
        pause.Ready();
        var afterFirst = playing.Paused && playing.Resumes == 1 && pause.Waiting;
        pause.Ready();
        Check("device change: with two changes under way only the last ready resumes, once", afterFirst && playing.Pauses == 3 && !playing.Paused && playing.Resumes == 2 && !pause.Waiting);
        // The first of two fails, the second opens: the song resumes on the device that is there.
        pause.Changing(Rows()); pause.Changing(Rows());
        pause.Failed();
        var keptPaused = playing.Paused;
        pause.Ready();
        Check("device change: a failed change followed by one that opens resumes", keptPaused && !playing.Paused && playing.Resumes == 3);
        DeviceChangeThroughHost();
    }

    /// <summary>The window side of a device change: the host's pause / resume path, a failed open, a recording.</summary>
    private static void DeviceChangeThroughHost()
    {
        var client = new TabForge.Audio.AudioEngineClient();
        var host = new EngineSyncTestHost();
        var controller = new TabForge.Controllers.EngineSyncController(host, client);
        var doc = host.ActiveDocument;
        var engine = doc.Playback.Engine;
        try
        {
            // Stopped: nothing is paused, nothing resumes.
            controller.OnDeviceChanging(); controller.OnDeviceReady();
            Check("device change: a stopped song is not touched by the host path", host.Toggles == 0 && !engine.IsPlaying);

            engine.Start(doc.Project, new TabForge.Playback.PlaybackOptions(), _ => { }, () => { });
            var started = engine.IsPlaying && !engine.IsPaused;
            controller.OnDeviceChanging();
            var pausedByHost = host.Toggles == 1 && engine.IsPaused;
            var at = engine.SchedulerClockMs;
            Thread.Sleep(60);
            var held = engine.SchedulerClockMs == at;
            controller.OnDeviceReady(); var resumedAt = engine.SchedulerClockMs;
            Check("device change: the playing song pauses and resumes through the window's pause path, holding its position meanwhile",
                started && pausedByHost && held && resumedAt - at is >= 0 and < 10 && host.Toggles == 2 && engine.IsPlaying && !engine.IsPaused, $"started {started} paused {pausedByHost} held {held} toggles {host.Toggles} resumed {resumedAt - at:0.00} ms after the paused position");

            controller.OnDeviceChanging();
            controller.OnDeviceFailed("no device");
            controller.OnDeviceReady();
            Check("device change: when the device does not open the song stays shown as paused, and one toggle resumes it",
                host.Toggles == 3 && engine.IsPaused && host.Statuses.Contains("Audio output: no device"));
            host.TogglePlayback();
            var resumed = engine.IsPlaying && !engine.IsPaused;

            // Recording: stopped through the window's stop, not paused, not resumed.
            host.IsRecording = true;
            var togglesBefore = host.Toggles;
            controller.OnDeviceChanging(); controller.OnDeviceReady();
            Check("device change: a recording song is stopped through the window's stop and stays stopped",
                resumed && host.Stops == 1 && host.Toggles == togglesBefore && !engine.IsPlaying && host.Statuses.LastOrDefault() == "Recording stopped: the audio device changed");
        }
        finally { engine.Stop(); controller.Dispose(); }
    }

    private sealed class FakeTransport
    {
        public bool Playing, Paused;
        public int Position, Pauses, Resumes;
        public void Pause() { Paused = true; Pauses++; }
        public void Resume() { Paused = false; Resumes++; }
    }
}

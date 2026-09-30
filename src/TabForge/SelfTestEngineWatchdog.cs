using System.Diagnostics;
using System.IO;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using EA = TabForge.AudioEngine.Audio;
using EE = TabForge.AudioEngine.Editors;
using EH = TabForge.AudioEngine.EngineHost.Headless;
using EO = TabForge.AudioEngine.Output;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// Engine liveness and watchdog policy (audit WP-3: R-05, R-06, R-08, S-03, audit 2 D8 / D9) and recording quality (WP-10 / RT-09).
/// Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    /// <summary>R-06 policy: per-kind limits, slow is not hung, unattributed hangs (71) only after 30 s outside any plug-in call.</summary>
    private static void TestWatchdogPolicy()
    {
        EngineWatchdog.Verdict D(double busy, double? call, PluginCallKind kind, double since = double.MaxValue) => EngineWatchdog.Decide(busy, call, kind, since);
        var cases = new (EngineWatchdog.Verdict Got, EngineWatchdog.Verdict Want, string What)[]
        {
            (D(12, 12, PluginCallKind.Load), EngineWatchdog.Verdict.Slow, "load 12 s (the old watchdog killed at 10 s)"),
            (D(89, 89, PluginCallKind.SetState), EngineWatchdog.Verdict.Slow, "SetState 89 s"),
            (D(91, 91, PluginCallKind.Load), EngineWatchdog.Verdict.PluginHung, "load 91 s"),
            (D(29, 29, PluginCallKind.GetState), EngineWatchdog.Verdict.Slow, "GetState 29 s"),
            (D(31, 31, PluginCallKind.GetState), EngineWatchdog.Verdict.PluginHung, "GetState 31 s"),
            (D(21, 21, PluginCallKind.Editor), EngineWatchdog.Verdict.PluginHung, "editor 21 s"),
            (D(11, 11, PluginCallKind.Other), EngineWatchdog.Verdict.PluginHung, "other call 11 s"),
            (D(3, 3, PluginCallKind.Load), EngineWatchdog.Verdict.Fine, "load 3 s"),
            (D(31, null, PluginCallKind.Other), EngineWatchdog.Verdict.UnattributedHang, "31 s busy, no plug-in call"),
            (D(29, null, PluginCallKind.Other), EngineWatchdog.Verdict.Fine, "29 s busy, no plug-in call"),
            (D(120, null, PluginCallKind.Other, 2), EngineWatchdog.Verdict.Fine, "a 120 s chain load made of many attributed calls (last ended 2 s ago)"),
        };
        var wrong = cases.Where(c => c.Got != c.Want).Select(c => $"{c.What}: {c.Got} (want {c.Want})").ToList();
        Check("R-06: watchdog limits per call kind (load / SetState 90 s, GetState 30 s, editor 20 s, other 10 s); slow != hung; unattributed exit only after 30 s",
            wrong.Count == 0 && EngineWatchdog.ExitPluginHung == 70 && EngineWatchdog.ExitUnattributedHang == 71, string.Join("; ", wrong));

        // The main thread's breadcrumb is its own: the audio thread's per-block crumb cannot clear it, and a main call is the suspect.
        var block = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        try
        {
            block.EnterPlugin(3, 0, "audio-thread.dll");
            block.EnterMain(1, 2, "main-thread.dll", PluginCallKind.GetState);
            block.LeavePlugin();   // an audio block ends meanwhile
            var main = block.MainCall();
            var blamed = block.LastPluginCall();
            block.LeaveMain();
            var recent = block.MainCallRecent(5);
            Check("R-06: main-thread breadcrumb carries its kind, survives the audio thread's crumb, is what a crash blames, and is 'recent' after it ends",
                main is { Kind: PluginCallKind.GetState, Slot: 1, Index: 2, Path: "main-thread.dll" } && blamed?.Path == "main-thread.dll" && block.MainCall() is null && recent,
                $"main {main}, blamed {blamed?.Path}, recent {recent}");
        }
        finally { block.Dispose(); }

        // In the engine: an in-process load carries a Load breadcrumb; an isolated plug-in's load (a bounded pipe wait) carries none.
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        (int, int, string, PluginCallKind, long, bool)? seen = null;
        var observed = false;
        EH.Attach(shared, (spec, rate, max) => { seen = shared.MainCall(); observed = true; return EP.Vst2Plugin.TestEffect.Create(rate, max); });
        try
        {
            var spec = new PluginSpec(EP.Vst2Plugin.TestEffect.PathName, "VST2", false, true, 100, null, Id: "a");
            EH.Configure(NullConfig(48000, 256, manual: true));
            EH.LoadChain(0, "t", false, new List<PluginSpec> { spec });
            var inProcess = observed ? seen : null;
            observed = false; seen = null;
            EH.Configure(new EngineConfig(EO.AudioOutputFactory.Null, EO.AudioOutputFactory.NullManual, 48000, 256, SeparateProcessPerPlugin: true));
            EH.LoadChain(1, "t", false, new List<PluginSpec> { spec with { Id = "b" } });
            Check("R-06: in-process plug-in loads carry a blaming Load breadcrumb; isolated loads only a non-blaming busy marker (their own 15 s timeout applies)",
                inProcess is { Item4: PluginCallKind.Load, Item6: true } && observed && seen is { Item6: false } && shared.MainCall() is null,
                $"in-process {inProcess?.Item4}/{inProcess?.Item6}, isolated observed {observed} crumb {seen?.Item4}/{seen?.Item6}");
            shared.EnterMain(1, 0, "isolated.dll", PluginCallKind.GetState, blame: false);
            var notBlamed = shared.LastPluginCall() is null && shared.MainCallRecent(5);
            shared.LeaveMain();
            Check("R-06: a wait on an isolated plug-in is never blamed for an engine crash, but counts as busy (not deaf)", notBlamed);
        }
        finally { EH.Detach(); shared.Dispose(); }
    }

    /// <summary>S-03: a malformed or out-of-range command frame is dropped and logged; the reader keeps going.</summary>
    private static void TestCommandFrameRobustness()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException(spec.Path));
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: true));
            var truncated = EH.Command(EngineCommand.SetTrackMix, w => w.Write(1));                                      // slot only: EndOfStream
            var badArm = EH.Command(EngineCommand.SetArm, w => { w.Write(5000); w.Write(true); w.Write(0); w.Write(true); });
            var badRecord = EH.Command(EngineCommand.Record, w => { w.Write(true); w.WriteString("x"); w.Write(1); w.Write(-7); w.WriteString("t"); });
            var badClips = EH.Command(EngineCommand.SetClips, w => { w.Write(99999); w.Write(0); });
            var unknown = EH.Command((EngineCommand)200, w => w.Write(123));
            var good = EH.Command(EngineCommand.SetTrackMix, w => { w.Write(1); w.Write(100); w.Write(64); });
            EH.Collect();
            Check("S-03: truncated / out-of-range frames (SetTrackMix, SetArm slot 5000, Record slot -7, SetClips slot 99999) are dropped; the next frame is handled",
                !truncated && !badArm && !badRecord && !badClips && unknown && good && EH.ArmedCount == 0,
                $"truncated {truncated}, arm {badArm}, record {badRecord}, clips {badClips}, unknown {unknown}, good {good}, armed {EH.ArmedCount}");
        }
        finally { EH.Detach(); shared.Dispose(); }
    }

    /// <summary>Audit 2 D9: the shared message-loop turn runs the idle hook at its cadence and ends the loop when it says so.</summary>
    private static void TestPumpOnce()
    {
        int calls = 0; bool first = true, second = false, third = true; long elapsedMs = -1;
        var t = new Thread(() =>
        {
            long lastIdle = 0;
            var sw = Stopwatch.StartNew();
            first = EE.EditorWindows.PumpOnce(ref lastIdle, () => { calls++; return false; });   // due: idle runs, says stop
            second = EE.EditorWindows.PumpOnce(ref lastIdle, () => { calls++; return false; });  // not due yet: keeps going
            Thread.Sleep(40);
            third = EE.EditorWindows.PumpOnce(ref lastIdle, () => { calls++; return true; });
            elapsedMs = sw.ElapsedMilliseconds;
        }) { IsBackground = true, Name = "selftest pump" };
        t.Start();
        t.Join(5000);
        Check("D9: EditorWindows.PumpOnce runs the idle hook every ~33 ms and stops the loop when it returns false",
            !first && second && third && calls == 2 && elapsedMs is >= 0 and < 1000, $"first {first}, second {second}, third {third}, idle calls {calls}, {elapsedMs} ms");
    }

    /// <summary>Audit 2 D8: one plug-in factory; nothing else constructs VST2 / VST3 instances.</summary>
    private static void TestPluginFactorySingleSource()
    {
        var detection = EP.PluginFactory.IsVst3(@"C:\x\Synth.vst3", "") && EP.PluginFactory.IsVst3(@"C:\x\Synth.dll", "vst3")
            && !EP.PluginFactory.IsVst3(@"C:\x\Synth.dll", "VST2") && !EP.PluginFactory.IsVst3(@"C:\x\Synth.dll", "");
        var root = FindRepositoryRoot();
        if (root is null) { Check("D8: plug-in format detection (.vst3 or format VST3)", detection); Skip("D8: only PluginFactory constructs plug-ins", "no source checkout found", "source-hygiene"); return; }
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var name = Path.GetFileName(file);
            if (name is "PluginLoading.cs" or "Vst2TestEffect.cs" or "SelfTestEngineWatchdog.cs") continue;
            var text = File.ReadAllText(file);
            if (text.Contains("new Vst2Plugin(") || text.Contains("new Vst3Plugin(")) offenders.Add(name);
        }
        Check("D8: one PluginFactory.Create for the engine, the plug-in host and the scan probe (format detection shared)",
            detection && offenders.Count == 0, $"detection {detection}; constructed elsewhere: {string.Join(", ", offenders)}");
    }

    /// <summary>R-08: a child in the kill-on-close job ends when the job closes.</summary>
    private static void TestChildProcessJob()
    {
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        if (!File.Exists(ping)) { Skip("R-08: a child in the job ends when the job closes", "ping.exe not found"); return; }
        using var child = Process.Start(new ProcessStartInfo(ping, "-n 60 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        var job = new ChildProcessJob();
        var added = job.Add(child);
        var aliveBefore = !child.HasExited;
        job.Dispose();
        var ended = child.WaitForExit(5000);
        if (!ended) { try { child.Kill(); } catch (InvalidOperationException) { } }
        Check("R-08: a child in the kill-on-close Job Object ends as soon as the job closes (as when TabForge is killed)",
            job.IsActive == false && added && aliveBefore && ended, $"added {added}, alive before {aliveBefore}, ended {ended}");
    }

    private static bool PumpUntil(PumpedContext ui, Func<bool> done, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ui.Pump();
            if (done()) return true;
            Thread.Sleep(20);
        }
        ui.Pump();
        return done();
    }

    /// <summary>
    /// R-05 / R-06 / R-08 with a real engine process (null output): the start does not block; a plug-in 7 s slow in effOpen loads with
    /// a "still loading" report and nothing quarantined; a deaf engine (main thread asleep, no plug-in call) is restarted within 6 s,
    /// again quarantining nothing, and the new engine answers.
    /// </summary>
    private static void TestEngineLivenessAndSlowLoad()
    {
        Environment.SetEnvironmentVariable("TABFORGE_ENGINE_TEST_HOOKS", "1");
        Environment.SetEnvironmentVariable("TABFORGE_TEST_OPEN_DELAY_MS", "7000");
        var ui = new PumpedContext();
        var client = new AudioEngineClient();
        var quarantine = new List<string>();
        var slow = new List<(string Path, PluginCallKind Kind, int Seconds)>();
        var acks = new List<ChainAck>();
        var crashed = new List<string>();
        client.Quarantine = () => quarantine;
        client.PluginSlow += (p, k, s) => slow.Add((p, k, s));
        client.ChainAcknowledged += acks.Add;
        client.PluginCrashed += crashed.Add;
        var config = new EngineConfig(EO.AudioOutputFactory.Null, "", 48000, 256, false);
        try
        {
            var sw = Stopwatch.StartNew();
            var started = client.StartForTest(config, ui);
            var returnedMs = sw.ElapsedMilliseconds;
            var ready = PumpUntil(ui, () => client.Output is not null, 15000);
            Check("R-08: starting the engine returns at once (no 8 s wait on the UI thread); commands sent meanwhile arrive in order (Ready)",
                started && returnedMs < 1000 && ready, $"started {started}, returned after {returnedMs} ms, ready {ready} after {sw.ElapsedMilliseconds} ms");
            if (!ready) return;
            var pid = client.EngineProcessId;

            client.LoadChainForTest(0, new List<PluginSpec> { new(EP.Vst2Plugin.TestEffect.PathName, "VST2", false, true, 100, null, Id: "slow") });
            var loaded = PumpUntil(ui, () => acks.Any(a => a.Slot == 0), 20000);
            var status = acks.FirstOrDefault(a => a.Slot == 0)?.Plugins.FirstOrDefault()?.Status;
            Check("R-06: a plug-in 7 s slow in effOpen loads: 'still loading' is reported, nothing is quarantined, the engine is not restarted",
                loaded && status == PluginLoadStatus.Loaded && slow.Any(s => s.Kind == PluginCallKind.Load && s.Seconds >= 5) && quarantine.Count == 0 && crashed.Count == 0 && client.EngineProcessId == pid,
                $"loaded {loaded} ({status}), slow reports [{string.Join(", ", slow.Select(s => $"{s.Kind} {s.Seconds} s"))}], quarantined {quarantine.Count}, restarts {crashed.Count}");

            // The load just ended: give the liveness check a clean baseline (5 s), during which the engine must keep answering pings.
            var aliveBefore = client.LastAliveForTest;
            PumpUntil(ui, () => false, 5500);
            var answering = client.LastAliveForTest > aliveBefore && crashed.Count == 0 && client.EngineProcessId == pid;
            Check("R-05: a healthy engine keeps answering pings (no false restart)", answering, $"last alive advanced {client.LastAliveForTest > aliveBefore}, restarts {crashed.Count}");

            using var engine = pid is int id ? Process.GetProcessById(id) : null;
            client.SendTestHangForTest(60);
            sw.Restart();
            var died = engine?.WaitForExit(9000) ?? false;
            var diedMs = sw.ElapsedMilliseconds;
            var cleaned = PumpUntil(ui, () => crashed.Count == 1 && !client.IsRunning, 5000);
            Check("R-05: a deaf engine (main thread asleep 60 s, no plug-in call) is restarted within 6 s; nothing is quarantined",
                died && diedMs <= 6500 && cleaned && crashed.SequenceEqual(new[] { "" }) && quarantine.Count == 0,
                $"ended {died} after {diedMs} ms, cleaned {cleaned}, restart reports [{string.Join(", ", crashed.Select(c => c.Length == 0 ? "(none)" : c))}], quarantined {quarantine.Count}");

            var restarted = client.StartForTest(config, ui) && PumpUntil(ui, () => client.Output is not null, 15000);
            var alive0 = client.LastAliveForTest;
            var answers = restarted && PumpUntil(ui, () => client.LastAliveForTest > alive0, 4000);
            Check("R-05: the restarted engine answers pings", restarted && answers && client.EngineProcessId != pid, $"restarted {restarted}, answers {answers}");
        }
        finally
        {
            client.Dispose();
            ui.Pump();
            client.DisposeRetiredForTest();
            Environment.SetEnvironmentVariable("TABFORGE_ENGINE_TEST_HOOKS", null);
            Environment.SetEnvironmentVariable("TABFORGE_TEST_OPEN_DELAY_MS", null);
        }
    }

    /// <summary>RT-09: capture resampling is band-limited (windowed sinc, no linear-interpolation aliasing) and allocation-free; takes line up.</summary>
    private static void TestCaptureResampling()
    {
        const int inRate = 44100, outRate = 48000, seconds = 2;
        const double tone = 5000;
        var capture = new EA.InputCapture("selftest", outRate, 2, 10, sourceRate: inRate);
        var output = new List<float>(outRate * seconds * 2 + 8192);
        capture.BlockCaptured = (buffer, frames) => output.AddRange(buffer.AsSpan(0, frames * 2).ToArray());
        var input = new float[inRate * seconds * 2];
        for (var i = 0; i < inRate * seconds; i++) input[i * 2] = input[i * 2 + 1] = 0.5f * MathF.Sin((float)(2 * Math.PI * tone * i / inRate));
        long allocated = -1;
        var bare = new EA.InputCapture("selftest", outRate, 2, 10, sourceRate: inRate);   // no collector: measures the capture path alone
        var feeder = new Thread(() =>
        {
            for (var at = 0; at < inRate * seconds; at += 441) capture.Feed(input.AsSpan(at * 2, Math.Min(441, inRate * seconds - at) * 2));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var at = 0; at + 441 <= inRate; at += 441) bare.Feed(input.AsSpan(at * 2, 441 * 2));
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }) { IsBackground = true, Name = "selftest capture feeder" };
        feeder.Start();
        feeder.Join(10000);
        // Least-squares fit of a 5 kHz sine over the steady middle second: the residual is noise + aliasing.
        var frames = output.Count / 2;
        int from = frames / 4, to = frames * 3 / 4;
        double ss = 0, sc = 0, cc = 0, ys = 0, yc = 0;
        var w = 2 * Math.PI * tone / outRate;
        for (var n = from; n < to; n++) { var s = Math.Sin(w * n); var c = Math.Cos(w * n); var y = output[n * 2]; ss += s * s; sc += s * c; cc += c * c; ys += y * s; yc += y * c; }
        var det = ss * cc - sc * sc;
        var a = (ys * cc - yc * sc) / det; var b = (yc * ss - ys * sc) / det;
        double signal = 0, residual = 0;
        for (var n = from; n < to; n++) { var fit = a * Math.Sin(w * n) + b * Math.Cos(w * n); signal += fit * fit; var e = output[n * 2] - fit; residual += e * e; }
        var snrDb = 10 * Math.Log10(signal / Math.Max(residual, 1e-30));
        var expectedFrames = inRate * seconds * (double)outRate / inRate;
        Check("RT-09: 44.1 -> 48 kHz capture resampling is band-limited (5 kHz tone SNR >= 60 dB)",
            snrDb >= 60 && Math.Abs(frames - expectedFrames) < 600, $"SNR {snrDb:0.0} dB, frames {frames} (expected ~{expectedFrames:0})");
        Check("RT-09: the capture callback path allocates nothing (resampler preallocated in the constructor)", allocated == 0, $"{allocated} bytes");

        var same = new EA.InputCapture("selftest", outRate, 2, 10);
        var copy = new List<float>();
        same.BlockCaptured = (buffer, n) => copy.AddRange(buffer.AsSpan(0, n * 2).ToArray());
        var probe = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };
        same.Feed(probe);
        Check("RT-09: at the engine rate the input passes unchanged (no resampler)", copy.SequenceEqual(probe) && same.SourceRate == outRate, string.Join(",", copy));

        // Take alignment: song time minus output delay, device latency and the user's offset (positive = earlier).
        var t0 = EA.TakeAlignment.StartSec(10, 0.05, 12, 0);
        var t1 = EA.TakeAlignment.StartSec(10, 0.05, 12, 30);
        var t2 = EA.TakeAlignment.StartSec(10, 0.05, 12, -30);
        var clamp = EA.TakeAlignment.StartSec(0.01, 0, 0, 5000);
        Check("RT-09: take start = song time - output delay - input latency - recording offset (clamped, never negative)",
            Math.Abs(t0 - 9.938) < 1e-9 && Math.Abs(t1 - 9.908) < 1e-9 && Math.Abs(t2 - 9.968) < 1e-9 && clamp == 0, $"{t0} / {t1} / {t2} / {clamp}");
        var row = TabForge.Services.SettingsCatalog.Build(new TabForge.Services.AppSettings()).FirstOrDefault(d => d.Key == "vst.recordoffset");
        Check("RT-09: Settings > Audio & VST has 'Recording offset (ms)' (default 0)", row is not null && Convert.ToInt32(row.Get()) == 0, row?.Key ?? "missing");
    }
}

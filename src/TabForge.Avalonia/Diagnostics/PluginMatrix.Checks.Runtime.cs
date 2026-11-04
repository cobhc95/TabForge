using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;

namespace TabForge.Avalonia.Diagnostics;

// Owns: checks 6-10 of the Stage 10 plan (editor/preset, reconfigure, offline, helper kill).
// Does not own: checks 1-5 (PluginMatrix.Checks) or the process/screenshot harness.
internal static partial class PluginMatrix
{
    private static void CheckEditor(MatrixResult result, MatrixEntry entry, IPluginInstance plugin, MatrixOwner owner, string outDir, EngineThreads threads)
    {
        var check6 = new MatrixCheck { Number = 6, Name = CheckName(6) };
        var check7 = new MatrixCheck { Number = 7, Name = CheckName(7) };
        result.Checks.Add(check6);
        result.Checks.Add(check7);
        if (!plugin.HasEditor)
        {
            check6.Status = "skip";
            check6.Detail = "the plug-in reports no editor";
            check7.Status = "skip";
            check7.Detail = "the plug-in reports no editor";
            return;
        }

        var closed = new ManualResetEventSlim();
        if (plugin is RemotePlugin remote) remote.EditorClosed += closed.Set;
        var ownerHandle = OperatingSystem.IsWindows() ? owner.Handle : IntPtr.Zero;
        try
        {
            var opened = plugin is RemotePlugin remoteOpen
                ? remoteOpen.OpenEditor(ownerHandle, docked: false)
                : plugin.OpenEditor(ownerHandle);
            if (opened is not { } size || size.Width <= 0 || size.Height <= 0)
            {
                check6.Status = "fail";
                check6.Detail = "the editor did not open";
                check7.Status = "fail";
                check7.Detail = "the editor did not open";
                return;
            }
            File.WriteAllText(Path.Combine(outDir, "editor-open.txt"),
                $"open width={size.Width} height={size.Height} owner={(ownerHandle == IntPtr.Zero ? "0" : "window")} " +
                $"helper={(plugin is RemotePlugin helperPlugin ? helperPlugin.HostProcessId : 0)}");
            Console.Out.WriteLine($"matrix editor open width={size.Width} height={size.Height}");

            var before = plugin.GetState();
            var program = false;
            if (plugin is RemotePlugin programRemote) program = programRemote.SetProgram(1);
            else if (plugin is Vst2Plugin vst2 && vst2.Programs.Count > 1) { vst2.SetProgram(1); program = true; }
            var parameter = FirstWritable(plugin);
            var parameterMoved = parameter is not null && SetParameter(plugin, parameter.Id, parameter.Value > 0.5 ? 0.2 : 0.8);
            if (before is not null) plugin.SetState(before);
            // No Process while a native VST3 editor is open: this headless diagnostic does not run the
            // VST3 run loop, so a block could wait. Check 3/8/9 own the audio path.
            threads.RunPending();
            check6.Status = "pass";
            check6.Detail = $"program={program} state={before is not null} parameter={parameterMoved}";
            check6.Metrics["program"] = program;
            check6.Metrics["state"] = before is not null;
            check6.Metrics["parameter"] = parameterMoved;

            File.WriteAllText(Path.Combine(outDir, "editor-ready.txt"), "ready");
            if (!WaitForFile(Path.Combine(outDir, "editor-close.request"), 60000, plugin, threads))
            {
                check7.Status = "fail";
                check7.Detail = "the runner did not request the first screenshot/close";
                try { plugin.CloseEditor(); } catch (Exception) { /* swallow: best-effort editor close on timeout/failure path; check already records the failure. */ }
                return;
            }

            var wait = Stopwatch.StartNew();
            while (!closed.IsSet && wait.ElapsedMilliseconds < 5000)
            {
                try { plugin.EditorIdle(); }
                catch (Exception) { /* swallow: best-effort editor pump; idle failure must not abort the close wait, check reports the outcome. */ }
                threads.RunPending();
                PumpWindowsMessages();
                Thread.Sleep(20);
            }
            // Under Wine the runner closes the window through the WM, so EditorClosed is the proof. Only ask the
            // helper directly when no WM close arrived (native Windows, or a window the runner could not find).
            if (!closed.IsSet) plugin.CloseEditor();
            File.WriteAllText(Path.Combine(outDir, "editor-closed.txt"),
                $"editor-closed=yes close={(closed.IsSet ? "event" : "app")}");
            closed.Reset();

            // Under Wine the helper may still be finishing the close when the app asks to reopen. Retry for a few
            // seconds instead of failing on the first null: TDR/Valhalla editors take longer than the old immediate
            // call, and the previous run marked seven healthy editors "did not reopen" (editor-closed.txt close=app).
            (int Width, int Height)? again = null;
            var reopenWait = Stopwatch.StartNew();
            while (reopenWait.ElapsedMilliseconds < 8000)
            {
                again = plugin is RemotePlugin remoteAgain
                    ? remoteAgain.OpenEditor(ownerHandle, docked: false)
                    : plugin.OpenEditor(ownerHandle);
                if (again is { Width: > 0, Height: > 0 }) break;
                try { plugin.EditorIdle(); } catch (Exception) { /* swallow: best-effort editor pump; idle failure must not abort the close wait, check reports the outcome. */ }
                threads.RunPending();
                PumpWindowsMessages();
                Thread.Sleep(100);
            }
            if (again is not { } reopened || reopened.Width <= 0 || reopened.Height <= 0)
            {
                check7.Status = "fail";
                check7.Detail = "the editor did not reopen";
                return;
            }
            File.WriteAllText(Path.Combine(outDir, "editor-reopen.txt"),
                $"reopen width={reopened.Width} height={reopened.Height}");
            Console.Out.WriteLine($"matrix editor reopen width={reopened.Width} height={reopened.Height}");

            if (!WaitForFile(Path.Combine(outDir, "editor-close2.request"), 30000, plugin, threads))
            {
                check7.Status = "fail";
                check7.Detail = "the runner did not request the reopened screenshot/close";
                try { plugin.CloseEditor(); } catch (Exception) { /* swallow: best-effort editor close on timeout/failure path; check already records the failure. */ }
                return;
            }
            if (!closed.IsSet)
            {
                var secondWait = Stopwatch.StartNew();
                while (!closed.IsSet && secondWait.ElapsedMilliseconds < 3000)
                {
                    threads.RunPending();
                    PumpWindowsMessages();
                    Thread.Sleep(20);
                }
            }
            if (!closed.IsSet) plugin.CloseEditor();
            File.WriteAllText(Path.Combine(outDir, "editor-reopened-closed.txt"), "editor-closed=yes");
            check7.Status = "pass";
            check7.Detail = $"open={size.Width}x{size.Height} reopen={reopened.Width}x{reopened.Height} close={(closed.IsSet ? "event" : "app")}";
            check7.Metrics["open"] = $"{size.Width}x{size.Height}";
            check7.Metrics["reopen"] = $"{reopened.Width}x{reopened.Height}";
            check7.Metrics["close_event"] = closed.IsSet;
        }
        catch (Exception ex)
        {
            check6.Status = "fail";
            check6.Detail = ex.GetBaseException().Message;
            check7.Status = "fail";
            check7.Detail = ex.GetBaseException().Message;
            try { plugin.CloseEditor(); } catch (Exception) { /* swallow: best-effort editor close on timeout/failure path; check already records the failure. */ }
        }
    }

    private static void CheckReconfigure(MatrixEntry entry, IPluginInstance plugin, MatrixCheck check)
    {
        var instrument = entry.Role == "Instrument";
        var combos = new[] { (Rate: 44100, Frames: 128), (Rate: 48000, Frames: 256), (Rate: 96000, Frames: 1024) };
        var measured = new List<string>();
        foreach (var combo in combos)
        {
            try
            {
                plugin.Reconfigure(combo.Rate, combo.Frames);
                var render = Render(plugin, instrument, combo.Frames, blocks: 4, skip: 0, rate: combo.Rate);
                if (!render.Finite)
                {
                    check.Status = "fail";
                    check.Detail = $"non-finite output at {combo.Rate} Hz / {combo.Frames} frames";
                    return;
                }
                measured.Add($"{combo.Rate}/{combo.Frames}:rms={Rms(render.Samples):0.000000}");
            }
            catch (Exception ex)
            {
                check.Status = "fail";
                check.Detail = $"reconfigure to {combo.Rate} Hz / {combo.Frames} frames failed: {ex.GetBaseException().Message}";
                return;
            }
        }
        try { plugin.Reconfigure(Rate, Frames); } catch (Exception) { /* swallow: best-effort restore to default rate/buffer; must not fail a passing check. */ }
        check.Metrics["combos"] = measured;
        check.Status = "pass";
        check.Detail = string.Join("; ", measured);
    }

    private static void CheckOffline(MatrixEntry entry, PluginSpec spec, IPluginInstance plugin, EngineThreads threads, MatrixCheck check)
    {
        var instrument = entry.Role == "Instrument";
        var state = plugin.GetState();
        // Compare two fresh instances from the same saved state. Rendering the same advancing instance offline and then
        // realtime would charge a free-running LFO/oscillator phase to the offline flag; a fresh pair asks the question
        // the check is meant to ask (does offline mode change the rendering?) and leaves random/modulated plug-ins to
        // the repeat control below.
        var offline = RenderFresh(entry, spec, threads, instrument, offline: true, state);
        var realtime = RenderFresh(entry, spec, threads, instrument, offline: false, state);
        if (!offline.Finite || !realtime.Finite)
        {
            check.Status = "fail";
            check.Detail = "offline or realtime render produced a non-finite sample";
            return;
        }
        var diff = MaxDiff(offline.Samples, realtime.Samples);
        var rmsOffline = Rms(offline.Samples);
        var rmsRealtime = Rms(realtime.Samples);
        var relative = rmsRealtime <= 1e-9f ? Math.Abs(rmsOffline) : Math.Abs(rmsOffline - rmsRealtime) / rmsRealtime;
        check.Metrics["max_diff"] = Math.Round(diff, 8);
        check.Metrics["rms_offline"] = Math.Round(rmsOffline, 6);
        check.Metrics["rms_realtime"] = Math.Round(rmsRealtime, 6);
        check.Metrics["relative_rms"] = Math.Round(relative, 6);
        // Max diff alone is the tolerance. The old code also required a large relative RMS, which let a
        // free-running phase shift (same loudness, different waveform) through as a pass.
        if (diff > 0.02f)
        {
            var repeatA = RenderFresh(entry, spec, threads, instrument, offline: true, state);
            var repeatB = RenderFresh(entry, spec, threads, instrument, offline: true, state);
            var repeatDiff = MaxDiff(repeatA.Samples, repeatB.Samples);
            var repeatRmsA = Rms(repeatA.Samples);
            var repeatRmsB = Rms(repeatB.Samples);
            var repeatRelative = repeatRmsB <= 1e-9f ? Math.Abs(repeatRmsA) : Math.Abs(repeatRmsA - repeatRmsB) / repeatRmsB;
            check.Metrics["repeat_max_diff"] = Math.Round(repeatDiff, 8);
            check.Metrics["repeat_rms_a"] = Math.Round(repeatRmsA, 6);
            check.Metrics["repeat_rms_b"] = Math.Round(repeatRmsB, 6);
            check.Metrics["repeat_relative_rms"] = Math.Round(repeatRelative, 6);
            if (repeatDiff > 0.02f)
            {
                check.Status = "nondeterministic";
                check.Detail = $"offline and realtime differ (maxDiff {diff:0.000000}), and two fresh-instance offline renders also differ (maxDiff {repeatDiff:0.000000}); free-running modulation or randomness, not an offline-mode difference";
                return;
            }
            check.Status = "fail";
            check.Detail = $"offline and realtime differ (maxDiff {diff:0.000000}, relative RMS {relative:0.0000}); identical fresh offline renders differ by only {repeatDiff:0.000000}, so this is an offline-mode difference";
            return;
        }
        check.Status = "pass";
        check.Detail = $"maxDiff={diff:0.000000} relativeRMS={relative:0.0000}";
    }

    private static (float[] Samples, bool Finite, float Peak) RenderFresh(MatrixEntry entry, PluginSpec spec,
        EngineThreads threads, bool instrument, bool offline, byte[]? state)
    {
        using var created = CreateInstance(entry, spec, threads).Plugin;
        return Render(created, instrument, Frames, blocks: 8, skip: 2, offline: offline, state: state);
    }

    private static void CheckKill(MatrixEntry entry, IPluginInstance plugin, EngineThreads threads, MatrixCheck check)
    {
        if (plugin is not RemotePlugin remote)
        {
            check.Status = "skip";
            check.Detail = "the in-process route has no helper to kill";
            return;
        }
        var pid = remote.HostProcessId;
        var crashed = new ManualResetEventSlim();
        remote.Crashed += _ => crashed.Set();
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(true);
        }
        catch (Exception ex)
        {
            check.Status = "fail";
            check.Detail = $"could not kill helper {pid}: {ex.GetBaseException().Message}";
            return;
        }
        var wait = Stopwatch.StartNew();
        while (!crashed.IsSet && wait.ElapsedMilliseconds < 5000)
        {
            threads.RunPending();
            Thread.Sleep(20);
        }
        if (!crashed.IsSet)
        {
            check.Status = "fail";
            check.Detail = "the engine did not report the killed helper";
            return;
        }
        var input = Enumerable.Repeat(0.3f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };
        remote.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Transport(0, Frames));
        var expected = entry.Role == "Instrument" ? 0f : 0.3f;
        var passed = output[0].All(s => Math.Abs(s - expected) <= 1e-5f) && output[1].All(s => Math.Abs(s - expected) <= 1e-5f);
        check.Metrics["pid"] = pid;
        check.Metrics["engine_alive"] = true;
        check.Status = passed ? "pass" : "fail";
        check.Detail = passed
            ? $"helper {pid} killed; engine alive; chain {(entry.Role == "Instrument" ? "silent" : "passed dry")}"
            : $"helper {pid} killed; engine alive but the chain did not behave (expected {expected:0.###})";
    }

}

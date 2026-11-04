using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Plugins;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Diagnostics;

// Owns: checks 1-10 of the Stage 10 plan for one already-extracted plug-in. Called only by PluginMatrix.RunAsync.
// Does not own: process isolation (RemotePlugin), the helper, the editor window or screenshots (the runner).
internal static partial class PluginMatrix
{
    private static async Task CheckApproveAsync(MatrixEntry entry, PluginSpec spec, MatrixCheck check)
    {
        var settings = new PluginSettings();
        var trust = new PluginTrust();
        IPluginFileTrust files = new PortablePluginFileTrust();
        var info = new VstPluginInfo(entry.Name, spec.Path, entry.Format, "", entry.Role);
        var steps = new List<PluginApprovalStep>();
        var done = await PluginApprovalPresenter.RunAsync(new[] { info }, settings, trust, files, steps.Add, CancellationToken.None);
        var step = steps.FirstOrDefault();
        check.Metrics["done"] = done;
        if (step is null || !step.Identified)
        {
            check.Status = "refused";
            check.Detail = step?.Refusal ?? "the probe did not identify the plug-in";
            return;
        }
        check.Metrics["role"] = step.Role;
        check.Metrics["vendor"] = step.Vendor;
        if (!string.Equals(step.Role, entry.Role, StringComparison.OrdinalIgnoreCase))
        {
            check.Status = "fail";
            check.Detail = $"identified role {step.Role} does not match the manifest role {entry.Role}";
            return;
        }
        check.Status = "pass";
        check.Detail = $"role={step.Role} vendor={step.Vendor}";
    }

    private static MatrixCheck CheckReload(MatrixEntry entry, PluginSpec spec, EngineThreads threads)
    {
        var check = new MatrixCheck { Number = 2, Name = CheckName(2) };
        var loads = new List<long>();
        var errors = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            try
            {
                var watch = Stopwatch.StartNew();
                var created = CreateInstance(entry, spec, threads);
                using (created.Plugin)
                {
                    var input = new[] { new float[Frames], new float[Frames] };
                    var output = new[] { new float[Frames], new float[Frames] };
                    created.Plugin.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Transport(0, Frames));
                }
                loads.Add(watch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                errors.Add(ex.GetBaseException().Message);
            }
        }
        check.Metrics["loads_ms"] = loads;
        if (loads.Count == 5)
        {
            check.Status = "pass";
            check.Detail = $"5/5 loaded; ms={string.Join(",", loads)}";
        }
        else
        {
            check.Status = "fail";
            check.Detail = $"{loads.Count}/5 loaded" + (errors.Count > 0 ? ": " + string.Join(" | ", errors.Take(2)) : "");
        }
        return check;
    }

    private static void CheckAudio(MatrixEntry entry, IPluginInstance plugin, MatrixCheck check)
    {
        var instrument = entry.Role == "Instrument";
        var wet = Render(plugin, instrument, Frames, blocks: 16, skip: 2);
        var rms = Rms(wet.Samples);
        check.Metrics["rms"] = Math.Round(rms, 6);
        check.Metrics["peak"] = Math.Round(wet.Peak, 6);
        check.Metrics["finite"] = wet.Finite;
        if (!wet.Finite)
        {
            check.Status = "fail";
            check.Detail = "the plug-in produced a non-finite sample";
            return;
        }
        if (instrument && rms <= 1e-5f)
        {
            var moved = TryMoveParameter(plugin);
            check.Metrics["parameter_tweak"] = moved;
            if (moved is null)
            {
                check.Status = "fail";
                check.Detail = $"an instrument produced no sound from MIDI (RMS {rms:0.000000}) and no parameter moved it";
                return;
            }
        }
        if (!instrument && rms <= 1e-9f)
        {
            var moved = TryMoveParameter(plugin);
            check.Metrics["parameter_tweak"] = moved;
            if (moved is null)
            {
                check.Status = "fail";
                check.Detail = "an effect is silent at defaults and no parameter moved its output";
                return;
            }
        }

        var silent = Render(plugin, instrument, Frames, blocks: 4, skip: 0, silence: true);
        check.Metrics["silence_rms"] = Math.Round(Rms(silent.Samples), 6);
        if (!silent.Finite)
        {
            check.Status = "fail";
            check.Detail = "a silent input produced a non-finite sample";
            return;
        }
        if (!instrument) CheckBypass(plugin, check);
        if (check.Status != "fail")
        {
            check.Status = "pass";
            check.Detail = $"{(instrument ? "instrument" : "effect")} rms={rms:0.000000} peak={wet.Peak:0.000000}";
        }
    }

    private static string? TryMoveParameter(IPluginInstance plugin)
    {
        var parameter = FirstWritable(plugin);
        if (parameter is null) return null;
        foreach (var target in new[] { 1.0, 0.0, 0.5 }.Where(t => Math.Abs(t - parameter.Value) > 0.05))
        {
            if (!SetParameter(plugin, parameter.Id, target)) continue;
            var render = Render(plugin, plugin.IsInstrument, Frames, blocks: 8, skip: 2);
            var moved = Rms(render.Samples) > 1e-5f;
            SetParameter(plugin, parameter.Id, parameter.Value);
            if (moved) return $"{parameter.Name}={target:0.##}";
        }
        return null;
    }

    private static void CheckBypass(IPluginInstance plugin, MatrixCheck check)
    {
        using var shared = SharedBlock.Create("tf-matrix-" + Guid.NewGuid().ToString("N"));
        var chain = new TrackChain(0, -1, null, new[] { new TrackChain.Effect(plugin, 100, 0, "Stereo", 1, false) }, Frames);
        chain.Kept.Add(plugin);
        try
        {
            chain.SetMix(127, 64);
            var inputL = new float[Frames];
            var inputR = new float[Frames];
            var phase = 0.0;
            var rng = 0x12345678u;
            FillTestSignal(inputL, inputR, ref phase, ref rng);
            var mixL = new float[Frames];
            var mixR = new float[Frames];
            plugin.SetOfflineMode(true);
            chain.Render(mixL, mixR, 0, Frames, Transport(0, Frames), shared, null, null, inputL, inputR);
            chain.SetPluginBypass(0, true);
            Array.Clear(mixL);
            Array.Clear(mixR);
            chain.Render(mixL, mixR, 0, Frames, Transport(0, Frames), shared, null, null, inputL, inputR);
            var diff = Math.Max(MaxDiff(mixL, inputL), MaxDiff(mixR, inputR));
            check.Metrics["bypass_max_diff"] = Math.Round(diff, 8);
            // A few plug-ins keep a tiny in-place state/rounding residue in the dry path; 0.5 % of the test peak
            // is the same "pass-through" tolerance the audio matrix uses for a bypassed stage.
            if (diff > 0.005f)
            {
                check.Status = "fail";
                check.Detail = $"bypassed output differs from dry (maxDiff {diff:0.000000})";
            }
            else check.Metrics["bypass"] = "dry";
        }
        finally
        {
            try { plugin.SetOfflineMode(false); } catch (Exception) { /* swallow: best-effort offline-mode restore; must not mask the check verdict. */ }
            chain.Dispose();
        }
    }

    private static void CheckState(MatrixEntry entry, PluginSpec spec, IPluginInstance plugin, EngineThreads threads, MatrixCheck check)
    {
        var state = plugin.GetState();
        if (state is null)
        {
            check.Status = "skip";
            check.Detail = "the plug-in reports no state";
            return;
        }
        var parameter = FindSettable(plugin);
        if (parameter is null)
        {
            plugin.SetState(state);
            check.Status = "pass";
            check.Detail = "state round-trip; no settable parameter to compare";
            return;
        }
        var original = parameter.Value;
        var target = original > 0.5 ? 0.2 : 0.8;
        if (!SetParameter(plugin, parameter.Id, target))
        {
            check.Status = "fail";
            check.Detail = $"could not move parameter {parameter.Name} before the state restore";
            return;
        }
        plugin.SetState(state);
        Render(plugin, plugin.IsInstrument, Frames, blocks: 1, skip: 0);
        var restored = ReadParameter(plugin, parameter.Id);
        check.Metrics["parameter"] = parameter.Name;
        check.Metrics["original"] = Math.Round(original, 4);
        check.Metrics["restored"] = Math.Round(restored, 4);
        if (!double.IsFinite(restored) || Math.Abs(restored - original) > 0.05)
        {
            var control = TryRestoreParameter(entry, spec, threads, state, parameter.Id, check, "restored_control");
            if (control is { } repeat && double.IsFinite(restored) && Math.Abs(repeat - restored) > 0.05)
            {
                if (Math.Abs(repeat - original) <= 0.05)
                {
                    // The state itself is good: a fresh instance restores the original value. Only the restore
                    // into the live instance differs, so this is a deterministic restore failure, not randomness.
                    check.Status = "fail";
                    check.Detail = $"state restore left {parameter.Name} at {restored:0.###}, expected {original:0.###}; " +
                                   $"a fresh instance from the same state reads {repeat:0.###}, so the live-instance restore failed";
                    return;
                }
                check.Status = "nondeterministic";
                check.Detail = $"two restores of {parameter.Name} disagree ({restored:0.###} vs {repeat:0.###}) and neither matches the original {original:0.###}; the plug-in does not restore this value reproducibly";
                return;
            }
            check.Status = "fail";
            check.Detail = $"state restore left {parameter.Name} at {restored:0.###}, expected {original:0.###}";
            return;
        }
        try
        {
            using var reopened = CreateInstance(entry, spec, threads).Plugin;
            reopened.SetState(state);
            Render(reopened, reopened.IsInstrument, Frames, blocks: 1, skip: 0);
            var again = ReadParameter(reopened, parameter.Id);
            check.Metrics["reopened"] = Math.Round(again, 4);
            if (!double.IsFinite(again) || Math.Abs(again - original) > 0.05)
            {
                var control = TryRestoreParameter(entry, spec, threads, state, parameter.Id, check, "reopened_control");
                if (control is { } repeat && double.IsFinite(again) && Math.Abs(repeat - again) > 0.05)
                {
                    if (Math.Abs(repeat - original) <= 0.05)
                    {
                        check.Status = "fail";
                        check.Detail = $"a reopened instance left {parameter.Name} at {again:0.###}, expected {original:0.###}; " +
                                       $"a fresh instance from the same state reads {repeat:0.###}, so the reopen restore failed";
                        return;
                    }
                    check.Status = "nondeterministic";
                    check.Detail = $"two reopened instances restored {parameter.Name} differently ({again:0.###} vs {repeat:0.###}) and neither matches the original {original:0.###}; the plug-in does not restore this value reproducibly";
                    return;
                }
                check.Status = "fail";
                check.Detail = $"a reopened instance left {parameter.Name} at {again:0.###}, expected {original:0.###}";
                return;
            }
        }
        catch (Exception ex)
        {
            check.Status = "fail";
            check.Detail = "project reopen failed: " + ex.GetBaseException().Message;
            return;
        }
        check.Status = "pass";
        check.Detail = $"{parameter.Name}: {original:0.###} -> restored {restored:0.###}, reopened {check.Metrics["reopened"]}";
    }

    private static double? TryRestoreParameter(MatrixEntry entry, PluginSpec spec, EngineThreads threads, byte[] state,
        uint parameterId, MatrixCheck check, string metric)
    {
        try
        {
            var value = RestoreParameter(entry, spec, threads, state, parameterId);
            check.Metrics[metric] = Math.Round(value, 4);
            return value;
        }
        catch (Exception ex)
        {
            check.Metrics[metric + "_error"] = ex.GetBaseException().Message;
            return null;
        }
    }

    private static double RestoreParameter(MatrixEntry entry, PluginSpec spec, EngineThreads threads, byte[] state, uint parameterId)
    {
        using var created = CreateInstance(entry, spec, threads).Plugin;
        created.SetState(state);
        Render(created, created.IsInstrument, Frames, blocks: 1, skip: 0);
        return ReadParameter(created, parameterId);
    }

    private static void CheckParameters(MatrixEntry entry, IPluginInstance plugin, MatrixCheck check)
    {
        var list = Parameters(plugin);
        check.Metrics["count"] = list.Count;
        if (list.Count == 0)
        {
            check.Status = "skip";
            check.Metrics["coverage"] = "no parameters";
            check.Detail = "the plug-in lists no parameters";
            return;
        }
        var parameter = FindSettable(plugin);
        if (parameter is null)
        {
            // Report a coverage gap, not a pass: several thousand-parameter instruments expose their first
            // eight writable parameters in a form whose setter refuses a value, and check 5 is then unverified.
            var writable = list.Count(p => !p.ReadOnly && p.Max > p.Min && double.IsFinite(p.Value));
            var tried = Math.Min(writable, 8);
            check.Metrics["writable"] = writable;
            check.Metrics["set_attempts"] = tried;
            check.Metrics["coverage"] = "skipped";
            check.Status = "skip";
            check.Detail = writable == 0
                ? $"{list.Count} parameters, none writable; set and read-back were not verified"
                : $"{list.Count} parameters, {writable} writable; none of the first {tried} accepted a set, so set and read-back were not verified";
            return;
        }
        var original = parameter.Value;
        var read = new List<double>();
        var (tolerance, rule, legalStep) = ParameterTolerance(entry.Format, parameter);
        check.Metrics["tolerance"] = tolerance;
        check.Metrics["tolerance_rule"] = rule;
        var mismatched = false;
        foreach (var target in new[] { 0.25, 0.75 })
        {
            if (!SetParameter(plugin, parameter.Id, target))
            {
                check.Status = "fail";
                check.Detail = $"could not set {parameter.Name}";
                return;
            }
            var value = ReadParameter(plugin, parameter.Id);
            read.Add(Math.Round(value, 4));
            if (!double.IsFinite(value) || Math.Abs(value - target) > tolerance || !legalStep(value)) mismatched = true;
        }
        if (mismatched)
        {
            // VST2 has no general stepped flag. Accept a stable quantized response only when both
            // targets are finite, in range, stable on a re-read and actually different; a stuck or
            // broken parameter still fails. VST3 has an explicit stepped flag and the step-aware
            // tolerance above, so it must fail rather than use this loose pair check.
            if (string.Equals(entry.Format, "VST2", StringComparison.OrdinalIgnoreCase))
            {
                var a = read[0];
                var b = read[1];
                var inRange = a >= parameter.Min - 1e-6 && a <= parameter.Max + 1e-6 &&
                              b >= parameter.Min - 1e-6 && b <= parameter.Max + 1e-6;
                var stable = SetParameter(plugin, parameter.Id, 0.25) &&
                             Math.Abs(ReadParameter(plugin, parameter.Id) - a) <= 0.001;
                SetParameter(plugin, parameter.Id, original);
                if (inRange && stable && double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) > 0.05)
                {
                    check.Metrics["parameter"] = parameter.Name;
                    check.Metrics["read_back"] = read;
                    check.Metrics["quantized"] = true;
                    check.Status = "pass";
                    check.Detail = $"{list.Count} parameters; {parameter.Name} quantizes 0.25/0.75 to {a}/{b} (stable; no VST2 stepped flag)";
                    return;
                }
            }
            check.Status = "fail";
            check.Detail = $"{parameter.Name} set to 0.25/0.75 read back {read[0]}/{read[1]} (allowed by {rule})";
            return;
        }
        SetParameter(plugin, parameter.Id, original);
        check.Metrics["parameter"] = parameter.Name;
        check.Metrics["read_back"] = read;
        check.Status = "pass";
        check.Detail = $"{list.Count} parameters; {parameter.Name} 0.25/0.75 read back {read[0]}/{read[1]} (allowed by {rule})";
    }

    /// <summary>
    /// Check-5 tolerance. A continuous parameter keeps the normalised 0.1; a VST3 stepped parameter is
    /// exposed as 0..steps in whole steps, so one step is 1 and half a step (0.5) is the nearest-step limit.
    /// Only legal whole steps are accepted. A stepped parameter from another format keeps the old guard.
    /// </summary>
    private static (double Tolerance, string Rule, Func<double, bool> LegalStep) ParameterTolerance(string format, PluginParameterInfo parameter)
    {
        if (!parameter.Stepped) return (0.1, "continuous 0.1", _ => true);
        if (string.Equals(format, "VST3", StringComparison.OrdinalIgnoreCase) && parameter.Max > parameter.Min && parameter.Max > 0)
        {
            var step = (parameter.Max - parameter.Min) / parameter.Max;
            return (step / 2, $"VST3 half-step {step / 2:0.###} ({parameter.Max:0} steps)", value => Math.Abs(value - Math.Round(value)) <= 1e-6);
        }
        return (0.26, "stepped guard 0.26", _ => true);
    }

    private static (float[] Samples, bool Finite, float Peak) Render(IPluginInstance plugin, bool instrument, int frames, int blocks, int skip,
        int rate = Rate, bool offline = false, byte[]? state = null, bool silence = false)
    {
        if (state is not null) plugin.SetState(state);
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var collected = new List<float>();
        var finite = true;
        var peak = 0f;
        var phase = 0.0;
        var rng = 0x12345678u;
        try
        {
            plugin.SetOfflineMode(offline);
            for (var block = 0; block < blocks; block++)
            {
                if (instrument || silence)
                {
                    Array.Clear(input[0]);
                    Array.Clear(input[1]);
                }
                else FillTestSignal(input[0], input[1], ref phase, ref rng);
                BlockMidi[] midiArray = instrument && block == 0
                    ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
                    : instrument && block == 8
                        ? new[] { new BlockMidi { Frame = 0, Status = 0x80, Data1 = 60, Data2 = 0 } }
                        : Array.Empty<BlockMidi>();
                plugin.Process(input, output, frames, midiArray, Transport(block, frames, rate));
                finite &= AllFinite(output[0], output[1]);
                if (block < skip) continue;
                foreach (var sample in output[0]) { collected.Add(sample); peak = Math.Max(peak, Math.Abs(sample)); }
                foreach (var sample in output[1]) { collected.Add(sample); peak = Math.Max(peak, Math.Abs(sample)); }
            }
        }
        finally
        {
            try { plugin.SetOfflineMode(false); } catch (Exception) { /* swallow: best-effort offline-mode restore; must not mask the check verdict. */ }
        }
        return (collected.ToArray(), finite, peak);
    }

    private static IReadOnlyList<PluginParameterInfo> Parameters(IPluginInstance plugin) =>
        (plugin as IPluginParameters)?.GetParameters() ?? Array.Empty<PluginParameterInfo>();

    private static PluginParameterInfo? FirstWritable(IPluginInstance plugin) =>
        Parameters(plugin).FirstOrDefault(p => !p.ReadOnly && p.Max > p.Min && double.IsFinite(p.Value));

    /// <summary>The first writable parameter whose setter really accepts a value; the probe value is restored.</summary>
    private static PluginParameterInfo? FindSettable(IPluginInstance plugin)
    {
        foreach (var parameter in Parameters(plugin).Where(p => !p.ReadOnly && p.Max > p.Min && double.IsFinite(p.Value)).Take(8))
        {
            var original = parameter.Value;
            var target = original > 0.5 ? 0.2 : 0.8;
            if (!SetParameter(plugin, parameter.Id, target)) continue;
            SetParameter(plugin, parameter.Id, original);
            return parameter;
        }
        return null;
    }

    private static bool SetParameter(IPluginInstance plugin, uint id, double value) =>
        (plugin as IPluginParameters)?.SetParameter(id, value) ?? false;

    private static double ReadParameter(IPluginInstance plugin, uint id) =>
        Parameters(plugin).FirstOrDefault(p => p.Id == id)?.Value ?? double.NaN;
}

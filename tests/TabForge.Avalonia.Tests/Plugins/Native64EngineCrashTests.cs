using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Windows proof that a fault in a 64-bit VST2 running in the native in-process route (no helper) ends only the
///     audio-engine process: the UI process survives, the plug-in is named and quarantined, the notice is readable, and the
///     next sync starts a fresh engine without that plug-in.
/// Does not own: the helper routes (Vst32BridgeCrashTests), the native 64-bit loader itself (Vst32Native64Tests), or the
///     matrix crash classification.
/// Tests: this class.
/// </summary>
[Collection("EngineAudible")]
public sealed class Native64EngineCrashTests
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private const int Frames = 256;

    private readonly ITestOutputHelper _output;
    public Native64EngineCrashTests(ITestOutputHelper output) => _output = output;

    /// <summary>The path of a real Synth1 64-bit build, for the second-note-on scenario; only the owner PC has it.</summary>
    private static string? Synth1Path
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("TABFORGE_SYNTH1_VST64");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            return null;
        }
    }

    [Native64Fact]
    public void AProcessFaultInTheNative64RouteKillsOnlyTheEngineAndTheNextSyncRestartsIt()
    {
        var folder = Directory.CreateTempSubdirectory("tf-native64-crash").FullName;
        try
        {
            var dll = Path.Combine(folder, "passthrough64.dll");
            File.Copy(Vst32TestAssets.TestPluginPath64!, dll);
            File.WriteAllText(Path.Combine(folder, "passthrough32.faultonce"), "1");

            var result = RunCrash(dll, instrument: false);
            Assert.Equal(dll, result.CrashedPath);
            Assert.False(result.CrashedInHelper);
            Assert.False(result.EngineKeptRunning);
            Assert.Null(result.ReportPath);
            Assert.Contains(dll, result.Quarantine, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(1, result.EngineCrashes);
            Assert.Contains("stopped working and was switched off; playback continues", result.Notice);
            Assert.Contains("passthrough64", result.Notice);
            Assert.True(result.EnginePidExited, "the engine process must have died");
            Assert.False(result.TooManyCrashes);
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Regression for Stage 13 C7: once a native 64-bit VST2 is marked crash-prone, the next load uses the existing
    /// isolation host. Its fault loses only that plug-in; the audio engine and the UI stay up and the notice is readable.
    /// (The first fault still ends the engine: the in-process route has no SEH. The flag makes the repeat safe.)
    /// </summary>
    [Native64Fact]
    public void ACrashProneNative64PluginIsIsolatedAndDoesNotEndTheEngine()
    {
        var folder = Directory.CreateTempSubdirectory("tf-native64-isolated").FullName;
        try
        {
            var dll = Path.Combine(folder, "passthrough64.dll");
            File.Copy(Vst32TestAssets.TestPluginPath64!, dll);
            File.WriteAllText(Path.Combine(folder, "passthrough32.faultonce"), "1");

            var result = RunCrash(dll, instrument: false, crashProne: true);
            Assert.Equal(dll, result.CrashedPath);
            Assert.False(result.CrashedInHelper);
            Assert.True(result.EngineKeptRunning, "the isolated host must contain the fault");
            Assert.False(result.EnginePidExited, "the engine process must stay alive");
            Assert.Contains(dll, result.Quarantine, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(1, result.EngineCrashes);
            Assert.Contains("stopped working and was switched off; playback continues", result.Notice);
            Assert.Contains("passthrough64", result.Notice);
            Assert.False(result.TooManyCrashes);
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    [RealSynth1Fact]
    public void Synth1SixtyFourSecondNoteOnKillsOnlyTheEngineAndTheRestartedEngineSkipsIt()
    {
        var result = RunCrash(Synth1Path!, instrument: true);
        Assert.Equal(Synth1Path, result.CrashedPath);
        Assert.False(result.CrashedInHelper);
        Assert.False(result.EngineKeptRunning);
        Assert.Contains(Synth1Path!, result.Quarantine, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, result.EngineCrashes);
        Assert.Contains("stopped working and was switched off; playback continues", result.Notice);
        Assert.Contains("Synth1 VST64", result.Notice);
        Assert.True(result.EnginePidExited, "the engine process must have died");
        Assert.False(result.TooManyCrashes);
        Assert.True(result.PluginMarkedUnavailable, "the restarted sync must mark the crashed plug-in unavailable");
    }

    private CrashResult RunCrash(string pluginPath, bool instrument, bool crashProne = false)
    {
        var project = new SongProject();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Guitar);
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new PluginSlot
        {
            Name = Path.GetFileNameWithoutExtension(pluginPath),
            Path = pluginPath,
            Format = PluginFormats.Vst2,
            Type = instrument ? PluginSlotType.Instrument : PluginSlotType.Effect,
        });
        project.Tracks.Add(track);
        var settings = new PluginSettings { Driver = "Null", Device = "", SampleRate = 48000, BufferSize = Frames };
        if (crashProne) settings.CrashProne.Add(pluginPath);
        var trust = new PluginTrust();
        trust.Approve(settings, pluginPath, new Vst32TestAssets.NoFiles());
        var quarantine = new List<string>();
        using var client = new AudioEngineClient(trust)
        {
            EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe"),
            WarmIdle = TimeSpan.Zero,
        };
        client.Quarantine = () => quarantine;
        var crashed = new ManualResetEventSlim();
        string? crashedPath = null;
        var inHelper = false;
        var engineKeptRunning = false;
        string? reportPath = null;
        var crashCount = 0;
        client.PluginCrashed += path =>
        {
            Interlocked.Increment(ref crashCount);
            crashedPath = path;
            inHelper = client.PluginCrashedInHelper;
            engineKeptRunning = client.CrashLeftEngineRunning;
            reportPath = client.PluginCrashReportPath;
            crashed.Set();
        };
        var routing = new RoutedMidiOutput(new NullMidiOutput(), client);
        var owner = new object();
        var uiPid = Environment.ProcessId;

        AvaloniaEngineRouting.Apply(project, routing, client, settings, AvaloniaEngineRouting.FullMasterPercent, owner);
        Assert.True(client.IsRunning, "the engine did not start");
        var enginePid = client.EngineProcessId;
        Assert.NotNull(enginePid);

        if (instrument)
        {
            WaitUntil(() => client.Output is not null && ChainsReady(client), "the engine chain did not get ready");
            var slot = client.SlotOf(track);
            Assert.True(slot >= 0, "the plug-in track has no engine slot");
            Send(client, slot, 0x90, 60, 100);
            Thread.Sleep(250);
            Send(client, slot, 0x80, 60, 0);
            Thread.Sleep(100);
            Assert.True(client.IsRunning, "the first note-on killed the engine; Synth1 64 should survive until the second");
            Send(client, slot, 0x90, 64, 100);   // the second note-on is what faults Synth1 64
        }

        Assert.True(crashed.Wait(TimeSpan.FromSeconds(20)), "the engine did not report the plug-in crash");
        if (crashProne) Assert.True(client.IsRunning, "the isolated host must leave the engine running");
        else WaitUntil(() => !client.IsRunning, "the client still thinks the dead engine is running");
        Assert.Equal(uiPid, Environment.ProcessId);   // the UI process was never the engine process

        var engineGone = enginePid is int pid && Vst32TestAssets.WaitsForExit(pid, 5000);
        var notice = AvaloniaEngineEvents.CrashNotice(crashedPath ?? "", client.TooManyCrashes,
            client.PluginCrashedInHelper, client.PluginCrashReportPath);

        if (!crashProne)
        {
            // The production path: after a crash the window syncs the active song again, which starts a fresh engine.
            AvaloniaEngineRouting.Apply(project, routing, client, settings, AvaloniaEngineRouting.FullMasterPercent, owner);
            WaitUntil(() => client.IsRunning && client.Output is not null, "the engine did not restart");
            Thread.Sleep(500);   // give a reload of the quarantined plug-in time to crash again
            Assert.True(client.IsRunning, "the restarted engine did not stay up");
            Assert.Equal(1, Volatile.Read(ref crashCount));   // the quarantined plug-in did not crash the new engine
        }

        _output.WriteLine($"crashed {crashedPath}; helper={inHelper}; engine kept={engineKeptRunning}; " +
                          $"engine pid exited={engineGone}; notice={notice}");
        return new CrashResult(crashedPath ?? "", inHelper, engineKeptRunning, reportPath, quarantine.ToList(),
            engineGone, notice, client.TooManyCrashes, track.Rig.Plugins[0].Unavailable, Volatile.Read(ref crashCount));
    }

    private static void Send(AudioEngineClient client, int slot, byte status, byte data1, byte data2) =>
        Assert.True(client.Write(new TimedMidi
        {
            Timestamp = Stopwatch.GetTimestamp(),
            Slot = slot,
            Status = status,
            Data1 = data1,
            Data2 = data2,
        }), "the MIDI ring refused the note");

    private static bool ChainsReady(AudioEngineClient client)
    {
        var readiness = new ChainReadiness(client.RequestedChains);
        foreach (var ack in client.LastAcknowledgements) readiness.Acknowledge(ack);
        return readiness.IsReady;
    }

    private static void WaitUntil(Func<bool> ready, string failure)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(clock.Elapsed < ReadyTimeout, failure);
            Thread.Sleep(20);
        }
    }

    private sealed record CrashResult(string CrashedPath, bool CrashedInHelper, bool EngineKeptRunning,
        string? ReportPath, List<string> Quarantine, bool EnginePidExited, string Notice, bool TooManyCrashes,
        bool PluginMarkedUnavailable, int EngineCrashes);
}

/// <summary>A test that needs a real Synth1 64-bit build (TABFORGE_SYNTH1_VST64); never committed.</summary>
internal sealed class RealSynth1FactAttribute : FactAttribute
{
    public RealSynth1FactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The native 64-bit VST2 route is Windows-only";
        else
        {
            var path = Environment.GetEnvironmentVariable("TABFORGE_SYNTH1_VST64");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                Skip = "TABFORGE_SYNTH1_VST64 is not set (real Synth1 64-bit fixture, never committed)";
        }
    }
}

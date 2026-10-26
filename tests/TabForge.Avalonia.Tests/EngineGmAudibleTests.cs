using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that the demo song is heard through the real audio engine process on the Avalonia routing (every track on the engine's
///     General MIDI synth): with a headless engine (Null driver, no speakers) and its master tap on, each track played alone from its first note
///     at or after bar 85 makes the master output non-silent.
/// Does not own: the routing's message stream (AvaloniaEngineRoutingTests), the synth's own sound (AudioSmokeTests), any device.
/// Tests: this class.
/// </summary>
public class EngineGmAudibleTests
{
    private const int FromBar = 84;                 // 0-based: bar 85
    private const double SilenceFloorRms = 0.001;
    private const int ListenMs = 1500;              // each track's window
    private const int SkipMs = 150;                 // the start of a window, before the first note reaches the output
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _out;
    public EngineGmAudibleTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryDemoTrackFromBar85IsHeardOnTheEngineMasterOutput()
    {
        var project = FullDemoSongFactory.Create();
        var settings = new PluginSettings { Driver = "Null" };
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust()) { EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe") };
        var routing = new RoutedMidiOutput(new NullMidiOutput(), client);
        var owner = new object();
        AvaloniaEngineRouting.Apply(project, routing, client, settings, AvaloniaEngineRouting.FullMasterPercent, owner);
        Assert.True(client.IsRunning, "the engine did not start");
        WaitUntil(() => client.Output is not null && ChainsReady(client), "the engine and its chains did not get ready");

        var gate = new object();
        var measuring = false;
        double sum = 0;
        long count = 0;
        client.MasterAudio += (_, _, samples, frames) =>
        {
            lock (gate)
            {
                if (!measuring) return;
                for (var i = 0; i < frames * 2; i++) sum += (double)samples[i] * samples[i];
                count += frames * 2;
            }
        };
        client.SetMasterTap(true);

        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var fromMs = timeline.Bars.First(b => b.Bar == FromBar).StartMs;
        var report = new StringBuilder();
        var silent = 0;
        using var engine = new PlaybackEngine(routing);
        for (var track = 0; track < project.Tracks.Count; track++)
        {
            var first = timeline.NotesFor(track).Where(n => n.OnsetMs >= fromMs && !n.Dead).OrderBy(n => n.OnsetMs).FirstOrDefault();
            if (first is null) continue;
            for (var other = 0; other < project.Tracks.Count; other++) project.Tracks[other].Mute = other != track;
            AvaloniaEngineRouting.Apply(project, routing, client, settings, AvaloniaEngineRouting.FullMasterPercent, owner);
            engine.Start(project, new PlaybackOptions { StartBar = first.Bar, StartCell = first.Cell }, _ => { }, () => { });
            Thread.Sleep(SkipMs);
            lock (gate) { measuring = true; sum = 0; count = 0; }
            Thread.Sleep(ListenMs);
            double rms;
            lock (gate) { measuring = false; rms = count == 0 ? 0 : Math.Sqrt(sum / count); }
            engine.Stop();
            report.AppendLine($"{project.Tracks[track].Name}: bar {first.Bar + 1}, rms {rms:0.00000} over {count / 2} frames");
            if (rms < SilenceFloorRms) silent++;
        }
        client.SetMasterTap(false);
        _out.WriteLine(report.ToString());
        Assert.True(silent == 0, $"{silent} tracks silent on the engine master:\n{report}");
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

    /// <summary>Every chain the sync requested has acknowledged its generation.</summary>
    private static bool ChainsReady(AudioEngineClient client)
    {
        var readiness = new ChainReadiness(client.RequestedChains);
        foreach (var ack in client.LastAcknowledgements) readiness.Acknowledge(ack);
        return readiness.IsReady;
    }
}

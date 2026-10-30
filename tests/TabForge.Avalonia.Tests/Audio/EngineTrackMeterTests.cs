using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Transport;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that a playing song's per-track levels leave the real engine process and reach the track rows' reading:
///     with a headless engine (Null driver) every track that plays from bar 85 shows a non-silent per-track reading on the engine's track tap.
/// Does not own: the drawn meters (TrackMeterTests, TrackMeterFeedTests), the master output (EngineGmAudibleTests).
/// Tests: this class.
/// </summary>
[Collection("EngineAudible")]
public class EngineTrackMeterTests
{
    private const int FromBar = 84;                 // 0-based: bar 85
    private const double SilenceFloor = 0.001;
    private const int ListenMs = 1500;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _out;
    public EngineTrackMeterTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryPlayingTrackShowsItsLevelOnTheEngineTrackTap()
    {
        var project = FullDemoSongFactory.Create();
        var settings = new PluginSettings { Driver = "Null" };
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust()) { EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe") };
        var routing = new RoutedMidiOutput(new NullMidiOutput(), client);
        var owner = new object();
        AvaloniaEngineRouting.Apply(project, routing, client, settings, AvaloniaEngineRouting.FullMasterPercent, owner);
        Assert.True(client.IsRunning, "the engine did not start");
        WaitUntil(() => client.Output is not null && ChainsReady(client), "the engine and its chains did not get ready");

        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var fromMs = timeline.Bars.First(b => b.Bar == FromBar).StartMs;
        var report = new StringBuilder();
        var silent = 0;
        var played = 0;
        using var engine = new PlaybackEngine(routing);
        for (var track = 0; track < project.Tracks.Count; track++)
        {
            var first = timeline.NotesFor(track).Where(n => n.OnsetMs >= fromMs && !n.Dead).OrderBy(n => n.OnsetMs).FirstOrDefault();
            if (first is null) continue;
            played++;
            for (var other = 0; other < project.Tracks.Count; other++) project.Tracks[other].Mute = other != track;
            AvaloniaEngineRouting.Apply(project, routing, client, settings, AvaloniaEngineRouting.FullMasterPercent, owner);
            engine.Start(project, new PlaybackOptions { StartBar = first.Bar, StartCell = first.Cell }, _ => { }, () => { });
            var peak = 0f;
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ListenMs)
            {
                Thread.Sleep(20);
                var reading = client.ReadTrackMeter(project.Tracks[track], false);
                if (reading is { } r) peak = Math.Max(peak, Math.Max(r.PeakLeft, r.PeakRight));
            }
            engine.Stop();
            report.AppendLine($"{project.Tracks[track].Name}: bar {first.Bar + 1}, peak {peak:0.00000}");
            if (peak < SilenceFloor) silent++;
        }
        _out.WriteLine(report.ToString());
        Assert.True(played > 0, "the demo song has no notes from bar 85");
        Assert.True(silent == 0, $"{silent} of {played} playing tracks read silence on their track tap:\n{report}");
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

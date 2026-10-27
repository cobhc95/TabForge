using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TabForge.Audio;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine's audible playhead when the output latency rises after the playhead has left its start (the audio engine's output opens
///     a moment into a start): the position holds until the sound catches up and never moves back toward the start.
/// Does not own: the latency values (RoutedMidiOutput) or the drawn clock (PlayheadClockTests).
/// Tests: this class.
/// </summary>
public class AudiblePlayheadLatencyRiseTests
{
    private const int RiseAfterMovedMs = 60;     // song ms the playhead has moved before the latency rises
    private const int RisenLatencyMs = 150;      // the latency once the output is open
    private const int WatchMs = 700;             // wall ms watched after the rise
    private const double ToleranceMs = 0.5;

    private sealed class NullSink : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void ALatencyRiseAfterTheStartNeverMovesTheAudiblePlayheadBack()
    {
        var previousLatency = RoutedMidiOutput.WindowsMidiLatencyMs;
        RoutedMidiOutput.WindowsMidiLatencyMs = 0;
        try
        {
            var song = FullDemoSongFactory.Create();
            using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
            using var engine = new PlaybackEngine(new RoutedMidiOutput(new NullSink(), client));
            engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
            var frame = new PlaybackPosition();
            var startMs = double.NaN;
            var previous = double.NegativeInfinity;
            var risen = false;
            var afterRise = Stopwatch.StartNew();
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 5000 && (!risen || afterRise.ElapsedMilliseconds < WatchMs))
            {
                var ms = engine.AudiblePlayhead(frame).ElapsedMs;
                if (engine.Timeline is not null && double.IsNaN(startMs)) startMs = ms;
                Assert.True(ms >= previous - ToleranceMs, $"the audible playhead moved back from {previous:0.0} to {ms:0.0} ms (latency rose: {risen})");
                previous = ms;
                if (!risen && !double.IsNaN(startMs) && ms - startMs >= RiseAfterMovedMs)
                {
                    RoutedMidiOutput.WindowsMidiLatencyMs = RisenLatencyMs;
                    risen = true;
                    afterRise.Restart();
                }
                Thread.Sleep(2);
            }
            Assert.True(risen, "the playhead never left its start");
            Assert.True(previous - startMs > RiseAfterMovedMs + 100, $"the playhead stopped at {previous - startMs:0} ms past the start");
            engine.Stop();
        }
        finally
        {
            RoutedMidiOutput.WindowsMidiLatencyMs = previousLatency;
        }
    }
}

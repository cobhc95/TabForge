using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a live mix push while playing (a track click, mute/solo, Focus track) keeps the instrument the song's Mix Table points have switched to.
// Does not own: the push itself (VolPanLiveProbeTests). Tests: this class.
public class MixPointLivePushTests
{
    private static string NoMoreLies => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TabForge", "samples", "iron_maiden", "no_more_lies.gp3"));

    [Theory]
    [InlineData(51, 0)]      // started after bar 50's switch to distortion (the switch is chased)
    [InlineData(48, 3600)]   // started before it and played through it
    [InlineData(123, 0)]     // bars 120-130, where a track click was reported to bring back the clean guitar
    public void LivePushKeepsTheMixPointsProgram(int startBar, int waitMs)
    {
        AlphaTabWarmUp.EnsureReady();
        if (!File.Exists(NoMoreLies)) return;   // the sample is local only (a copyrighted song, not in the repository)
        var song = GuitarProImporter.Import(NoMoreLies, new ImportContext());
        var output = new Recorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions { StartBar = startBar }, _ => { }, () => { });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000 && !output.Any()) Thread.Sleep(50);
        Thread.Sleep(400 + waitMs);
        var channel = ChannelAllocator.Assign(song)[0];
        output.Take();
        engine.SetMuteSolo(song);   // what a track click's live mix push does
        Thread.Sleep(300);
        var programs = output.Take().Where(p => p.Ch == channel).Select(p => p.Program).ToList();
        engine.Stop();
        Assert.NotEmpty(programs);
        Assert.All(programs, p => Assert.Equal(30, p));   // distortion, not the clean 26 the song starts on
    }

    private sealed class Recorder : IMidiOutput
    {
        private readonly List<(int Ch, int Program)> _pc = new();
        private bool _any;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public bool Any() { lock (_pc) return _any; }
        public void Send(int deviceId, int status, int data1, int data2) { lock (_pc) { _any = true; if ((status & 0xF0) == 0xC0) _pc.Add((status & 0x0F, data1)); } }
        public List<(int Ch, int Program)> Take() { lock (_pc) { var c = _pc.ToList(); _pc.Clear(); return c; } }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}

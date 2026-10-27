using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the live level push of the mixer. The engine receives only the tracks whose (volume, pan) changed since it last held them, and the
/// pushes queued by one drag coalesce into one push per UI pass.
/// Tests: AudioEngineClient.SetTrackLevelsNow (through AvaloniaEngineRouting.ApplyLevelsNow), NextFrameCoalescer.
/// </summary>
public class LiveMixPushTests
{
    [Fact]
    public void MovingOneFaderSendsOneTrackMixAfterTheEngineHoldsTheLevels()
    {
        var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var song = TemplateFactory.Blank();
        song.Tracks.Add(PluginTrack(song));
        song.Tracks.Add(PluginTrack(song));
        client.Sync(song.Tracks, new PluginSettings(), null, song, new object());
        var sent = new List<EngineCommand>();
        client.SentForTest = sent.Add;
        try
        {
            AvaloniaEngineRouting.ApplyLevelsNow(song, client, 100);
            Assert.Empty(sent);

            song.Tracks[^1].Volume = 60;
            AvaloniaEngineRouting.ApplyLevelsNow(song, client, 100);
            Assert.Equal(new[] { EngineCommand.SetTrackMix }, sent);
        }
        finally { client.SentForTest = null; }
    }

    [Fact]
    public void LiveMixRequestsWithinOneUiPassCoalesceToOnePush()
    {
        var ui = new DeferredUiThread();
        var pushes = 0;
        var coalescer = new NextFrameCoalescer(ui, () => pushes++, UiPriority.Render);
        for (var i = 0; i < 5; i++) coalescer.Request();
        Assert.Equal(0, pushes);

        ui.RunPosted();
        Assert.Equal(1, pushes);

        coalescer.Request();
        ui.RunPosted();
        Assert.Equal(2, pushes);
    }

    [Fact]
    public void MasterKnobDragBurstRepaintsTheHeaderOnce()
    {
        var ui = new DeferredUiThread();
        var repaints = 0;
        var painted = 0;
        var volume = 0;
        // The arrangement's master knob handler feeds this coalescer with Header.InvalidateVisual: one request per ValueChanged, one repaint per pass.
        var coalescer = new NextFrameCoalescer(ui, () => { repaints++; painted = volume; }, UiPriority.Render);
        for (var tick = 0; tick < 40; tick++)
        {
            volume = tick;
            coalescer.Request();   // before the coalescer each tick called Header.InvalidateVisual: 40 repaints for this burst
        }
        Assert.Equal(0, repaints);

        ui.RunPosted();
        Assert.Equal(1, repaints);
        Assert.Equal(39, painted);   // the one repaint reads the latest value, so the painted result is the same
    }

    private static TrackModel PluginTrack(SongProject song)
    {
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Guitar);
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
        return track;
    }

    /// <summary>Holds posted work until the test runs it, so the requests of one pass can be counted.</summary>
    private sealed class DeferredUiThread : IUiThread
    {
        private readonly List<Action> _posted = new();

        public void RunPosted()
        {
            var work = _posted.ToArray();
            _posted.Clear();
            foreach (var item in work) item();
        }

        public void Post(Action work, UiPriority priority = UiPriority.Normal) => _posted.Add(work);
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) { work(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => Task.FromResult(work());
        public Task YieldAsync(UiPriority priority) => Task.CompletedTask;
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background) => throw new NotSupportedException("The coalescer test uses no timer.");
    }
}

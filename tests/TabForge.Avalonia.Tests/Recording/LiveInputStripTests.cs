using System;
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Audio;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.LiveInput;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the live input strip: the pitch detector on synthetic tones (accuracy, octave guard, window size), the dB maths and peak hold, the tuner readout and
/// A4, the knobs through the session to the engine messages, resets, persistence, and the strip being present only in the live input FX window.
/// Does not own: the engine audio path (the allocation self-tests), the chain window itself (FxChainWindowTests).
/// Tests: this class.
/// </summary>
public class LiveInputStripTests
{
    private const int Rate = 48000;

    private static float[] Tone(double hz, int samples, bool saw = false)
    {
        var x = new float[samples];
        for (var i = 0; i < samples; i++)
        {
            var phase = hz * i / Rate;
            x[i] = saw ? (float)(0.5 * (2 * (phase - Math.Floor(phase + 0.5)))) : (float)(0.5 * Math.Sin(2 * Math.PI * phase));
        }
        return x;
    }

    private static double Cents(double measured, double expected) => 1200 * Math.Log2(measured / expected);

    private static float Detect(double hz, int windowMs, bool saw = false)
    {
        var d = new PitchDetector(Rate) { WindowMs = windowMs };
        return d.Detect(Tone(hz, d.RawWindow, saw), out _);
    }

    [Theory]
    [InlineData(41.2, false), InlineData(82.41, false), InlineData(110, true), InlineData(329.63, false), InlineData(440, false)]
    public void DetectorIsWithinOneCentOnCleanTones(double hz, bool saw)
    {
        var found = Detect(hz, 100, saw);
        Assert.True(found > 0);
        Assert.InRange(Cents(found, hz), -1, 1);
    }

    [Fact]
    public void DetectorStaysWithinTwoCentsOnAHighSawtooth() => Assert.InRange(Cents(Detect(440, 100, saw: true), 440), -2, 2);   // a sharp-cornered wave bends the parabolic refinement a little

    [Theory]
    [InlineData(-30), InlineData(-8), InlineData(12), InlineData(40)]
    public void DetectorFollowsDetunedBass(int cents)
    {
        var hz = 41.2 * Math.Pow(2, cents / 1200.0);
        Assert.InRange(Cents(Detect(hz, 100), hz), -1, 1);
    }

    [Fact]
    public void DetectorDoesNotJumpAnOctaveOnHarmonicRichBass()
    {
        foreach (var hz in new[] { 41.2, 55.0, 98.0 })
            Assert.InRange(Cents(Detect(hz, 100, saw: true), hz), -5, 5);
    }

    [Fact]
    public void ShortWindowsLoseTheLowestNotesAndLongOnesKeepThem()
    {
        Assert.Equal(0f, Detect(41.2, 20));
        Assert.InRange(Cents(Detect(41.2, 200), 41.2), -1, 1);
        Assert.InRange(Cents(Detect(440, 5), 440), -3, 3);
        foreach (var ms in LiveStripRules.WindowChoicesMs) Assert.True(Detect(Math.Max(110, 2500.0 / ms), ms) > 0, $"{ms} ms");   // two and a half periods fit every window
    }

    [Fact]
    public void SilenceHasNoPitch()
    {
        var d = new PitchDetector(Rate);
        Assert.Equal(0f, d.Detect(new float[d.RawWindow], out _));
    }

    [Fact]
    public void DbMathAndTexts()
    {
        Assert.Equal(0, LiveStripRules.PeakToDb(1), 6);
        Assert.Equal(-6.0206, LiveStripRules.PeakToDb(0.5), 3);
        Assert.Equal(LiveStripRules.MeterFloorDb, LiveStripRules.PeakToDb(0));
        Assert.Equal(LiveStripRules.MeterFloorDb, LiveStripRules.PeakToDb(double.NaN));
        Assert.Equal(0.5, LiveStripRules.MeterFraction(-30), 6);
        Assert.Equal("-inf", LiveStripRules.DbText(-80));
        Assert.Equal("-12 dB", LiveStripRules.DbText(-12.2));
        Assert.Equal("+3 dB", LiveStripRules.GainText(3));
        Assert.Same(LiveStripRules.DbText(-12), LiveStripRules.DbText(-12.3));
        Assert.Equal(MeterTone.Clip, LiveStripRules.ToneForDb(0));
        Assert.Equal(0.5, LiveStripRules.MeterFraction(LiveStripRules.PeakToDb(Math.Pow(10, -30.0 / 20))), 6);   // the armed row meter uses this curve too
    }

    [Fact]
    public void PeakHoldKeepsTheTopThenReleases()
    {
        var hold = new PeakHold();
        hold.Update(0.8, 0);
        Assert.Equal(0.8, hold.Update(0.2, PeakHold.HoldMs - 1));
        Assert.Equal(0.2, hold.Update(0.2, PeakHold.HoldMs + 10));
    }

    [Fact]
    public void ReadoutNamesNotesFromTheReferenceAndHidesTheNeedleWithoutPitch()
    {
        var r = new LiveTunerReadout();
        var t = DateTime.UtcNow;
        Assert.True(r.Update(440, 0.95f, 1, t));
        Assert.Equal("A4", r.Note);
        Assert.InRange(r.Cents, -0.5, 0.5);
        var shifted = new LiveTunerReadout { ReferenceHz = 432 };
        shifted.Update(432, 0.95f, 1, t);
        Assert.Equal("A4", shifted.Note);
        var sharp = new LiveTunerReadout();
        sharp.Update(82.41f * 1.0046f, 0.95f, 1, t);
        Assert.Equal("E2", sharp.Note);
        Assert.Contains("cents", sharp.Detail); Assert.Contains("Hz", sharp.Detail); Assert.Contains("sharp", sharp.Detail);
        Assert.True(r.Update(0, 0, 2, t.AddSeconds(5)));
        Assert.True(double.IsNaN(r.Cents));
        Assert.Equal("", r.Detail);
        Assert.Equal(LiveStripRules.MinReferenceHz, new LiveTunerReadout { ReferenceHz = 100 }.ReferenceHz);
    }

    [Fact]
    public void NewMessagesRoundTrip()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) { new SetInputGainMessage(3, -6.5f).Write(w); new SetTunerWindowMessage(50).Write(w); }
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.Equal(new SetInputGainMessage(3, -6.5f), SetInputGainMessage.Read(r));
        Assert.Equal(new SetTunerWindowMessage(50), SetTunerWindowMessage.Read(r));
    }

    private sealed class Host : ILiveInputSessionHost
    {
        public Host(AudioEngineClient engine) => Engine = engine;
        public PluginSettings Settings { get; } = new();
        public AudioEngineClient Engine { get; }
        public IUiThread Ui { get; set; } = new AvaloniaPlaybackSessionTests.FakeUiThread();
        public IUiThread UiThread => Ui;
        public int Syncs, Saves;
        public void SyncAudioEngine() => Syncs++;
        public void SaveSettings() => Saves++;
        public int Quiet;
        public void SaveLevelQuietly() => Quiet++;
        public void SetStatus(string text) { }
        public (bool Restore, bool Remember) AskRestoreChain() => (false, false);
        public void ShowFxChain(Action closed) { }
    }

    [Fact]
    public void KnobsGoThroughTheSessionToSettingsAndTheStandIn()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new Host(client);
        using var session = new LiveInputSession(host);
        session.SetGain(30);
        Assert.Equal(LiveStripRules.MaxGainDb, session.GainDb);
        Assert.Equal(LiveStripRules.MaxGainDb, host.Settings.LiveInput.GainDb);
        Assert.Equal(LiveStripRules.MaxGainDb, client.LiveInput.GainDb);
        session.SetMuted(true);
        Assert.True(host.Settings.LiveInput.Muted);
        Assert.Equal(0, client.LiveInput.Mix.Volume);
        session.SetTunerWindow(40);
        session.SetTunerWindow(41);
        Assert.Equal(40, host.Settings.LiveInput.TunerWindowMs);
        Assert.True(host.Saves + host.Quiet >= 3);
        host.Settings.LiveInput.GainDb = -5; host.Settings.LiveInput.Muted = false;
        session.ApplySavedLevel();
        Assert.Equal(-5, client.LiveInput.GainDb);
        Assert.False(client.LiveInput.Track.Mute);
    }

    [Fact]
    public void KnobDragSavesQuietlyAndSyncsOncePerFrame()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var queue = new QueueUi();
        var host = new Host(client) { Ui = queue };
        using var session = new LiveInputSession(host);
        client.LiveInput.Arm();
        var syncs0 = host.Syncs;
        for (var i = 0; i < 50; i++) { session.SetGain(i % 20 - 10); session.SetLevel(i, 64); }
        Assert.Equal(0, host.Saves);          // no SettingsChanged broadcast per move
        Assert.Equal(100, host.Quiet);
        Assert.Equal(syncs0, host.Syncs);     // nothing synchronous on the move
        queue.RunAll();
        Assert.True(host.Syncs - syncs0 <= 1);
    }

    private sealed class QueueUi : IUiThread
    {
        private readonly AvaloniaPlaybackSessionTests.FakeUiThread _inner = new();
        private readonly System.Collections.Generic.List<Action> _work = new();
        public void RunAll() { var w = _work.ToArray(); _work.Clear(); foreach (var a in w) a(); }
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, UiPriority priority = UiPriority.Normal) => _work.Add(work);
        public System.Threading.Tasks.Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) => _inner.InvokeAsync(work, priority);
        public System.Threading.Tasks.Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => _inner.InvokeAsync(work, priority);
        public System.Threading.Tasks.Task YieldAsync(UiPriority priority) => _inner.YieldAsync(priority);
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background) => _inner.CreateTimer(interval, priority);
    }

    [AvaloniaFact]
    public void StripKnobsRaiseChangesAndShowDefaults()
    {
        var knobs = new LiveStripKnobs();
        int? gain = null, volume = null, pan = null; bool? mute = null, solo = null;
        knobs.GainChanged += v => gain = v; knobs.VolumeChanged += v => volume = v; knobs.PanChanged += v => pan = v; knobs.MuteChanged += v => mute = v; knobs.SoloChanged += v => solo = v;
        knobs.Gain.SetByUser(6); Assert.Equal(6, gain);
        knobs.Pan.SetByUser(-10); Assert.Equal(54, pan);
        knobs.Volume.SetByUser(50); Assert.Equal(50, volume);
        knobs.Mute.IsChecked = true; Assert.True(mute);
        knobs.Solo.IsChecked = true; Assert.True(solo);
        gain = volume = pan = null; mute = solo = null;
        knobs.Show(0, 100, 64, false, false);
        Assert.Equal(LiveStripRules.DefaultGainDb, knobs.Gain.Value);
        Assert.Equal(MixerStripPresenter.VolumeDefault, knobs.Volume.Value);
        Assert.Equal(MixerStripPresenter.PanDefault, knobs.Pan.Value);
        Assert.False(knobs.Mute.IsChecked); Assert.False(knobs.Solo.IsChecked);
        Assert.Null(gain); Assert.Null(volume); Assert.Null(pan); Assert.Null(mute); Assert.Null(solo);   // Show raises nothing
    }

    [AvaloniaFact]
    public void StripMetersDrawBarsAndHoldWithoutTextAndTipShowsDb()
    {
        var meters = new LiveStripMeters();
        meters.Update(0.5, 0.25, 0);
        Assert.Equal("Input -6 dB, Output -12 dB", meters.Tip());
        Assert.False(double.IsNaN(meters.InputHold));
        Assert.True(meters.InputFraction > meters.OutputFraction);
        meters.Update(0, 0, 10);
        Assert.Equal("Input -inf, Output -inf", meters.Tip());
        Assert.True(meters.InputHold > 0);
        Assert.Equal(0, meters.InputFraction);
    }

    private static StandaloneFxChainHost ChainHost(TabForge.Models.TrackModel track) =>
        new(new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { track }, _ => { });

    [AvaloniaFact]
    public void StripIsOnlyInTheLiveInputWindowAndTunerWindowFollowsSettings()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var host = new Host(client);
        using var session = new LiveInputSession(host);
        var strip = new LiveInputStrip(session, client);
        var track = FxTestKit.Track(FxChainWindow.SampleChain(), MainWindowLiveInput.FxChainName);
        var live = new FxChainWindow(new StandaloneFxChainHost(new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { track }, _ => { }) { LiveStrip = strip }, track);
        live.Show();
        Assert.Single(live.GetVisualDescendants().OfType<LiveInputStrip>());
        Assert.NotEmpty(live.GetVisualDescendants().OfType<Menu>());
        var plain = new FxChainWindow(ChainHost(track), track);
        plain.Show();
        Assert.Empty(plain.GetVisualDescendants().OfType<LiveInputStrip>());
        host.Settings.LiveInput.TunerWindowMs = 200;
        strip.Tick(0, DateTime.UtcNow);
        Assert.Equal("200 ms", strip.Tuner.Window.SelectedItem);
        strip.Tuner.Window.SelectedItem = "20 ms";
        Assert.Equal(20, host.Settings.LiveInput.TunerWindowMs);
        strip.Tuner.Switch.IsChecked = true;
        Assert.True(strip.Tuner.IsOn);
        strip.Tuner.Switch.IsChecked = false;
        Assert.False(strip.Tuner.IsOn);
        Assert.Equal(LiveStripTuner.OffNote, strip.Tuner.NoteText);
        live.Close(); plain.Close();
    }
}

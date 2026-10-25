using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the slim live input strip: one control row high under an unmoved menu row, the mini sliders and M/S buttons being the track list painters, the lit and solo
/// states, the double-click reset, the cents text rate, no overlap at the window minimum width, and nothing in it changing size per frame.
/// Does not own: the strip logic and the engine messages (LiveInputStripTests), the track list itself (TrackRowControlsTests).
/// Tests: this class.
/// </summary>
public class LiveStripSliderTests
{
    internal sealed class Host : ILiveInputSessionHost
    {
        public Host(AudioEngineClient engine) => Engine = engine;
        public PluginSettings Settings { get; } = new();
        public AudioEngineClient Engine { get; }
        public IUiThread UiThread => new AvaloniaPlaybackSessionTests.FakeUiThread();
        public void SyncAudioEngine() { }
        public void SaveSettings() { }
        public void SetStatus(string text) { }
        public (bool Restore, bool Remember) AskRestoreChain() => (false, false);
        public void ShowFxChain(Action closed) { }
    }

    internal static (FxChainWindow Live, LiveInputStrip Strip, LiveInputSession Session, AudioEngineClient Client) OpenLive(double width = 980)
    {
        var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        var session = new LiveInputSession(new Host(client));
        var strip = new LiveInputStrip(session, client);
        var track = FxTestKit.Track(FxChainWindow.SampleChain(), MainWindowLiveInput.FxChainName);
        var live = new FxChainWindow(new StandaloneFxChainHost(new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { track }, _ => { }) { LiveStrip = strip }, track);
        live.Width = width;
        live.Show();
        live.UpdateLayout();
        return (live, strip, session, client);
    }

    internal static Point TopLeftIn(Visual of, Visual root) => of.TranslatePoint(new Point(0, 0), root) ?? default;

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { ArchitectureScan.RepoRoot(), "Source", "src", "TabForge.Avalonia" }.Concat(parts).ToArray()));

    [AvaloniaFact]
    public void StripIsOneControlRowHighAndTheMenuRowStaysWhereItIs()
    {
        var (live, strip, session, client) = OpenLive(1600);
        var plain = new FxChainWindow(new StandaloneFxChainHost(new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { live.Track }, _ => { }), live.Track);
        plain.Width = live.Width;
        plain.Show();
        plain.UpdateLayout();
        Assert.Equal(ChromeMetrics.LiveStripHeight, strip.Bounds.Height);
        Assert.True(strip.Bounds.Height <= TimelineView.DefaultRowHeight);
        var liveMenu = live.GetVisualDescendants().OfType<Menu>().First();
        var plainMenu = plain.GetVisualDescendants().OfType<Menu>().First();
        Assert.Equal(TopLeftIn(plainMenu, plain), TopLeftIn(liveMenu, live));
        Assert.Equal(plainMenu.Bounds.Height, liveMenu.Bounds.Height);
        Assert.True(TopLeftIn(strip, live).X >= TopLeftIn(liveMenu, live).X + liveMenu.Bounds.Width);   // the strip is right of the menus, in the same row
        live.Close(); plain.Close(); session.Dispose(); client.Dispose();
    }

    [AvaloniaFact]
    public void SlidersAndButtonsAreTheTrackListPaintersWithTheDeclaredDefaults()
    {
        var knobs = new LiveStripKnobs();
        Assert.Equal(TrackRowCells.SliderDefault(TrackRowCells.Cell.Volume), knobs.Volume.Default);
        Assert.Equal(TrackRowCells.SliderDefault(TrackRowCells.Cell.Pan), knobs.Pan.Default);
        Assert.Equal(TrackRowCells.SliderRange(TrackRowCells.Cell.Volume), (knobs.Volume.Minimum, knobs.Volume.Maximum));
        Assert.Equal(TrackRowCells.SliderRange(TrackRowCells.Cell.Pan), (knobs.Pan.Minimum, knobs.Pan.Maximum));
        Assert.Equal(LiveStripRules.DefaultGainDb, knobs.Gain.Default);
        Assert.Contains("TrackRowCells.DrawSlider(", Source("Platform", "MiniSlider.cs"));
        Assert.Contains("TrackRowCells.SliderValueAt(", Source("Platform", "MiniSlider.cs"));
        Assert.Contains("TrackHeaderColumn.DrawToggle(", Source("Views", "Fx", "LiveStripToggle.cs"));
    }

    [AvaloniaFact]
    public void DoubleClickResetsEachSliderToItsDefaultAndDragMapsLikeTheTrackList()
    {
        var knobs = new LiveStripKnobs();
        int? gain = null, volume = null, pan = null;
        knobs.GainChanged += v => gain = v; knobs.VolumeChanged += v => volume = v; knobs.PanChanged += v => pan = v;
        knobs.Gain.SetByUser(9); knobs.Volume.SetByUser(20); knobs.Pan.SetByUser(30);
        knobs.Gain.ResetToDefault(); knobs.Volume.ResetToDefault(); knobs.Pan.ResetToDefault();
        Assert.Equal(0, gain); Assert.Equal(MixerStripPresenter.VolumeDefault, volume); Assert.Equal(MixerStripPresenter.PanDefault + LiveStripKnobs.PanOffset, pan);
        var window = new Window { Width = 400, Height = 100, Content = knobs };
        window.Show();
        window.UpdateLayout();
        var slider = knobs.Volume;
        var x = slider.Bounds.Width * 0.2;
        var at = slider.TranslatePoint(new Point(x, slider.Bounds.Height / 2), window) ?? default;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        var expected = TrackRowCells.SliderValueAt(TrackRowCells.Cell.Volume, new Rect(slider.Bounds.Size), x, (0, 127));
        Assert.Equal(expected, slider.Value);
        Assert.Equal(expected, volume);
        window.Close();
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the strip at the top of a track's FX window: the Options dropdown (its count and each setting it holds), the groups wrapping whole at 500, 900 and 1600,
/// Vol, Pan, M and S changing the track as the track list does (a drag is one undo step), and a frame allocating nothing.
/// Does not own: what each option does (FxChainOptionsTests) or the track list itself.
/// Tests: this class.
/// </summary>
public class TrackFxStripTests
{
    private static string Count(Window window) => (string)DialogTestKit.Named<Button>(window, "OptionsButton").Content!;

    private static CheckBox Box(Window window, string name) => DialogTestKit.Named<CheckBox>(window, name);

    private static int Ticked(Window window) => new[] { "UseChainCheck", "GmSoundCheck", "AutoGmCheck", "AutoLoadCheck", "StartupCheck", "MatchPitchCheck" }.Count(n => Box(window, n).IsChecked == true);

    [AvaloniaFact]
    public void TheOptionsDropdownHoldsTheSixSettingsAndCountsTheTickedOnes()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var settings = new PluginSettings { AutoGmSound = false };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine(), settings));
        window.Show();
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CheckBox>(), c => c.Name is "UseChainCheck" or "GmSoundCheck" or "MatchPitchCheck");   // the boxes are in the popup, not in the window row
        Assert.Equal($"Options ({Ticked(window)} on)", Count(window));
        var pitch = Box(window, "MatchPitchCheck");
        Assert.Same(pitch.Parent, DialogTestKit.Named<Button>(window, "RemeasureButton").Parent);   // Re-measure sits next to Match pitch
        foreach (var name in new[] { "UseChainCheck", "GmSoundCheck", "AutoGmCheck", "AutoLoadCheck", "StartupCheck", "MatchPitchCheck" })
        {
            var box = Box(window, name);
            var was = box.IsChecked == true;
            var ticked = Ticked(window);
            FxTestKit.Press(window, box);
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(!was, box.IsChecked == true);
            Assert.Equal(ticked + (was ? -1 : 1), Ticked(window));
            Assert.Equal($"Options ({Ticked(window)} on)", Count(window));
        }
        Assert.Equal(settings.AutoGmSound, Box(window, "AutoGmCheck").IsChecked);
        Assert.Equal(Box(window, "GmSoundCheck").IsChecked, track.MidiSound);
        Assert.Equal(Box(window, "UseChainCheck").IsChecked, track.SoundSource == SoundSources.Plugins);
        window.Close();
    }

    private static Control[] Groups(TrackFxStrip strip) => strip.GetLogicalDescendants().OfType<WrapPanel>().Single().Children.ToArray();

    private static void Resize(TrackFxStrip strip, double width)
    {
        strip.Measure(new Size(width, double.PositiveInfinity));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
    }

    private static Point TopLeftIn(Control control, Control ancestor) => control.TranslatePoint(default, ancestor) ?? default;

    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(900)]
    [InlineData(1600)]
    public void GroupsWrapWholeWithoutOverlapOrClip(int width)
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        var strip = window.Strip!;
        var groups = Groups(strip);
        Resize(strip, width);
        var rects = groups.Select(g => new Rect(TopLeftIn(g, strip), g.Bounds.Size)).ToArray();
        for (var i = 0; i < rects.Length; i++)
        {
            Assert.True(rects[i].Right <= strip.Bounds.Width + 0.5 && rects[i].Bottom <= strip.Bounds.Height + 0.5, $"group {i} clipped at {width}");
            Assert.True(groups[i].Bounds.Width >= groups[i].DesiredSize.Width - 0.5, $"group {i} narrower than its content");
            for (var j = i + 1; j < rects.Length; j++) Assert.False(rects[i].Intersects(rects[j]), $"groups {i} and {j} overlap at {width}");
        }
        var rows = rects.Select(r => Math.Round(r.Center.Y / 10)).Distinct().Count();
        if (width >= 1600) Assert.Equal(1, rows);
        Resize(strip, 300);   // narrower than the groups side by side: whole groups drop to another row
        Assert.True(Groups(strip).Select(g => Math.Round(TopLeftIn(g, strip).Y / 10)).Distinct().Count() >= 2);
        window.Close();
    }

    [AvaloniaFact]
    public void TheStripSitsInTheMenuRowWithNoGapRow()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        window.UpdateLayout();
        var row = DialogTestKit.Named<Grid>(window, "MenuRow");
        var menu = DialogTestKit.Named<Menu>(window, "FxMenu");
        var strip = window.Strip!;
        Assert.Same(row, menu.Parent);
        Assert.Same(row, strip.Parent);
        var menuTop = menu.TranslatePoint(default, window)!.Value.Y;
        var stripTop = strip.TranslatePoint(default, window)!.Value.Y;
        Assert.True(stripTop < menuTop + menu.Bounds.Height, "the strip starts beside the menu, not under it");
        Assert.True(strip.Bounds.Left >= menu.Bounds.Right, "the strip is right of the menus");
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<WrapPanel>(), w => w.Name == "OptionsRow");   // no separate row for the options
        window.Close();
    }

    [AvaloniaFact]
    public void BothStripsHaveTheSameGroupsExceptTheOptionsButton()
    {
        var (live, liveStrip, session, client) = LiveStripSliderTests.OpenLive();
        var window = FxTestKit.Window(FxTestKit.Track(FxChainWindow.SampleChain()));
        window.Show();
        string Shape(StripFrame strip) => string.Join(",", strip.GetLogicalDescendants().OfType<WrapPanel>().Single().Children.Where(c => c is not Button).Select(c => c.GetType().Name));
        Assert.Equal(Shape(liveStrip), Shape(window.Strip!));
        Assert.Equal(liveStrip.Meters.Height, window.Strip!.Meters.Height);   // the same two stacked bars
        window.Close();
        live.Close(); session.Dispose(); client.Dispose();
    }

    [AvaloniaFact]
    public void TheTunerBlockNeedsAnArmedAudioInput()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var strip = window.Strip!;
        Assert.Contains(strip.Tuner, window.GetLogicalDescendants());
        Assert.False(strip.HasInput);
        Assert.False(strip.Tuner.Switch.IsEnabled);
        Assert.Equal("--", strip.Tuner.NoteText);
        Assert.Equal("No audio input on this track", ToolTip.GetTip(strip.Tuner.Switch));
        track.RecordArm = true;
        strip.Tick(0, DateTime.UtcNow);
        Assert.True(strip.Tuner.Switch.IsEnabled);
        Assert.True(strip.Tuner.IsOn);   // on by default
        window.Close();
    }

    [AvaloniaFact]
    public void TheTunerOffChoiceIsKeptForTheTrack()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.RecordArm = true;
        var settings = new PluginSettings();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine(), settings));
        window.Show();
        window.Strip!.Tuner.Switch.IsChecked = false;
        Assert.Contains(track.Id, settings.TunerOffTracks);
        window.Close();
        var again = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine(), settings));
        again.Show();
        Assert.False(again.Strip!.Tuner.IsOn);
        again.Close();
    }

    [AvaloniaFact]
    public void TheWindowSelectorShowsItsNumberUnclipped()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        window.Strip!.Tick(0, DateTime.UtcNow);
        window.UpdateLayout();
        var combo = window.Strip.Tuner.Window;
        var number = combo.GetVisualDescendants().OfType<TextBlock>().First(tb => tb.Text == "100");
        number.Measure(Size.Infinity);
        Assert.True(number.Bounds.Width >= number.DesiredSize.Width - 0.5, "the number is squeezed");
        Assert.True(number.TranslatePoint(new Point(number.Bounds.Width, 0), combo)!.Value.X <= combo.Bounds.Width - 10, "the number runs under the arrow");
        Assert.Contains("100 ms", ((TextBlock)ToolTip.GetTip(combo)!).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void AnArmedFrameWithTheTunerOnAllocatesNothing()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.RecordArm = true;
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine { OutputPeak = 0.5f }));
        window.Show();
        var strip = window.Strip!;
        var now = DateTime.UtcNow;
        for (var i = 0; i < 1000; i++) strip.Tick(i, now);   // warm up first: tiered compilation and first-use work settle here, not in the measured window
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) strip.Tick(1000 + i, now);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        window.Close();
    }

    [AvaloniaFact]
    public void VolPanMuteAndSoloChangeTheTrackAsTheTrackListDoes()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = FxTestKit.Window(track, host);
        window.Show();
        var knobs = window.Strip!.Knobs;
        Assert.False(knobs.Gain.IsVisible);   // a song track has no input gain

        knobs.Mute.IsChecked = true;
        var reference = new SongProject();
        reference.Tracks.Add(new TrackModel());
        TrackGroupEdits.SetFlag(reference, new[] { 0 }, TrackFlag.Mute, true);
        Assert.Equal(reference.Tracks[0].Mute, track.Mute);
        knobs.Solo.IsChecked = true;
        Assert.True(track.Solo);
        Assert.True(document.Undo.CanUndo);

        knobs.Volume.SetByUser(40);
        Assert.Equal(40, track.Volume);
        knobs.Pan.SetByUser(-10);
        Assert.Equal(54, track.Pan);

        track.Volume = 90; track.Pan = 64; track.Mute = false;   // changed in the track list
        window.RefreshFromTrack();
        Assert.Equal(90, knobs.Volume.Value);
        Assert.Equal(0, knobs.Pan.Value);
        Assert.False(knobs.Mute.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void ADragOfVolumeIsOneUndoStep()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = FxTestKit.Window(track, host);
        window.Show();
        window.UpdateLayout();
        var slider = window.Strip!.Knobs.Volume;
        var start = track.Volume;
        var y = slider.Bounds.Height / 2;
        Point At(double x) => slider.TranslatePoint(new Point(x, y), window)!.Value;
        window.MouseDown(At(slider.Bounds.Width * 0.9), MouseButton.Left);
        window.MouseMove(At(slider.Bounds.Width * 0.5));
        window.MouseMove(At(slider.Bounds.Width * 0.2));
        Assert.False(document.Undo.CanUndo);   // nothing is stored while the drag runs
        window.MouseUp(At(slider.Bounds.Width * 0.2), MouseButton.Left);
        Assert.NotEqual(start, track.Volume);
        Assert.Equal(1, document.Undo.UndoCount);
        DocumentEdits.Undo(document);
        Assert.Equal(start, document.Project.Tracks[0].Volume);
        window.Close();
    }

    [AvaloniaFact]
    public void AFrameAllocatesNothingOnceWarm()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine { OutputPeak = 0.5f };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var strip = window.Strip!;
        for (var i = 0; i < 20; i++) strip.Tick(i, DateTime.UtcNow);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) strip.Tick(20 + i, DateTime.UtcNow);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(strip.Meters.OutputFraction > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void ALostPointerCaptureEndsTheDragLikeARelease()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = FxTestKit.Window(track, host);
        window.Show();
        window.UpdateLayout();
        var slider = window.Strip!.Knobs.Volume;
        var released = 0;
        slider.Released += () => released++;
        window.MouseDown(slider.TranslatePoint(new Point(slider.Bounds.Width * 0.3, slider.Bounds.Height / 2), window)!.Value, MouseButton.Left);
        Assert.True(slider.IsDragging);
        slider.RaiseEvent(new PointerCaptureLostEventArgs(slider, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true)));
        Assert.False(slider.IsDragging);
        Assert.Equal(1, released);
        Assert.Equal(1, document.Undo.UndoCount);   // the cut-short drag still made its one undo step
        window.Close();
    }

    [AvaloniaFact]
    public void AWindowReplacedByAnUndoDoesNotSaveItsStaleTrackAsTheStartupTemplate()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var settings = new PluginSettings();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine(), settings));
        window.Show();
        FxTestKit.Press(window, Box(window, "StartupCheck"));
        DialogTestKit.RunQueuedUiWork();
        Assert.True(StartupTracks.Has(settings, track));
        StartupTracks.Find(settings, track)!.Volume = 11;   // the template as the undo left it
        track.Volume = 77;                                  // the stale track
        window.CloseReplaced();
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(11, StartupTracks.Find(settings, track)!.Volume);
    }

    [AvaloniaFact]
    public void EscapeClosesTheOpenOptionsPopupNotTheWindow()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        var button = DialogTestKit.Named<Button>(window, "OptionsButton");
        button.Flyout!.ShowAt(button);
        Assert.True(button.Flyout.IsOpen);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(button.Flyout.IsOpen);
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void PreferencesChangesReachTheOpenOptions()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-fxstrip-" + Guid.NewGuid().ToString("N"), "settings.json");
        var settings = TabForge.Avalonia.MainWindowSettings.Open(path);
        settings.Settings.Plugins.AutoGmSound = true;
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var track = document.Project.Tracks[0];
        var host = new SongFxChainHost(document, null, settings, () => null, null, new TabForge.Plugins.PluginTrust());
        var window = FxTestKit.Window(track, host);
        window.Show();
        var shown = Count(window);
        Assert.Equal(true, Box(window, "AutoGmCheck").IsChecked);
        settings.Settings.Plugins.AutoGmSound = false;
        settings.SaveSettings();
        Assert.Equal(false, Box(window, "AutoGmCheck").IsChecked);
        Assert.NotEqual(shown, Count(window));
        Assert.Equal($"Options ({Ticked(window)} on)", Count(window));
        window.Close();
    }
}

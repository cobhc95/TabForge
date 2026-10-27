using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the Mixer window: structure and WPF texts, the strip texts from MixerStripPresenter, the order from
/// TrackOrdering.Layout, group collapse, the renders in both themes and the contrast of its texts.
/// Does not own: the window, the presenter or the ordering rules.
/// Tests: this class.
/// </summary>
public class MixerWindowTests
{
    /// <summary>The song as the window edits it: a document of its own, so its gestures are undo steps of that document.</summary>
    private static SongDocument DocumentOf(SongProject project) => new(() => null) { Project = project };

    [AvaloniaFact]
    public void StructureMatchesWpf()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();

        // The default size now follows the working area and the UI scale (stage 3a), so only its bounds are fixed: never below the computed minimum.
        Assert.Equal("Mixer", window.Title);
        Assert.True(window.Width >= window.MinWidth && window.Height >= window.MinHeight, $"{window.Width}x{window.Height} vs min {window.MinWidth}x{window.MinHeight}");
        Assert.Equal(new[] { "Audio & Plug-ins settings…", "Group rules…", "Collapse all" }, Of<Button>(window).Where(b => b.GetType() == typeof(Button)).Select(b => b.Content).OfType<string>().Where(s => s.Length > 3));
        Assert.Equal(new[] { "Volume in track list", "Pan in track list", "Groups in track list" }, Of<CheckBox>(window).Where(c => c.Name!.StartsWith("Show-")).Select(c => c.Content));
        var header = Of<Grid>(window).Single(g => g.Name == "ColumnHeader").Children.OfType<TextBlock>().Select(t => t.Text);
        Assert.Equal(MixerWindow.ColumnNames, header);
        Assert.Equal(MixerWindow.Hint, Of<TextBlock>(window).Single(t => t.Name == "MixerHint").Text);
        Assert.Equal(song.Tracks.Count, Of<FxSplitButton>(window).Count(b => b.Name == "TrackFx"));
        Assert.Single(Of<MonitorFxButton>(window));
        Assert.Contains(Of<Border>(window), b => b.Name == "Mixer row: Live input");
        Assert.Equal(song.Tracks.Count, Of<CheckBox>(window).Count(c => c.Name == "MidiSound"));
        window.Close();
    }

    [AvaloniaFact]
    public void TrackRowSendSlidersWriteReverbToTheTrack()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();

        var reverb = Of<Slider>(window).First(s => ToolTip.GetTip(s) as string is string tip && tip.StartsWith("Reverb send"));
        reverb.Value = 50;

        Assert.Contains(song.Tracks, t => t.Reverb == 50);
    }

    [AvaloniaFact]
    public void StripTextsComeFromThePresenter()
    {
        var song = DemoSongFactory.Create();
        var track = song.Tracks[0];
        track.Transpose = 3;
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();

        Assert.Contains(MixerStripPresenter.SemitoneText(3), Of<TextBlock>(window).Where(t => t.Name == "Semitones").Select(t => t.Text));
        Assert.Contains(Of<Slider>(window), s => ToolTip.GetTip(s) as string == $"Track pan ({MixerStripPresenter.PanRange}; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: centre)");
        var strip = Of<Border>(window).First(b => b.Name == MixerStripPresenter.StripName(track.Name, false, false));
        var mute = strip.GetLogicalDescendants().OfType<ToggleButton>().Single(b => b.Name == "Mute");
        mute.IsChecked = true;
        Assert.True(track.Mute);
        Assert.Equal(MixerStripPresenter.StripName(track.Name, true, false), AutomationProperties.GetName(strip));
        Assert.Contains(Of<TextBlock>(window), t => t.Text == MixerStripPresenter.MasterName);
        window.Close();
    }

    [AvaloniaFact]
    public void OrderFollowsTrackOrderingAndCollapseHidesTracks()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();

        var expected = TrackOrdering.Layout(song).SelectMany(g => g.Tracks.Select(t => MixerStripPresenter.StripName(t.Name, t.Mute, t.Solo)));
        Assert.Equal(expected, Of<Border>(window).Where(b => b.Name?.StartsWith("Mixer strip: ") == true).Select(b => b.Name));
        var groups = TrackOrdering.Layout(song).Select(g => $"Mixer group: {g.Group}");
        Assert.Equal(groups, Of<Border>(window).Where(b => b.Name?.StartsWith("Mixer group: ") == true).Select(b => b.Name));

        DialogTestKit.Click(Of<Button>(window).Single(b => b.Name == "CollapseAll"));
        Assert.DoesNotContain(Of<Border>(window), b => b.Name?.StartsWith("Mixer strip: ") == true);
        Assert.Equal("Expand all", Of<Button>(window).Single(b => b.Name == "CollapseAll").Content);
        window.Close();
    }

    [AvaloniaFact]
    public void FaderThumbSitsAtItsValueAndTheFillReachesIt()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
        window.Show();
        window.EnsureStripsBuilt();
        window.UpdateLayout();
        var slider = Of<Slider>(window).First(s => s.Maximum == 100 && s.Value == 100);
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var track = slider.GetVisualDescendants().OfType<Track>().Single();
        Assert.True(thumb.Bounds.Right >= track.Bounds.Width - 1, $"thumb {thumb.Bounds} track {track.Bounds}");
        var fill = track.DecreaseButton!.GetVisualDescendants().OfType<Border>().First();
        Assert.True(fill.Bounds.Width > thumb.Bounds.X - 2, $"fill {fill.Bounds} btn {track.DecreaseButton!.Bounds} {track.DecreaseButton.GetType().Name} thumb {thumb.Bounds}");
        window.Close();
    }

    [AvaloniaFact]
    public void GroupRowKeysCollapseAndExpand()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();
        var group = TrackOrdering.Layout(song)[0].Group;
        Of<Border>(window).First(b => b.Name == $"Mixer group: {group}").Focus();
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Contains(group, song.Mixer.CollapsedGroups);
        Of<Border>(window).First(b => b.Name == $"Mixer group: {group}").Focus();
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Assert.DoesNotContain(group, song.Mixer.CollapsedGroups);
        window.Close();
    }

    [AvaloniaFact]
    public void GroupColourBusPowerAndReorderUseCoreRules()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();

        var group = TrackOrdering.Layout(song)[0].Group;
        var bus = song.Mixer.Bus(group);
        var fx = Of<FxSplitButton>(window).First(b => b.Name == "BusFx" && AutomationProperties.GetName(b) == $"{group} effects bus");
        Assert.True(fx.ChainOn);
        MixerBuses.SetOn(bus, false);
        Assert.False(bus.On);
        MixerBuses.SetOn(bus, true);
        var swatch = Of<Button>(window).First(b => b.Name == "GroupColour");
        Assert.Equal(song.Tracks.Count(t => MixerGroups.GroupOf(song, t) == group) > 0, swatch.Content is Border);
        var tracks = TrackOrdering.Layout(song)[0].Tracks;
        if (tracks.Count > 1)
        {
            Assert.True(TrackOrdering.MoveTrackToGroup(song, tracks[0], group, 1));
            window.Rebuild();
            Assert.Equal(tracks[0], TrackOrdering.Layout(song)[0].Tracks[1]);
        }
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixerRendersPngInEachTheme(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
        window.EnsureStripsBuilt();
        var path = DialogTestKit.SavePng(window, nameof(MixerWindowTests), $"mixer-{(light ? "light" : "dark")}.png");
        window.Close();
        Assert.True(new FileInfo(path).Length > 0);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOne(bool light)
    {
        foreach (var surface in new[] { "WindowBackground", "PanelBackground" })
            foreach (var text in new[] { "TextPrimary", "TextSecondary", "TextMuted", MixerWindow.AccentNameKey(light) })
                Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), AvaloniaPalette.Get(surface, light)) >= 4.5, $"{text} on {surface} (light: {light})");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FxAndMonitorButtonTextReadsOnItsSurfaces(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
        window.Show();
        window.EnsureStripsBuilt();
        UiColor Brush(string name) => window.TryFindResource(name, window.ActualThemeVariant, out var o) && o is global::Avalonia.Media.ISolidColorBrush b
            ? new UiColor(b.Color.A, b.Color.R, b.Color.G, b.Color.B) : throw new System.InvalidOperationException(name);
        var ink = AvaloniaPalette.Get("MonitorFxInkOnAmber", light);
        Assert.True(ThemeColours.Contrast(ink, Brush("MeterHotBrush")) >= 4.5, "MON ink on the active amber");
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), Brush("Panel2Brush")) >= 4.5, $"{text} on the button surface");
        Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get("TextPrimary", light), Brush("HoverBrush")) >= 4.5, "text on the hover surface");
        window.Close();
    }

    private static System.Collections.Generic.IEnumerable<T> Of<T>(Window window) where T : Control => window.GetLogicalDescendants().OfType<T>();

    [AvaloniaFact]
    public void SyncValuesPicksUpAnAddedTrack()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();
        var strips = Of<Border>(window).Where(b => b.Name?.StartsWith("Mixer strip: ") == true).Select(b => b.Name).ToList();

        var added = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(added);
        window.SyncValues();

        var after = Of<Border>(window).Where(b => b.Name?.StartsWith("Mixer strip: ") == true).Select(b => b.Name).ToList();
        Assert.Equal(strips.Count + 1, after.Count);
        Assert.Contains(MixerStripPresenter.StripName(added.Name, added.Mute, added.Solo), after);
        window.Close();
    }

    [AvaloniaFact]
    public void SyncValuesWithoutAnyChangeKeepsTheStrips()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();
        var before = Of<Border>(window).Where(b => b.Name?.StartsWith("Mixer strip: ") == true).ToList();

        window.SyncValues();

        var after = Of<Border>(window).Where(b => b.Name?.StartsWith("Mixer strip: ") == true).ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.All(before.Zip(after), pair => Assert.Same(pair.First, pair.Second));
        window.Close();
    }

    [AvaloniaFact]
    public void SyncValuesShowsAMuteMadeElsewhere()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();

        song.Tracks[0].Mute = true;
        window.SyncValues();

        Assert.Contains(MixerStripPresenter.StripName(song.Tracks[0].Name, true, song.Tracks[0].Solo), Of<Border>(window).Select(b => b.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void SyncValuesShowsAGroupPanMadeElsewhere()
    {
        var song = DemoSongFactory.Create();
        var window = new MixerWindow(DocumentOf(song));
        window.Show();
        window.EnsureStripsBuilt();
        var group = TrackOrdering.Layout(song).First().Group;
        var before = Of<Border>(window).Single(p => p.Name == $"Group: {group}");

        song.Mixer.Edit(group).Pan = 30;
        window.SyncValues();

        var after = Of<Border>(window).Single(p => p.Name == $"Group: {group}");
        Assert.NotSame(before, after);   // the group's panel was rebuilt to show the new pan
        window.Close();
    }
}

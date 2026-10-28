using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the transport bar's buttons: WPF order and sizes, disabled buttons with a reason in the tooltip, the live input button, the local tuning
/// fork and snap magnet, the zoom pair, the master volume knob and the mixer button, the owner's SVG icons, and dark and light renders in
/// %TEMP%\tf-avalonia-tests\TransportBarViewTests.
/// Tests: TransportBarView, TransportIconArt.
/// </summary>
public class TransportBarViewTests
{
    private static TransportBarView.TransportButton[] Buttons(TransportBarView bar) =>
        ((WrapPanel)bar.Child!).Children.OfType<StackPanel>().SelectMany(group => group.Children).OfType<TransportBarView.TransportButton>().ToArray();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaybackButtonsAreInWpfOrderDisabledWithAReason(bool light)
    {
        var bar = new TransportBarView();
        var window = new Window { Width = 480, Height = 60, Content = bar, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        var buttons = Buttons(bar);
        Assert.Equal(new[] { "mic", "rewind_to_beginning", "stop", null, "play", "next_section", "click_beat", "metronome", "loop", "tuning_fork", "snap_magnet", "mixer" }, buttons.Select(b => b.Item.Icon));
        // At 480 px the groups wrap to more rows; the buttons keep their normal size.
        Assert.All(buttons, b => Assert.Equal(TransportBarView.TransportButtonSize, b.Bounds.Width));
        Assert.All(buttons, b => Assert.Equal(buttons[0].Bounds.Width, b.Bounds.Width));
        Assert.All(buttons.Where(b => !b.Local), b =>
        {
            Assert.False(b.IsEnabled);
            Assert.Contains(TransportBarView.NotAvailable, (string)ToolTip.GetTip(b)!);
        });
        Assert.All(buttons.Where(b => b.Local), b => Assert.True(b.IsEnabled));
        // The knob closes the tuning, snap, mixer group; the timeline zoom pair is the row's last group.
        var groups = ((WrapPanel)bar.Child!).Children.OfType<StackPanel>().ToList();
        Assert.Same(bar.MasterVolumeKnob, groups[^2].Children.Last());
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(DialogTestKit.RenderDirectory(nameof(TransportBarViewTests)), light ? "transport-light.png" : "transport-dark.png"));
        window.Close();
    }

    private sealed class RecordingCommands : IMenuCommandRunner
    {
        public List<string> Ran { get; } = new();
        public bool Handles(string commandId) => true;
        public void Run(string commandId, string? argument) => Ran.Add(commandId);
    }

    [AvaloniaFact]
    public void ZoomPairAndMixerRunTheirWindowCommandsWithoutAPlaybackSession()
    {
        var commands = new RecordingCommands();
        var bar = new TransportBarView();
        bar.BindCommands(new ContextMenuHost { Commands = commands });
        bar.Bind(null);
        var wired = bar.GetLogicalDescendants().OfType<TransportBarView.TransportButton>()
            .Where(b => b.Item.Name is "Zoom in timeline" or "Zoom out timeline" or "Mixer").ToArray();
        Assert.Equal(3, wired.Length);
        Assert.All(wired, b => Assert.True(b.IsEnabled));
        // Pressed in a fixed order: the logical tree lists the mixer before the zoom pair, so the tree order is not the press order.
        foreach (var name in new[] { "Zoom in timeline", "Zoom out timeline", "Mixer" }) wired.Single(b => b.Item.Name == name).Pressed!();
        Assert.Equal(new[] { MainWindowTimelineZoom.ZoomInId, MainWindowTimelineZoom.ZoomOutId, "View.Mixer" }, commands.Ran);
    }

    [AvaloniaFact]
    public void TuningSnapAndMixerBeforeTheMasterVolumeKnobWithTheZoomPairLast()
    {
        var bar = new TransportBarView();
        var groups = ((WrapPanel)bar.Child!).Children.OfType<StackPanel>().ToList();
        var zoomPair = (StackPanel)groups[^1].Children[0];
        Assert.Equal(new[] { "zoom_plus", "zoom_minus" }, zoomPair.Children.OfType<TransportBarView.TransportButton>().Select(b => b.Item.Icon));
        Assert.All(zoomPair.Children.OfType<TransportBarView.TransportButton>(), b => Assert.Equal(26, b.Width));
        Assert.Same(bar.MasterVolumeKnob, groups[^2].Children[^1]);
        Assert.Equal(100, bar.MasterVolumeKnob.Value);
        Assert.Equal(TransportBarView.MasterVolumeLabel, bar.MasterVolumeKnob.Label);
        Assert.Equal("mixer", ((TransportBarView.TransportButton)groups[^2].Children[^2]).Item.Icon);
    }

    [AvaloniaFact]
    public void SnapMagnetTogglesSnapAndItsLitStateWithoutASession()
    {
        var bar = new TransportBarView();
        var snap = Buttons(bar).Single(b => b.Item.Name == "Snap");
        Assert.True(snap.Lit);
        snap.Pressed!.Invoke();
        Assert.False(bar.Snap.Enabled);
        Assert.False(snap.Lit);
    }

    [AvaloniaFact]
    public void TransportIconsLoad()
    {
        foreach (var name in new[] { "rewind_to_beginning", "stop", "play", "pause", "next_section", "click_beat", "metronome", "loop", "mic", "tuning_fork", "snap_magnet" })
            Assert.True(TransportIconArt.Has(name), name);
    }
}

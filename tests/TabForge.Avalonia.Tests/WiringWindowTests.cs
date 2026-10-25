using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Avalonia Wiring window on its sample routing graph: its controls, texts, enabled rules, the edit callback, the loop refusal,
/// the dark and light renders and their contrast. Tests: this file, plus the Tools menu item in PreviewToolsMenu.
/// </summary>
public sealed class WiringWindowTests
{
    private static (WiringWindow Window, PluginSlot Slot, IReadOnlyList<TrackModel> Tracks, List<string> Edits) Open(bool light)
    {
        Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var tracks = WiringWindow.SampleTracks();
        var slot = WiringWindow.SampleSlot();
        tracks[0].Rig.Plugins.Add(slot);
        var edits = new List<string>();
        var window = new WiringWindow(tracks, tracks[0], slot, change => { change(); edits.Add("edit"); });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, slot, tracks, edits);
    }

    private static T Named<T>(WiringWindow window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static IEnumerable<string> TextsOf(WiringWindow window) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void ShowsTheSectionsRowsAndControlsOfTheWiringWindow()
    {
        var (window, _, _, _) = Open(light: false);
        Assert.Equal("Wiring: Chorus", window.Title);
        var texts = TextsOf(window).ToList();
        foreach (var expected in new[] { "Audio", "MIDI input", "MIDI output", "Input 1/2", "Sidechain 3/4", "Source", "Track", "Channel", "Forward to", "Stereo → track" })
            Assert.Contains(expected, texts);
        Assert.Equal(2, window.GetLogicalDescendants().OfType<CheckBox>().Count());
        Assert.Equal("Pass incoming MIDI to next plug-in", Named<CheckBox>(window, "PassMidiCheck").Content);
        window.Close();
    }

    [AvaloniaFact]
    public void ChoicesAndSelectionsComeFromThePresenter()
    {
        var (window, slot, tracks, _) = Open(light: false);
        Assert.Equal(WiringPresenter.Pins, Named<ComboBox>(window, "InputCombo").ItemsSource);
        Assert.Equal("L+R stereo", Named<ComboBox>(window, "InputCombo").SelectedItem?.ToString());
        Assert.Equal("Not connected", Named<ComboBox>(window, "SidechainCombo").SelectedItem?.ToString());
        Assert.Equal("Off", Named<ComboBox>(window, "ForwardCombo").SelectedItem?.ToString());
        var sidechainLabels = Named<ComboBox>(window, "SidechainCombo").ItemsSource!.Cast<WiringTrackChoice>().Select(t => t.Label);
        Assert.Equal(new[] { "Not connected", "Bass", "Pad" }, sidechainLabels);
        Assert.True(Named<ComboBox>(window, "SidechainCombo").IsEnabled);
        Assert.True(Named<ComboBox>(window, "ForwardCombo").IsEnabled);
        Assert.False(Named<ComboBox>(window, "TrackCombo").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ChangingAControlGoesThroughTheEditCallback()
    {
        var (window, slot, tracks, edits) = Open(light: false);
        Named<ComboBox>(window, "SourceCombo").SelectedIndex = 1;
        Assert.Equal(PluginMidiIn.OtherTrack, slot.MidiIn!.Source);
        Assert.True(Named<ComboBox>(window, "TrackCombo").IsEnabled);
        Named<ComboBox>(window, "SidechainCombo").SelectedIndex = 2;
        Assert.Equal(tracks[2].Id.ToString("N"), slot.SidechainTrackId);
        Assert.Equal(2, edits.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void ForwardingIntoAFeedingTrackIsRefusedOnTheStatusLine()
    {
        var (window, slot, tracks, edits) = Open(light: false);
        // Pad already forwards its MIDI to Lead, so Lead forwarding back to Pad is a loop.
        Named<ComboBox>(window, "ForwardCombo").SelectedIndex = 2;
        Assert.Equal(WiringPresenter.RefusalText("Pad"), Named<TextBlock>(window, "StatusText").Text);
        Assert.Equal("", slot.MidiOutTrackId);
        Assert.Equal("Off", Named<ComboBox>(window, "ForwardCombo").SelectedItem?.ToString());
        Assert.Empty(edits);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "wiring-window-dark.png")]
    [InlineData(true, "wiring-window-light.png")]
    public void RendersPngAndKeepsTextContrastAtLeast45(bool light, string fileName)
    {
        var (window, _, _, _) = Open(light);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
        var path = DialogTestKit.RenderPath(nameof(WiringWindowTests), fileName);
        frame.Save(path);
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Equal(fileName, Path.GetFileName(path));

        var background = AvaloniaPalette.Get("WindowBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), background) >= 4.5, $"{text} on WindowBackground (light: {light})");

        window.Close();
        Application.Current!.RequestedThemeVariant = null;
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensTheWiringWindow()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Wiring…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.OfType<WiringWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}

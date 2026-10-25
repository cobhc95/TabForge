using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the FX chain window checks: the structure in WPF order, the title, the notices from the engine state, the Remove rule, the bypass switch and
/// volume, the MIDI... hand-off, the chain files, dark and light PNGs, contrast and the preview's menu item.
/// Does not own: the window, the presenter or the palette; the edits' undo (FxChainEditsTests), the keys (FxChainKeysTests).
/// Tests: this class.
/// </summary>
public class FxChainWindowTests
{
    private static FxChainWindow Make() => FxTestKit.Window();

    private static FxChainList Chain(Window window) => FxTestKit.List(window);

    /// <summary>The buttons, check boxes and text of the window's controls in tree order, outside the chain list.</summary>
    private static (string Role, string Text)[] Layout(Window window) => DialogTestKit.LayoutControls(window)
        .Where(c => c is Button or TextBlock or CheckBox or ComboBox && !c.GetLogicalAncestors().Any(a => a is FxChainList))
        .Select(c => (Role: c.GetType().Name, Text: c switch
        {
            CheckBox k => k.Content as string ?? "",
            Button b => b.Content as string ?? "",
            TextBlock t => t.Text ?? "",
            _ => "",
        }))
        .ToArray();

    [AvaloniaFact]
    public void ControlsMatchWpfWindowOrderAndTexts()
    {
        var window = Make();
        window.Show();

        // The WPF window's controls in tree order (FxChainWindow.Sections.cs and FxChainWindow.cs): the track strip (name, Options dropdown), then the chain's Add and Remove,
        // then the plug-in bar (presets, +, Wiring, MIDI, Volume, On). The status line is the last control.
        var expected = new[]
        {
            ("AccessText", "_FX"), ("AccessText", "_Chains"), ("AccessText", "_Options"),
            ("TextBlock", "Sample track"),
        };
        Assert.Equal(expected, Layout(window).Take(expected.Length));
        var texts = Layout(window).Select(c => c.Text).ToList();
        Assert.StartsWith("Options (", texts[expected.Length]);   // the six options sit in this dropdown's popup
        Assert.True(texts.IndexOf("Add…") > expected.Length);
        Assert.True(texts.IndexOf("Remove") > texts.IndexOf("Add…"));
        Assert.True(texts.IndexOf("Wiring…") > texts.IndexOf("Remove"));
        Assert.True(texts.IndexOf("MIDI…") > texts.IndexOf("Wiring…"));
        Assert.True(texts.IndexOf("On") > texts.IndexOf("MIDI…"));
        Assert.Equal(("TextBlock", ""), Layout(window)[^1]);
        Assert.Equal("FX: Sample track", window.Title);
        Assert.Equal((980d, 620d, 640d, 380d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        window.Close();
    }

    [AvaloniaFact]
    public void ChainSwitchStartsFromTheTrackNotFromTheWindow()
    {
        var chainOn = FxTestKit.Track(FxChainWindow.SampleChain());
        chainOn.SoundSource = SoundSources.Plugins;
        var on = FxTestKit.Window(chainOn);
        on.Show();
        Assert.True(DialogTestKit.Named<CheckBox>(on, "UseChainCheck").IsChecked);
        on.Close();

        var off = FxTestKit.Window(FxTestKit.Track(FxChainWindow.SampleChain()));
        off.Show();
        Assert.False(DialogTestKit.Named<CheckBox>(off, "UseChainCheck").IsChecked);
        off.Close();
    }

    [AvaloniaFact]
    public void NoticeFollowsTheEngineStateAndTheSelectedPlugin()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine { Running = false };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var notice = DialogTestKit.Named<TextBlock>(window, "DockMessage");
        Assert.Equal(FxChainPresenter.ChainOffNotice, notice.Text);

        track.SoundSource = SoundSources.Plugins;
        window.RefreshFromTrack();
        Chain(window).Click(0);
        Assert.Equal(FxChainPresenter.StartingNotice, notice.Text);
        Chain(window).Click(2);
        Assert.Equal(FxChainPresenter.PluginBypassedNotice, notice.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void BypassStampShowsOnlyOverTheDockedEditorOfAPluginThatDoesNotPlay()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
        Chain(window).Click(0);
        Assert.True(area.EditorVisible);
        Assert.False(area.OverlayVisible);   // an enabled plug-in on a chain that is on
        Chain(window).Click(2);
        Assert.True(area.OverlayVisible);    // the bypassed delay
        track.SoundSource = SoundSources.Midi;
        window.RefreshFromTrack();
        Chain(window).Click(0);
        Assert.True(area.OverlayVisible);    // the chain is off
        window.Close();
    }

    [AvaloniaFact]
    public void EmptyChainShowsThePresenterNoticeAndRemoveDoesNothing()
    {
        var window = FxTestKit.Window(FxTestKit.Track());
        window.Show();
        Assert.Equal(FxChainPresenter.NoPluginsNotice, DialogTestKit.Named<TextBlock>(window, "DockMessage").Text);
        FxTestKit.PressButton(window, "RemoveButton");
        Assert.Equal(0, Chain(window).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void RemoveTakesTheSelectedPluginOnly()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        Chain(window).Click(1);
        FxTestKit.PressButton(window, "RemoveButton");
        Assert.Equal(new[] { "Sample piano", "Sample delay" }, track.Rig.Plugins.Select(s => s.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void BypassSwitchChangesTheSelectedPluginAndTheEngine()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        Chain(window).Click(1);
        var bypass = DialogTestKit.Named<CheckBox>(window, "BypassCheck");
        Assert.True(bypass.IsChecked);
        FxTestKit.Press(window, bypass);
        Assert.False(track.Rig.Plugins[1].Enabled);
        Assert.Contains("enabled:Sample reverb:False", engine.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void VolumeTextUsesThePresenterFormat()
    {
        Assert.Equal("-inf dB", FxChainWindow.VolumeText(-60));
        Assert.Equal("+2.0 dB", FxChainWindow.VolumeText(2));
        Assert.Equal("-1.5 dB", FxChainWindow.VolumeText(-1.5));
    }

    [AvaloniaFact]
    public void MidiButtonOpensTheMidiProcessingWindowForTheSelectedPlugin()
    {
        var window = Make();
        window.Show();
        Chain(window).Click(0);
        FxTestKit.PressButton(window, "MidiButton");
        var opened = window.OwnedWindows.OfType<MidiProcessingWindow>().ToList();
        Assert.Single(opened);
        Assert.Equal("MIDI processing: Sample piano", opened[0].Title);
        foreach (var midi in opened) midi.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void WiringButtonOpensTheWiringWindowOfTheSelectedPlugin()
    {
        var window = Make();
        window.Show();
        Chain(window).Click(1);
        FxTestKit.PressButton(window, "WiringButton");
        var opened = window.OwnedWindows.OfType<WiringWindow>().Single();
        Assert.Contains("Sample reverb", opened.Title);
        opened.Close();
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "fx-chain-dark.png")]
    [InlineData(true, "fx-chain-light.png")]
    public void RendersPngInEachTheme(bool light, string fileName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Make();
        Assert.True(new FileInfo(DialogTestKit.SavePng(window, nameof(FxChainWindowTests), fileName)).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Make();
        window.Show();
        foreach (var text in new IBrush?[]
        {
            DialogTestKit.Named<TextBlock>(window, "DockMessage").Foreground, DialogTestKit.Named<TextBlock>(window, "StatusLine").Foreground,
            DialogTestKit.Named<Button>(window, "OptionsButton").Foreground, DialogTestKit.Named<Button>(window, "RemoveButton").Foreground,
        })
            Assert.True(DialogTestKit.Contrast(text, window.Background, "FX chain") >= 4.5);
        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensFxChain()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "FX chain…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var opened = main.OwnedWindows.OfType<FxChainWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void LoadAndAddChainFilesPutTheirPluginsOnTheChain()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-chain-{Guid.NewGuid():N}.tfchain");
        try
        {
            PluginLibrary.SaveChain(file, FxChainWindow.SampleChain());
            var track = FxTestKit.Track();
            var window = FxTestKit.Window(track, files: new FxTestKit.PickFile(file));
            window.Show();
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, track.Rig.Plugins.Select(s => s.Name));
            Assert.Equal(3, Chain(window).Count);

            FxTestKit.Choose(window, "Add FX chain to the end…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(6, track.Rig.Plugins.Count);
            Assert.Equal(6, Chain(window).Count);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void SaveChainFileWritesTheChainToThePickedFile()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-chain-{Guid.NewGuid():N}.tfchain");
        try
        {
            var window = FxTestKit.Window(files: new FxTestKit.PickFile(file));
            window.Show();
            FxTestKit.Choose(window, "Save FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, PluginLibrary.LoadChain(file)!.Select(s => s.Name));
            Assert.Equal($"Saved {Path.GetFileName(file)}", DialogTestKit.Named<TextBlock>(window, "StatusLine").Text);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void StatusLineSpansTheWindowWidthSoLongNoticesWrapWide()
    {
        var window = Make();
        window.Show();
        window.UpdateLayout();
        var status = DialogTestKit.Named<TextBlock>(window, "StatusLine");
        Assert.True(status.Bounds.Width >= 300, $"The status line is {status.Bounds.Width} px wide.");
        window.Close();
    }
}

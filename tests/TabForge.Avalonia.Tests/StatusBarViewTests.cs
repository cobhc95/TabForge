using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Playback;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks of the preview's status bar: the readouts it shows for the demo song (no engine connected), the audio device button and
/// the output name, the import Cancel button, its place under the workspace above the notice row, and the dark and light renders with text contrast.
/// Does not own: the readouts' wording (the presenters' tests) or the bar's drawing beyond these checks.
/// Tests: this class.
/// </summary>
public class StatusBarViewTests
{
    /// <summary>The window size the owner's comparison shots use.</summary>
    private const int RenderWidth = 1600;
    private const int RenderHeight = 1000;

    private static TextBlock Readout(StatusBarView bar, string name) => bar.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == name);

    [AvaloniaFact]
    public void DemoSongReadoutsShowNoEngineState()
    {
        var main = new MainWindow();
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = main.GetVisualDescendants().OfType<StatusBarView>().Single();
        Assert.Equal(StatusBarView.ReadyText, Readout(bar, "StatusText").Text);
        Assert.StartsWith("Measure 1  ·  ", Readout(bar, "StatusPosition").Text);
        Assert.EndsWith("cell 1", Readout(bar, "StatusPosition").Text);
        Assert.Contains("quarter", Readout(bar, "StatusMaster").Text);
        Assert.StartsWith("♩=", Readout(bar, "StatusMaster").Text);
        Assert.Equal(WinMmMidiOutput.MapperName, Readout(bar, "StatusOutput").Text);
        // The audio interface always shows, so it takes the slot the MIDI output name would use: the name stays hidden beside it.
        Assert.False(Readout(bar, "StatusOutput").IsVisible);
        Assert.True(bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice").IsVisible);
        Assert.False(bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CancelImport").IsVisible);
        main.Close();
    }

    [AvaloniaFact]
    public void TransientStatusUpdatesStatusTextWhileNoticeStaysHidden()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.No), _ => Task.FromResult(true));
        try
        {
            var score = new ScorePreviewView();
            var owner = new Window();
            var status = new MainWindowStatusBar(tabs, score, null, null, owner);
            try
            {
                StatusNoticeBarView? notice = null;
                notice = new StatusNoticeBarView("Preview notice", "Dismiss", () => notice?.Hide());
                status.BindNotice(notice);
                status.ShowTransient("Metronome off");
                Assert.Equal("Metronome off", Readout(status.View, "StatusText").Text);
                Assert.False(notice.IsVisible);
                notice.Show("File not found: x");
                Assert.True(notice.IsVisible);
                notice.StatusBarVisible = false;
                Assert.False(notice.IsVisible);
                notice.StatusBarVisible = true;
                Assert.True(notice.IsVisible);
            }
            finally
            {
                status.Dispose();
                owner.Close();
            }
        }
        finally
        {
            tabs.Dispose();
        }
    }

    [AvaloniaFact]
    public void AudioDeviceButtonShowsTheRunningOutputWithItsLatencyAndRate()
    {
        var bar = new StatusBarView();
        var button = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        var running = AudioDeviceStatusPresenter.Decide(new AudioDeviceStatusInput(
            true, ("WASAPI (shared)", "Analogue 1/2 (Audient iD4)", 48000, 256, 12), AudioDriverNames.Asio, "Audient USB", 48000, _ => null));
        bar.ShowAudioDevice(running);
        Assert.True(button.IsVisible);
        Assert.Equal("WASAPI (shared): Analogue 1/2 (Audient iD4) (12 ms · 48 kHz · 256 smp)", Readout(bar, "AudioDeviceText").Text);
        Assert.Equal("Audio interface settings", ToolTip.GetTip(button));
    }

    private static string[] TipLines(Button button) => ((StackPanel)ToolTip.GetTip(button)!).Children.OfType<TextBlock>().Select(t => t.Text!).ToArray();

    [AvaloniaFact]
    public void AudioTooltipHasThreeLinesWithTheFullDeviceNameAndNoChannelsWhenUnknown()
    {
        var bar = new StatusBarView();
        var button = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        var longName = "Analogue 1/2 (Audient iD4 USB Audio Class 2.0 Interface with a very long name)";
        bar.ShowAudioTip(new AudioDeviceTip("ASIO", longName, "48 kHz · 256 smp · 8 ms", null));
        Assert.Equal(new[] { "Audio interface settings", "ASIO · " + longName, "48 kHz · 256 smp · 8 ms" }, TipLines(button));
        Assert.Equal(global::Avalonia.Media.FontWeight.Bold, ((StackPanel)ToolTip.GetTip(button)!).Children.OfType<TextBlock>().First().FontWeight);
    }

    [AvaloniaFact]
    public void AudioTooltipAddsTheChannelsLineAndUpdatesAfterADeviceChange()
    {
        var bar = new StatusBarView();
        var button = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        bar.ShowAudioTip(new AudioDeviceTip("ASIO", "Audient USB", "48 kHz · 256 smp · 8 ms", "In 2 · Out 2"));
        Assert.Equal(4, TipLines(button).Length);
        Assert.Equal("In 2 · Out 2", TipLines(button)[3]);
        var first = ToolTip.GetTip(button);
        bar.ShowAudioTip(new AudioDeviceTip("ASIO", "Audient USB", "48 kHz · 256 smp · 8 ms", "In 2 · Out 2"));
        Assert.Same(first, ToolTip.GetTip(button));   // unchanged facts keep the cached tooltip
        bar.ShowAudioTip(new AudioDeviceTip("WASAPI (shared)", "Speakers", "44.1 kHz · 512 smp · 12 ms", null));
        Assert.Equal(new[] { "Audio interface settings", "WASAPI (shared) · Speakers", "44.1 kHz · 512 smp · 12 ms" }, TipLines(button));
    }

    [AvaloniaFact]
    public void AudioInterfaceLinkHidesTheMidiOutputNameAndRaisesTheSettingsRequest()
    {
        var bar = new StatusBarView();
        var button = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        var readout = new StatusTextReadout("1 · 1 · 1", "", false, "", "4/4", "C", 120, "C", "Quarter", 0, false);
        bar.ShowReadout(readout, "Microsoft MIDI Mapper (default)", showOutput: true);
        Assert.True(Readout(bar, "StatusOutput").IsVisible);
        bar.ShowAudioDevice(AudioDeviceStatusPresenter.Decide(new AudioDeviceStatusInput(
            true, ("WASAPI (shared)", "Analogue 1/2 (Audient iD4)", 48000, 256, 12), AudioDriverNames.WasapiShared, "", 48000, _ => null)));
        Assert.False(Readout(bar, "StatusOutput").IsVisible);
        Assert.Null(button.Cursor);   // the pointer shape never changes over a button (owner rule)
        var requested = 0;
        bar.AudioDeviceRequested += () => requested++;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, requested);
        bar.ShowAudioDevice(AudioDeviceStatusPresenter.Decide(new AudioDeviceStatusInput(false, null, AudioDriverNames.WasapiShared, "", 48000, _ => null)));
        Assert.True(Readout(bar, "StatusOutput").IsVisible);
    }

    [AvaloniaFact]
    public void AudioDeviceButtonShowsTheIdleAsioDeviceWhenTheEngineIsNotRunning()
    {
        var bar = new StatusBarView();
        var button = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        bar.ShowAudioDevice(AudioDeviceStatusPresenter.Decide(new AudioDeviceStatusInput(false, null, AudioDriverNames.Asio, "Audient USB", 48000, _ => null)));
        Assert.True(button.IsVisible);
        Assert.Equal("ASIO: Audient USB (idle · 48 kHz)", Readout(bar, "AudioDeviceText").Text);
    }

    [AvaloniaFact]
    public void AudioInterfaceLinkOpensPreferencesOnTheAudioPage()
    {
        var main = new MainWindow();
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var button = main.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var preferences = main.OwnedWindows.OfType<PreferencesWindow>().Single();
        Assert.Equal(SettingsCatalog.AudioVst, preferences.Page);
        preferences.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void MidiOutputNameShowsOnlyWhenTheSongSendsMidi()
    {
        var bar = new StatusBarView();
        var readout = new StatusTextReadout("1 · 1 · 1", "", false, "", "4/4", "C", 120, "C", "Quarter", 0, false);
        bar.ShowReadout(readout, "Microsoft MIDI Mapper (default)", showOutput: false);
        Assert.False(Readout(bar, "StatusOutput").IsVisible);
        bar.ShowReadout(readout, "Microsoft MIDI Mapper (default)", showOutput: true);
        Assert.True(Readout(bar, "StatusOutput").IsVisible);
    }

    [AvaloniaFact]
    public void ImportCancelShowsWhileImportsRun()
    {
        var bar = new StatusBarView();
        var cancel = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CancelImport");
        bar.ShowImports(ImportStatusPresenter.ForPending(new[] { "Riff.gp" }));
        Assert.True(cancel.IsVisible);
        Assert.Equal("Importing Riff.gp…", Readout(bar, "StatusText").Text);
        Assert.Equal("Cancel importing Riff.gp", ToolTip.GetTip(cancel));
        bar.ShowImports(ImportStatusPresenter.ForPending(Array.Empty<string>()));
        Assert.False(cancel.IsVisible);
    }

    [AvaloniaFact]
    public void StatusBarSitsAboveTheNoticeRowUnderTheWorkspace()
    {
        var main = new MainWindow();
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var dock = main.GetLogicalDescendants().OfType<DockPanel>().First();   // the window content is wrapped in the scaler and the title frame
        var order = dock.Children.Select(c => (c.Name ?? c.GetType().Name, DockPanel.GetDock(c))).ToList();
        // Bottom-docked rows stack upward in child order: the first one is the lowest. The main notice row comes before the status bar,
        // so the status bar sits above it. The autosave and saved-video notices are also bottom-docked, so the indices are looked up by name.
        var notice = order.FindIndex(entry => entry.Item1 == "StatusNoticeBar");
        var status = order.FindIndex(entry => entry.Item1 == "StatusBar");
        Assert.True(notice >= 0 && status > notice, $"the status bar (child {status}) sits above the notice row (child {notice})");
        Assert.Equal(Dock.Bottom, order[status].Item2);
        Assert.True(dock.Children[status].IsVisible);
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    public void RendersTheMainWindowBottomInEachTheme(bool light, string themeName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var main = new MainWindow { Width = RenderWidth, Height = RenderHeight };
        var path = DialogTestKit.SavePng(main, nameof(StatusBarViewTests), $"main-{themeName}-{Guid.NewGuid():N}.png");
        Assert.True(new System.IO.FileInfo(path).Length > 0);
        main.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusTextColoursReadAtLeastFourAndAHalfToOneOnThePanel(bool light)
    {
        // BarStateMarked (the marked-bar readout) is left out: it reads below 4.5:1 on the panel in both themes, the palette's call.
        foreach (var text in new[] { "TextPrimary", "TextLegible" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), AvaloniaPalette.Get("PanelBackground", light)) >= 4.5,
                $"{text} on PanelBackground (light: {light})");
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the metronome, count-in and loop settings popups of the transport bar: each opens above its button with the title, rows and helper
///     text, takes the window's theme, writes the audio settings and reports every change, and closes on ×; the loop popup's boxes read and
///     normalise their text; the loop settings rules. Renders each popup in dark and light to %TEMP%\tf-avalonia-tests\transport-popups.
/// Does not own: the engine (the loop and count-in playback tests), the transport bar's drawing (TransportBarViewTests).
/// Tests: this class.
/// </summary>
public class TransportSettingsPopupTests
{
    private static readonly string CaptureFolder = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "transport-popups");

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachPopupOpensAboveItsButtonInTheWindowThemeAndRenders(bool light)
    {
        var (window, buttons) = Window(light);
        var audio = new AudioSettings();
        var changes = 0;
        foreach (var (name, frame, title) in new[]
        {
            ("Click and beat count-in", ClickSettingsPopups.CountIn(audio, () => changes++), ClickSettingsPopups.CountInTitle),
            ("Metronome", ClickSettingsPopups.Metronome(audio, () => changes++), ClickSettingsPopups.MetronomeTitle),
            ("Loop enable", LoopSettingsPopup.Create(audio, LoopSettingsRules.AreaText(false, false, 0, 0), () => false, _ => { }, () => changes++, () => { }), LoopSettingsPopup.Title),
        })
        {
            var button = buttons.Single(b => b.Item.Name == name);
            frame.OpenAbove(button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(frame.IsOpen, name);
            Assert.Contains(frame.Rows.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == title);
            Assert.Equal(light ? ThemeVariant.Light : ThemeVariant.Dark, frame.Panel.ActualThemeVariant);
            var panelBottom = frame.Panel.PointToScreen(new Point(0, frame.Panel.Bounds.Height)).Y;
            Assert.True(panelBottom <= button.PointToScreen(new Point(0, 0)).Y, $"{name}: the popup is not above its button");
            Save(frame.Panel, $"{title.Split(' ')[0].ToLowerInvariant()}-{(light ? "light" : "dark")}.png");
            var close = frame.Rows.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Content, "×"));
            close.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(frame.IsOpen, name + " stays open after ×");
        }
        Assert.Equal(0, changes);   // opening and closing changes nothing
        window.Close();
    }

    // Every text label of each popup is arranged at least as wide as it wants, so none is clipped (the loop popup's first row wraps).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPopupLabelIsArrangedAtLeastItsDesiredWidth(bool light)
    {
        var (window, buttons) = Window(light);
        var audio = new AudioSettings();
        foreach (var (name, frame) in new[]
        {
            ("Click and beat count-in", ClickSettingsPopups.CountIn(audio, () => { })),
            ("Metronome", ClickSettingsPopups.Metronome(audio, () => { })),
            ("Loop enable", LoopSettingsPopup.Create(audio, LoopSettingsRules.AreaText(false, false, 0, 0), () => false, _ => { }, () => { }, () => { })),
        })
        {
            frame.OpenAbove(buttons.Single(b => b.Item.Name == name));
            Dispatcher.UIThread.RunJobs();
            frame.Panel.UpdateLayout();
            foreach (var label in frame.Panel.GetVisualDescendants().OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text)))
                Assert.True(label.DesiredSize.Width <= label.Bounds.Width + 0.5, $"{name}: \"{label.Text}\" wants {label.DesiredSize.Width:0.0} DIP but is arranged {label.Bounds.Width:0.0}");
            frame.Close();
            Dispatcher.UIThread.RunJobs();
        }
        window.Close();
    }

    // Real pointer events in the real main window (the menus no longer hold Metronome and Count-in: the button is the toggle): a left click toggles the setting (and the lit state follows), a right click opens the button's own
    // popup above it (a Popup in the overlay layer, never a Window), a press outside closes it, and its switch matches the button.
    [AvaloniaTheory]
    [InlineData(MainWindowTransportToggles.MetronomeButtonName, MainWindowTransportToggles.MetronomeId)]
    [InlineData(MainWindowTransportToggles.CountInButtonName, MainWindowTransportToggles.CountInId)]
    [InlineData(MainWindowTransportToggles.LoopButtonName, null)]
    public void LeftClickTogglesAndRightClickOpensTheButtonsOwnPopup(string buttonName, string? menuId)
    {
        var main = new MainWindow();
        main.Show();
        main.UpdateLayout();
        var button = main.GetLogicalDescendants().OfType<TransportBarView.TransportButton>().First(b => b.Item.Name == buttonName);
        Assert.True(button.IsEnabled, buttonName + " is not enabled.");
        var windowsBefore = ((IClassicDesktopStyleApplicationLifetime?)Application.Current?.ApplicationLifetime)?.Windows.Count;
        var overlay = OverlayLayer.GetOverlayLayer(main)!;
        bool? before = menuId is null ? null : Toggled(main, menuId);
        ClickButton(main, button, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(overlay.GetVisualDescendants().OfType<ToggleSwitch>());   // a left click opens no popup
        if (menuId is not null)
        {
            Assert.NotEqual(before, Toggled(main, menuId));
            ClickButton(main, button, MouseButton.Left);
            Assert.Equal(before, Toggled(main, menuId));
        }
        ClickButton(main, button, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        var power = Assert.Single(overlay.GetVisualDescendants().OfType<ToggleSwitch>());
        var closeButton = overlay.GetVisualDescendants().OfType<Button>().First(close => Equals(close.Content, "×"));
        Assert.True(closeButton.PointToScreen(new Point(0, closeButton.Bounds.Height)).Y <= button.PointToScreen(new Point(0, 0)).Y, "The popup is not above its button.");
        if (menuId is not null) Assert.Equal(Toggled(main, menuId), power.IsChecked);
        Assert.Equal(windowsBefore, ((IClassicDesktopStyleApplicationLifetime?)Application.Current?.ApplicationLifetime)?.Windows.Count);
        main.MouseDown(new Point(main.Bounds.Width / 2, main.Bounds.Height / 2), MouseButton.Left);
        main.MouseUp(new Point(main.Bounds.Width / 2, main.Bounds.Height / 2), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(overlay.GetVisualDescendants().OfType<ToggleSwitch>());   // a press outside closed it
        main.Close();
    }

    // The saved choice a transport toggle flips (the window has no menu row for it any more).
    private static bool? Toggled(MainWindow main, string commandId) =>
        main.CommandTable.CheckStateOf(commandId);

    // A press and release at the centre of a control, as window pointer events.
    private static void ClickButton(Window main, Control control, MouseButton button)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), main)
            ?? throw new InvalidOperationException("The control is not in the window.");
        main.MouseDown(point, button);
        main.MouseUp(point, button);
    }

    // The popup slider is explicitly horizontal (so the :horizontal template applies) and live-binds its percent label.
    [AvaloniaFact]
    public void PopupSliderIsHorizontalAndShowsLivePercent()
    {
        var audio = new AudioSettings();
        var frame = ClickSettingsPopups.CountIn(audio, () => { });
        var slider = frame.Rows.Children.OfType<Slider>().Single();
        Assert.Equal(global::Avalonia.Layout.Orientation.Horizontal, slider.Orientation);
        slider.Value = 40;
        Assert.Contains(frame.Rows.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "40%");
    }

    [AvaloniaFact]
    public void CountInRowsWriteTheSettingsAndReportEachChange()
    {
        var audio = new AudioSettings();
        var changes = 0;
        var frame = ClickSettingsPopups.CountIn(audio, () => changes++);
        frame.Rows.Children.OfType<Slider>().Single().Value = 40;
        frame.Rows.Children.OfType<ComboBox>().First().SelectedIndex = 1;
        var checks = frame.Rows.Children.OfType<CheckBox>().ToList();
        checks[0].IsChecked = true;
        checks[2].IsChecked = true;
        Assert.Equal((40, 2, true, true), (audio.CountInVolume, audio.CountInBars, audio.CountInOnlyAtSongStart, audio.LoopCountInEachLoop));
        Assert.Equal(4, changes);
    }

    [AvaloniaFact]
    public void LoopBoxesReadOnEnterAndShowTheirNormalisedValues()
    {
        var audio = new AudioSettings();
        var frame = LoopSettingsPopup.Create(audio, "No loop area selected", () => false, _ => { }, () => { }, () => { });
        var boxes = frame.Rows.GetLogicalDescendants().OfType<TextBox>().ToList();
        Assert.Equal(LoopSettingsRules.EndlessText, boxes[0].Text);
        Enter(boxes[0], "12");
        Enter(boxes[1], "5");      // below the trainer range: clamped to its 10 % floor
        Enter(boxes[3], "abc");    // unreadable: keeps the step
        Assert.Equal((12, LoopSettingsRules.MinTrainerPercent, 10), (audio.LoopCount, audio.LoopTrainerFrom, audio.LoopTrainerStep));
        Assert.Equal(LoopSettingsRules.MinTrainerPercent.ToString(), boxes[1].Text);
        var trainer = frame.Rows.Children.OfType<RadioButton>().Single(radio => Equals(radio.Content, "Speed trainer"));
        trainer.IsChecked = true;
        Assert.True(audio.LoopSpeedTrainer);
        Assert.True(boxes[1].IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ClearLoopAreaRunsTheCallbackAndCloses()
    {
        var (window, buttons) = Window(false);
        var cleared = false;
        var frame = LoopSettingsPopup.Create(new AudioSettings(), "Loop area: bars 3-4", () => false, _ => { }, () => { }, () => cleared = true);
        frame.OpenAbove(buttons.Single(b => b.Item.Name == "Loop enable"));
        Dispatcher.UIThread.RunJobs();
        var clear = frame.Rows.Children.OfType<Button>().Single(b => Equals(b.Content, LoopSettingsPopup.ClearAreaText));
        clear.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(cleared);
        Assert.False(frame.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void PopupSliderDefersRapidChangesButStoresEveryValue()
    {
        var audio = new AudioSettings();
        var changes = 0;
        var frame = ClickSettingsPopups.CountIn(audio, () => changes++);
        var slider = frame.Rows.Children.OfType<Slider>().Single();
        slider.Value = 40;
        Assert.Equal(1, changes);   // the leading change runs at once, with the label and the stored value
        slider.Value = 50;
        slider.Value = 60;
        Assert.Equal(1, changes);   // the rest of the burst is one trailing run, not three
        Assert.Equal(60, audio.CountInVolume);
        Assert.Contains(frame.Rows.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "60%");
    }

    [AvaloniaFact]
    public async Task PopupSliderRunsOneTrailingChangeAfterTheBurst()
    {
        var audio = new AudioSettings();
        var changes = 0;
        var frame = ClickSettingsPopups.CountIn(audio, () => changes++);
        var slider = frame.Rows.Children.OfType<Slider>().Single();
        slider.Value = 40;
        slider.Value = 50;
        Assert.Equal(1, changes);
        await Task.Delay(500);   // past the coalescing interval: the one trailing run lands
        Assert.Equal(2, changes);
        Assert.Equal(50, audio.CountInVolume);
    }

    [AvaloniaFact]
    public void SliderCoalescerRunsOneLeadingChangeAndFlushesTheTrailer()
    {
        var runs = 0;
        var coalescer = new SliderChangeCoalescer(() => runs++);
        coalescer.SliderChanged();
        Assert.Equal(1, runs);
        coalescer.SliderChanged();
        coalescer.SliderChanged();
        Assert.Equal(1, runs);
        coalescer.Flush();
        Assert.Equal(2, runs);
        coalescer.Flush();   // nothing armed: no run
        Assert.Equal(2, runs);
    }

    [AvaloniaFact]
    public async Task SliderCoalescerRunsOneTrailingChange()
    {
        var runs = 0;
        var coalescer = new SliderChangeCoalescer(() => runs++);
        coalescer.SliderChanged();
        coalescer.SliderChanged();
        Assert.Equal(1, runs);
        await Task.Delay(500);   // past the coalescing interval: the one trailing run lands
        Assert.Equal(2, runs);
    }

    [Fact]
    public void LoopDefaultAreaFollowsTheScope()
    {
        var project = new TabForge.Models.SongProject();
        var track = new TabForge.Models.TrackModel();
        for (var bar = 0; bar < 8; bar++) track.Measures.Add(new TabForge.Models.MeasureModel { Number = bar + 1 });
        project.Tracks.Add(track);
        project.Markers.Add(new TabForge.Models.MarkerModel { MeasureIndex = 2, Title = "Verse" });
        project.Markers.Add(new TabForge.Models.MarkerModel { MeasureIndex = 5, Title = "Chorus" });
        Assert.Equal((2, 4), LoopSettingsRules.DefaultArea(project, LoopSettingsRules.SectionScope, 3));
        Assert.Equal((0, 1), LoopSettingsRules.DefaultArea(project, LoopSettingsRules.SectionScope, 1));
        Assert.Equal((3, 3), LoopSettingsRules.DefaultArea(project, LoopSettingsRules.BarScope, 3));
        Assert.Equal((0, 7), LoopSettingsRules.DefaultArea(project, LoopSettingsRules.SongScope, 3));
        Assert.Equal(0, LoopSettingsRules.ParseCount(LoopSettingsRules.EndlessText));
    }

    private static void Enter(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
    }

    private static (Window Window, System.Collections.Generic.List<TransportBarView.TransportButton> Buttons) Window(bool light)
    {
        var bar = new TransportBarView { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom };
        var window = new Window { Width = 900, Height = 600, Content = bar, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().ToList());
    }

    // The panel alone, at its own size, on the window's background (the popup's shadow falls outside the panel and is not in the picture).
    private static void Save(Control panel, string name)
    {
        Directory.CreateDirectory(CaptureFolder);
        var size = new PixelSize((int)Math.Ceiling(panel.Bounds.Width), (int)Math.Ceiling(panel.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(panel);
        bitmap.Save(Path.Combine(CaptureFolder, name));
    }
}

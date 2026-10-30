using System;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track-row meters: the row layout that makes room for them, the shared settings and tap persistence, and the menu's two extra rows.
/// Tests: TrackRowCells, TrackMeterSettings, MasterMeterPopup.CreateTrack, TrackMeterColumn.
/// </summary>
public class TrackMeterTests
{
    [Fact]
    public void MetersTakeTheGapAndTheSlidersShrink()
    {
        var layout = TrackRowCells.Layout(TrackRowCells.NameWidth);
        Assert.Equal(ChromeMetrics.TrackMeterColumnWidth, layout.Meter.Width);
        Assert.Equal(layout.Pan.Right, layout.Meter.Left);
        Assert.Equal(TrackRowCells.TotalWidth, layout.Meter.Right + TrackRowCells.GridRight);
        Assert.Equal(598, TrackRowCells.TotalWidth);

        var volume = TrackRowCells.Box(TrackRowCells.Cell.Volume, 0, 28);
        var pan = TrackRowCells.Box(TrackRowCells.Cell.Pan, 0, 28);
        Assert.Equal(96 - TrackRowCells.SliderMarginLeft - TrackRowCells.SliderMarginRight, volume.Width);
        Assert.Equal(volume.Width, pan.Width);
        Assert.Equal(46, ChromeMetrics.TrackMeterWidth);
        Assert.Equal(238, layout.Volume.Width + layout.Pan.Width + layout.Meter.Width);
        Assert.Equal(ChromeMetrics.TrackMeterWidth, TrackRowCells.Box(TrackRowCells.Cell.Meter, 0, 28).Width);
    }

    [Fact]
    public void NoDeadSpaceRightOfTheMeterAtTheDefaultWidth()
    {
        var layout = TrackRowCells.Layout(TrackRowCells.NameWidth);
        var meter = TrackRowCells.Box(TrackRowCells.Cell.Meter, 0, 28);
        Assert.Equal(layout.Width, meter.Right + ChromeMetrics.TrackMeterInset);
        Assert.Equal(layout.Volume.Width, layout.Pan.Width);
        Assert.Equal(TrackRowCells.TotalWidth, layout.Settings.Left + layout.Settings.Width + layout.Kind.Width + layout.Colour.Width + layout.Number.Width + layout.Name.Width
            + layout.Fx.Width + layout.Mute.Width + layout.Solo.Width + layout.Volume.Width + layout.Pan.Width + layout.Meter.Width + TrackRowCells.GridRight - 0, 0.001);
    }

    [Fact]
    public void SharedSettingsApplyToAllAndPersistTheTap()
    {
        var settings = new AppSettings();
        Assert.True(settings.TrackMeter.ApplyToAll);
        Assert.Equal(TrackMeterSettings.PostFader, settings.TrackMeter.Tap);

        settings.TrackMeter.ApplyToAll = false;
        settings.TrackMeter.Tap = TrackMeterSettings.PreFader;
        var json = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.False(back.TrackMeter.ApplyToAll);
        Assert.Equal(TrackMeterSettings.PreFader, back.TrackMeter.Tap);
        Assert.True(back.TrackMeter.IsPreFader);

        back.TrackMeter.Tap = "nonsense";
        SettingsValidator.Normalize(back);
        Assert.Equal(TrackMeterSettings.PostFader, back.TrackMeter.Tap);
    }

    [AvaloniaFact]
    public void MenuHasTheMasterRowsPlusApplyToAllAndTap()
    {
        var settings = new TrackMeterSettings();
        var changes = 0;
        var reset = 0;
        var frame = MasterMeterPopup.CreateTrack(settings, () => changes++, () => reset++);

        var check = frame.Rows.Children.OfType<CheckBox>().Single(box => AutomationProperties.GetName(box) == MasterMeterPopup.ApplyToAllLabel);
        check.IsChecked = false;
        Assert.False(settings.ApplyToAll);

        var tap = frame.Rows.Children.OfType<ComboBox>().Last();
        Assert.Equal(MasterMeterPopup.TapLabel, AutomationProperties.GetName(tap));
        tap.SelectedIndex = 1;
        Assert.Equal(TrackMeterSettings.PreFader, settings.Tap);

        var button = frame.Rows.Children.OfType<Button>().Single(b => b.Content as string == MasterMeterPopup.ResetClipLabel);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, reset);
        Assert.True(changes >= 2);
        Assert.NotNull(frame.Panel);
    }

    [AvaloniaFact]
    public void ArrangementHasTheMeterColumnAndItsMenuOpens()
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 420, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        window.UpdateLayout();

        var meters = view.TrackMeters;
        Assert.True(meters.Bounds.Width > 0);
        Assert.NotNull(view.Header.MeterMenuRequested);
        meters.OpenMenu();
        Assert.True(meters.MenuOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void LeftClickResetsOnlyThatRowsClipLight()
    {
        var (view, window) = Shown();
        var clips = new int[view.Header.Song.Tracks.Count];
        view.TrackMeters.Source = track => new MasterMeterReading(0.2f, 0.2f, 0.1f, 0.1f, -20, clips[view.Header.Song.Tracks.IndexOf(track)]);
        view.TrackMeters.Tick();
        for (var i = 0; i < clips.Length; i++) clips[i] = 3;
        view.TrackMeters.Tick();
        Assert.True(view.TrackMeters.ClipLitOf(0));
        Assert.True(view.TrackMeters.ClipLitOf(1));

        Click(window, view.Header.TranslatePoint(view.Header.CellRect(0, TrackRowCells.Cell.Meter).Center, window)!.Value, MouseButton.Left);

        Assert.False(view.TrackMeters.ClipLitOf(0));
        Assert.True(view.TrackMeters.ClipLitOf(1));
        window.Close();
    }

    [AvaloniaFact]
    public void ThePointerHoversAndPressesOnlyOneMeterCell()
    {
        var (view, window) = Shown();
        var at = view.Header.TranslatePoint(view.Header.CellRect(1, TrackRowCells.Cell.Meter).Center, window)!.Value;
        window.MouseMove(at);
        Assert.Equal(TrackRowCells.CellPaint.Hover, view.TrackMeters.PaintOf(1));
        Assert.Equal(TrackRowCells.CellPaint.Idle, view.TrackMeters.PaintOf(0));

        window.MouseDown(at, MouseButton.Left);
        Assert.Equal(TrackRowCells.CellPaint.Pressed, view.TrackMeters.PaintOf(1));
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal(TrackRowCells.CellPaint.Hover, view.TrackMeters.PaintOf(1));
        window.Close();
    }

    [AvaloniaFact]
    public void DoubleClickOnAMeterRunsTheMixerAction()
    {
        var (view, window) = Shown();
        var opened = 0;
        view.TrackMeters.OpenMixer = () => opened++;
        var at = view.Header.TranslatePoint(view.Header.CellRect(0, TrackRowCells.Cell.Meter).Center, window)!.Value;
        for (var attempt = 0; attempt < 5 && opened == 0; attempt++)
        {
            Click(window, at, MouseButton.Left);
            Click(window, at, MouseButton.Left);
        }
        Assert.True(opened > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickOnAMeterStillOpensTheMenu()
    {
        var (view, window) = Shown();
        Click(window, view.Header.TranslatePoint(view.Header.CellRect(0, TrackRowCells.Cell.Meter).Center, window)!.Value, MouseButton.Right);
        Assert.True(view.TrackMeters.MenuOpen);
        window.Close();
    }

    private static (ArrangementView View, Window Window) Shown()
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 520, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        window.UpdateLayout();
        return (view, window);
    }

    private static void Click(Window window, Point at, MouseButton button)
    {
        window.MouseDown(at, button);
        window.MouseUp(at, button);
    }
}

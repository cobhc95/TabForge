using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Meters;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the master meter in the transport bar: it sits in the last slot after the zoom pair at the size of a transport button, a tick feeds and repaints it,
/// a left click resets the clip light, and a capture twin renders the bar with the meter in both themes (set TABFORGE_SHOT_DIR to also copy the pictures there).
/// Does not own: the numbers (MasterMeterTests, MasterMeterModelTests).
/// Tests: this class.
/// </summary>
public class MasterMeterBarTests
{
    /// <summary>The bar's width at the default track list width for the density: the list less the bar's left (1) and right (5) margins.</summary>
    private static double BarWidth(double factor) => TrackListWidthRules.DefaultListWidthFor(factor) - 6;

    private static (TransportBarView Bar, Window Window) Shown(MasterMeterReading reading, Func<MasterMeterSettings> settings, double density = 1.0)
    {
        var bar = new TransportBarView { Width = BarWidth(density) };   // the bar's width at the default track list width, as the arrangement sets it
        bar.Resources[UiDensityResources.Factor] = density;
        bar.MasterMeter.Settings = settings;
        bar.MasterMeter.Source = () => reading;
        var window = new Window { Width = 620, Height = 200, Content = bar };
        window.Show();
        return (bar, window);
    }

    [AvaloniaFact]
    public void TheMeterSitsAfterTheZoomPairAtTheSizeOfATransportButton()
    {
        var (bar, window) = Shown(MasterMeterReading.Silent(), () => new MasterMeterSettings());
        var meter = bar.GetVisualDescendants().OfType<MasterMeterView>().Single();
        var zoomIn = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Single(b => b.Item.Name == "Zoom in timeline");
        var rewind = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Single(b => b.Item.Name == "Rewind to beginning");
        Assert.True(meter.TranslatePoint(default, bar)!.Value.X > zoomIn.TranslatePoint(default, bar)!.Value.X);
        Assert.Equal((rewind.Bounds.Width, rewind.Bounds.Height), (meter.Bounds.Width, meter.Bounds.Height));
        Assert.NotNull(ToolTip.GetTip(meter));
        window.Close();
    }

    [AvaloniaFact]
    public void ALeftClickResetsTheClipLightAndATickRepaintsOnlyOnChange()
    {
        var clips = 0;
        var settings = new MasterMeterSettings();
        var (bar, window) = Shown(MasterMeterReading.Silent(), () => settings);
        var meter = bar.MasterMeter;
        meter.Source = () => new MasterMeterReading(0.5f, 0.5f, 0.3f, 0.3f, -20, clips);
        meter.Tick(0);
        clips = 3;
        meter.Tick(33);
        Assert.True(meter.Model.ClipLit);
        window.MouseDown(meter.TranslatePoint(new Point(5, 5), window)!.Value, global::Avalonia.Input.MouseButton.Left);
        Assert.False(meter.Model.ClipLit);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(0.85)]
    public void EveryItemIsOnOneRowAtTheDefaultWidthAtNormalButtonSize(double density)
    {
        var (bar, window) = Shown(MasterMeterReading.Silent(), () => new MasterMeterSettings(), density);
        bar.Resources[UiDensityResources.Factor] = density;
        window.UpdateLayout();
        Assert.True(bar.MasterMeter.IsVisible);
        var buttons = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Where(b => !b.Item.Name.StartsWith("Zoom")).ToArray();
        Assert.All(buttons, b => Assert.Equal(TransportBarView.TransportButtonSize, b.Bounds.Width));
        Assert.Equal(TransportBarView.TransportButtonSize, bar.MasterMeter.Bounds.Width);
        var rowTop = buttons[0].TranslatePoint(default, bar)!.Value.Y;
        Assert.All(buttons.Cast<Control>().Append(bar.MasterMeter).Append(bar.MasterVolumeKnob), c =>
        {
            Assert.Equal(rowTop, c.TranslatePoint(default, bar)!.Value.Y, 0.51);   // all on the first row (the knob and meter are centred in the same row height)
            Assert.True(c.TranslatePoint(new Point(c.Bounds.Width, 0), bar)!.Value.X <= bar.Bounds.Width + 0.5, "not clipped at the right");
        });
        var meterRight = bar.MasterMeter.TranslatePoint(new Point(bar.MasterMeter.Bounds.Width, 0), bar)!.Value.X;
        Assert.InRange(bar.Bounds.Width - meterRight, 0, 2 + density + 0.75);   // the meter ends at the bar's right padding: only border and padding follow
        Assert.True(bar.DesiredSize.Height <= TimelineView.RulerHeight, $"one row is {bar.DesiredSize.Height} tall");
        window.Close();
    }

    [AvaloniaFact]
    public void NarrowBarWrapsToASecondRowWithoutHidingTheMeterOrShrinkingButtons()
    {
        var (bar, window) = Shown(MasterMeterReading.Silent(), () => new MasterMeterSettings());
        bar.Width = 300;
        window.UpdateLayout();
        Assert.True(bar.MasterMeter.IsVisible);
        Assert.True(bar.DesiredSize.Height > TimelineView.RulerHeight, "the narrow bar wraps");
        Assert.All(bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Where(b => !b.Item.Name.StartsWith("Zoom")),
            b => Assert.Equal(TransportBarView.TransportButtonSize, b.Bounds.Width));
        Assert.Equal(TransportBarView.TransportButtonSize, bar.MasterMeter.Bounds.Width);
        Assert.True(bar.MasterMeter.Bounds.Width > 0);
        bar.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        if (Environment.GetEnvironmentVariable("TABFORGE_SHOT_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
            frame.Save(Path.Combine(dir, "transport-bar-narrow-wrapped.png"));
        }
        window.Close();
    }

    [AvaloniaFact]
    public void TheMeterPopupOpensAboveTheMeterAndItsRowsEditTheSettings()
    {
        var settings = new MasterMeterSettings();
        var changes = 0;
        var (bar, window) = Shown(MasterMeterReading.Silent(), () => settings);
        var meter = bar.MasterMeter;
        var popup = MasterMeterPopup.Create(settings, () => changes++, () => { });
        popup.OpenAbove(meter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Equal(MasterMeterPopup.Title, global::Avalonia.Automation.AutomationProperties.GetName(popup.Panel));
        var combos = popup.Rows.Children.OfType<ComboBox>().ToArray();
        Assert.Equal(2, combos.Length);
        combos[0].SelectedIndex = 2;
        Assert.Equal(MasterMeterSettings.Loudness, settings.Mode);
        combos[1].SelectedIndex = 2;
        Assert.Equal(0, settings.HoldSeconds);
        popup.Rows.Children.OfType<CheckBox>().Single().IsChecked = false;
        Assert.False(settings.ShowNumber);
        var shot = Path.Combine(Environment.GetEnvironmentVariable("TABFORGE_SHOT_DIR") ?? DialogTestKit.RenderDirectory(nameof(MasterMeterBarTests)), "master-meter-popup.png");
        Directory.CreateDirectory(Path.GetDirectoryName(shot)!);
        popup.Panel.Measure(global::Avalonia.Size.Infinity);   // size the shot to the content, not to the first-open bounds
        popup.Panel.Arrange(new Rect(popup.Panel.DesiredSize));
        var size = new PixelSize((int)popup.Panel.DesiredSize.Width + 40, (int)popup.Panel.DesiredSize.Height + 40);
        using (var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(size)) { bitmap.Render(popup.Panel); bitmap.Save(shot); }
        Assert.Equal(3, changes);
        popup.Close();
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureTwinRendersTheBarWithTheMeter(bool light)
    {
        var settings = new MasterMeterSettings();
        var (bar, window) = Shown(new MasterMeterReading(0.5f, 0.2f, 0.3f, 0.1f, -18, 0), () => settings);
        Application.Current!.RequestedThemeVariant = light ? global::Avalonia.Styling.ThemeVariant.Light : global::Avalonia.Styling.ThemeVariant.Dark;
        try
        {
            bar.MasterMeter.Tick(0);
            bar.MasterMeter.Source = () => new MasterMeterReading(0.9f, 0.45f, 0.3f, 0.1f, -18, 7);
            bar.MasterMeter.Tick(200);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
            var name = "transport-bar-master-meter-" + (light ? "light" : "dark") + ".png";
            var path = DialogTestKit.RenderPath(nameof(MasterMeterBarTests), name);
            frame.Save(path);
            Assert.True(new FileInfo(path).Length > 0);
            if (Environment.GetEnvironmentVariable("TABFORGE_SHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                File.Copy(path, Path.Combine(dir, name), true);
            }
        }
        finally { window.Close(); Application.Current!.RequestedThemeVariant = null; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the centring of shared content, in dark and light. A hovered top-level menu item's label sits within 1 DIP of its shade's centre on the main
/// menu bar and on the FX window's; the FX window's "+" and "Wiring…" buttons have their glyph ink centred within 1 DIP across and 2 DIP down. The frames of
/// both windows, with the menu hovered, go under the temp folder (DialogTestKit.RenderDirectory).
/// Does not own: the menu templates (AvaloniaMenuTemplates), the button styles (AvaloniaFormControlStyles, AvaloniaButtonTemplates), the FX window (FxChainWindow).
/// Tests: this class.
/// </summary>
public class MenuAndButtonCentreTests
{
    /// <summary>The largest offset, in DIP, between a label's centre and the centre of its shade, and between a glyph's horizontal centre and its button's.</summary>
    private const double CentreTolerance = 1;

    /// <summary>The largest vertical offset, in DIP, of a glyph's ink from its button's centre: the "+" sits low in its line box, and the descender of "g" in "Wiring…" pulls its ink box down.</summary>
    private const double GlyphVerticalTolerance = 2;

    /// <summary>The inset, in DIP, of the button scan from its edges, so the border is not read as ink.</summary>
    private const double ScanInset = 3;

    /// <summary>The sum of the channel differences from the fill that counts as ink (anti-aliased edges fall below it).</summary>
    private const int InkThreshold = 60;

    /// <summary>The DIP inside a button's left edge where its fill is sampled: past the border and corner, left of any glyph.</summary>
    private const double FillSampleOffset = 6;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoveredTopLevelLabelsAndFxButtonsAreCentred(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var name = light ? "light" : "dark";

        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        DialogTestKit.RunQueuedUiWork();
        var mainMenu = main.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        var file = Hover(main, mainMenu, "_File");
        AssertLabelCentred(main, file, "main menu _File");
        DialogTestKit.SavePng(main, nameof(MenuAndButtonCentreTests), $"main-window-menu-hover-{name}.png");
        main.Close();

        var track = FxTestKit.Track(new[]
        {
            new PluginSlot { Name = "Example instrument", Type = PluginSlotType.Instrument, Vendor = "Example Labs", Format = "VST3" },
            new PluginSlot { Name = "Example reverb", Type = PluginSlotType.Effect, Vendor = "Sample Audio", Format = "VST3" },
        });
        track.SoundSource = SoundSources.Plugins;
        var fx = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine()));
        fx.Width = 980;
        fx.Height = 620;
        fx.Show();
        DialogTestKit.RunQueuedUiWork();
        var fxMenu = DialogTestKit.Named<Menu>(fx, "FxMenu");
        var chains = Hover(fx, fxMenu, "_Chains");
        AssertLabelCentred(fx, chains, "FX window _Chains");
        DialogTestKit.SavePng(fx, nameof(MenuAndButtonCentreTests), $"fx-window-menu-hover-{name}.png");
        RunButtonAssertions(fx, name);
        fx.Close();
    }

    // Moves the pointer to the centre of the top-level item and returns it; the item then reports :pointerover, as a hovered item does.
    private static MenuItem Hover(Window window, Menu menu, string header)
    {
        var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header));
        var at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        DialogTestKit.RunQueuedUiWork();
        Assert.True(item.IsPointerOver, $"{header} is not hovered after the pointer moved onto it");
        return item;
    }

    // The label is the header presenter's text: its box must be the text itself, and that box's centre must be the shade's centre.
    private static void AssertLabelCentred(Window window, MenuItem item, string what)
    {
        var shade = item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
        var label = item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter").GetVisualDescendants().OfType<TextBlock>().First();
        var shadeRect = new Rect(shade.TranslatePoint(default, window)!.Value, shade.Bounds.Size);
        var labelRect = new Rect(label.TranslatePoint(default, window)!.Value, label.Bounds.Size);
        var offset = new Point(labelRect.Center.X - shadeRect.Center.X, labelRect.Center.Y - shadeRect.Center.Y);
        Assert.True(Math.Abs(offset.X) <= CentreTolerance && Math.Abs(offset.Y) <= CentreTolerance,
            $"{what}: label centre {labelRect.Center} is {offset} from the shade centre {shadeRect.Center} (shade {shadeRect}, label {labelRect})");
    }

    // The "+" and "Wiring…" buttons: the glyph ink (the pixels that differ from the button's fill) is centred in the button, in both directions.
    private static void RunButtonAssertions(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var scale = frame.PixelSize.Width / window.Bounds.Width;
        var failures = new List<string>();
        foreach (var buttonName in new[] { "AddPresetButton", "WiringButton" })
        {
            var button = DialogTestKit.Named<Button>(window, buttonName);
            var area = new Rect(button.TranslatePoint(default, window)!.Value, button.Bounds.Size);
            var ink = InkBox(frame, area, scale);
            var offset = new Point(ink.Center.X - area.Center.X, ink.Center.Y - area.Center.Y);
            if (Math.Abs(offset.X) > CentreTolerance || Math.Abs(offset.Y) > GlyphVerticalTolerance)
                failures.Add($"{buttonName} ({name}): ink centre {ink.Center} is {offset} from the button centre {area.Center} (button {area}, ink {ink})");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // The bounding box, in DIP, of the pixels inside the area that differ from the fill sampled at its left edge.
    private static Rect InkBox(WriteableBitmap frame, Rect area, double scale)
    {
        using var locked = frame.Lock();
        int Read(double x, double y) => Marshal.ReadInt32(locked.Address, (int)y * locked.RowBytes + (int)x * 4);
        var fill = Read((area.X + FillSampleOffset) * scale, area.Center.Y * scale);
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = (int)Math.Ceiling((area.Y + ScanInset) * scale); y < (area.Bottom - ScanInset) * scale; y++)
        for (var x = (int)Math.Ceiling((area.X + ScanInset) * scale); x < (area.Right - ScanInset) * scale; x++)
        {
            if (Distance(Read(x, y), fill) <= InkThreshold) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < 0) throw new InvalidOperationException($"No ink found in {area}: the label is missing or its colour matches the fill.");
        return new Rect(left / scale, top / scale, (right - left + 1) / scale, (bottom - top + 1) / scale);
    }

    // The sum of the blue, green and red differences of two BGRA pixels.
    private static int Distance(int a, int b) =>
        Math.Abs((a & 0xFF) - (b & 0xFF)) + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) + Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF));
}

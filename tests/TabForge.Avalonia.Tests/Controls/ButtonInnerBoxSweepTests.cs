using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// No button paints a box behind its label: for every button class and look in every state, the pixel just inside the label's text box
/// (outside the glyphs) equals the pixel in the button's padding.
/// Owns: the all-buttons inner-box sweep. Does not own: the button styles (AvaloniaFormControlStyles, AvaloniaLooks). Tests: this class.
/// </summary>
public class ButtonInnerBoxSweepTests
{
    private static readonly string[] States = { "normal", ":pointerover", ":pressed", ":focus-visible", "default", "disabled" };

    // The padding sample sits this far inside the button's left edge, past its border.
    private const double PaddingOffset = 6;

    [AvaloniaFact]
    public void NoButtonPaintsABoxBehindItsLabel()
    {
        var kinds = new List<(bool Toggle, string? Class)> { (false, null), (true, null), (false, "Button"), (false, "AccentButton"),
            (false, "PrefsActionButton"), (false, "PrefsActionSecondary"), (false, "PrefsActionReset"), (false, "StepButton"), (true, "MixerToggle"), (true, "PrefsMoreOptions") };
        foreach (var look in LookTable.Looks.Values.Where(l => l.Kind is "Button" or "ToggleButton"))
            kinds.Add((look.Kind == "ToggleButton", look.Name));
        var failures = new List<string>();
        foreach (var (toggle, buttonClass) in kinds)
            foreach (var state in States)
                if (InnerBox(toggle, buttonClass, state) is { } detail) failures.Add($"{(toggle ? "ToggleButton" : "Button")}.{buttonClass ?? "(none)"} {state}: {detail}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // Null when the label box shows the button fill; otherwise both pixels.
    private static string? InnerBox(bool toggle, string? buttonClass, string state)
    {
        ContentControl control = toggle ? new ToggleButton() : new Button { IsDefault = state == "default" };
        control.Content = "Cancel";
        control.Width = 140;
        control.Height = 34;
        control.HorizontalContentAlignment = HorizontalAlignment.Center;
        control.VerticalContentAlignment = VerticalAlignment.Center;
        control.IsEnabled = state != "disabled";
        if (buttonClass is not null) control.Classes.Add(buttonClass);
        var window = new Window { Width = 200, Height = 80, Content = new StackPanel { Children = { control, new Button { Content = "x" } } } };
        window.Show();
        if (state == ":focus-visible") control.Focus(NavigationMethod.Tab);
        if (state.StartsWith(':')) ((IPseudoClasses)control.Classes).Set(state, true);
        window.UpdateLayout();
        var text = control.GetVisualDescendants().OfType<TextBlock>().First();
        var textOrigin = text.TranslatePoint(new Point(0, 0), window)!.Value;
        var origin = control.TranslatePoint(new Point(0, 0), window)!.Value;
        using var frame = window.CaptureRenderedFrame()!;
        var padding = PixelAt(frame, origin.X + PaddingOffset, origin.Y + control.Bounds.Height / 2);
        var labelBox = PixelAt(frame, textOrigin.X + 1, textOrigin.Y + 1);
        window.Close();
        return padding == labelBox ? null : $"padding {padding:X8}, label box {labelBox:X8}";
    }

    // The 32-bit pixel at a point of a locked frame (BGRA, little-endian).
    private static int PixelAt(WriteableBitmap frame, double x, double y)
    {
        using var locked = frame.Lock();
        return Marshal.ReadInt32(locked.Address, (int)y * locked.RowBytes + (int)x * 4);
    }
}

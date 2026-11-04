using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Fx;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Active on headphones button states and the inactive-rule wash: active uses the accent tint,
//   inactive uses the translucent wash (not the opaque hover), no rule clears the fill, in both themes.
// Does not own: the popup, the rule matcher or the palette values (ThemeTests, FxActiveOnTests).
// Tests: this class.
public sealed class FxActiveOnButtonBrushTests
{
    private static ActiveOnRule Rule() => new() { DriverKind = ActiveOnDriverKind.Asio };

    [Fact]
    public void InactiveWashTokenIsTranslucentWithOriginalAlpha()
    {
        var dark = Palette.Get("FxInactiveRuleWash", light: false);
        var light = Palette.Get("FxInactiveRuleWash", light: true);
        Assert.Equal(new UiColor(0x0F, 0xFF, 0xFF, 0xFF), dark);
        // The light theme uses a faint dark tint (C4.json), so an inactive rule still reads on light backgrounds.
        Assert.Equal(new UiColor(0x0F, 0x00, 0x00, 0x00), light);
        var hover = AvaloniaPalette.Get("HoverSurface", light: false);
        Assert.Equal(0xFF, hover.A);
        Assert.NotEqual(hover, dark);
        var hoverLight = AvaloniaPalette.Get("HoverSurface", light: true);
        Assert.Equal(0xFF, hoverLight.A);
    }

    [AvaloniaFact]
    public void InactiveWashResolvesAsBrushInBothThemes()
    {
        var app = global::Avalonia.Application.Current!;
        foreach (var (variant, light) in new[] { (ThemeVariant.Dark, false), (ThemeVariant.Light, true) })
        {
            Assert.True(app.TryGetResource("FxInactiveRuleWash", variant, out var resource), $"FxInactiveRuleWash missing in {variant}");
            var brush = Assert.IsType<SolidColorBrush>(resource);
            Assert.Equal(AvaloniaPalette.ColorOf(Palette.Get("FxInactiveRuleWash", light)), brush.Color);
            Assert.Equal(0x0F, brush.Color.A);
        }
    }

    [AvaloniaFact]
    public void InactiveRuleUsesWashNotOpaqueHover()
    {
        var app = global::Avalonia.Application.Current!;
        foreach (var light in new[] { false, true })
        {
            app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            var holder = new FxActiveOnButton("TestWash");
            var window = new Window { Content = holder.Button };
            window.Show();
            try
            {
                holder.Update(Rule(), hasTarget: true, activeHere: false);
                Dispatcher.UIThread.RunJobs();
                var fill = Assert.IsType<SolidColorBrush>(holder.Button.Background);
                Assert.Equal(0x0F, fill.Color.A);
                Assert.Equal(AvaloniaPalette.ColorOf(Palette.Get("FxInactiveRuleWash", light)), AvaloniaPalette.ColorOf(new UiColor(fill.Color.A, fill.Color.R, fill.Color.G, fill.Color.B)));
                Assert.True(window.TryFindResource("FxInactiveRuleWash", window.ActualThemeVariant, out var expected));
                Assert.Equal(((ISolidColorBrush)expected!).Color, fill.Color);
                Assert.True(window.TryFindResource("HoverBrush", window.ActualThemeVariant, out var hover));
                Assert.NotEqual(((ISolidColorBrush)hover!).Color, fill.Color);
            }
            finally { window.Close(); }
        }
        app.RequestedThemeVariant = null;
    }

    [AvaloniaFact]
    public void ActiveRuleUsesSelectedTintAndNoRuleClearsFill()
    {
        var holder = new FxActiveOnButton("TestStates");
        var window = new Window { Content = holder.Button };
        window.Show();
        try
        {
            holder.Update(Rule(), hasTarget: true, activeHere: true);
            Dispatcher.UIThread.RunJobs();
            var active = Assert.IsType<SolidColorBrush>(holder.Button.Background);
            Assert.True(window.TryFindResource("SelectedTintBrush", window.ActualThemeVariant, out var tint));
            Assert.Equal(((ISolidColorBrush)tint!).Color, active.Color);

            holder.Update(null, hasTarget: true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(holder.HasRule);
            Assert.False(holder.Dot.IsVisible);
            if (holder.Button.Background is ISolidColorBrush cleared)
            {
                Assert.True(window.TryFindResource("FxInactiveRuleWash", window.ActualThemeVariant, out var wash));
                Assert.NotEqual(((ISolidColorBrush)wash!).Color, cleared.Color);
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TrackStringLayoutKeepsExactValuesWithoutSpeakerPadding()
    {
        Assert.Equal(24.0, TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringRowHeight);
        Assert.Equal(26.0, TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringSpeakerWidth);
        Assert.Equal(24.0, TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepWidth);
        Assert.Equal(20.0, TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepHeight);
        Assert.Equal(new Thickness(0), TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepButtonPadding);
        Assert.Equal(new Thickness(2, 0), TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackStringStepButtonGap);
        var field = typeof(TabForge.Avalonia.Views.Dialogs.DialogMetrics).GetField("TrackStringSpeakerPadding");
        Assert.True(field is null);
    }
}

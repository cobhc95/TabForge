using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the lit glow of the transport buttons: a lit button glows with its own family accent (orange metronome, green loop), and an unlit one has no glow.
/// Does not own: the drawing of the halo (TransportButton.DrawHalo) or the palette values (the theme tables).
/// </summary>
public class TransportGlowTests
{
    [AvaloniaFact]
    public void LitMetronomeGlowsOrangeAndLitLoopGlowsGreenAndUnlitHasNoGlow()
    {
        var bar = new TransportBarView();
        var window = new Window { Width = 900, Height = 200, Content = bar };
        window.Show();
        var buttons = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().ToList();
        var metronome = buttons.Single(button => button.Item.Name == "Metronome");
        var loop = buttons.Single(button => button.Item.Name == "Loop enable");

        Assert.Null(metronome.GlowAccent(false));

        metronome.Lit = true;
        loop.Lit = true;
        var orange = metronome.GlowAccent(false)!.Value;
        var green = loop.GlowAccent(false)!.Value;
        Assert.Equal(Palette.Get("PlaybarStopBorder", false), orange);
        Assert.True(orange.R > orange.G && orange.G > orange.B);
        Assert.Equal(Palette.Get("TransportLoopAccent", false), green);
        Assert.True(green.G > green.R && green.G > green.B);
        Assert.Equal(TransportGlowRules.BorderAlpha, TransportGlowRules.Border(orange).A / 255.0, 2);
    }

    [AvaloniaFact]
    public void LiveInputButtonGlowsRedWhileLiveInputIsOnAndNotWhenOff()
    {
        var on = false;
        var bar = new TransportBarView { LiveInputOn = () => on };
        var window = new Window { Width = 900, Height = 200, Content = bar };
        window.Show();
        var live = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Single(button => button.Item.Name == "Live input");
        Assert.Null(live.GlowAccent(false));

        on = true;
        bar.RefreshLiveInput();
        var red = live.GlowAccent(false)!.Value;
        Assert.Equal(Palette.Get("Danger", false), red);
        Assert.True(red.R > red.G && red.R > red.B);

        on = false;
        bar.RefreshLiveInput();
        Assert.Null(live.GlowAccent(false));
    }
}

// Owns: the colour a track's lane and header row are tinted with (the track colour, tamed, darkened, soft alpha; none in the light theme, where a stripe carries the colour), and the alpha of a muted track's dimmed ink.
// Does not own: drawing, the track colour storage, the tint and dim strengths (AppearanceSettings.TrackTintPercent and MutedTrackDimPercent, passed in by the caller).
// Tests: TabForge.Avalonia.Tests.ThemeSettingsBindingTests, TabForge.Avalonia.Tests.TimelineViewTests.
using System;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>The `ArrangementPanel.TintColour` rule on plain colours: same maths, so both apps tint a row alike. Pure; any thread.</summary>
public static class TrackRowTint
{
    private const double DarkThemeDarken = 0.35;
    private const double MaxStrength = 0.6;

    /// <summary>The width of the opaque track-colour stripe at the left of a light-theme header and lane.</summary>
    public const double StripeWidth = 3;

    /// <summary>How much more saturated the selected strip is than the track colour, in both themes.</summary>
    public const double SelectedStripSaturationBoost = 0.25;

    /// <summary>How much lighter the dark theme's selected strip is than the track colour.</summary>
    public const double SelectedStripDarkLift = 0.22;

    /// <summary>The least lightness the dark theme's selected strip keeps, so a very dark track still shows.</summary>
    public const double SelectedStripDarkMinLightness = 0.45;

    /// <summary>How much darker the light theme's selected strip is than the track colour.</summary>
    public const double SelectedStripLightDarken = 0.12;

    /// <summary>The least lightness the light theme's selected strip keeps.</summary>
    public const double SelectedStripLightMinLightness = 0.18;

    /// <summary>The tint as an RGBA colour with its alpha, or null when the theme is light, the track does not tint its row or <paramref name="tintPercent"/> is 0.</summary>
    public static Rgba? Of(TrackModel track, bool light, int tintPercent)
    {
        if (light || !track.TintRow || tintPercent <= 0 || !ColourHex.TryParse(track.ColorHex, out var parsed)) return null;
        var tamed = SectionColourPresenter.Tame(parsed);
        byte Dark(byte v) => (byte)Math.Round(v * (1 - DarkThemeDarken));
        var strength = Math.Clamp(tintPercent / 100.0, 0, MaxStrength);
        return new Rgba(Dark(tamed.R), Dark(tamed.G), Dark(tamed.B), (byte)Math.Round(255 * strength));
    }

    /// <summary>How much of the full ink the dimming removes at 100 percent (the muted-row opacity: 1 - 0.9 * dim).</summary>
    private const double MutedDimReach = 0.9;

    /// <summary>The alpha of a muted track's ink: 255 at 0 percent dimming, falling to 10 percent of it at 100.</summary>
    public static byte MutedAlpha(int dimPercent) => (byte)Math.Round(255 * (1 - MutedDimReach * Math.Clamp(dimPercent, 0, 100) / 100.0));

    /// <summary>Paints the tint over a row's background rectangle (nothing when <see cref="Of"/> has none).</summary>
    public static void Fill(IDrawContext d, TrackModel track, bool light, int tintPercent, double x, double y, double width, double height)
    {
        if (Of(track, light, tintPercent) is not { } t) return;
        d.Rect(t.ToUi(), null, x, y, width, height);
    }

    /// <summary>Paints the light theme's opaque track-colour stripe at the left of a row (nothing in the dark theme or for a colour that does not parse).</summary>
    public static void Stripe(IDrawContext d, TrackModel track, bool light, double x, double y, double height)
    {
        if (!light || !ColourHex.TryParse(track.ColorHex, out var parsed)) return;
        var tamed = SectionColourPresenter.Tame(parsed);
        d.Rect(tamed.ToUi(), null, x, y, StripeWidth, height);
    }

    /// <summary>The selected row's left-edge strip colour: the track's own colour, brighter and more saturated in the dark theme, darker and more saturated in the light theme, with a minimum lightness so a very dark track still shows. Tracks without a colour use the default track colour.</summary>
    public static UiColor SelectedStrip(TrackModel track, bool light)
    {
        if (!ColourHex.TryParse(track.ColorHex, out var parsed))
            ColourHex.TryParse(NamedColourSwatches.TrackDefaultColour, out parsed);
        var tamed = SectionColourPresenter.Tame(parsed);
        var baseColour = tamed.ToUi();
        var (hue, saturation, lightness) = ThemeColours.ToHsl(baseColour);
        var saturated = Math.Min(1, saturation + SelectedStripSaturationBoost);
        var level = light
            ? Math.Max(lightness - SelectedStripLightDarken, SelectedStripLightMinLightness)
            : Math.Max(lightness + SelectedStripDarkLift, SelectedStripDarkMinLightness);
        return HslToUi(baseColour, hue, saturated, Math.Clamp(level, 0, 1));
    }

    /// <summary>Paints the selected row's left-edge strip at <paramref name="x"/> over the full row height, slightly rounded, in the background below the cells.</summary>
    public static void DrawSelectedStrip(IDrawContext d, TrackModel track, bool light, double x, double y, double height)
    {
        d.Rect(SelectedStrip(track, light), null, x, y, ChromeMetrics.SelectedStripWidth, height, ChromeMetrics.SelectedStripCorner);
    }

    private static UiColor HslToUi(UiColor keepAlpha, double hue, double saturation, double lightness)
    {
        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        byte Channel(double t)
        {
            t = (t % 1 + 1) % 1;
            var v = t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            return (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        }
        return keepAlpha with { R = Channel(hue / 360 + 1.0 / 3), G = Channel(hue / 360), B = Channel(hue / 360 - 1.0 / 3) };
    }
}

using System;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the lit glow of a transport button: which palette accent each button glows with, and the alpha of its halo rings, its 1 px border,
//     its tinted fill and its pressed darkening. Every glow number lives here.
// Does not own: the palette values (the theme tables), the button's drawing (TransportBarView.TransportButton), the lit state (the session and the toggles).
// Tests: TabForge.Avalonia.Tests.TransportGlowTests.
// Threads: UI thread only; pure colour arithmetic, no state.
public static class TransportGlowRules
{
    /// <summary>How far the halo reaches outside the button, in DIPs.</summary>
    public const double HaloWidth = 8;

    /// <summary>The distance between two halo rings, in DIPs.</summary>
    public const double HaloStep = 2;

    /// <summary>The halo's alpha at the button's edge; it fades to nothing at <see cref="HaloWidth"/>.</summary>
    public const double HaloAlpha = 0.35;

    /// <summary>The alpha of the lit border.</summary>
    public const double BorderAlpha = 0.7;

    /// <summary>The alpha of the accent tint laid over the lit fill.</summary>
    public const double FillAlpha = 0.12;

    /// <summary>The accent tint over a lit button's fill while the pointer is on it: a clear step above <see cref="FillAlpha"/>, so the hover shows on a lit button.</summary>
    public const double LitHoverFillAlpha = 0.28;

    /// <summary>Light theme: how far a transport fill is mixed toward the surface, and the share of the lit tint kept (a paler fill, the outline keeps its colour).</summary>
    public const double LightFillLift = 0.6, LightTintScale = 0.5;

    /// <summary>The brightness factor of a pressed button's fill.</summary>
    public const double PressedShade = 0.85;

    /// <summary>The palette accent a button glows with when lit: its family colour (orange metronome, green loop, teal tuner and mixer, purple count-in, red record and live input).</summary>
    public static string AccentKeyOf(string itemName) => itemName switch
    {
        "Play or pause" => "PlaybarPlayBorder",
        "Record" => "Danger",
        "Metronome" => "PlaybarStopBorder",
        "Click and beat count-in" => "PlaybarRewindBorder",
        "Loop enable" => "TransportLoopAccent",
        "Snap" or "Global tuning" or "Mixer" => "TunerLitString",
        "Live input" => "Danger",
        _ => "Accent",
    };

    /// <summary>The accent at the halo ring <paramref name="distance"/> DIPs outside the button; the alpha falls off linearly to nothing at the reach.</summary>
    public static UiColor Halo(UiColor accent, double distance) =>
        accent with { A = Alpha(HaloAlpha * Math.Max(0, 1 - distance / HaloWidth)) };

    /// <summary>The accent for the lit border.</summary>
    public static UiColor Border(UiColor accent) => accent with { A = Alpha(BorderAlpha) };

    /// <summary>The accent laid over the fill of a lit button.</summary>
    public static UiColor Tint(UiColor accent, bool light = false) => accent with { A = Alpha(FillAlpha * (light ? LightTintScale : 1)) };

    /// <summary>The accent laid over the fill of a lit button under the pointer.</summary>
    public static UiColor HoveredTint(UiColor accent, bool light = false) => accent with { A = Alpha(LitHoverFillAlpha * (light ? LightTintScale : 1)) };

    private static byte Alpha(double opacity) => (byte)Math.Round(255 * opacity);
}

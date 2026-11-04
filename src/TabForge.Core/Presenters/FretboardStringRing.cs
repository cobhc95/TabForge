using System;

namespace TabForge.Presenters;

// Owns: how a sounding string looks over a note's life: the soft blur that starts wide on the strike and fades over the note's own length (quickly for a
//   palm mute), and the very slight sideways movement on top of it.
// Does not own: the drawing (FretboardArtist), the frame loop (InstrumentPanelView.StringVibration.cs) or the track properties strings (StringVibration).
// Tests: TabForge.Avalonia.Tests.FretboardStyleTests.
public static class FretboardStringRing
{
    /// <summary>The blur's half-height at the strike in DIPs, for each DIP of the vibration level's peak.</summary>
    public const double BlurPerPeak = 4.5;

    /// <summary>The share of the vibration level's peak the string really moves: a real string moves too fast to follow, so the blur carries the look.</summary>
    public const double MotionShare = 0.2;

    /// <summary>The blur's fade time as a share of the note's length, and its limits in milliseconds.</summary>
    private const double FadeShare = 0.55, MinFadeMs = 90, MaxFadeMs = 1500;

    /// <summary>How long a palm-muted string keeps ringing, in milliseconds.</summary>
    private const double MutedFadeMs = 70;

    /// <summary>The last part of a note, where the blur closes to nothing so it never stops abruptly: milliseconds, and its limit as a share of the note.</summary>
    private const double ReleaseMs = 120, ReleaseShare = 0.2;

    /// <summary>The blur's half-height in DIPs, <paramref name="elapsedMs"/> into a note of <paramref name="durationMs"/>; zero outside the note.</summary>
    public static double Blur(double elapsedMs, double durationMs, double peak, double rate = 1, bool muted = false)
    {
        if (elapsedMs < 0 || elapsedMs >= durationMs || peak <= 0) return 0;
        var fade = (muted ? MutedFadeMs : Math.Clamp(durationMs * FadeShare, MinFadeMs, MaxFadeMs)) / Math.Max(0.1, rate);
        var release = Math.Min(ReleaseMs, durationMs * ReleaseShare);
        var closing = release <= 0 ? 1 : Math.Clamp((durationMs - elapsedMs) / release, 0, 1);
        return peak * BlurPerPeak * Math.Exp(-elapsedMs / fade) * closing;
    }

    /// <summary>The string's very slight sideways offset in DIPs: the shared vibration curve at a small share of its peak.</summary>
    public static double Motion(double elapsedMs, double peak) => StringVibration.Offset(elapsedMs, peak * MotionShare);
}

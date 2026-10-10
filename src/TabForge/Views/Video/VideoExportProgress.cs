namespace TabForge.Views.Video;

/// <summary>One progress report of a video export: the overall fraction (0 to 1 over the audio render and the frames) and the text to show.</summary>
public readonly record struct VideoExportProgress(double Fraction, string Text)
{
    /// <summary>The share of the bar the audio render takes; the frames, which take far longer, get the rest.</summary>
    public const double AudioShare = 0.1;

    /// <summary>Progress of the audio render, <paramref name="audioFraction"/> being the render's own 0 to 1.</summary>
    public static VideoExportProgress Audio(double audioFraction) =>
        new(AudioShare * Math.Clamp(audioFraction, 0, 1), "Rendering audio…");

    /// <summary>Progress with <paramref name="done"/> of <paramref name="frames"/> frames of the range written (0 at the range's first frame,
    /// whatever its place in the song), <paramref name="elapsed"/> seconds into the frame phase.</summary>
    public static VideoExportProgress Frame(int done, int frames, double elapsed)
    {
        frames = Math.Max(1, frames);
        done = Math.Clamp(done, 0, frames);
        var text = $"Frame {done} / {frames}";
        if (done >= 8 && elapsed > 0.5)
        {
            var left = (int)Math.Ceiling(elapsed / done * (frames - done));
            text += $", about {left / 60}:{left % 60:00} left";
        }
        return new(AudioShare + (1 - AudioShare) * done / frames, text);
    }
}

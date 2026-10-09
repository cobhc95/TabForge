using TabForge.Models;

namespace TabForge.Services;

// Owns: the model rules of splitting a clip at a time, gluing adjacent clips of one lane back into one, and setting fades.
// Does not own: undo, selection and status text (ClipEditController) or fade playback (the engine's ClipPlayer).
// Tests: TestClipSplitGlueFades.
/// <summary>Clip cutting rules. Everything is non-destructive: the audio file is never rewritten, only the clips' offsets and lengths change.</summary>
public static class ClipSplitGlue
{
    /// <summary>The shortest piece a split may leave on either side, in seconds of timeline.</summary>
    public const double MinPieceSec = 0.01;
    private const double Tolerance = 0.005;

    /// <summary>True when <paramref name="sec"/> lies inside the clip with at least <see cref="MinPieceSec"/> on both sides.</summary>
    public static bool CanSplit(AudioClip clip, double sec) => sec >= clip.StartSec + MinPieceSec && sec <= clip.EndSec - MinPieceSec;

    /// <summary>
    /// Cuts <paramref name="clip"/> at the song time <paramref name="sec"/>: the clip keeps the first part, a new clip on the same lane takes the rest
    /// (same file, gain, pitch, speed, name; its offset advances by the source time of the first part). The fade-in stays on the first part and the
    /// fade-out moves to the second. Returns the second part, or null when the time is not inside the clip.
    /// </summary>
    public static AudioClip? Split(TrackModel track, AudioClip clip, double sec)
    {
        if (!CanSplit(clip, sec)) return null;
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var firstSource = (sec - clip.StartSec) * speed;
        var second = clip.Clone();
        second.StartSec = sec;
        // A looping clip is cut inside a pass: the second part starts at that spot of the media (never past its end).
        second.OffsetSec = clip.OffsetSec + (ClipLoop.Loops(clip) ? firstSource % ClipLoop.PeriodSec(clip) : firstSource);
        second.SourceLengthSec = clip.SourceLengthSec - firstSource;
        second.FadeInSec = 0;
        clip.SourceLengthSec = firstSource;
        clip.FadeOutSec = 0;
        track.AudioClips.Insert(track.AudioClips.IndexOf(clip) + 1, second);
        return second;
    }

    /// <summary>True when <paramref name="right"/> continues <paramref name="left"/> exactly: same lane, touching, same source, level, pitch and speed, next piece of the file.</summary>
    public static bool Continues(AudioClip left, AudioClip right) =>
        left.Lane == right.Lane && left.IsMidi == right.IsMidi && left.Muted == right.Muted
        && string.Equals(left.File, right.File, StringComparison.OrdinalIgnoreCase)
        && left.GainDb == right.GainDb && left.Pitch == right.Pitch && left.Speed == right.Speed
        && Math.Abs(left.EndSec - right.StartSec) <= Tolerance
        && Math.Abs(left.OffsetSec + left.SourceLengthSec - right.OffsetSec) <= Tolerance
        && (!left.IsMidi || left.Notes!.SequenceEqual(right.Notes!));

    /// <summary>The clips that join <paramref name="clip"/> into one: the chain of clips touching it on its lane, earliest first (the clip itself included).</summary>
    public static List<AudioClip> Chain(TrackModel track, AudioClip clip)
    {
        var chain = new List<AudioClip> { clip };
        for (var more = true; more;)
        {
            more = false;
            var before = track.AudioClips.FirstOrDefault(c => !chain.Contains(c) && Continues(c, chain[0]));
            if (before is not null) { chain.Insert(0, before); more = true; }
            var after = track.AudioClips.FirstOrDefault(c => !chain.Contains(c) && Continues(chain[^1], c));
            if (after is not null) { chain.Add(after); more = true; }
        }
        return chain;
    }

    /// <summary>
    /// Glues the clip with the pieces that continue it into the first piece (the others are removed; its fade-out becomes the last piece's).
    /// Returns the glued clip, or null when nothing continues it. Pieces that are not contiguous from the same file at the same speed and
    /// pitch are never merged (consolidating them into a new audio file is not available).
    /// </summary>
    public static AudioClip? Glue(TrackModel track, AudioClip clip)
    {
        var chain = Chain(track, clip);
        if (chain.Count < 2) return null;
        var first = chain[0];
        first.SourceLengthSec = chain.Sum(c => c.SourceLengthSec);
        first.FadeOutSec = chain[^1].FadeOutSec;
        foreach (var piece in chain.Skip(1)) track.AudioClips.Remove(piece);
        return first;
    }

    /// <summary>Sets a fade length, limited to the clip (the two fades never overlap): the other fade shrinks first.</summary>
    public static void SetFades(AudioClip clip, double? fadeIn, double? fadeOut)
    {
        var length = clip.LengthSec;
        if (fadeIn is { } i) { clip.FadeInSec = Math.Clamp(i, 0, length); clip.FadeOutSec = Math.Min(clip.FadeOutSec, length - clip.FadeInSec); }
        if (fadeOut is { } o) { clip.FadeOutSec = Math.Clamp(o, 0, length); clip.FadeInSec = Math.Min(clip.FadeInSec, length - clip.FadeOutSec); }
    }
}

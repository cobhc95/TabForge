namespace TabForge.KeyboardMode;

// Owns: the one value the keyboard view reads from playback: the song time, in milliseconds, at the playing position.
// Does not own: the playback clock or the compiled timeline (the playback session), the notes, the drawing.
// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests (a stub position).
public interface IKeyboardSongPosition
{
    /// <summary>The song time at the playing position, in milliseconds.</summary>
    double SongMs { get; }
}

/// <summary>A song position that stays where it is told (captures and tests).</summary>
public sealed class FixedKeyboardSongPosition(double songMs) : IKeyboardSongPosition
{
    public double SongMs { get; } = songMs;
}

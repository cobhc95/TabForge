using TabForge.KeyboardMode;

namespace TabForge.Avalonia.Transport;

// Owns: the keyboard view's song position read from one playback session: the playhead's song time in ms.
// Does not own: the playback clock (the session's transport), the notes or the drawing.
// Tests: TabForge.Avalonia.Tests.AvaloniaPlaybackSessionTests (the position follows the reported frame).
// Threads: UI thread, same as the session it reads.
public sealed class SessionKeyboardSongPosition(AvaloniaPlaybackSession session) : IKeyboardSongPosition
{
    public double SongMs => session.Transport.PlayheadMs;
}

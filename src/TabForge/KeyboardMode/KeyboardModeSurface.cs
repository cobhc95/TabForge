using System.Windows;

namespace TabForge.KeyboardMode;

/// <summary>What the falling-notes view offers the pane: an element to show and the frame step. The keyboard falling-notes view (KeyboardModeView) is the one implementation.</summary>
internal interface IKeyboardModeSurface
{
    /// <summary>The element the pane shows.</summary>
    FrameworkElement Element { get; }
    /// <summary>The notes to draw and the text to show when there are none.</summary>
    void SetSource(IKeyboardModeNoteSource source, string hint);
    /// <summary>One frame: the song time at the hit line, the loop, whether the song jumped and whether it is playing.</summary>
    void Update(double songMs, KeyboardModeLoop? loop, bool jumped, bool playing);
    /// <summary>The theme and how many milliseconds of song the view shows ahead of now.</summary>
    void SetLook(bool dark, double lookAheadMs);
    /// <summary>The keyboard run whose score, held keys, grades and wait state the view shows; null shows only the notes.</summary>
    void SetPlayAlong(KeyboardModeKeyboardSession? session);
    /// <summary>The hands filter, note names and finger numbers.</summary>
    void SetOptions(KeyboardHandsFilter hands, bool names, bool fingers);
    /// <summary>How far through the song the playing position is (0 to 1).</summary>
    void SetProgress(double fraction);
    /// <summary>The plus or minus key (or Ctrl + wheel) changed the look-ahead by this many seconds (the controller saves it).</summary>
    event Action<int>? LookAheadStep;
}

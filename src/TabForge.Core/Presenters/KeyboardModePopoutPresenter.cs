namespace TabForge.Presenters;

/// <summary>
/// The logic of the Keyboard mode pop-out: when it may open, its texts and the keys that toggle or leave full screen.
/// Owns: the open rule, the window title, the full screen button's text and tooltip, the not-open status text and the key rules.
/// Does not own: the window's layout and full screen state change (KeyboardMode/KeyboardModePopoutWindow), lending the keyboard view (KeyboardModePopout).
/// Tests: no dedicated test class.
/// </summary>
public static class KeyboardModePopoutPresenter
{
    public const string Title = "Keyboard mode: view";
    public const string FullScreenTooltip = "Full screen on or off: F11, and Esc leaves it";
    public const string NeedsModeStatus = "The keyboard pop-out needs Keyboard mode on";

    /// <summary>Opens only when none is open, Keyboard mode is on and the keyboard view is still in the pane.</summary>
    public static bool CanOpen(bool alreadyOpen, bool modeOn, bool keysDetached) => !alreadyOpen && modeOn && !keysDetached;

    public static string FullScreenButtonText(bool fullScreen) => fullScreen ? "Leave full screen" : "Full screen";

    /// <summary>F11 always toggles full screen; Esc toggles it only to leave it.</summary>
    public static bool TogglesFullScreen(bool isF11, bool isEscape, bool fullScreen) => isF11 || (isEscape && fullScreen);
}

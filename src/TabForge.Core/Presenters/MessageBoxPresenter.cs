namespace TabForge.Presenters;

/// <summary>The buttons a message box offers; the values match the usual MessageBoxButton values so the view casts.</summary>
public enum MessageBoxButtons { OK = 0, OKCancel = 1, YesNoCancel = 3, YesNo = 4 }

/// <summary>A message box answer; the values match the usual MessageBoxResult values so the view casts.</summary>
public enum MessageBoxAnswer { None = 0, OK = 1, Cancel = 2, Yes = 6, No = 7 }

/// <summary>The icon a message box shows; the values match the usual MessageBoxImage values so the view casts.</summary>
public enum MessageBoxIcon { None = 0, Error = 16, Question = 32, Warning = 48, Information = 64 }

/// <summary>The colour family of a message box icon; the view maps each tone to its theme brush.</summary>
public enum MessageBoxTone { Accent, Amber, Danger }

/// <summary>
/// The logic of the themed message box as plain data.
/// Owns: the button labels and answers, the default button, which button Esc presses, the close-box answer and the icon glyph and colour tone.
/// Does not own: building or showing the window (Views/ThemedMessageBox).
/// Tests: no dedicated test class.
/// </summary>
public static class MessageBoxPresenter
{
    public static (string Label, MessageBoxAnswer Answer)[] Buttons(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OKCancel => new[] { ("OK", MessageBoxAnswer.OK), ("Cancel", MessageBoxAnswer.Cancel) },
        MessageBoxButtons.YesNo => new[] { ("_Yes", MessageBoxAnswer.Yes), ("_No", MessageBoxAnswer.No) },
        MessageBoxButtons.YesNoCancel => new[] { ("_Yes", MessageBoxAnswer.Yes), ("_No", MessageBoxAnswer.No), ("Cancel", MessageBoxAnswer.Cancel) },
        _ => new[] { ("OK", MessageBoxAnswer.OK) },
    };

    /// <summary>The Enter button: the requested default, else the first button.</summary>
    public static MessageBoxAnswer DefaultAnswer(MessageBoxButtons buttons, MessageBoxAnswer requested) =>
        requested == MessageBoxAnswer.None ? Buttons(buttons)[0].Answer : requested;

    /// <summary>The Esc button: Cancel, or OK in an OK-only box.</summary>
    public static bool IsCancelButton(MessageBoxButtons buttons, MessageBoxAnswer answer) =>
        answer == MessageBoxAnswer.Cancel || (buttons == MessageBoxButtons.OK && answer == MessageBoxAnswer.OK);

    /// <summary>The close-box answer, following the system box: null when YesNo (no way out but a button), else OK or Cancel.</summary>
    public static MessageBoxAnswer? CloseAnswer(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.YesNo => null,
        MessageBoxButtons.OK => MessageBoxAnswer.OK,
        _ => MessageBoxAnswer.Cancel,
    };

    /// <summary>The icon's glyph (empty for none) and its colour tone.</summary>
    public static (string Glyph, MessageBoxTone Tone) Icon(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Warning => ("!", MessageBoxTone.Amber),
        MessageBoxIcon.Error => ("x", MessageBoxTone.Danger),
        MessageBoxIcon.Question => ("?", MessageBoxTone.Accent),
        MessageBoxIcon.Information => ("i", MessageBoxTone.Accent),
        _ => ("", MessageBoxTone.Accent),
    };
}

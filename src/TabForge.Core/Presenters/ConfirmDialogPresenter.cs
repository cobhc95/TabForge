// Owns: the confirm prompt's words that do not come from its caller: each button's tooltip and key hint, the "Show changes" link,
//       the remember box's tooltip, the scope heading, the window width and the clamping of a default option.
// Does not own: the question, the button texts the caller passes, the window, or what an answer means.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>Texts and sizes shared by the WPF confirm dialog and its Avalonia twins (SavePromptWindow, BarRangePromptWindow).</summary>
public static class ConfirmDialogPresenter
{
    public const string ScopeHeading = "Apply to";
    public const string HideDetails = "▾ Hide changes";

    /// <summary>The window width: a choice prompt needs room for its option labels.</summary>
    public static double WidthFor(bool hasChoices) => hasChoices ? 560 : 420;

    /// <summary>The button's tooltip (the caller's text or the save-prompt default) and the keys that answer it.
    /// A destructive question (<paramref name="defaultIsNo"/>) has no Y shortcut and puts Enter and Esc on No.</summary>
    public static string ToolTip(PromptButton button, string? text, bool defaultIsNo) =>
        (text ?? button switch { PromptButton.Yes => "Save the changes", PromptButton.No => "Discard the changes", _ => "Return without closing" })
        + button switch
        {
            PromptButton.Yes => defaultIsNo ? "" : "  (Y / Enter)",
            PromptButton.No => defaultIsNo ? "  (N / Enter / Esc)" : "  (N)",
            _ => "  (Esc)",
        };

    /// <summary>The details link: closed it names the number of changes.</summary>
    public static string DetailsLink(int count, bool open) => open ? HideDetails : $"▸ Show changes ({count})";

    public static string RememberToolTip(bool hasChoices) => hasChoices
        ? "Delete then does this directly. Turn the prompt back on in Settings > Editing > Safety > Ask what Delete does on bars."
        : "You can turn this warning back on in Settings → General → Safety.";
}

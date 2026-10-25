// Owns: the combined paste dialog's questions in display order, each option's label (the first marked recommended), the remember box's
//       words, the button texts, and the answers the picked options stand for.
// Does not own: the questions' titles and options (PasteQuestionInfo), the window, or the paste (PasteCommands.Paste through DocumentEdits).
// Tests: no dedicated test class.
using TabForge.Services;

namespace TabForge.Presenters;

public sealed class PasteOptionsPresenter
{
    public const string Title = "Paste";
    public const string RememberText = "Remember my choice";
    public const string RememberToolTip = "Stop asking this question; change it later in Preferences > Editing > Copy and paste.";
    public const string PasteToolTip = "Paste with these choices (Enter)";
    public const string CancelToolTip = "Cancel the paste (Esc)";

    public PasteOptionsPresenter(IEnumerable<PasteQuestion> questions) =>
        Questions = questions.Distinct().OrderBy(q => (int)q).ToList();

    /// <summary>The questions to show, each once, in design order.</summary>
    public IReadOnlyList<PasteQuestion> Questions { get; }

    public IReadOnlyList<string> OptionLabels(PasteQuestion question) =>
        PasteQuestionInfo.Options(question).Select((o, i) => o.Label + (i == 0 ? " (recommended)" : "")).ToList();

    /// <summary>The answers for the picked option index (negative = none, read as the recommended one) and remember box of each question shown.</summary>
    public PasteAnswers Answers(Func<PasteQuestion, int> chosen, Func<PasteQuestion, bool> remembered)
    {
        string? IdOf(PasteQuestion q) => Questions.Contains(q) ? PasteQuestionInfo.Options(q)[Math.Max(0, chosen(q))].Id : null;
        return PasteQuestionInfo.FromIds(IdOf, q => Questions.Contains(q) && remembered(q));
    }
}

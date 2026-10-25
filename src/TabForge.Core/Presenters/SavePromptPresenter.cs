using TabForge.Documents;

namespace TabForge.Presenters;

/// <summary>The button the person pressed in a Yes / No / Cancel prompt; closing the prompt any other way counts as Cancel.</summary>
public enum PromptButton { Yes, No, Cancel }

/// <summary>What a degraded save of an existing file does after the overwrite question.</summary>
public enum DegradedSaveChoice { SaveAsNewFile, Overwrite, DoNotSave }

/// <summary>The texts of one Yes / No / Cancel prompt.</summary>
public sealed record SavePrompt(string Title, string Message, string YesToolTip, string NoToolTip);

/// <summary>
/// The save, discard and cancel prompts: their texts and what each button means.
/// Owns: the save-changes question (one song, or the songs of a closing window), the overwrite question after an unexpected error, and the
///     mapping from the pressed button to the answer.
/// Does not own: the dialog (ThemedConfirmDialog), the save (DocumentSaveFlow) or the close sequencing (WindowCloseFlow, DocumentCloseFlow).
/// Tests: no dedicated test class.
/// </summary>
public static class SavePromptPresenter
{
    private const string Title = "TabForge";

    /// <summary>The save-changes question for one song.</summary>
    public static SavePrompt SaveChanges(string displayName) => new(Title, $"Save changes to {displayName}?", "Save the changes", "Discard the changes");

    /// <summary>The save-changes question while a window closes: the shown song, when it is the only unsaved one, is "the current project".</summary>
    public static SavePrompt SaveChangesOnWindowClose(string displayName, bool isShown, int unsavedCount) =>
        unsavedCount == 1 && isShown ? SaveChanges("the current project") : SaveChanges(displayName);

    /// <summary>Yes saves first, Cancel keeps the song open with its changes, No closes it without saving.</summary>
    public static DiscardAnswer Answer(PromptButton pressed) => pressed switch
    {
        PromptButton.Cancel => DiscardAnswer.Keep,
        PromptButton.Yes => DiscardAnswer.SaveFirst,
        _ => DiscardAnswer.Close,
    };

    /// <summary>After an unexpected error the song may be damaged in memory, so overwriting <paramref name="fileName"/> is opt-in.</summary>
    public static SavePrompt DegradedOverwrite(string fileName) => new(Title,
        $"TabForge hit an unexpected error earlier, so this song may be damaged in memory.\n\nSave it as a new file instead of overwriting {fileName}?\n\n" +
        $"Yes: save as a new file (recommended).\nNo: overwrite {fileName}.\nCancel: do not save.",
        "Save as a new file (recommended)", $"Overwrite {fileName}");

    public static DegradedSaveChoice DegradedChoice(PromptButton pressed) => pressed switch
    {
        PromptButton.Cancel => DegradedSaveChoice.DoNotSave,
        PromptButton.Yes => DegradedSaveChoice.SaveAsNewFile,
        _ => DegradedSaveChoice.Overwrite,
    };
}

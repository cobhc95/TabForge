// Owns: the words of the "Audio crosses this section" prompt (title, body, the two option cards, remember box and buttons) and the answer it gives.
// Does not own: finding the crossing clips or cutting them (SectionAudioCrossings), the stored setting (EditingSettings.SectionMoveAudio) or the window (Views/SectionAudioPromptWindow).
// Tests: TabForge.Avalonia.Tests.SectionAudioMoveTests.
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>What the prompt answered: cut the crossing audio and move the inside parts, or leave it in place; and whether to remember the choice.</summary>
public readonly record struct SectionAudioAnswer(bool Split, bool Remember);

public static class SectionAudioPromptPresenter
{
    public const string Title = "Audio crosses this section";
    public const string SplitTitle = "Split and move with the section";
    public const string SplitHint = "Cut the audio at the section edges; the inside part moves, the rest stays.";
    public const string LeaveTitle = "Leave the audio where it is";
    public const string LeaveHint = "Only the tab moves. Audio keeps its timing.";
    public const string RememberText = "Remember my choice";
    public const string RememberHint = "(change in Settings › Editing)";
    public const string CancelText = "Cancel move";
    public const string MoveText = "Move";
    public const string CancelToolTip = "Leave the section where it is";
    public const string MoveToolTip = "Move the section with the chosen audio handling";

    /// <summary>"Guitar has audio that starts or ends outside Verse (bars 5–8). What should happen to it?"; several tracks read "Guitar, Vocals have audio …".</summary>
    public static string Body(SongProject project, SectionAudioCrossing crossing)
    {
        var names = crossing.TrackNames(project);
        var section = string.IsNullOrWhiteSpace(crossing.SectionTitle) ? "this section" : crossing.SectionTitle;
        return $"{string.Join(", ", names)} {(names.Count == 1 ? "has" : "have")} audio that starts or ends outside {section} (bars {crossing.Start + 1}–{crossing.End}). What should happen to it?";
    }

    /// <summary>The answer without asking, when the setting remembers one; null when it asks.</summary>
    public static SectionAudioAnswer? Remembered(string? stored) => SectionAudioChoice.Normalize(stored) switch
    {
        SectionAudioChoice.Split => new SectionAudioAnswer(true, true),
        SectionAudioChoice.Leave => new SectionAudioAnswer(false, true),
        _ => null,
    };

    /// <summary>The value Remember stores for an answer.</summary>
    public static string Store(SectionAudioAnswer answer) => answer.Split ? SectionAudioChoice.Split : SectionAudioChoice.Leave;
}

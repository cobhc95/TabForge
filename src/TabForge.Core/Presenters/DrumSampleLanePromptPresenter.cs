using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the words of the drum-sample lane prompt (title, body, the two lane cards, remember box and buttons).
// Does not own: the window (Views/DrumSampleLanePromptWindow), the drop itself (DrumSampleDrop) or the stored lane choice (DrumSampleDropChoice).
// Tests: TabForge.Avalonia.Tests.DrumSampleDropTests.
public static class DrumSampleLanePromptPresenter
{
    public const string Title = "Add drum sample";
    public const string TabTitle = "Tab lane";
    public const string TabHint = "Drum notation in the tab bars: exact, editable and exportable. On a pitched track the rhythm lands on the lowest string.";
    public const string TabUnavailableHint = "This track has no tab lane (audio tracks hold clips only).";
    public const string MidiTitle = "MIDI lane";
    public const string MidiHint = "A MIDI clip on the track's take lanes: edit note by note in the piano roll, on the GM drum channel.";
    public const string RememberText = "Remember my choice";
    public const string RememberHint = "(change in Settings › Editing)";
    public const string CancelText = "Cancel";
    public const string OkText = "OK";
    public const string CancelToolTip = "Do not add the sample";
    public const string OkToolTip = "Add the sample to the chosen lane";

    /// <summary>The question text: the sample, the track under the pointer and the bar it lands on.</summary>
    public static string Body(DrumSampleDropQuestion question) =>
        $"'{question.SampleName}' ({question.Bars} bar{(question.Bars == 1 ? "" : "s")}) drops onto '{question.TrackName}' at bar {question.Bar}. Where should it land?";
}

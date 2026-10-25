using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

/// <summary>The question a whole-bar delete asks when audio or MIDI clips lie on the bars it removes.</summary>
public interface IBarDeleteQuestions
{
    /// <summary>True to go on deleting; <paramref name="text"/> names the tracks and clips affected.</summary>
    bool ContinueDeletingUnderClips(string text);
}

/// <summary>Takes the recommended answer (go on: undo restores the bars and clips) without asking.</summary>
public sealed class RecommendedBarDeleteAnswers : IBarDeleteQuestions
{
    public bool ContinueDeletingUnderClips(string text) => true;
}

// Owns: the model half of a cut or delete of whole bars over a range: the clip question before bars under clips go, removing the bars from
//   every track (markers, repeats and sections follow BarRangeEditor.Remove; the song keeps the bars its clips need), or clearing one track's
//   bars when only that track of several was cut, each as one undo step.
// Does not own: copying the bars (TimelineClips, ClipboardService), the clip question's window (IBarDeleteQuestions adapters), selection,
//   caret, loop and status text (the hosts: SectionEditFlow, the Avalonia MainWindowClipboard).
// Tests: no dedicated test class.
/// <summary>Whole-bar cut and delete over a bar range of an explicit document.</summary>
public static class WholeBarCut
{
    /// <summary>A cut of every track, or of the only track, removes the bars; a cut of one track of several clears them (its bars stay in line).</summary>
    public static bool RemovesBars(SongProject project, int onlyTrack) => onlyTrack < 0 || project.Tracks.Count <= 1;

    /// <summary>
    /// Removes bars <paramref name="first"/>..<paramref name="last"/> from every track as one edit, after asking when clips lie on them.
    /// Null when the user said no; otherwise the edit, whose value is the old-to-new bar mapping (null value: the song keeps its last bar).
    /// </summary>
    public static EditOutcome<int[]>? DeleteBars(IEditTarget document, int first, int last, IBarDeleteQuestions questions)
    {
        if (ClipDeleteImpact.Find(document.Project, new[] { (first, last) }) is { } impact && !questions.ContinueDeletingUnderClips(ClipDeleteImpact.Describe(impact)))
            return null;
        return document.Run<int[]>(project =>
        {
            var map = BarRangeEditor.Remove(project, first, last);
            if (map is not null) SongExtent.EnsureCoversClips(project);   // a clip past the song end extends it again (same undo step)
            return map;
        });
    }

    /// <summary>Empties bars <paramref name="first"/>..<paramref name="last"/> of track <paramref name="track"/> as one edit; the bars stay.</summary>
    public static EditOutcome ClearBars(IEditTarget document, int first, int last, int track, bool fillRests) =>
        document.Run(project => BarRangeGaps.Clear(project, first, last, track, fillRests));
}

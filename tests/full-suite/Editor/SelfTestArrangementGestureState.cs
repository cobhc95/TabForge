using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge;

/// <summary>The arrangement gestures' pending undo state, driven with a fake document (no window).</summary>
public static partial class SelfTest
{
    private sealed class GestureHost : IArrangementGestureHost
    {
        public GestureHost(DocumentSession document) => Document = document;
        public DocumentSession Document { get; }
    }

    private static void TestArrangementGestureState()
    {
        var document = DocumentSession.FromProject(DoSong(3), null);
        var state = new ArrangementGestureState(new GestureHost(document));

        // A mix gesture that changed something is one undo step; one that did not leaves none.
        state.MixEditStarting(); state.MixEditEnded();
        Check("gesture state: a mix gesture without a change stores no undo step", document.Undo.UndoCount == 0);
        state.MixEditStarting(); state.MixChanged(); document.Project.Tracks[0].Name = "Edited"; state.MixEditEnded();
        Check("gesture state: a mix gesture with a change stores one undo step", document.Undo.UndoCount == 1, document.Undo.UndoCount.ToString());
        state.MixEditEnded();
        Check("gesture state: a second end without a start does nothing", document.Undo.UndoCount == 1);

        // A track edit keeps its step only when the edit applied; Complete stores it once.
        state.BeginTrackEdit(() => false); state.CompleteTrackEdit();
        Check("gesture state: a refused track edit stores nothing", document.Undo.UndoCount == 1);
        state.BeginTrackEdit(() => { document.Project.Tracks[1].Name = "Renamed"; return true; });
        state.CompleteTrackEdit(); state.CompleteTrackEdit();
        Check("gesture state: an applied track edit stores one step, once", document.Undo.UndoCount == 2, document.Undo.UndoCount.ToString());

        // Drag snapshots are taken at the start and cleared by the read.
        Check("gesture state: no drag snapshot before a drag", state.TakeTrackSnapshot() is null && state.TakeSectionSnapshot() is null);
        state.TrackDragStarted(); state.SectionDragStarted();
        Check("gesture state: the track snapshot is returned once", state.TakeTrackSnapshot() is not null && state.TakeTrackSnapshot() is null);
        Check("gesture state: the section snapshot is returned once", state.TakeSectionSnapshot() is not null && state.TakeSectionSnapshot() is null);
        state.SectionDragStarted(); state.SectionDragCancelled();
        Check("gesture state: a cancelled section drag leaves no snapshot", state.TakeSectionSnapshot() is null);

        // The playing section: selects with the flag raised, reports a change once, forces a refresh on request.
        var marker = new MarkerModel();
        var selects = 0; var flagDuringSelect = false;
        void Select(MarkerModel m) { selects++; flagDuringSelect = state.SyncingPlayingSelection; }
        Check("gesture state: a new playing section reports a change", state.ShowPlayingSection(marker, false, Select) && selects == 1 && flagDuringSelect);
        Check("gesture state: the flag is lowered after selecting", !state.SyncingPlayingSelection);
        Check("gesture state: the same section is ignored", !state.ShowPlayingSection(marker, false, Select) && selects == 1);
        Check("gesture state: a forced refresh selects again without a change", !state.ShowPlayingSection(marker, true, Select) && selects == 2);
        Check("gesture state: no playing section selects nothing", state.ShowPlayingSection(null, false, Select) && selects == 2 && state.PlayingSection is null);

        // The fitted track count.
        Check("gesture state: the first track count is a change", state.TrackCountChanged(3));
        Check("gesture state: the same count is not", !state.TrackCountChanged(3));
        Check("gesture state: another count is", state.TrackCountChanged(4));
    }
}

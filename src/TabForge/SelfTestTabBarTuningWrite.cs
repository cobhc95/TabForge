using System.Windows;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Three older UI bugs: a background tab's dot and title, undoing a retune, and "Write notation" from a clip that fits nothing.</summary>
public static partial class SelfTest
{
    private static void DoTabBarFollowsDocumentCase()
    {
        var bar = new BrowserTabBar { Settings = new AppSettings().Tabs };
        var docs = new DocumentManager();
        bar.Bind(docs);
        var shown = docs.AddNew();
        var background = docs.Add(DocumentSession.Blank(), activate: false);
        background.Project.Title = "First name"; background.MarkClean();
        bar.Refresh();
        var items = LtField<System.Collections.ObjectModel.ObservableCollection<TabItemModel>>(bar, "_items")!;
        var item = items[1];
        var raised = new List<string>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        Check("tab bar: a clean background tab shows its name and no dot", item.Title == "First name" && item.DirtyVisibility == Visibility.Collapsed);
        background.Project.IsDirty = true;
        background.Project.Title = "Renamed";
        var followed = DoPumpUntil(() => raised.Contains("Title") && raised.Contains("DirtyVisibility"), 5);   // the bound tab is told, not just the getter right
        Check("tab bar: a background tab's dot and title follow its document without any rebuild of the bar", followed && ReferenceEquals(items[1], item) && ReferenceEquals(shown, items[0].Session));
        raised.Clear();
        background.MarkClean();
        Check("tab bar: saving a background tab clears its dot", DoPumpUntil(() => raised.Contains("DirtyVisibility"), 5) && item.DirtyVisibility == Visibility.Collapsed);
    }

    private static void DoRetuneUndoCase(MainWindow window)
    {
        var document = DoOpen(window, DoSong());
        var shift = new int[6];
        LtCall(window, "RetuneStrings", (object)Enumerable.Repeat(2, 6).ToArray());
        var retuned = document.TuningShift.All(o => o == 2);
        LtCall(window, "Undo_Click", window, new RoutedEventArgs());
        var undone = document.TuningShift.SequenceEqual(shift);
        LtCall(window, "Redo_Click", window, new RoutedEventArgs());
        var redone = document.TuningShift.All(o => o == 2);
        Check("retune: undo restores the tuning offset with the strings, redo brings it back", retuned && undone && redone, $"retuned {retuned}, undone {undone}, redone {redone}");
    }

    private static void DoWriteNotationCase()
    {
        ClipNote Note(int pitch) => new(0, 0.5, pitch, 90);
        AudioClip Clip(int pitch) => new() { StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2, Notes = new List<ClipNote> { Note(pitch) } };

        var document = DocumentSession.FromProject(DoSong(1, 4), null);
        var track = document.Project.Tracks[0];
        document.MarkClean();
        foreach (var m in track.Measures) m.Cells.Clear();   // short bars: the write pads them when it lands notes
        string Shape() => string.Join(',', document.Project.Tracks[0].Measures.Select(m => m.Cells.Count + "/" + m.Cells.Sum(c => c.Notes.Count)));
        var before = Shape();
        var nothing = DocumentEdits.Run(document, p => MidiClipToTab.Write(p, track, Clip(127), s => document.Playback.Clock.BarAt(p, s)) > 0);
        Check("write notation: a clip that fits nothing leaves the song exactly as it was", !nothing.Changed && Shape() == before && document.Undo.UndoCount == 0 && !document.Project.IsDirty);

        var fitting = Clip(track.PitchOf(2, 5));
        var written = DocumentEdits.Run(document, p => MidiClipToTab.Write(p, track, fitting, s => document.Playback.Clock.BarAt(p, s)) > 0);
        var after = Shape();
        Check("write notation: a clip that fits is one undo step that holds the padding and the notes", written.Changed && after != before && document.Undo.UndoCount == 1);
    }
}

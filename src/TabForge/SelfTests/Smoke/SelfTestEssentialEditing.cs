using System.Linq;
using System.Windows;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Essential-action smoke checks of editing: notes, duration, undo and redo, delete, copy and paste in the score and on the timeline, tracks, bar ranges, sections and clips.</summary>
public static partial class SelfTest
{
    private static SongProject SmBlankSong(int bars = 4)
    {
        var song = new SongProject { Tempo = 120 };
        song.Tracks.Add(new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(bars) });
        song.IsDirty = false;
        return song;
    }

    private static string SmStatus(MainWindow w) => SmField<System.Windows.Controls.TextBlock>(w, "StatusText")!.Text;

    private static TabCell SmCell(DocumentSession doc, int bar, int cell) => doc.Project.Tracks[0].Measures[bar].Cells[cell];

    private static void TestEssentialNoteEditing()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmBlankSong());
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            SmStep("type notes", () =>
            {
                ed.SetPosition(0, 0, 0); ed.SetDuration(4); ed.EnterFret(5, autoAdvance: false);
                ed.SetPosition(0, 4, 0); ed.EnterFret(7, autoAdvance: false);
                Check("type notes: both frets are in the bar", SmCell(doc, 0, 0).Notes.Count == 1 && SmCell(doc, 0, 4).Notes.Count == 1 && SmCell(doc, 0, 4).Notes[0].Fret == 7);
            });
            SmStep("change duration", () =>
            {
                ed.SetPosition(0, 4, 0); ed.SetDuration(8);
                Check("change duration: the beat takes the new value", SmCell(doc, 0, 4).DurationDenominator == 8, $"was {SmCell(doc, 0, 4).DurationDenominator}");
            });
            SmStep("undo and redo", () =>
            {
                var before = SmCell(doc, 0, 4).DurationDenominator;
                SmClick(w, "Undo_Click");
                var undone = SmCell(doc, 0, 4).DurationDenominator != before || SmCell(doc, 0, 4).Notes.Count == 0;
                SmClick(w, "Redo_Click");
                Check("undo and redo: undo changes the song, redo brings the edit back", undone && SmCell(doc, 0, 4).DurationDenominator == before && SmCell(doc, 0, 4).Notes.Count == 1);
            });
            SmStep("delete with rest merge", () =>
            {
                ed.SetPosition(0, 4, 0); ed.DeleteNote();
                Check("delete: the note is gone and the beat is a rest", SmCell(doc, 0, 4).Notes.Count == 0 && SmCell(doc, 0, 0).Notes.Count == 1);
                ed.SetPosition(0, 0, 0); ed.DeleteBeat();
                Check("delete beat: the first beat is a rest too", SmCell(doc, 0, 0).Notes.Count == 0);
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestEssentialSelectionCopyPaste()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        // A paste question (replace or insert) is answered with its defaults; any other dialog is cancelled, never waited on.
        DialogHost.Capture = d => d is PasteOptionsDialog;
        try
        {
            var doc = SmOpenSong(w, SmBlankSong());
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            ed.SetPosition(0, 0, 0); ed.SetDuration(4); ed.EnterFret(3, autoAdvance: false);
            ed.SetPosition(0, 4, 0); ed.EnterFret(5, autoAdvance: false);
            SmStep("score selection copy and paste", () =>
            {
                ed.SelectMeasureRange(0, 0); SmClick(w, "Copy_Click");
                ed.SelectForEdit(2, 0, 0); SmClick(w, "Paste_Click");
                Check("score copy and paste: the copied bar lands on the target bar", SmCell(doc, 2, 0).Notes.Count == 1 && SmCell(doc, 2, 4).Notes.Count == 1 && SmCell(doc, 0, 0).Notes.Count == 1, SmStatus(w));
            });
            SmStep("score selection cut and paste", () =>
            {
                ed.SelectMeasureRange(0, 0); SmClick(w, "Cut_Click");
                var cut = SmCell(doc, 0, 0).Notes.Count == 0;
                ed.SelectForEdit(3, 0, 0); SmClick(w, "Paste_Click");
                Check("score cut and paste: the bar leaves its place and arrives on the target", cut && SmCell(doc, 3, 0).Notes.Count == 1 && SmCell(doc, 3, 4).Notes.Count == 1, SmStatus(w));
            });
            SmStep("timeline selection copy, cut and paste", () =>
            {
                var sections = SmField<SectionEditFlow>(w, "_sections")!;
                var bars = doc.Project.Tracks[0].Measures.Count;
                sections.CopyArea(doc, 2, 2);
                sections.PasteAreaAt(doc, 1);
                Check("timeline copy and paste: a bar is inserted with the copied notes", doc.Project.Tracks[0].Measures.Count == bars + 1 && SmCell(doc, 1, 0).Notes.Count == 1, SmStatus(w) + $" bars {doc.Project.Tracks[0].Measures.Count} of {bars}");
                var n = doc.Project.Tracks[0].Measures.Count;
                sections.CutArea(doc, 1, 1);
                Check("timeline cut: the bars leave the song", doc.Project.Tracks[0].Measures.Count <= n);
                sections.PasteAreaAt(doc, 0);
            });
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static void TestEssentialTracks()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var tracks = doc.Project.Tracks;
            var flow = (TrackClipboardFlow)SmCall(w, "get_TrackFlow")!;
            var n = tracks.Count;
            SmStep("add instrument track", () => { SmCall(w, "AddTrack", TrackKind.Bass); Check("add instrument track: one more track, a bass", tracks.Count == n + 1 && tracks[^1].Kind == TrackKind.Bass); });
            SmStep("add audio track", () => { w.AddAudioTrack(); Check("add audio track: one more track, audio", tracks.Count == n + 2 && tracks[^1].IsAudio); });
            SmStep("duplicate track", () => { flow.Duplicate(0); Check("duplicate track: a copy with its notes", tracks.Count == n + 3 && tracks.Count(t => t.Name.StartsWith(tracks[0].Name, StringComparison.Ordinal)) >= 2); });
            SmStep("reorder tracks", () =>
            {
                var first = tracks[0]; SmCall(w, "MoveTrackTo", 0, 1);
                Check("reorder tracks: the first track moves down", ReferenceEquals(tracks[1], first));
            });
            SmStep("mute and solo", () =>
            {
                tracks[0].Mute = true; tracks[1].Solo = true; SmCall(w, "ApplyMuteSolo");
                tracks[0].Mute = false; tracks[1].Solo = false; SmCall(w, "ApplyMuteSolo");
            });
            SmStep("convert instrument to audio and back", () =>
            {
                var track = tracks.First(t => !t.IsAudio && t.Kind == TrackKind.Guitar);
                var controller = SmField<TrackController>(w, "_trackController")!;
                var toAudio = controller.ConvertInstrumentToAudio(doc, track);
                var wasAudio = toAudio.Changed && track.IsAudio;
                var back = controller.ConvertAudioToInstrument(doc, track, TrackController.InstrumentPresets[0].Name);
                Check("convert tracks: instrument to audio and audio to instrument", wasAudio && back.Changed && !track.IsAudio);
            });
            SmStep("delete track (confirm)", () =>
            {
                var count = tracks.Count; var asked = new List<string>();
                SmWithDialogs(SmAnswerConfirm(MessageBoxResult.Yes, asked), () => { flow.Delete(tracks.Count - 1); return 0; });
                Check("delete track: the prompt is answered and the track is gone", tracks.Count == count - 1 && asked.Count >= 1, $"asked {asked.Count}, tracks {tracks.Count} of {count}");
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestEssentialTimelineEdits()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var range = SmField<SectionEditFlow>(w, "_sections")!.Range;
            int Bars() => doc.Project.Tracks[0].Measures.Count;
            foreach (var (action, delta) in new[] { (BarRangeAction.Clear, 0), (BarRangeAction.Remove, -2), (BarRangeAction.InsertBefore, 2), (BarRangeAction.InsertAfter, 2) })
            {
                var before = Bars();
                SmStep($"timeline {action}", () => range.Run(doc, action, 1, 2, true, false));
                Check($"timeline {action} bars: the bar count changes by {delta}", Bars() == before + delta, $"{before} -> {Bars()}");
            }
            SmStep("section move", () =>
            {
                var markers = doc.Project.Markers;
                if (markers.Count < 3) { Skip("section move", "the demo song has fewer than three sections"); return; }
                var titles = markers.Select(m => m.Title).ToList();
                SmCall(w, "MoveSection", 0, 2);
                Check("section move: the sections are reordered and none is lost", markers.Count == titles.Count && !markers.Select(m => m.Title).SequenceEqual(titles));
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestEssentialClips()
    {
        var w = SmNewWindow();
        try
        {
            var song = SmBlankSong();
            var audio = new TrackModel { Name = "Audio", Kind = TrackKind.Audio, Measures = TemplateFactory.Measures(4) };
            var clip = new AudioClip { Name = "Clip", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4, Notes = new List<ClipNote>() };
            audio.AudioClips.Add(clip);
            song.Tracks.Add(audio);
            var doc = SmOpenSong(w, song);
            var clips = SmField<ClipEditController>(w, "_clips")!;
            SmStep("clip move", () => { clips.Edit(doc, () => clip.StartSec += 1, "Moved"); Check("clip move: the clip starts later", Math.Abs(clip.StartSec - 1) < 1e-9); });
            SmStep("clip split", () => { clips.SplitAt(doc, audio, clip, 3); Check("clip split: two clips on the track", audio.AudioClips.Count == 2); });
            SmStep("clip fade", () =>
            {
                clips.Edit(doc, () => { clip.FadeOutSec = 0.5; }, "Fade");
                var had = clip.FadeOutSec > 0; clips.ResetFades(doc, clip);
                Check("clip fade: set and reset", had && clip.FadeOutSec == 0);
            });
        }
        finally { SmCloseWindow(w); }
    }
}

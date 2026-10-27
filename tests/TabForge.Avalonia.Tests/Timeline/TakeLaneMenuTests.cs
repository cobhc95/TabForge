using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the take-lane menu: which right-click opens it (an empty part of a clip lane, not a clip, not the tab lane), its rows, and its commands: Paste clip here at the
//   clicked (snapped) time, New empty MIDI clip here, the lane edits (rename, move, delete with a question when it holds clips) as single undo steps, and the MIDI file import.
// Does not own: the clip menu (ContextMenuTests), the drop import of audio (AudioDropPlannerTests) or the piano roll.
// Tests: this class.
public sealed class TakeLaneMenuTests
{
    private const int Bars = 12;

    // Rows: 0 a guitar (tab lane), 1 an audio track with a clip over bars 2-4 on lane 0 and an empty lane 1, 2 an audio track with a MIDI clip over bars 0-1.
    private static SongDocument NewSong()
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel
        {
            Name = "Guitar",
            Measures = Enumerable.Range(0, Bars).Select(bar => new MeasureModel { Number = bar + 1 }).ToList(),
        });
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var from = AudioClipTimelineRules.SecondsAtBar(project, 2, 0);
        var to = AudioClipTimelineRules.SecondsAtBar(project, 5, 0);
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = from, SourceLengthSec = to - from, FileLengthSec = to - from });
        ClipLanes.Ensure(audio, 2);
        project.Tracks.Add(audio);
        var midi = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var end = AudioClipTimelineRules.SecondsAtBar(project, 2, 0);
        midi.AudioClips.Add(new AudioClip { Name = "notes", Notes = new List<ClipNote>(), StartSec = 0, SourceLengthSec = end, FileLengthSec = end });
        project.Tracks.Add(midi);
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    private sealed record Rig(Window Window, TimelineView View, SongDocument Doc)
    {
        /// <summary>A point in lane <paramref name="lane"/> of track row <paramref name="row"/>, just inside bar <paramref name="bar"/>.</summary>
        public Point InLane(int row, int lane, int bar) => new(View.BarX(bar) + 3, View.LaneBandTopOf(row, lane) + ClipLanes.AudioLaneHeight / 2);

        public List<MenuSpec> Menu(Point at) => ContextMenuRows.TimelineAt(View, at).ToList();
    }

    private static Rig Open()
    {
        var doc = NewSong();
        var view = new TimelineView(doc.Project) { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 1600, Height = 1100, Content = view };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return new Rig(window, view, doc);
    }

    private static IEnumerable<TimelineCommand> Commands(IEnumerable<MenuSpec> rows) => TimelineMenus.Leaves(rows).Select(row => row.Command);

    [AvaloniaFact]
    public void RightClickOnAnEmptyLaneShowsTheLaneMenuAndNoBarItems()
    {
        var rig = Open();
        foreach (var at in new[] { rig.InLane(1, 1, 6), rig.InLane(1, 0, 8) })
        {
            var rows = rig.Menu(at);
            var commands = Commands(rows).ToList();
            Assert.Contains(TimelineCommand.LanePasteClip, commands);
            Assert.Contains(TimelineCommand.LaneImport, commands);
            Assert.Contains(TimelineCommand.LaneRecordHere, commands);
            Assert.Contains(TimelineCommand.LaneRename, commands);
            Assert.Contains(TimelineCommand.LaneMoveUp, commands);
            Assert.Contains(TimelineCommand.LaneMoveDown, commands);
            Assert.Contains(TimelineCommand.LaneDelete, commands);
            Assert.Contains(TimelineCommand.TimelineSettings, commands);
            Assert.DoesNotContain(TimelineCommand.CopyBar, commands);
            Assert.DoesNotContain(TimelineCommand.PasteBar, commands);
            Assert.DoesNotContain(TimelineCommand.DeleteBar, commands);
            Assert.DoesNotContain(TimelineCommand.InsertBarBefore, commands);
            Assert.DoesNotContain(TimelineCommand.ToggleTrackLines, commands);
            Assert.DoesNotContain(TimelineCommand.ResetTrackListHeight, commands);
            Assert.DoesNotContain(TimelineCommand.LaneNewMidiClip, commands);   // an audio lane takes no MIDI clip
            Assert.DoesNotContain(TimelineCommand.LaneOpenPianoRoll, commands);
        }
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ARightClickOnAClipKeepsTheClipMenuAndOnTheTabLaneTheBarMenu()
    {
        var rig = Open();
        var track = rig.Doc.Project.Tracks[1];
        var box = TimelineAudioClips.ClipBounds(rig.View, track, track.AudioClips[0]);
        var onClip = Commands(rig.Menu(new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2))).ToList();
        Assert.Contains(TimelineCommand.ClipCopy, onClip);
        Assert.DoesNotContain(TimelineCommand.LanePasteClip, onClip);

        var onTab = Commands(rig.Menu(new Point(rig.View.BarX(6) + 5, rig.View.RowTop(0) + 10))).ToList();
        Assert.Contains(TimelineCommand.CopyBar, onTab);
        Assert.Contains(TimelineCommand.PasteBar, onTab);
        Assert.DoesNotContain(TimelineCommand.LanePasteClip, onTab);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void AMidiLaneAlsoOffersANewMidiClipAndTheLaneInThePianoRoll()
    {
        var rig = Open();
        var commands = Commands(rig.Menu(rig.InLane(2, 0, 6))).ToList();
        rig.Window.Close();
        Assert.Contains(TimelineCommand.LaneNewMidiClip, commands);
        Assert.Contains(TimelineCommand.LaneOpenPianoRoll, commands);
    }

    [AvaloniaFact]
    public void TheLaneMenuRowsAllRunAHandledCommandAndPasteNeedsACopiedClip()
    {
        var rig = Open();
        ClipClipboard.Copy(rig.Doc.Project.Tracks[1].AudioClips[0]);
        var rows = TimelineMenus.Leaves(rig.Menu(rig.InLane(2, 0, 6))).ToList();
        rig.Window.Close();
        Assert.All(rows, row => Assert.Contains(ContextMenuRows.CommandIdOf(row), MainWindowTakeLane.Commands.Concat(new[] { TrackListMenus.SettingsId })));
        Assert.True(rows.Single(row => row.Command == TimelineCommand.LanePasteClip).Enabled);
        var none = TimelineMenus.TakeLane(new TakeLaneMenuState(CanPaste: false, CanNewMidiClip: false, CanOpenInPianoRoll: false, CanMoveUp: false, CanMoveDown: false, CanDelete: false), _ => "");
        Assert.False(TimelineMenus.Leaves(none).Single(row => row.Command == TimelineCommand.LanePasteClip).Enabled);
        Assert.False(TimelineMenus.Leaves(none).Single(row => row.Command == TimelineCommand.LaneDelete).Enabled);
    }

    [AvaloniaFact]
    public void PasteClipHerePastesAtTheClickedTimeSnappedToTheGridAsOneUndoStep()
    {
        var rig = Open();
        var track = rig.Doc.Project.Tracks[1];
        ClipClipboard.Copy(track.AudioClips[0]);
        rig.View.Snap = new SnapSettings { Grid = "Bar", ToItems = false, ToPlayhead = false };
        rig.Menu(rig.InLane(1, 1, 7));   // just inside bar 7: the bar line is the nearest snap
        var undo = rig.Doc.Undo.UndoCount;

        MainWindowTakeLane.PasteHere(rig.Doc, rig.View, rig.View.ContextMenus);
        rig.Window.Close();

        Assert.Equal(2, track.AudioClips.Count);
        var pasted = track.AudioClips[1];
        Assert.Equal(AudioClipTimelineRules.SecondsAtBar(rig.Doc.Project, 7, 0), pasted.StartSec, 3);
        Assert.Equal(1, pasted.Lane);
        Assert.Equal(undo + 1, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void NewEmptyMidiClipHereAddsOneBarAtTheClickedTimeAsOneUndoStep()
    {
        var rig = Open();
        var track = rig.Doc.Project.Tracks[2];
        rig.View.Snap = new SnapSettings { Grid = "Bar", ToItems = false, ToPlayhead = false };
        rig.Menu(rig.InLane(2, 0, 6));
        var undo = rig.Doc.Undo.UndoCount;

        MainWindowTakeLane.NewMidiClipHere(rig.Doc, rig.View, rig.View.ContextMenus);
        rig.Window.Close();

        var made = Assert.Single(track.AudioClips, clip => clip.Name == ClipLaneEdits.NewMidiClipName);
        Assert.True(made.IsMidi);
        Assert.Empty(made.Notes!);
        Assert.Equal(AudioClipTimelineRules.SecondsAtBar(rig.Doc.Project, 6, 0), made.StartSec, 3);
        Assert.Equal(AudioClipTimelineRules.SecondsAtBar(rig.Doc.Project, 7, 0) - made.StartSec, made.LengthSec, 3);
        Assert.Equal(undo + 1, rig.Doc.Undo.UndoCount);
    }

    [AvaloniaFact]
    public async Task DeleteLaneWithClipsAsksFirstAndAnEmptyLaneDoesNot()
    {
        var rig = Open();
        var track = rig.Doc.Project.Tracks[1];
        var asked = new List<string>();
        Func<string, string, Task<bool>> no = (text, caption) => { asked.Add(text); return Task.FromResult(false); };
        Func<string, string, Task<bool>> yes = (text, caption) => { asked.Add(text); return Task.FromResult(true); };
        var undo = rig.Doc.Undo.UndoCount;

        rig.Menu(rig.InLane(1, 0, 8));
        await MainWindowTakeLane.DeleteLaneAsync(rig.Doc, rig.View, rig.View.ContextMenus, no);
        Assert.Single(asked);
        Assert.Single(track.AudioClips);
        Assert.Equal(undo, rig.Doc.Undo.UndoCount);

        await MainWindowTakeLane.DeleteLaneAsync(rig.Doc, rig.View, rig.View.ContextMenus, yes);
        Assert.Empty(track.AudioClips);
        Assert.Equal(undo + 1, rig.Doc.Undo.UndoCount);

        var other = rig.Doc.Project.Tracks[2];
        ClipLanes.Ensure(other, 2);
        rig.Menu(rig.InLane(2, 1, 8));
        await MainWindowTakeLane.DeleteLaneAsync(rig.Doc, rig.View, rig.View.ContextMenus, yes);
        rig.Window.Close();
        Assert.Equal(2, asked.Count);   // the empty lane went without a question
        Assert.Single(other.AudioClips);
    }

    [AvaloniaFact]
    public async Task RenameAndMoveLaneAreSingleUndoSteps()
    {
        var rig = Open();
        var track = rig.Doc.Project.Tracks[1];
        var undo = rig.Doc.Undo.UndoCount;

        rig.Menu(rig.InLane(1, 0, 8));
        await MainWindowTakeLane.RenameAsync(rig.Doc, rig.View, rig.View.ContextMenus, (title, label, initial) => Task.FromResult<string?>("Verse"));
        Assert.Equal("Verse", track.Lanes[0].Name);
        Assert.Equal(undo + 1, rig.Doc.Undo.UndoCount);

        MainWindowTakeLane.MoveLane(rig.Doc, rig.View, rig.View.ContextMenus, 1);
        rig.Window.Close();
        Assert.Equal(1, track.AudioClips[0].Lane);
        Assert.Equal("Verse", track.Lanes[1].Name);
        Assert.Equal(undo + 2, rig.Doc.Undo.UndoCount);
    }

    [Fact]
    public void LaneEditsKeepTheOtherLanesPlayStateAndClips()
    {
        var track = new TrackModel();
        track.AudioClips.Add(new AudioClip { Name = "a", Lane = 0, SourceLengthSec = 1 });
        track.AudioClips.Add(new AudioClip { Name = "b", Lane = 1, SourceLengthSec = 1 });
        track.AudioClips.Add(new AudioClip { Name = "c", Lane = 2, SourceLengthSec = 1 });
        ClipLanes.Ensure(track, 3);
        track.Lanes[1].Plays = false;

        Assert.False(ClipLaneEdits.Move(track, 0, -1));
        Assert.True(ClipLaneEdits.Delete(track, 1));
        Assert.Equal(new[] { "a", "c" }, track.AudioClips.Select(clip => clip.Name));
        Assert.Equal(new[] { 0, 1 }, track.AudioClips.Select(clip => clip.Lane));
        Assert.Equal(2, track.Lanes.Count);
        Assert.True(ClipLaneEdits.Rename(track, 0, "  Lead "));
        Assert.Equal("Lead", track.Lanes[0].Name);
        Assert.True(ClipLaneEdits.Rename(track, 0, ""));
        Assert.Null(track.Lanes[0].Name);
        Assert.False(ClipLaneEdits.Rename(track, 0, null));
    }

    [Fact]
    public void AMidiFileBecomesOneClipWithItsNotesInSeconds()
    {
        // Format 0, 480 ticks per quarter, one note: on at tick 0, off at tick 480 (0.5 s at the default 120 bpm).
        var file = new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 13, 0x00, 0x90, 0x3C, 0x64, 0x83, 0x60, 0x80, 0x3C, 0x40, 0x00, 0xFF, 0x2F, 0x00,
        };

        var clip = MidiClipImport.Read(file, "riff");

        Assert.NotNull(clip);
        Assert.True(clip!.IsMidi);
        var note = Assert.Single(clip.Notes!);
        Assert.Equal((60, 0.0, 0.5), (note.Pitch, note.StartSec, note.LengthSec));
        Assert.Equal(0.5, clip.SourceLengthSec, 6);
    }
}

using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track list's main line stays pinned to the top of the row, one normal row height tall, however tall the row is: an armed empty track,
/// and a track with a take (taller lane bands), place the name, FX, M, S and sliders at the same offset from the row's top.
/// The armed strip sits directly under that main line, at the same offset from the top of the row in every track state (armed empty MIDI, MIDI with a take,
/// audio with a 54 high lane band), in Compact and Comfortable, and never overlaps it.
/// Does not own: the controls and drawing of the strip (ArmedRowTests, ArmedInputPillTests), the cell geometry (TrackRowCells).
/// Tests: this class.
/// </summary>
public class TrackHeaderTopAlignTests
{
    private static readonly TrackRowCells.Cell[] MainLine =
    {
        TrackRowCells.Cell.Settings, TrackRowCells.Cell.Kind, TrackRowCells.Cell.Arm, TrackRowCells.Cell.Fx, TrackRowCells.Cell.Mute,
        TrackRowCells.Cell.Solo, TrackRowCells.Cell.Volume, TrackRowCells.Cell.Pan,
    };

    private static (Window Window, ArrangementView View, SongProject Project) Open(TrackKind kind)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, kind));
        song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, kind));
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view, song.Project);
    }

    [AvaloniaTheory]
    [InlineData(TrackKind.Audio)]
    [InlineData(TrackKind.Keys)]
    public void ATrackWithATakeKeepsTheMainLineAtTheSameOffsetAsAnArmedEmptyTrack(TrackKind kind)
    {
        var (window, view, project) = Open(kind);
        var empty = project.Tracks.Count - 2;
        var taken = project.Tracks.Count - 1;
        project.Tracks[empty].RecordArm = true;
        project.Tracks[taken].RecordArm = true;
        project.Tracks[taken].AudioClips.Add(new AudioClip { Name = "Take", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4, Lane = 0 });
        view.Timeline.InvalidateMeasure();
        window.UpdateLayout();

        Assert.True(view.Timeline.BaseBandHeightOf(taken) > view.Timeline.RowHeight);
        foreach (var cell in MainLine)
            Assert.Equal(view.Header.CellRect(empty, cell).Y - view.Timeline.RowTop(empty), view.Header.CellRect(taken, cell).Y - view.Timeline.RowTop(taken), 6);
        Assert.Equal((view.Timeline.RowHeight - 22) / 2, view.Header.CellRect(taken, TrackRowCells.Cell.Mute).Y - view.Timeline.RowTop(taken), 6);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(28.9)]
    [InlineData(34)]
    public void TheArmedStripSitsAtTheSameOffsetUnderTheMainLineInEveryTrackState(double rowHeight)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        foreach (var kind in new[] { TrackKind.Keys, TrackKind.Keys, TrackKind.Audio }) song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, kind));
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 1000, Height = 900, Content = view };
        window.Show();
        view.Timeline.BaseRowHeight = rowHeight;
        var emptyMidi = song.Project.Tracks.Count - 3;
        var midiTake = emptyMidi + 1;
        var audioTake = emptyMidi + 2;
        song.Project.Tracks[midiTake].AudioClips.Add(new AudioClip { Name = "Take", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4, Lane = 0 });
        song.Project.Tracks[audioTake].AudioClips.Add(new AudioClip { Name = "Take", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4, Lane = 0 });
        foreach (var row in new[] { emptyMidi, midiTake, audioTake }) song.Project.Tracks[row].RecordArm = true;
        view.Timeline.Reload(song.Project);
        view.ArmedRows.Sync();
        window.UpdateLayout();

        Assert.Equal(ClipLanes.AudioLaneHeight, view.Timeline.BaseBandHeightOf(audioTake));
        var pill = ArmedRowPainter.PillHeightOf(view.Timeline.RowHeight);
        var pickers = view.ArmedRows.Children.OfType<TrackInputPicker>().ToList();
        Assert.Equal(3, pickers.Count);
        foreach (var row in new[] { emptyMidi, midiTake, audioTake })
        {
            var offset = view.Timeline.ArmedStripTopOf(row) - view.Timeline.RowTop(row);
            Assert.Equal(view.Timeline.RowHeight, offset, 6);   // directly under the main line
            Assert.True(view.Timeline.RowHeightOf(row) >= offset + ArmedRowPainter.SubRowHeight - 0.001, $"row {row}: the strip overflows the row");
            var pickerTop = Canvas.GetTop(pickers[row - emptyMidi]);
            Assert.Equal(offset + (ArmedRowPainter.SubRowHeight - pill) / 2, pickerTop + view.Timeline.TrackRowsTop - view.Timeline.RowTop(row), 6);
        }
        window.Close();
    }
}

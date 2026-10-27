using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Clip.Split at the playhead (one step, second part selected; outside the clip a notice and no step) and Clip.Properties applying
//   the dialog's values as one step (unchanged values take none).
// Tests: this class.
public class ClipPropertiesTests
{
    private static (MainWindowClipKeys.Target Target, SongDocument Document, TrackModel Track) Make(double startSec, double lengthSec)
    {
        var document = new SongDocument(() => null);
        var track = new TrackModel { Name = "Audio" };
        var clip = new AudioClip { Name = "Take", StartSec = startSec, SourceLengthSec = lengthSec, FileLengthSec = lengthSec };
        track.AudioClips.Add(clip);
        document.Project.Tracks.Add(track);
        var timeline = new TimelineView(document.Project) { SelectedClip = new TimelineClipSelection(track, clip) };
        return (new MainWindowClipKeys.Target(document, timeline, new TimelineClipSelection(track, clip)), document, track);
    }

    [AvaloniaFact]
    public void SplitAtPlayheadIsOneStepAndSelectsTheSecondPart()
    {
        var (target, document, track) = Make(0, 600);
        target.Timeline.SetPlayhead(1, 0.5);
        var notices = 0;
        MainWindowClipProperties.Split(target, _ => notices++);
        Assert.Equal(0, notices);
        Assert.Equal(2, track.AudioClips.Count);
        Assert.Equal(1, document.Undo.UndoCount);
        Assert.Same(track.AudioClips[1], target.Timeline.SelectedClip!.Clip);
    }

    [AvaloniaFact]
    public void SplitOutsideTheClipShowsTheNoticeAndTakesNoStep()
    {
        var (target, document, track) = Make(500, 10);
        string? notice = null;
        MainWindowClipProperties.Split(target, text => notice = text);
        Assert.Equal(MainWindowClipProperties.SplitOutsideNotice, notice);
        Assert.Single(track.AudioClips);
        Assert.Equal(0, document.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void PropertiesApplyAsOneStepAndUnchangedTakesNone()
    {
        var (target, document, _) = Make(0, 10);
        var edited = target.Selection.Clip.Clone();
        MainWindowClipProperties.Apply(target, edited);
        Assert.Equal(0, document.Undo.UndoCount);
        edited.Name = "Lead";
        edited.GainDb = -6;
        edited.Muted = true;
        MainWindowClipProperties.Apply(target, edited);
        Assert.Equal(1, document.Undo.UndoCount);
        Assert.Equal("Lead", target.Selection.Clip.Name);
        Assert.Equal(-6, target.Selection.Clip.GainDb);
        Assert.True(target.Selection.Clip.Muted);
    }
}

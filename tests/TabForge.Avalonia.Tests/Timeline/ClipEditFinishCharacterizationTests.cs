using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: what the window's post-clip-edit fix-up does to the song once an arrangement exists: the song grows to hold its clips plus 2 bars of
//   headroom when the edit itself did not fit the song, empty clip lanes are compacted (the default setting) except on an armed track; and that without an arrangement the fix-up does
//   not run (the "no arrangement, no fix-up" rule).
// Does not own: the edits (MainWindowClipKeys, TimelineClipGestureController), the lane rules (ClipLanes) or the engine sync (RecordingLaneLifecycleTests).
// Tests: ClipEditFinisher.
public sealed class ClipEditFinishCharacterizationTests
{
    private static (SongDocument Song, TrackModel Audio, MainWindowDocumentPanes Panes) Build(bool arrangement, bool armed = false)
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, 4).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() });
        var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        audio.RecordArm = armed;
        audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2, Lane = 1 });
        project.Tracks.Add(audio);
        var song = new SongDocument(() => null) { Project = project };
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var panes = new MainWindowDocumentPanes(tabs, new ScorePreviewView());
        ClipEditFinisher.Register(panes, null, null);
        if (arrangement) panes.NewArrangement();
        return (song, audio, panes);
    }

    [AvaloniaFact]
    public void AClipEditPastTheEndGrowsTheSongWithHeadroomAndCompactsEmptyLanes()
    {
        var (song, audio, panes) = Build(arrangement: true);
        var clip = audio.AudioClips[0];
        MainWindowClipKeys.Edit(song, panes.Arrangement!.Timeline, () => { clip.StartSec = 9; return true; });
        // 9 s + 2 s at 120 BPM in 4/4 ends inside bar 6: the clip edit's own fit grows the song to 6 bars, so the fix-up's headroom (it grows only
        // a song that still ends before its clips) adds nothing here.
        Assert.Equal(6, song.Project.Tracks[0].Measures.Count);
        Assert.Equal(0, clip.Lane);   // the empty lane 0 was compacted away
    }

    [AvaloniaFact]
    public void ALandedEditPastTheEndGetsTwoBarsOfHeadroom()
    {
        var (song, audio, panes) = Build(arrangement: true);
        audio.AudioClips[0].StartSec = 9;   // a recording or a piano-roll edit landed outside the clip keys (no fit of its own)
        panes.ClipEdits!.Finish(song);
        Assert.Equal(8, song.Project.Tracks[0].Measures.Count);   // the clip ends inside bar 6, then 2 empty bars
        Assert.Equal(0, audio.AudioClips[0].Lane);
    }

    [AvaloniaFact]
    public void AnArmedTracksLanesAreNotCompacted()
    {
        var (song, audio, panes) = Build(arrangement: true, armed: true);
        var clip = audio.AudioClips[0];
        MainWindowClipKeys.Edit(song, panes.Arrangement!.Timeline, () => { clip.StartSec = 1; return true; });
        Assert.Equal(1, clip.Lane);
    }

    [AvaloniaFact]
    public void WithoutAnArrangementTheFixUpDoesNotRun()
    {
        var (song, audio, panes) = Build(arrangement: false);
        audio.AudioClips[0].StartSec = 9;   // an edit landed elsewhere (a recording, the piano roll, collected media)
        panes.ClipEdits!.Finish(song);
        Assert.Equal(4, song.Project.Tracks[0].Measures.Count);
        Assert.Equal(1, audio.AudioClips[0].Lane);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the repeat view (RV1-RV8): the one pure fold of a clip performed span onto the written bars (the three worked examples), the folded
//   drawing and hit test over a real song (parts pinned to the repeat, one outline, any part selects the whole clip), moving a clip sliding
//   its audio through fixed parts, plain playback (one clip, performed time), the migration of stamped T2 repeat groups, and a clip that
//   starts inside pass 2 showing only part 2.
// Does not own: landing a recording (RepeatTakeLandingTests), the pass mapping (RepeatPassesTests) or the live overlay stacks (RepeatTakeLandingTests F/G).
// Tests: this class.
public sealed class RepeatViewTests
{
    /// <summary>Bars 1-12, one second each, repeat 5-8 twice: performed 1-4, 5-8, 5-8, 9-12 (a clip of 14 one-bar chunks A..N).</summary>
    private static PerformedBar[] SpecSong()
    {
        var order = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 5, 6, 7, 8, 9, 10, 11, 12 };
        return order.Select((bar, index) => new PerformedBar(bar, index * 1000.0, (index + 1) * 1000.0)).ToArray();
    }

    private static void AssertSlice(RepeatSlice slice, double start, double end, double writtenStart, double writtenEnd, int part, int count)
    {
        Assert.Equal(start, slice.StartSec, 6);
        Assert.Equal(end, slice.EndSec, 6);
        Assert.Equal(writtenStart, slice.WrittenStartSec, 6);
        Assert.Equal(writtenEnd, slice.WrittenEndSec, 6);
        Assert.Equal(part, slice.PartIndex);
        Assert.Equal(count, slice.PartCount);
    }

    [Fact]
    public void Example1_ClipStartingAtBar2ShowsABC_DEFG_HIJK_LMN()
    {
        // Bar b starts at b-1 seconds; the clip is 14 one-bar chunks from bar 2.
        var slices = RepeatFold.Slices(1, 15, SpecSong());

        Assert.Equal(4, slices.Count);
        AssertSlice(slices[0], 1, 4, 1, 4, 0, 1);      // bars 2-4: A B C
        AssertSlice(slices[1], 4, 8, 4, 8, 0, 2);      // part 1 over bars 5-8: D E F G
        AssertSlice(slices[2], 8, 12, 4, 8, 1, 2);     // part 2 over the same bars: H I J K
        AssertSlice(slices[3], 12, 15, 12, 15, 0, 1);  // bars 9-11: L M N
    }

    [Fact]
    public void Example2_MovedLeftOneShowsABCD_EFGH_IJKL_MN()
    {
        var slices = RepeatFold.Slices(0, 14, SpecSong());

        Assert.Equal(4, slices.Count);
        AssertSlice(slices[0], 0, 4, 0, 4, 0, 1);      // bars 1-4: A B C D
        AssertSlice(slices[1], 4, 8, 4, 8, 0, 2);      // part 1: E F G H
        AssertSlice(slices[2], 8, 12, 4, 8, 1, 2);     // part 2: I J K L
        AssertSlice(slices[3], 12, 14, 12, 14, 0, 1);  // bars 9-10: M N
    }

    [Fact]
    public void Example3_MovedRightOneShowsAB_CDEF_GHIJ_KLMN()
    {
        var slices = RepeatFold.Slices(2, 16, SpecSong());

        Assert.Equal(4, slices.Count);
        AssertSlice(slices[0], 2, 4, 2, 4, 0, 1);      // bars 3-4: A B
        AssertSlice(slices[1], 4, 8, 4, 8, 0, 2);      // part 1: C D E F
        AssertSlice(slices[2], 8, 12, 4, 8, 1, 2);     // part 2: G H I J
        AssertSlice(slices[3], 12, 16, 12, 16, 0, 1);  // bars 9-12: K L M N
    }

    [Fact]
    public void ClipStartingInsidePass2ShowsOnlyPart2()
    {
        // RV6: a clip that starts at the second play of bar 6 covers part 2 only: its slice is row 2 of 2, part 1 stays empty.
        var slices = RepeatFold.Slices(9, 14, SpecSong());

        Assert.Equal(2, slices.Count);
        AssertSlice(slices[0], 9, 12, 5, 8, 1, 2);     // bars 6-8 of pass 2
        AssertSlice(slices[1], 12, 14, 12, 14, 0, 1);  // then bars 9-10 as usual
        Assert.DoesNotContain(slices, slice => slice.IsPart && slice.PartIndex == 0);
    }

    [Fact]
    public void AClipOutsideEveryRepeatIsOneWholeSlice()
    {
        var slices = RepeatFold.Slices(0.5, 3.5, SpecSong());

        var only = Assert.Single(slices);
        AssertSlice(only, 0.5, 3.5, 0.5, 3.5, 0, 1);
        Assert.False(only.IsPart);
    }

    // ---- over a real song: bars 0-3, bars 1-2 repeat x2, performed 0,1,2,1,2,3 ----

    private static (SongDocument Song, TrackModel Track) RepeatSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    private static double At(SongProject project, int bar, double fraction) => AudioClipTimelineRules.SecondsAtBar(project, bar, fraction);

    private static AudioClip AudioClipOver(SongProject project, double fromBar, double toBar) => new()
    {
        File = "take.wav", Name = "take", StartSec = fromBar * At(project, 1, 0), SourceLengthSec = (toBar - fromBar) * At(project, 1, 0),
        FileLengthSec = (toBar - fromBar) * At(project, 1, 0),
    };

    private static global::Avalonia.Point Centre(global::Avalonia.Rect rect) => new((rect.X + rect.Right) / 2, (rect.Y + rect.Bottom) / 2);

    [AvaloniaFact]
    public void FoldedClipDrawsPartsPinnedToTheRepeatBarsWithOneOutlineAndHitsAnyPartAsTheWholeClip()
    {
        var (song, track) = RepeatSong();
        var clip = AudioClipOver(song.Project, 0.5, 5.5);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var row = song.Project.Tracks.IndexOf(track);
        var laneTop = view.LaneBandTopOf(row);

        var slices = TimelineAudioClips.FoldOf(view, clip)!;
        var parts = slices.Where(slice => slice.IsPart).ToList();
        Assert.Equal(2, parts.Count);
        var rect1 = TimelineAudioClips.SliceRect(view, parts[0], laneTop);
        var rect2 = TimelineAudioClips.SliceRect(view, parts[1], laneTop);
        // Pinned to the repeat bars (bars 1-2 of the song), one above the other, each half the lane.
        Assert.Equal(view.BarX(1), rect1.X, 2);
        Assert.Equal(view.BarX(3) - view.BarX(1), rect1.Width, 1);
        Assert.Equal(rect1.X, rect2.X, 6);
        Assert.Equal(rect1.Width, rect2.Width, 6);
        Assert.Equal((ClipLanes.AudioLaneHeight - 4) / 2, rect1.Height, 6);
        Assert.Equal(rect1.Bottom, rect2.Y, 6);

        // RV4: a press on any slice (either part, or the ordinary span before the repeat) is the same one clip.
        Assert.Same(clip, TimelineAudioClips.ClipAt(view, Centre(rect1))!.Clip);
        Assert.Same(clip, TimelineAudioClips.ClipAt(view, Centre(rect2))!.Clip);
        var ordinary = TimelineAudioClips.SliceRect(view, slices.First(slice => !slice.IsPart), laneTop);
        Assert.Same(clip, TimelineAudioClips.ClipAt(view, Centre(ordinary))!.Clip);

        // One blue outline around the whole fold, and the numbers 1 and 2 on the parts.
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var drawn = new RepeatTakeLandingTests.TextRecorder();
        TimelineAudioClips.DrawRow(drawn, key => AvaloniaPalette.Get(key, false), view, track, row, laneTop, AvaloniaPalette.Get("InstrumentAccent", false));
        Assert.Contains("1", drawn.Texts);
        Assert.Contains("2", drawn.Texts);
        var box = TimelineAudioClips.ClipBounds(view, track, clip);
        Assert.Single(drawn.Rects, rect => !rect.Fill && rect.Stroke && Math.Abs(rect.X - box.X) < 1 && Math.Abs(rect.Width - box.Width) < 1);
    }

    [AvaloniaFact]
    public void MovingTheClipSlidesItThroughPartsThatStayPinned()
    {
        var (song, track) = RepeatSong();
        var clip = AudioClipOver(song.Project, 0.5, 5.5);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project) { Snap = new SnapSettings { Enabled = false } };
        var laneTop = view.LaneBandTopOf(song.Project.Tracks.IndexOf(track));
        var partsBefore = TimelineAudioClips.FoldOf(view, clip)!.Where(slice => slice.IsPart)
            .Select(slice => TimelineAudioClips.SliceRect(view, slice, laneTop)).ToList();
        var startBefore = TimelineAudioClips.ClipBounds(view, track, clip).X;
        var oneBar = At(song.Project, 1, 0);

        Assert.True(ClipEditService.Move(track, clip, 0.25 * oneBar));

        // RV3: the clip is still one clip and the part boxes did not move; the ordinary span before the repeat did.
        Assert.Single(track.AudioClips);
        var partsAfter = TimelineAudioClips.FoldOf(view, clip)!.Where(slice => slice.IsPart)
            .Select(slice => TimelineAudioClips.SliceRect(view, slice, laneTop)).ToList();
        Assert.Equal(partsBefore, partsAfter);
        Assert.NotEqual(startBefore, TimelineAudioClips.ClipBounds(view, track, clip).X);
    }

    [AvaloniaFact]
    public void ClipStartingInsidePass2DrawsOnlyPart2()
    {
        var (song, track) = RepeatSong();
        var oneBar = At(song.Project, 1, 0);
        var clip = AudioClipOver(song.Project, 3.5, 6);   // performed 3.5 = the middle of the second play of bar 1 (the repeated bar after bar 2)
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project);

        var slices = TimelineAudioClips.FoldOf(view, clip)!;

        // Pass 2 only is one ordinary box tagged "pass 2" (fold only when a clip covers 2+ passes); the raw fold (RV6) is RepeatFold.Slices.
        Assert.DoesNotContain(slices, slice => slice.IsPart);
        Assert.Equal(2, slices[0].TagPass);
        Assert.Contains(RepeatFold.Slices(clip.StartSec, clip.EndSec, SongExtent.Timing(song.Project).Performed), slice => slice.IsPart && slice.PartIndex == 1);
        Assert.Equal(clip.StartSec, slices[0].StartSec, 6);
        Assert.True(oneBar > 0);
    }

    [Fact]
    public void PlaybackIsPlainOneClipPlayedStraightInPerformedTime()
    {
        // RV5: a MIDI clip over the repeat sounds each note exactly once, at clip start + note offset (performed time): no per-part scheduling.
        var (song, track) = RepeatSong();
        track.Kind = TrackKind.Keys;
        track.AudioInput = AudioInputs.Midi;
        var oneBar = At(song.Project, 1, 0);
        var clip = new AudioClip
        {
            Name = "keys", StartSec = 0.5 * oneBar, SourceLengthSec = 5 * oneBar,
            Notes = new List<ClipNote> { new(0.0, 0.2, 60, 100), new(1.5 * oneBar, 0.2, 62, 100), new(3.5 * oneBar, 0.2, 64, 100) },
        };
        track.AudioClips.Add(clip);

        var timeline = MidiTimelineBuilder.Build(song.Project, new PlaybackOptions { RespectMuteSolo = false });

        var index = song.Project.Tracks.IndexOf(track);
        var onsets = timeline.Events.Where(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0 && e.TrackIndex == index).OrderBy(e => e.TimeMs).ToList();
        Assert.Equal(new[] { 60, 62, 64 }, onsets.Select(e => e.Data1).ToArray());
        Assert.Equal((clip.StartSec) * 1000, onsets[0].TimeMs, 1);
        Assert.Equal((clip.StartSec + 1.5 * oneBar) * 1000, onsets[1].TimeMs, 1);
        Assert.Equal((clip.StartSec + 3.5 * oneBar) * 1000, onsets[2].TimeMs, 1);
        // An audio clip is one engine piece reading its file from the clip own offset (the engine sync sends exactly these pieces).
        var audio = AudioClipOver(song.Project, 0.5, 5.5);
        var piece = Assert.Single(ClipLoop.Pieces(audio));
        Assert.Equal((audio.StartSec, audio.OffsetSec, audio.SourceLengthSec), (piece.StartSec, piece.OffsetSec, piece.SourceLengthSec));
    }

    private static AudioClip Stamped(string group, int pass, double start, double offset, double length, string name) => new()
    {
        File = "take.wav", Name = name, StartSec = start, OffsetSec = offset, SourceLengthSec = length, FileLengthSec = 6,
        TakeGroupId = group, TakeGroupKind = TakeGroupKind.Repeat, TakeGroupPass = pass,
    };

    [Fact]
    public void MigrationMergesAStampedRepeatGroupIntoOneOrdinaryClip()
    {
        // RV7: the T2 build stored pre + part 1 + part 2 + post (performed 0-1, 1-3, 3-5, 5-6; the file runs 0-6 straight through).
        var track = new TrackModel { Name = "Audio", Kind = TrackKind.Audio };
        track.AudioClips.Add(Stamped("g", 0, 0, 0, 1, "take pre"));
        track.AudioClips.Add(Stamped("g", 1, 1, 1, 2, "take part 1"));
        track.AudioClips.Add(Stamped("g", 2, 3, 3, 2, "take part 2"));
        track.AudioClips.Add(Stamped("g", 0, 5, 5, 1, "take post"));
        var other = new AudioClip { File = "other.wav", StartSec = 20, SourceLengthSec = 1 };
        track.AudioClips.Add(other);
        var project = new SongProject { Tracks = new List<TrackModel> { track } };

        var keptEarliestOnly = ProjectService.MigrateRepeatGroupsToOneClip(project);

        Assert.Equal(0, keptEarliestOnly);
        Assert.Equal(2, track.AudioClips.Count);
        var merged = track.AudioClips[0];
        Assert.Null(merged.TakeGroupId);
        Assert.NotEqual(TakeGroupKind.Repeat, merged.TakeGroupKind);
        Assert.Equal(0, merged.TakeGroupPass);
        Assert.Equal("take", merged.Name);
        Assert.Equal(0, merged.StartSec, 6);
        Assert.Equal(0, merged.OffsetSec, 6);
        Assert.Equal(6, merged.SourceLengthSec, 6);
        Assert.Equal(6, merged.EndSec, 6);
        Assert.Same(other, track.AudioClips[1]);
    }

    [Fact]
    public void MigrationOfANonContiguousAudioGroupKeepsTheEarliestPartAndReportsIt()
    {
        var track = new TrackModel { Name = "Audio", Kind = TrackKind.Audio };
        track.AudioClips.Add(Stamped("g", 1, 1, 1, 2, "take part 1"));
        track.AudioClips.Add(Stamped("g", 2, 3, 4, 2, "take part 2"));   // the file jumped: not contiguous
        var project = new SongProject { Tracks = new List<TrackModel> { track } };

        var keptEarliestOnly = ProjectService.MigrateRepeatGroupsToOneClip(project);

        Assert.Equal(1, keptEarliestOnly);
        var kept = Assert.Single(track.AudioClips);
        Assert.Null(kept.TakeGroupId);
        Assert.Equal(1, kept.StartSec, 6);
        Assert.Equal(2, kept.SourceLengthSec, 6);
    }

    [Fact]
    public void MigrationMergesStampedMidiPartsShiftingTheirNotes()
    {
        var track = new TrackModel { Name = "Keys", Kind = TrackKind.Keys };
        track.AudioClips.Add(new AudioClip
        {
            Name = "Keys MIDI part 1", StartSec = 1, SourceLengthSec = 2, Notes = new List<ClipNote> { new(0.1, 0.2, 60, 90) },
            TakeGroupId = "g", TakeGroupKind = TakeGroupKind.Repeat, TakeGroupPass = 1,
        });
        track.AudioClips.Add(new AudioClip
        {
            Name = "Keys MIDI part 2", StartSec = 3, SourceLengthSec = 2, Notes = new List<ClipNote> { new(0.1, 0.2, 62, 90) },
            TakeGroupId = "g", TakeGroupKind = TakeGroupKind.Repeat, TakeGroupPass = 2,
        });
        var project = new SongProject { Tracks = new List<TrackModel> { track } };

        ProjectService.MigrateRepeatGroupsToOneClip(project);

        var merged = Assert.Single(track.AudioClips);
        Assert.True(merged.IsMidi);
        Assert.Equal("Keys MIDI", merged.Name);
        Assert.Equal(1, merged.StartSec, 6);
        Assert.Equal(4, merged.SourceLengthSec, 6);
        Assert.Equal(new[] { (60, 0.1), (62, 2.1) }, merged.Notes!.Select(note => (note.Pitch, Math.Round(note.StartSec, 6))).ToArray());
    }
}

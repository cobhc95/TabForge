using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Ports;
using TabForge.Presenters;

namespace TabForge.Services;

/// <summary>Which lane of the drop target a Composer drum sample lands on.</summary>
public enum DrumSampleLane
{
    /// <summary>Drum notation written into the track's tab bars (converted to the target's instrument).</summary>
    Tab,
    /// <summary>A MIDI clip on the track's take lanes (piano roll).</summary>
    Midi,
}

/// <summary>What the lane prompt shows: the sample, the track under the pointer and where in bars it would land.</summary>
public sealed record DrumSampleDropQuestion(string SampleName, string TrackName, int Bar, int Bars, bool TabAvailable);

/// <summary>What the lane prompt answered: the chosen lane and whether to remember it (the stored setting skips the prompt).</summary>
public readonly record struct DrumSampleLaneAnswer(DrumSampleLane Lane, bool Remember);

/// <summary>What a landed drum-sample drop added, with the status line for the notice row.</summary>
public sealed record DrumSampleDropResult(TrackModel Track, AudioClip? Clip, int Bar, DrumSampleLane Lane, string Status);

// Owns: landing a Composer drum sample on a track at a bar: the tab-lane write (converted through the paste mapper and placed by the paste engine
//   over the sample's span, one undo step) and the MIDI-lane clip (a new take lane when the track is occupied, the same lane rule as an audio-file drop). View-free: the window
//   maps the pointer to a track and a bar, asks the lane question and refreshes after the edit.
// Does not own: the samples (DrumSamples), the drag and its ghost (MainWindowComposerDrums), the lane prompt (DrumSampleLanePromptWindow), or the
//   stored lane choice (DrumSampleDropChoice).
// Tests: TabForge.Avalonia.Tests.DrumSampleDropTests.
public static class DrumSampleDrop
{
    /// <summary>Places <paramref name="sample"/> on <paramref name="track"/> from bar <paramref name="bar"/> (0-based, the bar's first slot) on
    /// <paramref name="lane"/>, as one undo step; null when nothing changed (an audio track has no tab lane, a track with no strings takes no rhythm).
    /// <paramref name="newTrack"/> creates a drum track first (a drop on the Add track strip).</summary>
    public static DrumSampleDropResult? Apply(IEditTarget document, DrumSample sample, TrackModel? track, bool newTrack, int bar, DrumSampleLane lane)
    {
        DrumSampleDropResult? result = null;
        var edit = document.Run(project =>
        {
            var target = newTrack ? NewDrumTrack(project) : track;
            if (target is null) return false;
            if (newTrack) project.Tracks.Add(target);
            if (lane == DrumSampleLane.Tab && !target.HasNotation) return false;
            if (lane == DrumSampleLane.Tab)
            {
                result = ApplyTab(project, target, sample, bar);
                return result is not null;
            }
            result = ApplyMidi(project, target, sample, bar);
            return result is not null;
        });
        return edit.Changed ? result : null;
    }

    private static DrumSampleDropResult? ApplyTab(SongProject project, TrackModel target, DrumSample sample, int bar)
    {
        // The sample is drum rhythm: the paste mapper puts it on the drum map for a drum track, or as rhythm on the lowest string for a pitched one
        // (the same conversion a paste between instruments does).
        var drumLayout = InstrumentLayout.Of(new TrackModel
        {
            Kind = TrackKind.Drums,
            DrumMapPreset = target.IsDrumTrack ? target.DrumMapPreset : DrumMaps.GuitarPro5,
            CustomDrumMap = target.CustomDrumMap?.Select(e => e.Clone()).ToList(),
        });
        var mapped = NoteMapper.Map(drumLayout, InstrumentLayout.Of(target), sample.Cells());
        // The paste engine places the hits at their 16th onsets from the bar's start and clears the sample's span, like a paste over it:
        // it works on any bar (other meters, longer or dotted notes already there), splits across bar lines with ties and appends bars when needed.
        var beats = new List<ClipBeat>();
        for (var slot = 0; slot < mapped.Beats.Count; slot++)
            if (mapped.Beats[slot].Notes.Count > 0) beats.Add(new ClipBeat(slot, mapped.Beats[slot]));
        if (beats.Count == 0) return null;
        var placed = BarGrid.PlaceBeats(project, project.Tracks.IndexOf(target), 0, BarGrid.BarStart(project, bar),
            new BeatRun(beats, sample.Bars * DrumSample.StepsPerBar), BeatPasteMode.Replace);
        if (!placed.Ok) return null;
        return new DrumSampleDropResult(target, null, bar, DrumSampleLane.Tab,
            $"Added '{sample.Name}' to {target.Name} at bar {bar + 1} (tab lane)");
    }

    private static DrumSampleDropResult ApplyMidi(SongProject project, TrackModel target, DrumSample sample, int bar)
    {
        // The same lane rule as an audio-file drop: an empty track takes lane 0, an occupied one a new lane after its current ones.
        var lane = target.AudioClips.Count == 0 ? 0 : ClipLanes.Count(target);
        ClipLanes.Ensure(target, lane + 1);
        var startSec = AudioClipTimelineRules.SecondsAtBar(project, bar, 0);
        var endSec = AudioClipTimelineRules.SecondsAtBar(project, bar + sample.Bars, 0);
        var notes = new List<ClipNote>();
        foreach (var (hitBar, step, midi, velocity) in sample.Hits())
            notes.Add(new ClipNote(
                AudioClipTimelineRules.SecondsAtBar(project, bar + hitBar, step / (double)DrumSample.StepsPerBar) - startSec,
                StepLength(project, bar + hitBar, step),
                midi, velocity, Channel: 9));
        notes.Sort((a, b) => a.StartSec.CompareTo(b.StartSec));
        var clip = new AudioClip
        {
            Name = sample.Name, StartSec = startSec, SourceLengthSec = Math.Max(0.001, endSec - startSec),
            FileLengthSec = Math.Max(0.001, endSec - startSec), Lane = lane, Notes = notes, Controllers = new List<ClipController>(),
        };
        var overlaps = target.AudioClips.Any(c => c.Overlaps(clip.StartSec, clip.EndSec));
        target.AudioClips.Add(clip);
        // A drop over an existing clip makes the new lane the one that plays, as the file-drop applier does; otherwise the new lane simply plays.
        if (overlaps) ClipLanes.PlayNewTake(target, lane, midi: true);
        else if (!ClipLanes.Plays(target, lane)) target.Lanes[lane].Plays = true;
        return new DrumSampleDropResult(target, clip, bar, DrumSampleLane.Midi,
            $"Added '{sample.Name}' to {target.Name} at bar {bar + 1} (MIDI lane)");
    }

    private static double StepLength(SongProject project, int bar, int step)
    {
        var from = AudioClipTimelineRules.SecondsAtBar(project, bar, step / (double)DrumSample.StepsPerBar);
        var to = AudioClipTimelineRules.SecondsAtBar(project, bar, (step + 1) / (double)DrumSample.StepsPerBar);
        return Math.Max(0.01, to - from);
    }

    private static TrackModel NewDrumTrack(SongProject project) => NewTrackFactory.CreateTrack(project, TrackKind.Drums);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

/// <summary>Where a drop lands: nowhere, the Add track strip (a new audio track), or an existing track (a new lane on it).</summary>
public enum AudioDropTargetKind
{
    None,
    NewTrack,
    ExistingTrack,
}

/// <summary>The target a drop position maps to; <see cref="Track"/> is set only for <see cref="AudioDropTargetKind.ExistingTrack"/>.</summary>
public readonly record struct AudioDropTarget(AudioDropTargetKind Kind, TrackModel? Track = null)
{
    public static AudioDropTarget NewTrack => new(AudioDropTargetKind.NewTrack);
    public static AudioDropTarget None => new(AudioDropTargetKind.None);
    public static AudioDropTarget On(TrackModel track) => new(AudioDropTargetKind.ExistingTrack, track);
}

/// <summary>One accepted file of a drop: where it starts, how long it is, and the clip name (the file name without its extension). <see cref="Midi"/> is the imported clip of a MIDI file (null for audio).</summary>
public sealed record AudioDropItem(string File, double StartSec, double LengthSec, string Name, AudioClip? Midi = null)
{
    public double EndSec => StartSec + LengthSec;
}

/// <summary>The planned landing of a drop: the new audio track or the existing track's lane, the first start time, and the accepted files in order.</summary>
public sealed record AudioDropPlan(
    AudioDropTargetKind Kind,
    TrackModel? Track,
    int Lane,
    double StartSec,
    IReadOnlyList<AudioDropItem> Items,
    string? TrackName = null,
    string? Refusal = null,
    IReadOnlyList<string>? Skipped = null)
{
    /// <summary>True when at least one file can land; otherwise <see cref="Refusal"/> says why nothing was added.</summary>
    public bool Accepted => Refusal is null && Kind != AudioDropTargetKind.None && Items.Count > 0;

    public bool NewTrack => Kind == AudioDropTargetKind.NewTrack;

    /// <summary>The end of the last accepted file (the first start when nothing was accepted).</summary>
    public double EndSec => Items.Count == 0 ? StartSec : Items[^1].EndSec;
}

/// <summary>The track and last clip a landed drop added, with the status line for the notice row.</summary>
public sealed record AudioDropResult(TrackModel Track, AudioClip LastClip, int Added, string Status);

// Owns: where dropped audio files land (a new audio track from the Add track strip, or a new lane on the track under the pointer), the files that
//   were refused and why, and the one undo step that adds the track/clips. View-free: the Avalonia drop handler maps a pointer to a target and a
//   song time, and measures lengths with the existing decoder (WaveformCache.LengthOf) before planning.
// Does not own: reading the drag data, the drag affordance, the decoder, the status line or the engine sync (the view layer, after the edit).
// Tests: TabForge.Avalonia.Tests.AudioDropPlannerTests.
/// <summary>Maps dropped paths and a drop target to a plan, and applies an accepted plan as one undo step.</summary>
public static class AudioDropPlanner
{
    /// <summary>Shown when the pointer is over empty timeline space rather than the strip or a track.</summary>
    public const string NoTargetText = "Drop audio files on a track or the Add track strip";

    /// <summary>Shown for a file whose extension is not one of the audio extensions the decoder accepts.</summary>
    public const string NotAudioText = "Not an audio file";

    public static string? RefusalOf(string file, Func<string, string?>? pathGuard = null) =>
        pathGuard?.Invoke(file) is { Length: > 0 } refusal ? refusal
        : MediaPathPolicy.IsAudioExtension(file) ? null
        : NotAudioText;

    /// <summary>True when the file would be accepted (audio extension and the caller's path guard); used for the drag effect.</summary>
    public static bool Accepts(string file, Func<string, string?>? pathGuard = null) => RefusalOf(file, pathGuard) is null;

    /// <summary>
    /// Plans a drop at <paramref name="startSec"/>: accepted audio files land one after another from that song time; a new track gets the first
    /// file's name, an existing track gets a lane after its current ones (its own lane when it is empty). <paramref name="lengthOf"/> is the
    /// existing decoder (or a fake in tests); refused files are reported through <see cref="AudioDropPlan.Refusal"/> or
    /// <see cref="AudioDropPlan.Skipped"/> and never added silently.
    /// </summary>
    public static AudioDropPlan Plan(
        IReadOnlyList<string> files,
        AudioDropTarget target,
        double startSec,
        Func<string, double> lengthOf,
        Func<string, string?>? pathGuard = null,
        Func<string, AudioClip?>? midiOf = null)
    {
        if (target.Kind == AudioDropTargetKind.None) return Refused(target, startSec, NoTargetText);
        if (target.Kind == AudioDropTargetKind.ExistingTrack && target.Track is null) return Refused(target, startSec, NoTargetText);

        var start = NormaliseStart(startSec);
        var items = new List<AudioDropItem>(files.Count);
        var skipped = new List<string>();
        var at = start;
        foreach (var file in files)
        {
            if (midiOf is not null && FileTypes.IsMidi(Path.GetExtension(file)))
            {
                if (pathGuard?.Invoke(file) is { Length: > 0 } blocked) { skipped.Add($"{Path.GetFileName(file)}: {blocked}"); continue; }
                var midi = ReadMidi(midiOf, file);
                if (midi is null) { skipped.Add($"{Path.GetFileName(file)}: not a readable MIDI file with notes"); continue; }
                items.Add(new AudioDropItem(file, at, midi.SourceLengthSec, Path.GetFileNameWithoutExtension(file), midi));
                at += midi.SourceLengthSec;
                continue;
            }
            if (RefusalOf(file, pathGuard) is { } refusal)
            {
                skipped.Add($"{Path.GetFileName(file)}: {refusal}");
                continue;
            }
            var length = Measure(lengthOf, file);
            if (!(length > 0))
            {
                skipped.Add($"{Path.GetFileName(file)}: could not be read as audio");
                continue;
            }
            items.Add(new AudioDropItem(file, at, length, Path.GetFileNameWithoutExtension(file)));
            at += length;
        }
        if (items.Count == 0)
            return new AudioDropPlan(target.Kind, target.Track, 0, start, items, Refusal: skipped.Count > 0 ? skipped[0] : NotAudioText, Skipped: skipped);

        var lane = target.Kind == AudioDropTargetKind.NewTrack
            ? 0
            : target.Track!.AudioClips.Count == 0 ? 0 : ClipLanes.Count(target.Track!);
        var trackName = target.Kind == AudioDropTargetKind.NewTrack && !string.IsNullOrWhiteSpace(items[0].Name) ? items[0].Name : null;
        return new AudioDropPlan(target.Kind, target.Track, lane, start, items, trackName, Skipped: skipped);
    }

    /// <summary>The notice naming every file the plan could not add and why (null when none was skipped): a file that cannot be read never becomes a silent blank clip.</summary>
    public static string? SkippedNotice(AudioDropPlan plan) =>
        plan.Skipped is { Count: > 0 } skipped ? "Could not add " + string.Join("; ", skipped) : null;

    /// <summary>The status line after an accepted plan landed.</summary>
    public static string StatusText(AudioDropPlan plan, TrackModel track)
    {
        var what = plan.Items.Count == 1 ? Path.GetFileName(plan.Items[0].File) : $"{plan.Items.Count} files";
        var skipped = plan.Skipped is { Count: > 0 } notes ? $" (skipped {string.Join(", ", notes)})" : "";
        return $"Added {what} to {track.Name} at {plan.StartSec:0.00} s{skipped}";
    }

    private static AudioDropPlan Refused(AudioDropTarget target, double startSec, string refusal) =>
        new(target.Kind, target.Track, 0, NormaliseStart(startSec), Array.Empty<AudioDropItem>(), Refusal: refusal);

    private static double NormaliseStart(double sec) => double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0 ? 0 : sec;

    private static AudioClip? ReadMidi(Func<string, AudioClip?> midiOf, string file)
    {
        try { return midiOf(file) is { SourceLengthSec: > 0 } clip ? clip : null; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    private static double Measure(Func<string, double> lengthOf, string file)
    {
        try
        {
            var length = lengthOf(file);
            return double.IsNaN(length) || double.IsInfinity(length) ? 0 : length;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException)
        {
            return 0;
        }
    }
}

/// <summary>Applies an accepted <see cref="AudioDropPlan"/> to a document in one undo step: the new track (when planned) and every clip.</summary>
public static class AudioDropApplier
{
    /// <summary>Adds the plan's track and clips; null when nothing changed. The caller refreshes the view (and syncs the engine) afterwards through
    /// the normal clip-edit hook, so there is no second sync path here.</summary>
    public static AudioDropResult? Apply(IEditTarget document, AudioDropPlan plan)
    {
        if (!plan.Accepted) return null;
        AudioDropResult? result = null;
        var edit = document.Run(project =>
        {
            var track = plan.NewTrack ? NewAudioTrack(project, plan.TrackName) : plan.Track;
            if (track is null) return false;
            if (plan.NewTrack) project.Tracks.Add(track);
            ClipLanes.Ensure(track, plan.Lane + 1);
            var overlaps = track.AudioClips.Any(clip => !clip.IsMidi && clip.Overlaps(plan.StartSec, plan.EndSec));
            AudioClip? last = null;
            foreach (var item in plan.Items)
            {
                var clip = item.Midi is { } midi
                    ? midi.Clone()
                    : new AudioClip
                {
                    File = item.File, Name = item.Name, StartSec = item.StartSec, OffsetSec = 0,
                    SourceLengthSec = item.LengthSec, FileLengthSec = item.LengthSec, Lane = plan.Lane,
                };
                if (item.Midi is not null) { clip.Name = item.Name; clip.StartSec = item.StartSec; clip.Lane = plan.Lane; }
                track.AudioClips.Add(clip);
                last = clip;
            }
            if (last is null) return false;
            // A drop over an existing clip makes the new lane the one that plays (and greys that older take), as the recording-take rule does;
            // otherwise the new lane simply plays.
            if (overlaps) ClipLanes.PlayNewTake(track, plan.Lane, midi: plan.Items.All(item => item.Midi is not null));
            else if (!ClipLanes.Plays(track, plan.Lane)) track.Lanes[plan.Lane].Plays = true;
            result = new AudioDropResult(track, last, plan.Items.Count, AudioDropPlanner.StatusText(plan, track));
            return true;
        });
        return edit.Changed ? result : null;
    }

    private static TrackModel NewAudioTrack(SongProject project, string? name)
    {
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        if (!string.IsNullOrWhiteSpace(name)) track.Name = name;
        return track;
    }
}

using System;
using System.Collections.Generic;
using TabForge.Features.Recording.Model;
using TabForge.Models;

namespace TabForge.Avalonia.Features.Recording.Presenter;

// Owns: the takes being recorded, for the live arrangement overlay: per armed track its start (the first frame's engine-clock second),
//   its growing span, its lane (recomputed over the whole span every frame), a bounded peak envelope (audio) or the notes received so far
//   (MIDI), and the rules that keep an audio take after Stop until its file lands while a MIDI take goes at Stop.
// Does not own: the capture (TransportRecording/MidiTakeCapture in Core), the drawing (TimelineLiveTakeLayer), the per-frame feed (RecordingPresenter),
//   the lane model (ClipLanes in Core), the rows reload a new lane needs (the caller's LaneCreated hook).
// Tests: TabForge.Avalonia.Tests.RecordingLiveTakeTests, LiveTakeFoldTests, RepeatTakeLandingTests.

/// <summary>A take being recorded, drawn live in its lane until the recording stops (audio: until its WAV lands).</summary>
internal sealed class LiveTake
{
    public LiveTake(TrackModel track, double startSec, bool midi, (double Start, double End)? loop = null, PerformedBar[]? repeatPerformed = null)
    {
        Track = track;
        StartSec = startSec;
        EndSec = startSec;
        Midi = midi;
        Loop = loop;
        RepeatPerformed = repeatPerformed;
    }

    public TrackModel Track { get; }
    /// <summary>Where the take starts on the song timeline: the engine-clock second of its first frame (the engine's final take is aligned the same way).</summary>
    public double StartSec { get; private set; }
    /// <summary>How far the take has grown, in song seconds; never before <see cref="StartSec"/>.</summary>
    public double EndSec { get; private set; }
    /// <summary>The lane the take currently lands on: recomputed every frame over the growing span, so it can move down into a free lane.</summary>
    public int Lane { get; set; }
    /// <summary>The track's lane count when the lane was last computed; the overlay reads it instead of re-evaluating the lane rules per frame.</summary>
    public int LaneCount { get; set; }
    public bool Midi { get; }
    /// <summary>The loop range the recording was started with (null: no loop); its laps stack inside this range.</summary>
    public (double Start, double End)? Loop { get; }
    /// <summary>The song's performed order when the recording runs through score repeats (null: no repeat fold); its laps stack inside the take's lane.</summary>
    public PerformedBar[]? RepeatPerformed
    {
        get => _repeatPerformed;
        set { _repeatPerformed = value; _bands = null; }
    }
    private PerformedBar[]? _repeatPerformed;
    /// <summary>True once the track was disarmed while recording: the box stops growing but keeps its lane until the global stop.</summary>
    public bool Frozen { get; private set; }
    /// <summary>Where the take's lane footprint ends: the loop end when it loops (record start..loop end), else its growing front.</summary>
    public double FootprintEndSec => Loop is { } loop ? Math.Max(loop.End, StartSec) : EndSec;
    /// <summary>True while the take grows through score repeats (no transport loop): its bands stack inside its own lane, never below it.</summary>
    public bool RepeatBandsActive => Loop is null && RepeatPerformed is not null;
    /// <summary>
    /// The take's drawable bands from the one live-lap rule (<see cref="LiveTakeFold"/>), cached until the start or front moves.
    /// Before the first wrap this is one band; every completed lap adds a band on its own live lane below, folded onto the loop range.
    /// Through score repeats every play draws on the take's own lane instead (the second play under the first).
    /// </summary>
    public IReadOnlyList<LiveTakeBand> Bands => _bands ??= RepeatBandsActive
        ? LiveTakeFold.RepeatBands(StartSec, EndSec, RepeatPerformed)
        : LiveTakeFold.Bands(StartSec, EndSec, Loop);
    /// <summary>How many live lanes below the base lane the bands need (0 before the first wrap or without a loop).</summary>
    public int StackRows => Math.Max(0, Bands.Count - 1);

    /// <summary>The track was disarmed while recording: stop growing the take, keep it (and its lane) until the global stop.</summary>
    public void Freeze() => Frozen = true;
    /// <summary>Audio: the input peak of every frame, kept as a bounded envelope (one peak per bucket over the whole take).</summary>
    public LivePeaks Peaks { get; } = new();
    /// <summary>MIDI: the notes heard so far, times from the take start.</summary>
    public List<ClipNote> Notes { get; } = new();
    /// <summary>MIDI: the notes held now, by pitch: where they began on the timeline and their velocity.</summary>
    public Dictionary<int, (double Start, int Velocity)> Held { get; } = new();

    /// <summary>True while any part of the take's lane footprint lies in [from, to).</summary>
    public bool Overlaps(double from, double to) => StartSec < to && FootprintEndSec > from;

    /// <summary>Keeps a MIDI note message: a note-on is held (and drawn growing) until its note-off.</summary>
    public void Note(int type, int pitch, int velocity, double songSec)
    {
        if (type == 0x90 && velocity > 0) Held[pitch] = (Math.Max(StartSec, songSec), velocity);
        else if (type is 0x80 or 0x90 && Held.Remove(pitch, out var held))
        {
            var start = Math.Max(StartSec, held.Start);
            var end = Math.Max(start + MinNoteSec, songSec);
            Notes.Add(new ClipNote(start - StartSec, end - start, pitch, held.Velocity));
        }
    }

    /// <summary>The first frame is the take's real start: a note message that arrived before it moved the placeholder back.</summary>
    public void StartAt(double startSec)
    {
        if (startSec >= StartSec || EndSec != StartSec || Notes.Count > 0 || Held.Count > 0) return;
        StartSec = startSec;
        EndSec = startSec;
        _bands = null;
    }

    private const double MinNoteSec = 0.01;
    private IReadOnlyList<LiveTakeBand>? _bands;

    /// <summary>Grows the take to <paramref name="nowSec"/>; true when the span changed.</summary>
    public bool GrowTo(double nowSec)
    {
        var end = Math.Max(StartSec, nowSec);
        if (end <= EndSec) return false;
        EndSec = end;
        _bands = null;
        return true;
    }
}

/// <summary>
/// The whole take as a bounded peak envelope: one float per display frame goes in, at most <see cref="Capacity"/> buckets are kept, and when
/// the buckets fill the envelope is halved (adjacent buckets merge by max) so the whole take — not just its tail — stays visible.
/// Thread: UI only; adds allocate nothing.
/// </summary>
internal sealed class LivePeaks
{
    /// <summary>The buckets kept per take; enough to draw one value per column of a wide lane.</summary>
    public const int Capacity = 512;
    private readonly float[] _values = new float[Capacity];
    private int _count;
    private int _frames;
    private int _every = 1;   // frames per bucket

    /// <summary>How many buckets hold a value (0 before the first frame).</summary>
    public int Count => _count;

    /// <summary>How many frames one bucket merges.</summary>
    public int FramesPerBucket => _every;

    /// <summary>Adds this frame's input peak (0..1); a full envelope is halved first, so memory stays fixed.</summary>
    public void Add(float peak)
    {
        peak = Math.Clamp(peak, 0f, 1f);
        if (_count == 0 || _frames % _every == 0)
        {
            if (_count == Capacity) Halve();
            _values[_count++] = peak;
        }
        else if (_values[_count - 1] < peak) _values[_count - 1] = peak;
        _frames++;
    }

    /// <summary>The value of bucket <paramref name="index"/> (0 outside the filled range).</summary>
    public float this[int index] => index >= 0 && index < _count ? _values[index] : 0f;

    private void Halve()
    {
        for (var i = 0; i < Capacity / 2; i++) _values[i] = Math.Max(_values[i * 2], _values[i * 2 + 1]);
        _count = Capacity / 2;
        _every *= 2;
    }
}

/// <summary>The live takes of one recording: one per armed track, their growing spans, lanes and content.</summary>
internal sealed class LiveTakes
{
    private readonly List<LiveTake> _takes = new();

    /// <summary>The takes in the order they were begun (the overlay draws them in this order).</summary>
    public IReadOnlyList<LiveTake> All => _takes;

    /// <summary>The take at <paramref name="index"/> (the overlay's index loop: no enumerator allocation per frame).</summary>
    public LiveTake this[int index] => _takes[index];

    public int Count => _takes.Count;

    /// <summary>Raised when a growing take needs a lane the track does not have yet: the caller ensures the lane and refreshes the rows once.</summary>
    public Action<TrackModel>? LaneCreated { get; set; }

    /// <summary>The loop range of the recording in progress (null: no loop); every take begun now keeps it for its folded box and lane footprint.</summary>
    public (double Start, double End)? Loop { get; set; }

    /// <summary>
    /// The song's performed order when the recording runs through score repeats (null: no repeat fold). A transport loop wins:
    /// a looped recording folds one lane per lap, never stacked parts. Every take begun now keeps it.
    /// </summary>
    public PerformedBar[]? RepeatPerformed { get; set; }

    /// <summary>The lane bands the overlay needs for <paramref name="track"/>: its live take's base lane plus one band per lap
    /// (a wrapped take grows a live lane per lap inside the row; 0 when it has no take). A repeat take needs no extra lane:
    /// its plays stack inside the base lane.</summary>
    public int LaneCountOf(TrackModel track)
    {
        var take = Of(track);
        if (take is null || take.LaneCount == 0) return 0;
        if (take.RepeatBandsActive) return Math.Max(take.LaneCount, take.Lane + 1);
        return Math.Max(take.LaneCount, take.Lane + take.Bands.Count);
    }

    /// <summary>Live laps are full lane bands counted in <see cref="LaneCountOf"/>; nothing stacks below them (always 0).</summary>
    public double StackHeightOf(TrackModel track) => 0;

    /// <summary>The take of <paramref name="track"/>, or null.</summary>
    public LiveTake? Of(TrackModel track)
    {
        for (var i = 0; i < _takes.Count; i++)
            if (ReferenceEquals(_takes[i].Track, track)) return _takes[i];
        return null;
    }

    /// <summary>
    /// One display frame for <paramref name="track"/>: begins the take at the first frame's second, grows its span, adds the audio peak and
    /// recomputes its lane over the take's lane footprint (record start..loop end when it loops, else the whole grown span). True when anything changed.
    /// </summary>
    public bool Frame(TrackModel track, double nowSec, float peak, bool midi)
    {
        var take = Of(track);
        var changed = false;
        if (take is null)
        {
            take = new LiveTake(track, nowSec, midi, Loop, Loop is null ? RepeatPerformed : null);
            _takes.Add(take);
            changed = true;
        }
        else
        {
            if (take.Frozen) return false;   // disarmed mid-recording: the box and its lane stay until the global stop
            take.StartAt(nowSec);   // a MIDI take begun by a note before the first frame takes the frame's start
        }
        var rowsBefore = take.Bands.Count;
        changed |= take.GrowTo(nowSec);
        var rowsAfter = take.Bands.Count;
        if (!midi)
        {
            // A count-in hold freezes the span (GrowTo false): the envelope still moves, so the overlay still repaints and the 2 px hold box never looks dead.
            var before = take.Peaks.Count;
            take.Peaks.Add(peak);
            if (take.Peaks.Count != before) changed = true;
        }
        var lane = FreeLane(take);
        if (lane != take.Lane) { take.Lane = lane; changed = true; }
        // The song's lanes are not touched while recording: the overlay alone grows a lane per lap, and the landing edit stores the lane it needs.
        // A repeat take stacks its plays inside its base lane, so it never grows one.
        var modelLanes = ClipLanes.Count(track);
        var lanes = take.RepeatBandsActive ? Math.Max(modelLanes, lane + 1) : Math.Max(modelLanes, lane + take.Bands.Count);
        var needsReload = rowsAfter != rowsBefore;   // a completed lap opens a new live lane
        if (lanes > take.LaneCount)
        {
            take.LaneCount = lanes;
            needsReload = true;   // an armed track with no clips moves from its armed sub-row to a real lane band too
            changed = true;
        }
        if (needsReload)
        {
            LaneCreated?.Invoke(track);   // the row grows once per lap/lane, never every frame
            changed = true;
        }
        return changed;
    }

    /// <summary>Adds a MIDI note message to the track's live take (a take is begun at that note if none exists yet). True when accepted.</summary>
    public bool Note(TrackModel track, int type, int pitch, int velocity, double songSec)
    {
        var take = Of(track);
        if (take is null)
        {
            take = new LiveTake(track, songSec, midi: true, loop: Loop, repeatPerformed: Loop is null ? RepeatPerformed : null);
            _takes.Add(take);
        }
        if (!take.Midi || take.Frozen) return false;
        take.Note(type, pitch, velocity, songSec);
        return true;
    }

    /// <summary>Stops a track's take where it is (disarm mid-recording) but keeps its box and lane until the global stop.</summary>
    public bool Freeze(TrackModel track)
    {
        var take = Of(track);
        if (take is null || take.Frozen) return false;
        take.Freeze();
        return true;
    }

    /// <summary>Stops the recording: the MIDI takes go at once (their clips are committed), the audio takes stay until their files land.</summary>
    public bool Stop()
    {
        var removed = _takes.RemoveAll(take => take.Midi);
        return removed > 0;
    }

    /// <summary>A finished audio take's file arrived: its live box goes and the real clip replaces it. The song's lanes are left to the landing edit.</summary>
    public bool Remove(TrackModel track)
    {
        var removed = false;
        for (var i = _takes.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_takes[i].Track, track)) { _takes.RemoveAt(i); removed = true; }
        return removed;
    }

    /// <summary>Drops every take (a refused start, a new recording, or the window closing). True when any was dropped.</summary>
    public bool Clear()
    {
        if (_takes.Count == 0) return false;
        _takes.Clear();
        return true;
    }

    /// <summary>The first lane where the take's whole lane footprint crosses neither a clip nor another live take of the same track.</summary>
    private int FreeLane(LiveTake self)
    {
        for (var lane = 0; ; lane++)
        {
            if (Busy(self, lane)) continue;
            return lane;
        }
    }

    private bool Busy(LiveTake self, int lane)
    {
        foreach (var clip in self.Track.AudioClips)
            if (clip.Lane == lane && clip.Overlaps(self.StartSec, self.FootprintEndSec)) return true;
        foreach (var other in _takes)
            if (!ReferenceEquals(other, self) && ReferenceEquals(other.Track, self.Track) && other.Lane == lane && other.Overlaps(self.StartSec, self.FootprintEndSec))
                return true;
        return false;
    }
}

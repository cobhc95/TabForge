using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: Focus Track, the opt-in practice aid: while on, the selected track plays louder by the user's rule (decibels, percent or a level, for any track or for
//   a mixer group), and when that would pass the loudest level the other tracks of the song are lowered by the excess instead, so nothing clips. A listening
//   aid only: it never changes a fader, the saved song, an export or a render.
// Does not own: the toggle, its saved state, the rules and the selection (the app, FocusTrackSettings), or sending the levels (PlaybackEngine live state, EngineMixLevels).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
// Threads: set and refreshed on the UI thread, read on the scheduler and audio threads through one volatile reference.
// One instance per playback host (AvaloniaPlaybackSessions.Focus); engines and the engine sync read it, nothing is global.
public sealed class PracticeFocus
{
    // The focused track and the ids of the shown song's tracks (another open song's tracks are not in the set, so they play as they are), and the two
    // factors worked out from the rule: the focused track's, and the others' (below 1 only when the boost would pass the loudest level).
    private sealed record State(Guid Track, HashSet<Guid> Song, double FocusFactor, double OthersFactor, TrackModel Selected, FocusTrackSettings Rules);

    private State? _state;

    /// <summary>True while Focus Track is on.</summary>
    public bool IsOn => Volatile.Read(ref _state) is not null;

    /// <summary>The focused track's id while on, else <see cref="Guid.Empty"/>.</summary>
    public Guid FocusedTrack => Volatile.Read(ref _state)?.Track ?? Guid.Empty;

    /// <summary>
    /// Turns Focus Track on or off for <paramref name="selected"/>, one of <paramref name="project"/>'s tracks, by <paramref name="rules"/> (no track: nothing changes).
    /// True when this changed what plays.
    /// </summary>
    public bool Set(bool on, SongProject? project, TrackModel? selected, FocusTrackSettings? rules)
    {
        var next = on && project is not null && selected is not null ? Build(project, selected, rules ?? new FocusTrackSettings()) : null;
        return Replace(next);
    }

    /// <summary>Works the factors out again after a fader, group or rule change; true when they changed.</summary>
    public bool Refresh(SongProject project) =>
        Volatile.Read(ref _state) is { } s && project.Tracks.Contains(s.Selected) && Replace(Build(project, s.Selected, s.Rules));

    private bool Replace(State? next)
    {
        var before = Interlocked.Exchange(ref _state, next);
        if (before is null || next is null) return before is not null || next is not null;
        return before.Track != next.Track || before.FocusFactor != next.FocusFactor || before.OthersFactor != next.OthersFactor || !before.Song.SetEquals(next.Song);
    }

    private static State Build(SongProject project, TrackModel selected, FocusTrackSettings rules)
    {
        var own = MixerGroups.Volume(project, selected);
        var wanted = own > 0 ? rules.RuleFor(MixerGroups.GroupOf(project, selected)).Apply(own) : 0;
        double focus = 1, others = 1;
        if (own > 0 && wanted > 0)
        {
            if (wanted <= MidiLimits.Max) focus = wanted / own;
            else { focus = MidiLimits.Max / (double)own; others = MidiLimits.Max / wanted; }   // as loud as the track can go; the rest make up the difference
        }
        return new State(selected.Id, project.Tracks.Select(t => t.Id).ToHashSet(), focus, others, selected, rules);
    }

    /// <summary>A 0-127 level after Focus Track.</summary>
    public int Scale(TrackModel track, int level) => Scale(track.Id, level);

    /// <summary>The same, for a track known by its id (the playing timeline numbers its tracks by id).</summary>
    public int Scale(Guid track, int level)
    {
        if (Volatile.Read(ref _state) is not { } s || !s.Song.Contains(track)) return level;
        var factor = track == s.Track ? s.FocusFactor : s.OthersFactor;
        return factor == 1 ? level : Math.Clamp((int)Math.Round(level * factor), 0, MidiLimits.Max);
    }
}

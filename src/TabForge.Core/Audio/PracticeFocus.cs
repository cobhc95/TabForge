using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: Focus Track, the opt-in practice aid: while on, the selected track plays louder by the user's rule (decibels, percent or a level); the other tracks play by the template the user chose for their group (lowered like the boost, left alone, or lowered or raised by an amount of their own).
//   The selected track is never lowered: it is the boosted one. A listening aid only: it
//   never changes a fader, the saved song, an export or a render.
// Does not own: the toggle, its saved state, the rules and the selection (the app, FocusTrackSettings), or sending the levels (PlaybackEngine live state, EngineMixLevels).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
// Threads: set and refreshed on the UI thread, read on the scheduler and audio threads through one volatile reference.
// One instance per playback host (AvaloniaPlaybackSessions.Focus); engines and the engine sync read it, nothing is global.
public sealed class PracticeFocus
{
    // The focused track, the factor it plays at, and for every other track of the shown song (another open song's tracks are not in the map, so they play as
    // they are) the factor it plays at: its group's lowering together with the headroom excess.
    private sealed record State(Guid Track, double FocusFactor, Dictionary<Guid, double> Others, TrackModel Selected, FocusTrackSettings Rules);

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
        return before.Track != next.Track || before.FocusFactor != next.FocusFactor || before.Others.Count != next.Others.Count
            || before.Others.Any(p => !next.Others.TryGetValue(p.Key, out var f) || f != p.Value);
    }

    private static State Build(SongProject project, TrackModel selected, FocusTrackSettings rules)
    {
        var own = MixerGroups.Volume(project, selected);
        var wanted = own > 0 ? rules.Boost.Apply(own) : 0;
        var ratio = own > 0 && wanted > 0 ? wanted / own : 1;   // how many times louder the rule asks the selected track to be
        var focus = own > 0 && wanted > MidiLimits.Max ? MidiLimits.Max / (double)own : ratio;   // it cannot go past the loudest level
        var others = new Dictionary<Guid, double>();
        foreach (var track in project.Tracks)
        {
            if (track.Id == selected.Id) continue;   // the boosted track is never lowered, whatever its group says
            var level = MixerGroups.Volume(project, track);
            var rule = rules.OtherFor(MixerGroups.GroupOf(project, track));
            var factor = level <= 0 ? 1 : rule.Mode switch
            {
                FocusOtherMode.LeaveAlone => 1,
                FocusOtherMode.Lower => (rule.Amount ?? new FocusBoost()).ApplyLower(level) / level,
                FocusOtherMode.Raise => Math.Min((rule.Amount ?? new FocusBoost()).Apply(level), MidiLimits.Max) / level,
                _ => Mirror(rules.Boost, ratio),
            };
            others[track.Id] = factor;
        }
        return new State(selected.Id, focus, others, selected, rules);
    }

    // Lowered by the same amount the selected track is raised: the same decibels, the same percentage, or (for a level) the same change in loudness.
    private static double Mirror(FocusBoost boost, double ratio) =>
        boost.Mode == FocusBoostMode.Percent ? Math.Max(0, 1 - boost.ClampedAmount / 100) : ratio > 0 ? 1 / ratio : 1;

    /// <summary>A 0-127 level after Focus Track.</summary>
    public int Scale(TrackModel track, int level) => Scale(track.Id, level);

    /// <summary>The same, for a track known by its id (the playing timeline numbers its tracks by id).</summary>
    public int Scale(Guid track, int level)
    {
        if (Volatile.Read(ref _state) is not { } s) return level;
        double factor;
        if (track == s.Track) factor = s.FocusFactor;
        else if (!s.Others.TryGetValue(track, out factor)) return level;   // a track of a song that is not shown
        return factor == 1 ? level : Math.Clamp((int)Math.Round(level * factor), 0, MidiLimits.Max);
    }
}

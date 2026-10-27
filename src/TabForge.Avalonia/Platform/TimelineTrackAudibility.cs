using System;
using TabForge.Models;

namespace TabForge.Avalonia.Platform;

// Owns: which track rows the timeline draws dimmed because the mixer would not play them (muted, or not soloed while another track is soloed), cached per row.
// Does not own: the mute/solo rule (MixerGroups.IsAudible in Core, the one rule playback and the engine levels use), the dimming strength (TimelineView.MutedTrackDimPercent).
// Tests: TabForge.Avalonia.Tests.TimelineMuteSoloDimTests.
/// <summary>
/// The rows the mixer silences, refreshed once per body draw. The rule runs again only when a cheap hash of everything it reads
/// (the tracks' mute, solo, group and kind, the mixer's group mute/solo, the song's content revision) changes, so a steady frame allocates nothing.
/// Thread: UI thread only.
/// </summary>
internal sealed class TimelineTrackAudibility
{
    private bool[] _dimmed = Array.Empty<bool>();
    private int _key;
    private bool _known;

    /// <summary>Brings the per-row flags up to date with <paramref name="song"/>.</summary>
    public void Refresh(SongProject song)
    {
        var key = KeyOf(song);
        if (_known && key == _key && _dimmed.Length == song.Tracks.Count) return;
        _key = key;
        _known = true;
        if (_dimmed.Length != song.Tracks.Count) _dimmed = new bool[song.Tracks.Count];
        for (var i = 0; i < _dimmed.Length; i++) _dimmed[i] = !MixerGroups.IsAudible(song, song.Tracks[i]);
    }

    /// <summary>Whether the row's track is silenced by the mixer (a row outside the song is not dimmed).</summary>
    public bool Dimmed(int row) => (uint)row < (uint)_dimmed.Length && _dimmed[row];

    private static int KeyOf(SongProject song)
    {
        var hash = new HashCode();
        hash.Add(song.ContentRevision);
        hash.Add(song.Tracks.Count);
        hash.Add(song.Mixer.Grouping);
        foreach (var track in song.Tracks)
        {
            hash.Add(track.Mute);
            hash.Add(track.Solo);
            hash.Add(track.MixerGroup);
            hash.Add(track.Kind);
        }
        foreach (var group in song.Mixer.Groups)
        {
            hash.Add(group.Key);
            hash.Add(group.Value.Mute);
            hash.Add(group.Value.Solo);
        }
        return hash.ToHashCode();
    }
}

using System;
using System.Linq;
using TabForge.Avalonia.Transport;
using TabForge.Documents;

namespace TabForge.Avalonia;

// Owns: noticing that an edit (Track properties OK, an instrument change, Undo, Redo) changed what the shown song's tracks sound like (level, pan, sends, program,
//   channel, mute, solo, and the master, group and bus levels) and pushing that to the running playback at once, so the change is heard without a stop and play.
// Does not own: the edits, the push itself (AvaloniaPlaybackSessions.PushLiveMix) or the slider and mixer gestures, which push for themselves.
// Tests: no dedicated test class.
// Threads: UI thread only.
internal sealed class MainWindowLiveMix : IDisposable
{
    private readonly AvaloniaPlaybackSessions _playback;
    private readonly DocumentWatch _watch = new();
    private SongDocument? _song;
    private int _shown;

    public MainWindowLiveMix(AvaloniaPlaybackSessions playback)
    {
        _playback = playback;
        _watch.Marked = _ => Check();
        _watch.Restored = Check;
    }

    /// <summary>Watches <paramref name="song"/>, the shown one, from the mix it has now.</summary>
    public void Bind(SongDocument song)
    {
        _song = song;
        _shown = Fingerprint(song);
        _watch.Bind(song);
    }

    public void Dispose() => _watch.Dispose();

    /// <summary>Pushes the mix to the running playback when it differs from the one last seen.</summary>
    public void Check()
    {
        if (_song is not { } song) return;
        var now = Fingerprint(song);
        if (now == _shown) return;
        _shown = now;
        _playback.PushLiveMix(song);
    }

    private static int Fingerprint(SongDocument song)
    {
        var hash = new HashCode();
        var mixer = song.Project.Mixer;
        hash.Add(mixer.MasterPan); hash.Add(mixer.Master.On); hash.Add(mixer.Grouping);
        // Groups and buses by name, so the dictionary's order does not matter.
        foreach (var (name, levels) in mixer.Groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            hash.Add(name);
            hash.Add(levels.Volume); hash.Add(levels.Pan); hash.Add(levels.Pitch); hash.Add(levels.Mute); hash.Add(levels.Solo);
        }
        foreach (var (name, bus) in mixer.Buses.OrderBy(b => b.Key, StringComparer.Ordinal)) { hash.Add(name); hash.Add(bus.On); }
        foreach (var track in song.Project.Tracks)
        {
            hash.Add(track.Id);
            hash.Add(track.Volume); hash.Add(track.Pan); hash.Add(track.Reverb); hash.Add(track.Chorus);
            hash.Add(track.MidiProgram); hash.Add(track.MidiChannel); hash.Add(track.Mute); hash.Add(track.Solo);
        }
        return hash.ToHashCode();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: noticing that the shown song's track list changed (a track added, deleted or moved, by any command, Undo or Redo) and bringing the engine and
//   the playing schedule in line with it (AvaloniaPlaybackSessions.TracksChanged), so a deleted track stops sounding and the channels stay with their tracks.
// Does not own: the edits (MainWindowTrackMenu, DocumentEdits), the engine sync and the live refresh (AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.TrackStructureTests.
internal sealed class MainWindowTrackStructure : IDisposable
{
    private readonly AvaloniaPlaybackSessions _playback;
    private readonly Func<PluginSettings> _plugins;
    private readonly DocumentWatch _watch = new();
    private SongDocument? _song;
    private List<Guid> _trackIds = new();
    private bool _disposed;

    public MainWindowTrackStructure(AvaloniaPlaybackSessions playback, Func<PluginSettings> plugins)
    {
        (_playback, _plugins) = (playback, plugins);
        _watch.Marked = _ => Check();
        _watch.Restored = () => Check();
    }

    /// <summary>Watches <paramref name="song"/>, the shown one, from its track list as it is now.</summary>
    public void Bind(SongDocument song)
    {
        _song = song;
        _trackIds = IdsOf(song);
        _watch.Bind(song);
    }

    public void Dispose() { _disposed = true; _watch.Dispose(); }

    /// <summary>Raised after the track list changed (add, delete, move, Undo, Redo) and the engine was brought in line; the instrument panel re-reads its track.</summary>
    public event Action? StructureChanged;

    /// <summary>Brings the engine and the playing schedule in line when the track list changed; true when it had.</summary>
    public bool Check()
    {
        if (_song is not { } song) return false;
        var ids = IdsOf(song);
        if (ids.SequenceEqual(_trackIds)) return false;
        var appended = ids.Count > _trackIds.Count && ids.Take(_trackIds.Count).SequenceEqual(_trackIds);
        var removed = _trackIds.Except(ids).ToList();
        _trackIds = ids;
        _playback.TracksChanged(song, _plugins(), syncEngine: !appended, removed);
        RaiseStructureChanged();
        return true;
    }

    // The subscribers touch controls, so the event is always raised on the UI thread (marshalled when Check ran elsewhere).
    private void RaiseStructureChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) StructureChanged?.Invoke();
        else Dispatcher.UIThread.Post(() => { if (!_disposed) StructureChanged?.Invoke(); });
    }

    private static List<Guid> IdsOf(SongDocument song) => song.Project.Tracks.Select(track => track.Id).ToList();
}

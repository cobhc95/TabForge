using System;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Playback;

namespace TabForge.Avalonia.Transport;

// Owns: noticing that the document's score changed (an edit, an undo or redo, a replaced song) and, once the edits rest for a moment, handing the playing
//   song to the engine to re-schedule from the edited score (PlaybackEditRefresh).
// Does not own: the decision what is audible (PlaybackEditDiff) or the re-schedule itself (PlaybackEngine).
// Tests: no dedicated test class; the engine side is covered by SelfTestPlaybackEditRefresh.
// Threads: UI thread only.
internal sealed class PlaybackEditWatch : IDisposable
{
    private readonly DocumentWatch _watch = new();
    private readonly IUiThread _ui;
    private int _armed;   // bumped by every edit and by Dispose: only the last wait's refresh runs
    private readonly SongDocument _document;
    private readonly AvaloniaPlaybackSession _session;
    private PlaybackEditRefresh? _refresh;   // made on the first edit of a playing song

    public PlaybackEditWatch(IUiThread ui, SongDocument document, AvaloniaPlaybackSession session)
    {
        (_ui, _document, _session) = (ui, document, session);
        _watch.Marked = _ => Arm();
        _watch.Restored = Arm;
        _watch.ProjectChanged = (_, _) => Arm();
        _watch.Bind(document);
    }

    // A burst of edits restarts the wait, so the song is re-scheduled once, after the last of them.
    private void Arm()
    {
        if (!_session.IsPlaying) return;   // a stopped song copies the edited score at its next start
        var armed = ++_armed;
        _ = Task.Delay(TimeSpan.FromMilliseconds(UiTiming.PlaybackEditDebounceMs)).ContinueWith(_ => _ui.Post(() => { if (armed == _armed) Refresh(); }), TaskScheduler.Default);
    }

    private void Refresh()
    {
        if (_session.IsPlaying && _session.Engine is PlaybackEngine engine) (_refresh ??= new PlaybackEditRefresh(engine)).Run(_document.Project);
    }

    public void Dispose()
    {
        _armed++;
        _watch.Dispose();
    }
}

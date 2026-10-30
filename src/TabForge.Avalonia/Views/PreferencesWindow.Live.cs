using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the Settings window's live application: marking edits changed, copying them into the live settings (WriteLive),
//   the once-per-frame run of pending edits (FlushLive) and the changes-link refresh.
// Does not own: the rows and cards (PreferencesPagePresenter), the live writer (PreferencesLiveWriter) or the closing question.
// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.
public sealed partial class PreferencesWindow
{
    private void MarkChanged()
    {
        _dirty = true;
        WriteLive(Working);
        RefreshChanges();
    }

    /// <summary>Refreshes the changes link; the status text is blank while changes wait (the link says so) and "All changes saved" otherwise.</summary>
    private void RefreshChanges()
    {
        _changes.Refresh();
        _status.Text = _changes.Count > 0 ? "" : SavedText;
    }

    /// <summary>Copies <paramref name="source"/> into the live settings (same object, new section values) and raises <see cref="Previewed"/>; the dock layout stays the live one.</summary>
    private void WriteLive(AppSettings source, bool preview = true)
    {
        _live.WriteAll(source, includeWorkspace: false);
        _rebuildPage = false;
        if (preview) Previewed?.Invoke();
    }

    /// <summary>The once-per-frame run of the edits made since the last one: only the edited rows reach the live settings, then one preview notice, change list and enabled state.</summary>
    private void FlushLive()
    {
        if (!_dirty || !_live.Pending) return;   // applied, written or cancelled since the edit
        _live.WritePending(Working, Rows, includeWorkspace: false);
        Previewed?.Invoke();
        _dirty = PreferencesPagePresenter.Changes(SavedRows(), Rows).Count > 0;   // an edit put back (a cancelled colour pick) leaves nothing to apply
        RefreshChanges();
        if (_rebuildPage) { _rebuildPage = false; DropRows(); Rebuild(); } else RefreshEnabled();
    }
}

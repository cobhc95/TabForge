using System;
using System.Collections.Generic;
using TabForge.Platform;

namespace TabForge.Avalonia.Views;

// Owns: the window's single top notice: the text, kind (Info/Warn/Error), optional one action and close X, the auto-hide of
//   transients (Info 5 s, Info with action 8 s) and the sticky list (Warn/Error and engine/autosave/approval/import/video sources stay
//   until dismissed, acted on or resolved; newest replaces the shown one; a timed-out transient restores the newest sticky;
//   the same sticky source updates in place, never duplicates).
// Does not own: the row (StatusNoticeBarView), the texts (the callers), or what the actions do.
// Tests: TabForge.Avalonia.Tests.NotificationBarTests.
// Threads: UI thread only; Show/HideSticky/Dismiss marshal via the UI thread when called elsewhere (engine/autosave callbacks).
/// <summary>The unified top bar. UI thread only (calls from elsewhere are marshalled).</summary>
public sealed class NotificationBar : IDisposable
{
    /// <summary>A transient Info notice hides after this long.</summary>
    public static readonly TimeSpan InfoTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A transient Info notice with an action (e.g. video saved with Open folder) hides after this long.</summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Sticky sources: the same source updates in place, never duplicates.</summary>
    public const string EngineSource = "engine";
    public const string AutosaveSource = "autosave";
    public const string ApprovalMediaSource = "approval-media";
    public const string ApprovalTrustSource = "approval-trust";
    public const string ImportSource = "import";
    public const string VideoSource = "video";
    public const string CheckBarsSource = "checkbars";
    public const string LearnSource = "learn";
    public const string MissingMediaSource = "missing-media";

    private sealed class Entry
    {
        public Entry(string text, NotificationKind kind, string? actionLabel, Action? action, string? source, string? tooltip)
        {
            Text = text; Kind = kind; ActionLabel = actionLabel; Action = action; Source = source; ToolTip = tooltip;
        }

        public string Text { get; set; }
        public NotificationKind Kind { get; set; }
        public string? ActionLabel { get; set; }
        public Action? Action { get; set; }
        public string? Source { get; }
        public string? ToolTip { get; set; }
    }

    private readonly StatusNoticeBarView _view;
    private readonly IUiThread _ui;
    private readonly Dictionary<string, Entry> _stickies = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private Entry? _current;
    private string? _currentSource;
    private IUiTimer? _timer;
    private EventHandler? _tick;
    private bool _disposed;

    /// <param name="view">The top-docked row this controller drives (the video-saved bar).</param>
    /// <param name="ui">The UI thread: timeouts are created on it (injectable for tests), and off-thread calls are posted to it.</param>
    public NotificationBar(StatusNoticeBarView view, IUiThread ui)
    {
        _view = view;
        _ui = ui;
        _view.CloseRequested += DismissCurrent;
        _view.SetAction(null, null);
        _view.SetKind(NotificationKind.Info);
    }

    /// <summary>The row this controller drives.</summary>
    public StatusNoticeBarView View => _view;

    /// <summary>The shown text, or null when hidden.</summary>
    public string? CurrentText => _current?.Text;

    /// <summary>The shown kind, or null when hidden.</summary>
    public NotificationKind? CurrentKind => _current?.Kind;

    /// <summary>The shown action label, or null when the bar has no action.</summary>
    public string? CurrentAction => _current?.ActionLabel;

    /// <summary>True while the row shows.</summary>
    public bool IsVisible => _view.IsVisible;

    /// <summary>How many sticky notices are kept (shown or stacked behind a transient).</summary>
    public int StickyCount => _stickies.Count;

    /// <summary>Shows a notice: newest replaces the shown one. A <paramref name="source"/> makes it sticky (updated in place when repeated);
    /// null is a transient (auto-hides, then the newest sticky returns). <paramref name="timeout"/> overrides the default
    /// (Info 5 s, Info with action 8 s, Warn/Error sticky). UI thread only (posted otherwise).</summary>
    public void Show(string text, NotificationKind kind, (string Label, Action Callback)? action = null, TimeSpan? timeout = null, string? source = null, string? tooltip = null)
    {
        if (_disposed) return;
        if (!_ui.CheckAccess())
        {
            _ui.Post(() => Show(text, kind, action, timeout, source, tooltip));
            return;
        }
        if (source is not null)
        {
            if (_stickies.TryGetValue(source, out var existing))
            {
                existing.Text = text;
                existing.Kind = kind;
                existing.ActionLabel = action?.Label;
                existing.Action = action?.Callback;
                existing.ToolTip = tooltip;
            }
            else
            {
                existing = new Entry(text, kind, action?.Label, action?.Callback, source, tooltip);
                _stickies[source] = existing;
                _order.Add(source);
            }
            StopTimer();
            _current = existing;
            _currentSource = source;
            Apply(existing);
            return;
        }
        var effective = timeout ?? (kind == NotificationKind.Info ? (action.HasValue ? ActionTimeout : InfoTimeout) : null);
        var entry = new Entry(text, kind, action?.Label, action?.Callback, null, tooltip);
        StopTimer();
        _current = entry;
        _currentSource = null;
        Apply(entry);
        if (effective.HasValue) StartTimer(effective.Value);
    }

    /// <summary>Resolves a sticky source (autosave fixed, approval done, import finished): removes it, restoring the next sticky or hiding.</summary>
    public void HideSticky(string source)
    {
        if (_disposed) return;
        if (!_ui.CheckAccess())
        {
            _ui.Post(() => HideSticky(source));
            return;
        }
        if (!_stickies.Remove(source)) return;
        _order.Remove(source);
        if (_currentSource == source) RestoreStickyOrHide();
    }

    /// <summary>Dismisses the shown notice (the close X): a sticky source is removed, a transient is dropped, then the newest sticky returns.</summary>
    public void DismissCurrent()
    {
        if (_disposed) return;
        if (!_ui.CheckAccess())
        {
            _ui.Post(DismissCurrent);
            return;
        }
        StopTimer();
        if (_currentSource is not null)
        {
            _stickies.Remove(_currentSource);
            _order.Remove(_currentSource);
        }
        _current = null;
        _currentSource = null;
        RestoreStickyOrHide();
    }

    /// <summary>Detaches the close handler and stops the timer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimer();
        _view.CloseRequested -= DismissCurrent;
    }

    private void Apply(Entry entry)
    {
        _view.SetKind(entry.Kind);
        if (entry.ActionLabel is not null && entry.Action is not null)
        {
            var captured = entry;
            _view.SetAction(captured.ActionLabel, () => OnAction(captured));
        }
        else
        {
            _view.SetAction(null, null);
        }
        _view.Show(entry.Text, entry.ToolTip ?? entry.Text);
    }

    private void OnAction(Entry entry)
    {
        var before = _current;
        try
        {
            entry.Action?.Invoke();
        }
        finally
        {
            // A transient action (video Open folder) goes after it runs; a sticky action (Retry/Review/Cancel) stays for its refresh.
            // When the callback already replaced the notice, the new one stands.
            if (ReferenceEquals(_current, before) && before is not null && before.Source is null) DismissCurrent();
        }
    }

    private void StartTimer(TimeSpan timeout)
    {
        StopTimer();
        var timer = _ui.CreateTimer(timeout, UiPriority.Background);
        _tick = (_, _) => OnTimeout();
        timer.Tick += _tick;
        timer.Start();
        _timer = timer;
    }

    private void StopTimer()
    {
        if (_timer is not null)
        {
            if (_tick is not null) _timer.Tick -= _tick;
            _timer.Stop();
            _timer = null;
            _tick = null;
        }
    }

    private void OnTimeout()
    {
        if (_disposed) return;
        // A sticky never times out; a replaced transient's old timer is already stopped.
        if (_currentSource is not null) return;
        StopTimer();
        _current = null;
        _currentSource = null;
        RestoreStickyOrHide();
    }

    private void RestoreStickyOrHide()
    {
        if (_order.Count > 0)
        {
            var source = _order[_order.Count - 1];
            if (_stickies.TryGetValue(source, out var entry))
            {
                _current = entry;
                _currentSource = source;
                Apply(entry);
                return;
            }
        }
        _current = null;
        _currentSource = null;
        _view.Hide();
    }
}

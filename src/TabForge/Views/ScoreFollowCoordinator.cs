using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Keeps the played music in view while playback runs (vertical system follow, horizontal row follow)
/// and stops following when the user scrolls by hand. Owns all follow state and its render-cadence
/// timer. It only observes playback (bar/fraction/paused come from the host) and never affects timing.
/// </summary>
internal sealed class ScoreFollowCoordinator
{
    private readonly ScrollViewer _scroll;
    private readonly TabEditorControl _editor;
    private readonly Func<bool> _isPlayingVisual;
    private readonly Func<bool> _isPaused;
    private readonly Func<int> _playheadBar;
    private readonly Func<double> _playheadFraction;
    private readonly Func<int> _barCount;
    private readonly Func<FollowSettings> _settings;
    private readonly Shell.FrameTicker _timer = new();

    private double _marginPercent = 20;
    private int _anticipation = 1;
    private bool _stopOnManualScroll = true;
    private bool _stopAtEnd = true;
    private bool _following = true;
    private double _setOffset = -1;
    private double _setHorizontalOffset = -1;
    private double _verticalTarget = -1;
    private int _horizontalSystem = -1;
    private DateTime _setAt = DateTime.MinValue;
    private DateTime _ignoreScrollUntil = DateTime.MinValue;
    private DateTime _lastFollowCheck = DateTime.MinValue;

    public ScoreFollowCoordinator(ScrollViewer scroll, TabEditorControl editor, Func<bool> isPlayingVisual,
        Func<bool> isPaused, Func<int> playheadBar, Func<double> playheadFraction, Func<int> barCount,
        Func<FollowSettings> settings)
    {
        _scroll = scroll;
        _editor = editor;
        _isPlayingVisual = isPlayingVisual;
        _isPaused = isPaused;
        _playheadBar = playheadBar;
        _playheadFraction = playheadFraction;
        _barCount = barCount;
        _settings = settings;
        _timer.Tick += (_, _) => Tick();
    }

    public string Mode { get; private set; } = FollowModes.Smooth;
    /// <summary>
    /// Follow style. False (default): page turn, the view stays still and jumps half a screen / to the next
    /// line near the edge. True: smooth page turn, the same turns but glided over a fraction of a second.
    /// </summary>
    public bool Continuous { get; private set; }
    private double _horizontalTarget = -1;
    public bool FollowFretboard { get; private set; } = true;

    public void ApplySettings(FollowSettings follow)
    {
        Mode = follow.Mode is FollowModes.Off or FollowModes.Jump or FollowModes.Smooth ? follow.Mode : FollowModes.Smooth;
        _marginPercent = Math.Clamp(follow.MarginPercent, 0, 60);
        _anticipation = Math.Clamp(follow.AnticipationBars, 0, 4);
        _stopOnManualScroll = follow.StopOnManualScroll;
        _stopAtEnd = follow.StopAtEnd;
        FollowFretboard = follow.FollowFretboard;
        Continuous = follow.ContinuousScroll;
        _timer.Interval = follow.MaxFps >= 240 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(follow.MaxFps, 10, 240));
        if (Mode == FollowModes.Off || !follow.VerticalFollow) _timer.Stop();
    }

    public void WriteSettings(FollowSettings follow)
    {
        follow.Mode = Mode;
        follow.MarginPercent = (int)Math.Round(_marginPercent);
        follow.AnticipationBars = _anticipation;
        follow.StopOnManualScroll = _stopOnManualScroll;
        follow.StopAtEnd = _stopAtEnd;
        follow.FollowFretboard = FollowFretboard;
        follow.ContinuousScroll = Continuous;
        follow.MaxFps = _timer.Interval == TimeSpan.Zero ? 240 : (int)Math.Round(1000.0 / _timer.Interval.TotalMilliseconds);
    }

    /// <summary>Stops any pending smooth scroll (pause, stop, tab switch, close).</summary>
    public void Halt()
    {
        _timer.Stop();
        _verticalTarget = -1;
        _horizontalTarget = -1;
    }

    /// <summary>A fresh play: follow again from the current row.</summary>
    public void ResetForPlayback()
    {
        Halt();
        _following = true;
        _horizontalSystem = -1;
        _lastFollowCheck = DateTime.MinValue;
    }

    /// <summary>Resuming from pause re-enables following after a manual scroll.</summary>
    public void Resume() => _following = true;

    /// <summary>Forget the row used for horizontal follow (playback stopped, finished or re-attached).</summary>
    public void ResetRow() => _horizontalSystem = -1;

    /// <summary>Moves the page to <paramref name="offset"/> for a cursor jump and re-enables following.</summary>
    public void JumpTo(double offset)
    {
        Halt();
        SetOffset(offset);
        _following = true;
    }

    /// <summary>The editor reports the bar being played; keep it in view.</summary>
    public void OnPlayheadBar(int bar)
    {
        if (Mode == FollowModes.Off || !_isPlayingVisual() || _isPaused()) return;
        var now = DateTime.UtcNow;
        if ((now - _lastFollowCheck).TotalMilliseconds < 50) return;
        _lastFollowCheck = now;
        if (Continuous) { QueueSmoothTurns(bar); return; }
        FollowHorizontally(bar);
        QueueVertical(bar);
    }

    /// <summary>True while playback follow is armed (a manual scroll or Off mode clears it).</summary>
    internal bool IsFollowing => _following;

    /// <summary>
    /// Layout/zoom offset changes are not user scrolling and must not suspend playback follow. The zoom
    /// pass rewrites the offsets later than the layout change (and its re-anchor can be delayed while
    /// playback keeps the dispatcher busy), so scroll detection stays off until the re-anchor has run,
    /// not for a fixed time. A layout pass that lands inside a pending one never captures a state that
    /// the zoom's own scroll already cleared.
    /// </summary>
    public void OnScoreLayoutChanging()
    {
        var now = DateTime.UtcNow;
        if (!_layoutPending && now >= _ignoreScrollUntil) _followingBeforeLayout = _following;
        _layoutPending = true;
        _layoutPendingSince = now;
        _ignoreScrollUntil = now.AddMilliseconds(500);
        _horizontalSystem = -1;
        Halt();
        _lastFollowCheck = DateTime.MinValue;
    }

    private bool _followingBeforeLayout = true;
    private bool _layoutPending;
    private DateTime _layoutPendingSince = DateTime.MinValue;

    /// <summary>
    /// After a zoom the scroll offsets are rewritten (later than the layout change itself): treat that as
    /// layout, restore the follow state from before the zoom and re-anchor on the playhead.
    /// </summary>
    public void ReanchorAfterZoom()
    {
        _layoutPending = false;
        _ignoreScrollUntil = DateTime.UtcNow.AddMilliseconds(600);
        _following = _followingBeforeLayout;
        _horizontalSystem = -1;
        _horizontalTarget = -1;
        _verticalTarget = -1;
        _lastFollowCheck = DateTime.MinValue;
        if (_isPlayingVisual() && !_isPaused()) OnPlayheadBar(_playheadBar());
    }

    /// <summary>
    /// Following is a courtesy, not a cage: if the player scrolls by hand we stop moving the page until
    /// they press play again or move the cursor (a hand scroll shows as a scrollbar position we did not set).
    /// </summary>
    public void OnScrollChanged(ScrollChangedEventArgs e)
    {
        if (!_stopOnManualScroll || !_isPlayingVisual()) return;
        if (Math.Abs(e.VerticalChange) < 0.5 && Math.Abs(e.HorizontalChange) < 0.5) return;
        // Only a genuine scroll gesture (wheel without Ctrl, scrollbar drag/click, scroll keys, touch pan)
        // may stop follow. ScrollChanged from zoom, reflow, panel or extent changes never can, however late
        // the layout pass lands.
        if (!UserScrollGestureActive) return;
        if (DateTime.UtcNow < _ignoreScrollUntil) return;
        if (_layoutPending && (DateTime.UtcNow - _layoutPendingSince).TotalSeconds < 5) return; // zoom/layout not re-anchored yet
        if ((DateTime.UtcNow - _setAt).TotalMilliseconds < 150) return; // our own scroll, still settling

        // A re-layout (score reflow, panel or window resize) moves the offset too; that is not the user.
        var verticalLayoutChange = Math.Abs(e.ExtentHeightChange) >= 0.5 || Math.Abs(e.ViewportHeightChange) >= 0.5;
        var manualVertical = Math.Abs(e.VerticalChange) >= 0.5 && !verticalLayoutChange &&
            (_setOffset < 0 || Math.Abs(e.VerticalOffset - _setOffset) >= 2);
        var horizontalLayoutChange = Math.Abs(e.ExtentWidthChange) >= 0.5 || Math.Abs(e.ViewportWidthChange) >= 0.5;
        var manualHorizontal = Math.Abs(e.HorizontalChange) >= 0.5 && !horizontalLayoutChange &&
            (_setHorizontalOffset < 0 || Math.Abs(e.HorizontalOffset - _setHorizontalOffset) >= 2);
        if (manualVertical || manualHorizontal)
        {
            _following = false;
            Halt();
        }
    }

    private DateTime _userScrollAt = DateTime.MinValue;
    private bool _scrollBarDrag;

    /// <summary>The user just made a scroll gesture on the score (wheel without Ctrl, scroll key, touch pan).</summary>
    public void NoteUserScrollGesture() => _userScrollAt = DateTime.UtcNow;

    /// <summary>The user pressed (true) or released (false) the mouse on the score's scrollbar.</summary>
    public void SetScrollBarDrag(bool dragging)
    {
        _scrollBarDrag = dragging;
        if (dragging) NoteUserScrollGesture();
    }

    // The scroll a gesture causes arrives within a frame or two; 300 ms also covers a smooth-wheel glide.
    private bool UserScrollGestureActive =>
        _scrollBarDrag || (DateTime.UtcNow - _userScrollAt).TotalMilliseconds < 300;

    private bool CanFollowVertically =>
        Mode != FollowModes.Off && _settings().VerticalFollow && _following && _isPlayingVisual() && !_isPaused();

    /// <summary>Queues a vertical scroll only when the active system leaves its comfortable viewport band.</summary>
    private void QueueVertical(int bar)
    {
        if (!CanFollowVertically || bar < 0) return;
        if (_stopAtEnd && EndOfScoreVisible(Math.Max(1, _barCount()), bar)) return;
        if (NextVerticalOffset(bar) is not double target) return;
        _verticalTarget = target;
        if (Mode == FollowModes.Jump)
        {
            SetOffset(target);
            _verticalTarget = -1;
            _timer.Stop();
        }
        else _timer.Start();
    }

    /// <summary>Animates a threshold-triggered vertical follow target at the render cadence.</summary>
    private void Tick()
    {
        if (Continuous) { ContinuousTick(); return; }
        var bar = _playheadBar();
        if (!CanFollowVertically || bar < 0 || (_stopAtEnd && EndOfScoreVisible(Math.Max(1, _barCount()), bar)))
        {
            Halt();
            return;
        }
        if (NextVerticalOffset(bar) is double nextTarget) _verticalTarget = nextTarget;
        if (_verticalTarget < 0) { _timer.Stop(); return; }

        var offset = _scroll.VerticalOffset;
        var delta = _verticalTarget - offset;
        if (Math.Abs(delta) < 0.5) { Halt(); return; }
        // Ease toward the target without scrolling on each tiny playhead movement.
        var step = delta * 0.22;
        if (Math.Abs(step) < 0.6) step = Math.Sign(delta) * 0.6;
        SetOffset(offset + step);
    }

    // Smooth page turn: the same half-page (horizontal) and next-line (vertical) turns as the default
    // style, but each one glides over ~0.2 s. The frame ticker runs only while a glide is under way.
    private void QueueSmoothTurns(int bar)
    {
        if (!_following || bar < 0) return;
        var settings = _settings();
        if (settings.HorizontalFollow && _scroll.ViewportWidth > 1 &&
            _editor.PlaybackHorizontalGeometry(bar, _playheadFraction()) is { } row)
        {
            var from = _horizontalTarget >= 0 ? _horizontalTarget : _scroll.HorizontalOffset;
            if (_horizontalSystem >= 0 && row.SystemIndex != _horizontalSystem) from = 0; // new line starts at the left
            _horizontalSystem = row.SystemIndex;
            var next = ScoreHorizontalFollow.NextOffset(from, _scroll.ViewportWidth,
                row.PlayheadX, row.BarWidth, row.SystemRight, _anticipation * 0.75);
            var target = next ?? from;
            if (Math.Abs(target - _scroll.HorizontalOffset) > 0.5) _horizontalTarget = target;
        }
        if (CanFollowVertically && !(_stopAtEnd && EndOfScoreVisible(Math.Max(1, _barCount()), bar)) &&
            NextVerticalOffset(bar) is double vertical)
            _verticalTarget = vertical;
        if (_horizontalTarget >= 0 || _verticalTarget >= 0) _timer.Start();
    }

    private void ContinuousTick()
    {
        if (Mode == FollowModes.Off || !_following || !_isPlayingVisual() || _isPaused())
        {
            _horizontalTarget = -1;
            Halt();
            return;
        }
        var moving = false;
        if (_horizontalTarget >= 0)
        {
            var next = Glide(_scroll.HorizontalOffset, _horizontalTarget);
            if (next is double x) { SetHorizontalOffset(x); moving = true; } else { SetHorizontalOffset(_horizontalTarget); _horizontalTarget = -1; }
        }
        if (_verticalTarget >= 0)
        {
            var next = Glide(_scroll.VerticalOffset, _verticalTarget);
            if (next is double y) { SetOffset(y); moving = true; } else { SetOffset(_verticalTarget); _verticalTarget = -1; }
        }
        if (!moving) _timer.Stop(); // idle between turns: no per-frame work
    }

    // One eased step (about 0.2 s to settle at 120 Hz+); null once close enough to land.
    private static double? Glide(double current, double target)
    {
        var delta = target - current;
        if (Math.Abs(delta) < 0.75) return null;
        var step = delta * 0.2;
        if (Math.Abs(step) < 0.75) step = Math.Sign(delta) * 0.75;
        return current + step;
    }

    private double? NextVerticalOffset(int bar)
    {
        var viewport = _scroll.ViewportHeight;
        if (viewport <= 1) return null;
        return ScoreVerticalFollow.NextOffset(_scroll.VerticalOffset, viewport,
            _editor.SystemTopForMeasure(bar), _editor.SystemHeightNow, _scroll.ExtentHeight,
            _marginPercent, _settings().VerticalTriggerPercent);
    }

    /// <summary>True once the last system fits entirely in the usable score viewport.</summary>
    private bool EndOfScoreVisible(int barCount, int activeBar)
    {
        var lastTop = _editor.SystemTopForMeasure(barCount - 1);
        // Keep the end-of-song stop rule from blocking an upward follow after a backward seek.
        if (_editor.SystemTopForMeasure(activeBar) < lastTop - 0.5) return false;
        var offset = _scroll.VerticalOffset;
        return lastTop >= offset && lastTop + _editor.SystemHeightNow <= offset + _scroll.ViewportHeight;
    }

    /// <summary>
    /// The score stays still until the playhead enters the trailing 0.75-bar zone, then advances by
    /// half a viewport. A row transition returns to the row's left edge so the opening notation is
    /// visible again, rather than carrying a stale offset from the previous row.
    /// </summary>
    private void FollowHorizontally(int bar)
    {
        if (!_settings().HorizontalFollow || !_following || !_isPlayingVisual() || _isPaused() || bar < 0 || _scroll.ViewportWidth <= 1)
            return;
        if (_editor.PlaybackHorizontalGeometry(bar, _playheadFraction()) is not { } row) return;

        var offset = _scroll.HorizontalOffset;
        if (_horizontalSystem >= 0 && row.SystemIndex != _horizontalSystem)
        {
            SetHorizontalOffset(0);
            offset = 0;
        }
        _horizontalSystem = row.SystemIndex;

        var next = ScoreHorizontalFollow.NextOffset(offset, _scroll.ViewportWidth,
            row.PlayheadX, row.BarWidth, row.SystemRight, _anticipation * 0.75);
        if (next is double target && Math.Abs(target - offset) > 0.5)
            SetHorizontalOffset(target);
    }

    /// <summary>Scrolls and remembers the offset, so a manual scroll can be told apart from ours.</summary>
    private void SetOffset(double y)
    {
        y = Math.Max(0, y);
        _setOffset = y;
        _setAt = DateTime.UtcNow;
        _scroll.ScrollToVerticalOffset(y);
    }

    private void SetHorizontalOffset(double x)
    {
        x = Math.Max(0, x);
        _setHorizontalOffset = x;
        _setAt = DateTime.UtcNow;
        _scroll.ScrollToHorizontalOffset(x);
    }
}

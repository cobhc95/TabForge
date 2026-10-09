using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Keeps the played music in view while playback runs (vertical system follow, horizontal row follow)
/// and stops following when the user scrolls by hand. Owns all follow state and its render-cadence
/// timer. It only observes playback (bar/fraction/paused come from the host) and never affects timing.
/// While stopped it also brings the cursor bar back on screen after any edit or cursor move.
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
    private int _lastBar = -1;
    private double _lastExtent = -1;
    private bool _followPending;

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
        // Any cursor move or edit (typing with auto-advance, paste, bar insert/delete, undo, a relayout) keeps the cursor bar on screen.
        _editor.Edited += (_, _) => KeepCursorInSight();
        _editor.SelectionChanged += (_, _) => KeepCursorInSight();
    }

    private bool _sightPending;

    /// <summary>
    /// Once the current action is done (and laid out), scrolls to the cursor bar when less than a quarter of its system is on
    /// screen. Playback has its own follow; one-line mode scrolls on keys (MainWindow.ScrollToCursor).
    /// </summary>
    public void KeepCursorInSight()
    {
        if (_sightPending) return;
        _sightPending = true;
        _scroll.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            _sightPending = false;
            if (_isPlayingVisual() || _editor.HorizontalScroll || _editor.Track is not { Measures.Count: > 0 } || _scroll.ViewportHeight <= 1) return;
            var top = _editor.SystemTopForMeasure(_editor.SelectedMeasure);
            var height = _editor.SystemHeightNow;
            var shown = Math.Min(top + height, _scroll.VerticalOffset + _scroll.ViewportHeight) - Math.Max(top, _scroll.VerticalOffset);
            var quarter = Math.Min(height, _scroll.ViewportHeight) / 4;
            if (shown >= quarter) return;
            // The usual target keeps headings above the system in view; a pane too short for both shows the system itself.
            var offset = _editor.ScrollOffsetForMeasure(_editor.SelectedMeasure);
            JumpTo(top < offset || top + quarter > offset + _scroll.ViewportHeight ? top : offset);
        });
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
        _glideAt = DateTime.MinValue;
        _timer.Stop();
        _verticalTarget = -1;
        _horizontalTarget = -1;
        _followPending = false;
    }

    /// <summary>A fresh play, or a playing song shown again (tab switch back): follow again from its playhead.</summary>
    public void ResetForPlayback()
    {
        Halt();
        _following = true;
        _horizontalSystem = -1;
        _lastBar = -1;
        _lastFollowCheck = DateTime.MinValue;
        ArmSnap();
    }

    // A relayout, zoom, tab or track switch puts the page at the playhead in one step: turns inside this window jump, not glide.
    private DateTime _snapUntil = DateTime.MinValue;
    private void ArmSnap() => _snapUntil = DateTime.UtcNow.AddMilliseconds(600);
    private bool Snapping => DateTime.UtcNow < _snapUntil;

    /// <summary>Resuming from pause re-enables following after a manual scroll.</summary>
    public void Resume()
    {
        if (!_following) Services.Trace.Write("ui", "follow resumed by play");
        _following = true;
    }

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
        if (bar >= 0)
        {
            // A loop wrap, repeat or seek reads as a backward or non-contiguous bar: follow the playhead again.
            if (!_following && _lastBar >= 0 && (bar < _lastBar || bar > _lastBar + 1))
            {
                _following = true;
                Services.Trace.Write("ui", $"follow resumed: playhead moved to bar {bar + 1}");
            }
            _lastBar = bar;
        }
        // A relayout moves the systems under the playhead: forget the cached row and targets.
        if (Math.Abs(_scroll.ExtentHeight - _lastExtent) >= 0.5)
        {
            _lastExtent = _scroll.ExtentHeight;
            ForgetLayoutCache();
        }
        var now = DateTime.UtcNow;
        // A check inside the 50 ms window is kept and applied by the next render tick, not dropped.
        if ((now - _lastFollowCheck).TotalMilliseconds < 50)
        {
            _followPending = true;
            _timer.Start();
            return;
        }
        _lastFollowCheck = now;
        ApplyPlayhead(bar);
    }

    private void ApplyPlayhead(int bar)
    {
        if (Continuous) { QueueSmoothTurns(bar); return; }
        FollowHorizontally(bar);
        QueueVertical(bar);
    }

    private void ForgetLayoutCache()
    {
        ArmSnap();
        _horizontalSystem = -1;
        _horizontalTarget = -1;
        _verticalTarget = -1;
        _lastFollowCheck = DateTime.MinValue;
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
        ArmSnap();
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
        ArmSnap();
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
            if (_following) Services.Trace.Write("ui", "follow paused by a manual scroll");
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
        _verticalTarget = -1;
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

    internal void PumpForTest() => Tick();

    /// <summary>Animates a threshold-triggered vertical follow target at the render cadence.</summary>
    private void Tick()
    {
        if (_followPending)
        {
            if ((DateTime.UtcNow - _lastFollowCheck).TotalMilliseconds < 50) return;
            _followPending = false;
            _lastFollowCheck = DateTime.UtcNow;
            ApplyPlayhead(_playheadBar());
        }
        if (Continuous) { ContinuousTick(); return; }
        var bar = _playheadBar();
        if (!CanFollowVertically || bar < 0 || (_stopAtEnd && EndOfScoreVisible(Math.Max(1, _barCount()), bar)))
        {
            Halt();
            return;
        }
        // A target the playhead no longer needs stops the ease at once, instead of gliding to a stale target.
        _verticalTarget = NextVerticalOffset(bar) ?? -1;
        if (_verticalTarget < 0) { _timer.Stop(); return; }

        var offset = _scroll.VerticalOffset;
        var delta = _verticalTarget - offset;
        if (Math.Abs(delta) < 0.5) { Halt(); return; }
        SetOffset(VerticalGlideNext(offset, _verticalTarget));
    }

    // Smooth page turn: the same half-page (horizontal) and next-line (vertical) turns as the default
    // style, but each one glides over ~0.2 s. The frame ticker runs only while a glide is under way.
    private void QueueSmoothTurns(int bar)
    {
        if (!_following || bar < 0) return;
        var settings = _settings();
        if (settings.HorizontalFollow && _scroll.ViewportWidth > 1 &&
            _editor.Playback.HorizontalGeometry(bar, _playheadFraction()) is { } row)
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
            var next = Snapping ? null : Glide(_scroll.HorizontalOffset, _horizontalTarget);
            if (next is double x) { SetHorizontalOffset(x); moving = true; } else { SetHorizontalOffset(_horizontalTarget); _horizontalTarget = -1; }
        }
        if (_verticalTarget >= 0)
        {
            var y = VerticalGlideNext(_scroll.VerticalOffset, _verticalTarget);
            SetOffset(y);
            if (y == _verticalTarget) _verticalTarget = -1; else moving = true;
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

    // Vertical turns glide by elapsed time (a turn lands in ~250 ms at any frame rate), not by frame count.
    private DateTime _glideAt = DateTime.MinValue;

    private double VerticalGlideNext(double current, double target)
    {
        if (Snapping) return target;
        var now = DateTime.UtcNow;
        var dt = _glideAt == DateTime.MinValue ? 1.0 / 60 : Math.Min((now - _glideAt).TotalSeconds, 0.1);
        _glideAt = now;
        return ScoreVerticalFollow.GlideStep(current, target, dt);
    }

    private double? NextVerticalOffset(int bar)
    {
        var viewport = _scroll.ViewportHeight;
        if (viewport <= 1) return null;
        return ScoreVerticalFollow.NextOffset(_scroll.VerticalOffset, viewport,
            _editor.SystemTopForMeasure(bar), _editor.SystemHeightNow, _scroll.ExtentHeight,
            _marginPercent, _settings().VerticalTriggerPercent);
    }

    /// <summary>True when the whole system holding <paramref name="bar"/> is inside the score viewport (keyboard moves scroll only when it is not).</summary>
    public bool BarInView(int bar)
    {
        var top = _editor.SystemTopForMeasure(bar);
        var offset = _scroll.VerticalOffset;
        return top >= offset && top + _editor.SystemHeightNow <= offset + _scroll.ViewportHeight;
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
        if (_editor.Playback.HorizontalGeometry(bar, _playheadFraction()) is not { } row) return;

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
        _scroll.ScrollToVerticalOffset(y);
        // The viewer stops at its extent, which can be behind the request: remember the offset it will show.
        _setOffset = Math.Min(y, Math.Max(0, _scroll.ExtentHeight - _scroll.ViewportHeight));
        _setAt = DateTime.UtcNow;
    }

    private void SetHorizontalOffset(double x)
    {
        x = Math.Max(0, x);
        _scroll.ScrollToHorizontalOffset(x);
        _setHorizontalOffset = Math.Min(x, Math.Max(0, _scroll.ExtentWidth - _scroll.ViewportWidth));
        _setAt = DateTime.UtcNow;
    }
}

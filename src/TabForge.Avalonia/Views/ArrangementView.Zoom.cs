using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The arrangement's wheel, pinch and zoom behaviour: the frame-driven zoom preview, its settle timer, the Shift and Alt wheel and the anchored zoom.
/// Owns: the zoom and wheel state of <see cref="ArrangementView"/>. Does not own: the zoom maths (TimelineZoomGesture) or the rest of the view.
/// Tests: TabForge.Avalonia.Tests.ArrangementViewTests.
/// </summary>
public sealed partial class ArrangementView
{
    private readonly ScrollViewer _sideways;

    /// <summary>
    /// Raised once when a wheel zoom has settled (no tick for 150 ms) with the factor it ended at; the window saves it. The ticks before it only preview.
    /// </summary>
    public event Action<double>? ZoomSettled;

    private readonly TimelineZoomGesture _zoomGesture = new();
    private IUiTimer? _zoomSettle;
    private readonly Action<Action> _requestFrame;
    // Wheel notches and the latest pointer x waiting for the next frame; one frame applies them all with one relayout.
    private double _pendingNotches, _pendingAnchor, _pendingPinch = 1, _lastPinchScale = 1;
    private bool _frameRequested, _zoomPreviewing, _zoomDetached, _ownsZoomSettle;

    /// <summary>How many frames applied a wheel zoom preview (one relayout each).</summary>
    internal int ZoomFramesApplied { get; private set; }

    private void RequestDisplayFrame(Action callback)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(_ => callback());
        else global::Avalonia.Threading.Dispatcher.UIThread.Post(callback, global::Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>A wheel zoom of <paramref name="notches"/> (positive in) around the viewport x <paramref name="anchorX"/>: queued for the next frame, which previews every tick that arrived since with one relayout; the settle timer restarts.</summary>
    internal void ZoomByWheel(double notches, double anchorX)
    {
        _pendingNotches += notches;
        QueueZoom(anchorX);
    }

    /// <summary>A pinch step whose finger spread changed by <paramref name="scale"/> (1 = unchanged) around the viewport x <paramref name="anchorX"/>: joins the wheel's frame-driven path (one relayout per frame, one settle).</summary>
    internal void ZoomByPinch(double scale, double anchorX)
    {
        if (!double.IsFinite(scale) || scale <= 0) return;
        _pendingPinch *= scale;
        QueueZoom(anchorX);
    }

    // The pinch event carries the scale since the gesture began; the step is its ratio to the last one. The centre is in this view's coordinates.
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_sideways.TranslatePoint(default, this) is not { } lanesOrigin) return;
        var step = _lastPinchScale > 0 ? e.Scale / _lastPinchScale : 1;
        _lastPinchScale = e.Scale;
        ZoomByPinch(step, e.ScaleOrigin.X - lanesOrigin.X);
        e.Handled = true;
    }

    // The view left the tree: the settle timer stops and unhooks (and the one this view made is dropped), and a frame already asked for does nothing.
    private void StopZoomSettle()
    {
        _zoomDetached = true;
        if (_zoomSettle is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnZoomSettled;
            if (_ownsZoomSettle) { (timer as IDisposable)?.Dispose(); _zoomSettle = null; _ownsZoomSettle = false; }
        }
        _pendingNotches = 0;
        _pendingPinch = 1;
        _frameRequested = false;
        _zoomPreviewing = false;
    }

    private void QueueZoom(double anchorX)
    {
        _zoomDetached = false;
        _pendingAnchor = anchorX;
        _zoomPreviewing = true;
        if (_zoomSettle is null) { _zoomSettle = AvaloniaUiThread.ForApplication().CreateTimer(TimeSpan.FromMilliseconds(150)); _ownsZoomSettle = true; }
        _zoomSettle.Stop();
        _zoomSettle.Tick -= OnZoomSettled;
        _zoomSettle.Tick += OnZoomSettled;
        _zoomSettle.Start();
        if (_frameRequested) return;
        _frameRequested = true;
        _requestFrame(ApplyPendingZoom);
    }

    private void ApplyPendingZoom()
    {
        _frameRequested = false;
        if (_zoomDetached || (_pendingNotches == 0 && _pendingPinch == 1)) return;
        var factor = _zoomGesture.Pinch(_zoomGesture.Wheel(Timeline.ZoomFactor, _pendingNotches), _pendingPinch);
        _pendingNotches = 0;
        _pendingPinch = 1;
        if (ApplyZoom(factor, _pendingAnchor)) ZoomFramesApplied++;
    }

    private void OnZoomSettled(object? sender, EventArgs e)
    {
        _zoomSettle?.Stop();
        if (_zoomDetached) return;
        if (_frameRequested || _pendingNotches != 0 || _pendingPinch != 1) ApplyPendingZoom();
        _zoomPreviewing = false;
        Timeline.SetWaveZooming(false);
        Timeline.InvalidateVisual();   // the waveforms of the visible range rebuild sharp at the settled scale
        ZoomSettled?.Invoke(Timeline.ZoomFactor);
    }

    /// <summary>Horizontal distance one Shift + wheel notch scrolls the timeline, in DIPs.</summary>
    private const double SidewaysWheelStep = 60;

    // A wheel whose pointer is over the lanes (the sideways viewer's area) is the timeline's; any other wheel, such as one over the track list, passes on.
    private void OnArrangementWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_sideways.TranslatePoint(default, this) is not { } lanesOrigin) return;
        if (!new Rect(lanesOrigin, _sideways.Bounds.Size).Contains(e.GetPosition(this))) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0) NoteHandScroll();
        OnTimelineWheel(e);
    }

    // Over the timeline a wheel zooms around the pointer; Shift + wheel scrolls the bars sideways. Track rows scroll from the header column instead.
    // Alt + wheel over an N>=4 repeat region steps its shown pass (clamped, no wrap) and is consumed; plain wheel and Ctrl/Shift keep their behaviour.
    private void OnTimelineWheel(PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasPrimary() && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var region = TimelineRepeatPicker.RegionAtX(Timeline, e.GetPosition(Timeline).X);
            if (region is not null)
            {
                var direction = e.Delta.Y > 0 ? 1 : -1;
                var current = Timeline.RepeatShown.ShownPass(region.Value);
                var next = Math.Clamp(current + direction, 1, region.Value.PassCount);
                if (next != current)
                {
                    Timeline.RepeatShown.SetPass(region.Value, next);
                    Timeline.InvalidateVisual();
                }
                e.Handled = true;
                return;
            }
        }
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _sideways.Offset = new Vector(Math.Max(0, _sideways.Offset.X - e.Delta.Y * SidewaysWheelStep), _sideways.Offset.Y);
            return;
        }
        ZoomByWheel(e.Delta.Y, e.GetPosition(_sideways).X);
    }

    /// <summary>
    /// Sets the timeline's bar zoom and scrolls so the bar under <paramref name="anchorX"/> (the timeline viewport's x; the middle when null) stays under it.
    /// </summary>
    public void SetTimelineZoom(double factor, double? anchorX = null)
    {
        ApplyZoom(factor, anchorX);
        if (!_zoomPreviewing) Timeline.SetWaveZooming(false);
    }

    // One relayout at the new factor, then the scroll that keeps the bar under the anchor. Cached waveform outlines draw scaled (SetWaveZooming) until the settle.
    // True when the factor changed (a relayout ran); a clamped step changes nothing.
    private bool ApplyZoom(double factor, double? anchorX)
    {
        var before = Timeline.ZoomFactor;
        var width = Timeline.BarsWidth;
        var viewport = _sideways.Viewport.Width;
        var anchor = Math.Clamp(anchorX ?? viewport / 2, 0, viewport);
        var offset = _sideways.Offset.X;
        Timeline.SetWaveZooming(true);
        Timeline.ZoomFactor = factor;
        _sideways.UpdateLayout();
        _sideways.Offset = new Vector(_zoomGesture.AnchoredOffset(offset, anchor, width, Timeline.BarsWidth), _sideways.Offset.Y);
        return Timeline.ZoomFactor != before;
    }
}

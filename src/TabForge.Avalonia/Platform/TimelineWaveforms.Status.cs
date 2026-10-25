using System;
using Avalonia;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: what an audio clip shows while it has no waveform: a moving loading stripe, a lock with "Click to allow access", or a warning glyph; the
//   status of a clip file in the shown document media scope.
// Does not own: the outline (TimelineWaveforms), the click that approves (TimelineView.TryRequestApproval) or the retry (the clip menu).
// Tests: TabForge.Avalonia.Tests.TimelineWaveStatusTests.
public sealed partial class TimelineWaveforms
{
    /// <summary>The hover text of a clip whose file could not be read.</summary>
    public const string FailedTip = "Could not read this file (format?)";
    /// <summary>The words beside the lock of a clip whose file waits for approval.</summary>
    public const string ApproveText = "Click to allow access";
    /// <summary>The warning glyph of a clip whose file could not be read.</summary>
    public const string WarningGlyph = "⚠";

    private const double StatusInset = 6;
    private const double StatusTextPoints = 11;
    private const double StripeBand = 48;
    private const double StripeOpacity = 0.16;
    private const int StripeCycleMs = 1600;
    private const int StripeRedrawMs = 90;
    private const double LockBodyWidth = 9, LockBodyHeight = 7, LockShackleRadius = 3;
    private const double LockOpacity = 0.85;
    private const double LockStrokeWidth = 1.4;

    private bool _stripeScheduled;
    private UiText? _approveText, _warningText;
    private UiColor _approveInk, _warningInk;

    /// <summary>Why <paramref name="clip"/> has no waveform (or that it has one) in the shown document media scope.</summary>
    public WaveStatus StatusOf(AudioClip clip) => _service.StatusOf(clip.File, _media());

    // Only the part of the clip inside the scrolled-in span draws its status; a clip off screen costs nothing.
    private void DrawStatus(IDrawContext d, WaveState state, Rect box, UiColor ink)
    {
        if (state == WaveState.Ready) return;
        var from = box.X;
        var to = box.Right;
        if (_viewWidth > 0) { from = Math.Max(from, _viewFrom); to = Math.Min(to, _viewFrom + _viewWidth); }
        if (to - from < 1) return;
        switch (state)
        {
            case WaveState.Loading: DrawLoading(d, box, from, to, ink); break;
            case WaveState.NeedsApproval: DrawApproval(d, box, from, to, ink); break;
            default: DrawWarning(d, box, from, to, ink); break;
        }
    }

    // A soft band sweeping across the visible clip; no allocation per frame (the redraw is one timer tick every ~90 ms while a clip loads).
    private void DrawLoading(IDrawContext d, Rect box, double from, double to, UiColor ink)
    {
        var phase = Environment.TickCount64 % StripeCycleMs / (double)StripeCycleMs;
        var x = from - StripeBand + phase * (to - from + StripeBand);
        d.PushClip(from, box.Y, to - from, box.Height);
        d.Rect(ink.WithOpacity(StripeOpacity), null, x, box.Y, StripeBand, box.Height);
        d.Pop();
        if (_stripeScheduled) return;
        _stripeScheduled = true;
        _ = System.Threading.Tasks.Task.Delay(StripeRedrawMs).ContinueWith(_ => _ui.Post(() => { _stripeScheduled = false; _redraw(); }), System.Threading.Tasks.TaskScheduler.Default);
    }

    private void DrawApproval(IDrawContext d, Rect box, double from, double to, UiColor ink)
    {
        var x = from + StatusInset;
        var y = box.Y + box.Height / 2 - LockBodyHeight / 2 + LockShackleRadius / 2;
        var fill = ink.WithOpacity(LockOpacity);
        d.Rect(fill, null, x, y, LockBodyWidth, LockBodyHeight, 1);
        d.Ellipse(null, new UiStroke(fill, LockStrokeWidth), x + LockBodyWidth / 2, y, LockShackleRadius, LockShackleRadius * 1.1);
        if (_approveText is null || _approveInk != ink) { _approveText = d.Text(ApproveText, TimelineView.Font, StatusTextPoints, ink, d.PixelsPerDip); _approveInk = ink; }
        var text = _approveText.Value;
        var textX = x + LockBodyWidth + StatusInset;
        if (textX + text.Width <= to) d.DrawText(text, textX, box.Y + box.Height / 2 - text.Height / 2);
    }

    private void DrawWarning(IDrawContext d, Rect box, double from, double to, UiColor ink)
    {
        if (_warningText is null || _warningInk != ink) { _warningText = d.Text(WarningGlyph, TimelineView.Font, StatusTextPoints + 3, ink, d.PixelsPerDip); _warningInk = ink; }
        var text = _warningText.Value;
        d.DrawText(text, from + StatusInset, box.Y + box.Height / 2 - text.Height / 2);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TabForge.Views.Band;

// Owns: dragging a row by its name strip: the row follows the pointer, the others glide aside, and a drop slides the row into its slot.
//   Every movement is a render-transform change (animated on the render thread); the rows are cached bitmaps while one is held, so
//   no layout runs and no lane is engraved during the drag. Near the top or bottom edge of the viewport the list scrolls by itself.
// Does not own: the order itself (BandLayoutState decides it from the drop, the controller applies it) or the rows' content.
// Tests: TestBandReorder, TestBandReorderAutoScroll.
internal sealed class BandReorder
{
    private const double StartDistance = 4;
    private static readonly Duration Glide = new(TimeSpan.FromMilliseconds(120));

    /// <summary>Height of the strip at each edge of the viewport where a held row scrolls the list, and the most it scrolls per tick.</summary>
    internal const double EdgeZone = 40;
    internal const double MaxScrollStep = 18;

    private readonly Panel _stack;
    private readonly ScrollViewer _viewport;
    private readonly DispatcherTimer _autoScroll = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Func<IReadOnlyList<BandRow>> _rows;
    private BandRow? _row;
    private bool _started;
    private double _downY, _grab;
    private int _from, _target;
    private List<double> _pitch = new();
    private double[] _tops = Array.Empty<double>();
    private double[] _want = Array.Empty<double>();

    public BandReorder(Panel stack, ScrollViewer viewport, Func<IReadOnlyList<BandRow>> rows)
    {
        _stack = stack;
        _viewport = viewport;
        _rows = rows;
        _autoScroll.Tick += (_, _) => AutoScroll(Mouse.GetPosition(_viewport).Y);
    }

    /// <summary>Raised when a drag ends in a new place: the row and its new index among the rows.</summary>
    public event Action<BandRow, int>? Dropped;

    public bool Dragging => _started;

    /// <summary>Where the held row would land if it were dropped now.</summary>
    internal int Target => _target;

    /// <summary>How far the list scrolls (pixels, negative = up) with the pointer at <paramref name="pointerY"/> in a viewport of <paramref name="height"/>: faster the closer to the edge.</summary>
    internal static double AutoScrollStep(double pointerY, double height)
    {
        if (pointerY < EdgeZone) return -MaxScrollStep * Math.Min(1, (EdgeZone - pointerY) / EdgeZone);
        if (pointerY > height - EdgeZone) return MaxScrollStep * Math.Min(1, (pointerY - (height - EdgeZone)) / EdgeZone);
        return 0;
    }

    /// <summary>A tick of the edge scroll: scrolls the list and lets the held row follow the pointer. False when nothing moved.</summary>
    internal bool AutoScroll(double pointerY)
    {
        if (!_started || _row is null) return false;
        var step = AutoScrollStep(pointerY, _viewport.ActualHeight);
        if (step == 0) return false;
        var offset = Math.Clamp(_viewport.VerticalOffset + step, 0, _viewport.ScrollableHeight);
        if (offset == _viewport.VerticalOffset) return false;
        _viewport.ScrollToVerticalOffset(offset);
        Move(pointerY + offset);   // the stack's coordinates are the viewport's plus the scroll offset
        return true;
    }

    /// <summary>Lets the row's name strip start a drag.</summary>
    public void Attach(BandRow row)
    {
        var handle = row.DragHandle;
        handle.MouseLeftButtonDown += (_, e) => { Begin(row, e.GetPosition(_stack).Y); handle.CaptureMouse(); };
        handle.MouseMove += (_, e) => { if (handle.IsMouseCaptured) Move(e.GetPosition(_stack).Y); };
        handle.MouseLeftButtonUp += (_, _) => { handle.ReleaseMouseCapture(); End(true); };
        handle.LostMouseCapture += (_, _) => { if (_row == row && _started) End(true); };
    }

    internal void Begin(BandRow row, double y)
    {
        _row = row;
        _downY = y;
        _started = false;
    }

    /// <summary>The pointer moved to <paramref name="y"/> (stack coordinates); the held row follows and the others make room.</summary>
    internal void Move(double y)
    {
        if (_row is null) return;
        if (!_started)
        {
            if (Math.Abs(y - _downY) < StartDistance) return;
            Start();
        }
        var total = _tops[^1] + _pitch[^1];
        var top = Math.Clamp(y - _grab, 0, Math.Max(0, total - _pitch[_from]));
        _row.Shift.BeginAnimation(TranslateTransform.YProperty, null);
        _row.Shift.Y = top - _tops[_from];
        var target = BandLayoutState.DropIndex(_pitch, _from, top);
        if (target == _target) return;
        _target = target;
        var rows = _rows();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i == _from) continue;
            var want = i > _from && i <= target ? -_pitch[_from] : i < _from && i >= target ? _pitch[_from] : 0;
            if (_want[i] == want) continue;
            _want[i] = want;
            rows[i].Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(want, Glide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    /// <summary>The pointer was released: the held row slides into its slot (or snaps there when not animating), then the drop is reported.</summary>
    internal void End(bool animate)
    {
        var row = _row;
        if (row is null || !_started) { _row = null; return; }
        _autoScroll.Stop();
        int from = _from, target = _target, epoch = _epoch;
        _row = null;   // a second release while sliding does nothing
        if (!animate) { Finish(row, from, target); return; }
        var finalY = BandLayoutState.SlotTop(_pitch, from, target) - _tops[from];
        var animation = new DoubleAnimation(finalY, Glide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => { if (epoch == _epoch) Finish(row, from, target); };
        row.Shift.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private int _epoch;

    /// <summary>Forgets a drag in progress and puts every row back (the rows are being replaced).</summary>
    public void Cancel()
    {
        foreach (var r in _rows()) if (r.DragHandle.IsMouseCaptured) r.DragHandle.ReleaseMouseCapture();
        _epoch++;
        _row = null;
        _started = false;
        _autoScroll.Stop();
        ClearShifts();
    }

    private void Start()
    {
        var rows = _rows();
        _from = rows.ToList().IndexOf(_row!);
        _pitch = rows.Select(r => r.ActualHeight + r.Margin.Bottom).ToList();
        _tops = new double[rows.Count];
        for (var i = 1; i < rows.Count; i++) _tops[i] = _tops[i - 1] + _pitch[i - 1];
        _want = new double[rows.Count];
        _grab = _downY - _tops[_from];
        _target = _from;
        _started = true;
        _autoScroll.Start();
        foreach (var r in rows) r.Shift.BeginAnimation(TranslateTransform.YProperty, null);
        _row!.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(_row!).PixelsPerDip);
        Panel.SetZIndex(_row, 1);
    }

    private void Finish(BandRow row, int from, int target)
    {
        _started = false;
        _autoScroll.Stop();
        ClearShifts();
        if (target != from) Dropped?.Invoke(row, target);
    }

    private void ClearShifts()
    {
        foreach (var r in _rows())
        {
            r.Shift.BeginAnimation(TranslateTransform.YProperty, null);
            r.Shift.Y = 0;
            r.CacheMode = null;
            Panel.SetZIndex(r, 0);
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Docking;

namespace TabForge.Avalonia.Views.Docking;

// Owns: placing the two halves of a dock split from the node's ratio and orientation, and turning a splitter drag into a new ratio.
// Does not own: the halves' content (DockWorkspaceView builds them) or saving the layout.
// Tests: TabForge.Avalonia.Tests.DockWorkspaceViewTests.
/// <summary>Two children with a splitter between them. The first takes <see cref="DockNodeState.Ratio"/> of the space without the splitter.</summary>
public sealed class DockSplitPanel : Panel
{
    private const double MinRatio = 0.02;   // the clamp DockLayoutTree puts on a saved ratio
    private readonly DockNodeState _node;
    private readonly DockSplitter _splitter;
    private readonly Func<double>? _firstMaximum;
    private readonly Func<double>? _secondMaximum;
    private readonly Func<double>? _firstMinimum;
    private readonly Func<double>? _secondMinimum;
    private double _firstLocked;
    private double _secondLocked;
    private double _ratioAtPress;

    /// <param name="firstLocked">A pixel height that locks the first half of a vertical split (NaN for none).</param>
    /// <param name="secondLocked">The same for the second half; a locked half wins over the ratio.</param>
    /// <param name="firstMinimum">The least height the first half keeps, read live (null or 0 for none); the splitter and the arrange stop there.</param>
    /// <param name="secondMinimum">The same for the second half.</param>
    /// <param name="firstMaximum">The most height the first half takes, read live (NaN or null for none); the splitter stops there.</param>
    /// <param name="secondMaximum">The same for the second half.</param>
    public DockSplitPanel(DockNodeState node, Control first, Control second, Action? edited = null, double firstLocked = double.NaN, double secondLocked = double.NaN,
        Func<double>? firstMinimum = null, Func<double>? secondMinimum = null, Func<double>? firstMaximum = null, Func<double>? secondMaximum = null)
    {
        _node = node;
        _firstLocked = firstLocked;
        _secondLocked = secondLocked;
        _firstMinimum = firstMinimum;
        _secondMinimum = secondMinimum;
        _firstMaximum = firstMaximum;
        _secondMaximum = secondMaximum;
        _splitter = new DockSplitter(Horizontal);
        Children.Add(first);
        Children.Add(_splitter);
        Children.Add(second);
        _splitter.DragStarted += () => _ratioAtPress = _node.Ratio;
        _splitter.DragMoved += OnDragMoved;
        if (edited is not null) _splitter.DragEnded += edited;
    }

    public DockSplitter Splitter => _splitter;

    /// <summary>The split this panel shows.</summary>
    public DockNodeState Node => _node;

    /// <summary>The space the two halves share (without the splitter), or 0 before the first layout.</summary>
    public double AvailableNow => Available(Bounds.Size);

    /// <summary>Re-lays the halves after the node's ratio changed. Each half is measured with its share (see MeasureOverride), so a new share
    /// measures again: a half arranged at a share it was not measured at keeps the layout of the old one (the timeline's rows).</summary>
    public void Rearrange() => InvalidateMeasure();

    private bool Horizontal => _node.Orientation == "Horizontal";

    private double Available(Size size) => Math.Max(0, (Horizontal ? size.Width : size.Height) - DockSplitter.BarThickness);

    /// <summary>Sets the pixel height that locks one half of a vertical split (NaN unlocks it) and re-arranges the halves at once.</summary>
    public void SetLockedHeight(Control half, double height)
    {
        if (ReferenceEquals(half, Children[0])) _firstLocked = height;
        else if (ReferenceEquals(half, Children[2])) _secondLocked = height;
        Rearrange();
    }

    private static double? Limit(double maximum) => double.IsFinite(maximum) ? maximum : null;

    private void OnDragMoved(double delta)
    {
        var available = Available(Bounds.Size);
        if (available <= 0 || (!Horizontal && !(double.IsNaN(_firstLocked) && double.IsNaN(_secondLocked)))) return;   // a locked row cannot be dragged
        // A vertical split keeps each half at its minimum height (the timeline's rows and strip); a side-by-side split has none here.
        var (minimumA, minimumB) = Horizontal ? (0.0, 0.0) : (MinimumOf(_firstMinimum), MinimumOf(_secondMinimum));
        var firstSize = DockSizeRules.ClampSplit(_ratioAtPress * available + delta, available, minimumA, minimumB, !Horizontal,
            Array.Empty<(double Min, double Max)>(), Limit(MaximumOf(_firstMaximum)), Limit(MaximumOf(_secondMaximum))).First;
        _node.Ratio = Math.Clamp(firstSize / available, MinRatio, 1 - MinRatio);
        _node.UserRatio = null;
        Rearrange();
    }

    // Each half is measured with its own share of the space: a half that is measured with the whole split keeps the taller layout
    // of its content (a scroll viewer's star rows) after the arrange gives it the smaller share, so it shows no scroll bar.
    protected override Size MeasureOverride(Size availableSize)
    {
        var (first, second) = SharesOf(availableSize);
        if (Horizontal)
        {
            Children[0].Measure(new Size(first, availableSize.Height));
            Children[2].Measure(new Size(second, availableSize.Height));
        }
        else
        {
            Children[0].Measure(new Size(availableSize.Width, first));
            Children[2].Measure(new Size(availableSize.Width, second));
        }
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    /// <summary>The extent of each half along the split for a size of the whole panel: the first half's share, and the second's share of what is left after the splitter.</summary>
    private (double First, double Second) SharesOf(Size size)
    {
        var available = Available(size);
        var first = FirstShare(available);
        var second = double.IsInfinity(available) ? available : Math.Max(0, available - first);
        return (first, second);
    }

    private double FirstShare(double available)
    {
        var first = Math.Round(available * _node.Ratio);
        if (!Horizontal && !double.IsNaN(_firstLocked)) first = Math.Min(_firstLocked, available);
        else if (!Horizontal && !double.IsNaN(_secondLocked)) first = Math.Max(0, available - _secondLocked);
        if (!Horizontal) first = WithinMaximums(first, available);
        return first;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var first = SharesOf(finalSize).First;
        var bar = DockSplitter.BarThickness;
        if (Horizontal)
        {
            Children[0].Arrange(new Rect(0, 0, first, finalSize.Height));
            _splitter.Arrange(new Rect(first, 0, bar, finalSize.Height));
            Children[2].Arrange(new Rect(first + bar, 0, Math.Max(0, finalSize.Width - first - bar), finalSize.Height));
        }
        else
        {
            Children[0].Arrange(new Rect(0, 0, finalSize.Width, first));
            _splitter.Arrange(new Rect(0, first, finalSize.Width, bar));
            Children[2].Arrange(new Rect(0, first + bar, finalSize.Width, Math.Max(0, finalSize.Height - first - bar)));
        }
        return finalSize;
    }

    private static double MaximumOf(Func<double>? maximum) => maximum?.Invoke() ?? double.NaN;

    private static double MinimumOf(Func<double>? minimum) => Math.Max(0, minimum?.Invoke() ?? 0);

    /// <summary>The first half's height kept within both halves' maximums: a half past its maximum leaves empty space, so the other half takes it.
    /// The minimums are kept by the splitter drag only, so a window resize keeps the ratio's layout as it did before.</summary>
    private double WithinMaximums(double first, double available)
    {
        var upper = double.IsFinite(MaximumOf(_firstMaximum)) ? Math.Min(available, MaximumOf(_firstMaximum)) : available;
        var lower = double.IsFinite(MaximumOf(_secondMaximum)) ? Math.Max(0, available - MaximumOf(_secondMaximum)) : 0;
        return Math.Min(Math.Max(first, lower), upper);
    }
}

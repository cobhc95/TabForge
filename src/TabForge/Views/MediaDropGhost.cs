using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>
/// The drop preview over the timeline: a translucent block where dragged files will land (as long as they are), and, when the
/// drop makes a new lane or track, that slot growing open under it. One element for the whole drag: a DragOver only moves the
/// block's transform; the block is redrawn only when its size or text changes, so following the pointer does no layout.
/// </summary>
internal sealed class MediaDropGhost : FrameworkElement
{
    private readonly DrawingVisual _slot = new();
    private readonly DrawingVisual _block = new();
    private readonly TranslateTransform _blockShift = new();
    private readonly TranslateTransform _slotShift = new();
    private readonly ScaleTransform _slotGrow = new(1, 1);
    private readonly VisualTheme _theme = new();
    private string? _blockKey;
    private string? _slotKey;
    private bool _shown;
    private int _fadeToken;
    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(110));

    public MediaDropGhost()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Visibility = Visibility.Collapsed;
        Opacity = 0;
        _block.Transform = _blockShift;
        var slotTransform = new TransformGroup();
        slotTransform.Children.Add(_slotGrow);
        slotTransform.Children.Add(_slotShift);
        _slot.Transform = slotTransform;
        AddVisualChild(_slot);
        AddVisualChild(_block);
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index == 0 ? _slot : _block;

    /// <summary>Where the block is now (timeline coordinates), or null while hidden.</summary>
    internal Rect? BlockBounds { get; private set; }
    internal Rect? SlotBounds { get; private set; }
    internal string? BlockLabel { get; private set; }
    internal bool ShowsInvalid { get; private set; }
    internal bool IsShown => _shown;

    /// <summary>Shows, moves or hides the preview. <paramref name="animate"/> false: no fades (off-screen renders, tests).</summary>
    public void Show(DropPreview? preview, bool animate = true)
    {
        if (preview is null) { Hide(animate); return; }
        var block = preview.Block;
        var blockKey = $"{Math.Round(block.Width)}|{block.Height}|{preview.Label}|{preview.Detail}|{preview.Colour}|{preview.Valid}|{VisualTheme.IsLight}|{string.Join(",", preview.Splits.Select(s => Math.Round(s)))}";
        if (blockKey != _blockKey)
        {
            _blockKey = blockKey;
            using var dc = _block.RenderOpen();
            using (Draw.UseDpi(this)) DrawBlock(dc, preview, new Rect(0, 0, Math.Round(block.Width), block.Height));
        }
        _blockShift.X = block.X; _blockShift.Y = block.Y;
        BlockBounds = new Rect(block.X, block.Y, Math.Round(block.Width), block.Height);
        BlockLabel = preview.Label;
        ShowsInvalid = !preview.Valid;

        if (preview.Slot is { } slot)
        {
            var slotKey = $"{slot.Width}|{slot.Height}|{preview.SlotLabel}|{VisualTheme.IsLight}";
            var opening = SlotBounds is null || Math.Abs(SlotBounds.Value.Y - slot.Y) > 0.5;
            if (slotKey != _slotKey)
            {
                _slotKey = slotKey;
                using var dc = _slot.RenderOpen();
                using (Draw.UseDpi(this)) DrawSlot(dc, new Rect(0, 0, slot.Width, slot.Height), preview.SlotLabel ?? "");
            }
            _slotShift.X = slot.X; _slotShift.Y = slot.Y;
            SlotBounds = slot;
            if (opening)
            {
                // The new lane / track opens downwards from its top edge.
                if (animate) _slotGrow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.15, 1, new Duration(TimeSpan.FromMilliseconds(140))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                else { _slotGrow.BeginAnimation(ScaleTransform.ScaleYProperty, null); _slotGrow.ScaleY = 1; }
            }
        }
        else if (SlotBounds is not null)
        {
            SlotBounds = null;
            _slotKey = null;
            using (_slot.RenderOpen()) { }
        }

        if (!_shown)
        {
            _shown = true;
            _fadeToken++;
            Visibility = Visibility.Visible;
            if (animate) BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, Fade));
            else { BeginAnimation(OpacityProperty, null); Opacity = 1; }
        }
    }

    private void Hide(bool animate)
    {
        if (!_shown) return;
        _shown = false;
        BlockBounds = null; SlotBounds = null; BlockLabel = null;
        var token = ++_fadeToken;
        void Clear()
        {
            if (token != _fadeToken) return;
            Visibility = Visibility.Collapsed;
            _blockKey = null; _slotKey = null;
            using (_slot.RenderOpen()) { }
            using (_block.RenderOpen()) { }
        }
        if (!animate) { BeginAnimation(OpacityProperty, null); Opacity = 0; Clear(); return; }
        var fade = new DoubleAnimation(Opacity, 0, Fade);
        fade.Completed += (_, _) => Clear();
        BeginAnimation(OpacityProperty, fade);
    }

    private void DrawBlock(DrawingContext dc, DropPreview preview, Rect box)
    {
        var colour = preview.Valid ? preview.Colour : (TryFindResource("DangerBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0xE0, 0x4F, 0x4F);
        dc.DrawRoundedRectangle(Draw.Solid(colour, preview.Valid ? 0.38 : 0.16), Draw.Pen(colour, 1.2, 0.95), box, 3, 3);
        dc.PushClip(new RectangleGeometry(box, 3, 3));
        foreach (var split in preview.Splits)
            dc.DrawLine(Draw.Pen(colour, 1, 0.8), new Point(Math.Round(split) + 0.5, box.Top + 2), new Point(Math.Round(split) + 0.5, box.Bottom - 2));
        var text = Draw.Solid(_theme.Text, 0.92);
        if (box.Width > 24) Draw.At(dc, preview.Label, box.X + 6, box.Y + 3, 11, text, bold: true);
        if (preview.Detail.Length > 0)
        {
            // The length (and "new lane") only when it fits: a cut-off number would read as a different length.
            // A short block shows just the length (the first part).
            var parts = preview.Detail.Split(" · ");
            for (var count = parts.Length; count >= 1; count = count > 1 ? 1 : 0)
            {
                var detail = Draw.Text(string.Join(" · ", parts.Take(count)), 10, Draw.Solid(_theme.Text, 0.75));
                if (detail.Width + 12 > box.Width) continue;
                Draw.DrawText(dc, detail, new Point(box.X + 6, box.Y + 19));
                break;
            }
        }
        dc.Pop();
    }

    private void DrawSlot(DrawingContext dc, Rect box, string label)
    {
        dc.DrawRectangle(Draw.Solid(_theme.Board, 0.92), null, box);
        dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.07), Draw.DashedPen(_theme.Accent, 1, 4, 3), new Rect(box.X + 0.5, box.Y + 0.5, Math.Max(0, box.Width - 1), Math.Max(0, box.Height - 1)));
        // The slot is as wide as the song: its name rides on the block ("· new lane"), which is always in view.
        _ = label;
    }
}

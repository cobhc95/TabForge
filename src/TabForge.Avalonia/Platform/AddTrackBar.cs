using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the "Add track" strip control in the arrangement's own row under the track list and the timeline: its full-width band, the hover glow
//   and the left click that asks the Add track question.
// Does not own: the strip's drawing (AddTrackStrip), its size and words (AddTrackLane in Core), or whether it shows (ArrangementView sets IsVisible).
// Tests: TabForge.Avalonia.Tests.AddTrackStripTests, TabForge.Avalonia.Tests.TrackListOwnerLayoutTests.
public sealed class AddTrackBar : Control
{
    private readonly AvaloniaDrawContext _draw = new();
    private double _listWidth;
    private bool _hot;
    private bool _dropActive;

    public AddTrackBar()
    {
        Name = "AddTrackBar";
        Height = AddTrackLane.Height;
        PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Clicked?.Invoke(); };
    }

    /// <summary>The track list's width: the label is centred across it, and the band fills the rest of the pane.</summary>
    public double ListWidth
    {
        get => _listWidth;
        set { if (value == _listWidth) return; _listWidth = value; InvalidateVisual(); }
    }

    /// <summary>Raised when the left button presses the strip.</summary>
    public event Action? Clicked;

    /// <summary>True while a dragged audio file is over the strip: the strip shows its drop shade and words.</summary>
    internal bool DropActive
    {
        get => _dropActive;
        set { if (value == _dropActive) return; _dropActive = value; InvalidateVisual(); }
    }

    /// <summary>The words shown while <see cref="DropActive"/> instead of the default file-drop ones (a clip dragged from the timeline); null: the default.</summary>
    internal string? DropWords
    {
        get => _dropWords;
        set { if (value == _dropWords) return; _dropWords = value; InvalidateVisual(); }
    }
    private string? _dropWords;

    private const double GhostFillOpacity = 0.38, GhostStrokeWidth = 1.2, ReadoutFillOpacity = 0.92;
    private (double X1, double X2, string Label, string Readout)? _dropGhost;

    /// <summary>While dropped files hover over the strip: where their new track's first clip would start and end (this strip's x) with its name and the time readout, drawn as a ghost block with a time line.</summary>
    internal (double X1, double X2, string Label, string Readout)? DropGhost
    {
        get => _dropGhost;
        set { if (_dropGhost == value) return; _dropGhost = value; InvalidateVisual(); }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        SetHot(true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHot(false);
    }

    private void SetHot(bool hot)
    {
        if (_hot == hot) return;
        _hot = hot;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var d = _draw.Use(context);
        AddTrackStrip.Draw(d, key => AvaloniaPalette.Get(key, light), 0, Bounds.Width, ListWidth, _hot, _dropActive, DropWords);
        if (_dropGhost is { } ghost) DrawDropGhost(d, key => AvaloniaPalette.Get(key, light), ghost);
    }

    private void DrawDropGhost(IDrawContext d, Func<string, UiColor> ink, (double X1, double X2, string Label, string Readout) ghost)
    {
        var accent = ink("InstrumentAccent");
        var textInk = ink("TextPrimary");
        var left = Math.Max(ListWidth, 0);
        d.PushClip(left, 0, Math.Max(0, Bounds.Width - left), Bounds.Height);
        const double inset = 3, pad = 5;
        var width = Math.Max(3, ghost.X2 - ghost.X1);
        d.Rect(accent.WithOpacity(GhostFillOpacity), new UiStroke(accent, GhostStrokeWidth), ghost.X1, inset, width, Math.Max(1, Bounds.Height - 2 * inset), 3);
        d.Rect(accent, null, ghost.X1 - 0.5, 0, 1, Bounds.Height);
        if (ghost.Label.Length > 0 && width > 4 * pad)
        {
            d.PushClip(ghost.X1, inset, width, Math.Max(1, Bounds.Height - 2 * inset));
            d.DrawText(d.Text(ghost.Label, TimelineView.Font, 11, textInk, d.PixelsPerDip, weight: 600), ghost.X1 + pad, inset + 1);
            d.Pop();
        }
        if (ghost.Readout.Length > 0)
        {
            var text = d.Text(ghost.Readout, TimelineView.Font, 10, textInk, d.PixelsPerDip);
            var x = ghost.X1 + 6 + text.Width + 2 * pad > Bounds.Width ? ghost.X1 - 6 - text.Width - 2 * pad : ghost.X1 + 6;
            d.Rect(ink("TimelineRow").WithOpacity(ReadoutFillOpacity), new UiStroke(accent, 1), x, 1, text.Width + 2 * pad, Math.Min(Bounds.Height - 2, text.Height + pad), 3);
            d.DrawText(text, x + pad, 1 + pad / 2);
        }
        d.Pop();
    }
}

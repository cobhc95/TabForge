using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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
        AddTrackStrip.Draw(d, key => AvaloniaPalette.Get(key, light), 0, Bounds.Width, ListWidth, _hot, _dropActive);
    }
}

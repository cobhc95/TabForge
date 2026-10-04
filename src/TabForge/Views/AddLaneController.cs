using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the Add-track lane needs from the arrangement panel that hosts it (implemented by <see cref="ArrangementPanel"/>).</summary>
internal interface IAddLaneHost
{
    SongProject? Project { get; }
    /// <summary>Height of the track list's viewport.</summary>
    double ControlsViewportHeight { get; }
    /// <summary>Horizontal scroll offset of the timeline.</summary>
    double TimelineHorizontalOffset { get; }
    void RebuildControls();
    void RefreshTimelineExtent();
    void RaiseAddTrackLaneClicked();
}

// Owns: the track-list half of the Add-track lane (shown flag, the row, its glow and label, hover state) and drops onto it.
// Does not own: the timeline's half and the drop planning (TrackTimeline), where the row sits (ArrangementPanel.TrackRows),
// what a click does (MainWindow via AddTrackLaneClicked).
// Tests: TestAddTrackLane, TestAddTrackMenu, TestMediaDropPlan.
internal sealed class AddLaneController
{
    private readonly IAddLaneHost _host;
    private readonly TrackTimeline _timeline;
    private bool _shown = true;
    private Border? _row;
    private Border? _glow;
    private TextBlock? _text;
    private bool _hot;

    public AddLaneController(IAddLaneHost host, TrackTimeline timeline)
    {
        _host = host;
        _timeline = timeline;
        _timeline.AddLaneShown = _shown;
        _timeline.AddLaneClicked += _host.RaiseAddTrackLaneClicked;
        _timeline.AddLaneHotChanged += hot => { _hot = hot; ApplyState(); };
        _timeline.AddLaneDragChanged += _ => ApplyState();
    }

    public bool Shown
    {
        get => _shown;
        set
        {
            if (_shown == value) return;
            _shown = value;
            _timeline.AddLaneShown = value;
            if (_host.Project is null) return;
            _host.RebuildControls();
            _host.RefreshTimelineExtent();
        }
    }

    /// <summary>Extra height of the lane (0 when hidden or no song is bound).</summary>
    public double Extra => _host.Project is null ? 0 : AddTrackLane.HeightOf(_shown);

    /// <summary>Actual height of the lane: all the room left below the last row in the pane (at least one row); 0 when hidden.</summary>
    public double FillHeight => _host.Project is not { } project || !_shown
        ? 0 : Math.Max(AddTrackLane.Height, _host.ControlsViewportHeight - ArrangementPanel.RowsHeight(project));

    /// <summary>The track-list half of the lane (null when hidden).</summary>
    public Border? Row => _row;

    public Border? Build()
    {
        _row = null;
        if (!_shown || _host.Project is null) return null;
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Height = AddTrackLane.Height };
        content.Children.Add(new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource("IconPlus"), Style = (Style)Application.Current.FindResource("IconPath"),
            Width = 13, Height = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        });
        var text = new TextBlock { Text = AddTrackLane.Label, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        text.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        content.Children.Add(text);
        _text = text;
        // One zone with the timeline's half: the same panel colour, a soft glow over both on hover, an accent outline while a file is dragged over.
        var glow = new Border { IsHitTestVisible = false, Opacity = 0 };
        glow.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var inner = new Grid();
        inner.Children.Add(glow);
        inner.Children.Add(content);
        _glow = glow;
        var lane = new Border
        {
            Tag = "add-lane", Height = FillHeight, VerticalAlignment = VerticalAlignment.Top, Child = inner, Focusable = true, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0), AllowDrop = true,
            FocusVisualStyle = null,   // the default dotted focus rectangle's right edge showed as a dotted line at the list/timeline seam
            ToolTip = "Add a track: click, or drop an audio or MIDI file here to get an audio track"
        };
        lane.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        lane.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        AutomationProperties.SetName(lane, AddTrackLane.Label);
        AutomationProperties.SetHelpText(lane, "Opens the add-track prompt: audio track or instrument track");
        lane.MouseEnter += (_, _) => SetHot(true);
        lane.MouseLeave += (_, _) => SetHot(lane.IsKeyboardFocused);
        lane.GotKeyboardFocus += (_, _) => SetHot(true);   // keyboard focus shows as the same glow as hover
        lane.LostKeyboardFocus += (_, _) => SetHot(lane.IsMouseOver);
        lane.DragLeave += (_, _) => _timeline.MediaDragLeave();
        lane.MouseLeftButtonUp += (_, e) => { if (e.ClickCount <= 1) _host.RaiseAddTrackLaneClicked(); e.Handled = true; };
        lane.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            _host.RaiseAddTrackLaneClicked();
            e.Handled = true;
        };
        lane.DragEnter += OnDragOver;
        lane.DragOver += OnDragOver;
        lane.Drop += OnDrop;
        _row = lane;
        ApplyState();
        return lane;
    }

    /// <summary>Hover over either half lights the whole zone: the list's half here, the timeline's through its own flag.</summary>
    private void SetHot(bool hot)
    {
        _hot = hot;
        _timeline.AddLaneHot = hot;
        ApplyState();
    }

    /// <summary>Idle, hover (soft glow) or drag (accent outline and the drop words) on the list's half.</summary>
    private void ApplyState()
    {
        if (_row is not { } lane || _glow is not { } glow) return;
        var drag = _timeline.AddLaneDrag;
        glow.Opacity = drag ? 0.14 : _hot ? 0.10 : 0;
        lane.BorderThickness = drag ? new Thickness(2, 2, 0, 2) : new Thickness(0);
        if (_text is { } text) text.Text = drag ? AddTrackLane.DropLabel : AddTrackLane.Label;
    }

    /// <summary>Shows the zone hovered and/or dragged over without a pointer (test and render seam).</summary>
    public void Simulate(bool hot, bool drag) { SetHot(hot); _timeline.SetAddLaneDrag(drag); }

    /// <summary>The list's half as it looks now: "idle", "hover" or "drag" (test seam).</summary>
    public string State => _timeline.AddLaneDrag ? "drag" : _hot ? "hover" : "idle";

    // A file dragged onto the track-list half lands where the timeline half would: on the lane, at the left edge of the visible timeline.
    private Point DropPoint() => new(_host.TimelineHorizontalOffset + 8, _timeline.AddLaneTop + AddTrackLane.Height / 2);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_host.Project is null) return;
        if (_timeline.MediaDragOver(e.Data, DropPoint(), ArrangementPanel.AltHeld(e)) is not { } effect) return;
        e.Effects = ArrangementPanel.EffectFor(effect, e.AllowedEffects, _timeline.CurrentDropPreview);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_host.Project is null) return;
        var preview = _timeline.CurrentDropPreview;
        if (_timeline.DropMedia(e.Data, DropPoint(), ArrangementPanel.AltHeld(e)) is not { } effect) return;
        e.Effects = ArrangementPanel.EffectFor(effect, e.AllowedEffects, preview);
        e.Handled = true;
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// ArrangementPanel: the Add-track lane (a strip under the last track, across the track list and the timeline) and the audio-track row parts
// (waveform label instead of the instrument picker, the row's right-click menu with "Convert to instrument track").
public sealed partial class ArrangementPanel
{
    /// <summary>The Add-track lane was clicked (either half): the host asks Audio or Instrument.</summary>
    public event Action? AddTrackLaneClicked;
    /// <summary>"Convert to instrument track" on an audio track's row (track index).</summary>
    public event Action<int>? ConvertAudioTrackRequested;

    private bool _showAddTrackLane = true;
    private Border? _addLaneRow;

    /// <summary>The strip labelled "Add track" under the last track (Preferences > Timeline &amp; Tracks). The + Track button stays either way.</summary>
    public bool ShowAddTrackLane
    {
        get => _showAddTrackLane;
        set
        {
            if (_showAddTrackLane == value) return;
            _showAddTrackLane = value;
            _timeline.AddLaneShown = value;
            if (_project is null) return;
            RebuildControls();
            RefreshTimelineExtent();
        }
    }

    /// <summary>Extra height of the lane (0 when hidden or no song is bound).</summary>
    private double AddLaneExtra => _project is null ? 0 : AddTrackLane.HeightOf(_showAddTrackLane);

    /// <summary>Actual height of the lane: all the room left below the last row in the pane (at least one row); 0 when hidden.</summary>
    internal double AddLaneFillHeight => _project is null || !_showAddTrackLane
        ? 0 : Math.Max(AddTrackLane.Height, _controlsScroll.ActualHeight - RowsHeight(_project));

    /// <summary>The track-list half of the lane (null when hidden): test and render hook.</summary>
    internal FrameworkElement? AddLaneRow => _addLaneRow;

    /// <summary>Clicks the lane as the pointer would (test seam).</summary>
    internal void ClickAddLane() => AddTrackLaneClicked?.Invoke();

    private Border? BuildAddLane()
    {
        _addLaneRow = null;
        if (!_showAddTrackLane || _project is null) return null;
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Height = AddTrackLane.Height };
        content.Children.Add(new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource("IconPlus"), Style = (Style)Application.Current.FindResource("IconPath"),
            Width = 13, Height = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        });
        var text = new TextBlock { Text = AddTrackLane.Label, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        text.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        content.Children.Add(text);
        _addLaneText = text;
        // One zone with the timeline's half: the same panel colour, a soft glow over both on hover, an accent outline while a file is dragged over.
        var glow = new Border { IsHitTestVisible = false, Opacity = 0 };
        glow.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var inner = new Grid();
        inner.Children.Add(glow);
        inner.Children.Add(content);
        _addLaneGlow = glow;
        var lane = new Border
        {
            Tag = "add-lane", Height = AddLaneFillHeight, VerticalAlignment = VerticalAlignment.Top, Child = inner, Focusable = true, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0), AllowDrop = true,
            FocusVisualStyle = null,   // the default dotted focus rectangle's right edge showed as a dotted line at the list/timeline seam
            ToolTip = "Add a track: click, or drop an audio or MIDI file here to get an audio track"
        };
        lane.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        lane.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        AutomationProperties.SetName(lane, AddTrackLane.Label);
        AutomationProperties.SetHelpText(lane, "Opens the add-track prompt: audio track or instrument track");
        lane.MouseEnter += (_, _) => SetAddLaneHot(true);
        lane.MouseLeave += (_, _) => SetAddLaneHot(lane.IsKeyboardFocused);
        lane.GotKeyboardFocus += (_, _) => SetAddLaneHot(true);   // keyboard focus shows as the same glow as hover
        lane.LostKeyboardFocus += (_, _) => SetAddLaneHot(lane.IsMouseOver);
        lane.DragLeave += (_, _) => _timeline.MediaDragLeave();
        lane.MouseLeftButtonUp += (_, e) => { if (e.ClickCount <= 1) AddTrackLaneClicked?.Invoke(); e.Handled = true; };
        lane.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            AddTrackLaneClicked?.Invoke();
            e.Handled = true;
        };
        lane.DragEnter += OnAddLaneDragOver;
        lane.DragOver += OnAddLaneDragOver;
        lane.Drop += OnAddLaneDrop;
        _addLaneRow = lane;
        ApplyAddLaneState();
        return lane;
    }

    private void RaiseTimelineAddLane() => AddTrackLaneClicked?.Invoke();

    private Border? _addLaneGlow;
    private TextBlock? _addLaneText;
    private bool _addLaneHot;

    /// <summary>Hover over either half lights the whole zone: the list's half here, the timeline's through its own flag.</summary>
    private void SetAddLaneHot(bool hot)
    {
        _addLaneHot = hot;
        _timeline.AddLaneHot = hot;
        ApplyAddLaneState();
    }

    /// <summary>Idle, hover (soft glow) or drag (accent outline and the drop words) on the list's half.</summary>
    private void ApplyAddLaneState()
    {
        if (_addLaneRow is not { } lane || _addLaneGlow is not { } glow) return;
        var drag = _timeline.AddLaneDrag;
        glow.Opacity = drag ? 0.14 : _addLaneHot ? 0.10 : 0;
        lane.BorderThickness = drag ? new Thickness(2, 2, 0, 2) : new Thickness(0);
        if (_addLaneText is { } text) text.Text = drag ? AddTrackLane.DropLabel : AddTrackLane.Label;
    }

    /// <summary>Shows the zone hovered and/or dragged over without a pointer (test and render seam).</summary>
    internal void SimulateAddLaneState(bool hot, bool drag) { SetAddLaneHot(hot); _timeline.SetAddLaneDrag(drag); }

    /// <summary>The list's half as it looks now: "idle", "hover" or "drag" (test seam).</summary>
    internal string AddLaneState => _timeline.AddLaneDrag ? "drag" : _addLaneHot ? "hover" : "idle";

    // A file dragged onto the track-list half lands where the timeline half would: on the lane, at the left edge of the visible timeline.
    private Point LaneDropPoint() => new(_horizontal.HorizontalOffset + 8, _timeline.AddLaneTop + AddTrackLane.Height / 2);

    private void OnAddLaneDragOver(object sender, DragEventArgs e)
    {
        if (_project is null) return;
        if (_timeline.MediaDragOver(e.Data, LaneDropPoint(), AltHeld(e)) is not { } effect) return;
        e.Effects = EffectFor(effect, e.AllowedEffects, _timeline.CurrentDropPreview);
        e.Handled = true;
    }

    private void OnAddLaneDrop(object sender, DragEventArgs e)
    {
        if (_project is null) return;
        var preview = _timeline.CurrentDropPreview;
        if (_timeline.DropMedia(e.Data, LaneDropPoint(), AltHeld(e)) is not { } effect) return;
        e.Effects = EffectFor(effect, e.AllowedEffects, preview);
        e.Handled = true;
    }

    /// <summary>An audio track's row shows a waveform and "Audio" where an instrument track has its instrument picker.</summary>
    private static FrameworkElement AudioKindCell()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 4, 2) };
        var icon = AudioTrackIcon.Element(16);
        icon.Margin = new Thickness(0, 0, 6, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        var text = new TextBlock { Text = "Audio", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        content.Children.Add(text);
        content.ToolTip = "Audio track: audio and MIDI clips, no notation";
        AutomationProperties.SetName(content, "Audio track");
        return content;
    }

    /// <summary>Right-click on an audio track's row: its properties, or turning it into an instrument track.</summary>
    private void ShowAudioRowMenu(int index, FrameworkElement target)
    {
        var menu = AudioRowMenu(index, target);
        menu.IsOpen = true;
    }

    internal ContextMenu AudioRowMenu(int index, FrameworkElement target)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)), PlacementTarget = target };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header, Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Item("Track properties…", () => TrackOptionsRequested?.Invoke(this, index));
        Item("Convert to instrument track…", () => ConvertAudioTrackRequested?.Invoke(index))
            .ToolTip = "Choose an instrument; the track keeps its audio and MIDI clips, moved to a second lane below the new tab lane";
        return menu;
    }
}

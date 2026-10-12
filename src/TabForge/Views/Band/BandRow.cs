using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views.Band;

// Owns: one track's row: the track name over its instrument view on the left, its tab lane on the right, a grip on the bottom edge
//   that resizes the row, and a vertical shift transform used while the row is dragged or makes way. With the instrument hidden the
//   left column is gone, the name with its toggle is a thin strip across the top of the row, and the lane spans the row below it
//   (the row is taller by the strip's height, so the lane keeps the height it has beside a shown instrument).
// Does not own: what the instrument shows (the controller feeds it), the lane's drawing (BandLane) or the row's place in the list.
// Tests: TestBandViewRows, TestBandRowSizing.
internal sealed class BandRow : Border
{
    private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _grip = new() { Height = 6, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeNS };
    private readonly ColumnDefinition _instrumentColumn = new() { Width = new GridLength(38, GridUnitType.Star), MinWidth = 200 };
    private readonly Border _widthGrip = new() { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeWE };
    private readonly Button _toggleInstrument = new() { Width = 22, Height = 22, Padding = new Thickness(3), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    /// <summary>The width a hidden instrument leaves in the row: none.</summary>
    internal const double HiddenHeaderWidth = 0;
    /// <summary>The height of the name strip over the lane of a row whose instrument is hidden.</summary>
    internal const double StripHeight = 20;
    private readonly DockPanel _title = new() { LastChildFill = true, Background = Brushes.Transparent };
    private readonly Grid _header = new();
    private readonly Grid _root = new();
    private readonly Border _backdrop = new() { IsHitTestVisible = false };
    private readonly string _panelWord;
    private readonly Grid _left = new() { ClipToBounds = true };
    private bool _instrumentHidden, _applied, _stacked, _framed;
    private readonly BandRowFrame _frame;
    /// <summary>The height of the name line over a shown instrument.</summary>
    private const double HeaderHeight = 30;
    private double _width;
    private double _gripGrab = double.NaN;
    private bool _selected;
    private double _grab = double.NaN;

    public BandRow(TrackModel track, int trackIndex, SongProject project, ScoreAppearance look, BandSettings band, BandFollow follow)
    {
        Track = track;
        Key = KeyOf(track);
        TrackIndex = trackIndex;
        if (track.Kind == TrackKind.Drums) Instrument.DrumLabel = midi => Services.DrumMaps.For(track, midi).Label;
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "BorderSoftBrush");
        Margin = new Thickness(0, 0, 0, 2);
        ClipToBounds = true;

        _name.Text = track.Name;
        _name.ToolTip = track.Name;
        _name.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        var chip = new Rectangle { Width = 4, Height = 16, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 8, 0), Fill = new SolidColorBrush(ColourOf(track)) };
        var title = _title;
        _panelWord = track.IsAudio ? "instrument" : Visualization.InstrumentVisualizer.NaturalKind(track) switch
        {
            Visualization.InstrumentKind.Keyboard => "keyboard", Visualization.InstrumentKind.Drums => "drum kit", _ => "fretboard"
        };
        DockPanel.SetDock(chip, Dock.Left);
        title.Children.Add(chip);
        var fretboard = FretboardIcon.Build();   // a neck with nut, frets, hairline strings and inlays
        _toggleInstrument.Content = fretboard;
        _toggleInstrument.SetResourceReference(StyleProperty, "ToolButton");
        System.Windows.Automation.AutomationProperties.SetName(_toggleInstrument, "Show or hide the instrument of " + track.Name);
        DockPanel.SetDock(_toggleInstrument, Dock.Right);
        title.Children.Add(_toggleInstrument);
        _toggleInstrument.Click += (_, _) => InstrumentToggleRequested?.Invoke();
        _widthGrip.MouseLeftButtonDown += OnWidthDown;
        _widthGrip.MouseMove += OnWidthMove;
        _widthGrip.MouseLeftButtonUp += (_, _) => _widthGrip.ReleaseMouseCapture();
        // Losing the capture ends the drag however it happens (button up, another window taking the mouse).
        _widthGrip.LostMouseCapture += (_, _) => { var dragged = !double.IsNaN(_gripGrab); _gripGrab = double.NaN; if (dragged) CommitWidth(); };
        title.Children.Add(_name);

        Instrument.Title = track.Name;
        Instrument.AudioTrack = track.IsAudio;
        System.Windows.Automation.AutomationProperties.SetName(Instrument, "Instrument of " + track.Name);
        var left = _left;
        left.SetResourceReference(Panel.BackgroundProperty, "InstrumentHostBrush");
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _backdrop.SetResourceReference(Border.BackgroundProperty, "InstrumentHostBrush");
        _header.Children.Add(_backdrop);
        _header.Children.Add(title);
        Grid.SetRow(Instrument, 1);
        left.Children.Add(_header);
        left.Children.Add(Instrument);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(_instrumentColumn);
        _grid = grid;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
        Grid.SetColumn(Lane, 1);
        grid.Children.Add(left);
        grid.Children.Add(Lane);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRowSpan(left, 2); Grid.SetRowSpan(Lane, 2); Grid.SetRowSpan(_widthGrip, 2);
        grid.Children.Add(_widthGrip);
        Panel.SetZIndex(_widthGrip, 2);
        var root = _root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid, 1);
        Grid.SetRowSpan(_grip, 2);
        root.Children.Add(grid);
        root.Children.Add(_grip);
        Child = _frame = new BandRowFrame(root, ColourOf(track));
        ApplyInstrument(false, 0);
        RenderTransform = Shift;
        DragHandle = title;
        _grip.MouseLeftButtonDown += OnGripDown;
        _grip.MouseMove += OnGripMove;
        _grip.MouseLeftButtonUp += OnGripUp;

        System.Windows.Automation.AutomationProperties.SetName(Lane, "Tab of " + track.Name);
        Lane.Follow = follow;
        Lane.SetContent(BandChoices.NormalizeContent(band.LaneContent));
        Lane.Bind(project, trackIndex, look);
    }

    private readonly Grid _grid;

    /// <summary>Raised by the header's instrument toggle: show or hide this row's instrument.</summary>
    public event Action? InstrumentToggleRequested;

    /// <summary>Raised while the grip between instrument and lane is dragged: the width the instrument should take.</summary>
    public event Action<double>? WidthRequested;

    /// <summary>Raised by a double-click on the grip: back to the default width.</summary>
    public event Action? WidthReset;

    /// <summary>Raised when a width drag ends (the width is kept).</summary>
    public event Action? WidthCommitted;

    internal void RequestWidth(double width) => WidthRequested?.Invoke(width);
    internal void RequestWidthReset() => WidthReset?.Invoke();
    internal void CommitWidth() => WidthCommitted?.Invoke();
    internal void RequestInstrumentToggle() => InstrumentToggleRequested?.Invoke();
    internal bool InstrumentHidden => _instrumentHidden;
    internal double InstrumentColumnWidth => _left.ActualWidth;
    /// <summary>The name and toggle (a self-test reads where they lie).</summary>
    internal FrameworkElement Header => _header;
    /// <summary>True while the toggle has its accent "on" look.</summary>
    internal bool ToggleLit => _toggleInstrument.ReadLocalValue(BackgroundProperty) != DependencyProperty.UnsetValue;
    internal string ToggleTip => _toggleInstrument.ToolTip as string ?? "";

    private void OnWidthDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { RequestWidthReset(); e.Handled = true; return; }
        _gripGrab = _left.ActualWidth - e.GetPosition(_grid).X;
        _widthGrip.CaptureMouse();
        e.Handled = true;
    }

    private void OnWidthMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (double.IsNaN(_gripGrab) || !_widthGrip.IsMouseCaptured) return;
        // The lane keeps a readable share of the row.
        RequestWidth(Math.Min(e.GetPosition(_grid).X + _gripGrab, Math.Max(BandChoices.MinInstrumentWidth, ActualWidth - 160)));
    }

    /// <summary>A grip drag in progress: only the instrument column takes the width; a hidden instrument stays hidden.</summary>
    internal void DragInstrumentWidth(double width)
    {
        if (_instrumentHidden || width == _width) return;
        _width = width;
        _instrumentColumn.MinWidth = Math.Min(200, width);
        _instrumentColumn.Width = new GridLength(width);
    }

    /// <summary>Shows or hides the instrument and sets its width (0 = automatic). Shown: the name and toggle head the instrument column and
    /// the toggle is lit. Hidden: the column has no width, and the name and toggle are a thin strip across the top of the row.</summary>
    public void ApplyInstrument(bool hidden, double width)
    {
        if (_applied && hidden == _instrumentHidden && width == _width) return;
        _applied = true; _width = width;
        _instrumentHidden = hidden;
        var shown = hidden ? Visibility.Collapsed : Visibility.Visible;
        Instrument.Visibility = shown; _left.Visibility = shown; _widthGrip.Visibility = _stacked ? Visibility.Collapsed : shown;
        var home = hidden ? _root : _left;
        if (!ReferenceEquals(_header.Parent, home))
        {
            (_header.Parent as Panel)?.Children.Remove(_header);
            home.Children.Add(_header);
        }
        // The strip is one line of the name: the toggle shrinks to it; the row grows by the strip (StackedExtra), so the lane loses nothing.
        _header.Height = hidden ? StripHeight : double.NaN;
        _title.Margin = hidden ? new Thickness(8, 1, 8, 1) : new Thickness(8, 4, 8, 4);
        _toggleInstrument.Width = _toggleInstrument.Height = hidden ? StripHeight - 2 : 22;
        _toggleInstrument.Padding = new Thickness(hidden ? 2 : 3);
        _backdrop.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        var wanted = hidden ? new GridLength(HiddenHeaderWidth) : width > 0 ? new GridLength(width) : new GridLength(38, GridUnitType.Star);
        var min = hidden ? 0 : width > 0 ? Math.Min(200, width) : 200;
        if (_instrumentColumn.Width != wanted) _instrumentColumn.Width = wanted;
        if (_instrumentColumn.MinWidth != min) _instrumentColumn.MinWidth = min;
        _instrumentColumn.MaxWidth = hidden ? HiddenHeaderWidth : double.PositiveInfinity;
        if (hidden) { _toggleInstrument.ClearValue(BackgroundProperty); _toggleInstrument.ClearValue(BorderBrushProperty); }
        else
        {
            _toggleInstrument.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
            _toggleInstrument.SetResourceReference(BorderBrushProperty, "AccentBrush");
        }
        _toggleInstrument.ToolTip = (hidden ? "Show the " : "Hide the ") + _panelWord;
    }

    /// <summary>Places the instrument above the lane at the full row width (<paramref name="above"/>) or beside it, and shows or hides
    /// the track colour frame. Above, the instrument has the height the row fit leaves it, up to the single-track instrument pane's; the stored width is kept for beside.</summary>
    public void Arrange(bool above, bool framed)
    {
        if (framed != _framed)
        {
            _framed = framed;
            BorderThickness = new Thickness(framed ? 0 : 1);
            _frame.Set(framed, _selected);
        }
        if (above != _stacked)
        {
            _stacked = above;
            Grid.SetColumnSpan(_left, above ? 2 : 1); Grid.SetRowSpan(_left, above ? 1 : 2);
            Grid.SetColumn(Lane, above ? 0 : 1); Grid.SetColumnSpan(Lane, above ? 2 : 1); Grid.SetRow(Lane, above ? 1 : 0); Grid.SetRowSpan(Lane, above ? 1 : 2);
            _widthGrip.Visibility = above || _instrumentHidden ? Visibility.Collapsed : Visibility.Visible;
        }
        if (UpdateExtra()) StackedExtraChanged?.Invoke();
    }

    /// <summary>The room above the lane between an instrument that lies over it and the lane's first line.</summary>
    private const double LaneGap = 6;
    private double _room = double.PositiveInfinity;
    /// <summary>True while the instrument lies above the lane and shows.</summary>
    internal bool InstrumentAbove => _stacked && !_instrumentHidden;
    /// <summary>The instrument's height above the lane now (0 beside the lane or hidden), the least it stays readable at, and its natural height.</summary>
    internal double InstrumentHeightNow => InstrumentAbove && !double.IsNaN(Instrument.Height) ? Instrument.Height : 0;
    internal double InstrumentMin => Math.Min(Instrument.RequiredHeight, Instrument.MediumHeight());
    internal double InstrumentNatural => Instrument.MediumHeight();

    /// <summary>The height the row fit leaves for this row's instrument above the lane: it takes that, between its least and its natural height.</summary>
    internal void SetInstrumentRoom(double room) { _room = room; UpdateExtra(); }

    /// <summary>Sizes the instrument above the lane and works out what the row has on top of its lane; true when that changed.</summary>
    private bool UpdateExtra()
    {
        var above = _stacked;
        var height = above ? Math.Max(InstrumentMin, Math.Min(InstrumentNatural, _room)) : double.NaN;
        if (!height.Equals(Instrument.Height) && !(Math.Abs(height - Instrument.Height) < 0.5)) Instrument.Height = height;
        var gap = InstrumentAbove ? LaneGap : 0;
        if (Lane.Margin.Top != gap) Lane.Margin = new Thickness(0, gap, 0, 0);
        var extra = _instrumentHidden ? StripHeight : above ? (_header.ActualHeight > 1 ? _header.ActualHeight : HeaderHeight) + height + gap : 0;
        // One system at the lane's full text size (the lane shrinks its text only below this height).
        var zoom = Lane.Editor.Zoom;
        var full = zoom <= 0 ? 0 : Lane.Editor.SystemHeightNow / zoom * Math.Clamp(Lane.TextScale, 0.5, 2.0) * Lane.UserZoom;
        // A tab-only lane keeps its text size at exactly one system; a lane with notation needs a little over it.
        var system = full <= 0 ? 0 : Lane.Editor.Notation == NotationMode.TabOnly ? Math.Ceiling(full) : Math.Ceiling(full / 0.98) + 1;
        if (Math.Abs(extra - StackedExtra) < 0.5 && Math.Abs(system - SystemHeight) < 0.5) return false;
        StackedExtra = extra; SystemHeight = system;
        return true;
    }

    /// <summary>The height the row has on top of its lane's: the instrument and its name while they lie above the lane, or the thin name strip of a hidden instrument (0 beside the lane).</summary>
    internal double StackedExtra { get; private set; }
    internal event Action? StackedExtraChanged;
    /// <summary>The lane height that holds one system of this track at its full text size (0 until the row is arranged).</summary>
    internal double SystemHeight { get; private set; }
    /// <summary>What lies between the row's edge and its lane, top plus bottom: the colour frame, or the plain outline.</summary>
    internal double Chrome => _framed ? 2 * BandRowFrame.Inset : 2;
    internal bool Stacked => _stacked;
    internal BandRowFrame Frame => _frame;
    internal FrameworkElement InstrumentSide => _left;
    internal bool WidthGripShown => _widthGrip.Visibility == Visibility.Visible;

    public TrackModel Track { get; }

    /// <summary>The part of the row a drag starts from (the name strip).</summary>
    public FrameworkElement DragHandle { get; }

    /// <summary>Moves the whole row (instrument and tab) without a layout pass.</summary>
    public TranslateTransform Shift { get; } = new();

    /// <summary>Raised while the bottom edge is dragged: the height the row should take.</summary>
    public event Action<double>? ResizeRequested;

    /// <summary>Raised by a double-click on the bottom edge: back to the shared height.</summary>
    public event Action? ResizeReset;

    /// <summary>The bottom edge was dragged to <paramref name="height"/> (also what a self-test calls).</summary>
    internal void RequestHeight(double height) => ResizeRequested?.Invoke(height);

    internal void RequestReset() => ResizeReset?.Invoke();

    private void OnGripDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { RequestReset(); e.Handled = true; return; }
        _grab = ActualHeight - e.GetPosition(this).Y;
        _grip.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (double.IsNaN(_grab) || !_grip.IsMouseCaptured) return;
        RequestHeight(e.GetPosition(this).Y + _grab);
    }

    private void OnGripUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _grab = double.NaN;
        _grip.ReleaseMouseCapture();
    }

    /// <summary>What the row shows of its track (name, colour, instrument, strings); a different <see cref="KeyOf"/> means the row is stale.</summary>
    public int Key { get; }

    public static int KeyOf(TrackModel track)
    {
        var hash = new HashCode();
        hash.Add(track.Name); hash.Add(track.ColorHex); hash.Add(track.InstrumentName); hash.Add(track.Kind); hash.Add(track.MidiChannel);
        hash.Add(track.NumberOfFrets); hash.Add(track.Capo);
        var tunings = track.StringTunings;
        for (var i = 0; i < tunings.Count; i++) hash.Add(tunings[i]);
        return hash.ToHashCode();
    }

    /// <summary>The track's index in the song when the row was built.</summary>
    public int TrackIndex { get; }

    public InstrumentPanel Instrument { get; } = new();

    /// <summary>The share of the window the main score's viewport takes (the rest is the side panel), so a row of a given width
    /// draws at the same "Fit width" scale as the main score in that window.</summary>
    private const double ScoreViewportShare = 0.8;

    /// <summary>Tab lane and instrument follow the main score's scale rule (see <see cref="ScoreZoomController.FitZoom"/>).</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!sizeInfo.WidthChanged) return;
        var scale = ScoreZoomController.FitZoom(ActualWidth * ScoreViewportShare);
        Lane.TextScale = scale;
        Instrument.ContentScale = scale;
    }

    public BandLane Lane { get; } = new();

    /// <summary>The selected track's row has an accent outline.</summary>
    public void SetSelected(bool selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        _frame.Set(_framed, selected);
        SetResourceReference(BorderBrushProperty, selected ? "AccentBrush" : "BorderSoftBrush");
    }

    private static Color ColourOf(TrackModel track) =>
        ColourHex.TryParse(track.ColorHex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : Colors.Gray;
}

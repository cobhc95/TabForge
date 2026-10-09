using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views.Band;

// Owns: one track's row: the track name over its instrument view on the left, its tab lane on the right, a grip on the bottom edge
//   that resizes the row, and a vertical shift transform used while the row is dragged or makes way.
// Does not own: what the instrument shows (the controller feeds it), the lane's drawing (BandLane) or the row's place in the list.
// Tests: TestBandViewRows, TestBandRowSizing.
internal sealed class BandRow : Border
{
    private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _grip = new() { Height = 6, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeNS };
    private readonly ColumnDefinition _instrumentColumn = new() { Width = new GridLength(38, GridUnitType.Star), MinWidth = 200 };
    private readonly Border _widthGrip = new() { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeWE };
    private readonly Button _hideInstrument = new() { Content = "×", Width = 18, Height = 18, Padding = new Thickness(0), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0), ToolTip = "Hide this row's instrument" };
    private readonly Button _toggleInstrument = new() { Width = 22, Height = 22, Padding = new Thickness(3), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    internal const double HiddenHeaderWidth = 170;
    private readonly Grid _left = new() { ClipToBounds = true };
    private bool _instrumentHidden, _applied;
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
        var title = new DockPanel { Margin = new Thickness(8, 4, 8, 4), LastChildFill = true };
        DockPanel.SetDock(chip, Dock.Left);
        title.Children.Add(chip);
        var fretboard = new Path { Data = (Geometry)Application.Current.FindResource("IconFretboard"), StrokeThickness = 1.4, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round };
        fretboard.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        _toggleInstrument.Content = fretboard;
        _toggleInstrument.SetResourceReference(StyleProperty, "ToolButton");
        System.Windows.Automation.AutomationProperties.SetName(_toggleInstrument, "Show or hide the instrument of " + track.Name);
        DockPanel.SetDock(_toggleInstrument, Dock.Right);
        title.Children.Add(_toggleInstrument);
        _toggleInstrument.Click += (_, _) => InstrumentToggleRequested?.Invoke();
        _hideInstrument.SetResourceReference(StyleProperty, "ToolButton");
        System.Windows.Automation.AutomationProperties.SetName(_hideInstrument, "Hide the instrument of " + track.Name);
        _hideInstrument.Click += (_, _) => InstrumentToggleRequested?.Invoke();
        _widthGrip.MouseLeftButtonDown += OnWidthDown;
        _widthGrip.MouseMove += OnWidthMove;
        _widthGrip.MouseLeftButtonUp += (_, _) => { var dragged = !double.IsNaN(_gripGrab); _gripGrab = double.NaN; _widthGrip.ReleaseMouseCapture(); if (dragged) WidthCommitted?.Invoke(); };
        title.Children.Add(_name);

        Instrument.Title = track.Name;
        Instrument.AudioTrack = track.IsAudio;
        System.Windows.Automation.AutomationProperties.SetName(Instrument, "Instrument of " + track.Name);
        var left = _left;
        left.SetResourceReference(Panel.BackgroundProperty, "InstrumentHostBrush");
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(title, 0);
        Grid.SetRow(Instrument, 1);
        left.Children.Add(title);
        left.Children.Add(Instrument);
        left.Children.Add(_hideInstrument);
        Grid.SetRow(_hideInstrument, 1);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(_instrumentColumn);
        _grid = grid;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
        Grid.SetColumn(Lane, 1);
        grid.Children.Add(left);
        grid.Children.Add(Lane);
        grid.Children.Add(_widthGrip);
        Panel.SetZIndex(_widthGrip, 2);
        ApplyInstrument(false, 0);
        var root = new Grid();
        root.Children.Add(grid);
        root.Children.Add(_grip);
        Child = root;
        RenderTransform = Shift;
        DragHandle = title;
        title.Background = Brushes.Transparent;
        _grip.MouseLeftButtonDown += OnGripDown;
        _grip.MouseMove += OnGripMove;
        _grip.MouseLeftButtonUp += OnGripUp;

        System.Windows.Automation.AutomationProperties.SetName(Lane, "Tab of " + track.Name);
        Lane.Follow = follow;
        Lane.SetContent(BandChoices.NormalizeContent(band.LaneContent));
        Lane.Bind(project, trackIndex, look);
    }

    private readonly Grid _grid;

    /// <summary>Raised by the header's instrument icon and the instrument's × : show or hide this row's instrument.</summary>
    public event Action? InstrumentToggleRequested;

    /// <summary>Raised while the grip between instrument and lane is dragged: the width the instrument should take.</summary>
    public event Action<double>? WidthRequested;

    /// <summary>Raised by a double-click on the grip: back to the default width.</summary>
    public event Action? WidthReset;

    /// <summary>Raised when a width drag ends (the width is kept).</summary>
    public event Action? WidthCommitted;

    internal void RequestWidth(double width) => WidthRequested?.Invoke(width);
    internal void RequestWidthReset() => WidthReset?.Invoke();
    internal void RequestInstrumentToggle() => InstrumentToggleRequested?.Invoke();
    internal bool InstrumentHidden => _instrumentHidden;
    internal double InstrumentColumnWidth => _left.ActualWidth;

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

    /// <summary>Shows or hides the instrument and sets its width (0 = automatic); the header keeps the name and the icon, lit while the instrument shows.</summary>
    public void ApplyInstrument(bool hidden, double width)
    {
        if (_applied && hidden == _instrumentHidden && width == _width) return;
        _applied = true; _width = width;
        _instrumentHidden = hidden;
        var shown = hidden ? Visibility.Collapsed : Visibility.Visible;
        Instrument.Visibility = shown; _hideInstrument.Visibility = shown; _widthGrip.Visibility = shown;
        // Hidden: a header-only strip of fixed width (never content-sized), the lane takes the rest.
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
        _toggleInstrument.ToolTip = hidden ? "Show this row's instrument" : "Hide this row's instrument";
    }

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
        SetResourceReference(BorderBrushProperty, selected ? "AccentBrush" : "BorderSoftBrush");
    }

    private static Color ColourOf(TrackModel track) =>
        ColourHex.TryParse(track.ColorHex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : Colors.Gray;
}

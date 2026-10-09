using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views.Band;

// Owns: the Band panel's content: a strip of track pills on top and under it a vertical list of rows sized so a set number fill the
//   viewport (a row dragged to its own height keeps it), the rest scrolling.
// Does not own: which rows exist or in what order (BandLayoutState and BandViewController) or anything inside a row.
// Tests: TestBandViewRows, TestBandPillsAndRows, TestBandRowSizing.
internal sealed class BandView : Grid
{
    private const double MinAutoRowHeight = 120;
    internal const string EmptyNoTracks = "This song has no tracks yet. Add a track to see it here.";
    internal const string EmptyNoneShown = "No rows are shown. Click a track's name above to show its row.";

    private readonly BandLayoutState _state;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _stack = new();
    private readonly WrapPanel _pills = new() { Margin = new Thickness(4, 4, 4, 2) };
    private readonly TextBlock _empty = new()
    {
        Text = EmptyNoTracks, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, FontSize = 14
    };
    private readonly List<BandRow> _rows = new();

    public BandView(BandLayoutState state)
    {
        _state = state;
        Reorder = new BandReorder(_stack, _scroll, () => _rows);
        Reorder.Dropped += (row, index) => RowDropped?.Invoke(row, index);
        ClipToBounds = true;
        _scroll.ClipToBounds = true;
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.Margin = new Thickness(16);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_scroll, 1);
        SetRow(_empty, 1);
        _scroll.Content = _stack;
        Children.Add(_pills);
        Children.Add(_scroll);
        Children.Add(_empty);
        System.Windows.Automation.AutomationProperties.SetName(this, "Band view");
        _scroll.SizeChanged += (_, _) => FitRows();
    }

    /// <summary>Raised when a pill is clicked: the track whose row should show or hide.</summary>
    public event Action<TrackModel>? PillClicked;

    /// <summary>Raised when a dragged row is dropped in a new place: the row and its index among the rows.</summary>
    public event Action<BandRow, int>? RowDropped;

    /// <summary>Raised by Ctrl + mouse wheel over the view: true = bigger.</summary>
    public event Action<bool>? LaneZoomWheel;

    protected override void OnPreviewMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && e.Delta != 0) { LaneZoomWheel?.Invoke(e.Delta > 0); e.Handled = true; return; }
        base.OnPreviewMouseWheel(e);
    }

    public BandReorder Reorder { get; }

    public int RowsPerScreen => _state.RowsPerScreen;

    /// <summary>The message shown when there are no rows, and whether it is on screen.</summary>
    internal string EmptyText => _empty.Text;
    internal bool EmptyVisible => _empty.Visibility == Visibility.Visible;
    internal double ScrollOffset => _scroll.VerticalOffset;
    internal double ScrollableHeight => _scroll.ScrollableHeight;

    public IReadOnlyList<BandRow> Rows => _rows;

    public IReadOnlyList<ToggleButton> Pills => _pills.Children.OfType<ToggleButton>().ToList();

    /// <summary>The height of the rows' viewport (the panel under the pills).</summary>
    public double Viewport => _scroll.ActualHeight;

    /// <summary>One pill per track, in the song's order, lit when the track's row is shown.</summary>
    public void SetPills(IReadOnlyList<TrackModel> tracks)
    {
        _pills.Children.Clear();
        foreach (var track in tracks)
        {
            var label = new TextBlock { Text = track.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160, VerticalAlignment = VerticalAlignment.Center };
            // The app-wide TextBlock style paints TextBrush; the pill's state colour (TrackPill style) must win.
            label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1) });
            var chip = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(ColourHex.TryParse(track.ColorHex, out var c) ? Color.FromRgb(c.R, c.G, c.B) : Colors.Gray)
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(chip);
            content.Children.Add(label);
            var pill = new ToggleButton
            {
                IsChecked = _state.IsShown(track), Margin = new Thickness(0, 0, 4, 4), Content = content,
                ToolTip = track.Name + " (click to show or hide its row)"
            };
            pill.SetResourceReference(StyleProperty, "TrackPill");
            System.Windows.Automation.AutomationProperties.SetName(pill, "Show " + track.Name + " in Band view");
            var t = track;
            pill.Click += (_, _) => PillClicked?.Invoke(t);
            _pills.Children.Add(pill);
        }
    }

    /// <summary>Shows these rows in this order; rows that are no longer shown let go of their song.</summary>
    public void SetRows(IReadOnlyList<BandRow> rows)
    {
        Reorder.Cancel();
        _stack.Children.Clear();
        foreach (var old in _rows) if (!rows.Contains(old)) old.Lane.Release();
        _rows.Clear();
        foreach (var row in rows) { _stack.Children.Add(row); _rows.Add(row); }
        _empty.Visibility = _stack.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = _state.Order.Count == 0 ? EmptyNoTracks : EmptyNoneShown;
        FitRows();
    }

    /// <summary>Hooks a new row's drag and resize to this view.</summary>
    public void Adopt(BandRow row)
    {
        Reorder.Attach(row);
        row.ResizeRequested += h => { _state.SetHeight(row.Track, h, Viewport); FitRows(); };
        row.ResizeReset += () => { _state.ResetHeight(row.Track); FitRows(); };
    }

    /// <summary>Shown rows that still share the screen when rows per screen is the default 3 or more: a 4th track fits without scrolling.</summary>
    public const int FitShownUpTo = 4;

    /// <summary>The shared row height: the shown rows fill the viewport, up to <see cref="RowsPerScreen"/> of them (or <see cref="FitShownUpTo"/>
    /// at 3 or more); further rows scroll (never below the readable minimum).</summary>
    public double RowHeight
    {
        get
        {
            var fit = RowsPerScreen >= BandLayoutState.DefaultRowsPerScreen ? Math.Max(RowsPerScreen, FitShownUpTo) : RowsPerScreen;
            var n = Math.Clamp(_rows.Count, 1, fit);
            return Math.Max(MinAutoRowHeight, (Viewport - 2 * n) / n);
        }
    }

    /// <summary>Sizes every row: its own height when it was dragged to one, else the shared height.</summary>
    public void FitRows()
    {
        if (Viewport <= 0) return;
        var shared = RowHeight;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var height = _state.HeightOf(row.Track) is { } own ? BandLayoutState.ClampHeight(own, Viewport) : shared;
            if (double.IsNaN(row.Height) || Math.Abs(row.Height - height) > 0.5) row.Height = height;
        }
    }
}

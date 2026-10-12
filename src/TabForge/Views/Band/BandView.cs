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
    // The pane's own caption (the dock draws no tab for it) with the pills on the same line; pills that do not fit stay on that line, cut off at the edge.
    private readonly TextBlock _caption = TrackPillStrip.Caption("BAND VIEW");
    private readonly Button _layout = BandLayoutMenu.Button();
    private readonly Button _close = TrackPillStrip.CloseButton("Close Band view", "View.BandView");
    /// <summary>The menu id the close button hands to the host: leave Band view, as the Band view command does.</summary>
    internal const string CloseId = "band.close";
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
        var header = new DockPanel();
        DockPanel.SetDock(_caption, Dock.Left);
        header.Children.Add(_caption); BandLayoutMenu.AlsoOpens(_caption, _layout);   // the whole caption opens the layout menu, not only the chevron
        DockPanel.SetDock(_layout, Dock.Left);
        header.Children.Add(_layout);
        // Docked before the pills, so their scroll area ends where the button begins.
        _close.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(_close, Dock.Right);
        header.Children.Add(_close);
        header.Children.Add(TrackPillStrip.PillScroll(_pills));
        Children.Add(header);
        Children.Add(_scroll);
        Children.Add(_empty);
        System.Windows.Automation.AutomationProperties.SetName(this, "Band view");
        _scroll.SizeChanged += (_, _) => FitRows();
        _scroll.ScrollChanged += (_, _) => SeeLanes();
    }

    /// <summary>Raised by the × at the right end of the header: leave Band view.</summary>
    public event Action? CloseRequested;
    internal Button CloseButton => _close;
    /// <summary>The header row (caption, roll-down button, pills, close).</summary>
    internal FrameworkElement Header => (FrameworkElement)Children[0];
    /// <summary>Raised with the roll-down menu just before it opens: another feature may add its rows below the layout rows.</summary>
    internal event Action<ContextMenu>? LayoutMenuOpening;
    internal void RaiseLayoutMenuOpening(ContextMenu menu) => LayoutMenuOpening?.Invoke(menu);
    /// <summary>The roll-down button after the caption (BandLayoutMenu opens its menu).</summary>
    internal Button LayoutButton => _layout;

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

    /// <summary>The "BAND VIEW" caption left of the pills.</summary>
    internal TextBlock Caption => _caption;

    public IReadOnlyList<ToggleButton> Pills => _pills.Children.OfType<ToggleButton>().ToList();

    /// <summary>The height of the rows' viewport (the panel under the pills).</summary>
    public double Viewport => _scroll.ActualHeight;

    /// <summary>One pill per track, in the song's order, lit when the track's row is shown.</summary>
    public void SetPills(IReadOnlyList<TrackModel> tracks)
    {
        _pills.Children.Clear();
        foreach (var track in tracks)
        {
            var pill = TrackPillStrip.Pill(track, track.Name + " (click to show or hide its row)", "Show " + track.Name + " in Band view");
            pill.IsChecked = _state.IsShown(track);
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
        row.ResizeRequested += h =>
        {
            var kept = _state.SetHeight(row.Track, h - row.StackedExtra, Viewport);
            if (row.SystemHeight > 1) _state.RememberSystems(_rows.Count > 0 && ReferenceEquals(row, _rows[0]), (int)Math.Round((kept - row.Chrome) / row.SystemHeight));
            FitRows();
        };
        row.StackedExtraChanged += FitRows;
        row.Lane.SizeChanged += (_, _) => SeeLanes();
        BandLaneHover.Attach(row.Lane);
        row.ResizeReset += () => { _state.ResetHeight(row.Track); FitRows(); };
    }

    /// <summary>True while the song plays (the controller sets it).</summary>
    internal bool Playing { get; set; }

    /// <summary>Raised when the part of a lane that lies inside the rows' viewport changed (a row resized, the rows scrolled, the pane resized).</summary>
    public event Action? LanesSeenChanged;

    /// <summary>Tells every lane how much of it, from its top, is inside the rows' viewport, so its follow keeps the playing system there.
    /// Runs on size and scroll events only. A lane whose top is outside the viewport is not limited.</summary>
    private void SeeLanes()
    {
        var changed = false;
        for (var i = 0; i < _rows.Count; i++)
        {
            var lane = _rows[i].Lane;
            var top = lane.IsDescendantOf(_scroll) ? lane.TransformToAncestor(_scroll).Transform(new Point()).Y : -1;
            var seen = top < -0.5 || top >= Viewport ? double.PositiveInfinity : Math.Min(lane.ActualHeight, Viewport - top);
            if (seen.Equals(lane.Seen)) continue;
            lane.Seen = seen;
            changed = true;
        }
        if (changed) LanesSeenChanged?.Invoke();
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
            // The name strips of hidden instruments come out of the shared height, so the rows still fill the viewport without scrolling.
            double strips = 0;
            for (var i = 0; i < _rows.Count && i < n; i++) if (_rows[i].InstrumentHidden) strips += BandRow.StripHeight;
            return Math.Max(MinAutoRowHeight, (Viewport - 2 * n - strips) / n);
        }
    }

    /// <summary>Sizes every row: its own height when it was dragged to one, else the shared height; a row with its instrument above the lane is taller by the instrument, one with a hidden instrument by its name strip.</summary>
    public void FitRows()
    {
        if (Viewport <= 0) return;
        var shared = RowHeight;
        // The default: whole systems per row, as many as fit (BandRowFit); "rows per screen" shares the viewport instead.
        Func<BandRow, double?> kept = r => _state.HeightOf(r.Track) is { } h ? BandLayoutState.ClampHeight(h, Viewport) : null;
        var (systems, main) = _state.AutoSystems < 0 ? (0.0, 0.0) : BandRowFit.Fit(_rows, kept, Viewport, _state.AutoSystems, _state.MainSystems);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var height = _state.HeightOf(row.Track) is { } own ? BandLayoutState.ClampHeight(own, Viewport) + row.StackedExtra : systems > 0 ? BandRowFit.Height(row, i == 0 ? main : systems) : shared + row.StackedExtra;
            if (double.IsNaN(row.Height) || Math.Abs(row.Height - height) > 0.5) row.Height = height;
        }
    }
}

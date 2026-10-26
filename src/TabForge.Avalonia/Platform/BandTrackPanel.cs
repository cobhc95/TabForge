using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: one track's card in the Band view: a rounded border in the track colour (the selected card too) on the second panel shade,
//   a header with a small track-colour tick, the track name and the fretboard toggle at the right (accent-filled while on), the track's
//   fretboard above or beside its staff while on, and the staff: a ScorePreviewView that draws only this track, tablature only at first.
//   The staff shows a window of ShownSystems systems that the Band view slides to the playing system; the card is as tall as that content, so
//   the Band view's one scroll bar is the only one (a wheel over the staff scrolls the view).
// Does not own: which cards are shown, their order and which fretboards are on by default (BandView), the playback session (passed to the
//   staff), the notes that light the fretboard (none here), the layout choices (BandView passes them in).
// Tests: TabForge.Avalonia.Tests.BandCardTests, TabForge.Avalonia.Tests.BandViewTests.
public sealed partial class BandTrackPanel : Border
{
    /// <summary>The header row's height: the tick, the name and the toggle sit in it.</summary>
    public const double TitleHeight = 22;

    private const double FrameThickness = 1.5;
    private const double PlainThickness = 1;
    private const double CardRadius = 4;
    private static readonly Thickness CardPadding = new(8, 4);
    /// <summary>The theme brush of the card's fill (the second panel shade).</summary>
    private const string CardBackgroundKey = "Panel2Brush";
    private const double TickWidth = 4;
    private const double TickHeight = 16;
    private const double TickRadius = 2;
    private const double TickLeftMargin = 8;
    private const double TickGap = 8;
    private const double ToggleRightMargin = 6;
    private const double ToggleRadius = 4;
    private const double NameSize = 12;

    /// <summary>How many systems of its staff a card shows: a window that slides to the playing system.</summary>
    public const int ShownSystems = 2;

    /// <summary>How many systems every card but the first shows.</summary>
    public const int OtherCardSystems = 1;

    /// <summary>How many systems this card shows: <see cref="ShownSystems"/> for the first card, <see cref="OtherCardSystems"/> for the others.</summary>
    public int SystemCount
    {
        get => _systemCount;
        set { var count = Math.Clamp(value, 1, 6); if (_systemCount == count) return; _systemCount = count; ApplySystemWindow(); }
    }

    private int _systemCount = OtherCardSystems;

    /// <summary>The distance one wheel notch over a card scrolls the Band view, in DIP.</summary>
    private const double WheelStep = 50;

    /// <summary>The staff's page margin in a card, and the room left of its first bar for the clef and the T-A-B letters (the main score keeps 44 and 46).</summary>
    private const double StaffPagePadding = 0, StaffLabelGutter = 26;

    /// <summary>The height and width of a card's keyboard or drum map: fixed, centred above the staff. A fretboard takes its size from <see cref="InstrumentHeight"/>.</summary>
    private const double FixedInstrumentHeight = InstrumentPanelView.PanelHeight, FixedInstrumentWidth = 760;

    // The card's track is read from the document each time (by index): an undo or redo replaces the TrackModel objects. A track that left the song is null (the card hides).
    private readonly SongDocument _document;
    private TrackModel? _track => TrackIndex < _document.Project.Tracks.Count ? _document.Project.Tracks[TrackIndex] : null;
    private readonly Border _tick = new() { Width = TickWidth, Height = TickHeight, CornerRadius = new CornerRadius(TickRadius), Margin = new Thickness(TickLeftMargin, 0, TickGap, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, FontSize = NameSize, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Canvas _fretboardIcon = BandFretboardIcon.Build();
    private readonly Button _fretboardToggle = new() { Width = BandFretboardIcon.Width + 4, Height = BandFretboardIcon.Height + 4, Padding = new Thickness(0), Margin = new Thickness(0, 0, ToggleRightMargin, 0), CornerRadius = new CornerRadius(ToggleRadius), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _body = new();
    private readonly ScrollViewer _staffScroll;
    private readonly BandFretboardGrip _grip;
    private readonly BandFretboardGrip _cardGrip;
    private double? _chosenHeight;
    private double _autoHeight;
    private bool _selected;
    private AvaloniaPlaybackSession? _instrumentSession;
    private bool _attached;
    private int _windowFirst;

    /// <summary>Whether the card is the selected one.</summary>
    public bool IsSelected => _selected;
    private bool _colourFrame = true;
    private bool _fretboardAbove = true;
    private bool _fretboardShown;

    /// <summary>The track's index in the song; the card draws that track only.</summary>
    public int TrackIndex { get; }

    /// <summary>The staff: a score view of this track alone, tablature only, with the playhead and bar follow of the playback.</summary>
    public ScorePreviewView Staff { get; } = new();

    /// <summary>The track's own fretboard or keyboard, above or beside the staff while <see cref="FretboardShown"/>.</summary>
    public InstrumentPanelView Instrument { get; } = new();

    /// <summary>Whether the track's fretboard shows in the card.</summary>
    public bool FretboardShown => _fretboardShown;

    /// <summary>Raised with the track index when the card is pressed anywhere (its staff included).</summary>
    public event Action<int>? Selected;

    /// <summary>Raised with the height the user's drag of the fretboard's bottom edge asks for.</summary>
    public event Action<double>? InstrumentResized;

    /// <summary>Raised when the drag of the fretboard's edge ends.</summary>
    public event Action? InstrumentResizeEnded;

    /// <summary>Raised with the fretboard's toggle; the owner decides and calls <see cref="SetFretboardShown"/>.</summary>
    public event Action<int>? FretboardToggled;

    /// <summary>Name of the card's bottom-edge grip, so tests find it beside the fretboard's own grip.</summary>
    public const string CardGripName = "CardResizeGrip";

    /// <summary>Raised with the total card height the user drags the bottom edge to, in DIP.</summary>
    public event Action<double>? CardResized;

    /// <summary>Raised when the drag of the card's bottom edge ends.</summary>
    public event Action? CardResizeEnded;

    /// <summary>Raised with the row count picked in the header's right-click menu.</summary>
    public event Action<int>? RowsPicked;

    public BandTrackPanel(SongDocument document, int trackIndex)
    {
        TrackIndex = trackIndex;
        _document = document;
        Staff.ShowInPanel(document);
        Staff.LightPaper = !document.DarkPaper;   // a card draws the song's own paper
        Staff.DrawsHeader = false;
        Staff.Notation = NotationMode.TabOnly;
        // The card's score starts at the card's padding: no page margin, only the room the clef and the T-A-B letters need.
        Staff.Probe.TightLineBand = true;
        Staff.Probe.PagePadding = StaffPagePadding;
        Staff.Probe.LabelGutter = StaffLabelGutter;
        Staff.ShowTrack(trackIndex);
        // The Band view slides the card's window to the playing system (ShowSystemsAround); the staff's own follow would scroll it again.
        Staff.Follow = new TabForge.Services.FollowSettings { FollowPlayback = false };
        Instrument.Show(document.Project.Tracks[trackIndex], Array.Empty<InstrumentLitNotes.Lit>());
        Instrument.IsVisible = false;
        // The fretboard paints no backdrop of its own in a card, so the card's fill shows round it.
        Instrument.TransparentBackdrop = true;
        Instrument.MinDrawingScale = BandFretboardSize.MinScale;
        _autoHeight = InstrumentAutoMaxHeight;
        _grip = new BandFretboardGrip(Instrument) { IsVisible = false };
        _grip.Dragged += height => InstrumentResized?.Invoke(height);
        _grip.Released += () => InstrumentResizeEnded?.Invoke();
        // The card's own bottom edge: the user drags it to resize the card (the height is measured from the card's top).
        // It floats over the card's bottom, so it takes no room from the staff.
        _cardGrip = new BandFretboardGrip(this, cardEdge: true) { Name = CardGripName };
        _cardGrip.Dragged += height => CardResized?.Invoke(height);
        _cardGrip.Released += () => CardResizeEnded?.Invoke();
        _title.Text = document.Project.Tracks[trackIndex].Name;
        ToolTip.SetTip(_title, _title.Text);
        _fretboardToggle.Content = _fretboardIcon;
        _fretboardToggle.Click += (_, _) => FretboardToggled?.Invoke(TrackIndex);
        // The staff scrolls inside its window (no bar of its own); a wheel over it scrolls the Band view, the one scroll bar.
        _staffScroll = new ScrollViewer { Content = Staff, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        _staffScroll.AddHandler(InputElement.PointerWheelChangedEvent, ForwardWheel, RoutingStrategies.Tunnel);
        // The systems reflow when the staff's size or the card's width changes, so the window is placed again then.
        Staff.SizeChanged += (_, _) => ApplySystemWindow();
        _staffScroll.SizeChanged += (_, _) => ApplySystemWindow();
        // An offset set before the viewer's extent caught up is clamped; the extent's change places the window again.
        _staffScroll.ScrollChanged += (_, e) => { if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0) ApplySystemWindow(); };
        Child = BuildCard();
        CornerRadius = new CornerRadius(CardRadius);
        Padding = CardPadding;
        this[!BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(CardBackgroundKey);
        AddHandler(InputElement.PointerPressedEvent, (_, _) => Selected?.Invoke(TrackIndex), RoutingStrategies.Tunnel);
        // The fretboard lights the score cursor's notes while stopped and the playing notes while playing (set below); an edit under it repaints here.
        Staff.CursorChanged += RefreshInstrumentCursor;
        this.FollowTheme(ApplyLook);
        ApplyPlacement();
        ApplyLook();
        RefreshInstrumentCursor();
    }

    /// <summary>Follows <paramref name="session"/> (null unbinds) in the staff and the fretboard: the staff draws its playhead, the fretboard lights the playing notes.</summary>
    public void BindPlayback(AvaloniaPlaybackSession? session)
    {
        Staff.BindPlayback(session);
        if (ReferenceEquals(_instrumentSession, session)) return;
        UnsubscribeInstrument();
        _instrumentSession = session;
        if (_attached) SubscribeInstrument();
        ShowInstrumentPlayback();
        RefreshInstrumentCursor();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        SubscribeInstrument();
        ShowInstrumentPlayback();
        RefreshInstrumentCursor();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UnsubscribeInstrument();
        Instrument.ShowPlayback(null, 0);
    }

    private void SubscribeInstrument()
    {
        if (_instrumentSession is null) return;
        _instrumentSession.PlayheadFrame += ShowInstrumentPlayback;
        _instrumentSession.PlayheadBarChanged += ShowInstrumentPlayback;
        _instrumentSession.StateChanged += ShowInstrumentPlayback;
    }

    private void UnsubscribeInstrument()
    {
        if (_instrumentSession is null) return;
        _instrumentSession.PlayheadFrame -= ShowInstrumentPlayback;
        _instrumentSession.PlayheadBarChanged -= ShowInstrumentPlayback;
        _instrumentSession.StateChanged -= ShowInstrumentPlayback;
    }

    // The playing notes of this card's track at the playhead, or none when the song is not playing (the view's own dedup repaints only on change).
    private void ShowInstrumentPlayback()
    {
        if (!Instrument.Look.FollowFretboard || _instrumentSession is not { IsPlaying: true } session || session.Transport.Timeline is not { } timeline)
        {
            Instrument.ShowPlayback(null, 0);
            return;
        }
        Instrument.ShowPlayback(timeline.NotesFor(TrackIndex), session.Transport.PlayheadMs);
    }

    /// <summary>Re-lights the fretboard with the staff cursor's notes (an edit under it calls this); while playback shows, the playing notes win.</summary>
    internal void RefreshInstrumentCursor()
    {
        IsVisible = _track is not null;   // a track that left the song leaves no stale card
        if (_track is not { IsAudio: false } track) return;
        var cursor = Staff.Editor.Cursor;
        Instrument.Show(track, cursor is { } c ? InstrumentLitNotes.At(track, c.Measure, c.Cell) : Array.Empty<InstrumentLitNotes.Lit>());
    }

    /// <summary>Whether the instrument is a fretboard, the only one whose size the user and the fit change.</summary>
    internal bool InstrumentResizable => Instrument.Kind == InstrumentPanelKind.Fretboard;

    /// <summary>The string count of the track's fretboard (the panel's default of six without tuning).</summary>
    public int InstrumentStrings => _track is { StringTunings.Count: > 0 } track ? track.StringTunings.Count : DefaultStrings;

    /// <summary>The layout input of the card's fretboard (strings, frets, bass): the card's width follows the board's real size.</summary>
    public FretboardGeometryRules.Input InstrumentShape => Instrument.FretInput;

    private const int DefaultStrings = 6;

    /// <summary>The smallest height of the card's instrument.</summary>
    public double InstrumentMinHeight => InstrumentResizable ? BandFretboardSize.MinHeight(InstrumentStrings) : FixedInstrumentHeight;

    /// <summary>The height the automatic size never passes.</summary>
    public double InstrumentAutoMaxHeight => InstrumentResizable ? BandFretboardSize.AutoMaxHeight : FixedInstrumentHeight;

    /// <summary>The largest height the user can drag the fretboard to.</summary>
    internal double InstrumentMaxHeight => InstrumentResizable ? BandFretboardSize.MaxHeight(InstrumentStrings) : FixedInstrumentHeight;

    /// <summary>True while the fretboard shows at the automatic size (the user has chosen none).</summary>
    public bool InstrumentIsAuto => _fretboardShown && InstrumentResizable && _chosenHeight is null;

    /// <summary>The height the card's instrument has now: the user's, else the automatic one.</summary>
    public double InstrumentHeight => InstrumentResizable ? Math.Clamp(_chosenHeight ?? _autoHeight, InstrumentMinHeight, InstrumentMaxHeight) : FixedInstrumentHeight;

    /// <summary>Sets the size the user chose (null = automatic) and the automatic size, then lays the instrument out at the one in force.</summary>
    public void SetInstrumentSize(double? chosen, double auto)
    {
        _chosenHeight = chosen;
        _autoHeight = auto;
        ApplyPlacement();
    }

    /// <summary>The card's height with <paramref name="lines"/> lines of its staff: the header, the padding and border, the fretboard above while shown, and the lines at the staff's zoom, less the unused top.</summary>
    public double NaturalHeight(int lines)
    {
        var staff = StaffLineHeight() * lines - (lines > 0 ? CropUnits() * Staff.Zoom : 0);
        var instrument = _fretboardShown ? InstrumentHeight : 0;
        var body = _fretboardShown && !_fretboardAbove ? Math.Max(instrument, staff) : instrument + staff;
        return TitleHeight + body + Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;
    }

    /// <summary>The height of one system of the staff at its zoom (a line of the card), or 0 for an audio track.</summary>
    public double StaffLineHeight() => StaffLineUnits() * Staff.Zoom;

    /// <summary>The system height before the zoom, for the Band view's fit.</summary>
    public double StaffLineUnits()
    {
        if (Staff.Probe.Track is not { IsAudio: false } track) return 0;
        Staff.Probe.Layout.GetLayout(track);   // the system height follows the marks the layout finds, so it is laid out before it is read
        return Staff.Probe.StaffTop(1) - Staff.Probe.StaffTop(0);
    }

    /// <summary>The room above the tab that the window's first system leaves unused, cut from the card's top (before the zoom).</summary>
    public double CropUnits() => TopCropFromFirst(Staff, _windowFirst, _systemCount);

    /// <summary>How many whole systems show in a card <paramref name="height"/> tall (at least one, at most the systems the staff lays out).</summary>
    internal int SystemsForHeight(double height) => BandRowRules.Snap(height, NaturalHeight, Math.Max(1, PlaySystemCount()));

    /// <summary>Marks the card selected; the frame keeps the track colour, so the mark is the selection state only.</summary>
    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyLook();
    }

    /// <summary>The top of the card's window of systems: the first system shown, in the staff's own (zoomed) coordinates.</summary>
    public double WindowTop => _staffScroll.Offset.Y;

    /// <summary>The height of the card's window of systems (<see cref="ShownSystems"/> systems of its staff).</summary>
    public double WindowHeight => _staffScroll.Height;

    /// <summary>Slides the card's window of systems so it shows the system holding <paramref name="measure"/>: the window stays while the playing
    /// system is inside it and jumps (the system at the top) once it leaves, clamped at the song's end.</summary>
    public void ShowSystemsAround(int measure)
    {
        var system = SystemOf(measure);
        if (system >= 0 && (system < _windowFirst || system >= _windowFirst + _systemCount))
            _windowFirst = Math.Clamp(system, 0, Math.Max(0, PlaySystemCount() - _systemCount));
        ApplySystemWindow();
    }

    /// <summary>Smooth follow: the window's goal is the playing system at the top; each call glides the card's scroll a share of the way there
    /// (the score's Smooth glide), so a call per frame scrolls continuously. Allocates nothing.</summary>
    public void GlideSystemsTo(int measure)
    {
        var system = SystemOf(measure);
        if (system >= 0) _windowFirst = Math.Clamp(system, 0, Math.Max(0, PlaySystemCount() - _systemCount));
        if (SystemWindowFromFirst(Staff, _windowFirst, _systemCount) is not var (top, height)) { ApplySystemWindow(); return; }
        if (_staffScroll.Height != height) _staffScroll.Height = height;
        var y = _staffScroll.Offset.Y;
        var next = Math.Abs(top - y) < GlideArrivalPixels ? top : y + (top - y) * GlideShare;
        if (next != y) _staffScroll.Offset = new Vector(0, next);
    }

    private const double GlideShare = 0.25;          // share of the remaining distance a Smooth frame covers (as the score's follow)
    private const double GlideArrivalPixels = 0.5;   // a glide lands on its goal within this distance

    // The system holding measure, or -1 for an audio track or a song without bars.
    private int SystemOf(int measure)
    {
        var probe = Staff.Probe;
        if (probe.Track is not { IsAudio: false } track || track.Measures.Count == 0) return -1;
        var layout = probe.Layout.GetLayout(track);
        if (layout.SystemCount == 0) return -1;
        return layout.SystemForMeasure(Math.Clamp(measure, 0, track.Measures.Count - 1));
    }

    // How many systems the staff lays out (0 for an audio track or a song without bars).
    private int PlaySystemCount()
    {
        var probe = Staff.Probe;
        if (probe.Track is not { IsAudio: false } track || track.Measures.Count == 0) return 0;
        return probe.Layout.GetLayout(track).SystemCount;
    }

    // Writes the window's height and offset only when they change; an offset the viewer clamps is placed again when its extent changes.
    private void ApplySystemWindow()
    {
        if (SystemWindowFromFirst(Staff, _windowFirst, _systemCount) is not var (top, height))
        {
            if (!double.IsNaN(_staffScroll.Height)) _staffScroll.Height = double.NaN;
            return;
        }
        if (_staffScroll.Height != height) _staffScroll.Height = height;
        if (_staffScroll.Offset.Y != top) _staffScroll.Offset = new Vector(0, top);
    }

    private void ForwardWheel(object? sender, PointerWheelEventArgs e)
    {
        if (this.FindAncestorOfType<ScrollViewer>() is not { } view) return;
        view.Offset = new Vector(view.Offset.X, Math.Max(0, view.Offset.Y - e.Delta.Y * WheelStep));
        e.Handled = true;
    }

    /// <summary>The fretboard or drum/keyboard board the card shows (the track's own instrument, or the view the fretboard menu picked for it).</summary>
    internal void SetInstrumentKind(InstrumentPanelKind kind)
    {
        if (Instrument.Kind == kind) return;
        Instrument.Kind = kind;
        ApplyPlacement();   // the kind sets the pane's height rules (a drum map keeps its own height, a fretboard takes the card's)
    }

    /// <summary>What the card's board draws (the Fretboard settings); applied to every card, so the board's right-click rows act on them.</summary>
    internal void SetInstrumentLook(FretboardSettingsBinding.Look look)
    {
        if (Instrument.Look == look) return;
        Instrument.Look = look;
        ApplyPlacement();
    }

    /// <summary>Takes the window's fretboard menu on the card's board, naming this card's own track in it.</summary>
    internal void AttachInstrumentMenu(FretboardContextMenu menu, Action? findScale) =>
        menu.Attach(Instrument, () => Instrument.Kind, findScale, () => _track);

    /// <summary>The card's fretboard toggle button (a test clicks it); the icon is its content.</summary>
    internal Button FretboardButton => _fretboardToggle;

    /// <summary>Shows or hides the track's fretboard in the card, the toggle's lit state with it.</summary>
    public void SetFretboardShown(bool shown)
    {
        _fretboardShown = shown;
        Instrument.IsVisible = shown;
        ApplyPlacement();
        ApplyLook();
    }

    /// <summary>The fretboard above the staff (true) or beside it (false), as the Band view's layout menu chooses.</summary>
    public void SetFretboardAbove(bool above)
    {
        _fretboardAbove = above;
        ApplyPlacement();
    }

    /// <summary>The track colour frame (true) or the plain card outline (false).</summary>
    public void SetColourFrame(bool on)
    {
        _colourFrame = on;
        ApplyLook();
    }

    private Grid BuildCard()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = TitleHeight };
        header[!Panel.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(CardBackgroundKey);   // fills the header so a right-click anywhere on it reaches the menu
        header.ContextMenu = BandRowsMenu.Build(() => _systemCount, rows => RowsPicked?.Invoke(rows));
        Grid.SetColumn(_tick, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_fretboardToggle, 2);
        header.Children.Add(_tick);
        header.Children.Add(_title);
        header.Children.Add(_fretboardToggle);
        Grid.SetRow(header, 0);
        Grid.SetRow(_body, 1);
        _body.Children.Add(Instrument);
        _body.Children.Add(_grip);
        _body.Children.Add(_staffScroll);
        var card = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        card.Children.Add(header);
        card.Children.Add(_body);
        // The card's resize grip floats over the card's bottom edge (its own row would take room from the staff).
        Grid.SetRowSpan(_cardGrip, 2);
        card.Children.Add(_cardGrip);
        return card;
    }

    /// <summary>Places the fretboard above the staff, or beside it while it is on and beside is chosen; the staff takes the rest.</summary>
    private void ApplyPlacement()
    {
        var beside = _fretboardShown && !_fretboardAbove;
        _body.RowDefinitions = beside ? new RowDefinitions("*") : new RowDefinitions("Auto,Auto");
        _body.ColumnDefinitions = beside ? new ColumnDefinitions("*,2*") : new ColumnDefinitions("*");
        var height = InstrumentHeight;
        Instrument.Height = height;
        // Stretched but capped at the width that keeps the board's proportions: a narrow card squeezes the drawing, a wide one centres it.
        Instrument.MaxWidth = beside ? double.PositiveInfinity : InstrumentResizable ? BandFretboardSize.WidthFor(height, InstrumentShape) : FixedInstrumentWidth;
        Instrument.HorizontalAlignment = HorizontalAlignment.Stretch;
        _grip.IsVisible = _fretboardShown && InstrumentResizable;
        _grip.MaxWidth = Instrument.MaxWidth;
        Grid.SetRow(Instrument, 0);
        Grid.SetColumn(Instrument, 0);
        Grid.SetRow(_grip, 0);
        Grid.SetColumn(_grip, 0);
        Grid.SetRow(_staffScroll, beside ? 0 : 1);
        Grid.SetColumn(_staffScroll, beside ? 1 : 0);
    }

    private void ApplyLook()
    {
        var accent = BandBrush.Of(this, "InstrumentAccent");
        var trackBrush = Color.TryParse(_track?.ColorHex, out var colour) ? new SolidColorBrush(colour) : accent;
        // Every card, the selected one too, is framed in its own track's colour (the plain outline when the colour frame is off).
        BorderBrush = _colourFrame ? trackBrush : BandBrush.Of(this, "InstrumentBoardEdge");
        BorderThickness = new Thickness(_colourFrame ? FrameThickness : PlainThickness);
        _tick.Background = trackBrush;
        _title.Foreground = BandBrush.Of(this, "TextPrimary");
        _fretboardToggle.Background = _fretboardShown ? accent : BandBrush.Of(this, "InstrumentRowAlt");
        // The icon takes the ink that reads on the fill it sits on: the instrument accent is not the UI accent, so OnAccent is not its ink.
        var onAccent = new SolidColorBrush(AvaloniaPalette.ColorOf(TabForge.Platform.Theme.ThemeColours.InkOn(AvaloniaPalette.Get("InstrumentAccent", ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light))));
        BandFretboardIcon.Paint(_fretboardIcon, _fretboardShown ? onAccent : BandBrush.Of(this, "TextPrimary"));
    }
}

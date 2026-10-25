using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Documents;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the header strip shown above the arrangement while compact mode or the Band view is on: a caption with its chevron (one flat button that opens
//   the owner's menu), one capsule per track whose lit state and click the owner gives (compact mode: the selected track; the Band view: each shown card,
//   so several capsules can be lit), and the flat close button that the owner's close runs.
// Does not own: the visibility and lit-capsule rules (CompactModeStripPresenter in Core), the modes themselves (MainWindowCompactMode, MainWindowBandChrome),
//   the track selection or the shown cards (their owners), the capsule (TrackPillButton), the flat button (FlatToolButton), and what the stand-in menu holds (CompactModeMenu);
//   the strip only asks for it on a right-click or a click on the caption. Capsules are rebuilt after each edit ending (SongProject.TimelineMarked) or a replaced song when the track list or a name or colour changed.
// Tests: TabForge.Avalonia.Tests.CompactTrackStripTests.
/// <summary>The compact mode header strip with its track capsules.</summary>
public sealed class CompactTrackStrip : DockPanel
{
    private static readonly Thickness PillRowMargin = new(4, 4, 4, 2);

    private const string CloseTooltip = "Close compact mode";
    private const string MenuTooltip = "Menus and tools hidden by compact mode";

    private const double CaptionFontSize = 12.65;
    private const double CaptionGap = 4;
    private const double MenuButtonHeight = 27;
    private static readonly Thickness MenuButtonPadding = new(8, 0, 8, 0);
    private static readonly Thickness MenuButtonMargin = new(4, 2, 8, 0);
    private const string ChevronData = "M 1,1 L 5,5 L 9,1";
    private const double ChevronWidth = 10;
    private const double ChevronHeight = 6;
    private const double ChevronStroke = 1.4;

    private const double CloseWidth = 26;
    private const double CloseHeight = 24;
    private static readonly Thickness CloseMargin = new(2, 1, 8, 0);
    private const string CrossData = "M 0,0 L 10,10 M 10,0 L 0,10";
    private const double CrossSize = 10;
    private const double CrossStroke = 1.5;

    private readonly WrapPanel _pills = new() { Margin = PillRowMargin };
    private SongDocument? _song;
    // Listens on the DOCUMENT, so an undo that replaces the song keeps the capsules following the current one.
    private readonly DocumentWatch _watch = new();

    public CompactTrackStrip()
    {
        IsVisible = false;
        _watch.Marked = _ => RefreshPills();
        _watch.ProjectChanged = (_, _) => RefreshPills();
        Bind(BackgroundProperty, this.GetResourceObservable("PanelBrush"), BindingPriority.Template);
        Styles.Add(TrackPillButton.IdleLook());
        var menuButton = CaptionMenuButton();
        ToolTip.SetTip(menuButton, MenuTooltip);
        menuButton.Click += (_, _) => MenuRequested?.Invoke(menuButton, PlacementMode.Bottom);
        MenuButton = menuButton;
        SetDock(menuButton, Dock.Left);
        Children.Add(menuButton);
        ContextRequested += (_, e) => { MenuRequested?.Invoke(this, PlacementMode.Pointer); e.Handled = true; };
        var close = CloseButton();
        ToolTip.SetTip(close, CloseTooltip);
        close.Click += (_, _) => CloseRequested?.Invoke();
        SetDock(close, Dock.Right);
        Children.Add(close);
        Children.Add(_pills);
    }

    /// <summary>Run by the close button: leave compact mode. Set by the mode's owner, so only one handler runs.</summary>
    public Action? CloseRequested { get; set; }

    /// <summary>Run by a right-click on the strip (pointer placement) or a click on its caption (under it), with the control to open the menu on. Set by the menu's owner.</summary>
    public Action<Control, PlacementMode>? MenuRequested { get; set; }

    /// <summary>The caption with its chevron, which opens the stand-in menu.</summary>
    public Button MenuButton { get; }

    /// <summary>The toolbar icons the strip holds before its close button while the menu row is hidden; null when it holds none.</summary>
    public Control? Toolbar { get; private set; }

    /// <summary>Docks <paramref name="toolbar"/> just left of the close button; the caller detaches it from its old parent first.</summary>
    public void HoldToolbar(Control toolbar)
    {
        ReleaseToolbar();
        SetDock(toolbar, Dock.Right);
        Children.Insert(Children.IndexOf(_pills), toolbar);
        Toolbar = toolbar;
    }

    /// <summary>Takes the held toolbar out of the strip and returns it (null when none is held), for its owner to put back.</summary>
    public Control? ReleaseToolbar()
    {
        var held = Toolbar;
        if (held is not null) Children.Remove(held);
        Toolbar = null;
        return held;
    }

    /// <summary>The capsules, one per track in track order.</summary>
    public IReadOnlyList<TrackPillButton> Pills => _pills.Children.OfType<TrackPillButton>().ToList();

    /// <summary>Whether the capsule of the track at <paramref name="trackIndex"/> is lit; the owner's selection decides it. Read again by <see cref="SyncLit"/>.</summary>
    public Func<int, bool> IsLit { get; set; } = _ => false;

    /// <summary>Run with the track index when its capsule is clicked; the owner changes its selection, then the strip reads the lit capsules again.</summary>
    public Action<int>? PillClicked { get; set; }

    /// <summary>The hover text of the capsule of a track, given its name.</summary>
    public Func<string, string> PillToolTip { get; set; } = CompactModeStripPresenter.PillTooltip;

    /// <summary>Shows the strip for <paramref name="song"/> (on) or hides it (off), and rebuilds its capsules from the song's tracks; the owner sets
    /// <see cref="IsLit"/> and <see cref="PillClicked"/> before it turns the strip on.</summary>
    public void SetOn(bool on, SongDocument? song)
    {
        if (_song is not null) _song.DisplayStateChanged -= OnSongDisplayStateChanged;
        _watch.Bind(null);
        _song = song;
        if (on && song is not null)
        {
            song.DisplayStateChanged += OnSongDisplayStateChanged;
            _watch.Bind(song);
        }
        IsVisible = CompactModeStripPresenter.StripShown(on, otherHeaderShown: false);
        Rebuild();
    }

    private readonly TextBlock _caption = new() { Text = CompactModeStripPresenter.Caption, FontSize = CaptionFontSize, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Sets the caption's text: the mode entered last, as HeaderModeCaption names it.</summary>
    public void SetCaption(string text) => _caption.Text = text;

    private Button CaptionMenuButton()
    {
        var caption = _caption;
        caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        var chevron = new Path
        {
            Data = Geometry.Parse(ChevronData), Width = ChevronWidth, Height = ChevronHeight, Stretch = Stretch.None, StrokeThickness = ChevronStroke,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        chevron[!Shape.StrokeProperty] = new DynamicResourceExtension("MutedBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = CaptionGap, Children = { caption, chevron } };
        var button = FlatToolButton.Create(content);
        button.Height = MenuButtonHeight;
        button.Padding = MenuButtonPadding;
        button.Margin = MenuButtonMargin;
        return button;
    }

    private static Button CloseButton()
    {
        var cross = new Path
        {
            Data = Geometry.Parse(CrossData), Width = CrossSize, Height = CrossSize, Stretch = Stretch.None, StrokeThickness = CrossStroke,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        cross[!Shape.StrokeProperty] = new DynamicResourceExtension("TextBrush");
        var button = FlatToolButton.Create(cross);
        button.Width = CloseWidth;
        button.Height = CloseHeight;
        button.Margin = CloseMargin;
        button.Padding = new Thickness(0);
        return button;
    }

    private void OnSongDisplayStateChanged(object? sender, EventArgs e)
    {
        RefreshPills();
    }

    private void RefreshPills()
    {
        if (_song is { } song && (Pills.Count != song.Project.Tracks.Count || Pills.Where((pill, i) => !pill.Shows(song.Project.Tracks[i].Name, song.Project.Tracks[i].ColorHex)).Any())) Rebuild();
        else SyncLit();
    }

    private void Rebuild()
    {
        _pills.Children.Clear();
        if (!IsVisible || _song is not { } song) return;
        for (var i = 0; i < song.Project.Tracks.Count; i++)
        {
            var index = i;
            var track = song.Project.Tracks[i];
            var pill = new TrackPillButton(track.Name, track.ColorHex, PillToolTip(track.Name));
            pill.Click += (_, _) => { PillClicked?.Invoke(index); SyncLit(); };
            _pills.Children.Add(pill);
        }
        SyncLit();
    }

    /// <summary>Re-reads which capsules are lit from <see cref="IsLit"/>: the owner calls it after its selection changed.</summary>
    public void SyncLit()
    {
        var pills = Pills;
        for (var i = 0; i < pills.Count; i++) pills[i].IsChecked = IsLit(i);
    }
}

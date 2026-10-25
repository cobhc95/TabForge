using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The instrument pane as the WPF pane shows it: the panel (fretboard, keyboard or drum key map) of the track with the score cursor's notes lit,
/// the Scales button under the colour legend, and a small close (X) button at the top-right corner. No title tab and no kind buttons: the kind
/// follows the track.
/// Owns: the pane's layout (the panel and the two overlay buttons), the cursor-to-panel wiring and the kind a track shows. Does not own: the panel's
/// drawing and hit tests (<see cref="InstrumentPanelView"/>), the cursor (<see cref="ScorePreviewView"/>), the notes lit for a cell
/// (<see cref="InstrumentLitNotes"/>), or what closing does (the window passes its close action in).
/// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests, MainWindowMenuTests.
/// </summary>
public sealed class InstrumentPanelStrip : Grid
{
    /// <summary>The string count of a track with no tuning list: a six-string guitar.</summary>
    private const int StandardStrings = 6;
    /// <summary>The Scales button's top when the panel has no legend to sit under: a few DIPs below the pane's top edge.</summary>
    private const double CornerScalesTop = 4;

    private readonly ScorePreviewView _score;
    private readonly Button _scales = ScalesButton();
    private readonly Canvas _scalesLayer = new();
    private readonly Func<AvaloniaPlaybackSession?> _activePlayback;
    private readonly FretboardContextMenu? _contextMenu;
    private AvaloniaPlaybackSession? _playback;
    private TrackModel? _track;

    // What ShowPlaybackNotes last showed: a bar-change frame raises PlayheadBarChanged and then PlayheadFrame with the same clock, so the second
    // call would repeat the whole fill for an identical result. Remembering the inputs skips only that exact repeat; Refresh (a cursor/track change),
    // RebindPlayback and detach clear the key so an edit at the same playhead time still repaints. A paused seek moves only the bar and cell
    // (the transport keeps the old song time while paused), so the key holds them too and the paused time is read from the timeline.
    private bool _notesShown;
    private int _notesTrack = -1;
    private double _notesMs = double.NaN;
    private int _notesBar = -1;
    private int _notesCell;
    private bool _notesPaused;

    /// <param name="close">What the X button does (the window hides the pane); null leaves the button doing nothing.</param>
    /// <param name="activePlayback">The playback session of the shown tab; the strip follows it while attached and after <see cref="RebindPlayback"/>. Null: no playback.</param>
    /// <param name="findScale">What the Scales button does (the window opens the scale finder); null leaves the button doing nothing.</param>
    public InstrumentPanelStrip(ScorePreviewView score, Action? close = null, Func<AvaloniaPlaybackSession?>? activePlayback = null, FretboardContextMenu? contextMenu = null, Action? findScale = null)
    {
        _score = score;
        _activePlayback = activePlayback ?? (() => null);
        Panel = new InstrumentPanelView { VerticalAlignment = VerticalAlignment.Top };
        _scales.Click += (_, _) => findScale?.Invoke();
        _contextMenu = contextMenu;
        contextMenu?.Attach(Panel, () => Panel.Kind, () => _scales.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
        _scalesLayer.Children.Add(_scales);
        Children.Add(Panel);
        Children.Add(_scalesLayer);
        Children.Add(CloseButton(close));
        score.CursorChanged += Refresh;
        score.ZoomChanged += ApplyScoreTextScale;
        Panel.NotePicked += OnNotePicked;
        Panel.PlacementCommitted += place => _contextMenu?.SetFretboardPosition(place);
        ApplyScoreTextScale();
        Refresh();
    }

    /// <summary>The pane's text scale follows the score's zoom and spacing, as the WPF pane's does (the score sets it on every zoom).</summary>
    private void ApplyScoreTextScale() => Panel.ContentScale = ScoreInstrumentScale.ContentScaleFor(_score.Zoom, _score.Probe.Appearance.ScoreSpacing);

    /// <summary>A fretboard click writes or removes that note at the cursor's cell; a drum-pad click writes its sound on the drum track's line; a
    /// key click edits nothing. Through the score's own edit path, as a typed note is: the score repaints and reports the cursor change.</summary>
    private void OnNotePicked(InstrumentNotePick pick)
    {
        bool written;
        if (pick.StringIndex is { } stringIndex && pick.Fret is { } fret)
            written = _score.Edit(editor => FretboardNoteEntry.Toggle(editor, stringIndex, fret));
        else if (_track is { } drumTrack && KindOf(drumTrack) == InstrumentPanelKind.Drums)
            written = _score.Edit(editor => FretboardNoteEntry.ToggleDrumSound(editor, drumTrack, pick.Midi));
        else
            return;
        if (written) _score.Focus();
    }

    /// <summary>Follows the playback session of the shown tab (called when the tab changes).</summary>
    public void RebindPlayback()
    {
        UnsubscribePlayback();
        _playback = _activePlayback();
        if (_playback is not null)
        {
            _playback.PlayheadFrame += ShowPlaybackNotes;
            _playback.PlayheadBarChanged += ShowPlaybackNotes;
            _playback.StateChanged += ShowPlaybackNotes;
        }
        ClearPlaybackNotesKey();
        ShowPlaybackNotes();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RebindPlayback();
        if (_contextMenu is not null) _contextMenu.TrackViewChanged += Refresh;
    }

    // A detached strip follows no session. It must not ask the window for its shown song: a window whose last song just moved away has none.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_contextMenu is not null) _contextMenu.TrackViewChanged -= Refresh;
        UnsubscribePlayback();
        _playback = null;
        ClearPlaybackNotesKey();
        ShowPlaybackNotes();
    }

    private void UnsubscribePlayback()
    {
        if (_playback is null) return;
        _playback.PlayheadFrame -= ShowPlaybackNotes;
        _playback.PlayheadBarChanged -= ShowPlaybackNotes;
        _playback.StateChanged -= ShowPlaybackNotes;
    }

    /// <summary>The playing notes of the shown track at the playhead, or none when the song is not playing.</summary>
    private void ShowPlaybackNotes()
    {
        if (!Panel.Look.FollowFretboard)
        {
            ClearPlaybackNotesKey();
            Panel.ShowPlayback(null, 0);
            return;
        }
        var project = _score.Editor.Project;
        var index = _track is null || project is null ? -1 : project.Tracks.IndexOf(_track);
        if (_playback is not { IsPlaying: true } session || index < 0 || session.Transport.Timeline is not { } timeline)
        {
            ClearPlaybackNotesKey();
            Panel.ShowPlayback(null, 0);
            return;
        }
        var transport = session.Transport;
        var bar = transport.PlayheadBar;
        var cell = transport.PlayheadCell;
        var nowMs = transport.PlayheadMs;
        if (session.IsPaused && bar >= 0 && bar < timeline.Bars.Count)
        {
            var scoreBar = timeline.Bars[bar];
            var slots = Math.Max(1, scoreBar.Slots);
            nowMs = scoreBar.MsAtFraction(Math.Clamp((double)cell / slots, 0, 1));
        }
        if (_notesShown && _notesTrack == index && _notesMs == nowMs && _notesBar == bar && _notesCell == cell && _notesPaused == session.IsPaused) return;
        _notesShown = true;
        _notesTrack = index;
        _notesMs = nowMs;
        _notesBar = bar;
        _notesCell = cell;
        _notesPaused = session.IsPaused;
        Panel.ShowPlayback(timeline.NotesFor(index), nowMs);
    }

    /// <summary>Forgets the last shown playhead state, so the next ShowPlaybackNotes fills even at the same clock (edits, a rebound session, detach).</summary>
    private void ClearPlaybackNotesKey()
    {
        _notesShown = false;
        _notesTrack = -1;
        _notesMs = double.NaN;
        _notesBar = -1;
        _notesCell = 0;
    }

    public InstrumentPanelView Panel { get; }

    /// <summary>The kind a track shows under "Match the instrument": the one Core rule (<see cref="InstrumentViewRules"/>).</summary>
    internal static InstrumentPanelKind KindOf(TrackModel track) => InstrumentViewRules.For(track) switch
    {
        InstrumentViewKind.DrumMap => InstrumentPanelKind.Drums,
        InstrumentViewKind.Keyboard => InstrumentPanelKind.Keyboard,
        _ => InstrumentPanelKind.Fretboard,
    };

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        PlaceScalesButton();
        return size;
    }

    /// <summary>Whether the dock locks the instrument row to its content height when the window starts: only the "Lock fretboard size"
    /// setting does, as in the WPF dock. Unlocked (the default), the splitter resizes the row and the drawing scales with it.</summary>
    public static bool LocksRowHeight(AppearanceSettings? appearance) => appearance?.LockInstrumentSize ?? false;

    /// <summary>The pane's height limits by kind. A fretboard stops growing where its board stops growing (the gap cap, or the width's own limit when
    /// lower) and never shrinks below the smallest gap. A keyboard stops at its natural key height for the pane's width, plus the keys' insets, and
    /// never shrinks below the shortest keys. A drum map has no limit. The caps follow the pane's width, so a width change re-applies them.</summary>
    private void ApplyPaneHeightLimits(TrackModel? track)
    {
        var strings = track is { StringTunings.Count: > 0 } ? track.StringTunings.Count : StandardStrings;
        var padding = FretboardGeometryRules.TopPad + FretboardGeometryRules.BottomPad;
        (MinHeight, MaxHeight) = Panel.Kind switch
        {
            InstrumentPanelKind.Fretboard => (FretboardSizeRules.MinPaneHeight(strings, padding), FretboardMaxHeight(strings, padding)),
            InstrumentPanelKind.Keyboard => (KeyboardPaneSizeRules.MinPaneHeight, KeyboardMaxHeight()),
            _ => (0.0, double.PositiveInfinity),
        };
        // A fretboard or keyboard panel fills the pane so its drawing scales with the pane's height; a drum map keeps its own height.
        Panel.VerticalAlignment = Panel.Kind == InstrumentPanelKind.Drums ? VerticalAlignment.Top : VerticalAlignment.Stretch;
    }

    /// <summary>The keyboard's pane height cap at this width. Before the strip has a width (not yet arranged) the tallest keys apply.</summary>
    private double KeyboardMaxHeight() =>
        KeyboardPaneSizeRules.MaxPaneHeight(Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity, Panel.WhiteKeyCount);

    /// <summary>The fretboard's pane height cap: the gap cap's height, or the height this width alone allows when that is lower. Before the
    /// strip has a width (not yet arranged) only the gap cap applies.</summary>
    private double FretboardMaxHeight(int strings, double padding)
    {
        var gapCapHeight = FretboardSizeRules.GapCapPaneHeight(strings, padding, Bounds.Width, InstrumentPanelView.BoardLimits);
        return Bounds.Width > 0 ? Math.Min(gapCapHeight, Panel.WidthLimitedPaneHeight(Bounds.Width)) : gapCapHeight;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) ApplyPaneHeightLimits(_track);
    }

    /// <summary>Re-reads the selected track and the cursor into the panel; FretboardSettingsBinding calls it when the look changes. The selected track is
    /// the one the score shows and a fretboard click writes to (<see cref="ScorePreviewEditor.Track"/>), so hover, lit notes and clicks agree.
    /// An audio track, or no track, shows the placeholder.</summary>
    internal void Refresh()
    {
        if (_score.Editor.Project is not { Tracks.Count: > 0 }) return;
        var selected = _score.Editor.Track;
        _track = selected is { IsAudio: false } ? selected : null;
        Panel.AudioTrack = selected is { IsAudio: true };
        var cursor = _score.Editor.Cursor;
        var view = _contextMenu is { } menu ? menu.ViewFor(_track, Panel.Look.InstrumentView) : Panel.Look.InstrumentView;
        Panel.Kind = _track is null ? InstrumentPanelKind.Fretboard : FretboardSettingsBinding.KindFor(_track, view);
        Panel.Show(_track, _track is not null && cursor is { } c ? InstrumentLitNotes.At(_track, c.Measure, c.Cell) : System.Array.Empty<InstrumentLitNotes.Lit>());
        ApplyPaneHeightLimits(_track);
        ClearPlaybackNotesKey();
        ShowPlaybackNotes();
        _scales.IsVisible = _track is not null;
        PlaceScalesButton();
    }

    /// <summary>Under the fretboard's colour legend when the panel has one; otherwise at the top-right, left of the close X (a keyboard or drum panel).</summary>
    private void PlaceScalesButton()
    {
        if (Panel.ScalesAnchor() is { } anchor)
        {
            Canvas.SetRight(_scales, double.NaN);
            Canvas.SetLeft(_scales, anchor.X);
            Canvas.SetTop(_scales, anchor.Y);
            return;
        }
        Canvas.SetLeft(_scales, double.NaN);
        Canvas.SetRight(_scales, InstrumentPanelView.CloseButtonReserve);
        Canvas.SetTop(_scales, CornerScalesTop);
    }

    private static Button ScalesButton()
    {
        var icon = new Path
        {
            Data = Geometry.Parse(global::TabForge.Platform.Theme.IconGeometry.ScaleSteps), StrokeThickness = 1.6, StrokeJoin = PenLineJoin.Round,
            Width = 13, Height = 13, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        icon[!Shape.StrokeProperty] = new DynamicResourceExtension("IconActiveBrush");
        var label = new TextBlock { Text = "Scales", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush");
        var button = new Button
        {
            Name = "ScalesButton", Height = 24, Padding = new Thickness(6, 0, 8, 0), BorderThickness = new Thickness(1), Focusable = false,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
        };
        button[!BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        button[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        // The hover shade replaces the panel fill under the pointer; leaving restores the theme's Panel2 fill.
        TabForge.Avalonia.Platform.HoverShade.Attach(button, hovered =>
        {
            if (hovered) button.Background = TabForge.Avalonia.Platform.HoverShade.Brush(button);
            else button[!BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        });
        return button;
    }

    private static Button CloseButton(Action? close)
    {
        var glyph = new Path { Data = Geometry.Parse("M0,0 L9,9 M9,0 L0,9"), Width = 9, Height = 9, StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round };
        glyph[!Shape.StrokeProperty] = new DynamicResourceExtension("IconDefaultBrush");
        var button = new Button
        {
            Name = "InstrumentHideButton", Content = glyph, Width = 20, Height = 20, MinWidth = 20, MinHeight = 20, Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 2, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = AvaloniaPalette.TransparentBrush, BorderThickness = new Thickness(0), Focusable = false,
        };
        HotkeyTip.Set(button, "View.InstrumentPanel", "Hide fretboard / keyboard");
        button.Click += (_, _) => close?.Invoke();
        return button;
    }
}

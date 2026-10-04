using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

using static TabForge.Views.TrackControlWidgets;
using static TabForge.Views.TrackColumnLayout;

namespace TabForge.Views;

/// <summary>
/// Bottom arrangement overview: one precise grid cell per measure per track,
/// a section strip above, compact per-track controls on the left and a playhead
/// that aligns exactly with musical time. Every cell is drawn from real score data.
/// </summary>
public sealed partial class ArrangementPanel : Grid
{
    // The ruler is taller than the minimum so the controls side has room for the column-label strip.
    public const double RulerHeight = 40;
    public const double ColumnHeaderHeight = 18;
    public const double SectionHeight = 24;
    /// <summary>A track row's height when nothing was stretched (the minimum).</summary>
    public const double DefaultTrackRowHeight = 30;
    /// <summary>The tallest a stretched track row gets (3x the default).</summary>
    public const double MaxTrackRowHeight = DefaultTrackRowHeight * 3;
    // The stretched row height belongs to the panel showing a song (keyed by the song it is bound to, so another window's
    // panel is never touched); the static geometry helpers read it through the project they are given.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SongProject, StrongBox<double>> RowHeights = new();
    private static readonly List<WeakReference<ArrangementPanel>> AllPanels = new();

    /// <summary>The track row height of the panel showing <paramref name="project"/> (the default until it is stretched).</summary>
    public static double RowHeightFor(SongProject? project) =>
        project is not null && RowHeights.TryGetValue(project, out var box) ? box.Value : DefaultTrackRowHeight;

    /// <summary>This panel's track row height: the mixer rows on the left and the timeline lanes share it.</summary>
    public double TrackRowHeight => RowHeightFor(_project);
    /// <summary>Height of a track's audio lane (under its row when it has audio or is armed).</summary>
    public const double AudioLaneHeight = 54;
    /// <summary>Height of an audio row's controls (they share lane 0 with its armed strip).</summary>
    public const double AudioControlsHeight = DefaultTrackRowHeight;

    public const double ControlsWidth = 580;

    private readonly TrackTimeline _timeline = new();
    private readonly TrackColumnLayout _columns;
    private readonly TuningButtonController _tuning;
    private readonly AddLaneController _addLane;
    private readonly GroupDragController _groupDrag;
    private readonly TrackRowWidgets _rowWidgets;
    private InputGate<double> _rowScrollInputs;
    private SettleAction? _waveSettle;   // zoom steps draw cached waveforms scaled; one rebuild after the zoom settles
    private SettleAction? _extentSettle;   // pane height changes (splitter drags) re-fit the Add-track lane once, after the drag settles
    internal void FlushExtent() { _extentSettle?.Cancel(); RefreshTimelineExtent(); }
    private ResizeShade? _resizePreview;
    private readonly List<UIElement> _hiddenForResize = new();

    /// <summary>
    /// While the dock splitter above the panel is dragged, the real rows are collapsed and a light shade is drawn instead: one
    /// translucent band per track in its colour, scaled with the live height (like the section-drag shadow). A drag step costs
    /// one tiny redraw, no layout; the real rows come back once, at the final size, when the drag ends.
    /// </summary>
    internal void BeginResizePreview()
    {
        if (_resizePreview is not null || _project is null || ActualHeight < 1) return;
        var header = RulerHeight + SectionHeight;
        var listWidth = ColumnDefinitions.Count > 0 ? ColumnDefinitions[0].ActualWidth : ControlsWidth;
        var scroll = _horizontal.HorizontalOffset;
        var viewWidth = Math.Max(0, ActualWidth - listWidth);
        // Built once from the song (not from the controls): per track a band with its name, and in the timeline one cell per
        // visible bar, filled where the bar has notes; drawn later with a vertical scale only.
        var drawing = new System.Windows.Media.DrawingGroup();
        var names = new List<(System.Windows.Media.FormattedText Text, double Centre)>();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Draw.Solid(System.Windows.Media.Color.FromRgb(0x22, 0x26, 0x2D), 1), null, new Rect(0, 0, ActualWidth, header));
            var top = header;
            var typeface = new System.Windows.Media.Typeface("Segoe UI");
            for (var t = 0; t < _project.Tracks.Count; t++)
            {
                var track = _project.Tracks[t];
                var h = RowHeightOf(_project, track);
                var colour = Draw.Tame(ParseColour(track.ColorHex));
                dc.DrawRectangle(Draw.Solid(colour, 0.16), null, new Rect(0, top, ActualWidth, h));
                names.Add((new System.Windows.Media.FormattedText(track.Name ?? "", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, 12, Draw.Solid(System.Windows.Media.Colors.White, 0.55), 1.0), top + h / 2));
                for (var bar = 0; bar < track.Measures.Count; bar++)
                {
                    var x = listWidth + _timeline.XOfBar(bar) - scroll; var x2 = listWidth + _timeline.XOfBar(bar + 1) - scroll;
                    if (x2 < listWidth) continue; if (x > ActualWidth) break;
                    var filled = track.Measures[bar].Cells.Any(c => c.Notes.Count > 0);
                    dc.DrawRoundedRectangle(Draw.Solid(colour, filled ? 0.55 : 0.12), null, new Rect(Math.Max(listWidth, x + 1), top + 2, Math.Max(0, x2 - x - 2), Math.Max(1, h - 4)), 3, 3);
                }
                top += h;
            }
        }
        drawing.Freeze();
        foreach (UIElement child in Children) { if (child.Visibility == Visibility.Visible) { child.Visibility = Visibility.Collapsed; _hiddenForResize.Add(child); } }
        _resizePreview = new ResizeShade(drawing, names, ActualHeight, header) { IsHitTestVisible = false };
        SetColumnSpan(_resizePreview, Math.Max(1, ColumnDefinitions.Count)); SetRowSpan(_resizePreview, Math.Max(1, RowDefinitions.Count));
        Children.Add(_resizePreview);
    }

    /// <summary>Ends the resize preview: the real rows come back (laid out once at the final size).</summary>
    internal void EndResizePreview()
    {
        if (_resizePreview is null) return;
        Children.Remove(_resizePreview);
        _resizePreview = null;
        foreach (var child in _hiddenForResize) child.Visibility = Visibility.Visible;
        _hiddenForResize.Clear();
    }
    private void RequestExtentSettle() => (_extentSettle ??= new SettleAction(RefreshTimelineExtent, 120)).Request();

    /// <summary>The shared view options (track tint) the rows and the timeline read; the main window hands in the application's.</summary>
    public TabForge.Visualization.VisualOptions ViewOptions { get => _timeline.ViewOptions; set => _timeline.ViewOptions = value; }
    private readonly SectionDragOverlay _sectionDragOverlay = new();
    private readonly MediaDropGhost _dropGhost = new();
    private readonly SectionInsertionIndicator _sectionInsertionIndicator = new();
    private bool _sectionDragAutoScroll;
    private double _sectionDragPointerViewportX;
    private readonly Canvas _timelineHost = new();
    private readonly System.Windows.Shapes.Path _sectionHighlight = new()
    {
        Fill = null,
        StrokeThickness = 10,
        StrokeStartLineCap = PenLineCap.Square,
        StrokeEndLineCap = PenLineCap.Square,
        StrokeLineJoin = PenLineJoin.Miter,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly System.Windows.Shapes.Rectangle _dragLaneOutline = new()
    {
        Fill = Draw.Solid(Color.FromArgb(10, 0xF2, 0xC1, 0x4E)),
        Stroke = Draw.Solid(Color.FromRgb(0xF2, 0xC1, 0x4E)),
        StrokeThickness = 2,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    // Move-area mode: the picked area pulses (a GPU-composited opacity animation, no redraws).
    private readonly System.Windows.Shapes.Rectangle _areaMoveOutline = new()
    {
        Fill = Draw.Solid(Color.FromArgb(60, 0x4C, 0xB8, 0xFF)),
        Stroke = Draw.Solid(Color.FromRgb(0x4C, 0xB8, 0xFF)),
        StrokeThickness = 2, RadiusX = 3, RadiusY = 3,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly System.Windows.Shapes.Rectangle _playheadLine = new()
    {
        Width = 3,
        Fill = Brushes.White,
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 4,
            ShadowDepth = 0,
            Opacity = 0.72
        },
        IsHitTestVisible = false
    };
    // Hover shade: one reusable rectangle, moved only when the hovered cell changes (never hit-testable).
    private readonly System.Windows.Shapes.Rectangle _hoverCell = new()
    {
        RadiusX = 2, RadiusY = 2,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private static readonly Brush HoverShadeDark = Draw.Solid(Colors.White, 0.12);
    private static readonly Brush HoverShadeLight = Draw.Solid(Colors.Black, 0.08);
    // Bar marker (playback position marker style): one cached element, moved only when the bar, track or layout changes.
    private readonly System.Windows.Shapes.Rectangle _barMarker = new()
    {
        Fill = Draw.Solid(Color.FromRgb(0x14, 0x17, 0x1D)),
        Stroke = Draw.Solid(Colors.White, 0.8),
        StrokeThickness = 1.25,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly System.Windows.Shapes.Path _selectedTrackPlayMarker = new()
    {
        Fill = Brushes.White,
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 4,
            ShadowDepth = 0,
            Opacity = 0.72
        },
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Data = CreatePlayMarkerGeometry()
    };
    private readonly StackPanel _controls = new();
    private readonly ContentControl _playbarHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Border _controlsHeader;
    private readonly ScrollViewer _controlsScroll = new();
    private readonly ScrollViewer _horizontal = new();
    /// <summary>The interactive timeline, excluding its transport and track controls.</summary>
    public FrameworkElement TimelineSurface => _timeline;
    internal int TimelineRenderCount => _timeline.RenderCount;
    internal double PlayheadLineLeft => Canvas.GetLeft(_playheadLine);
    internal double TimelineXOfBar(int bar, double fraction) => _timeline.XOfBar(bar) + _timeline.BarWidthOf(bar) * fraction;
    private readonly ScrollBar _timelineScrollBar = new() { Orientation = Orientation.Horizontal, Height = 16, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private bool _syncingHorizontalScroll;
    private int _zoomGeneration;
    private bool _playheadFollowQueued;
    private bool _statusPlaybackActive;

    private SongProject? _project;
    private IReadOnlyList<MidiOutputDeviceInfo> _devices = Array.Empty<MidiOutputDeviceInfo>();

    public event EventHandler<int>? BarSelected;
    public event EventHandler<int>? TrackSelected;
    public event EventHandler<(int start, int end)>? RangeSelected;
    /// <summary>A plain click on the timeline (not a drag): the shared selection is cleared, as on the score.</summary>
    public event EventHandler? SelectionClearRequested;
    /// <summary>A track's mute or solo flag changed; applied live, without a score or timeline rebuild.</summary>
    public event EventHandler? MuteSoloChanged;
    public event EventHandler? ProjectEdited;
    /// <summary>Track colour changed; this is visual-only and does not require an audio rebuild.</summary>
    public event EventHandler? TrackColorChanged;
    public event Action<TrackEditRequest>? TrackEditRequested;
    /// <summary>Volume/pan changed live (a cheap CC update, not a structural edit that needs a rebuild).</summary>
    public event EventHandler? MixChanged;
    public event EventHandler? MixEditStarting;
    public event EventHandler? MixEditEnded;
    public event EventHandler<int>? TrackOptionsRequested;
    /// <summary>The record-arm button of a track row.</summary>
    public event EventHandler<int>? ArmRequested;
    /// <summary>The audio input of a track changed (track index, input).</summary>
    public event Action<int, string>? AudioInputChosen;
    /// <summary>The live-monitoring button of an armed track: track, new state.</summary>
    public event Action<int, bool>? MonitorToggled;

    /// <summary>The snap button was toggled (the settings object was changed: save it).</summary>
    public event EventHandler? SnapChanged;
    /// <summary>Right-click on the snap button: open the snap settings.</summary>
    public event EventHandler? SnapSettingsRequested;
    private Button? _snapButton;

    /// <summary>Snapping of clips: the shared settings and the playhead (a snap target).</summary>
    public void SetSnap(SnapSettings snap, Func<double> playheadSec)
    {
        _timeline.Snap = snap;
        _timeline.PlayheadSec = playheadSec;
        UpdateSnapButton();
    }

    public void UpdateSnapButton()
    {
        if (_snapButton is null) return;
        var on = _timeline.Snap?.Enabled == true;
        _snapButton.SetResourceReference(Control.BackgroundProperty, on ? "AccentSoftBrush" : "Panel2Brush");
        _snapButton.SetResourceReference(Control.BorderBrushProperty, on ? "AccentBrush" : "BorderBrush");
        TooltipShortcuts.Bind(_snapButton, $"Snapping {(on ? "on" : "off")}: clips snap to the grid, other clips and the playhead. Hold Alt while dragging to bypass. Right-click for the snap settings.", "Timeline.Snap");
    }
    /// <summary>Right-click on + Track: the host shows its track options menu there.</summary>
    public event Action<FrameworkElement>? AddTrackMenuRequested;
    /// <summary>A group's collapse arrow in the track list (group name).</summary>
    public event Action<string>? GroupCollapseToggled;
    /// <summary>A whole group was dragged: first track, track count, insert before this track index (in the old order).</summary>
    public event Action<int, int, int>? GroupMoved;
    /// <summary>Right-click on a group row: open the Mixer at that group.</summary>
    public event Action<string>? GroupMixerRequested;
    /// <summary>"Show tracks in groups" in an empty-area menu (on / off).</summary>
    public event Action<bool>? GroupsToggleRequested;
    /// <summary>"Colours > Colour tracks by group" and "Colours > Colour tracks..." in an empty-area menu.</summary>
    public event Action? ColourByGroupRequested;
    public event Action? ColourTracksRequested;
    /// <summary>"Track list settings..." in an empty-area menu (Preferences > Timeline &amp; sections).</summary>
    public event Action? TrackListSettingsRequested;
    /// <summary>Whether the track list shows groups (the checked state of the menu item).</summary>
    public Func<bool>? GroupsShownState { get; set; }
    /// <summary>The dock pane's own items (reset / close panel), appended below the track list's items.</summary>
    public Func<IEnumerable<Control>>? DockMenuItems
    {
        get => _dockMenuItems;
        set { _dockMenuItems = value; RefreshEmptyAreaMenus(); }
    }
    private Func<IEnumerable<Control>>? _dockMenuItems;

    /// <summary>(Re)builds the empty-area menus of the header strip, the column header row and the empty rows area.</summary>
    internal void RefreshEmptyAreaMenus()
    {
        _controlsHeader.ContextMenu = BuildEmptyAreaMenu();
        if (_columns.Header is { } columnHeader) columnHeader.ContextMenu = BuildEmptyAreaMenu(new Control[] { ResetColumnsItem() });
        _controlsScroll.ContextMenu = BuildEmptyAreaMenu(new Control[] { AutoFitItem() });
    }

    /// <summary>Self-test hook: every empty-area menu (header strip, column header row, empty rows area) starts with "Show tracks in groups".</summary>
    internal bool EmptyAreaMenusHaveGroupsItem() =>
        new[] { _controlsHeader.ContextMenu, _columns.Header?.ContextMenu, _controlsScroll.ContextMenu }
            .All(m => m is { Items.Count: > 0 } && m.Items[0] is MenuItem { Header: "Show tracks in groups" });

    private MenuItem ResetColumnsItem()
    {
        var reset = new MenuItem { Header = "Reset column layout", Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
        reset.Click += (_, _) => ResetColumnLayout();
        return reset;
    }

    private MenuItem AutoFitItem()
    {
        var fit = new MenuItem
        {
            Header = "Auto-resize track list to fit", IsCheckable = true, IsChecked = AutoFitState?.Invoke() ?? true,
            Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
            ToolTip = "Grow or shrink this panel to fit every track and group row when tracks or groups change"
        };
        fit.Click += (_, _) => AutoFitToggleRequested?.Invoke();
        return fit;
    }
    /// <summary>A take was clicked on the timeline: track, lane, is MIDI, Ctrl held (the host makes its lane the playing one).</summary>
    public event Action<int, int, bool, bool>? ClipLaneSelected;
    public event EventHandler? ClipEditStarting;
    public event EventHandler<AudioClip>? ClipEdited;
    /// <summary>Esc while dragging a clip: cancel the drag. False when none is being dragged.</summary>
    public bool CancelClipDrag() => _timeline.ClipGestures.Cancel();
    public event Action<int, AudioClip?, double>? ClipContextRequested;
    public event Action<int, AudioClip>? ClipPropertiesRequested;
    /// <summary>Audio / MIDI files dropped on the timeline: where they land (see <see cref="MediaDrop.Plan"/>).</summary>
    public event Action<MediaDropPlan>? MediaDropped;
    public event Action<AudioClip, int, MediaDropPlan, bool>? ClipMoveRequested;
    public event Action<AudioClip, int, int>? MidiClipToNotation;
    public event Action<int, int, double>? LaneClicked;

    /// <summary>Takes being recorded (drawn live on the timeline overlay; call <see cref="RefreshLiveTakes"/>).</summary>
    internal List<LiveTake> LiveTakes => _timeline.LiveTakes;

    /// <summary>Repaints only the overlay layer (live takes), not the lanes.</summary>
    public void RefreshLiveTakes() => _timeline.RefreshOverlay();

    /// <summary>The timeline holds the focus (keyboard focus, or the window's logical focus while another window is in front).</summary>
    /// <summary>Gives the timeline the focus, so the keys for its selected bars (Delete, Ctrl+Delete...) act on them.</summary>
    public void FocusTimeline() { if (!_timeline.Focus()) FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_timeline), _timeline); }   // window in the background: its focus on return
    public bool TimelineHasFocus => _timeline.IsKeyboardFocused || (Window.GetWindow(_timeline) is { } w && FocusManager.GetFocusedElement(w) == _timeline);

    /// <summary>Song time of bars, for placing audio clips (from the song clock).</summary>
    public void SetSongTime(Func<int, double> barStartSec, Func<double, (int Bar, double Fraction)> barOfSec)
    {
        _timeline.BarStartSec = barStartSec;
        _timeline.BarOfSec = barOfSec;
    }

    public AudioClip? SelectedClip { get => _timeline.SelectedClip; set { _timeline.SelectedClip = value; _timeline.InvalidateVisual(); } }
    /// <summary>The FX part of a track's FX button: open that track's chain.</summary>
    public event EventHandler<int>? FxChainRequested;

    /// <summary>Plug-ins the engine switched off after a crash (settings quarantine list); tracks using one show the faulted FX icon.</summary>
    public Func<ICollection<string>>? QuarantinedPlugins { get; set; }

    /// <summary>True when any plug-in of the chain is in the quarantine list (it crashed and was switched off).</summary>
    internal static bool IsChainFaulted(RigPreset rig, ICollection<string>? quarantined) =>
        quarantined is { Count: > 0 } && rig.Plugins.Any(p => !string.IsNullOrEmpty(p.Path) &&
            quarantined.Contains(p.Path, StringComparer.OrdinalIgnoreCase));
    /// <summary>The power part of a track's FX button: switch chain / Windows MIDI.</summary>
    public event EventHandler<int>? FxPowerRequested;
    /// <summary>The FX part of a group header's FX button: open that group's bus chain (group name).</summary>
    public event EventHandler<string>? BusFxRequested;
    /// <summary>The power part of a group header's FX button: bypass / enable that group's bus chain (group name).</summary>
    public event EventHandler<string>? BusPowerRequested;
    /// <summary>The Mixer button in the controls header.</summary>
    public event EventHandler? MixerRequested;
    /// <summary>A track was dragged to a new position (from, to).</summary>
    public event EventHandler<(int from, int to)>? TrackReordered;
    /// <summary>A track drag has started moving (the model is untouched until the drop).</summary>
    public event EventHandler? TrackDragStarted;
    public event EventHandler<(int from, int insertBefore)>? SectionReordered;
    public event EventHandler? SectionDragStarted;
    public event EventHandler? SectionDragCancelled;
    /// <summary>A section block was right-clicked; its marker index is sorted by measure.</summary>
    public event EventHandler<int>? SectionContextRequested;
    public event EventHandler? SectionResizeStarting;
    public event EventHandler? SectionResized;
    public event EventHandler<(int markerIndex, int bar)>? SectionMarkerMoved;
    public event EventHandler<(int markerIndex, int bar)>? SectionLaneContextRequested;
    /// <summary>A timeline bar was right-clicked (bar index, track index; track may be -1).</summary>
    public event EventHandler<(int bar, int track)>? TimelineContextRequested;
    /// <summary>Shift+F10 / the Menu key on the focused timeline: the host opens the bar or selection menu at the current bar.</summary>
    public event EventHandler? TimelineKeyboardContextRequested;

    /// <summary>Where a keyboard-opened timeline menu goes: the top-left of <paramref name="bar"/> just under the ruler and section lane, in this panel's coordinates.</summary>
    public Point TimelineBarAnchor(int bar) =>
        _timeline.TranslatePoint(new Point(_timeline.BarX(bar) + 4, RulerHeight + SectionHeight + 4), this);

    internal bool TryHandleContextKey(Key key, ModifierKeys mods) => _timeline.TryHandleContextMenuKey(key, mods);

    public double MeasureWidth { get; private set; } = 30;
    public bool ShowIndividualNotes
    {
        get => _timeline.ShowIndividualNotes;
        set
        {
            if (_timeline.ShowIndividualNotes == value && (!value || !_timeline.ShowContinuousBlocks)) return;
            _timeline.ShowIndividualNotes = value;
            if (value) _timeline.ShowContinuousBlocks = false;
            _timeline.InvalidateVisual();
        }
    }
    public bool ShowContinuousBlocks
    {
        get => _timeline.ShowContinuousBlocks;
        set
        {
            if (_timeline.ShowContinuousBlocks == value && (!value || !_timeline.ShowIndividualNotes)) return;
            _timeline.ShowContinuousBlocks = value;
            if (value) _timeline.ShowIndividualNotes = false;
            _timeline.InvalidateVisual();
        }
    }
    public bool HideEmptyTimelineGrid
    {
        get => _timeline.HideEmptyTimelineGrid;
        set
        {
            if (_timeline.HideEmptyTimelineGrid == value) return;
            _timeline.HideEmptyTimelineGrid = value;
            _timeline.InvalidateVisual();
        }
    }
    private string _playheadStyle = PlayheadStyles.Line;
    /// <summary>Playback position marker: the line, a marker in the current bar cell, or both (<see cref="PlayheadStyles"/>).</summary>
    public string PlayheadStyle
    {
        get => _playheadStyle;
        set
        {
            value = PlayheadStyles.Normalize(value);
            if (_playheadStyle == value) return;
            _playheadStyle = value;
            LayoutPlayhead();
        }
    }
    public bool ShowBarGlow
    {
        get => _timeline.ShowBarGlow;
        set
        {
            if (_timeline.ShowBarGlow == value) return;
            _timeline.ShowBarGlow = value;
            _timeline.InvalidateVisual();
        }
    }
    /// <summary>Show the [ ] brackets around the section that contains the playhead/cursor.</summary>
    public bool ShowSectionBrackets
    {
        get => _showSectionBrackets;
        set
        {
            if (_showSectionBrackets == value) return;
            _showSectionBrackets = value;
            if (!value) _sectionHighlight.Visibility = Visibility.Collapsed;
            else LayoutSectionHighlight(_lastSectionHighlightBar);
        }
    }
    private bool _showSectionBrackets = true;
    private int _lastSectionHighlightBar = -1;

    public double SectionBracketThickness
    {
        get => _sectionHighlight.StrokeThickness;
        set
        {
            var thickness = Math.Clamp(value, 1, 24);
            if (Math.Abs(_sectionHighlight.StrokeThickness - thickness) < 0.01) return;
            _sectionHighlight.StrokeThickness = thickness;
            LayoutSectionHighlight(_playheadBar);
        }
    }
    public double SectionGlowIntensity
    {
        get => _timeline.SectionGlowIntensity;
        set
        {
            _timeline.SectionGlowIntensity = Math.Clamp(value, 0, 1);
            _timeline.InvalidateVisual();
        }
    }
    /// <summary>Colour sections with the same base name alike (display only; marker colours are unchanged).</summary>
    public bool MatchSimilarSectionColours
    {
        get => _timeline.MatchSimilarSectionColours;
        set
        {
            if (_timeline.MatchSimilarSectionColours == value) return;
            _timeline.MatchSimilarSectionColours = value;
            RefreshSections();
        }
    }
    public bool ShowSectionNames
    {
        get => _timeline.ShowSectionNames;
        set { if (_timeline.ShowSectionNames == value) return; _timeline.ShowSectionNames = value; _timeline.InvalidateVisual(); }
    }
    public bool ShowBarNumbers
    {
        get => _timeline.ShowBarNumbers;
        set { if (_timeline.ShowBarNumbers == value) return; _timeline.ShowBarNumbers = value; _timeline.InvalidateVisual(); }
    }
    public bool AnimateSectionDragging
    {
        get => _timeline.AnimateSectionDragging;
        set { _timeline.AnimateSectionDragging = value; }
    }

    public ArrangementPanel()
    {
        _columns = new TrackColumnLayout(this);
        _tuning = new TuningButtonController(this);
        _addLane = new AddLaneController(this, _timeline);
        _groupDrag = new GroupDragController(this);
        _rowWidgets = new TrackRowWidgets(this);
        lock (AllPanels) { AllPanels.RemoveAll(w => !w.TryGetTarget(out _)); AllPanels.Add(new WeakReference<ArrangementPanel>(this)); }
        SetResourceReference(BackgroundProperty, "PanelBrush");
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ControlsWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        PreviewMouseDown += (_, e) => DismissTrackNameEditOnClick(e.OriginalSource as DependencyObject);

        _controlsHeader = new Border
        {
            Background = (Brush)Application.Current.FindResource("Panel2Brush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderSoftBrush"),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
        var headerStack = new DockPanel();
        var columnStrip = _columns.BuildHeader();
        DockPanel.SetDock(columnStrip, Dock.Bottom);
        headerStack.Children.Add(columnStrip);
        headerStack.Children.Add(BuildControlsHeader());
        _controlsHeader.Child = headerStack;
        _controlsHeader.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        _controlsHeader.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        SetColumn(_controlsHeader, 0);

        var controlsHost = new Grid();
        controlsHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RulerHeight + SectionHeight) });
        controlsHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_controlsHeader, 0);
        controlsHost.Children.Add(_controlsHeader);
        _controlsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _controlsScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _controlsScroll.Content = _controls;
        _controlsScroll.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "PanelBrush");
        // Right-click on empty space in the track list (rows keep their own menus; a click on a row opens nothing here).
        _controlsScroll.ContextMenuOpening += (_, e) =>
        {
            for (var d = e.OriginalSource as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is UIElement el && el != _controls && !ReferenceEquals(el, _addLane.Row) && _controls.Children.Contains(el)) { e.Handled = true; return; }   // the Add-track lane is empty space: it keeps the pane's menu
            _controlsScroll.ContextMenu = BuildEmptyAreaMenu(new Control[] { AutoFitItem() });
        };
        // The header strip (gaps between the buttons, column header row): the same "Show tracks in groups" item above the pane's items.
        _controlsHeader.ContextMenu = BuildEmptyAreaMenu();
        _controlsHeader.ContextMenuOpening += (_, _) => _controlsHeader.ContextMenu = BuildEmptyAreaMenu();
        _controlsScroll.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar))
        {
            BasedOn = (Style)Application.Current.FindResource(typeof(ScrollBar)),
            Setters =
            {
                new Setter(FrameworkElement.WidthProperty, 7d),
                new Setter(UIElement.OpacityProperty, 0.55)
            }
        };
        _controlsScroll.ScrollChanged += (_, e) =>
        {
            // A viewport-only change (the dock or window resizing) leaves the offset alone: nothing to redraw.
            if (!_rowScrollInputs.Changed(Math.Round(e.VerticalOffset, 2))) return;
            _timeline.VerticalScrollOffset = e.VerticalOffset;
            _timeline.InvalidateVisual();
            LayoutPlayhead();
            _timeline.RefreshHover();
            LayoutDragLaneOutline();
        };
        _controlsScroll.SizeChanged += (_, e) => { if (e.HeightChanged) RequestExtentSettle(); };   // the Add-track lane fills the room below the rows
        SetRow(_controlsScroll, 1);
        controlsHost.Children.Add(_controlsScroll);
        SetColumn(controlsHost, 0);
        Children.Add(controlsHost);
        // Edge grip between the track controls and the timeline: widens the whole controls area
        // (the track-name column absorbs the change).
        var areaGripFactory = new FrameworkElementFactory(typeof(Border));
        areaGripFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var areaGrip = new Thumb
        {
            Width = 6, Cursor = Cursors.SizeWE, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, -3, 0), ToolTip = "Drag to resize the track controls",
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = areaGripFactory }
        };
        areaGrip.DragDelta += (_, e) =>
            ColumnDefinitions[0].Width = new GridLength(Math.Clamp(ColumnDefinitions[0].Width.Value + e.HorizontalChange, 420, 900));
        areaGrip.DragCompleted += (_, _) => ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
        SetColumn(areaGrip, 0);
        SetZIndex(areaGrip, 50);
        Children.Add(areaGrip);

        // Hide WPF's built-in bar but keep the ScrollViewer horizontally scrollable. Disabled
        // also disables programmatic scrolling, which breaks playhead follow and our footer bar.
        _horizontal.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _horizontal.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _horizontal.HorizontalContentAlignment = HorizontalAlignment.Left;
        _horizontal.VerticalContentAlignment = VerticalAlignment.Top;
        _horizontal.VerticalAlignment = VerticalAlignment.Stretch;
        _timelineHost.HorizontalAlignment = HorizontalAlignment.Left;
        _timelineHost.VerticalAlignment = VerticalAlignment.Top;
        _timelineScrollBar.Opacity = 0.92;
        _timelineScrollBar.Style = (Style)Application.Current.FindResource("TimelineScrollBar");
        System.Windows.Automation.AutomationProperties.SetName(_timelineScrollBar, "Arrangement timeline horizontal scrollbar");
        // The timeline is one element; the playhead is a cheap overlay on top of it so following
        // playback never re-renders every measure cell.
        _timelineHost.Children.Add(_timeline);
        _timelineHost.Children.Add(_sectionDragOverlay);
        _timelineHost.Children.Add(_sectionInsertionIndicator);
        _timelineHost.Children.Add(_hoverCell);
        _timelineHost.Children.Add(_sectionHighlight);
        _timelineHost.Children.Add(_barMarker);
        _timelineHost.Children.Add(_playheadLine);
        _timelineHost.Children.Add(_selectedTrackPlayMarker);
        _timelineHost.Children.Add(_dragLaneOutline);
        _timelineHost.Children.Add(_areaMoveOutline);
        _timelineHost.Children.Add(_dropGhost);
        HookMediaDrop();
        _timeline.AreaMoveFinished += (_, target) => EndAreaMoveVisual(target);
        _timeline.SizeChanged += (_, e) =>
        {
            LayoutPlayhead();
            // The extent depends on the width (bars, zoom), not the height: a height-only change (splitter drag) settles once.
            if (e.WidthChanged) RefreshTimelineExtent(); else RequestExtentSettle();
            LayoutSectionHighlight(_playheadBar);
        };
        _horizontal.Content = _timelineHost;
        _horizontal.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "WindowBrush");
        SizeChanged += (_, e) => { if (e.WidthChanged) RefreshTimelineExtent(); else RequestExtentSettle(); };
        var timelineColumn = new Grid();
        timelineColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(_horizontal, 0);
        timelineColumn.Children.Add(_horizontal);
        SetColumn(timelineColumn, 1);
        Children.Add(timelineColumn);

        _timelineScrollBar.Scroll += (_, e) => _horizontal.ScrollToHorizontalOffset(e.NewValue);
        _horizontal.ScrollChanged += (_, _) => { SyncTimelineScrollBar(); _timeline.RefreshHover(); _timeline.SetViewport(_horizontal.HorizontalOffset, _horizontal.ViewportWidth); };
        _timeline.HoverCellChanged += (_, c) => LayoutHoverCell(c.bar, c.track);
        _timelineScrollBar.ValueChanged += (_, _) =>
        {
            if (!_syncingHorizontalScroll) _horizontal.ScrollToHorizontalOffset(_timelineScrollBar.Value);
        };

        _horizontal.PreviewMouseWheel += (_, e) =>
        {
            // Wheel over the timeline zooms around the pointer so the bar under the cursor stays put.
            var pointer = e.GetPosition(_horizontal);
            ZoomTimeline(ZoomStep(e.Delta > 0), pointer.X);
            e.Handled = true;
        };

        _timelineScrollBar.PreviewMouseWheel += (_, e) =>
        {
            var pointer = e.GetPosition(_horizontal);
            ZoomTimeline(ZoomStep(e.Delta > 0), pointer.X);
            e.Handled = true;
        };

        _controlsScroll.PreviewMouseWheel += (_, e) =>
        {
            if (_controlsScroll.ScrollableHeight <= 0) return;
            _controlsScroll.ScrollToVerticalOffset(_controlsScroll.VerticalOffset - e.Delta * 0.55);
            e.Handled = true;
        };
        _timeline.BarClicked += (_, bar) => BarSelected?.Invoke(this, bar);
        _timeline.TrackClicked += (_, track) => TrackSelected?.Invoke(this, track);
        _timeline.RangeDragged += (_, range) => RangeSelected?.Invoke(this, range);
        _timeline.PlainClicked += (_, _) => SelectionClearRequested?.Invoke(this, EventArgs.Empty);
        _timeline.TrackReordered += (_, move) => TrackReordered?.Invoke(this, move);
        _timeline.SectionReordered += (_, move) => SectionReordered?.Invoke(this, move);
        _timeline.SectionDragStarted += (_, _) => SectionDragStarted?.Invoke(this, EventArgs.Empty);
        _timeline.SectionDragCancelled += (_, _) => SectionDragCancelled?.Invoke(this, EventArgs.Empty);
        _timeline.SectionDragPreviewChanged += (_, preview) =>
        {
            _sectionDragOverlay.SetPreview(preview);
            _sectionInsertionIndicator.SetPreview(preview);
            if (preview is null)
            {
                if (_sectionDragAutoScroll) CompositionTarget.Rendering -= AdvanceSectionAutoScroll;
                _sectionDragAutoScroll = false;
                return;
            }
            _sectionDragPointerViewportX = preview.PointerX - _horizontal.HorizontalOffset;
            if (!_sectionDragAutoScroll)
            {
                CompositionTarget.Rendering += AdvanceSectionAutoScroll;
                _sectionDragAutoScroll = true;
            }
        };
        _timeline.SectionContextRequested += (_, markerIndex) => SectionContextRequested?.Invoke(this, markerIndex);
        _timeline.SectionResizeStarting += (_, _) => SectionResizeStarting?.Invoke(this, EventArgs.Empty);
        _timeline.SectionResized += (_, _) => SectionResized?.Invoke(this, EventArgs.Empty);
        _timeline.SectionMarkerMoved += (_, move) => SectionMarkerMoved?.Invoke(this, move);
        _timeline.SectionLaneContextRequested += (_, at) => SectionLaneContextRequested?.Invoke(this, at);
        _timeline.ContextRequested += (_, context) => TimelineContextRequested?.Invoke(this, context);
        _timeline.KeyboardContextRequested += (_, _) => TimelineKeyboardContextRequested?.Invoke(this, EventArgs.Empty);
        var clipGestures = _timeline.ClipGestures;
        clipGestures.ClipEditStarting += (_, _) => ClipEditStarting?.Invoke(this, EventArgs.Empty);
        clipGestures.ClipEdited += (_, clip) => ClipEdited?.Invoke(this, clip);
        clipGestures.ClipContextRequested += (track, clip, sec) => ClipContextRequested?.Invoke(track, clip, sec);
        clipGestures.ClipPropertiesRequested += (track, clip) => ClipPropertiesRequested?.Invoke(track, clip);
        _timeline.MediaDropped += plan => MediaDropped?.Invoke(plan);
        clipGestures.ClipMoveRequested += (clip, from, plan, copy) => ClipMoveRequested?.Invoke(clip, from, plan, copy);
        clipGestures.MidiClipToNotation += (clip, from, to) => MidiClipToNotation?.Invoke(clip, from, to);
        clipGestures.LaneClicked += (track, lane, sec) => LaneClicked?.Invoke(track, lane, sec);
        clipGestures.ClipLaneSelected += (track, lane, midi, ctrl) => ClipLaneSelected?.Invoke(track, lane, midi, ctrl);
        _timeline.TrackDragPreviewChanged += (_, preview) =>
        {
            if (!preview.active)
            {
                _dragFromTrack = -1;
                _dragTargetTrack = -1;
                _dragArmed = false;
                UpdateDragVisual();
                return;
            }
            _dragFromTrack = preview.from;
            _dragTargetTrack = preview.to;
            _dragArmed = true;
            _dragOrigin = new Point(0, RowTopOf(_project, preview.from) + TrackRowHeight / 2.0);
            UpdateDragVisual(new Point(0, _dragOrigin.Y + preview.deltaY));
        };
    }

    private void AdvanceSectionAutoScroll(object? sender, EventArgs e)
    {
        var viewport = _horizontal.ViewportWidth;
        if (viewport <= 1) return;
        const double edge = 38;
        var direction = _sectionDragPointerViewportX < edge ? -1 :
            _sectionDragPointerViewportX > viewport - edge ? 1 : 0;
        if (direction == 0) return;
        var penetration = direction < 0
            ? Math.Clamp((edge - _sectionDragPointerViewportX) / edge, 0, 1)
            : Math.Clamp((_sectionDragPointerViewportX - (viewport - edge)) / edge, 0, 1);
        var nextOffset = Math.Clamp(_horizontal.HorizontalOffset + direction * (3 + 15 * penetration),
            0, _horizontal.ScrollableWidth);
        if (Math.Abs(nextOffset - _horizontal.HorizontalOffset) < 0.1) return;
        _horizontal.ScrollToHorizontalOffset(nextOffset);
        _timeline.UpdateSectionDragPointer(_sectionDragPointerViewportX + nextOffset);
    }

    public void AttachTimelineScrollBar(ContentControl host) => host.Content = _timelineScrollBar;

    /// <summary>Height that shows the header plus exactly <paramref name="trackCount"/> track rows.</summary>
    // Include a small per-row allowance for device-pixel rounding/borders so the final track is not
    // clipped at fractional DPI scales.
    public double PreferredHeight(int trackCount) =>
        RulerHeight + SectionHeight + (_project is { } p ? RowsHeight(p) : trackCount * TrackRowHeight) + _addLane.Extra + 2;

    /// <summary>Test hook: the laid-out height of each track-control row.</summary>
    internal IReadOnlyList<double> TrackRowActualHeights => _trackRows.Select(r => r.ActualHeight).ToList();

    /// <summary>Height that shows the header plus every row of the bound song (group headers and collapsed groups included).</summary>
    public double PreferredHeight() => PreferredHeight(Math.Max(1, _project?.Tracks.Count ?? 1));

    private static int VisibleTrackCount(SongProject p, int fallback) =>
        p.Tracks.Count == 0 ? fallback : Enumerable.Range(0, p.Tracks.Count).Count(i => !IsCollapsed(p, i));

    /// <summary>The height that shows the header plus every row when each track row is <paramref name="rowHeight"/> tall.</summary>
    public double PreferredHeightAt(double rowHeight)
    {
        if (_project is not { Tracks.Count: > 0 } p) return PreferredHeight();
        return PreferredHeight() + VisibleTrackCount(p, 1) * (rowHeight - TrackRowHeight);
    }

    /// <summary>The track row height that makes the rows fill exactly <paramref name="paneHeight"/> (clamped to the default..maximum).</summary>
    public double RowHeightForPaneHeight(double paneHeight)
    {
        if (_project is not { Tracks.Count: > 0 } p) return DefaultTrackRowHeight;
        var visible = VisibleTrackCount(p, 1);
        if (visible == 0) return DefaultTrackRowHeight;
        var fixedPart = PreferredHeight() - visible * TrackRowHeight;   // header, group headers, audio lanes, per-row allowance
        return Math.Clamp((paneHeight - fixedPart) / visible, DefaultTrackRowHeight, MaxTrackRowHeight);
    }

    /// <summary>
    /// Stretches (or restores) every track row: the mixer rows on the left and the timeline lanes on the right change together,
    /// in place (no rebuild of the controls). False when the height is already that.
    /// </summary>
    public bool SetTrackRowHeight(double height)
    {
        height = Math.Round(Math.Clamp(height, DefaultTrackRowHeight, MaxTrackRowHeight), 1);
        if (_project is not { } p || Math.Abs(height - TrackRowHeight) < 0.05) return false;
        RowHeights.GetOrCreateValue(p).Value = height;
        // A second panel showing the same song follows, so its rows and lanes stay aligned too.
        lock (AllPanels)
            foreach (var weak in AllPanels.ToList())
                if (weak.TryGetTarget(out var other) && !ReferenceEquals(other, this) && ReferenceEquals(other._project, p)) other.ApplyRowHeight();
        ApplyRowHeight();
        return true;
    }

    private void ApplyRowHeight()
    {
        if (_project is { } p)
            for (var i = 0; i < _trackRows.Count && i < p.Tracks.Count; i++)
            {
                _trackRows[i].Height = RowHeightOf(p, p.Tracks[i]);
                if (_trackRows[i].Child is StackPanel { Children.Count: > 0 } stack && stack.Children[0] is FrameworkElement top) top.Height = p.Tracks[i].IsAudio ? AudioControlsHeight : TrackRowHeight;
            }
        RefreshTimelineGeometry();
        LayoutPlayhead();
    }

    /// <summary>
    /// The strip beside a track's clip lane. Only the first lane of an armed track has controls (monitoring, input, level);
    /// the other lanes are just space: a take is chosen by clicking it on the timeline.
    /// </summary>
    private FrameworkElement AudioLaneStrip(int index, TrackModel track, int lane)
    {
        // An audio row is lanes only: its controls take the top of lane 0, so lane 0's strip is the part below them.
        var strip = new DockPanel { Height = lane == 0 && track.IsAudio ? AudioLaneHeight - AudioControlsHeight : AudioLaneHeight,Margin = new Thickness(RowGridLeft + 8, 0, RowGridRight, 0), LastChildFill = false };
        if (lane != 0 || !track.RecordArm) return strip;
        var midi = AudioInputs.IsMidi(track.AudioInput);
        var label = new TextBlock
        {
            Text = midi ? "● MIDI (armed)" : "● Audio (armed)",
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Width = 92
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        strip.Children.Add(label);
        var monitor = new MonitorButton { On = track.MonitorInput, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        monitor.Toggled += (_, _) => MonitorToggled?.Invoke(index, !track.MonitorInput);
        strip.Children.Add(monitor);
        var input = new ComboBox
        {
            ItemsSource = AudioInputs.All, SelectedItem = track.AudioInput, Width = 136, Height = 22, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Input recorded and monitored when armed: an audio input, or MIDI (played live through the track and recorded as a MIDI clip)",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        input.SelectionChanged += (_, _) => { if (input.SelectedItem is string chosen && chosen != track.AudioInput) AudioInputChosen?.Invoke(index, chosen); };
        strip.Children.Add(input);
        var meter = new InputMeter { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 110, ToolTip = midi ? "MIDI input (velocity)" : "Input level" };
        _inputMeters[track] = meter;
        strip.Children.Add(meter);
        return strip;
    }

    private readonly Dictionary<TrackModel, InputMeter> _inputMeters = new();

    /// <summary>Input levels of armed tracks (peak 0..1), shown on their lane strips.</summary>
    public void ShowInputLevel(TrackModel track, double peak, bool midi)
    {
        if (_inputMeters.TryGetValue(track, out var meter)) meter.Show(peak, midi);
    }

    /// <summary>Caches the timeline grid as a GPU texture (see MainWindow.ApplyGpuLayerCaches).</summary>
    public void SetTimelineCache(CacheMode cache) => _timeline.SetGpuCache(cache);

    public void SetPlaybar(UIElement playbar)
    {
        if (VisualTreeHelper.GetParent(playbar) is Panel parent) parent.Children.Remove(playbar);
        _playbarHost.Content = playbar;
    }

    private UIElement BuildControlsHeader()
    {
        var panel = new DockPanel { Margin = new Thickness(5, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var add = HeaderIconButton("IconPlus", "Add track");
        // Narrow enough to leave the transport's loop button room; same icon, font and size.
        add.Width = 66;
        add.MinWidth = 66;
        add.Padding = new Thickness(4, 0, 4, 0);
        add.Height = 34;
        add.Margin = new Thickness(2, 0, 3, 0);
        add.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AddTrackSurfaceBrush");
        add.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "AddTrackBorderBrush");
        var addContents = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        addContents.Children.Add(new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource("IconPlus"),
            Style = (Style)Application.Current.FindResource("IconPath"),
            Width = 17, Height = 17, Stroke = Brushes.White
        });
        addContents.Children.Add(new TextBlock { Text = "Track", FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        add.Content = addContents;
        add.Click += (_, _) => AddTrackRequested?.Invoke(this, EventArgs.Empty);
        // Opens the Add track window (instrument and position). The tooltip shows the live Add track keys.
        TooltipShortcuts.Bind(add, "Add track", "Track.Add");
        add.MouseRightButtonUp += (_, e) => { e.Handled = true; AddTrackMenuRequested?.Invoke(add); };
        DockPanel.SetDock(add, Dock.Left);
        panel.Children.Add(add);
        var zoomButtons = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        var zoomIn = HeaderZoomButton("zoom_plus", "Zoom in timeline");
        var zoomOut = HeaderZoomButton("zoom_minus", "Zoom out timeline");
        foreach (var button in new[] { zoomIn, zoomOut })
        {
            button.Width = button.MinWidth = 26;
            button.Height = 17;
            button.Margin = new Thickness(1, 0, 1, 0);
            if (button.Content is SvgIconView icon) { icon.Width = 13; icon.Height = 13; }
            zoomButtons.Children.Add(button);
        }
        zoomIn.Click += (_, _) => ZoomTimeline(ZoomStep(true), PlayheadViewportX());
        zoomOut.Click += (_, _) => ZoomTimeline(ZoomStep(false), PlayheadViewportX());
        DockPanel.SetDock(zoomButtons, Dock.Right);
        panel.Children.Add(zoomButtons);
        MasterVolumeKnob = new KnobControl
        {
            Minimum = 0, Maximum = 100, DefaultValue = 100, Origin = 0, Value = 100,
            Width = 38, Height = 38, Label = "Master volume", Format = v => $"{v:0}%",
            Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(MasterVolumeKnob, Dock.Right);
        panel.Children.Add(MasterVolumeKnob);
        TuningButton = new Button
        {
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Height = 28, MinWidth = 34, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(2, 1, 2, 1),
            Background = (Brush)Application.Current.FindResource("Panel2Brush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Content = _tuning.BuildContent(), ToolTip = "Global tuning: click to open the tuning window · double-click to type a semitone shift · right-click for quick options (tune up or down a semitone, back to original)"
        };
        TuningButton.SetResourceReference(Control.BackgroundProperty, "Panel2Brush");
        TuningButton.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        System.Windows.Automation.AutomationProperties.SetName(TuningButton, "Global tuning");
        DockPanel.SetDock(TuningButton, Dock.Right);
        panel.Children.Add(TuningButton);
        // Snap: a magnet, lit while on (right-click for its settings). Docked beside the mixer button.
        _snapButton = new Button
        {
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Width = 30, MinWidth = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0), BorderThickness = new Thickness(1),
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M6,3 L6,11 A6,6 0 0 0 18,11 L18,3 L14,3 L14,11 A2,2 0 0 1 10,11 L10,3 Z M6,7 L10,7 M14,7 L18,7"),
                Stroke = (Brush)Application.Current.FindResource("TextBrush"), StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round,
                Width = 18, Height = 19, Stretch = Stretch.Uniform, IsHitTestVisible = false
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(_snapButton, "Snap");
        _snapButton.Click += (_, _) =>
        {
            if (_timeline.Snap is not { } snap) return;
            snap.Enabled = !snap.Enabled;
            UpdateSnapButton();
            SnapChanged?.Invoke(this, EventArgs.Empty);
        };
        _snapButton.MouseRightButtonUp += (_, e) => { e.Handled = true; SnapSettingsRequested?.Invoke(this, EventArgs.Empty); };
        UpdateSnapButton();
        DockPanel.SetDock(_snapButton, Dock.Right);
        panel.Children.Add(_snapButton);
        // Mixer: groups, levels, sound source and FX chains (drawn like the transport icons, readable in both themes).
        var mixer = new Button
        {
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Width = 34, MinWidth = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0),
            BorderThickness = new Thickness(1), ToolTip = "Mixer: track and group levels, pan, pitch, sound source and FX chains"
        };
        TooltipShortcuts.Bind(mixer, "Mixer: track and group levels, pan, pitch, sound source and FX chains", "View.Mixer");
        mixer.SetResourceReference(Control.BackgroundProperty, "Panel2Brush");
        mixer.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        var mixerIcon = new SvgIconView { Icon = "mixer", Width = 28, Height = 28, ShowFrame = false, IsHitTestVisible = false };
        mixerIcon.SetBinding(SvgIconView.ButtonHoveredProperty, new System.Windows.Data.Binding("IsMouseOver") { Source = mixer });
        mixer.Content = mixerIcon;
        System.Windows.Automation.AutomationProperties.SetName(mixer, "Mixer");
        mixer.Click += (_, _) => MixerRequested?.Invoke(this, EventArgs.Empty);
        DockPanel.SetDock(mixer, Dock.Right);
        panel.Children.Add(mixer);
        _tuning.Attach(TuningButton);
        DockPanel.SetDock(_playbarHost, Dock.Left);
        panel.Children.Add(_playbarHost);
        return panel;
    }

    private static Button HeaderIconButton(string resource, string tooltip)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Width = 25, MinWidth = 25, Height = 26, Margin = new Thickness(1),
            ToolTip = tooltip
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Content = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource(resource),
            Style = (Style)Application.Current.FindResource("IconPath")
        };
        return button;
    }

    private static Button HeaderZoomButton(string icon, string tooltip)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("TransportButton"),
            Width = 28, MinWidth = 28, Height = 28, Margin = new Thickness(2, 1, 2, 1),
            Padding = new Thickness(0),
            Background = (Brush)Application.Current.FindResource("Panel2Brush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            ToolTip = tooltip
        };
        button.SetResourceReference(Control.BackgroundProperty, "Panel2Brush");
        button.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        var iconView = new SvgIconView
        {
            Icon = icon,
            Width = 18,
            Height = 18,
            ShowFrame = false,
            IsHitTestVisible = false
        };
        BindingOperations.SetBinding(iconView, SvgIconView.ButtonHoveredProperty,
            new Binding(nameof(Button.IsMouseOver)) { Source = button });
        button.Content = iconView;
        return button;
    }
}

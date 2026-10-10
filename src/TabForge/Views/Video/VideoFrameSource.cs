using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.Band;
using TabForge.Views.Score;
using TabForge.Visualization;

namespace TabForge.Views.Video;

// Draws the song at a given time into BGRA pixels at the video size: a score page with its playhead (whole systems, turned a page at a
// time), the track's fretboard / keyboard / drums, and the Band view, as the VideoViewSpec chooses. Everything is built off-screen (nothing
// is shown in a window) and driven by the song time alone, so frame i is the same on every run; the Band view runs with UseFrameClock, so its rows and instruments refresh on every frame, never by the wall clock. The chosen theme is painted into the
// off-screen tree's own resources, so the app's windows keep their theme.
// Owns: the off-screen score, instrument and Band views, their theme resources, and the Band layout it borrows from the song.
// Does not own: the encoder, the audio or the frame times (VideoExportFlow). Use on the UI thread.
// Tests: TestVideoExport, TestVideoFrameGolden.
public sealed class VideoFrameSource : IDisposable
{
    /// <summary>Band rows draw their tab at this multiple of the lane's own size, so a 16:9 frame is filled with readable tab.</summary>
    private const double BandLaneZoom = 1.3;

    /// <summary>The Band rows' scrollbar column, in DIPs (26 px at 150%), whatever the display: the system metric would change the lane width and the thumb with the screen's scale.</summary>
    private const double ScrollBarWidth = 52.0 / 3;

    private readonly SongProject _project;
    private readonly ScoreTimeline _timeline;
    private readonly AppSettings _settings;
    private readonly VideoViewSpec _spec;
    private readonly ScoreAppearance? _look;
    private readonly int _track;
    private readonly bool _light;
    private readonly Grid _root = new();
    private readonly RenderTargetBitmap _bitmap;
    private readonly byte[] _pixels;
    private TabEditorControl? _editor;
    private PlayheadOverlay? _playhead;
    private Border? _page;
    private InstrumentPanel? _instrument;
    private BandViewController? _band;
    private readonly MatrixTransform _pageTransform = new();
    private double _pageScale = 1, _viewNative;
    private int _shownPage = -1;
    private readonly BandLayoutData? _savedBandLayout;
    private bool _disposed, _drawn;
    private Canvas? _scoreCanvas;
    private double _bakedMs;
    private byte[]? _basePixels;
    private int _topHeight;
    private UIElement? _live;
    private RenderTargetBitmap? _strip;
    private byte[] _stripPixels = Array.Empty<byte>();
    private RenderTargetBitmap? _scoreBitmap;
    private (int Page, int Bar, int Cell) _bakedKey = (-1, -1, -1);

    public VideoFrameSource(SongProject project, ScoreTimeline timeline, AppSettings settings, VideoViewSpec spec) : this(project, timeline, settings, spec, null) { }

    /// <param name="look">The live score's look (colours, spacing, highlights) to draw with; the defaults when null.</param>
    internal VideoFrameSource(SongProject project, ScoreTimeline timeline, AppSettings settings, VideoViewSpec spec, ScoreAppearance? look)
    {
        if (project.Tracks.Count == 0) throw new InvalidOperationException("The song has no tracks.");
        _project = project; _timeline = timeline; _spec = spec; _look = look;
        _settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings)) ?? new AppSettings();
        _track = Math.Clamp(spec.Tracks.Count > 0 ? spec.Tracks[0] : 0, 0, project.Tracks.Count - 1);
        _savedBandLayout = project.BandLayout;
        // The user's look in their own theme; a theme other than the app's takes that theme's preset colours.
        var mode = spec.Dark ? "Dark" : "Light";
        var own = _settings.Appearance;
        var appLight = string.Equals(own.ThemeMode, "Custom", StringComparison.OrdinalIgnoreCase) ? ThemeService.IsLightColour(own.Background) : ThemeService.IsLightTheme(own.ThemeMode);
        if (appLight == spec.Dark)
            ThemeService.ApplyPreset(_settings.Appearance, mode);
        _root.Resources = new ResourceDictionary();
        _light = ThemeService.Paint(_root.Resources, _settings.Appearance);
        _root.Resources[SystemParameters.VerticalScrollBarWidthKey] = ScrollBarWidth;
        _settings.Timeline.Band.LaneZoom = BandLaneZoom;
        try
        {
            _bitmap = new RenderTargetBitmap(spec.Width, spec.Height, 96, 96, PixelFormats.Pbgra32);
            _pixels = new byte[spec.Width * spec.Height * 4];
            WithTheme(Build);
        }
        catch { Dispose(); throw; }
    }

    private string PaperKey => _spec.Dark ? "PaperDarkBrush" : "PaperLightBrush";
    private bool ShowsScore => _spec.Layout != VideoLayout.Band;
    private bool ShowsBand => _spec.Layout is VideoLayout.Band or VideoLayout.ScoreAndBand;
    private bool ShowsInstrument => _spec.Layout == VideoLayout.Focus && _spec.ShowInstrument && !_project.Tracks[_track].IsAudio;

    /// <summary>Runs <paramref name="draw"/> with the drawing code's light/dark switch on the export's theme, and puts the app's back.</summary>
    private void WithTheme(Action draw)
    {
        var app = VisualTheme.IsLight;
        VisualTheme.IsLight = _light;
        try { draw(); } finally { VisualTheme.IsLight = app; }
    }

    private void Build()
    {
        _root.SetResourceReference(Panel.BackgroundProperty, ShowsBand ? "WorkspaceBrush" : PaperKey);
        _root.Width = _spec.Width; _root.Height = _spec.Height;
        double top = _spec.Layout switch { VideoLayout.Focus => ShowsInstrument ? 0.62 : 1, VideoLayout.ScoreAndBand => 0.42, _ => 1 };
        if (ShowsScore) BuildScore(top);
        if (ShowsInstrument) BuildInstrument();
        if (ShowsBand) BuildBand(top);
        _root.Measure(new Size(_spec.Width, _spec.Height));
        _root.Arrange(new Rect(0, 0, _spec.Width, _spec.Height));
        _root.UpdateLayout();
    }

    private void Rows(double topShare)
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(topShare, GridUnitType.Star) });
        if (topShare < 1) _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - topShare, GridUnitType.Star) });
    }

    private void BuildScore(double share)
    {
        Rows(share);
        _editor = new TabEditorControl { Project = _project, SelectedTrackIndex = _track, Notation = _spec.Notation };
        if (_look is not null) BandAppearance.Copy(_look, _editor.Appearance);
        _editor.Appearance.DarkPaper = _spec.Dark;
        // The page, the margins round it and the instrument share one paper colour: the theme's.
        _editor.Appearance.DarkPaperColor = TryColour(_settings.Appearance.DarkScorePaperColour) ?? _editor.Appearance.DarkPaperColor;
        _editor.Appearance.LightPaperColor = TryColour(_settings.Appearance.LightScorePaperColour) ?? _editor.Appearance.LightPaperColor;
        _playhead = new PlayheadOverlay();
        if (TryColour(_settings.Follow.PlayheadColour) is { } colour) _playhead.SetColor(colour);
        _playhead.SetThickness(_settings.Follow.PlayheadThickness);
        _page = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = new Grid { Children = { _editor, _playhead } } };
        _page.SetResourceReference(Border.BackgroundProperty, PaperKey);
        _page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = _page.DesiredSize;
        _page.Width = natural.Width; _page.Height = natural.Height;
        _page.RenderTransform = _pageTransform;
        _pageScale = _spec.Width / Math.Max(1, natural.Width);
        _viewNative = _spec.Height * share / _pageScale;
        var canvas = _scoreCanvas = new Canvas { ClipToBounds = true };
        canvas.SetResourceReference(Panel.BackgroundProperty, PaperKey);
        canvas.Children.Add(_page);
        Grid.SetRow(canvas, 0);
        _root.Children.Add(canvas);
        IScorePlayhead playback = _editor.Playback;
        playback.Bind(_timeline, null, _track);
        playback.Active = true;
        TurnPage(0);
    }

    private void BuildInstrument()
    {
        _instrument = new InstrumentPanel { Surface = BandAppearance.PaperOf(_editor!.Appearance) };
        var host = new Border { Child = _instrument, BorderThickness = new Thickness(0, 1, 0, 0) };
        host.SetResourceReference(Border.BackgroundProperty, PaperKey);
        host.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        _live = host;
        Grid.SetRow(host, 1);
        _root.Children.Add(host);
    }

    private void BuildBand(double share)
    {
        var onlyBand = _spec.Layout == VideoLayout.Band;
        if (onlyBand) Rows(1); else if (_root.RowDefinitions.Count == 0) Rows(share);
        _project.BandLayout = null;   // the song's own Band layout is put back by Dispose; the rows here are the export's
        _band = new BandViewController(new BandHost(_project, _settings, _editor ?? new TabEditorControl { Project = _project })) { UseFrameClock = true };
        var state = _band.State;
        state.Sync(_project);
        var chosen = _spec.Tracks.Where(i => i >= 0 && i < _project.Tracks.Count).Select(i => _project.Tracks[i]).Distinct().ToList();
        if (chosen.Count == 0) chosen.Add(_project.Tracks[_track]);
        foreach (var t in _project.Tracks)
        {
            if (state.IsShown(t) != chosen.Contains(t)) state.Toggle(t);
            if (!_spec.ShowInstrument && !state.IsInstrumentHidden(t)) state.ToggleInstrument(t);
        }
        state.ChangeRowsPerScreen(Math.Clamp(chosen.Count, BandLayoutState.MinRowsPerScreen, BandLayoutState.MaxRowsPerScreen) - state.RowsPerScreen);
        var view = _band.View;
        Grid.SetRow(view, onlyBand ? 0 : 1);
        _live = view;
        _root.Children.Add(view);
    }

    /// <summary>A video has no pointer: the Band view's track pills and row buttons are left out.</summary>
    private static void HideBandControls(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ButtonBase button) button.Visibility = Visibility.Collapsed; else HideBandControls(child);
        }
    }

    /// <summary>Draws the song at <paramref name="ms"/> (song time, as the timeline counts it) and returns the frame: width x height x 4 bytes, top-down BGRA.</summary>
    public ReadOnlySpan<byte> Render(double ms)
    {
        WithTheme(() =>
        {
            // The Band view checks on each tick here (UseFrameClock), playing or stopped. A few stopped ticks first make the rows,
            // layout and wrap of frame 0 the settled ones.
            for (var pass = 0; !_drawn && _band is not null && pass < 3; pass++)
            {
                _band.ProbePlay = null;
                _band.Tick();
                _root.UpdateLayout();
            }
            _drawn = true;
            Draw(ms);
        });
        return _pixels;
    }

    private void Draw(double ms)
    {
        Lap();
        var pos = PlayheadMapper.Map(_timeline, ms, 0, 0);
        if (_editor is not null)
        {
            IScorePlayhead score = _editor.Playback;
            score.Ms = ms;
            score.Fraction = pos.BarFraction;
            score.SetPlayhead(pos.Bar, pos.Cell);
            _playhead!.SetGeometry(score.PlayheadGeometry());
            if (score.PlaybackHorizontalGeometry(pos.Bar, pos.BarFraction) is { } g) TurnPage(g.SystemIndex);
            if (_live is null)   // a live row below costs a render per frame anyway, so only the score-only layout gains
            {
                Timings.Layout += Lap();
                BakeScore(pos.Bar, pos.Cell, ms);
                Timings.Bake += Lap();
            }
        }
        if (_instrument is not null) { ShowInstrument(ms); Timings.Instrument += Lap(); }
        if (_band is not null)
        {
            _band.ProbePlay = (pos.Bar, pos.BarFraction, ms, _timeline);
            _band.Tick();
            _root.UpdateLayout();
            HideBandControls(_band.View);
            VideoBandCull.Update(_band.View);
            Timings.Band += Lap();
        }
        _root.UpdateLayout();
        Timings.Layout += Lap();
        if (_basePixels is not null)
        {
            ComposeScoreFrame();
            Timings.Compose += Lap();
            return;
        }
        _bitmap.Clear();
        _bitmap.Render(_root);
        Timings.Render += Lap();
        _bitmap.CopyPixels(_pixels, _spec.Width * 4, 0);
        Timings.Copy += Lap();
    }

    /// <summary>Per-phase timings, filled only when TABFORGE_VIDEO_PERF is set (the export probe reads them); the phases are contiguous.</summary>
    public readonly VideoFrameTimings Timings = new();
    private readonly bool _perf = Environment.GetEnvironmentVariable("TABFORGE_VIDEO_PERF") is { Length: > 0 };
    private readonly System.Diagnostics.Stopwatch _lap = System.Diagnostics.Stopwatch.StartNew();
    /// <summary>Milliseconds since the previous call, or 0 when timings are off.</summary>
    private double Lap()
    {
        if (!_perf) return 0;
        var t = _lap.Elapsed.TotalMilliseconds; _lap.Restart(); return t;
    }

    /// <summary>The score page changes only on a page turn or when the lit cell moves, so it is drawn into <see cref="_basePixels"/> then;
    /// each frame copies those pixels, draws only the playhead's strip over them and redraws the live rows below (instrument or Band).</summary>
    private void BakeScore(int bar, int cell, double ms)
    {
        var key = (_shownPage, bar, cell);
        var moved = key != _bakedKey || ((IScorePlayhead)_editor!.Playback).NeedsRepaint(Math.Min(_bakedMs, ms), Math.Max(_bakedMs, ms));
        if (!moved) return;
        _bakedMs = ms; Timings.Bakes++;
        _bakedKey = key;
        _playhead!.Visibility = Visibility.Hidden;
        _root.UpdateLayout();
        var w = (int)Math.Ceiling(_scoreCanvas!.ActualWidth); _topHeight = Math.Min(_spec.Height, (int)Math.Ceiling(_scoreCanvas.ActualHeight));
        if (_scoreBitmap is null || _scoreBitmap.PixelWidth != w || _scoreBitmap.PixelHeight != _topHeight)
        {
            _scoreBitmap = new RenderTargetBitmap(w, _topHeight, 96, 96, PixelFormats.Pbgra32);
            _basePixels = new byte[w * _topHeight * 4];
        }
        _scoreBitmap.Clear();
        _scoreBitmap.Render(_scoreCanvas);
        _scoreBitmap.CopyPixels(_basePixels!, w * 4, 0);
        _playhead.Visibility = Visibility.Visible;
    }

    /// <summary>Puts the baked page and the playhead strip into <see cref="_pixels"/>.</summary>
    private void ComposeScoreFrame()
    {
        Array.Copy(_basePixels!, _pixels, _basePixels!.Length);
        if (((IScorePlayhead)_editor!.Playback).PlayheadGeometry() is { } g) BlendPlayhead(g);
    }

    private void BlendPlayhead((double X, double Top, double Bottom) g)
    {
        var pad = _settings.Follow.PlayheadThickness + 3;
        var native = new Rect(g.X - pad, g.Top - 2, pad * 2, Math.Max(1, g.Bottom - g.Top) + 4);
        var device = _pageTransform.Matrix.Transform(native.TopLeft); var far = _pageTransform.Matrix.Transform(native.BottomRight);
        int x0 = Math.Max(0, (int)Math.Floor(device.X)), y0 = Math.Max(0, (int)Math.Floor(device.Y));
        int x1 = Math.Min(_spec.Width, (int)Math.Ceiling(far.X)), y1 = Math.Min(_topHeight, (int)Math.Ceiling(far.Y));
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return;
        if (_strip is null || _strip.PixelWidth != w || _strip.PixelHeight != h)
        {
            _strip = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            _stripPixels = new byte[w * h * 4];
        }
        var brush = new VisualBrush(_playhead) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = native, ViewportUnits = BrushMappingMode.Absolute, Viewport = native, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-x0, -y0));
            dc.PushTransform(new MatrixTransform(_pageTransform.Matrix));
            dc.DrawRectangle(brush, null, native);
        }
        _strip.Clear();
        _strip.Render(dv);
        _strip.CopyPixels(_stripPixels, w * 4, 0);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var si = (y * w + x) * 4;
                int a = _stripPixels[si + 3];
                if (a == 0) continue;
                var di = ((y0 + y) * _spec.Width + x0 + x) * 4;
                for (var c = 0; c < 4; c++) _pixels[di + c] = (byte)Math.Min(255, _stripPixels[si + c] + _pixels[di + c] * (255 - a) / 255);
            }
    }

    /// <summary>Shows the page of whole systems that holds <paramref name="system"/>: as many systems as fit, the header above the first
    /// page when it fits too, centred in the view and cut below the last whole system, so no part of the next one shows.</summary>
    private void TurnPage(int system)
    {
        var (_, header, height, count) = _editor!.ExportMetrics();
        if (height <= 0) return;
        var perPage = Math.Max(1, (int)Math.Floor(_viewNative / height));
        var page = Math.Max(0, system) / perPage;
        if (page == _shownPage) return;
        _shownPage = page;
        var first = page * perPage;
        var top = first == 0 && header + perPage * height <= _viewNative ? 0 : header + first * height;
        var bottom = header + Math.Min(Math.Max(1, count), first + perPage) * height;
        var offset = top - Math.Max(0, _viewNative - (bottom - top)) / 2;
        _editor.ExportSystemRange = (first - 1, first + perPage);   // one system of margin each side: nothing else can reach the page
        _page!.Clip = new RectangleGeometry(new Rect(0, top, _page.Width, bottom - top));
        _pageTransform.Matrix = new Matrix(_pageScale, 0, 0, _pageScale, 0, -offset * _pageScale);
    }

    private void ShowInstrument(double ms)
    {
        var track = _project.Tracks[_track];
        var state = InstrumentVisualizer.Build(_project, track, _timeline, ms, true, false, 4, false, false, null, _settings.Editing.FretboardFrets is 12 or 24 ? _settings.Editing.FretboardFrets : 24);
        InstrumentPanelController.ApplyAppearance(state, _settings);
        _instrument!.DrumLabel = track.Kind == TrackKind.Drums ? midi => Services.DrumMaps.For(track, midi).Label : null;
        _instrument.Title = track.Name;
        _instrument.SetState(state);
    }

    private static Color? TryColour(string hex)
    {
        try { return ColorConverter.ConvertFromString(hex) is Color c ? c : null; }
        catch (FormatException) { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _band?.Dispose();
        _project.BandLayout = _savedBandLayout;
        _editor?.Playback.Clear();
    }

    /// <summary>The Band view's window: the song and settings of the export, no real window and no selected row.</summary>
    private sealed class BandHost : IBandViewHost
    {
        public BandHost(SongProject project, AppSettings settings, TabEditorControl editor)
        {
            Project = project; Settings = settings; Editor = editor;
            ActiveDocument = DocumentSession.FromProject(project, null);
        }
        public Window Window { get; } = new();
        public AppSettings Settings { get; }
        public TabEditorControl Editor { get; }
        public SongProject Project { get; }
        public TrackModel? SelectedTrack => null;
        public DocumentSession ActiveDocument { get; }
        public (bool LeftHanded, bool ShowNoteNames, string? Scale, int Horizon) InstrumentOptions => (false, false, null, 4);
        public VisualOptions? Visual => null;
        public void SaveSettings() { }
        public void SetStatus(string text) { }
        public void MoveSongTrack(int from, int to) { }
        public void RunScoreMenu(MenuSpec spec) { }
        public void ShowCursor(int trackIndex, int bar, int cell) { }
    }
}

/// <summary>Accumulated milliseconds per phase of drawing frames: Layout (playhead, page turn, layout passes), Bake (score page bake, score-only
/// and Focus without an instrument), Instrument, Band (Band tick and control hiding), Render (rasterising the tree), Copy (readback), Compose
/// (score-only page copy and playhead blend) and Encode (the hand-off to the encoder, including any wait for queue room).</summary>
public sealed class VideoFrameTimings
{
    public double Layout, Bake, Instrument, Band, Render, Copy, Compose, Encode;
    public int Bakes;
    public int Frames;
}

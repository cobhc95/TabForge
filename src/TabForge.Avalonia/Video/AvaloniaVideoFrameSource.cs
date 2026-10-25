using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Video;

// Owns: the Avalonia frames of File > Export video: the chosen track's score page drawn off-screen through the shared Core score host
//   (ScoreGeometryProbe, the drawing the score view uses), turned a page of whole systems at a time with its playhead at the song time, into
//   top-down BGRA pixels at the video size (the IVideoFrameSource contract VideoExportFlow encodes).
// Does not own: the frame clock, the audio or the encoder (VideoExportFlow), the score drawing itself (Core ScoreRenderer through the probe).
// Keys and ceilings: one render target, one read-back bitmap and one pixel buffer, all the video size; nothing grows per frame.
// Threads: UI thread (the flow draws there).
// Tests: TabForge.Avalonia.Tests.VideoExportParityTests (BGRA contract, repeatable frames, moving playhead).
/// <summary>Draws the song at a time as a video frame. Every layout draws the first chosen track's score: the Band rows and the instrument are not drawn yet.</summary>
internal sealed class AvaloniaVideoFrameSource : IVideoFrameSource
{
    private const double DotsPerInch = 96;
    private const int AllBars = int.MaxValue;
    private const int BytesPerPixel = 4;

    private readonly ScoreTimeline _timeline;
    private readonly VideoViewSpec _spec;
    private readonly int _track;
    private readonly AvaloniaDrawContext _draw = new();
    private readonly ScoreGeometryProbe _probe;
    private readonly RenderTargetBitmap _target;
    private readonly WriteableBitmap _readback;
    private readonly byte[] _pixels;
    private readonly AppearanceSettings? _appearance;
    private IBrush _paper = Brushes.Transparent;
    private UiColor _paperColour;
    private string? _paperText;
    private readonly double _scale, _viewNative;
    private int _shownPage = -1;
    private double _top, _bottom, _offset;   // the shown page's rows (page coordinates) and its scroll

    public AvaloniaVideoFrameSource(SongProject project, ScoreTimeline timeline, VideoViewSpec spec, AppearanceSettings? appearance = null)
    {
        if (project.Tracks.Count == 0) throw new InvalidOperationException("The song has no tracks.");
        _timeline = timeline; _spec = spec;
        _appearance = appearance;
        _track = Math.Clamp(spec.Tracks.Count > 0 ? spec.Tracks[0] : 0, 0, project.Tracks.Count - 1);
        _probe = new ScoreGeometryProbe(project, _draw) { Notation = spec.Notation };
        _probe.Appearance.DrawEmptyBarRests = true;
        _probe.Light = !spec.Dark;
        _probe.ShowTrack(_track);
        _scale = spec.Width / Math.Max(1, _probe.ContentSize().Width);
        _viewNative = spec.Height / _scale;
        var size = new PixelSize(spec.Width, spec.Height);
        var dpi = new Vector(DotsPerInch, DotsPerInch);
        _target = new RenderTargetBitmap(size, dpi);
        _readback = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        _pixels = new byte[spec.Width * spec.Height * BytesPerPixel];
        RefreshPaper();
        TurnPage(0);
    }

    public VideoFrameTimings Timings { get; } = new();

    public ReadOnlySpan<byte> Render(double ms)
    {
        RefreshPaper();   // the settings object is live: a paper change rebuilds the brush, never once per frame
        var position = PlayheadMapper.Map(_timeline, ms, 0, 0);
        _probe.Play.Set(_timeline, _track, position.Bar, position.Cell, position.BarFraction, ms);
        var (header, systemHeight, _) = _probe.PrintRows();
        if (_probe.Play.PlayheadLine(_probe) is var (_, lineTop, _) && systemHeight > 0) TurnPage((int)Math.Floor((lineTop - header) / systemHeight));

        using (var context = _target.CreateDrawingContext())
        {
            context.FillRectangle(_paper, new Rect(0, 0, _spec.Width, _spec.Height));
            using var page = context.PushTransform(new Matrix(_scale, 0, 0, _scale, 0, -_offset * _scale));
            using var clip = context.PushClip(new Rect(0, _top, _probe.ContentSize().Width, Math.Max(0, _bottom - _top)));
            _probe.DrawPage(_draw.Use(context), AllBars, withPlayhead: true, visibleRows: (_top, _bottom));
        }
        using (var locked = _readback.Lock())
        {
            _target.CopyPixels(locked, AlphaFormat.Premul);
            var row = _spec.Width * BytesPerPixel;
            for (var y = 0; y < _spec.Height; y++) Marshal.Copy(locked.Address + y * locked.RowBytes, _pixels, y * row, row);
        }
        return _pixels;
    }

    /// <summary>Rebuilds the paper brush only when the live paper colour changed (a theme or custom-colour edit during the export).</summary>
    private void RefreshPaper()
    {
        var text = _probe.Appearance.DarkPaper ? _appearance?.DarkScorePaperColour : _appearance?.LightScorePaperColour;
        if (_appearance is not null && text != _paperText)
        {
            _paperText = text;
            ScoreSettingsBinding.ApplyColours(_appearance, _probe.Appearance);
        }
        var colour = _probe.Appearance.PaperColor;
        if (colour == _paperColour) return;
        _paperColour = colour;
        _paper = new ImmutableSolidColorBrush(AvaloniaPalette.ColorOf(colour));
    }

    /// <summary>Shows the page of whole systems that holds <paramref name="system"/> (the rule of the WPF frame source): as many systems as fit,
    /// the header above the first page when it fits too, centred and cut below the last whole system.</summary>
    private void TurnPage(int system)
    {
        var (header, height, count) = _probe.PrintRows();
        if (height <= 0) { _top = 0; _bottom = _viewNative; return; }
        var perPage = Math.Max(1, (int)Math.Floor(_viewNative / height));
        var page = Math.Max(0, system) / perPage;
        if (page == _shownPage) return;
        _shownPage = page;
        var first = page * perPage;
        _top = first == 0 && header + perPage * height <= _viewNative ? 0 : header + first * height;
        _bottom = header + Math.Min(Math.Max(1, count), first + perPage) * height;
        _offset = _top - Math.Max(0, _viewNative - (_bottom - _top)) / 2;
    }

    public void Dispose()
    {
        _target.Dispose();
        _readback.Dispose();
    }
}

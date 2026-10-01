using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Visualization;

/// <summary>Pluggable instrument visualisation. New instruments implement this.</summary>
public interface IInstrumentRenderer
{
    void Render(DrawingContext dc, InstrumentVisualState state, Rect bounds, VisualTheme theme, InstrumentRenderPlacement placement);
}

public readonly record struct InstrumentRenderPlacement(
    FretboardHorizontalPosition Position,
    double HorizontalOffset,
    double PlacementWidth,
    FretboardHorizontalPosition? SnapPreview);

/// <summary>Colours for the code-drawn surfaces (timeline, fretboard); follows the Light / Dark theme.</summary>
public sealed class VisualTheme
{
    public static bool IsLight { get; set; }
    private static Color P(byte r, byte g, byte b, byte lr, byte lg, byte lb) => IsLight ? Color.FromRgb(lr, lg, lb) : Color.FromRgb(r, g, b);
    public Color Background => P(0x12, 0x15, 0x19, 0xC0, 0xC0, 0xC0);
    public Color RowAlt => P(0x14, 0x17, 0x1B, 0xB6, 0xB6, 0xB6);
    public Color Board => P(0x1B, 0x1F, 0x25, 0xD0, 0xD0, 0xD0);
    public Color BoardEdge => P(0x2E, 0x34, 0x3D, 0x3A, 0x28, 0x1C);
    public Color Wood => P(0x24, 0x22, 0x20, 0x5C, 0x3F, 0x2C);
    public Color String => P(0x8A, 0x93, 0xA0, 0xD8, 0xD3, 0xC6);
    public Color Fret => P(0x55, 0x5C, 0x66, 0xE6, 0xDF, 0xCC);
    public Color Nut => P(0xC8, 0xCE, 0xD8, 0xEF, 0xE8, 0xD6);
    public Color Text => P(0xE7, 0xEA, 0xEF, 0x1E, 0x24, 0x2B);
    public Color Muted => P(0x8B, 0x93, 0x9F, 0x5B, 0x66, 0x73);
    public Color Accent => P(0x4C, 0x9A, 0xFF, 0x1F, 0x6F, 0xD6);
    public Color Current => P(0x3F, 0xB9, 0x50, 0x2A, 0x9A, 0x3F);
    public Color Next => P(0xF2, 0xC1, 0x4E, 0xC4, 0x8A, 0x10);
    public Color Past => P(0x6B, 0x72, 0x7C, 0x9A, 0xA2, 0xAC);
    // Real fretboards stay wood-coloured: the light theme gets a rosewood neck with silver frets.
    public Color FretWire => P(0x77, 0x80, 0x8C, 0xB9, 0xBF, 0xC7);
    public Color Scale => P(0x27, 0x3A, 0x4D, 0x5E, 0x7F, 0xA8);
}

public static class Draw
{
    /// <summary>
    /// Display version of a user/imported colour (track and section colours): very bright, saturated
    /// values (pure reference reds/yellows/greens) are toned down and pure black is lifted slightly, so blocks
    /// stay readable and calm on both themes. The stored colour is unchanged.
    /// </summary>
    public static Color Tame(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2; var d = max - min;
        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        var nl = Math.Clamp(l, 0.16, 0.50);
        var ns = Math.Min(s, 0.68);
        if (Math.Abs(nl - l) < 0.001 && Math.Abs(ns - s) < 0.001) return c;
        var cc = (1 - Math.Abs(2 * nl - 1)) * ns; var x = cc * (1 - Math.Abs(h / 60 % 2 - 1)); var m = nl - cc / 2;
        var (rr, gg, bb) = h < 60 ? (cc, x, 0.0) : h < 120 ? (x, cc, 0.0) : h < 180 ? (0.0, cc, x) : h < 240 ? (0.0, x, cc) : h < 300 ? (x, 0.0, cc) : (cc, 0.0, x);
        return Color.FromArgb(c.A, (byte)Math.Round((rr + m) * 255), (byte)Math.Round((gg + m) * 255), (byte)Math.Round((bb + m) * 255));
    }

    public static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private const int OpacitySteps = 1024;
    private const int ThicknessStepsPerDip = 100;
    private static readonly FrozenResourceCache<SolidKey, SolidColorBrush> SolidCache = new(768, CreateSolid);
    private static readonly FrozenResourceCache<PenKey, Pen> PenCache = new(4096, CreatePen);
    private static readonly FrozenResourceCache<DashedPenKey, Pen> DashedPenCache = new(128, CreateDashedPen);
    private static readonly FrozenResourceCache<PenKey, Pen> RoundPenCache = new(512, CreateRoundPen);
    private static readonly FrozenResourceCache<LinearGradientKey, LinearGradientBrush> LinearGradientCache = new(64, CreateLinearGradient);
    private static readonly FrozenResourceCache<RadialGradientKey, RadialGradientBrush> RadialGradientCache = new(128, CreateRadialGradient);

    /// <summary>
    /// Device pixels per DIP (1.0 = 96 DPI) that text is shaped for right now: the value of the innermost <see cref="UseDpi(Visual)"/>
    /// scope on this thread, else 1.0. A-03: each control opens a scope with its own <c>VisualTreeHelper.GetDpi(this)</c> while it draws,
    /// so windows on monitors with different scaling each get their own (it used to be one static, last writer wins).
    /// </summary>
    public static double PixelsPerDip => t_pixelsPerDip > 0 ? t_pixelsPerDip : 1.0;
    [ThreadStatic] private static double t_pixelsPerDip;
    /// <summary><see cref="PixelsPerDip"/> as a cache key: recorded drawings with text are keyed by it (replaces the old DpiVersion).</summary>
    public static int DpiKey => (int)Math.Round(PixelsPerDip * 1000);

    /// <summary>Text drawn until the scope is disposed is shaped for <paramref name="visual"/>'s display DPI.</summary>
    public static DpiScope UseDpi(Visual visual)
    {
        double value;
        try { value = VisualTreeHelper.GetDpi(visual).PixelsPerDip; }
        catch (InvalidOperationException) { value = 1.0; }
        return UseDpi(value);
    }

    public static DpiScope UseDpi(double pixelsPerDip) =>
        new(double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? Math.Round(pixelsPerDip, 3) : 1.0);

    /// <summary>Restores the enclosing DPI when disposed (scopes nest; a default instance restores "no scope").</summary>
    public readonly struct DpiScope : IDisposable
    {
        private readonly double _previous;
        internal DpiScope(double value) { _previous = t_pixelsPerDip; t_pixelsPerDip = value; }
        public void Dispose() => t_pixelsPerDip = _previous;
    }

    // Shaping text is the most expensive thing these renderers do per frame (fret numbers, bar numbers,
    // section titles). The same few hundred strings repeat, so shaped text is reused. Callers never
    // mutate the returned object; brushes come from the frozen caches, so reference equality is exact.
    // The DPI is part of the key: controls on different monitors each get text shaped for their own.
    private static readonly object TextCacheGate = new();
    private static readonly Dictionary<(string, int, Brush, bool, int), FormattedText> TextCache = new();
    private const int TextCacheLimit = 2048;

    public static FormattedText Text(string text, double size, Brush brush, bool bold = false)
    {
        var pixelsPerDip = PixelsPerDip;
        var key = (text, (int)Math.Round(size * 8), brush, bold, (int)Math.Round(pixelsPerDip * 1000));
        lock (TextCacheGate)
        {
            if (TextCache.TryGetValue(key, out var cached)) return cached;
            var created = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                bold ? Bold : Face, size, brush, pixelsPerDip);
            if (TextCache.Count >= TextCacheLimit) TextCache.Clear();
            TextCache[key] = created;
            return created;
        }
    }

    public static void Centered(DrawingContext dc, string text, double cx, double y, double size, Brush brush, bool bold = false)
    {
        var ft = Text(text, size, brush, bold);
        DrawText(dc, ft, new Point(cx - ft.Width / 2, y));
    }

    public static void At(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool bold = false)
        => DrawText(dc, Text(text, size, brush, bold), new Point(x, y));

    // DrawingContext.DrawText re-runs line formatting and creates fresh native DirectWrite objects on
    // every call (measured: their finalisation alone was ~12% of CPU while dragging). Each cached
    // FormattedText is recorded once into a frozen drawing of its glyphs and replayed from then on:
    // the same glyphs, but no per-frame formatting and no native churn.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FormattedText, Drawing> TextDrawings = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FormattedText, Pen> TextOutlines = new();

    /// <summary>Draws this text with an outline (a stroke behind the glyphs), recorded once with the text.</summary>
    public static void SetOutline(FormattedText text, Pen pen) => TextOutlines.AddOrUpdate(text, pen);

    public static void DrawText(DrawingContext dc, FormattedText text, Point origin)
    {
        if (!TextDrawings.TryGetValue(text, out var drawing))
        {
            var group = new DrawingGroup();
            using (var recorder = group.Open())
            {
                if (TextOutlines.TryGetValue(text, out var outline))
                    recorder.DrawGeometry(null, outline, text.BuildGeometry(new Point(0, 0)));
                recorder.DrawText(text, new Point(0, 0));
            }
            group.Freeze();
            drawing = group;
            TextDrawings.AddOrUpdate(text, drawing);
        }
        if (origin.X == 0 && origin.Y == 0) { dc.DrawDrawing(drawing); return; }
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.DrawDrawing(drawing);
        dc.Pop();
    }

    public static Brush Solid(Color c) => Solid(c, 1);

    public static Brush Solid(Color c, double opacity)
    {
        var key = new SolidKey(Argb(c), QuantizeOpacity(opacity));
        return SolidCache.Get(key);
    }

    public static Pen Pen(Color c, double thickness) => Pen(c, thickness, 1);

    public static Pen Pen(Color c, double thickness, double opacity)
    {
        var key = new PenKey(Argb(c), QuantizeThickness(thickness), QuantizeOpacity(opacity));
        return PenCache.Get(key);
    }

    public static Pen Pen(Brush brush, double thickness)
        => brush is SolidColorBrush solid ? Pen(solid.Color, thickness, solid.Opacity) : CreateFrozenPen(brush, thickness);

    public static Pen RoundPen(Brush brush, double thickness)
        => brush is SolidColorBrush solid
            ? RoundPenCache.Get(new PenKey(Argb(solid.Color), QuantizeThickness(thickness), QuantizeOpacity(solid.Opacity)))
            : CreateFrozenPen(brush, thickness, PenLineCap.Round);

    public static Pen DashedPen(Color color, double thickness, double dashLength, double gapLength)
        => DashedPenCache.Get(new DashedPenKey(Argb(color), QuantizeThickness(thickness),
            QuantizeThickness(dashLength), QuantizeThickness(gapLength)));

    public static LinearGradientBrush LinearGradient(Color start, Color end, Point startPoint, Point endPoint)
    {
        var key = new LinearGradientKey(Argb(start), Argb(end), QuantizeCoordinate(startPoint.X),
            QuantizeCoordinate(startPoint.Y), QuantizeCoordinate(endPoint.X), QuantizeCoordinate(endPoint.Y));
        return LinearGradientCache.Get(key);
    }

    public static RadialGradientBrush RadialGlow(Color color, double intensity)
    {
        var alpha = (byte)Math.Clamp((int)(100 * (double.IsFinite(intensity) ? intensity : 0)), 0, 255);
        return RadialGradientCache.Get(new RadialGradientKey(Argb(color), alpha));
    }

    private static SolidColorBrush CreateSolid(SolidKey key)
    {
        var brush = new SolidColorBrush(ColorFromArgb(key.Color))
        {
            Opacity = key.Opacity / (double)OpacitySteps
        };
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(PenKey key)
    {
        var pen = new Pen(Solid(ColorFromArgb(key.Color), key.Opacity / (double)OpacitySteps),
            key.Thickness / (double)ThicknessStepsPerDip);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateDashedPen(DashedPenKey key)
    {
        var thickness = key.Thickness / (double)ThicknessStepsPerDip;
        var dash = new DashStyle(new[]
        {
            key.DashLength / (double)ThicknessStepsPerDip,
            key.GapLength / (double)ThicknessStepsPerDip
        }, 0);
        dash.Freeze();
        var pen = new Pen(Solid(ColorFromArgb(key.Color)), thickness)
        {
            DashStyle = dash,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        pen.Freeze();
        return pen;
    }

    private static Pen CreateRoundPen(PenKey key)
    {
        var pen = new Pen(Solid(ColorFromArgb(key.Color), key.Opacity / (double)OpacitySteps),
            key.Thickness / (double)ThicknessStepsPerDip)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness, PenLineCap cap = PenLineCap.Flat)
    {
        var pen = new Pen(brush, Math.Clamp(double.IsFinite(thickness) ? thickness : 1, 0, 1024))
        {
            StartLineCap = cap,
            EndLineCap = cap
        };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static LinearGradientBrush CreateLinearGradient(LinearGradientKey key)
    {
        var brush = new LinearGradientBrush(ColorFromArgb(key.StartColor), ColorFromArgb(key.EndColor),
            new Point(key.StartX / 1024.0, key.StartY / 1024.0),
            new Point(key.EndX / 1024.0, key.EndY / 1024.0));
        brush.Freeze();
        return brush;
    }

    private static RadialGradientBrush CreateRadialGradient(RadialGradientKey key)
    {
        var color = ColorFromArgb(key.Color);
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(key.Alpha, color.R, color.G, color.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        return brush;
    }

    private static int QuantizeOpacity(double opacity)
        => (int)Math.Round(Math.Clamp(double.IsFinite(opacity) ? opacity : 1, 0, 1) * OpacitySteps);

    private static int QuantizeThickness(double thickness)
        => (int)Math.Round(Math.Clamp(double.IsFinite(thickness) ? thickness : 1, 0, 1024) * ThicknessStepsPerDip);

    private static int QuantizeCoordinate(double coordinate)
        => (int)Math.Round(Math.Clamp(double.IsFinite(coordinate) ? coordinate : 0, -10, 10) * 1024);

    private static uint Argb(Color color)
        => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    private static Color ColorFromArgb(uint color)
        => Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);

    private readonly record struct SolidKey(uint Color, int Opacity);
    private readonly record struct PenKey(uint Color, int Thickness, int Opacity);
    private readonly record struct DashedPenKey(uint Color, int Thickness, int DashLength, int GapLength);
    private readonly record struct LinearGradientKey(uint StartColor, uint EndColor,
        int StartX, int StartY, int EndX, int EndY);
    private readonly record struct RadialGradientKey(uint Color, byte Alpha);

    /// <summary>Small bounded, lock-protected cache for immutable WPF drawing resources.</summary>
    private sealed class FrozenResourceCache<TKey, TValue> where TKey : notnull where TValue : Freezable
    {
        private readonly object _gate = new();
        private readonly Dictionary<TKey, TValue> _items = new();
        private readonly Queue<TKey> _insertionOrder = new();
        private readonly int _capacity;
        private readonly Func<TKey, TValue> _factory;

        public FrozenResourceCache(int capacity, Func<TKey, TValue> factory)
        {
            _capacity = capacity;
            _factory = factory;
        }

        public TValue Get(TKey key)
        {
            lock (_gate)
            {
                if (_items.TryGetValue(key, out var cached)) return cached;
                var resource = _factory(key);
                if (!resource.IsFrozen) resource.Freeze();
                if (_items.Count >= _capacity)
                    _items.Remove(_insertionOrder.Dequeue());
                _items.Add(key, resource);
                _insertionOrder.Enqueue(key);
                return resource;
            }
        }
    }
}

/// <summary>Guitar / bass fretboard with live current/next/upcoming note markers.</summary>
public sealed class FretboardRenderer : IInstrumentRenderer
{
    /// <summary>
    /// score-following layout: no movement line between beats (markers keep the TabForge look).
    /// </summary>
    public static bool Gp5Style { get; set; }

    private static readonly int[] InlayFrets = { 3, 5, 7, 9, 12, 15, 17, 19, 21, 24 };
    private object? _staticKey;
    private Drawing? _staticBoard;

    internal static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(a.A, (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Pitch class of a scale highlight's root ("E Natural Minor" -> 4), or -1.</summary>
    internal static int ScaleRoot(string? scaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleName)) return -1;
        return Array.IndexOf(MusicTheoryService.NoteNames, scaleName.Split(' ')[0]);
    }

    private static int SequenceHash(IEnumerable<int> values)
    {
        var hash = 17;
        foreach (var v in values) hash = unchecked(hash * 31 + v);
        return hash;
    }

    public void Render(DrawingContext dc, InstrumentVisualState state, Rect bounds, VisualTheme theme, InstrumentRenderPlacement placement)
    {
        var layout = FretboardGeometry.Compute(state, bounds, placement.Position, placement.HorizontalOffset, placement.PlacementWidth);
        var strings = layout.Strings;
        var frets = Math.Max(12, state.FretCount);
        var boardRect = layout.Board;
        var firstFret = layout.FirstFret;
        var lastFret = layout.LastFret;
        var fretWidth = layout.FretWidth;
        var stringGap = layout.StringGap;

        double StringY(int index) => FretboardGeometry.StringY(layout, index);
        double FretX(double fret) => FretboardGeometry.FretX(layout, fret);
        var scaleRoot = ScaleRoot(state.ScaleName);

        // The neck (board, scale band, frets, inlays, strings, labels) only changes with size, tuning,
        // scale, labels or theme, so it is recorded once and replayed; per beat only markers are drawn.
        var staticKey = (bounds, placement.Position, placement.HorizontalOffset, placement.PlacementWidth, strings, frets,
            firstFret, lastFret, state.ShowStringLabels, state.ShowNoteNames, state.LeftHanded, SequenceHash(state.Tuning),
            SequenceHash(state.ScalePitchClasses), theme, VisualTheme.IsLight, Draw.DpiKey,
            (state.ScaleStyle, state.ScaleColour, state.MarkerColour, state.MarkerBrightness, state.ScaleName, state.NumberScale, state.StringSpacing, state.ScaleStrength));
        if (!Equals(_staticKey, staticKey) || _staticBoard is null)
        {
            var group = new DrawingGroup();
            using (var boardDc = group.Open()) DrawNeck(boardDc);
            group.Freeze();
            _staticBoard = group;
            _staticKey = staticKey;
        }
        dc.DrawDrawing(_staticBoard);

        void DrawNeck(DrawingContext dc)
        {
            // Board
            dc.DrawRoundedRectangle(Draw.Solid(theme.Wood), Draw.Pen(theme.BoardEdge, 1), boardRect, 3, 3);

            // Scale highlight band behind everything.
            if (state.ScalePitchClasses.Count > 0)
            {
                // Shaded cells sit behind the frets; circles and rings are drawn over the strings (below).
                if (state.ScaleStyle == ScaleHighlightStyles.Shaded)
                    for (var s = 0; s < strings; s++)
                    {
                        var openMidi = state.Tuning[s];
                        for (var f = firstFret; f <= lastFret; f++)
                        {
                            var pc = ((openMidi + f) % 12 + 12) % 12;
                            if (!state.ScalePitchClasses.Contains(pc)) continue;
                            var x = FretX(f) - fretWidth / 2;
                            var fill = Draw.Solid(state.ScaleColour, ScaleHighlightStyles.ScaleAlpha(pc == scaleRoot ? 0.5 : 0.28, state.ScaleStrength, pc == scaleRoot));
                            dc.DrawRectangle(fill, null, new Rect(x, StringY(s) - stringGap / 2, fretWidth, stringGap));
                        }
                    }
            }

            // Frets
            for (var f = firstFret; f <= lastFret + 1; f++)
            {
                var x = FretX(f - 0.5);
                var isNut = f == 1;
                dc.DrawLine(isNut ? Draw.Pen(theme.Nut, 5) : Draw.Pen(theme.FretWire, 1.6), new Point(x, boardRect.Top), new Point(x, boardRect.Bottom));
            }

            // Inlays: the theme's dot colour (or the chosen one), lifted towards white by the brightness setting.
            var markerBase = state.MarkerColour ?? theme.Fret;
            var lift = state.MarkerColour is null ? state.MarkerBrightness * 0.55 : state.MarkerBrightness * 0.25;
            var markerColour = VisualTheme.IsLight && state.MarkerColour is null
                ? Blend(markerBase, Colors.Black, state.MarkerBrightness * 0.35)
                : Blend(markerBase, Colors.White, lift);
            var markerBrush = Draw.Solid(markerColour, Math.Min(1, 0.75 + state.MarkerBrightness * 0.25));
            foreach (var f in InlayFrets)
            {
                if (f < firstFret || f > lastFret) continue;
                var x = FretX(f);
                var y = boardRect.Top + boardRect.Height / 2;
                if (f == 12 || f == 24)
                {
                    dc.DrawEllipse(markerBrush, null, new Point(x, y - stringGap / 2), 3.4, 3.4);
                    dc.DrawEllipse(markerBrush, null, new Point(x, y + stringGap / 2), 3.4, 3.4);
                }
                else dc.DrawEllipse(markerBrush, null, new Point(x, y), 3.4, 3.4);
            }

            // Strings
            for (var s = 0; s < strings; s++)
            {
                var y = StringY(s);
                var thickness = 0.8 + (strings - 1 - s) * 0.22;
                dc.DrawLine(Draw.Pen(theme.String, thickness), new Point(boardRect.Left, y), new Point(boardRect.Right, y));
            }

            // Scale notes as circles (filled) or rings, on top of the strings; the root is stronger.
            if (state.ScalePitchClasses.Count > 0 && state.ScaleStyle != ScaleHighlightStyles.Shaded)
            {
                var radius = Math.Max(3, Math.Min(stringGap, fretWidth) * 0.3);
                for (var s = 0; s < strings; s++)
                {
                    var openMidi = state.Tuning[s];
                    for (var f = firstFret; f <= lastFret; f++)
                    {
                        var pc = ((openMidi + f) % 12 + 12) % 12;
                        if (!state.ScalePitchClasses.Contains(pc)) continue;
                        var centre = new Point(FretX(f), StringY(s));
                        var root = pc == scaleRoot;
                        var k = state.ScaleStrength;
                        if (state.ScaleStyle == ScaleHighlightStyles.Rings)
                            dc.DrawEllipse(root ? Draw.Solid(state.ScaleColour, ScaleHighlightStyles.ScaleAlpha(0.35, k, true)) : null,
                                Draw.Pen(state.ScaleColour, (root ? 2.2 : 1.5) * Math.Max(1, k), Math.Min(1, k)), centre, radius, radius);
                        else
                            dc.DrawEllipse(Draw.Solid(state.ScaleColour, ScaleHighlightStyles.ScaleAlpha(root ? 0.95 : 0.6, k, root)),
                                root ? Draw.Pen(Blend(state.ScaleColour, Colors.White, 0.5), 1.4, Math.Min(1, k)) : null, centre, radius, radius);
                    }
                }
            }

            // Fret numbers
            for (var f = firstFret; f <= lastFret; f++)
            {
                if (f % 2 != 0 && f != firstFret && lastFret - firstFret > 6) continue;
                Draw.Centered(dc, f.ToString(), FretX(f), boardRect.Bottom + 5, 10 * state.NumberScale, Draw.Solid(theme.Muted));
            }

            // String labels (note names, high string first)
            if (state.ShowStringLabels)
            {
                for (var s = 0; s < strings; s++)
                {
                    var y = StringY(s) - 7 * state.NumberScale;
                    var label = state.ShowNoteNames && s < state.Tuning.Count
                        ? MusicTheoryService.NoteName(state.Tuning[s])
                        : (s + 1).ToString();
                    Draw.At(dc, label, bounds.X + 12, y, 11 * state.NumberScale, Draw.Solid(theme.Muted));
                }
            }
        }

        // Movement path: exactly one segment, from the sounding note(s) to the next note(s), using the
        // chord centroid for each. Previously it chained every upcoming note, which read as a glowing
        // trajectory that did not correspond to anything being played.
        // Drawn only when the hand actually moves: the next beat's fret positions differ from what is
        // sounding now. A repeated chord (same strings and frets) gets no line. Released markers only
        // stand in for "current" when nothing is sounding, so a lingering note can't skew the path.
        var currentCount = 0;
        var nextCount = 0;
        var currentX = 0.0;
        var currentY = 0.0;
        var nextX = 0.0;
        var nextY = 0.0;
        var anyHeld = state.Notes.Any(n => n.Role == VisualRole.Current && !n.Released);
        var currentShape = new HashSet<(int, int)>();
        var nextShape = new HashSet<(int, int)>();
        foreach (var note in state.Notes)
        {
            if (note.Role == VisualRole.Current && (!anyHeld || !note.Released))
            {
                currentShape.Add((note.StringIndex, note.Fret));
                currentCount++;
                currentX += note.Fret == 0 ? boardRect.Left - 14 : FretX(note.Fret);
                currentY += StringY(note.StringIndex);
            }
            else if (note.Role == VisualRole.Next)
            {
                nextShape.Add((note.StringIndex, note.Fret));
                nextCount++;
                nextX += note.Fret == 0 ? boardRect.Left - 14 : FretX(note.Fret);
                nextY += StringY(note.StringIndex);
            }
        }
        if (currentCount > 0 && nextCount > 0 && !Gp5Style && !currentShape.SetEquals(nextShape))
        {
            var from = new Point(currentX / currentCount, currentY / currentCount);
            var to = new Point(nextX / nextCount, nextY / nextCount);
            var midY = (from.Y + to.Y) / 2 + (to.Y - from.Y) * 0.15;
            var fig = new PathFigure { StartPoint = from, IsClosed = false };
            fig.Segments.Add(new BezierSegment(new Point(from.X, midY), new Point(to.X, midY), to, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            dc.DrawGeometry(null, Draw.Pen(theme.Next, 1.8, 0.75), geo);
        }

        // Notes
        Point WhereIs(VisualNote n) => new(n.Fret == 0 ? boardRect.Left - 14 : FretX(n.Fret), StringY(n.StringIndex));
        foreach (var note in state.Notes)
        {
            if (note.Fret < firstFret - 1 || note.Fret > lastFret + 1) continue;
            var x = note.Fret == 0 ? boardRect.Left - 14 : FretX(note.Fret);
            var y = StringY(note.StringIndex);
            RenderMarker(dc, note, x, y, theme, fretWidth, state.Pulse, state.ShowNoteNames, state.NumberScale, bounds.Y, bounds.X + 34, state.Notes, WhereIs);
        }

        if (placement.SnapPreview is { } snapPreview)
            DrawSnapPreview(dc, state, bounds, theme, placement.PlacementWidth, snapPreview);

        // (legend is drawn by the hosting panel header)
    }

    private static void DrawSnapPreview(
        DrawingContext dc,
        InstrumentVisualState state,
        Rect bounds,
        VisualTheme theme,
        double placementWidth,
        FretboardHorizontalPosition target)
    {
        var targetBoard = FretboardGeometry.Compute(state, bounds, target, 0, placementWidth).Board;
        var left = Math.Max(0, targetBoard.Left - FretboardGeometry.LeftGutter);
        var right = Math.Min(placementWidth, targetBoard.Right + FretboardGeometry.LegendWidth);
        if (right <= left) return;

        var landing = new Rect(left + 2, bounds.Y + 2, right - left - 4, Math.Max(1, bounds.Height - 4));
        dc.DrawRoundedRectangle(Draw.Solid(theme.Accent, 0.07), null, landing, 9, 9);
        dc.DrawRoundedRectangle(null, Draw.Pen(theme.Accent, 14, 0.10), landing, 9, 9);
        dc.DrawRoundedRectangle(null, Draw.Pen(theme.Accent, 6, 0.24), landing, 9, 9);
        dc.DrawRoundedRectangle(null, Draw.Pen(theme.Accent, 1.8, 0.95), landing, 9, 9);

        var badge = new Rect(left + 3, landing.Bottom - 20, 48, 16);
        dc.DrawRoundedRectangle(Draw.Solid(theme.Background, 0.92), Draw.Pen(theme.Accent, 1, 0.9), badge, 4, 4);
        Draw.Centered(dc, target.ToString().ToUpperInvariant(), badge.X + badge.Width / 2, badge.Y + 3,
            8, Draw.Solid(theme.Text), bold: true);
    }

    private static readonly string[] MarkerNoteNames = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

    /// <summary>True when the tag pill would cover another marker's bubble (cheap box test over the notes; no allocation).</summary>
    private static bool PillHitsOther(Rect pill, VisualNote self, IReadOnlyList<VisualNote>? others, Func<VisualNote, Point>? whereIs, double reach)
    {
        if (others is null || whereIs is null) return false;
        var r = Math.Min(16, Math.Max(8, reach));
        for (var i = 0; i < others.Count; i++)
        {
            var o = others[i];
            if (ReferenceEquals(o, self)) continue;
            var p = whereIs(o);
            if (p.X + r > pill.Left && p.X - r < pill.Right && p.Y + r > pill.Top && p.Y - r < pill.Bottom) return true;
        }
        return false;
    }

    private static void RenderMarker(DrawingContext dc, VisualNote note, double x, double y, VisualTheme theme, double fretWidth, double pulse,
        bool showNoteNames = false, double numberScale = 1, double topLimit = double.NegativeInfinity, double leftLimit = double.NegativeInfinity,
        IReadOnlyList<VisualNote>? others = null, Func<VisualNote, Point>? whereIs = null)
    {
        _ = pulse;
        var color = note.Role switch
        {
            VisualRole.Current => note.Released ? theme.Past : theme.Current,
            VisualRole.Selected => theme.Accent,
            VisualRole.Next => theme.Next,
            VisualRole.Past => theme.Past,
            _ => theme.Accent
        };
        var emphasis = Math.Clamp(note.Role == VisualRole.Current ? (note.Released ? 0.6 : 1.0) : note.Emphasis, 0.12, 1.0);
        var radius = note.Role is VisualRole.Current or VisualRole.Selected ? Math.Min(16, fretWidth * 0.44)
            : note.Role == VisualRole.Next ? Math.Min(13, fretWidth * 0.36)
            : Math.Min(12, fretWidth * 0.32);
        radius *= numberScale;   // the bubble scales with its number so the number always fits

        // Sounding note: a tight halo. Nothing else glows, so a marker can only look "on" while it is
        // actually sounding (the next/recent markers are outlines).
        if (note.Role == VisualRole.Current && !note.Released)
            dc.DrawEllipse(Draw.Solid(color, 0.20), null, new Point(x, y), radius + 4, radius + 4);

        if (note.Dead)
        {
            dc.DrawEllipse(null, Draw.Pen(color, 1.8, emphasis), new Point(x, y), radius, radius);
            dc.DrawLine(Draw.Pen(color, 1.6, emphasis), new Point(x - radius * 0.7, y - radius * 0.7), new Point(x + radius * 0.7, y + radius * 0.7));
            dc.DrawLine(Draw.Pen(color, 1.6, emphasis), new Point(x - radius * 0.7, y + radius * 0.7), new Point(x + radius * 0.7, y - radius * 0.7));
        }
        else if (note.Role == VisualRole.Next)
        {
            // The next note is an outline, never a filled/glowing note.
            dc.DrawEllipse(Draw.Solid(color, 0.12), Draw.Pen(color, 1.8, 0.6), new Point(x, y), radius, radius);
        }
        else if (note.Released)
        {
            // Position indicator only (nothing is sounding): a faint hollow ring.
            dc.DrawEllipse(null, Draw.Pen(color, 1.4, 0.7), new Point(x, y), radius, radius);
        }
        else if (note.Role is VisualRole.Upcoming or VisualRole.Past)
        {
            dc.DrawEllipse(Draw.Solid(color, Math.Max(0.14, emphasis * 0.5)), Draw.Pen(color, 1.1, Math.Max(0.25, emphasis)), new Point(x, y), radius, radius);
        }
        else
        {
            dc.DrawEllipse(Draw.Solid(color, emphasis), Draw.Pen(theme.Background, 1.2), new Point(x, y), radius, radius);
        }

        // Always label the marker: a fret number you can actually read at a glance.
        // Note names mode labels each marker with its pitch name instead of the fret number.
        var label = showNoteNames ? (note.Dead ? "x" : MarkerNoteNames[((note.Midi % 12) + 12) % 12])
            : note.Fret == 0 ? "0" : note.Fret.ToString();
        var textColor = note.Role is VisualRole.Current or VisualRole.Selected or VisualRole.Next ? theme.Background
            : emphasis > 0.55 ? theme.Text : theme.Muted;
        var size = numberScale * note.Role switch
        {
            VisualRole.Current => 13,
            VisualRole.Selected => 13,
            VisualRole.Next => 12,
            VisualRole.Past => 11,
            _ => 10
        };
        Draw.Centered(dc, label, x, y - size / 2 - 1, size, Draw.Solid(textColor, Math.Max(0.45, emphasis)), note.Role is VisualRole.Current or VisualRole.Selected);

        // Technique tag (TAP, H/P, BEND…): a small pill above the sounding, selected or next note only,
        // so upcoming/recent markers don't clutter the neck.
        if (note.Technique is { Length: > 0 } tag && note.Role is VisualRole.Current or VisualRole.Selected or VisualRole.Next && !note.Released)
        {
            var pillWidth = Math.Max(22, tag.Length * 6.2 + 10) * numberScale;
            var pill = new Rect(x - pillWidth / 2, y - radius - 17 * numberScale, pillWidth, 13 * numberScale);
            // No room above the marker (top string) or another marker / bubble is in the way: sit beside the marker instead,
            // on the left when there is room (left of the nut), otherwise on the right.
            if (pill.Y < topLimit || PillHitsOther(pill, note, others, whereIs, fretWidth * 0.44 * numberScale))
            {
                var left = new Rect(x - radius - 3 - pillWidth, y - 6.5 * numberScale, pillWidth, 13 * numberScale);
                var right = new Rect(x + radius + 3, left.Y, pillWidth, left.Height);
                pill = left.X >= leftLimit && !PillHitsOther(left, note, others, whereIs, fretWidth * 0.44 * numberScale) ? left : right;
            }
            var alpha = note.Role == VisualRole.Next ? 0.7 : 1.0;
            dc.DrawRoundedRectangle(Draw.Solid(theme.Background, 0.88 * alpha), Draw.Pen(color, 1, 0.9 * alpha), pill, 6.5 * numberScale, 6.5 * numberScale);
            Draw.Centered(dc, tag, pill.X + pill.Width / 2, pill.Y + 1.5 * numberScale, 8.5 * numberScale, Draw.Solid(color, alpha), bold: true);
        }
    }
}

/// <summary>Wide illustrated drum kit with live General MIDI percussion highlighting.</summary>
public sealed class DrumRenderer : IInstrumentRenderer
{
    private enum PieceKind { Cymbal, Drum, Kick, Auxiliary }

    private sealed record KitPiece(string Name, int[] MidiNotes, double X, double Y, double RadiusX, double RadiusY, PieceKind Kind);

    // GM percussion key map. Several pitches intentionally resolve to one physical surface:
    // e.g. acoustic/electric snare, closed/pedal/open hi-hat and the alternate crash cymbals.
    private static readonly KitPiece[] Kit =
    {
        new("Crash", new[] { 49, 52, 55, 57, 58 }, 0.105, 0.20, 0.105, 0.205, PieceKind.Cymbal),
        new("Hi-hat", new[] { 42, 44, 46 }, 0.145, 0.68, 0.080, 0.135, PieceKind.Cymbal),
        new("Ride", new[] { 51, 53, 59 }, 0.840, 0.19, 0.105, 0.180, PieceKind.Cymbal),
        new("Crash", new[] { 49, 52, 55, 57, 58 }, 0.925, 0.43, 0.075, 0.135, PieceKind.Cymbal),
        new("High tom", new[] { 50 }, 0.405, 0.37, 0.070, 0.145, PieceKind.Drum),
        new("Mid tom", new[] { 47, 48 }, 0.525, 0.36, 0.073, 0.150, PieceKind.Drum),
        new("Floor tom", new[] { 41, 43, 45 }, 0.685, 0.61, 0.085, 0.170, PieceKind.Drum),
        new("Snare", new[] { 37, 38, 39, 40 }, 0.330, 0.64, 0.075, 0.155, PieceKind.Drum),
        new("Kick", new[] { 35, 36 }, 0.515, 0.70, 0.120, 0.255, PieceKind.Kick),
        new("Aux", new[] { 54, 56, 58, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87 }, 0.775, 0.48, 0.030, 0.070, PieceKind.Auxiliary)
    };
    private static readonly double[] StandDirections = { -1.0, 1.0 };
    private static readonly double[] CymbalRings = { 0.42, 0.68, 0.86 };

    public void Render(DrawingContext dc, InstrumentVisualState state, Rect bounds, VisualTheme theme, InstrumentRenderPlacement placement)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 0 || height <= 0) return;

        // Floor and soft stage shadow ground the kit without adding a panel behind the transparent artwork.
        dc.DrawEllipse(Draw.Solid(Colors.Black, 0.42), null,
            new Point(bounds.X + width * 0.51, bounds.Y + height * 0.91), width * 0.40, height * 0.075);
        dc.DrawLine(Draw.Pen(theme.BoardEdge, 1, 0.5),
            new Point(bounds.X + width * 0.08, bounds.Y + height * 0.94),
            new Point(bounds.X + width * 0.94, bounds.Y + height * 0.94));

        // Stands are behind the drums and cymbals.
        Stand(dc, bounds, 0.105, 0.20, theme);
        Stand(dc, bounds, 0.145, 0.68, theme);
        Stand(dc, bounds, 0.840, 0.19, theme);
        Stand(dc, bounds, 0.925, 0.43, theme);
        Stand(dc, bounds, 0.330, 0.64, theme);

        // Cymbals: gold, flattened perspective discs with a raised bell and engraved grooves.
        DrawCymbal(dc, bounds, 0.105, 0.20, 0.105, 0.205, theme, false, 0);
        DrawCymbal(dc, bounds, 0.145, 0.68, 0.080, 0.135, theme, false, 0);
        DrawCymbal(dc, bounds, 0.840, 0.19, 0.105, 0.180, theme, false, 0);
        DrawCymbal(dc, bounds, 0.925, 0.43, 0.075, 0.135, theme, false, 0);

        // Rear rack toms, snare and floor tom.
        DrawDrum(dc, bounds, 0.405, 0.37, 0.070, 0.145, Color.FromRgb(0x12, 0x6D, 0xB8), theme, false, 0);
        DrawDrum(dc, bounds, 0.525, 0.36, 0.073, 0.150, Color.FromRgb(0x12, 0x6D, 0xB8), theme, false, 0);
        DrawDrum(dc, bounds, 0.685, 0.61, 0.085, 0.170, Color.FromRgb(0x13, 0x65, 0xA9), theme, false, 0);
        DrawDrum(dc, bounds, 0.330, 0.64, 0.075, 0.155, Color.FromRgb(0xBD, 0x22, 0x20), theme, false, 0);
        DrawDrum(dc, bounds, 0.515, 0.70, 0.120, 0.255, Color.FromRgb(0x12, 0x69, 0xB1), theme, true, 0);
        DrawAuxiliary(dc, bounds, theme);

        foreach (var piece in Kit)
        {
            var sounding = false;
            var selected = false;
            var next = false;
            var upcoming = 0.0;
            foreach (var note in state.Notes)
            {
                if (!ContainsMidi(piece.MidiNotes, note.Midi)) continue;
                if (note.Role == VisualRole.Current && note.Held && !note.Released) sounding = true;
                else if (note.Role == VisualRole.Selected) selected = true;
                else if (note.Role == VisualRole.Next) next = true;
                else if (note.Role == VisualRole.Upcoming && note.Emphasis > upcoming) upcoming = note.Emphasis;
            }
            var glow = sounding ? 1.0 : selected ? 0.72 : next ? 0.52 : Math.Clamp(upcoming * 0.32, 0, 0.32);
            if (glow <= 0) continue;

            var cx = bounds.X + width * piece.X;
            var cy = bounds.Y + height * piece.Y;
            var rx = width * piece.RadiusX;
            var ry = height * piece.RadiusY;
            var color = sounding ? theme.Current : selected ? theme.Accent : theme.Next;
            if (piece.Kind == PieceKind.Cymbal)
                DrawCymbal(dc, bounds, piece.X, piece.Y, piece.RadiusX, piece.RadiusY, theme, sounding || selected, glow);
            else
            {
                dc.DrawEllipse(Draw.Solid(color, glow * (sounding ? 0.28 : 0.16)), null,
                    new Point(cx, cy), rx * 1.22, ry * 1.20);
                if (piece.Kind != PieceKind.Auxiliary)
                    dc.DrawEllipse(null, Draw.Pen(color, Math.Max(2.2, height * 0.035), glow), new Point(cx, cy), rx * 0.91, ry * 0.70);
                else
                    dc.DrawRoundedRectangle(Draw.Solid(color, 0.78), Draw.Pen(color, 2, glow),
                        new Rect(cx - rx * 1.3, cy - ry * 0.65, rx * 2.6, ry * 1.3), 3, 3);
            }
        }
    }

    private static bool ContainsMidi(int[] midiNotes, int midi)
    {
        for (var index = 0; index < midiNotes.Length; index++)
            if (midiNotes[index] == midi) return true;
        return false;
    }

    private static Point At(Rect bounds, double x, double y) => new(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);

    private static void Stand(DrawingContext dc, Rect bounds, double x, double y, VisualTheme theme)
    {
        var center = At(bounds, x, y);
        var foot = At(bounds, x, 0.91);
        var steel = Color.FromRgb(0xA9, 0xB2, 0xBF);
        dc.DrawLine(Draw.Pen(Color.FromRgb(0x19, 0x20, 0x29), 4), center, foot);
        dc.DrawLine(Draw.Pen(steel, 1.8), center, foot);
        var baseY = bounds.Y + bounds.Height * 0.92;
        foreach (var direction in StandDirections)
        {
            var end = new Point(foot.X + direction * bounds.Width * 0.018, baseY);
            dc.DrawLine(Draw.Pen(Color.FromRgb(0x1B, 0x22, 0x2B), 3.5), foot, end);
            dc.DrawLine(Draw.Pen(steel, 1.2), foot, end);
        }
        dc.DrawEllipse(Draw.Solid(theme.Background), Draw.Pen(steel, 1), center, bounds.Width * 0.004, bounds.Height * 0.025);
    }

    private static void DrawCymbal(DrawingContext dc, Rect bounds, double x, double y, double rx, double ry,
        VisualTheme theme, bool glowing, double intensity)
    {
        var center = At(bounds, x, y);
        var rX = bounds.Width * rx;
        var rY = bounds.Height * ry;
        if (glowing)
        {
            var glowColor = theme.Current;
            var glow = Draw.RadialGlow(glowColor, intensity);
            dc.DrawEllipse(glow, null, center, rX * 1.55, rY * 1.55);
        }
        var gold = Draw.LinearGradient(Color.FromRgb(0xB7, 0x70, 0x0D), Color.FromRgb(0xFF, 0xD8, 0x68), new Point(0.2, 0), new Point(0.8, 1));
        dc.DrawEllipse(Draw.Solid(Color.FromRgb(0x0C, 0x11, 0x18), 0.55), null,
            new Point(center.X, center.Y + rY * 0.20), rX * 0.97, rY * 0.83);
        dc.DrawEllipse(gold, Draw.Pen(Color.FromRgb(0xE2, 0xA8, 0x31), Math.Max(1, bounds.Height * 0.012)), center, rX, rY * 0.78);
        foreach (var ring in CymbalRings)
            dc.DrawEllipse(null, Draw.Pen(Color.FromRgb(0xFF, 0xEF, 0xAF), 0.75, 0.36), center, rX * ring, rY * 0.78 * ring);
        dc.DrawEllipse(Draw.Solid(Color.FromRgb(0xF4, 0xBD, 0x43)), Draw.Pen(Color.FromRgb(0x7D, 0x4A, 0x0B), 1),
            new Point(center.X, center.Y - rY * 0.02), rX * 0.18, rY * 0.22);
        dc.DrawEllipse(Draw.Solid(Color.FromRgb(0x12, 0x18, 0x21)), Draw.Pen(Color.FromRgb(0xB9, 0xC4, 0xD2), 1),
            new Point(center.X, center.Y - rY * 0.03), rX * 0.045, rY * 0.095);
        if (glowing)
            dc.DrawEllipse(null, Draw.Pen(theme.Current, Math.Max(2.4, bounds.Height * 0.026), intensity),
                center, rX * 1.03, rY * 0.81);
    }

    private static void DrawDrum(DrawingContext dc, Rect bounds, double x, double y, double rx, double ry,
        Color shell, VisualTheme theme, bool kick, double unused)
    {
        _ = unused;
        var center = At(bounds, x, y);
        var rX = bounds.Width * rx;
        var rY = bounds.Height * ry;
        var shellTop = kick ? center.Y - rY * 0.34 : center.Y - rY * 0.48;
        var shellHeight = kick ? rY * 1.24 : rY * 0.82;
        var shellRect = new Rect(center.X - rX * 0.93, shellTop, rX * 1.86, shellHeight);
        var shellBrush = Draw.LinearGradient(
            Color.FromRgb((byte)(shell.R * 0.54), (byte)(shell.G * 0.54), (byte)(shell.B * 0.58)),
            shell, new Point(0, 0), new Point(1, 0));
        dc.DrawRoundedRectangle(Draw.Solid(Color.FromRgb(0x08, 0x0C, 0x12), 0.8), null,
            new Rect(shellRect.X - 2, shellRect.Y + shellRect.Height - 3, shellRect.Width + 4, 6), 4, 4);
        dc.DrawRoundedRectangle(shellBrush, Draw.Pen(Color.FromRgb(0x18, 0x22, 0x30), 2), shellRect, 8, 8);
        for (var lug = -1; lug <= 1; lug++)
        {
            var lx = center.X + lug * rX * 0.57;
            dc.DrawLine(Draw.Pen(Color.FromRgb(0xC3, 0xCD, 0xD9), 1.8), new Point(lx, shellTop + 5), new Point(lx, shellTop + shellHeight - 3));
            dc.DrawEllipse(Draw.Solid(Color.FromRgb(0xD3, 0xDB, 0xE4)), null, new Point(lx, shellTop + 6), 2.2, 2.2);
        }
        var headCenter = new Point(center.X, shellTop + 1);
        var headBrush = Draw.LinearGradient(Color.FromRgb(0xF1, 0xF4, 0xFA), Color.FromRgb(0xAE, 0xBB, 0xCC), new Point(0.2, 0), new Point(0.8, 1));
        dc.DrawEllipse(Draw.Solid(Color.FromRgb(0x0A, 0x0E, 0x14)), null, new Point(center.X, headCenter.Y + 2), rX, rY * 0.48);
        dc.DrawEllipse(headBrush, Draw.Pen(Color.FromRgb(0xD9, 0xE0, 0xE8), Math.Max(1.3, bounds.Height * 0.014)), headCenter, rX * 0.96, rY * 0.43);
        dc.DrawEllipse(null, Draw.Pen(Color.FromRgb(0x69, 0x78, 0x89), 0.8, 0.65), headCenter, rX * 0.83, rY * 0.34);
        if (kick)
        {
            dc.DrawEllipse(Draw.Solid(Color.FromRgb(0x16, 0x1C, 0x27), 0.85), Draw.Pen(Color.FromRgb(0x76, 0x88, 0x9B), 1.2),
                new Point(center.X, shellTop + shellHeight * 0.57), rX * 0.53, rY * 0.48);
            dc.DrawEllipse(Draw.Solid(Color.FromRgb(0x08, 0x0D, 0x14)), null,
                new Point(center.X, shellTop + shellHeight * 0.57), rX * 0.35, rY * 0.32);
        }
        else
        {
            var standY = shellTop + shellHeight;
            dc.DrawLine(Draw.Pen(Color.FromRgb(0x9D, 0xA8, 0xB5), 2), new Point(center.X, standY), new Point(center.X, bounds.Y + bounds.Height * 0.92));
        }
    }

    private static void DrawAuxiliary(DrawingContext dc, Rect bounds, VisualTheme theme)
    {
        var p = At(bounds, 0.775, 0.48);
        var rect = new Rect(p.X - bounds.Width * 0.025, p.Y - bounds.Height * 0.045, bounds.Width * 0.05, bounds.Height * 0.09);
        dc.DrawRoundedRectangle(Draw.Solid(Color.FromRgb(0xB5, 0x77, 0x20)), Draw.Pen(theme.BoardEdge, 1), rect, 3, 3);
        dc.DrawLine(Draw.Pen(Color.FromRgb(0xDB, 0xB5, 0x67), 1.5), new Point(rect.X + 3, p.Y), new Point(rect.Right - 3, p.Y));
    }
}

/// <summary>Piano keyboard for keys tracks.</summary>
public sealed class KeyboardRenderer : IInstrumentRenderer
{
    public void Render(DrawingContext dc, InstrumentVisualState state, Rect bounds, VisualTheme theme, InstrumentRenderPlacement placement)
    {
        // Standard keyboard sizes; smaller ones shift by octaves to keep the sounding notes in view.
        var (lowest, highest) = state.KeyboardKeys switch
        {
            76 => (28, 103), 61 => (36, 96), 49 => (36, 84), 37 => (48, 84), 25 => (48, 72), _ => (21, 108)
        };
        if (state.Notes.Count > 0 && highest - lowest < 87)
        {
            var low = state.Notes.Min(n => n.Midi); var high = state.Notes.Max(n => n.Midi);
            while (low < lowest && lowest - 12 >= 21) { lowest -= 12; highest -= 12; }
            while (high > highest && highest + 12 <= 108) { lowest += 12; highest += 12; }
        }
        var whiteCount = Enumerable.Range(lowest, highest - lowest + 1).Count(IsWhite);
        var keyWidth = bounds.Width / Math.Max(1, whiteCount);
        var whiteIndex = 0;
        // Key colours: pure white or a soft grey; scale keys tinted blue, the scale's root more strongly.
        var whiteKey = state.GreyKeys ? Color.FromRgb(0xC4, 0xC8, 0xCE) : Colors.White;
        var tint = state.ScaleColour;
        var strength = state.ScaleStrength;
        var whiteScale = FretboardRenderer.Blend(whiteKey, tint, Math.Min(0.9, 0.35 * strength));
        var whiteRoot = FretboardRenderer.Blend(whiteKey, tint, Math.Min(1, 0.62 * strength));
        var blackScale = FretboardRenderer.Blend(Colors.Black, tint, Math.Min(0.9, 0.5 * strength));
        var blackRoot = FretboardRenderer.Blend(Colors.Black, tint, Math.Min(1, 0.8 * strength));
        var scaleRoot = ScaleRootPitchClass(state.ScaleName);
        var shaded = state.ScaleStyle == ScaleHighlightStyles.Shaded;
        bool InScale(int midi) => state.ScalePitchClasses.Contains(((midi % 12) + 12) % 12);
        Color Idle(int midi, bool white)
        {
            var pc = ((midi % 12) + 12) % 12;
            if (!shaded || !state.ScalePitchClasses.Contains(pc)) return white ? whiteKey : Colors.Black;
            return pc == scaleRoot ? (white ? whiteRoot : blackRoot) : (white ? whiteScale : blackScale);
        }
        // Circles / rings: a small mark near the bottom of each scale key (only while the key is idle).
        void ScaleMark(int midi, double cx, double cy, double r)
        {
            if (shaded || !InScale(midi)) return;
            var root = ((midi % 12) + 12) % 12 == scaleRoot;
            if (state.ScaleStyle == ScaleHighlightStyles.Rings)
                dc.DrawEllipse(root ? Draw.Solid(tint, ScaleHighlightStyles.ScaleAlpha(0.35, strength, true)) : null,
                    Draw.Pen(tint, (root ? 2.2 : 1.5) * Math.Max(1, strength), Math.Min(1, strength)), new Point(cx, cy), r, r);
            else
                dc.DrawEllipse(Draw.Solid(tint, ScaleHighlightStyles.ScaleAlpha(root ? 1 : 0.7, strength, root)),
                    root ? Draw.Pen(FretboardRenderer.Blend(tint, Colors.White, 0.5), 1.2, Math.Min(1, strength)) : null, new Point(cx, cy), r, r);
        }
        var labelBrush = Draw.Solid(Color.FromRgb(0x44, 0x4A, 0x52));

        for (var midi = lowest; midi <= highest; midi++)
        {
            if (!IsWhite(midi)) continue;
            var x = bounds.X + whiteIndex * keyWidth;
            var rect = new Rect(x, bounds.Y, keyWidth, bounds.Height);
            var note = FindActiveNote(state.Notes, midi);
            var fill = note is null ? Idle(midi, true)
                : note.Role == VisualRole.Current ? theme.Current
                : note.Role == VisualRole.Selected ? theme.Accent
                : FretboardRenderer.Blend(Idle(midi, true), note.Role == VisualRole.Next ? theme.Next : theme.Accent, 0.38);
            dc.DrawRectangle(Draw.Solid(fill), Draw.Pen(theme.BoardEdge, 1), rect);
            if (note is null) ScaleMark(midi, x + keyWidth / 2, bounds.Bottom - 30, Math.Clamp(keyWidth * 0.28, 2.5, 7));
            if (midi % 12 == 0 || state.ShowNoteNames && keyWidth >= 12)
                Draw.Centered(dc, midi % 12 == 0 ? MusicTheoryService.NoteName(midi) : MusicTheoryService.NoteNames[midi % 12],
                    x + keyWidth / 2, bounds.Bottom - 16, 9 * state.NumberScale, labelBrush);
            whiteIndex++;
        }

        whiteIndex = 0;
        for (var midi = lowest; midi <= highest; midi++)
        {
            if (IsWhite(midi)) { whiteIndex++; continue; }
            var x = bounds.X + whiteIndex * keyWidth - keyWidth * 0.3;
            var rect = new Rect(x, bounds.Y, keyWidth * 0.6, bounds.Height * 0.6);
            var note = FindActiveNote(state.Notes, midi);
            var fill = note is null ? Idle(midi, false)
                : note.Role == VisualRole.Current ? theme.Current
                : note.Role == VisualRole.Selected ? theme.Accent
                : FretboardRenderer.Blend(Idle(midi, false), note.Role == VisualRole.Next ? theme.Next : theme.Accent, 0.38);
            dc.DrawRectangle(Draw.Solid(fill), Draw.Pen(theme.Background, 1), rect);
            if (note is null) ScaleMark(midi, x + keyWidth * 0.3, rect.Bottom - Math.Clamp(keyWidth * 0.3, 4, 10), Math.Clamp(keyWidth * 0.2, 2, 5));
            if (state.ShowNoteNames && keyWidth >= 14)
                Draw.Centered(dc, MusicTheoryService.NoteNames[midi % 12], x + keyWidth * 0.3, bounds.Y + bounds.Height * 0.6 - 14, 8 * state.NumberScale, Draw.Solid(Colors.White, 0.85));
        }
    }

    /// <summary>Pitch class of a scale highlight's root ("E Natural Minor" -> 4), or -1.</summary>
    private static int ScaleRootPitchClass(string? scaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleName)) return -1;
        var root = scaleName.Split(' ')[0];
        return Array.IndexOf(MusicTheoryService.NoteNames, root);
    }

    private static VisualNote? FindActiveNote(IReadOnlyList<VisualNote> notes, int midi)
    {
        for (var index = 0; index < notes.Count; index++)
            if (notes[index].Role != VisualRole.Past && notes[index].Midi == midi)
                return notes[index];
        return null;
    }

    private static bool IsWhite(int midi) => (midi % 12) is 0 or 2 or 4 or 5 or 7 or 9 or 11;
}

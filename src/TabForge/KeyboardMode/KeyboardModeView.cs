using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Views.Rendering;

namespace TabForge.KeyboardMode;

// Owns: the keyboard falling-notes view (Synthesia style): the key strip at the bottom and the notes of the selected keyboard track falling onto it in time with playback (top is the future, the
//   strip's top edge is now). Retained layers: back (guide lines), the notes page, the marks (hit and missed bars, moved with the page), the strip, the key layer (held, due and awaited keys), the hud
//   (legend, score panel, waiting cue), the progress line and the grade flash. The page (about three look-aheads tall) is drawn again only when the song time leaves it, the loop, the options (hands
//   filter, names, fingers) or the layout change, or the song jumps; a frame moves its one translate transform (shared by the marks). The other layers are drawn when their state changes; the grade
//   flash fades through its opacity only. Plus and minus, and Ctrl + wheel, ask for a shorter or longer look-ahead.
// Does not own: the playing position, the loop or the note source (KeyboardModeFrameController gives them), the judge, the score and the wait state (KeyboardModeKeyboardSession gives them), the geometry
//   (KeyboardModeLayout), the drawing (KeyboardPageDrawer, KeyboardChromeDrawer) or the colours (KeyboardColours).
// Tests: TestKeyboardModeKeyView (counters PageBuilds, KeyDraws, HudDraws, GradeDraws).
internal sealed class KeyboardModeView : FrameworkElement, IKeyboardModeSurface
{
    private const double PageSpanAheads = 3.2, PageBehindAheads = 0.1;
    private const int MaxPageNotes = 6000;
    /// <summary>A key lights this long before its note's onset, and at least this long in all.</summary>
    private const double DueLeadMs = 60, DueMinMs = 100;
    /// <summary>Notes that began longer ago than this are not looked for when the lit keys are found.</summary>
    private const double DueReachMs = 4000;

    private readonly DrawingVisual _back = new(), _page = new(), _marks = new(), _strip = new(), _keys = new(), _grade = new(), _hud = new(), _progress = new();
    private readonly Dictionary<(int, long), bool> _judged = new();
    /// <summary>A judged mark is drawn for notes up to this far past the song time (the judge's good window and a little).</summary>
    private const double MarkReachMs = 200;
    private readonly TranslateTransform _shift = new();
    private readonly List<KeyboardModePlaced> _placed = new();
    private readonly List<int> _awaitedKeys = new();
    private KeyboardNoteSource _source = KeyboardNoteSource.Empty();
    private string _hint = "";
    private KeyboardModeLayout? _layout;
    private KeyboardModePalette _palette = KeyboardModePalette.For(true);
    private double _lookAheadMs = 5000;
    private KeyboardModeKeyboardSession? _session;
    private KeyboardFeedback? _fb;
    private KeyboardModeScore? _score;
    private KeyboardModeWaitMode? _wait;
    private KeyboardModeJudge? _judge;
    private KeyboardPageOptions _options = KeyboardPageOptions.Default;
    private (KeyboardModeJudge?, int, int) _drawnMarks;
    private int _drawnProgress = -1;
    private bool _probing;
    private bool _pageValid;
    private double _pageEnd;
    private double _pageStart;
    private KeyboardModeLoop? _pageLoop;
    private KeyboardLitKeys _due;
    private KeyboardLitKeys _drawnLit;
    private double _dueFrom = double.PositiveInfinity, _dueUntil = double.NegativeInfinity;
    private int _drawnHeld = -1, _drawnGrade = -1;
    private (int, int, int, int, int, bool, bool) _drawnHud;
    private bool _keysDirty = true, _hudDirty = true;

    public KeyboardModeView()
    {
        foreach (var layer in Layers) { AddVisualChild(layer); AddLogicalChild(layer); }
        _page.Transform = _shift;
        _marks.Transform = _shift;
        _grade.Opacity = 0;
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        MinHeight = KeyboardModeLayout.MinHeight;
        AutomationProperties.SetName(this, "Keyboard mode (experimental): notes falling onto the keyboard");
        AutomationProperties.SetHelpText(this, "Plus and minus (or Ctrl and the mouse wheel) change how many seconds of notes show above the keys");
    }

    public event Action<int>? LookAheadStep;

    public FrameworkElement Element => this;

    /// <summary>How many times the notes page was drawn, and the key, hud and grade layers (self-test counters).</summary>
    public int PageBuilds { get; private set; }
    public int KeyDraws { get; private set; }
    public int HudDraws { get; private set; }
    public int GradeDraws { get; private set; }
    /// <summary>The page's translate Y (a self-test door: the only transform a frame changes).</summary>
    internal double PageShiftY => _shift.Y;
    internal KeyboardModeLayout? Layout => _layout;
    internal double GradeOpacity => _grade.Opacity;

    private DrawingVisual[] Layers => new[] { _back, _page, _marks, _strip, _keys, _hud, _progress, _grade };

    protected override int VisualChildrenCount => 8;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _back, 1 => _page, 2 => _marks, 3 => _strip, 4 => _keys, 5 => _hud, 6 => _progress, 7 => _grade,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>The hands filter, note names and finger numbers (the page is drawn again when they change).</summary>
    public void SetOptions(KeyboardHandsFilter hands, bool names, bool fingers)
    {
        var options = new KeyboardPageOptions(hands, names, fingers);
        if (options == _options) return;
        _options = options;
        _pageValid = false;
        _dueFrom = double.PositiveInfinity; _dueUntil = double.NegativeInfinity;
        _keysDirty = true;
    }

    /// <summary>How far through the song the playing position is (0 to 1); the progress line is drawn again only when it moves a pixel.</summary>
    public void SetProgress(double fraction)
    {
        if (_layout is null) return;
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var px = (int)(fraction * _layout.Width);
        if (px == _drawnProgress) return;
        _drawnProgress = px;
        using var dc = _progress.RenderOpen();
        KeyboardChromeDrawer.Progress(dc, _layout, _palette, fraction);
    }

    public void SetSource(IKeyboardModeNoteSource source, string hint)
    {
        _source = source as KeyboardNoteSource ?? KeyboardNoteSource.Empty();
        _hint = hint;
        Rebuild();
    }

    public void SetLook(bool dark, double lookAheadMs)
    {
        if (_palette.Dark == dark && Math.Abs(lookAheadMs - _lookAheadMs) < 1) return;
        _palette = KeyboardModePalette.For(dark);
        _lookAheadMs = lookAheadMs;
        Rebuild();
    }

    /// <summary>The run whose score, held keys, grades and wait state show; null: nothing is judged and only the falling notes show.</summary>
    public void SetPlayAlong(KeyboardModeKeyboardSession? session)
    {
        if (_probing || ReferenceEquals(session, _session)) return;
        _session = session;
        Show(session?.Feedback, session?.Score, session?.Wait, null);
    }

    /// <summary>Shows given state instead of a run's (off-screen captures): later <see cref="SetPlayAlong"/> calls are ignored.</summary>
    internal void ProbePlayAlong(KeyboardFeedback feedback, KeyboardModeScore score, KeyboardModeWaitMode? wait, KeyboardModeJudge? judge = null)
    {
        _probing = true;
        Show(feedback, score, wait, judge);
    }

    private void Show(KeyboardFeedback? feedback, KeyboardModeScore? score, KeyboardModeWaitMode? wait, KeyboardModeJudge? judge)
    {
        _fb = feedback; _score = score; _wait = wait; _judge = judge;
        _drawnMarks = default;
        _keysDirty = _hudDirty = true;
        _drawnGrade = -1;
        _grade.Opacity = 0;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
        if (_last is { } last) Update(last.Now, last.Loop, true, last.Playing);   // the page follows the new size at once, not on the next frame
    }

    private (double Now, KeyboardModeLoop? Loop, bool Playing)? _last;

    /// <summary>Redraws the standing layers and forgets the page and the drawn states (size, theme, look-ahead or notes changed).</summary>
    private void Rebuild()
    {
        if (ActualWidth < 10 || ActualHeight < 10) { _layout = null; return; }
        _layout = KeyboardModeLayout.Compute(_source.Lowest, _source.Highest, ActualWidth, ActualHeight, _lookAheadMs);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        using (var dc = _back.RenderOpen()) KeyboardChromeDrawer.Back(dc, _layout, _palette, _hint, dpi);
        using (var dc = _strip.RenderOpen()) KeyboardChromeDrawer.Strip(dc, _layout, _palette, dpi);
        using (var dc = _page.RenderOpen()) { }
        _pageValid = false;
        _dueFrom = double.PositiveInfinity; _dueUntil = double.NegativeInfinity;
        _keysDirty = _hudDirty = true;
        _drawnGrade = -1;
        _drawnProgress = -1;
    }

    /// <summary>One frame: <paramref name="nowV"/> is the virtual song time at the strip's top edge.</summary>
    public void Update(double nowV, KeyboardModeLoop? loop, bool jumped, bool playing)
    {
        _last = (nowV, loop, playing);
        if (_layout is null) return;
        var ahead = _layout.AheadMs;
        if (!_pageValid || jumped || loop != _pageLoop || nowV < _pageStart || nowV + ahead > _pageEnd) BuildPage(nowV, loop, ahead);
        // Whole device pixels: a fractional offset resamples the page every frame and blurs it.
        PixelSnap.SetOffset(_shift, 0, _layout.StripTop - (_pageEnd - nowV) * _layout.PxPerMs, PixelSnap.Dpi(this));
        DrawMarks(nowV);
        DrawKeys(nowV);
        DrawHud();
        DrawGrade();
    }

    /// <summary>The hit and missed bars of the run, drawn again only when the judge resolved notes or the page changed.</summary>
    private void DrawMarks(double nowV)
    {
        var judge = _session?.Judge ?? _judge;
        var sig = (judge, _score?.Judged ?? 0, PageBuilds);
        if (sig == _drawnMarks) return;
        _drawnMarks = sig;
        _judged.Clear();
        if (judge is not null)
            foreach (var r in judge.Results)
                if (r.IsResolved) _judged[(r.Expected.Midi, (long)Math.Round(r.Expected.OnsetSec * 1000))] = r.IsHit;
        using (var dc = _marks.RenderOpen())
            if (_judged.Count > 0)
                KeyboardPageDrawer.Marks(dc, _placed, _pageEnd, _pageStart, nowV + MarkReachMs,
                    p => _judged.TryGetValue((p.Note.Fret, (long)Math.Round(p.Note.OnsetMs)), out var hit) ? hit : null, _layout!, _palette.Keyboard, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }



    private void BuildPage(double nowV, KeyboardModeLoop? loop, double aheadMs)
    {
        _pageStart = nowV - aheadMs * PageBehindAheads;
        _pageEnd = _pageStart + aheadMs * PageSpanAheads;
        _pageLoop = loop;
        _placed.Clear();
        KeyboardModeWindow.Collect(_source, _pageStart, _pageEnd, loop, _placed);
        if (_placed.Count > MaxPageNotes) _placed.RemoveRange(MaxPageNotes, _placed.Count - MaxPageNotes);
        using (var dc = _page.RenderOpen()) KeyboardPageDrawer.Draw(dc, _placed, _pageEnd, _layout!, _palette.Keyboard, _options, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _pageValid = true;
        _dueFrom = double.PositiveInfinity; _dueUntil = double.NegativeInfinity;
        PageBuilds++;
    }

    /// <summary>The key layer, drawn again only when the held keys, the due keys or the awaited keys changed.</summary>
    private void DrawKeys(double nowV)
    {
        DueKeys(nowV);
        KeyMask awaited = default;
        if (_wait is { IsWaiting: true } wait)
        {
            wait.AwaitedInto(_awaitedKeys);
            foreach (var k in _awaitedKeys) awaited.Set(k);
        }
        var lit = _due with { Awaited = awaited };
        var held = _fb?.HeldVersion ?? 0;
        if (!_keysDirty && held == _drawnHeld && lit == _drawnLit) return;
        _keysDirty = false; _drawnHeld = held; _drawnLit = lit;
        using (var dc = _keys.RenderOpen()) KeyboardKeyPainter.Paint(dc, _layout!, _palette, _fb, lit);
        KeyDraws++;
    }

    /// <summary>The keys whose note is due at <paramref name="nowV"/> (from just before its onset until its end), per hand; a hidden hand's notes light nothing. The answer holds until the next onset or end,
    /// so most frames do not look at the notes.</summary>
    private void DueKeys(double nowV)
    {
        if (nowV >= _dueFrom && nowV < _dueUntil) return;
        KeyMask left = default, right = default;
        var until = double.PositiveInfinity;
        var after = FirstAfter(nowV + DueLeadMs);
        for (var i = after - 1; i >= 0; i--)
        {
            var p = _placed[i];
            if (p.VirtualMs < nowV - DueReachMs) break;
            var end = p.VirtualMs + Math.Max(DueMinMs, p.Note.DurationMs);
            if (end <= nowV || KeyboardHands.OpacityOf(_options.Hands, p.Note.LeftHand) <= 0) continue;
            if (p.Note.LeftHand) left.Set(p.Note.Fret); else right.Set(p.Note.Fret);
            until = Math.Min(until, end);
        }
        if (after < _placed.Count) until = Math.Min(until, _placed[after].VirtualMs - DueLeadMs);
        _due = new KeyboardLitKeys(left, right, default); _dueFrom = nowV; _dueUntil = until;
    }

    private int FirstAfter(double v)
    {
        int lo = 0, hi = _placed.Count;
        while (lo < hi) { var mid = (lo + hi) / 2; if (_placed[mid].VirtualMs <= v) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>The legend, score panel and waiting cue, drawn again only when the score or the waiting state changed.</summary>
    private void DrawHud()
    {
        var score = _score;
        var waiting = _wait is { IsWaiting: true };
        var sig = score is null ? (0, 0, 0, 0, 0, false, false) : (score.Hits, score.Misses, score.Streak, score.BestStreak, (int)Math.Round(score.AccuracyPercent), true, waiting);
        if (!_hudDirty && sig.Equals(_drawnHud)) return;
        _hudDirty = false; _drawnHud = sig;
        using (var dc = _hud.RenderOpen()) KeyboardChromeDrawer.Hud(dc, _layout!, _palette, score, waiting, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        HudDraws++;
    }

    /// <summary>The grade flash: drawn on a new grade, then only its opacity changes until it has faded.</summary>
    private void DrawGrade()
    {
        var fb = _fb;
        if (fb is null) return;
        if (fb.GradeVersion != _drawnGrade)
        {
            _drawnGrade = fb.GradeVersion;
            using var dc = _grade.RenderOpen();
            if (fb.Latest is { } kind) KeyboardChromeDrawer.Grade(dc, _layout!, _palette, kind, fb.LatestKey, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            GradeDraws++;
        }
        var alpha = fb.Alpha(Environment.TickCount64);
        if (Math.Abs(_grade.Opacity - alpha) > 0.004) _grade.Opacity = alpha;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0) return;
        LookAheadStep?.Invoke(e.Delta > 0 ? -1 : 1);   // wheel up: fewer seconds, longer notes (zoom in)
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key is Key.OemPlus or Key.Add ? 1 : e.Key is Key.OemMinus or Key.Subtract ? -1 : 0;
        if (step == 0) return;
        LookAheadStep?.Invoke(step);
        e.Handled = true;
    }
}

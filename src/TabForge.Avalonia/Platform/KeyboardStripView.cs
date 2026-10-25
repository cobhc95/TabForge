using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.KeyboardMode;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The keyboard falling-notes view (Synthesia style): the notes of the keyboard track falling onto the key strip in time with the song position (top is the future, the strip's top edge is now),
/// with the lit keys, the hand legend, the score panel, the song progress line and the grade flash of the play-along. Drawn at the Core strip geometry, rebuilt only when the size or the range changes.
/// Owns: the Avalonia view and its state holders (source, judge, score, feedback, options) and the order of the layers. Does not own: the numbers (<see cref="KeyboardStripGeometry"/>), the notes, judge, score and feedback
/// (Core), the playback position (<see cref="IKeyboardSongPosition"/>, given by the playback session), the drawing (KeyboardNotesPainter, KeyboardChromePainter), the colours (KeyboardInk) or the window (the pop-out).
/// A frame allocates only the text layouts the draw context shapes once per string.
/// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests.
/// </summary>
public sealed class KeyboardStripView : Control
{
    public const int DefaultLowest = 36;
    public const int DefaultHighest = 96;
    public const double DefaultLookAheadMs = 4000;
    /// <summary>A judged mark is drawn for notes up to this far past the song time (the judge's good window and a little).</summary>
    private const double MarkReachMs = 200;

    private readonly AvaloniaDrawContext _draw = new();
    private readonly KeyboardChromePainter _chrome = new();
    private readonly List<KeyboardModePlaced> _placed = new();
    // Judged notes by (key, onset ms) to hit or miss; rebuilt when the judge's resolved count changes.
    private readonly Dictionary<(int, long), bool> _judged = new();
    private (KeyboardModeJudge?, int) _judgedFor;
    private KeyboardStripGeometry? _geometry;
    private (double Width, double Height, int Lowest, int Highest, double LookAheadMs) _geometryKey;
    private KeyboardNoteSource? _source;
    private int _lowest = DefaultLowest, _highest = DefaultHighest;

    /// <summary>The song time source; null until playback connects, when the view reads time 0.</summary>
    public IKeyboardSongPosition? Position { get; set; }

    /// <summary>The notes that fall; null or empty shows the idle hint over the default key range.</summary>
    public KeyboardNoteSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            var any = value is { Notes.Count: > 0 };
            _lowest = any ? value!.Lowest : DefaultLowest;
            _highest = any ? value!.Highest : DefaultHighest;
            InvalidateVisual();
        }
    }

    public int Lowest { get => _lowest; set { _lowest = value; InvalidateVisual(); } }
    public int Highest { get => _highest; set { _highest = value; InvalidateVisual(); } }
    public double LookAheadMs { get; set; } = DefaultLookAheadMs;
    /// <summary>The loop in song milliseconds, or null.</summary>
    public KeyboardModeLoop? Loop { get; set; }
    /// <summary>The run whose marks, held keys, score and flash show; null shows only the falling notes.</summary>
    public KeyboardModeJudge? Judge { get; set; }
    public KeyboardModeScore? Score { get; set; }
    public KeyboardFeedback? Feedback { get; set; }
    public KeyboardHandsFilter Hands { get; set; } = KeyboardHandsFilter.Both;
    public bool ShowNames { get; set; } = true;
    public bool ShowFingers { get; set; } = true;
    /// <summary>How far through the song the position is (0 to 1).</summary>
    public double Progress { get; set; }
    /// <summary>True while wait mode holds the song; <see cref="Awaited"/> are the keys it waits for.</summary>
    public bool Waiting { get; set; }
    public KeyMask Awaited { get; set; }

    /// <summary>The strip geometry for the current size, or null before the view has a size.</summary>
    public KeyboardStripGeometry? Geometry => Bounds.Width > 0 && Bounds.Height > 0 ? GeometryFor(Bounds.Width, Bounds.Height) : null;

    private KeyboardStripGeometry GeometryFor(double width, double height)
    {
        var key = (width, height, Lowest, Highest, LookAheadMs);
        if (_geometry is null || key != _geometryKey)
        {
            _geometry = KeyboardStripGeometry.Compute(Lowest, Highest, width, height, LookAheadMs);
            _geometryKey = key;
        }
        return _geometry;
    }

    public override void Render(DrawingContext context)
    {
        var ink = KeyboardInk.For(ActualThemeVariant == ThemeVariant.Light);
        var d = _draw.Use(context);
        if (Geometry is not { } g) { d.Rect(ink.Panel, null, 0, 0, Bounds.Width, Bounds.Height); return; }
        var now = Position?.SongMs ?? 0;
        var empty = _source is not { Notes.Count: > 0 };
        _chrome.Back(d, g, ink, empty ? KeyboardChromePainter.IdleHint : "");
        _placed.Clear();
        if (_source is { } source) KeyboardModeWindow.Collect(source, now, now + g.AheadMs, Loop, _placed);
        d.PushClip(0, 0, g.Width, g.StripTop);
        KeyboardNotesPainter.Draw(d, _placed, now, g, ink, Hands, ShowNames, ShowFingers);
        if (Judge is { } judge)
        {
            RefreshJudged(judge);
            if (_judged.Count > 0)
                KeyboardNotesPainter.Marks(d, _placed, now, now - g.AheadMs, now + MarkReachMs, p => _judged.TryGetValue((p.Note.Fret, (long)Math.Round(p.Note.OnsetMs)), out var hit) ? hit : null, g, ink);
        }
        d.Pop();
        _chrome.Strip(d, g, ink);
        KeyboardDueKeys.Find(_placed, now, Hands, out var dueLeft, out var dueRight);
        _chrome.Keys(d, g, ink, Feedback, dueLeft, dueRight, Waiting ? Awaited : default);
        _chrome.Hud(d, g, ink, Score, Waiting);
        KeyboardChromePainter.Progress(d, g, ink, Progress);
        if (Feedback is { Latest: { } kind } feedback) KeyboardChromePainter.Grade(d, g, ink, kind, feedback.LatestKey, feedback.Alpha(Environment.TickCount64));
    }

    private void RefreshJudged(KeyboardModeJudge judge)
    {
        var sig = (judge, judge.ResolvedPrefix);
        if (sig == _judgedFor) return;
        _judgedFor = sig;
        _judged.Clear();
        foreach (var r in judge.Results)
            if (r.IsResolved) _judged[(r.Expected.Midi, (long)Math.Round(r.Expected.OnsetSec * 1000))] = r.IsHit;
    }
}

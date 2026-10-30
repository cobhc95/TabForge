using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's drawn surface: the bar ruler on top, the piano keys on the left, the note grid and the velocity lane at the bottom,
//       its zoom (pixels per beat, pixels per row) and scroll, the hit tests, and turning pointer gestures and keys into presenter edits. A drag only
//       moves a preview and repaints; the song changes once, on release (one undo step), so a drag never syncs, saves or rebuilds anything.
// Does not own: the notes, the selection and the commit (PianoRollPresenter), the toolbar and info line (PianoRollView), the playhead line
//       (PianoRollPlayheadLayer), the snap rules (PianoRollSnap) or the song, playback and loop (the host's hooks).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface : Control
{
    internal const double RulerSize = 30, KeysSize = KeyStripSize, EdgeGrip = 5, DividerGrip = 4;
    /// <summary>The lane strip under the grid: one toolbar control height, so the lane button in it is a real toolbar-size button.</summary>
    internal static double LaneHeaderSize => PianoRollToolbarLayout.ControlHeight;
    internal const double DefaultPixelsPerBeat = 48, MinPixelsPerBeat = 6, MaxPixelsPerBeat = 480;
    internal const double DefaultRowPx = 12, MinRowPx = 5, MaxRowPx = 32;
    internal const double MinLane = 30, DefaultLane = 84, MaxLaneShare = 0.6;
    internal const int TopPitch = 127;
    private const double ExtraBeats = 16;
    private const double ZoomStep = 1.25;

    private readonly AvaloniaDrawContext _draw = new();
    private ThemeColourCache? _theme;
    private ThemeColourCache Colours => _theme ??= new(this);

    public PianoRollSurface(PianoRollPresenter presenter)
    {
        Presenter = presenter;
        Focusable = true;
        ClipToBounds = true;
        Playhead = new PianoRollPlayheadLayer(this);
        LogicalChildren.Add(Playhead);
        VisualChildren.Add(Playhead);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public PianoRollPresenter Presenter { get; }

    /// <summary>The ghost notes (other takes and tracks) drawn faint behind the notes; empty when the option is off.</summary>
    internal IReadOnlyList<PianoRollNote> Ghosts { get; set; } = Array.Empty<PianoRollNote>();

    /// <summary>The keys a MIDI keyboard holds now (lit live on the piano keys by the playhead layer).</summary>
    internal PianoRollKeyState Keys { get; } = new();

    /// <summary>The notes of the take being recorded on this track (a translucent overlay, drawn by the playhead layer); empty when not recording.</summary>
    internal List<LiveRollNote> LiveNotes { get; } = new();

    /// <summary>The playhead line drawn over the surface; repainted alone on every playback frame.</summary>
    internal PianoRollPlayheadLayer Playhead { get; }

    public double PixelsPerBeat { get; private set; } = DefaultPixelsPerBeat;
    public double RowPx { get; private set; } = DefaultRowPx;
    public double ScrollX { get; private set; }
    public double ScrollY { get; private set; } = (TopPitch - 84) * DefaultRowPx;

    /// <summary>The velocity lane's height (DIP) and whether it is shown; the host stores both in the settings.</summary>
    public double Lane { get; set; } = DefaultLane;
    public bool LaneShown { get; set; } = true;

    /// <summary>True while new and moved notes sound (the toolbar's Audition button).</summary>
    public bool AuditionOn { get; set; } = true;

    // ---- Host hooks (set by PianoRollView) ----
    internal Func<SnapSettings?> Snap { get; set; } = () => null;
    internal Func<double?> PlayheadBeat { get; set; } = () => null;
    /// <summary>True while the song plays (the sounding notes light only then).</summary>
    internal Func<bool> Playing { get; set; } = () => false;
    /// <summary>True when the playback needle shows (the "Playback needle" setting, read live at each paint).</summary>
    internal Func<bool> NeedleOn { get; set; } = () => true;
    /// <summary>The colour of the playback needle and the ruler marker for a light (true) or dark theme: the Playback highlight setting.</summary>
    internal Func<bool, UiColor> PlayheadColour { get; set; } = light => AvaloniaPalette.Get("PianoRollPlayheadMarker", light);
    /// <summary>The user scrolled the piano roll sideways (wheel, scroll bar): the main timeline scrolls with it.</summary>
    internal Action? UserScrolled { get; set; }
    internal Func<(double From, double To)?> LoopSpan { get; set; } = () => null;
    internal Func<UiColor?> TrackColour { get; set; } = () => null;
    internal Action<int>? Audition { get; set; }
    internal Action<double>? Seek { get; set; }
    internal Action<double, double, bool>? MakeLoop { get; set; }
    internal Action? LaneChanged { get; set; }
    internal Action? ViewChanged { get; set; }
    internal Action? SelectionChanged { get; set; }
    internal Action<string>? Report { get; set; }
    /// <summary>The lane header's name was clicked: the view offers the lanes (velocity, CC, pitch bend).</summary>
    internal Action? LaneMenuRequested { get; set; }
    /// <summary>True when the track has more lanes than velocity (a MIDI track): the lane picker's controller rows are enabled (on a tab track they stay disabled).</summary>
    internal bool LanePicker { get; set; }

    // ---- Geometry ----
    internal double GridLeft => KeyStripSize;   // one width for piano keys and note names: switching modes never shifts the grid
    /// <summary>The key strip's width in both modes (piano keys: black keys ~62% of it; note names: the name at the right edge).</summary>
    internal const double KeyStripSize = 116;
    internal const double NamesSize = KeyStripSize;

    /// <summary>The key strip shows note names (drum names on a drum track) instead of piano keys (TrackModel.PianoRollNoteNames).</summary>
    internal bool NoteNames
    {
        get => _noteNames;
        set { if (_noteNames == value) return; _noteNames = value; InvalidateVisual(); Playhead.InvalidateVisual(); ViewChanged?.Invoke(); }
    }
    private bool _noteNames;

    /// <summary>Names rows by the General MIDI drum names (a drum track).</summary>
    internal bool DrumNames { get; set; }
    internal double GridTop => RulerSize;
    internal double LaneTotal => LaneHeaderSize + (LaneShown ? Lane : 0);
    internal double GridBottom => Math.Max(GridTop, Bounds.Height - LaneTotal);
    internal double LaneTop => GridBottom + LaneHeaderSize;
    internal double XOf(double beat) => GridLeft + beat * PixelsPerBeat - ScrollX;
    internal double BeatAt(double x) => Math.Max(0, (x - GridLeft + ScrollX) / PixelsPerBeat);
    internal double YOf(int pitch) => GridTop + RowIndex(pitch) * RowPx - ScrollY;
    internal int PitchAt(double y) => _rows[Math.Clamp((int)Math.Floor((y - GridTop + ScrollY) / RowPx), 0, _rows.Count - 1)];
    internal double ViewportWidth => Math.Max(1, Bounds.Width - GridLeft);
    internal double ViewportHeight => Math.Max(1, GridBottom - GridTop);
    internal double ContentHeight => _rows.Count * RowPx;

    // The pitches the rows show, highest first (the fold picks them), and each pitch's row (a pitch not shown takes the nearest row). UI thread only.
    private IReadOnlyList<int> _rows = Enumerable.Range(0, TopPitch + 1).Reverse().ToList();
    private readonly int[] _rowOf = Enumerable.Range(0, TopPitch + 1).Select(p => TopPitch - p).ToArray();

    /// <summary>The pitches shown as rows, highest first (all 128 unfolded); set by the view from the fold and the scale.</summary>
    internal IReadOnlyList<int> Rows
    {
        get => _rows;
        set
        {
            _rows = value.Count == 0 ? Enumerable.Range(0, TopPitch + 1).Reverse().ToList() : value;
            for (var pitch = 0; pitch <= TopPitch; pitch++)
            {
                var best = 0;
                for (var r = 1; r < _rows.Count; r++) if (Math.Abs(_rows[r] - pitch) < Math.Abs(_rows[best] - pitch)) best = r;
                _rowOf[pitch] = best;
            }
            ScrollTo(ScrollX, ScrollY);
            InvalidateVisual();
        }
    }

    /// <summary>The pitch classes of the Scale highlight while the scale button is on (rows in the scale are tinted), else null.</summary>
    internal IReadOnlySet<int>? ScaleClasses { get; set; }

    /// <summary>True while the rows are folded: every row is labelled, not only the Cs.</summary>
    internal bool FoldedLabels => _rows.Count < TopPitch + 1;

    /// <summary>How notes are coloured: Track (brighter = louder), Velocity, Pitch or Channel (PianoRollScale.ColourModes).</summary>
    internal string ColourMode { get; set; } = "Track";

    internal int RowIndex(int pitch) => _rowOf[PianoRollEdits.ClampPitch(pitch)];

    /// <summary>The first and last row index on screen.</summary>
    internal (int First, int Last) VisibleRows() =>
        (Math.Clamp((int)Math.Floor(ScrollY / RowPx), 0, _rows.Count - 1), Math.Clamp((int)Math.Floor((ScrollY + ViewportHeight) / RowPx), 0, _rows.Count - 1));

    /// <summary>Beats the surface scrolls over: the song, the notes and some room after them.</summary>
    internal double ContentBeats
    {
        get
        {
            var end = Presenter.Map.EndBeat;
            foreach (var note in Presenter.Notes) end = Math.Max(end, note.End);
            return end + ExtraBeats;
        }
    }

    internal double ContentWidth => ContentBeats * PixelsPerBeat;

    /// <summary>Scrolls to <paramref name="x"/>, <paramref name="y"/> (clamped to the content) and repaints.</summary>
    public void ScrollTo(double x, double y)
    {
        // The keyboard ends at its lowest and highest row: rows grow to fill a tall view, so no blank area shows below C-1 (MIDI 0) or above G9 (127).
        var rows = RowPx;
        if (_rows.Count * RowPx < ViewportHeight) RowPx = Math.Min(MaxRowPx, ViewportHeight / _rows.Count);
        var nx = Math.Clamp(x, 0, Math.Max(0, ContentWidth - ViewportWidth));
        var ny = Math.Clamp(y, 0, Math.Max(0, ContentHeight - ViewportHeight));
        if (nx == ScrollX && ny == ScrollY && rows == RowPx) return;
        ScrollX = nx;
        ScrollY = ny;
        InvalidateVisual();
        Playhead.InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>Horizontal zoom by <paramref name="factor"/>, keeping the beat under <paramref name="anchorX"/> in place.</summary>
    public void ZoomTime(double factor, double? anchorX = null)
    {
        var x = anchorX ?? GridLeft + ViewportWidth / 2;
        var beat = BeatAt(x);
        PixelsPerBeat = Math.Clamp(PixelsPerBeat * factor, MinPixelsPerBeat, MaxPixelsPerBeat);
        ScrollX = double.NaN;   // forces the ScrollTo below to apply
        ScrollTo(beat * PixelsPerBeat - (x - GridLeft), ScrollY);
    }

    /// <summary>Vertical zoom by <paramref name="factor"/>, keeping the pitch under <paramref name="anchorY"/> in place.</summary>
    public void ZoomPitch(double factor, double? anchorY = null)
    {
        var y = anchorY ?? GridTop + ViewportHeight / 2;
        var row = (y - GridTop + ScrollY) / RowPx;
        RowPx = Math.Clamp(RowPx * factor, MinRowPx, MaxRowPx);
        ScrollY = double.NaN;
        ScrollTo(ScrollX, row * RowPx - (y - GridTop));
    }

    public void ZoomIn() => ZoomTime(ZoomStep);

    public void ZoomOut() => ZoomTime(1 / ZoomStep);

    /// <summary>Fits the notes (the selection when there is one) into the view, in time and pitch.</summary>
    public void ZoomToFit()
    {
        var notes = Presenter.Notes;
        if (notes.Count == 0) return;
        double from = double.MaxValue, to = 0;
        int low = TopPitch, high = 0;
        var picked = Presenter.Selected.Count > 0 ? (IEnumerable<int>)Presenter.Selected : System.Linq.Enumerable.Range(0, notes.Count);
        foreach (var i in picked)
        {
            from = Math.Min(from, notes[i].Start);
            to = Math.Max(to, notes[i].End);
            low = Math.Min(low, notes[i].Pitch);
            high = Math.Max(high, notes[i].Pitch);
        }
        PixelsPerBeat = Math.Clamp(ViewportWidth / Math.Max(1, to - from), MinPixelsPerBeat, MaxPixelsPerBeat);
        RowPx = Math.Clamp(ViewportHeight / Math.Max(1, RowIndex(low) - RowIndex(high) + 3), MinRowPx, MaxRowPx);
        ScrollX = double.NaN;
        ScrollTo(from * PixelsPerBeat, (RowIndex(high) - 1) * RowPx);
    }

    /// <summary>Scrolls the beat <paramref name="beat"/> into view when it is off the visible part (a page at a time, as the score follows).</summary>
    public void Reveal(double beat)
    {
        var x = beat * PixelsPerBeat;
        if (x < ScrollX || x > ScrollX + ViewportWidth - EdgeGrip) ScrollTo(x - ViewportWidth * 0.1, ScrollY);
    }

    /// <summary>Centres the notes' pitch range vertically (a newly shown track); the time axis is left where the timeline put it.</summary>
    public void CentreOnNotes()
    {
        var notes = Presenter.Notes;
        var middle = 60;
        if (notes.Count > 0)
        {
            int low = TopPitch, high = 0;
            foreach (var note in notes) { low = Math.Min(low, note.Pitch); high = Math.Max(high, note.Pitch); }
            middle = (low + high) / 2;
        }
        ScrollY = double.NaN;
        ScrollTo(ScrollX, RowIndex(middle) * RowPx - ViewportHeight / 2);   // sideways the piano roll follows the main timeline
    }

    // ---- Hit tests ----

    /// <summary>The rectangle note <paramref name="note"/> is drawn in.</summary>
    internal Rect NoteRect(PianoRollNote note) =>
        new(XOf(note.Start), YOf(note.Pitch), Math.Max(2, note.Length * PixelsPerBeat), Math.Max(1, RowPx - 1));

    internal enum NotePart { Body, Start, End, Top }

    /// <summary>The value zone at a note's top edge (DIP): a vertical drag there sets the note's velocity.</summary>
    internal const double TopGrip = 3;

    /// <summary>The note under <paramref name="p"/> in the grid (the top one drawn) and which part of it, or -1.</summary>
    internal (int Index, NotePart Part) HitNote(Point p)
    {
        if (p.X < GridLeft || p.Y < GridTop || p.Y > GridBottom) return (-1, NotePart.Body);
        var notes = Presenter.Notes;
        for (var i = notes.Count - 1; i >= 0; i--)
        {
            var r = NoteRect(notes[i]);
            if (!r.Contains(p)) continue;
            var grip = r.Width > 3 * EdgeGrip ? EdgeGrip : 0;
            if (r.Height >= 3 * TopGrip && p.Y <= r.Top + TopGrip) return (i, NotePart.Top);
            if (p.X >= r.Right - grip) return (i, NotePart.End);
            if (p.X <= r.Left + grip) return (i, NotePart.Start);
            return (i, NotePart.Body);
        }
        return (-1, NotePart.Body);
    }

    internal bool InGrid(Point p) => p.X >= GridLeft && p.Y >= GridTop && p.Y < GridBottom;
    internal bool InRuler(Point p) => p.X >= GridLeft && p.Y < GridTop;
    internal bool InKeys(Point p) => p.X < GridLeft && p.Y >= GridTop && p.Y < GridBottom;
    internal bool InLaneHeader(Point p) => p.Y >= GridBottom && p.Y < LaneTop;
    internal bool InLane(Point p) => LaneShown && p.X >= GridLeft && p.Y >= LaneTop;
    internal bool OnDivider(Point p) => LaneShown && Math.Abs(p.Y - GridBottom) <= DividerGrip;

    /// <summary>The snap step at <paramref name="beat"/> (a sixteenth with snapping off), for the arrow keys.</summary>
    internal double StepAt(double beat) => Snap() is { Enabled: true } snap ? Presenter.Map.GridStep(snap.Grid, beat) : 0.25;

    // The snap items of the gesture in progress (or of the hover), built once per gesture (keyed on which notes are left out, not how many) so a drag allocates nothing per move; dropped when the
    // notes are read again or the gesture ends. Thread: UI only.
    private (IReadOnlyList<PianoRollNote> Notes, HashSet<int> Except, List<double> Items)? _items;

    /// <summary>The other notes' starts and ends, the snap "items" (the notes being dragged left out).</summary>
    internal List<double> Items(IReadOnlyCollection<int> except)
    {
        if (_items is { } cached && ReferenceEquals(cached.Notes, Presenter.Notes) && cached.Except.SetEquals(except)) return cached.Items;
        var list = new List<double>();
        var notes = Presenter.Notes;
        for (var i = 0; i < notes.Count; i++)
        {
            if (except.Contains(i)) continue;
            list.Add(notes[i].Start);
            list.Add(notes[i].End);
        }
        _items = (notes, new HashSet<int>(except), list);
        return list;
    }

    internal double SnapBeat(double beat, IReadOnlyCollection<int> except, bool alt) =>
        PianoRollSnap.Snap(Presenter.Map, Snap(), beat, PixelsPerBeat, Items(except), PlayheadBeat(), alt);

    /// <summary>Runs a commit: the drag preview stays while it waits (the tab prompt), then the surface repaints and the info line follows.</summary>
    internal void Run(Task<bool> commit)
    {
        _pending = true;
        InvalidateVisual();
        UiTasks.Forget(Settle(commit), "Piano roll edit", Report ?? (_ => { }));
    }

    private async Task Settle(Task<bool> commit)
    {
        try { await commit; }
        finally
        {
            _pending = false;
            EndGesture();
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize.Height > 0) ScrollTo(ScrollX, ScrollY);   // a taller view re-fits the rows to the keyboard
        Playhead.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

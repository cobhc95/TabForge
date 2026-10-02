using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Views;
using static TabForge.Diagnostics.LayoutAudit;
using TabForge.Views.Score;

namespace TabForge.Diagnostics;

/// <summary>One finding for one bar in one view. Types: collision, clip:*, gap, missing:*, extra:* (missing/extra = data versus drawing).</summary>
internal sealed record BarIssue(string View, string Type, string Detail);

/// <summary>
/// One track engraved off-screen in one view (notation = staff only, tab = tab only, both = the combined page). The layout is
/// computed once; every bar then reads its own slice of the per-system drawings (nothing is laid out again per bar).
/// </summary>
internal sealed class TrackViewAudit
{
    public const double MinGap = 1.5;
    public readonly SongProject Project;
    public readonly TrackModel Track;
    public readonly int TrackIndex;
    public readonly string View;
    public readonly bool Staff, Tab;
    public readonly TabEditorControl Editor;
    public readonly ScorePageLayout Layout;
    public readonly double K = TabEditorControl.AuditTextScale;
    private readonly Dictionary<int, Drawing> _drawings = new();
    private readonly Dictionary<int, List<Item>> _items = new();
    private readonly Dictionary<int, List<Collision>> _collisions = new();
    private readonly Dictionary<int, List<string>> _dynamics = new();

    public TrackViewAudit(SongProject project, int trackIndex, string view)
    {
        Project = project; TrackIndex = trackIndex; View = view; Track = project.Tracks[trackIndex];
        Staff = view != "tab"; Tab = view != "notation";
        Editor = new TabEditorControl
        {
            Project = project, SelectedTrackIndex = trackIndex, DarkPaper = false, HideCursor = true, PlaybackMeasure = -1,
            Notation = view == "notation" ? NotationMode.StaffOnly : view == "tab" ? NotationMode.TabOnly : NotationMode.TabAndStaff
        };
        Editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Editor.Arrange(new Rect(Editor.DesiredSize));
        Editor.UpdateLayout();
        foreach (var (system, drawing) in Editor.AuditSystemDrawings())
        {
            _drawings[system] = drawing;
            var list = new List<Item>();
            Walk(drawing, Matrix.Identity, list);
            _items[system] = list;
        }
        Layout = Editor.AuditLayout();
        var previous = -1;
        for (var b = 0; b < Track.Measures.Count; b++)
            foreach (var cell in Track.Measures[b].Cells)
            {
                var principal = cell.Notes.FirstOrDefault(n => !n.IsGraceNote);
                if (principal is null) continue;
                var index = Dynamics.NearestIndex(principal.Velocity);
                if (index == previous) continue;
                previous = index;
                if (!_dynamics.TryGetValue(b, out var names)) _dynamics[b] = names = new List<string>();
                names.Add(Dynamics.Names[index]);
            }
    }

    public int BarCount => Track.Measures.Count;
    public Drawing? DrawingOf(int system) => _drawings.GetValueOrDefault(system);
    public List<string> DynamicsFor(int bar) => _dynamics.GetValueOrDefault(bar) ?? new List<string>();

    /// <summary>The part of the drawing this view owns: the notation view stops above the tab (the editor still draws lyrics down there).</summary>
    public bool InRegion(Item item, TabEditorControl.AuditSystemBox box) => Tab || item.Box.Top < box.TabTop - 6;

    /// <summary>Every item of a system, including those below the notation band of the notation view.</summary>
    public List<Item> AllSystemItems(int system) => _items.GetValueOrDefault(system) ?? new List<Item>();

    public List<Item> SystemItems(int system)
    {
        if (!_items.TryGetValue(system, out var all)) return new List<Item>();
        var box = Editor.AuditSystemMetrics(system);
        return all.Where(i => InRegion(i, box)).ToList();
    }

    public List<Item> BarItems(int bar)
    {
        var position = Layout.Measure(bar);
        return SystemItems(position.SystemIndex)
            .Where(i => { var cx = i.Box.X + i.Box.Width / 2; return cx >= position.X && cx < position.X + position.Width; }).ToList();
    }

    public List<Collision> CollisionsOf(int bar)
    {
        var position = Layout.Measure(bar);
        if (!_collisions.TryGetValue(position.SystemIndex, out var found))
        {
            found = new List<Collision>();
            foreach (var (a, b) in Collisions(SystemItems(position.SystemIndex)))
                found.Add(new Collision(TrackIndex, Editor.AuditBarAt(position.SystemIndex, (a.Box.X + a.Box.Right) / 2), position.SystemIndex, a, b));
            _collisions[position.SystemIndex] = found;
        }
        return found.Where(c => c.Bar == bar).ToList();
    }

    /// <summary>Crop rectangle (page units) of a bar: its columns plus a margin, from the highest to the lowest ink of the bar's slice.</summary>
    public Rect CropOf(int bar)
    {
        var position = Layout.Measure(bar);
        var box = Editor.AuditSystemMetrics(position.SystemIndex);
        var x0 = Math.Max(0, position.X - 8);
        var x1 = Math.Min(box.PageWidth, position.X + position.Width + 8);
        double top = double.PositiveInfinity, bottom = double.NegativeInfinity;
        foreach (var item in SystemItems(position.SystemIndex))
        {
            if (item.Box.Right < x0 || item.Box.Left > x1) continue;
            top = Math.Min(top, item.Box.Top);
            bottom = Math.Max(bottom, item.Box.Bottom);
        }
        if (double.IsInfinity(top)) { top = box.SystemTop; bottom = box.SystemBottom; }
        var y0 = Math.Max(Math.Max(0, box.SystemTop - 24), top - 6);
        var y1 = Math.Min(Tab ? box.SystemBottom + 24 : box.TabTop + 4, bottom + 6);
        if (y1 < y0 + 20) y1 = y0 + 20;
        return new Rect(x0, y0, Math.Max(20, x1 - x0), y1 - y0);
    }
}

/// <summary>The automatic checks of one bar in one view: geometry (collisions, clipping, gaps) and data-versus-drawing consistency.</summary>
internal sealed class BarChecker
{
    private readonly TrackViewAudit V;
    private readonly int Bar;
    private readonly MeasureModel M;
    private readonly SongProject P;
    private readonly TrackModel Trk;
    private readonly List<Item> Items;
    private readonly List<Item> Texts;
    private readonly List<string> Lines;
    private readonly double X, W, K;
    private readonly TabEditorControl.AuditSystemBox Box;
    private readonly List<(int Voice, int Index, TabCell Cell)> Cells = new();
    private readonly IReadOnlyList<(int Voice, StaffNotationBeat Beat)> Beats;
    private readonly List<BarIssue> Issues = new();
    private readonly bool N, T, Drum;

    public BarChecker(TrackViewAudit view, int bar)
    {
        V = view; Bar = bar; P = view.Project; Trk = view.Track; M = Trk.Measures[bar];
        N = view.Staff; T = view.Tab; Drum = Trk.Kind == TrackKind.Drums; K = view.K;
        var position = view.Layout.Measure(bar);
        X = position.X; W = position.Width;
        Box = view.Editor.AuditSystemMetrics(position.SystemIndex);
        Items = view.BarItems(bar);
        Texts = Items.Where(i => i.Kind == Kind.Text).ToList();
        // one string per text line (runs of one line share a baseline); spaces are dropped because a font fallback splits a run at them
        Lines = Texts.GroupBy(t => Math.Round(t.BaseY * 2) / 2).OrderBy(g => g.Key)
            .Select(g => NoSpace(string.Concat(g.OrderBy(t => t.Box.X).Select(t => t.Label)))).ToList();
        for (var i = 0; i < M.Cells.Count; i++) Cells.Add((0, i, M.Cells[i]));
        if (M.Voice2Cells.Any(c => c.Notes.Count > 0)) for (var i = 0; i < M.Voice2Cells.Count; i++) Cells.Add((1, i, M.Voice2Cells[i]));
        Beats = view.Editor.AuditBeats(bar);
    }

    public List<BarIssue> Run()
    {
        Geometry();
        BarLevel();
        BeatLevel();
        if (!Drum) TechniqueChecks();
        // a simile bar shows only its sign: the notes behind it (a copy for playback) are deliberately not drawn
        if (M.SimileOneBar || M.SimileTwoBar) Issues.RemoveAll(i => i.Type.StartsWith("missing:", StringComparison.Ordinal) && i.Type != "missing:simile");
        return Issues;
    }

    // ---------------- helpers ----------------

    private void Add(string type, string detail) => Issues.Add(new BarIssue(V.View, type, detail));
    private void Miss(string feature, string detail) => Add("missing:" + feature, detail);
    private void Extra(string feature, string detail) => Add("extra:" + feature, detail);
    private static string NoSpace(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
    private bool HasText(string s) { var want = NoSpace(s); return Lines.Any(l => l.Contains(want, StringComparison.Ordinal)); }
    /// <summary>A short slanted stroke of the given pen width and horizontal extent (tremolo slashes), not a rest's curl or a stem.</summary>
    private static bool Slash(Item i, double thickness, double width) => i.Kind == Kind.Line && Diagonal(i) && Thick(i, thickness, 0.03) &&
        Math.Abs(Math.Abs(i.Paths[0][0].X - i.Paths[0][1].X) - width) < 0.6;
    private int CountSub(string s) => Texts.Sum(t => Occurrences(t.Label, s));
    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }
    private static double CenterX(Item i) => i.Box.X + i.Box.Width / 2;
    private static double CenterY(Item i) => i.Box.Y + i.Box.Height / 2;
    private static bool Diagonal(Item i) => i.Paths.Count == 1 && i.Paths[0].Length == 2 &&
        Math.Abs(i.Paths[0][0].X - i.Paths[0][1].X) > 0.5 && Math.Abs(i.Paths[0][0].Y - i.Paths[0][1].Y) > 0.5;
    private static bool Horizontal(Item i) => i.Paths.Count == 1 && i.Paths[0].Length == 2 && Math.Abs(i.Paths[0][0].Y - i.Paths[0][1].Y) < 0.05;
    private static bool Vertical(Item i) => i.Paths.Count == 1 && i.Paths[0].Length == 2 && Math.Abs(i.Paths[0][0].X - i.Paths[0][1].X) < 0.05;
    private static bool Thick(Item i, double value, double tolerance = 0.06) => Math.Abs(i.Thickness - value) <= tolerance;

    /// <summary>Data says <paramref name="expected"/>, the drawing shows <paramref name="drawn"/>: fewer is missing, more (when exact) is extra.</summary>
    private void Count(string feature, int expected, int drawn, string what, bool exact = true)
    {
        if (drawn < expected) Miss(feature, $"{what}: data {expected}, drawn {drawn}");
        else if (exact && drawn > expected) Extra(feature, $"{what}: data {expected}, drawn {drawn}");
    }

    private IEnumerable<(int Voice, int Index, TabCell Cell)> Sounding => Cells.Where(c => c.Cell.Notes.Count > 0);
    private IEnumerable<TabNote> AllNotes => Cells.SelectMany(c => c.Cell.Notes);

    // ---------------- geometry ----------------

    private void Geometry()
    {
        foreach (var c in V.CollisionsOf(Bar))
            Add("collision", c.ToString());
        foreach (var item in Items)
        {
            if (item.Box.Left < -0.5 || item.Box.Right > Box.PageWidth + 0.5)
                Add("clip:page", $"{Describe(item)} leaves the page [0..{Box.PageWidth:0}]");
            if (item.Box.Top < Box.SystemTop - 3 || item.Box.Bottom > Box.SystemBottom + 3)   // a few px may use the system gap; more reaches the next system's labels
                Add("clip:system", $"{Describe(item)} leaves its system slice [{Box.SystemTop:0}..{Box.SystemBottom:0}]");
            if (!T && item.Box.Bottom > Box.TabTop - 6 + 0.5 && item.Box.Top < Box.TabTop - 6)
                Add("clip:staff-region", $"{Describe(item)} reaches into the tab band");
            if (item.Kind == Kind.Text && (item.Box.Left < X - 1.5 || item.Box.Right > X + W + 1.5) && item.Label.Trim().Length > 0)
                Add("clip:barline", $"{Describe(item)} crosses the bar line [{X:0}..{X + W:0}]");
        }
        // marks too close: texts that do not overlap (the collision audit covers that) but sit closer than the minimum gap
        for (var i = 0; i < Texts.Count; i++)
            for (var j = i + 1; j < Texts.Count; j++)
            {
                var a = Texts[i]; var b = Texts[j];
                var boxA = InkBox(a); var boxB = InkBox(b);
                var dx = Math.Max(boxA.Left, boxB.Left) - Math.Min(boxA.Right, boxB.Right);   // < 0: the boxes overlap horizontally
                var dy = Math.Max(boxA.Top, boxB.Top) - Math.Min(boxA.Bottom, boxB.Bottom);
                if (dx < 0 && dy < 0) continue;                                                     // overlapping: the collision audit's job
                if (Math.Abs(a.Size - 19 * K) < 0.6 * K && Math.Abs(b.Size - 19 * K) < 0.6 * K) continue;   // time signature: numerator stacked on denominator by design
                if (Math.Abs(a.BaseY - b.BaseY) < 0.01 && dy < 0 && dx < 0.6) continue;           // two runs of one text line
                if (dx < 0 && dy < TrackViewAudit.MinGap || dy < 0 && dx < TrackViewAudit.MinGap)
                    Add("gap", $"\"{a.Label}\" and \"{b.Label}\" are {Math.Max(dx, dy):0.0} px apart (minimum {TrackViewAudit.MinGap:0.0})");
            }
    }

    /// <summary>A lone accent glyph ("^", ">") occupies only a small part of its text line box: its real outline bounds are used (audit only, never drawn).</summary>
    private static Rect InkBox(Item t)
    {
        if (t.Label is not ("^" or ">") || t.Size <= 0) return t.Box;
        var text = new FormattedText(t.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(t.Font.Length > 0 ? t.Font : "Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), t.Size, Brushes.Black, 1.0);
        var ink = text.BuildGeometry(t.Box.TopLeft).Bounds;
        return ink.IsEmpty ? t.Box : Rect.Intersect(ink, t.Box) is { IsEmpty: false } clipped ? clipped : t.Box;
    }

    private static string Describe(Item i) => $"{i.Kind}{(i.Label.Length > 0 ? " \"" + i.Label + "\"" : "")} [{i.Box.X:0}..{i.Box.Right:0}, {i.Box.Y:0}..{i.Box.Bottom:0}]";

    // ---------------- bar-level marks ----------------

    private void BarLevel()
    {
        var staves = (N ? 1 : 0) + (T ? 1 : 0);
        // section title
        var marker = P.Markers.FirstOrDefault(m => m.MeasureIndex == Bar);
        var title = marker?.Title ?? M.SectionName;
        if (V.Editor.Appearance.ShowSectionHeadings && !string.IsNullOrWhiteSpace(title) && !HasText(title)) Miss("section-title", $"\"{title}\"");

        // tempo change and swing
        var tempo = M.TempoChange is { } tc ? $"♩ = {tc}" : Bar == 0 && P.Tempo > 0 ? $"♩ = {P.Tempo}" : null;
        var feel = TripletFeels.Effective(M);
        var swing = feel != TripletFeels.None ? ScoreMarkText.SwingSymbol(feel) : null;
        if (tempo is not null && !HasText(tempo)) Miss("tempo", tempo);
        var expectedNotes = (tempo is not null ? 1 : 0) + (M.MidBarTempos?.Count ?? 0) + (swing is not null && swing.Contains('♩') ? 1 : 0);
        if (CountSub("♩") > expectedNotes) Extra("tempo", $"a tempo mark is drawn but the data has none ({CountSub("♩")} vs {expectedNotes})");
        if (M.MidBarTempos is { Count: > 0 } mid)
            foreach (var t in mid)
                if (!HasText($"♩ = {t.Tempo}")) Miss("tempo-mid-bar", $"tempo change to {t.Tempo} at slot {t.Slot:0.#} has no mark");
        if (swing is not null && !HasText(swing)) Miss("swing", swing);
        if (swing is null && CountSub("♫") + CountSub("♬") > 0) Extra("swing", "a swing symbol without triplet feel");

        // time and key signature (on the staff)
        var effNum = M.TimeSigNum ?? P.TimeSignatureNumerator; var effDen = M.TimeSigDenom ?? P.TimeSignatureDenominator;
        var previous = Bar > 0 ? Trk.Measures[Bar - 1] : null;
        var timeShown = Bar == 0 || effNum != (previous!.TimeSigNum ?? P.TimeSignatureNumerator) || effDen != (previous.TimeSigDenom ?? P.TimeSignatureDenominator);
        if (N)
        {
            var digits = Texts.Where(t => t.Font != "Consolas" && t.Label.Length > 0 && t.Label.All(char.IsDigit) && Math.Abs(t.Size - 19 * K) < 0.6 * K).Select(t => t.Label).OrderBy(s => s).ToList();
            var want = timeShown ? new[] { effNum.ToString(CultureInfo.InvariantCulture), effDen.ToString(CultureInfo.InvariantCulture) }.OrderBy(s => s).ToList() : new List<string>();
            if (!digits.SequenceEqual(want)) Add(digits.Count < want.Count ? "missing:time-signature" : "extra:time-signature", $"data {(timeShown ? effNum + "/" + effDen : "none")}, drawn [{string.Join(",", digits)}]");

            var key = M.KeySignature ?? P.KeySignature; var prevKey = previous is null ? 0 : previous.KeySignature ?? P.KeySignature;
            var keyChanges = Bar == 0 || key != prevKey || (M.KeySignatureMinor ?? P.KeySignatureMinor) != (previous!.KeySignatureMinor ?? P.KeySignatureMinor);
            var (naturals, accidentals) = keyChanges ? ScoreClefKey.KeySignatureGlyphs(prevKey, key) : (0, 0);
            var expectedKey = naturals + accidentals;
            var zoneRight = X + 30 + expectedKey * 10.5 + 8;
            var drawnKey = Texts.Where(t => Math.Abs(t.Size - 15 * K) < 0.6 * K && CenterX(t) >= X + 24 && CenterX(t) <= zoneRight)
                .Sum(t => t.Label.Count(ch => ch is '♯' or '♭' or '♮'));
            // A note's own accidental can sit in the zone too (no signature, first note close to the bar line): leave out the ones the notation drew for notes.
            var noteAccidentals = Beats.SelectMany(b => b.Beat.Notes).Where(n => n.Accidental is not null)
                .Select(n => n.X - StaffNotationRenderer.HeadRadiusX - 6.5 - n.AccidentalColumn * 10)
                .Count(ax => ax >= X + 24 && ax <= zoneRight);
            Count("key-signature", expectedKey, Math.Max(0, drawnKey - noteAccidentals), $"key {key} (was {prevKey})");
        }

        // repeat signs, double bar, alternate ending, directions, free time, simile
        var edgeStart = Math.Round(X) + 1.6; var edgeEnd = Math.Round(X + W) - 1.6;
        int Dots(double cx) => Items.Count(i => i.Kind == Kind.Head && i.Thickness == 0 && Math.Abs(i.Box.Width - 3.8) < 0.4 && Math.Abs(CenterX(i) - cx) < 1.5);
        int Heavy(double cx) => Items.Count(i => i.Kind == Kind.Line && Thick(i, 3.2, 0.15) && Math.Abs(CenterX(i) - cx) < 1.0);
        Count("repeat-start", M.RepeatStart ? 2 * staves : 0, Dots(edgeStart + 9), "repeat dots at the bar start");
        Count("repeat-start", M.RepeatStart ? staves : 0, Heavy(edgeStart), "repeat bar line at the bar start");
        Count("repeat-end", M.RepeatEnd ? 2 * staves : 0, Dots(edgeEnd - 9), "repeat dots at the bar end");
        Count("repeat-end", M.RepeatEnd ? staves : 0, Heavy(edgeEnd), "repeat bar line at the bar end");
        if (M.RepeatEnd && M.RepeatCount > 2 && !HasText($"x{M.RepeatCount}")) Miss("repeat-count", $"x{M.RepeatCount}");
        var doubleX = Math.Round(X + W) + 3.5;
        Count("double-bar", M.IsDoubleBar ? staves : 0,
            V.SystemItems(V.Layout.Measure(Bar).SystemIndex).Count(i => i.Kind == Kind.Line && Vertical(i) && Thick(i, 1.4) && Math.Abs(CenterX(i) - doubleX) < 0.4), "double bar line");

        var volta = M.AlternateEnding > 0 || M.AlternateEndingMask != 0;
        var bracket = V.SystemItems(V.Layout.Measure(Bar).SystemIndex).Count(i => i.Kind == Kind.Line && Horizontal(i) && Thick(i, 1.0, 0.05) &&
            i.Box.Bottom < Box.StaffTop - 12 && Math.Abs(i.Box.Width - (W - 2)) < 2.5 && Math.Abs(i.Box.X - (X + 1)) < 2.5);
        Count("volta-bracket", volta ? 1 : 0, bracket, $"ending \"{M.EndingLabel}\"");
        if (volta)
        {
            var openStart = previous is not null && (previous.AlternateEnding > 0 || previous.AlternateEndingMask != 0) &&
                previous.AlternateEnding == M.AlternateEnding && previous.AlternateEndingMask == M.AlternateEndingMask;
            if (!openStart && !HasText(M.EndingLabel)) Miss("volta-label", $"\"{M.EndingLabel}\"");
        }

        foreach (var raw in M.Directions.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var text = ScoreMarkText.DirectionText(raw).Text;
            if (!HasText(text)) Miss("direction", $"{raw} (\"{text}\")");
        }
        if (M.FreeTime && !HasText("free")) Miss("free-time", "free");
        // the simile mark and the coda sign are drawn with the same character (U+1D10C); a coda direction adds one (or two) of them
        const string CodaSign = "\U0001D10C";
        var codas = M.Directions.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Sum(raw => Occurrences(ScoreMarkText.DirectionText(raw).Text, CodaSign));
        var similes = (M.SimileOneBar ? 1 : 0) + (M.SimileTwoBar ? 2 : 0);
        // The repeat-bar sign is vector drawing (one heavy slash per repeated bar, centred in the bar), so only coda directions show up as glyphs.
        Count("coda", codas, CountSub(CodaSign), "coda mark");
        var simileSlashes = Items.Count(i => i.Kind == Kind.Line && Diagonal(i) && Thick(i, 2.6, 0.1) && Math.Abs(CenterX(i) - (X + W / 2)) < 12);
        Count("simile", similes, simileSlashes, "repeat-bar sign slashes");

        // dynamics (first note of the track, then each change)
        var expectedDynamics = V.Editor.Appearance.ShowDynamics ? V.DynamicsFor(Bar) : new List<string>();
        var left = new List<string>(expectedDynamics);
        foreach (var name in Texts.Where(t => t.Font.Contains("Times", StringComparison.OrdinalIgnoreCase)).Select(t => t.Label))
            if (!left.Remove(name)) Extra("dynamic", $"\"{name}\" is drawn but not in the data");
        foreach (var name in left) Miss("dynamic", $"\"{name}\" (first note or change of dynamic)");

        // chord names, beat text, lyrics
        foreach (var (voice, index, cell) in Cells)
        {
            var rest = cell.Notes.Count == 0 ? " (beat is a rest or empty)" : "";
            if (!string.IsNullOrWhiteSpace(cell.ChordName) && !HasText(cell.ChordName!)) Miss("chord-name", $"\"{cell.ChordName}\" on beat {index + 1}{rest}");
            if (!string.IsNullOrWhiteSpace(cell.Text) && !HasText(cell.Text!)) Miss("beat-text", $"\"{cell.Text}\" on beat {index + 1}{rest}");
            if (T && !string.IsNullOrWhiteSpace(cell.Lyrics))
                foreach (var line in cell.Lyrics.Split('\n').Take(3))
                    if (line.Length > 0 && !HasText(line)) Miss("lyrics", $"\"{line}\" on beat {index + 1}{rest}");
        }
    }

    // ---------------- per-beat: fret numbers, noteheads, rests ----------------

    private void BeatLevel()
    {
        var beatX = new Dictionary<(int, int), double>();
        foreach (var (voice, beat) in Beats) beatX[(voice, beat.CellIndex)] = beat.CenterX;
        var stringGap = Box.StringGapPx;

        if (T && !Drum && Trk.StringTunings.Count > 0)
        {
            // fret numbers: digits / X / (n) sitting on a string line (the score font is used for them, so the text itself decides)
            bool FretLike(Item t) => t.Label.Length > 0 && t.Size >= 8 * K && (t.Label.All(char.IsDigit) || t.Label == "X" || t.Label.Length > 2 && t.Label[0] == '(' && t.Label[^1] == ')' && t.Label[1..^1].All(ch => char.IsDigit(ch) || ch == 'X')) &&
                Enumerable.Range(0, Trk.StringTunings.Count).Any(s => Math.Abs(CenterY(t) - (Box.TabTop + s * stringGap)) <= 4.5) && t.Box.Top > Box.TabTop - 12 && t.Box.Bottom < Box.TabBottom + 12;
            var drawn = Texts.Where(FretLike).ToList();
            foreach (var (voice, index, cell) in Sounding)
                foreach (var note in cell.Notes)
                {
                    if (note.StringIndex < 0 || note.StringIndex >= Trk.StringTunings.Count) continue;
                    if (!beatX.TryGetValue((voice, index), out var cx)) { Miss("fret-number", $"beat {index + 1} has no laid-out position"); continue; }
                    if ((note.Tied || cell.IsTied) && !note.IsGraceNote && N) continue;   // a tied-to note prints no fret number in the TAB under a notation staff
                    var graceBeside = note.IsGraceNote && cell.Notes.Any(o => !o.IsGraceNote);
                    var label = note.Dead ? "X" : note.Ghost ? $"({note.Fret})" : note.Fret.ToString(CultureInfo.InvariantCulture);
                    var at = drawn.FindIndex(t => t.Label == label && (graceBeside ? CenterX(t) < cx - 9 && CenterX(t) > cx - 28 : Math.Abs(CenterX(t) - cx) <= 3.5) &&
                        Math.Abs(CenterY(t) - (Box.TabTop + note.StringIndex * stringGap)) <= stringGap * 0.6);
                    if (at >= 0) drawn.RemoveAt(at);
                    else Miss("fret-number", $"string {note.StringIndex + 1} fret {label} on beat {index + 1}");
                    if (note.Techniques.Contains("Trill") && ScoreMarkText.TrillFret(Trk, note) is var tf and >= 0)
                    {
                        var trill = drawn.FindIndex(t => t.Label == $"({tf})" && CenterX(t) - cx is >= 2 and <= 24 && Math.Abs(CenterY(t) - (Box.TabTop + note.StringIndex * stringGap)) <= 5);
                        if (trill >= 0) drawn.RemoveAt(trill);
                        else Miss("trill-fret", $"({tf}) beside string {note.StringIndex + 1} on beat {index + 1}");
                    }
                }
            foreach (var leftover in drawn) Extra("fret-number", $"\"{leftover.Label}\" at x {CenterX(leftover):0} is not in the data");
        }

        if (N)
        {
            foreach (var (voice, index, cell) in Sounding)
            {
                if (cell.Notes.All(n => n.IsGraceNote)) continue;
                if (!beatX.TryGetValue((voice, index), out var cx)) { Miss("notehead", $"beat {index + 1} has no laid-out position"); continue; }
                if (!Items.Any(i => Math.Abs(CenterX(i) - cx) <= 12 && (i.Kind is Kind.Head or Kind.Shape || Diagonal(i))))
                    Miss("notehead", $"beat {index + 1}: no notehead drawn near x {cx:0}");
            }
        }
        else if (T)
        {
            var expectedRests = Cells.Count(c => c.Cell.IsRest && c.Cell.Notes.Count == 0 && (N || c.Voice == 0));   // tab only shows the first voice's rests
            var drawnRests = 0;
            foreach (var t in Texts)
                for (var at = 0; at < t.Label.Length; at++)
                    if (char.IsHighSurrogate(t.Label[at]) && at + 1 < t.Label.Length && char.ConvertToUtf32(t.Label[at], t.Label[at + 1]) is >= 0x1D13B and <= 0x1D141) { drawnRests++; at++; }
            Count("rest", expectedRests, drawnRests, "rest glyphs");
        }
    }

    // ---------------- techniques and per-note marks ----------------

    private static bool Representable(string technique, IEnumerable<(int Voice, int Index, TabCell Cell)> cells)
    {
        if (TechniqueNames.IsPalmMute(technique) || technique is "LetRing" or "Vibrato" or "WideVibrato" or "HOPO" or "HOPOOrigin" or "HOPODestination" or "Tapping" or "LeftTap" or "FadeIn" or "FadeOut" or "Tie" or "Ghost" or "Dead") return true;
        if (TabEditorControl.AuditIsGeometryTechnique(technique) || TabSlideNotationProbe.IsGeometry(technique) || TabEditorControl.AuditShortTechnique(technique).Length > 0) return true;
        return technique switch
        {
            "Accent" => cells.Any(c => c.Cell.Accent == 1),
            "HeavyAccent" => cells.Any(c => c.Cell.Accent == 2),
            "Staccato" => cells.Any(c => c.Cell.Staccato),
            "Tenuto" => cells.Any(c => c.Cell.Tenuto),
            _ => false
        };
    }

    private void TechniqueChecks()
    {
        var sounding = Sounding.ToList();
        bool Any(Func<TabNote, bool> f) => AllNotes.Any(f);
        int CellsWith(Func<TabNote, bool> f) => sounding.Count(c => c.Cell.Notes.Any(f));
        var sys = V.SystemItems(V.Layout.Measure(Bar).SystemIndex);

        foreach (var name in AllNotes.SelectMany(n => n.Techniques).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!Representable(name, Cells)) Miss("technique-glyph", $"technique \"{name}\" has no drawn glyph or mark");

        // fermata, accents, staccato, tenuto
        // The staff engraves each voice's fermata (voice 2 inverted, below); tab only draws one per onset both voices share.
        bool SharedFermata((int Voice, int Index, TabCell Cell) c) => !N && c.Voice == 1 && Beats.Any(b => b.Voice == 1 && b.Beat.CellIndex == c.Index &&
            Beats.Any(o => o.Voice == 0 && o.Beat.Cell.Fermata && Math.Abs(o.Beat.StartSlots - b.Beat.StartSlots) < 0.001));
        var fermataCells = Cells.Count(c => c.Cell.Fermata && !SharedFermata(c));
        var fermataOnRests = Cells.Count(c => c.Cell.Fermata && !SharedFermata(c) && c.Cell.Notes.Count == 0);
        var fermataDrawn = CountSub("𝄐") + CountSub("𝄑");
        if (fermataDrawn < fermataCells) Miss("fermata", $"data {fermataCells} ({fermataOnRests} on rests), drawn {fermataDrawn}");
        else if (fermataDrawn > fermataCells) Extra("fermata", $"data {fermataCells}, drawn {fermataDrawn}");
        Count("accent", sounding.Count(c => c.Cell.Accent == 1), Texts.Count(t => t.Label == ">"), "accents (>)");
        Count("accent", sounding.Count(c => c.Cell.Accent == 2), Texts.Count(t => t.Label == "^"), "heavy accents (^)");
        var staccato = sounding.Count(c => c.Cell.Staccato); var tenuto = sounding.Count(c => c.Cell.Tenuto);
        if (N)
        {
            if (staccato > 0 && !Items.Any(i => i.Kind == Kind.Head && Math.Abs(i.Box.Width - 3.0) < 0.35)) Miss("staccato", $"{staccato} staccato beat(s), no dot drawn");
            var tenutoLines = Items.Count(i => i.Kind == Kind.Line && Horizontal(i) && Thick(i, 1.6, 0.05) && Math.Abs(i.Box.Width - 7) < 1);
            if (tenuto > 0 && tenutoLines == 0) Miss("tenuto", $"{tenuto} tenuto beat(s), no dash drawn");
            if (tenuto == 0 && tenutoLines > 0) Extra("tenuto", "a tenuto dash without data");
        }
        else
        {
            Count("staccato", staccato, Texts.Count(t => t.Label == "•"), "staccato dots");
            Count("tenuto", tenuto, Texts.Count(t => t.Label == "—"), "tenuto dashes");
        }

        // octave shift (notation)
        if (N)
        {
            var shifts = Cells.Count(c => c.Cell.OctaveShiftSemitones is -24 or -12 or 12 or 24);
            var labels = Texts.Count(t => t.Label is "8va" or "8vb" or "15ma" or "15mb");
            if (shifts > 0 && labels == 0) Miss("octave-shift", $"{shifts} beat(s) with an 8va/15ma shift, no label");
            if (shifts == 0 && labels > 0) Extra("octave-shift", "an 8va/15ma label without data");
        }

        // tuplets (notation): the ratio's number above or below each group
        if (N)
        {
            var wanted = Cells.Where(c => c.Cell.Tuplet.Numerator >= 2).Select(c => c.Cell.Tuplet.Numerator.ToString(CultureInfo.InvariantCulture)).Distinct().OrderBy(s => s).ToList();
            var drawn = Texts.Where(t => t.Label.Length > 0 && t.Label.All(char.IsDigit) && Math.Abs(t.Size - 11 * K) < 0.4 * K && t.Box.Bottom < Box.TabTop - 12).Select(t => t.Label).Distinct().OrderBy(s => s).ToList();
            foreach (var n in wanted.Except(drawn)) Miss("tuplet", $"tuplet {n}: no number drawn");
            foreach (var n in drawn.Except(wanted)) Extra("tuplet", $"a tuplet number {n} without a tuplet in the data");
        }

        // palm mute and let ring
        var palm = Any(n => n.Techniques.Any(TechniqueNames.IsPalmMute));
        // a passage is one line per system that can run over several bars: any dashed line overlapping this bar counts
        bool OverBar(Item i) => i.Box.Right > X + 1 && i.Box.Left < X + W - 1;
        var palmLines = T && !N ? sys.Count(i => i.Kind == Kind.DashedLine && OverBar(i) && Math.Abs(CenterY(i) - (Box.TabTop - 12)) <= 2.5)
            : sys.Count(i => i.Kind == Kind.DashedLine && OverBar(i) && i.Box.Top > Box.StaffBottom);
        // the notation-only view draws no palm-mute passages by design (the editor leaves them to the tab)
        if (T && palm && palmLines == 0) Miss("palm-mute", "palm mute in the data, no dashed P.M. line drawn");
        if (T && !palm && palmLines > 0 && !N) Extra("palm-mute", "a dashed P.M. line without palm mute in the data");
        var letRing = Any(n => n.Techniques.Contains("LetRing"));
        if (T)
        {
            if (letRing && CountSub("let ring") == 0) Miss("let-ring", "let ring in the data, no label drawn");
            if (!letRing && CountSub("let ring") > 0) Extra("let-ring", "a let ring label without data");
        }
        if (Any(n => n.Techniques.Contains("FadeIn") || n.Techniques.Contains("FadeOut")))
        {
            var lineY = T ? Box.TabTop - 8 : Box.StaffBottom + 20;
            if (!sys.Any(i => i.Kind == Kind.Line && OverBar(i) && Thick(i, 0.9, 0.05) && (T ? Math.Abs(CenterY(i) - lineY) < 5 : CenterY(i) > lineY - 8))) Miss("fade", "fade in/out in the data, no hairpin drawn");
        }

        // harmonics
        foreach (var caption in Sounding.SelectMany(c => c.Cell.Notes).Select(n => ScoreMarkText.HarmonicCaption(n.Techniques)).Where(s => s.Length > 0).Distinct())
            if (!HasText(caption)) Miss("harmonic", $"caption \"{caption}\"");
        if (T)
            foreach (var text in Sounding.SelectMany(c => c.Cell.Notes).Select(ScoreMarkText.HarmonicFretText).Where(s => s.Length > 0).Distinct())
                if (!HasText(text)) Miss("harmonic", $"harmonic value \"{text}\" under the tab");

        // technique label above the tab
        if (T)
            foreach (var (voice, index, cell) in sounding)
            {
                var label = ScoreMarkText.DrawnTechniqueLabel(cell.Notes, !N);
                if (label.Length > 0 && !HasText(label)) Miss("technique-label", $"\"{label}\" on beat {index + 1}");
            }

        // ties, bends, whammy, slides, vibrato, trill, wah/tap, brush/arpeggio, pick strokes
        if (N && Any(n => n.Tied) && !Items.Any(i => i.Kind == Kind.Curve)) Miss("tie", "tied notes in the data, no tie drawn");
        var bends = sounding.Count(c => c.Cell.Notes.Any(n => n.Techniques.Contains("Bend") || n.BendPoints.Count > 0 && !n.IsGraceNote));
        if (T && bends > 0 && !Items.Any(i => i.Kind == Kind.Shape && i.Box.Width < 9 && i.Box.Height < 9 && i.Box.Bottom < Box.TabBottom + 1))
            Miss("bend", $"{bends} bend beat(s), no bend arrow drawn");
        var whammy = Cells.Any(c => c.Cell.WhammyPoints.Count > 0) || Any(n => n.Techniques.Any(t => t.StartsWith("TremBar", StringComparison.OrdinalIgnoreCase)));
        if (T && whammy && !Items.Any(i => i.Kind == Kind.Curve && Thick(i, 1.0, 0.02))) Miss("whammy", "tremolo bar in the data, no whammy line drawn");
        var slides = Cells.Select(c => c.Voice).Distinct().Sum(v => TabSlideNotationProbe.Count(Trk, Bar, v));
        if (T && slides > 0 && !Items.Any(i => i.Kind == Kind.Line && Diagonal(i) && Thick(i, 1.05, 0.02) && i.Box.Top > Box.TabTop - 12 && i.Box.Bottom < Box.TabBottom + 12))
            Miss("slide", $"{slides} slide mark(s) in the data, no slide line drawn");
        var vibrato = Any(n => n.Techniques.Contains("Vibrato") || n.Techniques.Contains("WideVibrato"));
        var trills = sounding.Count(c => c.Cell.Notes.Any(n => n.Techniques.Contains("Trill")));
        var waves = Items.Count(i => i.Kind == Kind.Curve && (Thick(i, 1.5, 0.02) || Thick(i, 2.2, 0.02)));
        if (vibrato && waves == 0) Miss("vibrato", "vibrato in the data, no wavy line drawn");
        if (!vibrato && !(trills > 0 && T && !N) && waves > 0) Extra("vibrato", "a wavy line without vibrato in the data");
        Count("trill", trills, Texts.Count(t => t.Label == "tr"), "trill marks (tr)");
        var wah = CellsWith(n => n.Techniques.Contains("WahOpen") || n.Techniques.Contains("WahClose"));
        var tap = N ? CellsWith(n => n.Techniques.Contains("Tapping") || n.Techniques.Contains("LeftTap")) : 0;
        Count("wah-tap", wah + tap, Texts.Count(t => (t.Label == "+" || t.Label == "o") && t.Box.Bottom < Box.TabTop - 2), "wah / tapping marks (+ o)");
        var brush = CellsWith(n => n.Techniques.Contains("BrushDown") || n.Techniques.Contains("BrushUp") || n.Techniques.Contains("ArpeggioDown") || n.Techniques.Contains("ArpeggioUp"));
        if (brush > 0 && !Items.Any(i => i.Kind == Kind.Shape && i.Box.Width < 9 && i.Box.Height < 9)) Miss("brush-arpeggio", $"{brush} brush/arpeggio beat(s), no arrow drawn");
        if (T && brush > 0)
        {
            // GP5: the TAB arrow follows the stroke across the strings: a downstroke (bass string first) points up to the top string, an upstroke down.
            var beatAt = new Dictionary<(int, int), double>();
            foreach (var (v, b) in Beats) beatAt.TryAdd((v, b.CellIndex), b.CenterX);
            foreach (var (voice, index, cell) in Sounding)
            {
                var down = cell.Notes.Any(n => n.Techniques.Contains("BrushDown") || n.Techniques.Contains("ArpeggioDown"));
                var up = cell.Notes.Any(n => n.Techniques.Contains("BrushUp") || n.Techniques.Contains("ArpeggioUp"));
                if (down == up || !beatAt.TryGetValue((voice, index), out var bx)) continue;
                var mid = Box.TabTop + (cell.Notes.Min(n => n.StringIndex) + cell.Notes.Max(n => n.StringIndex)) / 2.0 * Box.StringGapPx;
                var head = Items.FirstOrDefault(i => i.Kind == Kind.Shape && i.Box.Width < 9 && i.Box.Height < 9 && Math.Abs(CenterX(i) - (bx - 10)) <= 4 &&
                    i.Box.Top > Box.TabTop - 20 && i.Box.Bottom < Box.TabBottom + 20);
                if (head is not null && (down ? CenterY(head) > mid : CenterY(head) < mid))
                    Extra("brush-direction", $"beat {index + 1}: the {(down ? "downstroke" : "upstroke")} arrow points {(down ? "down" : "up")} in the tab (GP5 points it {(down ? "up" : "down")})");
            }
        }
        if (T && CellsWith(n => n.Techniques.Contains("PickDown") || n.Techniques.Contains("PickUp")) > 0 &&
            !Items.Any(i => i.Kind == Kind.Line && Thick(i, 1.1, 0.03) && i.Box.Top > Box.TabBottom + 3)) Miss("pick-stroke", "pick stroke in the data, no mark under the tab");
        var slashes = sounding.Sum(c => ScoreMarkText.TremoloSlashCount(c.Cell));
        if (N) Count("tremolo-slash", slashes, Items.Count(i => Slash(i, 1.7, 9.6)), "tremolo slashes on the staff");
        if (T) Count("tremolo-slash", slashes, Items.Count(i => Slash(i, 1.6, 9.0)), "tremolo slashes on the tab");

        // grace notes
        var graces = sounding.Sum(c => c.Cell.Notes.Any(n => !n.IsGraceNote) ? Math.Min(3, c.Cell.Notes.Count(n => n.IsGraceNote)) : 0);
        if (N)
        {
            var graceSlashes = Items.Count(i => i.Kind == Kind.Line && Diagonal(i) && Thick(i, 0.8, 0.03));
            if (graces > 0 && graceSlashes == 0) Miss("grace-note", $"{graces} grace note(s), none drawn");
            if (graces == 0 && graceSlashes > 0 && !Cells.Any(c => c.Cell.IsGrace)) Extra("grace-note", "a grace note without data");
        }
        Count("grace-label", Cells.Count(c => c.Cell.IsGrace && !c.Cell.Notes.Any(n => n.IsGraceNote)), Texts.Count(t => t.Label == "gr"), "\"gr\" labels");
    }
}

/// <summary>TabSlideNotation is internal to Views; this probe exposes the two facts the audit needs.</summary>
internal static class TabSlideNotationProbe
{
    public static bool IsGeometry(string technique) => TabSlideNotation.IsRenderedAsGeometry(technique);
    /// <summary>Slide lines the TAB draws: a grace note's slide beside its main note is drawn as an arc between the two numbers instead (GP5).</summary>
    public static int Count(TrackModel track, int bar, int voice)
    {
        var cells = track.Measures[bar].CellsForVoice(voice);
        return TabSlideNotation.ForMeasure(track, bar, voice).Count(m => !(track.Kind != TrackKind.Drums && m.Source.IsGraceNote && m.SourceCellIndex < cells.Count && cells[m.SourceCellIndex].Notes.Any(o => !o.IsGraceNote)));
    }
}

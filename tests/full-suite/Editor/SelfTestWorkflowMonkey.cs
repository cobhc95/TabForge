using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

using K = System.Windows.Input.Key;

namespace TabForge;

// Owns: the seeded "human monkey" of the workflow area: thousands of human-weighted actions (typing, moving, deleting, marks,
//     dialogs, clipboard, undo bursts, tracks, bars, time signatures) through the window's key routing, palette and click path,
//     with the song's invariants checked after every action. A broken invariant is recorded (seed, step, last 15 actions) and
//     the run continues; one check per invariant reports the count and the shortest repro.
// Does not own: the input helpers (WorkflowKit.cs), product fixes.
// Tests: TestWorkflowMonkey (--areas workflow; --monkey-seeds N and --monkey-actions N scale it).
public static partial class SelfTest
{
    private sealed record MonkeyFailure(int Seed, int Step, string Song, string Detail, string[] Recent) { public int Count; }

    private static int MonkeyArg(string name, int fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var v)) return v;
        return fallback;
    }

    private static void TestWorkflowMonkey()
    {
        var seeds = MonkeyArg("--monkey-seeds", 20);
        var actions = MonkeyArg("--monkey-actions", 1300);
        var failures = new Dictionary<string, MonkeyFailure>(StringComparer.Ordinal);
        var total = 0;
        var watch = Stopwatch.StartNew();
        var scratch = SmScratch();
        try
        {
            for (var seed = MonkeyArg("--monkey-first", 1); seed <= seeds; seed++)
                total += MonkeyRun(seed, actions, seed % 3 == 0, failures, scratch);   // every third seed edits the demo song (about 4x slower per action)
        }
        finally { SmClean(scratch); }
        Log.Add($"  info  workflow monkey: {seeds} seeds x {actions} actions = {total} actions in {watch.Elapsed.TotalSeconds:0} s, {failures.Values.Sum(f => f.Count)} failures, {failures.Count} distinct invariants broken");
        foreach (var (invariant, f) in failures.OrderByDescending(kv => kv.Value.Count))
            Check($"workflow monkey: {invariant}", false,
                $"{f.Count} times; shortest repro seed {f.Seed} ({f.Song}) step {f.Step}: {f.Detail}; last actions: {string.Join(" > ", f.Recent)}");
        Check("workflow monkey: ran thousands of actions", total >= Math.Min(1000, seeds * actions), $"{total} actions");
    }

    private static int MonkeyRun(int seed, int actions, bool demo, Dictionary<string, MonkeyFailure> failures, string scratch)
    {
        var rng = new Random(seed);
        var w = SmNewWindow();
        var previousCapture = DialogHost.Capture;
        var previousMessages = DialogHost.MessageCapture;
        var errors = new List<string>();
        Exception? unhandled = null;
        void OnUnhandled(object? s, DispatcherUnhandledExceptionEventArgs e) { unhandled ??= e.Exception; e.Handled = true; }
        DialogHost.Capture = d => MonkeyAnswer(d, rng);
        DialogHost.MessageCapture = (caption, text) => errors.Add($"{caption}: {text}");
        Dispatcher.CurrentDispatcher.UnhandledException += OnUnhandled;
        var recent = new Queue<string>();
        var stray = new StrongBox<string?>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var songName = demo ? "demo" : "empty";
        var step = 0; long actTicks = 0, checkTicks = 0;
        try
        {
            SongProject song;
            if (demo) { song = FullDemoSongFactory.Create(); song.IsDirty = false; }
            else song = WfSong(4, 2);
            var s = new Wf { Window = w, Doc = SmOpenSong(w, song), Scenario = "monkey", Fast = true };
            s.Ed.SelectedTrackIndex = 0;
            s.Click(0, 0, 0);
            void Fail(string invariant, string detail)
            {
                if (failures.TryGetValue(invariant, out var known))
                {
                    known.Count++;
                    if (step >= known.Step) return;
                    failures[invariant] = new MonkeyFailure(seed, step, songName, detail, recent.ToArray()) { Count = known.Count };
                }
                else failures[invariant] = new MonkeyFailure(seed, step, songName, detail, recent.ToArray()) { Count = 1 };
            }
            MonkeyInvariants(s, step, scratch, Fail, errors, stray, known, ref unhandled, glyphs: true, roundTrips: false);
            for (step = 1; step <= actions; step++)
            {
                var t0 = Stopwatch.GetTimestamp();
                string name;
                // An exception thrown by an action is a failure; the run goes on from whatever state the song is in.
                try { name = MonkeyAct(s, rng, w, stray); }
                catch (Exception ex) { name = "(threw)"; unhandled ??= ex; }
                actTicks += Stopwatch.GetTimestamp() - t0;
                recent.Enqueue(name); while (recent.Count > 15) recent.Dequeue();
                t0 = Stopwatch.GetTimestamp();
                try { MonkeyInvariants(s, step, scratch, Fail, errors, stray, known, ref unhandled, glyphs: step % 25 == 0, roundTrips: true); }
                catch (Exception ex) { Fail("the invariant checks complete", $"{ex.GetType().Name}: {ex.Message}"); }
                checkTicks += Stopwatch.GetTimestamp() - t0;
            }
        }
        catch (Exception ex) { failures.TryAdd("the run itself completes", new MonkeyFailure(seed, step, songName, $"{ex.GetType().Name}: {ex.Message}", recent.ToArray()) { Count = 1 }); }
        finally
        {
            Dispatcher.CurrentDispatcher.UnhandledException -= OnUnhandled;
            DialogHost.Capture = previousCapture;
            DialogHost.MessageCapture = previousMessages;
            SmCloseWindow(w);
        }
        Log.Add($"  info  workflow monkey seed {seed} ({songName}): actions {actTicks * 1000.0 / Stopwatch.Frequency:0} ms, checks {checkTicks * 1000.0 / Stopwatch.Frequency:0} ms");
        return Math.Max(0, step - 1);
    }

    /// <summary>Answers a dialog the way a hurried user does: OK or Cancel; a time signature gets a new value.</summary>
    private static bool? MonkeyAnswer(Window d, Random rng)
    {
        if (d is PasteOptionsDialog) return true;
        if (d is ThemedConfirmDialog confirm) { confirm.AnswerForTest(rng.Next(2) == 0 ? MessageBoxResult.Yes : MessageBoxResult.No); return true; }
        var all = new List<DependencyObject>();
        void Walk(DependencyObject o) { all.Add(o); foreach (var c in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>()) Walk(c); }
        Walk(d);
        if (d.Title == "Time signature")
        {
            if (all.OfType<TextBox>().FirstOrDefault() is { } n) n.Text = new[] { 2, 3, 4, 5, 6, 7 }[rng.Next(6)].ToString();
            if (all.OfType<ComboBox>().FirstOrDefault() is { Items.Count: > 3 } den) den.SelectedIndex = 2 + rng.Next(2);
        }
        else if (rng.Next(3) == 0) return false;   // Cancel
        if (all.OfType<Button>().FirstOrDefault(b => b.IsDefault || Equals(b.Content, "OK")) is not { } ok) return false;
        try { ok.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        catch (InvalidOperationException) { }   // DialogResult on a dialog that was never shown modally; the result is already recorded
        return true;
    }

    /// <summary>One human-weighted action; returns its name for the repro trail. A mark key that adds a note on an empty string sets <paramref name="stray"/>.</summary>
    private static string MonkeyAct(Wf s, Random rng, MainWindow w, StrongBox<string?> stray)
    {
        var r = rng.Next(1000);
        string Key(Key k, ModifierKeys m = ModifierKeys.None) { s.Key(k, m); return (m == ModifierKeys.None ? "" : m + "+") + k; }
        string Twice(string name, Action act) { act(); if (rng.Next(3) == 0) { act(); return name + " x2"; } return name; }
        string Edit(string name, Action act)
        {
            act();
            if (rng.Next(12) == 0) { s.Ctrl(K.Z); return name + ", Ctrl+Z"; }   // changes their mind at once
            return name;
        }
        if (r < 200) { var f = rng.Next(10); return Edit("fret " + f, () => s.Fret(f)); }
        if (r < 240) { var f = 10 + rng.Next(15); return Edit("fret " + f, () => s.Fret(f)); }
        if (r < 270) return s.CursorCell?.Notes.FirstOrDefault(n => n.StringIndex == s.Ed.SelectedString) is { } same ? Edit("retype " + same.Fret, () => s.Fret(same.Fret)) : Key(K.Right);
        if (r < 400) return Key(rng.Next(2) == 0 ? K.Left : K.Right);
        if (r < 470) return Key(rng.Next(2) == 0 ? K.Up : K.Down);
        if (r < 500) return Key(rng.Next(2) == 0 ? K.Left : K.Right, ModifierKeys.Shift);
        if (r < 510) return Key(rng.Next(2) == 0 ? K.Home : K.End, ModifierKeys.Control);
        if (r < 520) { s.Ctrl(K.End); s.Key(K.End); s.Repeat(K.Right, 3); return "Ctrl+End, End, Right x3"; }
        if (r < 570) return Edit("Delete", () => s.Key(K.Delete));
        if (r < 600) return Edit("Backspace", () => s.Key(K.Back));
        if (r < 615) return Edit("Insert", () => s.Key(K.Insert));
        if (r < 650) { var longer = rng.Next(2) == 0; return Edit(longer ? "- (longer)" : "+ (shorter)", () => s.Key(longer ? K.OemMinus : K.OemPlus)); }
        if (r < 675)
        {
            var tool = new[] { "duration:whole", "duration:half", "duration:quarter", "duration:eighth", "duration:sixteenth", "duration:dotted", "duration:tuplet" }[rng.Next(7)];
            return Edit("tool " + tool, () => s.Tool(tool));
        }
        if (r < 700) return Twice(". (dot)", () => s.Key(K.OemPeriod));
        if (r < 715) return Twice("/ (triplet)", () => s.Key(K.OemQuestion));
        if (r < 745) return Twice("L (tie)", () => s.Key(K.L));
        if (r < 800)
        {
            var (k, label) = new (Key, string)[] { (K.Oem1, "accent"), (K.P, "P palm mute"), (K.V, "V vibrato"), (K.S, "S slide"), (K.H, "H hammer"), (K.O, "O ghost") }[rng.Next(6)];
            // With a selection the mark applies to the selected notes and the cursor may move, so the same beat is compared before and after.
            var cell = s.CursorCell;
            var empty = !s.Ed.HasSelection && (cell?.Notes.All(n => n.StringIndex != s.Ed.SelectedString) ?? true);
            var before = cell?.Notes.Count ?? 0;
            var name = Twice(label + (empty ? " on an empty string" : ""), () => s.Key(k));
            var after = cell?.Notes.Count ?? 0;
            if (empty && after > before) stray.Value = $"{label} on an empty string added {after - before} note(s)";
            return name;
        }
        if (r < 808) return Edit("X dead", () => s.Key(K.X));
        if (r < 815) { var harmonic = rng.Next(2) == 0; return Edit(harmonic ? "Y harmonic editor" : "G grace editor", () => s.Key(harmonic ? K.Y : K.G)); }
        if (r < 835)
        {
            var target = rng.Next(Math.Max(1, s.Track.StringTunings.Count));
            s.Click(s.Ed.SelectedMeasure, s.Ed.SelectedCell, s.Ed.SelectedString); s.Ed.ShiftClickExtend(s.Ed.SelectedMeasure, s.Ed.SelectedCell, target); SmSettle();
            return "drag inside the beat to string " + (target + 1);
        }
        if (r < 850)
        {
            var bar = s.Ed.SelectedMeasure; var to = Math.Min(s.Track.Measures.Count - 1, bar + rng.Next(2)); var cell = rng.Next(MusicTime.BarSlots(s.Song, to));
            s.Click(bar, s.Ed.SelectedCell, s.Ed.SelectedString); s.Ed.ShiftClickExtend(to, cell, s.Ed.SelectedString); SmSettle();
            return $"drag across beats to bar {to + 1} slot {cell}";
        }
        if (r < 870) return Key(K.C, ModifierKeys.Control);
        if (r < 880) return Edit("Ctrl+X", () => s.Ctrl(K.X));
        if (r < 900) return Edit("Ctrl+V", () => s.Ctrl(K.V));
        if (r < 935) { var n = rng.Next(1, 4); s.Repeat(K.Z, n, ModifierKeys.Control); return $"Ctrl+Z x{n}"; }
        if (r < 955) { var n = rng.Next(1, 3); s.Repeat(K.Y, n, ModifierKeys.Control); return $"Ctrl+Y x{n}"; }
        if (r < 965) { var t = rng.Next(Math.Max(1, s.Song.Tracks.Count)); SmField<DataGrid>(w, "TrackMixerGrid")!.SelectedIndex = t; SmSettle(); return "switch to track " + (t + 1); }
        if (r < 975) return Edit("Ctrl+Insert (insert bar)", () => s.Key(K.Insert, ModifierKeys.Control));
        if (r < 983) return s.Track.Measures.Count > 1 ? Edit("Ctrl+Delete (delete bar)", () => s.Key(K.Delete, ModifierKeys.Control)) : Key(K.Left);
        if (r < 988) return Edit("time signature", () => s.Tool("composition:time_signature"));
        if (r < 994) return Key(K.Escape);
        return Edit("R (rest)", () => s.Key(K.R));
    }

    /// <summary>The song's invariants after one action; each broken one is reported through <paramref name="fail"/>.</summary>
    private static void MonkeyInvariants(Wf s, int step, string scratch, Action<string, string> fail, List<string> errors, StrongBox<string?> stray, HashSet<string> known, ref Exception? unhandled, bool glyphs, bool roundTrips)
    {
        if (unhandled is not null) { fail("no exception escapes an action", $"{unhandled.GetType().Name}: {unhandled.Message} at {string.Join(" <- ", (unhandled.StackTrace ?? "").Split((char)10).Take(6).Select(l => l.Trim()))}"); unhandled = null; }
        if (errors.Count > 0) { fail("no error message is shown", errors[0]); errors.Clear(); }
        if (stray.Value is not null) { fail("a technique on an empty string adds no note", stray.Value); stray.Value = null; }
        var song = s.Song; var track = s.Ed.Track;
        if (track is null) { fail("the editor always has a track", ""); return; }
        var trackIndex = song.Tracks.IndexOf(track);
        // Defects the starting song already has are reported once at step 0 and then skipped (keyed by track and content, so undo snapshots and moved bars keep them known).
        void Structural(string owner, string invariant, string detail)
        {
            if (step == 0) { known.Add(owner); fail(invariant + " (in the starting song)", detail); }
            else if (!known.Contains(owner)) fail(invariant, detail);
        }
        // The edited track after every action; every track every 50 actions (a song-wide edit shows up within 50 steps).
        for (var t = 0; t < song.Tracks.Count; t++)
        {
            if (t != trackIndex && step % 50 != 0) continue;
            var tr = song.Tracks[t];
            for (var b = 0; b < tr.Measures.Count; b++)
            {
                var m = tr.Measures[b]; var slots = MusicTime.BarSlots(song, b);
                for (var i = 0; i < m.Cells.Count; i++)
                {
                    var cell = m.Cells[i];
                    if (cell.RhythmicPosition < 0 || !MusicTime.AllDenominators.Contains(cell.DurationDenominator) || cell.Dots is < 0 or > 2)
                        Structural($"{tr.Name}|{cell.RhythmicPosition}|{cell.DurationDenominator}|{cell.Dots}", "every beat has a valid position and duration", $"track {t + 1} bar {b + 1} cell {i}: pos {cell.RhythmicPosition}, 1/{cell.DurationDenominator}, dots {cell.Dots}");
                    if (cell.Notes.Count > 0 && !m.FreeTime && (cell.RhythmicPosition ?? i) >= slots - 0.01 && !MusicTime.AnalyzeBar(song, b, tr).Marked)   // a free-time bar has no length to pass
                        Structural($"{tr.Name}|past|{i}|{cell.RhythmicPosition}", "no note starts past the bar end in a bar that is not red", $"track {t + 1} bar {b + 1} cell {i} (pos {cell.RhythmicPosition}) of {slots} slots");
                    foreach (var n in cell.Notes)
                        if (tr.Kind != TrackKind.Drums && tr.MidiChannel != 9 && (n.Fret < 0 || n.Fret > tr.NumberOfFrets || n.StringIndex < 0 || n.StringIndex >= tr.StringTunings.Count))
                            Structural($"{tr.Name}|{n.StringIndex}|{n.Fret}", "every fret and string is inside the instrument", $"track {t + 1} '{tr.Name}' bar {b + 1} cell {i}: fret {n.Fret} string {n.StringIndex + 1} (frets {tr.NumberOfFrets}, strings {tr.StringTunings.Count})");
                }
                if (t == trackIndex && s.Ed.Layout.BarStateFor(tr, b).Marked != MusicTime.AnalyzeBar(song, b, tr).Marked)
                    fail("the drawn red bar matches MusicTime", $"bar {b + 1}: drawn {s.Ed.Layout.BarStateFor(tr, b)}, MusicTime {MusicTime.AnalyzeBar(song, b, tr)}");
            }
        }
        var ed = s.Ed;
        // The cursor is a cell index, not a slot: any real beat is a valid place, including one past the end of an overfull (red) bar, and the
        // empty spot after that bar's last beat, as GP5 (the user reaches it to fix the bar or to go on writing).
        bool onOverflowBeat() => s.Track.Measures[ed.SelectedMeasure].Cells.ElementAtOrDefault(ed.SelectedCell) is { } c && (c.Notes.Count > 0 || c.IsRest)
            || Views.Score.CursorPositions.Allowed(s.Track.Measures[ed.SelectedMeasure].Cells, MusicTime.BarSlots(song, ed.SelectedMeasure)).Contains(ed.SelectedCell);
        if (ed.SelectedMeasure < 0 || ed.SelectedMeasure >= track.Measures.Count || ed.SelectedCell < 0 || (ed.SelectedCell >= MusicTime.BarSlots(song, ed.SelectedMeasure) && !onOverflowBeat())
            || ed.SelectedString < 0 || ed.SelectedString >= Math.Max(1, track.StringTunings.Count))
            fail("the cursor stays inside the song", s.Cursor + $" of {track.Measures.Count} bars" + (ed.SelectedMeasure >= 0 && ed.SelectedMeasure < track.Measures.Count ? $"; bar ({MusicTime.BarSlots(song, ed.SelectedMeasure)} slots) {s.Dump(ed.SelectedMeasure)}; cells " + string.Join(" ", track.Measures[ed.SelectedMeasure].Cells.Select((c, i) => $"{i}@{c.RhythmicPosition}:{c.Notes.Count}{(c.IsRest ? "r" : "")}/{c.DurationDenominator}")) : ""));
        else if (!s.CursorInView(out var view)) fail("the cursor bar stays in view", $"{s.Cursor}: {view}");
        if (glyphs && track.Kind != TrackKind.Drums && track.MidiChannel != 9 && ed.SelectedMeasure < track.Measures.Count) MonkeyGlyphs(s, fail);
        if (roundTrips && step % 50 == 0 && s.Doc.Undo.CanUndo)
        {
            var before = Convert.ToHexString(ProjectService.ContentHash(song));
            s.Ctrl(System.Windows.Input.Key.Z); s.Ctrl(System.Windows.Input.Key.Y);
            var after = Convert.ToHexString(ProjectService.ContentHash(s.Song));
            if (before != after) fail("undo then redo returns the identical song", $"hash {before[..12]} -> {after[..12]}");
        }
        if (roundTrips && step % 300 == 0)
        {
            var path = Path.Combine(scratch, $"monkey-{step}.tforge");
            ProjectService.Save(path, song);
            var back = ProjectService.Load(path);
            if (!ProjectService.ContentHash(back).AsSpan().SequenceEqual(ProjectService.ContentHash(song)))
                fail("saving and reopening gives the identical song", path);
            File.Delete(path);
        }
    }

    /// <summary>Every fretted note in the cursor's system has a digit drawn (tied notes may hide their fret).</summary>
    private static void MonkeyGlyphs(Wf s, Action<string, string> fail)
    {
        var layout = s.Ed.Layout.GetLayout(s.Track);
        var system = layout.SystemForMeasure(s.Ed.SelectedMeasure);
        var bars = layout.Systems[system].Measures.Select(m => m.MeasureIndex).ToList();
        var notes = bars.Sum(b => ((TabForge.Views.Score.IEditorInputHost)s.Ed).CellsFor(s.Track.Measures[b], false).Sum(c => c.Notes.Count(n => !n.Tied && !c.IsTied && !n.Dead)));   // a beat tie ties every note (as the renderer and playback)
        var scroll = SmField<ScrollViewer>(s.Window, "ScoreScroll")!;
        s.Window.UpdateLayout(); SmSettle();
        var items = new List<LayoutAudit.Item>();
        foreach (var (sys, drawing) in s.Ed.AuditSystemDrawings()) if (sys == system) LayoutAudit.Walk(drawing, System.Windows.Media.Matrix.Identity, items);
        if (items.Count == 0) return;   // the system is not among those drawn (scrolled away): the in-view check reports that
        var digits = items.Count(i => i.Kind == LayoutAudit.Kind.Text && i.Label.Trim('(', ')').Length > 0 && i.Label.Trim('(', ')').All(char.IsDigit));
        if (digits < notes) fail("every note in view has its fret drawn", $"system {system} (bars {bars.First() + 1}-{bars.Last() + 1}): {notes} notes, {digits} digit glyphs"
            + "; notes " + string.Join(" ", bars.SelectMany(b => ((TabForge.Views.Score.IEditorInputHost)s.Ed).CellsFor(s.Track.Measures[b], false)).SelectMany(c => c.Notes.Select(n => $"{c.RhythmicPosition}/{c.DurationDenominator}:s{n.StringIndex}f{n.Fret}{(n.Tied ? "T" : "")}{(c.IsRest ? "R" : "")}[{string.Join(",", n.Techniques)}]")))
            + "; drawn text " + string.Join("|", items.Where(i => i.Kind == LayoutAudit.Kind.Text).Select(i => i.Label)));
    }
}

using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the input and observation helpers of the workflow scenarios (SelfTestWorkflow*.cs): key presses through the window's own
//     key router with explicit modifiers, tool-palette clicks, and what the user sees and hears after each step.
// Does not own: the scenarios themselves, product behaviour.
// Tests: TestWorkflow.
public static partial class SelfTest
{
    /// <summary>One workflow scenario's window and song: keys, clicks and observations all go through the real window.</summary>
    private sealed class Wf
    {
        public required MainWindow Window;
        public required DocumentSession Doc;
        public required string Scenario;
        public TabEditorControl Ed => SmField<TabEditorControl>(Window, "Editor")!;
        public SongProject Song => Doc.Project;
        public TrackModel Track => Ed.Track!;
        public string Step = "";
        /// <summary>One idle round per input instead of three (the monkey runs tens of thousands of inputs).</summary>
        public bool Fast;
        public void Settle() { if (Fast) System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { })); else SmSettle(); }

        /// <summary>
        /// A key press routed the way the window routes a real keystroke once its special cases are done (WindowKeyRouter: bound
        /// chords, then the editor's own keys, then single-key bindings), with the modifiers given here instead of the physical ones.
        /// </summary>
        public bool Key(Key key, ModifierKeys mods = ModifierKeys.None)
        {
            var maps = SmField<HotkeyMaps>(Window, "_hotkeys")!;
            var target = WindowKeyRouter.Dispatch(key, System.Windows.Input.Key.None, mods, Ed, maps.Global, id => (bool)SmCall(Window, "RunHotkey", id)!);
            Settle();
            return target != WindowKeyRouter.Target.None;
        }

        public void Keys(params Key[] keys) { foreach (var k in keys) Key(k); }
        public void Repeat(Key key, int times, ModifierKeys mods = ModifierKeys.None) { for (var i = 0; i < times; i++) Key(key, mods); }
        public bool Ctrl(Key key) => Key(key, ModifierKeys.Control);

        /// <summary>Types a fret the way a guitarist does: its digits.</summary>
        public void Fret(int fret) { foreach (var ch in fret.ToString()) Key(System.Windows.Input.Key.D0 + (ch - '0')); }

        /// <summary>A click on a tool-palette button (the handler its Button runs, with the button's tag).</summary>
        public void Tool(string id) { SmCall(Window, "ToolsPaletteButton_Click", new Button { Tag = id }, new System.Windows.RoutedEventArgs()); Settle(); }

        /// <summary>A click on a beat and string (the input controller's click path: clear selection, set cursor).</summary>
        public void Click(int bar, int cell, int stringIndex) { Ed.SelectForEdit(bar, cell, stringIndex); Settle(); }

        /// <summary>Moves the cursor to a string with Up / Down from wherever it is.</summary>
        public void ToString_(int stringIndex)
        {
            var d = stringIndex - Ed.SelectedString;
            Repeat(d < 0 ? System.Windows.Input.Key.Up : System.Windows.Input.Key.Down, Math.Abs(d));
        }

        public MeasureModel Bar(int bar) => Track.Measures[bar];
        public List<int> Beats(int bar) => MusicTime.BeatSlots(Bar(bar));
        public TabCell Cell(int bar, int cell) => Bar(bar).Cells[cell];
        public TabCell? CursorCell => Ed.SelectedMeasure < Track.Measures.Count && Ed.SelectedCell < Bar(Ed.SelectedMeasure).Cells.Count ? Bar(Ed.SelectedMeasure).Cells[Ed.SelectedCell] : null;
        public TabNote? NoteAt(int bar, int cell, int stringIndex) => Cell(bar, cell).Notes.FirstOrDefault(n => n.StringIndex == stringIndex);
        public BarState State(int bar) => MusicTime.AnalyzeBar(Song, bar, Track);
        public string Cursor => $"cursor bar {Ed.SelectedMeasure} cell {Ed.SelectedCell} string {Ed.SelectedString}";

        /// <summary>The bar as text: slot:frets@string/duration, for failure details.</summary>
        public string Dump(int bar) => string.Join(" ", Beats(bar).Select(i =>
        {
            var c = Cell(bar, i);
            var notes = c.IsRest || c.Notes.Count == 0 ? "r" : string.Join(",", c.Notes.Select(n => $"{n.Fret}@{n.StringIndex}{(n.Tied ? "~" : "")}"));
            return $"{i}:{notes}/{c.DurationDenominator}{new string('.', c.Dots)}{(c.IsTriplet ? "t" : "")}";
        })) + $" [{(State(bar).Error ? "over" : State(bar).Short ? "short" : State(bar).Complete ? "ok" : "partial")}]";

        /// <summary>Whether the cursor bar's system lies inside the score viewport (what the user sees).</summary>
        public bool CursorInView(out string detail)
        {
            var scroll = SmField<ScrollViewer>(Window, "ScoreScroll")!;
            Window.UpdateLayout();
            var top = Ed.SystemTopForMeasure(Ed.SelectedMeasure);
            var bottom = top + Ed.SystemHeightNow;
            detail = $"system {top:0}..{bottom:0}, viewport {scroll.VerticalOffset:0}..{scroll.VerticalOffset + scroll.ViewportHeight:0}";
            // A short score pane can be lower than one system: in view means at least a quarter of the smaller of the two is shown.
            var overlap = Math.Min(bottom, scroll.VerticalOffset + scroll.ViewportHeight) - Math.Max(top, scroll.VerticalOffset);
            return overlap >= Math.Min(bottom - top, scroll.ViewportHeight) / 4;
        }

        /// <summary>Counts of each drawn item kind in the systems the editor keeps drawn, with the cursor bar's system revealed.</summary>
        public Dictionary<LayoutAudit.Kind, int> Drawn(int revealBar)
        {
            var scroll = SmField<ScrollViewer>(Window, "ScoreScroll")!;
            scroll.ScrollToVerticalOffset(Ed.ScrollOffsetForMeasure(revealBar));
            Window.UpdateLayout(); SmSettle();
            var system = Ed.Layout.GetLayout(Track).SystemForMeasure(revealBar);
            var items = new List<LayoutAudit.Item>();
            foreach (var (s, drawing) in Ed.AuditSystemDrawings()) if (s == system) LayoutAudit.Walk(drawing, System.Windows.Media.Matrix.Identity, items);
            return Enum.GetValues<LayoutAudit.Kind>().ToDictionary(k => k, k => items.Count(i => i.Kind == k));
        }

        /// <summary>Note-ons the playback compiler schedules for a pitch in a bar (a tie sustains: one onset, not two).</summary>
        public int Onsets(int bar, int stringIndex, int fret)
        {
            var midi = TabForge.Views.Score.ScoreEditCommands.MidiOf(Track, stringIndex, fret);
            var timeline = MidiTimelineBuilder.Build(Song, new PlaybackOptions { Speed = 1.0, Metronome = false, CountIn = false });
            return timeline.Notes.Count(n => n.TrackIndex == Song.Tracks.IndexOf(Track) && n.Bar == bar && n.Midi == midi);
        }

        public void Begin(string step) => Step = step;

        /// <summary>One user-visible expectation; the GP5 reference behaviour is part of the check name, so a failure reads as a bug report.</summary>
        public void Expect(string what, bool ok, string gp5, string? detail = null) =>
            Check($"workflow: {Scenario} / {Step}: {what} (GP5: {gp5})", ok, detail);
    }

    private static SongProject WfSong(int bars, int tracks = 1)
    {
        var song = new SongProject { Tempo = 120 };
        for (var t = 0; t < tracks; t++) song.Tracks.Add(new TrackModel { Name = "Guitar " + (t + 1), Measures = TemplateFactory.Measures(bars) });
        song.IsDirty = false;
        return song;
    }

    /// <summary>Opens a fresh song as a new tab of the scenario window, cursor on bar 1, beat 1, string 1, quarter notes.</summary>
    private static Wf WfOpen(MainWindow window, string scenario, int bars = 4, int tracks = 1)
    {
        var doc = SmOpenSong(window, WfSong(bars, tracks));
        var wf = new Wf { Window = window, Doc = doc, Scenario = scenario };
        wf.Ed.SelectedTrackIndex = 0;
        wf.Ed.AutoAdvanceAfterEntry = false;
        wf.Click(0, 0, 0);
        wf.Tool("duration:quarter");
        return wf;
    }
}

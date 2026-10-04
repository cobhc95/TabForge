using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Pins for the score editor's behaviour, written before it is split into owned classes: pixel invariance across the layout and
/// frozen-drawing caches, which setters rebuild the layout, which repaints keep the retained system drawings, a scripted input
/// sequence, the automation peer, and what the control still holds after its window closes.
/// </summary>
public static partial class SelfTest
{
    private static IEnumerable<(string Name, SongProject Song)> PinSongs()
    {
        yield return ("demo", DemoSongFactory.Create());
        yield return ("technique", GmSongAudit.TechniqueSong());
    }

    /// <summary>The same pixels from a fresh editor, a warm one (playback tick, repaint) and after a layout rebuild: the caches change nothing.</summary>
    private static void TestTabEditorRenderInvariance()
    {
        var rethrow = TabEditorControl.RethrowRenderFailures;
        TabEditorControl.RethrowRenderFailures = true;
        try
        {
            var mismatches = new List<string>();
            var cases = 0;
            foreach (var (song, project) in PinSongs())
                foreach (var zoom in ScoreRenderIdentity.Scales)
                    foreach (var dark in new[] { true, false })
                        foreach (var state in new[] { "plain", "edit", "play" })
                        {
                            cases++;
                            // A case that differs once is drawn again with a fresh editor: on a cold machine the first use of a
                            // fallback font can land between two renders. A real cache bug differs on the second attempt too.
                            string? mismatch = null;
                            for (var attempt = 0; attempt < 2; attempt++)
                            {
                                var editor = ScoreRenderIdentity.CreateEditor(project, 0, zoom, dark, state);
                                var fresh = ScoreRenderIdentity.Hash(ScoreRenderIdentity.Render(editor));
                                if (state == "play") editor.SetPlayhead(editor.PlaybackMeasure, editor.PlaybackCell);
                                var warm = ScoreRenderIdentity.Hash(ScoreRenderIdentity.Render(editor));
                                editor.InvalidateVisual();
                                var repainted = ScoreRenderIdentity.Hash(ScoreRenderIdentity.Render(editor));
                                editor.InvalidateScoreLayout();
                                var rebuilt = ScoreRenderIdentity.Hash(ScoreRenderIdentity.Render(editor));
                                mismatch = fresh == warm && fresh == repainted && fresh == rebuilt ? null
                                    : $"{ScoreRenderIdentity.CaseName(song, 0, zoom, dark, state)} warm={fresh == warm} repaint={fresh == repainted} rebuilt={fresh == rebuilt}";
                                if (mismatch is null) break;
                            }
                            if (mismatch is not null) mismatches.Add(mismatch);
                        }
            Check("tab editor: a fresh, a warm, a repainted and a re-laid-out editor draw the same pixels", mismatches.Count == 0,
                $"{mismatches.Count} of {cases}: {string.Join("; ", mismatches.Take(3))}");
        }
        finally { TabEditorControl.RethrowRenderFailures = rethrow; }
    }

    /// <summary>Allocation per playback repaint (recorded in the log; guards the playback frame cost).</summary>
    private static void TestTabEditorPlaybackAllocation()
    {
        // The score text style is process-wide: start from the defaults so earlier tests cannot change what a repaint costs.
        TabEditorControl.ConfigureTextAreas(null);
        TabEditorControl.ConfigureScoreTextStyle(null, 13.5, false, false);
        // Every main window that earlier tests left open adds about 1.3 KB to each layout pass, so the measurement starts from none.
        // (A window whose close an earlier test cancelled holds unsaved test songs; they are marked clean so closing asks nothing.)
        var previousCapture = DialogHost.Capture;
        var previousMessage = DialogHost.MessageCapture;
        DialogHost.Capture = _ => false;
        DialogHost.MessageCapture = (_, _) => { };
        try
        {
            foreach (var w in (Application.Current?.Windows.OfType<MainWindow>().Where(w => w.IsVisible).ToList() ?? new List<MainWindow>()))
            {
                foreach (var session in w.OpenDocuments.ToList()) session.MarkClean();
                try { w.Close(); } catch (InvalidOperationException) { }
            }
        }
        finally { DialogHost.Capture = previousCapture; DialogHost.MessageCapture = previousMessage; }
        var perRepaint = long.MaxValue;
        {
            var project = DemoSongFactory.Create();
            var editor = ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, "play");
            ScoreRenderIdentity.Render(editor);
            var bar = editor.PlaybackMeasure;
            void Tick(int i)
            {
                editor.SetPlayhead(bar, i & 1);
                editor.UpdateLayout();
            }
            for (var i = 0; i < 40; i++) Tick(i);
            // The text cache is cleared when it fills, which makes one batch dearer than the rest: the cheapest of several batches is the cost of a repaint.
            const int Repaints = 100;
            for (var batch = 0; batch < 4; batch++)
            {
                GC.Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < Repaints; i++) Tick(i);
                perRepaint = Math.Min(perRepaint, (GC.GetAllocatedBytesForCurrentThread() - before) / Repaints);
            }
        }
        Log.Add($"  PERF  tab editor playback repaint: {perRepaint:N0} bytes allocated per repaint (cheapest of 4 batches of 100; demo song, 1.0x, dark, play state)");
        const long BudgetBytes = 110_000;   // recorded: about 105,000 alone and in a full run (main windows left open by earlier tests are closed first)
        Check("tab editor: a playback repaint allocates no more than the recorded budget", perRepaint <= BudgetBytes, $"{perRepaint:N0} bytes, budget {BudgetBytes:N0}");
    }

    private static string LayoutIdentity(TabEditorControl editor) => editor.Track is null ? "-" : editor.AuditLayout().GetHashCode().ToString("x");

    /// <summary>Which settings rebuild the page layout (a new layout object) and which keep it.</summary>
    private static void TestTabEditorLayoutMatrix()
    {
        SongProject NewProject()
        {
            var p = new SongProject { Tempo = 120 };
            p.Tracks.Add(new TrackModel { Name = "A", Measures = TemplateFactory.Measures(12) });
            p.Tracks.Add(new TrackModel { Name = "B", Measures = TemplateFactory.Measures(6) });
            return p;
        }
        var project = NewProject();
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, PageWidthOverride = 1280 };
        editor.Measure(new Size(1400, 700));
        editor.Arrange(new Rect(0, 0, 1400, 700));
        editor.UpdateLayout();

        var rebuilt = new List<string>();
        var kept = new List<string>();
        void Probe(string name, Action change)
        {
            var before = editor.AuditLayout();
            change();
            editor.Measure(new Size(1400, 700));
            editor.Arrange(new Rect(0, 0, 1400, 700));
            editor.UpdateLayout();
            (ReferenceEquals(before, editor.AuditLayout()) ? kept : rebuilt).Add(name);
        }
        Probe("Zoom", () => editor.Zoom = 1.5);
        Probe("DarkPaper", () => editor.DarkPaper = false);
        Probe("PlaybackActive", () => editor.PlaybackActive = true);
        Probe("SetPlayhead", () => editor.SetPlayhead(3, 0));
        Probe("PlayingBarEnabled", () => editor.PlayingBarEnabled = true);
        Probe("SetPosition", () => editor.SetPosition(2, 0, 1, false));
        Probe("SelectMeasureRange", () => editor.SelectMeasureRange(1, 3));
        Probe("ClearSelection", () => editor.ClearSelection());
        Probe("ScoreSpacing", () => editor.Appearance.ScoreSpacing = 1.2);
        Probe("SystemVerticalSpacing", () => editor.Appearance.SystemVerticalSpacing = 1.2);
        Probe("MeasureHorizontalSpacing", () => editor.Appearance.MeasureHorizontalSpacing = 1.2);
        Probe("Notation", () => editor.Notation = NotationMode.TabOnly);
        Probe("PageWidthOverride", () => editor.PageWidthOverride = 900);
        Probe("HorizontalScroll", () => editor.HorizontalScroll = true);
        Probe("HorizontalScrollOff", () => editor.HorizontalScroll = false);
        Probe("SelectedTrackIndex", () => editor.SelectedTrackIndex = 1);
        Probe("Project", () => editor.Project = NewProject());
        Probe("InvalidateScoreLayout", () => editor.InvalidateScoreLayout());
        Probe("SetActiveVoice", () => editor.SetActiveVoice(1));
        var observed = "rebuilt=" + string.Join(",", rebuilt) + " kept=" + string.Join(",", kept);
        const string Expected = "rebuilt=ScoreSpacing,SystemVerticalSpacing,MeasureHorizontalSpacing,Notation,PageWidthOverride,HorizontalScroll,HorizontalScrollOff,SelectedTrackIndex,Project,InvalidateScoreLayout kept=Zoom,DarkPaper,PlaybackActive,SetPlayhead,PlayingBarEnabled,SetPosition,SelectMeasureRange,ClearSelection,SetActiveVoice";
        Log.Add("  INFO  tab editor layout matrix: " + observed);
        Check("tab editor: the layout is rebuilt by spacing, notation, page width, scroll mode, track and song changes and kept for the rest", observed == Expected, observed);
    }

    private static (TabEditorControl Editor, int FirstBar, int LastBarOfFirstSystem) FrozenEditor()
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(12) });
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, HideCursor = true };
        editor.Measure(new Size(1800, 700));
        editor.Arrange(new Rect(0, 0, 1800, 700));
        editor.UpdateLayout();
        var layout = editor.AuditLayout();
        return (editor, 0, layout.Systems[0].LastMeasure);
    }

    private static Dictionary<int, Drawing> Frozen(TabEditorControl editor) => editor.AuditSystemDrawings().ToDictionary(d => d.System, d => d.Drawing);

    /// <summary>A playback tick keeps the frozen drawings of the systems that did not change; any other repaint request clears them.</summary>
    private static void TestTabEditorFrozenSystems()
    {
        var (editor, _, lastOfFirst) = FrozenEditor();
        var next = lastOfFirst + 1;
        var log = new StringBuilder();
        editor.PlaybackActive = true;
        editor.SetPlayhead(0, 0);
        var a = Frozen(editor);
        log.Append($"cached after first tick: {string.Join(",", a.Keys)}; ");
        Check("tab editor (frozen): with two systems, the system holding the playhead is drawn live and the other is retained", a.Count >= 1 && !a.ContainsKey(0) && a.ContainsKey(1), string.Join(",", a.Keys));
        editor.SetPlayhead(0, 1);
        var b = Frozen(editor);
        Check("tab editor (frozen): a tick inside the playing bar keeps the other system's retained drawing", ReferenceEquals(a[1], b[1]));
        editor.SetPlayhead(next, 0);
        var c = Frozen(editor);
        Check("tab editor (frozen): moving the playhead into the other system retains the first and draws the second live", c.ContainsKey(0) && !c.ContainsKey(1), string.Join(",", c.Keys));
        editor.SetPlayhead(next, 1);
        var d = Frozen(editor);
        Check("tab editor (frozen): a tick inside that bar keeps the first system's drawing", ReferenceEquals(c[0], d[0]));

        editor.SetPosition(2, 0, 2, false);
        var e = Frozen(editor);
        Check("tab editor (frozen): a cursor move clears the retained drawings", !ReferenceEquals(d[0], e[0]));
        editor.Zoom = 1.2;
        var f = Frozen(editor);
        Check("tab editor (frozen): a zoom change clears them", !ReferenceEquals(e[0], f[0]));
        editor.Appearance.PlayingBarColor = Color.FromRgb(0x20, 0xA0, 0x40);
        var g = Frozen(editor);
        Check("tab editor (frozen): a playing-bar setting change clears them", !ReferenceEquals(f[0], g[0]));
        editor.InvalidateVisual();
        var h = Frozen(editor);
        Check("tab editor (frozen): InvalidateVisual clears them", !ReferenceEquals(g[0], h[0]));
        editor.InvalidateScoreLayout();
        var i = Frozen(editor);
        Check("tab editor (frozen): a layout rebuild clears them", !ReferenceEquals(h[0], i[0]));
        var before = editor.Playback.PlayingBarBuilds;
        editor.SetPlayhead(next, 0);
        editor.SetPlayhead(next, 1);
        editor.SetPlayhead(next, 0);
        Check("tab editor (frozen): ticks inside one bar do not rebuild the playing-bar band", editor.Playback.PlayingBarBuilds <= before + 1, $"{editor.Playback.PlayingBarBuilds - before}");
    }

    /// <summary>A scripted keyboard and selection sequence: cursor, selection and the events raised after each step.</summary>
    private static void TestTabEditorInputScript()
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(4) });
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, AutoAdvanceAfterEntry = true };   // the recorded script types with advance on
        editor.Measure(new Size(1400, 700));
        editor.Arrange(new Rect(0, 0, 1400, 700));
        editor.UpdateLayout();
        int starting = 0, edited = 0, changed = 0;
        editor.EditStarting += (_, _) => starting++;
        editor.Edited += (_, _) => edited++;
        editor.SelectionChanged += (_, _) => changed++;
        var log = new StringBuilder();
        void Step(string name, Func<bool?> act)
        {
            (starting, edited, changed) = (0, 0, 0);
            var handled = act();
            var range = editor.HasSelection ? editor.SelectionCellRange : default;
            log.Append($"{name}:{(handled is null ? "-" : handled.Value ? "1" : "0")} pos={editor.SelectedMeasure}.{editor.SelectedCell}.{editor.SelectedString} " +
                $"sel={(editor.HasSelection ? $"{range.StartMeasure}.{range.StartCell}-{range.EndMeasure}.{range.EndCell}" : "none")} ev={starting}/{edited}/{changed}|");
        }
        bool? Key(Key key, ModifierKeys mods = ModifierKeys.None) => editor.TryHandleKey(key, mods);
        editor.SetPosition(0, 0, 0, false);
        Step("right", () => Key(System.Windows.Input.Key.Right));
        Step("right", () => Key(System.Windows.Input.Key.Right));
        Step("down", () => Key(System.Windows.Input.Key.Down));
        Step("fret5", () => Key(System.Windows.Input.Key.D5));
        Step("right", () => Key(System.Windows.Input.Key.Right));
        Step("shiftRight", () => Key(System.Windows.Input.Key.Right, ModifierKeys.Shift));
        Step("shiftRight", () => Key(System.Windows.Input.Key.Right, ModifierKeys.Shift));
        Step("clear", () => { editor.ClearSelection(); return null; });
        Step("home", () => Key(System.Windows.Input.Key.Home));
        Step("end", () => Key(System.Windows.Input.Key.End));
        Step("ctrlA", () => Key(System.Windows.Input.Key.A, ModifierKeys.Control));
        Step("shiftClick", () => editor.ShiftClickExtend(2, 0, 1));
        Step("rangeBars", () => { editor.SelectMeasureRange(1, 2); return null; });
        Step("selectAllClear", () => { editor.ClearSelection(); return null; });
        Step("dur8", () => { editor.SetDuration(8); return null; });
        Step("dot", () => { editor.ToggleDot(); return null; });
        Step("delete", () => { editor.DeleteNote(); return null; });
        Step("voice2", () => { editor.SetActiveVoice(1); return null; });
        Step("voice1", () => { editor.SetActiveVoice(0); return null; });
        var cell = project.Tracks[0].Measures.SelectMany(m => m.Cells).FirstOrDefault(c => c.Notes.Count > 0);
        log.Append($"firstNoteFret={(cell is null ? "none" : cell.Notes[0].Fret.ToString())}");
        var observed = log.ToString();
        Log.Add("  INFO  tab editor input script: " + observed);
        const string Expected = "right:1 pos=1.0.0 sel=none ev=0/0/1|right:1 pos=2.0.0 sel=none ev=0/0/1|down:1 pos=2.0.1 sel=none ev=0/0/1|fret5:1 pos=2.4.1 sel=none ev=1/1/1|right:1 pos=3.0.1 sel=none ev=0/0/1|shiftRight:1 pos=3.0.1 sel=none ev=0/0/0|shiftRight:1 pos=3.0.1 sel=none ev=0/0/0|clear:- pos=3.0.1 sel=none ev=0/0/0|home:1 pos=3.0.1 sel=none ev=0/0/1|end:1 pos=3.0.1 sel=none ev=0/0/1|ctrlA:0 pos=3.0.1 sel=none ev=0/0/0|shiftClick:1 pos=2.0.1 sel=2.0-3.0 ev=0/0/1|rangeBars:- pos=1.0.1 sel=1.0-2.0 ev=0/0/1|selectAllClear:- pos=1.0.1 sel=none ev=0/0/1|dur8:- pos=1.0.1 sel=none ev=0/0/1|dot:- pos=1.0.1 sel=none ev=1/1/0|delete:- pos=1.0.1 sel=none ev=1/1/0|voice2:- pos=1.0.1 sel=none ev=0/0/1|voice1:- pos=1.0.1 sel=none ev=0/0/1|firstNoteFret=5";
        Check("tab editor: the scripted keyboard and selection sequence gives the recorded cursor, selection and events", observed == Expected, observed);
    }

    /// <summary>The automation peer: class, control type, name, status and the bar and beat names for two cursor positions.</summary>
    private static void TestTabEditorAutomationSnapshot()
    {
        TabEditorControl.AutomationListenerOverride = false;
        try
        {
            var (editor, _) = StructureSong();
            var log = new StringBuilder();
            foreach (var (bar, cell) in new[] { (1, 0), (4, 0) })
            {
                editor.SetPosition(bar, cell, 1, false);
                var peer = UIElementAutomationPeer.CreatePeerForElement(editor)!;
                log.Append($"[{peer.GetClassName()}|{peer.GetLocalizedControlType()}|{peer.GetAutomationControlType()}|{peer.GetName()}|{peer.GetItemStatus()}|{peer.GetHelpText()}|{peer.IsKeyboardFocusable()}]");
                foreach (var barPeer in peer.GetChildren() ?? new List<AutomationPeer>())
                {
                    log.Append($"{{{barPeer.GetName()}:{string.Join("/", (barPeer.GetChildren() ?? new List<AutomationPeer>()).Select(b => b.GetName() + "=" + b.GetItemStatus()))}}}");
                }
                log.Append($"<{editor.DescribeCursor()}|{editor.DescribePosition()}|{editor.DescribeBar()}>");
            }
            var observed = log.ToString();
            Log.Add("  INFO  tab editor automation: " + observed);
            const string Expected =
                "[TabEditorControl|score editor|Custom|Tab editor|Track Guitar, bar 2, beat 1, string 2, eighth note, fret 7, note F sharp4, palm mute|Arrow keys move the cursor, digits set the fret, Enter or space plays.|True]{Bar 1:}{Bar 2, current bar:Bar 2, beat 1, eighth note, string 2 fret 7, palm mute=cursor/Bar 2, beat 3, quarter rest=}{Bar 3, section Verse:}{Bar 4:}{Bar 5:}{Bar 6:}<Track Guitar, bar 2, beat 1, string 2, eighth note, fret 7, note F sharp4, palm mute|Track Guitar, bar 2 of 6, beat 1, string 2, time signature 4/4, tempo 120, about 0:02|Bar 2, 4/4, tempo 120. Bar 2, beat 1, eighth note, string 2 fret 7, palm mute. Bar 2, beat 3, quarter rest>[TabEditorControl|score editor|Custom|Tab editor|Track Guitar, bar 5, beat 1, string 2, eighth note, empty|Arrow keys move the cursor, digits set the fret, Enter or space plays.|True]{Bar 1:}{Bar 2:Bar 2, beat 1, eighth note, string 2 fret 7, palm mute=/Bar 2, beat 3, quarter rest=}{Bar 3, section Verse:}{Bar 4:}{Bar 5, current bar:}{Bar 6:}<Track Guitar, bar 5, beat 1, string 2, eighth note, empty|Track Guitar, bar 5 of 6, beat 1, string 2, section Verse, time signature 3/4, tempo 90, about 0:08|Bar 5, section Verse, 3/4, tempo 90, empty>";
            Check("tab editor: the automation peer, its bar and beat names and the cursor descriptions are as recorded", observed == Expected, observed);
        }
        finally { TabEditorControl.AutomationListenerOverride = null; }
    }

    /// <summary>After the window's release (events cut, document forgotten) the editor and its helper objects hold no song model.</summary>
    private static void TestTabEditorLifetime()
    {
        var (editor, _) = StructureSong();
        editor.SetPosition(1, 0, 1, false);
        editor.SelectMeasureRange(1, 2);
        ScoreRenderIdentity.Render(editor);
        var host = new Grid();
        host.Children.Add(editor);
        TabForge.Shell.ChildEventRelease.Release(host);
        editor.ReleaseDocument();

        var holders = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        static bool IsModel(Type t) => t.Namespace is "TabForge.Models" or "TabForge.Documents" || (t.IsArray && IsModel(t.GetElementType()!)) ||
            (t.IsGenericType && t.GetGenericArguments().Any(IsModel));
        void Walk(object target, string path)
        {
            if (!seen.Add(target)) return;
            for (var t = target.GetType(); t is not null && t.Assembly == typeof(TabEditorControl).Assembly; t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsValueType) continue;
                    var value = f.GetValue(target);
                    if (value is null) continue;
                    if (IsModel(f.FieldType))
                    {
                        var empty = value is System.Collections.ICollection { Count: 0 } || value is Array { Length: 0 };
                        if (!empty) holders.Add($"{path}.{f.Name}");
                    }
                    else if (value is not DependencyObject && value.GetType().Namespace is { } ns && (ns == "TabForge.Views" || ns.StartsWith("TabForge.Views.", StringComparison.Ordinal)))
                        Walk(value, $"{path}.{f.Name}");
                }
        }
        Walk(editor, "editor");
        var observed = string.Join(",", holders.OrderBy(h => h, StringComparer.Ordinal));
        Log.Add("  INFO  tab editor holders of song models after release: " + (observed.Length == 0 ? "none" : observed));
        Check("tab editor: after the window releases it, neither the control nor its helper objects hold a song model", observed.Length == 0, observed);
    }
    /// <summary>The playback surface forwards to the editor's own members, and the appearance object drives the same invalidation as the old properties.</summary>
    private static void TestTabEditorPlayheadAndAppearance()
    {
        var project = DemoSongFactory.Create();
        var editor = ScoreRenderIdentity.CreateEditor(project, 0, 1.0, true, "plain");
        var timeline = Playback.MidiTimelineBuilder.Build(project, new Playback.PlaybackOptions());
        var remap = new[] { 0, 1, 2 };
        IScorePlayhead playhead = editor.Playback;
        playhead.Bind(timeline, remap, 0);
        playhead.Active = true;
        playhead.Fraction = 0.5;
        playhead.Ms = 1234;
        playhead.SetPlayhead(3, 1);
        Check("tab editor playhead: the surface sets the editor's playback state",
            editor.PlaybackActive && ReferenceEquals(editor.Timeline, timeline) && ReferenceEquals(editor.PlaybackBarRemap, remap) && editor.PlaybackTrackIndex == 0 &&
            editor.PlaybackFraction == 0.5 && editor.PlaybackMs == 1234 && editor.PlaybackMeasure == 3 && editor.PlaybackCell == 1);
        Check("tab editor playhead: the geometry the surface returns is the editor's",
            playhead.PlayheadGeometry() == editor.PlayheadGeometry() && playhead.PlaybackDurationGeometries().Count == editor.PlaybackDurationGeometries().Count &&
            playhead.PlaybackHorizontalGeometry(3, 0.5) == editor.PlaybackHorizontalGeometry(3, 0.5) && playhead.NeedsRepaint(0, 5000) == editor.PlaybackNeedsRepaint(0, 5000));
        playhead.Clear();
        Check("tab editor playhead: clearing removes the playhead", editor.PlaybackMeasure < 0 && editor.PlaybackCell < 0);

        var before = editor.AuditLayout();
        editor.Appearance.PlayingBarOpacity = 0.4;
        Check("tab editor appearance: a playing-bar setting repaints and keeps the layout", ReferenceEquals(before, editor.AuditLayout()));
        editor.Appearance.ScoreSpacing = 1.3;
        Check("tab editor appearance: a spacing setting rebuilds the layout", !ReferenceEquals(before, editor.AuditLayout()));
        editor.DarkPaper = false;
        editor.CenterSystems = true;
        editor.PlayingBarEnabled = true;
        Check("tab editor appearance: the editor's forwarding settings write the appearance",
            !editor.Appearance.DarkPaper && editor.Appearance.CenterSystems && editor.Appearance.PlayingBarEnabled);
    }
}

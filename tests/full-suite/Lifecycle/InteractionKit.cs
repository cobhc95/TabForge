using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Diagnostics;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Shared pieces of the interaction scenarios (SelfTestInteractions*.cs): a recording MIDI output, documents opened into a real window with a silent
/// engine, key presses through the window's own routing, a pump that samples the playhead, musical identity (content hash, compiled MIDI events),
/// render identity (the live editor's per-system drawings against a fresh editor's) and the glitch counters a scenario must leave unchanged.
/// </summary>
public static partial class SelfTest
{
    /// <summary>One MIDI message a document's engine sent to its (silent) output.</summary>
    private readonly record struct IxMessage(double Ms, int DeviceId, int Status, int Data1, int Data2)
    {
        public bool IsNoteOn => (Status & 0xF0) == 0x90 && Data2 > 0;
        public int Channel => Status & 0x0F;
    }

    /// <summary>A silent MIDI output that logs every message with the time it arrived.</summary>
    private sealed class IxOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<IxMessage> _messages = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public bool Disposed { get; private set; }
        public int Resets { get; private set; }

        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_gate) _messages.Add(new IxMessage(_clock.Elapsed.TotalMilliseconds, deviceId, status, data1, data2));
        }

        public void ResetAll() => Resets++;
        public void Close() { }
        public void Dispose() => Disposed = true;

        public IxMessage[] Messages { get { lock (_gate) return _messages.ToArray(); } }
        public IxMessage[] NoteOns => Messages.Where(m => m.IsNoteOn).ToArray();
        public int Count => Messages.Length;
    }

    /// <summary>The demo song at a quick tempo (a bar lasts about half a second at double speed), so a scenario that plays eight bars stays short.</summary>
    private static SongProject IxDemoSong(int tempo = 240)
    {
        var song = DemoSongFactory.Create();
        song.Tempo = tempo;
        song.IsDirty = false;
        return song;
    }

    /// <summary>Opens <paramref name="song"/> as a new tab of <paramref name="window"/> (the way a tab dropped on the strip opens), with an engine on a silent, logging output.</summary>
    private static DocumentSession IxOpen(MainWindow window, SongProject song, out IxOutput output)
    {
        output = new IxOutput();
        var session = new DocumentSession(new PlaybackEngine(output)) { Project = song };
        session.MarkClean();
        LtCall(window, "AdoptDroppedDocument", session, window.OpenDocuments.Count);
        SettleLifetimeDispatcher();
        return session;
    }

    /// <summary>Ends a document the way closing its tab does, and releases the engine ownership the window's sync gave a test session (it has no routing of its own).</summary>
    private static void IxRelease(DocumentSession session)
    {
        session.DisposePlayback();
        TabForge.Audio.AudioEngineClient.Instance.ReleaseOwner(session);
    }

    private static int IxIndexOf(MainWindow window, DocumentSession session) => window.OpenDocuments.ToList().IndexOf(session);

    private static void IxActivate(MainWindow window, DocumentSession session)
    {
        LtCall(window, "ActivateTabAt", IxIndexOf(window, session));
        SettleLifetimeDispatcher();
    }

    private static TabEditorControl IxEditor(MainWindow window) => LtField<TabEditorControl>(window, "Editor")!;
    private static DataGrid IxTrackGrid(MainWindow window) => LtField<DataGrid>(window, "TrackMixerGrid")!;

    /// <summary>Selects a track and puts the cursor on a beat, as a click on the score does.</summary>
    private static void IxCursor(MainWindow window, int track, int bar, int cell, int stringIndex)
    {
        var grid = IxTrackGrid(window);
        grid.SelectedIndex = track;
        var editor = IxEditor(window);
        editor.SelectedTrackIndex = track;
        editor.SetPosition(bar, cell, stringIndex, false);
    }

    /// <summary>
    /// A key press routed through the window's own preview handler, the one a real keystroke reaches (never the editor's method directly).
    /// The window reads the physical modifier keys while it routes (a Shift held by whoever is at the machine turns a digit into an effect
    /// shortcut): the press waits for them to be released and, if the window declined it while one was held, is sent again. A declined
    /// press changed nothing, so sending it again cannot double an edit; a press the window handled is never repeated.
    /// </summary>
    private static bool IxKey(MainWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("the window has no presentation source");
        for (var attempt = 1; ; attempt++)
        {
            if (!IxPumpUntil(() => Keyboard.Modifiers == ModifierKeys.None, 5000))
            {
                Check($"interactions: key {key} waits for physical modifiers to clear", false, "modifiers remained held for 5 s");
                return false;
            }
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            var modifiers = Keyboard.Modifiers;
            IxEditor(window).RaiseEvent(args);
            SettleLifetimeDispatcher();
            if (args.Handled || (modifiers == ModifierKeys.None && Keyboard.Modifiers == ModifierKeys.None) || attempt >= 5) return args.Handled;
            Log.Add($"  info  key {key} was declined while a modifier key was held ({modifiers}/{Keyboard.Modifiers}); sent again (attempt {attempt})");
        }
    }

    private static bool IxCommand(MainWindow window, string commandId)
    {
        var handled = (bool)LtCall(window, "RunHotkey", commandId)!;
        SettleLifetimeDispatcher();
        return handled;
    }

    /// <summary>Runs queued dispatcher work for <paramref name="ms"/> milliseconds, calling <paramref name="each"/> between rounds (a sampler).</summary>
    private static void IxPump(int ms, Action? each = null) => IxPumpUntil(() => false, ms, each);

    private static bool IxPumpUntil(Func<bool> done, int ms, Action? each = null)
    {
        var end = Stopwatch.GetTimestamp() + ms * Stopwatch.Frequency / 1000;
        while (!done() && Stopwatch.GetTimestamp() < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            each?.Invoke();
            Thread.Sleep(3);
        }
        return done();
    }

    /// <summary>Waits for measurable playback progress or a stop, while sampling the same trace used by no-jump and no-replay checks.</summary>
    private static bool IxWaitForPlaybackProgress(DocumentSession session, double from, double minimumBars, int timeoutMs, Action? each = null) =>
        IxPumpUntil(() => !session.Playback.Engine.IsPlaying || IxPosition(session) >= from + minimumBars, timeoutMs, each);

    // ---------- playhead ----------

    /// <summary>Where a document's engine is now, as one number: the performed bar plus how far into it.</summary>
    private static double IxPosition(DocumentSession session)
    {
        var p = session.Playback.Engine.Playhead();
        return p.Bar + p.BarFraction;
    }

    /// <summary>Samples a playing document's position on every pump round; a jump larger than the allowance (other than at a declared seek) is recorded.</summary>
    private sealed class IxPlayheadTrace
    {
        private readonly DocumentSession _session;
        private double _last = double.NaN;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastAt, _stuckSince;
        public int Samples { get; private set; }
        public int Backward { get; private set; }
        public int Jumps { get; private set; }
        public int Stalls { get; private set; }
        public double Max { get; private set; }
        public string FirstProblem { get; private set; } = "";

        public IxPlayheadTrace(DocumentSession session) => _session = session;

        /// <summary>How far back (in bars) a sample may be before it counts as going backwards: a restart from the playhead resumes at the start of its beat.</summary>
        public double BackwardTolerance { get; init; } = 0.02;

        /// <summary>Called after a seek the scenario made on purpose: the next sample starts a new run.</summary>
        public void Reset() => _last = double.NaN;

        public void Sample()
        {
            if (!_session.Playback.Engine.IsPlaying) return;
            var position = IxPosition(_session);
            var at = _clock.Elapsed.TotalMilliseconds;
            Samples++;
            Max = Math.Max(Max, position);
            if (!double.IsNaN(_last))
            {
                if (position < _last - BackwardTolerance) { Backward++; if (FirstProblem.Length == 0) FirstProblem = $"went back {_last:0.00} -> {position:0.00} after {Samples} samples"; }
                // At most 2 bars of progress in the time that passed (a bar is about half a second here): a bigger step is a jump.
                else if (position - _last > 1.0 + (at - _lastAt) / 250.0) { Jumps++; if (FirstProblem.Length == 0) FirstProblem = $"jumped {_last:0.00} -> {position:0.00} in {at - _lastAt:0} ms"; }
                // Stuck: no progress over 400 ms of closely spaced samples (a starved sampler leaves a gap and starts the count again).
                if (position - _last < 1e-9) { if (at - _lastAt > 100) _stuckSince = at; else if (at - _stuckSince > 400) { Stalls++; _stuckSince = at; } }
                else _stuckSince = at;
            }
            _last = position;
            _lastAt = at;
        }

        public override string ToString() => $"{Samples} samples, max {Max:0.00}, backward {Backward}, jumps {Jumps}, stalls {Stalls}{(FirstProblem.Length > 0 ? ", first: " + FirstProblem : "")}";
    }

    // ---------- glitch counters ----------

    /// <summary>The counters a scenario must leave unchanged: contained render failures, unhandled dispatcher exceptions and events the scheduler sent late.</summary>
    private readonly record struct IxGlitches(int RenderContained, int UnhandledExceptions, int LateEvents, int HostStalls = 0)
    {
        public static IxGlitches Take(LifetimeContext context, params DocumentSession[] playing) =>
            new(RenderGuard.Contained, context.Failures.Count, playing.Sum(IxLateEvents), IxHostStalls.Count);

        /// <summary>Equal when nothing glitched. Late events are not compared when the whole machine stalled in between (see <see cref="IxHostStalls"/>): a starved scheduler is load, not a defect.</summary>
        public bool Equals(IxGlitches other) =>
            RenderContained == other.RenderContained && UnhandledExceptions == other.UnhandledExceptions
            && (LateEvents == other.LateEvents || HostStalls != other.HostStalls);

        public override int GetHashCode() => HashCode.Combine(RenderContained, UnhandledExceptions);
        public override string ToString() => $"render contained {RenderContained}, unhandled {UnhandledExceptions}, late events {LateEvents}, host stalls {HostStalls}";
    }

    /// <summary>
    /// Counts the times a plain background thread, asking to run every 10 ms, got no time for 150 ms or more. Such a stall delays the playback
    /// scheduler too, so late events during it say the machine was loaded, not that playback glitched; the glitch check ignores them then and the stall count is logged.
    /// </summary>
    private static class IxHostStalls
    {
        private static int _count;
        private static int _started;

        public static int Count
        {
            get
            {
                if (Interlocked.Exchange(ref _started, 1) == 0)
                    new Thread(() =>
                    {
                        var last = Stopwatch.GetTimestamp();
                        while (true)
                        {
                            Thread.Sleep(10);
                            var now = Stopwatch.GetTimestamp();
                            if (Stopwatch.GetElapsedTime(last, now).TotalMilliseconds >= 150) Interlocked.Increment(ref _count);
                            last = now;
                        }
                    }) { IsBackground = true, Name = "IxHostStalls" }.Start();
                return Volatile.Read(ref _count);
            }
        }
    }

    /// <summary>Events the scheduler dispatched more than 250 ms after their time (far above the 25 ms playtest limit: a loaded machine must not fail this).</summary>
    private static int IxLateEvents(DocumentSession session) => session.Playback.Engine.DispatchLog.Count(r => r.LatencyMs > 250);

    // ---------- musical identity ----------

    private static string IxContentHash(SongProject project) => Convert.ToHexString(ProjectService.ContentHash(project))[..16];

    /// <summary>The song as the playback compiler hears it: every event (time, track, status, data), sorted, hashed.</summary>
    private static string IxMidiHash(SongProject project)
    {
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions { Speed = 1.0, Metronome = false, CountIn = false });
        var text = new StringBuilder();
        foreach (var e in timeline.Events.OrderBy(e => Math.Round(e.TimeMs, 2)).ThenBy(e => e.TrackIndex).ThenBy(e => e.Status).ThenBy(e => e.Data1).ThenBy(e => e.Data2))
            text.Append(Math.Round(e.TimeMs, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(e.TrackIndex).Append(' ').Append(e.Status).Append(' ').Append(e.Data1).Append(' ').Append(e.Data2).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    // ---------- render identity ----------

    private static string IxF(double value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Hash of each system's drawing: every text, line, head and shape with its rounded box (what the layout audit reads).</summary>
    private static Dictionary<int, string> IxDrawingHashes(IEnumerable<(int System, Drawing Drawing)> systems, Dictionary<int, string[]>? lines = null)
    {
        var hashes = new Dictionary<int, string>();
        foreach (var (system, drawing) in systems)
        {
            var items = new List<LayoutAudit.Item>();
            LayoutAudit.Walk(drawing, Matrix.Identity, items);
            var rows = items.Select(item => $"{item.Kind}|{item.Label}|{IxF(item.Box.X)},{IxF(item.Box.Y)},{IxF(item.Box.Width)},{IxF(item.Box.Height)}").ToArray();
            if (lines is not null) lines[system] = rows;
            hashes[system] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))))[..12] + "/" + items.Count;
        }
        return hashes;
    }

    /// <summary>The systems both editors drew, and the ones among them that differ.</summary>
    private readonly record struct IxRender(int Compared, int[] Differing, string Detail)
    {
        public bool Identical => Compared > 0 && Differing.Length == 0;
    }

    /// <summary>
    /// What the window's editor draws for its song (the systems it keeps drawn for the viewport), against what a new editor draws for the same song at the same
    /// size and view settings: a system whose two drawings differ is a stale layout or drawing in the live editor. Both are drawn without the cursor and the
    /// playhead, so stop playback first. <paramref name="reveal"/> scrolls the score to that bar first so the system of an edit is among those drawn.
    /// </summary>
    /// <summary>The window's editor drawing, per system it keeps drawn, scrolled so that <paramref name="reveal"/>'s system is among them (no cursor).</summary>
    private static Dictionary<int, string> IxEditorHashes(MainWindow window, int reveal, Dictionary<int, string[]>? lines = null)
    {
        var live = IxEditor(window);
        LtField<ScrollViewer>(window, "ScoreScroll")!.ScrollToVerticalOffset(live.ScrollOffsetForMeasure(reveal));
        window.UpdateLayout();
        var hidden = live.HideCursor;
        live.HideCursor = true;
        try { return IxDrawingHashes(live.AuditSystemDrawings(), lines); }
        finally { live.HideCursor = hidden; live.InvalidateVisual(); }
    }

    private static IxRender IxRenderIdentity(MainWindow window, int reveal = 0)
    {
        var live = IxEditor(window);
        var project = live.Project!;
        var track = live.SelectedTrackIndex;
        var liveLines = new Dictionary<int, string[]>();
        var liveHashes = IxEditorHashes(window, reveal, liveLines);
        var fresh = new TabEditorControl
        {
            Project = project, SelectedTrackIndex = track, DarkPaper = live.DarkPaper, HideCursor = true, PlaybackMeasure = -1,
            Notation = live.Notation, Zoom = live.Zoom, CenterSystems = live.CenterSystems, HorizontalScroll = live.HorizontalScroll,
            PageWidthOverride = live.PageWidthOverride, Width = live.Width,
        };
        var width = Math.Max(1, live.ActualWidth); var height = Math.Max(1, live.ActualHeight);
        fresh.Measure(new Size(width, height));
        fresh.Arrange(new Rect(0, 0, width, height));
        fresh.UpdateLayout();
        var freshLines = new Dictionary<int, string[]>();
        var freshHashes = IxDrawingHashes(fresh.AuditSystemDrawings(), freshLines);
        var common = liveHashes.Keys.Intersect(freshHashes.Keys).OrderBy(k => k).ToArray();
        var differing = common.Where(k => liveHashes[k] != freshHashes[k]).ToArray();
        var first = differing.Length == 0 ? "" : FirstDifference(liveLines[differing[0]], freshLines[differing[0]]);
        return new IxRender(common.Length, differing, $"live systems [{string.Join(",", liveHashes.Keys.OrderBy(k => k))}], fresh systems [{string.Join(",", freshHashes.Keys.OrderBy(k => k))}], differing [{string.Join(",", differing)}]{first}, live size {live.ActualWidth:0}x{live.ActualHeight:0}");

        static string FirstDifference(string[] live, string[] fresh)
        {
            for (var i = 0; i < Math.Min(live.Length, fresh.Length); i++)
                if (live[i] != fresh[i]) return $"; first difference at item {i}: live '{live[i]}' vs fresh '{fresh[i]}'";
            return $"; one drawing is a prefix of the other ({live.Length} vs {fresh.Length} items)";
        }
    }

    // ---------- reporting ----------

    /// <summary>Progress line for a hung or slow run: appended to the file named by TABFORGE_SELFTEST_TRACE (the log is only written when the run ends).</summary>
    private static void IxTrace(string text)
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_SELFTEST_TRACE") is not { Length: > 0 } path) return;
        try { File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    /// <summary>
    /// A check of a scenario that exposes a defect recorded in the architecture traces. It is reported but never required: a failure is logged as
    /// KNOWN with the trace id (it does not fail the run), a pass is logged as an ordinary pass and "now passing" so the wave that fixes the defect
    /// can move the check into the required list.
    /// </summary>
    private static void IxKnown(string traceId, string name, bool condition, string? detail = null)
    {
        if (condition) { _pass++; Log.Add($"  PASS  interactions-known [{traceId}] {name}  (now passing: move to the required list)"); IxKnownPassing++; }
        else if (_required.Contains("interactions-known")) Check($"interactions-known [{traceId}] {name}", false, detail);   // --require interactions-known: the known defects are required to be fixed
        else { Log.Add($"  KNOWN interactions-known [{traceId}] {name}{(detail is null ? "" : "  -> " + detail)}"); IxKnownFailing++; IxKnownIds.Add(traceId); }
    }

    private static int IxKnownFailing, IxKnownPassing;
    private static readonly SortedSet<string> IxKnownIds = new(StringComparer.Ordinal);
}

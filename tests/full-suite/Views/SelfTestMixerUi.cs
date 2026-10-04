using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Mixer window sliders and drag-and-drop ordering, driven with simulated routed mouse events (never the real cursor) (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    // Simulated pointer: routed mouse events raised on the element, with the pointer position pretended through PointerSource.
    // Nothing here touches the real cursor, needs focus or sends system input.
    private static UIElement? _simTarget;
    private static IEnumerable<string> ChainOf(DependencyObject? d) { for (var i = 0; d is not null && i < 12; i++, d = System.Windows.Media.VisualTreeHelper.GetParent(d)) yield return d.GetType().Name + ((d as FrameworkElement)?.Tag is string s ? "[" + s + "]" : ""); }

    private static void SimulateMouse(RoutedEvent routed, UIElement target, bool button)
    {
        var ts = Environment.TickCount;
        RoutedEventArgs args = button
            ? new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, ts, System.Windows.Input.MouseButton.Left) { RoutedEvent = routed, Source = target }
            : new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, ts) { RoutedEvent = routed, Source = target };
        target.RaiseEvent(args);
        PumpUi();
    }

    /// <summary>The pointer moves to a screen point: a mouse-move goes to the element holding the capture (else the aimed one).</summary>
    private static void MouseTo(Point screen)
    {
        Views.PointerSource.Simulated = screen;
        if ((System.Windows.Input.Mouse.Captured as UIElement ?? _simTarget) is { } target)
            SimulateMouse(System.Windows.Input.Mouse.PreviewMouseMoveEvent, target, button: false);
    }

    private static void MouseDown()
    {
        if (_simTarget is { } target) SimulateMouse(UIElement.PreviewMouseLeftButtonDownEvent, target, button: true);
    }

    private static void MouseUp()
    {
        var target = System.Windows.Input.Mouse.Captured as UIElement ?? _simTarget;
        if (target is not null) SimulateMouse(UIElement.PreviewMouseLeftButtonUpEvent, target, button: true);
        System.Windows.Input.Mouse.Capture(null);
        Views.PointerSource.Simulated = null;
        _simTarget = null;
    }


    /// <summary>A test window must not end the application when it is the last one closed.</summary>
    private static IDisposable KeepAlive()
    {
        var app = Application.Current; var was = app.ShutdownMode;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return new Restore(() => app.ShutdownMode = was);
    }

    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLongForTest(IntPtr hwnd, int index, int value);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    /// <summary>
    /// Every self-test window goes through here: a real top-level window (own HWND, layout, input, capture) that the user
    /// never sees: off screen, not activated, no taskbar button, and fully transparent (layered, alpha 0), so even a test
    /// that maximises it shows nothing and the real mouse passes through. Returns once the window has its presentation
    /// source, is loaded and laid out, so input can be raised at once (deterministic, whatever ran before).
    /// </summary>
    private static void ShowTestWindow(Window window, bool offScreen = true, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Topmost = false;
        if (offScreen)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = -20000;
        }
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            // No-activate: a simulated press focuses a control, which would make this hidden window the foreground window; when
            // the shell takes the foreground back mid-drag, Windows cancels the mouse capture (WM_CANCELMODE) and the drag ends.
            const int GwlExStyle = -20, WsExLayered = 0x00080000, WsExNoActivate = 0x08000000; const uint LwaAlpha = 0x2;
            SetWindowLongForTest(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExLayered | WsExNoActivate);
            SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
        };
        window.Show();
        for (var i = 0; i < 50 && (PresentationSource.FromVisual(window) is null || !window.IsLoaded); i++) PumpUi();
        window.UpdateLayout();
        PumpUi();
        CheckWindowFitsWorkArea(window, caller);
    }

    /// <summary>Minimal host for a MixerWindow over a plain project: records what the mixer asked for.</summary>
    private sealed class FakeMixerHost : IMixerHost
    {
        public SongProject Project { get; } = new();
        public int Changes, Edits, Reorders;
        public int MasterVolume { get; set; } = 100;
        public void BeginMixerEdit() => Edits++;
        public Action? AfterChange;   // what the real host does after a value change (e.g. its idle refresh)
        public void MixerChanged(bool recompile) { Changes++; AfterChange?.Invoke(); }
        public void OpenFxChain(TrackModel track) { }
        public void OpenAudioSettings() { }
        public void SetGroupColour(string group, string hex) { }
        public string GroupColour(string group) => "#888888";
        public bool TrackListShows(string what) => true;
        public void SetTrackListShows(string what, bool on) { }
        public void OpenBusFx(string? group) { }
        public BusChain MonitorChain { get; } = new();
        public void OpenMonitorFx() { MonitorOpened++; }
        public int MonitorOpened;
        public string? HotkeyAction(string gesture) => HotkeyGestureToAction(gesture);
        public bool ReorderFromMixer(Func<SongProject, bool> apply, string status)
        {
            if (!apply(Project)) return false;
            Reorders++;
            return true;
        }
    }

    private static string? HotkeyGestureToAction(string gesture) => gesture switch
    {
        "Alt+Up" or "Up" => "Track.MoveUp",       // (plain Up / Down: the test cannot hold Alt)
        "Alt+Down" or "Down" => "Track.MoveDown",
        _ => null,
    };

    private static TrackModel MixerTestTrack(string name, TrackKind kind, int program, int volume = 100) =>
        new() { Name = name, InstrumentName = name, Kind = kind, MidiProgram = program, MidiChannel = kind == TrackKind.Drums ? 9 : 0, Volume = volume, Pan = 64 };

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in VisualDescendants<T>(child)) yield return deeper;
        }
    }


    /// <summary>
    /// Input as WPF delivers it: through the InputManager (preview event, then the promoted bubbling event, so the
    /// class handlers of Thumb / RepeatButton / Slider run too), aimed at the element under the point or the capture.
    /// The pointer position is pretended through PointerSource; the real cursor never moves.
    /// </summary>
    private static void InputAt(RoutedEvent preview, UIElement root, Point screen, bool button)
    {
        Views.PointerSource.Simulated = screen;
        var target = System.Windows.Input.Mouse.Captured ?? root.InputHitTest(root.PointFromScreen(screen)) ?? root;
        var ts = Environment.TickCount;
        System.Windows.Input.InputEventArgs args = button
            ? new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, ts, System.Windows.Input.MouseButton.Left) { RoutedEvent = preview, Source = target }
            : new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, ts) { RoutedEvent = preview, Source = target };
        System.Windows.Input.InputManager.Current.ProcessInput(args);
        PumpUi();
    }

    /// <summary>
    /// The screen point whose x maps exactly to <paramref name="value"/>: the inverse of TrackControlWidgets.SliderValueAt with the
    /// real handle width, so every integer is hit on any DPI, window size or slider width (no assumed 18-px handle, no pixel rounding).
    /// </summary>
    private static Point SliderScreenPoint(Slider slider, double value)
    {
        var thumbWidth = 18.0;
        if (slider.Template?.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track { Thumb: { ActualWidth: > 0 } thumb })
            thumbWidth = thumb.ActualWidth;
        var usable = Math.Max(1, slider.ActualWidth - thumbWidth);
        var ratio = (value - slider.Minimum) / (slider.Maximum - slider.Minimum);
        if (slider.IsDirectionReversed) ratio = 1 - ratio;
        var exact = slider.PointToScreen(new Point(thumbWidth / 2 + usable * ratio, slider.ActualHeight / 2));
        // Real pointers sit on whole device pixels (CI runners deliver whole-pixel positions): use the whole pixel nearest the
        // exact point that maps to the value, as a user would stop where the value shows.
        foreach (var dx in new[] { 0.0, -1, 1, -2, 2 })
        {
            var px = new Point(Math.Round(exact.X) + dx, Math.Round(exact.Y));
            if (Math.Abs(TrackControlWidgets.SliderValueAt(slider, slider.PointFromScreen(px).X) - value) < 0.001) return px;
        }
        return exact;
    }

    /// <summary>
    /// Presses the handle, drags to <paramref name="to"/> and back to <paramref name="back"/> one unit per move; returns
    /// (pointer value, pointer x in the slider, slider value) per move. <paramref name="onStep"/> sees each step (evidence).
    /// </summary>
    private static string _pressCapture = "";   // who held the capture after the last simulated press (failure detail)

    private static List<(double Want, double X, double Got)> RealDrag(Slider slider, UIElement root, double to, double back, Action<double>? onStep = null)
    {
        var trace = new List<(double, double, double)>();
        var from = slider.Value;
        InputAt(System.Windows.Input.Mouse.PreviewMouseDownEvent, root, SliderScreenPoint(slider, from), button: true);
        _pressCapture = ReferenceEquals(System.Windows.Input.Mouse.Captured, slider) ? "slider" : System.Windows.Input.Mouse.Captured?.GetType().Name ?? "none";
        void Walk(double a, double b)
        {
            var dir = Math.Sign(b - a);
            for (var v = a; dir > 0 ? v <= b : v >= b; v += dir)
            {
                if (PresentationSource.FromVisual(slider) is null) { trace.Add((v, double.NaN, double.NaN)); return; }   // the slider was replaced mid-drag
                var screen = SliderScreenPoint(slider, v);
                InputAt(System.Windows.Input.Mouse.PreviewMouseMoveEvent, root, screen, button: false);
                trace.Add((v, PresentationSource.FromVisual(slider) is null ? double.NaN : slider.PointFromScreen(screen).X, slider.Value));
                onStep?.Invoke(v);
            }
        }
        Walk(from, to);
        Walk(to, back);
        if (PresentationSource.FromVisual(slider) is not null)
            InputAt(System.Windows.Input.Mouse.PreviewMouseUpEvent, root, SliderScreenPoint(slider, back), button: true);
        System.Windows.Input.Mouse.Capture(null);
        Views.PointerSource.Simulated = null;
        return trace;
    }

    /// <summary>Renders a window's content to a PNG off screen (RenderTargetBitmap; no screen capture).</summary>
    private static void SaveWindowPng(Window window, string path)
    {
        if (window.Content is not FrameworkElement content) return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(content.ActualWidth)), Math.Max(1, (int)Math.Ceiling(content.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    private static void TestMixerSlidersRealInput()
    {
        var host = new FakeMixerHost();
        host.Project.Tracks.Add(MixerTestTrack("Lead", TrackKind.Guitar, 30, volume: 20));
        host.Project.Tracks.Add(MixerTestTrack("Rhythm", TrackKind.Guitar, 30, volume: 20));
        host.Project.Tracks.Add(MixerTestTrack("Bass", TrackKind.Bass, 33));
        using var alive = KeepAlive();
        var window = new MixerWindow(host, null);
        // As the real MainWindow: after a value change its idle follow-up (RefreshTracks -> track grid SelectionChanged ->
        // RefreshPluginChain -> SyncMixerWindows) rebuilds the mixer. That used to detach the dragged slider after one step.
        host.AfterChange = () => window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(window.Rebuild));
        // A fixed size so the slider widths do not depend on the runner's screen or DPI.
        window.SizeToContent = SizeToContent.Manual; window.Width = 1000; window.Height = 700;
        try
        {
            ShowTestWindow(window);
            List<Slider> Sliders() => VisualDescendants<Slider>(window).Where(s => s.Maximum > s.Minimum).ToList();
            var count = Sliders().Count;
            Check("mixer real-input drag: sliders found", count >= 6, $"{count}");
            // Optional evidence (TABFORGE_SLIDER_EVIDENCE = a folder): a CSV of pointer x -> value for every sweep and PNGs of the mixer mid-drag.
            var evidence = Environment.GetEnvironmentVariable("TABFORGE_SLIDER_EVIDENCE");
            System.IO.StreamWriter? csv = null;
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                System.IO.Directory.CreateDirectory(evidence);
                csv = new System.IO.StreamWriter(System.IO.Path.Combine(evidence, "mixer-slider-sweeps.csv"));
                csv.WriteLine("slider,label,step,pointer_value,pointer_x,slider_value");
            }
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var slider = Sliders()[i];   // fresh each time: the deferred rebuild after a drag replaces the rows
                    var label = System.Windows.Automation.AutomationProperties.GetName(slider);
                    double min = slider.Minimum, max = slider.Maximum;
                    slider.Value = min; PumpUi();
                    slider = Sliders()[i];   // setting the start value rebuilt the rows (no drag yet)
                    slider.BringIntoView(); PumpUi(); window.UpdateLayout(); PumpUi();   // the press hit-tests where the slider is on screen
                    for (var k = 0; k < 20 && PresentationSource.FromVisual(slider) is null; k++) { PumpUi(); slider = Sliders()[i]; }   // a late rebuild: take the live slider
                    System.Threading.Thread.Sleep((int)GetDoubleClickTime() + 150);   // each press is a new click, not a double-click (the real cursor never moves)
                    // Snapshots of the first slider of each kind at 0 / 25 / 50 / 75 / 100 % and back.
                    var shots = new List<double> { 0, 0.25, 0.5, 0.75, 1 }.Select(f => Math.Round(min + (max - min) * f)).ToHashSet();
                    var steps = 0;
                    void Shot(double v)
                    {
                        steps++;
                        if (csv is null || i > 3 || !shots.Contains(v)) return;
                        var name = $"slider{i}-{(steps <= max - min + 1 ? "up" : "down")}-{v:0}.png";
                        SaveWindowPng(window, System.IO.Path.Combine(evidence!, name));
                    }
                    var trace = RealDrag(slider, window, max, min, Shot);   // every value, min -> max -> min, one unit per move
                    for (var s = 0; s < trace.Count && csv is not null; s++)
                        csv.WriteLine($"{i},\"{label.Replace("\"", "'")}\",{s},{trace[s].Want:0},{trace[s].X:0.###},{trace[s].Got:0.###}");
                    // A narrow slider (small CI screens) can have fewer whole pixels than values: then a value may be unreachable
                    // by mouse (wheel and double-click still reach it), so allow the neighbouring value, never more.
                    var thumbW = slider.Template?.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track { Thumb: { ActualWidth: > 0 } th } ? th.ActualWidth : 18.0;
                    var pxPerValue = Math.Max(1, slider.ActualWidth - thumbW) / (max - min);
                    var tolerance = pxPerValue >= 1.05 ? 0 : 1;
                    var bad = trace.Where(t => double.IsNaN(t.Got) || Math.Abs(Math.Round(t.Got) - t.Want) > tolerance).ToList();
                    var expected = 2 * (int)(max - min) + 1 + 1;   // up (incl. both ends) and down (incl. both ends): the turn value appears twice
                    Check($"mixer slider {i} ({label}): a drag {min} -> {max} -> {min} through a host rebuild reaches every value in order, no jump, no stuck end",
                        bad.Count == 0 && trace.Count == expected,
                        $"{trace.Count}/{expected} steps, capture after press: {_pressCapture}, first misses: " + string.Join(" ", bad.Take(12).Select(t => $"{t.Want:0}>{t.Got:0.#}")));
                    PumpUi();
                    Check($"mixer slider {i}: the rebuild asked for during the drag runs after it", !ReferenceEquals(Sliders()[i], slider));
                }
            }
            finally { csv?.Dispose(); }
            // One model value, shown by both views: the mixer shows a change made elsewhere (the track list) in place.
            var lead = host.Project.Tracks[0];
            lead.Volume = 77; lead.Pan = 64 + 21;
            window.SyncValues(); PumpUi();
            Check("mixer shows a track-list volume / pan change at once (one model value)",
                Sliders().Any(s => s.Value == 77) && Sliders().Any(s => s.Value == 21));
        }
        finally { window.Close(); }
    }

    private static void TestMixerSliders()
    {
        var host = new FakeMixerHost();
        host.Project.Tracks.Add(MixerTestTrack("Lead", TrackKind.Guitar, 30, volume: 20));
        host.Project.Tracks.Add(MixerTestTrack("Rhythm", TrackKind.Guitar, 30, volume: 20));
        host.Project.Tracks.Add(MixerTestTrack("Bass", TrackKind.Bass, 33));
        using var alive = KeepAlive();
        var window = new MixerWindow(host, null);
        try
        {
            ShowTestWindow(window);
            var sliders = VisualDescendants<Slider>(window).Where(s => s.Maximum > s.Minimum).ToList();
            // Order in the visual tree: group row (pan, volume), then each track row (pan, volume)...
            Check("mixer has pan and volume sliders for groups and tracks", sliders.Count >= 6, $"{sliders.Count} sliders");
            // Dragging is covered by TestMixerSlidersRealInput (every value, both ways, real input path, rebuilding host);
            // the older 10-step drag here raised the button events directly and was timing-sensitive (removed).
        }
        finally { window.Close(); }
    }

    /// <summary>Value mapping of the pointer to a slider, the group value meaning, and keyboard stepping.</summary>
    private static void TestSliderMappingAndGroupValues()
    {
        var slider = new Slider { Minimum = -64, Maximum = 63, Width = 218, Height = 24, Style = (Style)Application.Current.FindResource("ArrangementSlider"), SmallChange = 1 };
        var host = new Grid(); host.Children.Add(slider);
        var window = new Window { Content = host, Width = 300, Height = 100, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -5000, Top = -5000 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            Check("slider mapping: the left end of the handle's travel is the minimum", Math.Abs(TrackControlWidgets.SliderValueAt(slider, 9) - -64) < 0.01);
            Check("slider mapping: the right end of the handle's travel is the maximum", Math.Abs(TrackControlWidgets.SliderValueAt(slider, slider.ActualWidth - 9) - 63) < 0.01);
            Check("slider mapping: the middle is the middle", Math.Abs(TrackControlWidgets.SliderValueAt(slider, slider.ActualWidth / 2) - -0.5) < 0.6);
            Check("slider mapping: beyond the ends clamps", TrackControlWidgets.SliderValueAt(slider, -50) == -64 && TrackControlWidgets.SliderValueAt(slider, 999) == 63);
            TrackControlWidgets.AttachSmoothDrag(slider, 0);
            Check("slider is keyboard focusable and steps by 1", slider.Focusable && slider.SmallChange == 1);
        }
        finally { window.Close(); }

        // A group row is an offset / percentage on top of every track, never an average of them.
        var song = new SongProject();
        var a = MixerTestTrack("A", TrackKind.Guitar, 30, volume: 120); a.Pan = 119;
        var b = MixerTestTrack("B", TrackKind.Guitar, 30, volume: 80); b.Pan = 30;
        song.Tracks.Add(a); song.Tracks.Add(b);
        var group = song.Mixer.Edit(MixerGroups.Guitars);
        group.Pan = -64; group.Volume = 50;
        Check("group pan is an offset on each track (119 -> 55, 30 -> 0), tracks keep their own values",
            MixerGroups.Pan(song, a) == 55 && MixerGroups.Pan(song, b) == 0 && a.Pan == 119 && b.Pan == 30);
        Check("group volume is a percentage of each track's own volume", MixerGroups.Volume(song, a) == 60 && MixerGroups.Volume(song, b) == 40);
        group.Volume = 0;
        Check("group volume 0 silences the group without touching the tracks", MixerGroups.Volume(song, a) == 0 && a.Volume == 120);
        group.Pan = 63; group.Volume = 200;
        Check("group values stay inside their ranges when pushed to the ends", MixerGroups.Pan(song, a) == 127 && MixerGroups.Volume(song, a) == 127);
    }

    private static List<string> MixerTrackOrder(MixerWindow window) =>
        VisualDescendants<Border>(window)
            .Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer strip: ", StringComparison.Ordinal))
            .OrderBy(b => b.PointToScreen(new Point(0, 0)).Y)
            .Select(b => System.Windows.Automation.AutomationProperties.GetName(b)["Mixer strip: ".Length..].Split(',')[0]).ToList();

    private static List<string> MixerGroupOrder(MixerWindow window) =>
        VisualDescendants<Border>(window)
            .Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer group: ", StringComparison.Ordinal))
            .OrderBy(b => b.PointToScreen(new Point(0, 0)).Y)
            .Select(b => System.Windows.Automation.AutomationProperties.GetName(b)["Mixer group: ".Length..].Split(',')[0]).ToList();

    private static SongProject OrderedSong()
    {
        var song = new SongProject();
        song.Tracks.Add(MixerTestTrack("Lead", TrackKind.Guitar, 30));
        song.Tracks.Add(MixerTestTrack("Rhythm", TrackKind.Guitar, 30));
        song.Tracks.Add(MixerTestTrack("Bass", TrackKind.Bass, 33));
        song.Tracks.Add(MixerTestTrack("Piano", TrackKind.Keys, 0));
        return song;
    }

    private static string Names(SongProject p) => string.Join(",", p.Tracks.Select(t => t.Name));

    /// <summary>The shared ordering: track moves, group moves, group changes and nudges (no UI).</summary>
    private static void TestTrackOrderingModel()
    {
        var p = OrderedSong();
        var lead = p.Tracks[0]; var rhythm = p.Tracks[1]; var bass = p.Tracks[2];
        Check("mixer layout follows the track list: groups by first appearance",
            string.Join("|", TrackOrdering.Layout(p).Select(l => l.Group + ":" + string.Join("+", l.Tracks.Select(t => t.Name)))) == "Guitars:Lead+Rhythm|Basses:Bass|Keys:Piano");
        Check("moving a track within its group reorders the track list", TrackOrdering.MoveTrackToGroup(p, lead, "Guitars", 1) && Names(p) == "Rhythm,Lead,Bass,Piano");
        Check("dropping a track into another group joins that group at that place",
            TrackOrdering.MoveTrackToGroup(p, rhythm, "Basses", 1) && Names(p) == "Lead,Bass,Rhythm,Piano" && MixerGroups.GroupOf(p, rhythm) == "Basses" && rhythm.MixerGroup == "Basses");
        Check("dropping it back into its instrument's group clears the explicit choice",
            TrackOrdering.MoveTrackToGroup(p, rhythm, "Guitars", 0) && rhythm.MixerGroup is null && Names(p) == "Rhythm,Lead,Bass,Piano");
        Check("moving a group as a whole moves all its tracks", TrackOrdering.MoveGroup(p, "Basses", 0) && Names(p) == "Bass,Rhythm,Lead,Piano");
        Check("moving a group to where it is changes nothing", !TrackOrdering.MoveGroup(p, "Basses", 0));
        Check("track-list group drag (a run before another run)", TrackOrdering.MoveRun(p, 0, 1, 4) && Names(p) == "Rhythm,Lead,Piano,Bass");
        p = OrderedSong(); lead = p.Tracks[0]; rhythm = p.Tracks[1]; bass = p.Tracks[2];
        Check("nudge down inside a group swaps neighbours", TrackOrdering.Nudge(p, lead, 1) && Names(p) == "Rhythm,Lead,Bass,Piano");
        Check("nudge down past the end of a group joins the next group", TrackOrdering.Nudge(p, lead, 1) && MixerGroups.GroupOf(p, lead) == "Basses" && Names(p) == "Rhythm,Lead,Bass,Piano");
        Check("nudge up at the very top does nothing", !TrackOrdering.Nudge(p, p.Tracks[0], -1));
        p = OrderedSong();
        Check("nudging a group swaps it with its neighbour", TrackOrdering.NudgeGroup(p, "Guitars", 1) && Names(p) == "Bass,Lead,Rhythm,Piano" && !TrackOrdering.NudgeGroup(p, "Keys", 1));
    }

    private static void TestMixerDragAndDrop()
    {
        var host = new FakeMixerHost();
        foreach (var t in OrderedSong().Tracks) host.Project.Tracks.Add(t);
        using var alive = KeepAlive();
        var window = new MixerWindow(host, null);
        try
        {
            ShowTestWindow(window);
            Check("mixer lists the tracks in the track list's order", string.Join(",", MixerTrackOrder(window)) == "Lead,Rhythm,Bass,Piano" && string.Join(",", MixerGroupOrder(window)) == "Guitars,Basses,Keys");

            // Reverse direction: the track list reorders (a drag or Alt+Up there) and the open mixer follows.
            host.Project.MoveTrack(3, 0);
            window.SyncValues();
            PumpUi(); window.UpdateLayout(); PumpUi();
            Check("track list reorder shows in the open mixer (order and groups)",
                string.Join(",", MixerTrackOrder(window)) == "Piano,Lead,Rhythm,Bass" && string.Join(",", MixerGroupOrder(window)) == "Keys,Guitars,Basses",
                string.Join(",", MixerTrackOrder(window)) + " / " + string.Join(",", MixerGroupOrder(window)));
            host.Project.MoveTrack(0, 3);
            window.SyncValues(); PumpUi(); window.UpdateLayout(); PumpUi();

            // Mixer -> track list: drag Rhythm into the Basses group, below Bass (real mouse).
            Point NameAt(string name)
            {
                var text = VisualDescendants<TextBlock>(window).First(t => t.Text == name && t.FontWeight == FontWeights.SemiBold);
                DependencyObject? up = text; while (up is not null && !(up is Border rowBorder && System.Windows.Automation.AutomationProperties.GetName(rowBorder).StartsWith("Mixer ", StringComparison.Ordinal))) up = System.Windows.Media.VisualTreeHelper.GetParent(up);
                _simTarget = up as UIElement ?? text;   // the next press is raised on the strip / group row that holds this name
                return text.PointToScreen(new Point(text.ActualWidth / 2, text.ActualHeight / 2));
            }
            var rhythmStrip = VisualDescendants<Border>(window).First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer strip: Rhythm", StringComparison.Ordinal));
            var bassStrip = VisualDescendants<Border>(window).First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Mixer strip: Bass", StringComparison.Ordinal));
            var from = NameAt("Rhythm");
            var bassBottom = bassStrip.PointToScreen(new Point(60, bassStrip.ActualHeight * 0.85));
            var keysPanel = VisualDescendants<Border>(window).First(b => b.Tag as string == "Basses");   // the group that closes up under the leaving row
            MouseTo(from); MouseDown();
            for (var i = 1; i <= 8; i++) MouseTo(new Point(from.X, from.Y + (bassBottom.Y - from.Y) * i / 8));
            Check("during the drag the neighbouring panels slide to open the gap (transform animation, live)",
                keysPanel.RenderTransform is TranslateTransform { HasAnimatedProperties: true } || keysPanel.RenderTransform is TranslateTransform { Y: < -1 } || Services.UiMotion.DurationMilliseconds(100) <= 0);
            MouseUp();
            Check("after the drop no slide offset is left behind", keysPanel.RenderTransform is not TranslateTransform { Y: not 0 });
            Check("dragging a mixer track into another group reorders the track list and joins that group",
                Names(host.Project) == "Lead,Bass,Rhythm,Piano" && host.Project.Tracks[2].MixerGroup == "Basses" && host.Reorders == 1,
                $"{Names(host.Project)} reorders={host.Reorders}");
            window.Rebuild(); PumpUi(); window.UpdateLayout(); PumpUi();
            Check("the mixer shows the dropped track in its new group", string.Join(",", MixerTrackOrder(window)) == "Lead,Bass,Rhythm,Piano");

            // A whole group: drag Basses above Guitars.
            var guitarsPanel = VisualDescendants<Border>(window).First(b => b.Tag as string == "Guitars");
            var basses = NameAt("Basses");
            var above = guitarsPanel.PointToScreen(new Point(60, 2));
            MouseTo(basses); MouseDown();
            for (var i = 1; i <= 8; i++) MouseTo(new Point(basses.X, basses.Y + (above.Y - basses.Y) * i / 8));
            MouseUp();
            Check("dragging a mixer group above another moves the whole group in the track list", Names(host.Project) == "Bass,Rhythm,Lead,Piano", Names(host.Project));
            window.Rebuild(); PumpUi(); window.UpdateLayout(); PumpUi();

            // Keyboard: the selected row moves with the move commands (the fake host binds plain Up / Down for this check).
            host.Project.Tracks.Clear();
            foreach (var t in OrderedSong().Tracks) host.Project.Tracks.Add(t);
            window.Rebuild(); PumpUi(); window.UpdateLayout(); PumpUi();
            var click = NameAt("Lead");
            MouseTo(click); MouseDown(); MouseUp();
            var before = host.Reorders;
            var keyEvent = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.Down)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(keyEvent);
            Check("the move-down command moves the selected mixer row (and the ordering follows)", host.Reorders == before + 1 && Names(host.Project) == "Rhythm,Lead,Bass,Piano", Names(host.Project));
            window.Rebuild(); PumpUi(); window.UpdateLayout(); PumpUi();
            var selected = VisualDescendants<Border>(window).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Mixer strip: Lead, selected");
            Check("the selected row stays selected after the rebuild and says so by name (not colour alone)", selected);

            // Animation: a rebuilt, reordered mixer glides its rows (same duration constant as the track list).
            var layout = window.CaptureLayout();
            host.Project.MoveTrack(0, 1);
            window.Rebuild(); window.AnimateReorder(layout); PumpUi();
            var animated = VisualDescendants<Border>(window).Any(b => b.RenderTransform is TranslateTransform { HasAnimatedProperties: true });
            Check("a reorder plays the movement animation in the mixer", animated || Services.UiMotion.DurationMilliseconds(100) <= 0);
        }
        finally { window.Close(); }

        var defaults = HotkeyCatalog.BuildMap(new HotkeySettings());
        Check("Track.MoveUp / Track.MoveDown are bindable commands on Alt+Up / Alt+Down",
            defaults.TryGetValue("Alt+Up", out var up) && up == "Track.MoveUp" && defaults.TryGetValue("Alt+Down", out var down) && down == "Track.MoveDown");
    }

    /// <summary>A group move animates the track list's rows and group headers and the timeline lanes, and speed presets step.</summary>
    private static void TestOrderAnimationAndSpeedCommands()
    {
        var song = OrderedSong();
        song.Mixer.ShowGroupsInTrackList = true;
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 900, Height = 500, ShowInTaskbar = false, Left = -5000, Top = -5000 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var before = panel.CaptureRowTops();
            Check("the snapshot holds every track row and every group header", before.Count == song.Tracks.Count + 3);
            TrackOrdering.MoveGroup(song, "Basses", 0);
            panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
            window.UpdateLayout(); PumpUi();
            panel.AnimateReorder(before);
            var animatedHeaders = VisualDescendants<Border>(panel).Count(b => b.Tag as string == "group-header" && b.RenderTransform is TranslateTransform { HasAnimatedProperties: true });
            var animatedRows = VisualDescendants<Border>(panel).Count(b => b.RenderTransform is TranslateTransform { HasAnimatedProperties: true }) - animatedHeaders;
            var reduced = Services.UiMotion.DurationMilliseconds(100) <= 0;
            Check("a group move glides the group headers in the track list", animatedHeaders >= 2 || reduced, $"{animatedHeaders}");
            Check("a group move glides the track rows in the track list", animatedRows >= 3 || reduced, $"{animatedRows}");
            Check("a group move glides the timeline lanes too", panel.TimelineAnimatingLanes || reduced);
        }
        finally { window.Close(); }

        Check("speed up steps to the next preset", MainWindow.NextSpeedPreset(1.0, 1) == 1.25 && MainWindow.NextSpeedPreset(0.5, 1) == 0.75);
        Check("slow down steps to the previous preset", MainWindow.NextSpeedPreset(1.0, -1) == 0.75 && MainWindow.NextSpeedPreset(2.0, -1) == 1.5);
        Check("the speed presets stop at their ends", MainWindow.NextSpeedPreset(2.0, 1) == 2.0 && MainWindow.NextSpeedPreset(0.5, -1) == 0.5);
        Check("a custom speed steps to its neighbouring preset", MainWindow.NextSpeedPreset(0.9, 1) == 1.0 && MainWindow.NextSpeedPreset(0.9, -1) == 0.75);
        var map = HotkeyCatalog.BuildMap(new HotkeySettings());
        Check("speed up / down / reset are bindable commands with free default keys",
            map.TryGetValue("Ctrl+Alt+Up", out var su) && su == "Playback.SpeedUp" && map.TryGetValue("Ctrl+Alt+Down", out var sd) && sd == "Playback.SpeedDown"
            && map.TryGetValue("Ctrl+Alt+D0", out var sr) && sr == "Playback.SpeedReset");
    }

    /// <summary>
    /// A Mixer drag moves the track list's slider in place (one model value, both views), and that in-place sync is far
    /// cheaper per step than the full track-list rebuild (Bind) the drag used to cause. Logs both timings.
    /// </summary>
    private static void TestTrackListFollowsMixerInPlace()
    {
        var song = OrderedSong();
        song.Mixer.ShowGroupsInTrackList = true;
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 1000, Height = 600 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var lead = song.Tracks[0];
            var sliders = VisualDescendants<Slider>(panel).ToList();
            var before = sliders.Count;
            lead.Volume = 61; lead.Pan = 64 - 17; song.Mixer.Edit(MixerGroups.Guitars).Volume = 143;
            panel.SyncMixValues(); PumpUi();
            var after = VisualDescendants<Slider>(panel).ToList();
            Check("track list follows a Mixer change in place: the same sliders, now showing the model values",
                after.Count == before && after.SequenceEqual(sliders) && after.Any(s => s.Value == 61) && after.Any(s => s.Value == 143),   // (track pan is a knob by default: volume + group values)
                string.Join(",", after.Select(s => s.Value.ToString("0"))));
            Check("the in-place sync writes nothing back to the song", lead.Volume == 61 && lead.Pan == 47 && song.Mixer.Levels(MixerGroups.Guitars).Volume == 143);

            const int steps = 60;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < steps; i++) { lead.Volume = 20 + i; panel.SyncMixValues(); window.UpdateLayout(); }
            var inPlace = watch.Elapsed.TotalMilliseconds / steps;
            watch.Restart();
            for (var i = 0; i < steps; i++) { lead.Volume = 20 + i; panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>()); window.UpdateLayout(); }
            var rebuild = watch.Elapsed.TotalMilliseconds / steps;
            Log.Add($"  info  mixer drag step, track-list side: in-place sync {inPlace:0.000} ms vs full rebuild (Bind) {rebuild:0.000} ms per step");
            Check("in-place track-list sync per Mixer drag step is much cheaper than a track-list rebuild", inPlace * 5 < rebuild, $"{inPlace:0.000} ms vs {rebuild:0.000} ms");
        }
        finally { window.Close(); }
    }

    private static void TestTrackListGroupRows()
    {
        var song = OrderedSong();
        song.Mixer.ShowGroupsInTrackList = true;
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 900, Height = 500, ShowInTaskbar = false, Left = -5000, Top = -5000 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var headers = VisualDescendants<Border>(panel).Where(b => b.Tag as string == "group-header").ToList();
            Check("the track list shows a header per group", headers.Count == 3, $"{headers.Count}");
            Check("group rows have no record-arm control", headers.All(h => !VisualDescendants<RecordArmButton>(h).Any()));
            Check("track rows still have their record-arm control", VisualDescendants<RecordArmButton>(panel).Count() == song.Tracks.Count);

            string? requested = null;
            panel.GroupMixerRequested += g => requested = g;
            headers[1].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.MouseRightButtonUpEvent, Source = headers[1] });
            Check("right-click on a group row asks for the mixer at that group", requested == "Basses", requested);

            var shown = false; bool? toggled = null;
            panel.GroupsShownState = () => shown;
            panel.GroupsToggleRequested += on => toggled = on;
            var menu = panel.BuildEmptyAreaMenu();
            var item = menu.Items.OfType<MenuItem>().FirstOrDefault();
            Check("the empty-area menu starts with a checkable 'Show tracks in groups' (off by default)",
                item is { Header: "Show tracks in groups", IsCheckable: true, IsChecked: false } && menu.Items[0] == item);
            shown = true;
            Check("its checked state follows the setting", panel.BuildEmptyAreaMenu().Items.OfType<MenuItem>().First().IsChecked);
            item!.IsChecked = true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("choosing it asks for the groups to be shown", toggled == true);
            Check("the header strip, the column header row and the empty rows area all carry the item",
                panel.EmptyAreaMenusHaveGroupsItem());
        }
        finally { window.Close(); }
        Check("a new project shows groups off by default", !new SongProject().Mixer.ShowGroupsInTrackList);
        Check("'Show tracks in groups' is a bindable command", HotkeyCatalog.All.Any(a => a.Id == "View.ShowTrackGroups"));
    }
}

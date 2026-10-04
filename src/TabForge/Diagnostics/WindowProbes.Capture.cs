using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Window probes, scripted off-screen capture (test / documentation tooling, never used in normal runs):
//   TabForge.exe --profile <scratch> --capture <script.json> <outDir>
// The real main window is created far off-screen (never activated, no taskbar button), a JSON script drives it through the same
// methods the UI uses, and named shots are written as 2x PNGs. Nothing is ever shown on a monitor: windows and dialogs are placed
// off-screen, and menus are rendered from their templates without ever opening a popup (a popup would be pulled back onto a monitor).
// Reference: docs/CAPTURE_SCRIPT.md.
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
    private const double CaptureOffscreen = -20000;
    private const double CaptureScale = 2.0;

    /// <summary>Called by App before Show() when --capture is used: off-screen, not activated, no taskbar button, 2x root DPI.</summary>
    internal void PrepareOffscreenCapture()
    {
        Window.ShowInTaskbar = false;
        Window.ShowActivated = false;
        Window.WindowStartupLocation = WindowStartupLocation.Manual;
        Window.WindowState = WindowState.Normal;
        Window.Left = CaptureOffscreen;
        Window.Top = CaptureOffscreen;
        TryRootDpi(Window);
        // Windows limits a window to about the primary monitor's size; a capture window may be larger than the owner's screen.
        Window.SourceInitialized += (_, _) =>
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(Window).Handle)?.AddHook(UnlimitedTrackSize);
    }

    private static IntPtr UnlimitedTrackSize(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        // MINMAXINFO: ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize (pairs of ints)
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 8, 20000);    // ptMaxSize.x
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 12, 20000);   // ptMaxSize.y
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 32, 20000);   // ptMaxTrackSize.x
        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 36, 20000);   // ptMaxTrackSize.y
        return IntPtr.Zero;   // not handled: WPF still applies the window's own minimum
    }

    private static void TryRootDpi(Visual visual)
    {
        try { VisualTreeHelper.SetRootDpi(visual, new DpiScale(CaptureScale, CaptureScale)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Root DPI not set: {ex.Message}"); }
    }

    /// <summary>Runs <paramref name="scriptPath"/> after the window is up, writes capture-report.json / capture.log in <paramref name="outDir"/>, then exits.</summary>
    public void RunCaptureScript(string scriptPath, string outDir)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var exit = 0;
            var runner = new CaptureRun(this, outDir);
            try { await runner.RunAsync(scriptPath); }
            catch (Exception ex) { exit = 1; runner.Log($"FAILED: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
            finally
            {
                try { runner.Finish(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Capture clean-up failed: {ex}"); }
                DialogHost.Capture = null;
                ContextMenuCapture = null;
                _confirmOnClose = false;
                Application.Current.Shutdown(exit);
            }
        }));
    }

    private sealed partial class CaptureRun
    {
        private readonly WindowProbes _w;
        private readonly string _out;
        private readonly StringBuilder _log = new();
        private readonly List<Dictionary<string, object>> _shots = new();
        private readonly Dictionary<string, Window> _tools = new(StringComparer.OrdinalIgnoreCase);
        private FrameworkElement? _lastMenu;
        private string _toolName = "tool";
        private string _theme = "dark";
        private string _paper = "";
        private string _notation = "both";
        private string _view = "vertical";
        private string _openName = "demo";
        private int _track, _bar = 1;
        private double _uiScale = 1;
        private string? _selection, _page, _menu, _window;
        private Size _size = new(1600, 1000);

        public CaptureRun(WindowProbes w, string outDir)
        {
            _w = w;
            _out = FilePathPolicy.OutputDirectory(outDir, "capture folder");
            Directory.CreateDirectory(_out);
        }

        private string _name = "capture";
        public int Failures;

        public void Log(string line) { _log.AppendLine(line); }

        public async Task RunAsync(string scriptPath)
        {
            if (!UserPaths.IsProfile || UserPaths.ProfileIsRealUserFolder)
                throw new InvalidOperationException("--capture needs --profile <scratch folder> (never the real user folder).");
            _name = Path.GetFileNameWithoutExtension(scriptPath);
            using var doc = JsonDocument.Parse(File.ReadAllText(scriptPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("The script must be a JSON array of steps.");
            // Defaults, so a script can start with just shots: demo song, dark, 1600x1000.
            await _w.Settle(1200);
            SetSize(_size);
            ApplyTheme("dark");
            OpenSong("demo");
            await _w.Settle(900);
            var n = 0;
            foreach (var step in doc.RootElement.EnumerateArray())
            {
                n++;
                var props = step.EnumerateObject().ToList();
                if (props.Count == 0) continue;
                var verb = props[0].Name;
                var value = props[0].Value;
                var args = props.Skip(1).ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                try { await StepAsync(verb, value, args); }
                catch (Exception ex)
                {
                    // A failed step is logged and the script goes on (one missing page must not lose the other shots); the exit code reports it.
                    Log($"step {n} {verb}: FAILED {ex.GetBaseException().Message}");
                    Failures++;
                }
            }
            if (Failures > 0) throw new InvalidOperationException($"{Failures} step(s) failed (see capture.log)");
        }

        public void Finish()
        {
            foreach (var tool in _tools.Values.ToList()) CloseWindow(tool);
            _tools.Clear();
            foreach (var undo in _cleanup) { try { undo(); } catch (Exception ex) { Log($"clean-up failed: {ex.Message}"); } }
            var report = new Dictionary<string, object> { ["shots"] = _shots };
            File.WriteAllText(Path.Combine(_out, _name + ".report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_out, _name + ".log"), _log.ToString(), new UTF8Encoding(false));
        }

        private static void CloseWindow(Window window)
        {
            try { window.Close(); } catch (InvalidOperationException) { }
        }

        // ---------- steps ----------

        private async Task StepAsync(string verb, JsonElement value, Dictionary<string, JsonElement> args)
        {
            switch (verb.ToLowerInvariant())
            {
                case "open": OpenSong(value.GetString() ?? "demo"); await _w.Settle(900); break;
                case "theme": ApplyTheme(value.GetString() ?? "dark"); await _w.Settle(500); break;
                case "paper": _paper = value.GetString() ?? ""; _w.SetPaper(string.Equals(_paper, "dark", StringComparison.OrdinalIgnoreCase)); await _w.Settle(400); break;
                case "size": SetSize(new Size(value[0].GetDouble(), value[1].GetDouble())); await _w.Settle(500); break;
                case "scale": _uiScale = value.GetDouble(); _w._settings.Appearance.UiScale = _uiScale; _w.ApplyAppearance(); await _w.Settle(600); break;
                case "notation": SetNotation(value.GetString() ?? "both"); await _w.Settle(500); break;
                case "view": SetScoreView(value.GetString() ?? "vertical"); await _w.Settle(600); break;
                case "track": _track = value.GetInt32(); _w.TrackMixerGrid.SelectedIndex = _track; await _w.Settle(500); break;
                case "bar": GoToBar(value.GetInt32()); await _w.Settle(400); break;
                case "select": SelectBars(value[0].GetInt32(), value[1].GetInt32()); await _w.Settle(400); break;
                case "clear-selection": _w._selection.Clear(SelectionOrigin.Command); _selection = null; await _w.Settle(300); break;
                case "panel": ShowPanel(value.GetString() ?? ""); await _w.Settle(500); break;
                case "layout": _w.SwitchLayout(value.GetString() ?? "Compose"); await _w.Settle(800); break;
                case "instrument": SetInstrument(value.GetString() ?? "auto"); await _w.Settle(500); break;
                case "highlight-scale": _w._scaleHighlight = value.ValueKind == JsonValueKind.Null ? null : value.GetString(); _w.RefreshInstrument(); await _w.Settle(500); break;
                case "playhead": SetPlayhead(value[0].GetInt32(), value.GetArrayLength() > 1 ? value[1].GetDouble() : 0); await _w.Settle(300); break;
                case "window": OpenTool(value.GetString() ?? "", args); await _w.Settle(700); break;
                case "page": SelectPage(value.GetString() ?? ""); await _w.Settle(400); break;
                case "search": SetSearch(value.GetString() ?? ""); await _w.Settle(500); break;
                case "close": CloseTool(value.GetString() ?? "all"); await _w.Settle(200); break;
                case "menu": await ShowMenuAsync(value.GetString() ?? ""); break;
                case "context": ShowContext(value.GetString() ?? ""); await _w.Settle(300); break;
                case "dump": DumpSong(); break;
                case "example-plugins": ExampleSlots(); _w.RefreshTracks(); await _w.Settle(400); break;
                case "quarantine-example":   // the next FX chain window shows the "Switched off after a crash ... Allow again" state for the example instrument
                {
                    var path = _w.SelectedTrack?.Rig.Plugins.FirstOrDefault(p => p.Name == "Example instrument")?.Path ?? throw new InvalidOperationException("no example instrument (use example-plugins first)");
                    _w._settings.Plugins.Quarantined.Add(path);
                    _cleanup.Add(() => _w._settings.Plugins.Quarantined.Remove(path));
                    break;
                }
                case "midi-add": foreach (var type in value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(e => e.GetString() ?? "") : new[] { value.GetString() ?? "" }) MidiWindow().CaptureAdd(type); await _w.Settle(300); break;
                case "midi-select": MidiWindow().CaptureSelect(value.GetInt32()); await _w.Settle(300); break;
                case "midi-search": MidiWindow().CaptureSearch(value.GetString() ?? ""); await _w.Settle(300); break;
                case "midi-param": MidiWindow().CaptureParam(value[0].GetString() ?? "", value[1].GetDouble()); await _w.Settle(300); break;
                case "popup": ShowPopup(value.GetString() ?? ""); break;
                case "audio-track": await AddExampleAudioTrackAsync(); break;
                case "timeline-drag": TimelineDrag(value.GetString() ?? "drop"); await _w.Settle(400); break;
                case "playhead-style": _w.Arrangement.PlayheadStyle = value.GetString() ?? "Line"; await _w.Settle(300); break;
                case "solo": _w._project.Tracks[value.GetInt32()].Solo = true; _w.RefreshTracks(); await _w.Settle(300); break;
                case "clip-edit": case "delete-prompt": case "marker-size": await ExtraStepAsync(verb, value); break;
                case "frames": await FramesAsync(value.GetString() ?? "frames", args); break;
                case "wait": await _w.Settle(value.GetInt32()); break;
                case "shot": await ShotAsync(value.GetString() ?? "shot", args); break;
                default: throw new InvalidOperationException($"unknown step '{verb}'");
            }
        }

        private void SetSize(Size size)
        {
            _size = size;
            _w.Window.WindowState = WindowState.Normal;
            _w.Window.Width = Math.Max(_w.Window.MinWidth, size.Width);
            _w.Window.Height = Math.Max(_w.Window.MinHeight, size.Height);
            _w.Window.Left = CaptureOffscreen;
            _w.Window.Top = CaptureOffscreen;
            _w.UpdateLayout();
        }

        private void ApplyTheme(string theme)
        {
            _theme = theme;
            _w.ApplyThemeOverride(string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark");
        }

        /// <summary>Only the bundled demo song or a file under a "samples" folder (or the capture folder) may be opened: never the owner's Tabs.</summary>
        private void OpenSong(string name)
        {
            var path = name;
            if (name.Equals("demo", StringComparison.OrdinalIgnoreCase)) path = FindDemo();
            else if (!Path.IsPathRooted(name)) path = Path.GetFullPath(name);
            var norm = path.Replace('/', '\\');
            var allowed = norm.Contains("\\samples\\", StringComparison.OrdinalIgnoreCase) || norm.StartsWith(_out, StringComparison.OrdinalIgnoreCase);
            if (!allowed || norm.Contains("\\Tabs\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidOperationException($"only the demo song or a file in a 'samples' folder may be opened ({Path.GetFileName(path)})");
            _openName = Path.GetFileNameWithoutExtension(path);
            _w.OpenStartupFile(path, background: false);
            _track = 0; _bar = 1;
        }

        private static string FindDemo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var up = 0; up < 8 && dir is not null; up++, dir = dir.Parent)
                foreach (var sub in new[] { "samples", "Samples" })
                {
                    var candidate = Path.Combine(dir.FullName, sub);
                    if (!Directory.Exists(candidate)) continue;
                    var file = Directory.EnumerateFiles(candidate, "*Ashen Meridian*").OrderBy(f => f.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
                    if (file is not null) return file;
                }
            throw new FileNotFoundException("The demo song was not found next to the application (samples folder).");
        }

        private void SetNotation(string mode)
        {
            _notation = mode.ToLowerInvariant();
            _w.SetNotation(_notation switch { "tab" => NotationMode.TabOnly, "staff" or "notation" => NotationMode.StaffOnly, _ => NotationMode.TabAndStaff });
        }

        private void SetScoreView(string view)
        {
            _view = view.ToLowerInvariant();
            switch (_view)
            {
                case "horizontal": _w.SetHorizontalScoreView(true); break;
                case "vertical": _w.SetHorizontalScoreView(false); _w.SetContinuousScoreView(false); break;
                case "page": _w.SetContinuousScoreView(false); break;
                case "continuous": _w.SetContinuousScoreView(true); break;
                default: throw new InvalidOperationException($"unknown score view '{view}' (vertical, horizontal, page, continuous)");
            }
        }

        private void GoToBar(int bar)
        {
            _bar = bar;
            _w.Editor.SetBar(Math.Clamp(bar - 1, 0, Math.Max(0, _w.MaxMeasures() - 1)));
            _w.ScrollToCursor();
        }

        private void SelectBars(int first, int last)
        {
            _selection = $"bars {first}-{last}";
            _w._selection.SetRange(_w.Editor.SelectedTrackIndex, first - 1, last - 1, SelectionOrigin.Command);
        }

        private void ShowPanel(string spec)
        {
            var dock = _w._dockWorkspace ?? throw new InvalidOperationException("no dock workspace");
            if (spec.StartsWith("hide:", StringComparison.OrdinalIgnoreCase)) { dock.SetPanelVisible(spec[5..], false); return; }
            if (!dock.IsPanelVisible(spec)) dock.SetPanelVisible(spec, true);
            dock.SelectPanel(spec);
        }

        private void SetInstrument(string view)
        {
            _w.SetInstrumentView(view.ToLowerInvariant() switch
            {
                "fretboard" => InstrumentViews.Fretboard, "keyboard" => InstrumentViews.Keyboard, "drums" => InstrumentViews.Drums, _ => null
            });
        }

        private void SetPlayhead(int bar, double fraction)
        {
            var w = _w;
            var index = Math.Max(0, bar - 1);
            w._playheadBar = index;
            w._playheadFraction = fraction;
            w.Editor.PlaybackActive = true;
            w.Editor.PlaybackTrackIndex = Math.Max(0, w.Editor.SelectedTrackIndex);
            w.Editor.PlaybackFraction = fraction;
            w.Editor.SetPlayhead(index, 0);
            w.Arrangement.SetPlayhead(index, fraction, playbackActive: true);
            w.Playhead.SetGeometry(w.Editor.PlayheadGeometry());
        }

        /// <summary>Writes the song's sections, track summary and bar structure (time signatures, tempo changes, repeats) to &lt;script&gt;.song.txt, to confirm bar numbers.</summary>
        private void DumpSong()
        {
            var project = _w._project;
            var text = new StringBuilder();
            text.AppendLine($"title: {project.Title}; tempo {project.Tempo}; {project.TimeSignatureNumerator}/{project.TimeSignatureDenominator}; key {project.KeySignature}");
            text.AppendLine("sections (bar = 1-based):");
            foreach (var marker in project.Markers.OrderBy(m => m.MeasureIndex)) text.AppendLine($"  bar {marker.MeasureIndex + 1}: {marker.Title} (length {(marker.LengthBars?.ToString() ?? "to next")})");
            text.AppendLine("tracks:");
            for (var i = 0; i < project.Tracks.Count; i++)
            {
                var t = project.Tracks[i];
                text.AppendLine($"  {i} (0-based) {t.Name}: {t.Kind}, {t.InstrumentName}, {t.StringTunings.Count} strings, capo {t.Capo}, transpose {t.Transpose}, volume {t.Volume}, pan {t.Pan}, bars {t.Measures.Count}, first bars with notes {string.Join(",", t.Measures.Select((m, b) => (m, b)).Where(x => x.m.Cells.Any(c => c.Notes.Count > 0)).Take(10).Select(x => x.b + 1))}");
            }
            text.AppendLine("bar structure on track 0 (only bars with something special):");
            var first = project.Tracks.FirstOrDefault();
            for (var b = 0; first is not null && b < first.Measures.Count; b++)
            {
                var m = first.Measures[b];
                var notes = new List<string>();
                if (m.TimeSigNum > 0) notes.Add($"time signature {m.TimeSigNum}/{m.TimeSigDenom}");
                if (m.TempoChange is not null) notes.Add($"tempo {m.TempoChange}");
                if (m.RepeatStart) notes.Add("repeat start");
                if (m.RepeatEnd) notes.Add($"repeat end x{m.RepeatCount}");
                if (m.AlternateEnding != 0) notes.Add($"alternate ending {m.AlternateEnding}");
                if (notes.Count > 0) text.AppendLine($"  bar {b + 1}: {string.Join(", ", notes)}");
            }
            File.WriteAllText(Path.Combine(_out, _name + ".song.txt"), text.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Photographs one of the playbar's popovers (metronome, countin, loop) from its template, without opening a popup.</summary>
        private void ShowPopup(string name)
        {
            var popup = name.ToLowerInvariant() switch
            {
                "metronome" => _w.MetronomeSettingsPopup, "countin" => _w.CountInSettingsPopup, "loop" => _w.LoopSettingsPopup,
                _ => throw new InvalidOperationException($"unknown popup '{name}' (metronome, countin, loop)")
            };
            _menu = "popup:" + name;
            _lastMenu = RenderHost(popup, () =>
            {
                var child = popup.Child as FrameworkElement ?? throw new InvalidOperationException("popup has no content");
                popup.Child = null;   // nothing to show on screen while the real handler fills the controls and "opens" the (empty) popup
                var click = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent };
                switch (name.ToLowerInvariant())
                {
                    case "metronome": _w.MetronomeButton_PreviewMouseRightButtonUp(_w.MetronomeButton, click); break;
                    case "countin": _w.CountInButton_PreviewMouseRightButtonUp(_w.CountInButton, click); break;
                    default: _w.LoopButton_PreviewMouseRightButtonUp(_w.LoopButton, click); break;
                }
                popup.IsOpen = false;
                return (child, () => popup.Child = child);
            });
        }
    }
}

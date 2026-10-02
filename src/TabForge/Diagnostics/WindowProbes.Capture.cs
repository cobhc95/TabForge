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

    private sealed class CaptureRun
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
                case "solo": _w._project.Tracks[value.GetInt32()].Solo = true; _w.RefreshTracks(); await _w.Settle(300); break;
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
                text.AppendLine($"  {i} (0-based) {t.Name}: {t.Kind}, {t.InstrumentName}, {t.StringTunings.Count} strings, capo {t.Capo}, transpose {t.Transpose}, volume {t.Volume}, pan {t.Pan}, bars {t.Measures.Count}");
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

        // ---------- tool windows and dialogs ----------

        /// <summary>Shows a window off-screen, not activated, and remembers it under the current tool name.</summary>
        private bool Adopt(Window window)
        {
            window.Owner = null;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = CaptureOffscreen;
            window.Top = CaptureOffscreen;
            TryRootDpi(window);
            window.Show();
            window.UpdateLayout();
            _tools[_toolName] = window;
            if (window is PreferencesWindow p) _prefs = p;
            return false;   // a modal dialog's caller sees "cancelled"
        }

        private PreferencesWindow? _prefs;

        private MidiProcessingWindow MidiWindow() =>
            _tools.GetValueOrDefault("MidiProcessing") as MidiProcessingWindow ?? throw new InvalidOperationException("open the MidiProcessing window first");

        private void OpenTool(string name, Dictionary<string, JsonElement> args)
        {
            var w = _w;
            _window = name;
            _toolName = name;
            if (_tools.TryGetValue(name, out var existing)) { CloseWindow(existing); _tools.Remove(name); }
            DialogHost.Capture = dialog => Adopt(dialog);
            try
            {
                switch (name.ToLowerInvariant())
                {
                    case "mixer": Adopt(new MixerWindow(w.Window, w.Window) { Width = 1180, Height = 560 }); break;
                    case "render": w._settings.Render.Directory = "Renders"; Adopt(new RenderWindow(new RenderContext { Project = w._project, Settings = w._settings, Engine = Audio.AudioEngineClient.Instance }, w.Window)); break;
                    case "preferences":
                        _page = args.TryGetValue("page", out var page) ? page.GetString() : null;
                        PreferencesWindow.InitialCategory = _page;
                        w.Prefs_Click(w, new RoutedEventArgs());
                        break;
                    case "fxchain": case "fxchain-example": FxChain(name.EndsWith("example", StringComparison.OrdinalIgnoreCase)); break;
                    case "wiring": case "midiprocessing": ExampleRigWindow(name.Equals("wiring", StringComparison.OrdinalIgnoreCase)); break;
                    case "addtrack": w.AddTrackWithWindow(); break;
                    case "trackproperties": w.TrackProps_Click(w, new RoutedEventArgs()); break;
                    case "tuner": TunerWindow.Open(w.Window, Audio.AudioEngineClient.Instance, () => w.SelectedTrack); AdoptOpen<TunerWindow>(); break;
                    case "songinfo": w.ScoreInfo_Click(w, new RoutedEventArgs()); break;
                    case "projectsettings": w.ProjectSettings_Click(w, new RoutedEventArgs()); break;
                    case "globaltuning": w.ShowGlobalTuningWindow(); break;
                    case "scalefinder": w.OpenScaleFinder(); break;
                    case "instrumentpicker": InstrumentPickerWindow.Show(w.Window, w.SelectedTrack?.InstrumentName, Colors.SteelBlue); break;
                    case "scoretextstyle": w.ShowScoreTextStyleWindow(); break;
                    case "mixtable": w.ShowMixTable(); break;
                    case "newfromtemplate": w.ApplyTemplate_Click(w, new RoutedEventArgs()); break;
                    case "commandpalette": Adopt(new Views.CommandPalette(w.Window, w._settings.Hotkeys) { KeepOpen = true }); break;
                    case "addplugin": PluginBrowser.Choose(w.Window, w.Window); break;
                    case "shortcuts":
                        _toolName = "Preferences";
                        PreferencesWindow.InitialCategory = SettingsCatalog.Hotkeys;
                        w.Prefs_Click(w, new RoutedEventArgs());
                        break;
                    default: throw new InvalidOperationException($"unknown window '{name}'");
                }
            }
            finally { DialogHost.Capture = null; }
            if (!_tools.ContainsKey(_toolName) && !_tools.ContainsKey(name)) throw new InvalidOperationException($"window '{name}' did not open");
            if (name.Equals("CommandPalette", StringComparison.OrdinalIgnoreCase) && args.TryGetValue("text", out var text))
                SetText(_tools[name], text.GetString() ?? "");
        }

        private void AdoptOpen<T>() where T : Window
        {
            foreach (var window in Application.Current.Windows.OfType<T>().ToList())
            {
                if (_tools.ContainsValue(window)) continue;
                window.ShowInTaskbar = false;
                window.Left = CaptureOffscreen; window.Top = CaptureOffscreen;
                _tools[_toolName] = window;
            }
        }

        /// <summary>Neutral stand-in slots ("Example instrument" / "Example effect") for the plug-in screens; removed again in Finish.</summary>
        private (TabForge.Plugins.PluginSlot Instrument, TabForge.Plugins.PluginSlot Effect, TrackModel Track) ExampleSlots()
        {
            var track = _w.SelectedTrack ?? throw new InvalidOperationException("no track selected");
            var existingInstrument = track.Rig.Plugins.FirstOrDefault(p => p.Name == "Example instrument");
            var existingEffect = track.Rig.Plugins.FirstOrDefault(p => p.Name == "Example effect");
            if (existingInstrument is not null && existingEffect is not null) return (existingInstrument, existingEffect, track);   // later steps reuse the same two slots
            // Placeholder files in the capture folder, approved, so the slots look trusted (no "Blocked" message with a path).
            var folder = Path.Combine(_out, "example");
            Directory.CreateDirectory(folder);
            var instrumentPath = Path.Combine(folder, "Example instrument.dll");
            var effectPath = Path.Combine(folder, "Example effect.dll");
            File.WriteAllText(instrumentPath, "placeholder"); File.WriteAllText(effectPath, "placeholder");
            TabForge.Plugins.PluginTrust.Approve(_w._settings.Plugins, instrumentPath);
            TabForge.Plugins.PluginTrust.Approve(_w._settings.Plugins, effectPath);
            var instrument = new TabForge.Plugins.PluginSlot { Name = "Example instrument", Path = instrumentPath, Format = "VST2", Type = TabForge.Plugins.PluginSlotType.Instrument };
            var effect = new TabForge.Plugins.PluginSlot { Name = "Example effect", Path = effectPath, Format = "VST2", Wet = 80 };
            track.Rig.Plugins.Add(instrument);
            track.Rig.Plugins.Add(effect);
            _cleanup.Add(() => { track.Rig.Plugins.Remove(effect); track.Rig.Plugins.Remove(instrument); });
            return (instrument, effect, track);
        }

        private void FxChain(bool example)
        {
            var track = example ? ExampleSlots().Track : _w.SelectedTrack ?? throw new InvalidOperationException("no track selected");
            Adopt(new FxChainWindow(_w.Window, track, _w.Window));
        }

        private void ExampleRigWindow(bool wiring)
        {
            var (instrument, _, track) = ExampleSlots();
            Window window = wiring
                ? new WiringWindow(_w.Window, track, instrument, _w.Window, change => change())
                : new MidiProcessingWindow(_w.Window, track, instrument, _w.Window, change => change());
            Adopt(window);
        }

        private readonly List<Action> _cleanup = new();

        private void SetText(Window window, string text) =>
            FindVisual<TextBox>(window)?.SetCurrentValue(TextBox.TextProperty, text);

        private void SelectPage(string page)
        {
            var prefs = _prefs ?? throw new InvalidOperationException("open the Preferences window first");
            var button = FindVisuals<ButtonBase>(prefs.NavigationPanel).FirstOrDefault(b => TextOf(b).Equals(page, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"no Preferences page '{page}'");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            prefs.UpdateLayout();
            _page = page;
        }

        private void SetSearch(string text)
        {
            var prefs = _prefs ?? throw new InvalidOperationException("open the Preferences window first");
            prefs.SearchBox.Text = text;
        }

        private void CloseTool(string name)
        {
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var tool in _tools.Values.ToList()) CloseWindow(tool);
                _tools.Clear();
                _prefs = null; _window = null;
                return;
            }
            if (_tools.TryGetValue(name, out var window)) { CloseWindow(window); _tools.Remove(name); }
            if (_window == name) _window = null;
        }

        // ---------- menus (rendered from their templates; no popup is ever opened) ----------

        private async Task ShowMenuAsync(string path)
        {
            _menu = path;
            var menu = FindVisuals<Menu>(_w.Window).FirstOrDefault(m => m.Items.Count > 3) ?? throw new InvalidOperationException("main menu not found");
            ItemsControl owner = menu;
            MenuItem? item = null;
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                item = owner.Items.OfType<MenuItem>().FirstOrDefault(i => Clean(i.Header?.ToString()).Equals(Clean(part), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"menu item '{part}' not found in '{path}'");
                item.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, item));   // lets lazily filled submenus (Layouts, Open recent) build themselves
                owner = item;
            }
            if (item is null) throw new InvalidOperationException("empty menu path");
            if (_w._layoutsMenu is not null) _w.RefreshLayoutsMenu();
            await _w.Settle(150);
            _lastMenu = RenderHost(item, () =>
            {
                item.ApplyTemplate();
                var popup = item.Template?.FindName("PART_Popup", item) as Popup ?? throw new InvalidOperationException("menu has no popup part");
                var child = popup.Child as FrameworkElement ?? throw new InvalidOperationException("menu popup has no content");
                popup.Child = null;
                return (child, () => popup.Child = child);
            });
        }

        /// <summary>Adds an audio track with one synthetic clip (a generated tone file in the capture folder), selects it and removes it when the run ends.</summary>
        private async Task AddExampleAudioTrackAsync()
        {
            _w.Window.AddAudioTrack();
            var track = _w._project.Tracks[^1];
            var path = Path.Combine(_out, "example-clip.wav");
            WriteExampleWav(path, 8);
            track.AudioClips.Add(new AudioClip { Name = "Example clip", File = path, StartSec = _w.SongClock.BarStartSec(_w._project, 2), SourceLengthSec = 8, FileLengthSec = 8 });
            _cleanup.Add(() => { _w._project.Tracks.Remove(track); _w.RefreshTracks(); _w.RefreshArrangement(); });
            _track = _w._project.Tracks.IndexOf(track);
            _w.RefreshTracks(); _w.RefreshArrangement();
            _w.TrackMixerGrid.SelectedIndex = _track;
            await _w.Settle(600);
        }

        private static void WriteExampleWav(string path, int seconds)
        {
            const int rate = 22050;
            var samples = new short[rate * seconds];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (double)i / rate;
                samples[i] = (short)(9000 * Math.Sin(2 * Math.PI * 220 * t) * (0.55 + 0.45 * Math.Sin(2 * Math.PI * 1.5 * t)));
            }
            using var w = new BinaryWriter(File.Create(path));
            w.Write("RIFF"u8.ToArray()); w.Write(36 + samples.Length * 2); w.Write("WAVEfmt "u8.ToArray());
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(samples.Length * 2);
            foreach (var x in samples) w.Write(x);
        }

        private static string Clean(string? header) => (header ?? "").Replace("_", "").Replace("…", "").Trim();

        private void ShowContext(string kind)
        {
            _menu = "context:" + kind;
            ContextMenu? caught = null;
            ContextMenuCapture = m => caught = m;
            try
            {
                switch (kind.ToLowerInvariant())
                {
                    case "score": _w.ShowScoreContextMenu(new Point(200, 200)); break;
                    case "note":
                        _w.ShowNoteContextMenu(new Views.ContextMenuEventArgs(new Point(200, 200)) { Measure = Math.Max(0, _bar - 1), Cell = 0, StringIndex = 2, OverBeat = true, OnNote = true });
                        break;
                    case "timeline": _w.ShowArrangementContextMenu(Math.Max(0, _bar - 1), Math.Max(0, _track)); break;
                    case "fretboard": _w.ShowInstrumentContextMenu(fromKeyboard: false); break;
                    case "timeline-empty": caught = _w.Arrangement.BuildEmptyAreaMenu(); break;
                    case "track-row": caught = _w.Window.TrackRowMenu(Math.Clamp(_track, 0, _w._project.Tracks.Count - 1)); break;
                    default: throw new InvalidOperationException($"unknown context menu '{kind}' (score, note, timeline, fretboard, timeline-empty, track-row)");
                }
            }
            finally { ContextMenuCapture = null; }
            var menu = caught ?? throw new InvalidOperationException($"the '{kind}' menu did not open");
            // A ContextMenu cannot be a child of another element: it is laid out as its own root.
            TryRootDpi(menu);
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            _menuBitmap = Render(menu, menu.ActualWidth, menu.ActualHeight);
            _menuSize = new Size(menu.ActualWidth, menu.ActualHeight);
            _lastMenu = menu;
        }

        /// <summary>Lays <paramref name="take"/>'s element out in a detached host at 2x DPI so it can be photographed without a popup.</summary>
        private FrameworkElement RenderHost(DependencyObject owner, Func<(FrameworkElement Element, Action Restore)> take)
        {
            var (element, restore) = take();
            var host = new Border { Background = (Brush)_w.FindResource("WindowBrush"), Child = element, UseLayoutRounding = true };
            TryRootDpi(host);
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            var bitmap = Render(host, host.ActualWidth, host.ActualHeight);
            host.Child = null;
            restore();
            _menuBitmap = bitmap;
            _menuSize = new Size(host.ActualWidth, host.ActualHeight);
            return host;
        }

        private BitmapSource? _menuBitmap;
        private Size _menuSize;

        // ---------- shots ----------

        private async Task ShotAsync(string id, Dictionary<string, JsonElement> args)
        {
            await _w.Settle(args.TryGetValue("settle", out var s) ? s.GetInt32() : 350);
            var target = args.TryGetValue("target", out var t) ? t.GetString() ?? "window" : "window";
            BitmapSource bitmap;
            string description = target;
            if (target.Equals("menu", StringComparison.OrdinalIgnoreCase))
            {
                bitmap = _menuBitmap ?? throw new InvalidOperationException("no menu has been opened (use the menu or context step first)");
                description = "menu:" + _menu;
            }
            else
            {
                var element = Resolve(target);
                ScrubHardwareNames();
                RenderTreeFresh(element);
                bitmap = Render(element, element.ActualWidth, element.ActualHeight, args.TryGetValue("region", out var r) ? r : (JsonElement?)null);
            }
            var file = id + ".png";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            FilePathPolicy.WriteAtomically(FilePathPolicy.OutputFile(Path.Combine(_out, file), "screenshot", ".png"), encoder.Save);
            var state = new Dictionary<string, object>
            {
                ["id"] = id, ["file"] = file, ["target"] = description, ["width"] = bitmap.PixelWidth, ["height"] = bitmap.PixelHeight,
                ["theme"] = _theme, ["window"] = $"{(int)_size.Width}x{(int)_size.Height}", ["song"] = _openName,
                ["track"] = _track, ["bar"] = _bar, ["notation"] = _notation, ["scoreView"] = _view, ["uiScale"] = _uiScale,
            };
            if (_selection is not null) state["selection"] = _selection;
            if (_window is not null) state["toolWindow"] = _window;
            if (_page is not null) state["page"] = _page;
            if (target == "menu" && _menu is not null) state["menu"] = _menu;
            _shots.Add(state);
            Log($"shot {id}: {bitmap.PixelWidth}x{bitmap.PixelHeight} ({description})");
        }

        /// <summary>Keeps the owner's audio hardware out of pictures: the status bar's device text becomes a generic one.</summary>
        private void ScrubHardwareNames()
        {
            foreach (var root in new DependencyObject[] { _w.Window }.Concat(_tools.Values))
                foreach (var text in FindVisuals<TextBlock>(root))
                    if (text.Text.StartsWith("WASAPI", StringComparison.Ordinal) || text.Text.StartsWith("ASIO", StringComparison.Ordinal))
                        text.Text = "WASAPI (shared): Windows default device";
        }

        private static void RenderTreeFresh(FrameworkElement element) => element.UpdateLayout();

        private FrameworkElement Resolve(string target)
        {
            if (target.Equals("window", StringComparison.OrdinalIgnoreCase)) return _w.Window;
            if (target.StartsWith("window:", StringComparison.OrdinalIgnoreCase))
            {
                var name = target[7..];
                return _tools.TryGetValue(name, out var tool) ? tool : throw new InvalidOperationException($"window '{name}' is not open");
            }
            if (target.StartsWith("panel:", StringComparison.OrdinalIgnoreCase)) return PanelOf(target[6..]);
            if (target.StartsWith("element:", StringComparison.OrdinalIgnoreCase)) return FindElement(target[8..]);
            return target.ToLowerInvariant() switch
            {
                "score" => _w.ScoreScroll, "timeline" => _w.Arrangement, "fretboard" => _w.InstrumentHost, "tracks" => _w.TrackMixerGrid,
                "tools" => _w.ToolsPanelContent, "sections" => _w.SectionsPanelContent, "transport" => _w.ControllerPanel, "titlebar" => _w.TitleBar,
                _ => FindElement(target)
            };
        }

        private FrameworkElement PanelOf(string id)
        {
            var content = id.ToLowerInvariant() switch
            {
                "instrument" or "fretboard" => (FrameworkElement)_w.InstrumentHost, "timeline" => _w.ArrangementHost, "tools" => _w.ToolsPanelContent,
                "sections" => _w.SectionsPanelContent, "practice" => _w.LowerPanelScroll, "playback" => _w.ControllerPanel,
                "structure" or "rhythm" or "layout" => _w._palettePanelContents[id.ToLowerInvariant()],
                _ => throw new InvalidOperationException($"unknown dock panel '{id}'")
            };
            // The pane including its tab strip: the nearest ancestor that contains the dock's tab surfaces.
            for (DependencyObject? up = content; up is not null; up = VisualTreeHelper.GetParent(up))
                if (up is FrameworkElement fe && !ReferenceEquals(fe, content) && FindVisuals<DependencyObject>(fe).Any(d => d.GetType().Name == "DockTabSurface")) return fe;
            return content;
        }

        private FrameworkElement FindElement(string name)
        {
            if (_w.Window.FindName(name) is FrameworkElement named) return named;
            foreach (var root in new DependencyObject[] { _w.Window }.Concat(_tools.Values))
                foreach (var el in FindVisuals<FrameworkElement>(root))
                    if (el.Name == name || System.Windows.Automation.AutomationProperties.GetAutomationId(el) == name) return el;
            throw new InvalidOperationException($"no element named or with AutomationId '{name}'");
        }

        /// <summary>Photographs a visual at 2x with the theme's window colour behind it (some panels and popups are transparent).</summary>
        private BitmapSource Render(FrameworkElement element, double width, double height, JsonElement? region = null)
        {
            var rect = new Rect(0, 0, width, height);
            if (region is { ValueKind: JsonValueKind.Array } r)
                rect = Rect.Intersect(rect, new Rect(r[0].GetDouble(), r[1].GetDouble(), r[2].GetDouble(), r[3].GetDouble()));
            if (rect.IsEmpty || rect.Width < 2 || rect.Height < 2) throw new InvalidOperationException("the element has no size (not laid out or collapsed)");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle((Brush)_w.FindResource("WindowBrush"), null, new Rect(0, 0, rect.Width, rect.Height));
                dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = rect, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, rect.Width, rect.Height) },
                    null, new Rect(0, 0, rect.Width, rect.Height));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(rect.Width * CaptureScale), (int)Math.Ceiling(rect.Height * CaptureScale), 96 * CaptureScale, 96 * CaptureScale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static string TextOf(DependencyObject root) =>
            FindVisuals<TextBlock>(root).Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "page";
    }
}

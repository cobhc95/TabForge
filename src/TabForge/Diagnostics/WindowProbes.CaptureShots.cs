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

// Window probes, scripted off-screen capture: tool windows, menus and the shots (see WindowProbes.Capture.cs).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
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
                    case "grouprules": Adopt(new GroupRulesDialog(w.Window, w._project.Mixer)); break;
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
                    case "chordfinder": ChordFinderWindow.Show(w.Window, _ => false); break;
                    case "songstats": SongStatsWindow.Show(w.Window, w._project, "demo.tforge"); break;
                    case "newfromtemplate": w.ApplyTemplate_Click(w, new RoutedEventArgs()); break;
                    case "commandpalette": Adopt(new Views.CommandPalette(w.Window, w._settings.Hotkeys) { KeepOpen = true }); break;
                    case "addplugin": PluginBrowser.Choose(w.Window, w.Window); break;
                    case "pasteoptions": Adopt(new PasteOptionsDialog(new[] { PasteQuestion.BeatsOntoNotes, PasteQuestion.Octave })); break;
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
        /// <summary>Draws a media drag (drop, dropnew, dropbelow) or a clip move (move) over the main timeline, as the timeline render does.</summary>
        private void TimelineDrag(string mode)
        {
            DiagnosticCommands.AddDropRenderClip(_w._project);
            _w.RefreshArrangement();
            _w.Window.UpdateLayout();
            if (mode == "move") DiagnosticCommands.SimulateMoveForRender(_w.Arrangement, _w._project);
            else DiagnosticCommands.SimulateDropForRender(_w.Arrangement, _w._project, mode);
        }

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
                    case "mixer-colour": caught = Views.MixerWindow.ColourMenu(_w._project.Tracks[Math.Clamp(_track, 0, _w._project.Tracks.Count - 1)].ColorHex, _ => { }); break;
                    default: throw new InvalidOperationException($"unknown context menu '{kind}' (score, note, timeline, fretboard, timeline-empty, track-row, mixer-colour)");
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
                "sections" => _w.SectionsPanelContent,
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

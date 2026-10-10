using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the `--screenshots` tour: photographs the main window, every menu, the side panels, each Settings page with a search, and the main dialogs as PNG files.
// Does not own: the windows and menus it photographs; dialogs are captured through DialogHost.
// Tests: no named test.

// Window probes, screenshot tour: `TabForge.exe <song> --screenshots <folder>` photographs the main window,
// every menu, the side panels, each Settings page (plus a search) and the main dialogs as PNGs for the
// README (docs/screenshots). Nothing is clicked for real: dialogs are captured through DialogHost.
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
    private string? _screenshotFolder;

    /// <summary>Runs the tour after the window and song are fully shown, then closes the app.</summary>
    public void RunScreenshotTour(string folder)
    {
        _screenshotFolder = FilePathPolicy.OutputDirectory(folder, "screenshot folder");
        Directory.CreateDirectory(_screenshotFolder);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try { await ScreenshotTourAsync(); }
            catch (Exception ex) { StatusText.Text = $"Screenshot tour failed: {ex.GetBaseException().Message}"; } // Not logged: diagnostic probe: the failure goes to its report, not errors.log
            finally
            {
                DialogHost.Capture = null;
                _confirmOnClose = false;
                Application.Current.Shutdown(0);
            }
        }));
    }

    private async Task ScreenshotTourAsync()
    {
        await Settle(1500);
        Shoot(Window, "main-window");
        Shoot(InstrumentHost, "fretboard");
        SetInstrumentView(TabForge.Services.InstrumentViews.Keyboard);
        await Settle(400);
        Shoot(InstrumentHost, "instrument-keyboard");
        SetInstrumentView(TabForge.Services.InstrumentViews.Drums);
        await Settle(400);
        Shoot(InstrumentHost, "instrument-drums");
        SetInstrumentView(null);
        await Settle(300);
        SetHorizontalScoreView(true);
        await Settle(800);
        Shoot(Window, "score-horizontal");
        SetHorizontalScoreView(false);
        await Settle(800);
        Shoot(Arrangement, "arrangement-timeline");
        // A non-persistent recording/take scene: real clip-lane controls and MIDI note rendering,
        // without recording from the user's hardware or writing into their song.
        if (_project.Tracks.Count > 0)
        {
            var track = _project.Tracks[Math.Min(3, _project.Tracks.Count - 1)];
            var originalArm = track.RecordArm;
            var originalInput = track.AudioInput;
            var originalLaneCount = track.Lanes.Count;
            var originalLaneStates = track.Lanes.Select(l => l.Plays).ToArray();
            var start = SongClock.BarStartSec(_project, 4);
            var examples = Enumerable.Range(0, 2).Select(i => new AudioClip
            {
                Name = $"MIDI take {i + 1}", StartSec = start, SourceLengthSec = 6,
                FileLengthSec = 6, Lane = i,
                Notes = Enumerable.Range(0, 12).Select(n => new ClipNote(n * 0.4, 0.3, 48 + (n + i) % 8, 90)).ToList()
            }).ToList();
            try
            {
                track.RecordArm = true;
                track.AudioInput = AudioInputs.Midi;
                track.AudioClips.AddRange(examples);
                ClipLanes.PlayNewTake(track, 1, midi: true);
                RefreshTracks();
                RefreshArrangement();
                await Settle(600);
                Shoot(Window, "recording-layout");
                Shoot(Arrangement, "recording-lanes");
            }
            finally
            {
                foreach (var clip in examples) track.AudioClips.Remove(clip);
                track.RecordArm = originalArm;
                track.AudioInput = originalInput;
                while (track.Lanes.Count > originalLaneCount) track.Lanes.RemoveAt(track.Lanes.Count - 1);
                for (var i = 0; i < originalLaneStates.Length; i++) track.Lanes[i].Plays = originalLaneStates[i];
                RefreshTracks();
                RefreshArrangement();
            }
        }
        Shoot(ToolsPanelContent, "tool-palette");
        Shoot(SectionsPanelContent, "sections-panel");

        // Every top-level menu, opened.
        var menu = FindVisuals<Menu>(Window).FirstOrDefault(m => m.Items.Count > 3);
        if (menu is not null)
            foreach (var item in menu.Items.OfType<MenuItem>().ToList())
            {
                item.IsSubmenuOpen = true;
                await Settle(250);
                if (item.Template.FindName("PART_Popup", item) is Popup { Child: FrameworkElement popup })
                    Shoot(popup, $"menu-{Slug(item.Header?.ToString())}");
                item.IsSubmenuOpen = false;
                await Settle(100);
            }

        // Dialogs: built by the real code paths, photographed instead of shown.
        DialogHost.Capture = CaptureDialog;
        foreach (var kind in new[] { TrackKind.Guitar, TrackKind.Bass, TrackKind.Drums })
        {
            var index = _project.Tracks.FindIndex(t => t.Kind == kind);
            if (index < 0) continue;
            TrackMixerGrid.SelectedIndex = index;
            _dialogName = $"track-properties-{kind.ToString().ToLowerInvariant()}";
            TrackProps_Click(Window, new RoutedEventArgs());
        }
        _dialogName = "instrument-picker";
        InstrumentPickerWindow.Show(Window, SelectedTrack?.InstrumentName, Colors.SteelBlue);
        _dialogName = "global-tuning";
        ShowGlobalTuningWindow();
        _dialogName = "project-settings";
        ProjectSettings_Click(Window, new RoutedEventArgs());
        _dialogName = "score-text-style";
        ShowScoreTextStyleWindow();
        _dialogName = "mix-table";
        ShowMixTable();
        _dialogName = "update-available";
        UpdateAvailableWindow.Show(Window, new ReleaseInfo("0.1.0-beta.2", UpdateService.PageFor("0.1.0-beta.2")), AppInfo.Version, true);
        _dialogName = "add-track";
        AddTrackWithWindow();
        _dialogName = "scale-finder";
        OpenScaleFinder();
        _dialogName = "plugin-save";
        PluginSaveDialog.Ask(Window, "song.gp");
        // Mixer and a track's FX chain (modeless windows: shown off-screen, photographed, closed).
        CaptureDialog(new MixerWindow(Window.MixerHost, Window) { Width = 1180, Height = 560 }, "mixer");
        CaptureDialog(new RenderWindow(new RenderContext { Project = _project, Settings = _settings, Engine = Audio.AudioEngineClient.Instance }, Window), "render");
        if (SelectedTrack is { } fxTrack)
        {
            var instrument = new TabForge.Plugins.PluginSlot { Name = "Example instrument", Path = @"C:\Example\instrument.dll", Format = "VST2", Type = TabForge.Plugins.PluginSlotType.Instrument };
            var effect = new TabForge.Plugins.PluginSlot { Name = "Example effect", Path = @"C:\Example\effect.dll", Format = "VST2", Wet = 80 };
            fxTrack.Rig.Plugins.Add(instrument);
            fxTrack.Rig.Plugins.Add(effect);
            try
            {
                CaptureDialog(new FxChainWindow(Window.MixerHost, fxTrack, Window), "fx-chain");
                CaptureDialog(new WiringWindow(Window.MixerHost, fxTrack, instrument, Window, change => change()), "plugin-wiring");
                CaptureDialog(new MidiProcessingWindow(Window.MixerHost, fxTrack, instrument, Window, change => change()), "midi-processing");
                _dialogName = "add-plugin";
                PluginBrowser.Choose(Window, Window.MixerHost);
            }
            finally
            {
                fxTrack.Rig.Plugins.Remove(effect);
                fxTrack.Rig.Plugins.Remove(instrument);
            }
        }
        _dialogName = "new-from-template";
        ApplyTemplate_Click(Window, new RoutedEventArgs());
        _dialogName = "command-palette";
        try { CaptureDialog(new Views.CommandPalette(Window, _settings.Hotkeys) { KeepOpen = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"palette shot failed: {ex}"); } // Not logged: diagnostic probe: the failure goes to its report, not errors.log
        DialogHost.Capture = null;
        try { await ShootNewerFeaturesAsync(); }
        catch (Exception ex) { System.IO.File.WriteAllText(Path.Combine(_screenshotFolder!, "tour-error.txt"), ex.ToString()); } // Not logged: diagnostic probe: the failure goes to its report, not errors.log
        DialogHost.Capture = CaptureDialog;
        _dialogName = "settings";
        Views.PreferencesWindow.InitialCategory = null;
        Prefs_Click(Window, new RoutedEventArgs());
        DialogHost.Capture = null;
        StatusText.Text = $"Screenshots written to {_screenshotFolder}";
    }

    // Newer features: tuner, mixer groups, zoom & speed, fretboard and track-list menus.
    private async Task ShootNewerFeaturesAsync()
    {
        TunerWindow.Open(Window, Audio.AudioEngineClient.Instance, () => SelectedTrack);
        await Settle(500);
        foreach (var tuner in Application.Current.Windows.OfType<TunerWindow>().ToList())
        {
            Shoot(tuner, "tuner");
            tuner.Close();
        }
        CaptureDialog(new MixerWindow(Window.MixerHost, Window) { Width = 1180, Height = 560 }, "mixer-groups");
        var showedGroups = _project.Mixer.ShowGroupsInTrackList;
        try
        {
            Window.MixerHost.SetTrackListShows("groups", true);
            await Settle(400);
            Shoot(Arrangement, "track-list-groups");
        }
        finally { Window.MixerHost.SetTrackListShows("groups", showedGroups); }
        // Fretboard context menu (lean: view, scale, practice toggles, lock, settings), then its Scale submenu.
        Instrument_MouseRightButtonUp(Instrument, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            { RoutedEvent = UIElement.MouseRightButtonUpEvent });
        await Settle(250);
        if (Instrument.ContextMenu is { } fretMenu)
        {
            Shoot(fretMenu, "fretboard-menu");
            var scaleMenu = fretMenu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header?.ToString() == InstrumentMenus.Scale);
            if (scaleMenu is not null)
            {
                scaleMenu.IsSubmenuOpen = true;
                await Settle(250);
                if (scaleMenu.Template.FindName("PART_Popup", scaleMenu) is Popup { Child: FrameworkElement sp })
                    Shoot(sp, "fretboard-menu-scale");
                scaleMenu.IsSubmenuOpen = false;
            }
            fretMenu.IsOpen = false;
        }
        // Track-list empty-area menu ("Show tracks in groups").
        var emptyMenu = Arrangement.BuildEmptyAreaMenu();
        emptyMenu.PlacementTarget = Arrangement;
        emptyMenu.IsOpen = true;
        await Settle(250);
        Shoot(emptyMenu, "track-list-empty-area-menu");
        emptyMenu.IsOpen = false;
    }

    private string _dialogName = "dialog";

    private void CaptureDialog(Window window, string name)
    {
        _dialogName = name;
        CaptureDialog(window);
    }

    private bool? CaptureDialog(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = SystemParameters.VirtualScreenLeft - dialog.Width - 200; // off-screen: never flashes
        dialog.Top = 0;
        dialog.ShowActivated = false;
        dialog.Show();
        Pump();
        if (dialog is Views.CommandPalette) { FindVisual<TextBox>(dialog)?.SetCurrentValue(TextBox.TextProperty, "tun"); Pump(); }
        if (dialog is PreferencesWindow preferences) ShootSettingsPages(preferences);
        else Shoot(dialog, _dialogName);
        try { dialog.Close(); } catch (InvalidOperationException) { } // Not logged: a dialog that is already closed is expected here.
        return false;
    }

    // Each Settings category, then a live search to show searchability.
    private void ShootSettingsPages(PreferencesWindow preferences)
    {
        var buttons = FindVisuals<ButtonBase>(preferences.NavigationPanel).ToList();
        foreach (var button in buttons)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Shoot(preferences, $"settings-{Slug(TextOf(button))}");
        }
        preferences.SearchBox.Text = "tempo";
        Pump();
        Shoot(preferences, "settings-search");
        preferences.SearchBox.Text = "";
        foreach (var (page, row, name) in new[] { ("General", "Autosave unsaved songs", "settings-autosave-row"),
                     (SettingsCatalog.AudioVst, "Windows MIDI latency", "settings-midi-latency-measure"),
                     (SettingsCatalog.AudioVst, "Play the whole song through the audio engine", "settings-audio-engine-toggle") })
        {
            var nav = buttons.FirstOrDefault(b => TextOf(b) == page);
            if (nav is null) continue;
            nav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            var label = FindVisuals<TextBlock>(preferences).FirstOrDefault(t => t.Text.StartsWith(row, StringComparison.Ordinal));
            label?.BringIntoView();
            Pump();
            if (row.StartsWith("Windows MIDI", StringComparison.Ordinal))   // the number box and Measure button sit below the label
                foreach (var sv in FindVisuals<ScrollViewer>(preferences).Where(v => v.ScrollableHeight > 0))
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + 220);
            Pump();
            Shoot(preferences, name);
        }
    }

    private void Shoot(FrameworkElement element, string name)
    {
        if (_screenshotFolder is null || element.ActualWidth < 2 || element.ActualHeight < 2) return;
        const double scale = 1.5; // crisp on high-DPI README views
        var width = (int)Math.Ceiling(element.ActualWidth * scale);
        var height = (int)Math.Ceiling(element.ActualHeight * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Popups and panels can be transparent: paint the window background behind them.
            var background = (Brush)FindResource("WindowBrush");
            dc.DrawRectangle(background, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = FilePathPolicy.OutputFile(Path.Combine(_screenshotFolder, $"{name}.png"), "screenshot", ".png");
        FilePathPolicy.WriteAtomically(path, encoder.Save);
    }

    private async Task Settle(int ms)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
    }

    // Synchronous layout + render pass for dialogs captured inside a modal call.
    private void Pump()
    {
        for (var i = 0; i < 3; i++) Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static string Slug(string? text)
    {
        var clean = new string((text ?? "").Replace("_", "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (clean.Contains("--", StringComparison.Ordinal)) clean = clean.Replace("--", "-", StringComparison.Ordinal);
        return clean.Trim('-') is { Length: > 0 } slug ? slug : "item";
    }

    private static string TextOf(DependencyObject root) =>
        FindVisuals<TextBlock>(root).Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "page";
}

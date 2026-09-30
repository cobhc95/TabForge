using TabForge.Controllers;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

public static partial class SelfTest
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private static void PumpUi() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));

    /// <summary>
    /// Zooming out during playback must keep follow armed and put the playhead's system back in view at
    /// the new zoom (regression: the zoom's own scroll was treated as the user scrolling away).
    /// </summary>
    private static void TestFollowSurvivesZoom()
    {
        var editor = new Views.TabEditorControl();
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(48) };
        project.Tracks.Add(track);
        editor.Project = project;
        editor.SelectedTrackIndex = 0;
        var scroll = new System.Windows.Controls.ScrollViewer
        {
            Width = 900, Height = 320, Content = editor,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        };
        void Layout()
        {
            scroll.Measure(new System.Windows.Size(900, 320));
            scroll.Arrange(new System.Windows.Rect(0, 0, 900, 320));
            scroll.UpdateLayout();
        }
        Layout();
        const int bar = 40;
        var follow = new Views.ScoreFollowCoordinator(scroll, editor, () => true, () => false, () => bar, () => 0.0,
            () => 48, () => new Services.FollowSettings { Mode = Services.FollowModes.Jump });
        follow.ApplySettings(new Services.FollowSettings { Mode = Services.FollowModes.Jump });
        scroll.ScrollChanged += (_, e) => follow.OnScrollChanged(e); // as MainWindow wires it
        follow.ResetForPlayback();
        follow.OnPlayheadBar(bar);
        Layout();
        foreach (var zoom in new[] { 0.5, 2.0, 0.75 })
        {
            follow.OnScoreLayoutChanging();
            editor.Zoom = zoom;
            editor.InvalidateScoreLayout();
            editor.InvalidateMeasure();
            Layout();
            scroll.ScrollToVerticalOffset(0); // the zoom's own scroll rewrite, as ApplyPageWidth does
            Layout();
            follow.ReanchorAfterZoom();
            Layout();
            var top = editor.SystemTopForMeasure(bar);
            var offset = scroll.VerticalOffset;
            Check($"follow stays on after zoom {zoom:0.##}x", follow.IsFollowing);
            Check($"scroll target contains the playhead system after zoom {zoom:0.##}x",
                top >= offset - 0.5 && top < offset + scroll.ViewportHeight,
                $"systemTop={top:0} offset={offset:0} viewport={scroll.ViewportHeight:0}");
        }
        // A layout/panel pass that moves the offset after every timing window has expired (the fragile case:
        // follow used to infer "user" from ScrollChanged + timing) must not stop follow without a gesture.
        System.Threading.Thread.Sleep(700);
        scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - 150));
        Layout();
        Check("follow stays on after a late layout scroll with no user gesture", follow.IsFollowing);
        // A genuine gesture (wheel without Ctrl / scrollbar / scroll key) still stops it.
        System.Threading.Thread.Sleep(200);
        follow.NoteUserScrollGesture();
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 120);
        Layout();
        Check("a user scroll gesture still stops follow", !follow.IsFollowing);
    }

    /// <summary>
    /// Windows 11 Snap Layouts need WM_NCHITTEST at the maximise button to answer HTMAXBUTTON (9). A real shown borderless window
    /// built like the main window (same WindowChrome, same hooks) is asked at every caption button, normal and maximised.
    /// </summary>
    private static void TestSnapLayoutHitTest()
    {
        var min = new System.Windows.Controls.Button { Width = 46, Height = 44 };
        var max = new System.Windows.Controls.Button { Width = 46, Height = 44 };
        var close = new System.Windows.Controls.Button { Width = 46, Height = 44 };
        // The real caption styles (hover / pressed triggers) when the application resources are loaded.
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource("ChromeCaptionButton") is System.Windows.Style captionStyle &&
            app.TryFindResource("ChromeCloseButton") is System.Windows.Style closeStyle)
        {
            min.Style = captionStyle; max.Style = captionStyle; close.Style = closeStyle;
        }
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Height = 44 };
        foreach (var b in new[] { min, max, close })
        {
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            buttons.Children.Add(b);
        }
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(buttons, true);
        var title = new System.Windows.Controls.Border { Height = 44, Child = new System.Windows.Controls.DockPanel() };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
        ((System.Windows.Controls.DockPanel)title.Child).Children.Add(buttons);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(title, true);
        var root = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(title, System.Windows.Controls.Dock.Top);
        root.Children.Add(title);
        var window = new System.Windows.Window
        {
            WindowStyle = System.Windows.WindowStyle.None, ResizeMode = System.Windows.ResizeMode.CanResize, ShowInTaskbar = false,
            Left = 80, Top = 80, Width = 900, Height = 500, Content = root
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new System.Windows.Thickness(6), CornerRadius = new System.Windows.CornerRadius(0),
            GlassFrameThickness = new System.Windows.Thickness(0), UseAeroCaptionButtons = false
        });
        Shell.WpfCaptionButtonFrame? frame = null;
        window.SourceInitialized += (_, _) =>
        {
            _ = new Shell.WpfResizeBorderFrame(window);
            frame = new Shell.WpfCaptionButtonFrame(window, min, max, close);
        };
        try
        {
            ShowTestWindow(window);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Check("snap layouts: the window has the maximise-box style", (GetWindowLong(hwnd, -16) & 0x00010000) != 0);

            static int HitAt(IntPtr hwnd, System.Windows.FrameworkElement element)
            {
                var rect = Shell.ScreenPoints.ScreenRect(element);
                var x = (int)Math.Round(rect.X + rect.Width / 2);
                var y = (int)Math.Round(rect.Y + rect.Height / 2);
                return (int)(SendMessage(hwnd, 0x0084, IntPtr.Zero, (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF))).ToInt64() & 0xFFFF);
            }

            Check("snap layouts (normal window): WM_NCHITTEST at the buttons answers HTMINBUTTON / HTMAXBUTTON / HTCLOSE",
                HitAt(hwnd, min) == 8 && HitAt(hwnd, max) == 9 && HitAt(hwnd, close) == 20,
                $"min={HitAt(hwnd, min)} max={HitAt(hwnd, max)} close={HitAt(hwnd, close)}");
            // Hover: the native non-client mouse messages drive the button's hot state and background.
            static System.Windows.Media.Color? BackgroundOf(System.Windows.Controls.Button b)
            {
                b.ApplyTemplate();
                return (b.Template?.FindName("Bd", b) as System.Windows.Controls.Border)?.Background is System.Windows.Media.SolidColorBrush brush ? brush.Color : null;
            }
            var before = BackgroundOf(close);
            SendMessage(hwnd, 0x00A0, (IntPtr)20, IntPtr.Zero); // WM_NCMOUSEMOVE over HTCLOSE (checked at once: the cursor is not really there, so the poll would clear it)
            var hotColour = BackgroundOf(close);
            Check("caption hover: WM_NCMOUSEMOVE over HTCLOSE marks only the close button hot",
                Shell.CaptionButtonState.GetIsHot(close) && !Shell.CaptionButtonState.GetIsHot(max) && !Shell.CaptionButtonState.GetIsHot(min));
            if (before is not null)
                Check("caption hover: the close button turns red (its template background changes)",
                    hotColour is { } c && c != before && c.R > 150 && c.G < 90, $"before={before} hot={hotColour}");
            SendMessage(hwnd, 0x00A0, (IntPtr)9, IntPtr.Zero); // moving on to HTMAXBUTTON
            Check("caption hover: hot follows the pointer to the maximise button",
                Shell.CaptionButtonState.GetIsHot(max) && !Shell.CaptionButtonState.GetIsHot(close));
            SendMessage(hwnd, 0x02A2, IntPtr.Zero, IntPtr.Zero); // WM_NCMOUSELEAVE
            Check("caption hover: WM_NCMOUSELEAVE clears the hot state",
                !Shell.CaptionButtonState.GetIsHot(max) && !Shell.CaptionButtonState.GetIsHot(close) && !Shell.CaptionButtonState.GetIsHot(min));

            Shell.WindowPolish.Apply(window); // what the app-wide Loaded handler does to every window (caps Max size to the work area)
            window.WindowState = System.Windows.WindowState.Maximized;
            PumpUi(); window.UpdateLayout(); PumpUi();
            Check("snap layouts (maximised window): the maximise button still answers HTMAXBUTTON",
                HitAt(hwnd, max) == 9 && HitAt(hwnd, close) == 20 && HitAt(hwnd, min) == 8,
                $"min={HitAt(hwnd, min)} max={HitAt(hwnd, max)} close={HitAt(hwnd, close)}");
            CheckMaximisedFillsWorkArea(window, hwnd, "maximised");
            window.WindowState = System.Windows.WindowState.Normal;
            PumpUi(); window.UpdateLayout(); PumpUi();
            window.WindowState = System.Windows.WindowState.Maximized;
            PumpUi(); window.UpdateLayout(); PumpUi();
            CheckMaximisedFillsWorkArea(window, hwnd, "restored then re-maximised");
        }
        finally
        {
            frame?.Dispose();
            window.Close();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProbeRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProbeMonitorInfo { public int Size; public ProbeRect Monitor, Work; public uint Flags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out ProbeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref ProbeMonitorInfo info);

    /// <summary>A maximised window built like the main window must cover exactly its monitor's work area (+-1 px), and its content must fill it.</summary>
    private static void CheckMaximisedFillsWorkArea(System.Windows.Window window, IntPtr hwnd, string state)
    {
        GetWindowRect(hwnd, out var r);
        var info = new ProbeMonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<ProbeMonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info);
        var w = info.Work;
        Check($"maximised fill ({state}): window rectangle equals the monitor work area",
            Math.Abs(r.Left - w.Left) <= 1 && Math.Abs(r.Top - w.Top) <= 1 && Math.Abs(r.Right - w.Right) <= 1 && Math.Abs(r.Bottom - w.Bottom) <= 1,
            $"window=({r.Left},{r.Top},{r.Right},{r.Bottom}) work=({w.Left},{w.Top},{w.Right},{w.Bottom})");
        if (window.Content is System.Windows.FrameworkElement root)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            var pw = root.ActualWidth * dpi.DpiScaleX; var ph = root.ActualHeight * dpi.DpiScaleY;
            Check($"maximised fill ({state}): the content fills the work area",
                Math.Abs(pw - (w.Right - w.Left)) <= 2 && Math.Abs(ph - (w.Bottom - w.Top)) <= 2,
                $"content={pw:0}x{ph:0} work={w.Right - w.Left}x{w.Bottom - w.Top}");
        }
    }

    /// <summary>Automation peers: the score editor reports its cursor as a Value, track rows are list items, the fretboard is named.</summary>
    private static void TestAutomationPeers()
    {
        var project = Presets.TemplateFactory.Create("Blank");
        project.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 2, Fret = 7 } } };
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        editor.SetPosition(0, 0, 2, false);
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(editor);
        var value = (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Value) as System.Windows.Automation.Provider.IValueProvider)?.Value ?? "";
        Check("score editor peer: the Value names bar, beat, string, fret and duration",
            value.Contains("bar 1") && value.Contains("beat 1") && value.Contains("string 3") && value.Contains("fret 7") && value.Contains("quarter note"), value);
        var row = new Views.TrackRowBorder();
        System.Windows.Automation.AutomationProperties.SetName(row, "Track 2: Lead, muted");
        var rowPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(row);
        Check("arrangement track row peer: a list item named with its mute state",
            rowPeer?.GetAutomationControlType() == System.Windows.Automation.Peers.AutomationControlType.ListItem &&
            rowPeer.GetName() == "Track 2: Lead, muted" && rowPeer.GetItemStatus() == "muted");
        var fret = new Views.InstrumentPanel();
        Check("fretboard peer: named and reports its shown notes",
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(fret)?.GetName() == "Fretboard" &&
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(fret)?.GetItemStatus().StartsWith("Fretboard") == true);
    }

    /// <summary>A saved template keeps the setup (tracks, tunings, mixer, tempo, signatures) and nothing of the song.</summary>
    private static void TestTemplateKeepsSetupOnly()
    {
        var song = Presets.TemplateFactory.Create("Rock Band");
        song.Tempo = 97; song.TimeSignatureNumerator = 3; song.TimeSignatureDenominator = 4; song.KeySignature = 2;
        song.Lyrics = "la la"; song.Markers.Add(new MarkerModel { Title = "Verse" });
        song.Tracks[0].Capo = 3; song.Tracks[0].Volume = 77; song.Tracks[0].Name = "Rhythm";
        song.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 1, Fret = 5 } } };
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-tpl-" + Guid.NewGuid().ToString("N"));
        var previous = Services.UserTemplates.FolderOverride;
        Services.UserTemplates.FolderOverride = folder;
        try
        {
            Services.UserTemplates.Save("Setup only", song);
            var loaded = Services.UserTemplates.Create("Setup only");
            var t = loaded.Tracks[0];
            Check("template keeps tracks, names, capo, volume, tempo and signatures",
                loaded.Tracks.Count == song.Tracks.Count && t.Name == "Rhythm" && t.Capo == 3 && t.Volume == 77 &&
                loaded.Tempo == 97 && loaded.TimeSignatureNumerator == 3 && loaded.KeySignature == 2);
            Check("template drops notes, lyrics and markers and has the default number of empty bars",
                loaded.Lyrics.Length == 0 && loaded.Markers.Count == 0 &&
                loaded.Tracks.All(x => x.Measures.Count == Services.UserTemplates.DefaultBars && x.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0))));
            // An older template file that still holds the whole song is stripped when it is opened.
            Services.ProjectService.Save(System.IO.Path.Combine(folder, "Old.tforge"), song);
            var old = Services.UserTemplates.Create("Old");
            Check("an old template with notes loads as setup only",
                old.Tracks[0].Measures.Count == Services.UserTemplates.DefaultBars && old.Tracks.All(x => x.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0))));
        }
        finally
        {
            Services.UserTemplates.FolderOverride = previous;
            try { System.IO.Directory.Delete(folder, true); } catch { }
        }
    }

    /// <summary>The layout audit finds no colliding text, marking or glyph on the technique test song (every track).</summary>
    private static void TestLayoutAuditTechniqueSong()
    {
        var song = Diagnostics.GmSongAudit.TechniqueSong();
        var found = new List<string>();
        for (var i = 0; i < song.Tracks.Count; i++)
            found.AddRange(Diagnostics.LayoutAudit.Run(song, i).Select(c => $"track {i + 1} {c}"));
        Check("layout audit: no text or marking collides on the technique song", found.Count == 0,
            string.Join(" | ", found.Take(5)));
    }

    /// <summary>Audit 3 section 6a engraving helpers (bends, whammy, tremolo, harmonics, trill, swing) and a render smoke test.</summary>
    private static void TestTechniqueEngraving()
    {
        Check("bend amounts use the conventional text (1/4, 1/2, 3/4, full, 1 1/2, 2)",
            Views.TabEditorControl.BendAmountLabel(1) == "1/4" && Views.TabEditorControl.BendAmountLabel(2) == "1/2" &&
            Views.TabEditorControl.BendAmountLabel(3) == "3/4" && Views.TabEditorControl.BendAmountLabel(4) == "full" &&
            Views.TabEditorControl.BendAmountLabel(6) == "1 1/2" && Views.TabEditorControl.BendAmountLabel(8) == "2");
        Check("whammy amounts are signed (-1/2, -1, -1 1/2)",
            Views.TabEditorControl.WhammyAmountLabel(-2) == "-1/2" && Views.TabEditorControl.WhammyAmountLabel(-4) == "-1" &&
            Views.TabEditorControl.WhammyAmountLabel(-6) == "-1 1/2");
        var bendNote = new TabNote { Techniques = { "Bend" }, BendTypeName = "Prebend" };
        var bendPoints = Views.TabEditorControl.EffectiveBendPoints(bendNote);
        Check("a pre-bend without points starts already bent; a plain bend starts at zero",
            bendPoints[0].Value > 0 && Views.TabEditorControl.EffectiveBendPoints(new TabNote { Techniques = { "Bend" } })[0].Value == 0);
        Check("tremolo slash count follows the picking speed",
            Views.TabEditorControl.TremoloSlashCount(new TabCell { TremoloPickDenominator = 8 }) == 1 &&
            Views.TabEditorControl.TremoloSlashCount(new TabCell { TremoloPickDenominator = 16 }) == 2 &&
            Views.TabEditorControl.TremoloSlashCount(new TabCell { TremoloPickDenominator = 32 }) == 3 &&
            Views.TabEditorControl.TremoloSlashCount(new TabCell()) == 0);
        var busy = new TabNote { Techniques = { "Bend", "Ghost", "Dead", "LetRing", "PickDown", "Harmonic", "TapHarmonic", "Slap" } };
        var label = Views.TabEditorControl.DrawnTechniqueLabel(new[] { busy });
        Check("TAB text labels drop what is drawn as geometry and never double a harmonic prefix",
            !label.Contains("b") && !label.Contains("G") && !label.Contains("X") && !label.Contains("let ring") &&
            label.Contains("T.H.") && !label.Contains("H T.H.") && !label.Contains("Harm.") && label.Contains("S"));
        Check("a natural harmonic is captioned Harm.", Views.TabEditorControl.HarmonicCaption(new HashSet<string> { "Harmonic" }) == "Harm.");
        Check("harmonic values use the conventional text: A.H. pitch name, T.H. tapped fret, nothing for natural",
            Views.TabEditorControl.HarmonicFretText(new TabNote { HarmonicFret = 12 }) == "" &&
            Views.TabEditorControl.HarmonicFretText(new TabNote { MidiValue = 64, Techniques = { "ArtificialHarmonic" } }) == "E" &&
            Views.TabEditorControl.HarmonicFretText(new TabNote { HarmonicFret = 5.8, Techniques = { "TapHarmonic" } }) == "5.8" &&
            Views.TabEditorControl.HarmonicFretText(new TabNote { HarmonicFret = 17, Techniques = { "TapHarmonic" } }) == "17" &&
            Views.TabEditorControl.HarmonicFretText(new TabNote()) == "");
        Check("swing symbols differ for eighths and sixteenths",
            Views.TabEditorControl.SwingSymbol(TripletFeels.Eighth) != Views.TabEditorControl.SwingSymbol(TripletFeels.Sixteenth));

        var project = Presets.TemplateFactory.Create("Blank");
        var cells = project.Tracks[0].Measures[0].Cells;
        cells[0] = new TabCell { DurationDenominator = 8, TremoloPickDenominator = 16, Notes = { new TabNote { StringIndex = 1, Fret = 7, Techniques = { "Bend", "TremoloPick" }, BendTypeName = "BendRelease" } } };
        cells[1] = new TabCell { DurationDenominator = 8, WhammyPoints = { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 30, Value = -4 }, new BendPointModel { Offset = 60, Value = 0 } },
            Notes = { new TabNote { StringIndex = 2, Fret = 5, IsGraceNote = true }, new TabNote { StringIndex = 2, Fret = 7, Techniques = { "TremBar", "Trill", "LetRing" }, LeftHandFinger = 1, RightHandFinger = 2 } } };
        cells[2] = new TabCell { DurationDenominator = 4, Accent = 2, Notes = { new TabNote { StringIndex = 3, Fret = 9, Ghost = true, Techniques = { "WahOpen", "ArpeggioDown", "PickUp", "LegatoSlide" } } } };
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, DarkPaper = false };
        try
        {
            editor.Measure(new System.Windows.Size(1000, 4000));
            editor.Arrange(new System.Windows.Rect(editor.DesiredSize));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(600, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(editor);
            Check("a bar full of bends, whammy, grace, trill, tremolo and fingering engraves without error", bmp.PixelWidth == 600);
        }
        catch (Exception ex) { Check("a bar full of technique marks engraves without error", false, ex.Message); }
    }

    /// <summary>Command palette scoring/catalogue, PDF pagination and file structure, editor cursor description.</summary>
    private static void TestCommandPaletteAndPdf()
    {
        Check("palette: subsequence matches, non-matches score 0",
            Views.CommandPalette.Score("sav", "File and tabs: Save") > 0 && Views.CommandPalette.Score("zzq", "File and tabs: Save") == 0);
        Check("palette: exact word beats scattered letters",
            Views.CommandPalette.Score("save", "File and tabs: Save as") > Views.CommandPalette.Score("save", "Sound: Volume reset"));
        Check("palette + PDF commands are in the catalogue and bound",
            Services.HotkeyCatalog.ById("App.CommandPalette")?.DefaultGesture == "Ctrl+Shift+A" &&
            Services.HotkeyCatalog.ById("File.ExportPdf") is not null &&
            Services.HotkeyCatalog.All.Count(a => a.DefaultGesture == "Ctrl+Shift+A") == 1);
        var tipped = new System.Windows.Controls.Button { ToolTip = "Zoom out (Ctrl+-)" };
        var texted = new System.Windows.Controls.Button { Content = "Refresh devices" };
        Views.AccessibleNames.Apply(tipped); Views.AccessibleNames.Apply(texted);
        var named = new System.Windows.Controls.Button { Name = "PlayButton", ToolTip = "Play or pause (Space)" };
        Views.AccessibleNames.Apply(named);
        Check("controls get an Area.Control automation id from their x:Name",
            System.Windows.Automation.AutomationProperties.GetAutomationId(named) == "Transport.Play" &&
            System.Windows.Automation.AutomationProperties.GetAutomationId(tipped).EndsWith(".ZoomOut") &&
            System.Windows.Automation.AutomationProperties.GetAutomationId(texted).EndsWith(".RefreshDevices"),
            System.Windows.Automation.AutomationProperties.GetAutomationId(named) + " / " + System.Windows.Automation.AutomationProperties.GetAutomationId(tipped));
        Check("controls without a name get one from their tooltip or text",
            System.Windows.Automation.AutomationProperties.GetName(tipped) == "Zoom out (Ctrl+-)" &&
            System.Windows.Automation.AutomationProperties.GetName(texted) == "Refresh devices");
        var pages = Views.ScorePdfExporter.Paginate(900, 100, 200, 10);
        Check("PDF pagination covers every system with header on page 1",
            pages.Count >= 2 && pages[0].Top == 0 && pages[0].Height >= 100 && pages.All(pg => pg.Height <= 842 / (595.0 - 56) * 900 + 1));
        var project = Presets.TemplateFactory.Create("Blank");
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        Check("editor describes its cursor for screen readers",
            editor.DescribeCursor().Contains("bar 1") && editor.DescribeCursor().Contains("string 1"));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var count = Views.ScorePdfExporter.Export(project, 0, path);
            var head = System.IO.File.ReadAllBytes(path);
            var text = System.Text.Encoding.ASCII.GetString(head);
            Check("PDF export writes a structurally valid file", count >= 1 && text.StartsWith("%PDF-1.4") && text.TrimEnd().EndsWith("%%EOF") && text.Contains("startxref"));
        }
        finally { try { System.IO.File.Delete(path); } catch { } }
    }

    /// <summary>User templates round-trip through the templates folder; a crashed plug-in marks its track's FX button.</summary>
    private static void TestUserTemplatesAndFaultedChain()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-templates-" + Guid.NewGuid().ToString("N"));
        var previous = Services.UserTemplates.FolderOverride;
        Services.UserTemplates.FolderOverride = folder;
        try
        {
            Check("template names lose path characters", Services.UserTemplates.CleanName("a/b:c") == "abc");
            Check("an empty template name is refused", Services.UserTemplates.CleanName("  ") is null);
            Check("a built-in name cannot be overwritten", Services.UserTemplates.CleanName("Blank") == "Blank (custom)");
            Check("no user templates at first", Services.UserTemplates.List().Count == 0);
            var source = Presets.TemplateFactory.Create("Rock Band");
            source.IsDirty = true;
            var stored = Services.UserTemplates.Save("My band", source);
            Check("saving a template keeps the open score's dirty state", source.IsDirty && stored == "My band");
            Check("the template is listed", Services.UserTemplates.List().SequenceEqual(new[] { "My band" }));
            var loaded = Services.UserTemplates.Create("My band");
            Check("a new score from the template has the same tracks and is unsaved",
                loaded.Tracks.Count == source.Tracks.Count && loaded.IsDirty);
            Check("built-in templates still resolve", Services.UserTemplates.Create("Blank").Tracks.Count >= 1);
        }
        finally
        {
            Services.UserTemplates.FolderOverride = previous;
            try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
        }

        var rig = new Plugins.RigPreset();
        rig.Plugins.Add(new Plugins.PluginSlot { Path = @"C:\VST\Amp.vst3" });
        Check("a chain with a quarantined plug-in is faulted",
            Views.ArrangementPanel.IsChainFaulted(rig, new List<string> { @"c:\vst\amp.vst3" }));
        Check("a healthy chain is not faulted", !Views.ArrangementPanel.IsChainFaulted(rig, new List<string> { @"C:\VST\Other.vst3" }));
    }

    /// <summary>Engraving rules from the audit: empty voice 2 is not drawn; key changes cancel with naturals.</summary>
    private static void TestEngravingHeader()
    {
        var empty = new MeasureModel { Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList() };
        Check("an empty second voice is not engraved", !Views.TabEditorControl.Voice2HasContent(empty));
        var withNote = new MeasureModel { Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList() };
        withNote.Voice2Cells[3].Notes.Add(new TabNote { StringIndex = 1, Fret = 3, MidiValue = 50 });
        Check("a second voice with a note is engraved", Views.TabEditorControl.Voice2HasContent(withNote));
        Check("key sig: G major from C has no naturals", Views.TabEditorControl.KeySignatureGlyphs(0, 1) == (0, 1));
        Check("key sig: C after A major cancels 3 sharps", Views.TabEditorControl.KeySignatureGlyphs(3, 0) == (3, 0));
        Check("key sig: A to E major adds without naturals", Views.TabEditorControl.KeySignatureGlyphs(3, 4) == (0, 4));
        Check("key sig: 4 sharps to 2 sharps cancels 2", Views.TabEditorControl.KeySignatureGlyphs(4, 2) == (2, 2));
        Check("key sig: sharps to flats cancels all", Views.TabEditorControl.KeySignatureGlyphs(2, -1) == (2, 1));
    }

    /// <summary>
    /// The shared score/timeline selection (SelectionModel + SelectionSync): each side mirrors the other,
    /// clearing either clears both, bar edits move the range, and no pair of views can loop.
    /// </summary>
    private static void TestSelectionModel()
    {
        // ---- pure model with a fake timeline view that mirrors it ----
        var model = new SelectionModel();
        (int Start, int End)? timeline = null;
        var timelineApplies = 0;
        model.Changed += (_, _) => { timeline = model.BarRange; timelineApplies++; };

        Check("model set from the editor: timeline state matches",
            model.SetRange(0, 2, 5, SelectionOrigin.Editor) && timeline == (2, 5));
        Check("setting the same range again is a no-op (no echo)",
            !model.SetRange(0, 2, 5, SelectionOrigin.Timeline) && timelineApplies == 1);
        Check("a backwards drag is normalised", model.SetRange(0, 7, 3, SelectionOrigin.Timeline) && timeline == (3, 7));
        Check("clear from the timeline clears the model", model.Clear(SelectionOrigin.Timeline) && !model.HasRange && timeline is null);
        Check("clearing an empty selection is a no-op", !model.Clear(SelectionOrigin.Editor));

        model.SetRange(0, 1, 2, SelectionOrigin.Editor, startCell: 4, endCell: 8);
        Check("switching track keeps the bars and drops the per-track cell bounds",
            model.SetTrack(1) && model.TrackIndex == 1 && model.BarRange == (1, 2) && model.StartCell == 0 && model.EndCell == -1);

        model.SetRange(1, 4, 6, SelectionOrigin.Timeline);
        model.Remap(SelectionModel.InsertMap(10, 2, 3), 13);
        Check("bars inserted before the range move it", model.BarRange == (7, 9), model.BarRange?.ToString());
        model.Remap(SelectionModel.RemoveMap(13, 8, 8), 12);
        Check("a bar deleted inside the range shrinks it", model.BarRange == (7, 8), model.BarRange?.ToString());
        model.Remap(SelectionModel.RemoveMap(12, 6, 9), 8);
        Check("deleting every selected bar clears the range in both views", !model.HasRange && timeline is null);
        model.SetRange(0, 5, 9, SelectionOrigin.Editor);
        model.ClampTo(7, 1);
        Check("undo to a shorter song clamps the range", model.BarRange == (5, 6), model.BarRange?.ToString());
        model.ClampTo(3, 1);
        Check("a range entirely past the song end is cleared", !model.HasRange);

        // ---- no infinite loop: two views that always answer with a different range ----
        var fight = new SelectionModel();
        var calls = 0;
        fight.Changed += (_, _) => { calls++; fight.SetRange(0, 0, calls, SelectionOrigin.Editor); };
        fight.Changed += (_, _) => { calls++; fight.SetRange(0, 0, calls + 100, SelectionOrigin.Timeline); };
        fight.SetRange(0, 1, 1, SelectionOrigin.Command);
        Check("two disagreeing views stop after a bounded number of rounds (no recursion, no endless loop)",
            fight.ChangeCount <= SelectionModel.MaxRounds && !fight.IsNotifying, $"{fight.ChangeCount} notifications");

        // ---- real score editor bound through SelectionSync (the window's glue) ----
        var editor = NewEditor(out var project, out var track);
        while (track.Measures.Count < 8) track.Measures.Add(new MeasureModel { Number = track.Measures.Count + 1 });
        project.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(8) });
        var shared = new SelectionModel();
        (int Start, int End)? arrangement = null;
        var sync = new SelectionSync(shared, editor, _ => arrangement = shared.BarRange);
        var editorEvents = 0;
        editor.SelectionChanged += (_, _) => { editorEvents++; sync.PushFromEditor(); };

        editor.SelectMeasureRange(1, 3);   // as a score drag of whole bars
        Check("range on the score: model and arrangement show the same bars and track",
            shared.BarRange == (1, 3) && arrangement == (1, 3) && shared.TrackIndex == 0);

        editorEvents = 0;
        shared.SetRange(0, 4, 6, SelectionOrigin.Timeline);   // as a timeline drag
        Check("range on the timeline: the score selects the same bars",
            editor.HasSelection && editor.AffectedMeasureRange == (4, 6) && arrangement == (4, 6));
        Check("applying to the score raised one selection event and did not write back",
            editorEvents == 1 && shared.BarRange == (4, 6) && !sync.IsApplyingToEditor, $"{editorEvents} events");

        editor.SelectedTrackIndex = 1;
        shared.SetTrack(1);
        Check("switching the active track keeps the same bars selected on the new track",
            editor.SelectedTrackIndex == 1 && editor.HasSelection && editor.AffectedMeasureRange == (4, 6));

        shared.Clear(SelectionOrigin.Timeline);   // plain click on empty timeline space
        Check("clear from the timeline clears the score", !editor.HasSelection && arrangement is null);

        editor.SelectMeasureRange(0, 2);
        editor.ClearSelection();                  // click on empty score paper / Esc in the score
        Check("clear from the score clears the timeline", !shared.HasRange && arrangement is null);

        editor.SelectAll();
        Check("Ctrl+A in the score selects every bar on the timeline", shared.BarRange == (0, 7));

        shared.SetRange(1, 5, 7, SelectionOrigin.Command);
        project.Tracks[1].Measures.RemoveRange(6, 2);
        project.Tracks[0].Measures.RemoveRange(6, 2);
        sync.Reconcile(6, project.Tracks.Count);  // after an edit / undo that removed bars 7-8
        Check("after bars are removed under the range, model and score agree on the clamped range",
            shared.BarRange == (5, 5) && editor.HasSelection && editor.AffectedMeasureRange == (5, 5),
            $"model {shared.BarRange}, score {editor.AffectedMeasureRange}");
    }
}

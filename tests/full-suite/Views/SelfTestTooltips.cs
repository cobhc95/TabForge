using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private const string LongTipSample =
        "Off by default. On: this chain (plug-ins, their settings, wiring and MIDI configuration, volume, audio input and monitor setting) is added as a track, " +
        "not armed, to every song you open or create. That track is not saved with the song until you untick this. Manage them in Settings > Audio & Plug-ins.";

    private static Size MeasureTip(object content, Style? style = null)
    {
        var tip = new ToolTip { Content = content };
        if (style is not null) tip.Style = style;
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tip.DesiredSize;
    }

    /// <summary>
    /// Tooltips: long plain-text tips wrap onto a few lines (about 460 DIP), short ones keep their natural width, rich content and
    /// explicit line breaks are unchanged; every control bound to a command shows the command's CURRENT key last, in brackets
    /// (follows rebinding, unbinding and presets), and the sources carry no hard-coded keys.
    /// </summary>
    private static void TestTooltips()
    {
        // 1. Wrapping (global implicit style, plus the Preferences window's own tooltip style).
        var oneLine = MeasureTip("Stop playback");
        var longTip = MeasureTip(LongTipSample);
        Check("a long tooltip is at most 480 DIP wide", longTip.Width <= 480, $"{longTip.Width:0}");
        Check("a long tooltip wraps onto several lines", longTip.Height >= oneLine.Height * 2.4, $"{longTip.Height:0} vs one line {oneLine.Height:0}");
        Check("a short tooltip stays one line at its natural width", oneLine.Width < 160 && oneLine.Height < longTip.Height / 2, $"{oneLine.Width:0} x {oneLine.Height:0}");
        var twoLines = MeasureTip("First line\nSecond line");
        Check("an explicit line break still works", twoLines.Height > oneLine.Height * 1.6 && twoLines.Width < 160, $"{twoLines.Width:0} x {twoLines.Height:0}");
        var rich = MeasureTip(new Border { Width = 700, Height = 20 });
        Check("rich (non-string) tooltip content is not narrowed", rich.Width >= 700, $"{rich.Width:0}");
        var prefs = new PreferencesWindow(new AppSettings());
        try
        {
            var local = (Style)prefs.FindResource(typeof(ToolTip));
            var prefsLong = MeasureTip(LongTipSample, local);
            var prefsShort = MeasureTip("Apply", local);
            Check("the Preferences window's tooltip style wraps a long tip too", prefsLong.Width <= 480 && prefsLong.Height >= prefsShort.Height * 2.4, $"{prefsLong.Width:0} x {prefsLong.Height:0}");
        }
        finally { prefs.Close(); }

        // 2. The bracket: composition rules.
        Check("the key goes last in brackets", HotkeyCatalog.TooltipWithDisplay("Play / pause", "Space") == "Play / pause (Space)");
        Check("no key, no brackets", HotkeyCatalog.TooltipWithDisplay("Play / pause", "") == "Play / pause");
        Check("a text that already ends in a bracket gets no second bracket group",
            HotkeyCatalog.TooltipWithDisplay("Score zoom (50-200%)", "Ctrl++") == "Score zoom (50-200%; Ctrl++)");
        Check("a bracket on an earlier line does not swallow the key",
            HotkeyCatalog.TooltipWithDisplay("Tie (legato)\nRight-click to unpin.", "L") == "Tie (legato)\nRight-click to unpin. (L)");

        // 3. Controls bound to a command show its current key; rebinding, unbinding and presets update them at once.
        var song = OrderedSong();
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var tabBar = new BrowserTabBar { Settings = new AppSettings().Tabs };
        var docs = new Documents.DocumentManager();
        tabBar.Bind(docs);
        docs.AddNew();
        tabBar.Refresh();
        var mixerHost = new FakeMixerHost();
        mixerHost.Project.Tracks.Add(MixerTestTrack("Lead", TrackKind.Guitar, 30));
        var mixer = new MixerWindow(mixerHost, null);
        var arrangementWindow = new Window { Content = panel, Width = 1000, Height = 500, ShowInTaskbar = false, Left = -5000, Top = -5000 };
        var tabWindow = new Window { Content = tabBar, Width = 900, Height = 80, ShowInTaskbar = false, Left = -5000, Top = -5000 };
        using var alive = KeepAlive();
        var original = TooltipShortcuts.Hotkeys;
        try
        {
            TooltipShortcuts.SetHotkeys(new HotkeySettings());
            ShowTestWindow(arrangementWindow); ShowTestWindow(tabWindow); ShowTestWindow(mixer);
            arrangementWindow.UpdateLayout(); tabWindow.UpdateLayout(); mixer.UpdateLayout(); PumpUi();

            List<(FrameworkElement Element, string Command)> Bound() =>
                new DependencyObject[] { arrangementWindow, tabWindow, mixer }
                    .SelectMany(root => VisualDescendants<FrameworkElement>(root))
                    .Select(e => (Element: e, Command: TooltipShortcuts.GetCommand(e)))
                    .Where(x => !string.IsNullOrEmpty(x.Command))
                    .Select(x => (x.Element, x.Command!)).ToList();
            string Expect(string command, HotkeySettings keys) => HotkeyCatalog.DisplayAll(keys, command);
            bool Shows(FrameworkElement e, string command, HotkeySettings keys)
            {
                var want = Expect(command, keys);
                var tip = e.ToolTip as string ?? "";
                var basis = TooltipShortcuts.GetBaseText(e) ?? "";
                return tip == HotkeyCatalog.TooltipWithDisplay(basis, want) && (want.Length == 0 || tip.EndsWith(want + ")", StringComparison.Ordinal));
            }

            var keys = new HotkeySettings();
            var bound = Bound();
            var commands = bound.Select(x => x.Command).Distinct().ToList();
            Check("the windows hold controls bound to commands (snap, mixer, FX, arm, properties, tab buttons, master FX)",
                commands.Count >= 8 && new[] { "Timeline.Snap", "View.Mixer", "Track.FxChain", "Track.Arm", "Track.Properties", "Tab.New", "Tab.Close", "Mixer.MasterFx" }.All(commands.Contains),
                string.Join(", ", commands));
            Check("every bound control's command exists in the shortcut catalogue", commands.All(c => HotkeyCatalog.ById(c) is not null), string.Join(", ", commands.Where(c => HotkeyCatalog.ById(c) is null)));
            var missing = bound.Where(x => !Shows(x.Element, x.Command, keys)).Select(x => x.Command).Distinct().ToList();
            Check("every bound control with a key shows it in brackets at the end of its tooltip", missing.Count == 0, string.Join(", ", missing));
            var snap = bound.First(x => x.Command == "Timeline.Snap").Element;
            var tabNew = bound.First(x => x.Command == "Tab.New").Element;
            Check("the new-tab button shows its default key", tabNew.ToolTip is string t && t.EndsWith("(Ctrl+T)"), tabNew.ToolTip as string);

            keys.Bindings["Tab.New"] = "Ctrl+Alt+K";
            keys.Disable("Timeline.Snap");
            TooltipShortcuts.SetHotkeys(keys);
            Check("a rebound command shows its new key at once (no polling)", tabNew.ToolTip is string t2 && t2.EndsWith("(Ctrl+Alt+K)") && !t2.Contains("Ctrl+T"), tabNew.ToolTip as string);
            Check("an unbound command's tooltip is its plain text, with no brackets", (snap.ToolTip as string) == TooltipShortcuts.GetBaseText(snap), snap.ToolTip as string);

            var classic = new HotkeySettings();
            HotkeyPresets.Apply(classic, HotkeyPresets.GuitarPro5);
            TooltipShortcuts.SetHotkeys(classic);
            var afterPreset = Bound().Where(x => !Shows(x.Element, x.Command, classic)).Select(x => x.Command).Distinct().ToList();
            Check("switching the shortcut preset updates every bound tooltip", afterPreset.Count == 0, string.Join(", ", afterPreset));

            // The main window and the other tool windows are too heavy to open here (audio engine, user settings); their sources are
            // checked instead: no key typed into a tooltip by hand, and every bound control names a catalogue command and has a tooltip.
            var root = FindRepositoryRoot();
            if (root is null) Skip("tooltip sources carry no hard-coded keys", "no source checkout found", "source-hygiene");
            else
            {
                var tipKey = new System.Text.RegularExpressions.Regex(@"ToolTip\s*=\s*""[^""]*\((Ctrl|Alt|Shift|F\d{1,2}|Space)[^""]*\)""");
                var offenders = new List<string>();
                var unbound = new List<string>();
                foreach (var file in EnumerateHygieneFiles(root).Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith("SelfTest", StringComparison.Ordinal) || name.StartsWith("GpDialogs", StringComparison.Ordinal)) continue;
                    var text = File.ReadAllText(file);
                    foreach (System.Text.RegularExpressions.Match m in tipKey.Matches(text))
                    {
                        var shown = m.Value;
                        // keys of dialogs themselves (Enter / Esc) and of a TextBox are not catalogue commands
                        if (shown.Contains("(Esc)") || shown.Contains("(Enter)")) continue;
                        offenders.Add($"{name}: {shown}");
                    }
                    if (name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"<[A-Za-z]+[^<>]*TooltipShortcuts\.Command=""([^""]+)""[^<>]*>"))
                            if (HotkeyCatalog.ById(m.Groups[1].Value) is null || !m.Value.Contains("ToolTip=\"")) unbound.Add($"{name}: {m.Groups[1].Value}");
                }
                Check("no tooltip in the sources has a key typed into it by hand", offenders.Count == 0, string.Join("; ", offenders.Take(6)));
                Check("every XAML control bound to a command names a catalogue command and has a tooltip", unbound.Count == 0, string.Join("; ", unbound));
            }

            RenderTooltipPictures();
        }
        finally
        {
            TooltipShortcuts.SetHotkeys(original);
            arrangementWindow.Close(); tabWindow.Close(); mixer.Close();
        }
    }

    /// <summary>With TABFORGE_TIP_PNG_DIR set: tooltip pictures for review (long, short, with a key, light theme).</summary>
    private static void RenderTooltipPictures()
    {
        var folder = Environment.GetEnvironmentVariable("TABFORGE_TIP_PNG_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        void Shoot(string file, string text)
        {
            var tip = new ToolTip { Content = text };
            tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var pad = 12.0;
            tip.Arrange(new Rect(0, 0, tip.DesiredSize.Width, tip.DesiredSize.Height));
            tip.UpdateLayout();
            const double dpi = 192;
            var width = tip.DesiredSize.Width + 2 * pad; var height = tip.DesiredSize.Height + 2 * pad;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
                dc.DrawRectangle(Application.Current.TryFindResource("WindowBrush") as Brush ?? Brushes.Gray, null, new Rect(0, 0, width, height));
            bmp.Render(backdrop);
            var placed = new DrawingVisual();
            using (var dc = placed.RenderOpen())
                dc.DrawRectangle(new VisualBrush(tip), null, new Rect(pad, pad, tip.DesiredSize.Width, tip.DesiredSize.Height));
            bmp.Render(placed);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var stream = File.Create(Path.Combine(folder, file));
            encoder.Save(stream);
        }
        ThemeService.Apply(new AppSettings().Appearance);
        Shoot("tooltip-long-dark.png", LongTipSample);
        Shoot("tooltip-short-dark.png", "Stop playback");
        Shoot("tooltip-shortcut-dark.png", HotkeyCatalog.TooltipWithDisplay("Play / pause", "Space"));
        Shoot("tooltip-long-shortcut-dark.png", HotkeyCatalog.TooltipWithDisplay("Snapping on: clips snap to the grid, other clips and the playhead. Hold Alt while dragging to bypass. Right-click for the snap settings.", "Alt+S"));
        var light = new AppSettings();
        ThemeService.ApplyPreset(light.Appearance, "Light");
        ThemeService.Apply(light.Appearance);
        Shoot("tooltip-long-light.png", LongTipSample);
        Shoot("tooltip-shortcut-light.png", HotkeyCatalog.TooltipWithDisplay("Play / pause", "Space"));
        ThemeService.Apply(new AppSettings().Appearance);
    }
}

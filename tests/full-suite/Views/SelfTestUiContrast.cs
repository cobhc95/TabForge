using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>Text and glyph contrast of themed widgets, computed from the live theme brushes in every built-in theme.</summary>
public static partial class SelfTest
{
    private static Color BrushColour(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    /// <summary>Band view track pills: shown and hidden label text reads at 4.5:1 or better on the pill's own fill, also hovered and pressed.</summary>
    private static void TestBandPillContrast()
    {
        using var alive = KeepAlive();
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var appearance = new AppSettings().Appearance;
                ThemeService.ApplyPreset(appearance, theme);
                ThemeService.Apply(appearance);
                var song = new SongProject();
                song.Tracks.Add(new TrackModel { Name = "Shown", ColorHex = "#4C9AFF" });
                song.Tracks.Add(new TrackModel { Name = "Hidden", ColorHex = "#F2F2F2" });
                var state = new BandLayoutState();
                state.Sync(song);
                if (state.IsShown(song.Tracks[1])) state.Toggle(song.Tracks[1]);
                if (!state.IsShown(song.Tracks[0])) state.Toggle(song.Tracks[0]);
                var view = new BandView(state);
                view.SetPills(song.Tracks);
                var window = new Window { Content = view, Width = 600, Height = 300 };
                try
                {
                    ShowTestWindow(window);
                    window.UpdateLayout(); PumpUi();
                    foreach (var pill in view.Pills)
                    {
                        var label = ((StackPanel)pill.Content).Children.OfType<TextBlock>().Single();
                        var fg = ((SolidColorBrush)label.Foreground).Color;
                        var bg = ((SolidColorBrush)pill.Background).Color;
                        var ratio = ThemeService.Contrast(fg, bg);
                        Check($"band pill ({theme}, {(pill.IsChecked == true ? "shown" : "hidden")}): label {fg} on {bg} is {ratio:0.0}:1 (>= 4.5)", ratio >= 4.5);
                        Check($"band pill ({theme}): themed style, tooltip names the track", pill.Style == Application.Current.Resources["TrackPill"] && (pill.ToolTip as string ?? "").Contains(label.Text));
                    }
                    Check($"band pill ({theme}): one shown and one hidden", view.Pills.Count(p => p.IsChecked == true) == 1 && view.Pills.Count == 2);
                    foreach (var surface in new[] { "HoverBrush", "PressBrush" })
                    {
                        var ratio = ThemeService.Contrast(BrushColour("TextBrush"), BrushColour(surface));
                        Check($"band pill ({theme}): hover/press text on {surface} is {ratio:0.0}:1 (>= 4.5)", ratio >= 4.5);
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { ThemeService.Apply(new AppSettings().Appearance); }
    }

    /// <summary>Fretboard marker numbers read at 4.5:1 on the marker's real surface (the outlined "next" circle shows the dark board through it).</summary>
    private static void TestFretMarkerLabelContrast()
    {
        var previous = Visualization.VisualTheme.IsLight;
        try
        {
            foreach (var light in new[] { false, true })
            {
                Visualization.VisualTheme.IsLight = light;
                var t = new Visualization.VisualTheme();
                foreach (var (name, colour, fill) in new[] { ("next", t.Next, 0.30), ("current", t.Current, 1.0), ("upcoming, far", t.Accent, 0.45), ("upcoming, near", t.Accent, 0.6), ("recent, old", t.Past, 0.7), ("recent, new", t.Past, 0.9) })
                {
                    var surface = Visualization.FretboardRenderer.Blend(t.Wood, colour, fill);
                    var ratio = Visualization.MarkerInk.Contrast(Visualization.MarkerInk.On(surface), surface);
                    Check($"fret marker ({(light ? "light" : "dark")}, {name}): number on its circle is {ratio:0.0}:1 (>= 4.5)", ratio >= 4.5);
                }
            }
        }
        finally { Visualization.VisualTheme.IsLight = previous; }
    }

    /// <summary>A long track name in a narrow track list ends in "…" (the read-only name shows as trimmed text) and its tooltip carries the full name.</summary>
    private static void TestTrackNameEllipsis()
    {
        const string name = "Rhythm Guitar with a very long name indeed";
        var song = new SongProject();
        song.Tracks.Add(MixerTestTrack(name, TrackKind.Guitar, 8));
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var host = new Border { Width = 520, Height = 300, Child = panel };
        host.Measure(new Size(520, 300));
        host.Arrange(new Rect(0, 0, 520, 300));
        host.UpdateLayout();
        var box = VisualDescendants<TextBox>(panel).FirstOrDefault(t => t.Text == name);
        var text = box is null ? null : VisualDescendants<TextBlock>(box).FirstOrDefault(t => t.Text == name);
        Check("track name: the read-only name uses the trimmed style", box?.Style == Application.Current.TryFindResource("TrimmedNameBox"));
        var full = new TextBlock { Text = name, FontSize = box?.FontSize ?? 11 };
        full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Check("track name: a long name ends in an ellipsis inside its column",
            text?.TextTrimming == TextTrimming.CharacterEllipsis && text.ActualWidth + 1 < full.DesiredSize.Width, $"shown {text?.ActualWidth:0} of {full.DesiredSize.Width:0}");
        Check("track name: the tooltip shows the full name", (box?.ToolTip as string)?.StartsWith(name, StringComparison.Ordinal) == true);
    }


    /// <summary>A dialog's default button uses the shared AccentButton style and its label reads at 4.5:1 on the accent.</summary>
    private static void DefaultButtonReads(string what, Button? button, string text)
    {
        var fg = (button?.Foreground as SolidColorBrush)?.Color ?? Colors.White;
        var bg = (button?.Background as SolidColorBrush)?.Color ?? Colors.White;
        var ratio = ThemeService.Contrast(fg, bg);
        Check($"{what}: label {fg} on accent {bg} is {ratio:0.0}:1 (>= 4.5)",
            button?.Content as string == text && button.Style == Application.Current.TryFindResource("AccentButton") && ratio >= 4.5);
    }

    private static Button? FindDefaultButton(Window window)
    {
        window.Measure(new Size(800, 600));   // builds the content tree without showing the dialog
        return Logical<Button>(window).FirstOrDefault(b => b.IsDefault);
    }


    /// <summary>The note-effect editors' OK button: its label reads at 4.5:1 on the accent fill in every theme.</summary>
    private static void TestEffectEditorOkContrast()
    {
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var appearance = new AppSettings().Appearance;
                ThemeService.ApplyPreset(appearance, theme);
                ThemeService.Apply(appearance);
                var dialog = new Views.EffectEditors.ThemedEditorDialog("Test", new Border());
                DefaultButtonReads($"effect editor OK ({theme})", dialog.OkButton, "OK");
                dialog.Close();
                var paste = new Views.PasteOptionsDialog(new[] { PasteQuestion.Octave });
                DefaultButtonReads($"paste dialog Paste ({theme})", FindDefaultButton(paste), "Paste");
                paste.Close();
            }
        }
        finally { ThemeService.Apply(new AppSettings().Appearance); }
    }

    /// <summary>Hover and press fills stand out from the panels and cards they sit on (>= 1.3:1), and the side-pane cards follow a theme switch.</summary>
    private static void TestHoverSurfaceContrast()
    {
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var appearance = new AppSettings().Appearance;
                ThemeService.ApplyPreset(appearance, theme);
                ThemeService.Apply(appearance);
                foreach (var surface in new[] { "PanelBrush", "Panel2Brush" })
                    foreach (var state in new[] { "HoverBrush", "PressBrush" })
                    {
                        var ratio = ThemeService.Contrast(BrushColour(state), BrushColour(surface));
                        Check($"{state} on {surface} ({theme}) is {ratio:0.00}:1 (>= 1.3)", ratio >= 1.3);
                    }
            }
            // A card built while dark (as the Structure / Rhythm / Layout panes are) must turn light with the theme.
            var dark = new AppSettings().Appearance;
            ThemeService.ApplyPreset(dark, "Dark");
            ThemeService.Apply(dark);
            var card = new Border();
            card.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
            var light = new AppSettings().Appearance;
            ThemeService.ApplyPreset(light, "Light");
            ThemeService.Apply(light);
            using var alive = KeepAlive();
            var host = new Window { Content = card, Width = 200, Height = 100 };   // the pane joins the tree when its tab is chosen
            try
            {
                ShowTestWindow(host);
                host.UpdateLayout(); PumpUi();
                Check("side-pane card follows a dark -> light switch", ((SolidColorBrush)card.Background).Color == BrushColour("Panel2Brush"));
            }
            finally { host.Close(); }
        }
        finally { ThemeService.Apply(new AppSettings().Appearance); }
    }

    /// <summary>Framed icons (the toolbar zoom buttons): in the light theme the frame is an opaque pale tint and the glyph is darkened, so the glyph keeps 3:1.</summary>
    private static void TestFramedIconContrast()
    {
        var glyph = Color.FromRgb(0x3A, 0x9B, 0xFF);   // zoom_minus / zoom_plus stroke
        var frame = Color.FromRgb(0x10, 0x2E, 0x52);   // their panel tint
        var previous = Visualization.VisualTheme.IsLight;
        try
        {
            Visualization.VisualTheme.IsLight = true;
            var dark = SvgIconView.ForTheme(glyph)!.Value;
            var ratio = ThemeService.Contrast(dark, SvgIconView.FrameFill(frame, true));
            Check($"framed icon (light): glyph {dark} on frame is {ratio:0.0}:1 (>= 3)", ratio >= 3);
            Visualization.VisualTheme.IsLight = false;
            Check("framed icon (dark): art unchanged", SvgIconView.ForTheme(glyph) == glyph && SvgIconView.FrameFill(frame, false) == frame);
        }
        finally { Visualization.VisualTheme.IsLight = previous; }
    }
}

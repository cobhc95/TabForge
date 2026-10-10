using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>The opt-in playing-bar band: off by default, one band over the playing bar, moves only on a bar change.</summary>
public static partial class SelfTest
{
    private static TabEditorControl PlayingBarEditor(bool dark, double zoom, double arrangeWidth = 1100)
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(12) });
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, Appearance = { DarkPaper = dark }, Zoom = zoom, HideCursor = true };
        editor.Measure(new Size(arrangeWidth, 700));
        editor.Arrange(new Rect(0, 0, arrangeWidth, 700));
        editor.UpdateLayout();
        return editor;
    }

    private static byte[] PlayingBarPixels(TabEditorControl editor, out int width, out int height, string? png, int imageHeight = 420)
    {
        // A retained drawing is re-recorded in the layout pass, so run one before capturing.
        editor.InvalidateMeasure();
        editor.Measure(new Size(imageHeight == 420 ? 1100 : 1800, 700));
        editor.Arrange(new Rect(0, 0, imageHeight == 420 ? 1100 : 1800, 700));
        editor.UpdateLayout();
        width = (int)Math.Max(1, Math.Min(imageHeight == 420 ? 1100 : 4000, editor.ActualWidth));
        height = imageHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(editor);
        if (png is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(png);
            encoder.Save(stream);
        }
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>The frozen per-system drawings never keep or miss a band: bar changes across systems, settings changes and stop.</summary>
    private static void CheckPlayingBarFrozenSystems()
    {
        var editor = PlayingBarEditor(true, 1.0, 1800);
        var layout = editor.Layout.GetLayout(editor.Project!.Tracks[0]);
        int b0 = layout.Systems[0].LastMeasure, b1 = b0 + 1;
        Check("playing bar (frozen): the test song wraps onto a second system", layout.Measure(b1).SystemIndex == 1);
        byte[] Shot(out int w) { var p = PlayingBarPixels(editor, out w, out var h, null, 700); return p; }
        var reference = Shot(out var width);
        int At(int bar)
        {
            var m = layout.Measure(bar);
            var rect = editor.Playback.BandRectFor(bar);
            return ((int)(rect.Y + 3) * width + (int)(m.X + m.Width * 0.5)) * 4;
        }
        HashSet<int> Tinted(byte[] shot) => new[] { b0, b1 }.Where(b => shot[At(b)] != reference[At(b)] || shot[At(b) + 1] != reference[At(b) + 1]).ToHashSet();

        editor.Appearance.PlayingBarEnabled = true;
        editor.Playback.Active = true;
        editor.Playback.SetPlayhead(b0, 0);
        var first = Tinted(Shot(out _));
        Check("playing bar (frozen): the last bar of system A is banded", first.SetEquals(new[] { b0 }), $"tinted {{{string.Join(",", first)}}} b0={b0} b1={b1}");
        editor.Playback.SetPlayhead(b1, 0);
        Check("playing bar (frozen): crossing into system B moves the band; A keeps no stale copy", Tinted(Shot(out _)).SetEquals(new[] { b1 }));
        editor.Playback.SetPlayhead(b0, 0);
        Check("playing bar (frozen): a jump back moves it again", Tinted(Shot(out _)).SetEquals(new[] { b0 }));

        editor.Appearance.PlayingBarColor = Color.FromRgb(0x20, 0xA0, 0x40);
        var recoloured = Shot(out _);
        Check("playing bar (frozen): a colour change repaints at once while playing", Tinted(recoloured).SetEquals(new[] { b0 }) && recoloured[At(b0) + 1] > reference[At(b0) + 1] + 5);
        editor.Appearance.PlayingBarOpacity = 0.55;
        var stronger = Shot(out _);
        Check("playing bar (frozen): an opacity change repaints at once", stronger[At(b0) + 1] != recoloured[At(b0) + 1]);
        editor.Appearance.PlayingBarEnabled = false;
        Check("playing bar (frozen): turning it off removes the band at once", Tinted(Shot(out _)).Count == 0);
        editor.Appearance.PlayingBarEnabled = true;
        Check("playing bar (frozen): turning it on again shows it at once", Tinted(Shot(out _)).SetEquals(new[] { b0 }));

        editor.Playback.Active = false;   // stop without any other repaint request
        Check("playing bar (frozen): stop removes the band from the frozen copy", Tinted(Shot(out _)).Count == 0);

        editor.HideCursor = false;
        editor.Appearance.PlayingBarWhenStopped = true;
        editor.Playback.Measure = -1;
        editor.SetPosition(b0, 0, 0, false);
        Check("playing bar (frozen): 'when stopped' bands the cursor's bar", Tinted(Shot(out _)).SetEquals(new[] { b0 }));
        editor.SetPosition(b1, 0, 0, false);
        Check("playing bar (frozen): moving the cursor to another system moves the band", Tinted(Shot(out _)).SetEquals(new[] { b1 }));
        editor.Appearance.PlayingBarWhenStopped = false;
        Check("playing bar (frozen): turning 'when stopped' off removes it", Tinted(Shot(out _)).Count == 0);
    }

    private static void TestPlayingBar()
    {
        CheckPlayingBarFrozenSystems();
        var defaults = new FollowSettings();
        Check("playing bar: off by default, 20% opacity, 'when stopped' off", !defaults.PlayingBarEnabled && !defaults.PlayingBarWhenStopped && Math.Abs(defaults.PlayingBarOpacity - 0.20) < 1e-9);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<FollowSettings>("{\"Mode\":\"Jump\"}")!;
        Check("playing bar: a settings file without the new keys opens with it off", !legacy.PlayingBarEnabled);
        var saved = System.Text.Json.JsonSerializer.Deserialize<FollowSettings>(System.Text.Json.JsonSerializer.Serialize(
            new FollowSettings { PlayingBarEnabled = true, PlayingBarColour = "#112233", PlayingBarOpacity = 0.4, PlayingBarWhenStopped = true }))!;
        Check("playing bar: the four settings round-trip", saved.PlayingBarEnabled && saved.PlayingBarColour == "#112233" && Math.Abs(saved.PlayingBarOpacity - 0.4) < 1e-9 && saved.PlayingBarWhenStopped);
        var command = HotkeyCatalog.ById("View.PlayingBar");
        Check("playing bar: the toggle is a bindable, unbound command", command is not null && command.DefaultGesture.Length == 0);
        Check("playing bar: its settings rows exist", new[] { "follow.playingbar", "follow.playingbar.colour", "follow.playingbar.opacity", "follow.playingbar.stopped" }
            .All(id => SettingsCatalog.Build(new AppSettings()).Any(r => r.Key == id)));

        var outDir = Environment.GetEnvironmentVariable("TF_PLAYINGBAR_SHOTS");
        foreach (var (dark, zoom) in new[] { (true, 1.0), (false, 1.0), (true, 1.5), (false, 1.5) })
        {
            var name = $"{(dark ? "dark" : "light")}-{zoom:0.0}x";
            var editor = PlayingBarEditor(dark, zoom);
            editor.Playback.SetPlayhead(2, 0);
            editor.Playback.Active = true;
            Check($"playing bar ({name}): no band while off", editor.Playback.PlayingBarRect() is null);
            var offPixels = PlayingBarPixels(editor, out var w, out var h, outDir is null ? null : Path.Combine(outDir, $"off-{name}.png"));

            editor.Appearance.PlayingBarEnabled = true;
            editor.InvalidateVisual();
            var layout = editor.Layout.GetLayout(editor.Project!.Tracks[0]);
            var position = layout.Measure(2);
            var rect = editor.Playback.PlayingBarRect();
            Check($"playing bar ({name}): one band covers the playing bar's x-range", rect is { } r && Math.Abs(r.X - position.X) < 0.01 && Math.Abs(r.Width - position.Width) < 0.01);
            Check($"playing bar ({name}): the band spans the system's staff and tab height", rect is { } r2 && r2.Height > 90 && r2.Height < 220);
            var onPixels = PlayingBarPixels(editor, out _, out _, outDir is null ? null : Path.Combine(outDir, $"on-{name}.png"));
            if (rect is { } rr)
            {
                // A pixel inside the bar changes, one in the next bar does not.
                int Index(double x, double y) => ((int)(y * zoom) * w + (int)(x * zoom)) * 4;
                var inside = Index(rr.X + rr.Width * 0.5, rr.Y + 3);
                var next = layout.Measure(3);
                var outside = Index(next.X + next.Width * 0.5, rr.Y + 3);
                Check($"playing bar ({name}): the band tints the bar and not its neighbour",
                    inside + 3 < onPixels.Length && onPixels[inside] != offPixels[inside] && onPixels[outside] == offPixels[outside]);
            }

            // Ticks inside one bar rebuild nothing; a bar change rebuilds once.
            var builds = editor.Playback.PlayingBarBuilds;
            for (var tick = 0; tick < 200; tick++)
            {
                editor.Playback.Fraction = tick / 200.0;
                editor.Playback.SetPlayhead(2, tick % 4);
                _ = editor.Playback.PlayingBarRect();
            }
            Check($"playing bar ({name}): 200 ticks within one bar rebuild the band 0 times", editor.Playback.PlayingBarBuilds == builds, $"{editor.Playback.PlayingBarBuilds - builds} rebuilds");
            editor.Playback.SetPlayhead(3, 0);
            _ = editor.Playback.PlayingBarRect();
            Check($"playing bar ({name}): moving to the next bar rebuilds it exactly once", editor.Playback.PlayingBarBuilds == builds + 1);
            editor.Appearance.PlayingBarColor = Color.FromRgb(0x20, 0xA0, 0x40);
            editor.Appearance.PlayingBarOpacity = 0.5;
            _ = editor.Playback.PlayingBarRect();
            Check($"playing bar ({name}): a new colour or opacity is applied", editor.Playback.PlayingBarBuilds == builds + 2);

            editor.Playback.Active = false;
            editor.Playback.Clear();
            Check($"playing bar ({name}): hidden when stopped", editor.Playback.PlayingBarRect() is null);
            editor.HideCursor = false;
            editor.Appearance.PlayingBarWhenStopped = true;
            editor.SetPosition(5, 0, 0, false);
            Check($"playing bar ({name}): 'when stopped' bands the cursor's bar", editor.Playback.PlayingBarRect() is { } stopped && Math.Abs(stopped.X - layout.Measure(5).X) < 0.01);
        }
    }
}

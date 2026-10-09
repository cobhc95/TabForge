using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Docking;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// The fretboard pane opens at a "medium" size computed from the size model (at most the score's text scale), and cannot be made taller than the
/// board's maximum stretch (no empty space above and below it) nor smaller than its full-draw minimum.
/// Set TABFORGE_FRETPANE_PNG to a folder to also get pictures (default, minimum, maximum; light and dark).
/// </summary>
public static partial class SelfTest
{
    private static void TestFretboardPaneSize()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var state = InstrumentVisualizer.Build(p, p.Tracks[0], tl, 0, false, false, 4, false, false, null);
        var png = Environment.GetEnvironmentVariable("TABFORGE_FRETPANE_PNG");
        using var alive = KeepAlive();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var appearance = new AppSettings().Appearance;
            ThemeService.ApplyPreset(appearance, theme);
            ThemeService.Apply(appearance);
            foreach (var (width, height) in new[] { (2000.0, 1700.0), (1200.0, 1300.0) })
            {
                var panel = new InstrumentPanel();
                var host = new Border { BorderThickness = new Thickness(1), Child = panel };
                var workspace = new DockWorkspace(new Window());
                var stage = new FitStage(workspace, width, height);
                workspace.SetEditorContent(new Border());
                workspace.RegisterPanel("instrument", "Fretboard", host, 360, 150, "instrument", "score-editor");
                panel.RequiredHeightChanged += r => workspace.SetPanelContentMinHeight("instrument", r + 2);
                panel.MaximumHeightChanged += m => workspace.SetPanelContentMaxHeight("instrument", double.IsPositiveInfinity(m) ? null : m + 2);
                workspace.RestoreLayout(null);
                panel.SetState(state);
                stage.UpdateLayout(); PumpUi(); stage.UpdateLayout(); PumpUi();
                var tag = $"{theme} {width:0}x{height:0}";
                var strings = state.Tuning.Count;

                // Medium default: string spacing about 29 px at the wide window, from the size model (not a pixel constant).
                var medium = panel.MediumHeight();
                var scale = Math.Min(Math.Min(InstrumentPanel.MediumScale, panel.ScaleCap), panel.ActualWidth / InstrumentPanel.NaturalWidth);
                var gap = (medium / scale - FretboardGeometry.TopPad - FretboardGeometry.BottomPad) / (strings - 1);
                Log.Add($"  info  fretboard pane {tag}: panel width {panel.ActualWidth:0}, required {panel.RequiredHeight:0}, medium {medium:0}, max {panel.MaximumHeight:0}, string gap {gap * scale:0.0}px");
                Check($"fretboard pane {tag}: the default is between the minimum and the maximum", medium >= panel.RequiredHeight - 0.5 && medium <= panel.MaximumHeight + 0.5);
                if (width >= 2000)
                    Check($"fretboard pane {tag}: the medium default draws at the score's scale (about 23 px string spacing at 100%)", gap * scale is > 21 and < 26, $"{gap * scale:0.0}");
                if (png is { Length: > 0 }) FretPanePng(stage, png, $"default-{tag}", width, height, "instrument", workspace, medium + 2);

                // Splitter/ratio far too tall: the pane stops at its maximum and the board has no empty band.
                var layout = workspace.CaptureLayout();
                var node = FindInstrumentSplit(layout.Root);
                if (node is null) { Check($"fretboard pane {tag}: the instrument sits above the score", false); continue; }
                node.Ratio = 0.9;
                workspace.RestoreLayout(layout);
                stage.UpdateLayout(); PumpUi(); stage.UpdateLayout(); PumpUi();
                Check($"fretboard pane {tag}: the pane cannot grow past the board's maximum stretch",
                    host.ActualHeight <= panel.MaximumHeight + 2 + 0.5, $"{host.ActualHeight:0.0} vs {panel.MaximumHeight + 2:0.0}");
                if (panel.CurrentFretboardLayout() is { } fb)
                {
                    var slack = panel.ActualHeight / fb.Scale - FretboardGeometry.TopPad - FretboardGeometry.BottomPad - fb.Layout.Board.Height;
                    Check($"fretboard pane {tag}: no empty band above and below the board at the maximum", slack <= 12, $"{slack:0.0} virtual px");
                }
                if (png is { Length: > 0 }) FretPanePng(stage, png, $"max-{tag}", width, height, "instrument", workspace, 0);

                // Far too short: never below the full-draw minimum.
                node.Ratio = 0.01;
                workspace.RestoreLayout(layout);
                stage.UpdateLayout(); PumpUi(); stage.UpdateLayout(); PumpUi();
                Check($"fretboard pane {tag}: the pane cannot go below its full-draw minimum",
                    host.ActualHeight >= panel.RequiredHeight + 2 - 0.5, $"{host.ActualHeight:0.0} vs {panel.RequiredHeight + 2:0.0}");
                if (png is { Length: > 0 }) FretPanePng(stage, png, $"min-{tag}", width, height, "instrument", workspace, 0);

                // A locked (saved) height taller than the maximum is shown at the maximum; a smaller one is kept.
                workspace.SetPanelFixedHeight("instrument", panel.MaximumHeight + 300);
                stage.UpdateLayout(); PumpUi(); stage.UpdateLayout();
                Check($"fretboard pane {tag}: a locked height beyond the maximum leaves no gap", host.ActualHeight <= panel.MaximumHeight + 2 + 0.5, $"{host.ActualHeight:0.0}");
                workspace.SetPanelFixedHeight("instrument", medium + 2);
                stage.UpdateLayout(); PumpUi(); stage.UpdateLayout();
                Check($"fretboard pane {tag}: a locked height inside the limits is kept", Math.Abs(host.ActualHeight - (medium + 2)) < 1.5, $"{host.ActualHeight:0.0} vs {medium + 2:0.0}");
            }
        }

        // Other instruments share the host: the keyboard stops at its tallest keys, the drum map has no maximum.
        Check("fretboard pane: a keyboard stops at its tallest keys", InstrumentPanel.MaximumPaneHeight(
            new InstrumentVisualState { Kind = InstrumentKind.Keyboard }, 1500, 120) == TabForge.Visualization.KeyboardPaneSizing.MaxKeyHeight);
        Check("fretboard pane: a drum map has no maximum height", double.IsPositiveInfinity(InstrumentPanel.MaximumPaneHeight(
            new InstrumentVisualState { Kind = InstrumentKind.Drums }, 1500, 120)));
        foreach (var n in new[] { 4, 7, 8 })
        {
            var natural = InstrumentPanel.RequiredHeightFor(null, 900);
            var max = InstrumentPanel.MaximumPaneHeight(null, 1500, natural);
            Check($"fretboard pane: maximum is above the natural height ({n} strings model)", max >= natural);
        }
    }

    private static DockNodeState? FindInstrumentSplit(DockNodeState? node)
    {
        if (node is null) return null;
        if (node.Kind == "split" && node.Orientation == "Vertical" && node.First is { Kind: "tabs" } f && f.Panels.Contains("instrument")) return node;
        return FindInstrumentSplit(node.First) ?? FindInstrumentSplit(node.Second);
    }

    private static void FretPanePng(FitStage stage, string folder, string name, double width, double height, string id, DockWorkspace workspace, double fixedHeight)
    {
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        stage.Render(bitmap);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, $"fretboard-pane-{name.Replace(' ', '_')}.png"));
        encoder.Save(file);
    }
}

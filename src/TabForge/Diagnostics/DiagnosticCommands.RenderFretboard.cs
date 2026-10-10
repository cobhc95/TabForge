using System.Globalization;
using System.Windows;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Diagnostics;

// Owns: the `--render-fretboard` command: reads its size and key=value options (track, now, scale, strength, style, colour, spacing, view) and writes the instrument panel to a PNG off-screen.
// Does not own: the instrument panel itself (Views/InstrumentPanel*.cs).
// Tests: no named test.

internal static partial class DiagnosticCommands
{
    /// <summary>
    /// `--render-fretboard &lt;song&gt; &lt;out.png&gt; [width height] [key=value ...]`: the instrument panel (fretboard or keyboard) off-screen.
    /// Options: track=N, now=ms (playhead, for markers), scale="E Natural Minor", strength=10..150, style=Shaded|Circles|Rings,
    /// colour=Blue, spacing=Compact|Natural|Wide, view=keyboard.
    /// </summary>
    private static int RunRenderFretboard(string[] args)
    {
        if (args.Length < 3) return Usage("--render-fretboard <song> <out.png> [width height] [track=N] [now=ms] [scale=\"E Natural Minor\"] [strength=100] [style=Shaded] [colour=Blue] [spacing=Natural] [view=keyboard]");
        return Guard("Fretboard render", () =>
        {
            var outPath = FilePathPolicy.OutputFile(args[2], "fretboard render", ".png");
            var project = LoadAny(args[1]);
            var positional = args.Skip(3).Where(a => !a.Contains('=')).ToList();
            var options = args.Skip(3).Where(a => a.Contains('='))
                .Select(a => a.Split('=', 2)).ToDictionary(p => p[0].ToLowerInvariant(), p => p[1].Trim('"'));
            var width = positional.Count > 0 && int.TryParse(positional[0], out var w) ? Math.Clamp(w, 200, 4000) : 1100;
            var height = positional.Count > 1 && int.TryParse(positional[1], out var h) ? Math.Clamp(h, 60, 2000) : 230;
            var trackIndex = options.TryGetValue("track", out var t) && int.TryParse(t, out var ti) ? Math.Clamp(ti, 0, Math.Max(0, project.Tracks.Count - 1)) : 0;
            var now = options.TryGetValue("now", out var n) && double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var nowMs) ? nowMs : 0;
            options.TryGetValue("scale", out var scale);

            var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
            var state = InstrumentVisualizer.Build(project, project.Tracks[trackIndex], timeline, now, true, false, 6, false, false, scale);
            if (options.TryGetValue("view", out var view) && view.Equals("keyboard", StringComparison.OrdinalIgnoreCase)) state.Kind = InstrumentKind.Keyboard;
            if (options.TryGetValue("style", out var style)) state.ScaleStyle = ScaleHighlightStyles.All.FirstOrDefault(s => s.Equals(style, StringComparison.OrdinalIgnoreCase)) ?? state.ScaleStyle;
            var colour = options.TryGetValue("colour", out var c) ? c : "Blue";
            state.ScaleColour = ThemeService.ScaleHighlightColour(colour, VisualTheme.IsLight);
            if (options.TryGetValue("strength", out var st) && int.TryParse(st, out var pct)) state.ScaleStrength = ScaleHighlightStyles.StrengthFactor(pct);
            if (options.TryGetValue("spacing", out var sp)) state.StringSpacing = FretStringSpacings.Factor(sp);
            if (options.TryGetValue("tagtop", out var tagTop) && tagTop == "1")
            {
                // A tagged sounding note on the top string (string 1), to look at the beside-the-marker tag placement.
                var extra = state.Notes.ToList();
                extra.Add(new VisualNote { StringIndex = 0, Fret = 7, Midi = state.Tuning[0] + 7, Role = VisualRole.Current, Held = true, Technique = "TAP" });
                state.Notes = extra;
            }
            var d = new EditingSettings();
            state.MarkerColour = ThemeService.FretMarkerColour(d.FretMarkerColour);
            state.MarkerBrightness = FretMarkerLevels.Level(d.FretMarkerBrightness);
            state.NumberScale = FretNumberSizes.Scale(d.FretNumberSize);

            var panel = new Views.InstrumentPanel { Width = width, Height = height };
            panel.SetState(state);
            panel.Measure(new Size(width, height));
            panel.Arrange(new Rect(0, 0, width, height));
            panel.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            FilePathPolicy.WriteAtomically(outPath, encoder.Save);
            Console.WriteLine($"Wrote {outPath}");
            return Ok;
        });
    }
}

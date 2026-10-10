using System.Windows;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Owns: the `--render-gp-export-dialog` command: builds a sample lossy-export report and writes the export dialog to a PNG off-screen, in dark or light.
// Does not own: the dialog (Views/GpExportPreflightDialog) or the export itself.
// Tests: no named test.

internal static partial class DiagnosticCommands
{
    /// <summary>`--render-gp-export-dialog &lt;out.png&gt; [light] [save]`: the lossy-export dialog off-screen, with a sample report.</summary>
    private static int RunRenderGpExportDialog(string[] args)
    {
        if (args.Length < 2) return Usage("--render-gp-export-dialog <out.png> [light] [save]");
        return Guard("Render Guitar Pro export dialog", () =>
        {
            var outPath = FilePathPolicy.OutputFile(args[1], "dialog render", ".png");
            var light = args.Any(a => a.Equals("light", StringComparison.OrdinalIgnoreCase));
            var kind = args.Any(a => a.Equals("save", StringComparison.OrdinalIgnoreCase)) ? GpExportKind.Save : GpExportKind.Export;
            var appearance = new AppearanceSettings();
            ThemeService.ApplyPreset(appearance, light ? "Light" : "Dark");
            ThemeService.Apply(appearance);
            var report = new GpPreflightReport { HasNativeOnlyAudioData = true };
            report.Losses.Add(new GpLoss("Bend curve with more points than a .gp file keeps", "reduced to origin, middle and end", 3, "Lead Guitar: bars 12-14"));
            report.Losses.Add(new GpLoss("Mix-table change on a beat (volume, pan, sound)", "not written; the track keeps its starting mix", 1, "Rhythm Guitar: bar 40"));
            report.Losses.Add(new GpLoss("Reverb / chorus sends", "not written to the .gp file; they reopen at the defaults", 2, "Lead Guitar; Rhythm Guitar"));
            report.Losses.Add(new GpLoss("Fermata on some tracks only", "a .gp file stores it for every track", 1, "all tracks: bar 64"));
            var handle = Views.GpExportPreflightDialog.Build(report, kind, "Blinded.gp");
            var w = handle.Window;
            w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -20000; w.Top = -20000;
            w.ShowActivated = false; w.WindowStyle = WindowStyle.None;
            try
            {
                w.Show();
                Pump(); w.UpdateLayout(); Pump();
                var width = (int)Math.Ceiling(w.ActualWidth); var height = (int)Math.Ceiling(w.ActualHeight);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(w);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                FilePathPolicy.WriteAtomically(outPath, encoder.Save);
                Console.WriteLine($"Wrote {outPath}");
            }
            finally { w.Close(); }
            return Ok;
        });
    }
}

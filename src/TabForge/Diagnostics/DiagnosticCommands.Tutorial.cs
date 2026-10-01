using System.Globalization;
using System.IO;
using TabForge.Services;

namespace TabForge.Diagnostics;

internal static partial class DiagnosticCommands
{
    /// <summary>
    /// `--tutorial-shot &lt;tutorial folder&gt; &lt;out.png&gt; [dark|light] [chapter] [search] [WxH]`: the Help &gt; Tutorial window drawn off-screen
    /// (nothing is shown) with the chosen theme, for documentation and visual checks.
    /// </summary>
    private static int RunTutorialShot(string[] args)
    {
        if (args.Length < 3) return Usage("--tutorial-shot <tutorial folder> <out.png> [dark|light] [chapter] [search] [WxH]");
        return Guard("Tutorial picture", () =>
        {
            var outPath = FilePathPolicy.OutputFile(args[2], "tutorial window picture", ".png");
            var library = TutorialLibrary.Load(args[1]);
            var appearance = new AppearanceSettings();
            ThemeService.ApplyPreset(appearance, args.Length > 3 && args[3].Equals("light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark");
            ThemeService.Apply(appearance);
            double width = 1040, height = 720;
            if (args.Length > 6)
            {
                var parts = args[6].Split('x', 'X');
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                { width = Math.Clamp(w, 400, 3000); height = Math.Clamp(h, 300, 3000); }
            }
            Views.TutorialWindow.RenderPng(library, args.Length > 4 && args[4] != "-" ? args[4] : null, args.Length > 5 && args[5] != "-" ? args[5] : null, width, height, outPath, scale: 1);
            Console.WriteLine($"Wrote {outPath}");
            return Ok;
        });
    }

    /// <summary>`--tutorial-pdf &lt;tutorial folder&gt; &lt;out.pdf&gt;`: the Beginner's Guide PDF export, from the command line.</summary>
    private static int RunTutorialPdf(string[] args)
    {
        if (args.Length < 3) return Usage("--tutorial-pdf <tutorial folder> <out.pdf>");
        return Guard("Tutorial PDF", () =>
        {
            var outPath = FilePathPolicy.OutputFile(args[2], "tutorial PDF", ".pdf");
            var library = TutorialLibrary.Load(args[1]);
            var options = Views.TutorialPdfOptions.For(library, $"Version {AppInfo.DisplayVersion}");
            var pages = Views.TutorialPdfExporter.Export(library, outPath, options, (done, total, text) => Console.WriteLine($"  {done}/{total} {text}"));
            Console.WriteLine($"Wrote {outPath} ({pages} pages)");
            return Ok;
        });
    }
}

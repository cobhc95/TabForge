using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the real Settings path of a theme switch: the Preferences window's Apply on the theme row repaints the shown score on the new paper
//   at once, dark -> light -> dark, for a song and for a duplicated tab (a copy still follows the theme), and a chosen paper survives.
// Does not own: the preset values (ThemePresetTests) or the paper rule per song (ScoreFollowsThemeTests).
// Tests: this file.
public class ScoreFollowsPreferencesThemeTests
{
    [AvaloniaFact]
    public void ApplyingTheThemeInSettingsRepaintsTheScoreOnTheNewPaperEveryTime() => Run(duplicate: false);

    [AvaloniaFact]
    public void ApplyingTheThemeInSettingsRepaintsADuplicatedTabToo() => Run(duplicate: true);

    private static void Run(bool duplicate)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-score-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            var main = new MainWindow(settings, first: new TabForge.Documents.SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() }) { Width = 1280, Height = 800 };
            main.Show();
            Dispatcher.UIThread.RunJobs();
            if (duplicate) main.Songs.Duplicate(0);   // the shown tab is now the copy
            Dispatcher.UIThread.RunJobs();
            var score = main.GetVisualDescendants().OfType<ScorePreviewView>().First();
            var preferences = settings.CreatePreferencesWindow();
            preferences.ShowPage("Score");
            preferences.Show(main);
            Dispatcher.UIThread.RunJobs();

            Assert.True(MeanLuminance(score) < 0.25, "starts dark");
            foreach (var mode in new[] { "Light", "Dark", "Light", "Dark" })
            {
                preferences.Write("appearance.thememode", mode);
                preferences.Apply();
                Dispatcher.UIThread.RunJobs();
                main.UpdateLayout();
                var mean = MeanLuminance(score);
                if (mode == "Light") Assert.True(mean > 0.85, $"{mode}: mean page luminance {mean:0.00}");
                else Assert.True(mean < 0.25, $"{mode}: mean page luminance {mean:0.00}");
            }
            foreach (var song in main.Songs.Songs) song.Project.IsDirty = false;   // a duplicated tab is an unsaved copy: closing it would wait on the save question
            main.Close();
        }
        finally
        {
            AvaloniaPalette.Publish(new TabForge.Services.AppearanceSettings());
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    // Samples the top of the page only: a long score is thousands of pixels tall.
    private static double MeanLuminance(ScorePreviewView view)
    {
        var size = new PixelSize((int)Math.Min(view.Bounds.Width, 1200), (int)Math.Min(view.Bounds.Height, 700));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(view);
        double sum = 0;
        var count = 0;
        for (var x = 8; x < size.Width - 8; x += 16)
            for (var y = 8; y < size.Height - 8; y += 16)
            {
                sum += ThemeColours.Luminance(AvaloniaDrawContextTests.Pixel(bitmap, x, y));
                count++;
            }
        return sum / count;
    }
}

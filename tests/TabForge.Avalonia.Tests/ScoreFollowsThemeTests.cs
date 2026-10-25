using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score following the theme preset: a song on the default paper turns light with the light preset (live, no restart),
//   a song whose paper was chosen keeps it, and a headless render of the light score has a mostly light page.
// Does not own: the paper colours (ScoreSettingsBindingTests) or the View menu (MainWindowScoreThemeMenuTests).
// Tests: this file.
public class ScoreFollowsThemeTests
{
    [AvaloniaFact]
    public void ThePresetSwitchTurnsTheDefaultPaperLightAndBackButNotAChosenPaper()
    {
        var settings = new AppSettings();
        var song = MainWindowTabs.NewSong();
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), settings: settings);
        var score = new ScorePreviewView();
        _ = new MainWindowDocumentPanes(tabs, score);
        tabs.New();
        var chosen = tabs.Active;
        chosen.DarkPaper = true;   // the View menu chose the dark paper for this song
        Assert.True(song.DarkPaper);
        Assert.False(score.LightPaper);

        ThemeColours.ApplyPreset(settings.Appearance, light: true);
        tabs.ApplySettings(settings);
        Assert.False(song.DarkPaper);
        Assert.True(chosen.DarkPaper);

        tabs.Switch(0);
        Assert.True(score.LightPaper);

        ThemeColours.ApplyPreset(settings.Appearance, light: false);
        tabs.ApplySettings(settings);
        Assert.True(song.DarkPaper);
        Assert.False(score.LightPaper);   // the shown score repainted on the dark page again without a restart
    }

    [AvaloniaFact]
    public void TheEffectiveThemeTurnsTheDefaultPaperWhateverSetTheTheme()
    {
        var settings = new AppSettings();   // paper setting untouched: Dark, not overridden
        var song = MainWindowTabs.NewSong();
        var light = false;
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), settings: settings) { EffectiveThemeIsLight = () => light };
        tabs.New();
        var chosen = tabs.Active;
        chosen.DarkPaper = false;   // the View menu chose the light paper for this song
        Assert.True(song.DarkPaper);

        light = true;   // the theme changed by a route that never touched the settings (System followed the OS, a startup variant)
        tabs.RefreshDefaultPaper();
        Assert.False(song.DarkPaper);
        Assert.False(chosen.DarkPaper);
        chosen.DarkPaper = true;
        tabs.RefreshDefaultPaper();
        Assert.True(chosen.DarkPaper);   // a paper chosen in View stays pinned

        light = false;
        tabs.RefreshDefaultPaper();
        Assert.True(song.DarkPaper);

        settings.Appearance.ScorePaper = settings.Appearance.Own(nameof(settings.Appearance.ScorePaper), "Light");   // the user named a paper in Settings: it wins over the theme
        tabs.ApplySettings(settings);
        Assert.False(song.DarkPaper);
    }

    [AvaloniaFact]
    public void TheLightScoreRendersOnAMostlyLightPage()
    {
        using var theme = DialogTestKit.UseTheme(light: true);
        var view = new ScorePreviewView();
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize(1280, 800), new global::Avalonia.Vector(96, 96));
        bitmap.Render(view);
        double sum = 0;
        var count = 0;
        for (var x = 8; x < 1272; x += 16)
            for (var y = 8; y < 792; y += 16)
            {
                sum += ThemeColours.Luminance(AvaloniaDrawContextTests.Pixel(bitmap, x, y));
                count++;
            }
        var mean = sum / count;
        window.Close();
        Assert.True(mean > 0.85, $"mean page luminance {mean:0.00}");
    }
}

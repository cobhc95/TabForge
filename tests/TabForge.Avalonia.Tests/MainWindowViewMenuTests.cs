using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the check of the View menu's playing bar toggle on a score view (MainWindowViewMenu.TogglePlayingBar).</summary>
public sealed class MainWindowViewMenuTests
{
    [AvaloniaFact]
    public void PlayingBarToggleFlipsTheScoresBand()
    {
        var score = new ScorePreviewView();
        var before = score.Probe.Appearance.PlayingBarEnabled;

        MainWindowViewMenu.TogglePlayingBar(score, null);

        Assert.NotEqual(before, score.Probe.Appearance.PlayingBarEnabled);
    }
}

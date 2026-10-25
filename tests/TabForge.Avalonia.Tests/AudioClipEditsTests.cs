using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the clip mute and fade-reset edits: a mute flips, and a fade reset changes only a clip that has fades.
// Does not own: the selected-clip key guard (MainWindowClipKeysTests) and the undo step (DocumentEdits).
// Tests: this class.
public class AudioClipEditsTests
{
    [Fact]
    public void MuteFlipsAndFadeResetChangesOnlyClipsWithFades()
    {
        var clip = new AudioClip { FadeInSec = 0.5, FadeOutSec = 0.25 };
        Assert.True(AudioClipEdits.ToggleMute(clip));
        Assert.True(clip.Muted);
        Assert.True(AudioClipEdits.ResetFades(clip));
        Assert.Equal(0, clip.FadeInSec);
        Assert.Equal(0, clip.FadeOutSec);
        Assert.False(AudioClipEdits.ResetFades(clip));
    }
}

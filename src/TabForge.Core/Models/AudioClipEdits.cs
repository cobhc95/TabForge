namespace TabForge.Models;

// Owns: the single-clip edits that need no UI: flipping a clip's mute and clearing its fades.
//   Each returns whether the clip changed, so the caller takes an undo step only for a real change.
// Does not own: the take-group edits (TakeGroupEdits), the undo step (DocumentEdits), the command keys (MainWindowClipEditKeys), the status text.
// Tests: TabForge.Avalonia.Tests.AudioClipEditsTests.
public static class AudioClipEdits
{
    /// <summary>Flips the clip's mute; always a change.</summary>
    public static bool ToggleMute(AudioClip clip)
    {
        clip.Muted = !clip.Muted;
        return true;
    }

    /// <summary>Removes the clip's fade-in and fade-out; false when the clip has no fades.</summary>
    public static bool ResetFades(AudioClip clip)
    {
        if (clip.FadeInSec == 0 && clip.FadeOutSec == 0) return false;
        clip.FadeInSec = 0;
        clip.FadeOutSec = 0;
        return true;
    }
}

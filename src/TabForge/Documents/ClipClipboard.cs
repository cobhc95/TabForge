using TabForge.Models;

namespace TabForge.Documents;

/// <summary>
/// The audio or MIDI clip copied on a clip lane. One per process, like the score clipboard: a clip copied in one window pastes into the song
/// of another window. It holds a private copy, so later edits of the source clip do not change what pastes. UI thread only.
/// </summary>
public static class ClipClipboard
{
    private static AudioClip? _clip;

    public static bool HasClip => _clip is not null;

    public static string? Name => _clip?.Name;

    /// <summary>Remembers a copy of <paramref name="clip"/>.</summary>
    public static void Copy(AudioClip clip) => _clip = clip.Clone();

    /// <summary>A new copy of the remembered clip for the caller to place, or null when nothing was copied.</summary>
    public static AudioClip? Clone() => _clip?.Clone();
}

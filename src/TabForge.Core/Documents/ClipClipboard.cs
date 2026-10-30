using System.Collections.Generic;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Documents;

// Owns: the process-wide copied audio/MIDI clip (private copy, cross-window paste), including the whole member list of a take group.
// Does not own: the score clipboard or the clip lane UI.
// Tests: clip lane tests.

/// <summary>
/// The app-wide clip clipboard: a stable facade over the one <see cref="ClipClipboardService"/> of the process, so a clip copied
/// in one window pastes into the song of another window. A test may swap the service with <see cref="Use"/>.
/// </summary>
public static class ClipClipboard
{
    private static readonly ServiceSlot<ClipClipboardService> Slot = new(new ClipClipboardService());

    /// <summary>The service the facade currently delegates to.</summary>
    internal static ClipClipboardService Current => Slot.Current;

    /// <summary>Makes <paramref name="service"/> the clipboard until the returned scope is disposed.</summary>
    internal static IDisposable Use(ClipClipboardService service) => Slot.Use(service);

    public static bool HasClip => Current.HasClip;

    public static string? Name => Current.Name;

    /// <summary>True when the copied entry is a loop-record take group (even one with a single remaining member).</summary>
    public static bool IsGroup => Current.IsGroup;

    /// <summary>Remembers a copy of <paramref name="clip"/>.</summary>
    public static void Copy(AudioClip clip) => Current.Copy(clip);

    /// <summary>True when several selected clips were copied together (not a take group).</summary>
    public static bool IsMany => Current.IsMany;

    /// <summary>
    /// Remembers copies of several selected clips: starts relative to the earliest, track rows relative to the first row, lanes kept.
    /// A copy leaves its take group (the copies would otherwise join the originals' group).
    /// </summary>
    public static void CopyMany(IReadOnlyList<(int Row, AudioClip Clip)> items) => Current.CopyMany(items);

    /// <summary>New copies of the clips copied together, each with its track row offset from the first copied row.</summary>
    public static List<(int RowOffset, AudioClip Clip)> CloneAllWithRows() => Current.CloneAllWithRows();

    /// <summary>
    /// Remembers the clip and every member of its take group, each start relative to the earliest member. An ordinary clip is copied alone.
    /// </summary>
    public static void Copy(TrackModel track, AudioClip clip) => Current.Copy(track, clip);

    /// <summary>A new copy of the remembered clip for the caller to place, or null when nothing was copied.</summary>
    public static AudioClip? Clone() => Current.Clone();

    /// <summary>New copies of the whole remembered entry: one clip, or every remaining member of a take group.</summary>
    public static List<AudioClip> CloneAll() => Current.CloneAll();
}

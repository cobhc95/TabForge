using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Documents;

// Owns: the process-wide copied audio/MIDI clip (private copy, cross-window paste), including the whole member list of a take group.
// Does not own: the score clipboard or the clip lane UI.
// Tests: clip lane tests.

/// <summary>
/// The audio or MIDI clip copied on a clip lane. One per process, like the score clipboard: a clip copied in one window pastes into the song
/// of another window. It holds a private copy, so later edits of the source clip do not change what pastes; a take group is kept as its
/// whole member list with starts relative to the earliest member. UI thread only.
/// </summary>
public static class ClipClipboard
{
    // Each copied clip with its track row minus the first copied row (all 0 for one clip or one take group): one static, replaced whole by each copy.
    private static List<(int RowOffset, AudioClip Clip)> _items = new();

    public static bool HasClip => _items.Count > 0;

    public static string? Name => _items.Count > 0 ? _items[0].Clip.Name : null;

    /// <summary>True when the copied entry is a loop-record take group (even one with a single remaining member).</summary>
    public static bool IsGroup => _items.Count > 0 && TakeGroups.IsGrouped(_items[0].Clip);

    /// <summary>Remembers a copy of <paramref name="clip"/>.</summary>
    public static void Copy(AudioClip clip)
    {
        _items = new List<(int, AudioClip)> { (0, clip.Clone()) };
    }

    /// <summary>True when several selected clips were copied together (not a take group).</summary>
    public static bool IsMany => _items.Count > 1 && !IsGroup;

    /// <summary>
    /// Remembers copies of several selected clips: starts relative to the earliest, track rows relative to the first row, lanes kept.
    /// A copy leaves its take group (the copies would otherwise join the originals' group).
    /// </summary>
    public static void CopyMany(IReadOnlyList<(int Row, AudioClip Clip)> items)
    {
        var earliest = items.Min(item => item.Clip.StartSec);
        var firstRow = items.Min(item => item.Row);
        _items = items.Select(item =>
        {
            var copy = item.Clip.Clone();
            copy.StartSec -= earliest;
            copy.TakeGroupId = null;
            copy.TakeGroupPass = 0;
            return (item.Row - firstRow, copy);
        }).ToList();
    }

    /// <summary>New copies of the clips copied together, each with its track row offset from the first copied row.</summary>
    public static List<(int RowOffset, AudioClip Clip)> CloneAllWithRows() => _items.Select(item => (item.RowOffset, item.Clip.Clone())).ToList();

    /// <summary>
    /// Remembers the clip and every member of its take group, each start relative to the earliest member. An ordinary clip is copied alone.
    /// </summary>
    public static void Copy(TrackModel track, AudioClip clip)
    {
        if (!TakeGroups.IsGrouped(clip))
        {
            Copy(clip);
            return;
        }
        var members = TakeGroups.Members(track, clip);
        var earliest = members.Min(member => member.StartSec);
        _items = members.Select(member =>
        {
            var copy = member.Clone();
            copy.StartSec = member.StartSec - earliest;
            copy.Lane = 0;
            return (0, copy);
        }).ToList();
    }

    /// <summary>A new copy of the remembered clip for the caller to place, or null when nothing was copied.</summary>
    public static AudioClip? Clone() => _items.Count == 0 ? null : _items[0].Clip.Clone();

    /// <summary>New copies of the whole remembered entry: one clip, or every remaining member of a take group.</summary>
    public static List<AudioClip> CloneAll() => _items.Select(item => item.Clip.Clone()).ToList();
}

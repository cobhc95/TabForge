using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Documents;

// Owns: the copied audio/MIDI clip state for one clipboard service (private copy, cross-window paste), including the whole member list of a take group.
// Does not own: the score clipboard or the clip lane UI.
// Tests: clip lane tests.

/// <summary>
/// One audio or MIDI clip clipboard. The app uses the process-wide <see cref="ClipClipboard"/> facade; a test or an isolated
/// window may use its own service, whose copied entry no other service sees. It holds a private copy, so later edits of the
/// source clip do not change what pastes; a take group is kept as its whole member list with starts relative to the earliest
/// member. UI thread only.
/// </summary>
public sealed class ClipClipboardService
{
    // Each copied clip with its track row minus the first copied row (all 0 for one clip or one take group): replaced whole by each copy.
    private List<(int RowOffset, AudioClip Clip)> _items = new();

    public bool HasClip => _items.Count > 0;

    public string? Name => _items.Count > 0 ? _items[0].Clip.Name : null;

    /// <summary>True when the copied entry is a loop-record take group (even one with a single remaining member).</summary>
    public bool IsGroup => _items.Count > 0 && TakeGroups.IsGrouped(_items[0].Clip);

    /// <summary>Remembers a copy of <paramref name="clip"/>.</summary>
    public void Copy(AudioClip clip)
    {
        _items = new List<(int, AudioClip)> { (0, clip.Clone()) };
    }

    /// <summary>True when several selected clips were copied together (not a take group).</summary>
    public bool IsMany => _items.Count > 1 && !IsGroup;

    /// <summary>
    /// Remembers copies of several selected clips: starts relative to the earliest, track rows relative to the first row, lanes kept.
    /// A copy leaves its take group (the copies would otherwise join the originals' group).
    /// </summary>
    public void CopyMany(IReadOnlyList<(int Row, AudioClip Clip)> items)
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
    public List<(int RowOffset, AudioClip Clip)> CloneAllWithRows() => _items.Select(item => (item.RowOffset, item.Clip.Clone())).ToList();

    /// <summary>
    /// Remembers the clip and every member of its take group, each start relative to the earliest member. An ordinary clip is copied alone.
    /// </summary>
    public void Copy(TrackModel track, AudioClip clip)
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
    public AudioClip? Clone() => _items.Count == 0 ? null : _items[0].Clip.Clone();

    /// <summary>New copies of the whole remembered entry: one clip, or every remaining member of a take group.</summary>
    public List<AudioClip> CloneAll() => _items.Select(item => item.Clip.Clone()).ToList();
}

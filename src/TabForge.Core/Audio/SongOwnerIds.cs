using TabForge.Audio.Contracts;

namespace TabForge.Audio;

// Owns: the engine song-transport owner-id table (weak refs, last positions, id-0 sharing).
// Does not own: the engine transport or the documents that hold the ids.
// Tests: AudioSmokeTests.

/// <summary>
/// The engine keeps one song transport per owner id. This table hands each open document the lowest free id and remembers the last
/// position sent per id, so it can be sent again after an engine restart. Documents are held weakly: a document that is dropped without
/// being released frees its id (<see cref="Sweep"/> reports it so the engine's transport can be stopped). When every id is taken, a new
/// document shares id 0: it is remembered as sharing, so its late messages never stop the song that really owns id 0. Thread-safe.
/// </summary>
internal sealed class SongOwnerIds
{
    private readonly object _gate = new();
    private readonly WeakReference<object>?[] _owners = new WeakReference<object>?[SongOwners.Max];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _sharing = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _sharingAnnounced = new();
    private readonly (bool Playing, double SongSec, long Stamp, double Speed)?[] _positions = new (bool, double, long, double)?[SongOwners.Max];

    private int Find(object owner)
    {
        for (var i = 0; i < _owners.Length; i++)
            if (_owners[i] is { } weak && weak.TryGetTarget(out var target) && ReferenceEquals(target, owner)) return i;
        return -1;
    }

    /// <summary>The document's id, allocated on first use; 0 (shared) when every id is taken.</summary>
    public int IdOf(object owner)
    {
        lock (_gate)
        {
            var id = Find(owner);
            if (id >= 0) return id;
            for (var i = 0; i < _owners.Length; i++)
                if (_owners[i] is not { } weak || !weak.TryGetTarget(out _)) { _owners[i] = new WeakReference<object>(owner); _positions[i] = null; return i; }
            _sharing.AddOrUpdate(owner, owner);
            return 0;
        }
    }

    /// <summary>True for a document that got the shared id 0 because the table was full.</summary>
    public bool IsSharing(object owner)
    {
        lock (_gate) return _sharing.TryGetValue(owner, out _);
    }

    /// <summary>True once per sharing document (the first call): the "all transports are taken" notice is logged one time, not on every call.</summary>
    public bool TakeSharingNotice(object owner)
    {
        lock (_gate)
        {
            if (!_sharing.TryGetValue(owner, out _) || _sharingAnnounced.TryGetValue(owner, out _)) return false;
            _sharingAnnounced.Add(owner, owner);
            return true;
        }
    }

    /// <summary>Frees the ids of documents that were dropped without being released and returns them (the engine's transport for each must be stopped).</summary>
    public int[] Sweep()
    {
        lock (_gate)
        {
            List<int>? dead = null;
            for (var i = 0; i < _owners.Length; i++)
                if (_owners[i] is { } weak && !weak.TryGetTarget(out _)) { (dead ??= new()).Add(i); _owners[i] = null; _positions[i] = null; }
            return dead?.ToArray() ?? Array.Empty<int>();
        }
    }

    /// <summary>The document's id, or -1 when it has none (never allocated or released); 0 for a document that shares id 0.</summary>
    public int Existing(object owner)
    {
        lock (_gate)
        {
            var id = Find(owner);
            if (id >= 0) return id;
            return _sharing.TryGetValue(owner, out _) ? 0 : -1;
        }
    }

    /// <summary>Frees the document's id; false when it had none of its own.</summary>
    public bool Release(object owner, out int id)
    {
        lock (_gate)
        {
            _sharing.Remove(owner);
            _sharingAnnounced.Remove(owner);
            id = Find(owner);
            if (id < 0) return false;
            _owners[id] = null; _positions[id] = null;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) { Array.Clear(_owners); Array.Clear(_positions); }
    }

    public void Record(int id, bool playing, double songSec, long stamp, double speed)
    {
        if (id is < 0 or >= SongOwners.Max) return;
        lock (_gate) _positions[id] = (playing, songSec, stamp, speed);
    }

    public (bool Playing, double SongSec)? Last(int id)
    {
        if (id is < 0 or >= SongOwners.Max) return null;
        lock (_gate) return _positions[id] is { } p ? (p.Playing, p.SongSec) : null;
    }

    /// <summary>Every id's last position sent.</summary>
    public (int Id, bool Playing, double SongSec, long Stamp, double Speed)[] Positions()
    {
        lock (_gate)
        {
            var list = new List<(int, bool, double, long, double)>();
            for (var i = 0; i < _positions.Length; i++) if (_positions[i] is { } p) list.Add((i, p.Playing, p.SongSec, p.Stamp, p.Speed));
            return list.ToArray();
        }
    }
}

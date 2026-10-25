using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Autosave;

// Owns: the autosave state of each open song (its recovery copy file, the fingerprint it was written with, a recovered copy it was opened from),
//     and the rules that retire a song's copies when the song is closed, saved or missing from every window.
// Does not own: the copy writes (AutosaveRunner), the timer and notices (the window's autosave controller), the file naming (AutosaveService).
// Tests: MainWindowAutosaveTests.
// Keys: a song (by reference) to its state; one entry per open song, so the ceiling is the number of open songs. Thread: one registry per process;
//     its dictionary is guarded by a lock, and each state's fields by its own Gate (the runner's worker reads them off the UI thread).

/// <summary>One song's autosave state. Its fields are read and written under <see cref="Gate"/>.</summary>
internal sealed class AutosaveState
{
    public readonly Guid Id = Guid.NewGuid();
    public readonly object Gate = new();
    /// <summary>The copy this process last wrote (null: none yet).</summary>
    public string? Path;
    /// <summary>The content fingerprint the copy at <see cref="Path"/> holds (null: the copy is stale or none exists).</summary>
    public string? LastFingerprint;
    /// <summary>A recovered copy (from a crashed session) the song was opened from: kept until this session has its own copy.</summary>
    public string? Adopted;
    public bool Writing, Retired;
    public DateTime? MissingSince;

    /// <summary>Deletes the copies now, or (a write in flight) lets the writer do it when it finishes.</summary>
    public void Retire()
    {
        lock (Gate)
        {
            Retired = true;
            if (!Writing) DeleteFilesLocked();
        }
    }

    /// <summary>The song was saved (or is unchanged since opening): no copy is needed any more.</summary>
    public void MarkSaved()
    {
        lock (Gate)
        {
            if (Writing) return;   // the pass re-checks after the write
            DeleteFilesLocked();
            LastFingerprint = null;
        }
    }

    /// <summary>Deletes this song's copy and its recovered copy; the caller holds <see cref="Gate"/>.</summary>
    public void DeleteFilesLocked()
    {
        AutosaveService.Delete(Path);
        AutosaveService.Delete(Adopted);
        Path = null;
        Adopted = null;
    }
}

/// <summary>The songs that have autosave state, across every window of the process, and the start-up recovery offer's once-only claim.</summary>
public sealed class AutosaveRegistry
{
    /// <summary>How long a song may be missing from every window before its copies are retired (a tab moved between windows is briefly in neither).</summary>
    public static readonly TimeSpan MissingGrace = TimeSpan.FromSeconds(2);

    private readonly Dictionary<SongDocument, AutosaveState> _states = new();
    private readonly object _sync = new();
    private bool _recoveryOffered;

    internal AutosaveState For(SongDocument song)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(song, out var state)) _states[song] = state = new AutosaveState();
            return state;
        }
    }

    /// <summary>The song was opened from a recovered copy: keep that copy until the session has a durable copy of its own.</summary>
    public void Adopt(SongDocument song, string recoveredFile)
    {
        var state = For(song);
        lock (state.Gate) state.Adopted = recoveredFile;
    }

    /// <summary>The songs are closed for good (window closing): their copies go, safely even while a write is in flight.</summary>
    public void Retire(IEnumerable<SongDocument> songs)
    {
        foreach (var song in songs)
        {
            AutosaveState? state;
            lock (_sync)
            {
                if (!_states.Remove(song, out state)) continue;
            }
            state.Retire();
        }
    }

    /// <summary>
    /// Retires the state of every song that is in no window any more. A song is retired only after it has been missing for
    /// <see cref="MissingGrace"/>. Returns how many songs are missing but still within the grace period (the caller looks again soon).
    /// </summary>
    public int Reconcile(IReadOnlyCollection<SongDocument> liveEverywhere, DateTime now)
    {
        var pending = 0;
        List<SongDocument>? gone = null;
        lock (_sync)
        {
            foreach (var (song, state) in _states)
            {
                if (liveEverywhere.Contains(song)) { state.MissingSince = null; continue; }
                state.MissingSince ??= now;
                if (now - state.MissingSince.Value >= MissingGrace) (gone ??= new()).Add(song);
                else pending++;
            }
        }
        if (gone is not null) Retire(gone);
        return pending;
    }

    /// <summary>
    /// Retires the state of every song that is in no window, at once and without the grace period: a window that closes has no tab in mid-move, so a
    /// song discarded from its tab leaves no copy behind for the next start to offer.
    /// </summary>
    public void RetireMissing(IReadOnlyCollection<SongDocument> liveEverywhere)
    {
        List<SongDocument> gone;
        lock (_sync) gone = _states.Keys.Where(song => !liveEverywhere.Contains(song)).ToList();
        Retire(gone);
    }

    /// <summary>True for the first caller only: the start-up recovery offer is made once per process.</summary>
    public bool TryClaimRecoveryOffer()
    {
        lock (_sync)
        {
            if (_recoveryOffered) return false;
            _recoveryOffered = true;
            return true;
        }
    }
}

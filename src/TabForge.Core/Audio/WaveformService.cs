using System.Diagnostics;
using System.IO;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: the waveform outline service (an instance: its slots, queue, worker and Ready event belong to it, not to the process).
// Does not own: the decoder (NAudioMediaSource) or the clip draw path.
// Tests: AudioSmokeTests, TimelineWaveStatusTests.

/// <summary>
/// Waveform outlines for drawing audio clips: the peak level of every 10 ms of a file, read in the background by one worker
/// (a 5-minute file is ~30,000 numbers). Drawing never waits: until a file is read its clip shows a plain block, then the
/// timeline redraws. Every path goes through <see cref="MediaAccess"/> first (device paths refused, network and removable
/// locations only after approval); decoding is bounded (file size, duration, samples, time, queue length) and cancellable;
/// the cache keeps a memory budget (least recently drawn outlines go first) and notices a file replaced at the same path.
/// </summary>
public sealed partial class WaveformService : IDisposable
{
    public const double SecondsPerPeak = 0.01;
    public const long MaxFileBytes = 2L << 30;      // 2 GiB
    public const double MaxSeconds = 2 * 3600;      // 2 h
    public const int MaxPending = 64;
    private static readonly TimeSpan MaxDecodeTime = TimeSpan.FromMinutes(5);

    /// <summary>Peak data kept at most (bytes).</summary>
    internal long BudgetBytes = 256L << 20;
    /// <summary>How often a drawn file is looked at again to see whether it was replaced (ms).</summary>
    internal long RecheckMs = 2000;
    /// <summary>Test seam: replaces the decoder.</summary>
    internal Func<string, IMediaSource>? OpenOverride;
    /// <summary>Test seam: limits for the output (peak count) are derived from <see cref="MaxSeconds"/>; tests lower it.</summary>
    internal double MaxSecondsOverride = MaxSeconds;
    internal long MaxFileBytesOverride = MaxFileBytes;

    /// <summary>Test seam: replaces the file-size / last-write probe (<c>null</c>: the file does not exist); counts probes.</summary>
    internal Func<string, (long Size, long Ticks)?>? StatOverride { get; set; }

    // One slot per (document, file): the request, its permission and its state belong to the document that asked. Decoded peaks are shared
    // between slots by canonical file identity (a second document asking for the same, approved file reuses them) but only after that
    // document's own permission was checked; the permission is checked again on every read of a slot, so a revoked approval or a replaced
    // document never keeps granting access through data another document decoded.
    private sealed class Slot
    {
        public required string Key;
        public required string Raw;
        public required MediaContext Context;
        public required int Revision;
        public string? Path;   // canonical file the request resolved to (once allowed)
        public MediaVerdict? Verdict;   // how the worker classified it: later permission checks reuse it (no file-system call on the draw path or under the lock)
        public float[]? Peaks;
        public WaveState State = WaveState.Loading;
        public string? Message;
        public long Size = -1, Ticks;
        public long LastUsed, LastChecked;
        public bool Queued;
        public CancellationTokenSource Cts = new();
    }

    private readonly object Gate = new();
    private readonly Dictionary<string, Slot> Slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Slot> Queue = new();
    private bool _workerRunning;
    private int _decodeCount;
    private int _active;   // jobs taken off the queue and not finished (tests wait for idle)
    /// <summary>Terminal (Failed / NeedsApproval) slots kept at most (LRU by LastUsed): entries with no peaks are still bounded.</summary>
    internal const int MaxTerminalSlots = 256;

    /// <summary>A file's outline finished reading or changed state (raised on a background thread).</summary>
    public event Action<string>? Ready;

    public WaveformService()
    {
        // An approval changed (or a song's scope moved): what waited for it is asked again, outlines no longer allowed are dropped, and queued work
        // is checked again by the worker before it opens anything. Calmed: one lock pass collects both sets, permission re-evaluation runs
        // outside the lock, and redraw notifications are de-duplicated by file so a burst of Changed events cannot storm the timelines.
        MediaAccess.Changed += OnMediaAccessChanged;
    }

    /// <summary>Stops listening to approval changes and stops all reading; the instance is not used afterwards.</summary>
    public void Dispose()
    {
        MediaAccess.Changed -= OnMediaAccessChanged;
        ClearAll();
    }

    private void OnMediaAccessChanged()
    {
        List<(Slot Slot, MediaVerdict Verdict, MediaContext Context)> ready;
        List<string> raws;
        lock (Gate)
        {
            var hit = Slots.Values.Where(s => s.State is WaveState.NeedsApproval or WaveState.Failed).ToList();
            raws = hit.Select(s => s.Raw).Distinct().ToList();
            foreach (var s in hit) RemoveLocked(s);
            ready = Slots.Values.Where(x => x.State == WaveState.Ready && x.Verdict is not null)
                .Select(x => (x, x.Verdict!, x.Context)).ToList();
        }
        // Re-evaluate outside the lock: Decide is memory-only but must not hold Gate across every slot.
        var revoked = new List<Slot>();
        foreach (var (slot, verdict, context) in ready)
        {
            // The document may have closed while we were outside the lock: Decide on its last verdict is still safe (memory-only).
            if (!MediaAccess.Decide(verdict, context).Allowed) revoked.Add(slot);
        }
        List<string> revokedRaws = new();
        if (revoked.Count > 0)
        {
            lock (Gate)
            {
                foreach (var s in revoked)
                {
                    // Still the same slot and still Ready: downgrade to approval; otherwise another thread already handled it.
                    if (Slots.TryGetValue(s.Key, out var current) && ReferenceEquals(current, s) && s.State == WaveState.Ready)
                    {
                        s.State = WaveState.NeedsApproval; s.Message = "not loaded (approve to load)"; s.Peaks = null;
                        revokedRaws.Add(s.Raw);
                    }
                }
                EnforceTerminalCapLocked();
            }
        }
        foreach (var raw in raws.Concat(revokedRaws).Distinct()) RaiseReady(raw);   // timelines ask again
    }

    /// <summary>Files decoded so far (self-test: a blocked path must never get here).</summary>
    internal int DecodeCount => Volatile.Read(ref _decodeCount);
    internal int CachedCount { get { lock (Gate) return Slots.Count; } }
    internal long CachedBytes { get { lock (Gate) return Slots.Values.Sum(s => (long)(s.Peaks?.Length ?? 0) * 4); } }

    private string KeyOf(string file, MediaContext context)
    {
        var scope = context.ScopeKey + "\0";
        if (Path.IsPathFullyQualified(file)) return scope + file;
        return scope + (context.BaseDirectory ?? "") + "\0" + file;
    }

    /// <summary>The outline of <paramref name="file"/> for this document, or null while it is not (or may not be) read; starts reading when needed.</summary>
    public float[]? Get(string file, MediaContext context)
    {
        var s = Touch(file, context);
        return s.State == WaveState.Ready ? s.Peaks : null;
    }

    /// <summary>Why a clip has no outline: still loading, waiting for approval, or an error.</summary>
    public WaveStatus StatusOf(string file, MediaContext context)
    {
        var s = Touch(file, context);
        return new WaveStatus(s.State, s.Message);
    }

    private Slot Touch(string file, MediaContext context)
    {
        var key = KeyOf(file, context);
        var now = Environment.TickCount64;
        // A closed document asks for nothing: no slot, no queued work, no file touched.
        if (context.IsClosed)
            return new Slot { Key = key, Raw = file, Context = context, Revision = context.Revision, LastUsed = now, LastChecked = now, State = WaveState.Failed, Message = "the song is closed" };
        lock (Gate)
        {
            if (Slots.TryGetValue(key, out var old) && old.Revision != context.Revision)
            {
                // The song's path or source folder changed since this was asked (Save As, rebase): that request is over.
                RemoveLocked(old);
            }
            if (!Slots.TryGetValue(key, out var s))
            {
                s = new Slot { Key = key, Raw = file, Context = context, Revision = context.Revision, LastUsed = now, LastChecked = now };
                if (Queue.Count >= MaxPending) return s;   // not stored: asked again on the next draw
                Slots[key] = s;
                Enqueue(s);
                return s;
            }
            s.LastUsed = now;
            // Permission is this document's, every time: data a slot already holds is only handed out while its own approval still stands.
            // Memory only: the classification (a link may lead to an unreachable share) was done by the worker; the draw path never touches the file system.
            if (s.State == WaveState.Ready && s.Verdict is { } known && MediaAccess.Decide(known, context) is { Allowed: false } denied)
            {
                s.State = denied.State == MediaAccessState.Refused ? WaveState.Failed : WaveState.NeedsApproval;
                s.Message = denied.State == MediaAccessState.Refused ? denied.Message : "not loaded (approve to load)";
                s.Peaks = null;
            }
            if (!s.Queued && s.State != WaveState.Loading && now - s.LastChecked > RecheckMs) { s.LastChecked = now; Enqueue(s); }
            return s;
        }
    }

    private void Enqueue(Slot s)
    {
        if (s.Queued) return;
        s.Queued = true;
        Queue.AddLast(s);
        if (!_workerRunning) { _workerRunning = true; _ = Task.Run(Work); }
        Monitor.PulseAll(Gate);
    }

    private void Work()
    {
        while (true)
        {
            Slot s;
            lock (Gate)
            {
                while (Queue.Count == 0)
                    if (!Monitor.Wait(Gate, 3000) && Queue.Count == 0) { _workerRunning = false; return; }
                s = Queue.First!.Value;
                Queue.RemoveFirst();
                s.Queued = false;
                _active++;
            }
            bool changed;
            try { changed = Process(s); }
            catch (Exception ex) { Services.Trace.Error(Services.Trace.Import, "waveform cache: read: " + ex.Message); Fail(s, "could not be read: " + ex.GetType().Name); changed = true; }   // never takes the worker (or the app) down
            lock (Gate) _active--;
            if (changed) RaiseReady(s.Raw);
        }
    }

    private void RaiseReady(string raw) { try { Ready?.Invoke(raw); } catch (Exception ex) { Services.Trace.Error(Services.Trace.Import, "waveform cache: ready handler: " + ex.Message); } }

    private void Fail(Slot s, string message)
    {
        lock (Gate)
        {
            if (WasCancelled(s)) return;
            s.State = WaveState.Failed; s.Message = message; s.Peaks = null;
            EnforceTerminalCapLocked();
        }
    }

    /// <summary>Central remove (Gate held): unlinks the slot, cancels its decode and disposes its CTS. Tolerates double-remove.</summary>
    private void RemoveLocked(Slot s)
    {
        if (Slots.TryGetValue(s.Key, out var current) && ReferenceEquals(current, s)) Slots.Remove(s.Key);
        if (s.Queued) { Queue.Remove(s); s.Queued = false; }
        try { s.Cts.Cancel(); } catch (ObjectDisposedException) { } // swallow: the slot was already disposed
        try { s.Cts.Dispose(); } catch (ObjectDisposedException) { } // swallow: the slot was already disposed
    }

    /// <summary>True when the slot was cancelled or its CTS was already disposed by a concurrent remove (treat as cancelled).</summary>
    private bool WasCancelled(Slot s)
    {
        try { return s.Cts.IsCancellationRequested; }
        catch (ObjectDisposedException) { return true; }
    }

    private CancellationToken TokenOf(Slot s)
    {
        try { return s.Cts.Token; }
        catch (ObjectDisposedException) { return new CancellationToken(true); }
    }

    /// <summary>Drops the oldest terminal slots beyond <see cref="MaxTerminalSlots"/> (Gate held).</summary>
    private void EnforceTerminalCapLocked()
    {
        var terminal = Slots.Values.Where(x => x.State is WaveState.Failed or WaveState.NeedsApproval).OrderBy(x => x.LastUsed).ToList();
        for (var i = 0; terminal.Count - i > MaxTerminalSlots && i < terminal.Count; i++) RemoveLocked(terminal[i]);
    }

    /// <summary>Runs on the worker. Returns true when the slot's state changed (the timeline redraws).</summary>
    private bool Process(Slot s)
    {
        var token = TokenOf(s);
        if (token.IsCancellationRequested) return false;
        var context = s.Context;
        // Work asked for by a document that has closed, or whose path changed since (Save As), is dropped before anything is opened.
        if (!context.IsCurrent(s.Revision)) { DropSlot(s); return false; }
        // The permission is evaluated now, on the worker, with this document's context: an approval revoked while the request waited in the queue does not open the file.
        var decision = MediaAccess.Evaluate(s.Raw, context);
        if (decision.State == MediaAccessState.Refused) { Fail(s, decision.Message); return true; }
        if (decision.State == MediaAccessState.NeedsApproval)
        {
            lock (Gate)
            {
                var was = s.State; s.State = WaveState.NeedsApproval; s.Message = "not loaded (approve to load)"; s.Peaks = null;
                EnforceTerminalCapLocked();
                return was != WaveState.NeedsApproval;
            }
        }
        var path = decision.Verdict.FullPath;
        long size, ticks;
        try
        {
            if (StatOverride is { } stat)
            {
                if (stat(path) is not { } probed) { Fail(s, "file not found"); return true; }
                (size, ticks) = probed;
                if (size > MaxFileBytesOverride) { Fail(s, "file too large (limit 2 GiB)"); return true; }
            }
            else if (OpenOverride is null)
            {
                var info = new FileInfo(path);
                if (!info.Exists) { Fail(s, "file not found"); return true; }
                size = info.Length; ticks = info.LastWriteTimeUtc.Ticks;
                if (size > MaxFileBytesOverride) { Fail(s, "file too large (limit 2 GiB)"); return true; }
            }
            else { size = 0; ticks = 0; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Fail(s, "file not readable"); return true; } // Not logged: file state: reported as the entry's state
        // Same file as last time (size and last-write unchanged): nothing to read again. A replaced file changes either.
        float[]? shared = null;
        lock (Gate)
        {
            s.Path = path;
            s.Verdict = decision.Verdict;
            if (s.State is WaveState.Ready or WaveState.Failed && s.Size == size && s.Ticks == ticks && OpenOverride is null) return false;
            if (s.State != WaveState.Loading && s.State != WaveState.Ready) s.State = WaveState.Loading;
            // Another document already decoded this very file (same size and last write): this document was just allowed to read it, so reuse the peaks.
            if (size > 0 || StatOverride is not null)
                shared = Slots.Values.FirstOrDefault(x => !ReferenceEquals(x, s) && x.State == WaveState.Ready && x.Peaks is not null && x.Size == size && x.Ticks == ticks
                    && string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))?.Peaks;
            if (shared is not null) { s.Size = size; s.Ticks = ticks; s.State = WaveState.Ready; s.Message = null; s.Peaks = shared; return true; }
        }
        var peaks = Decode(path, token, out var error);
        if (token.IsCancellationRequested) return false;
        // The file was decoded for a request that was allowed when it started: apply the result only if the document is still the same and its
        // permission still stands (a close, Save As or revoke during the read drops the result).
        if (!context.IsCurrent(s.Revision)) { DropSlot(s); return false; }
        if (MediaAccess.Evaluate(s.Raw, context) is { Allowed: false })
        {
            lock (Gate) { s.State = WaveState.NeedsApproval; s.Message = "not loaded (approve to load)"; s.Peaks = null; EnforceTerminalCapLocked(); }
            return true;
        }
        lock (Gate)
        {
            if (WasCancelled(s)) return false;
            s.Size = size; s.Ticks = ticks;
            if (peaks is null) { s.State = WaveState.Failed; s.Message = error; s.Peaks = null; EnforceTerminalCapLocked(); }
            else { s.State = WaveState.Ready; s.Message = null; s.Peaks = peaks; Evict(s); }
        }
        return true;
    }

    private void DropSlot(Slot s)
    {
        lock (Gate) RemoveLocked(s);
    }

    /// <summary>Reads the peaks; null (with <paramref name="error"/>) when the file is refused or unreadable, or the token was cancelled.</summary>
    private float[]? Decode(string path, CancellationToken token, out string? error)
    {
        error = null;
        IMediaSource? reader = null;
        try
        {
            Interlocked.Increment(ref _decodeCount);
            reader = OpenOverride?.Invoke(path) ?? new NAudioMediaSource(path);
            var seconds = reader.TotalSeconds;
            if (!(seconds <= MaxSecondsOverride)) { error = "audio too long (limit 2 hours)"; return null; }
            var channels = Math.Max(1, reader.Channels);
            var rate = Math.Max(1, reader.SampleRate);
            var perPeak = Math.Max(1, (int)(rate * SecondsPerPeak)) * channels;
            var buffer = new float[perPeak * 64];
            var maxSamples = (long)((MaxSecondsOverride * 1.05 + 1) * rate * channels);   // a little slack: MP3 length estimates are not exact
            var peaks = new List<float>(Math.Clamp((int)(seconds / SecondsPerPeak) + 2, 16, 1_000_000));   // sized from the length: no repeated doubling of a long file's list
            var current = 0f; var inPeak = 0; long total = 0;
            var clock = Stopwatch.StartNew();
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (token.IsCancellationRequested) return null;
                total += read;
                if (total > maxSamples) { error = "audio too long (limit 2 hours)"; return null; }
                if (clock.Elapsed > MaxDecodeTime) { error = "reading took too long"; return null; }
                for (var i = 0; i < read; i++)
                {
                    current = Math.Max(current, Math.Abs(buffer[i]));
                    if (++inPeak >= perPeak) { peaks.Add(current); current = 0; inPeak = 0; }
                }
            }
            if (token.IsCancellationRequested) return null;
            if (inPeak > 0) peaks.Add(current);
            return peaks.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException or NotSupportedException) // Not logged: file state: reported as the entry's state
        {
            error = "could not be read as audio";
            return null;
        }
        finally { reader?.Dispose(); }
    }

    /// <summary>Drops the least recently drawn outlines until the peak data fits the budget (the one just read stays). Called under the lock.</summary>
    private void Evict(Slot keep)
    {
        long total = 0;
        foreach (var x in Slots.Values) total += (long)(x.Peaks?.Length ?? 0) * 4;
        if (total > BudgetBytes)
        {
            foreach (var victim in Slots.Values.Where(x => !ReferenceEquals(x, keep) && x.Peaks is not null).OrderBy(x => x.LastUsed).ToList())
            {
                total -= (long)victim.Peaks!.Length * 4;
                RemoveLocked(victim);
                if (total <= BudgetBytes) break;
            }
        }
        EnforceTerminalCapLocked();
    }

    /// <summary>Removes matching slots (their decoding is cancelled); the next draw asks again.</summary>
    private void Reset(Func<Slot, bool> match)
    {
        List<string> raws;
        lock (Gate)
        {
            var hit = Slots.Values.Where(match).ToList();
            raws = hit.Select(s => s.Raw).Distinct().ToList();
            foreach (var s in hit) RemoveLocked(s);
        }
        foreach (var raw in raws) RaiseReady(raw);   // timelines ask again
    }

    private sealed class Unsubscriber : IDisposable
    {
        private Action? _undo;
        public Unsubscriber(Action undo) { _undo = undo; }
        public void Dispose() { Interlocked.Exchange(ref _undo, null)?.Invoke(); }
    }

    /// <summary>
    /// Length of an audio file in seconds (0 when it cannot be read or is refused). <paramref name="userPicked"/>: the user just
    /// chose this file, so a network or removable location is fine (the caller approves its folder); device paths never are.
    /// </summary>
    public double LengthOf(string file, MediaContext context, bool userPicked = false)
    {
        try
        {
            var v = MediaPathPolicy.Classify(file, context.BaseDirectory);
            if (v.Refused) return 0;
            if (!userPicked && MediaAccess.Evaluate(file, context).State != MediaAccessState.Allowed) return 0;
            if (StatOverride is { } stat)
            {
                if (stat(v.FullPath) is not { } probed || probed.Size > MaxFileBytesOverride) return 0;
            }
            else if (OpenOverride is null)
            {
                var info = new FileInfo(v.FullPath);
                if (!info.Exists || info.Length > MaxFileBytesOverride) return 0;
            }
            using var reader = OpenOverride?.Invoke(v.FullPath) ?? new NAudioMediaSource(v.FullPath);
            var seconds = reader.TotalSeconds;
            return seconds > 0 && seconds <= MaxSecondsOverride ? seconds : 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException or NotSupportedException) { return 0; } // Not logged: device or file probe: the caller uses its fallback value
    }

}

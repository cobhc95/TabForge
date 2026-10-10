using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using TabForge.Services;

namespace TabForge.Audio;

/// <summary>An opened audio file, as the waveform reader sees it (tests replace the real decoder).</summary>
internal interface IMediaSource : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    double TotalSeconds { get; }
    int Read(float[] buffer, int offset, int count);
}

internal sealed class NAudioMediaSource : IMediaSource
{
    private readonly AudioFileReader _reader;
    public NAudioMediaSource(string file) { _reader = new AudioFileReader(file); }
    public int SampleRate => _reader.WaveFormat.SampleRate;
    public int Channels => _reader.WaveFormat.Channels;
    public double TotalSeconds => _reader.TotalTime.TotalSeconds;
    public int Read(float[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);
    public void Dispose() => _reader.Dispose();
}

public enum WaveState { Loading, Ready, NeedsApproval, Failed }

/// <summary>What a clip shows while (or instead of) its outline: nothing yet, the outline, an approval request or an error.</summary>
public readonly record struct WaveStatus(WaveState State, string? Message);

/// <summary>
/// Waveform outlines for drawing audio clips: the peak level of every 10 ms of a file, read in the background by one worker
/// (a 5-minute file is ~30,000 numbers). Drawing never waits: until a file is read its clip shows a plain block, then the
/// timeline redraws. Every path goes through <see cref="MediaAccess"/> first (device paths refused, network and removable
/// locations only after approval); decoding is bounded (file size, duration, samples, time, queue length) and cancellable;
/// the cache keeps a memory budget (least recently drawn outlines go first) and notices a file replaced at the same path.
/// </summary>
public static class WaveformCache
{
    public const double SecondsPerPeak = 0.01;
    public const long MaxFileBytes = 2L << 30;      // 2 GiB
    public const double MaxSeconds = 2 * 3600;      // 2 h
    public const int MaxPending = 64;
    private static readonly TimeSpan MaxDecodeTime = TimeSpan.FromMinutes(5);

    /// <summary>Peak data kept at most (bytes).</summary>
    internal static long BudgetBytes = 256L << 20;
    /// <summary>How often a drawn file is looked at again to see whether it was replaced (ms).</summary>
    internal static long RecheckMs = 2000;
    /// <summary>Test seam: replaces the decoder.</summary>
    internal static Func<string, IMediaSource>? OpenOverride;
    /// <summary>Test seam: limits for the output (peak count) are derived from <see cref="MaxSeconds"/>; tests lower it.</summary>
    internal static double MaxSecondsOverride = MaxSeconds;
    internal static long MaxFileBytesOverride = MaxFileBytes;

    /// <summary>Test seam: replaces the file-size / last-write probe (<c>null</c>: the file does not exist); counts probes.</summary>
    internal static Func<string, (long Size, long Ticks)?>? StatOverride { get; set; }

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

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Slot> Slots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<Slot> Queue = new();
    private static bool _workerRunning;
    private static int _decodeCount;
    private static int _active;   // jobs taken off the queue and not finished (tests wait for idle)

    /// <summary>A file's outline finished reading or changed state (raised on a background thread).</summary>
    public static event Action<string>? Ready;

    static WaveformCache()
    {
        // An approval changed (or a song's scope moved): what waited for it is asked again, outlines no longer allowed are dropped, and queued work
        // is checked again by the worker before it opens anything.
        MediaAccess.Changed += () =>
        {
            Reset(s => s.State is WaveState.NeedsApproval or WaveState.Failed);
            lock (Gate)
                foreach (var s in Slots.Values.Where(x => x.State == WaveState.Ready).ToList())
                    if (s.Verdict is { } known && !MediaAccess.Decide(known, s.Context).Allowed) { s.State = WaveState.NeedsApproval; s.Message = "not loaded (approve to load)"; s.Peaks = null; }
        };
    }

    /// <summary>Files decoded so far (self-test: a blocked path must never get here).</summary>
    internal static int DecodeCount => Volatile.Read(ref _decodeCount);
    internal static int CachedCount { get { lock (Gate) return Slots.Count; } }
    internal static long CachedBytes { get { lock (Gate) return Slots.Values.Sum(s => (long)(s.Peaks?.Length ?? 0) * 4); } }

    private static string KeyOf(string file, MediaContext context)
    {
        var scope = context.ScopeKey + "\0";
        if (Path.IsPathFullyQualified(file)) return scope + file;
        return scope + (context.BaseDirectory ?? "") + "\0" + file;
    }

    /// <summary>The outline of <paramref name="file"/> for this document, or null while it is not (or may not be) read; starts reading when needed.</summary>
    public static float[]? Get(string file, MediaContext context)
    {
        var s = Touch(file, context);
        return s.State == WaveState.Ready ? s.Peaks : null;
    }

    /// <summary>Why a clip has no outline: still loading, waiting for approval, or an error.</summary>
    public static WaveStatus StatusOf(string file, MediaContext context)
    {
        var s = Touch(file, context);
        return new WaveStatus(s.State, s.Message);
    }

    private static Slot Touch(string file, MediaContext context)
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
                Slots.Remove(key); old.Cts.Cancel();
                if (old.Queued) { Queue.Remove(old); old.Queued = false; }
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

    private static void Enqueue(Slot s)
    {
        if (s.Queued) return;
        s.Queued = true;
        Queue.AddLast(s);
        if (!_workerRunning) { _workerRunning = true; _ = Task.Run(Work); }
        Monitor.PulseAll(Gate);
    }

    private static void Work()
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

    private static void RaiseReady(string raw) { try { Ready?.Invoke(raw); } catch (Exception ex) { Services.Trace.Error(Services.Trace.Import, "waveform cache: ready handler: " + ex.Message); } }

    private static void Fail(Slot s, string message)
    {
        lock (Gate) { if (s.Cts.IsCancellationRequested) return; s.State = WaveState.Failed; s.Message = message; s.Peaks = null; }
    }

    /// <summary>Runs on the worker. Returns true when the slot's state changed (the timeline redraws).</summary>
    private static bool Process(Slot s)
    {
        var token = s.Cts.Token;
        if (token.IsCancellationRequested) return false;
        var context = s.Context;
        // Work asked for by a document that has closed, or whose path changed since (Save As), is dropped before anything is opened.
        if (!context.IsCurrent(s.Revision)) { DropSlot(s); return false; }
        // The permission is evaluated now, on the worker, with this document's context: an approval revoked while the request waited in the queue does not open the file.
        var decision = MediaAccess.Evaluate(s.Raw, context);
        if (decision.State == MediaAccessState.Refused) { Fail(s, decision.Message); return true; }
        if (decision.State == MediaAccessState.NeedsApproval)
        {
            lock (Gate) { var was = s.State; s.State = WaveState.NeedsApproval; s.Message = "not loaded (approve to load)"; s.Peaks = null; return was != WaveState.NeedsApproval; }
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
            lock (Gate) { s.State = WaveState.NeedsApproval; s.Message = "not loaded (approve to load)"; s.Peaks = null; }
            return true;
        }
        lock (Gate)
        {
            if (s.Cts.IsCancellationRequested) return false;
            s.Size = size; s.Ticks = ticks;
            if (peaks is null) { s.State = WaveState.Failed; s.Message = error; s.Peaks = null; }
            else { s.State = WaveState.Ready; s.Message = null; s.Peaks = peaks; Evict(s); }
        }
        return true;
    }

    private static void DropSlot(Slot s)
    {
        lock (Gate)
        {
            if (Slots.TryGetValue(s.Key, out var current) && ReferenceEquals(current, s)) Slots.Remove(s.Key);
            s.Cts.Cancel();
        }
    }

    /// <summary>Reads the peaks; null (with <paramref name="error"/>) when the file is refused or unreadable, or the token was cancelled.</summary>
    private static float[]? Decode(string path, CancellationToken token, out string? error)
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
            var peaks = new List<float>();
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
    private static void Evict(Slot keep)
    {
        long total = 0;
        foreach (var x in Slots.Values) total += (long)(x.Peaks?.Length ?? 0) * 4;
        if (total <= BudgetBytes) return;
        foreach (var victim in Slots.Values.Where(x => !ReferenceEquals(x, keep) && x.Peaks is not null).OrderBy(x => x.LastUsed).ToList())
        {
            total -= (long)victim.Peaks!.Length * 4;
            Slots.Remove(victim.Key);
            victim.Cts.Cancel();
            if (total <= BudgetBytes) break;
        }
    }

    /// <summary>Removes matching slots (their decoding is cancelled); the next draw asks again.</summary>
    private static void Reset(Func<Slot, bool> match)
    {
        List<string> raws;
        lock (Gate)
        {
            var hit = Slots.Values.Where(match).ToList();
            raws = hit.Select(s => s.Raw).ToList();
            foreach (var s in hit) { Slots.Remove(s.Key); s.Cts.Cancel(); if (s.Queued) { Queue.Remove(s); s.Queued = false; } }
        }
        foreach (var raw in raws) RaiseReady(raw);   // timelines ask again
    }

    /// <summary>Stops reading (and forgets) every file that is still loading and is not one of <paramref name="wanted"/>: a clip was removed or a timeline closed.</summary>
    public static void CancelUnused(IEnumerable<string> wanted, MediaContext context)
    {
        var keep = new HashSet<string>(wanted.Select(f => KeyOf(f, context)), StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            // Only this document's own requests: another window showing the same file keeps reading it.
            foreach (var s in Slots.Values.Where(s => ReferenceEquals(s.Context, context) && s.State == WaveState.Loading && !keep.Contains(s.Key)).ToList())
            {
                Slots.Remove(s.Key);
                s.Cts.Cancel();
                if (s.Queued) { Queue.Remove(s); s.Queued = false; }
            }
        }
    }

    /// <summary>Stops reading (and forgets) these files if they are still loading: their timeline closed.</summary>
    public static void Cancel(IEnumerable<string> files, MediaContext context)
    {
        var drop = new HashSet<string>(files.Select(f => KeyOf(f, context)), StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            foreach (var s in Slots.Values.Where(s => ReferenceEquals(s.Context, context) && s.State == WaveState.Loading && drop.Contains(s.Key)).ToList())
            {
                Slots.Remove(s.Key);
                s.Cts.Cancel();
                if (s.Queued) { Queue.Remove(s); s.Queued = false; }
            }
        }
    }

    /// <summary>A song closed: every slot it asked for goes (reading stops, the outlines leave the memory budget). Other songs' slots are untouched, whatever they share.</summary>
    public static void Forget(MediaContext context)
    {
        lock (Gate)
            foreach (var s in Slots.Values.Where(s => ReferenceEquals(s.Context, context)).ToList())
            {
                Slots.Remove(s.Key);
                s.Cts.Cancel();
                if (s.Queued) { Queue.Remove(s); s.Queued = false; }
            }
    }

    /// <summary>Forgets everything (tests).</summary>
    internal static void ClearAll()
    {
        lock (Gate)
        {
            foreach (var s in Slots.Values) s.Cts.Cancel();
            Slots.Clear(); Queue.Clear();
        }
    }

    /// <summary>Waits until nothing is queued or being read (tests).</summary>
    internal static bool WaitIdle(int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            lock (Gate) { if (Queue.Count == 0 && _active == 0 && Slots.Values.All(s => s.State != WaveState.Loading)) return true; }
            Thread.Sleep(10);
        }
        return false;
    }

    /// <summary>
    /// Subscribes <paramref name="handler"/> to <see cref="Ready"/> without keeping <paramref name="owner"/> alive (a closed timeline
    /// is collected; the subscription removes itself when it finds the owner gone). The handler must not capture the owner.
    /// </summary>
    public static IDisposable SubscribeWeak<T>(T owner, Action<T, string> handler) where T : class
    {
        var weak = new WeakReference<T>(owner);
        Action<string>? h = null;
        h = file =>
        {
            if (weak.TryGetTarget(out var o)) handler(o, file);
            else Ready -= h;
        };
        Ready += h;
        return new Unsubscriber(() => Ready -= h);
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
    public static double LengthOf(string file, MediaContext context, bool userPicked = false)
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

    public static readonly string[] Extensions = { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".ogg", ".m4a", ".wma" };
}

using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundTouch;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Audio;

/// <summary>
/// Plays one audio clip. A background disk thread (<see cref="DiskStreamer"/>) decodes the file from the right
/// place into a small lock-free ring (resampled to the engine rate, pitch / speed through SoundTouch); the audio
/// thread only copies from the ring. When the song position jumps, the audio thread asks for the new place and
/// plays silence for that block until it is ready. The ring exists only while the clip is near the playhead.
/// </summary>
public sealed class ClipPlayer : IDisposable
{
    private const int RingFrames = 1 << 16; // ~1.4 s at 48 kHz
    private readonly int _rate;
    public ClipSpec Spec { get; }
    private readonly float _gain;

    // Shared between the audio thread and the disk thread.
    private volatile float[]? _ring;
    private long _write, _read;
    private double _ringStartSec = double.NaN; // song time of ring frame 0
    private double _request = double.NaN;     // song time the audio thread wants (NaN = none)
    private long _lastUsed;

    // Disk thread only.
    private ISampleProvider? _source;
    private AudioFileReader? _reader;
    private SoundTouchProcessor? _stretch;
    private bool _sourceDone;   // the source returned nothing (clip end): no point in waking up for it until the next seek
    private readonly float[] _decode = new float[4096 * 2];
    private readonly float[] _stretched = new float[8192 * 2];

    public bool Failed { get; private set; }

    private readonly bool _offline;

    /// <param name="offline">Offline render: decoding, resampling and stretching happen synchronously inside <see cref="Mix"/> (no disk thread, fully deterministic).</param>
    public ClipPlayer(ClipSpec spec, int rate, bool offline = false)
    {
        Spec = spec;
        _rate = rate;
        _offline = offline;
        _gain = (float)Gain.FromDb(spec.GainDb);
    }

    /// <summary>Offline: seeks (when the position jumped) and decodes exactly what this block needs, then copies it.</summary>
    private void MixOffline(float[] left, float[] right, int frames, double songSec, int from, double wantedSec, double end)
    {
        var ringStart = _ring is null ? double.NaN : _ringStartSec;
        var index = double.IsNaN(ringStart) ? long.MaxValue : (long)Math.Round((wantedSec - ringStart) * _rate);
        if (_ring is null || Math.Abs(index - _read) > 256) { Seek(wantedSec); if (Failed) return; }
        var ring = _ring;
        if (ring is null) return;
        var count = (int)Math.Min(frames - from, (long)((end - wantedSec) * _rate) + 1);
        var stalls = 0;
        while (_write - _read < count && stalls < 64)
        {
            var free = RingFrames - (_write - _read);
            var produced = free >= 1024 ? Produce(ring, (int)Math.Min(free, 4096)) : 0;
            stalls = produced > 0 ? 0 : stalls + 1;
        }
        count = (int)Math.Min(count, _write - _read);
        for (var i = 0; i < count; i++)
        {
            var at = (int)((_read + i) & (RingFrames - 1)) * 2;
            left[from + i] += ring[at] * _gain;
            right[from + i] += ring[at + 1] * _gain;
        }
        _read += Math.Max(0, count);
    }

    private double Length => Spec.SourceLengthSec / Spec.Speed;

    /// <summary>
    /// Audio thread: adds the clip's audio for the block starting at song time <paramref name="songSec"/>.
    /// Allocation-free.
    /// </summary>
    public void Mix(float[] left, float[] right, int frames, double songSec, bool playing)
    {
        if (!playing || Failed) return;
        var start = Spec.StartSec;
        var end = start + Length;
        var blockEnd = songSec + (double)frames / _rate;
        if (_offline)
        {
            if (blockEnd <= start || songSec >= end) return;
            var offlineFrom = Math.Max(0, (int)Math.Round((start - songSec) * _rate));
            if (offlineFrom >= frames) return;
            MixOffline(left, right, frames, songSec, offlineFrom, songSec + (double)offlineFrom / _rate, end);
            return;
        }
        // Prepare a little ahead of the clip's start.
        if (blockEnd < start)
        {
            if (start - songSec < 1.5 && !Primed(start)) Volatile.Write(ref _request, start);
            return;
        }
        if (songSec >= end) return;
        var from = Math.Max(0, (int)Math.Round((start - songSec) * _rate));
        var wantedSec = songSec + (double)from / _rate;
        var ring = _ring;
        var ringStart = Volatile.Read(ref _ringStartSec);
        if (ring is null || double.IsNaN(ringStart)) { Volatile.Write(ref _request, wantedSec); return; }
        var index = (long)Math.Round((wantedSec - ringStart) * _rate);
        var read = Volatile.Read(ref _read);
        if (Math.Abs(index - read) > 256) { Volatile.Write(ref _request, wantedSec); return; } // jumped: re-seek
        var available = Volatile.Read(ref _write) - read;
        var count = (int)Math.Min(Math.Min(frames - from, available), (long)((end - wantedSec) * _rate) + 1);
        for (var i = 0; i < count; i++)
        {
            var at = (int)((read + i) & (RingFrames - 1)) * 2;
            left[from + i] += ring[at] * _gain;
            right[from + i] += ring[at + 1] * _gain;
        }
        Volatile.Write(ref _read, read + Math.Max(0, count));
        Volatile.Write(ref _lastUsed, Environment.TickCount64);
    }

    private bool Primed(double atSec) => _ring is not null && Math.Abs(Volatile.Read(ref _ringStartSec) - atSec) < 0.002 && Volatile.Read(ref _read) == 0;

    /// <summary>Disk thread: serves a seek request and keeps the ring filled. Returns true when it did work.</summary>
    public bool Service()
    {
        if (Failed) return false;
        var request = Volatile.Read(ref _request);
        if (!double.IsNaN(request))
        {
            Volatile.Write(ref _request, double.NaN);
            Seek(request);
        }
        var ring = _ring;
        if (ring is null) return false;
        // Released when unused for a while (the audio thread keeps its own reference while reading).
        if (Environment.TickCount64 - Volatile.Read(ref _lastUsed) > 8000 && double.IsNaN(Volatile.Read(ref _request)))
        {
            _ring = null;
            Volatile.Write(ref _ringStartSec, double.NaN);
            CloseSource();
            return false;
        }
        var free = RingFrames - (Volatile.Read(ref _write) - Volatile.Read(ref _read));
        if (free < 2048 || _source is null) return false;
        var produced = Produce(ring, (int)Math.Min(free, 4096));
        return produced > 0;
    }

    private void Seek(double songSec)
    {
        try
        {
            var intoClip = Math.Max(0, songSec - Spec.StartSec) * Spec.Speed; // seconds of source
            if (!ClipPathGuard.IsAllowed(Spec.File, out var refused))
            {
                Failed = true;
                EngineLog.Write($"audio clip refused ({refused}): {Spec.File}");
                return;
            }
            CloseSource();
            _reader = new AudioFileReader(Spec.File);
            if (_reader.TotalTime.TotalHours > 2)   // same limit as the UI
            {
                Failed = true;
                EngineLog.Write($"audio clip too long: {Spec.File}");
                CloseSource();
                return;
            }
            var sourcePos = Spec.OffsetSec + intoClip;
            _reader.CurrentTime = TimeSpan.FromSeconds(Math.Min(sourcePos, _reader.TotalTime.TotalSeconds));
            ISampleProvider provider = _reader;
            if (provider.WaveFormat.Channels == 1) provider = new MonoToStereoSampleProvider(provider);
            else if (provider.WaveFormat.Channels > 2) provider = new MultiplexingSampleProvider(new[] { provider }, 2);
            if (provider.WaveFormat.SampleRate != _rate) provider = new WdlResamplingSampleProvider(provider, _rate);
            _source = new TakeProvider(provider, (long)((Spec.SourceLengthSec - intoClip) * _rate));
            _sourceDone = false;
            if (Math.Abs(Spec.Speed - 1) > 0.001 || Math.Abs(Spec.Pitch) > 0.001)
                _stretch = new SoundTouchProcessor { SampleRate = _rate, Channels = 2, Tempo = Spec.Speed, PitchSemiTones = Spec.Pitch };
            var ring = _ring ?? new float[RingFrames * 2];
            Volatile.Write(ref _read, 0);
            Volatile.Write(ref _write, 0);
            Volatile.Write(ref _ringStartSec, songSec);
            _ring = ring;
            Volatile.Write(ref _lastUsed, Environment.TickCount64);
            Produce(ring, 4096); // a first block right away
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException)
        {
            Failed = true;
            EngineLog.Write($"audio clip could not be read: {Spec.File}: {ex.Message}");
        }
    }

    private int Produce(float[] ring, int maxFrames)
    {
        if (_source is null) return 0;
        int frames;
        if (_stretch is null)
        {
            frames = _source.Read(_decode, 0, Math.Min(maxFrames, _decode.Length / 2) * 2) / 2;
            if (frames == 0) _sourceDone = true;
            Write(ring, _decode, frames);
            return frames;
        }
        // Keep SoundTouch fed until it gives output (it needs a few thousand frames to start).
        var received = _stretch.ReceiveSamples(_stretched.AsSpan(), Math.Min(maxFrames, _stretched.Length / 2));
        if (received == 0)
        {
            var read = _source.Read(_decode, 0, _decode.Length) / 2;
            if (read == 0) { _sourceDone = true; return 0; }
            _stretch.PutSamples(_decode.AsSpan(0, read * 2), read);
            received = _stretch.ReceiveSamples(_stretched.AsSpan(), Math.Min(maxFrames, _stretched.Length / 2));
        }
        Write(ring, _stretched, received);
        return received;
    }

    private void Write(float[] ring, float[] data, int frames)
    {
        var write = Volatile.Read(ref _write);
        for (var i = 0; i < frames; i++)
        {
            var at = (int)((write + i) & (RingFrames - 1)) * 2;
            ring[at] = data[i * 2];
            ring[at + 1] = data[i * 2 + 1];
        }
        Volatile.Write(ref _write, write + frames);
    }

    private void CloseSource()
    {
        _source = null;
        _stretch = null;
        _reader?.Dispose();
        _reader = null;
    }

    /// <summary>
    /// Disk thread, after <see cref="Service"/>: the player needs the thread back soon (its ring is below half while it has a
    /// source to read, or a seek is waiting). Otherwise the disk thread can sleep its idle interval.
    /// </summary>
    internal bool Hungry
    {
        get
        {
            if (Failed) return false;
            if (!double.IsNaN(Volatile.Read(ref _request))) return true;
            return _ring is not null && _source is not null && !_sourceDone && Volatile.Read(ref _write) - Volatile.Read(ref _read) < RingFrames / 2;
        }
    }

    /// <summary>Set once by <see cref="DiskStreamer.Register"/>: from then on only the disk thread touches the decoder.</summary>
    internal bool Streamed;
    private int _disposed;

    /// <summary>
    /// A streamed player is handed to the disk thread, which closes the file on its next pass (it may be inside
    /// <see cref="Service"/> for this player right now); a player that was never streamed (offline render) closes here.
    /// Idempotent, any thread.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Streamed) DiskStreamer.Unregister(new[] { this });
        else CloseSource();
    }

    /// <summary>Disk thread only: closes the decoder of a player that left the streamer (no Service call can be running).</summary>
    internal void CloseOnDiskThread() => CloseSource();

    /// <summary>Disk thread: Service threw. The player goes silent for good (Mix checks Failed); it is not serviced again.</summary>
    internal void MarkFailed() => Failed = true;

    /// <summary>Stops after a number of frames (the clip's trimmed end).</summary>
    private sealed class TakeProvider(ISampleProvider inner, long frames) : ISampleProvider
    {
        private long _left = frames;
        public WaveFormat WaveFormat => inner.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            if (_left <= 0) return 0;
            var want = (int)Math.Min(count, _left * 2);
            var read = inner.Read(buffer, offset, want);
            _left -= read / 2;
            return read;
        }
    }
}

/// <summary>
/// The engine's one disk thread: serves every clip's seeks and keeps their rings filled. It also owns closing the files
/// of players that leave it: <see cref="Unregister"/> only hands them over, and the thread closes them at the start of its
/// next pass, when it can no longer be inside <see cref="ClipPlayer.Service"/> for them. A player whose Service throws is
/// marked failed and logged once; the thread itself never dies.
/// </summary>
public static class DiskStreamer
{
    private static readonly List<ClipPlayer> Players = new();
    private static readonly List<ClipPlayer> Leaving = new();
    private static Thread? _thread;
    private static long _passes, _faults;

    /// <summary>Players currently streamed.</summary>
    public static int Count { get { lock (Players) return Players.Count; } }
    /// <summary>Players handed over whose files the disk thread has not closed yet.</summary>
    public static int PendingClose { get { lock (Players) return Leaving.Count + Volatile.Read(ref _closing); } }
    private static int _closing;
    private static volatile bool _parked;
    /// <summary>The thread is parked (no players): it costs no wake-ups at all.</summary>
    public static bool Parked => _parked;
    /// <summary>Completed passes of the disk thread (tests wait on it).</summary>
    public static long Passes => Interlocked.Read(ref _passes);
    /// <summary>Exceptions caught on the disk thread since start.</summary>
    public static long Faults => Interlocked.Read(ref _faults);
    public static bool Running => _thread is { IsAlive: true };
    /// <summary>Wakes the parked thread (nothing registered) when players arrive or leave.</summary>
    private static readonly AutoResetEvent Wake = new(false);
    /// <summary>Idle interval: rings hold ~1.4 s, so 8 ms between top-ups is plenty; hungry rings (below half, or a seek) get 1 ms.</summary>
    private const int IdleSleepMs = 8, HungrySleepMs = 1;

    public static void Register(IEnumerable<ClipPlayer> players)
    {
        lock (Players)
        {
            foreach (var p in players) { p.Streamed = true; Players.Add(p); }
            Wake.Set();
            if (_thread is not null) return;
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "TabForge disk" };
            _thread.Start();
        }
    }

    /// <summary>Stops streaming these players; the disk thread closes their files on its next pass. Idempotent.</summary>
    public static void Unregister(IEnumerable<ClipPlayer> players)
    {
        lock (Players)
        {
            foreach (var p in players)
                if (Players.Remove(p) && !Leaving.Contains(p)) Leaving.Add(p);
            if (Leaving.Count > 0) Wake.Set();
        }
    }

    private static void Run()
    {
        var work = new List<ClipPlayer>();
        var closing = new List<ClipPlayer>();
        while (true)
        {
            var hungry = false;
            var parked = false;
            try
            {
                lock (Players)
                {
                    work.Clear(); work.AddRange(Players);
                    closing.Clear(); closing.AddRange(Leaving); Leaving.Clear();
                    Volatile.Write(ref _closing, closing.Count);
                }
                // Removed before this pass: the previous pass (same thread) has finished with them.
                foreach (var player in closing)
                {
                    try { player.CloseOnDiskThread(); }
                    catch (Exception ex) { Interlocked.Increment(ref _faults); EngineLog.Write($"audio clip could not be closed: {player.Spec.File}: {ex.Message}"); }
                    Interlocked.Decrement(ref _closing);
                }
                closing.Clear();
                foreach (var player in work)
                {
                    try { player.Service(); hungry |= player.Hungry; }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _faults);
                        player.MarkFailed();   // not serviced again, so this is logged once per player
                        EngineLog.Write($"audio clip stopped (disk thread error): {player.Spec.File}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                parked = work.Count == 0;
                work.Clear();
            }
            catch (Exception ex) { Interlocked.Increment(ref _faults); EngineLog.Write($"disk thread: {ex.Message}"); }
            Interlocked.Increment(ref _passes);
            // Nothing registered: park until players arrive or leave (Register / Unregister set Wake). Else top up at the idle
            // interval, or at 1 ms while a ring is hungry.
            if (parked) { lock (Players) parked = Players.Count == 0 && Leaving.Count == 0; }
            if (parked) { _parked = true; Wake.WaitOne(); _parked = false; }
            else if (hungry) Thread.Sleep(HungrySleepMs);
            else Wake.WaitOne(IdleSleepMs);
        }
    }
}

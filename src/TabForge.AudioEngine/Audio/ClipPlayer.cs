using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundTouch;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Audio;

// Owns: playing one audio clip (background disk decode into a lock-free ring, audio-thread copy, seek silence).

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
    private readonly bool _fades;

    // Shared between the audio thread and the disk thread.
    private volatile RingBuffer? _ring;       // null = no ring; a seek publishes a whole new one, so the audio thread always sees one consistent ring
    private double _requestedSpeed = 1;       // audio thread only: the speed a re-fill was last asked for
    private double _speed = 1;                 // transport speed, published by the audio thread each block
    private double _request = double.NaN;     // song time the audio thread wants (NaN = none)
    private long _lastUsed;

    // Disk thread only.
    private ISampleProvider? _source;
    private AudioFileReader? _reader;
    private SoundTouchProcessor? _stretch;
    private bool _sourceDone;   // the source returned nothing (clip end): no point in waking up for it until the next seek
    private const int DecodeFloats = 4096 * 2;
    private const int StretchedFloats = 8192 * 2;
    // Scratch for one read: taken when the clip first produces sound and dropped with the ring, so a clip far from the playhead holds none.
    private float[]? _decode;
    private float[]? _stretched;   // only for a clip with pitch or speed

    public bool Failed { get; private set; }

    private readonly bool _offline;

    /// <summary>Ring frame 0 starts at song time <paramref name="Sec"/> and the ring was stretched for transport speed <paramref name="Speed"/>: one ring frame is Speed / rate song seconds.</summary>
    private sealed record RingPlace(double Sec, double Speed);

    /// <summary>
    /// One ring with its place and counters. Never reused: a seek builds a new one and publishes it in a single write, so the audio thread
    /// (which reads <see cref="_ring"/> once per block) never sees a half-reset ring or a place from another ring. Only the disk thread
    /// moves <see cref="Write"/>, only the audio thread moves <see cref="Read"/> (both through Volatile).
    /// </summary>
    private sealed class RingBuffer(RingPlace place)
    {
        public readonly float[] Data = new float[RingFrames * 2];
        public readonly RingPlace Place = place;
        public long Read, Write;
    }

    /// <summary>The ring frame that plays at song time <paramref name="sec"/>.</summary>
    private long RingIndex(RingPlace place, double sec) => (long)Math.Round((sec - place.Sec) * _rate / place.Speed);

    /// <summary>The log of the session that owns this player (the shared disk thread writes through it).</summary>
    public EngineLogWriter Log { get; }

    /// <param name="offline">Offline render: decoding, resampling and stretching happen synchronously inside <see cref="Mix"/> (no disk thread, fully deterministic).</param>
    public ClipPlayer(ClipSpec spec, int rate, EngineLogWriter log, bool offline = false)
    {
        Spec = spec;
        Log = log;
        _rate = rate;
        _offline = offline;
        _gain = (float)Gain.FromDb(spec.GainDb);
        _fades = spec.FadeInSec > 0 || spec.FadeOutSec > 0;
    }

    /// <summary>
    /// The fade gain (0..1) at <paramref name="t"/> seconds into a clip that is <paramref name="length"/> long on the timeline: a linear ramp up over
    /// <paramref name="fadeIn"/> and down over <paramref name="fadeOut"/> (together never longer than the clip). Allocation-free.
    /// </summary>
    public static float FadeGain(double t, double length, double fadeIn, double fadeOut)
    {
        var total = fadeIn + fadeOut;
        if (total > length && total > 0) { var k = length / total; fadeIn *= k; fadeOut *= k; }
        var g = 1.0;
        if (fadeIn > 0 && t < fadeIn) g = Math.Max(0, t) / fadeIn;
        if (fadeOut > 0 && t > length - fadeOut) g = Math.Min(g, Math.Max(0, length - t) / fadeOut);
        return (float)g;
    }

    /// <summary>Offline: seeks (when the position jumped) and decodes exactly what this block needs, then copies it.</summary>
    private void MixOffline(float[] left, float[] right, int frames, double songSec, int from, double wantedSec, double end)
    {
        var current = _ring;
        var index = current is null ? long.MaxValue : RingIndex(current.Place, wantedSec);
        if (current is null || Math.Abs(index - current.Read) > 256) { Seek(wantedSec); if (Failed) return; }
        var ring = _ring;
        if (ring is null) return;
        var count = (int)Math.Min(frames - from, (long)((end - wantedSec) * _rate) + 1);
        var stalls = 0;
        while (ring.Write - ring.Read < count && stalls < 64)
        {
            var free = RingFrames - (ring.Write - ring.Read);
            var produced = free >= 1024 ? Produce(ring, (int)Math.Min(free, 4096)) : 0;
            stalls = produced > 0 ? 0 : stalls + 1;
        }
        count = (int)Math.Min(count, ring.Write - ring.Read);
        for (var i = 0; i < count; i++)
        {
            var at = (int)((ring.Read + i) & (RingFrames - 1)) * 2;
            var g = _fades ? _gain * FadeGain((wantedSec - Spec.StartSec) + (double)i / _rate, Length, Spec.FadeInSec, Spec.FadeOutSec) : _gain;
            left[from + i] += ring.Data[at] * g;
            right[from + i] += ring.Data[at + 1] * g;
        }
        ring.Read += Math.Max(0, count);
    }

    private double Length => Spec.SourceLengthSec / Spec.Speed;

    /// <summary>
    /// Audio thread: adds the clip's audio for the block starting at song time <paramref name="songSec"/>. The song runs at
    /// <paramref name="speed"/> times real time, so the block covers frames * speed / rate song seconds and the disk thread
    /// stretches the clip by that much more (a speed change re-fills the ring in the background, with no silence). Allocation-free.
    /// Right after a speed change the ring still holds audio stretched for the old speed: it plays on at the new advance (a brief tempo
    /// error, no silence) until the re-fill is published, which re-anchors on its own place, so the error never accumulates.
    /// </summary>
    public void Mix(float[] left, float[] right, int frames, double songSec, bool playing, double speed)
    {
        if (!playing || Failed) return;
        var start = Spec.StartSec;
        var end = start + Length;
        var blockEnd = songSec + frames * speed / _rate;
        if (_offline)
        {
            // Offline always renders at transport speed 1: the ring is not re-anchored for another speed here.
            if (speed != 1) throw new ArgumentException("offline clip mixing needs transport speed 1", nameof(speed));
            if (blockEnd <= start || songSec >= end) return;
            var offlineFrom = Math.Max(0, (int)Math.Round((start - songSec) * _rate));
            if (offlineFrom >= frames) return;
            MixOffline(left, right, frames, songSec, offlineFrom, songSec + (double)offlineFrom / _rate, end);
            return;
        }
        Volatile.Write(ref _speed, speed);
        // Prepare a little ahead of the clip's start.
        if (blockEnd < start)
        {
            if (start - songSec < 1.5 && !Primed(start, speed)) Volatile.Write(ref _request, start);
            return;
        }
        if (songSec >= end) return;
        var from = Math.Max(0, (int)Math.Round((start - songSec) * _rate / speed));
        var wantedSec = songSec + from * speed / _rate;
        var ring = _ring;
        if (ring is null) { Volatile.Write(ref _request, wantedSec); return; }
        var place = ring.Place;
        var index = RingIndex(place, wantedSec);
        var read = Volatile.Read(ref ring.Read);
        // The disk thread answers a seek while the song keeps running, so a fresh ring is usually a little behind the playhead:
        // skip the stale frames when the ring already holds the wanted one (otherwise a busy machine re-seeks forever and the clip never starts).
        // A ring stretched for another speed is asked for once from the place now wanted; it plays on meanwhile rather than go silent.
        var inStep = place.Speed == speed;
        if (!inStep && _requestedSpeed != speed) { _requestedSpeed = speed; Volatile.Write(ref _request, wantedSec); }
        if (inStep && index > read + 256 && index < Volatile.Read(ref ring.Write)) { read = index; Volatile.Write(ref ring.Read, read); }
        else if (inStep && Math.Abs(index - read) > 256) { Volatile.Write(ref _request, wantedSec); return; } // jumped: re-seek
        var available = Volatile.Read(ref ring.Write) - read;
        var count = (int)Math.Min(Math.Min(frames - from, available), (long)((end - wantedSec) * _rate / speed) + 1);
        for (var i = 0; i < count; i++)
        {
            var at = (int)((read + i) & (RingFrames - 1)) * 2;
            var g = _fades ? _gain * FadeGain((wantedSec - start) + i * speed / _rate, Length, Spec.FadeInSec, Spec.FadeOutSec) : _gain;
            left[from + i] += ring.Data[at] * g;
            right[from + i] += ring.Data[at + 1] * g;
        }
        Volatile.Write(ref ring.Read, read + Math.Max(0, count));
        Volatile.Write(ref _lastUsed, Environment.TickCount64);
    }

    private bool Primed(double atSec, double speed) => _ring is { } ring && Math.Abs(ring.Place.Sec - atSec) < 0.002 && ring.Place.Speed == speed && Volatile.Read(ref ring.Read) == 0;

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
            CloseSource();
            _decode = null;
            _stretched = null;
            return false;
        }
        var free = RingFrames - (Volatile.Read(ref ring.Write) - Volatile.Read(ref ring.Read));
        if (free < 2048 || _source is null) return false;
        var produced = Produce(ring, (int)Math.Min(free, 4096));
        return produced > 0;
    }

    /// <summary>Disk thread: the stretch tempo is the clip's speed times the transport speed (pitch unchanged); at tempo 1 with no pitch there is no stretch.</summary>
    private void SetTempo(double speed)
    {
        var tempo = Spec.Speed * speed;
        if (_stretch is not null) _stretch.Tempo = tempo;
        else if (Math.Abs(tempo - 1) > 0.001 || Math.Abs(Spec.Pitch) > 0.001)
            _stretch = new SoundTouchProcessor { SampleRate = _rate, Channels = 2, Tempo = tempo, PitchSemiTones = Spec.Pitch };
    }

    private void Seek(double songSec)
    {
        try
        {
            var intoClip = Math.Max(0, songSec - Spec.StartSec) * Spec.Speed; // seconds of source
            if (!ClipPathGuard.IsAllowed(Spec.File, out var refused))
            {
                Failed = true;
                Log.Write($"audio clip refused ({refused}): {Spec.File}");
                return;
            }
            CloseSource();
            _reader = new AudioFileReader(Spec.File);
            if (_reader.TotalTime.TotalHours > 2)   // same limit as the UI
            {
                Failed = true;
                Log.Write($"audio clip too long: {Spec.File}");
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
            var speed = Volatile.Read(ref _speed);
            SetTempo(speed);
            var ring = new RingBuffer(new RingPlace(songSec, speed));   // fresh: the audio thread may still be copying from the old one
            _ring = ring;
            Volatile.Write(ref _lastUsed, Environment.TickCount64);
            // A first block right away; a stretch needs a few reads before its first output.
            for (var tries = 0; tries < 4 && Volatile.Read(ref ring.Write) == 0 && !_sourceDone; tries++) Produce(ring, 4096);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException)
        {
            Failed = true;
            Log.Write($"audio clip could not be read: {Spec.File}: {ex.Message}");
        }
    }

    private int Produce(RingBuffer ring, int maxFrames)
    {
        if (_source is null) return 0;
        var decode = _decode ??= new float[DecodeFloats];
        int frames;
        if (_stretch is null)
        {
            frames = _source.Read(decode, 0, Math.Min(maxFrames, decode.Length / 2) * 2) / 2;
            if (frames == 0) _sourceDone = true;
            Write(ring, decode, frames);
            return frames;
        }
        var stretched = _stretched ??= new float[StretchedFloats];
        // Keep SoundTouch fed until it gives output (it needs a few thousand frames to start).
        var received = _stretch.ReceiveSamples(stretched.AsSpan(), Math.Min(maxFrames, stretched.Length / 2));
        if (received == 0)
        {
            var read = _source.Read(decode, 0, decode.Length) / 2;
            if (read == 0) { _sourceDone = true; return 0; }
            _stretch.PutSamples(decode.AsSpan(0, read * 2), read);
            received = _stretch.ReceiveSamples(stretched.AsSpan(), Math.Min(maxFrames, stretched.Length / 2));
        }
        Write(ring, stretched, received);
        return received;
    }

    private void Write(RingBuffer ring, float[] data, int frames)
    {
        var write = Volatile.Read(ref ring.Write);
        for (var i = 0; i < frames; i++)
        {
            var at = (int)((write + i) & (RingFrames - 1)) * 2;
            ring.Data[at] = data[i * 2];
            ring.Data[at + 1] = data[i * 2 + 1];
        }
        Volatile.Write(ref ring.Write, write + frames);
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
            return _ring is { } ring && _source is not null && !_sourceDone && Volatile.Read(ref ring.Write) - Volatile.Read(ref ring.Read) < RingFrames / 2;
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
                    catch (Exception ex) { Interlocked.Increment(ref _faults); player.Log.Write($"audio clip could not be closed: {player.Spec.File}: {ex.Message}"); }
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
                        player.Log.Write($"audio clip stopped (disk thread error): {player.Spec.File}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                parked = work.Count == 0;
                work.Clear();
            }
            catch (Exception ex) { Interlocked.Increment(ref _faults); foreach (var log in work.Select(p => p.Log).Distinct()) log.Write($"disk thread: {ex.Message}"); }   // each session with players on this shared thread
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

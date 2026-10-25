using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Mixing;

// Owns: the master output tap: a single-producer ring the audio callback copies each finished block into, and the drain thread that hands
// the blocks to a sink (the engine sends them to TabForge as MasterAudio frames), after a safety limiter so that levels above full scale
// (the output passes floats up to the ceiling to the Windows mixer) reach the recording as a limited signal and not as hard clipping.
// Does not own: the mix, the pipe, or what the receiver does with the audio.
// Tests: TabForge.Avalonia.Tests.MasterTapNumberingTests, TestLiveAudioFidelity.
/// <summary>
/// Off by default: <see cref="Write"/> is then one volatile read. On, the audio callback copies its block (no lock, no allocation) and
/// a background thread forwards it about every 5 ms. Frame numbers count from the moment the tap was switched on (the audio clock).
/// </summary>
public sealed class MasterTap
{
    private const int RingFrames = 1 << 17;   // about 2.7 s at 48 kHz: a sink stalled for that long loses audio (counted in Lost)
    private const int RetireWaitMs = 1000;    // a sink still stalled after this long loses its run (see RetirePreviousRun); bounds the engine command thread

    private readonly float[] _ring = new float[RingFrames * 2];
    private const int GapSlots = 64;          // lost runs waiting for the drain thread; past that a loss stays pending on the audio thread and grows

    private long _written, _read, _lost;
    // Lost runs: where in the ring numbering each sits and its length. The drain thread moves the numbering on over each, so the frames after a
    // loss keep their true time (the receiver fills the hole with silence) instead of sliding earlier and making the sound shorter than the run.
    private readonly long[] _gapAt = new long[GapSlots], _gapFrames = new long[GapSlots];
    private long _gapsWritten, _gapsRead;
    private long _pendingAt, _pendingFrames;   // audio thread only: the current loss, published before the next block that fits
    private volatile bool _on;
    private volatile int _generation;
    private int _rate = 48000;
    private Thread? _thread;
    private SafetyLimiter _limiter = new(48000);

    /// <summary>Receives (sample rate, first frame, interleaved stereo floats, frame count) on the drain thread; the array is reused after the call returns.</summary>
    public Action<int, long, float[], int>? Sink { get; set; }

    /// <summary>Frames the ring could not take because the sink stalled.</summary>
    public long Lost => Interlocked.Read(ref _lost);

    public bool On => _on;

    /// <summary>Level of the recording relative to the output block: 1 is the nominal mix (the engine sets 1 on every driver); the limiter that follows keeps the result under full scale.</summary>
    public volatile float Gain = 1f;

    /// <summary>Main thread: switches the tap. Switching on restarts frame numbering at 0.</summary>
    public void Enable(bool on, int sampleRate)
    {
        if (on == _on) return;
        if (!on) { _on = false; return; }   // the drain thread notices, forwards what is left and ends
        RetirePreviousRun();
        _rate = sampleRate;
        _limiter = new SafetyLimiter(sampleRate);
        Interlocked.Exchange(ref _lost, 0);
        Volatile.Write(ref _read, 0);
        Volatile.Write(ref _written, 0);
        Volatile.Write(ref _gapsRead, 0);
        Volatile.Write(ref _gapsWritten, 0);
        _pendingFrames = 0;
        var generation = ++_generation;
        _on = true;
        _thread = new Thread(() => Drain(generation)) { IsBackground = true, Name = "TabForge master tap" };
        _thread.Start();
    }

    // Waits until the previous drain thread has ended, so that its blocks are all forwarded before numbering restarts. A sink still stalled
    // after the bound loses its run: the generation moves on, so the thread forwards nothing more and publishes no shared counter.
    // Off the command thread: the join runs on the pool (the main loop must never stall up to RetireWaitMs on a stalled sink).
    private void RetirePreviousRun()
    {
        var previous = _thread;
        _thread = null;
        if (previous is null || previous.Join(0)) return;
        _generation++;
        var stalled = previous;
        ThreadPool.QueueUserWorkItem(_ => { try { stalled.Join(RetireWaitMs); } catch (Exception) { } // swallow: best-effort wait on a retiring tap
 });
    }

    /// <summary>Audio thread: copies <paramref name="frames"/> stereo frames starting at <paramref name="offset"/> (a float index) of the finished output block.</summary>
    /// <remarks>The drivers hand the mix a byte[] seen as float[] (NAudio's WaveBuffer). Array.Copy goes by the runtime type and would widen
    /// each byte to a float (a loud positive hiss), so the copy goes through spans, which index the block as floats.</remarks>
    public void Write(float[] block, int offset, int frames)
    {
        if (!_on) return;
        var written = Volatile.Read(ref _written);
        if (written - Volatile.Read(ref _read) + frames > RingFrames)
        {
            Interlocked.Add(ref _lost, frames);
            if (_pendingFrames == 0) _pendingAt = written;
            _pendingFrames += frames;
            return;
        }
        if (_pendingFrames > 0) PublishGap();
        var at = (int)(written & (RingFrames - 1));
        var first = Math.Min(frames, RingFrames - at);
        var source = block.AsSpan(offset, frames * 2);
        source[..(first * 2)].CopyTo(_ring.AsSpan(at * 2));
        if (first < frames) source[(first * 2)..].CopyTo(_ring);
        Volatile.Write(ref _written, written + frames);
    }

    // Audio thread: hands the pending loss to the drain thread; with every slot taken it stays pending until one frees.
    private void PublishGap()
    {
        var slot = Volatile.Read(ref _gapsWritten);
        if (slot - Volatile.Read(ref _gapsRead) >= GapSlots) return;
        _gapAt[slot % GapSlots] = _pendingAt;
        _gapFrames[slot % GapSlots] = _pendingFrames;
        Volatile.Write(ref _gapsWritten, slot + 1);
        _pendingFrames = 0;
    }

    private void Drain(int generation)
    {
        long skipped = 0;   // frames lost before the read position: added to the numbers the sink sees
        var chunk = new float[MasterTapLimits.MaxChunkFrames * 2];
        var left = new float[MasterTapLimits.MaxChunkFrames]; var right = new float[MasterTapLimits.MaxChunkFrames];
        var limiter = _limiter;
        while (true)
        {
            var wasOn = _on && generation == _generation;
            var sink = Sink;
            long read = Volatile.Read(ref _read), written = Volatile.Read(ref _written);
            while (read < written && sink is not null)
            {
                var gaps = Volatile.Read(ref _gapsRead);
                var gapAt = gaps < Volatile.Read(ref _gapsWritten) ? _gapAt[gaps % GapSlots] : long.MaxValue;
                if (gapAt <= read) { skipped += _gapFrames[gaps % GapSlots]; Volatile.Write(ref _gapsRead, gaps + 1); continue; }
                var frames = (int)Math.Min(Math.Min(written - read, MasterTapLimits.MaxChunkFrames), gapAt - read);
                var at = (int)(read & (RingFrames - 1));
                var first = Math.Min(frames, RingFrames - at);
                Array.Copy(_ring, at * 2, chunk, 0, first * 2);
                if (first < frames) Array.Copy(_ring, 0, chunk, first * 2, (frames - first) * 2);
                var gain = Gain;
                for (var i = 0; i < frames; i++) { left[i] = chunk[2 * i] * gain; right[i] = chunk[2 * i + 1] * gain; }
                limiter.Process(left, right, frames);
                for (var i = 0; i < frames; i++) { chunk[2 * i] = left[i]; chunk[2 * i + 1] = right[i]; }
                if (generation != _generation) return;   // a restart retired this run: the block is dropped, not forwarded
                try { sink(_rate, read + skipped, chunk, frames); } catch (IOException) { } // swallow: a block the sink cannot take is dropped
                read += frames;
                if (generation != _generation) return;   // the sink outlived the restart: its counter belongs to the new run
                Volatile.Write(ref _read, read);
            }
            if (!wasOn) return;
            Thread.Sleep(5);
        }
    }
}

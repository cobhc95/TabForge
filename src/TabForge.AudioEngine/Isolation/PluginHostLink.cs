using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

/// <summary>
/// The audio half of an isolated plug-in: each block goes to the plug-in's process through a
/// <see cref="PluginHostBlock"/> and must come back within the callback's budget. Every request carries a new
/// generation number that the plug-in process echoes when it finishes; only the answer for the current generation
/// is accepted. A missed deadline bypasses the block (effects dry, instruments silent) and leaves the in-flight buffers
/// alone; blocks stay bypassed while the child is busy with that generation and normal processing resumes once it reports
/// it done. The link fails for good (<c>onFailed</c> once, shared buffers never written again) only when the child is still
/// busy after the grace period (2 s), misses over 25 % of the blocks of a 5 s window, or misses an offline block.
/// <see cref="Process"/> is allocation-free and lock-free.
/// </summary>
public sealed class PluginHostLink
{
    /// <summary>The wait for one realtime block: 75 % of the block's duration, never under 2 ms nor over 40 ms.</summary>
    public const double MinBudgetMs = 2, MaxBudgetMs = 40, BlockShare = 0.75;
    /// <summary>Offline (render) blocks are not realtime: a slow plug-in may take this long before it counts as hung.</summary>
    public const double OfflineBudgetMs = 30000, ModeSwitchBudgetMs = 15000;

    /// <summary>Realtime miss tolerance: a child still busy with a missed block after this long is failed; so is one missing over this share of blocks in a window.</summary>
    public const double DefaultGraceMs = 2000, DefaultWindowMs = 5000, MaxMissShare = 0.25;

    private readonly PluginHostBlock _block;
    private readonly int _sampleRate;
    private readonly Action _onFailed;
    private readonly long _graceTicks, _windowTicks;
    private int _generation;
    private int _failed;
    private volatile bool _offline;
    // Audio thread only: the missed generation the child is still working on (0 = none), since when; the miss window.
    private int _outstanding;
    private long _outstandingSince, _windowStart;
    private int _windowBlocks, _windowMisses;

    public PluginHostLink(PluginHostBlock block, int sampleRate, Action onFailed, double graceMs = DefaultGraceMs, double windowMs = DefaultWindowMs)
    {
        _block = block;
        _sampleRate = Math.Max(1, sampleRate);
        _onFailed = onFailed;
        _graceTicks = (long)(graceMs * System.Diagnostics.Stopwatch.Frequency / 1000);
        _windowTicks = (long)(windowMs * System.Diagnostics.Stopwatch.Frequency / 1000);
    }

    /// <summary>Blocks bypassed so far because the child missed a deadline or was still busy (for tests / diagnostics).</summary>
    public long MissedBlocks { get; private set; }

    /// <summary>Effects pass the input through while the link is down; instruments are silent.</summary>
    public bool IsInstrument { get; set; }
    /// <summary>True once a request went unanswered (or <see cref="Stop"/> was called): the shared block is no longer touched.</summary>
    public bool Failed => Volatile.Read(ref _failed) != 0;

    public static double BudgetMs(int frames, int sampleRate) =>
        Math.Clamp(frames * 1000.0 * BlockShare / Math.Max(1, sampleRate), MinBudgetMs, MaxBudgetMs);

    /// <summary>Audio thread (or a render worker).</summary>
    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
    {
        frames = Math.Min(frames, _block.MaxBlock);
        if (Failed) { PassThrough(input, output, frames); return; }
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_outstanding != 0)
        {
            // The child still owns the in-flight buffers until it reports that generation done: bypass, never touch them.
            if (Volatile.Read(ref _block.DoneGeneration) != _outstanding)
            {
                PassThrough(input, output, frames);
                if (Miss(now) || now - _outstandingSince > _graceTicks) Fail();
                return;
            }
            _outstanding = 0;   // it caught up: resume normal processing with a fresh generation
        }
        input[0].AsSpan(0, frames).CopyTo(_block.Channel(0));
        input[1].AsSpan(0, frames).CopyTo(_block.Channel(1));
        var count = Math.Min(midi.Length, PluginHostBlock.MaxEvents);
        midi[..count].CopyTo(_block.Events);
        _block.EventCount = count;
        _block.Frames = frames;
        _block.Tempo = transport.Tempo; _block.Ppq = transport.PpqPosition; _block.Playing = transport.Playing ? 1 : 0;
        _block.BarStartPpq = transport.Meter.BarStartPpq; _block.TimeSigNumerator = transport.Meter.Numerator; _block.TimeSigDenominator = transport.Meter.Denominator;
        var generation = ++_generation;
        _block.Send(generation);
        if (_block.WaitDone(generation, _offline ? OfflineBudgetMs : BudgetMs(frames, _sampleRate)))
        {
            _block.Channel(2)[..frames].CopyTo(output[0]);
            _block.Channel(3)[..frames].CopyTo(output[1]);
            if (Tally(now, missed: false)) Fail();
            return;
        }
        // No answer in time: the request is still in flight, so its buffers stay the plug-in process's until it reports done.
        PassThrough(input, output, frames);
        if (_offline) { Fail(); return; }   // a render block waited the full offline budget: hung
        _outstanding = generation;
        _outstandingSince = now;
        if (Miss(now)) Fail();
    }

    private bool Miss(long now) { MissedBlocks++; return Tally(now, missed: true); }

    /// <summary>Counts one block in the current miss window; true when a finished window missed more than <see cref="MaxMissShare"/>.</summary>
    private bool Tally(long now, bool missed)
    {
        if (_windowStart == 0) _windowStart = now;
        _windowBlocks++;
        if (missed) _windowMisses++;
        if (now - _windowStart < _windowTicks) return false;
        var tooMany = _windowMisses > _windowBlocks * MaxMissShare;
        _windowStart = now; _windowBlocks = 0; _windowMisses = 0;
        return tooMany;
    }

    /// <summary>Engine main thread, while nothing processes: switches the plug-in process's mode (an empty block carries the request).</summary>
    public void SetOfflineMode(bool offline)
    {
        _offline = offline;
        if (Failed) return;
        if (_outstanding != 0)
        {
            if (!_block.WaitDone(_outstanding, ModeSwitchBudgetMs)) { Fail(); return; }
            _outstanding = 0;
        }
        _block.Offline = offline ? 1 : 0;
        _block.Frames = 0; _block.EventCount = 0;
        var generation = ++_generation;
        _block.Send(generation);
        if (!_block.WaitDone(generation, ModeSwitchBudgetMs)) Fail();
    }

    /// <summary>Marks the link down without reporting (the process already ended or is being closed).</summary>
    public void Stop() => Volatile.Write(ref _failed, 1);

    private void Fail()
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0) _onFailed();
    }

    private void PassThrough(float[][] input, float[][] output, int frames)
    {
        for (var c = 0; c < output.Length; c++)
            if (IsInstrument) output[c].AsSpan(0, frames).Clear(); else input[Math.Min(c, input.Length - 1)].AsSpan(0, frames).CopyTo(output[c]);
    }
}

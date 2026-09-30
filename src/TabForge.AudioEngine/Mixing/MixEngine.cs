using System.Diagnostics;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

/// <summary>
/// The audio callback: takes timed MIDI from the shared ring, places each message at its frame, renders every
/// track chain and sums them. Runs on the device thread; allocation-free after construction.
/// Timing: a message stamped for time t is heard at t + <see cref="DelayTicks"/>; TabForge delays its Windows MIDI
/// output by the same amount so plug-in tracks and MIDI tracks stay together.
/// </summary>
public sealed class MixEngine : ISampleProvider
{
    /// <summary>Track slots 0..255.</summary>
    public const int MaxSlots = 256;
    /// <summary>Group bus chains live in slots BusBase..BusBase+MaxBuses-1, the master chain in MasterSlot, the monitor chain (live output only, never rendered) in MonitorSlot.</summary>
    public const int BusBase = 256, MaxBuses = 32, MasterSlot = BusBase + MaxBuses, MonitorSlot = MasterSlot + 1, TotalSlots = MonitorSlot + 1;

    /// <summary>
    /// The routing graph, built by the engine thread and swapped in whole: track render order (sidechain / MIDI-forward sources
    /// before destinations), each track's destination bus slot (-1: master), and the bus slots that have a chain.
    /// </summary>
    public sealed class RenderGraph
    {
        public static readonly RenderGraph Empty = new(Array.Empty<int>(), NewDest(), Array.Empty<int>());
        public readonly int[] Order, Dest, Buses;
        public RenderGraph(int[] order, int[] dest, int[] buses) { Order = order; Dest = dest; Buses = buses; }
        public static int[] NewDest() { var d = new int[MaxSlots]; Array.Fill(d, -1); return d; }
    }

    private RenderGraph _graph = RenderGraph.Empty;
    /// <summary>Engine thread: publishes a new routing graph.</summary>
    public void SetGraph(RenderGraph graph) => Volatile.Write(ref _graph, graph);
    public RenderGraph Graph => Volatile.Read(ref _graph);
    private readonly float[][] _busL = new float[MaxBuses][], _busR = new float[MaxBuses][];
    private readonly float[] _masterInL, _masterInR;
    private readonly SharedBlock _shared;
    private readonly int _maxBlock;
    private readonly float[] _left, _right;
    private readonly TimedMidi[] _pending = new TimedMidi[4096];
    private int _pendingCount;
    private TrackChain?[] _chains = new TrackChain?[TotalSlots];
    private readonly double _ticksPerFrame;
    private long _heartbeat = Stopwatch.GetTimestamp();

    /// <summary>When the audio callback last finished (the engine's watchdog checks it).</summary>
    public long Heartbeat => Volatile.Read(ref _heartbeat);
    private TransportInfo _transport = new() { Tempo = 120 };
    /// <summary>Song position, tempo and bar map (RT-08: shared with the engine's command reader, survives a mixer swap).</summary>
    private readonly SongTransport _song;
    private int _barCursor;

    public WaveFormat WaveFormat { get; }
    /// <summary>Output latency of the device, in Stopwatch ticks.</summary>
    public long OutputLatencyTicks { get; set; }
    /// <summary>Total delay from a message's time stamp to it being heard.</summary>
    public long DelayTicks => OutputLatencyTicks + (long)(_maxBlock * _ticksPerFrame) + Stopwatch.Frequency / 200;

    /// <summary>Master level applied to the output (1 = unchanged); follows the Windows volume when that option is on.</summary>
    public volatile float MasterGain = 1f;

    /// <summary>
    /// Output safety clamp (±). 1 for outputs that convert to integers (ASIO, exclusive, DirectSound). WASAPI shared hands floats
    /// to the Windows mixer, which applies the volume before its own final clip, exactly as for the Windows GS synth (whose kicks
    /// peak about +8 dBFS on this scale): there it is raised so the calibrated GM synth is not clipped where GS is not.
    /// </summary>
    public volatile float Ceiling = 1f;

    /// <param name="transport">The engine's song transport (null: a private one, e.g. for tests).</param>
    public MixEngine(SharedBlock shared, int sampleRate, int maxBlock, SongTransport? transport = null)
    {
        _song = transport ?? new SongTransport();
        _shared = shared;
        _maxBlock = maxBlock;
        _inputBlock = new Audio.InputBlock(maxBlock);
        _left = new float[maxBlock];
        _right = new float[maxBlock];
        _masterInL = new float[maxBlock];
        _masterInR = new float[maxBlock];
        for (var b = 0; b < MaxBuses; b++) { _busL[b] = new float[maxBlock]; _busR[b] = new float[maxBlock]; }
        _ticksPerFrame = (double)Stopwatch.Frequency / sampleRate;
        _tpfEst = _ticksPerFrame;
        _sampleRate = sampleRate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    // Last program / controller / pitch-bend value each slot's MIDI set on each channel (255 = never set). Kept
    // even while the slot has no chain yet, and replayed into any chain that newly appears in a slot, so a synth
    // created after the song's setup was sent (cold start, tab switch, chain rebuild) never falls back to piano.
    private readonly byte[] _ccState = CreateUnset(MaxSlots * 16 * 128);
    private readonly byte[] _programState = CreateUnset(MaxSlots * 16);
    private readonly short[] _bendState = new short[MaxSlots * 16];
    private readonly TrackChain?[] _primed = new TrackChain?[MaxSlots];
    /// <summary>Slots whose General MIDI synth is switched off (engine thread writes; applied to every chain that newly appears in the slot). Static: survives a device restart.</summary>
    public static readonly bool[] SynthOff = new bool[TotalSlots];

    private static byte[] CreateUnset(int length) { var a = new byte[length]; Array.Fill(a, (byte)255); return a; }

    private void RememberChannelState(int slot, byte status, byte d1, byte d2)
    {
        var ch = status & 0x0F;
        switch (status & 0xF0)
        {
            case 0xB0 when d1 < 120: _ccState[(slot * 16 + ch) * 128 + d1] = d2; break;
            case 0xC0: _programState[slot * 16 + ch] = d1; break;
            case 0xE0: _bendState[slot * 16 + ch] = (short)(1 + (d1 | (d2 << 7))); break;
        }
    }

    /// <summary>Audio thread: a chain that is new in its slot gets the slot's remembered channel state first.</summary>
    private void PrimeNewChains(TrackChain?[] chains)
    {
        for (var slot = 0; slot < chains.Length && slot < MaxSlots; slot++)
        {
            var chain = chains[slot];
            if (ReferenceEquals(chain, _primed[slot]))
            {
                // Same chain, but its General MIDI synth was just switched back on: it skipped the MIDI while off, so replay the state.
                if (chain is null || !chain.Reprime) continue;
                chain.Reprime = false;
            }
            else
            {
                _primed[slot] = chain;
                if (chain is null) continue;
                if (SynthOff[slot]) chain.SetSynthOn(false);
                chain.Reprime = false;
            }
            for (var ch = 0; ch < 16; ch++)
            {
                var baseIndex = (slot * 16 + ch) * 128;
                // Bank select before the program change, then the other controllers.
                if (_ccState[baseIndex] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), 0, _ccState[baseIndex]);
                if (_ccState[baseIndex + 32] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), 32, _ccState[baseIndex + 32]);
                if (_programState[slot * 16 + ch] != 255) chain.AddEvent(0, (byte)(0xC0 | ch), _programState[slot * 16 + ch], 0);
                for (var cc = 1; cc < 120; cc++)
                    if (cc != 32 && _ccState[baseIndex + cc] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), (byte)cc, _ccState[baseIndex + cc]);
                var bend = _bendState[slot * 16 + ch] - 1;
                if (bend >= 0) chain.AddEvent(0, (byte)(0xE0 | ch), (byte)(bend & 0x7F), (byte)(bend >> 7));
            }
        }
    }

    /// <summary>Main thread: replaces the chain in a slot (the old one is returned for deferred disposal).</summary>
    public TrackChain? SetChain(int slot, TrackChain? chain)
    {
        if (slot is < 0 or >= TotalSlots) return chain;
        var copy = (TrackChain?[])Volatile.Read(ref _chains).Clone();
        var old = copy[slot];
        copy[slot] = chain;
        Volatile.Write(ref _chains, copy);
        return old;
    }

    // Source slot -> destination slots whose instrument takes that track's MIDI (built by the engine thread, swapped whole).
    private int[]?[] _fanout = new int[]?[MaxSlots];

    /// <summary>Engine thread: publishes the MIDI routing table (index = source slot).</summary>
    public void SetFanout(int[]?[] table) => Volatile.Write(ref _fanout, table);

    /// <summary>The MIDI routing table (index = source slot): destination slots whose instrument takes that track's MIDI.</summary>
    public int[]?[] Fanout => Volatile.Read(ref _fanout);

    /// <summary>The chain in a slot, or null.</summary>
    public TrackChain? ChainAt(int slot) => slot is >= 0 and < TotalSlots ? Volatile.Read(ref _chains)[slot] : null;

    public IEnumerable<TrackChain> Chains => Volatile.Read(ref _chains).Where(c => c is not null)!;

    /// <summary>Any thread: the song tempo used when there is no bar map (playing comes with <see cref="SetPosition"/>).</summary>
    public void SetTransport(double tempo, bool playing) => _song.SetTempo(tempo);

    /// <summary>Any thread: the song's bar map (RT-04: time signature, bar start and tempo per performed bar).</summary>
    public void SetTransportMap(TransportBar[] bars) => _song.SetMap(bars);

    /// <summary>The song transport is playing (pitch measurement waits for it to stop).</summary>
    public bool SongPlaying => _song.Current.Playing;

    /// <summary>Song position: <paramref name="songSec"/> at Stopwatch time <paramref name="stamp"/> (the MIDI time-stamp clock).</summary>
    public void SetPosition(bool playing, double songSec, long stamp) => _song.SetPosition(playing, songSec, stamp);

    /// <summary>The song time now, on the time-stamp clock (what TabForge's scheduler is sending).</summary>
    public double SongSecNow => _song.Current.SecAt(Stopwatch.GetTimestamp());

    private readonly Audio.InputBlock _inputBlock;

    /// <summary>Audio input for monitoring (null when no track is armed).</summary>
    public volatile Audio.InputCapture? Input;

    public void Panic()
    {
        foreach (var chain in Chains) chain.Panic();
    }

    // Delay-locked loop state (audio thread only, except the reset flag).
    private readonly int _sampleRate;
    private double _tpfEst;          // estimated ticks per frame
    private double _nextStart;       // predicted start of the next callback, in ticks
    private bool _clockValid;
    private volatile bool _clockReset;
    private const double DllBandwidthHz = 1.0;

    /// <summary>Forces the callback clock to re-anchor on the next block (stream restart, device change).</summary>
    public void ResetClock() => _clockReset = true;

    private double FilterCallTime(long raw, int frames)
    {
        if (_clockReset) { _clockReset = false; _clockValid = false; }
        if (frames <= 0) return _clockValid ? _nextStart : raw;
        if (!_clockValid || Math.Abs(raw - _nextStart) > Stopwatch.Frequency / 20)
        {
            _clockValid = true;
            _tpfEst = _ticksPerFrame;
            _nextStart = raw + frames * _tpfEst;
            return raw;
        }
        var err = raw - _nextStart;
        var b = 2 * Math.PI * DllBandwidthHz * frames / _sampleRate;
        var c = b * b / 2;
        var start = _nextStart + b * err;
        _tpfEst = Math.Clamp(_tpfEst + c * err / frames, _ticksPerFrame * 0.995, _ticksPerFrame * 1.005);
        _nextStart = start + frames * _tpfEst;
        return start;
    }

    /// <summary>
    /// Audio thread, one block: track chains in graph order (each post-fader into its group bus, or the master sum when the bus
    /// has no chain), then the bus chains into the master sum, then plug-in MIDI forwarding (heard next block), then the master
    /// chain over the whole mix. Result in _left / _right. No allocation.
    /// </summary>
    private void RenderGraphBlock(TrackChain?[] chains, RenderGraph graph, int n, Audio.InputBlock? input)
    {
        foreach (var bus in graph.Buses) { Array.Clear(_busL[bus - BusBase], 0, n); Array.Clear(_busR[bus - BusBase], 0, n); }
        foreach (var slot in graph.Order)
        {
            if (slot >= chains.Length || chains[slot] is not { } chain) continue;
            var dest = graph.Dest[slot];
            if (dest >= BusBase && dest < MasterSlot && chains[dest] is not null)
                chain.Render(_busL[dest - BusBase], _busR[dest - BusBase], 0, n, _transport, _shared, input, chains);
            else chain.Render(_left, _right, 0, n, _transport, _shared, input, chains);
        }
        foreach (var bus in graph.Buses)
            chains[bus]?.Render(_left, _right, 0, n, _transport, _shared, null, null, _busL[bus - BusBase], _busR[bus - BusBase]);
        foreach (var slot in graph.Order) if (slot < chains.Length) chains[slot]?.DeliverForwards(chains);
        if (chains[MasterSlot] is { } master)
        {
            Array.Copy(_left, _masterInL, n); Array.Copy(_right, _masterInR, n);
            Array.Clear(_left, 0, n); Array.Clear(_right, 0, n);
            master.Render(_left, _right, 0, n, _transport, _shared, null, null, _masterInL, _masterInR);
        }
        // Monitor chain (speaker / room calibration): after the master chain, live output only. OfflineRenderer never runs this method, so renders exclude it.
        if (chains[MonitorSlot] is { } monitor)
        {
            Array.Copy(_left, _masterInL, n); Array.Copy(_right, _masterInR, n);
            Array.Clear(_left, 0, n); Array.Clear(_right, 0, n);
            monitor.Render(_left, _right, 0, n, _transport, _shared, null, null, _masterInL, _masterInR);
        }
    }

    /// <summary>Odd while <see cref="Read"/> runs: replaced chains / plug-ins / clips are disposed only once it has moved on (<see cref="RetireQueue"/>).</summary>
    public CallbackEpoch Epoch { get; } = new();

    /// <summary>Audio callback timing (duration vs deadline, regularity, allocations); summarised in the engine log.</summary>
    public readonly CallbackMetrics Metrics = new();

    public int Read(float[] buffer, int offset, int count)
    {
        Epoch.Enter();   // before the chains are read
        try { return ReadBlock(buffer, offset, count); }
        finally { Epoch.Exit(); }
    }

    private int ReadBlock(float[] buffer, int offset, int count)
    {
        // Per thread (a thread-static read): a device reopened on the same mixer (after an offline render) calls from a new thread.
        if (!EngineThreads.IsAudioThread) EngineThreads.MarkAudioThread();
        var started = Stopwatch.GetTimestamp();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var chains = Volatile.Read(ref _chains);
        var fanout = Volatile.Read(ref _fanout);
        var frames = count / 2;
        // Where "frame 0 of this call" is on the shared clock, shifted back by the fixed delay - smoothed by a
        // delay-locked loop so device-callback jitter and the true sample rate do not move event placement.
        var rawCallTime = started + OutputLatencyTicks - DelayTicks;
        var callTime = FilterCallTime(rawCallTime, frames);

        while (_pendingCount < _pending.Length && _shared.TryRead(out var e)) _pending[_pendingCount++] = e;
        // M-05: the pending list is full: the rest stays in the shared ring for a later callback (late, not lost). Counted.
        if (_pendingCount == _pending.Length) Metrics.CountMidiDeferred();
        var song = _song.Current;
        var bars = _song.Bars;
        var songTempo = _song.Tempo;

        var done = 0;
        while (done < frames)
        {
            var n = Math.Min(_maxBlock, frames - done);
            var blockStart = callTime + done * _tpfEst;
            var blockEnd = blockStart + n * _tpfEst;
            // Hand every message due in this block to its chain, at its frame; keep later ones.
            var keep = 0;
            if (done == 0) PrimeNewChains(chains);
            for (var i = 0; i < _pendingCount; i++)
            {
                ref var e = ref _pending[i];
                if (e.Timestamp < blockEnd)
                {
                    // Earlier than the block start (stalled producer) still plays, at frame 0.
                    var frame = (int)Math.Clamp(Math.Round((e.Timestamp - blockStart) / _tpfEst), 0, n - 1);
                    if (e.Slot is >= 0 and < MaxSlots)
                    {
                        RememberChannelState(e.Slot, e.Status, e.Data1, e.Data2);
                        chains[e.Slot]?.AddEvent(frame, e.Status, e.Data1, e.Data2);   // a full chain buffer counts the drop (M-05)
                        // Instruments that take this track's MIDI as their input (filtered by channel in the chain).
                        if (fanout[e.Slot] is { } destinations)
                            foreach (var d in destinations) chains[d]?.AddRouted(frame, e.Status, e.Data1, e.Data2);
                    }
                }
                else _pending[keep++] = e;
            }
            _pendingCount = keep;

            Array.Clear(_left, 0, n);
            Array.Clear(_right, 0, n);
            _transport.Playing = song.Playing;
            _transport.SongSec = song.SecAt(blockStart);
            // RT-04: ppq position, tempo, time signature and bar start from the song's bar map (none yet: the plain tempo, no meter).
            if (TransportMap.Locate(bars, _transport.SongSec, ref _barCursor, out var ppq, out var barTempo, out var meter))
            {
                _transport.PpqPosition = ppq; _transport.Tempo = barTempo; _transport.Meter = meter;
            }
            else { _transport.Tempo = songTempo; _transport.Meter = default; }
            // Live input: read once per block and shared by every armed chain (each applies its own channel choice).
            Audio.InputBlock? inputBlock = null;
            if (Input is { } input)
            {
                input.Read(_inputBlock.L, _inputBlock.R, n);
                _inputBlock.Frames = n;
                inputBlock = _inputBlock;
            }
            RenderGraphBlock(chains, Volatile.Read(ref _graph), n, inputBlock);
            var master = MasterGain; var ceiling = Ceiling;
            // RT-02 last line of defence (each chain already mutes its own non-finite output): Math.Clamp keeps NaN, so a
            // non-finite master block is silenced whole. x - x is 0 for finite x and NaN otherwise; the sum is branch-free.
            var poison = 0f;
            for (var i = 0; i < n; i++) poison += (_left[i] - _left[i]) + (_right[i] - _right[i]);
            if (poison != 0f) { Array.Clear(_left, 0, n); Array.Clear(_right, 0, n); }
            if (!float.IsFinite(master)) master = 0f;
            for (var i = 0; i < n; i++)
            {
                buffer[offset + 2 * (done + i)] = Math.Clamp(_left[i] * master, -ceiling, ceiling);
                buffer[offset + 2 * (done + i) + 1] = Math.Clamp(_right[i] * master, -ceiling, ceiling);
            }
            done += n;
        }
        Volatile.Write(ref _heartbeat, Stopwatch.GetTimestamp());
        var ended = Stopwatch.GetTimestamp();
        var elapsed = ended - started;
        _shared.CpuLoad = elapsed / Math.Max(1.0, frames * _ticksPerFrame);
        Metrics.Record(started, ended, frames, _sampleRate, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        return count;
    }
}

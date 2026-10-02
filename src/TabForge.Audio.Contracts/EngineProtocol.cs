using System.IO;
using System.Text;

namespace TabForge.Audio.Contracts;

/// <summary>Commands from TabForge to the audio engine (named pipe, length-prefixed frames).</summary>
public enum EngineCommand : byte
{
    Configure = 1,
    SetTrack = 2, // reserved, never sent; do not reuse the value
    LoadChain = 3,
    RemoveTrack = 4,
    OpenEditor = 5,
    /// <summary>Every plug-in state of a chain (slot, request id); answered by one <see cref="EngineEvent.PluginState"/> frame per plug-in.</summary>
    GetStates = 6,
    Panic = 7,
    /// <summary>
    /// Song tempo (double) and playing (bool, ignored: <see cref="SetPosition"/> carries it); then (optional, at the tail) the song's bar
    /// map, <see cref="TransportMap.Write"/>: per performed bar its start (song seconds), start ppq, tempo and time signature; after the
    /// map (optional) the owner id, an int: which open song's transport this describes (<see cref="SongOwners.Max"/> ids; absent: owner 0).
    /// </summary>
    SetTransport = 9,
    CloseEditor = 10,
    GetPrograms = 11,
    SetProgram = 12,
    SetState = 13,
    /// <summary>Track level and pan (slot, volume 0..127, pan 0..127) for audio clips, input and plug-ins.</summary>
    SetTrackMix = 14,
    /// <summary>A track's audio clips (slot, clip list); then (optional) the owner id of the song that plays them, absent: owner 0.</summary>
    SetClips = 15,
    /// <summary>Record-arm / input monitoring (slot, armed, input mode 0 = in 1, 1 = in 2, 2 = stereo).</summary>
    SetArm = 16,
    /// <summary>Song position: playing, song seconds at a Stopwatch timestamp; then (optional) the owner id of the song, absent: owner 0.</summary>
    SetPosition = 17,
    /// <summary>Start (true, folder) or stop (false) recording the armed tracks; after the offset (optional) the owner id of the recording song, absent: owner 0.</summary>
    Record = 18,
    /// <summary>One plug-in's output volume (slot, chain index, dB): a light update, ramped in the engine, no chain rebuild.</summary>
    SetPluginGain = 19,
    /// <summary>One plug-in's bypass (slot, chain index, enabled): the plug-in stays loaded, the engine just stops processing it. No chain rebuild.</summary>
    SetPluginBypass = 20,
    /// <summary>One instrument's MIDI input routing (slot, chain index, source track slot: -1 own, -2 none; channel mask, bit n = channel n+1). Live, no chain rebuild.</summary>
    SetMidiRoute = 23,
    /// <summary>A chain's MIDI processors, every plug-in at once (slot, count, then per plug-in: index, list of type / enabled / JSON parameters). Each list runs right before its plug-in. Live, no chain rebuild; whole new immutable chains are built engine-side and swapped in.</summary>
    SetMidiProcessors = 21,
    /// <summary>
    /// The routing graph, whole (replaces the previous one): count + (track slot, destination bus slot or -1 master); count + sidechains
    /// (slot, plug-in index, source slot); count + MIDI-output forwards (slot, plug-in index, destination slot). Bus slots 256..287, master 288.
    /// </summary>
    SetGraph = 26,
    /// <summary>Start / stop watching a track's MIDI log (slot, watching); while watched the engine sends <see cref="EngineEvent.MidiLog"/> about 20 times a second.</summary>
    SetMidiLogWatch = 24,
    /// <summary>One plug-in's serial-chain options (slot, chain index, flags: bit 0 pass incoming MIDI on, bit 1 send its MIDI output on, bit 2 instrument audio replaces). Live, no chain rebuild.</summary>
    SetPluginWiring = 25,
    /// <summary>The chain's General MIDI synth sounds or not (slot, on). Live, no chain rebuild: GM taking over from a bypassed instrument stays in time and keeps the editors open.</summary>
    SetSynth = 27,
    /// <summary>Offline render: a <see cref="RenderSpec"/>. The engine stops its device, renders, writes the files, reopens the device (see RenderProtocol.cs).</summary>
    RenderOffline = 30,
    /// <summary>Cancels a running render (handled on the command reader thread, so it works while the engine is busy rendering).</summary>
    RenderCancel = 31,
    /// <summary>
    /// One plug-in's saved state for a pending <see cref="LoadChain"/> (slot, load id, chain index, plug-in id, base64 state): sent right after the
    /// chain spec, one frame per plug-in, so a chain's states never have to fit one frame together.
    /// </summary>
    ChainState = 32,
    /// <summary>All <see cref="ChainState"/> frames of a load were sent (slot, load id): the engine builds the chain now.</summary>
    ChainCommit = 33,
    /// <summary>Measure an instrument's sounding pitch silently (slot, chain index, request id, channel 0..15, note count, notes); answered by <see cref="EngineEvent.PitchMeasured"/>.</summary>
    MeasurePitch = 34,
    /// <summary>Automatic pitch-match transposes of a chain's instruments (slot, count, then chain index + semitones each; unlisted instruments: 0). Live.</summary>
    SetAutoPitch = 35,
    /// <summary>Extra attenuation (dB, float) the Windows audio path applies beyond the endpoint volume and TabForge's session, measured by the editor; followed under ASIO.</summary>
    SetWindowsPathOffset = 36,
    /// <summary>Liveness probe (sequence number, int), answered by <see cref="EngineEvent.Pong"/> from the engine's main thread (not the reader).</summary>
    Ping = 37,
    /// <summary>
    /// Test hook: the engine main thread sleeps (seconds, int, 1..120) as a deaf engine would. Ignored unless the engine process was
    /// started with TABFORGE_ENGINE_TEST_HOOKS=1 in its environment (the self-test sets it; users never do).
    /// </summary>
    TestHang = 38,
    /// <summary>All notes off on some slots only (count, slots): one document stops while other open documents keep playing.</summary>
    PanicSlots = 39,
    /// <summary>Live master safety limiter on / off (bool). Off by default (A7-A01); renders have their own switch in the render spec.</summary>
    SetLiveLimiter = 40,
    Shutdown = 99,
}

/// <summary>Owner ids: each open song has its own transport (position, tempo, bar map) in the engine, so songs that play at the same time keep their own clip timing.</summary>
public static class SongOwners
{
    /// <summary>Owner ids are 0 .. Max - 1; the engine allocates nothing per id after start.</summary>
    public const int Max = 16;
}

/// <summary>What a main-thread plug-in call does, written into the breadcrumb: the engine watchdog allows each kind its own time.</summary>
public enum PluginCallKind
{
    /// <summary>Anything else on the main thread (programs, idle, misc.): 10 s.</summary>
    Other = 0,
    /// <summary>Creating the plug-in (including its saved state) or switching it to a new device rate: 90 s.</summary>
    Load = 1,
    /// <summary>Applying a preset / state to a running plug-in: 90 s.</summary>
    SetState = 2,
    /// <summary>Reading its state (save, preset capture): 30 s.</summary>
    GetState = 3,
    /// <summary>Opening its editor window: 20 s.</summary>
    Editor = 4,
}

/// <summary>
/// Watchdog policy: how long a plug-in call of each kind may block the engine main thread before the engine ends itself
/// and the plug-in is blamed (exit 70), how long unattributed work may (exit 71, nothing quarantined), and when a call counts as slow
/// (TabForge says "still loading" instead of treating it as a crash).
/// </summary>
public static class EngineWatchdog
{
    /// <summary>70: a main-thread plug-in call passed its limit; 71: unattributed main-thread hang (nothing blamed); 72: the audio thread froze (its own breadcrumb is blamed).</summary>
    public const int ExitPluginHung = 70, ExitUnattributedHang = 71, ExitAudioHung = 72;
    /// <summary>Main thread busy this long outside any plug-in call: the engine ends itself (exit 71).</summary>
    public const double UnattributedLimitSec = 30;
    /// <summary>A plug-in call running this long is reported as slow (once).</summary>
    public const double SlowNoticeSec = 5;
    /// <summary>A plug-in call still running this long is reported a second time; for a load TabForge then asks "Keep waiting / Disable it".</summary>
    public const double LongNoticeSec = 30;
    /// <summary>TabForge restarts an engine that answered no ping for this long while no plug-in call was in progress.</summary>
    public const double DeafLimitSec = 5;

    public static double LimitSec(PluginCallKind kind) => kind switch
    {
        PluginCallKind.Load or PluginCallKind.SetState => 90,
        PluginCallKind.GetState => 30,
        PluginCallKind.Editor => 20,
        _ => 10,
    };

    public enum Verdict { Fine, Slow, PluginHung, UnattributedHang }

    /// <param name="busySec">Seconds the main thread has been inside one queued task (0 = idle).</param>
    /// <param name="callSec">Seconds the current main-thread plug-in call has run; null = none in progress.</param>
    /// <param name="sinceCallSec">Seconds since the last main-thread plug-in call ended (a long chain load is many attributed calls).</param>
    public static Verdict Decide(double busySec, double? callSec, PluginCallKind kind, double sinceCallSec)
    {
        if (callSec is double call)
            return call > LimitSec(kind) ? Verdict.PluginHung : call > SlowNoticeSec && LimitSec(kind) > SlowNoticeSec ? Verdict.Slow : Verdict.Fine;
        return Math.Min(busySec, sinceCallSec) > UnattributedLimitSec ? Verdict.UnattributedHang : Verdict.Fine;
    }
}

/// <summary>Messages from the engine to TabForge.</summary>
public enum EngineEvent : byte
{
    Ready = 1,
    DeviceError = 2,
    PluginFailed = 3,
    States = 4, // reserved, never sent; do not reuse the value
    StateChanged = 6,
    ChainLoaded = 7,
    /// <summary>A plug-in running in its own process crashed or hung (the engine itself is fine).</summary>
    PluginCrashed = 8,
    /// <summary>A plug-in window opened or changed size (slot, index, width, height).</summary>
    EditorSize = 9,
    /// <summary>A plug-in's programs (factory presets): slot, index, current, names.</summary>
    Programs = 10,
    /// <summary>A recording finished: slot, file path, song start seconds, length seconds.</summary>
    Recorded = 11,
    /// <summary>The audio input could not be opened or stopped (message).</summary>
    InputError = 12,
    /// <summary>MIDI log entries of a watched track: slot, count, then per entry time (float), status, data1, data2, stage (bytes).</summary>
    MidiLog = 13,
    /// <summary>Render progress about 10 times a second: fraction 0..1 (double), rendered seconds (double), speed x realtime (double).</summary>
    RenderProgress = 14,
    /// <summary>Render finished and the device is back: a <see cref="RenderResult"/>.</summary>
    RenderDone = 15,
    /// <summary>Render failed or was cancelled (device restored): message, cancelled (bool), plug-in path that hung (string, may be empty).</summary>
    RenderFailed = 16,
    /// <summary>The user closed a floating plug-in window (no payload); TabForge re-activates its window.</summary>
    EditorClosed = 17,
    /// <summary>
    /// One plug-in's state for a <see cref="EngineCommand.GetStates"/> request, one frame per plug-in (so a chain's states never have to fit
    /// one frame together): slot, request id, chain index (-1 when the slot has no live chain), chain count, <see cref="PluginStateStatus"/> (byte),
    /// base64 state ("" unless captured).
    /// </summary>
    PluginState = 18,
    /// <summary>
    /// Answer to <see cref="EngineCommand.MeasurePitch"/>: slot, chain index, request id, <see cref="PitchMatch.Status"/> (byte), transpose (int),
    /// confidence (double), note count, then per note: note (int), sounding offset in semitones (double, NaN unpitched).
    /// </summary>
    PitchMeasured = 19,
    /// <summary>Answer to <see cref="EngineCommand.Ping"/> (the same sequence number), sent by the engine's main thread.</summary>
    Pong = 20,
    /// <summary>
    /// A main-thread plug-in call is slow but still within its limit (slot, chain index, path, <see cref="PluginCallKind"/> (int), seconds so far
    /// (int)): TabForge shows "still loading", it is not a crash.
    /// </summary>
    PluginSlow = 21,
    /// <summary>
    /// A plug-in produced non-finite audio (NaN / infinity): the engine discarded it and now skips that plug-in (slot, chain index,
    /// path); index -1 means the track's own sound (General MIDI synth, clip or input) was non-finite and that block was silenced. Once each.
    /// </summary>
    PluginMisbehaved = 22,
    /// <summary>A recording lost input to a slow disk (the take keeps its length; the lost stretches are silence): one summary line (message), sent after the Recorded events.</summary>
    RecordingLoss = 23,
}

/// <summary>What the engine could read for one plug-in's state.</summary>
public enum PluginStateStatus : byte
{
    /// <summary>Read now (the base64 text follows).</summary>
    Captured = 0,
    /// <summary>The plug-in exposes no state, or is not loaded (quarantined / failed): the stored state is still its state.</summary>
    NoState = 1,
    /// <summary>The plug-in's state getter failed.</summary>
    Failed = 2,
    /// <summary>The state is larger than <see cref="PluginStateLimits.MaxRawBytes"/>.</summary>
    TooLarge = 3,
    /// <summary>The plug-in (in its own process) did not answer the state request in time.</summary>
    TimedOut = 4,
}

/// <summary>
/// The single plug-in state size contract, used by the project validator, chain / preset loading, live SetState, the native getters and
/// the state transfer: at most <see cref="MaxRawBytes"/> raw bytes, i.e. <see cref="MaxBase64Chars"/> base64 characters as stored.
/// One state always fits one IPC frame (<see cref="Frames.MaxFrameBytes"/>), states travel one plug-in per frame.
/// </summary>
public static class PluginStateLimits
{
    public const int MaxRawBytes = 16 * 1024 * 1024;
    /// <summary>Base64 expands 3 bytes to 4 characters (padded).</summary>
    public const int MaxBase64Chars = (MaxRawBytes + 2) / 3 * 4;
}

/// <summary>One plug-in of a track's chain as sent to the engine.</summary>
/// <remarks><see cref="Id"/> is stable for one plug-in in a chain: the engine keeps that live instance when the chain changes.</remarks>
public sealed record PluginSpec(string Path, string Format, bool IsInstrument, bool Enabled, int Wet, string? State, string Name = "", string Pins = "Stereo", double OutputDb = 0, string Id = "", bool Skip = false, bool Untrusted = false, string ExpectedSha256 = "");   // Skip: a quarantined plug-in that must not be loaded at all (Enabled is only the live bypass). ExpectedSha256: the approved binary's hash, checked by the engine right before each real load (see PluginIdentity); empty = no per-file approval (folder-trusted, engine-internal)

/// <summary>One MIDI processor of a plug-in's list as sent to the engine: type name, enabled, and its parameters as a flat JSON object.</summary>
public sealed record MidiProcSpec(string Type, bool Enabled, string ParamsJson);

/// <summary>Audio device settings sent to the engine.</summary>
/// <remarks>ASIO: inputs on/off with the first and last channel (0-based, 1 or 2 channels); the output pair starts at AsioOutput.</remarks>
public sealed record EngineConfig(string Driver, string Device, int SampleRate, int BufferSize, bool SeparateProcessPerPlugin, string InputDevice = "", int AsioInput = 0, int AsioOutput = 0, int AsioInputLast = 1, bool AsioInputs = true, bool FollowWindowsVolume = true, int AsioOutputLast = 1);

/// <summary>One audio clip to play on a track: file, song position and processing.</summary>
public sealed record ClipSpec(string File, double StartSec, double OffsetSec, double SourceLengthSec, double GainDb, double Pitch, double Speed);

/// <summary>
/// Framing: [int32 length][byte type][payload]. Payloads use BinaryWriter primitives. Every read is bounded so a
/// broken or hostile peer cannot make the other side allocate without limit.
/// </summary>
public static class Frames
{
    public const int MaxFrameBytes = 32 * 1024 * 1024;

    public static void Write(Stream stream, byte type, Action<BinaryWriter>? payload = null)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(type);
            payload?.Invoke(writer);
        }
        if (buffer.Length > MaxFrameBytes) throw new InvalidDataException("Message too large.");
        var header = BitConverter.GetBytes((int)buffer.Length);
        lock (stream)
        {
            stream.Write(header, 0, 4);
            buffer.Position = 0;
            buffer.CopyTo(stream);
            stream.Flush();
        }
    }

    /// <summary>Reads one frame; null when the peer closed the connection.</summary>
    public static (byte Type, BinaryReader Reader)? Read(Stream stream)
    {
        var header = new byte[4];
        if (!ReadExactly(stream, header)) return null;
        var length = BitConverter.ToInt32(header, 0);
        if (length < 1 || length > MaxFrameBytes) throw new InvalidDataException("Bad message length.");
        var body = new byte[length];
        if (!ReadExactly(stream, body)) return null;
        var reader = new BinaryReader(new MemoryStream(body, writable: false), Encoding.UTF8);
        return (reader.ReadByte(), reader);
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public static void WriteString(this BinaryWriter writer, string? value) => writer.Write(value ?? "");

    public static string ReadBoundedString(this BinaryReader reader, int maxChars = 32_768)
    {
        var value = reader.ReadString();
        if (value.Length > maxChars) throw new InvalidDataException("Text too long.");
        return value;
    }

    public static void Write(this BinaryWriter w, EngineConfig c)
    {
        w.WriteString(c.Driver); w.WriteString(c.Device); w.Write(c.SampleRate); w.Write(c.BufferSize); w.Write(c.SeparateProcessPerPlugin); w.WriteString(c.InputDevice); w.Write(c.AsioInput); w.Write(c.AsioOutput); w.Write(c.AsioInputLast); w.Write(c.AsioInputs); w.Write(c.FollowWindowsVolume); w.Write(c.AsioOutputLast);
    }

    public static EngineConfig ReadConfig(this BinaryReader r) =>
        new(r.ReadBoundedString(64), r.ReadBoundedString(256), Math.Clamp(r.ReadInt32(), 8000, 384000), Math.Clamp(r.ReadInt32(), 16, 16384), r.ReadBoolean(), r.ReadBoundedString(256), Math.Clamp(r.ReadInt32(), 0, 62), Math.Clamp(r.ReadInt32(), 0, 62), Math.Clamp(r.ReadInt32(), 0, 63), r.ReadBoolean(), r.ReadBoolean(), Math.Clamp(r.ReadInt32(), 0, 63));

    /// <summary>The owner id at the tail of SetPosition, SetClips and SetTransport: absent (an older sender) or out of range is owner 0.</summary>
    public static int ReadOwnerTail(this BinaryReader r)
    {
        if (r.BaseStream.Position >= r.BaseStream.Length) return 0;
        var owner = r.ReadInt32();
        return owner is >= 0 and < SongOwners.Max ? owner : 0;
    }

    public static void Write(this BinaryWriter w, IReadOnlyList<ClipSpec> clips)
    {
        w.Write(clips.Count);
        foreach (var c in clips)
        {
            w.WriteString(c.File); w.Write(c.StartSec); w.Write(c.OffsetSec); w.Write(c.SourceLengthSec); w.Write(c.GainDb); w.Write(c.Pitch); w.Write(c.Speed);
        }
    }

    public static List<ClipSpec> ReadClips(this BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > 4096) throw new InvalidDataException("Bad clip count.");
        var list = new List<ClipSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var file = r.ReadBoundedString(1024);
            double Finite(double v, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : min;
            list.Add(new ClipSpec(file, Finite(r.ReadDouble(), 0, 86_400), Finite(r.ReadDouble(), 0, 86_400), Finite(r.ReadDouble(), 0, 86_400),
                Finite(r.ReadDouble(), -96, 24), Finite(r.ReadDouble(), -24, 24), Finite(r.ReadDouble(), 0.25, 4)));
        }
        return list;
    }

    public static void Write(this BinaryWriter w, IReadOnlyList<MidiProcSpec> list)
    {
        w.Write(list.Count);
        foreach (var p in list) { w.WriteString(p.Type); w.Write(p.Enabled); w.WriteString(p.ParamsJson); }
    }

    public static List<MidiProcSpec> ReadMidiProcs(this BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > 64) throw new InvalidDataException("Bad MIDI processor count.");
        var list = new List<MidiProcSpec>(count);
        for (var i = 0; i < count; i++) list.Add(new MidiProcSpec(r.ReadBoundedString(48), r.ReadBoolean(), r.ReadBoundedString(64 * 1024)));
        return list;
    }

    public static void Write(this BinaryWriter w, IReadOnlyList<PluginSpec> chain)
    {
        w.Write(chain.Count);
        foreach (var p in chain)
        {
            w.WriteString(p.Path); w.WriteString(p.Format); w.Write(p.IsInstrument); w.Write(p.Enabled); w.Write(p.Wet); w.WriteString(p.State); w.WriteString(p.Name); w.WriteString(p.Pins); w.Write(p.OutputDb); w.WriteString(p.Id); w.Write(p.Skip); w.Write(p.Untrusted); w.WriteString(p.ExpectedSha256);
        }
    }

    public static List<PluginSpec> ReadChain(this BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > 64) throw new InvalidDataException("Bad chain length.");
        var list = new List<PluginSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var path = r.ReadBoundedString(1024); var format = r.ReadBoundedString(8); var inst = r.ReadBoolean();
            var enabled = r.ReadBoolean(); var wet = Math.Clamp(r.ReadInt32(), 0, 100);
            var state = r.ReadBoundedString(PluginStateLimits.MaxBase64Chars);
            var name = r.ReadBoundedString(256);
            var pins = r.ReadBoundedString(32);
            var outputDb = r.ReadDouble();
            var id = r.ReadBoundedString(64);
            var skip = r.ReadBoolean();
            var untrusted = r.ReadBoolean();
            var expectedSha = r.ReadBoundedString(64);
            list.Add(new PluginSpec(path, format, inst, enabled, wet, state.Length == 0 ? null : state, name, pins,
                double.IsFinite(outputDb) ? Math.Clamp(outputDb, -60, 12) : 0, id, skip, untrusted, expectedSha));
        }
        return list;
    }
}

/// <summary>Audio driver names, shared by the settings and the engine.</summary>
public static class AudioDriverNames
{
    public const string WasapiShared = "WASAPI (shared)";
    public const string WasapiExclusive = "WASAPI (exclusive)";
    public const string Asio = "ASIO";
    public const string DirectSound = "DirectSound";
    public static readonly string[] All = { WasapiShared, WasapiExclusive, Asio, DirectSound };
}

/// <summary>Names shared by both sides for one engine session.</summary>
public static class EngineNames
{
    public static string Pipe(string session) => $"TabForge.AudioEngine.{session}";
    public static string SharedMemory(string session) => $"TabForge.AudioEngine.{session}.shm";
}

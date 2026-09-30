using System.IO;
using System.Runtime.InteropServices;

namespace TabForge.Audio.Contracts;

// Offline render ("File > Render"): TabForge sends one RenderSpec (EngineCommand.RenderOffline) plus a sorted event file;
// the engine stops its audio device, renders the loaded chains faster than realtime on parallel workers, writes the WAV
// files itself, sends RenderProgress about 10 times a second and finally RenderDone or RenderFailed, then reopens the
// device. All frame numbers are in frames at the ENGINE's sample rate (AudioEngineClient.Output.Rate); frame 0 = song start.

/// <summary>What is rendered after the end bound (reverb / delay ring-out, release of held notes).</summary>
public enum RenderTailMode : byte
{
    /// <summary>Stop exactly at the end bound.</summary>
    None = 0,
    /// <summary>Render <see cref="RenderSpec.TailMs"/> past the end (at least as much as the plug-ins report as their tail; at most 30 s).</summary>
    Fixed = 1,
    /// <summary>Render until every chain has been below -90 dB for 1 s; <see cref="RenderSpec.TailMs"/> is the cap (0 or more than 30 s = 30 s).</summary>
    Auto = 2,
}

public enum RenderFormat : byte
{
    /// <summary>16-bit PCM with TPDF dither.</summary>
    Pcm16 = 0,
    Pcm24 = 1,
    Float32 = 2,
}

public enum RenderThreads : byte
{
    /// <summary>min(cores - 1, chains) workers, one chain always on the same worker.</summary>
    Auto = 0,
    /// <summary>One worker renders every chain (for plug-ins that misbehave across threads; also pins a crash to one plug-in).</summary>
    One = 1,
}

/// <summary>One timed MIDI message of the render (16 bytes, the record of the event file). Flags is reserved (0).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RenderEvent
{
    /// <summary>Song time in frames (engine rate). Events before <see cref="RenderSpec.StartFrame"/> are delivered in the first block; events at or after the end are ignored.</summary>
    public long Frame;
    /// <summary>The engine slot of the track the message belongs to (its own MIDI; routed instruments get it through their MIDI input routing, as in playback).</summary>
    public int Slot;
    public byte Status, Data1, Data2, Flags;
}

/// <summary>The event file: [int magic 'TFRE'][int version 1][long count][count x <see cref="RenderEvent"/>], sorted by Frame (equal frames keep their order).</summary>
public static class RenderEventFile
{
    private const int Magic = 0x45524654;

    public static void Write(string path, IReadOnlyList<RenderEvent> events)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        using var w = new BinaryWriter(file);
        w.Write(Magic); w.Write(1); w.Write((long)events.Count);
        var chunk = new RenderEvent[4096];
        for (var i = 0; i < events.Count; i += chunk.Length)
        {
            var n = Math.Min(chunk.Length, events.Count - i);
            for (var k = 0; k < n; k++) chunk[k] = events[i + k];
            w.Write(MemoryMarshal.AsBytes(chunk.AsSpan(0, n)));
        }
    }

    public static RenderEvent[] Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var r = new BinaryReader(file);
        if (r.ReadInt32() != Magic || r.ReadInt32() != 1) throw new InvalidDataException("Not a render event file.");
        var count = r.ReadInt64();
        if (count < 0 || count > 200_000_000 || file.Length != 16 + count * 16) throw new InvalidDataException("Bad render event file length.");
        var events = new RenderEvent[count];
        var bytes = MemoryMarshal.AsBytes(events.AsSpan());
        var read = 0;
        while (read < bytes.Length) { var n = file.Read(bytes[read..]); if (n <= 0) throw new EndOfStreamException(); read += n; }
        return events;
    }
}

/// <summary>Per engine slot: mixer values for the render, whether it is in the master, and its stem file.</summary>
public sealed class RenderSlot
{
    public int Slot;
    /// <summary>Track volume / pan (0..127) at the start; -1 keeps the engine's current value. The engine restores the old values afterwards.</summary>
    public int Volume = -1, Pan = -1;
    /// <summary>Contributes to the master mix (false for muted tracks / tracks outside the solo). Stems ignore this.</summary>
    public bool InMaster = true;
    /// <summary>Stem WAV path (empty = no stem). A stem is the track's post-fader output, PDC-aligned, ignoring mute.</summary>
    public string StemPath = "";
    /// <summary>The audio clips to render for this track (null = the clips the engine has loaded; empty = none).</summary>
    public List<ClipSpec>? Clips;
}

/// <summary>Tempo at a frame and the quarter-note position there (plug-ins get tempo and PPQ position each block).</summary>
public readonly record struct RenderTempoPoint(long Frame, double Tempo, double Ppq);

public sealed class RenderSpec
{
    /// <summary>The rendered range [StartFrame, EndFrame) at the engine rate; the file starts at StartFrame.</summary>
    public long StartFrame, EndFrame;
    public RenderTailMode TailMode = RenderTailMode.Fixed;
    public int TailMs = 3000;
    /// <summary>Sample rate of the written files; 0 = the engine's rate. Other rates are resampled (WDL) inside the engine.</summary>
    public int SampleRate;
    /// <summary>1 (mono, L+R average) or 2.</summary>
    public int Channels = 2;
    public RenderFormat Format = RenderFormat.Pcm24;
    /// <summary>Master mix WAV path (empty = no master file).</summary>
    public string MasterPath = "";
    /// <summary>Linear gain on the master mix (the app's master level).</summary>
    public float MasterGain = 1f;
    /// <summary>Path of the <see cref="RenderEventFile"/>.</summary>
    public string EventFile = "";
    public RenderThreads Threads = RenderThreads.Auto;
    /// <summary>Render no faster than realtime (for streaming samplers that cannot keep up with a fast render).</summary>
    public bool RealtimePace;
    public List<RenderSlot> Slots = new();
    /// <summary>Tempo map sorted by frame (empty = 120 bpm from frame 0).</summary>
    public List<RenderTempoPoint> Tempo = new();

    public void Write(BinaryWriter w)
    {
        w.Write(StartFrame); w.Write(EndFrame); w.Write((byte)TailMode); w.Write(TailMs); w.Write(SampleRate); w.Write(Channels);
        w.Write((byte)Format); w.Write(MasterPath ?? ""); w.Write(MasterGain); w.Write(EventFile ?? ""); w.Write((byte)Threads); w.Write(RealtimePace);
        w.Write(Slots.Count);
        foreach (var s in Slots)
        {
            w.Write(s.Slot); w.Write(s.Volume); w.Write(s.Pan); w.Write(s.InMaster); w.Write(s.StemPath ?? "");
            w.Write(s.Clips is not null);
            if (s.Clips is not null) w.Write(s.Clips);
        }
        w.Write(Tempo.Count);
        foreach (var t in Tempo) { w.Write(t.Frame); w.Write(t.Tempo); w.Write(t.Ppq); }
    }

    public static RenderSpec Read(BinaryReader r)
    {
        var spec = new RenderSpec
        {
            StartFrame = r.ReadInt64(), EndFrame = r.ReadInt64(), TailMode = (RenderTailMode)r.ReadByte(), TailMs = r.ReadInt32(),
            SampleRate = r.ReadInt32(), Channels = r.ReadInt32(), Format = (RenderFormat)r.ReadByte(),
            MasterPath = r.ReadBoundedString(1024), MasterGain = r.ReadSingle(), EventFile = r.ReadBoundedString(1024),
            Threads = (RenderThreads)r.ReadByte(), RealtimePace = r.ReadBoolean(),
        };
        if (spec.StartFrame < 0 || spec.EndFrame <= spec.StartFrame) throw new InvalidDataException("Bad render range.");
        if (spec.Channels is < 1 or > 2 || spec.SampleRate is < 0 or > 384_000) throw new InvalidDataException("Bad render format.");
        if (!float.IsFinite(spec.MasterGain)) spec.MasterGain = 1f;
        var count = r.ReadInt32();
        if (count is < 0 or > 256) throw new InvalidDataException("Bad render slot count.");
        for (var i = 0; i < count; i++)
        {
            var s = new RenderSlot { Slot = r.ReadInt32(), Volume = r.ReadInt32(), Pan = r.ReadInt32(), InMaster = r.ReadBoolean(), StemPath = r.ReadBoundedString(1024) };
            if (r.ReadBoolean()) s.Clips = r.ReadClips();
            spec.Slots.Add(s);
        }
        var tempos = r.ReadInt32();
        if (tempos is < 0 or > 100_000) throw new InvalidDataException("Bad tempo map.");
        for (var i = 0; i < tempos; i++) spec.Tempo.Add(new RenderTempoPoint(r.ReadInt64(), r.ReadDouble(), r.ReadDouble()));
        return spec;
    }
}

/// <summary>EngineEvent.RenderProgress.</summary>
public readonly record struct RenderProgressInfo(double Fraction, double Seconds, double Speed);

/// <summary>One written file: master (Slot -1) or a stem, its peak (linear, before dither / clipping) and how many samples exceeded full scale.</summary>
public sealed record RenderFileResult(string Path, int Slot, float Peak, long ClippedSamples);

/// <summary>EngineEvent.RenderDone.</summary>
public sealed record RenderResult(long Frames, double Seconds, double ElapsedSeconds, int Workers, List<RenderFileResult> Files)
{
    public void Write(BinaryWriter w)
    {
        w.Write(Frames); w.Write(Seconds); w.Write(ElapsedSeconds); w.Write(Workers); w.Write(Files.Count);
        foreach (var f in Files) { w.Write(f.Path); w.Write(f.Slot); w.Write(f.Peak); w.Write(f.ClippedSamples); }
    }

    public static RenderResult Read(BinaryReader r)
    {
        var frames = r.ReadInt64(); var seconds = r.ReadDouble(); var elapsed = r.ReadDouble(); var workers = r.ReadInt32();
        var count = Math.Clamp(r.ReadInt32(), 0, 1024);
        var files = new List<RenderFileResult>(count);
        for (var i = 0; i < count; i++) files.Add(new RenderFileResult(r.ReadBoundedString(1024), r.ReadInt32(), r.ReadSingle(), r.ReadInt64()));
        return new RenderResult(frames, seconds, elapsed, workers, files);
    }
}

/// <summary>EngineEvent.RenderFailed as an exception: cancelled by the user, a plug-in that hung or crashed (its path), or another error.</summary>
public sealed class RenderException(string message, bool cancelled = false, string pluginPath = "") : Exception(message)
{
    public bool Cancelled { get; } = cancelled;
    public string PluginPath { get; } = pluginPath;
}

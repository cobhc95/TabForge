using System.IO;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;

namespace TabForge.AudioEngine;

/// <summary>Automatic pitch matching: command parsing (reader thread, on <see cref="EngineHost"/>).</summary>
public static partial class EngineHost
{
    private static void ReadMeasurePitch(BinaryReader r)
    {
        var slot = r.ReadInt32(); var index = r.ReadInt32(); var requestId = r.ReadInt32(); var channel = r.ReadInt32() & 0x0F;
        var count = r.ReadInt32();
        if (count is < 1 or > 8) throw new InvalidDataException("Bad test note count.");
        var notes = new int[count];
        for (var i = 0; i < count; i++) notes[i] = Math.Clamp(r.ReadInt32(), 0, 127);
        EngineThreads.Post(() => _session.StartMeasurePitch(slot, index, requestId, channel, notes));
    }

    private static void ReadSetAutoPitch(BinaryReader r)
    {
        var slot = r.ReadInt32(); var count = r.ReadInt32();
        if (count is < 0 or > 64) throw new InvalidDataException("Bad auto-pitch count.");
        var list = new (int Index, int Semitones)[count];
        for (var i = 0; i < count; i++) list[i] = (r.ReadInt32(), Math.Clamp(r.ReadInt32(), -48, 48));
        EngineThreads.Post(() => _session.SetAutoPitch(slot, list));
    }

    /// <summary>Per-note sounding offsets (NaN: unpitched / silent) folded to an octave transpose.</summary>
    internal static (int Transpose, PitchMatch.Status Status, double Confidence, double[] Offsets) Analyse(PitchProbe probe, int sampleRate)
    {
        var offsets = new double[probe.Notes.Length];
        var skip = sampleRate / 10;   // past the attack
        for (var i = 0; i < offsets.Length; i++)
        {
            var seg = new ReadOnlySpan<float>(probe.Capture, i * probe.NoteSamples + skip, probe.NoteSamples - skip);
            var (hz, _) = PitchMatch.EstimateF0(seg, sampleRate);
            offsets[i] = hz > 0 ? PitchMatch.SemitoneOffset(hz, probe.Notes[i]) : double.NaN;
        }
        var (t, s, c) = PitchMatch.Fold(offsets);
        return (t, s, c, offsets);
    }
}

/// <summary>Automatic pitch matching: silent measurement of an instrument's sounding pitch, and the implicit transposes.</summary>
internal sealed partial class EngineSession
{
    public void SetAutoPitch(int slot, (int Index, int Semitones)[] list)
    {
        AssertMain();
        if (!Loaded.TryGetValue(slot, out var chain)) return;
        foreach (var e in chain.Effects) if (e.IsInstrument) e.AutoPitch.Shift = 0;
        foreach (var (index, st) in list) chain.SetAutoPitch(index, st);
    }

    private static void SendPitch(int slot, int index, int requestId, PitchMatch.Status status, int transpose = 0, double confidence = 0, int[]? notes = null, double[]? offsets = null)
        => Send(EngineEvent.PitchMeasured, w =>
        {
            w.Write(slot); w.Write(index); w.Write(requestId); w.Write((byte)status); w.Write(transpose); w.Write(confidence);
            var n = notes?.Length ?? 0; w.Write(n);
            for (var i = 0; i < n; i++) { w.Write(notes![i]); w.Write(offsets![i]); }
        });

    /// <summary>
    /// Engine thread: routes the instrument's output to a scratch buffer while the transport is stopped, plays the test notes into its own MIDI
    /// input only (~0.5 s each), then estimates f0 on a worker. Deferred while playing, rendering, or another measurement runs on the chain.
    /// </summary>
    public void StartMeasurePitch(int slot, int index, int requestId, int channel, int[] notes)
    {
        AssertMain();
        var mix = Mix;
        if (mix is null || RenderActive || mix.SongPlaying) { SendPitch(slot, index, requestId, PitchMatch.Status.Deferred); return; }
        if (!Loaded.TryGetValue(slot, out var chain) || chain.Effects.FirstOrDefault(e => e.Index == index) is not { IsInstrument: true } effect || effect.Bypass)
        { SendPitch(slot, index, requestId, PitchMatch.Status.Failed); return; }
        if (chain.Probe is not null) { SendPitch(slot, index, requestId, PitchMatch.Status.Deferred); return; }
        var probe = new PitchProbe(index, notes, channel, SampleRate);
        chain.Probe = probe;
        var sampleRate = SampleRate;
        var limit = TimeSpan.FromSeconds(notes.Length * 0.7 + 4);
        Task.Run(() =>
        {
            // Worker thread: reads only the probe, the mixer it captured and the volatile render flag.
            var started = System.Diagnostics.Stopwatch.StartNew();
            var aborted = false;
            while (!probe.Done && started.Elapsed < limit)
            {
                if (!aborted && (mix.SongPlaying || RenderActive)) { probe.Stop = true; aborted = true; }
                Thread.Sleep(20);
            }
            if (!probe.Done) { probe.Stop = true; Thread.Sleep(100); }
            if (ReferenceEquals(chain.Probe, probe)) chain.Probe = null;
            if (aborted || !probe.Done) { SendPitch(slot, index, requestId, aborted ? PitchMatch.Status.Deferred : PitchMatch.Status.Failed); return; }
            var (transpose, status, confidence, offsets) = EngineHost.Analyse(probe, sampleRate);
            SendPitch(slot, index, requestId, status, transpose, confidence, notes, offsets);
        });
    }
}

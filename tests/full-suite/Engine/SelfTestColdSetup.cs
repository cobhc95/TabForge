using System.IO;
using TabForge.Audio.Contracts;
using EM = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;
using ES = TabForge.AudioEngine.Synth;

namespace TabForge;

/// <summary>Cold-start channel setup: a panic that lands in the same block as the setup must not eat it (the "piano on the first play" bug).</summary>
public static partial class SelfTest
{
    private sealed class MidiRecordingSynth : EP.IPluginInstance
    {
        public readonly List<EP.BlockMidi> Received = new();
        public string Path => "recording synth";
        public bool IsInstrument => true;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<EP.BlockMidi> midi, in EP.TransportInfo transport)
        { foreach (var e in midi) Received.Add(e); }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }

    private static void TestColdStartSetupSurvivesPanic()
    {
        const int frames = 256;
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var transport = new EP.TransportInfo { Tempo = 120 };
        var mixL = new float[frames]; var mixR = new float[frames];

        // A song start: Panic (by pipe) and the channel setup (by ring) land in the same block, in either order.
        foreach (var panicFirst in new[] { true, false })
        {
            var synth = new MidiRecordingSynth();
            using var chain = new EM.TrackChain(0, -1, synth, Array.Empty<EM.TrackChain.Effect>(), frames);
            if (panicFirst) chain.Panic();
            chain.AddEvent(0, 0xC0 | 3, 29, 0);            // channel 4: overdriven guitar
            chain.AddEvent(0, 0xB0 | 3, 0, 1);             // bank select
            chain.AddEvent(0, 0xE0 | 3, 0x00, 0x40);       // pitch wheel centre
            chain.AddEvent(0, 0xC0 | 9, 0, 0);
            chain.AddEvent(5, 0x90 | 3, 60, 100);          // a note queued in the same block
            if (!panicFirst) chain.Panic();
            chain.Render(mixL, mixR, 0, frames, transport, shared);
            var got = synth.Received;
            Check($"cold start: the program change, bank and pitch wheel queued with a panic ({(panicFirst ? "panic first" : "panic after")}) still reach the synth",
                got.Any(e => e.Status == (0xC0 | 3) && e.Data1 == 29) && got.Any(e => e.Status == (0xB0 | 3) && e.Data1 == 0 && e.Data2 == 1)
                && got.Any(e => e.Status == (0xE0 | 3)) && got.Any(e => e.Status == (0xC0 | 9)),
                string.Join(" ", got.Where(e => (e.Status & 0xF0) != 0xB0 || e.Data1 < 120).Select(e => $"{e.Status:X2}/{e.Data1}")));
            var offIndex = got.FindIndex(e => e.Status == (0xB0 | 3) && e.Data1 == 123);
            var programIndex = got.FindIndex(e => e.Status == (0xC0 | 3));
            Check("cold start: the panic still silences every channel, before the setup", offIndex >= 0 && offIndex < programIndex
                && Enumerable.Range(0, 16).All(ch => got.Any(e => e.Status == (0xB0 | ch) && e.Data1 == 123) && got.Any(e => e.Status == (0xB0 | ch) && e.Data1 == 120)));
            Check("cold start: the panic drops a note queued in its own block (notes are silenced, state is kept)", !got.Any(e => (e.Status & 0xF0) == 0x90));
        }

        // The real General MIDI synth keeps the program: a note after the panic block is rendered as the setup instrument, not piano.
        ES.GmSynth gm;
        try { gm = new ES.GmSynth(48000, frames); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException) { Skip("cold start with the real GM synth", ex.Message); return; }
        float Energy(int program)
        {
            using var chain = new EM.TrackChain(0, -1, program < 0 ? new ES.GmSynth(48000, frames) : new ES.GmSynth(48000, frames), Array.Empty<EM.TrackChain.Effect>(), frames);
            chain.Panic();
            chain.AddEvent(0, 0xC0, (byte)Math.Max(0, program), 0);
            chain.Render(mixL, mixR, 0, frames, transport, shared);
            chain.AddEvent(0, 0x90, 48, 110);
            double sum = 0;
            for (var block = 0; block < 12; block++)
            {
                Array.Clear(mixL); Array.Clear(mixR);
                chain.Render(mixL, mixR, 0, frames, transport, shared);
                for (var i = 0; i < frames; i++) sum += Math.Abs(mixL[i]) + Math.Abs(mixR[i]);
            }
            return (float)sum;
        }
        var piano = Energy(0); var strings = Energy(48);
        Check("cold start: with the real GM synth the program sent in the panic block is the one that plays (strings differ from piano)",
            piano > 0 && strings > 0 && Math.Abs(piano - strings) / Math.Max(piano, strings) > 0.05, $"piano {piano:0.###}, strings {strings:0.###}");
        gm.Dispose();
    }
}

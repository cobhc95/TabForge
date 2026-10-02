using System.IO;
using TabForge.Audio.Contracts;
using EM = TabForge.AudioEngine.Mixing;

namespace TabForge;

public static partial class SelfTest
{
    // SHA-256 of the master WAV rendered below; it pins the render path that applies clip gain and the auto tail's silence floor.
    private const string GainRenderHash = "2D0C2B48C8DCCC501A8705C35DB25993A9B99A74F5E2EC20BD85EAADAF6B81DE";

    /// <summary>Offline render of two gained clips (-6.02 dB and +1.5 dB) with the auto tail: the master WAV hash is fixed.</summary>
    private static void TestGainRenderHash()
    {
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var mix = new EM.MixEngine(shared, 48000, 64);
        var wav = Path.Combine(Path.GetTempPath(), $"tf-gain-{Guid.NewGuid():N}.wav");
        var dir = Directory.CreateTempSubdirectory("tf-gain-").FullName;
        try
        {
            using (var writer = new NAudio.Wave.WaveFileWriter(wav, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
            {
                var block = new float[2 * 4800];
                for (var done = 0; done < 48000; done += 4800)
                {
                    for (var i = 0; i < 4800; i++) block[i * 2] = block[i * 2 + 1] = 0.5f * MathF.Sin((done + i) * 0.031f) * (1 - (done + i) / 60000f);
                    writer.WriteSamples(block, 0, block.Length);
                }
            }
            var chain = new EM.TrackChain(0, -1, null, Array.Empty<EM.TrackChain.Effect>(), 64);
            chain.SetMix(100, 40);
            mix.SetChain(0, chain);
            mix.SetGraph(new EM.MixEngine.RenderGraph(new[] { 0 }, EM.MixEngine.RenderGraph.NewDest(), Array.Empty<int>()));
            var events = Path.Combine(dir, "e.events");
            RenderEventFile.Write(events, new List<RenderEvent>());
            var master = Path.Combine(dir, "m.wav");
            var spec = new RenderSpec
            {
                StartFrame = 0, EndFrame = 48000, TailMode = RenderTailMode.Auto, TailMs = 2000, Channels = 2, SafetyLimiter = false,
                Format = RenderFormat.Float32, MasterPath = master, EventFile = events, Threads = RenderThreads.One,
                Slots = { new RenderSlot { Slot = 0, Volume = 100, Pan = 40, Clips = new List<ClipSpec> { new(wav, 0, 0, 1, -6.0206, 0, 1), new(wav, 0.1, 0, 0.8, 1.5, 0, 1) } } },
                Tempo = { new RenderTempoPoint(0, 120, 0) },
            };
            var renderer = new EM.OfflineRenderer(spec, mix, shared, 48000, 64, () => false, _ => { });
            renderer.Prepare();
            try { renderer.Run(); } finally { renderer.Restore(); }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(master)));
            Check("Gain: an offline render of gained clips with the auto tail writes the pinned master WAV bytes",
                hash == GainRenderHash, $"hash {hash}, length {new FileInfo(master).Length}");
        }
        finally { try { File.Delete(wav); Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.Services.Video;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// WASAPI shared passes floats up to +18 dB to the Windows mixer; the recording must carry the level that is heard: a hot sine fed with the
    /// tap's gain of 1/8 comes out clean at its heard level, not squashed by the safety limiter into a harsh, high-frequency-heavy clip.
    /// </summary>
    private static void TestMasterTapHotLevel()
    {
        var tap = new TabForge.AudioEngine.Mixing.MasterTap { Gain = 1f / 8f };
        var got = new List<float>(); var gate = new object();
        tap.Sink = (_, _, data, frames) => { lock (gate) for (var i = 0; i < frames; i++) got.Add(data[i * 2]); };
        tap.Enable(true, 48000);
        var block = new float[480 * 2]; var n = 0;
        for (var b = 0; b < 100; b++)
        {
            for (var i = 0; i < 480; i++, n++) block[i * 2] = block[i * 2 + 1] = 4f * MathF.Sin(2 * MathF.PI * 440 * n / 48000);   // +12 dB over full scale
            tap.Write(block, 0, 480);
            Thread.Sleep(2);
        }
        tap.Enable(false, 48000);
        Thread.Sleep(100);
        float[] x; lock (gate) x = got.ToArray();
        var peak = 0f; var nearCeiling = 0;
        foreach (var v in x) { peak = Math.Max(peak, Math.Abs(v)); if (Math.Abs(v) > 0.95f) nearCeiling++; }
        Check("master tap: a +12 dB hot block is recorded at its heard level (peak 0.5), unlimited and at the right pitch",
            x.Length > 40000 && Math.Abs(peak - 0.5f) < 0.02f && nearCeiling == 0 && Math.Abs(DominantHz(x[4800..(4800 + 24000)], 48000) - 440) < 2,
            $"{x.Length} frames, peak {peak:0.###}, near ceiling {nearCeiling}");
    }

    /// <summary>The real engine tap at 44.1 and 48 kHz into a live session: the MP4 holds the clip's tone at its pitch (part of <see cref="SelfTest"/>).</summary>
    private static void TestVideoAudioPitchEngine()
    {
        foreach (var rate in new[] { 44100, 48000 })
        {
            var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
            var wav = WriteTestWav(8);   // 0.05 rad per sample at 48 kHz = 381.97 Hz; the engine resamples the clip to its own rate
            var path = Path.Combine(Path.GetTempPath(), $"tf-pitche-{Guid.NewGuid():N}.mp4");
            try
            {
                EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException("selftest: unexpected plug-in"));
                EH.Configure(NullConfig(rate, 256, manual: false));
                EH.LoadChain(0, "song", false, new List<PluginSpec>());
                EH.SetClips(0, new List<ClipSpec> { new(wav, 0, 0, 8, 0, 0, 1) }, owner: 1);
                EH.Command(EngineCommand.SetTrackMix, w => { w.Write(0); w.Write(100); w.Write(64); });
                EH.Collect();
                EH.SetPlaying(1, true, 0.0);
                var until = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < until && shared.Peak(0) <= 0.01f) { EH.Collect(); Thread.Sleep(2); }
                var session = LiveVideoSession.Start(path, 1920, 1080, 30, _ => true);
                var tapRates = new HashSet<int>(); long rawFrames = 0; var raw = new List<float>();
                EH.Tap.Sink = (r, first, data, frames) => { lock (tapRates) { tapRates.Add(r); for (var i = 0; i < frames; i++, rawFrames++) raw.Add(data[i * 2]); } session.OnAudio(r, first, (float[])data.Clone(), frames); };
                EH.Command(EngineCommand.SetMasterTap, w => w.Write(true));
                EH.Collect();
                Thread.Sleep(2500);
                EH.Command(EngineCommand.SetMasterTap, w => w.Write(false));
                EH.Collect();
                Thread.Sleep(50);
                var elapsed = session.Elapsed.TotalSeconds;
                session.Finish();
                EH.Tap.Sink = null;
                var (outRate, channels, pcm, nativeRate) = DecodeMp4Audio(path);
                var frames = pcm.Length / Math.Max(1, channels);
                var hz = DominantHz(pcm, channels, outRate);
                var rawHz = DominantHz(raw.GetRange(rate / 5, rate * 2).ToArray(), rate);
                Check($"video audio pitch (engine tap at {rate} Hz): tap rate, MP4 rate and the clip's 382 Hz tone agree",
                    tapRates.Count == 1 && tapRates.Contains(rate) && nativeRate == rate && Math.Abs(hz - 382) < 3 && Math.Abs(rawHz - 382) < 3 && Math.Abs(frames / (double)outRate - elapsed) < 0.3,
                    $"raw tap {rawHz:F0} Hz over {rawFrames / (double)rate:F2} s, tap {string.Join(",", tapRates)}, mp4 {nativeRate}, {hz:F1} Hz, {frames / (double)outRate:F2} s, clock {elapsed:F2} s");
            }
            finally { StopTestEngine(shared, wav); try { File.Delete(path); } catch (IOException) { } }
        }
    }
}

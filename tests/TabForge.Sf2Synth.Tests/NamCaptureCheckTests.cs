using System;
using System.IO;
using System.Runtime.InteropServices;
using TabForge.Sf2Synth.Nam;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Sf2Synth.Tests;

// Owns: the on-demand check that a freshly trained capture renders in our runtime as in the trainer. Set TABFORGE_NAM_CHECK
//   to a folder holding model.nam, in.f32 (the input, raw little-endian float32 mono) and ref.f32 (the trainer's render of
//   it); the test writes ours to out.f32 and requires an error-to-signal ratio below 1e-3. Without the variable it does nothing.
// Does not own: the runtime checks against the naive reference (NamTests), training, the capture files (kept outside the repo).
// Tests: NamFile, NamWaveNet (raw net, no NamAmp levels).
public sealed class NamCaptureCheckTests(ITestOutputHelper output)
{
    [Fact]
    public void TrainedCaptureMatchesTheTrainerRender()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_NAM_CHECK");
        if (string.IsNullOrEmpty(dir))
        {
            output.WriteLine("TABFORGE_NAM_CHECK not set: nothing to check");
            return;
        }
        static float[] Read(string path) => MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(path)).ToArray();
        var model = NamFile.Load(Path.Combine(dir, "model.nam"));
        var y = Read(Path.Combine(dir, "in.f32"));
        var want = Read(Path.Combine(dir, "ref.f32"));
        var net = model.Build();
        net.Process(new float[8192]);   // prewarm as NamAmp does: the trainer pads the start with silence
        for (var at = 0; at < y.Length; at += 512) net.Process(y.AsSpan(at, Math.Min(512, y.Length - at)));
        File.WriteAllBytes(Path.Combine(dir, "out.f32"), MemoryMarshal.AsBytes(y.AsSpan()).ToArray());
        double err = 0, sig = 0;
        for (var i = 0; i < y.Length; i++) { err += (y[i] - want[i]) * (double)(y[i] - want[i]); sig += want[i] * (double)want[i]; }
        output.WriteLine($"{model.Architecture} at {model.Rate} Hz: {y.Length} samples, ESR against the trainer {err / sig:E2}");
        Assert.True(err / sig < 1e-3, $"ESR {err / sig}");
    }
}


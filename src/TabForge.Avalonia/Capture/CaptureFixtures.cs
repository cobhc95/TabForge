using System;
using System.IO;
using System.Text;

namespace TabForge.Avalonia.Capture;

// Owns: the made-up media of capture mode: a synthetic mono WAV file (a swelling tone, so a waveform draws) and the text of an example REAPER FX chain with containers.
// Does not own: putting them on a track or in a window (FixtureSteps, FxWindowSteps).
// Tests: TabForge.Avalonia.Tests.CaptureFixtureTests.
internal static class CaptureFixtures
{
    public const int SampleRate = 8000;

    /// <summary>The example .RfxChain: a gate and an amp inside the container "Amp", then a reverb and a delay. The plug-ins are named by files no machine has.</summary>
    public const string ReaperChain = """
        BYPASS 0 0
        <CONTAINER Container "Amp"
          CONTAINER_CFG 2 2 2 0
          <IN_PINS
          >
          <OUT_PINS
          >
          BYPASS 0 0 0
          <VST "VST3: Example Gate (Sample Audio)" ExampleGate.vst3 0 "" 1<00> ""
          >
          BYPASS 0 0 0
          <VST "VST3: Example Amp (Sample Audio)" ExampleAmp.vst3 0 "" 2<00> ""
          >
        >
        BYPASS 0 0 0
        <VST "VST3: Example Reverb (Sample Audio)" ExampleReverb.vst3 0 "" 3<00> ""
        >
        BYPASS 1 0 0
        <VST "VST3: Example Delay (Sample Audio)" ExampleDelay.vst3 0 "" 4<00> ""
        >
        """;

    /// <summary>Writes a 16-bit mono WAV of <paramref name="seconds"/> whose loudness swells and fades about every <paramref name="phraseSec"/> seconds; returns the path.</summary>
    public static string WriteWave(string folder, string name, double seconds, double phraseSec = 1.6)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".wav");
        var count = (int)(seconds * SampleRate);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream, Encoding.ASCII);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)SampleRate;
            var envelope = 0.25 + 0.7 * Math.Abs(Math.Sin(Math.PI * t / phraseSec)) * (0.6 + 0.4 * Math.Sin(t * 1.7));
            w.Write((short)(Math.Sin(2 * Math.PI * 220 * t) * Math.Sin(t * 37) * envelope * 22000));
        }
        return path;
    }
}

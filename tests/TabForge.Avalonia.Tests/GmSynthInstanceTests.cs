using System;
using System.IO;
using System.Linq;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that two GmSynth instances (two engine sessions) share no mutable state: notes and program changes sent to one never reach the other.
/// Does not own: the synth's sound level (AudioSmokeTests).
/// Tests: GmSynth.
/// </summary>
public class GmSynthInstanceTests
{
    private const int Rate = 44100;
    private const int Block = 4410;

    private static float[][] Render(GmSynth synth, params BlockMidi[] midi)
    {
        var output = new[] { new float[Block], new float[Block] };
        synth.Process(Array.Empty<float[]>(), output, Block, midi, default);
        return output;
    }

    private static BlockMidi Msg(int status, int d1, int d2) => new() { Status = (byte)status, Data1 = (byte)d1, Data2 = (byte)d2 };

    private static double Energy(float[][] o) => o.Sum(c => c.Sum(v => (double)v * v));

    private static bool Available => OperatingSystem.IsWindows() && File.Exists(DlsToSoundFont.WindowsBankPath);

    [Fact]
    public void A_note_on_one_synth_is_silent_on_another()
    {
        if (!Available) return;
        using var a = new GmSynth(Rate, Block);
        using var b = new GmSynth(Rate, Block);
        var onA = Energy(Render(a, Msg(0x90, 60, 100)));
        var onB = Energy(Render(b));
        Assert.True(onA > 0, "the note must sound on the synth it was sent to");
        Assert.Equal(0, onB);
    }

    [Fact]
    public void A_program_change_on_one_synth_leaves_another_on_its_own_program()
    {
        if (!Available) return;
        var note = Msg(0x90, 60, 100);
        using var alone = new GmSynth(Rate, Block);
        var expected = Render(alone, note);
        using var a = new GmSynth(Rate, Block);
        using var b = new GmSynth(Rate, Block);
        var changed = Render(a, Msg(0xC0, 40, 0), note);
        var untouched = Render(b, note);
        Assert.NotEqual(expected[0], changed[0]);
        Assert.Equal(expected[0], untouched[0]);
    }
}

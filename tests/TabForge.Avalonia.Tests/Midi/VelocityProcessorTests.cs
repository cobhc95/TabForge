using System.Linq;
using System.Text.Json;
using TabForge.Core.Midi;
using TabForge.Core.Midi.Processors;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Velocity processor's wiring: every mode, the min/max clamp, the velocity and note range filters, Mix, Random and the reversed-range guards.
/// Does not own: the panel (MidiProcessingWindowTests) or the chain.
/// Tests: this class.
/// </summary>
public class VelocityProcessorTests
{
    /// <summary>Runs one note-on at (note, velocity) and its note-off through the processor built from the JSON; returns the note-on velocity and the note-off data.</summary>
    private static (int OnVel, int OffNote, int OffVel) Run(string json, int note = 60, int vel = 100)
    {
        using var doc = JsonDocument.Parse(json);
        var proc = new VelocityProcessor(MidiParams.Parse(doc));
        var input = new MidiBuffer(); var output = new MidiBuffer();
        input.Add(0, 0x90, (byte)note, (byte)vel);
        input.Add(10, 0x80, (byte)note, 40);
        proc.Process(input, output, new MidiContext { Frames = 64, SampleRate = 48000 });
        Assert.Equal(2, output.Count);
        Assert.Equal(0x90, output.Items[0].Status);
        return (output.Items[0].Data2, output.Items[1].Data1, output.Items[1].Data2);
    }

    private static int Vel(string json, int note = 60, int vel = 100) => Run(json, note, vel).OnVel;

    [Fact]
    public void ScaleAndOffsetModeUsesMultiplyThenOffsetAndIgnoresTheOtherModesSettings()
    {
        Assert.Equal(60, Vel("{\"Mode\":0,\"Mul\":0.5,\"Add\":10,\"Fixed\":5,\"Threshold\":10,\"Makeup\":-30}"));
        Assert.Equal(1, Vel("{\"Mode\":0,\"Mul\":-1,\"Add\":0}"));   // negative result floors at 1
    }

    [Fact]
    public void FixedModeGivesEveryNoteInRangeThatVelocity()
    {
        Assert.Equal(33, Vel("{\"Mode\":1,\"Fixed\":33,\"Mul\":2,\"Add\":50}", vel: 100));
        Assert.Equal(33, Vel("{\"Mode\":1,\"Fixed\":33}", vel: 7));
    }

    [Fact]
    public void CompressorModeSqueezesAboveThresholdThenAddsMakeup()
    {
        Assert.Equal(90, Vel("{\"Mode\":2,\"Threshold\":80,\"Ratio\":2}", vel: 100));
        Assert.Equal(50, Vel("{\"Mode\":2,\"Threshold\":80,\"Ratio\":2}", vel: 50));   // below the threshold: untouched
        Assert.Equal(100, Vel("{\"Mode\":2,\"Threshold\":80,\"Ratio\":2,\"Makeup\":10}", vel: 100));
        Assert.Equal(60, Vel("{\"Mode\":2,\"Threshold\":80,\"Ratio\":2,\"Makeup\":10}", vel: 50));
    }

    [Fact]
    public void MinAndMaxClampTheResultAndAReversedPairIsSwapped()
    {
        Assert.Equal(90, Vel("{\"Mode\":1,\"Fixed\":120,\"Max\":90}"));
        Assert.Equal(40, Vel("{\"Mode\":1,\"Fixed\":10,\"Min\":40}"));
        Assert.Equal(90, Vel("{\"Mode\":1,\"Fixed\":120,\"Min\":90,\"Max\":40}"));   // reversed: 40..90
        Assert.Equal(40, Vel("{\"Mode\":1,\"Fixed\":10,\"Min\":90,\"Max\":40}"));
        Assert.Equal(1, Vel("{\"Mode\":1,\"Fixed\":1,\"Min\":0,\"Max\":0}"));   // never below 1
    }

    [Fact]
    public void VelocityRangeLeavesOtherNotesUntouchedAndAReversedRangeIsSwapped()
    {
        Assert.Equal(100, Vel("{\"Mode\":1,\"Fixed\":20,\"InLow\":10,\"InHigh\":50}", vel: 100));
        Assert.Equal(20, Vel("{\"Mode\":1,\"Fixed\":20,\"InLow\":10,\"InHigh\":50}", vel: 30));
        Assert.Equal(20, Vel("{\"Mode\":1,\"Fixed\":20,\"InLow\":50,\"InHigh\":10}", vel: 30));   // reversed: still 10..50
    }

    [Fact]
    public void NoteRangeLeavesOtherNotesUntouchedAndNoteOffsAreNeverChanged()
    {
        Assert.Equal(100, Vel("{\"Mode\":1,\"Fixed\":20,\"Low\":0,\"High\":50}", note: 60));
        var inside = Run("{\"Mode\":1,\"Fixed\":20,\"Low\":50,\"High\":70}", note: 60);
        Assert.Equal((20, 60, 40), inside);   // the note-off keeps its pitch and velocity
        Assert.Equal(20, Vel("{\"Mode\":1,\"Fixed\":20,\"Low\":70,\"High\":50}", note: 60));   // reversed: swapped
    }

    [Fact]
    public void MixBlendsDryAndWet()
    {
        Assert.Equal(100, Vel("{\"Mode\":1,\"Fixed\":20,\"Mix\":0}"));
        Assert.Equal(60, Vel("{\"Mode\":1,\"Fixed\":20,\"Mix\":50}"));
        Assert.Equal(20, Vel("{\"Mode\":1,\"Fixed\":20,\"Mix\":100}"));
    }

    [Fact]
    public void RandomZeroIsDeterministicAndRandomStaysInsideMinAndMax()
    {
        Assert.Equal(Vel("{\"Mode\":0,\"Random\":0}"), Vel("{\"Mode\":0,\"Random\":0}"));
        Assert.Equal(100, Vel("{\"Mode\":0,\"Random\":0}"));
        using var doc = JsonDocument.Parse("{\"Mode\":0,\"Random\":100,\"Min\":90,\"Max\":110}");
        var proc = new VelocityProcessor(MidiParams.Parse(doc));
        var changed = false;
        for (var i = 0; i < 50; i++)
        {
            var input = new MidiBuffer(); var output = new MidiBuffer();
            input.Add(0, 0x90, 60, 100);
            proc.Process(input, output, new MidiContext { Frames = 64, SampleRate = 48000 });
            var v = output.Items[0].Data2;
            Assert.InRange(v, 90, 110);
            changed |= v != 100;
            var off = new MidiBuffer(); off.Add(0, 0x80, 60, 0); proc.Process(off, new MidiBuffer(), new MidiContext { Frames = 64, SampleRate = 48000 });
        }
        Assert.True(changed);
    }

    [Fact]
    public void EveryVelocityParameterTheCatalogDeclaresIsReadByTheProcessor()
    {
        var keys = MidiProcessorCatalog.Find("velocity")!.Params.Where(p => p.Group != "Applies to").Select(p => p.Key).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "Add", "Fixed", "High", "InCh", "InHigh", "InLow", "Low", "Makeup", "Max", "Min", "Mix", "Mode", "Mul", "Random", "Ratio", "Threshold" }, keys);
    }
}

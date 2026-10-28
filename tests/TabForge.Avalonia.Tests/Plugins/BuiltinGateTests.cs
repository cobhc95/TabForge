using System.Linq;
using TabForge.AudioEngine.Builtin.Gate;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in noise gate sound: closing below the threshold by its range, passing loud sound, the open light, the hold time, and a range of 0 dB.
// Does not own: the editor window (BuiltinEditorWindowTests), the framework (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinGateTests
{
    private const int Rate = 48000;

    private static GateEffect Gate(double threshold, double range)
    {
        var g = new GateEffect(Rate, 512);
        g.SetParameter(GateEffect.ThresholdId, threshold);
        g.SetParameter(GateEffect.RangeId, range);
        g.SetParameter(GateEffect.HoldId, 100);
        g.SetParameter(GateEffect.ReleaseId, 50);
        return g;
    }

    private static bool IsOpen(GateEffect g) => g.GetParameters().Single(p => p.Id == GateEffect.OpenId).Value >= 0.5;

    [Fact]
    public void TheGateClosesBelowTheThresholdByItsRangeAndPassesLoudSound()
    {
        var gate = Gate(-40, -40);
        var quiet = BuiltinTestKit.Sine(500, -60, Rate, Rate);
        var (closed, _) = BuiltinTestKit.Run(gate, quiet);
        Assert.False(IsOpen(gate));
        Assert.Equal(-40.0, BuiltinTestKit.RmsDb(closed, Rate / 2, Rate) - BuiltinTestKit.RmsDb(quiet, Rate / 2, Rate), 0.5);
        var loud = BuiltinTestKit.Sine(500, -20, Rate, Rate);
        var (open, _) = BuiltinTestKit.Run(gate, loud);
        Assert.True(IsOpen(gate));
        Assert.Equal(0.0, BuiltinTestKit.RmsDb(open, Rate / 2, Rate) - BuiltinTestKit.RmsDb(loud, Rate / 2, Rate), 0.1);
    }

    [Fact]
    public void TheGateHoldsOpenForTheHoldTimeThenCloses()
    {
        var gate = Gate(-40, -60);
        BuiltinTestKit.Run(gate, BuiltinTestKit.Sine(500, -20, Rate / 2, Rate));
        Assert.True(IsOpen(gate));
        BuiltinTestKit.Run(gate, new float[2400]);   // 50 ms of silence: inside the 100 ms hold
        Assert.True(IsOpen(gate));
        BuiltinTestKit.Run(gate, new float[Rate / 5]);
        Assert.False(IsOpen(gate));
    }

    [Fact]
    public void ARangeOfZeroDecibelsMeansTheGateChangesNothing()
    {
        var gate = Gate(-30, 0);
        var quiet = BuiltinTestKit.Sine(500, -50, Rate, Rate);
        var (output, _) = BuiltinTestKit.Run(gate, quiet);
        Assert.Equal(BuiltinTestKit.RmsDb(quiet, Rate / 2, Rate), BuiltinTestKit.RmsDb(output, Rate / 2, Rate), 0.01);
    }
}

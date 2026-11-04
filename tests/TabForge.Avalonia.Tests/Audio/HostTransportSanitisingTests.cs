using System;
using System.IO;
using TabForge.AudioEngine.Isolation;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 13 A8's regression proof that a compromised engine writing NaN/Infinity into the host block cannot pass
// those values to plug-in DSP: the host clamps the transport exactly like the native helper.
public sealed class HostTransportSanitisingTests
{
    [Fact]
    public void NonFiniteTransportValuesNeverReachThePlugin()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-transport-" + Guid.NewGuid().ToString("N") + ".shm");
        try
        {
            using var block = PluginHostBlock.CreateFile(path, 256, 1);
            block.Tempo = double.NaN;
            block.Ppq = double.PositiveInfinity;
            block.BarStartPpq = double.NegativeInfinity;
            block.TimeSigNumerator = 999;
            block.TimeSigDenominator = -4;
            block.Playing = 2;

            var transport = PluginHostMain.TransportFor(block);

            Assert.Equal(120, transport.Tempo);
            Assert.Equal(0, transport.PpqPosition);
            Assert.Equal(0, transport.Meter.BarStartPpq);
            Assert.Equal(64, transport.Meter.Numerator);
            Assert.Equal(1, transport.Meter.Denominator);
            Assert.True(transport.Playing);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}

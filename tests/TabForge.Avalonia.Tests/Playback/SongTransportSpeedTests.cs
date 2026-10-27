using System.Diagnostics;
using TabForge.AudioEngine.Mixing;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the engine song transport advancing at the anchor's speed between anchors (half speed: 2 s real = 1 song second).
// Does not own: the wire format (TestEngineMessagesRoundTrip/Golden) or the session's anchors (EnginePositionAnchorTests).
// Tests: this file.
public class SongTransportSpeedTests
{
    [Theory]
    [InlineData(1.0, 22.0)]
    [InlineData(0.5, 21.0)]
    public void AdvancesAtTheAnchoredSpeed(double speed, double expected)
    {
        var transport = new SongTransport();
        transport.SetPosition(true, 20, 0, speed);
        Assert.Equal(expected, transport.Current.SecAt(2.0 * Stopwatch.Frequency), 6);
    }

    [Fact]
    public void AnAnchorWithoutSpeedKeepsTheOneToOneClock()
    {
        var transport = new SongTransport();
        transport.SetPosition(true, 20, 0);
        Assert.Equal(22.0, transport.Current.SecAt(2.0 * Stopwatch.Frequency), 6);
    }
}

using TabForge.Avalonia.Capture;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the held-moment engine checks: it holds the chosen performed bar and fraction, and the elapsed time grows with the performed order.
/// Does not own: the playback sessions that read it.
/// Tests: this class.
/// </summary>
public class CaptureMomentEngineTests
{
    private static CaptureMomentEngine Started()
    {
        var engine = new CaptureMomentEngine();
        engine.Start(FullDemoSongFactory.Create(), new PlaybackOptions(), _ => { }, () => { });
        return engine;
    }

    [Fact]
    public void HoldsTheChosenPerformedBarAndFraction()
    {
        using var engine = Started();
        Assert.True(engine.PerformedBars > 12);
        engine.MoveTo(12, 0.4);
        var moment = engine.Playhead();
        Assert.Equal(0.4, moment.BarFraction);
        engine.MoveTo(13, 0);
        Assert.True(engine.Playhead().ElapsedMs > moment.ElapsedMs);
        Assert.True(engine.IsPlaying);
    }

    [Fact]
    public void AudiblePlayheadFillsTheCallersFrame()
    {
        using var engine = Started();
        engine.MoveTo(3, 0.5);
        var frame = engine.AudiblePlayhead(new PlaybackPosition());
        Assert.Equal(0.5, frame.BarFraction);
        Assert.Equal(engine.Playhead().Bar, frame.Bar);
    }
}

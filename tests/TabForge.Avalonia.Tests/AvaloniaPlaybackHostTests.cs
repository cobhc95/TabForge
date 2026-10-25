using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the host hands every document a real engine session and that closing a document drops it.
/// Does not own: the engine's scheduling (self-tests) or the transport rules.
/// Tests: this class.
/// </summary>
public class AvaloniaPlaybackHostTests
{
    [Fact]
    public void EveryDocumentGetsARealEngineSession()
    {
        using var host = new AvaloniaPlaybackHost(new AvaloniaUiThread(Dispatcher.UIThread));
        var document = MainWindowTabs.NewSong();
        var session = host.Sessions.For(document);
        Assert.NotNull(session);
        Assert.Same(session, host.Sessions.For(document));
        Assert.False(session!.IsPlaying);
        host.Sessions.Close(document);
        Assert.NotSame(session, host.Sessions.For(document));
    }
}

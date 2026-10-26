using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Band view in its own window: the Band panels over a song.
/// Owns: the window shell. Does not own: the header, panels and pills (BandView), the dock panel.
/// Tests: TabForge.Avalonia.Tests.BandViewTests.
/// </summary>
public sealed class BandWindow : Window
{
    public BandWindow(SongDocument document)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Title = "Band";
        Width = 760;
        Height = 520;
        View = new BandView(document);
        Content = View;
    }

    /// <summary>Follows <paramref name="session"/> (null unbinds): the panels show their playhead and keep it in view.</summary>
    public void BindPlayback(AvaloniaPlaybackSession? session) => View.BindPlayback(session);

    public BandView View { get; }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: an FX chain window belongs to its song: hidden when another song's tab is shown, back when its tab returns, closed with its song.
/// Does not own: the registry or the song (production classes).
/// Tests: this class.
/// </summary>
public class DocumentWindowVisibilityTests
{
    [AvaloniaFact]
    public void ChainWindowFollowsItsSongTab()
    {
        var a = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var b = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var shown = a;
        var owner = new Window();
        owner.Show();
        var windows = new FxChainWindows(owner, () => shown, null, null, new TabForge.Plugins.PluginTrust());
        var window = windows.Open(a.Project.Tracks[0]);
        Assert.True(window.IsVisible);

        shown = b;
        windows.SongShown(new[] { a, b });
        Assert.False(window.IsVisible);

        shown = a;
        windows.SongShown(new[] { a, b });
        Assert.True(window.IsVisible);

        windows.CloseSong(a);
        Assert.False(window.IsVisible);
    }
}

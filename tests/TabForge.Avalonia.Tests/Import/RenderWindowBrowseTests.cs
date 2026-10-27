using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Rendering;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Render dialog's Browse button (RenderWindow.BrowsePressed through its folder picker).
// Does not own: the render plan and options (RenderDialogPresenter tests), the real folder dialog (AvaloniaFileDialogs).
// Tests: itself.
public sealed class RenderWindowBrowseTests
{
    [AvaloniaFact]
    public void BrowsePutsThePickedFolderInTheOutputBox()
    {
        string? asked = null;
        var window = new RenderWindow(FullDemoSongFactory.Create(), new RenderSettings { Directory = "Start" }, false, null, start => { asked = start; return "Picked"; });
        window.BrowsePressed();
        Assert.Equal("Start", asked);
        Assert.Equal("Picked", window.OutputFolder);
    }

    [AvaloniaFact]
    public void BrowseCancelledLeavesTheOutputFolder()
    {
        var window = new RenderWindow(FullDemoSongFactory.Create(), new RenderSettings { Directory = "Start" }, false, null, _ => null);
        window.BrowsePressed();
        Assert.Equal("Start", window.OutputFolder);
    }
}

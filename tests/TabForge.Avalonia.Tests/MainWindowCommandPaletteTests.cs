using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the test that the command palette lists the catalogued commands the window handles and leaves out the rest.
// Does not own: the palette's filtering (CommandPalettePresenter tests) and the menu's enabled rows (MainWindowMenuTests).
public sealed class MainWindowCommandPaletteTests
{
    [AvaloniaFact]
    public void PaletteListsOnlyHandledCommands()
    {
        var owner = new Window();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowChordFinder.Register(commands, owner);
        var entries = MainWindowCommandPalette.Entries(commands, new HotkeySettings());
        Assert.Contains(entries, entry => entry.Id == "Tools.ChordFinder");
        Assert.DoesNotContain(entries, entry => entry.Id == "File.Save");
    }
}

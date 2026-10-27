using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the guard that keeps a clip key from being used while no clip is selected: the key falls through (returns false) and its handler does not run;
//   and the clip clipboard keeping its own copy, so a later edit of the copied clip does not change what Paste places.
// Tests: this class.
public class MainWindowClipKeysTests
{
    [AvaloniaFact]
    public void ClipKeyIsUsedOnlyWhileItsGuardApplies()
    {
        var commands = new MainWindowCommands(new Window(), () => { });
        var runs = 0;
        var clipSelected = false;
        commands.AddWhen("Clip.Delete", () => runs++, () => clipSelected);
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands, focus: () => FocusTarget.Clips);   // the clip keys apply while clips have the focus (the last click)
        Assert.False(keyboard.Route(Key.Delete, KeyModifiers.None, null));
        Assert.Equal(0, runs);
        clipSelected = true;
        Assert.True(keyboard.Route(Key.Delete, KeyModifiers.None, null));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ClipboardKeepsItsOwnCopyOfTheClip()
    {
        var source = new AudioClip { Name = "Intro" };
        ClipClipboard.Copy(source);
        source.Name = "Changed";
        Assert.True(ClipClipboard.HasClip);
        Assert.Equal("Intro", ClipClipboard.Name);
    }
}

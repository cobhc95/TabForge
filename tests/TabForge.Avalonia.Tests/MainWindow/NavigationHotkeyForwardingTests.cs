using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the rules for navigation and transport keys pressed outside the main window: which windows forward them, that text boxes keep their keys, that the user's own
//   bindings are what count, and which plug-in window classes are text fields.
// Does not own: the key table (MainWindowSpaceEscapeRoutingTests), the Windows hotkey calls, which need a real plug-in window.
// Tests: this class.
public class NavigationHotkeyForwardingTests
{
    private static KeyEventArgs Press(Control source, Key key)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = source };
        source.RaiseEvent(args);
        return args;
    }

    private sealed record Opened(MainWindow Main, Button Button, TextBox Text, AvaloniaPlaybackSession Session, IDisposable Sessions);

    private static Opened Open()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var document = new SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() };
        var main = new MainWindow(first: document, playback: sessions);
        main.Show();
        var button = new Button();
        var text = new TextBox();
        new Window { Content = new StackPanel { Children = { button, text } } }.Show(main);
        return new Opened(main, button, text, sessions.For(document)!, sessions);
    }

    [AvaloniaFact]
    public void SpaceOnAToolWindowButtonPlaysAndDoesNotClick()
    {
        var opened = Open();
        using (opened.Sessions)
        {
            var clicks = 0;
            opened.Button.Click += (_, _) => clicks++;
            opened.Button.Focus();
            Assert.True(Press(opened.Button, Key.Space).Handled);
            Assert.True(opened.Session.IsPlaying);
            Assert.Equal(0, clicks);
        }
    }

    [AvaloniaFact]
    public void ATextBoxInAToolWindowKeepsSpace()
    {
        var opened = Open();
        using (opened.Sessions)
        {
            opened.Text.Focus();
            Press(opened.Text, Key.Space);
            Assert.False(opened.Session.IsPlaying);
        }
    }

    [AvaloniaFact]
    public void AModalDialogKeepsSpace()
    {
        var opened = Open();
        using (opened.Sessions)
        {
            var button = new Button();
            var dialog = new Window { Content = button };
            _ = dialog.ShowDialog(opened.Main);
            Press(button, Key.Space);
            Assert.False(opened.Session.IsPlaying);
        }
    }

    [AvaloniaFact]
    public void TheUsersOwnBindingIsTheOneForwarded()
    {
        var played = 0;
        var commands = new MainWindowCommands(new Window(), () => { });
        commands.Add("Transport.PlayPause", () => played++);
        var bindings = new HotkeySettings();
        bindings["Transport.PlayPause"] = "Ctrl+Alt+Shift+F7";
        var keyboard = new MainWindowKeyboard(bindings, commands);

        Assert.False(keyboard.RouteNavigation(Key.Space, KeyModifiers.None, null));
        Assert.True(keyboard.RouteNavigation(Key.F7, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, null));
        Assert.Equal(1, played);
        Assert.Contains("Ctrl+Alt+Shift+F7", keyboard.NavigationGestures());
        Assert.DoesNotContain("Space", keyboard.NavigationGestures());
    }

    [AvaloniaFact]
    public void OtherHotkeysAreNotForwarded()
    {
        var saved = 0;
        var commands = new MainWindowCommands(new Window(), () => { });
        commands.Add("File.Save", () => saved++);
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands);

        Assert.False(keyboard.RouteNavigation(Key.S, KeyModifiers.Control, null));
        Assert.Equal(0, saved);
        Assert.DoesNotContain("Ctrl+S", keyboard.NavigationGestures());
    }

    [Fact]
    public void EveryForwardedCommandIsAWindowCommandInTheCatalogue()
    {
        foreach (var id in NavigationHotkeys.All)
        {
            Assert.NotNull(HotkeyCatalog.ById(id));
            Assert.Equal(0, HotkeyCatalog.ContextOf(id));
        }
    }

    [AvaloniaFact]
    public void BoundGesturesParseBackToTheSameSpelling()
    {
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(new Window(), () => { }));
        Assert.NotEmpty(keyboard.NavigationGestures());
        foreach (var gesture in keyboard.NavigationGestures())
        {
            Assert.True(MainWindowKeyboard.TryParseGesture(gesture, out var key, out var modifiers), gesture);
            Assert.Equal(gesture, MainWindowKeyboard.GestureOf(key, modifiers));
        }
    }

    [Fact]
    public void SettingsWindowsKeepTheirKeys()
    {
        Assert.True(ToolWindowKeyForwarding.KeepsItsKeys(typeof(PreferencesWindow)));
        Assert.True(ToolWindowKeyForwarding.KeepsItsKeys(typeof(FretboardSettingsWindow)));
        Assert.False(ToolWindowKeyForwarding.KeepsItsKeys(typeof(FxChainWindow)));
        Assert.False(ToolWindowKeyForwarding.KeepsItsKeys(typeof(MixerWindow)));
    }

    [Theory]
    [InlineData("Edit", true)]
    [InlineData("RICHEDIT50W", true)]
    [InlineData("RichEdit20W", true)]
    [InlineData("WindowsForms10.EDIT.app.0.1", true)]
    [InlineData("TabForgePluginEditor", false)]
    [InlineData("JUCE_18f2a", false)]
    public void TextFieldClassesAreRecognised(string name, bool isText) => Assert.Equal(isText, PluginEditorHotkeys.IsTextClass(name));
}

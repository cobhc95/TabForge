using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the window keys Space (Play/Pause, before any control) and Escape (stops an active song, after the selection and any editing
//   control) in the real main window: a focused Tools pane button, an editing control, and the Escape stop.
// Does not own: the key table (MainWindowKeyboard), the playback engine (AvaloniaPlaybackSession), the score Space and Escape selection
//   (MainWindowSpaceEscapeKeysTests).
// Tests: this class.
public class MainWindowSpaceEscapeRoutingTests
{
    [AvaloniaFact]
    public void SpaceOnFocusedToolsPaneButtonTogglesPlaybackAndDoesNotClick()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var document = new SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() };
        var window = new MainWindow(first: document, playback: sessions);
        window.Show();
        var session = sessions.For(document)!;
        var chrome = window.GetLogicalDescendants().OfType<Border>().First(b => b.Focusable && b.Child is Platform.ToolIconView);
        chrome.Focus();

        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space, Source = chrome };
        chrome.RaiseEvent(key);

        Assert.True(key.Handled);
        Assert.True(session.IsPlaying);
    }

    [AvaloniaFact]
    public void EscapeStopsAPlayingSong()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var document = new SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() };
        var window = new MainWindow(first: document, playback: sessions);
        window.Show();
        var session = sessions.For(document)!;
        var score = window.GetLogicalDescendants().OfType<Platform.ScorePreviewView>().Single();
        score.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space, Source = score });
        Assert.True(session.IsPlaying);

        score.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = score });

        Assert.False(session.IsPlaying);
        Assert.False(session.IsPaused);
    }

    [AvaloniaFact]
    public void SpaceBelongsToTextBoxesAndEscapeToAnyEditingControl()
    {
        var played = 0;
        var commands = new MainWindowCommands(new Window(), () => { });
        commands.Add("Transport.PlayPause", () => played++);
        commands.AddWhen(MainWindowTransportMenu.EscapeStopCommand, () => played++, () => true);
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands);

        Assert.False(keyboard.RouteBeforeControls(Key.Space, KeyModifiers.None, new TextBox()));   // a text box types its space
        Assert.False(keyboard.Route(Key.Escape, KeyModifiers.None, new ComboBox()));               // Escape still belongs to an editing control
        Assert.Equal(0, played);
        Assert.True(keyboard.RouteBeforeControls(Key.Space, KeyModifiers.None, new Button()));
        Assert.True(keyboard.RouteBeforeControls(Key.Space, KeyModifiers.None, new ComboBox()));   // WPF blocks Space for text inputs only
        Assert.True(keyboard.RouteBeforeControls(Key.Space, KeyModifiers.None, new Slider()));
        Assert.Equal(3, played);
    }

    [AvaloniaFact]
    public void SpaceEndsARecordingBeforeThePlayPauseCommand()
    {
        var stopped = 0;
        var played = 0;
        var commands = new MainWindowCommands(new Window(), () => { });
        commands.Add("Transport.PlayPause", () => played++);
        var keyboard = new MainWindowKeyboard(new HotkeySettings(), commands, () => { stopped++; return true; });

        Assert.False(keyboard.RouteBeforeControls(Key.Space, KeyModifiers.None, new TextBox()));
        Assert.Equal(0, stopped);   // a text box keeps its space, even while recording

        Assert.True(keyboard.RouteBeforeControls(Key.Space, KeyModifiers.None, new ComboBox()));

        Assert.Equal(1, stopped);
        Assert.Equal(0, played);    // the recording's stop ends it; Play/Pause does not also run
    }
}

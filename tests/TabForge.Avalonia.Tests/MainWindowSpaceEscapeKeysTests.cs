using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the window keys Space (Transport.PlayPause) and Escape (clears the score selection) reaching their commands when the score has focus.
// Does not own: the key table (MainWindowKeyboard), the playback engine (AvaloniaPlaybackSession), the score selection rules (Core ScorePreviewEditor).
// Tests: this class.
public class MainWindowSpaceEscapeKeysTests
{
    [AvaloniaFact]
    public void SpaceTogglesPlaybackAndEscapeClearsTheScoreSelection()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var document = new SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() };
        var window = new MainWindow(first: document, playback: sessions);
        window.Show();
        var session = sessions.For(document)!;
        var score = window.GetLogicalDescendants().OfType<Platform.ScorePreviewView>().Single();

        Press(score, Key.Space);
        Assert.True(session.IsPlaying);
        Press(score, Key.Space);
        Assert.True(session.IsPaused);

        score.Editor.SelectRange(0, 0, 0, 2);
        Assert.True(score.Editor.HasSelection);
        Press(score, Key.Escape);
        Assert.False(score.Editor.HasSelection);
    }

    /// <summary>Raises a key-down on the focused control; it bubbles to the window like a real key press.</summary>
    private static void Press(global::Avalonia.Controls.Control target, Key key) =>
        target.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = key, Source = target });
}

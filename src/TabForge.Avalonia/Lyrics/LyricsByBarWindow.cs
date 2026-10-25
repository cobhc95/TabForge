using Avalonia.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Documents;

namespace TabForge.Avalonia.Lyrics;

// Owns: the window that shows the per-bar syllable editor (LyricsPaneView) of one song, opened from the Lyrics pane's "Edit by bar" button.
// Does not own: the syllables (LyricsController), the song-level lyrics (LyricsSongPane), the playback (the session it is given).
// Tests: TabForge.Avalonia.Tests.LyricsSongPaneTests.
/// <summary>The per-bar lyrics editor as a dialog over the main window. UI thread only.</summary>
public sealed class LyricsByBarWindow : Window
{
    /// <summary>The window title (the per-bar editor's name).</summary>
    public const string Heading = "Lyrics by bar";
    private const double EditorWidth = 520, EditorHeight = 560;

    public LyricsByBarWindow(SongDocument document, AvaloniaPlaybackSession? session)
    {
        Title = Heading;
        ShowInTaskbar = false;
        Width = EditorWidth;
        Height = EditorHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var editor = new LyricsPaneView();
        editor.Show(document, session);
        Content = editor;
    }
}

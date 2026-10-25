// Owns: the document tab label text: the name (the song's file name, else its title, else "Untitled") and the tooltip (its path, the song title when different, the unsaved note).
// Does not own: the unsaved dot on a tab (TabItemModel.DirtyVisibility and the tab bar draw it), the tab strip, or the document's path and project (SongDocument holds them).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>The text of a document tab: its name and its tooltip, from the file path and the song title.</summary>
public static class TabLabelPresenter
{
    /// <summary>The tab name: the saved file's name, else the song title, else "Untitled".</summary>
    public static string Name(string? path, string songTitle) =>
        !string.IsNullOrWhiteSpace(path) ? System.IO.Path.GetFileName(path)! :
        string.IsNullOrWhiteSpace(songTitle) ? "Untitled" : songTitle;

    /// <summary>The tab tooltip: the file's path (marked unsaved while it is only an import source), or the name with the unsaved note while the song has no file.</summary>
    public static string Tooltip(string? path, string name, string songTitle, bool hasSaveFile)
    {
        if (string.IsNullOrWhiteSpace(path)) return name + TitleMarkers.UnsavedNote;
        var shown = hasSaveFile ? path : path + TitleMarkers.UnsavedNote;
        // The song title goes on a second line when it is not already the file's name.
        var differs = !string.IsNullOrWhiteSpace(songTitle) && !string.Equals(songTitle, name, StringComparison.Ordinal)
            && !string.Equals(songTitle, System.IO.Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal);
        return differs ? shown + "\n" + songTitle : shown;
    }
}

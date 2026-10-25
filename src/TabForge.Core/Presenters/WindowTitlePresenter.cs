// Owns: the main window title text: "TabForge - {song title}", then the unsaved marker and the degraded-mode suffix, built from plain state.
// Does not own: setting the window's Title, reading the project or the close flow (the window passes those values in).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>The main window title for a song, from its title and its unsaved and degraded flags.</summary>
public static class WindowTitlePresenter
{
    public static string Title(string songTitle, bool isDirty, bool isDegraded) =>
        $"TabForge - {songTitle}{TitleMarkers.DirtySuffix(isDirty)}{TitleMarkers.DegradedSuffix(isDegraded)}";
}

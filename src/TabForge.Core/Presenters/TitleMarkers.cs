// Owns: the unsaved-changes marker, the never-saved note and the degraded-mode suffix that window and tab titles share, as plain text.
// Does not own: deciding when a song is unsaved or degraded (the document and the close flow do), or drawing a title.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>Title markers shared by the window title, the document tab labels and the song stats heading, so they all read the same.</summary>
public static class TitleMarkers
{
    /// <summary>Shown after a title while the song has unsaved changes.</summary>
    public const string DirtyMarker = " *";

    /// <summary>Shown after a song's name while it has never been saved: the tab tooltip and the song stats heading.</summary>
    public const string UnsavedNote = " (unsaved)";

    /// <summary>Shown after a title in degraded mode: the model may be inconsistent after an unexpected error.</summary>
    public const string DegradedMarker = "  [after an error: restart recommended]";

    /// <summary>The unsaved marker, or nothing when the song is clean.</summary>
    public static string DirtySuffix(bool isDirty) => isDirty ? DirtyMarker : "";

    /// <summary>The restart note, or nothing when the window is not degraded.</summary>
    public static string DegradedSuffix(bool isDegraded) => isDegraded ? DegradedMarker : "";
}

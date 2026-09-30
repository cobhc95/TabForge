using System.Windows;

namespace TabForge.Documents;

/// <summary>
/// Process-wide state for a tab drag in progress. The live <see cref="DocumentSession"/> is carried
/// here so an in-process drop moves the real document (undo history included) instead of round-tripping
/// through JSON. A serialized payload is attached to the OLE data object as well, so a drop into
/// another TabForge process still works.
/// </summary>
public static class TabDragService
{
    /// <summary>OLE clipboard format used for cross-process tab drops.</summary>
    public const string Format = "TabForge.DocumentTab";

    public static TabDragState? Current { get; set; }

    public static bool IsDragging => Current is not null;
}

public sealed class TabDragState
{
    public DocumentSession Session { get; init; } = null!;
    public Window? Source { get; init; }
    /// <summary>Set by whichever tab bar accepts the drop for a *cross-window* transfer.</summary>
    public bool Consumed { get; set; }
    /// <summary>Set when this tab bar reordered the tab in its own strip; the drag is done, no detach.</summary>
    public bool HandledLocally { get; set; }
    /// <summary>Set when the user cancels the drag with Esc (no detach in that case).</summary>
    public bool Cancelled { get; set; }
    /// <summary>Project JSON, for a drop handled by a different process.</summary>
    public string Payload { get; init; } = "";
    public string? PayloadPath { get; init; }
}

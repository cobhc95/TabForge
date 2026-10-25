namespace TabForge.Documents;

// Owns: the outcomes of placing an opened song among a window's tabs.
// Does not own: the placement itself (DocumentPlacement, TabForge project) or what the window does with the placed song (DocumentRoutingDecision).

public enum PlacementKind
{
    /// <summary>Opened as a new tab.</summary>
    Added,
    /// <summary>Replaced the target tab (its playback is released).</summary>
    Replaced,
    /// <summary>Replaced every open tab (startup / file association launch).</summary>
    ReplacedAll,
    /// <summary>The target has unsaved changes and the person wants them saved: the new song opened beside it; the target closes once its save succeeded.</summary>
    OpenedBesideToSave,
    /// <summary>The person kept the target tab: nothing was opened.</summary>
    Kept,
}

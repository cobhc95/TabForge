namespace TabForge.Documents;

// Owns: the toolkit-free rules the window applies when it opens a song among its tabs: where a new tab is inserted, whether a shown tab that
//     plays is stopped before it is replaced, and how the placed song is shown (playback-on-switch policy, save-then-close of the target).
// Does not own: the tab collection and its placement (DocumentManager, DocumentPlacement), loading and saving, or the unsaved-changes question.
// Tests: no dedicated test class.

/// <summary>What the window does with a song after it was placed.</summary>
/// <param name="Activate">False when nothing was opened: the person kept the target tab.</param>
/// <param name="ApplyPlaybackSwitchPolicy">The playback-on-tab-switch policy applies to showing the song.</param>
/// <param name="SaveThenCloseTarget">The target has unsaved changes the person chose to save: it closes once that save succeeds.</param>
public readonly record struct PlacementActivation(bool Activate, bool ApplyPlaybackSwitchPolicy, bool SaveThenCloseTarget);

public static class DocumentRoutingDecision
{
    /// <summary>The insert index of a new tab: the end of the strip, or right after the active tab.</summary>
    public static int NewTabIndex(bool appendAtEnd, int documentCount, int activeIndex) =>
        appendAtEnd ? documentCount : activeIndex + 1;

    /// <summary>
    /// Only the shown tab is stopped before the window replaces it while it plays. A playing tab that is not shown is released by the
    /// placement when it is replaced.
    /// </summary>
    public static bool StopsShownTargetFirst(bool hasTarget, bool targetIsActive, bool targetIsPlaying) =>
        hasTarget && targetIsActive && targetIsPlaying;

    /// <summary>
    /// How a placed song is shown. Replacing the tab on screen switches nothing, so the playback-on-switch policy does not apply; replacing
    /// another tab (a background import) switches away from the displayed one, so it applies as for any other switch.
    /// </summary>
    /// <param name="kind">How the song was placed.</param>
    /// <param name="targetIsDisplayed">The replaced target is the tab on screen.</param>
    public static PlacementActivation ActivationAfterPlacement(PlacementKind kind, bool targetIsDisplayed) => kind switch
    {
        PlacementKind.Kept => new(Activate: false, ApplyPlaybackSwitchPolicy: false, SaveThenCloseTarget: false),
        PlacementKind.ReplacedAll => new(Activate: true, ApplyPlaybackSwitchPolicy: false, SaveThenCloseTarget: false),
        PlacementKind.Replaced => new(Activate: true, ApplyPlaybackSwitchPolicy: !targetIsDisplayed, SaveThenCloseTarget: false),
        PlacementKind.OpenedBesideToSave => new(Activate: true, ApplyPlaybackSwitchPolicy: true, SaveThenCloseTarget: true),
        PlacementKind.Added => new(Activate: true, ApplyPlaybackSwitchPolicy: true, SaveThenCloseTarget: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown placement kind."),
    };
}

namespace TabForge.Documents;

// Owns: where a new tab is inserted when the window opens a song.
// Does not own: the tab collection and its placement (DocumentManager, DocumentPlacement), loading and saving.
// Tests: no dedicated test class.

public static class DocumentRoutingDecision
{
    /// <summary>The insert index of a new tab: the end of the strip, or right after the active tab.</summary>
    public static int NewTabIndex(bool appendAtEnd, int documentCount, int activeIndex) =>
        appendAtEnd ? documentCount : activeIndex + 1;

}

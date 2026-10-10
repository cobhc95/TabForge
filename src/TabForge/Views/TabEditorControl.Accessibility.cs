using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using TabForge.Models;
using TabForge.Views.Score;

namespace TabForge.Views;

// Owns: the editor's automation: the peer over the describer (EditorPeer, EditorDescriber), the spoken announcements
//   (AnnounceText, AnnounceCursor), the structure rebuild, the export metrics for the pages, and the self-test seam that overrides
//   the 'clients listening' answer.
// Does not own: the peer and the describer themselves (Views/Score/EditorAutomation.cs).
// Tests: TestTabEditorAutomationSnapshot.

public sealed partial class TabEditorControl : IEditorDescribeHost
{
    /// <summary>Self-test hook: null uses the real "is any automation client listening" answer.</summary>
    internal static bool? AutomationListenerOverride;
    /// <summary>Self-test counters: how many structure trees / cursor announcements were built.</summary>
    internal static int StructureBuilds, AnnounceBuilds;

    private static bool ClientsListening => AutomationListenerOverride ?? AutomationInteropProvider.ClientsAreListening;

    private EditorDescriber? _describer;
    internal EditorDescriber Describer => _describer ??= new EditorDescriber(this);

    SongProject? IEditorDescribeHost.Project => _project;
    (int m1, int c1, int m2, int c2) IEditorDescribeHost.SelectionRange() => SelectionRange();
    ScorePageLayout IEditorDescribeHost.PageLayout(TrackModel track) => Layout.GetLayout(track);


    /// <summary>Speaks a text through the editor peer (the "read" commands); the peer is created on demand here.</summary>
    public void AnnounceText(string text)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(this) is EditorPeer peer) peer.Speak(text);
    }

    /// <summary>Layout facts the PDF exporter needs to slice the score into pages (page width, header and system heights).</summary>
    internal (double PageWidth, double HeaderHeight, double SystemHeight, int Systems) ExportMetrics()
    {
        var track = Track;
        var systems = track is null ? 0 : Layout.GetLayout(track).SystemCount;   // first: the layout scan sets the system height
        return (PageWidth, HeaderHeight, SystemHeight, systems);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorPeer(this, Describer);

    /// <summary>Drops the structural tree (called by score layout invalidation: edits, track and project changes).</summary>
    private void InvalidateStructure()
    {
        if (UIElementAutomationPeer.FromElement(this) is EditorPeer peer) peer.Invalidate();
    }

    /// <summary>The cursor moved: the structural tree goes stale only when it left the cached system; then the announcement, if a client listens.</summary>
    private void AnnounceCursor()
    {
        var peer = UIElementAutomationPeer.FromElement(this) as EditorPeer;
        peer?.CursorMoved();
        if (!ClientsListening) return;   // no screen reader attached: no strings built, no events raised
        peer?.Announce();
    }
}

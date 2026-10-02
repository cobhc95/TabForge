using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using TabForge.Models;
using TabForge.Views.Score;

namespace TabForge.Views;

// TabEditorControl: screen-reader support. The texts and the peers live in Views/Score/EditorAutomation.cs; nothing is built
// until a client asks for the children, and cursor announcements cost nothing while no automation client listens.
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
    ScorePageLayout IEditorDescribeHost.PageLayout(TrackModel track) => GetScoreLayout(track);

    public string DescribeCursor() => Describer.Cursor();
    public string DescribePosition() => Describer.Position();
    public string DescribeBar() => Describer.Bar();

    /// <summary>Speaks a text through the editor peer (the "read" commands); the peer is created on demand here.</summary>
    public void AnnounceText(string text)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(this) is EditorPeer peer) peer.Speak(text);
    }

    /// <summary>Layout facts the PDF exporter needs to slice the score into pages (page width, header and system heights).</summary>
    internal (double PageWidth, double HeaderHeight, double SystemHeight, int Systems) ExportMetrics()
    {
        var track = Track;
        var systems = track is null ? 0 : GetScoreLayout(track).SystemCount;   // first: the layout scan sets the system height
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

using System.Linq;
using System.Windows.Automation.Peers;

namespace TabForge.Views;

// ArrangementPanel: the screen-reader summary (ArrangementAutomation.cs holds the peers).
public sealed partial class ArrangementPanel
{
    /// <summary>Tracks, bars and clips in one sentence (also the panel peer's Value).</summary>
    internal string DescribeArrangement()
    {
        var project = _project;
        if (project is null) return "No score open";
        var bars = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        var clips = project.Tracks.Sum(t => t.AudioClips.Count);
        return $"{project.Tracks.Count} tracks, {bars} bars, {clips} audio clips";
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ArrangementPanelPeer(this);
}

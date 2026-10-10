using System.Linq;
using System.Windows.Automation.Peers;

namespace TabForge.Views;

// Owns: the panel's accessibility: the one-sentence description of the tracks, bars and clips (DescribeArrangement) and the
//   panel's automation peer.
// Does not own: the peer class (ArrangementAutomation.cs) and the timeline's own description (TrackTimeline.Accessibility.cs).
// Tests: no named test.

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

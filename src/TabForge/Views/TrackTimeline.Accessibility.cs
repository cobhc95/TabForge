using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace TabForge.Views;

// Owns: the timeline's accessibility: its one-sentence description (DescribeTimeline) and the timeline's automation peer
//   (TimelinePeer, value provider).
// Does not own: the arrangement's description (ArrangementPanel.Accessibility.cs).
// Tests: no named test.

internal sealed partial class TrackTimeline
{
    internal string DescribeTimeline()
    {
        var project = _project;
        if (project is null) return "No score open";
        var bars = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        var text = $"{bars} bars on {project.Tracks.Count} tracks, {project.Tracks.Sum(t => t.AudioClips.Count)} audio clips";
        if (SelectedBar >= 0) text += $"; selected bar {SelectedBar + 1}";
        if (SelectedTrack >= 0 && SelectedTrack < project.Tracks.Count) text += $" on track {SelectedTrack + 1}, {project.Tracks[SelectedTrack].Name}";
        if (PlayheadBar >= 0) text += $"; playhead at bar {PlayheadBar + 1}";
        return text;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TimelinePeer(this);

    private sealed class TimelinePeer : FrameworkElementAutomationPeer, IValueProvider
    {
        private readonly TrackTimeline _timeline;
        public TimelinePeer(TrackTimeline owner) : base(owner) => _timeline = owner;
        protected override string GetClassNameCore() => nameof(TrackTimeline);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetLocalizedControlTypeCore() => "timeline";
        protected override string GetNameCore() =>
            string.IsNullOrEmpty(AutomationProperties.GetName(_timeline)) ? "Arrangement timeline" : AutomationProperties.GetName(_timeline);
        protected override string GetItemStatusCore() => _timeline.DescribeTimeline();
        protected override bool IsControlElementCore() => true;
        public string Value => _timeline.DescribeTimeline();
        public bool IsReadOnly => true;
        public void SetValue(string value) => throw new InvalidOperationException("The timeline is read-only through automation.");
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
    }
}

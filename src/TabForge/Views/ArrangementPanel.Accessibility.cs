using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace TabForge.Views;

/// <summary>A track row of the arrangement: exposed to screen readers as a list item ("Track 2: Lead, muted, solo").</summary>
internal sealed class TrackRowBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    private sealed class RowPeer : FrameworkElementAutomationPeer
    {
        public RowPeer(TrackRowBorder owner) : base(owner) { }
        protected override string GetClassNameCore() => "TrackRow";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override string GetLocalizedControlTypeCore() => "track";
        protected override string GetNameCore() => AutomationProperties.GetName(Owner);
        protected override string GetItemStatusCore()
        {
            var name = AutomationProperties.GetName(Owner);
            var status = new List<string>();
            if (name.Contains(", muted", StringComparison.Ordinal)) status.Add("muted");
            if (name.Contains(", solo", StringComparison.Ordinal)) status.Add("solo");
            return status.Count == 0 ? "audible" : string.Join(", ", status);
        }
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }
}

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

    protected override AutomationPeer OnCreateAutomationPeer() => new PanelPeer(this);

    private sealed class PanelPeer : FrameworkElementAutomationPeer, IValueProvider
    {
        private readonly ArrangementPanel _panel;
        public PanelPeer(ArrangementPanel owner) : base(owner) => _panel = owner;
        protected override string GetClassNameCore() => nameof(ArrangementPanel);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
        protected override string GetLocalizedControlTypeCore() => "arrangement";
        protected override string GetNameCore() =>
            string.IsNullOrEmpty(AutomationProperties.GetName(_panel)) ? "Arrangement" : AutomationProperties.GetName(_panel);
        protected override string GetItemStatusCore() => _panel.DescribeArrangement();
        protected override bool IsControlElementCore() => true;
        public string Value => _panel.DescribeArrangement();
        public bool IsReadOnly => true;
        public void SetValue(string value) => throw new InvalidOperationException("The arrangement is read-only through automation.");
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
    }
}

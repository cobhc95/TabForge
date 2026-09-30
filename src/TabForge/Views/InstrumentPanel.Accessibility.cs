using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// InstrumentPanel: accessibility. The fretboard / keyboard / drum map is named and reports the notes currently shown.
public sealed partial class InstrumentPanel
{
    /// <summary>The instrument and the notes on it right now, e.g. "Fretboard: string 3 fret 5 (A), string 2 fret 5 (E)".</summary>
    internal string DescribeShownNotes()
    {
        var state = _state;
        if (state is null) return $"{InstrumentName}: nothing shown";
        var shown = new List<VisualNote>();
        if (state.Current is { } current) shown.Add(current);
        foreach (var note in state.Notes)
            if ((note.Held || note.Role is VisualRole.Current or VisualRole.Selected) && !shown.Contains(note)) shown.Add(note);
        if (shown.Count == 0) return $"{InstrumentName}: no notes lit" + (state.ChordName is { Length: > 0 } chord ? $", chord {chord}" : "") +
                                     (state.ScaleName is { Length: > 0 } scale ? $", scale {scale}" : "");
        var parts = shown.Take(8).Select(n => state.Kind == InstrumentKind.Guitar || state.Kind == InstrumentKind.Bass
            ? $"string {n.StringIndex + 1} fret {n.Fret} ({MusicTheoryService.NoteName(n.Midi)})"
            : MusicTheoryService.NoteName(n.Midi));
        return $"{InstrumentName}: {string.Join(", ", parts)}" + (state.ChordName is { Length: > 0 } c ? $"; chord {c}" : "");
    }

    private string InstrumentName => _state?.Kind switch
    {
        InstrumentKind.Drums => "Drum map",
        InstrumentKind.Keyboard => "Keyboard",
        _ => "Fretboard"
    };

    protected override AutomationPeer OnCreateAutomationPeer() => new InstrumentPeer(this);

    private sealed class InstrumentPeer : FrameworkElementAutomationPeer, IValueProvider
    {
        private readonly InstrumentPanel _panel;
        public InstrumentPeer(InstrumentPanel owner) : base(owner) => _panel = owner;
        protected override string GetClassNameCore() => nameof(InstrumentPanel);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetLocalizedControlTypeCore() => "instrument display";
        protected override string GetNameCore() =>
            string.IsNullOrEmpty(AutomationProperties.GetName(_panel)) ? _panel.InstrumentName : AutomationProperties.GetName(_panel);
        protected override string GetItemStatusCore() => _panel.DescribeShownNotes();
        protected override bool IsControlElementCore() => true;
        public string Value => _panel.DescribeShownNotes();
        public bool IsReadOnly => true;
        public void SetValue(string value) => throw new InvalidOperationException("The instrument display is read-only through automation.");
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
    }
}

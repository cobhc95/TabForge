using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// TabEditorControl: structural view for screen readers (A5-13). The editor peer exposes bar -> beat elements for the
// system that holds the cursor. Nothing is built until a client asks for the children, it is cached per song
// revision (SongProject.TimelineRevision), and cursor announcements cost nothing while no automation client listens.
public sealed partial class TabEditorControl
{
    /// <summary>Self-test hook: null uses the real "is any automation client listening" answer.</summary>
    internal static bool? AutomationListenerOverride;
    /// <summary>Self-test counters: how many structure trees / cursor announcements were built.</summary>
    internal static int StructureBuilds, AnnounceBuilds;

    private static bool ClientsListening => AutomationListenerOverride ?? AutomationInteropProvider.ClientsAreListening;

    // What the last cursor announcement said, so the next one mentions only changes.
    private bool _ctxKnown;
    private string _ctxSection = "";
    private string _ctxTimeSig = "";
    private int _ctxTempo;

    // ---------- text builders ----------

    private static readonly Regex CamelWords = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>"PalmMute" -> "palm mute" (the origin/destination bookkeeping flags of hammer-ons are not spoken).</summary>
    internal static string TechniqueText(TabNote note)
    {
        if (note.Techniques.Count == 0) return "";
        var names = new List<string>();
        foreach (var t in note.Techniques.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (t.Equals(TechniqueNames.HopoOrigin, StringComparison.OrdinalIgnoreCase) ||
                t.Equals(TechniqueNames.HopoDestination, StringComparison.OrdinalIgnoreCase)) continue;
            if (t.Equals(TechniqueNames.PalmMuteLegacy, StringComparison.OrdinalIgnoreCase)) { names.Add("palm mute"); continue; }
            if (t.Equals(TechniqueNames.Hopo, StringComparison.OrdinalIgnoreCase)) { names.Add("hammer-on or pull-off"); continue; }
            names.Add(CamelWords.Replace(t, " ").ToLowerInvariant());
        }
        return string.Join(", ", names.Distinct());
    }

    /// <summary>"string 2 fret 7, palm mute" for one note of a beat.</summary>
    private string DescribeNoteOfBeat(TrackModel track, TabNote note)
    {
        var text = $"string {note.StringIndex + 1} " + (note.Dead ? "dead note" : $"fret {note.Fret}");
        if (note.Ghost) text += ", ghost";
        var techniques = TechniqueText(note);
        return techniques.Length > 0 ? text + ", " + techniques : text;
    }

    /// <summary>"Bar 5, beat 3, eighth note, string 2 fret 7, palm mute" - the name of one beat element.</summary>
    internal string DescribeBeat(int bar, int cellIndex)
    {
        var track = Track;
        if (track is null || bar < 0 || bar >= track.Measures.Count) return "No score open";
        var measure = track.Measures[bar];
        if (cellIndex < 0 || cellIndex >= measure.Cells.Count) return $"Bar {bar + 1}";
        var cell = measure.Cells[cellIndex];
        var text = $"Bar {bar + 1}, beat {cellIndex + 1}, {DurationName(cell)}";
        if (cell.Notes.Count > 0)
            text += ", " + string.Join("; ", cell.Notes.OrderBy(n => n.StringIndex).Select(n => DescribeNoteOfBeat(track, n)));
        if (!string.IsNullOrWhiteSpace(cell.ChordName)) text += ", chord " + cell.ChordName;
        return text;
    }

    private string SectionAt(int bar)
    {
        var track = Track;
        if (track is null) return "";
        for (var i = Math.Min(bar, track.Measures.Count - 1); i >= 0; i--)
            if (!string.IsNullOrWhiteSpace(track.Measures[i].SectionName)) return track.Measures[i].SectionName.Trim();
        return "";
    }

    private (string TimeSig, int Tempo) BarContext(int bar)
    {
        var p = _project;
        if (p is null) return ("", 0);
        var m = MusicTime.BarOf(p, bar);
        return ($"{m?.TimeSigNum ?? p.TimeSignatureNumerator}/{m?.TimeSigDenom ?? p.TimeSignatureDenominator}", MusicTime.TempoAt(p, bar));
    }

    /// <summary>Approximate time of the start of a bar (repeats not followed), as m:ss.</summary>
    private string BarTimeText(int bar)
    {
        var p = _project;
        if (p is null) return "0:00";
        double ms = 0;
        for (var i = 0; i < bar; i++)
            ms += MusicTime.OffsetMs(MusicTime.BarOf(p, i), MusicTime.BarSlots(p, i), MusicTime.TempoAt(p, i));
        var seconds = (int)Math.Round(ms / 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>"Read position": track, bar, beat, string, section, signature, tempo and time.</summary>
    public string DescribePosition()
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return "No score open";
        var (timeSig, tempo) = BarContext(SelectedMeasure);
        var section = SectionAt(SelectedMeasure);
        return $"Track {track.Name}, bar {SelectedMeasure + 1} of {track.Measures.Count}, beat {SelectedCell + 1}, string {SelectedString + 1}" +
            (section.Length > 0 ? $", section {section}" : "") + $", time signature {timeSig}, tempo {tempo}, about {BarTimeText(SelectedMeasure)}";
    }

    /// <summary>"Read current bar": the bar's header and every beat in it.</summary>
    public string DescribeBar()
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return "No score open";
        var (timeSig, tempo) = BarContext(SelectedMeasure);
        var section = SectionAt(SelectedMeasure);
        var text = $"Bar {SelectedMeasure + 1}" + (section.Length > 0 ? $", section {section}" : "") + $", {timeSig}, tempo {tempo}";
        var beats = BeatCells(track.Measures[SelectedMeasure]);
        if (beats.Count == 0) return text + ", empty";
        return text + ". " + string.Join(". ", beats.Select(i => DescribeBeat(SelectedMeasure, i)));
    }

    private static List<int> BeatCells(MeasureModel measure) => MusicTime.BeatSlots(measure);

    /// <summary>The cursor text plus what changed since the last announcement (new section, time signature, tempo).</summary>
    internal string DescribeCursorAnnouncement()
    {
        AnnounceBuilds++;
        var text = DescribeCursor();
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return text;
        var section = SectionAt(SelectedMeasure);
        var (timeSig, tempo) = BarContext(SelectedMeasure);
        var extra = new List<string>();
        if (_ctxKnown)
        {
            if (section.Length > 0 && section != _ctxSection) extra.Add("entering section " + section);
            if (timeSig != _ctxTimeSig) extra.Add("time signature " + timeSig);
            if (tempo != _ctxTempo) extra.Add("tempo " + tempo);
        }
        else if (section.Length > 0) extra.Add("section " + section);
        (_ctxKnown, _ctxSection, _ctxTimeSig, _ctxTempo) = (true, section, timeSig, tempo);
        return extra.Count == 0 ? text : text + "; " + string.Join("; ", extra);
    }

    /// <summary>Speaks a text through the editor peer (the "read" commands); the peer is created on demand here.</summary>
    public void AnnounceText(string text)
    {
        if (UIElementAutomationPeer.CreatePeerForElement(this) is EditorPeer peer) peer.Speak(text);
    }

    // ---------- structure cache ----------

    /// <summary>Drops the structural tree (called by score layout invalidation: edits, track and project changes).</summary>
    private void InvalidateStructure()
    {
        if (UIElementAutomationPeer.FromElement(this) is EditorPeer peer) peer.Invalidate();
    }

    /// <summary>True when the cached tree belongs to another system than the cursor's (a client is reading the structure).</summary>
    private void StructureCursorMoved()
    {
        if (UIElementAutomationPeer.FromElement(this) is EditorPeer peer) peer.CursorMoved();
    }

    internal int CurrentSystemIndex()
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return -1;
        return GetScoreLayout(track).SystemForMeasure(Math.Clamp(SelectedMeasure, 0, track.Measures.Count - 1));
    }

    internal (int First, int Last) SystemBars(int system)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return (0, -1);
        var layout = GetScoreLayout(track);
        if (system < 0 || system >= layout.SystemCount) return (0, -1);
        return (layout.Systems[system].FirstMeasure, layout.Systems[system].LastMeasure);
    }

    private sealed record StructureKey(SongProject? Project, int Revision, int Track, int System);

    private StructureKey CurrentStructureKey() =>
        new(_project, _project?.TimelineRevision ?? 0, _selectedTrackIndex, CurrentSystemIndex());

    // ---------- peers without elements ----------

    private abstract class StructPeer : AutomationPeer
    {
        protected readonly EditorPeer Owner;
        protected StructPeer(EditorPeer owner) => Owner = owner;
        protected override string GetClassNameCore() => GetType().Name;
        protected override string GetAutomationIdCore() => "";
        protected override Rect GetBoundingRectangleCore() => Owner.GetBoundingRectangle();
        protected override bool IsOffscreenCore() => false;
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
        protected override string GetItemStatusCore() => "";
        protected override string GetItemTypeCore() => "";
        protected override string GetAcceleratorKeyCore() => "";
        protected override string GetAccessKeyCore() => "";
        protected override string GetHelpTextCore() => "";
        protected override bool HasKeyboardFocusCore() => false;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override bool IsEnabledCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsPasswordCore() => false;
        protected override bool IsRequiredForFormCore() => false;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override Point GetClickablePointCore() => new(double.NaN, double.NaN);
        protected override void SetFocusCore() => Owner.SetFocus();
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        public override object? GetPattern(PatternInterface patternInterface) => null;
    }

    /// <summary>A bar: a one-row grid whose cells are its beats.</summary>
    private sealed class BarPeer : StructPeer, IGridProvider
    {
        private readonly TabEditorControl _editor;
        private readonly int _bar;
        private List<AutomationPeer>? _beats;
        public BarPeer(EditorPeer owner, TabEditorControl editor, int bar) : base(owner) { _editor = editor; _bar = bar; }
        internal int Bar => _bar;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetLocalizedControlTypeCore() => "bar";
        protected override string GetNameCore()
        {
            var track = _editor.Track;
            var text = $"Bar {_bar + 1}";
            if (track is not null && _bar < track.Measures.Count && !string.IsNullOrWhiteSpace(track.Measures[_bar].SectionName))
                text += ", section " + track.Measures[_bar].SectionName.Trim();
            return _bar == _editor.SelectedMeasure ? text + ", current bar" : text;
        }
        protected override List<AutomationPeer>? GetChildrenCore() => Beats();
        private List<AutomationPeer> Beats()
        {
            if (_beats is not null) return _beats;
            var list = new List<AutomationPeer>();
            var track = _editor.Track;
            if (track is not null && _bar < track.Measures.Count)
            {
                var cells = BeatCells(track.Measures[_bar]);
                for (var i = 0; i < cells.Count; i++) list.Add(new BeatPeer(Owner, _editor, this, _bar, cells[i], i));
            }
            return _beats = list;
        }
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Grid ? this : null;
        public int RowCount => 1;
        public int ColumnCount => Beats().Count;
        public IRawElementProviderSimple? GetItem(int row, int column) =>
            row == 0 && column >= 0 && column < Beats().Count ? ProviderFromPeer(Beats()[column]) : null;
        internal IRawElementProviderSimple? Provider => ProviderFromPeer(this);
    }

    /// <summary>One beat, named like "Bar 5, beat 3, eighth note, string 2 fret 7, palm mute".</summary>
    private sealed class BeatPeer : StructPeer, IGridItemProvider
    {
        private readonly TabEditorControl _editor;
        private readonly BarPeer _barPeer;
        private readonly int _bar, _cell, _column;
        public BeatPeer(EditorPeer owner, TabEditorControl editor, BarPeer barPeer, int bar, int cell, int column) : base(owner)
        { _editor = editor; _barPeer = barPeer; _bar = bar; _cell = cell; _column = column; }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override string GetLocalizedControlTypeCore() => "beat";
        protected override string GetNameCore() => _editor.DescribeBeat(_bar, _cell);
        protected override string GetItemStatusCore() =>
            _bar == _editor.SelectedMeasure && _cell == _editor.SelectedCell ? "cursor" : "";
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.GridItem ? this : null;
        public int Row => 0;
        public int Column => _column;
        public int RowSpan => 1;
        public int ColumnSpan => 1;
        public IRawElementProviderSimple? ContainingGrid => _barPeer.Provider;
    }
}

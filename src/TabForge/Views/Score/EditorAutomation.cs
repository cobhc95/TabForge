using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

/// <summary>What the describer reads from the editor: the shown track, the cursor, the selection and the page layout.</summary>
internal interface IEditorDescribeHost
{
    SongProject? Project { get; }
    TrackModel? Track { get; }
    int SelectedTrackIndex { get; }
    int SelectedMeasure { get; }
    int SelectedCell { get; }
    int SelectedString { get; }
    bool HasSelection { get; }
    (int m1, int c1, int m2, int c2) SelectionRange();
    ScorePageLayout PageLayout(TrackModel track);
}

/// <summary>Cache key of the structural tree: it is stale when the song revision, the track or the cursor's system changes.</summary>
internal sealed record StructureKey(SongProject? Project, int Revision, int Track, int System);

/// <summary>
/// The spoken and structural descriptions of the score editor for screen readers: cursor, beat, bar and position texts, the
/// cursor announcement (which mentions only what changed since the last one) and the system/bar lookups of the structure tree.
/// It holds the host only; <see cref="Release"/> forgets the last announcement.
/// </summary>
internal sealed class EditorDescriber
{
    private readonly IEditorDescribeHost _h;
    private bool _ctxKnown;
    private string _ctxSection = "";
    private string _ctxTimeSig = "";
    private int _ctxTempo;

    public EditorDescriber(IEditorDescribeHost host) => _h = host;

    /// <summary>Forgets the last announcement.</summary>
    public void Release() => (_ctxKnown, _ctxSection, _ctxTimeSig, _ctxTempo) = (false, "", "", 0);

    /// <summary>Spoken description of the cursor: track, bar, beat, string, fret and note name (screen readers).</summary>
    public string Cursor()
    {
        var h = _h;
        var track = h.Track;
        if (track is null || h.SelectedMeasure < 0 || h.SelectedMeasure >= track.Measures.Count) return "No score open";
        var measure = track.Measures[h.SelectedMeasure];
        var text = $"Track {track.Name}, bar {h.SelectedMeasure + 1}, beat {h.SelectedCell + 1}, string {h.SelectedString + 1}";
        if (h.SelectedCell >= 0 && h.SelectedCell < measure.Cells.Count)
        {
            var cell = measure.Cells[h.SelectedCell];
            text += ", " + ScoreMarkText.DurationName(cell);
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == h.SelectedString);
            if (cell.IsRest) text += ", rest";
            else if (note is null) text += ", empty";
            else
            {
                var midi = note.MidiValue > 0 ? note.MidiValue
                    : h.SelectedString < track.StringTunings.Count ? track.PitchOf(h.SelectedString, note.Fret) : -1;
                text += note.Dead ? ", dead note" : $", fret {note.Fret}";
                if (midi >= 0 && !note.Dead) text += ", note " + MusicTheoryService.NoteName(midi).Replace("#", " sharp");
                if (note.Ghost) text += ", ghost";
                var techniques = ScoreMarkText.TechniqueText(note);
                if (techniques.Length > 0) text += ", " + techniques;
            }
        }
        if (h.HasSelection)
        {
            var (m1, c1, m2, c2) = h.SelectionRange();
            if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
            text += $"; selection from bar {m1 + 1} beat {c1 + 1} to bar {m2 + 1} beat {c2 + 1}";
        }
        return text;
    }

    /// <summary>"string 2 fret 7, palm mute" for one note of a beat.</summary>
    private static string NoteOfBeat(TabNote note)
    {
        var text = $"string {note.StringIndex + 1} " + (note.Dead ? "dead note" : $"fret {note.Fret}");
        if (note.Ghost) text += ", ghost";
        var techniques = ScoreMarkText.TechniqueText(note);
        return techniques.Length > 0 ? text + ", " + techniques : text;
    }

    /// <summary>"Bar 5, beat 3, eighth note, string 2 fret 7, palm mute" - the name of one beat element.</summary>
    public string Beat(int bar, int cellIndex)
    {
        var track = _h.Track;
        if (track is null || bar < 0 || bar >= track.Measures.Count) return "No score open";
        var measure = track.Measures[bar];
        if (cellIndex < 0 || cellIndex >= measure.Cells.Count) return $"Bar {bar + 1}";
        var cell = measure.Cells[cellIndex];
        var text = $"Bar {bar + 1}, beat {cellIndex + 1}, {ScoreMarkText.DurationName(cell)}";
        if (cell.Notes.Count > 0)
            text += ", " + string.Join("; ", cell.Notes.OrderBy(n => n.StringIndex).Select(NoteOfBeat));
        if (!string.IsNullOrWhiteSpace(cell.ChordName)) text += ", chord " + cell.ChordName;
        return text;
    }

    private string SectionAt(int bar)
    {
        var track = _h.Track;
        if (track is null) return "";
        for (var i = Math.Min(bar, track.Measures.Count - 1); i >= 0; i--)
            if (!string.IsNullOrWhiteSpace(track.Measures[i].SectionName)) return track.Measures[i].SectionName.Trim();
        return "";
    }

    private (string TimeSig, int Tempo) BarContext(int bar)
    {
        var p = _h.Project;
        if (p is null) return ("", 0);
        var m = MusicTime.BarOf(p, bar);
        return ($"{m?.TimeSigNum ?? p.TimeSignatureNumerator}/{m?.TimeSigDenom ?? p.TimeSignatureDenominator}", MusicTime.TempoAt(p, bar));
    }

    /// <summary>Approximate time of the start of a bar (repeats not followed), as m:ss.</summary>
    private string BarTimeText(int bar)
    {
        var p = _h.Project;
        if (p is null) return "0:00";
        double ms = 0;
        for (var i = 0; i < bar; i++)
            ms += MusicTime.OffsetMs(MusicTime.BarOf(p, i), MusicTime.BarSlots(p, i), MusicTime.TempoAt(p, i));
        var seconds = (int)Math.Round(ms / 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>"Read position": track, bar, beat, string, section, signature, tempo and time.</summary>
    public string Position()
    {
        var h = _h;
        var track = h.Track;
        if (track is null || h.SelectedMeasure < 0 || h.SelectedMeasure >= track.Measures.Count) return "No score open";
        var (timeSig, tempo) = BarContext(h.SelectedMeasure);
        var section = SectionAt(h.SelectedMeasure);
        return $"Track {track.Name}, bar {h.SelectedMeasure + 1} of {track.Measures.Count}, beat {h.SelectedCell + 1}, string {h.SelectedString + 1}" +
            (section.Length > 0 ? $", section {section}" : "") + $", time signature {timeSig}, tempo {tempo}, about {BarTimeText(h.SelectedMeasure)}";
    }

    /// <summary>"Read current bar": the bar's header and every beat in it.</summary>
    public string Bar()
    {
        var h = _h;
        var track = h.Track;
        if (track is null || h.SelectedMeasure < 0 || h.SelectedMeasure >= track.Measures.Count) return "No score open";
        var (timeSig, tempo) = BarContext(h.SelectedMeasure);
        var section = SectionAt(h.SelectedMeasure);
        var text = $"Bar {h.SelectedMeasure + 1}" + (section.Length > 0 ? $", section {section}" : "") + $", {timeSig}, tempo {tempo}";
        var beats = BeatCells(track.Measures[h.SelectedMeasure]);
        if (beats.Count == 0) return text + ", empty";
        return text + ". " + string.Join(". ", beats.Select(i => Beat(h.SelectedMeasure, i)));
    }

    internal static List<int> BeatCells(MeasureModel measure) => MusicTime.BeatSlots(measure);

    /// <summary>The cursor text plus what changed since the last announcement (new section, time signature, tempo).</summary>
    public string CursorAnnouncement()
    {
        TabEditorControl.AnnounceBuilds++;
        var h = _h;
        var text = Cursor();
        var track = h.Track;
        if (track is null || h.SelectedMeasure < 0 || h.SelectedMeasure >= track.Measures.Count) return text;
        var section = SectionAt(h.SelectedMeasure);
        var (timeSig, tempo) = BarContext(h.SelectedMeasure);
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

    public int CurrentSystemIndex()
    {
        var h = _h;
        var track = h.Track;
        if (track is null || track.Measures.Count == 0) return -1;
        return h.PageLayout(track).SystemForMeasure(Math.Clamp(h.SelectedMeasure, 0, track.Measures.Count - 1));
    }

    public (int First, int Last) SystemBars(int system)
    {
        var track = _h.Track;
        if (track is null || track.Measures.Count == 0) return (0, -1);
        var layout = _h.PageLayout(track);
        if (system < 0 || system >= layout.SystemCount) return (0, -1);
        return (layout.Systems[system].FirstMeasure, layout.Systems[system].LastMeasure);
    }

    public StructureKey CurrentStructureKey() =>
        new(_h.Project, _h.Project?.TimelineRevision ?? 0, _h.SelectedTrackIndex, CurrentSystemIndex());
}

/// <summary>
/// UI Automation peer of the score editor: the cursor description is its read-only value, and its children are the bars (each
/// with its beats) of the system that holds the cursor, built when a client asks and cached per <see cref="StructureKey"/>.
/// </summary>
internal sealed class EditorPeer : FrameworkElementAutomationPeer, IValueProvider
{
    private readonly TabEditorControl _editor;
    private readonly EditorDescriber _describer;
    private StructureKey? _key;
    private List<AutomationPeer>? _bars;

    public EditorPeer(TabEditorControl owner, EditorDescriber describer) : base(owner) { _editor = owner; _describer = describer; }

    internal EditorDescriber Describer => _describer;
    internal TabEditorControl Editor => _editor;

    public string Value => _describer.Cursor();
    public bool IsReadOnly => true;
    public void SetValue(string value) => throw new InvalidOperationException("The score editor is read-only through automation.");
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

    protected override string GetClassNameCore() => nameof(TabEditorControl);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetLocalizedControlTypeCore() => "score editor";
    protected override string GetNameCore() =>
        string.IsNullOrEmpty(AutomationProperties.GetName(_editor)) ? "Tab editor" : AutomationProperties.GetName(_editor);
    protected override string GetItemStatusCore() => _describer.Cursor();
    protected override string GetHelpTextCore() => "Arrow keys move the cursor, digits set the fret, Enter or space plays.";
    protected override bool IsKeyboardFocusableCore() => true;

    protected override List<AutomationPeer>? GetChildrenCore()
    {
        var key = _describer.CurrentStructureKey();
        if (_bars is not null && key == _key) return _bars;
        TabEditorControl.StructureBuilds++;
        var list = new List<AutomationPeer>();
        var (first, last) = _describer.SystemBars(key.System);
        for (var bar = first; bar <= last; bar++) list.Add(new BarPeer(this, bar));
        _key = key;
        return _bars = list;
    }

    /// <summary>An edit or a track/project change: the tree is stale.</summary>
    public void Invalidate()
    {
        if (_bars is null) return;
        _bars = null; _key = null;
        ResetChildrenCache();
    }

    /// <summary>The cursor moved: the tree is stale only when it left the cached system.</summary>
    public void CursorMoved()
    {
        if (_bars is null || _key is null) return;
        if (_describer.CurrentSystemIndex() != _key.System) Invalidate();
    }

    public void Speak(string text) =>
        RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, "cursor");

    public void Announce()
    {
        var cursor = _describer.CursorAnnouncement();
        var value = _describer.Cursor();
        RaisePropertyChangedEvent(AutomationElement.ItemStatusProperty, "", value);
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, "", value);
        Speak(cursor);
    }
}

/// <summary>A peer without an element: it answers from its owner peer's editor.</summary>
internal abstract class StructPeer : AutomationPeer
{
    protected readonly EditorPeer Owner;
    protected StructPeer(EditorPeer owner) => Owner = owner;
    protected EditorDescriber Describer => Owner.Describer;
    protected TabEditorControl Editor => Owner.Editor;
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
internal sealed class BarPeer : StructPeer, IGridProvider
{
    private readonly int _bar;
    private List<AutomationPeer>? _beats;
    public BarPeer(EditorPeer owner, int bar) : base(owner) => _bar = bar;
    internal int Bar => _bar;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    protected override string GetLocalizedControlTypeCore() => "bar";
    protected override string GetNameCore()
    {
        var track = Editor.Track;
        var text = $"Bar {_bar + 1}";
        if (track is not null && _bar < track.Measures.Count && !string.IsNullOrWhiteSpace(track.Measures[_bar].SectionName))
            text += ", section " + track.Measures[_bar].SectionName.Trim();
        return _bar == Editor.SelectedMeasure ? text + ", current bar" : text;
    }
    protected override List<AutomationPeer>? GetChildrenCore() => Beats();
    private List<AutomationPeer> Beats()
    {
        if (_beats is not null) return _beats;
        var list = new List<AutomationPeer>();
        var track = Editor.Track;
        if (track is not null && _bar < track.Measures.Count)
        {
            var cells = EditorDescriber.BeatCells(track.Measures[_bar]);
            for (var i = 0; i < cells.Count; i++) list.Add(new BeatPeer(Owner, this, _bar, cells[i], i));
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
internal sealed class BeatPeer : StructPeer, IGridItemProvider
{
    private readonly BarPeer _barPeer;
    private readonly int _bar, _cell, _column;
    public BeatPeer(EditorPeer owner, BarPeer barPeer, int bar, int cell, int column) : base(owner)
    { _barPeer = barPeer; _bar = bar; _cell = cell; _column = column; }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string GetLocalizedControlTypeCore() => "beat";
    protected override string GetNameCore() => Describer.Beat(_bar, _cell);
    protected override string GetItemStatusCore() =>
        _bar == Editor.SelectedMeasure && _cell == Editor.SelectedCell ? "cursor" : "";
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.GridItem ? this : null;
    public int Row => 0;
    public int Column => _column;
    public int RowSpan => 1;
    public int ColumnSpan => 1;
    public IRawElementProviderSimple? ContainingGrid => _barPeer.Provider;
}

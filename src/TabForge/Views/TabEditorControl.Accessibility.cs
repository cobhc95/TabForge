using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

// TabEditorControl: accessibility (EditorPeer) and export helpers.
public sealed partial class TabEditorControl
{
    // ---------- accessibility / export helpers ----------

    /// <summary>Spoken description of the cursor: track, bar, beat, string, fret and note name (screen readers).</summary>
    public string DescribeCursor()
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return "No score open";
        var measure = track.Measures[SelectedMeasure];
        var text = $"Track {track.Name}, bar {SelectedMeasure + 1}, beat {SelectedCell + 1}, string {SelectedString + 1}";
        if (SelectedCell >= 0 && SelectedCell < measure.Cells.Count)
        {
            var cell = measure.Cells[SelectedCell];
            text += ", " + DurationName(cell);
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
            if (cell.IsRest) text += ", rest";
            else if (note is null) text += ", empty";
            else
            {
                var midi = note.MidiValue > 0 ? note.MidiValue
                    : SelectedString < track.StringTunings.Count ? track.PitchOf(SelectedString, note.Fret) : -1;
                text += note.Dead ? ", dead note" : $", fret {note.Fret}";
                if (midi >= 0 && !note.Dead) text += ", note " + TabForge.Services.MusicTheoryService.NoteName(midi).Replace("#", " sharp");
                if (note.Ghost) text += ", ghost";
                var techniques = TechniqueText(note);
                if (techniques.Length > 0) text += ", " + techniques;
            }
        }
        if (HasSelection)
        {
            var (m1, c1, m2, c2) = SelectionRange();
            if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
            text += $"; selection from bar {m1 + 1} beat {c1 + 1} to bar {m2 + 1} beat {c2 + 1}";
        }
        return text;
    }

    /// <summary>"quarter note", "dotted eighth note", "eighth triplet" ... for the beat's written duration.</summary>
    internal static string DurationName(TabCell cell)
    {
        var name = cell.DurationDenominator switch
        {
            1 => "whole", 2 => "half", 4 => "quarter", 8 => "eighth", 16 => "sixteenth", 32 => "thirty-second", 64 => "sixty-fourth",
            var d => $"1/{d}"
        };
        var dots = cell.Dots == 1 ? "dotted " : cell.Dots >= 2 ? "double-dotted " : "";
        return dots + name + (cell.IsRest ? " rest" : " note") + (cell.Tuplet.Numerator > 0 ? $" in a {cell.Tuplet.Numerator}-tuplet" : "");
    }

    /// <summary>Layout facts the PDF exporter needs to slice the score into pages (page width, header and system heights).</summary>
    internal (double PageWidth, double HeaderHeight, double SystemHeight, int Systems) ExportMetrics()
    {
        var track = Track;
        var systems = track is null ? 0 : GetScoreLayout(track).SystemCount;   // first: the layout scan sets the system height
        return (PageWidth, HeaderHeight, SystemHeight, systems);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new EditorPeer(this);

    private sealed class EditorPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer, System.Windows.Automation.Provider.IValueProvider
    {
        // The cursor description is the (read-only) Value: bar, beat, string, fret, duration and selection.
        public string Value => _editor.DescribeCursor();
        public bool IsReadOnly => true;
        public void SetValue(string value) => throw new InvalidOperationException("The score editor is read-only through automation.");
        public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface) =>
            patternInterface == System.Windows.Automation.Peers.PatternInterface.Value ? this : base.GetPattern(patternInterface);

        private readonly TabEditorControl _editor;
        public EditorPeer(TabEditorControl owner) : base(owner) => _editor = owner;
        protected override string GetClassNameCore() => nameof(TabEditorControl);
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Custom;
        protected override string GetLocalizedControlTypeCore() => "score editor";
        protected override string GetNameCore() =>
            string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(_editor)) ? "Tab editor" : System.Windows.Automation.AutomationProperties.GetName(_editor);
        protected override string GetItemStatusCore() => _editor.DescribeCursor();
        protected override string GetHelpTextCore() => "Arrow keys move the cursor, digits set the fret, Enter or space plays.";
        protected override bool IsKeyboardFocusableCore() => true;
        private StructureKey? _key;
        private List<AutomationPeer>? _bars;

        // Bars of the system that holds the cursor, each with its beats; built when a client asks, cached per song revision.
        protected override List<AutomationPeer>? GetChildrenCore()
        {
            var key = _editor.CurrentStructureKey();
            if (_bars is not null && key == _key) return _bars;
            StructureBuilds++;
            var list = new List<AutomationPeer>();
            var (first, last) = _editor.SystemBars(key.System);
            for (var bar = first; bar <= last; bar++) list.Add(new BarPeer(this, _editor, bar));
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
            if (_editor.CurrentSystemIndex() != _key.System) Invalidate();
        }

        public void Speak(string text) =>
            RaiseNotificationEvent(System.Windows.Automation.AutomationNotificationKind.Other,
                System.Windows.Automation.AutomationNotificationProcessing.MostRecent, text, "cursor");

        public void Announce()
        {
            var cursor = _editor.DescribeCursorAnnouncement();
            var value = _editor.DescribeCursor();
            RaisePropertyChangedEvent(System.Windows.Automation.AutomationElement.ItemStatusProperty, "", value);
            RaisePropertyChangedEvent(System.Windows.Automation.ValuePatternIdentifiers.ValueProperty, "", value);
            Speak(cursor);
        }
    }

    private void AnnounceCursor()
    {
        StructureCursorMoved();
        if (!ClientsListening) return;   // no screen reader attached: no strings built, no events raised
        if (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(this) is EditorPeer peer) peer.Announce();
    }
}

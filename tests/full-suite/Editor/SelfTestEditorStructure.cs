using System.Linq;
using System.Windows.Automation.Peers;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    private static (Views.TabEditorControl Editor, SongProject Project) StructureSong()
    {
        var project = Presets.TemplateFactory.Create("Blank");
        project.Tracks[0].Measures = Presets.TemplateFactory.Measures(6);
        var bars = project.Tracks[0].Measures;
        bars[1].Cells[0] = new TabCell { DurationDenominator = 8, Notes = { new TabNote { StringIndex = 1, Fret = 7, Techniques = { TechniqueNames.PalmMute } } } };
        bars[1].Cells[2] = new TabCell { DurationDenominator = 4, IsRest = true };
        bars[2].SectionName = "Verse";
        bars[4].TempoChange = 90;
        bars[4].TimeSigNum = 3; bars[4].TimeSigDenom = 4;
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        return (editor, project);
    }

    private static List<AutomationPeer> StructureBeats(AutomationPeer bar) => bar.GetChildren() ?? new List<AutomationPeer>();

    /// <summary>The editor peer exposes bar -> beat elements for the current system, named in words, rebuilt after an edit, and costing nothing without a client.</summary>
    private static void TestEditorStructurePeer()
    {
        Views.TabEditorControl.AutomationListenerOverride = false;
        try
        {
            // Relative to now: the counters are process-wide, and a UI Automation client on the machine can walk other windows' editor peers meanwhile.
            int structureBefore = Views.TabEditorControl.StructureBuilds, announceBefore = Views.TabEditorControl.AnnounceBuilds;
            var (editor, project) = StructureSong();
            editor.SetPosition(1, 0, 1, false);
            var peer = UIElementAutomationPeer.CreatePeerForElement(editor);
            Check("structure: building the peer and moving the cursor build no tree and no announcement without a client",
                Views.TabEditorControl.StructureBuilds == structureBefore && Views.TabEditorControl.AnnounceBuilds == announceBefore,
                $"builds={Views.TabEditorControl.StructureBuilds - structureBefore}/{Views.TabEditorControl.AnnounceBuilds - announceBefore}");
            int b0 = Views.TabEditorControl.StructureBuilds, a0 = Views.TabEditorControl.AnnounceBuilds;
            editor.SetPosition(2, 0, 1, false);
            editor.InvalidateScoreLayout();
            Check("structure: cursor moves and edits cost nothing while no automation client listens",
                Views.TabEditorControl.StructureBuilds == b0 && Views.TabEditorControl.AnnounceBuilds == a0);

            editor.SetPosition(1, 0, 1, false);
            var bars = peer!.GetChildren();
            var (first, last) = editor.Describer.SystemBars(editor.Describer.CurrentSystemIndex());
            Check("structure: the editor's children are the bars of the current system only",
                bars is { Count: > 0 } && bars.Count == last - first + 1 && bars.Count <= project.Tracks[0].Measures.Count,
                $"children={bars?.Count} system bars={first}..{last}");
            var bar2 = bars!.First(b => b.GetName().StartsWith("Bar 2"));
            Check("structure: the cursor's bar is marked and is a one-row grid", bar2.GetName().Contains("current bar") &&
                bar2.GetPattern(PatternInterface.Grid) is System.Windows.Automation.Provider.IGridProvider { RowCount: 1, ColumnCount: 2 });
            var beats = StructureBeats(bar2);
            Check("structure: a bar's children are its beats (notes and rests, not empty slots)", beats.Count == 2, $"beats={beats.Count}");
            Check("structure: a beat with a technique is named in words",
                beats[0].GetName() == "Bar 2, beat 1, eighth note, string 2 fret 7, palm mute", beats[0].GetName());
            Check("structure: a rest beat is named as a rest and the cursor beat is flagged",
                beats[1].GetName() == "Bar 2, beat 3, quarter rest" && beats[0].GetItemStatus() == "cursor" && beats[1].GetItemStatus() == "",
                beats[1].GetName());
            var builds = Views.TabEditorControl.StructureBuilds;
            peer.GetChildren();
            Check("structure: asking again does not rebuild", Views.TabEditorControl.StructureBuilds == builds);

            // Invalidation: a real edit (toggle a technique) makes the beat name change without any manual reset.
            editor.Effects.ToggleTechnique(TechniqueNames.Vibrato);
            var rebuilt = StructureBeats(peer.GetChildren().First(b => b.GetName().StartsWith("Bar 2")));
            Check("structure: the tree is rebuilt after an edit (new technique appears)",
                Views.TabEditorControl.StructureBuilds > builds && rebuilt[0].GetName().Contains("palm mute") && rebuilt[0].GetName().Contains("vibrato"),
                rebuilt.Count > 0 ? rebuilt[0].GetName() : "no beats");
            var rev = project.TimelineRevision;
            project.Tracks[0].Measures[1].Cells[0].Notes[0].Fret = 9;
            project.MarkTimelineChanged();
            editor.InvalidateScoreLayout();
            Check("structure: a song revision bump also invalidates",
                project.TimelineRevision != rev && StructureBeats(peer.GetChildren().First(b => b.GetName().StartsWith("Bar 2")))[0].GetName().Contains("fret 9"));
            editor.SetPosition(5, 0, 1, false);
            var afterMove = peer.GetChildren();
            Check("structure: after moving to another system the children follow the cursor",
                afterMove.Any(b => b.GetName().Contains("current bar")));

            // Read commands.
            editor.SetPosition(1, 0, 1, false);
            var bar = editor.Describer.Bar();
            Check("read current bar: header and every beat", bar.StartsWith("Bar 2, 4/4, tempo 120") && bar.Contains("string 2 fret 9") && bar.Contains("quarter rest"), bar);
            editor.SetPosition(3, 0, 1, false);
            var pos = editor.Describer.Position();
            Check("read position: bar, beat, section, signature, tempo and time",
                pos.Contains("bar 4 of 6") && pos.Contains("beat 1") && pos.Contains("section Verse") && pos.Contains("time signature 4/4") && pos.Contains("tempo 120") && pos.Contains("about 0:"), pos);
            Check("read commands are bindable, unique and documented",
                HotkeyCatalog.ById("Reader.ReadBar") is { DefaultGesture.Length: > 0 } && HotkeyCatalog.ById("Reader.ReadPosition") is { DefaultGesture.Length: > 0 } &&
                HotkeyCatalog.All.Count(a => a.DefaultGesture == "Ctrl+Alt+B" || a.DefaultGesture == "Ctrl+Alt+P") == 2);
        }
        finally { Views.TabEditorControl.AutomationListenerOverride = null; }

        // Announcements (a client is listening): section on entry, time signature and tempo only when they change.
        Views.TabEditorControl.AutomationListenerOverride = true;
        try
        {
            var (editor, _) = StructureSong();
            editor.SetPosition(0, 0, 1, false);
            var start = editor.Describer.CursorAnnouncement();
            editor.SetPosition(1, 0, 1, false);
            var same = editor.Describer.CursorAnnouncement();
            editor.SetPosition(2, 0, 1, false);
            var section = editor.Describer.CursorAnnouncement();
            editor.SetPosition(3, 0, 1, false);
            var stay = editor.Describer.CursorAnnouncement();
            editor.SetPosition(4, 0, 1, false);
            var changed = editor.Describer.CursorAnnouncement();
            Check("announce: nothing extra while the section, signature and tempo stay the same", !start.Contains(";") && !same.Contains(";"), start + " | " + same);
            Check("announce: entering a new section names it", section.Contains("entering section Verse"), section);
            Check("announce: staying inside the section does not repeat it", !stay.Contains("section"), stay);
            Check("announce: a changed time signature and tempo are spoken",
                changed.Contains("time signature 3/4") && changed.Contains("tempo 90"), changed);
            UIElementAutomationPeer.CreatePeerForElement(editor);   // a client attached
            int before = Views.TabEditorControl.AnnounceBuilds;
            editor.SetPosition(0, 1, 1, false);
            Check("announce: a listening client gets the announcement built", Views.TabEditorControl.AnnounceBuilds > before);
        }
        finally { Views.TabEditorControl.AutomationListenerOverride = null; }
    }
}

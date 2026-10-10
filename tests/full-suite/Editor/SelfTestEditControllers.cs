using System.Linq;
using System.Reflection;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// The global-tuning controller and the bar-command flow run against fake hosts (no window): the edits, the one undo step each,
/// the refresh requests and the status texts the window used to produce itself.
/// </summary>
public static partial class SelfTest
{
    private sealed class FakeTuningHost : IGlobalTuningHost
    {
        public FakeTuningHost(DocumentSession document) => Document = document;
        public DocumentSession Document { get; }
        public readonly List<string> Labels = new();
        public int AfterRetuneCalls;
        public string Status = "";
        public int[]? Chosen;
        public void SetTuningLabel(int shift) => Labels.Add($"shift {shift}");
        public void SetTuningLabel(string name) => Labels.Add(name);
        public void AfterRetune() => AfterRetuneCalls++;
        public void SetStatus(string text) => Status = text;
        public int[]? ChooseTuning(int[] current) => Chosen;
    }

    private static void TestGlobalTuningController()
    {
        var song = DoSong(2, 2);
        var note = new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67, SlideTargetMidi = 69 };
        song.Tracks[0].Measures[0].Cells[0].Notes.Add(note);
        var drums = new TrackController().CreateTrack(song, TrackKind.Drums);
        drums.MidiChannel = 9; drums.Measures = TemplateFactory.Measures(2);
        song.Tracks.Add(drums);
        var drumTuning = drums.StringTunings.ToList();
        var before = song.Tracks[0].StringTunings.ToList();
        var doc = DocumentSession.FromProject(song, null);
        var host = new FakeTuningHost(doc);
        var tuning = new GlobalTuningController(host);

        tuning.Retune(new int[6]);
        Check("tuning: a zero change does nothing", host.AfterRetuneCalls == 0 && host.Status == "" && doc.TuningShift.All(o => o == 0));
        tuning.RetuneAll(2);
        Check("tuning: every pitched track and its notes move together", song.Tracks[0].StringTunings.SequenceEqual(before.Select(p => p + 2)) && note.MidiValue == 69 && note.SlideTargetMidi == 71 && note.Fret == 3);
        Check("tuning: the drum track stays as it is", drums.StringTunings.SequenceEqual(drumTuning));
        Check("tuning: the document's shift readout, label, refresh and status follow", doc.TuningShift.All(o => o == 2) && host.Labels.Last() == "shift 2" && host.AfterRetuneCalls == 1
            && host.Status == "All tracks retuned +2 semitones", host.Status);
        Check("tuning: the edit is one undo step and dirties the song", song.IsDirty && doc.Undo.CanUndo);

        tuning.SetUniformShift(-1);
        Check("tuning: a uniform shift is relative to the original tuning", doc.TuningShift.All(o => o == -1) && host.Status == "All tracks retuned -1 semitones" && tuning.IsUniformNow, host.Status);
        host.Chosen = GlobalTuningWindowStandardPlus(-1, highE: 1);
        tuning.ChooseAndRetune();
        Check("tuning: the chosen per-string tuning is applied as a difference", doc.TuningShift[0] == 0 && doc.TuningShift[1] == -1 && !tuning.IsUniformNow
            && host.Status == "All tracks retuned (custom per-string tuning)", host.Status);
        host.Chosen = null;
        var calls = host.AfterRetuneCalls;
        tuning.ChooseAndRetune();
        Check("tuning: cancelling the tuning window changes nothing", host.AfterRetuneCalls == calls);
        tuning.ResetToOriginal();
        Check("tuning: back to original tuning", doc.TuningShift.All(o => o == 0) && host.Status == "Original tuning" && song.Tracks[0].StringTunings.SequenceEqual(before), host.Status);
    }

    private static int[] GlobalTuningWindowStandardPlus(int all, int highE) =>
        TabForge.Views.GlobalTuningWindow.StandardE.Select((p, i) => p + all + (i == 0 ? highE : 0)).ToArray();

    private sealed class FakeBarHost : IBarCommandHost
    {
        public FakeBarHost(DocumentSession document) => Document = document;
        public DocumentSession Document { get; }
        public EditingSettings Editing { get; } = new();
        public MeasureModel? CurrentBar => SelectedTrackIndex < Document.Project.Tracks.Count && SelectedBar < Document.Project.Tracks[SelectedTrackIndex].Measures.Count ? Document.Project.Tracks[SelectedTrackIndex].Measures[SelectedBar] : null;
        public int SelectedBar { get; set; }
        public int SelectedString => 0;
        public int SelectedTrackIndex { get; set; }
        public bool IsSelecting { get; set; }
        public (int Start, int End)? SelectedBars { get; set; }
        public readonly List<string> Calls = new();
        public string Status = "";
        public bool DeleteAnswer = true;
        public (int num, int denom, bool onlyThisBar)? TimeAnswer;
        public string? TextAnswer;
        public void SetPosition(int bar, int cell, int stringIndex) { SelectedBar = bar; Calls.Add($"position {bar}"); }
        public void MoveToBarStart(int bar) => Calls.Add($"start {bar}");
        public void WriteLikeBeatBefore(int bar) => Calls.Add($"like {bar}");
        public void Refresh(EditViews views) => Calls.Add($"refresh {views}");
        public void RefreshAfterBarDelete() => Calls.Add("afterDelete");
        public void SetStatus(string text) => Status = text;
        public bool ConfirmDeleteBar(int barNumber) { Calls.Add($"confirm {barNumber}"); return DeleteAnswer; }
        public (int num, int denom, bool onlyThisBar)? AskTimeSignature(int num, int denom, string? selectedBars) { Calls.Add($"askTime {num}/{denom} {selectedBars}"); return TimeAnswer; }
        public (int signature, bool minor, bool onlyThisBar)? AskKeySignature(int signature, bool minor, string? selectedBars) => null;
        public string? AskDirections(string current, int ending, out int selectedEnding) { selectedEnding = 0; return null; }
        public string? AskText(string title, string label, string initial) => TextAnswer;
    }

    private static void TestBarCommandFlow()
    {
        var editRefresh = typeof(MainWindow).GetNestedType("EditRefresh", BindingFlags.NonPublic)!;
        Check("bar flow: EditViews carries the window's refresh flags unchanged", Enum.GetNames<EditViews>().All(n => Enum.Parse(editRefresh, n) is { } v && (int)v == (int)Enum.Parse<EditViews>(n)));

        var song = DoSong(2, 4);
        var doc = DocumentSession.FromProject(song, null);
        var host = new FakeBarHost(doc);
        var flow = new BarCommandFlow(host, new ArrangementController());
        int Bars() => BarRangeEditor.MaxMeasures(song);

        flow.AppendBar();
        Check("bar flow: Add bar appends one bar to every track and redraws score and timeline", Bars() == 5 && song.Tracks.All(t => t.Measures.Count == 5) && host.Status == "Added bar 5 at the end"
            && host.Calls.Contains("refresh Score, Arrangement"), host.Status + " " + string.Join(",", host.Calls));
        host.Calls.Clear();
        host.SelectedBar = 1; host.IsSelecting = true;
        flow.InsertBar();
        Check("bar flow: Insert bar with a selection asks to clear it first", Bars() == 5 && host.Status.StartsWith("Insert bar works at the cursor") && host.Calls.Count == 0);
        host.IsSelecting = false;
        flow.InsertBar();
        Check("bar flow: Insert bar adds a bar at the cursor and writes like the beat before", Bars() == 6 && host.Status == "Inserted bar 2" && host.Calls.SequenceEqual(new[] { "refresh Score, Arrangement", "start 1", "like 1" }), string.Join(",", host.Calls));

        host.Calls.Clear(); host.Editing.ConfirmDeleteBar = true; host.DeleteAnswer = false;
        flow.DeleteBar();
        Check("bar flow: a declined delete question keeps the bar", Bars() == 6 && host.Calls.SequenceEqual(new[] { "confirm 2" }));
        host.DeleteAnswer = true;
        flow.DeleteBar();
        Check("bar flow: Delete bar removes it from every track, moves the cursor back and redraws", Bars() == 5 && host.SelectedBar == 0 && host.Calls.Contains("afterDelete") && host.Status == "Deleted bar", string.Join(",", host.Calls));
        var single = DocumentSession.FromProject(DoSong(1, 1), null);
        var singleHost = new FakeBarHost(single);
        new BarCommandFlow(singleHost, new ArrangementController()).DeleteBar();
        Check("bar flow: the last bar cannot be deleted", BarRangeEditor.MaxMeasures(single.Project) == 1 && singleHost.Status == "Cannot delete the last bar" && singleHost.Calls.Count == 0);

        host.Calls.Clear(); host.SelectedBar = 2;
        host.TimeAnswer = (3, 4, false);
        flow.SetTimeSignature();
        Check("bar flow: a time signature applies from the cursor bar to the end", song.Tracks[0].Measures[2].TimeSigNum == 3 && song.Tracks[0].Measures[4].TimeSigNum == 3
            && host.Status == "Time signature 3/4 from bar 3 to the end" && host.Calls.Contains("refresh Score, TimelineGeometry, Palette, Status"), host.Status);
        host.TimeAnswer = null;
        host.Calls.Clear();
        flow.SetTimeSignature();
        Check("bar flow: cancelling the dialog changes nothing", host.Calls.Count == 1 && host.Calls[0].StartsWith("askTime 3/4"));

        host.SelectedBars = (1, 3);
        Check("bar flow: a selection of several bars is the signature range", flow.SelectedBarRange() == (1, 3));
        host.SelectedBars = (2, 2);
        Check("bar flow: a one-bar selection is no range", flow.SelectedBarRange() is null);
        host.SelectedBars = null;
        Check("bar flow: the span text for one bar, a stretch and the end", flow.SignatureSpan(4, 4) == "for bar 5" && flow.SignatureSpan(1, 2) == "from bar 2 to bar 3" && flow.SignatureSpan(1, 4) == "from bar 2 to the end");

        host.Calls.Clear();
        flow.CycleClef();
        Check("bar flow: the clef cycles on the selected track and bar", host.Status.StartsWith("Clef ") && host.Calls.SequenceEqual(new[] { "refresh Score" }), host.Status);
        host.Calls.Clear();
        flow.ToggleSimile(1);
        Check("bar flow: the repeat-bar mark toggles", song.Tracks[0].Measures[2].SimileOneBar && host.Calls.SequenceEqual(new[] { "refresh Score" }));
        flow.ToggleSimile(1);
        Check("bar flow: the repeat-bar mark toggles off again", !song.Tracks[0].Measures[2].SimileOneBar);
        host.TextAnswer = "Verse";
        flow.RenameSection();
        Check("bar flow: the section name is set on the bar", song.Tracks[0].Measures[2].SectionName == "Verse");
        host.SelectedTrackIndex = 9;
        host.Calls.Clear();
        flow.CycleClef(); flow.ToggleDoubleBar(); flow.ToggleTripletFeel(); flow.EditDirections(); flow.RenameSection();
        Check("bar flow: with no current bar the bar commands do nothing", host.Calls.Count == 0);
    }

    /// <summary>A timeline window that answers as both the timeline menu host and the selection loop host, with the song on show set by the test.</summary>
    private sealed class FakeTimelineMenuHost : ITimelineMenuHost, ISelectionLoopHost
    {
        public FakeTimelineMenuHost(DocumentSession document) => Document = document;
        public DocumentSession Document { get; set; }
        public DocumentSession ActiveDocument => Document;
        public TabForge.Services.AppSettings Settings { get; } = new();
        public bool LoopOn { get; private set; }
        public int SelectedTrackRow => 0;
        public TrackModel? SelectedTrack => null;
        public int KeyboardMenuBar => 0;
        /// <summary>The command handler of the last menu built.</summary>
        public Action<TabForge.Views.TimelineCommand>? Run { get; private set; }
        public string MenuKey(string id) => "";
        public System.Windows.Controls.ContextMenu NewMenu(string name, IEnumerable<TabForge.Views.MenuSpec> specs, Action<TabForge.Views.TimelineCommand> run)
        {
            Run = run;
            return new System.Windows.Controls.ContextMenu();
        }
        public void OpenMenu(System.Windows.Controls.ContextMenu menu, System.Windows.Point? anchor, bool fromKeyboard) { }
        public System.Windows.Point? BarAnchor(int bar) => null;
        public void OpenSettings(string category, string row) { }
        public void SetLoopActive(bool loop) => LoopOn = loop;
        public void ApplyLoopRange(int start, int end, SelectionScope scope) { }
        public void SyncAreaVisuals() { }
        public void SetStatus(string text) { }
        public void BeginAreaMove(int start, int end) { }
        public void ToggleTrackLines() { }
        public void ResetTrackListHeight() { }
        public void PlaceCaretAfterOpen(int trackIndex, int bar) { }
        public void Refresh(EditViews views) { }
        public void AddSectionAt(int bar) { }
        public void RenameSection(MarkerModel marker) { }
        public void GoToSection(MarkerModel marker) { }
        public string[]? PickAudioFiles() => null;
        public void ShowArea(int start, int end, int startCell, int endCell) { }
        public void ShowScoreSelection(SelectionModel selection) { }
    }

    private static void TestTimelineMenuSkipRanges()
    {
        // The skip commands act on the song the menu was built for, not on whichever song is on show when the command runs.
        var built = DocumentSession.FromProject(DoSong(2, 6), null);
        var other = DocumentSession.FromProject(DoSong(2, 6), null);
        other.SkipRanges.Add((3, 4));
        var host = new FakeTimelineMenuHost(built);
        var menus = new TimelineMenuController(host, new SelectionModel(), new SelectionLoopController(host), null!, null!, null!);
        built.LoopStartBar = 1;
        built.LoopEndBar = 2;

        menus.BuildSelectionMenu();
        host.Document = other;
        host.Run!(TabForge.Views.TimelineCommand.SkipSelection);
        Check("timeline menu: Skip selection marks the range on the song the menu was built for, not the one on show",
            built.SkipRanges.SequenceEqual(new[] { (1, 2) }) && other.SkipRanges.SequenceEqual(new[] { (3, 4) }));

        host.Document = built;
        menus.BuildSelectionMenu();
        host.Document = other;
        host.Run!(TabForge.Views.TimelineCommand.PlaySkippedAgain);
        Check("timeline menu: Play skipped again clears the built-for song's ranges and leaves the other song's",
            built.SkipRanges.Count == 0 && other.SkipRanges.SequenceEqual(new[] { (3, 4) }));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: "Stretch a section" (dragging a section's right edge past the next section): the edge holds at the next section's start (the bump), the live preview (bars asked for, no model edit), the prompt, each option's result
/// on every track (add empty bars, repeat the last bars, add a repeat sign, stop), sections / tempo / clips moving later, Remember skipping the prompt, Escape and Cancel stretching nothing, a drag in free space staying as it
/// was, one undo step, the prompt window and the setting.
/// Tests: SectionStretch, SectionStretchPromptPresenter, SectionStretchPromptWindow, TimelineInputController (section stretch), TimelineView (stretch preview), SettingsCatalog (editing.sectionstretch).
/// </summary>
public class SectionStretchTests
{
    // Verse 1-8, Chorus 9-12, Break 13-16, Verse 2 17-20; Lead and Bass play something in every bar, Lead changes tempo at Verse 2; an audio track holds a clip in bar 3 and one in bar 19.
    private const int Bars = 20;

    private static void Note(TrackModel track, int bar, int fret)
    {
        var cell = track.Measures[bar].Cells[0];
        cell.DurationDenominator = 1;
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = fret });
    }

    private static double Start(SongProject song, int bar) => BarRangeGaps.Span(song, bar, bar)!.Value.From;

    private static SongDocument Make(Action<SongProject>? prepare = null)
    {
        var song = new SongProject();
        TrackModel Track(string name, TrackKind kind) { var t = new TrackModel { Name = name, Kind = kind, Measures = TemplateFactory.Measures(Bars) }; song.Tracks.Add(t); return t; }
        var lead = Track("Lead", TrackKind.Guitar);
        var bass = Track("Bass", TrackKind.Bass);
        var audio = Track("Audio", TrackKind.Audio);
        for (var b = 0; b < Bars; b++) { Note(lead, b, b % 12 + 1); Note(bass, b, b % 5 + 1); }
        lead.Measures[16].TempoChange = 140;
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Verse" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "Chorus" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 12, Title = "Break" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 16, Title = "Verse 2" });
        audio.AudioClips.Add(new AudioClip { Name = "late", StartSec = Start(song, 18), SourceLengthSec = 2, Notes = new List<ClipNote>() });
        audio.AudioClips.Add(new AudioClip { Name = "early", StartSec = Start(song, 2), SourceLengthSec = 2, Notes = new List<ClipNote>() });
        prepare?.Invoke(song);
        return new SongDocument(() => null) { Project = song };
    }

    private static string Notes(TrackModel track, int bar) => bar >= track.Measures.Count ? "?"
        : string.Join(",", track.Measures[bar].Cells.Concat(track.Measures[bar].Voice2Cells).SelectMany(c => c.Notes).Select(n => $"{n.StringIndex}:{n.Fret}"));

    private static MarkerModel Section(SongProject song, string title) => song.Markers.Single(m => m.Title == title);

    private static (int Start, int End) Bounds(SongProject song, string title)
    {
        Assert.True(SectionLayout.TryGetBounds(song, Section(song, title), out var start, out var end));
        return (start, end);
    }

    private sealed class Rig
    {
        public readonly SongDocument Doc;
        public readonly TimelineView View;
        public readonly TimelineInputController Input;
        public readonly Window Win;
        public readonly List<SectionStretchQuestion> Asked = new();
        public SectionStretchAnswer? Answer;
        public SectionStretchBackAnswer? BackAnswer;
        public readonly List<SectionStretchBackQuestion> AskedBack = new();
        public string Setting = SectionStretchChoice.Ask;

        public Rig(SectionStretchAnswer? answer = null, Action<SongProject>? prepare = null)
        {
            Answer = answer;
            Doc = Make(prepare);
            View = new TimelineView(Doc.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Input = new TimelineInputController(View, Doc, sectionTimer: new AvaloniaPlaybackSessionTests.FakeTimer())
            {
                SectionStretchSetting = () => Setting,
                SectionStretchRemember = value => Setting = value,
                SectionStretchPrompt = question => { Asked.Add(question); return Task.FromResult(Answer); },
                SectionStretchBackPrompt = question => { AskedBack.Add(question); return Task.FromResult(BackAnswer); },
            };
            Win = new Window { Width = 2600, Height = 500, Content = View };
            Win.Show();
        }

        public double Edge(int end = 12) => View.BarX(end);
        public double Unit(int end = 12) => View.BarX(end) - View.BarX(end - 1);
        public Point Knob(int end = 12) => new(Edge(end) - 3, TimelineView.RulerHeight + 10);
        public Point Past(int bars, int end = 12) => new(Edge(end) + SectionStretch.BumpPx + bars * Unit(end) + 1, TimelineView.RulerHeight + 10);

        public void Grab(int end = 12)
        {
            Win.MouseMove(Knob(end));
            Win.MouseDown(Knob(end), MouseButton.Left);
        }

        public void Release(Point at)
        {
            Win.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>Drags the right edge of the section that ends at bar <paramref name="end"/> (exclusive) <paramref name="bars"/> bars past the next section's start and releases.</summary>
        public void Stretch(int bars, int end = 12)
        {
            Grab(end);
            Win.MouseMove(Past(bars, end));
            Release(Past(bars, end));
        }

        /// <summary>Drags the left edge of the section that starts at bar <paramref name="start"/> <paramref name="bars"/> bars before the previous section's end and releases.</summary>
        public Point StretchBack(int bars, int start = 12, bool release = true)
        {
            var knob = new Point(Edge(start) + 3, TimelineView.RulerHeight + 10);
            var at = new Point(Edge(start) - SectionStretch.BumpPx - bars * (View.BarX(start + 1) - View.BarX(start)) - 1, knob.Y);
            Win.MouseMove(knob);
            Win.MouseDown(knob, MouseButton.Left);
            Win.MouseMove(at);
            if (release) Release(at);
            return at;
        }

        public SongProject Song => Doc.Project;
    }

    private static void AssertBarCount(SongProject song, int count) => Assert.All(song.Tracks, t => Assert.Equal(count, t.Measures.Count));

    /// <summary>One undo step that puts the song back exactly.</summary>
    private static void AssertOneUndoStep(Rig rig, string before)
    {
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        DocumentEdits.Undo(rig.Doc);
        Assert.Equal(before, ProjectService.Snapshot(rig.Doc.Project));
    }

    [AvaloniaFact]
    public void TheEdgeHoldsAtTheNextSectionThenPreviewsTheBarsWithoutTouchingTheSongAndPromptsOnRelease()
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.Stop, false));
        rig.Grab();
        rig.Win.MouseMove(rig.Past(0));   // 12 px past the next section's start: still held
        Assert.Equal(0, rig.View.SectionStretchBars);
        Assert.Equal((1, 8, 12), rig.View.SectionResizePreview);
        rig.Win.MouseMove(new Point(rig.Edge() + 6, rig.Past(0).Y));
        Assert.Equal(0, rig.View.SectionStretchBars);
        rig.Win.MouseMove(rig.Past(4));
        Assert.Equal(4, rig.View.SectionStretchBars);
        Assert.Equal((1, 8, 12), rig.View.SectionResizePreview);   // the chip stretches by the bars, the model's bounds stay
        Assert.Equal(4 * rig.Unit(), rig.View.StretchShiftPx, 3);
        Assert.Equal(Bars, BarRangeEditor.MaxMeasures(rig.Song));   // a preview only
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        rig.Win.MouseMove(rig.Past(2));
        Assert.Equal(2, rig.View.SectionStretchBars);
        rig.Win.MouseMove(rig.Past(4));
        rig.Release(rig.Past(4));
        var asked = Assert.Single(rig.Asked);
        Assert.Equal(new SectionStretchQuestion("Chorus", 4, 4, "Break", null, 4), asked);
        Assert.Null(rig.View.SectionResizePreview);   // answered: the preview goes
        Assert.Equal(0, rig.View.SectionStretchBars);
        Assert.Equal(0, rig.Doc.Undo.UndoCount);   // Stop: nothing changes
        AssertBarCount(rig.Song, Bars);
        rig.Win.Close();
    }

    [AvaloniaFact]
    public void AddEmptyBarsPutsFourEmptyBarsAtTheEndOfTheSectionOnEveryTrackAndMovesWhatFollows()
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.AddEmpty, false));
        var before = ProjectService.Snapshot(rig.Song);
        var old = Make().Project;
        rig.Stretch(4);
        var song = rig.Song;
        AssertBarCount(song, Bars + 4);
        Assert.Equal((8, 16), Bounds(song, "Chorus"));
        Assert.Equal(16, Section(song, "Break").MeasureIndex);
        Assert.Equal(20, Section(song, "Verse 2").MeasureIndex);
        Assert.Equal(0, Section(song, "Verse").MeasureIndex);
        for (var t = 0; t < 2; t++)
            for (var bar = 0; bar < Bars + 4; bar++)
                Assert.Equal(bar < 12 ? Notes(old.Tracks[t], bar) : bar < 16 ? "" : Notes(old.Tracks[t], bar - 4), Notes(song.Tracks[t], bar));
        Assert.Equal(140, song.Tracks[0].Measures[20].TempoChange);
        Assert.Equal(Start(song, 22), song.Tracks[2].AudioClips.Single(c => c.Name == "late").StartSec, 6);
        Assert.Equal(Start(song, 2), song.Tracks[2].AudioClips.Single(c => c.Name == "early").StartSec, 6);
        AssertOneUndoStep(rig, before);
        rig.Win.Close();
    }

    [AvaloniaTheory]
    [InlineData(4, new[] { 8, 9, 10, 11 })]
    [InlineData(2, new[] { 10, 11 })]
    [InlineData(6, new[] { 8, 9, 10, 11, 8, 9 })]   // the section has fewer bars than asked: it repeats round
    public void RepeatLastBarsCopiesTheSectionsTailIntoTheNewSpaceOnEveryTrack(int bars, int[] sources)
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.RepeatLast, false));
        var before = ProjectService.Snapshot(rig.Song);
        var old = Make().Project;
        rig.Stretch(bars);
        var song = rig.Song;
        AssertBarCount(song, Bars + bars);
        Assert.Equal((8, 12 + bars), Bounds(song, "Chorus"));
        Assert.Equal(12 + bars, Section(song, "Break").MeasureIndex);
        Assert.Equal(16 + bars, Section(song, "Verse 2").MeasureIndex);
        for (var t = 0; t < 2; t++)
        {
            for (var k = 0; k < bars; k++) Assert.Equal(Notes(old.Tracks[t], sources[k]), Notes(song.Tracks[t], 12 + k));
            for (var bar = 12; bar < Bars; bar++) Assert.Equal(Notes(old.Tracks[t], bar), Notes(song.Tracks[t], bar + bars));
        }
        Assert.Equal(Start(song, 18 + bars), song.Tracks[2].AudioClips.Single(c => c.Name == "late").StartSec, 6);
        AssertOneUndoStep(rig, before);
        rig.Win.Close();
    }

    [AvaloniaFact]
    public void AddRepeatSignAddsNoBarsAndMarksTheSectionsLastBarsOnEveryTrackWithTwoPlays()
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.AddRepeatSign, false));
        var before = ProjectService.Snapshot(rig.Song);
        rig.Stretch(2);
        var song = rig.Song;
        AssertBarCount(song, Bars);
        Assert.Equal(12, Section(song, "Break").MeasureIndex);   // nothing moved
        Assert.All(song.Tracks, t =>
        {
            Assert.True(t.Measures[10].RepeatStart);
            Assert.True(t.Measures[11].RepeatEnd);
            Assert.Equal(2, t.Measures[11].RepeatCount);
            Assert.False(t.Measures[9].RepeatStart || t.Measures[9].RepeatEnd || t.Measures[10].RepeatEnd || t.Measures[11].RepeatStart);
        });
        AssertOneUndoStep(rig, before);

        // More bars than the section has: the sign goes round the whole section.
        var whole = new Rig(new SectionStretchAnswer(SectionStretchMode.AddRepeatSign, false));
        whole.Stretch(7);
        Assert.All(whole.Song.Tracks, t => { Assert.True(t.Measures[8].RepeatStart); Assert.True(t.Measures[11].RepeatEnd); });
        whole.Win.Close();
        rig.Win.Close();
    }

    [AvaloniaFact]
    public void ARepeatInTheWayDisablesTheSignAndTheEditRefusesIt()
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.AddRepeatSign, false), song => song.Tracks[0].Measures[9].RepeatEnd = true);
        var before = ProjectService.Snapshot(rig.Song);
        rig.Stretch(4);
        var asked = Assert.Single(rig.Asked);
        Assert.Equal("Bars 9–12 already have a repeat sign.", asked.SignConflict);
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        Assert.Equal(before, ProjectService.Snapshot(rig.Song));
        rig.Win.Close();

        var inside = Make(song => { song.Tracks[0].Measures[3].RepeatStart = true; song.Tracks[0].Measures[14].RepeatEnd = true; }).Project;
        var plan = SectionStretch.PlanFor(inside, Section(inside, "Chorus"))!;
        Assert.Equal("Bars 9–12 are inside an existing repeat.", SectionStretch.RepeatSignConflict(inside, plan, 4));
        var free = Make().Project;
        Assert.Null(SectionStretch.RepeatSignConflict(free, SectionStretch.PlanFor(free, Section(free, "Chorus"))!, 4));
    }

    [AvaloniaFact]
    public void StopLeavesTheSongAsItWasAndCancelAndEscapeStretchNothing()
    {
        var stop = new Rig(new SectionStretchAnswer(SectionStretchMode.Stop, true));
        stop.Stretch(3);
        Assert.Equal(SectionStretchChoice.Stop, stop.Setting);   // Remember with Stop is a choice too
        Assert.Equal(0, stop.Doc.Undo.UndoCount);
        AssertBarCount(stop.Song, Bars);
        stop.Win.Close();

        var cancel = new Rig(null);
        cancel.Stretch(3);
        Assert.Single(cancel.Asked);
        Assert.Equal(0, cancel.Doc.Undo.UndoCount);
        AssertBarCount(cancel.Song, Bars);
        Assert.Equal(SectionStretchChoice.Ask, cancel.Setting);
        Assert.Null(cancel.View.SectionResizePreview);
        cancel.Win.Close();

        var esc = new Rig(new SectionStretchAnswer(SectionStretchMode.AddEmpty, false));
        esc.Grab();
        esc.Win.MouseMove(esc.Past(4));
        Assert.Equal(4, esc.View.SectionStretchBars);
        esc.Win.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(0, esc.View.SectionStretchBars);
        Assert.Null(esc.View.SectionResizePreview);
        esc.Release(esc.Past(4));
        Assert.Empty(esc.Asked);
        Assert.Equal(0, esc.Doc.Undo.UndoCount);
        AssertBarCount(esc.Song, Bars);
        esc.Win.Close();
    }

    [AvaloniaFact]
    public void RememberStoresTheChoiceAndTheNextStretchSkipsThePrompt()
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.RepeatLast, true));
        rig.Stretch(4);
        Assert.Equal(SectionStretchChoice.RepeatLast, rig.Setting);
        Assert.Single(rig.Asked);
        rig.Stretch(2, end: 16);   // Chorus now ends at bar 16, the Break starts there
        Assert.Single(rig.Asked);   // no second question
        AssertBarCount(rig.Song, Bars + 6);
        Assert.Equal(18, Bounds(rig.Song, "Chorus").End);
        Assert.Equal(2, rig.Doc.Undo.UndoCount);
        rig.Win.Close();

        var preset = new Rig(null) { Setting = SectionStretchChoice.AddEmpty };
        preset.Stretch(3);
        Assert.Empty(preset.Asked);
        AssertBarCount(preset.Song, Bars + 3);
        preset.Win.Close();
    }

    [AvaloniaFact]
    public void DraggingWithinFreeSpaceOrTheBumpIsTheOldResizeAndAsksNothing()
    {
        var gap = new Rig(new SectionStretchAnswer(SectionStretchMode.AddEmpty, false), song => Section(song, "Chorus").LengthBars = 2);   // Chorus 9-10, then two free bars
        Assert.Equal((8, 10), Bounds(gap.Song, "Chorus"));
        gap.Grab(end: 10);
        gap.Win.MouseMove(new Point(gap.Edge(12) - 2, gap.Knob().Y));
        Assert.Equal(0, gap.View.SectionStretchBars);
        gap.Release(new Point(gap.Edge(12) - 2, gap.Knob().Y));
        Assert.Empty(gap.Asked);
        Assert.Equal((8, 12), Bounds(gap.Song, "Chorus"));
        AssertBarCount(gap.Song, Bars);
        Assert.Equal(1, gap.Doc.Undo.UndoCount);
        gap.Win.Close();

        var bump = new Rig(new SectionStretchAnswer(SectionStretchMode.AddEmpty, false));
        bump.Grab();
        bump.Win.MouseMove(bump.Past(0));
        bump.Release(bump.Past(0));
        Assert.Empty(bump.Asked);
        Assert.Equal(0, bump.Doc.Undo.UndoCount);
        AssertBarCount(bump.Song, Bars);
        bump.Win.Close();
    }

    [AvaloniaFact]
    public void TheLastSectionStretchesAtTheEndOfTheSong()
    {
        var rig = new Rig(new SectionStretchAnswer(SectionStretchMode.AddEmpty, false));
        rig.Stretch(3, end: Bars);
        var asked = Assert.Single(rig.Asked);
        Assert.Null(asked.Next);
        AssertBarCount(rig.Song, Bars + 3);
        Assert.Equal((16, Bars + 3), Bounds(rig.Song, "Verse 2"));
        rig.Win.Close();
    }

    [Fact]
    public void ThePromptWordsNameTheSectionTheBarsAndTheNextSection()
    {
        var song = Make().Project;
        var plan = SectionStretch.PlanFor(song, Section(song, "Chorus"))!;
        var q = SectionStretchPromptPresenter.Question(song, plan, 4);
        Assert.Equal("Stretch Chorus by 4 bars?", SectionStretchPromptPresenter.Title(q));
        Assert.Equal("Chorus goes from 4 to 8 bars; Break and everything after moves 4 bars later.", SectionStretchPromptPresenter.Body(q));
        Assert.Equal("Add 4 empty bars", SectionStretchPromptPresenter.AddTitle(q));
        Assert.Equal("Repeat Chorus's last bars to fill", SectionStretchPromptPresenter.RepeatTitle(q));
        Assert.Equal("Stop at Break", SectionStretchPromptPresenter.StopTitle(q));
        Assert.Equal("(Settings › Editing › Sections)", SectionStretchPromptPresenter.RememberHint);
        var one = SectionStretchPromptPresenter.Question(song, plan, 1);
        Assert.Equal("Stretch Chorus by 1 bar?", SectionStretchPromptPresenter.Title(one));
        Assert.Equal("Chorus goes from 4 to 5 bars; Break and everything after moves 1 bar later.", SectionStretchPromptPresenter.Body(one));
        var six = SectionStretchPromptPresenter.Question(song, plan, 6);
        Assert.Equal(4, six.TailBars);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void TheWindowDefaultsToEmptyBarsDisablesTheSignWithAReasonAndEnterOrEscAnswers()
    {
        var question = new SectionStretchQuestion("Chorus", 4, 4, "Break", "Bars 9–12 already have a repeat sign.", 4);
        var ok = new SectionStretchPromptWindow(question);
        ok.Show();
        Assert.Equal("Stretch Chorus by 4 bars?", ok.Title);
        Assert.True(Named<RadioButton>(ok, "AddRadio").IsChecked);
        Assert.False(Named<RadioCard>(ok, "SignCard").IsEnabled);
        Assert.Equal(question.SignConflict, Named<TextBlock>(ok, "SignReason").Text);
        Assert.Equal(SectionStretchPromptPresenter.RememberHint, Named<TextBlock>(ok, "RememberHint").Text);
        Assert.Equal("Stretch", (string?)Named<Button>(ok, "OkButton").Content);
        ok.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(new SectionStretchAnswer(SectionStretchMode.AddEmpty, false), ok.Answer);

        var sign = new SectionStretchPromptWindow(question with { SignConflict = null });
        sign.Show();
        Assert.DoesNotContain(sign.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SignReason");
        Named<RadioButton>(sign, "SignRadio").IsChecked = true;
        Named<CheckBox>(sign, "RememberBox").IsChecked = true;
        sign.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(new SectionStretchAnswer(SectionStretchMode.AddRepeatSign, true), sign.Answer);

        var repeat = new SectionStretchPromptWindow(question);
        repeat.Show();
        Named<RadioButton>(repeat, "RepeatRadio").IsChecked = true;
        repeat.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(SectionStretchMode.RepeatLast, repeat.Answer!.Value.Mode);

        var stop = new SectionStretchPromptWindow(question);
        stop.Show();
        Named<RadioButton>(stop, "StopRadio").IsChecked = true;
        stop.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(SectionStretchMode.Stop, stop.Answer!.Value.Mode);

        var cancelled = new SectionStretchPromptWindow(question);
        cancelled.Show();
        cancelled.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(cancelled.Answer);
    }

    [Fact]
    public void PreferencesRowIsSearchableAndWritesTheStoredValue()
    {
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "editing.sectionstretch");
        Assert.Equal(SettingsCatalog.Editing, row.Category);
        Assert.Equal("Sections", row.Group);
        Assert.Equal("When stretching a section past the next one", row.Title);
        Assert.Equal(new[] { "Ask each time", "Add empty bars", "Repeat last bars", "Add repeat sign", "Stop at the next section" }, row.Choices);
        Assert.Equal(row.Choices[0], row.Get());
        Assert.Contains("stretch", row.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section", row.SearchText, StringComparison.OrdinalIgnoreCase);
        row.Set(row.Choices[3]);
        Assert.Equal(SectionStretchChoice.RepeatSign, settings.Editing.SectionStretch);
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(SectionStretchChoice.RepeatSign, back.Editing.SectionStretch);
        Assert.Equal(SectionStretchChoice.Ask, SectionStretchChoice.Normalize("nonsense"));
        Assert.Equal(SectionStretchChoice.Ask, new AppSettings().Editing.SectionStretch);
    }

    [Fact]
    public void TheBarsRuleHoldsTheBumpThenSnapsToWholeBars()
    {
        Assert.Equal(0, SectionStretch.BarsFor(100 + SectionStretch.BumpPx, 100, 50, 99));
        Assert.Equal(0, SectionStretch.BarsFor(100 + SectionStretch.BumpPx + 24, 100, 50, 99));
        Assert.Equal(1, SectionStretch.BarsFor(100 + SectionStretch.BumpPx + 26, 100, 50, 99));
        Assert.Equal(4, SectionStretch.BarsFor(100 + SectionStretch.BumpPx + 200, 100, 50, 99));
        Assert.Equal(3, SectionStretch.BarsFor(100 + SectionStretch.BumpPx + 200, 100, 50, 3));
        Assert.Equal(0, SectionStretch.BarsFor(50, 100, 50, 99));
    }

    private static Rig Back(SectionStretchBackMode mode) => new() { BackAnswer = new SectionStretchBackAnswer(mode, false) };

    [AvaloniaFact]
    public void BackAddEmptyPutsEmptyBarsAtTheSectionStartAndMovesItAndWhatFollows()
    {
        var rig = Back(SectionStretchBackMode.AddEmpty);
        var before = ProjectService.Snapshot(rig.Song);
        var old = Make().Project;
        var at = rig.StretchBack(3, release: false);
        Assert.Equal(3, rig.View.SectionStretchBars);
        Assert.True(rig.View.SectionStretchBack);
        rig.Release(at);
        Assert.Equal(new SectionStretchBackQuestion("Break", 4, 3, "Chorus", 3), Assert.Single(rig.AskedBack));
        var song = rig.Song;
        AssertBarCount(song, Bars + 3);
        Assert.Equal((8, 12), Bounds(song, "Chorus"));
        Assert.Equal((12, 19), Bounds(song, "Break"));
        Assert.Equal(19, Section(song, "Verse 2").MeasureIndex);
        for (var bar = 0; bar < Bars + 3; bar++)
            Assert.Equal(bar < 12 ? Notes(old.Tracks[0], bar) : bar < 15 ? "" : Notes(old.Tracks[0], bar - 3), Notes(song.Tracks[0], bar));
        AssertOneUndoStep(rig, before);
        rig.Win.Close();
    }

    [AvaloniaFact]
    public void BackRepeatFirstCopiesTheOpeningBarsInFront()
    {
        var rig = Back(SectionStretchBackMode.RepeatFirst);
        var old = Make().Project;
        rig.StretchBack(2);
        AssertBarCount(rig.Song, Bars + 2);
        Assert.Equal((12, 18), Bounds(rig.Song, "Break"));
        Assert.Equal(new[] { Notes(old.Tracks[0], 12), Notes(old.Tracks[0], 13), Notes(old.Tracks[0], 12) }, new[] { 12, 13, 14 }.Select(b => Notes(rig.Song.Tracks[0], b)));
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        rig.Win.Close();
    }

    [AvaloniaFact]
    public void BackTakeFromPreviousMovesTheBoundaryWithoutAddingBarsAndKeepsOneBar()
    {
        var rig = Back(SectionStretchBackMode.TakeFromPrevious);
        rig.StretchBack(6);   // Chorus has 4 bars: it gives 3
        AssertBarCount(rig.Song, Bars);
        Assert.Equal((8, 9), Bounds(rig.Song, "Chorus"));
        Assert.Equal((9, 16), Bounds(rig.Song, "Break"));
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        rig.Win.Close();
    }

    [AvaloniaFact]
    public void BackStopAndEscapeChangeNothing()
    {
        var stop = Back(SectionStretchBackMode.Stop);
        stop.StretchBack(2);
        Assert.Equal(0, stop.Doc.Undo.UndoCount);
        stop.Win.Close();
        var esc = Back(SectionStretchBackMode.AddEmpty);
        var escAt = esc.StretchBack(2, release: false);
        esc.Win.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(0, esc.View.SectionStretchBars);
        esc.Release(escAt);
        Assert.Empty(esc.AskedBack);
        Assert.Equal(0, esc.Doc.Undo.UndoCount);
        AssertBarCount(esc.Song, Bars);
        esc.Win.Close();
    }
}

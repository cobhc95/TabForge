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
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: moving a section that audio or MIDI clips cross: the crossing clips are found (clips fully inside are not), Split cuts them at the
/// section edges and moves the inside parts, Leave leaves them, a refused move restores the cuts, the whole move is one undo step, the prompt is
/// asked only under the Ask setting (Remember stores Split or Leave), and the setting and the prompt wording.
/// Tests: SectionAudioCrossings, SectionAudioPromptPresenter, SectionAudioPromptWindow, TimelineInputController (section drop), SettingsCatalog (editing.sectionaudio).
/// </summary>
public class SectionAudioMoveTests
{
    // 120 BPM in 4/4: a bar is 2 s. Intro bars 1-4 (0-8 s), Verse bars 5-8 (8-16 s), Chorus bars 9-12 (16-24 s). Track 1 "Vocals" holds the clips.
    private static SongDocument Make(params (double Start, double Length)[] clips)
    {
        var project = new SongProject { Title = "Song", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(12) });
        var vocals = new TrackModel { Name = "Vocals", Measures = TemplateFactory.Measures(12) };
        foreach (var (start, length) in clips)
            vocals.AudioClips.Add(new AudioClip { Name = "Take", File = "take.wav", StartSec = start, SourceLengthSec = length, FileLengthSec = 100 });
        project.Tracks.Add(vocals);
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Verse" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "Chorus" });
        return new SongDocument(() => null) { Project = project };
    }

    private static List<AudioClip> Clips(SongDocument doc) => doc.Project.Tracks[1].AudioClips.OrderBy(c => c.StartSec).ToList();

    private static (double Start, double Length)[] Shape(SongDocument doc) => Clips(doc).Select(c => (c.StartSec, c.LengthSec)).ToArray();

    private static string[] Order(SongDocument doc) => SectionLayout.Sorted(doc.Project).Select(m => m.Title).ToArray();

    private static EditResult<int[]> MoveVerseFirst(SongDocument doc, bool split) =>
        DocumentEdits.Run<int[]>(doc, song => SectionAudioCrossings.Move(song, 1, 0, split), label: "Move section");

    private static void AssertShape((double Start, double Length)[] expected, (double Start, double Length)[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Start, actual[i].Start, 6);
            Assert.Equal(expected[i].Length, actual[i].Length, 6);
        }
    }

    [Fact]
    public void OnlyClipsThatCrossTheEdgesAreFoundAndClipsFullyInsideAreNot()
    {
        var doc = Make((6, 4), (9, 2), (0, 2), (14, 4));   // crosses the start, inside, outside, crosses the end
        var crossing = SectionAudioCrossings.Find(doc.Project, 1)!;
        Assert.Equal(new[] { 6.0, 14.0 }, crossing.Clips.Select(c => c.Clip.StartSec).OrderBy(s => s));
        Assert.Equal(8.0, crossing.StartSec, 6);
        Assert.Equal(16.0, crossing.EndSec, 6);
        Assert.Null(SectionAudioCrossings.Find(Make((9, 2), (0, 2)).Project, 1));
    }

    [Fact]
    public void MidiClipsCrossingAreFoundToo()
    {
        var doc = Make();
        doc.Project.Tracks[1].AudioClips.Add(new AudioClip { Name = "Take", StartSec = 6, SourceLengthSec = 4, Notes = new List<ClipNote>() });
        Assert.Single(SectionAudioCrossings.Find(doc.Project, 1)!.Clips);
    }

    [Fact]
    public void SplitCutsAClipThatStartsBeforeAndMovesTheInsidePartByTheSectionDelta()
    {
        var doc = Make((6, 6));   // 6-12 s: 6-8 outside, 8-12 inside the verse
        Assert.True(MoveVerseFirst(doc, split: true).Changed);
        var clips = Clips(doc);
        AssertShape(new[] { (0.0, 4.0), (6.0, 2.0) }, Shape(doc));   // the inside part moved by -8 s to bar 1, the outside part stayed
        Assert.Equal(2.0, clips[0].OffsetSec - clips[1].OffsetSec, 6);   // the inside part continues the same file
        Assert.Equal(new[] { "Verse", "Intro", "Chorus" }, Order(doc));
    }

    [Fact]
    public void SplitCutsAClipOverBothEdgesIntoThreeAndOnlyTheMiddleMoves()
    {
        var doc = Make((6, 12), (9, 2));   // 6-18 s over the whole verse (8-16 s), and one clip fully inside
        MoveVerseFirst(doc, split: true);
        AssertShape(new[] { (0.0, 8.0), (1.0, 2.0), (6.0, 2.0), (16.0, 2.0) }, Shape(doc));
    }

    [Fact]
    public void SplitMovesAClipThatEndsAfterTheSectionLikeOne()
    {
        var doc = Make((12, 8));   // 12-20 s: 12-16 inside, 16-20 outside
        MoveVerseFirst(doc, split: true);
        AssertShape(new[] { (4.0, 4.0), (16.0, 4.0) }, Shape(doc));   // 12 s - 8 s = 4 s
    }

    [Fact]
    public void TheWholeSplitMoveIsOneUndoStepThatRestoresEveryClip()
    {
        var doc = Make((6, 12), (9, 2));
        MoveVerseFirst(doc, split: true);
        Assert.Equal(1, doc.Undo.UndoCount);
        Assert.NotNull(DocumentEdits.Undo(doc));
        AssertShape(new[] { (6.0, 12.0), (9.0, 2.0) }, Shape(doc));
        Assert.Equal(new[] { "Intro", "Verse", "Chorus" }, Order(doc));
    }

    [Fact]
    public void LeaveMovesOnlyTheNotationAndCrossingClipsStayExactlyAsTheyWere()
    {
        var doc = Make((6, 12), (9, 2));
        var crossing = doc.Project.Tracks[1].AudioClips[0];
        Assert.True(MoveVerseFirst(doc, split: false).Changed);
        AssertShape(new[] { (1.0, 2.0), (6.0, 12.0) }, Shape(doc));   // the clip fully inside still travels
        Assert.Contains(crossing, doc.Project.Tracks[1].AudioClips);
        Assert.Equal(1, doc.Undo.UndoCount);
    }

    [Fact]
    public void ARefusedMoveGivesTheCutClipsBack()
    {
        var doc = Make((14, 4));   // crosses the start of the chorus (16 s)
        var clip = doc.Project.Tracks[1].AudioClips[0];
        SectionLayout.Sorted(doc.Project)[1].LockPosition = true;   // the locked verse would be pushed by moving the chorus first
        Assert.Null(SectionAudioCrossings.Move(doc.Project, 2, 0, splitCrossing: true));
        Assert.Same(clip, Assert.Single(doc.Project.Tracks[1].AudioClips));
        AssertShape(new[] { (14.0, 4.0) }, Shape(doc));
        Assert.Equal(new[] { "Intro", "Verse", "Chorus" }, Order(doc));
    }

    // ---- the prompt through a section drop ----

    private sealed class Rig
    {
        public Window Window = null!;
        public TimelineView View = null!;
        public SongDocument Doc = null!;
        public List<SectionAudioCrossing> Asked = new();
        public string Setting = SectionAudioChoice.Ask;
        public SectionAudioAnswer? Answer;
    }

    private static Rig Open(SongDocument doc, SectionAudioAnswer? answer)
    {
        var rig = new Rig { Doc = doc, Answer = answer };
        rig.View = new TimelineView(doc.Project) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var input = new TimelineInputController(rig.View, doc, sectionClock: () => TimeSpan.FromSeconds(1), sectionTimer: new AvaloniaPlaybackSessionTests.FakeTimer());
        input.SectionAnimator.Enabled = false;
        input.SectionAudioSetting = () => rig.Setting;
        input.SectionAudioRemember = value => rig.Setting = value;
        input.SectionAudioPrompt = crossing => { rig.Asked.Add(crossing); return Task.FromResult(rig.Answer); };
        rig.Window = new Window { Width = 1300, Height = 400, Content = rig.View };
        rig.Window.Show();
        return rig;
    }

    /// <summary>Drags the section in slot <paramref name="slot"/> to the left of the first one.</summary>
    private static void DragToFront(Rig rig, int slot)
    {
        var slots = rig.View.SectionSlots();
        var from = new Point(slots[slot].Left + slots[slot].Width / 2, slots[slot].Top + 4);
        rig.Window.MouseDown(from, MouseButton.Left);
        rig.Window.MouseMove(from + new Vector(-2, 0));
        var to = new Point(slots[0].Left + 2, from.Y);
        rig.Window.MouseMove(to);
        rig.Window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public void AskWithACrossingClipRequestsThePromptAndCancelChangesNothing()
    {
        var rig = Open(Make((6, 6)), answer: null);
        DragToFront(rig, 1);
        var asked = Assert.Single(rig.Asked);
        Assert.Equal(new[] { "Vocals" }, asked.TrackNames(rig.Doc.Project));
        Assert.Equal("Verse", asked.SectionTitle);
        Assert.Equal(new[] { "Intro", "Verse", "Chorus" }, Order(rig.Doc));
        AssertShape(new[] { (6.0, 6.0) }, Shape(rig.Doc));
        Assert.Equal(0, rig.Doc.Undo.UndoCount);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void SplitAnswerWithoutRememberSplitsMovesAndStillAsksNextTime()
    {
        var rig = Open(Make((6, 6), (23, 3)), new SectionAudioAnswer(Split: true, Remember: false));
        DragToFront(rig, 1);
        Assert.Equal(new[] { "Verse", "Intro", "Chorus" }, Order(rig.Doc));
        AssertShape(new[] { (0.0, 4.0), (6.0, 2.0), (23.0, 3.0) }, Shape(rig.Doc));   // the verse clip cut in two, the chorus clip untouched
        Assert.Equal(1, rig.Doc.Undo.UndoCount);
        Assert.Equal(SectionAudioChoice.Ask, rig.Setting);
        DragToFront(rig, 2);   // the chorus is crossed by the 23-26 s clip: asked again
        Assert.Equal(2, rig.Asked.Count);
        rig.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true, SectionAudioChoice.Split, 4)]
    [InlineData(false, SectionAudioChoice.Leave, 2)]
    public void RememberStoresTheChoiceAndTheNextMoveDoesNotAsk(bool split, string stored, int clipCount)
    {
        var rig = Open(Make((6, 6), (23, 3)), new SectionAudioAnswer(split, Remember: true));
        DragToFront(rig, 1);
        Assert.Equal(stored, rig.Setting);
        Assert.Single(rig.Asked);
        DragToFront(rig, 2);   // the chorus is crossed by the 23-26 s clip: the stored choice answers
        Assert.Single(rig.Asked);
        Assert.Equal(new[] { "Chorus", "Verse", "Intro" }, Order(rig.Doc));
        Assert.Equal(clipCount, Clips(rig.Doc).Count);
        if (!split) Assert.Contains(Clips(rig.Doc), c => c.StartSec == 23 && c.LengthSec == 3);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void NoCrossingClipMeansNoPromptAndFullyInsideClipsMoveAlong()
    {
        var rig = Open(Make((9, 2), (0, 2)), new SectionAudioAnswer(true, false));
        DragToFront(rig, 1);
        Assert.Empty(rig.Asked);
        Assert.Equal(new[] { "Verse", "Intro", "Chorus" }, Order(rig.Doc));
        AssertShape(new[] { (0.0, 2.0), (1.0, 2.0) }, Shape(rig.Doc));   // the verse clip 9 s -> 1 s; the intro clip stays
        rig.Window.Close();
    }

    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    // ---- the setting and the words ----

    [Fact]
    public void TheSettingDefaultsToAskAndRoundTripsInSettingsJson()
    {
        Assert.Equal("Ask", new EditingSettings().SectionMoveAudio);
        var settings = new AppSettings();
        settings.Editing.SectionMoveAudio = SectionAudioChoice.Split;
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(SectionAudioChoice.Split, back.Editing.SectionMoveAudio);
        Assert.Equal(SectionAudioChoice.Ask, SectionAudioChoice.Normalize("nonsense"));
    }

    [Fact]
    public void PreferencesRowShowsTheThreeChoicesAndWritesTheStoredValue()
    {
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "editing.sectionaudio");
        Assert.Equal(SettingsCatalog.Editing, row.Category);
        Assert.Equal("When a moved section cuts through audio", row.Title);
        Assert.Equal(3, row.Choices.Count);
        Assert.Equal(row.Choices[0], row.Get());
        row.Set(row.Choices[2]);
        Assert.Equal(SectionAudioChoice.Leave, settings.Editing.SectionMoveAudio);
        Assert.Equal(row.Choices[2], row.Get());
        row.Set(row.Choices[0]);   // Ask brings the prompt back
        Assert.Null(SectionAudioPromptPresenter.Remembered(settings.Editing.SectionMoveAudio));
    }

    [Fact]
    public void ThePromptBodyNamesTheTrackSectionAndBars()
    {
        var doc = Make((6, 6));
        var crossing = SectionAudioCrossings.Find(doc.Project, 1)!;
        Assert.Equal("Vocals has audio that starts or ends outside Verse (bars 5\u20138). What should happen to it?", SectionAudioPromptPresenter.Body(doc.Project, crossing));
    }

    [AvaloniaFact]
    public void TheWindowDefaultsToSplitEnterMovesAndEscCancels()
    {
        var doc = Make((6, 6));
        var crossing = SectionAudioCrossings.Find(doc.Project, 1)!;
        var moved = new SectionAudioPromptWindow(doc.Project, crossing);
        moved.Show();
        Assert.True(Named<RadioButton>(moved, "SplitRadio").IsChecked);
        Assert.False(Named<RadioButton>(moved, "LeaveRadio").IsChecked);
        Named<CheckBox>(moved, "RememberBox").IsChecked = true;
        moved.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(new SectionAudioAnswer(true, true), moved.Answer);

        var cancelled = new SectionAudioPromptWindow(doc.Project, crossing);
        cancelled.Show();
        Named<RadioButton>(cancelled, "LeaveRadio").IsChecked = true;
        cancelled.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(cancelled.Answer);
    }
}

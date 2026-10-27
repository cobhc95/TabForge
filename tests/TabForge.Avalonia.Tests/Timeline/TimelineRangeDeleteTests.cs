using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Delete on bars selected on the timeline: leave-empty vs remove on every track, the clips (untouched, or split and moved), the repeat-split refusal,
//   the time-signature rule, clip-only tracks kept, the remembered answer skipping the prompt, Cancel changing nothing, one undo step, the prompt clip row,
//   and a score (ThisTrack) selection never reaching Range.Delete. The prompt is always the injected fake: no modal window opens.
// Does not own: the window other texts (PasteOptionsSpecialAndDeleteBarsTests), BarRangeGaps one-track and insert rules (TestBarRangeGaps).
// Tests: this class.
public sealed class TimelineRangeDeleteTests
{
    private const int Bars = 8;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Two guitar tracks of 8 bars; the note fret on each bar first cell is the original bar index, so a moved bar can be recognised.
    private static SongDocument NewSong(bool audioTrack = true)
    {
        var project = new SongProject();
        for (var t = 0; t < 2; t++)
            project.Tracks.Add(new TrackModel
            {
                Name = "Guitar " + (t + 1),
                Measures = Enumerable.Range(0, Bars).Select(bar =>
                {
                    var measure = new MeasureModel { Number = bar + 1 };
                    measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar });
                    return measure;
                }).ToList(),
            });
        var document = new SongDocument(() => null) { Project = project };
        if (audioTrack)
        {
            var audio = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
            var span = BarRangeGaps.Span(project, 1, 5).GetValueOrDefault();   // the clip lies over bars 2-6
            audio.AudioClips.Add(new AudioClip { File = "take.wav", Name = "take", StartSec = span.From, SourceLengthSec = span.To - span.From, FileLengthSec = span.To - span.From });
            project.Tracks.Add(audio);
        }
        document.MarkClean();
        return document;
    }

    private static MainWindowTimelineBars.DeletePrompt Answer(BarRangeAction action, Action? asked = null) => (_, _, _) =>
    {
        asked?.Invoke();
        return Task.FromResult<BarRangeAnswer?>(new BarRangeAnswer(action, AllTracks: true, Remember: false));
    };

    private static async Task<BarRangeAction?> Delete(SongDocument document, int first, int last, MainWindowTimelineBars.DeletePrompt prompt, EditingSettings? settings = null, List<string>? notices = null)
    {
        settings ??= new EditingSettings();
        return await MainWindowTimelineBars.DeleteRange(document, first, last, 0, allTracksDefault: true, text => notices?.Add(text), prompt, () => settings, () => { })
            .WaitAsync(Timeout);
    }

    private static List<int?> Frets(TrackModel track) => track.Measures.Select(m => m.Cells[0].Notes.Select(n => (int?)n.Fret).FirstOrDefault()).ToList();

    [Fact]
    public async Task LeaveEmptyKeepsEveryBarEmptiesTheRangeOnAllTracksAndLeavesTheClips()
    {
        var song = NewSong();
        var clip = song.Project.Tracks[2].AudioClips[0];
        var (start, length) = (clip.StartSec, clip.SourceLengthSec);

        Assert.Equal(BarRangeAction.Clear, await Delete(song, 2, 3, Answer(BarRangeAction.Clear)));

        foreach (var track in song.Project.Tracks.Take(2))
        {
            Assert.Equal(Bars, track.Measures.Count);
            Assert.Equal(new int?[] { 0, 1, null, null, 4, 5, 6, 7 }, Frets(track));
        }
        var kept = Assert.Single(song.Project.Tracks[2].AudioClips);
        Assert.Equal((start, length), (kept.StartSec, kept.SourceLengthSec));
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.False(song.Undo.CanUndo);   // one step
        Assert.Equal(Enumerable.Range(0, Bars).Select(b => (int?)b), Frets(song.Project.Tracks[0]));
    }

    [Fact]
    public async Task RemoveClosesTheGapSplitsTheSpanningClipAndKeepsTheClipOnlyTrack()
    {
        var song = NewSong();
        var seconds = BarRangeGaps.Span(song.Project, 0, 0).GetValueOrDefault().To;
        var tracks = song.Project.Tracks.Count;

        Assert.Equal(BarRangeAction.Remove, await Delete(song, 2, 3, Answer(BarRangeAction.Remove)));

        Assert.Equal(tracks, song.Project.Tracks.Count);   // the audio track stays
        Assert.Equal(new int?[] { 0, 1, 4, 5, 6, 7 }, Frets(song.Project.Tracks[0]));   // old bar 5 is now bar 3
        var parts = song.Project.Tracks[2].AudioClips.OrderBy(c => c.StartSec).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(seconds, parts[0].StartSec, 3);                       // bar 2 and ...
        Assert.Equal(seconds * 2, parts[0].EndSec, 3);
        Assert.Equal(parts[0].EndSec, parts[1].StartSec, 3);               // ... old bars 5-6 are joined at the cut
        Assert.Equal(seconds * 4, parts[1].EndSec, 3);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.False(song.Undo.CanUndo);
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);
        Assert.Single(song.Project.Tracks[2].AudioClips);
    }

    [Fact]
    public async Task RemovingARangeThatCutsARepeatIsRefusedButLeavingItEmptyIsAllowed()
    {
        var song = NewSong(audioTrack: false);
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[4].RepeatEnd = true;
        var notices = new List<string>();

        Assert.Null(await Delete(song, 3, 5, Answer(BarRangeAction.Remove), notices: notices));
        Assert.Contains("repeat", Assert.Single(notices));
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);
        Assert.False(song.Undo.CanUndo);   // the refusal leaves no undo step

        Assert.Equal(BarRangeAction.Clear, await Delete(song, 3, 5, Answer(BarRangeAction.Clear)));
        Assert.Equal(Bars, song.Project.Tracks[0].Measures.Count);

        var whole = NewSong(audioTrack: false);
        whole.Project.Tracks[0].Measures[1].RepeatStart = true;
        whole.Project.Tracks[0].Measures[4].RepeatEnd = true;
        Assert.Equal(BarRangeAction.Remove, await Delete(whole, 1, 4, Answer(BarRangeAction.Remove)));   // the whole repeat goes
        Assert.Equal(Bars - 4, whole.Project.Tracks[0].Measures.Count);
    }

    [Fact]
    public async Task RemovingABarWithATimeSignatureChangeLetsTheNextBarsOwnSignatureApplyAndKeepsLaterMeter()
    {
        var own = NewSong(audioTrack: false);
        foreach (var track in own.Project.Tracks)
        {
            track.Measures[3].TimeSigNum = 6; track.Measures[3].TimeSigDenom = 8;
            track.Measures[4].TimeSigNum = 3; track.Measures[4].TimeSigDenom = 4;
        }
        await Delete(own, 3, 3, Answer(BarRangeAction.Remove));
        Assert.Equal((3, 4), BarSignatures.TimeAt(own.Project, 3));    // old bar 5 keeps its own signature
        Assert.Equal((4, 4), BarSignatures.TimeAt(own.Project, 4));

        var none = NewSong(audioTrack: false);
        foreach (var track in none.Project.Tracks) { track.Measures[3].TimeSigNum = 6; track.Measures[3].TimeSigDenom = 8; }
        await Delete(none, 3, 3, Answer(BarRangeAction.Remove));
        for (var bar = 3; bar < Bars - 1; bar++) Assert.Equal((4, 4), BarSignatures.TimeAt(none.Project, bar));   // later bars do not change meter

        var first = NewSong(audioTrack: false);
        first.Project.TimeSignatureNumerator = 3;
        foreach (var measure in first.Project.Tracks.SelectMany(t => t.Measures.Skip(1))) { measure.TimeSigNum = 4; measure.TimeSigDenom = 4; }
        await Delete(first, 0, 0, Answer(BarRangeAction.Remove));
        for (var bar = 0; bar < Bars - 1; bar++) Assert.Equal((4, 4), BarSignatures.TimeAt(first.Project, bar));   // the song signature followed the new first bar
    }

    [Fact]
    public async Task ARememberedAnswerSkipsThePromptAndCancelChangesNothing()
    {
        var remembered = NewSong();
        var asked = false;
        var settings = new EditingSettings { BarRangeDelete = BarRangePromptText.Store(BarRangeAction.Remove, allTracks: true) };
        Assert.Equal(BarRangeAction.Remove, await Delete(remembered, 2, 3, Answer(BarRangeAction.Clear, () => asked = true), settings));
        Assert.False(asked);
        Assert.Equal(Bars - 2, remembered.Project.Tracks[0].Measures.Count);

        var cancelled = NewSong();
        var before = DocumentEdits.Fingerprint(cancelled);
        var cancelSettings = new EditingSettings();
        Assert.Null(await Delete(cancelled, 2, 3, (_, _, _) => Task.FromResult<BarRangeAnswer?>(null), cancelSettings));
        Assert.Equal(before, DocumentEdits.Fingerprint(cancelled));
        Assert.False(cancelled.Undo.CanUndo);
        Assert.Equal("Ask", cancelSettings.BarRangeDelete);
    }

    [Fact]
    public void AnOlderRememberedInsertAnswerReadsAsAskAndTheSettingsRowOffersOnlyTheDeleteActions()
    {
        Assert.Null(BarRangePromptText.Remembered(BarRangePromptText.Store(BarRangeAction.InsertBefore, allTracks: true)));
        Assert.Equal(BarRangePromptText.AskLabel, BarRangePromptText.ToChoice(BarRangeAction.InsertAfter.ToString()));
        Assert.Equal(1 + 2 * BarRangePromptText.DeleteActions.Count, BarRangePromptText.Choices.Length);
        Assert.Equal(new[] { "Leave empty bars", "Remove bars (close the gap)" }, BarRangePromptText.DeleteActions.Select(BarRangePromptText.Label));
    }

    [AvaloniaFact]
    public void ThePromptPairsLeaveWithLeaveEmptyAndSplitAndMoveWithRemove()
    {
        var window = new BarRangePromptWindow("Bars 3-4 are selected.", BarRangeAction.Clear, allTracks: true, _ => "");
        window.Show();
        RadioButton Radio(string name) => window.GetLogicalDescendants().OfType<RadioButton>().Single(r => r.Name == name);

        Assert.Equal(new[] { "Leave", "Split and move" }, BarRangePromptPresenter.ClipLabels);
        Assert.True(Radio("Choice0").IsChecked);                        // the default: leave empty bars ...
        Assert.True(Radio("Clip0").IsChecked == true && Radio("Clip0").IsEnabled);   // ... and leave the clips
        Assert.False(Radio("Clip1").IsEnabled);

        Radio("Choice1").IsChecked = true;                              // Remove: Leave is disabled, Split and move is forced
        Assert.True(Radio("Clip1").IsChecked == true && Radio("Clip1").IsEnabled);
        Assert.False(Radio("Clip0").IsEnabled);
        Assert.Equal(new BarRangeAnswer(BarRangeAction.Remove, true, false), window.Current());
        window.Close();
    }

    [AvaloniaFact]
    public void AScoreSelectionOrTheLoopGivesRangeDeleteItsBarsWithoutAsking()
    {
        var song = NewSong(audioTrack: false);
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
        var model = arrangement.Input.Selection;
        var commands = (MainWindowCommands)arrangement.Timeline.ContextMenus.Commands;

        model.SetRange(0, 1, 2, SelectionOrigin.Timeline, 0, -1, SelectionScope.ThisTrack);
        Assert.True(commands.Applies(MainWindowRangeDelete.DeleteId));   // the white selection's bar span (SelectionBars)
        model.SetLoop(1, 2);   // the loop region wins when set
        Assert.True(commands.Applies(MainWindowRangeDelete.DeleteId));
        Assert.Empty(window.OwnedWindows);   // nothing was asked

        song.MarkClean();
        window.Close();
    }
}

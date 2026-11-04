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
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the races around the Track properties tuning dialog: a removal warning answered after a later preset or retune, OK while a
/// confirmation or the instrument picker is still open, an instrument picker cancelled over an edited tuning draft, and the note data Undo restores.
/// Does not own: the tuning rules (TrackTuningDraftTests) or the ordinary window checks (TrackPropertiesWindowTests).
/// Tests: this class.
/// </summary>
public sealed class TrackPropertiesWindowConcurrencyTests
{
    private static TrackModel GuitarWithLowNotes()
    {
        var track = new TrackModel
        {
            Name = "Tuning test", Kind = TrackKind.Guitar, NumberOfFrets = 24,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
        };
        var measure = new MeasureModel();
        track.Measures.Add(measure);
        measure.Cells[0].Notes.Add(new TabNote { StringIndex = 5, Fret = 5, MidiValue = track.PitchOf(5, 5) });   // A2
        measure.Cells[1].Notes.Add(new TabNote { StringIndex = 5, Fret = 7, MidiValue = track.PitchOf(5, 7) });   // B2
        return track;
    }

    private static (TrackModel Track, ScorePreviewEditor.PreviewDocument Doc, TrackPropertiesWindow Window) Open(TrackModel track)
    {
        var doc = new ScorePreviewEditor.PreviewDocument(new SongProject { Tracks = new List<TrackModel> { track } });
        var window = new TrackPropertiesWindow(track, doc);
        window.Show();
        return (track, doc, window);
    }

    [AvaloniaFact]
    public void AnAnsweredRemovalWarningDoesNotOverwriteAPresetPickedWhileItWasOpen()
    {
        var (track, doc, window) = Open(GuitarWithLowNotes());
        var answer = new TaskCompletionSource<bool>();
        window.ConfirmNoteMove = _ => answer.Task;
        try
        {
            RemoveLowString(window);
            DialogTestKit.RunQueuedUiWork();
            Assert.False(answer.Task.IsCompleted);

            // The preset has the same count, so it needs no question: it replaces the working strings at once.
            window.ApplyPreset(new TuningMenuEntry("Drop D", "Guitar", 6, new[] { 64, 59, 55, 50, 45, 38 }));

            answer.SetResult(true);
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal(3, Named<ComboBox>(window, "StringCountBox").SelectedIndex);   // still six strings
            Assert.Equal("D2", Named<TextBox>(window, "NoteBox5").Text);
            Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, track.StringTunings);
            Assert.Equal(0, doc.Undo.UndoCount);
        }
        finally { Finish(window, answer); }
    }

    [AvaloniaFact]
    public void OkDoesNothingWhileARemovalWarningIsStillOpen()
    {
        var (track, doc, window) = Open(GuitarWithLowNotes());
        var answer = new TaskCompletionSource<bool>();
        window.ConfirmNoteMove = _ => answer.Task;
        try
        {
            RemoveLowString(window);
            DialogTestKit.RunQueuedUiWork();
            Click(window, "OkButton");
            DialogTestKit.RunQueuedUiWork();

            Assert.True(window.IsVisible);
            Assert.False(window.Applied);
            Assert.Equal(0, doc.Undo.UndoCount);
            Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, track.StringTunings);

            answer.SetResult(false);
            DialogTestKit.RunQueuedUiWork();
            Assert.True(window.IsVisible);
            Assert.Equal(3, Named<ComboBox>(window, "StringCountBox").SelectedIndex);
        }
        finally { Finish(window, answer); }
    }

    [AvaloniaFact]
    public void CancellingTheInstrumentPickerKeepsTheEditedTuningDraft()
    {
        var (track, doc, window) = Open(GuitarWithLowNotes());
        try
        {
            Click(window, "StringUp0");
            Assert.Equal("F4", Named<TextBox>(window, "NoteBox0").Text);

            Click(window, "InstrumentChooserButton");
            DialogTestKit.RunQueuedUiWork();
            var picker = window.OwnedWindows.OfType<InstrumentPickerWindow>().Single();
            DialogTestKit.Click(picker.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel"));
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal("F4", Named<TextBox>(window, "NoteBox0").Text);
            Assert.Equal(3, Named<ComboBox>(window, "StringCountBox").SelectedIndex);
            Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, track.StringTunings);
            Assert.Equal(0, doc.Undo.UndoCount);
        }
        finally { Finish(window, null); }
    }

    [AvaloniaFact]
    public void OkDoesNothingWhileTheInstrumentPickerIsOpen()
    {
        var (_, doc, window) = Open(GuitarWithLowNotes());
        try
        {
            Click(window, "InstrumentChooserButton");
            DialogTestKit.RunQueuedUiWork();
            var picker = window.OwnedWindows.OfType<InstrumentPickerWindow>().Single();

            Click(window, "OkButton");
            DialogTestKit.RunQueuedUiWork();

            Assert.True(window.IsVisible);
            Assert.False(window.Applied);
            Assert.Equal(0, doc.Undo.UndoCount);
            Assert.Contains(picker, window.OwnedWindows);

            DialogTestKit.Click(picker.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel"));
            DialogTestKit.RunQueuedUiWork();
            Assert.True(window.IsVisible);
        }
        finally { Finish(window, null); }
    }

    [AvaloniaFact]
    public void ChoosingAnInstrumentThatKeepsTheStringsKeepsTheEditedTuningDraft()
    {
        var (track, doc, window) = Open(GuitarWithLowNotes());
        try
        {
            Click(window, "StringUp0");
            InstrumentPickerRunTests.PickThroughTheChooser(window, "Electric Guitar (Clean)");

            Assert.Equal("F4", Named<TextBox>(window, "NoteBox0").Text);
            Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, track.StringTunings);
            Assert.Equal(0, doc.Undo.UndoCount);
        }
        finally { Finish(window, null); }
    }

    [AvaloniaFact]
    public void ANewTuningEditWhileTheOkWarningIsOpenIsWarnedAgainBeforeTheCommit()
    {
        var (track, doc, window) = Open(GuitarWithLowNotes());
        var answers = new List<TuningRemovalImpact>();
        var first = new TaskCompletionSource<bool>();
        window.ConfirmNoteMove = _ => Task.FromResult(true);
        window.ConfirmUndisclosedPitchChange = impact =>
        {
            answers.Add(impact);
            return answers.Count == 1 ? first.Task : Task.FromResult(true);
        };
        try
        {
            RemoveLowString(window);
            Click(window, "StringUp4");
            Click(window, "StringUp4");   // the lowest kept string is B2: A2 on the removed string must change pitch
            DialogTestKit.RunQueuedUiWork();

            Click(window, "OkButton");
            DialogTestKit.RunQueuedUiWork();
            Assert.Single(answers);
            Assert.Equal((2, 1), (answers[0].Total, answers[0].Unplayable));

            Click(window, "StringUp4");   // the lowest kept string becomes C3: B2 must now change pitch too
            first.SetResult(true);
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal(2, answers.Count);
            Assert.Equal((2, 2), (answers[1].Total, answers[1].Unplayable));
            Assert.True(window.Applied);
            Assert.Equal(1, doc.Undo.UndoCount);
            Assert.Equal(new[] { 64, 59, 55, 50, 48 }, doc.Project.Tracks[0].StringTunings);
            Assert.All(doc.Project.Tracks[0].Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes),
                note => Assert.Equal(note.MidiValue, doc.Project.Tracks[0].PitchOf(note.StringIndex, note.Fret)));
        }
        finally { Finish(window, first); }
    }

    [AvaloniaFact]
    public void UndoRestoresEveryNoteFieldAfterARemovalAndRetune()
    {
        var track = GuitarWithLowNotes();
        track.Measures[0].Cells[0].Notes[0].SlideTargetMidi = 50;
        track.Measures[0].Cells[0].Notes[0].TrillTargetMidi = 52;
        track.Measures[0].Cells[1].Notes[0].Tied = true;
        var before = NoteStates(track);
        var (_, doc, window) = Open(track);
        window.ConfirmNoteMove = _ => Task.FromResult(true);
        window.ConfirmUndisclosedPitchChange = _ => Task.FromResult(true);
        try
        {
            RemoveLowString(window);
            Click(window, "StringUp4");
            Click(window, "StringUp4");   // the kept low string moves from A2 to B2
            Click(window, "OkButton");
            DialogTestKit.RunQueuedUiWork();

            Assert.True(window.Applied);
            Assert.Equal(1, doc.Undo.UndoCount);
            Assert.NotEqual(before, NoteStates(doc.Project.Tracks[0]));

            Assert.NotNull(DocumentEdits.Undo(doc));
            Assert.Equal(before, NoteStates(doc.Project.Tracks[0]));
            Assert.Equal(new[] { 64, 59, 55, 50, 45, 40 }, doc.Project.Tracks[0].StringTunings);
        }
        finally { Finish(window, null); }
    }

    private static string[] NoteStates(TrackModel track) => track.Measures
        .SelectMany(m => m.Cells.Concat(m.Voice2Cells))
        .SelectMany(c => c.Notes)
        .Select(n => $"{n.StringIndex}/{n.Fret}/{n.MidiValue}/{n.SlideTargetMidi}/{n.TrillTargetMidi}/{n.Tied}")
        .ToArray();

    // v7: removal goes through the string-count box (ChangeCount drops the lowest string).
    private static void RemoveLowString(Window window) { var box = Named<ComboBox>(window, "StringCountBox"); box.SelectedIndex -= 1; }

    private static void Click(Window window, string name) => DialogTestKit.Click(Named<Button>(window, name));

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Finish(TrackPropertiesWindow window, TaskCompletionSource<bool>? pending)
    {
        if (pending is not null && !pending.Task.IsCompleted) pending.SetResult(false);
        DialogTestKit.RunQueuedUiWork();
        if (!window.IsVisible) return;
        window.Close();
        DialogTestKit.RunQueuedUiWork();
        if (!window.IsVisible) return;
        var prompt = window.OwnedWindows.OfType<SavePromptWindow>().SingleOrDefault();
        if (prompt is not null) DialogTestKit.Click(prompt.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "Yes"));
        DialogTestKit.RunQueuedUiWork();
    }
}

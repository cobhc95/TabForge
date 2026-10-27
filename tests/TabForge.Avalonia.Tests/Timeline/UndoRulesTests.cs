using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Lyrics;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the app-wide undo rules from the 2026-10-21 spec: record arm is its own undo step per toggle (the 2026-10-21 user decision; the
//   earlier "arm is never undone" rule changed), a recorded take is its own step, a recording stop is one step for several tracks, a no-op
//   tempo/project-settings apply keeps the redo stack, lyrics are one labelled step, a mixer drag is one dirty step, and a new recording lane
//   goes with undo.
// Does not own: the undo engine (UndoController), the edit pipeline (DocumentEdits) or the recording capture (TransportRecordingTests).
// Tests: this class. Headless, in-memory, no engine and no sleeps.
public sealed class UndoRulesTests
{
    private static SongDocument NewSong()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        song.MarkClean();
        return song;
    }

    private static TrackModel AddAudioTrack(SongDocument song)
    {
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return track;
    }

    [AvaloniaFact]
    public void UndoingARecordedTakeKeepsTheTrackArmedAndRedoBringsItBack()
    {
        var song = NewSong();
        var track = AddAudioTrack(song);
        track.RecordArm = true;

        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", 1, 2);
        Assert.Single(song.Project.Tracks[^1].AudioClips);
        Assert.Equal(1, song.Undo.UndoCount);

        Assert.NotNull(DocumentEdits.Undo(song));
        var undone = song.Project.Tracks[^1];
        Assert.Empty(undone.AudioClips);
        Assert.True(undone.RecordArm);                 // arming is not rewound by the take's undo

        Assert.NotNull(DocumentEdits.Redo(song));
        var redone = song.Project.Tracks[^1];
        Assert.Single(redone.AudioClips);
        Assert.True(redone.RecordArm);
    }

    [AvaloniaFact]
    public void RecordingOnTwoTracksAndStoppingIsOneUndoStep()
    {
        var song = NewSong();
        var first = AddAudioTrack(song);
        var second = AddAudioTrack(song);
        first.RecordArm = true;
        second.RecordArm = true;

        // The stop's landing state is taken once; every file that arrives afterwards joins that same step.
        var before = song.Undo.Snapshot(song.Project, "Record takes");
        MainWindowTransportRecord.LandTake(new[] { song }, first, "a.wav", 1, 2, before: before);
        MainWindowTransportRecord.LandTake(new[] { song }, second, "b.wav", 1, 2, before: before);

        Assert.Equal(1, song.Undo.UndoCount);
        Assert.Single(song.Project.Tracks[^2].AudioClips);
        Assert.Single(song.Project.Tracks[^1].AudioClips);

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Empty(song.Project.Tracks[^2].AudioClips);
        Assert.Empty(song.Project.Tracks[^1].AudioClips);
        Assert.True(song.Project.Tracks[^2].RecordArm && song.Project.Tracks[^1].RecordArm);
    }

    [AvaloniaFact]
    public void ANoOpProjectSettingsApplyKeepsTheUndoCountAndRedoStack()
    {
        var song = NewSong();
        Assert.False(MainWindowProjectSettings.Apply(song, MainWindowProjectSettings.ResultOf(song.Project)));
        Assert.Equal(0, song.Undo.UndoCount);

        Assert.True(MainWindowTempoBar.SetTempo(song, 140));
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.True(song.Undo.CanRedo);

        Assert.False(MainWindowProjectSettings.Apply(song, MainWindowProjectSettings.ResultOf(song.Project)));
        Assert.Equal(0, song.Undo.UndoCount);
        Assert.True(song.Undo.CanRedo);
    }

    [AvaloniaFact]
    public void OneLyricsEditIsOneLabelledStepAndARepeatedTextTakesNoStep()
    {
        var song = NewSong();
        Assert.True(LyricsController.SetBarText(song, 0, "hello"));
        Assert.Equal(1, song.Undo.UndoCount);
        Assert.False(LyricsController.SetBarText(song, 0, "hello"));   // the text already holds those syllables
        Assert.Equal(1, song.Undo.UndoCount);

        var undone = DocumentEdits.Undo(song);
        Assert.NotNull(undone);
        Assert.Equal("Edit lyrics", undone!.Value.Label);
        Assert.DoesNotContain("hello", song.Project.FirstNotationTrack!.Measures[0].Cells.Select(cell => cell.Lyrics));
    }

    [AvaloniaFact]
    public void OneMasterPanGestureIsOneLabelledUndoStepAndMarksTheSongDirty()
    {
        var song = NewSong();
        var before = song.Project.Mixer.MasterPan;
        var window = new MixerWindow(song);
        window.Show();
        var slider = window.GetVisualDescendants().OfType<Slider>()
            .First(candidate => candidate.Name == "Fader" &&
                (AutomationProperties.GetName(candidate) ?? "").StartsWith("Master pan", System.StringComparison.Ordinal));

        slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, Source = slider });
        slider.Value = before + 3;

        Assert.True(song.HasUnsavedChanges);
        Assert.Equal(1, song.Undo.UndoCount);
        var undone = DocumentEdits.Undo(song);
        Assert.NotNull(undone);
        Assert.Equal("Master pan", undone!.Value.Label);
        Assert.Equal(before, song.Project.Mixer.MasterPan);
        window.Close();
    }

    [AvaloniaFact]
    public void UndoingATakeThatOpenedANewLaneRemovesTheLaneToo()
    {
        var song = NewSong();
        var track = AddAudioTrack(song);
        track.AudioClips.Add(new AudioClip
        {
            File = "old.wav", Name = "old", StartSec = 0, SourceLengthSec = 10, FileLengthSec = 10, Lane = 0,
        });
        var lanesBefore = track.Lanes.Count;

        MainWindowTransportRecord.LandTake(new[] { song }, track, "new.wav", 2, 3);
        var landed = song.Project.Tracks[^1];
        Assert.Equal(1, landed.AudioClips[^1].Lane);   // the new take needed the lane below the old clip
        Assert.Equal(2, landed.Lanes.Count);

        Assert.NotNull(DocumentEdits.Undo(song));
        var undone = song.Project.Tracks[^1];
        Assert.Single(undone.AudioClips);
        Assert.Equal(lanesBefore, undone.Lanes.Count);   // no orphan lane left behind
        Assert.Equal(0, undone.AudioClips[^1].Lane);
    }

    [AvaloniaFact]
    public void ArmingAndDisarmingAreEachTheirOwnUndoStep()
    {
        // Rule changed by the user (2026-10-21): arm and disarm are each their own step, never merged into the take's landing.
        var song = NewSong();
        var track = AddAudioTrack(song);
        song.TrackIndex = song.Project.Tracks.IndexOf(track);
        var count = song.Undo.UndoCount;

        MainWindowTrackArmKeys.ToggleArm(song);
        Assert.True(track.RecordArm);
        Assert.Equal(count + 1, song.Undo.UndoCount);
        Assert.Equal("Arm track", song.Undo.Latest!.Value.Label);

        MainWindowTrackArmKeys.ToggleArm(song);
        Assert.False(track.RecordArm);
        Assert.Equal(count + 2, song.Undo.UndoCount);
        Assert.Equal("Disarm track", song.Undo.Latest!.Value.Label);

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.True(song.Project.Tracks[^1].RecordArm);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.False(song.Project.Tracks[^1].RecordArm);
    }
}

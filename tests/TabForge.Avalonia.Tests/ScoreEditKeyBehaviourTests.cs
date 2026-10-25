using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the two score-editing defaults: + shortens / - lengthens (with the "Plus key lengthens notes" flip), and the cursor
/// staying on the note just written (with the "Move to the next note after writing" opt-in), plus their settings file round-trip.
/// Tests: ScorePreviewEditor, ScoreEditCommands, HotkeyCatalog, SettingsFileService.
/// </summary>
public class ScoreEditKeyBehaviourTests
{
    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor, TrackModel Track) Make()
    {
        var song = DemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, Views.PreferencesCatalog.DefaultHotkeys()), song.Tracks[0]);
    }

    private static bool Press(ScorePreviewEditor editor, Key key, bool ctrl = false, bool shift = false) =>
        editor.Key(AvaloniaEditorKeys.ToEditorKey(key), key.ToString(), ctrl, shift, false);

    // Writes one note on an empty bar at the current writing duration and returns its beat (bar 5 of the demo is empty).
    private static TabCell WriteNote(ScoreGeometryProbe probe, ScorePreviewEditor editor, int measure, int denominator)
    {
        var track = probe.Project!.Tracks[0];
        var stringIndex = Enumerable.Range(0, track.StringTunings.Count)
            .First(s => track.Measures[measure].CellsForVoice(0).All(c => c.Notes.All(n => n.StringIndex != s)));
        editor.Tools.DurationDenominator = denominator;
        probe.Cursor = (measure, 0, stringIndex);
        Press(editor, Key.D5);
        return track.Measures[measure].CellsForVoice(0)[0];
    }

    [AvaloniaFact]
    public void PlusShortensAndMinusLengthensByDefault()
    {
        var (probe, editor, _) = Make();
        var beat = WriteNote(probe, editor, 5, 2);
        Assert.Equal(2, beat.DurationDenominator);

        Assert.True(Press(editor, Key.OemPlus, shift: true));   // the "+" key
        Assert.Equal(4, beat.DurationDenominator);

        Assert.True(Press(editor, Key.OemMinus));   // the "-" key
        Assert.Equal(2, beat.DurationDenominator);
    }

    [AvaloniaFact]
    public void PlusKeyLengthensNotesSettingRestoresTheOldPolarity()
    {
        var view = new ScorePreviewView();
        var settings = new AppSettings();
        settings.Editing.PlusKeyLengthensNotes = true;
        new ScoreSettingsApplier(view).Apply(settings);
        Assert.True(view.Editor.Tools.PlusKeyLengthensNotes);

        var track = view.Probe.Project!.Tracks[0];
        view.Editor.Tools.DurationDenominator = 4;
        view.Probe.Cursor = (0, 0, 0);
        Press(view.Editor, Key.D5);
        var beat = track.Measures[0].CellsForVoice(0)[0];
        Assert.Equal(4, beat.DurationDenominator);

        Assert.True(Press(view.Editor, Key.OemPlus, shift: true));
        Assert.Equal(2, beat.DurationDenominator);

        Assert.True(Press(view.Editor, Key.OemMinus));
        Assert.Equal(4, beat.DurationDenominator);
    }

    [AvaloniaFact]
    public void RestFillAndOverflowSettingsReachTheScoreTools()
    {
        var view = new ScorePreviewView();
        var settings = new AppSettings();
        settings.Editing.FillBarsWithRests = true;
        settings.Editing.MergeRestsOnDelete = false;
        settings.Editing.PreventBarOverflow = true;
        view.Editor.Tools.FillBarsWithRests = false;
        view.Editor.Tools.MergeRestsOnDelete = true;
        view.Editor.Tools.PreventBarOverflow = false;

        new ScoreSettingsApplier(view).Apply(settings);

        Assert.True(view.Editor.Tools.FillBarsWithRests);
        Assert.False(view.Editor.Tools.MergeRestsOnDelete);
        Assert.True(view.Editor.Tools.PreventBarOverflow);
    }

    [AvaloniaFact]
    public void WritingANoteKeepsTheCursorOnItByDefault()
    {
        var (probe, editor, _) = Make();
        WriteNote(probe, editor, 5, 4);
        Assert.Equal((5, 0), (editor.Cursor!.Value.Measure, editor.Cursor!.Value.Cell));
    }

    [AvaloniaFact]
    public void MoveToTheNextNoteAfterWritingSettingAdvancesAsBefore()
    {
        var (probe, editor, _) = Make();
        editor.Tools.AutoAdvanceAfterEntry = true;
        WriteNote(probe, editor, 5, 4);

        var (measure, cell, stringIndex) = editor.Cursor!.Value;
        Assert.NotEqual((5, 0), (measure, cell));
        Assert.Equal(stringIndex, editor.Cursor!.Value.String);
    }

    [Fact]
    public void TheAutoAdvanceDefaultMovesOffOnceAndALaterOptInIsKept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-auto-advance-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"Editing\":{\"AutoAdvance\":true}}");

            var migrated = SettingsFileService.Load(path);
            Assert.False(migrated.Editing.MoveToNextNoteAfterWriting);
            Assert.Equal(1, migrated.Editing.MoveToNextNoteVersion);

            migrated.Editing.MoveToNextNoteAfterWriting = true;
            SettingsFileService.SaveAtomic(path, migrated);
            var reloaded = SettingsFileService.Load(path);
            Assert.True(reloaded.Editing.MoveToNextNoteAfterWriting);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheTwoSettingsRoundTripAndOldFilesLoadTheDefaults()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-score-edit-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var settings = new AppSettings();
            settings.Editing.MoveToNextNoteAfterWriting = true;
            settings.Editing.PlusKeyLengthensNotes = true;
            SettingsFileService.SaveAtomic(path, settings);

            var loaded = SettingsFileService.Load(path);
            Assert.True(loaded.Editing.MoveToNextNoteAfterWriting);
            Assert.True(loaded.Editing.PlusKeyLengthensNotes);
            Assert.True(loaded.Hotkeys.PlusKeyLengthensNotes);

            File.WriteAllText(path, "{\"Editing\":{\"DefaultDuration\":8}}");
            var old = SettingsFileService.Load(path);
            Assert.Equal(8, old.Editing.DefaultDuration);
            Assert.False(old.Editing.MoveToNextNoteAfterWriting);
            Assert.False(old.Editing.PlusKeyLengthensNotes);
            Assert.False(old.Hotkeys.PlusKeyLengthensNotes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

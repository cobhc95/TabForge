using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the sound of a fretboard entry: a click that writes a note asks for the same audition a typed note asks for (the track's MIDI note,
/// its output and the cell's length).
/// Does not own: the audio output (the playback engine), the note rules (FretboardNoteEntryTests), the setting (SettingsCatalog).
/// Tests: FretboardNoteEntry, ScorePreviewEditor.NotePreviewed.
/// </summary>
public class FretboardNoteAuditionTests
{
    [global::Avalonia.Headless.XUnit.AvaloniaFact]
    public void FretboardEntryRequestsTheAuditionOfTheNoteItWrites()
    {
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = DemoSongFactory.Create() });
        ScorePreviewEditor.NotePreview? heard = null;
        score.Editor.NotePreviewed += preview => heard = preview;
        score.Editor.SetPosition(0, 0, 0);

        Assert.True(FretboardNoteEntry.Toggle(score.Editor, 1, 3));

        var track = score.Editor.Project!.Tracks[0];
        Assert.NotNull(heard);
        Assert.Equal(track.PitchOf(1, 3), heard!.Value.Midi);
        Assert.Equal(track.MidiOutputDeviceId, heard.Value.DeviceId);
        Assert.True(heard.Value.LengthMs > 0);
    }
}

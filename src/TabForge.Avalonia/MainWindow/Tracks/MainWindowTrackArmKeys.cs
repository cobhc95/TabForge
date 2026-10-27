using System;
using System.Collections.Generic;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: Track.Arm, the key that arms or disarms the selected track for recording on the shown song. Each toggle is its own undo step
//   ("Arm track"/"Disarm track"), saved and redrawn; disarming trims the track's clip lanes (the same rule as the arm cell in the track
//   row) unless the track is still being recorded, whose lane stays until the global stop.
// Does not own: the track row arm cell (TimelineInputController), recording itself (the transport), the key table (MainWindowKeyboard).
// Tests: TabForge.Avalonia.Tests.MainWindowTrackArmKeysTests, TabForge.Avalonia.Tests.UndoRulesTests, TabForge.Avalonia.Tests.MainWindowMenuTests.
public static class MainWindowTrackArmKeys
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Track.Arm" };

    /// <param name="commands">The window's command table.</param>
    /// <param name="songs">The tabs; the command acts on the shown song's selected track.</param>
    /// <param name="recordingTrack">True while that track is still being captured (its lane must survive the disarm); null: never.</param>
    /// <param name="afterToggle">Told after a committed toggle (the recording capture ends on the disarm).</param>
    /// <param name="refused">Told the status text when the track has no input and is not armed.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Func<TrackModel, bool>? recordingTrack = null,
        Action<SongDocument>? afterToggle = null, Action<string>? refused = null) =>
        commands.Add("Track.Arm", () => ToggleArm(songs.Active, recordingTrack, afterToggle, refused));

    /// <summary>Arms the selected track, or disarms it and trims its clip lanes; one undo step each. A song without tracks takes no step.</summary>
    public static void ToggleArm(SongDocument song, Func<TrackModel, bool>? recordingTrack = null, Action<SongDocument>? afterToggle = null, Action<string>? refused = null)
    {
        var index = song.TrackIndex;
        if (index < 0 || index >= song.Project.Tracks.Count) return;
        var armed = song.Project.Tracks[index].RecordArm;
        if (!armed && !AudioInputs.HasInput(song.Project.Tracks[index]))
        {
            refused?.Invoke(AudioInputs.NoInputText);   // nothing to record from: not armed
            return;
        }
        var changed = DocumentEdits.Run(song, project =>
        {
            var track = project.Tracks[index];
            track.RecordArm = !track.RecordArm;
            if (!track.RecordArm && recordingTrack?.Invoke(track) != true) ClipLanes.Trim(track);
            return true;
        }, label: armed ? "Disarm track" : "Arm track").Changed;
        if (!changed) return;
        afterToggle?.Invoke(song);
    }
}

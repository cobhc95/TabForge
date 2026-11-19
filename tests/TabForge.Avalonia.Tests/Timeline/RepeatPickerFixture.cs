using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Tests;

internal static class RepeatPickerFixture
{
    internal static (SongDocument Song, TrackModel Track) RepeatSevenSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        song.Project.Tracks[0].Measures[2].RepeatCount = 7;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    internal static (SongDocument Song, TrackModel Track) RepeatTwoSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        song.Project.Tracks[0].Measures[1].RepeatStart = true;
        song.Project.Tracks[0].Measures[2].RepeatEnd = true;
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        return (song, track);
    }
}

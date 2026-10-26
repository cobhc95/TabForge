namespace TabForge.Models;

// Owns: WithTracks: a shallow copy of the song that plays a given track list (a render of a selection).
// Does not own: the song's own state, which this copy leaves unchanged (SongProject).
// Tests: TabForge.Avalonia.Tests (render snip tests).
public sealed partial class SongProject
{
    /// <summary>A shallow copy that plays <paramref name="tracks"/> instead of this song's (a render of a selection); this song is not changed.</summary>
    public SongProject WithTracks(List<TrackModel> tracks)
    {
        var copy = (SongProject)MemberwiseClone();
        copy.DisplayStateChanged = null;
        copy.TimelineMarked = null;
        copy._songExtentMeasureCache = null;
        copy.Tracks = tracks;
        return copy;
    }
}

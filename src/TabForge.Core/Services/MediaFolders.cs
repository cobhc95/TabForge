namespace TabForge.Services;

// Owns: where a song keeps its recordings and the media collected into it (the folder name only).
// Does not own: copying other files into it (MediaCollector, which collects external media here), the recording itself (TransportRecording, RecordingController).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests.
/// <summary>Where a song keeps its recordings and the media collected into it. A dropped file is linked, never copied here unless the user collects it.</summary>
public static class MediaFolders
{
    /// <summary>The folder under Music for recordings of a song that has not been saved.</summary>
    public const string UnsavedRecordingsFolderName = "TabForge Recordings";

    /// <summary>"&lt;song&gt; Media" beside the saved song, else Music\TabForge Recordings.</summary>
    public static string ForSong(string? songPath)
    {
        if (songPath is { Length: > 0 } path && System.IO.Path.GetDirectoryName(path) is { Length: > 0 } folder)
            return System.IO.Path.Combine(folder, $"{System.IO.Path.GetFileNameWithoutExtension(path)} Media");
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), UnsavedRecordingsFolderName);
    }
}

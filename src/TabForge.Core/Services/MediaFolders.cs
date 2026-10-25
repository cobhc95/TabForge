namespace TabForge.Services;

// Owns: where a song keeps its recordings and kept copies of dropped files.
// Does not own: copying files there (MediaDrop.KeepCopy), the recording itself (TransportRecording, RecordingController).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests.
/// <summary>Where a song keeps its recordings and kept copies of dropped files.</summary>
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

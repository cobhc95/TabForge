using System.IO;

namespace TabForge.Services;

/// <summary>Where a collect puts a song's outside audio: with the song in its own folder, or in a "&lt;song&gt; Media" folder beside the song.</summary>
public enum MediaCollectLayout { SongFolder, MediaBeside }

/// <summary>Where the song file and its collected audio go for the "one song folder" layout.</summary>
/// <param name="SongPath">The song file's new path (inside <paramref name="Folder"/>).</param>
/// <param name="Folder">The song folder.</param>
/// <param name="AudioFolder">The "Audio" folder inside it.</param>
public sealed record MediaSongFolderTarget(string SongPath, string Folder, string AudioFolder);

// Owns: the "Collected audio layout" setting's text, and the paths of the one-song-folder layout (folder named after the song, numbered when taken)
//   and the move of the song file (and its audio data file) into it.
// Does not own: the plan and the copy (MediaCollectPlan, MediaCollector), the prompt (MediaCollectPromptWindow), when a save collects (MediaSavePolicy).
// Tests: TabForge.Avalonia.Tests.MediaSongFolderTests.
/// <summary>The collected-audio layout choices and the song-folder paths. Never deletes: a taken name gets a number, a move only renames.</summary>
public static class MediaLayouts
{
    public const string SongFolderText = "One song folder", MediaBesideText = "Media folder beside";
    public const string AudioFolderName = "Audio";
    public static string[] ChoiceTexts => new[] { SongFolderText, MediaBesideText };

    public static MediaCollectLayout Parse(string? text) =>
        string.Equals(text, MediaBesideText, StringComparison.OrdinalIgnoreCase) ? MediaCollectLayout.MediaBeside : MediaCollectLayout.SongFolder;

    public static string TextOf(MediaCollectLayout layout) => layout == MediaCollectLayout.MediaBeside ? MediaBesideText : SongFolderText;

    /// <summary>
    /// "&lt;folder&gt;/&lt;Song&gt;/&lt;Song&gt;.gp" with "&lt;Song&gt;/Audio" for the audio; a folder or file already named "&lt;Song&gt;" there gets a number ("Song (2)").
    /// A song that already sits in a folder named after it keeps that folder (nothing nests twice).
    /// </summary>
    public static MediaSongFolderTarget SongFolderFor(string songPath)
    {
        var full = Path.GetFullPath(songPath);
        var parent = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);
        if (string.Equals(Path.GetFileName(parent), stem, StringComparison.OrdinalIgnoreCase))
            return new MediaSongFolderTarget(full, parent, Path.Combine(parent, AudioFolderName));
        var folder = Path.Combine(parent, stem);
        for (var n = 2; (Directory.Exists(folder) || File.Exists(folder)) && n < 1000; n++) folder = Path.Combine(parent, $"{stem} ({n})");
        return new MediaSongFolderTarget(Path.Combine(folder, Path.GetFileName(full)), folder, Path.Combine(folder, AudioFolderName));
    }

    /// <summary>
    /// Renames <paramref name="from"/> (and its audio data file, when there is one) to <paramref name="to"/>, making the folder first; nothing is overwritten
    /// or deleted. Returns null on success, else the reason (the song is then where it was).
    /// </summary>
    public static string? MoveSong(string from, string to)
    {
        var folder = Path.GetDirectoryName(to)!;
        var made = !Directory.Exists(folder);
        try
        {
            Directory.CreateDirectory(folder);
            File.Move(from, to, overwrite: false);
            var side = AudioDataFile.PathFor(from);
            if (File.Exists(side))
            {
                try { File.Move(side, AudioDataFile.PathFor(to), overwrite: false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    File.Move(to, from, overwrite: false);   // keep the pair together: the song goes back
                    return ex.Message;
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Trace.Error(Trace.Import, "media collect: move song: " + ex.Message);
            if (made) TryRemoveEmpty(folder);
            return ex.Message;
        }
    }

    // Only the folder this move made and only while it is empty (Directory.Delete without recursion refuses otherwise).
    private static void TryRemoveEmpty(string folder)
    {
        try { Directory.Delete(folder, recursive: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Import, "media collect: remove empty folder: " + ex.Message); }
    }
}

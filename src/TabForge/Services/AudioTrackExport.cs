using TabForge.Models;

namespace TabForge.Services;

// Owns: what the export formats do with audio tracks (skip them) and the notice texts for it.
// Does not own: the exporters themselves.
// Tests: TestAudioTrackExports.
/// <summary>Audio tracks hold no notation, so every notation export skips them; a song with no notation track cannot be written as .gp or MusicXML.</summary>
internal static class AudioTrackExport
{
    internal static int AudioCount(SongProject project)
    {
        var n = 0;
        foreach (var t in project.Tracks) if (t.IsAudio) n++;
        return n;
    }

    /// <summary>The refusal for a format that cannot hold a song without notation tracks.</summary>
    internal static string NoNotationNotice(string format) =>
        $"This song has only audio tracks, so there is no notation to write as {format}. Add an instrument track, or save it as a .tforge project to keep the audio tracks.";

    /// <summary>Throws the clear refusal when the song has no notation track.</summary>
    internal static void RequireNotation(SongProject project, string format)
    {
        foreach (var t in project.Tracks) if (!t.IsAudio) return;
        throw new InvalidOperationException(NoNotationNotice(format));
    }

    /// <summary>The preflight line for a compatible export, or null when the song has no audio track.</summary>
    internal static string? SkippedNotice(SongProject project)
    {
        var n = AudioCount(project);
        if (n == 0) return null;
        return $"Audio tracks ({n}): not written; the file holds the notation tracks only";
    }
}

using System.IO;
using AlphaTab.Model;
using TabForge.Models;

namespace TabForge.Services;

// Owns: saving a copy of a song as .gp3, .gp4 or .gp5: the score is built exactly as for the current format (same rules for transposition, drums, ties and bends) and written by LegacyGpWriter.
// Does not own: the bytes (LegacyGpWriter), the list of what is lost (LegacyGpPreflight) or the dialog and the save flow around them.
// Tests: TestLegacyGpExport.
/// <summary>The file bytes of a legacy-format copy and the report of what the format could not keep.</summary>
public sealed record LegacyGpResult(byte[] Bytes, GpPreflightReport Report);

public static class LegacyGpExport
{
    /// <summary>Builds the copy in memory (nothing is written) and the report of everything it changes or leaves out. Safe on a worker thread.</summary>
    public static LegacyGpResult Build(SongProject project, LegacyGpFormat format)
    {
        AudioTrackExport.RequireNotation(project, "a score file");
        var notation = project.NotationTracks.ToList();
        var extras = new LegacyGpSongExtras(notation.Select(t => t.Reverb).ToList(), notation.Select(t => t.Chorus).ToList());
        var loss = new LegacyGpLossLog();
        var bytes = GuitarProExporter.WithScore(project, score => LegacyGpWriter.Write(score, format, loss, extras));
        return new LegacyGpResult(bytes, LegacyGpPreflight.Report(project, format, loss));
    }

    public static byte[] ToBytes(SongProject project, LegacyGpFormat format) => Build(project, format).Bytes;

    /// <summary>Writes the copy beside nothing else: staged next to the target and swapped in only when complete, so a failure leaves any earlier file as it was.</summary>
    public static GpPreflightReport Save(SongProject project, string path, LegacyGpFormat format)
    {
        path = FilePathPolicy.OutputFile(path, "score file", LegacyGpFormats.Extension(format));
        var result = Build(project, format);
        FilePathPolicy.WriteAtomically(path, stream => stream.Write(result.Bytes));
        return result.Report;
    }
}

public static partial class GuitarProExporter
{
    /// <summary>Builds the finished score model of the song (as the current-format export does) and hands it to <paramref name="use"/> while alphaTab's model lock is held.</summary>
    internal static T WithScore<T>(SongProject project, Func<Score, T> use) =>
        AlphaTabBoundary.WithModelLock(() =>
        {
            var built = Build(project, new List<GpifAutomationWriter.MixPoint>());
            built.Finish(new AlphaTab.Settings());
            return use(built);
        });
}

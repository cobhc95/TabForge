using System.Linq;

namespace TabForge.Services;

// Owns: opening a Guitar Pro 3-5 file that stops in the middle of its bars (a cut-off download), keeping the bars before the cut.
// Does not own: the alphaTab access itself (AlphaTabBoundary) and the conversion to a song (GuitarProImporter).
// Tests: GuitarProTruncatedImportTests.
/// <summary>
/// Repair for a cut-off Guitar Pro 3-5 file. alphaTab throws at the missing bytes and discards everything; the partly built score it
/// leaves behind holds every bar read before the cut, so the bars after the last complete one are removed and the score is finished.
/// </summary>
internal static class GpRepair
{
    /// <param name="Score">The finished score holding only the complete bars.</param>
    /// <param name="Notice">The open notice for the user.</param>
    internal sealed record Truncated(AlphaTab.Model.Score Score, int KeptBars, int DeclaredBars, string Notice);

    /// <summary>True when <paramref name="error"/> is alphaTab running out of bytes (the file ends inside its data).</summary>
    internal static bool IsEndOfData(Exception error) => error.GetBaseException() is AlphaTab.Io.EndOfReaderError;

    /// <summary>The score of the bars <paramref name="data"/> holds completely, or null when none is complete or the file cannot be read this way.</summary>
    internal static Truncated? KeepCompleteBars(byte[] data)
    {
        try
        {
            if (data.AsSpan(0, Math.Min(data.Length, 32)).IndexOf("FICHIER GUITAR PRO v"u8) < 0) return null;   // only Guitar Pro 3-5 files
            lock (AlphaTabBoundary.ModelLock)   // the partial read and the Finish below both draw on alphaTab's static id counters
            {
                if (AlphaTabBoundary.ReadPartial(data) is not { } score || score.Tracks.Count == 0) return null;
                var declared = score.MasterBars.Count;
                var kept = Math.Min(declared, score.Tracks.Min(t => t.Staves.Count == 0 ? 0 : t.Staves.Min(s => s.Bars.Count)));
                if (kept <= 0) return null;
                foreach (var staff in score.Tracks.SelectMany(t => t.Staves))
                {
                    while (staff.Bars.Count > kept) staff.Bars.RemoveAt(staff.Bars.Count - 1);
                    staff.Bars[kept - 1].NextBar = null;
                }
                while (score.MasterBars.Count > kept) score.MasterBars.RemoveAt(score.MasterBars.Count - 1);
                score.MasterBars[kept - 1].NextMasterBar = null;
                score.Finish(AlphaTabBoundary.CreateImportSettings(InputLimits.MaxMeasuresPerTrack));
                var notice = $"this file is cut off: it ends inside bar {kept + 1} of {declared:N0}, so the last {declared - kept:N0} bars are missing. The {kept:N0} bars before the cut were opened.";
                return new Truncated(score, kept, declared, notice);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.Error(Trace.Import, "gp repair: " + ex.Message);
            return null;
        }
    }
}

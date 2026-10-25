using System;
using System.Security.Cryptography;
using System.Text;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Characterization of the score clipboard: the exact clipboard text bytes of a copy, and the exact song state after pasting that text.
/// Other TabForge instances read this text, so the bytes are pinned by hash. The song state is hashed uncompressed: a gzip stream differs
/// between runtimes (the compression library), the content does not.
/// Owns: the copy-bytes and paste-state pins. Does not own: paste rules (TestPasteCommands), timeline sharing (TestTimelineClipsShareClipboard).
/// Tests: TestClipboardCopyPasteBytes.
/// </summary>
public static partial class SelfTest
{
    private const string PinnedBeatsCopyHash = "A55921A9FCD7B4A7062936E75FEA192E8D1ECAEFC6F20421B6C56B0AF2C08A0E";
    private const string PinnedBarsCopyHash = "212485777284AF6B8C02F32357CF8B8E9101D06AF61D17EDED1967642892EE27";
    private const string PinnedBeatsPasteHash = "5D808EE71E9405DA39A64DCA18FE46A36B1D574C4EBC2EDA7820619816628B81";
    private const string PinnedBarsPasteHash = "30124ECD4E157DBEC6AD3DC83C2D22207E5DB5E643FE33B600F3AD8A02729780";

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>The clip song with fixed track ids, so its bytes repeat run to run.</summary>
    private static SongProject PinnedClipSong()
    {
        var song = ClipSong();
        for (var i = 0; i < song.Tracks.Count; i++) song.Tracks[i].Id = new Guid(i + 1, 0, 0, new byte[8]);
        return song;
    }

    private static void TestClipboardCopyPasteBytes()
    {
        var song = PinnedClipSong();
        var beats = ClipboardService.CaptureSelection(song, 0, 0, 0, 0, 0, 7, "pinned-song");
        var bars = ClipboardService.CaptureSelection(song, 0, 0, 0, 0, 1, -1, "pinned-song");
        var beatsText = beats.ToJson();
        var barsText = bars.ToJson();
        var beatsHash = Sha(Encoding.UTF8.GetBytes(beatsText));
        var barsHash = Sha(Encoding.UTF8.GetBytes(barsText));
        Check("beats copy writes the pinned clipboard bytes", beatsHash == PinnedBeatsCopyHash, beatsHash);
        Check("bars copy writes the pinned clipboard bytes", barsHash == PinnedBarsCopyHash, barsHash);

        var beatsTarget = PinnedClipSong();
        var beatsPaste = PasteCommands.Paste(beatsTarget, ScoreClip.Parse(beatsText), new PasteTarget(0, 0, 2, 4), new EditingSettings(), new RecommendedPasteAnswers());
        var beatsState = Sha(ProjectService.GunzipBounded(ProjectService.SnapshotBytes(beatsTarget)));
        Check("beats paste of that text gives the pinned song state", beatsPaste.Changed && beatsState == PinnedBeatsPasteHash, beatsState);

        var barsTarget = PinnedClipSong();
        var barsPaste = PasteCommands.Paste(barsTarget, ScoreClip.Parse(barsText), new PasteTarget(0, 0, 2, 0), new EditingSettings(), new RecommendedPasteAnswers());
        var barsState = Sha(ProjectService.GunzipBounded(ProjectService.SnapshotBytes(barsTarget)));
        Check("bars paste of that text gives the pinned song state", barsPaste.Changed && barsState == PinnedBarsPasteHash, barsState);
    }
}

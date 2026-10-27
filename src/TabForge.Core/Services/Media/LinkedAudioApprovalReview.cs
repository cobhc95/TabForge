using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the data of the "Linked audio" review for one song: the folders waiting for approval, the stored approvals split into this song,
//     other songs and older unsaved-song approvals, and the notice text that opens the Linked audio window. Allow and Revoke change the song's media context.
// Does not own: the window (Views/LinkedAudioReviewWindow), saving and the engine sync after a change (the caller), the approval rules (MediaAccess).
// Tests: TabForge.Avalonia.Tests.LinkedAudioReviewDataTests.
/// <summary>The "Linked audio" review for one song. Allow and Revoke act on that song's media context.</summary>
public sealed class LinkedAudioApprovalReview
{
    /// <summary>The second half of the notice text: what the clips show until their folders are allowed.</summary>
    public const string NotLoadedYetText = "Until then the clips show as not loaded.";

    private readonly MediaContext _media;

    public LinkedAudioApprovalReview(MediaContext media, IReadOnlyList<MediaDecision> waiting, IReadOnlyList<MediaApproval> allowedForThisSong,
        IReadOnlyList<MediaApproval> allowedForOtherSongs, IReadOnlyList<MediaApproval> older)
    {
        _media = media;
        Waiting = waiting;
        AllowedForThisSong = allowedForThisSong;
        AllowedForOtherSongs = allowedForOtherSongs;
        Older = older;
    }

    public IReadOnlyList<MediaDecision> Waiting { get; }
    public IReadOnlyList<MediaApproval> AllowedForThisSong { get; }
    public IReadOnlyList<MediaApproval> AllowedForOtherSongs { get; }
    public IReadOnlyList<MediaApproval> Older { get; }

    /// <summary>The review for a song: its clips' folders not yet allowed, and the stored approvals sorted by the song they belong to.</summary>
    public static LinkedAudioApprovalReview Begin(IEnumerable<AudioClip> clips, MediaContext media, IReadOnlyList<MediaApproval> stored)
    {
        var waiting = MediaAccess.Unapproved(clips, media);
        var here = MediaAccess.ApprovalsOf(media);   // this song's stored approvals (its path) or its session's (unsaved song)
        // Approvals stored with an empty path belong to no song: listed so they can be revoked, never honoured.
        var older = stored.Where(a => string.IsNullOrEmpty(a.Project)).ToList();
        var elsewhere = stored.Where(a => !string.IsNullOrEmpty(a.Project) && !here.Contains(a)).ToList();
        return new LinkedAudioApprovalReview(media, waiting, here, elsewhere, older);
    }

    /// <summary>The notice text for the waiting folders (the notice is shown only when some wait).</summary>
    public static string MediaNoticeText(IReadOnlyList<MediaDecision> waiting) =>
        waiting.Count == 1
            ? waiting[0].Message + " " + NotLoadedYetText
            : $"This song links audio in {waiting.Count} network or removable folders you haven't allowed. " + NotLoadedYetText;

    /// <summary>The notice tooltip: the full path of each waiting folder, one per line.</summary>
    public static string MediaNoticeToolTip(IReadOnlyList<MediaDecision> waiting) => string.Join("\n", waiting.Select(d => d.Verdict.FullPath));

    public void Allow(IEnumerable<MediaDecision> chosen)
    {
        foreach (var decision in chosen) MediaAccess.Approve(decision.Verdict, _media);
    }

    public void Revoke(IEnumerable<MediaApproval> chosen)
    {
        foreach (var approval in chosen) MediaAccess.Revoke(approval, _media);
    }
}

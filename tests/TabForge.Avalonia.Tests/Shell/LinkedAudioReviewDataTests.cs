using System.Linq;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the linked audio review sorts the stored approvals: one with no song path is an older approval, one for another
/// song is listed as allowed for other songs, and nothing is waiting when the song has no linked audio.
/// Does not own: the window (ApprovalReviewWindowsTests) or the approval rules (MediaAccess).
/// Tests: this class.
/// </summary>
public sealed class LinkedAudioReviewDataTests
{
    [Fact]
    public void LinkedAudioReviewSortsStoredApprovalsByTheSongTheyBelongTo()
    {
        var older = new MediaApproval { Folder = @"\nas\audio", Project = "" };
        var other = new MediaApproval { Folder = @"E:\Sounds", Project = @"C:\Songs\other.tab" };

        var review = LinkedAudioApprovalReview.Begin([], MediaContext.Anonymous, [older, other]);

        Assert.Empty(review.Waiting);
        Assert.Equal(new[] { older }, review.Older.ToArray());
        Assert.Equal(new[] { other }, review.AllowedForOtherSongs.ToArray());
    }
}

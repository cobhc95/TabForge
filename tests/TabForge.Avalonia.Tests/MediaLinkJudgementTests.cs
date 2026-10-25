using System.IO;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the media policy's answer for a link that cannot be read (MediaPathPolicy): it is judged as a removable place, so the user approves it first.
/// Does not own: the link reading itself (WindowsPathResolver, full suite) or the approval dialog.
/// Tests: this class.
/// </summary>
public sealed class MediaLinkJudgementTests
{
    /// <summary>A file system whose links cannot be read; nothing else is asked of it.</summary>
    private sealed class UnreadableLinkFileSystem : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => throw new UnresolvedLinkException("test link");
    }

    [Fact]
    public void LinkThatCannotBeReadIsJudgedAsRemovableSoTheUserApprovesIt()
    {
        var saved = MediaPathPolicy.FileSystem;
        try
        {
            MediaPathPolicy.FileSystem = new UnreadableLinkFileSystem();

            var verdict = MediaPathPolicy.Classify(@"C:\Music\link.wav", @"C:\Music");

            Assert.Equal(MediaLocation.Removable, verdict.Location);
            Assert.True(verdict.Remote);
        }
        finally
        {
            MediaPathPolicy.FileSystem = saved;
        }
    }
}

using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the documentation-picture check's one exception: a picture in the versioned folder of an earlier release is kept without a doc naming
///   it; a picture at the top of the folder, in another folder or in the current release's folder still has to be named.
/// Does not own: the scan of the docs (SelfTest.TestDocImagesAreReferenced).
/// Tests: SelfTest.KeptForOlderRelease.
/// </summary>
public class DocPictureRuleTests
{
    [Theory]
    [InlineData("0.8.75/main-window.png", "0.9.1")]
    [InlineData("0.9.0/band-above.jpg", "0.9.1")]
    [InlineData("0.9.0/sub/menu-file.png", "0.9.1-beta")]
    public void APictureInAnEarlierReleasesFolderIsKept(string path, string current) =>
        Assert.True(SelfTest.KeptForOlderRelease(path, current));

    [Theory]
    [InlineData("main-window.png", "0.9.1")]          // the top of the folder: not a release's folder
    [InlineData("0.9.1/main-window.png", "0.9.1")]    // the current release's folder
    [InlineData("1.0/main-window.png", "0.9.1")]      // a later release
    [InlineData("drafts/main-window.png", "0.9.1")]   // not a version
    public void AnyOtherUnnamedPictureStillFails(string path, string current) =>
        Assert.False(SelfTest.KeptForOlderRelease(path, current));
}

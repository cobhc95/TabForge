using System.Net.Http;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the update check's version order and how it reads a reply: which release is offered and that an unusable reply is a failed check.
// Does not own: the request itself (no network here) or the update dialog (UpdateAvailableWindowTests).
public sealed class UpdateServiceTests
{
    private const string Feed = """[{"tag_name":"v0.9.0","draft":false,"prerelease":false},{"tag_name":"v0.8.8","draft":false,"prerelease":false}]""";

    [Theory]
    [InlineData("0.9.0", "0.8.75", true)]
    [InlineData("0.8.8", "0.8.75", false)] // Each part is a whole number: 8 is lower than 75.
    [InlineData("0.8.75", "0.8.8", true)]
    [InlineData("0.9.0", "0.9.0", false)]
    [InlineData("0.9.0", "0.9.0-beta.1", true)]
    public void VersionsCompareEachPartAsAWholeNumber(string candidate, string current, bool newer) =>
        Assert.Equal(newer, UpdateService.IsNewer(candidate, current));

    [Theory]
    [InlineData("0.8.75")]
    [InlineData("0.8.8")]
    public void OlderVersionsAreOfferedTheHighestRelease(string current) =>
        Assert.Equal("0.9.0", UpdateService.NewerRelease(Feed, current)?.Version);

    [Fact]
    public void TheHighestVersionIsNotOfferedAnything() =>
        Assert.Null(UpdateService.NewerRelease(Feed, "0.9.0"));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""[{"tag_name":"nightly","draft":false}]""")]
    public void AReplyWithNoReleaseVersionIsAFailedCheck(string json) =>
        Assert.Throws<HttpRequestException>(() => UpdateService.NewerRelease(json, "0.9.0"));
}

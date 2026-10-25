using System;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the file-name sanitiser (<see cref="SafeFileNames"/>) and the engine clip-path guard (<see cref="ClipPathGuard"/>).
/// Does not own: the media approval policy or any file creation.
/// Tests: this class.
/// </summary>
public sealed class SafePathTests
{
    [Theory]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\x01" + "b")]
    public void InvalidCharsDoNotSurviveSanitising(string raw)
    {
        var clean = SafeFileNames.SafeFileName("x" + raw + "y", "fallback");
        foreach (var c in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
            Assert.DoesNotContain(c.ToString(), clean);
        Assert.True(clean.IndexOfAny(new[] { '\x00', '\x01', '\x1F' }) < 0);
        Assert.NotEmpty(clean);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    [InlineData("con")]
    [InlineData("con.txt")]
    [InlineData("COM1.txt")]
    public void ReservedNamesAreNotReturnedUnchanged(string reserved)
    {
        var clean = SafeFileNames.SafeFileName(reserved, "fallback");
        Assert.NotEqual(reserved, clean);
        Assert.StartsWith("_", clean);
        Assert.Equal("_" + reserved, clean);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void EmptyOrWhitespaceFallsBack(string? raw)
    {
        Assert.Equal("fallback", SafeFileNames.SafeFileName(raw, "fallback"));
    }

    [Fact]
    public void NormalNamePassesThrough()
    {
        Assert.Equal("hello", SafeFileNames.SafeFileName("hello", "fallback"));
    }

    [Theory]
    [InlineData(@"..\x.wav")]
    [InlineData(@"a/../../x.wav")]
    public void ClipGuardRejectsDotDotRelatives(string path)
    {
        Assert.False(ClipPathGuard.IsAllowed(path, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Theory]
    [InlineData(@"C:\Windows\x.wav")]
    [InlineData(@"\\server\share\x.wav")]
    public void ClipGuardAcceptsAbsoluteAudioPaths(string path)
    {
        Assert.True(ClipPathGuard.IsAllowed(path, out var reason), reason);
    }

    [Fact]
    public void ClipGuardRejectsRelativeAudioPath()
    {
        Assert.False(ClipPathGuard.IsAllowed("takes/guitar.wav", out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Theory]
    [InlineData(@"C:\x.txt")]
    [InlineData(@"\\?\C:\x.wav")]
    [InlineData(@"C:\CON.wav")]
    public void ClipGuardRejectsNonAudioDeviceAndReservedNames(string path)
    {
        Assert.False(ClipPathGuard.IsAllowed(path, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }
}

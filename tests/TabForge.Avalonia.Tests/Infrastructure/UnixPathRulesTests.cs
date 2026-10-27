using System;
using TabForge.Audio.Contracts;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the macOS and Linux path rules for clips (engine side) and linked media (UI side), run on any machine because they are string-only.
// Does not own: the Windows rules (MediaPathPolicy, ClipPathGuard on Windows).
// Tests: itself.
public sealed class UnixPathRulesTests
{
    private static MediaVerdict Classify(string? path, string? project = null, bool audio = true, string? link = null) =>
        UnixPathRules.Classify(path, project, audio, _ => link);

    [Theory]
    [InlineData("/Users/u/Music/a.wav", true)]
    [InlineData("/home/u/take 1.FLAC", true)]
    [InlineData("/home/u/a:b.mp3", true)]          // a colon is an ordinary character on Unix
    [InlineData("/home/u/back\\slash.ogg", true)]  // so is a backslash
    [InlineData("music/a.wav", false)]
    [InlineData("C:\\Music\\a.wav", false)]        // a Windows path is not absolute here
    [InlineData("/dev/zero.wav", false)]
    [InlineData("/proc/self/mem.wav", false)]
    [InlineData("/sys/x.wav", false)]
    [InlineData("/home/../dev/x.wav", false)]      // normalised first
    [InlineData("/home/u/notes.txt", false)]
    [InlineData("/home/u.d/noext", false)]
    [InlineData("", false)]
    [InlineData("/home/u/a\n.wav", false)]
    public void TheEngineGuardAcceptsOnlyAbsolutePlainAudioFiles(string path, bool allowed)
    {
        Assert.Equal(allowed, UnixClipPathRules.IsAllowed(path, out var reason));
        Assert.Equal(allowed, reason.Length == 0);
    }

    [Fact]
    public void TheGuardUsesTheUnixRulesOffWindows()
    {
        if (OperatingSystem.IsWindows()) { Assert.True(ClipPathGuard.IsAllowed("C:\\Music\\a.wav", out _)); return; }
        Assert.True(ClipPathGuard.IsAllowed("/home/u/a.wav", out _));
        Assert.False(ClipPathGuard.IsAllowed("/dev/null.wav", out _));
    }

    [Theory]
    [InlineData("/a//b/./c/../d", "/a/b/d")]
    [InlineData("/../../x", "/x")]
    [InlineData("/", "/")]
    [InlineData("/a/b/", "/a/b")]
    public void NormalizeCollapsesDotsAndSlashes(string input, string expected) => Assert.Equal(expected, UnixClipPathRules.Normalize(input));

    [Fact]
    public void AnAbsoluteAudioFileIsLocal()
    {
        var v = Classify("/home/u/music/a.wav");
        Assert.Equal(MediaLocation.Local, v.Location);
        Assert.Equal("/home/u/music/a.wav", v.FullPath);
        Assert.False(v.InProject);
    }

    [Fact]
    public void ARelativePathResolvesInsideTheProjectFolder()
    {
        var v = Classify("clips/a.wav", "/home/u/proj");
        Assert.Equal("/home/u/proj/clips/a.wav", v.FullPath);
        Assert.True(v.InProject);
        Assert.False(v.EscapesProject);
    }

    [Fact]
    public void ARelativePathLeadingOutOfTheProjectIsFlagged()
    {
        var v = Classify("../shared/a.wav", "/home/u/proj");
        Assert.Equal("/home/u/shared/a.wav", v.FullPath);
        Assert.False(v.InProject);
        Assert.True(v.EscapesProject);
    }

    [Fact]
    public void ARelativePathNeedsAProjectFolder()
    {
        Assert.Equal(MediaLocation.Invalid, Classify("a.wav").Location);
        Assert.Equal(MediaLocation.Invalid, Classify("C:\\Music\\a.wav").Location);
    }

    [Theory]
    [InlineData("/Volumes/Backup/a.wav")]
    [InlineData("/media/u/usb/a.wav")]
    [InlineData("/run/media/u/usb/a.wav")]
    [InlineData("/mnt/nas/a.wav")]
    public void MountedVolumesNeedApproval(string path)
    {
        var v = Classify(path);
        Assert.Equal(MediaLocation.Removable, v.Location);
        Assert.True(v.Remote);
    }

    [Theory]
    [InlineData("/dev/sda1.wav")]
    [InlineData("/proc/1/fd/0.wav")]
    public void DeviceTreesAreNeverOpened(string path)
    {
        var v = Classify(path);
        Assert.Equal(MediaLocation.Device, v.Location);
        Assert.True(v.Refused);
    }

    [Fact]
    public void ALinkIsJudgedByWhereItLeads()
    {
        Assert.Equal(MediaLocation.Device, Classify("/home/u/a.wav", link: "/dev/zero").Location);
        var onUsb = Classify("/home/u/a.wav", link: "/Volumes/Usb/real.wav");
        Assert.Equal(MediaLocation.Removable, onUsb.Location);
        Assert.Equal("/Volumes/Usb/real.wav", onUsb.FullPath);
        Assert.Equal("/home/u/b/real.wav", Classify("/home/u/a.wav", link: "/home/u/b/real.wav").FullPath);
    }

    [Fact]
    public void ANonAudioFileIsInvalidUnlessAnyTypeIsAllowed()
    {
        Assert.Equal(MediaLocation.Invalid, Classify("/home/u/a.mid").Location);
        Assert.Equal(MediaLocation.Local, Classify("/home/u/a.mid", audio: false).Location);
    }
}

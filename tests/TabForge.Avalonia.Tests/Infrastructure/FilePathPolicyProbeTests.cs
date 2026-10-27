using System;
using System.IO;
using TabForge.Autosave;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the shared folder checks the recording and recovery paths use: the write probe, the device-path and link
/// refusal, the access-denied hint, and the autosave notice wording for a denied folder.
/// </summary>
public sealed class FilePathPolicyProbeTests
{
    private static string Scratch() => Path.Combine(Path.GetTempPath(), "tf-probe-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProbeOfAWritableFolderPassesAndLeavesNothingBehind()
    {
        var folder = Scratch();
        try
        {
            Assert.True(FilePathPolicy.TryProbeWritableFolder(folder, out var failure));
            Assert.Null(failure);
            Assert.True(Directory.Exists(folder));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ProbeWhereAFolderShouldBeFailsWithoutThrowing()
    {
        var blocked = Path.Combine(Path.GetTempPath(), "tf-probe-blocked-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocked, "a file where the folder should be");
        try
        {
            Assert.False(FilePathPolicy.TryProbeWritableFolder(blocked, out var failure));
            Assert.NotNull(failure);
            Assert.NotEmpty(failure!.Message);
        }
        finally { try { File.Delete(blocked); } catch (IOException) { } }
    }

    [Fact]
    public void DevicePathIsRefusedBeforeAnythingIsCreated()
    {
        var target = @"\\?\C:\tf-probe-device-" + Guid.NewGuid().ToString("N");
        var ex = Assert.Throws<InvalidDataException>(() => FilePathPolicy.ValidateWritableFolder(target, "video folder"));
        Assert.Contains("device path", ex.Message);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void NormalFolderPassesValidationUnchanged()
    {
        var folder = Scratch();
        try
        {
            Directory.CreateDirectory(folder);
            Assert.Equal(Path.GetFullPath(folder), FilePathPolicy.ValidateWritableFolder(folder, "recording folder"));
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ALinkLeafIsRefused()
    {
        var folder = Scratch();
        var link = folder + "-link";
        try
        {
            Directory.CreateDirectory(folder);
            try { Directory.CreateSymbolicLink(link, folder); }
            catch (Exception linkEx) when (linkEx is IOException or UnauthorizedAccessException) { return; }   // no right to create links here: nothing to prove
            var ex = Assert.Throws<InvalidDataException>(() => FilePathPolicy.ValidateWritableFolder(link, "video folder"));
            Assert.Contains("link", ex.Message);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            try { Directory.Delete(link); } catch (IOException) { }
        }
    }

    [Fact]
    public void AccessDeniedIsRecognisedThroughInnerErrors()
    {
        Assert.True(FilePathPolicy.IsAccessDenied(new UnauthorizedAccessException("denied")));
        Assert.True(FilePathPolicy.IsAccessDenied(new IOException("wrapped", new UnauthorizedAccessException("denied"))));
        Assert.False(FilePathPolicy.IsAccessDenied(new IOException("disk full")));
        Assert.NotEmpty(FilePathPolicy.AccessDeniedHint);
    }

    [Fact]
    public void DeniedRecoveryFolderNoticeNamesTheAccessHint()
    {
        var health = new AutosaveHealth();
        health.Failed(DateTime.UtcNow, new UnauthorizedAccessException("Access to the path is denied."));
        var text = health.Describe(DateTime.UtcNow);
        Assert.True(text is not null && text.Contains("Controlled folder access") && text.Contains("previous copy is kept"));
    }

    [Fact]
    public void RetryProbeDetailIsReportedInTheNotice()
    {
        var health = new AutosaveHealth();
        health.Failed(DateTime.UtcNow, new IOException("a file where the Recovery folder should be"), "C:\\Recovery", "a file where the Recovery folder should be");
        Assert.Equal("C:\\Recovery", health.Folder);
        var text = health.Describe(DateTime.UtcNow);
        Assert.True(text is not null && text.Contains("Folder check:"));
        health.Succeeded(DateTime.UtcNow);
        Assert.Null(health.Describe(DateTime.UtcNow));
        Assert.Null(health.ProbeDetail);
    }
}

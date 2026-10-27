using System;
using System.IO;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: self-test runs stay off the user's real profile (no --profile gives a temp one) and the "all transports taken" notice is logged once per sharing owner.
/// Does not own: the owner id allocation itself (AudioSmokeTests).
/// Tests: TabForge.Avalonia.Tests.SelfTestProfileOwnerIdTests.
/// </summary>
[Collection("UserPaths")]
public class SelfTestProfileOwnerIdTests
{
    [Fact]
    public void SelfTestProfile_WithoutProfileArgument_IsNotTheRealLocalFolder()
    {
        var before = UserPaths.ProfileRoot;
        try
        {
            UserPaths.SetProfile(null);
            using var profile = UserPaths.ApplySelfTestProfile(new[] { "--selftest", "x.log" });
            var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabForge");
            Assert.True(UserPaths.IsProfile);
            Assert.False(UserPaths.Diagnostics.StartsWith(real, StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith(Path.GetTempPath(), UserPaths.Diagnostics, StringComparison.OrdinalIgnoreCase);
        }
        finally { UserPaths.SetProfile(before); }
    }

    [Fact]
    public void SelfTestProfile_ExplicitProfileArgument_IsKept()
    {
        var before = UserPaths.ProfileRoot;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "tf-explicit-profile");
            UserPaths.SetProfile(null);
            using var profile = UserPaths.ApplySelfTestProfile(new[] { "--selftest", "x.log", "--profile", dir });
            Assert.Equal(Path.GetFullPath(dir), UserPaths.ProfileRoot);
        }
        finally { UserPaths.SetProfile(before); }
    }

    [Fact]
    public void SelfTestProfile_AutoFolder_IsDeletedAtEnd()
    {
        var before = UserPaths.ProfileRoot;
        string? folder = null;
        try
        {
            UserPaths.SetProfile(null);
            using var profile = UserPaths.ApplySelfTestProfile(new[] { "--selftest", "x.log" });
            folder = UserPaths.ProfileRoot;
            Assert.NotNull(folder);
            Directory.CreateDirectory(UserPaths.Diagnostics);
            Assert.True(Directory.Exists(folder));
            profile.Dispose();
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            UserPaths.SetProfile(before);
        }
    }

    [Fact]
    public void SelfTestProfile_ExplicitProfileFolder_IsNotDeletedAtEnd()
    {
        var before = UserPaths.ProfileRoot;
        var dir = Path.Combine(Path.GetTempPath(), "tf-explicit-keep-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "keep.txt"), "x");
            UserPaths.SetProfile(null);
            using var profile = UserPaths.ApplySelfTestProfile(new[] { "--selftest", "x.log", "--profile", dir });
            profile.Dispose();
            Assert.True(File.Exists(Path.Combine(dir, "keep.txt")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            UserPaths.SetProfile(before);
        }
    }

    [Fact]
    public void OwnerIdSharingNotice_IsTakenOncePerSharingOwner()
    {
        var ids = new SongOwnerIds();
        var holders = new object[SongOwners.Max];
        for (var i = 0; i < holders.Length; i++) { holders[i] = new object(); ids.IdOf(holders[i]); }
        var a = new object();
        var b = new object();
        Assert.False(ids.TakeSharingNotice(a));   // not sharing yet
        ids.IdOf(a); ids.IdOf(b);
        Assert.True(ids.TakeSharingNotice(a));
        Assert.False(ids.TakeSharingNotice(a));
        Assert.True(ids.TakeSharingNotice(b));
        Assert.False(ids.TakeSharingNotice(holders[0]));
        GC.KeepAlive(holders);
    }
}

[CollectionDefinition("UserPaths", DisableParallelization = true)]
public sealed class UserPathsCollection { }

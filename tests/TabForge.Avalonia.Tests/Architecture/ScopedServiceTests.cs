using System;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the scoped services behind the static facades (ClipClipboard, MediaPathPolicy, UserTemplates): two services keep their own state.
/// Does not own: the facades' process-wide defaults (the feature tests) or the settings store.
/// Tests: this class.
/// </summary>
public sealed class ScopedServiceTests
{
    private sealed class FixedFileSystem(DriveType driveType) : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => driveType;
        public string? FinalPathOf(string fullPath) => fullPath;
    }

    private sealed class Folders : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tabforge-scoped-" + Guid.NewGuid().ToString("N"));
        public Folders() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void FacadesDelegateToTheScopedService()
    {
        var clips = new ClipClipboardService();
        using (ClipClipboard.Use(clips))
        {
            ClipClipboard.Copy(new AudioClip { Name = "Chorus" });
            Assert.True(ClipClipboard.HasClip);
            Assert.Equal("Chorus", ClipClipboard.Name);
        }

        using var folder = new Folders();
        var templates = new UserTemplatesStore { FolderOverride = folder.Path };
        using (UserTemplates.Use(templates)) Assert.Equal(folder.Path, UserTemplates.Folder);

        var media = new MediaPathPolicyService(new FixedFileSystem(DriveType.Fixed));
        using (MediaPathPolicy.Use(media)) Assert.Same(media.FileSystem, MediaPathPolicy.FileSystem);
    }

    [Fact]
    public void SettingsStoreFacadeUsesTheScopedStore()
    {
        using var folder = new Folders();
        using var store = AppSettingsStore.Open(Path.Combine(folder.Path, "settings.json"), TimeSpan.Zero);
        using (AppSettingsStore.OverrideSharedForTest(store))
            Assert.Same(store, AppSettingsStore.Shared);
    }

    [Fact]
    public void TwoClipboardServicesDoNotShareTheCopiedClip()
    {
        var one = new ClipClipboardService();
        var two = new ClipClipboardService();

        one.Copy(new AudioClip { Name = "Intro" });

        Assert.True(one.HasClip);
        Assert.Equal("Intro", one.Name);
        Assert.False(two.HasClip);
        Assert.Null(two.Clone());
    }

    [Fact]
    public void TwoTemplateStoresListTheirOwnFolder()
    {
        using var first = new Folders();
        using var second = new Folders();
        File.WriteAllText(System.IO.Path.Combine(first.Path, "Mine" + UserTemplates.Extension), "{}");
        var one = new UserTemplatesStore { FolderOverride = first.Path };
        var two = new UserTemplatesStore { FolderOverride = second.Path };

        Assert.Equal(new[] { "Mine" }, one.List());
        Assert.Empty(two.List());
        Assert.Equal(second.Path, two.Folder);
    }

    [Fact]
    public void TwoMediaPathServicesKeepTheirOwnFileSystem()
    {
        if (!OperatingSystem.IsWindows()) return;
        var network = new MediaPathPolicyService(new FixedFileSystem(DriveType.Network));
        var local = new MediaPathPolicyService(new FixedFileSystem(DriveType.Fixed));

        Assert.Equal(MediaLocation.Network, network.Classify(@"C:\Music\take.wav", null).Location);
        Assert.Equal(MediaLocation.Local, local.Classify(@"C:\Music\take.wav", null).Location);
    }
}

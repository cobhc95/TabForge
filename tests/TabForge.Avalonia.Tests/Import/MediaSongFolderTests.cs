using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the paths and names of the one-song-folder layout and the layout setting text (MediaLayouts).</summary>
public sealed class MediaLayoutsTests
{
    [Fact]
    public void SettingTextRoundTripsAndDefaultsToTheSongFolder()
    {
        Assert.Equal(MediaCollectLayout.SongFolder, MediaLayouts.Parse(null));
        Assert.Equal(MediaCollectLayout.SongFolder, MediaLayouts.Parse("nonsense"));
        Assert.Equal(MediaCollectLayout.MediaBeside, MediaLayouts.Parse(MediaLayouts.TextOf(MediaCollectLayout.MediaBeside)));
        Assert.Equal(MediaCollectLayout.SongFolder, MediaLayouts.Parse(MediaLayouts.TextOf(MediaCollectLayout.SongFolder)));
        Assert.Equal(new[] { "One song folder", "Media folder beside" }, MediaLayouts.ChoiceTexts);
    }

    [Fact]
    public void SongFolderIsNamedAfterTheSongAndAChosenNameThatExistsGetsANumber()
    {
        using var tmp = new MediaTestFolder();
        var dir = tmp.Dir("music");
        var plain = MediaLayouts.SongFolderFor(Path.Combine(dir, "Riff.gp"));
        Assert.Equal(Path.Combine(dir, "Riff", "Riff.gp"), plain.SongPath);
        Assert.Equal(Path.Combine(dir, "Riff", "Audio"), plain.AudioFolder);

        tmp.Dir(Path.Combine("music", "Riff"));
        tmp.File(dir, "Riff (2)", "a file with the next name");
        var numbered = MediaLayouts.SongFolderFor(Path.Combine(dir, "Riff.gp"));
        Assert.Equal(Path.Combine(dir, "Riff (3)", "Riff.gp"), numbered.SongPath);
    }

    [Fact]
    public void ASongAlreadyInAFolderOfItsNameKeepsThatFolder()
    {
        using var tmp = new MediaTestFolder();
        var folder = tmp.Dir(Path.Combine("music", "Riff"));
        var target = MediaLayouts.SongFolderFor(Path.Combine(folder, "Riff.gp"));
        Assert.Equal(Path.Combine(folder, "Riff.gp"), target.SongPath);
        Assert.Equal(Path.Combine(folder, "Audio"), target.AudioFolder);
    }
}

/// <summary>A notice sink that remembers the category, like the top bar's.</summary>
internal sealed class RecordedNotices : INoticeReceiver
{
    public List<(string Text, NoticeCategory Category)> Items { get; } = new();
    public void Receive(string text, NoticeCategory category) => Items.Add((text, category));
    public void Plain(string text) => Receive(text, NoticeCategory.Info);
    public bool HasError(string part) => Items.Any(i => i.Category == NoticeCategory.Error && i.Text.Contains(part, StringComparison.Ordinal));
}

/// <summary>Owns: the save with the one-song-folder layout (MediaSaveStep + MainWindowSaving): the folder, the move, the audio in Audio, relative paths, fallback.</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaSongFolderSaveTests
{
    private sealed class SaveTo : IFileDialogs
    {
        private readonly string _path;
        public SaveTo(string path) => _path = path;
        public string? SaveFile(FileDialogOptions options, object? owner) => _path;
        public string? OpenFile(FileDialogOptions options, object? owner) => null;
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => null;
        public string? PickFolder(FileDialogOptions options, object? owner) => null;
        public string[]? PickFolders(FileDialogOptions options, object? owner) => null;
    }

    private sealed class Rig
    {
        public int Asked;
        public MediaChoiceAnswer Answer = new(Copy: true, Remember: false, Layout: MediaCollectLayout.SongFolder);
        public ExternalMediaChoice Setting = ExternalMediaChoice.Ask;
        public MediaCollectLayout Layout = MediaCollectLayout.SongFolder;
        public RecordedNotices Notices { get; } = new();
        public MediaSavePolicy Policy { get; } = new();

        public MediaSaveStep Step() => new(Policy, () => Setting, choice => Setting = choice,
            (_, _, _) => { Asked++; return Task.FromResult(Answer); }, plan => MediaCollector.RunAsync(plan, null, CancellationToken.None), _ => { },
            Notices.Plain, () => Layout, layout => Layout = layout);

        public MainWindowSaving Saving(string target) => new(new SaveTo(target), null, Notices.Plain, SongPluginStates.ForEngine(null, () => null), () => "tforge",
            _ => throw new InvalidOperationException("no question expected"), () => new DegradedSaveGate(), Step());
    }

    // A song saved once at <dir>/<name>.tforge (no outside audio yet), then given an outside clip: the next save in place asks.
    private static async Task<(SongDocument Document, string Path, string Source)> SavedSongWithOutsideAudio(MediaTestFolder tmp, Rig rig, string name = "song")
    {
        var dir = tmp.Dir("music");
        var path = Path.Combine(dir, name + ".tforge");
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song() };
        rig.Policy.Observe(document);
        rig.Setting = ExternalMediaChoice.NeverCopy;
        Assert.True(await rig.Saving(path).SaveAs(document));
        rig.Setting = ExternalMediaChoice.Ask;
        document.Project.Tracks[0].AudioClips.Add(new AudioClip { File = source, Name = "take.wav", SourceLengthSec = 1, FileLengthSec = 1 });
        document.Project.IsDirty = true;
        return (document, path, source);
    }

    [Fact]
    public async Task OneSongFolderMovesTheSongCopiesAudioIntoAudioAndRelativePathsResolveAfterReopen()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig();
        var (document, oldPath, source) = await SavedSongWithOutsideAudio(tmp, rig);
        var neighbour = tmp.File(Path.GetDirectoryName(oldPath)!, "notes.txt", "keep me");

        Assert.True(await rig.Saving(oldPath).Save(document));

        var folder = Path.Combine(tmp.Root, "music", "song");
        var newPath = Path.Combine(folder, "song.tforge");
        Assert.Equal(1, rig.Asked);
        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(oldPath));                                   // moved, not copied
        Assert.True(File.Exists(neighbour));                                  // nothing else touched
        Assert.Equal("audio bytes", File.ReadAllText(Path.Combine(folder, "Audio", "take.wav")));
        Assert.Equal("audio bytes", File.ReadAllText(source));                // the original is unchanged
        Assert.Equal(newPath, document.Path);
        Assert.False(document.HasUnsavedChanges);
        var reopened = ProjectService.Load(newPath, InputLimits.MaxTforgeFileBytes).Tracks[0].AudioClips[0].File;
        Assert.Equal(Path.Combine("Audio", "take.wav"), reopened);
        Assert.True(File.Exists(MediaStoredPaths.Resolve(reopened, folder)));
        Assert.DoesNotContain(rig.Notices.Items, i => i.Category == NoticeCategory.Error);
    }

    [Fact]
    public async Task ATakenFolderNameGetsANumberAndItsContentsAreLeftAlone()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig();
        var (document, oldPath, _) = await SavedSongWithOutsideAudio(tmp, rig);
        var taken = tmp.File(tmp.Dir(Path.Combine("music", "song")), "other.txt", "someone else");

        Assert.True(await rig.Saving(oldPath).Save(document));

        Assert.True(File.Exists(taken));
        Assert.True(File.Exists(Path.Combine(tmp.Root, "music", "song (2)", "song.tforge")));
        Assert.True(File.Exists(Path.Combine(tmp.Root, "music", "song (2)", "Audio", "take.wav")));
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public async Task FirstSaveOfANewSongCreatesTheFolderWithoutMovingAnything()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig();
        rig.Policy.Observe(document);

        Assert.True(await rig.Saving(Path.Combine(tmp.Dir("music"), "fresh.tforge")).SaveAs(document));

        var folder = Path.Combine(tmp.Root, "music", "fresh");
        Assert.True(File.Exists(Path.Combine(folder, "fresh.tforge")));
        Assert.True(File.Exists(Path.Combine(folder, "Audio", "take.wav")));
        Assert.False(File.Exists(Path.Combine(tmp.Root, "music", "fresh.tforge")));
        Assert.Equal(Path.Combine(folder, "fresh.tforge"), document.Path);
    }

    [Fact]
    public async Task MediaFolderBesideLeavesTheSongWhereItIsAsBefore()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig { Answer = new MediaChoiceAnswer(Copy: true, Remember: false, Layout: MediaCollectLayout.MediaBeside) };
        var (document, oldPath, _) = await SavedSongWithOutsideAudio(tmp, rig);

        Assert.True(await rig.Saving(oldPath).Save(document));

        Assert.True(File.Exists(oldPath));
        Assert.Equal(oldPath, document.Path);
        Assert.True(File.Exists(Path.Combine(tmp.Root, "music", "song Media", "take.wav")));
        Assert.False(Directory.Exists(Path.Combine(tmp.Root, "music", "song")));
        Assert.Equal(Path.Combine("song Media", "take.wav"), ProjectService.Load(oldPath, InputLimits.MaxTforgeFileBytes).Tracks[0].AudioClips[0].File);
    }

    [Fact]
    public async Task RememberStoresBothTheCopyChoiceAndTheLayout()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig { Answer = new MediaChoiceAnswer(Copy: true, Remember: true, Layout: MediaCollectLayout.MediaBeside) };
        var (document, oldPath, _) = await SavedSongWithOutsideAudio(tmp, rig);

        Assert.True(await rig.Saving(oldPath).Save(document));

        Assert.Equal(ExternalMediaChoice.AlwaysCopy, rig.Setting);
        Assert.Equal(MediaCollectLayout.MediaBeside, rig.Layout);
    }

    [Fact]
    public async Task KeepLinksWithRememberStoresNeverCopyAndTheLayoutAndMovesNothing()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig { Answer = new MediaChoiceAnswer(Copy: false, Remember: true, Layout: MediaCollectLayout.SongFolder) };
        var (document, oldPath, source) = await SavedSongWithOutsideAudio(tmp, rig);
        rig.Layout = MediaCollectLayout.MediaBeside;

        Assert.True(await rig.Saving(oldPath).Save(document));

        Assert.Equal(ExternalMediaChoice.NeverCopy, rig.Setting);
        Assert.Equal(MediaCollectLayout.SongFolder, rig.Layout);
        Assert.True(File.Exists(oldPath));
        Assert.Equal(source, document.Project.Tracks[0].AudioClips[0].File);
    }

    [Fact]
    public async Task AlwaysCopyFollowsTheLayoutSettingWithoutAsking()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig();
        var (document, oldPath, _) = await SavedSongWithOutsideAudio(tmp, rig);
        rig.Setting = ExternalMediaChoice.AlwaysCopy;

        Assert.True(await rig.Saving(oldPath).Save(document));

        Assert.Equal(0, rig.Asked);
        Assert.True(File.Exists(Path.Combine(tmp.Root, "music", "song", "Audio", "take.wav")));
    }

    [Fact]
    public async Task ASaveInAFolderOfTheSongNameDoesNotNestAnotherFolder()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig();
        rig.Policy.Observe(document);
        var target = Path.Combine(tmp.Dir(Path.Combine("music", "song")), "song.tforge");

        Assert.True(await rig.Saving(target).SaveAs(document));

        Assert.True(File.Exists(target));
        Assert.True(File.Exists(Path.Combine(tmp.Root, "music", "song", "Audio", "take.wav")));
        Assert.False(Directory.Exists(Path.Combine(tmp.Root, "music", "song", "song")));
    }

    [Fact]
    public async Task AMoveThatFailsFallsBackToTheMediaFolderBesideAndSaysSoAsAnError()
    {
        if (!OperatingSystem.IsWindows()) return;   // an open file blocks a rename only on Windows
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var rig = new Rig();
        var (document, oldPath, _) = await SavedSongWithOutsideAudio(tmp, rig);

        string? written;
        using (new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.Read))   // no Delete share: the rename is refused
            written = await rig.Step().BeforeSave(document, oldPath);

        Assert.Equal(oldPath, written);
        Assert.True(File.Exists(oldPath));
        Assert.Equal(oldPath, document.Path);
        Assert.True(rig.Notices.HasError("own folder"));
        Assert.True(File.Exists(Path.Combine(tmp.Root, "music", "song Media", "take.wav")));
        Assert.False(Directory.Exists(Path.Combine(tmp.Root, "music", "song")));
    }
}

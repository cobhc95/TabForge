using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>A local-drive file system for the media policy (no links), restored on dispose; the tests that classify paths use it.</summary>
internal sealed class LocalMediaFileSystem : IMediaFileSystem, IDisposable
{
    private readonly IMediaFileSystem _saved = MediaPathPolicy.FileSystem;
    public LocalMediaFileSystem() => MediaPathPolicy.FileSystem = this;
    public DriveType DriveTypeOf(string root) => DriveType.Fixed;
    public string? FinalPathOf(string fullPath) => fullPath;
    public void Dispose() => MediaPathPolicy.FileSystem = _saved;
}

/// <summary>Shared builders for the media tests: a temp folder, files, a song with clips.</summary>
internal sealed class MediaTestFolder : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "claude", $"tf-media-{Guid.NewGuid():N}");
    public MediaTestFolder() => Directory.CreateDirectory(Root);
    public string Dir(string name) { var d = Path.Combine(Root, name); Directory.CreateDirectory(d); return d; }
    public string File(string dir, string name, string content)
    {
        var p = Path.Combine(dir, name);
        System.IO.File.WriteAllText(p, content);
        return p;
    }
    public static SongProject Song(params string[] files)
    {
        var project = new SongProject { Title = "Media" };
        var track = new TrackModel { Name = "Audio", Measures = TemplateFactory.Measures(1) };
        foreach (var f in files) track.AudioClips.Add(new AudioClip { File = f, Name = Path.GetFileName(f), SourceLengthSec = 1, FileLengthSec = 1 });
        project.Tracks.Add(track);
        return project;
    }
    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}

/// <summary>Owns: relative storage of clip paths (MediaStoredPaths): inside the song folder written relative, outside absolute, restored after the write.</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaStoredPathsTests
{
    [Fact]
    public void PathInsideTheSongFolderIsStoredRelativeAndOutsideStaysAbsolute()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var inside = tmp.File(tmp.Dir(Path.Combine("song", "Song Media")), "take.wav", "a");
        var outside = tmp.File(tmp.Dir("other"), "loop.wav", "b");
        var project = MediaTestFolder.Song(inside, outside);

        using (var scope = MediaStoredPaths.Apply(project, null, song))
        {
            Assert.Equal(Path.Combine("Song Media", "take.wav"), project.Tracks[0].AudioClips[0].File);
            Assert.Equal(outside, project.Tracks[0].AudioClips[1].File);
            Assert.True(scope.Changed);
        }
        Assert.Equal(inside, project.Tracks[0].AudioClips[0].File);   // restored: the live song is untouched
    }

    [Fact]
    public void RelativePathIsResolvedAgainstTheOldFolderWhenTheSongMoves()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var oldFolder = tmp.Dir("old");
        var file = tmp.File(tmp.Dir(Path.Combine("old", "Old Media")), "a.wav", "a");
        var project = MediaTestFolder.Song(Path.Combine("Old Media", "a.wav"));
        var newFolder = tmp.Dir("new");

        using var scope = MediaStoredPaths.Apply(project, oldFolder, newFolder);

        Assert.Equal(file, project.Tracks[0].AudioClips[0].File);   // outside the new folder: absolute, still the same file
    }

    [Fact]
    public void ResolveJoinsRelativePathsToTheBaseAndRefusesDeviceForms()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "x", "a.wav")), MediaStoredPaths.Resolve(Path.Combine("x", "a.wav"), Path.GetTempPath()));
        Assert.Null(MediaStoredPaths.Resolve(Path.Combine("x", "a.wav"), null));
        Assert.Null(MediaStoredPaths.Resolve("//./pipe/x", Path.GetTempPath()));
    }
}

/// <summary>Owns: the collect plan and its copy (MediaCollectPlan, MediaCollector): only external files, reuse of identical names, numbering of different ones, no overwrite.</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaCollectPlanTests
{
    private static MediaContext ContextFor(string songFile)
    {
        var context = new MediaContext(() => null);
        context.SetSavedPath(songFile);
        return context;
    }

    [Fact]
    public void OnlyFilesOutsideTheSongFolderAreExternal()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var inside = tmp.File(song, "in.wav", "1");
        var outside = tmp.File(tmp.Dir("other"), "out.wav", "2");
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(inside, outside), ContextFor(Path.Combine(song, "s.tforge")), song, Path.Combine(song, "s Media"));

        var item = Assert.Single(plan.Items);
        Assert.Equal(outside, item.Source);
        Assert.Equal(Path.Combine(song, "s Media", "out.wav"), item.Target);
        Assert.False(item.Reuse);
    }

    [Fact]
    public void MissingFilesAreSkippedNotPlanned()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var gone = Path.Combine(tmp.Dir("other"), "gone.wav");
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(gone), ContextFor(Path.Combine(song, "s.tforge")), song, Path.Combine(song, "s Media"));

        Assert.Empty(plan.Items);
        Assert.Equal("missing", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void SameNameDifferentContentIsNumberedAndIdenticalContentIsReused()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var media = tmp.Dir(Path.Combine("song", "s Media"));
        tmp.File(media, "riff.wav", "already here");
        var same = tmp.File(tmp.Dir("a"), "riff.wav", "already here");     // identical to the one in the media folder
        var different = tmp.File(tmp.Dir("b"), "riff.wav", "another riff"); // same name, other content
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(same, different), ContextFor(Path.Combine(song, "s.tforge")), song, media);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal((Path.Combine(media, "riff.wav"), true), (plan.Items[0].Target, plan.Items[0].Reuse));
        Assert.Equal((Path.Combine(media, "riff (2).wav"), false), (plan.Items[1].Target, plan.Items[1].Reuse));
    }

    [Fact]
    public void TwoDifferentSourcesWithOneNameGetDifferentTargets()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var a = tmp.File(tmp.Dir("a"), "kick.wav", "kick A");
        var b = tmp.File(tmp.Dir("b"), "kick.wav", "kick B");
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(a, b), ContextFor(Path.Combine(song, "s.tforge")), song, Path.Combine(song, "s Media"));

        Assert.Equal(2, plan.Items.Select(i => i.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task CopyKeepsOriginalsNeverOverwritesAndLeavesNoTempFiles()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var media = Path.Combine(song, "s Media");
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(source), ContextFor(Path.Combine(song, "s.tforge")), song, media);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(media).FullName, "take.wav"), "someone else's file, created after planning");   // appears meanwhile

        var result = await MediaCollector.RunAsync(plan, null, CancellationToken.None);

        Assert.Empty(result.Done);                                                      // the rename refused to overwrite
        Assert.Equal("someone else's file, created after planning", File.ReadAllText(Path.Combine(media, "take.wav")));
        Assert.True(File.Exists(source));                                               // the original is untouched
        Assert.DoesNotContain(Directory.GetFiles(media), f => f.EndsWith(".tmp"));         // the temp copy is gone
    }

    [Fact]
    public async Task CopySucceedsAndReportsTheMapping()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var media = Path.Combine(song, "s Media");
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(source), ContextFor(Path.Combine(song, "s.tforge")), song, media);
        var seen = new List<MediaCopyProgress>();

        var result = await MediaCollector.RunAsync(plan, new SyncProgress(seen), CancellationToken.None);

        Assert.Equal(Path.Combine(media, "take.wav"), result.Done[source]);
        Assert.Equal("audio bytes", File.ReadAllText(Path.Combine(media, "take.wav")));
        Assert.NotEmpty(seen);
    }

    [Fact]
    public async Task CancelBeforeStartCopiesNothingAndLeavesTheClipsLinked()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var plan = MediaCollectPlan.Build(MediaTestFolder.Song(source), ContextFor(Path.Combine(song, "s.tforge")), song, Path.Combine(song, "s Media"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var result = await MediaCollector.RunAsync(plan, null, cancel.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(result.Done);
    }

    [Fact]
    public void ApplyChangesOnlyTheClipsThatWereCopiedAsOneChange()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var project = MediaTestFolder.Song(@"C:\x\a.wav", @"C:\x\b.wav");
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"C:\x\a.wav"] = @"C:\song\s Media\a.wav" };

        Assert.True(MediaCollectApply.WouldChange(project, moved, null, null));
        Assert.Equal(1, MediaCollectApply.Apply(project, moved, null, null));
        Assert.Equal(@"C:\song\s Media\a.wav", project.Tracks[0].AudioClips[0].File);
        Assert.Equal(@"C:\x\b.wav", project.Tracks[0].AudioClips[1].File);
        Assert.False(MediaCollectApply.WouldChange(project, moved, null, null));
    }

    private sealed class SyncProgress : IProgress<MediaCopyProgress>
    {
        private readonly List<MediaCopyProgress> _into;
        public SyncProgress(List<MediaCopyProgress> into) => _into = into;
        public void Report(MediaCopyProgress value) { lock (_into) _into.Add(value); }
    }
}

/// <summary>Owns: when a save asks about external media (MediaSavePolicy): first save, Save As and newly added files; the setting's Always and Never.</summary>
public sealed class MediaSavePolicyTests
{
    [Theory]
    [InlineData(ExternalMediaChoice.Ask, 0, true, true, MediaSaveAction.Nothing)]       // nothing external: never asks
    [InlineData(ExternalMediaChoice.Ask, 2, true, false, MediaSaveAction.Ask)]          // first save or Save As
    [InlineData(ExternalMediaChoice.Ask, 2, false, true, MediaSaveAction.Ask)]          // a file was added since the last save
    [InlineData(ExternalMediaChoice.Ask, 2, false, false, MediaSaveAction.Nothing)]     // an ordinary save of a known song
    [InlineData(ExternalMediaChoice.AlwaysCopy, 2, false, false, MediaSaveAction.Copy)]
    [InlineData(ExternalMediaChoice.AlwaysCopy, 0, true, true, MediaSaveAction.Nothing)]
    [InlineData(ExternalMediaChoice.NeverCopy, 2, true, true, MediaSaveAction.Nothing)]
    public void DecideFollowsTheTriggerRules(ExternalMediaChoice choice, int external, bool firstOrAs, bool added, MediaSaveAction expected) =>
        Assert.Equal(expected, MediaSavePolicy.Decide(choice, external, firstOrAs, added));

    [Fact]
    public void SettingTextRoundTripsAndUnknownTextMeansAsk()
    {
        foreach (var choice in Enum.GetValues<ExternalMediaChoice>()) Assert.Equal(choice, MediaSavePolicy.Parse(MediaSavePolicy.TextOf(choice)));
        Assert.Equal(ExternalMediaChoice.Ask, MediaSavePolicy.Parse("nonsense"));
        Assert.Equal(ExternalMediaChoice.Ask, MediaSavePolicy.Parse(null));
    }

    [Fact]
    public void OnlyAFileAddedAfterTheLastSaveCountsAsNew()
    {
        var known = Path.Combine(Path.GetTempPath(), "known.wav");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(known) };
        var policy = new MediaSavePolicy();
        policy.Observe(document);

        Assert.False(policy.HasNewFile(document, new[] { Path.GetFullPath(known) }));
        Assert.True(policy.HasNewFile(document, new[] { Path.GetFullPath(known), Path.Combine(Path.GetTempPath(), "added.wav") }));

        policy.Saved(document);   // saved with the added file kept linked: it is known now
        document.Project.Tracks[0].AudioClips.Add(new AudioClip { File = Path.Combine(Path.GetTempPath(), "added.wav"), SourceLengthSec = 1 });
        policy.Saved(document);
        Assert.False(policy.HasNewFile(document, new[] { Path.Combine(Path.GetTempPath(), "added.wav") }));
    }
}

/// <summary>Owns: the missing-media detection and the by-name folder search (MediaRelink).</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaRelinkTests
{
    [Fact]
    public void FindMissingListsOnlyFilesThatAreNotThere()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var song = tmp.Dir("song");
        var present = tmp.File(song, "here.wav", "x");
        var gone = Path.Combine(song, "gone.wav");
        var context = new MediaContext(() => null);
        context.SetSavedPath(Path.Combine(song, "s.tforge"));

        var missing = MediaRelink.FindMissing(MediaTestFolder.Song(present, gone), context);

        Assert.Equal(gone, Assert.Single(missing).FullPath);
    }

    [Fact]
    public void SearchFindsEveryMissingFileByNameAndPrefersTheNearestMatch()
    {
        using var tmp = new MediaTestFolder();
        var root = tmp.Dir("moved");
        var deep = tmp.File(tmp.Dir(Path.Combine("moved", "a", "b")), "riff.wav", "deep");
        var near = tmp.File(root, "RIFF.wav", "near");
        var other = tmp.File(tmp.Dir(Path.Combine("moved", "c")), "click.wav", "c");
        var missing = new[]
        {
            new MissingMedia(Path.Combine("Z:", "old", "riff.wav"), "riff.wav", "Z:"),
            new MissingMedia(Path.Combine("Z:", "old", "click.wav"), "click.wav", "Z:"),
            new MissingMedia(Path.Combine("Z:", "old", "nothing.wav"), "nothing.wav", "Z:"),
        };

        var found = MediaRelink.Search(root, missing);

        Assert.Equal(near, found[missing[0].FullPath]);
        Assert.Equal(other, found[missing[1].FullPath]);
        Assert.False(found.ContainsKey(missing[2].FullPath));
        Assert.True(File.Exists(deep));
    }
}

/// <summary>Owns: the save with the media step (MainWindowSaving + MediaSaveStep): copy, keep links, cancel, relative paths in the file, one undo step.</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaSaveStepTests
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
        public MediaChoiceAnswer Answer = new(Copy: true, Remember: false);
        public ExternalMediaChoice Setting = ExternalMediaChoice.Ask;
        public List<string> Reports { get; } = new();
        public MediaSavePolicy Policy { get; } = new();

        public MainWindowSaving Saving(string target)
        {
            var step = new MediaSaveStep(Policy, () => Setting, choice => Setting = choice,
                (_, _, _) => { Asked++; return Task.FromResult(Answer); }, plan => MediaCollector.RunAsync(plan, null, CancellationToken.None), _ => { }, Reports.Add);
            return new MainWindowSaving(new SaveTo(target), null, Reports.Add, SongPluginStates.ForEngine(null, () => null), () => "tforge",
                _ => throw new InvalidOperationException("no question expected"), () => new DegradedSaveGate(), step);
        }
    }

    [Fact]
    public async Task FirstSaveCopiesTheExternalFileAndWritesARelativePathAndStaysClean()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var target = Path.Combine(tmp.Dir("song"), "s.tforge");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig();
        rig.Policy.Observe(document);

        Assert.True(await rig.Saving(target).SaveAs(document));

        Assert.Equal(1, rig.Asked);
        var copy = Path.Combine(tmp.Root, "song", "s Media", "take.wav");
        Assert.Equal("audio bytes", File.ReadAllText(copy));
        Assert.True(File.Exists(source));
        Assert.Equal(copy, document.Project.Tracks[0].AudioClips[0].File);
        Assert.Equal(Path.Combine("s Media", "take.wav"), ProjectService.Load(target, InputLimits.MaxTforgeFileBytes).Tracks[0].AudioClips[0].File);
        Assert.False(document.HasUnsavedChanges);
    }

    [Fact]
    public async Task CollectIsOneUndoStepThatPutsTheOldPathBack()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var target = Path.Combine(tmp.Dir("song"), "s.tforge");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig();
        rig.Policy.Observe(document);
        Assert.True(await rig.Saving(target).SaveAs(document));

        Assert.NotNull(DocumentEdits.Undo(document));

        Assert.Equal(source, document.Project.Tracks[0].AudioClips[0].File);
    }

    [Fact]
    public async Task KeepLinksLeavesThePathAndOnlyTheNextAddedFileAsksAgain()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var target = Path.Combine(tmp.Dir("song"), "s.tforge");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig { Answer = new MediaChoiceAnswer(Copy: false, Remember: false) };
        rig.Policy.Observe(document);
        var saving = rig.Saving(target);

        Assert.True(await saving.SaveAs(document));
        Assert.Equal(source, document.Project.Tracks[0].AudioClips[0].File);
        Assert.False(Directory.Exists(Path.Combine(tmp.Root, "song", "s Media")));
        Assert.Equal(source, ProjectService.Load(target, InputLimits.MaxTforgeFileBytes).Tracks[0].AudioClips[0].File);

        Assert.True(await saving.Save(document));
        Assert.Equal(1, rig.Asked);

        var added = tmp.File(tmp.Dir("more"), "bass.wav", "bass");
        document.Project.Tracks[0].AudioClips.Add(new AudioClip { File = added, SourceLengthSec = 1, FileLengthSec = 1 });
        document.Project.IsDirty = true;
        Assert.True(await saving.Save(document));
        Assert.Equal(2, rig.Asked);
    }

    [Fact]
    public async Task ClosingTheQuestionCancelsTheSaveAndWritesNothing()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var target = Path.Combine(tmp.Dir("song"), "s.tforge");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig { Answer = new MediaChoiceAnswer(false, false, Cancel: true) };
        rig.Policy.Observe(document);

        Assert.False(await rig.Saving(target).SaveAs(document));

        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task RememberStoresTheChoiceAndAlwaysCopySavesWithoutAsking()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var source = tmp.File(tmp.Dir("other"), "take.wav", "audio bytes");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        var rig = new Rig { Answer = new MediaChoiceAnswer(Copy: true, Remember: true) };
        rig.Policy.Observe(document);
        Assert.True(await rig.Saving(Path.Combine(tmp.Dir("one"), "a.tforge")).SaveAs(document));
        Assert.Equal(ExternalMediaChoice.AlwaysCopy, rig.Setting);

        var second = new SongDocument(() => null) { Project = MediaTestFolder.Song(source) };
        rig.Policy.Observe(second);
        Assert.True(await rig.Saving(Path.Combine(tmp.Dir("two"), "b.tforge")).SaveAs(second));

        Assert.Equal(1, rig.Asked);
        Assert.True(File.Exists(Path.Combine(tmp.Root, "two", "b Media", "take.wav")));
    }

    [Fact]
    public async Task SaveAsToAnotherFolderKeepsAnOldRelativeLinkWorking()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        var oldFolder = tmp.Dir("old");
        var oldFile = tmp.File(tmp.Dir(Path.Combine("old", "a Media")), "take.wav", "audio bytes");
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(Path.Combine("a Media", "take.wav")) };
        document.Path = Path.Combine(oldFolder, "a.tforge");
        document.IsNew = false;
        var rig = new Rig { Setting = ExternalMediaChoice.NeverCopy };
        rig.Policy.Observe(document);

        Assert.True(await rig.Saving(Path.Combine(tmp.Dir("new"), "b.tforge")).SaveAs(document));

        Assert.Equal(oldFile, document.Project.Tracks[0].AudioClips[0].File);
    }
}

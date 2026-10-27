using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the by-name folder search with the several-matches pick, the matches beside a located file, and the relink of many clips as one undo step.</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaFindMissingTests
{
    private sealed class Collect : IProgress<MediaMatch>
    {
        public List<MediaMatch> Reports { get; } = new();
        public Action<MediaMatch>? OnReport;
        public void Report(MediaMatch value) { Reports.Add(value); OnReport?.Invoke(value); }
    }

    private static MissingMedia Gone(MediaTestFolder tmp, string name) =>
        new(Path.Combine(tmp.Root, "gone", name), name, Path.Combine(tmp.Root, "gone"));

    // Three missing files, each living in its own subfolder (one of them two levels down) of the folder that will be searched.
    private static (string Search, List<MissingMedia> Missing) ThreeInSubfolders(MediaTestFolder tmp)
    {
        var search = tmp.Dir("found");
        tmp.File(tmp.Dir(Path.Combine("found", "one")), "kick.wav", "k");
        tmp.File(tmp.Dir(Path.Combine("found", "two", "deeper")), "snare.wav", "s");
        tmp.File(tmp.Dir(Path.Combine("found", "three")), "hat.wav", "h");
        tmp.File(tmp.Dir(Path.Combine("found", "three")), "unrelated.wav", "u");
        return (search, new List<MissingMedia> { Gone(tmp, "kick.wav"), Gone(tmp, "snare.wav"), Gone(tmp, "hat.wav") });
    }

    [Fact]
    public void ASearchFindsEveryMissingFileInAnySubfolderAndNamesTheSubfolder()
    {
        using var tmp = new MediaTestFolder();
        var (search, missing) = ThreeInSubfolders(tmp);

        var matches = MediaRelink.Find(search, missing);

        Assert.Equal(3, matches.Count);
        Assert.All(matches, m => Assert.NotNull(m.FoundPath));
        Assert.Equal("one", matches.Single(m => m.MissingPath.EndsWith("kick.wav")).Subfolder);
        Assert.Equal(Path.Combine("two", "deeper"), matches.Single(m => m.MissingPath.EndsWith("snare.wav")).Subfolder);
        Assert.Equal(Path.Combine(search, "three", "hat.wav"), matches.Single(m => m.MissingPath.EndsWith("hat.wav")).FoundPath);
    }

    [Fact]
    public void TheSearchReportsMatchesWhileItRunsSoTheListFillsLive()
    {
        using var tmp = new MediaTestFolder();
        var (search, missing) = ThreeInSubfolders(tmp);
        var progress = new Collect();

        MediaRelink.Find(search, missing, default, progress);

        Assert.Equal(3, progress.Reports.Select(r => r.MissingPath).Distinct().Count());
        Assert.All(progress.Reports, r => Assert.NotNull(r.FoundPath));
    }

    [Fact]
    public void NothingFoundLeavesTheClipsUnchanged()
    {
        using var tmp = new MediaTestFolder();
        var empty = tmp.Dir("empty");
        tmp.File(empty, "other.wav", "x");
        var missing = new List<MissingMedia> { Gone(tmp, "kick.wav"), Gone(tmp, "snare.wav") };
        var project = MediaTestFolder.Song(missing.Select(m => m.FullPath).ToArray());

        var matches = MediaRelink.Find(empty, missing);
        var fixes = matches.Where(m => m.FoundPath is not null).ToDictionary(m => m.MissingPath, m => m.FoundPath!, StringComparer.OrdinalIgnoreCase);

        Assert.All(matches, m => { Assert.Null(m.FoundPath); Assert.Equal(0, m.Matches); });
        Assert.Equal(0, MediaCollectApply.Apply(project, fixes, null, null));
        Assert.Equal(missing.Select(m => m.FullPath), project.Tracks[0].AudioClips.Select(c => c.File));
    }

    [Fact]
    public void LocatingOneFileRelinksTheOthersFoundInTheSameFolderOnly()
    {
        using var tmp = new MediaTestFolder();
        var folder = tmp.Dir("moved");
        var located = tmp.File(folder, "kick.wav", "k");
        var sibling = tmp.File(folder, "snare.wav", "s");
        tmp.File(tmp.Dir(Path.Combine("moved", "below")), "hat.wav", "h");   // below the folder: not a sibling
        var others = new List<MissingMedia> { Gone(tmp, "snare.wav"), Gone(tmp, "hat.wav"), Gone(tmp, "ride.wav") };

        var siblings = MediaRelink.SiblingsOf(located, others);

        Assert.Equal(sibling, Assert.Single(siblings).Value);
        Assert.Equal(others[0].FullPath, Assert.Single(siblings).Key);
    }

    [Fact]
    public void SeveralFilesWithTheNamePickTheCommonSizeThenTheNewest()
    {
        using var tmp = new MediaTestFolder();
        var search = tmp.Dir("search");
        var older = tmp.File(tmp.Dir(Path.Combine("search", "a")), "take.wav", "0123456789");
        var newer = tmp.File(tmp.Dir(Path.Combine("search", "b")), "take.wav", "abcdefghij");
        var odd = tmp.File(tmp.Dir(Path.Combine("search", "c")), "take.wav", "0123456789ABCDEFGHIJ");   // another size, and the newest of all
        File.SetLastWriteTimeUtc(older, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(odd, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var match = Assert.Single(MediaRelink.Find(search, new[] { Gone(tmp, "take.wav") }));

        Assert.Equal(newer, match.FoundPath);
        Assert.Equal(3, match.Matches);
        Assert.Equal("same size", match.Pick);
    }

    [Fact]
    public void TwoFilesOfDifferentSizesPickTheNewestAndSayNewest()
    {
        using var tmp = new MediaTestFolder();
        var search = tmp.Dir("search");
        var small = tmp.File(tmp.Dir(Path.Combine("search", "a")), "take.wav", "12345");
        var big = tmp.File(tmp.Dir(Path.Combine("search", "b")), "take.wav", "1234567890");
        File.SetLastWriteTimeUtc(small, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(big, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var match = Assert.Single(MediaRelink.Find(search, new[] { Gone(tmp, "take.wav") }));

        Assert.Equal(small, match.FoundPath);
        Assert.Equal("newest", match.Pick);
    }

    [Fact]
    public void CancellingMidSearchStopsItAndKeepsWhatWasReported()
    {
        using var tmp = new MediaTestFolder();
        var search = tmp.Dir("search");
        for (var i = 0; i < 30; i++) tmp.File(search, $"filler{i}.wav", "f");
        tmp.File(tmp.Dir(Path.Combine("search", "x")), "kick.wav", "k");
        tmp.File(tmp.Dir(Path.Combine("search", "y")), "snare.wav", "s");
        using var cancel = new CancellationTokenSource();
        var progress = new Collect { OnReport = _ => cancel.Cancel() };   // the first match arrives: stop

        Assert.ThrowsAny<OperationCanceledException>(() => MediaRelink.Find(search, new[] { Gone(tmp, "kick.wav"), Gone(tmp, "snare.wav") }, cancel.Token, progress));

        Assert.Single(progress.Reports);   // the second wanted file was never looked at
    }

    [Fact]
    public void RelinkingManyClipsIsOneUndoStepAndTheOldPathsComeBack()
    {
        using var tmp = new MediaTestFolder();
        var (search, missing) = ThreeInSubfolders(tmp);
        var document = new SongDocument(() => null) { Project = MediaTestFolder.Song(missing.Select(m => m.FullPath).ToArray()) };
        var fixes = MediaRelink.Find(search, missing).ToDictionary(m => m.MissingPath, m => m.FoundPath!, StringComparer.OrdinalIgnoreCase);

        var result = DocumentEdits.Run(document, project => MediaCollectApply.Apply(project, fixes, null, null) > 0, invalidatesTimeline: false, label: "Relink media");

        Assert.True(result.Changed);
        Assert.All(document.Project.Tracks[0].AudioClips, c => Assert.True(File.Exists(c.File)));
        Assert.NotNull(DocumentEdits.Undo(document));   // one step puts all three back
        Assert.Equal(missing.Select(m => m.FullPath), document.Project.Tracks[0].AudioClips.Select(c => c.File));
    }
}

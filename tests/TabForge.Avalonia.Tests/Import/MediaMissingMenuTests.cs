using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the top rows of the right-click menu of a clip whose audio file is missing (Locate file..., Find all missing audio... with the count, Retry, then the normal clip rows).</summary>
[Collection("MediaPolicyFileSystem")]
public sealed class MediaMissingMenuTests
{
    private static readonly Rect Box = new(0, 0, 400, 30);
    private static readonly UiColor Colour = new(255, 200, 100, 50);

    [AvaloniaFact]
    public void AMissingClipMenuStartsWithLocateAndFindAllThenRetryAndTheNormalRows()
    {
        using var fs = new LocalMediaFileSystem();
        using var tmp = new MediaTestFolder();
        WaveformCache.Default.ClearAll();
        var project = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        var gone = new AudioClip { File = Path.Combine(tmp.Root, "gone", "kick.wav"), Name = "kick", StartSec = 0.5, SourceLengthSec = 2, FileLengthSec = 2 };
        var goneToo = new AudioClip { File = Path.Combine(tmp.Root, "gone", "snare.wav"), Name = "snare", StartSec = 3, SourceLengthSec = 2, FileLengthSec = 2 };
        track.AudioClips.Add(gone);
        track.AudioClips.Add(goneToo);
        project.Tracks.Add(track);
        var view = new TimelineView(project) { Media = new MediaContext(() => null), Width = 1400, Height = 700 };
        var window = new Window { Width = 1400, Height = 700, Content = view };
        window.Show();
        foreach (var clip in new[] { gone, goneToo }) view.Waveforms.Draw(new NullDrawContext(), clip, Box, Colour, 1, 400);
        Assert.True(WaveformCache.Default.WaitIdle(5000));
        Assert.True(view.Waveforms.StatusOf(gone).IsMissing);

        var bounds = TimelineAudioClips.ClipBounds(view, track, gone);
        var rows = ContextMenuRows.TimelineAt(view, new Point((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2)).ToList();

        Assert.Equal("Locate file\u2026", rows[0].Header);
        Assert.Equal(ContextMenuRows.LocateMediaId, rows[0].Id);
        Assert.Equal("Find all missing audio\u2026", rows[1].Header);
        Assert.Equal("2 missing", rows[1].Shortcut);
        Assert.Equal(ContextMenuRows.RetryReadHeader, rows[2].Header);
        Assert.True(rows[3].IsSeparator);
        Assert.Same(gone, view.ContextMenus.ClickedUnreadableClip);
        Assert.Contains(rows.Skip(4), r => r.Command == TabForge.Menus.TimelineCommand.ClipCopy);
        window.Close();
        WaveformCache.Default.ClearAll();
    }
}

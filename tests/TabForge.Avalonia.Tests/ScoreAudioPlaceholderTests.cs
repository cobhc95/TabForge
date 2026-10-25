using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: an audio track shown in the score pane gives the placeholder note, not a score; a non-audio track restores the score.
/// Tests: ScorePreviewView.ShowTrack / ShowsAudioPlaceholder.
/// </summary>
public class ScoreAudioPlaceholderTests
{
    [AvaloniaFact]
    public void AudioTrackShowsPlaceholderAndGuitarTrackRestoresScore()
    {
        var view = new ScorePreviewView();
        var project = view.Editor.Project!;
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, Models.TrackKind.Audio));
        view.ShowTrack(project.Tracks.Count - 1);
        Assert.True(view.ShowsAudioPlaceholder);
        view.ShowTrack(0);
        Assert.False(view.ShowsAudioPlaceholder);
    }
}

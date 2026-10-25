using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the check that the Add track strip's Audio answer names a new audio track after the existing ones.
// Does not own: the prompt, the Track properties window or the document edit (ArrangementView and DocumentEdits).
// Tests: this file.
public class NewTrackFactoryTests
{
    [Fact]
    public void AudioTrackIsNumberedAfterExistingAudioTracks()
    {
        var project = new SongProject();
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var second = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        Assert.Equal("Audio 2", second.Name);
        Assert.True(second.IsAudio);
    }
}

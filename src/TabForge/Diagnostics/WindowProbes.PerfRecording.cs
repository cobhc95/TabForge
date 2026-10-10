using System.IO;
using System.Windows.Media;
using TabForge.Services.Video;

namespace TabForge.Diagnostics;

// Owns: the playback frame rate lines of `--perf-follow`: the UI render rate with playback alone and while Record video runs (4K, 60 fps, Score), into a temp folder that is removed afterwards.
// Does not own: the follow-style loop (WindowProbes.PerfProbe) or recording itself (LiveVideoRecordController).
// Tests: none (diagnostics only).
internal sealed partial class WindowProbes
{
    /// <summary>Render callbacks per second over <paramref name="milliseconds"/> (the compositor frame rate of this window).</summary>
    private async Task<double> RenderFpsAsync(int milliseconds)
    {
        var frames = 0;
        EventHandler count = (_, _) => frames++;
        CompositionTarget.Rendering += count;
        await Task.Delay(milliseconds);
        CompositionTarget.Rendering -= count;
        return frames * 1000.0 / milliseconds;
    }

    /// <summary>Plays from the start twice: plain, then recording; returns one report line each.</summary>
    private async Task<IEnumerable<string>> PlaybackFpsLinesAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-perf-rec-{Guid.NewGuid():N}");
        var video = _settings.LiveVideo ??= new LiveVideoSettings();
        var saved = (video.Folder, video.Resolution, video.Fps);
        video.Folder = folder; video.Resolution = LiveVideoChoices.P4K; video.Fps = 60;
        var lines = new List<string>();
        try
        {
            PlayFromStart(); await Task.Delay(1500);
            lines.Add($"render fps, playback alone: {await RenderFpsAsync(6000):0.0}");
            StopPlayback(); await Task.Delay(500);
            Window.ToggleVideoRecording(); await Task.Delay(2500);
            lines.Add($"render fps, playback while recording 4K/60: {await RenderFpsAsync(6000):0.0}");
            Window.ToggleVideoRecording(); await Task.Delay(5000);
            lines.Add(Directory.Exists(folder) ? $"recorded files: {string.Join(", ", Directory.GetFiles(folder).Select(f => $"{new FileInfo(f).Length / 1024} KB"))}" : "recorded files: none");
        }
        finally
        {
            (video.Folder, video.Resolution, video.Fps) = saved;
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } // Not logged: probe cleanup of a temporary folder.
        }
        return lines;
    }
}

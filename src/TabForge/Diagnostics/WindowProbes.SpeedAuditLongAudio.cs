using System.IO;
using System.Linq;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Owns: the real long-audio drop action measured by the speed audit.
// Does not own: report timing, probe access or the speed-audit action list.
// Tests: none (diagnostics only).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private object? LongAudioDrop()
        {
            var document = (DocumentSession?)_w.Window.GetType().GetProperty("Doc", Any)?.GetValue(_w.Window)
                ?? throw new InvalidOperationException("The speed-audit window has no active document.");
            var project = document.Project;
            TrackModel? targetTrack = null;
            AudioClip? clip = null;
            var targetIndex = -1;
            for (var i = 0; i < project.Tracks.Count && clip is null; i++)
            {
                targetTrack = project.Tracks[i];
                clip = targetTrack.AudioClips.FirstOrDefault(audio => audio.Name == "Long clip");
                if (clip is not null) targetIndex = i;
            }
            if (clip is null || targetTrack is null) throw new InvalidOperationException("The speed-audit WAV clip is missing after undo.");
            if (!File.Exists(clip.File) || clip.FileLengthSec <= 0)
                throw new InvalidOperationException("The speed-audit WAV path or measured duration is invalid.");
            if (!targetTrack.IsAudio) throw new InvalidOperationException("The long-audio drop target is not an audio track.");

            _longClip = clip;
            _audioTrack = targetIndex;
            var originalTrackCount = project.Tracks.Count;
            var originalBars = project.Tracks.Select(track => track.Measures.Count).ToArray();
            var originalClips = targetTrack.AudioClips.Count;
            var startSec = SongExtent.Measure(project).EndSec;
            var item = new DropItem
            {
                Path = _longClip.File,
                Name = "Long audio drop",
                Kind = DropItemKind.Audio,
                Seconds = _longClip.FileLengthSec
            };
            var plan = MediaDrop.Plan(project, new[] { item }, _audioTrack, 0, startSec, SongQuarterMap.For(project));
            if (!plan.Valid || plan.NewTrack || plan.TrackIndex != _audioTrack)
                throw new InvalidOperationException("The long-audio drop plan does not target the existing audio track.");

            var controller = Get(_w.Window, "_clips") ?? throw new InvalidOperationException("The clip controller is unavailable.");
            Call(controller, "ApplyMediaDrop", document, plan);
            if (!ReferenceEquals(document.Project, project) || project.Tracks.Count != originalTrackCount
                || project.Tracks.Select(track => track.Measures.Count).Zip(originalBars).Any(pair => pair.First <= pair.Second)
                || project.Tracks[targetIndex].AudioClips.Count != originalClips + 1)
                throw new InvalidOperationException("The actual long-audio drop did not grow every track and append one clip.");
            return true;
        }
    }
}

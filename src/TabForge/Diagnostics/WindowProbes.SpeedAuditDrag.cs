using System.Windows;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the drag-start entries of the speed audit (file drag entering the timeline, clip drag start): pointer event to the ghost in place.
// Does not own: report timing or the action list.
// Tests: TestDragStartSpeedAuditEntries.
/// <summary>The names of the drag-start speed-audit entries; <c>TABFORGE_SPEED_ONLY="drag start"</c> selects both.</summary>
internal static class SpeedAuditDragEntries
{
    public const string FileEnter = "drag start: file enters timeline (ghost)";
    public const string ClipStart = "drag start: clip drag (ghost)";
    public static readonly string[] All = { FileEnter, ClipStart };
}

internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private async Task DragStartAsync()
        {
            var timeline = _w.Arrangement.TimelineForTest;
            var clip = _longClip ?? throw new InvalidOperationException("The speed-audit clip is missing.");
            // Three copies of the long WAV: a header read each while the drag enters, as for a multi-file drag from Explorer.
            var files = new[] { clip.File, clip.File, clip.File };
            var y = timeline.LaneTop(_audioTrack, 0) + 8;
            await TimeAsync(SpeedAuditDragEntries.FileEnter, () =>
            {
                var data = new DataObject(DataFormats.FileDrop, files);
                var effect = timeline.MediaDragOver(data, new Point(320, y), false);
                if (effect != DragDropEffects.Copy || timeline.CurrentDropPreview is not { Valid: true })
                    throw new InvalidOperationException("The file drag over the audio lane produced no valid ghost.");
                return null;
            }, async () => { timeline.MediaDragLeave(); timeline.SetDropItemsForTest(null); await _w.Settle(60); },
            note: "3 x 180 s WAV; DragEnter to ghost set + idle (cold session each rep)");

            var press = new Point(timeline.XOfSec(clip.StartSec) + 20, y);
            await TimeAsync(SpeedAuditDragEntries.ClipStart, () =>
            {
                if (timeline.ClipGestures.SimulateMove(clip, _audioTrack, press, new Point(press.X + 30, press.Y)) is not { Valid: true })
                    throw new InvalidOperationException("The clip drag produced no valid ghost.");
                return null;
            }, async () => { timeline.ClipGestures.Cancel(); await _w.Settle(60); },
            note: "first pointer move past the drag threshold to ghost set + idle");
        }
    }
}

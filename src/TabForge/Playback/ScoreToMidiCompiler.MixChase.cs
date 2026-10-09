using System.Linq;
using TabForge.Models;

namespace TabForge.Playback;

// ScoreToMidiCompiler: mix-table state that was set before the start point.
// Owns: replaying skipped mix points into the channel setup. Does not own: emitting mix points in the performed range (EmitMix).
// Tests: TestMixPointsSurviveSeek.
internal sealed partial class ScoreToMidiCompiler
{
    private bool _chasing;
    private readonly Dictionary<(int Track, int Channel, int Status, int Data1), int> _chased = new();

    /// <summary>
    /// Folds the mix points of the bars played before <see cref="PlaybackOptions.StartBar"/> into the chase.
    /// A ramp counts as finished (its target), the same state continuous playback has once the ramp is over.
    /// </summary>
    private void ChaseBarsBeforeStart(int firstBar)
    {
        if (_opt.StartBar <= 0) return;
        var full = PlaybackOrder.Build(_project, new PlaybackOptions { RepeatExpansion = _opt.RepeatExpansion });
        var cut = full.FindIndex(bar => bar == firstBar);
        if (cut <= 0) return;
        foreach (var bar in full.Take(cut))
            foreach (var track in _players)
            {
                if (track.IsAudio || bar >= track.Measures.Count) continue;
                var index = _project.Tracks.IndexOf(track);
                var m = track.Measures[bar];
                var source = m.SimileOneBar && bar > 0 ? bar - 1 : m.SimileTwoBar && bar > 1 ? bar - 2 : bar;
                var measure = track.Measures[source];
                foreach (var cells in new[] { measure.Cells, measure.Voice2Cells })
                    foreach (var cell in cells)
                        if (cell.Mix is { IsEmpty: false } mix) ChaseMix(mix, index);
            }
    }

    private void ChaseMix(MixChange mix, int trackIndex)
    {
        _chasing = true;
        try { EmitMix(mix, trackIndex, 0, 0); }
        finally { _chasing = false; }
    }

    private void RecordChase(int channel, int statusBase, int data1, int data2, int trackIndex) =>
        _chased[(trackIndex, channel, statusBase, statusBase == 0xC0 ? 0 : data1)] = statusBase == 0xC0 ? data1 : data2;

    /// <summary>Writes the chased state over the base channel setup (inserted at <paramref name="setupEnd"/> when the setup has no such message).</summary>
    private void ApplyChase(int setupEnd)
    {
        foreach (var ((track, channel, status, data1), value) in _chased)
        {
            var e = _timeline.ChannelSetup.FirstOrDefault(s => s.TrackIndex == track && s.Channel == channel
                && (s.Status & 0xF0) == status && (status == 0xC0 || s.Data1 == data1));
            if (e is null)
            {
                var device = track >= 0 && track < _project.Tracks.Count ? _project.Tracks[track].MidiOutputDeviceId : -1;
                e = new ScoreEvent
                {
                    TimeMs = _timeline.PlayFromMs, DeviceId = device, Channel = channel, Status = status | (channel & 0x0F),
                    Data1 = status == 0xC0 ? value : data1, Data2 = status == 0xC0 ? 0 : value, TrackIndex = track, IsSetup = true
                };
                _timeline.Events.Insert(setupEnd, e);
                _timeline.ChannelSetup.Add(e);
            }
            else if (status == 0xC0) e.Data1 = value;
            else e.Data2 = value;
        }
    }
}

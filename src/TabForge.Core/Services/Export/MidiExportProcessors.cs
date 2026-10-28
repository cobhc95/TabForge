using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Midi;
using TabForge.AudioEngine.Plugins;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;

namespace TabForge.Services;

// Owns: applying each track's MIDI processors during MIDI file export (the same chain the live engine
//   runs, not a copy of it), so an exported file plays what TabForge plays.
// Does not own: the chain itself (TabForge.Core/Midi), the timeline compile or the file encoding (MidiExportService).
// Tests: TabForge.Avalonia.Tests.MidiExportProcessorsTests.

/// <summary>
/// Runs the live MIDI processor chain over each track's compiled events for a MIDI file export.
/// The same <see cref="MidiProcessorChain"/> the engine runs live processes every track that has processors
/// (transpose, arpeggiation, humanize, velocity, controller LFOs and the rest); articulation keyswitches stay
/// out of the file (their pitch/velocity replacements are already baked into the notes). No audio is involved,
/// so audio-coupled processors pass the notes through exactly as they do live without audio. Deterministic:
/// fixed sample rate, fixed block size, the processors' own fixed random seeds, bounded flush.
/// </summary>
internal static class MidiExportProcessors
{
    private const int SampleRate = 48000;
    private const int BlockFrames = 4800;   // 100 ms blocks: exact sample positions, no timing drift
    private const int MaxFlushBlocks = 80;  // 8 s of tail (delay/humanize/arpeggiator tails never need this much)
    private const int QuietFlushBlocks = 4;

    /// <summary>A copy of <paramref name="timeline"/> whose events ran through each track's processor chain.</summary>
    internal static ScoreTimeline WithProcessors(SongProject project, ScoreTimeline timeline)
    {
        var processed = new ScoreTimeline
        {
            TotalMs = timeline.TotalMs,
            CountInMs = timeline.CountInMs,
            PlayFromMs = timeline.PlayFromMs,
            StartedAfterMixPoints = timeline.StartedAfterMixPoints,
            RetriggerCollisions = timeline.RetriggerCollisions,
            TieMerges = timeline.TieMerges,
            TieOrphans = timeline.TieOrphans,
            LetRingExtensions = timeline.LetRingExtensions,
            MaxLetRingExtensionMs = timeline.MaxLetRingExtensionMs,
            LongestSoundingNoteMs = timeline.LongestSoundingNoteMs,
            LongestSoundingNoteAtBar = timeline.LongestSoundingNoteAtBar,
        };
        processed.Bars.AddRange(timeline.Bars);
        processed.Notes.AddRange(timeline.Notes);
        processed.ChannelSetup.AddRange(timeline.ChannelSetup);
        var events = new List<ScoreEvent>(timeline.Events.Count);
        for (var trackIndex = 0; trackIndex < project.Tracks.Count; trackIndex++)
            events.AddRange(ProcessTrack(project, timeline, trackIndex));
        // Events of no track (metronome, count-in) never go through a chain.
        events.AddRange(timeline.Events.Where(e => e.TrackIndex < 0 || e.TrackIndex >= project.Tracks.Count));
        processed.Events.AddRange(events.OrderBy(e => e.TimeMs));   // stable: same-time events keep their order
        return processed;
    }

    private static List<ScoreEvent> ProcessTrack(SongProject project, ScoreTimeline timeline, int trackIndex)
    {
        var source = timeline.Events.Where(e => e.TrackIndex == trackIndex && !e.IsArticulation).OrderBy(e => e.TimeMs).ToList();
        var specs = new List<MidiProcSpec>();
        foreach (var slot in project.Tracks[trackIndex].Rig.Plugins)
            if (slot.MidiProcessors is { Count: > 0 } list)
                foreach (var p in list)
                    specs.Add(new MidiProcSpec(p.Type, p.Enabled, MidiProcessorCatalog.EngineParams(p)));
        if (specs.Count == 0 || MidiProcessorChain.Create(specs, SampleRate) is not { } chain) return source;

        var deviceId = source.Count > 0 ? source[0].DeviceId : project.Tracks[trackIndex].MidiOutputDeviceId;
        var lastSample = source.Count == 0 ? 0 : (long)Math.Round(source[^1].TimeMs / 1000.0 * SampleRate);
        var lastBlock = (int)(lastSample / BlockFrames);
        var output = new List<ScoreEvent>(source.Count + 16);
        var input = new MidiBuffer();
        var at = 0;
        // One stopped block first, like the live chain (which idles stopped, then starts): load-time senders
        // (program/bank/CC senders) emit here, start-time senders on the first music block, exactly as live.
        RunBlock(chain, project, timeline, ReadOnlySpan<BlockMidi>.Empty, output, trackIndex, deviceId, -BlockFrames, playing: false);
        for (var block = 0; block <= lastBlock; block++)
        {
            input.Clear();
            while (at < source.Count && (long)Math.Round(source[at].TimeMs / 1000.0 * SampleRate) < (long)(block + 1) * BlockFrames)
            {
                var e = source[at++];
                input.Add((int)((long)Math.Round(e.TimeMs / 1000.0 * SampleRate) - (long)block * BlockFrames), (byte)e.Status, (byte)e.Data1, (byte)e.Data2);
            }
            RunBlock(chain, project, timeline, input.Span, output, trackIndex, deviceId, (long)block * BlockFrames, playing: true);
        }
        // Drain scheduled futures (delays, arpeggiator tails) with the transport stopped, then release anything left.
        var quiet = 0;
        var endBlock = lastBlock + 1;
        for (var flush = 0; flush < MaxFlushBlocks && quiet < QuietFlushBlocks; flush++, endBlock++)
        {
            var before = output.Count;
            RunBlock(chain, project, timeline, ReadOnlySpan<BlockMidi>.Empty, output, trackIndex, deviceId, (long)endBlock * BlockFrames, playing: false);
            quiet = output.Count == before ? quiet + 1 : 0;
        }
        var release = new MidiBuffer();
        chain.ReleaseAll(release);
        var releaseAt = (double)endBlock * BlockFrames / SampleRate * 1000.0;
        foreach (var e in release.Span)
            output.Add(new ScoreEvent { TimeMs = releaseAt, DeviceId = deviceId, Channel = e.Status & 0x0F, Status = e.Status, Data1 = e.Data1, Data2 = e.Data2, TrackIndex = trackIndex });
        return output.OrderBy(e => e.TimeMs).ToList();   // stable: same-time events keep their order
    }

    private static void RunBlock(MidiProcessorChain chain, SongProject project, ScoreTimeline timeline, ReadOnlySpan<BlockMidi> input,
        List<ScoreEvent> output, int trackIndex, int deviceId, long blockStart, bool playing)
    {
        var outputBuffer = new MidiBuffer();
        var midMs = Math.Max(0, (blockStart + BlockFrames / 2) * 1000.0 / SampleRate);
        var tempo = timeline.Bars.Count == 0 ? project.Tempo : timeline.BarAt(midMs).Tempo;
        var transport = new TransportInfo
        {
            Tempo = tempo,
            PpqPosition = Math.Max(0, blockStart) / (double)SampleRate * tempo / 60.0,
            Playing = playing,
            SongSec = Math.Max(0, blockStart) / (double)SampleRate,
            Speed = 1,
        };
        chain.Process(input, outputBuffer, BlockFrames, transport, log: null);
        foreach (var e in outputBuffer.Span)
            output.Add(new ScoreEvent
            {
                TimeMs = Math.Max(0, (blockStart + e.Frame) * 1000.0 / SampleRate),
                DeviceId = deviceId,
                Channel = e.Status < 0xF0 ? e.Status & 0x0F : 0,
                Status = e.Status, Data1 = e.Data1, Data2 = e.Data2, TrackIndex = trackIndex,
            });
    }
}

using System.Linq;

namespace TabForge.Playback;

/// <summary>
/// Post-passes that make the raw compiled event list musically playable:
/// let-ring tails, a release gap before same-pitch retriggers, and a deterministic event order.
/// Runs after the compiler and before the timeline is handed to the scheduler.
/// </summary>
internal static class SustainResolver
{
    /// <summary>Release before a same-pitch retrigger; without it many synths swallow the attack.</summary>
    private const double ReleaseGapMs = 9.0;
    private const double LetRingStopGapMs = 3.0;

    public static void Resolve(ScoreTimeline timeline, int[] channels, double letRingCapMs = 2000)
    {
        ApplyLetRing(timeline, letRingCapMs);
        ApplyRetriggerGaps(timeline, channels);
        SortEvents(timeline);
        timeline.Notes.Sort((a, b) => a.OnsetMs.CompareTo(b.OnsetMs));
        foreach (var note in timeline.Notes)
        {
            if (note.DurationMs <= timeline.LongestSoundingNoteMs) continue;
            timeline.LongestSoundingNoteMs = note.DurationMs;
            timeline.LongestSoundingNoteAtBar = note.Bar;
        }
    }

    private static int ChannelOf(NoteEvent note, int[] channels) =>
        note.Channel >= 0 ? note.Channel : note.TrackIndex >= 0 && note.TrackIndex < channels.Length ? channels[note.TrackIndex] : 0;

    /// <summary>
    /// Let-ring: a let-ring note keeps sounding until the next note on the same string (the string is
    /// physically re-plucked there) or its natural ring time, whichever comes first. The extension is
    /// capped so a let-ring run can never turn into a multi-second drone - the previous
    /// chain-through-every-following-let-ring-note rule produced 9-14 second notes that made the mix
    /// melt together.
    /// </summary>
    private static void ApplyLetRing(ScoreTimeline timeline, double letRingCapMs)
    {
        // Every note of a let-ring run keeps ringing until the run ends (the end of its last beat), so a
        // let-ring chord or arpeggio rings together. Runs are found per track from the written note ends, before any
        // note is extended.
        var runEnd = new Dictionary<NoteEvent, double>();
        var runSize = new Dictionary<NoteEvent, int>();
        foreach (var track in timeline.Notes.Where(n => n.OffEventIndices.Count > 0 && n.LetRing).GroupBy(n => n.TrackIndex))
        {
            var run = new List<NoteEvent>();
            var end = 0.0;
            void Close()
            {
                foreach (var member in run) { runEnd[member] = end; runSize[member] = run.Count; }
                run.Clear();
            }
            foreach (var note in track.OrderBy(candidate => candidate.OnsetMs))
            {
                if (run.Count > 0 && note.OnsetMs > end + 5.0) Close();
                if (run.Count == 0) end = 0;
                run.Add(note);
                end = Math.Max(end, note.EndMs);
            }
            Close();
        }

        var groups = timeline.Notes
            .Where(n => n.OffEventIndices.Count > 0)
            .GroupBy(n => (n.TrackIndex, n.StringIndex));

        foreach (var group in groups)
        {
            var list = group.OrderBy(n => n.OnsetMs).ToList();
            for (var i = 0; i < list.Count; i++)
            {
                if (!list[i].LetRing) continue;

                var written = list[i].DurationMs;
                double end;
                if (runSize.TryGetValue(list[i], out var size) && size > 1)
                    end = Math.Min(runEnd[list[i]], list[i].OnsetMs + Math.Max(written, Math.Max(600.0, letRingCapMs)));
                else
                {
                    var ringCap = Math.Clamp(written * 2.0, 600.0, Math.Max(600.0, letRingCapMs));
                    end = list[i].OnsetMs + ringCap;
                }
                if (i + 1 < list.Count) end = Math.Min(end, list[i + 1].OnsetMs - LetRingStopGapMs);
                if (end <= list[i].EndMs) continue;

                var extension = end - list[i].EndMs;
                SetEnd(timeline, list[i], end);
                timeline.LetRingExtensions++;
                timeline.MaxLetRingExtensionMs = Math.Max(timeline.MaxLetRingExtensionMs, extension);
            }
        }
    }

    /// <summary>
    /// Moves a note-off earlier, to at most <paramref name="limitMs"/>, without ever placing it before its own
    /// note-on (the on is always emitted right before its off). A repeated attack (tremolo picking, trill) that
    /// would start at or after the limit is dropped: both events are marked <see cref="ScoreEvent.Dropped"/> and
    /// <see cref="SortEvents"/> leaves them out, so nothing is sent for them (no quiet blip that a sampled plug-in could
    /// still voice, and no note-off landing inside the next note of the same pitch).
    /// Cutting only the off used to leave such an attack sounding forever (a hanging note).
    /// </summary>
    internal static void LimitOffTime(ScoreTimeline timeline, int offIndex, double limitMs)
    {
        if (offIndex < 0 || offIndex >= timeline.Events.Count) return;
        var off = timeline.Events[offIndex];
        if (off.TimeMs <= limitMs) return;
        off.TimeMs = limitMs;
        if (offIndex > 0 && timeline.Events[offIndex - 1] is { IsNoteOn: true } on &&
            on.Data1 == off.Data1 && on.Channel == off.Channel && on.TimeMs + 1.0 > limitMs)
        {
            on.Dropped = true;
            off.Dropped = true;
        }
    }

    private static void SetEnd(ScoreTimeline timeline, NoteEvent note, double endMs)
    {
        var sustainReleases = note.SustainOffEventIndices.Count > 0
            ? note.SustainOffEventIndices : note.OffEventIndices;
        foreach (var index in sustainReleases)
        {
            if (index < 0 || index >= timeline.Events.Count) continue;
            timeline.Events[index].TimeMs = endMs;
        }
        note.DurationMs = endMs - note.OnsetMs;
    }

    /// <summary>
    /// Guarantees a short release before the next note of the same pitch on the same channel.
    /// Without it a NoteOff and the following NoteOn share a timestamp and synths (notably the
    /// Microsoft GS Wavetable) swallow the retrigger - heard as a note hanging over or a late groove.
    /// </summary>
    private static void ApplyRetriggerGaps(ScoreTimeline timeline, int[] channels)
    {
        var byChannelPitch = new Dictionary<(int channel, int pitch), List<NoteEvent>>();
        foreach (var n in timeline.Notes)
        {
            if (n.OffEventIndices.Count == 0) continue;
            var key = (ChannelOf(n, channels), n.Midi);
            if (!byChannelPitch.TryGetValue(key, out var list)) byChannelPitch[key] = list = new List<NoteEvent>();
            list.Add(n);
        }

        foreach (var list in byChannelPitch.Values)
        {
            list.Sort((a, b) => a.OnsetMs.CompareTo(b.OnsetMs));
            for (var i = 0; i < list.Count; i++)
            {
                var current = list[i];
                var nextOnset = i + 1 < list.Count ? list[i + 1].OnsetMs : double.MaxValue;

                // A unison (two strings sounding the same pitch together) must NOT be split apart.
                if (nextOnset > current.OnsetMs + 1.0)
                {
                    // The release must be strictly before the next attack, otherwise many synths
                    // swallow the retrigger. Shortening a sustained note by a few ms is inaudible;
                    // dropping the new attack is not.
                    var desiredEnd = Math.Max(current.OnsetMs + 1.0, nextOnset - ReleaseGapMs);
                    foreach (var index in current.OffEventIndices)
                        LimitOffTime(timeline, index, desiredEnd);
                }

                var lastOff = current.OffEventIndices
                    .Where(idx => idx >= 0 && idx < timeline.Events.Count)
                    .Select(idx => timeline.Events[idx].TimeMs)
                    .DefaultIfEmpty(current.EndMs)
                    .Max();
                current.DurationMs = Math.Max(1, lastOff - current.OnsetMs);

                // Count what is still colliding, here where the indices are still valid.
                if (i + 1 < list.Count && nextOnset > current.OnsetMs + 1.0)
                {
                    foreach (var index in current.OffEventIndices)
                    {
                        if (index < 0 || index >= timeline.Events.Count) continue;
                        var t = timeline.Events[index].TimeMs;
                        if (t >= nextOnset - 0.001 && t < nextOnset + 8.0) { timeline.RetriggerCollisions++; break; }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Deterministic order: time, then note-offs before controllers before note-ons. Writes to the
    /// same channel parameter preserve emission order so a final reset cannot sort ahead of its value.
    /// </summary>
    internal static void SortEvents(ScoreTimeline timeline)
    {
        // Stable ordering is important for stateful MIDI streams: the pitch-bend-range RPN must
        // remain select -> data-entry -> deselect, and expression endpoints must remain before
        // their same-time resets. Setup is sent before score events at an identical timestamp,
        // followed by track and original emission order for deterministic ties.
        // Dropped events (an attack cut away by LimitOffTime) leave the list here, after the last use of event indices.
        var ordered = timeline.Events
            .Select((item, index) => (item, index))
            .Where(pair => !pair.item.Dropped)
            .OrderBy(pair => pair.item.TimeMs)
            .ThenBy(pair => Priority(pair.item))
            .ThenBy(pair => pair.item.IsSetup ? 0 : 1)
            .ThenBy(pair => pair.item.TrackIndex)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.item)
            .ToArray();
        timeline.Events.Clear();
        timeline.Events.AddRange(ordered);
    }

    private static int Priority(ScoreEvent e) => (e.Status & 0xF0) switch
    {
        0x80 => 0,          // note off first
        0xB0 or 0xE0 => 1,  // controllers / pitch bend
        0x90 => 2,          // note on last
        _ => 1
    };
}

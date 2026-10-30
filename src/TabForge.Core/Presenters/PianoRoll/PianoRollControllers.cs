using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the piano roll's controller lanes for a MIDI track: the lanes offered (velocity, CC1 modulation, CC7 volume, CC10 pan, CC11 expression,
//       CC64 sustain, pitch bend, program change, channel aftertouch, and any other CC by number) with their value ranges, reading a controller's
//       points from the shown clips on the performed clock, writing them back (each point to the clip that holds it, which grows to hold it),
//       and the lane edits: a drawn line (points at each step, replacing the old ones in its span), a stepped program point (one value from its
//       beat), and an erased span.
// Does not own: the clip model (AudioClip.Controllers), the playback of the points (ScoreToMidiCompiler.Clips), the lane drawing and gestures (the app).
// A lane program change overrides the track's own program (mixer / channel setup) from its position; seeking restores the last lane value, and the
// track's program plays again only where no lane point holds. Poly aftertouch is out of scope: it needs per-note storage the clip model has none of.
// Tests: TabForge.Avalonia.Tests.PianoRollControllerTests.

/// <summary>One controller point at <paramref name="Beat"/> on the piano roll's clock.</summary>
public readonly record struct PianoRollPoint(double Beat, int Value);

public static class PianoRollControllers
{
    public const int Velocity = -1;
    public const int PitchBend = ClipController.PitchBend;
    public const int Program = ClipController.ProgramChange;
    public const int ChannelAftertouch = ClipController.ChannelAftertouch;

    /// <summary>Whether <paramref name="lane"/> is a lane id: velocity, a CC (0 to 127), pitch bend, program change or channel aftertouch.</summary>
    public static bool IsLane(int lane) => lane == Velocity || (lane >= 0 && lane <= ChannelAftertouch);

    /// <summary>The lanes a MIDI track offers, in the lane menu's order (a tab track offers only <see cref="Velocity"/>). The lane ids of the original
    /// seven (velocity, CC1, CC7, CC10, CC11, CC64, pitch bend) are unchanged: old songs load with them as before.</summary>
    public static IReadOnlyList<(int Id, string Name)> Lanes { get; } = new[]
    {
        (Velocity, "Velocity"), (PitchBend, "Pitch bend"), (1, "Mod wheel (CC1)"), (7, "Volume (CC7)"), (10, "Pan (CC10)"), (11, "Expression (CC11)"),
        (64, "Sustain (CC64)"), (Program, "Program change"), (ChannelAftertouch, "Channel aftertouch"),
    };

    /// <summary>Known names for CC lanes outside <see cref="Lanes"/> (picked through "+ Any CC...").</summary>
    private static string CcName(int lane) => lane switch
    {
        2 => "CC2 Breath",
        4 => "CC4 Foot",
        5 => "CC5 Portamento time",
        65 => "CC65 Portamento",
        66 => "CC66 Sostenuto",
        67 => "CC67 Soft pedal",
        68 => "CC68 Legato",
        69 => "CC69 Hold 2",
        84 => "CC84 Portamento control",
        _ => "CC" + lane,
    };

    /// <summary>The lane button's label: "Lane: " and the lane's short name (the menu adds the CC number, the label does not).</summary>
    public static string ButtonName(int lane) => "Lane: " + (lane switch
    {
        Velocity => "Velocity", PitchBend => "Pitch bend", Program => "Program change", ChannelAftertouch => "Channel aftertouch",
        7 => "Volume", 10 => "Pan", 11 => "Expression", 64 => "Sustain", 1 => "Mod wheel",
        _ => "CC " + lane,
    });

    public static string NameOf(int lane) => Lanes.FirstOrDefault(l => l.Id == lane).Name ?? CcName(lane);

    public static int Min(int lane) => lane == PitchBend ? -8192 : lane == Velocity ? 1 : 0;

    public static int Max(int lane) => lane == PitchBend ? 8191 : 127;

    /// <summary>The lane's resting value, drawn as its centre line (pitch bend 0, pan 64).</summary>
    public static int Centre(int lane) => lane == PitchBend ? 0 : lane == 10 ? 64 : 0;

    public static int Clamp(int lane, int value) => Math.Clamp(value, Min(lane), Max(lane));

    /// <summary>The points of <paramref name="controller"/> in the shown clips, by beat.</summary>
    public static List<PianoRollPoint> Read(TrackModel track, PianoRollTimeMap map, int controller, int? lane = null)
    {
        var list = new List<PianoRollPoint>();
        foreach (var clip in PianoRollClipSource.Shown(track, lane))
            foreach (var point in clip.Controllers ?? new List<ClipController>())
            {
                if (point.Controller != controller || point.StartSec < clip.OffsetSec - 1e-6 || point.StartSec >= clip.OffsetSec + clip.SourceLengthSec) continue;
                list.Add(new PianoRollPoint(map.BeatsAt(clip.StartSec + (point.StartSec - clip.OffsetSec) / PianoRollClipSource.Speed(clip)), Clamp(controller, point.Value)));
            }
        return list.OrderBy(p => p.Beat).ToList();
    }

    /// <summary>Stores <paramref name="points"/> as <paramref name="controller"/>'s points in the shown clips (the other controllers kept); false when nothing changed.</summary>
    public static bool Write(TrackModel track, PianoRollTimeMap map, int controller, IReadOnlyList<PianoRollPoint> points, int? lane = null)
    {
        var before = Read(track, map, controller, lane);
        if (before.Count == points.Count && before.Zip(points).All(p => Math.Abs(p.First.Beat - p.Second.Beat) < 1e-6 && p.First.Value == p.Second.Value)) return false;
        var timed = points.Select(p => (Point: p, Sec: map.SecondsAt(p.Beat))).ToList();
        var clips = PianoRollClipSource.Shown(track, lane);
        if (clips.Count == 0)
        {
            if (timed.Count == 0) return false;
            clips.Add(PianoRollClipSource.NewClip(track, timed.Min(t => t.Sec), timed.Max(t => t.Sec) + 0.001));
        }
        foreach (var clip in clips)
            clip.Controllers = (clip.Controllers ?? new List<ClipController>())
                .Where(p => p.Controller != controller || p.StartSec < clip.OffsetSec - 1e-6 || p.StartSec >= clip.OffsetSec + clip.SourceLengthSec).ToList();
        foreach (var (point, sec) in timed)
        {
            var clip = PianoRollClipSource.Owner(clips, sec);
            if (sec < clip.StartSec || sec >= clip.EndSec)
            {
                var front = PianoRollClipSource.Cover(clip, sec, sec + 0.001);
                if (front > 0)
                {
                    clip.Notes = clip.Notes?.Select(n => n with { StartSec = n.StartSec + front }).ToList();
                    clip.Controllers = clip.Controllers!.Select(p => p with { StartSec = p.StartSec + front }).ToList();
                }
            }
            clip.Controllers!.Add(new ClipController(clip.OffsetSec + (sec - clip.StartSec) * PianoRollClipSource.Speed(clip), controller, Clamp(controller, point.Value)));
        }
        foreach (var clip in clips) clip.Controllers = clip.Controllers!.OrderBy(p => p.StartSec).ToList();
        return true;
    }

    /// <summary>A line drawn in the lane from (<paramref name="fromBeat"/>, <paramref name="fromValue"/>) to (<paramref name="toBeat"/>, <paramref name="toValue"/>):
    /// the old points in its span go and a point is placed at every <paramref name="step"/> along it (both ends included).</summary>
    public static List<PianoRollPoint> DrawLine(IReadOnlyList<PianoRollPoint> points, int lane, double fromBeat, int fromValue, double toBeat, int toValue, double step)
    {
        if (toBeat < fromBeat) (fromBeat, fromValue, toBeat, toValue) = (toBeat, toValue, fromBeat, fromValue);
        var list = points.Where(p => p.Beat < fromBeat - 1e-6 || p.Beat > toBeat + 1e-6).ToList();
        var span = toBeat - fromBeat;
        var count = step <= 0 || span <= 0 ? 0 : (int)Math.Floor(span / step + 1e-6);
        for (var i = 0; i <= count; i++)
        {
            var beat = fromBeat + i * step;
            list.Add(new PianoRollPoint(beat, Clamp(lane, (int)Math.Round(fromValue + (toValue - fromValue) * (span > 0 ? (beat - fromBeat) / span : 0)))));
        }
        if (count * step < span - 1e-6) list.Add(new PianoRollPoint(toBeat, Clamp(lane, toValue)));
        return list.OrderBy(p => p.Beat).ToList();
    }

    /// <summary>A stepped program point at <paramref name="beat"/> (a click sets the value from that beat on):
    /// the point at that beat is replaced, the rest of the lane kept.</summary>
    public static List<PianoRollPoint> DrawStep(IReadOnlyList<PianoRollPoint> points, int lane, double beat, int value)
    {
        var list = points.Where(p => Math.Abs(p.Beat - beat) > 1e-6).ToList();
        list.Add(new PianoRollPoint(beat, Clamp(lane, value)));
        return list.OrderBy(p => p.Beat).ToList();
    }

    /// <summary>The points left after erasing the span from <paramref name="fromBeat"/> to <paramref name="toBeat"/>.</summary>
    public static List<PianoRollPoint> Erase(IReadOnlyList<PianoRollPoint> points, double fromBeat, double toBeat) =>
        points.Where(p => p.Beat < Math.Min(fromBeat, toBeat) - 1e-6 || p.Beat > Math.Max(fromBeat, toBeat) + 1e-6).ToList();

    /// <summary>The value the lane holds at <paramref name="beat"/>: the last point at or before it, else the resting value.</summary>
    public static int ValueAt(IReadOnlyList<PianoRollPoint> points, int lane, double beat)
    {
        var value = Centre(lane);
        foreach (var point in points) { if (point.Beat > beat + 1e-9) break; value = point.Value; }
        return value;
    }
}

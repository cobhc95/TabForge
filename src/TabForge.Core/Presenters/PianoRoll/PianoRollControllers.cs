using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the piano roll's controller lanes for a MIDI track: the lanes offered (velocity, CC1 modulation, CC7 volume, CC10 pan, CC11 expression,
//       CC64 sustain, pitch bend) with their value ranges, reading a controller's points from the shown clips on the performed clock, writing them
//       back (each point to the clip that holds it, which grows to hold it), and the lane edits: a drawn line (points at each step, replacing the
//       old ones in its span) and an erased span.
// Does not own: the clip model (AudioClip.Controllers), the playback of the points (ScoreToMidiCompiler.Clips), the lane drawing and gestures (the app).
// Tests: TabForge.Avalonia.Tests.PianoRollControllerTests.

/// <summary>One controller point at <paramref name="Beat"/> on the piano roll's clock.</summary>
public readonly record struct PianoRollPoint(double Beat, int Value);

public static class PianoRollControllers
{
    public const int Velocity = -1;
    public const int PitchBend = ClipController.PitchBend;

    /// <summary>The lanes a MIDI track offers, in order (a tab track offers only <see cref="Velocity"/>).</summary>
    public static IReadOnlyList<(int Id, string Name)> Lanes { get; } = new[]
    {
        (Velocity, "Velocity"), (1, "CC1 Modulation"), (7, "CC7 Volume"), (10, "CC10 Pan"), (11, "CC11 Expression"), (64, "CC64 Sustain"), (PitchBend, "Pitch bend"),
    };

    public static string NameOf(int lane) => Lanes.FirstOrDefault(l => l.Id == lane).Name ?? "CC" + lane;

    public static int Min(int lane) => lane == PitchBend ? -8192 : lane == Velocity ? 1 : 0;

    public static int Max(int lane) => lane == PitchBend ? 8191 : 127;

    /// <summary>The lane's resting value, drawn as its centre line (pitch bend 0, pan 64).</summary>
    public static int Centre(int lane) => lane == PitchBend ? 0 : lane == 10 ? 64 : 0;

    public static int Clamp(int lane, int value) => Math.Clamp(value, Min(lane), Max(lane));

    /// <summary>The points of <paramref name="controller"/> in the shown clips, by beat.</summary>
    public static List<PianoRollPoint> Read(TrackModel track, PianoRollTimeMap map, int controller)
    {
        var list = new List<PianoRollPoint>();
        foreach (var clip in PianoRollClipSource.Shown(track))
            foreach (var point in clip.Controllers ?? new List<ClipController>())
            {
                if (point.Controller != controller || point.StartSec < clip.OffsetSec - 1e-6 || point.StartSec >= clip.OffsetSec + clip.SourceLengthSec) continue;
                list.Add(new PianoRollPoint(map.BeatsAt(clip.StartSec + (point.StartSec - clip.OffsetSec) / PianoRollClipSource.Speed(clip)), Clamp(controller, point.Value)));
            }
        return list.OrderBy(p => p.Beat).ToList();
    }

    /// <summary>Stores <paramref name="points"/> as <paramref name="controller"/>'s points in the shown clips (the other controllers kept); false when nothing changed.</summary>
    public static bool Write(TrackModel track, PianoRollTimeMap map, int controller, IReadOnlyList<PianoRollPoint> points)
    {
        var before = Read(track, map, controller);
        if (before.Count == points.Count && before.Zip(points).All(p => Math.Abs(p.First.Beat - p.Second.Beat) < 1e-6 && p.First.Value == p.Second.Value)) return false;
        var timed = points.Select(p => (Point: p, Sec: map.SecondsAt(p.Beat))).ToList();
        var clips = PianoRollClipSource.Shown(track);
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

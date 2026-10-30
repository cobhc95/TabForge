using TabForge.Models;

namespace TabForge.Services;

// Owns: the MIDI import's non-note data: a MIDI track's one clip (notes, controller / pitch-bend points), the count of points a tab track drops, and the keyswitch notes kept out of the score.
// Does not own: reading the file (SmfReader), the score notes (MidiToSong) or the lane model (AudioClipModel.ClipController).
// Tests: MidiImportExtrasTests, MidiTechniqueMarkerTests.
internal static class MidiImportExtras
{
    /// <summary>Pitch ceiling of a keyswitch note: below any normal guitar/bass part's playable range.</summary>
    private const int KeyswitchPitchMax = 24;
    private const long KeyswitchMaxTicks = 10;
    private const int KeyswitchMaxVelocity = 110;
    private const long KeyswitchLeadTicks = 100;

    /// <summary>
    /// Very short, low-velocity notes in the keyswitch range that immediately precede a real note are an articulation map's keyswitches,
    /// not music. They are removed from the score (the caller reports how many) so they do not pollute it. A normal low note (longer,
    /// louder or with no following note in range) is left alone.
    /// </summary>
    public static List<SmfNote> WithoutKeyswitches(List<SmfNote> notes, ImportContext context)
    {
        if (notes.Count < 2) return notes;
        var drop = new bool[notes.Count];
        var moved = 0;
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (note.Pitch > KeyswitchPitchMax || note.Velocity > KeyswitchMaxVelocity || note.End - note.Start > KeyswitchMaxTicks) continue;
            for (var j = i + 1; j < notes.Count && notes[j].Start - note.Start <= KeyswitchLeadTicks; j++)
            {
                if (notes[j].Channel != note.Channel) continue;
                if (notes[j].Pitch > KeyswitchPitchMax) { drop[i] = true; moved++; }
                break;
            }
        }
        if (moved == 0) return notes;
        context.KeyswitchNotesMoved += moved;
        context.KeyswitchNotice = $"{moved:N0} keyswitch note{(moved == 1 ? "" : "s")} in the low keyswitch range were left out of the score (they are articulation switches, not music).";
        return notes.Where((_, i) => !drop[i]).ToList();
    }

    private static bool Belongs(SmfController point, int? partChannel, bool drums) =>
        drums ? point.Channel == 9 : partChannel is int only ? point.Channel == only : point.Channel != 9;

    /// <summary>How many CC and pitch-bend points the part has (a tab track cannot hold them).</summary>
    public static int CountControllers(SmfTrack source, int? partChannel, bool drums) =>
        source.Controllers.Count(point => Belongs(point, partChannel, drums));

    /// <summary>
    /// A MIDI track's one clip: every note of the part plus its CC and pitch-bend points, in seconds through the file's own tempo map, so they
    /// play back, show in the piano roll and export again. The track's written bars stay empty.
    /// </summary>
    public static void AddMidiClip(TrackModel track, SmfTrack source, SmfFile smf, int? partChannel, bool drums, List<SmfNote> notes)
    {
        var clock = new TickClock(smf);
        var clipNotes = notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).Select(n =>
        {
            var start = Math.Max(0, clock.Ms(n.Start)) / 1000.0;
            return new ClipNote(start, Math.Max(0.001, clock.Ms(Math.Max(n.End, n.Start + 1)) / 1000.0 - start), n.Pitch, Math.Clamp(n.Velocity, 1, MidiLimits.Max));
        }).ToList();
        var points = new List<ClipController>();
        foreach (var point in source.Controllers)
        {
            if (!Belongs(point, partChannel, drums)) continue;
            var at = Math.Max(0, clock.Ms(point.Tick)) / 1000.0;
            if (point.Kind == 0xE0) points.Add(new ClipController(at, ClipController.PitchBend, Math.Clamp(point.Value, -8192, 8191)));
            else points.Add(new ClipController(at, Math.Clamp(point.Controller, 0, MidiLimits.Max), Math.Clamp(point.Value, 0, MidiLimits.Max)));
        }
        var length = Math.Max(0.001, Math.Max(clipNotes.Count == 0 ? 0 : clipNotes.Max(n => n.StartSec + n.LengthSec), points.Count == 0 ? 0 : points.Max(p => p.StartSec) + 0.001));
        track.AudioClips.Add(new AudioClip
        {
            Name = track.Name, StartSec = 0, SourceLengthSec = length, FileLengthSec = length, Lane = 0,
            Notes = clipNotes, Controllers = points.Count == 0 ? null : points.OrderBy(p => p.StartSec).ToList(),
        });
        track.Lanes.Add(new ClipLane());
    }

    /// <summary>Tick to milliseconds through the file's own tempo map (the default 500,000 µs/quarter before the first tempo).</summary>
    private sealed class TickClock
    {
        private readonly List<long> _ticks = new() { 0 };
        private readonly List<int> _mpq = new() { 500_000 };
        private readonly List<double> _ms = new() { 0 };
        private readonly int _division;

        public TickClock(SmfFile smf)
        {
            _division = Math.Max(1, smf.Division);
            foreach (var (change, mpq) in smf.Tempos.OrderBy(t => t.Tick))
            {
                if (change <= _ticks[^1]) { _mpq[^1] = mpq; continue; }
                _ms.Add(_ms[^1] + (change - _ticks[^1]) * (double)_mpq[^1] / _division / 1000.0);
                _ticks.Add(change);
                _mpq.Add(mpq);
            }
        }

        public double Ms(long tick)
        {
            var i = _ticks.BinarySearch(Math.Max(0, tick));
            if (i < 0) i = ~i - 1;
            return _ms[i] + (Math.Max(0, tick) - _ticks[i]) * (double)_mpq[i] / _division / 1000.0;
        }
    }
}

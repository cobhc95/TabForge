using System.Text;
using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the GP4/GP5 mix-table details the score library discards (transition lengths, apply-to-all flags, chorus and reverb).
// Does not own: the rest of the import.
// Tests: TestMixFadeAndAudiblePlayhead.
/// <summary>
/// GP4/GP5 mix-table details alphaTab reads and throws away: the transition length (in beats) of each
/// value, the "apply to all tracks" flags, and chorus / reverb / phaser / tremolo. A reference fade-out is exactly
/// this (volume 8 → 4 → 2 → 0, each over 16 beats, on all tracks), so without them it imported as a hard
/// volume drop on one track. alphaTab keeps the mix tables' volume/balance values in file order, so each
/// such beat is matched to the next raw mix table in the file with the same volume and balance.
/// </summary>
public static class GuitarProMixTableScanner
{
    public sealed record RawMix(int Volume, int Balance, int Chorus, int Reverb, int Phaser, int Tremolo, int Tempo, int TransitionBeats, bool AllTracks, int TempoTransitionBeats = 0);

    /// <summary>Raw mix-table data per alphaTab beat (by reference); empty for anything but GP4/GP5.</summary>
    public static Dictionary<object, RawMix> Scan(byte[] data, Score score)
    {
        var result = new Dictionary<object, RawMix>(ReferenceEqualityComparer.Instance);
        var version = data.Length > 31 ? Encoding.ASCII.GetString(data, 1, Math.Min(data[0], (byte)30)) : "";
        var gp5 = version.Contains("v5.", StringComparison.Ordinal);
        var hideTempoByte = gp5 && !version.Contains("v5.00", StringComparison.Ordinal);
        if (!gp5 && !version.Contains("v4.", StringComparison.Ordinal)) return result;

        var cursor = 0;
        for (var bar = 0; bar < score.MasterBars.Count; bar++)
            foreach (var track in score.Tracks)
                foreach (var staff in track.Staves)
                {
                    if (bar >= staff.Bars.Count) continue;
                    foreach (var voice in staff.Bars[bar].Voices)
                        foreach (var beat in voice.Beats)
                        {
                            int vol = -1, bal = -1;
                            foreach (var a in beat.Automations)
                            {
                                if (a.Type == AutomationType.Volume) vol = (int)a.Value;
                                else if (a.Type == AutomationType.Balance) bal = (int)a.Value;
                            }
                            if (vol < 0 && bal < 0) continue;
                            for (var p = cursor; p < data.Length - 16; p++)
                            {
                                if ((sbyte)data[p] != vol || (sbyte)data[p + 1] != bal) continue;
                                if (TryParse(data, p, gp5, hideTempoByte, out var mix, out var end)) { result[beat] = mix; cursor = end; break; }
                            }
                        }
                }
        return result;
    }

    /// <summary>
    /// Finds the transition length (beats) of each tempo change, in file order. alphaTab keeps tempo changes as
    /// master-bar automations without their duration, so each one is matched to the next raw mix table
    /// carrying the same tempo. Returns 0 (instant) when nothing matches or for non-GP4/GP5 data.
    /// </summary>
    public sealed class TempoRampFinder
    {
        private readonly byte[] _data;
        private readonly bool _gp5, _hideTempoByte, _enabled;
        private int _cursor;

        public TempoRampFinder(byte[] data)
        {
            _data = data;
            var version = data.Length > 31 ? Encoding.ASCII.GetString(data, 1, Math.Min(data[0], (byte)30)) : "";
            _gp5 = version.Contains("v5.", StringComparison.Ordinal);
            _hideTempoByte = _gp5 && !version.Contains("v5.00", StringComparison.Ordinal);
            _enabled = _gp5 || version.Contains("v4.", StringComparison.Ordinal);
        }

        /// <summary>Transition beats of the next tempo change (which sets <paramref name="tempo"/>), 0 if instant/unknown.</summary>
        public int Next(int tempo)
        {
            if (!_enabled) return 0;
            for (var p = _cursor; p < _data.Length - 16; p++)
            {
                if (!TryParse(_data, p, _gp5, _hideTempoByte, out var mix, out var end) || mix.Tempo != tempo) continue;
                _cursor = end;
                return Math.Clamp(mix.TempoTransitionBeats, 0, 64);
            }
            return 0;
        }
    }

    /// <summary>Parses a mix table from its volume byte: six values, tempo name, tempo, durations, flags.</summary>
    internal static bool TryParse(byte[] d, int p, bool gp5, bool hideTempoByte, out RawMix mix, out int end)
    {
        mix = null!; end = p;
        var v = new int[6];
        for (var i = 0; i < 6; i++) { v[i] = (sbyte)d[p + i]; if (v[i] < -1) return false; }
        var q = p + 6;
        if (gp5)
        {
            if (q + 5 > d.Length) return false;
            var size = BitConverter.ToInt32(d, q);
            if (size < 1 || size > 64 || d[q + 4] > size - 1) return false;
            q += 4 + size;
        }
        if (q + 4 > d.Length) return false;
        var tempo = BitConverter.ToInt32(d, q); q += 4;
        if (tempo != -1 && tempo is < 1 or > 1000) return false;
        var duration = 0; var first = true; var tempoDuration = 0;
        for (var i = 0; i < 7; i++)
        {
            if ((i < 6 ? v[i] : tempo) < 0) continue;
            if (q >= d.Length) return false;
            var dur = (sbyte)d[q++];
            if (dur is < 0 or > 64) return false;
            if (first && i < 6) { duration = dur; first = false; }
            if (i == 6) tempoDuration = dur;
            if (i == 6 && hideTempoByte) q++; // GP5.10 "hide tempo" flag
        }
        if (q >= d.Length) return false;
        var flags = d[q++];
        var allTracks = v[0] >= 0 ? (flags & 1) != 0 : v[1] >= 0 && (flags & 2) != 0;
        mix = new RawMix(v[0], v[1], v[2], v[3], v[4], v[5], tempo, duration, allTracks, tempoDuration);
        end = q;
        return true;
    }
}

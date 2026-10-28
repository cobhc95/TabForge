using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the track headers of the older binary formats and the 64-entry mixer channel table the tracks point into.
// Does not own: the beats and notes (LegacyGpWriter.Beats, LegacyGpWriter.Notes) or the file header (LegacyGpWriter).
// Tests: TestLegacyGpExport.
internal sealed partial class LegacyGpWriter
{
    private const int ChannelCount = 64;
    private const int DrumChannelIndex = 9;
    /// <summary>The strings a track can have in the 3-5 formats; a drum track is written with six.</summary>
    private const int MaxStrings = 7;
    private const int DrumStrings = 6;

    private readonly (int Program, int Volume, int Balance, int Chorus, int Reverb)[] _table = new (int, int, int, int, int)[ChannelCount];
    private (int Port, int Channel, int Effect)[] _channels = Array.Empty<(int, int, int)>();

    private static Staff StaffOf(Track track) => track.Staves[0];

    private static string NameOf(Track track, int index) => string.IsNullOrWhiteSpace(track.Name) ? $"Track {index + 1}" : track.Name;

    /// <summary>The strings written for a track: all of them up to seven; a drum track has six lanes.</summary>
    private static int StringsOf(Track track) => StaffOf(track).IsPercussion ? DrumStrings : Math.Min(MaxStrings, Math.Max(1, StaffOf(track).Tuning.Count));

    /// <summary>A 0..127 level as the 0..16 step of the channel table.</summary>
    private static int Step(int value127) => Math.Clamp((int)Math.Round(value127 * 16 / 127.0), 0, 16);

    /// <summary>Gives every track its own mixer channel and a spare one for effects (drums on the percussion channel), as the program itself does.</summary>
    private void AllocateChannels()
    {
        for (var i = 0; i < ChannelCount; i++) _table[i] = (25, 13, 8, 0, 0);
        var free = Enumerable.Range(0, ChannelCount).Where(i => i % 16 != DrumChannelIndex).ToList();
        var drums = Enumerable.Range(0, 4).Select(p => p * 16 + DrumChannelIndex).ToList();
        _channels = new (int, int, int)[_score.Tracks.Count];
        var next = 0;
        int Take(List<int> pool) => pool.Count == 0 ? 0 : pool[next++ % pool.Count];
        for (var t = 0; t < _score.Tracks.Count; t++)
        {
            var track = _score.Tracks[t];
            int primary, effect;
            if (StaffOf(track).IsPercussion) { primary = drums[Math.Min(drums.Count - 1, _score.Tracks.Take(t).Count(x => StaffOf(x).IsPercussion))]; effect = primary + 1; }
            else
            {
                if (next + 1 >= free.Count) { _track = NameOf(track, t); Hit("Mixer channels", "more tracks than the format has mixer channels: this track shares one"); }
                primary = Take(free); effect = Take(free);
            }
            var info = track.PlaybackInfo;
            var entry = ((int)info.Program, (int)info.Volume, (int)info.Balance, t < _extras.Chorus.Count ? Step(_extras.Chorus[t]) : 0, t < _extras.Reverb.Count ? Step(_extras.Reverb[t]) : 0);
            _table[primary] = entry; _table[effect] = entry;
            _channels[t] = (primary / 16 + 1, primary % 16 + 1, effect % 16 + 1);
        }
        _track = "whole song";
    }

    private void WriteChannelTable()
    {
        foreach (var (program, volume, balance, chorus, reverb) in _table)
        {
            _w.Int(program); _w.Byte(Math.Clamp(volume, 0, 16)); _w.Byte(Math.Clamp(balance, 0, 16)); _w.Byte(chorus); _w.Byte(reverb);
            _w.Byte(0); _w.Byte(0); _w.Zeros(2);                                     // phaser, tremolo, two unused bytes
        }
    }

    private void WriteTracks()
    {
        for (var t = 0; t < _score.Tracks.Count; t++)
        {
            var track = _score.Tracks[t]; var staff = StaffOf(track); var drums = staff.IsPercussion;
            _track = NameOf(track, t);
            if (_score.Tracks[t].Staves.Count > 1) Hit("More than one staff on a track", "only the first staff is written");
            var flags = (drums ? 0x01 : 0) | 0x08 | (track.PlaybackInfo.IsSolo ? 0x10 : 0) | (track.PlaybackInfo.IsMute ? 0x20 : 0);
            _w.Byte(flags);
            _w.ByteStringPadded(track.Name, 40);
            var strings = StringsOf(track);
            if (!drums && staff.Tuning.Count > MaxStrings) Hit("More than seven strings", $"strings {MaxStrings + 1} and up are left out, with their notes");
            _w.Int(strings);
            for (var i = 0; i < MaxStrings; i++) _w.Int(i < strings ? (drums ? new[] { 64, 59, 55, 50, 45, 40 }[i] : (i < staff.Tuning.Count ? (int)staff.Tuning[i] : 0)) : 0);
            var (port, channel, effect) = _channels[t];
            _w.Int(port); _w.Int(channel); _w.Int(effect);
            _w.Int(24); _w.Int(Math.Clamp((int)staff.Capo, 0, 24));
            var color = track.Color;
            _w.Byte((int)color.R); _w.Byte((int)color.G); _w.Byte((int)color.B); _w.Byte(0);
            if (!V5) continue;
            _w.Short((staff.ShowTablature ? 0x01 : 0) | (staff.ShowStandardNotation ? 0x02 : 0));   // what the staff shows
            _w.Byte(0); _w.Byte(0);                                                  // automatic accentuation, sound bank
            _w.Byte(0); _w.Zeros(12); _w.Zeros(12); _w.Zeros(16);                    // sound engine: humanise, unused ints, instrument
            if (_version >= 510) { _w.Zeros(4); _w.IntByteString(""); _w.IntByteString(""); }   // equaliser, effect name and category
        }
        _track = "whole song";
    }
}

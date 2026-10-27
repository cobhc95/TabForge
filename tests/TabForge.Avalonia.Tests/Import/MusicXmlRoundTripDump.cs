using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Avalonia.Tests;

// Owns: a text snapshot of the parts of a song MusicXML can carry, so a TabForge export read back can be compared line by line.
// Does not own: the export or the import.
internal static class MusicXmlRoundTripDump
{
    /// <summary>One line per fact of the song: header, each track, each bar and each beat that holds a note or a rest.</summary>
    public static List<string> Lines(SongProject song, bool mixer = true)
    {
        var lines = new List<string> { $"song title={song.Title} artist={song.Artist} album={song.Album} music={song.MusicAuthor} words={song.LyricsAuthor} copyright={song.Copyright} tempo={song.Tempo} tracks={song.NotationTracks.Count()}" };
        var markers = song.Markers.GroupBy(m => m.MeasureIndex).ToDictionary(g => g.Key, g => g.Last().Title);
        var t = 0;
        foreach (var track in song.NotationTracks)
        {
            t++;
            lines.Add($"T{t} name={track.Name} kind={track.Kind} prog={track.MidiProgram} tuning={(track.IsDrumTrack ? "kit" : string.Join(",", track.StringTunings))} capo={track.Capo} colour={track.ColorHex} follows={track.ColorFollowsInstrument} channel={track.MidiChannel}"
                + (mixer ? $" vol={track.Volume} pan={track.Pan} mute={track.Mute} solo={track.Solo}" : ""));
            var tempoNow = song.Tempo;
            for (var b = 0; b < track.Measures.Count; b++)
            {
                var m = track.Measures[b];
                var shownTempo = m.TempoChange is { } change && change != tempoNow ? change : (int?)null;
                if (m.TempoChange is { } latest) tempoNow = latest;
                if (t == 1)
                    lines.Add($"T{t}B{b + 1} time={m.TimeSigNum ?? song.TimeSignatureNumerator}/{m.TimeSigDenom ?? song.TimeSignatureDenominator} key={m.KeySignature ?? song.KeySignature}{(m.KeySignatureMinor ?? song.KeySignatureMinor ? "m" : "")} tempo={shownTempo} "
                        + $"rep={(m.RepeatStart ? "[" : "")}{(m.RepeatEnd ? "]" + m.RepeatCount : "")} ending={m.EndingPasses} section={(markers.TryGetValue(b, out var s) ? s : "")} clef={(m.Clef == "G2" ? Clefs.Treble : m.Clef)} dbl={m.IsDoubleBar} anac={m.Anacrusis} tf={m.TripletFeelKind} mid={m.MidBarTempos?.Count} dir={string.Join(",", TabForge.Playback.PlaybackOrder.DirectionTokens(m.Directions).OrderBy(x => x, StringComparer.Ordinal))}");
                foreach (var (voice, cells) in new[] { (1, m.Cells), (2, m.Voice2Cells) })
                    for (var c = 0; c < cells.Count; c++)
                    {
                        var cell = cells[c];
                        if (cell.Notes.Count == 0 && !cell.IsRest) continue;
                        if (cell.IsRest && cell.Notes.Count == 0 && cells.All(x => x.Notes.Count == 0)) continue;   // a bar of rests only is written as one whole-bar rest, which reads back as an empty bar
                        var at = cell.RhythmicPosition ?? c;
                        var sb = new StringBuilder($"T{t}B{b + 1}V{voice}@{at:0.###} {(cell.IsRest ? "rest" : "notes")} d={cell.DurationDenominator}.{cell.Dots} tup={cell.Tuplet.Numerator}:{cell.Tuplet.Denominator} acc={cell.Accent} stac={cell.Staccato} fer={cell.Fermata} chord={cell.ChordName?.Replace(" ", "_")} text={cell.Text?.Trim().Replace(" ", "_").Replace("\n", "/")} lyr={cell.Lyrics.Replace(" ", "_").Replace("\n", "/")} oct={cell.OctaveShiftSemitones} wh={cell.WhammyPoints.Count} trem={cell.TremoloPickDenominator} ten={cell.Tenuto} snd={cell.SoundDurationPercent} mix={(cell.Mix is { } mx ? $"{mx.Program}/{mx.Volume}/{mx.Pan}/{mx.Chorus}/{mx.Reverb}/{mx.Phaser}/{mx.Tremolo}/{mx.Tempo}/{mx.TransitionBeats}/{mx.AllTracks}" : "")} beam={cell.BeamMode}/{cell.BreakSecondaryBeamBefore} stem={cell.StemDirection} brush={cell.BrushStepSlots} wp={string.Join(",", cell.WhammyPoints.Select(w => $"{w.Offset}:{w.Value}"))}");
                        // A keys track has no strings: its string and fret are only a way to show the pitch, and MusicXML carries the pitch alone.
                        foreach (var n in cell.Notes.OrderBy(n => n.IsGraceNote ? 0 : 1).ThenBy(n => track.Kind == TrackKind.Keys ? n.MidiValue : n.StringIndex).ThenBy(n => n.Fret))
                            sb.Append($" | {(track.Kind == TrackKind.Keys ? "s-f-" : $"s{n.StringIndex}f{n.Fret}")} midi={n.MidiValue} vel={n.Velocity} tied={n.Tied} dead={n.Dead} ghost={n.Ghost} grace={n.IsGraceNote} bend={n.BendPoints.Count} [{string.Join(",", n.Techniques.Where(x => x != "Tie" && !(x == "Ghost" && n.Ghost)).OrderBy(x => x, StringComparer.Ordinal))}]");
                        lines.Add(sb.ToString());
                    }
            }
        }
        return lines;
    }
}

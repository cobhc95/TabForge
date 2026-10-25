using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using static TabForge.Services.GuitarProBarConverter;
using static TabForge.Services.GuitarProImporter;
using static TabForge.Services.GuitarProReflection;

namespace TabForge.Services;

// Owns: converting one imported track (tuning, capo, instrument, mixer values) into the song model.
// Does not own: bar and beat conversion.
// Tests: no dedicated test class.
/// <summary>Converts one alphaTab track into a <see cref="TrackModel"/>: its tuning, colour, instrument kind and, bar by bar, its content.</summary>
internal static class GuitarProTrackConverter
{
    internal static TrackModel ConvertTrack(List<object> masterBars, object sourceTrack, int fallbackChannel, ImportBudget budget)
    {
        var name = TrackName(GetString(sourceTrack, "Name"));
        var staves = AsObjects(Get(sourceTrack, "Staves")).Take(InputLimits.MaxStringsPerTrack + 1).ToList();
        if (staves.Count > InputLimits.MaxStringsPerTrack)
            throw new InvalidDataException("A score track contains too many staves or strings.");
        var isPercussion = GetBool(sourceTrack, "IsPercussion", false);

        // Tuning: alphaTab exposes it on the staff; fall back to a sane default per track kind.
        var tuning = ReadTuning(staves);

        var maxString = tuning.Count > 0 ? tuning.Max() : 0;
        var lowerName = name.ToLowerInvariant();
        // Percussion is the file's own flag (channel 10), never the name: a melodic "Drum Fill (Bell)" track stays melodic.
        var isDrums = isPercussion;
        // The file's own MIDI program is authoritative (0 = Acoustic Grand Piano is a real program).
        // A few strings does not make a bass: mandolin, violin and ukulele have 4.
        var program = Math.Clamp(GetInt(Get(sourceTrack, "PlaybackInfo"), "Program", 0), 0, MidiLimits.Max);
        // A piano, synth or pad exported with the pitch-only octave ladder is not fretted (it would come back as a 7-string guitar).
        var isBass = !isDrums && (lowerName.Contains("bass") || program is >= 32 and <= 39);
        var fretted = !isDrums && tuning.Count > 0 && (isBass || !TrackInstrumentRules.IsPitchOnlyTuning(tuning));
        var kind = isDrums ? TrackKind.Drums : isBass ? TrackKind.Bass : fretted ? TrackKind.Guitar : TrackKind.Keys;
        var midiProgram = isDrums ? 0 : program;

        var result = NewTrackModel(sourceTrack, name, kind, isDrums, isBass, tuning, midiProgram, fallbackChannel, budget);

        var barByIndex = CollectStaffBars(staves, result);

        AddMeasures(result, barByIndex, masterBars.Count, budget);

        LinkTieOrigins(result);

        // The song's very first beat carries the track's initial mixer values, not a Mix Table change.
        if (result.Measures.FirstOrDefault()?.Cells.FirstOrDefault(c => c.Notes.Count > 0 || c.IsRest || c.Mix is not null) is { } opening)
            opening.Mix = null;

        if (fretted && result.StringTunings.Count > 0) InferTuningFromNotes(result);

        // Number of frets (from the highest fret actually used, with a 24-fret default).
        var highestFret = result.Measures
            .SelectMany(m => m.Cells)
            .SelectMany(c => c.Notes)
            .Select(n => n.Fret)
            .DefaultIfEmpty(0)
            .Max();
        result.NumberOfFrets = Math.Clamp(Math.Max(24, highestFret), 12, 36);

        return result;
    }

    /// <summary>The track with its instrument, colour, mixer values (Guitar Pro 0..16 steps or the exact fractions), sends and tuning, before any bar is read.</summary>
    private static TrackModel NewTrackModel(object sourceTrack, string name, TrackKind kind, bool isDrums, bool isBass, List<int> tuning, int midiProgram, int fallbackChannel, ImportBudget budget)
    {
        // Guitar Pro stores mixer volume/balance on a 0..16 scale (8 = centre). TabForge used to
        // discard these entirely, so every imported track played at the default volume and centre pan.
        var playbackInfo = Get(sourceTrack, "PlaybackInfo");
        var gpVolume = GetInt(playbackInfo, "Volume", 16);
        var gpBalance = GetInt(playbackInfo, "Balance", 8);
        // A Guitar Pro 7/8 file (gpif) carries the exact fractions (TabForge.AlphaTab patch 0002); -1 = not known (Guitar Pro 3-5 files): the 0..16 steps are used.
        var gpVolumeFraction = GetDouble(playbackInfo, "VolumeFraction", -1);
        var gpBalanceFraction = GetDouble(playbackInfo, "BalanceFraction", -1);
        // Chorus and reverb come from the file's channel block (Guitar Pro 3-5 only; TuxGuitar leaves the others at 0).
        var channelSends = GuitarProChannelBlock.For(budget.Context.ChannelBlock, GetInt(playbackInfo, "Program", 0), gpVolume, gpBalance);

        return new TrackModel
        {
            Name = name,
            Kind = kind,
            MidiChannel = isDrums ? 9 : fallbackChannel,
            MidiProgram = midiProgram,
            InstrumentName = isDrums ? GeneralMidi.DrumKit
                : InstrumentNaming.ForStringCount(GeneralMidi.NameOf(midiProgram), kind, tuning.Count),
            ColorHex = ReadColor(sourceTrack),
            MidiOutputDeviceId = -1,
            Volume = gpVolumeFraction >= 0 ? GpMixerScale.FromFraction(gpVolumeFraction) : GuitarProChannelBlock.ToMidi(gpVolume),   // truncated, as Guitar Pro and TuxGuitar do (step 6 is 47, not 48)
            Pan = gpBalanceFraction >= 0 ? GpMixerScale.FromFraction(gpBalanceFraction) : Math.Clamp((int)Math.Round(gpBalance * 127.0 / 16.0), 0, MidiLimits.Max),
            Chorus = channelSends is { } sends ? GuitarProChannelBlock.ToMidi(sends.Chorus) : 0,
            Reverb = channelSends is { } sent ? GuitarProChannelBlock.ToMidi(sent.Reverb) : 0,
            // The mixer's mute and solo buttons are part of the file (the exporter wrote them; the importer used to drop them).
            Mute = GetBool(playbackInfo, "IsMute", false),
            Solo = GetBool(playbackInfo, "IsSolo", false),
            StringTunings = isDrums ? new List<int> { 49, 42, 48, 38, 43, 36 } : tuning.Count >= 4 ? tuning : DefaultTuning(kind, isBass ? 4 : 6),
            // (no-string instruments get octave-spaced "strings" so every pitch has a readable TAB number)
            Rig = new RigPreset
            {
                Name = isDrums ? "Imported Drum Kit" : isBass ? "Imported Bass" : "Imported Guitar",
                ArticulationMap = isDrums ? "GM Drums" : isBass ? "Generic Bass" : "Generic Guitar"
            }
        };
    }

    /// <summary>Groups the bars of every staff of the track by bar index (nothing is dropped) and takes the capo and the string tuning of the staves.</summary>
    private static Dictionary<int, List<object>> CollectStaffBars(List<object> staves, TrackModel result)
    {
        var barByIndex = new Dictionary<int, List<object>>();
        foreach (var staff in staves)
        {
            var staffBars = AsObjects(Get(staff, "Bars"))
                .Take(InputLimits.MaxMeasuresPerTrack + 1).ToList();
            if (staffBars.Count > InputLimits.MaxMeasuresPerTrack)
                throw new InvalidDataException("The score file contains too many measures in a track.");
            for (var i = 0; i < staffBars.Count; i++)
            {
                if (!barByIndex.TryGetValue(i, out var list)) barByIndex[i] = list = new List<object>();
                list.Add(staffBars[i]);
            }
            var capo = GetInt(staff, "Capo", 0);
            if (capo > 0 && result.Capo == 0) result.Capo = capo;
            var staffTuning = ReadStaffTuning(staff);
            if (staffTuning.Count >= 4 && result.Kind != TrackKind.Drums) result.StringTunings = staffTuning;
        }
        return barByIndex;
    }

    private static void AddMeasures(TrackModel result, Dictionary<int, List<object>> barByIndex, int masterBarCount, ImportBudget budget)
    {
        var barCount = Math.Max(barByIndex.Count, masterBarCount);
        if (barCount == 0) barCount = 32;
        if (barCount > InputLimits.MaxMeasuresPerTrack)
            throw new InvalidDataException("The score file contains too many measures in a track.");
        budget.AddMeasures(barCount);

        for (var i = 0; i < barCount; i++)
        {
            budget.AddCells(16);
            var measure = new MeasureModel { Number = i + 1 };
            if (barByIndex.TryGetValue(i, out var bars))
            {
                var measureNotes = 0;
                foreach (var bar in bars)
                    ConvertBar(bar, measure, result, budget, ref measureNotes);
            }
            result.Measures.Add(measure);
        }
    }

    /// <summary>Tuning check against the notes themselves: each note sounding pitch minus its fret is the open pitch of its string; consistent open pitches win over a tuning field that was not readable.</summary>
    private static void InferTuningFromNotes(TrackModel result)
    {
        var inferred = result.StringTunings.ToList();
        var changed = false;
        for (var s = 0; s < inferred.Count; s++)
        {
            var opens = result.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes)
                // Harmonics sound above their string (an octave or more) and a tie destination carries its origin's pitch: neither says
                // anything about the open string, and a string played mostly in harmonics came out tuned an octave high (59 -> 71 -> 83).
                .Where(n => n.StringIndex == s && !n.Dead && !n.Tied && n.HarmonicFret is null && !n.Techniques.Any(t => t.Contains("Harmonic", StringComparison.Ordinal))
                    && n.MidiValue > 0 && result.Capo == 0)
                .Select(n => n.MidiValue - n.Fret).GroupBy(v => v).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (opens is null || opens.Count() < 3 || opens.Key == inferred[s] || opens.Key is < 12 or > 108) continue;
            inferred[s] = opens.Key;
            changed = true;
        }
        if (changed) result.StringTunings = inferred;
    }

    /// <summary>A track name as the project accepts it: the file's control characters (a DEL padding a name) dropped, cut to the title limit.</summary>
    internal static string TrackName(string? raw)
    {
        var name = new string((raw ?? "").Where(c => !char.IsControl(c)).ToArray());
        if (name.Length > InputLimits.MaxTitleLength) name = name[..InputLimits.MaxTitleLength];
        return string.IsNullOrWhiteSpace(name) ? "Imported Track" : name;
    }

    internal static List<int> ReadTuning(List<object> staves)
    {
        foreach (var staff in staves)
        {
            var tuning = ReadStaffTuning(staff);
            if (tuning.Count >= 4) return tuning;
        }
        return new List<int>();
    }

    internal static List<int> ReadStaffTuning(object staff)
    {
        // alphaTab: Staff.StringTuning is a Tuning object whose Tunings list holds the values; Staff.Tuning
        // is the same list. (Reading StringTuning itself yielded nothing, so every file came in as standard.)
        var raw = Get(Get(staff, "StringTuning"), "Tunings") ?? Get(staff, "Tuning") ?? Get(staff, "TuningValues");
        var values = AsObjects(raw).Take(InputLimits.MaxStringsPerTrack + 1)
            .Select(v => ConvertToInt(v, -1)).Where(v => v is >= 0 and <= MidiLimits.Max).ToList();
        if (values.Count > InputLimits.MaxStringsPerTrack)
            throw new InvalidDataException("A score track contains too many strings.");
        // A string with a junk tuning byte (a 6-string bass file listing 4 for its last) stays: dropping it renumbers every note's string.
        if (values.Count is >= 4 and <= InputLimits.MaxStringsPerTrack && values.Count(v => v is >= 12 and <= 108) >= 4)
        {
            // alphaTab already lists strings high -> low, the same order TabForge uses.
            return values;
        }
        return new List<int>();
    }

    internal static string ReadColor(object track)
    {
        var color = Get(track, "Color");
        if (color is AlphaTab.Model.Color c)
        {
            static int Channel(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);
            return TabForge.Models.ColourHex.Format((byte)Channel(c.R), (byte)Channel(c.G), (byte)Channel(c.B));
        }
        var hex = color?.ToString();
        return string.IsNullOrWhiteSpace(hex) ? NamedColourSwatches.TrackDefaultColour : hex!;
    }

    /// <summary>
    /// "Strings" for instruments without a fretboard (piano, whistle, synth): one per octave, so a note
    /// is shown as its semitone within the octave instead of every note collapsing onto fret 0.
    /// </summary>
    internal static readonly int[] PitchOnlyStrings = { 96, 84, 72, 60, 48, 36, 24 };

    /// <summary>Highest octave "string" at or below the pitch; fret = semitones above it.</summary>
    internal static (int StringIndex, int Fret) PlacePitch(IReadOnlyList<int> strings, int midi)
    {
        for (var s = 0; s < strings.Count; s++)
            if (midi >= strings[s]) return (s, midi - strings[s]);
        return (strings.Count - 1, 0);
    }

    internal static List<int> DefaultTuning(TrackKind kind, int count)
    {
        if (kind == TrackKind.Drums) return new List<int> { 49, 46, 42, 38, 36 };
        if (kind == TrackKind.Keys) return new List<int>(PitchOnlyStrings);
        if (kind == TrackKind.Bass)
        {
            var bass = new[] { 43, 38, 33, 28, 23, 18 };
            return bass.Take(Math.Clamp(count, 4, bass.Length)).ToList();
        }
        var guitar = new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25 };
        return guitar.Take(Math.Clamp(count, 4, guitar.Length)).ToList();
    }

    internal static int NextMelodicChannel(int channel)
    {
        do channel = (channel + 1) % 16; while (channel == 9);
        return channel;
    }

}

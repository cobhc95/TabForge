// Owns: the General MIDI program -> track-row icon key mapping (one icon per instrument family) shared by every toolkit's track list.
// Does not own: the artwork (Assets/TrackRowIcons/owner-icons.json), drawing, or the instrument catalogue.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>Maps a GM program (0-127) or a drum kit to the key of its track-row icon. Static, read-only after the type loads.</summary>
public static class TrackIconKeys
{
    /// <summary>The icon of an audio track.</summary>
    public const string AudioKey = "AudioWaveform";
    private const string DrumKitKey = "DrumKit";

    /// <summary>GM program ranges per icon ("Name:0-3,5;..."); drum kits use <see cref="DrumKitKey"/>.</summary>
    private const string ProgramMap =
        "PianoKeys:0-3;RhodesPiano:4,5,7;Harpsichord:6;Xylophone:8-13;Koto1:112;TubularBells:14;Koto2:15,107;" +
        "HammondDrawbars:16-18;ChurchOrgan:19,20;Accordion:21;Harmonica1:23;Harmonica2:22;" +
        "ClassicalHeadstock:24;GibsonHeadstock:25,26;Stratocaster:27,28,31;IbanezHeadstock:29;PointedHeadstock:30;" +
        "AcousticBass:32;PrecisionBass:33,34;RingRaBass:35;StingRayBass:36,37;TB303:38,39;" +
        "Violin:40,41,110;ViolinScroll:44,45,48-51;CelloFHoles:42,43;PedalHarp:46;Timpani:47;" +
        "Choir:52;Microphone:53,54;OrchestraHit:55;Trumpet:56,59,61-63;Trombone:57;Tuba:58;FrenchHorn:60;" +
        "AltoSax:64-67;Eltumory:68,69,111;Clarinet:70,71;Aunbal:72,78;Flute:73;SopranoRecorder:74;PanPipes:75;Whistle:76,77;Ocarina:79;" +
        "Minimoog:80-87;SynthPad:88-95;SynthEffects:96-103;" +
        "Sitar:104;Banjo:105;Shamisen:106;Kalimba:108;Bagpipe:109;" +
        "SteelPan:114;Woodblock:113,115;TaikoDrum:116;SnareDrum:117;DrumKit:118;Cymbal:119;Loudspeaker:120-127";

    private static readonly IReadOnlyList<string> ByProgram = BuildProgramMap();

    private static string[] BuildProgramMap()
    {
        var map = new string[128];
        foreach (var entry in ProgramMap.Split(';'))
        {
            var (name, ranges) = (entry[..entry.IndexOf(':')], entry[(entry.IndexOf(':') + 1)..]);
            foreach (var range in ranges.Split(','))
            {
                var bounds = range.Split('-').Select(int.Parse).ToArray();
                for (var p = bounds[0]; p <= bounds[^1]; p++) map[p] = name;
            }
        }
        return map;
    }

    /// <summary>Icon for a GM program (0-127) or a drum kit.</summary>
    public static string KeyFor(int program, bool drumKit) => drumKit ? DrumKitKey : ByProgram[Math.Clamp(program, 0, MidiLimits.Max)];
}

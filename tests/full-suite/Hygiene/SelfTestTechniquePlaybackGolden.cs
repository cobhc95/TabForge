using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

// TestTechniquePlaybackGolden: the MIDI events and sounding notes the playback compiler produces for every technique and for the combinations
// that interact (ghost + accent, palm mute + length, wah open/close, slap/pop levels, arpeggio and brush, bend + vibrato, harmonics, grace hammer-on).
// Each case is a one-bar song compiled headless (no audio device); every event is hashed with its exact double bits, so a changed constant,
// cast, rounding or order of steps changes the hash. The golden hashes were captured from the compiler
// before its per-technique numbers moved into TechniqueInfo.
public static partial class SelfTest
{
    private static readonly int[] PlaybackGoldenVelocities = { 1, 64, 95, 127 };

    /// <summary>Case name and how the note and its cell are set up. Techniques are written as a "+" list.</summary>
    private static IEnumerable<(string Name, Action<TabCell, TabNote> Setup)> PlaybackGoldenCases()
    {
        static (string, Action<TabCell, TabNote>) Tech(string name, Action<TabCell, TabNote>? extra = null) =>
            (name, (c, n) => { foreach (var tag in name.Split('+', StringSplitOptions.RemoveEmptyEntries)) n.Techniques.Add(tag); extra?.Invoke(c, n); });
        static void Bend(TabCell c, TabNote n) { n.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 }); n.BendPoints.Add(new BendPointModel { Offset = 60, Value = 4 }); n.BendTypeName = "Bend"; }
        static void Chord(TabCell c, TabNote n) { c.Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = 55, Velocity = n.Velocity }); c.Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = 50, Velocity = n.Velocity }); }
        static TabNote Principal(TabNote n, bool ghost = false, params string[] tags)
        {
            var p = new TabNote { StringIndex = 1, Fret = 7, MidiValue = 57, Velocity = n.Velocity, Ghost = ghost };
            foreach (var tag in tags) p.Techniques.Add(tag);
            return p;
        }
        foreach (var row in TechniqueInfo.All) yield return Tech(row.Name, row.Name == "Bend" ? Bend : null);
        yield return Tech("PM");
        yield return ("plain", (c, n) => { });
        yield return ("ghost", (c, n) => n.Ghost = true);
        yield return ("ghost+accent1", (c, n) => { n.Ghost = true; c.Accent = 1; });
        yield return ("ghost+accent2", (c, n) => { n.Ghost = true; c.Accent = 2; });
        yield return ("accent1", (c, n) => c.Accent = 1);
        yield return ("accent2", (c, n) => c.Accent = 2);
        yield return ("dead", (c, n) => n.Dead = true);
        yield return ("dead+ghost", (c, n) => { n.Dead = true; n.Ghost = true; });
        yield return ("staccato", (c, n) => c.Staccato = true);
        yield return ("soundDuration50", (c, n) => c.SoundDurationPercent = 50);
        yield return ("soundDuration200", (c, n) => c.SoundDurationPercent = 200);
        yield return Tech("PalmMute+staccato", (c, n) => c.Staccato = true);
        yield return Tech("PalmMute+ghost", (c, n) => n.Ghost = true);
        yield return Tech("PalmMute+accent1", (c, n) => c.Accent = 1);
        yield return Tech("PalmMute+accent2", (c, n) => c.Accent = 2);
        yield return Tech("PalmMute+ghost+accent2", (c, n) => { n.Ghost = true; c.Accent = 2; });
        yield return Tech("PalmMute+dead", (c, n) => n.Dead = true);
        yield return Tech("PalmMute+soundDuration200", (c, n) => c.SoundDurationPercent = 200);
        yield return Tech("PalmMute+LetRing");
        yield return Tech("PalmMute+Slap");
        yield return Tech("PalmMute+HOPODestination");
        yield return Tech("Slap+accent1", (c, n) => c.Accent = 1);
        yield return Tech("Slap+accent2", (c, n) => c.Accent = 2);
        yield return Tech("Slap+ghost", (c, n) => n.Ghost = true);
        yield return Tech("Pop+accent1", (c, n) => c.Accent = 1);
        yield return Tech("Pop+ghost+accent2", (c, n) => { n.Ghost = true; c.Accent = 2; });
        yield return Tech("Slap+Pop");
        yield return Tech("HOPODestination+ghost", (c, n) => n.Ghost = true);
        yield return Tech("HOPODestination+PalmMute+ghost+accent2", (c, n) => { n.Ghost = true; c.Accent = 2; });
        yield return Tech("HOPODestination+accent1", (c, n) => c.Accent = 1);
        yield return Tech("LetRing+staccato", (c, n) => c.Staccato = true);
        yield return Tech("WahOpen+WahClose");
        yield return Tech("WahOpen+Vibrato");
        yield return Tech("WahClose+ghost+accent1", (c, n) => { n.Ghost = true; c.Accent = 1; });
        yield return Tech("WahOpen+PalmMute");
        yield return Tech("FadeIn+FadeOut");
        yield return Tech("FadeOut+ghost", (c, n) => n.Ghost = true);
        yield return Tech("Bend+Vibrato", Bend);
        yield return Tech("Bend+WideVibrato", Bend);
        yield return Tech("Bend+PalmMute+ghost", (c, n) => { Bend(c, n); n.Ghost = true; });
        yield return Tech("Harmonic+ghost", (c, n) => { n.Ghost = true; n.HarmonicFret = 12; });
        yield return Tech("ArtificialHarmonic+PalmMute", (c, n) => n.HarmonicFret = 7);
        yield return Tech("SemiHarmonic+accent1", (c, n) => { c.Accent = 1; n.HarmonicFret = 7; });
        yield return Tech("TremoloPick+Slap", (c, n) => c.TremoloPickDenominator = 16);
        yield return Tech("TremoloPick+ghost+accent2", (c, n) => { n.Ghost = true; c.Accent = 2; c.TremoloPickDenominator = 16; });
        yield return Tech("Trill+ghost", (c, n) => { n.Ghost = true; n.TrillDurationDenominator = 16; });
        yield return Tech("TremBar+ghost", (c, n) => n.Ghost = true);
        yield return Tech("LegatoSlide+Slap", (c, n) => n.SlideTargetMidi = 60);
        yield return Tech("SlideInBelow+PalmMute");
        yield return Tech("SlideOutDown+ghost", (c, n) => n.Ghost = true);
        yield return Tech("ArpeggioDown", Chord);
        yield return Tech("ArpeggioUp+BrushDown", Chord);
        yield return Tech("BrushUp+ArpeggioDown+ghost", (c, n) => { Chord(c, n); n.Ghost = true; });
        yield return Tech("ArpeggioDown+PalmMute+accent1", (c, n) => { Chord(c, n); c.Accent = 1; });
        yield return ("graceHopoOrigin", (c, n) => { n.IsGraceNote = true; n.Techniques.Add("HOPOOrigin"); c.Notes.Add(Principal(n)); });
        yield return ("graceHopo", (c, n) => { n.IsGraceNote = true; n.Techniques.Add("HOPO"); c.Notes.Add(Principal(n, true)); });
        yield return ("graceOnBeatHopo+Slap", (c, n) => { n.IsGraceNote = true; n.GraceBeforeBeat = false; n.Techniques.Add("HOPO"); c.Notes.Add(Principal(n, false, "Slap")); });
    }

    private static string PlaybackGoldenHash(Action<TabCell, TabNote> setup, int velocity)
    {
        var track = new TrackModel { Name = "G", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Measures = TemplateFactory.Measures(1) };
        var song = new SongProject { Tempo = 120 };
        song.Tracks.Add(track);
        var lead = track.Measures[0].Cells[0];
        lead.DurationDenominator = 4;
        lead.Notes.Add(new TabNote { StringIndex = 3, Fret = 2, MidiValue = track.StringTunings[3] + 2 });
        var cell = track.Measures[0].Cells[4];
        cell.DurationDenominator = 4;
        var note = new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.StringTunings[0] + 5, Velocity = velocity };
        cell.Notes.Add(note);
        setup(cell, note);
        var timeline = new ScoreToMidiCompiler(song, new PlaybackOptions()).Build();
        static string B(double d) => BitConverter.DoubleToInt64Bits(d).ToString("X");
        var sb = new System.Text.StringBuilder();
        foreach (var e in timeline.Events)
            sb.Append(B(e.TimeMs)).Append(':').Append(e.DeviceId).Append(':').Append(e.Channel).Append(':').Append(e.Status).Append(':').Append(e.Data1).Append(':').Append(e.Data2)
                .Append(':').Append(e.TrackIndex).Append(e.IsSetup ? 'S' : 'n').Append(e.Dropped ? 'D' : 'k').Append('\n');
        foreach (var n in timeline.Notes)
            sb.Append("N ").Append(B(n.OnsetMs)).Append(' ').Append(B(n.DurationMs)).Append(' ').Append(n.TrackIndex).Append(' ').Append(n.Bar).Append(' ').Append(n.Cell).Append(' ').Append(n.StringIndex)
                .Append(' ').Append(n.Fret).Append(' ').Append(n.Midi).Append(' ').Append(n.Velocity).Append(' ').Append(n.Dead).Append(n.Ghost).Append(n.LetRing).Append(n.UsesEffectChannel).Append(n.FadeIn)
                .Append(' ').Append(n.Channel).Append(' ').Append(n.Technique).Append(' ').Append(n.Strum).Append('\n');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())))[..12];
    }

    private static void TestTechniquePlaybackGolden()
    {
        var problems = new List<string>();
        var actual = new List<string>();
        foreach (var (name, setup) in PlaybackGoldenCases())
            foreach (var velocity in PlaybackGoldenVelocities)
            {
                var key = $"{name}@{velocity}";
                var hash = PlaybackGoldenHash(setup, velocity);
                if (hash != PlaybackGoldenHash(setup, velocity)) problems.Add($"{key}: the compile is not deterministic");
                actual.Add($"{key}={hash}");
            }
        var golden = PlaybackGoldenHashes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(line => line.Split('=') is [var name, var hashes] ? hashes.Split(' ').Select((h, i) => $"{name}@{PlaybackGoldenVelocities[i]}={h}") : new[] { "malformed golden line: " + line }).ToArray();
        if (golden.Length == 0) problems.Add("GOLDEN-CAPTURE " + string.Join(";", actual));
        else
        {
            if (golden.Length != actual.Count) problems.Add($"{actual.Count} cases compiled but {golden.Length} golden entries");
            foreach (var g in golden) if (!actual.Contains(g)) problems.Add($"{g.Split('=')[0]}: events differ from the golden compile");
        }
        Check($"technique playback: {actual.Count} case/velocity compiles produce the same MIDI events as before", problems.Count == 0, string.Join("; ", problems.Take(6)));
    }

    /// <summary>One line per case: the hashes at the velocities 1, 64, 95 and 127 (captured from the compiler before the technique numbers moved into TechniqueInfo).</summary>
    private const string PlaybackGoldenHashes = """
PalmMute=C702DD102025 78AE6E90E4B8 F0B9D9605CEF FFA3187E9AB6
LetRing=612CF1304C8D D51F8973C598 C5B8A8C67119 A7EC7FEB569B
HOPO=EAE79F5B82AC FA4D3DCB31E3 464F7EBB88D8 4D004FB5A8BB
HOPOOrigin=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
HOPODestination=34EB3915BA68 C5291F9FCE47 E14E38C394BF EE3AF0228AE1
Bend=5F6C668962E6 687E5D797ECF 5E511A5305F2 510E2FBF8C32
LegatoSlide=E829D2243FE7 02E0CA754BE7 B7B307771AFD ED8C36EF8853
ShiftSlide=E829D2243FE7 02E0CA754BE7 B7B307771AFD ED8C36EF8853
Vibrato=3CE1EA2B3D23 D972E6F4CBB1 B9142EDABAFD 2118C1E078D7
WideVibrato=3F016E2AE8BD 73D890D410C5 974C04BD924D BC44168A1B9F
TremBar=5F8B2019C145 3116BA70A250 DA4AA15E5192 1B775E0F5975
Harmonic=46A719A490C5 60493F46B235 E2BC6C112E57 A66329872071
ArtificialHarmonic=46A719A490C5 60493F46B235 E2BC6C112E57 A66329872071
Tapping=DA4586F99AEA C31C91C729E1 4F39740E5144 BBA1D31A3F07
Slap=33CCDD2161B7 D284C5FB0C39 2862C87C6CD9 F652F1B22460
Pop=91FE9E1701B4 DB7832CCF7B8 FBD1E2F35754 8CC9BE57AF42
Trill=7E800880599F C72063969881 49B97DFDFFAA A93E3ACA623B
TremoloPick=9F848F6B400A 4579DF46FD35 A3388B36AD75 FF02EA9C4CA1
FadeIn=9BD08EAED2E9 86182F815845 47D29C6E9E89 419DC4E31A93
FadeOut=02808C64D55A 2C63955BB2AF 580E5897D4CD 619DADAB4572
WahOpen=9F8B3BB9009F CB4B180D3713 0CF86E973487 60D906C9802C
WahClose=B3AF030A63F6 88D5E9571244 BF293F60A39C 538BA873C105
BrushDown=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
BrushUp=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
ArpeggioDown=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
ArpeggioUp=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
PinchHarmonic=48AE930F37C8 45C2B88B47D8 3924CE91830E 54272807A808
TapHarmonic=05EAAF483831 6F67C3951609 E33872477D90 B6486EF64D15
SemiHarmonic=46A719A490C5 60493F46B235 E2BC6C112E57 A66329872071
FeedbackHarmonic=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
SlideInBelow=DAA99AAF3A84 DB0E94CB159C 413B01401DC5 97D1667F7BD8
SlideInAbove=7D23C7157A48 9448B857FC8A 7400185F8BBF 9891FC548EDA
SlideOutUp=8EC48B408840 73F679E1D01E 9665716449DF 9B6A3C95B8EB
SlideOutDown=E1827B1FD842 B52D9B8DCB07 061B244348B4 5B9E947A5FAB
PickSlideUp=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
PickSlideDown=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
DeadSlapped=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
LeftTap=DA4586F99AEA C31C91C729E1 4F39740E5144 BBA1D31A3F07
GraceBefore=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
GraceOnBeat=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
GraceBend=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
Ghost=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
Dead=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
PM=34EB3915BA68 0A01AF0C89FD B2FDE108F6F7 5910D45FCE75
plain=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
ghost=D404107C87F8 0FEAB7913247 D9BAD82895CB 50D124BB6E73
ghost+accent1=633A04B677AB E8AEBEA2ED12 223F3B3627D1 0C7C17EB93EA
ghost+accent2=F7E137B7F366 422C270E0256 659F1A99F878 7A1DCF5F24AE
accent1=0E810DDBAD05 C48AE2EB47E7 5910D45FCE75 55E4514A3B86
accent2=DCC94D69D567 B20A6CABD3F2 55E4514A3B86 55E4514A3B86
dead=C677905988A2 90B40A22F31C 50B920DD680C CE2856FD9A2D
dead+ghost=65DC64DA4DBA 5D491647BDD6 B95591119E21 1CB9FD822A3F
staccato=2925D4C86A10 16DF6E717EB2 DACB4C1137F3 4814F9E4BAEE
soundDuration50=2925D4C86A10 16DF6E717EB2 DACB4C1137F3 4814F9E4BAEE
soundDuration200=11A6D15DA872 FCECE4C8E36E 1111FC109F6A 25141589FC6E
PalmMute+staccato=DB3C4B24072A F981F350D50C A3BD8A445A40 06D5C8FF3621
PalmMute+ghost=2D5DADB3B95C 0E436B208DA3 ED771E0A92B5 92E8D1E4A7B9
PalmMute+accent1=7D4FD86B4144 CE1EBE5996B0 8A3AD2CE13B5 A74BE79D85B1
PalmMute+accent2=C03EBDC85D19 4D60D15EDF75 A74BE79D85B1 A74BE79D85B1
PalmMute+ghost+accent2=5C4C766C2AEB 7AE9D6077AAE 507C0A1690D5 C1BF8D2CBA6D
PalmMute+dead=D37DE491F81A 16FD5989C086 DBB84CA0930A D398392454A2
PalmMute+soundDuration200=C702DD102025 78AE6E90E4B8 F0B9D9605CEF FFA3187E9AB6
PalmMute+LetRing=1E110DE21E0B D09778FA7214 D837C50C3B5B 15DE239B4801
PalmMute+Slap=33CCDD2161B7 F08770200A87 418CEC2A6880 F652F1B22460
PalmMute+HOPODestination=C702DD102025 D5E5BDBAA287 36B98DE395E0 7A9160F1BD66
Slap+accent1=0262ED6755D6 6FE6C5003445 F652F1B22460 F652F1B22460
Slap+accent2=D7A14B287A76 14C28806DCEB F652F1B22460 F652F1B22460
Slap+ghost=97E6A3A03993 C4A06CC123E0 4C7CEA3456A8 26ECAAD30419
Pop+accent1=D8DD19F55242 ECA6A2998E33 56595941D406 8CC9BE57AF42
Pop+ghost+accent2=BE7912F10846 C2D96C17E7C9 7D77A9D39D49 954B512FA209
Slap+Pop=33CCDD2161B7 94C050D38024 6CDFA9AA9EA0 F652F1B22460
HOPODestination+ghost=D404107C87F8 D26F05EDD7DC 8BC52D73EC4F 0AE0323FEB42
HOPODestination+PalmMute+ghost+accent2=5C4C766C2AEB AD2FBED65B5D DDEEC7D7C6D9 4CA2EDBB769C
HOPODestination+accent1=0E810DDBAD05 6C6B2736A0EA C4D19736AE6C 5A62BADDA53C
LetRing+staccato=8EC73EE6F3B2 CCCEFF995B4B C836C027F1FF 2E91BF2DF7D2
WahOpen+WahClose=9F8B3BB9009F CB4B180D3713 0CF86E973487 60D906C9802C
WahOpen+Vibrato=E3455E605D4A 20F5927FB124 C4C8DBFA346F A8D8827FFFD8
WahClose+ghost+accent1=540360C7C7C5 C04A39959250 243F4358C26F A569CDCF324A
WahOpen+PalmMute=780680516B1E E546954EC0C3 4EE5D160200A DE5F0EC1307A
FadeIn+FadeOut=D25F08F4F9E8 916BBEDD5D67 F6D0392DA71C 64C93D5987E0
FadeOut+ghost=1C6B300A1772 8C71C665DA6D 75869BD93B8B CED9DC4F647B
Bend+Vibrato=8036F0A85DED 3530F08E811A DFDBF4037BE3 F50627BFBAE4
Bend+WideVibrato=9539F24D8E05 202AF22E651E 94D8C4A51A85 522D942D7C04
Bend+PalmMute+ghost=4E2712D26DF4 C37CCA22AB70 84D5406A86FE 025056DB4197
Harmonic+ghost=838E1C66CDC4 E79573DE6CB4 AC120749C70F 52C4670749F0
ArtificialHarmonic+PalmMute=46A719A490C5 714E30269143 7C83AA14BAB8 32468F2B52BF
SemiHarmonic+accent1=3172A3118194 5CE7D9EA2484 32468F2B52BF A66329872071
TremoloPick+Slap=2AB4C7434E2C 002AAFE7ED88 A259D08C2EE1 F45BB04C0C23
TremoloPick+ghost+accent2=66C872CCD781 9BF557279A17 A1EF563B6533 9AC5B454D605
Trill+ghost=F31A32C570C5 CA59680EBE8A A64B0E342246 08B3AAC46AF6
TremBar+ghost=87AAFF8F98F1 A5854865C365 FB0704EAC4AC E5DE0F9A9508
LegatoSlide+Slap=93181D2B94B1 5BC75798CD4A 0E0701CEC2D5 2228E37FF12C
SlideInBelow+PalmMute=618C836977AD 620FDDA869F3 21B0FC606C62 F73AEEB49B41
SlideOutDown+ghost=4E2D6642290A BD69961C9838 4B75D86D5084 2C2D92BE450E
ArpeggioDown=96749442277C B7D53DD34694 C53E3CB0E9F2 7C4C3932AB39
ArpeggioUp+BrushDown=73A219BFD85B 1DDE835E3F3E 2D2CFB585091 386D1B8E23F8
BrushUp+ArpeggioDown+ghost=C054C18413B5 D6AA84330C1C CF5F4B2074C3 46639F53590C
ArpeggioDown+PalmMute+accent1=EEDEF91295F1 7F076C45C9C7 E4D8ACFC6B38 ACA3B7962BA1
graceHopoOrigin=6528417FDC3B DA6699BC4484 6E2F319FECFE 77B1226C691D
graceHopo=8886E6D6A739 F49BC0CAFF5C 4C852A97DDBC A5A37937BC49
graceOnBeatHopo+Slap=F6BAD28FCE4B F4D1B28ACD8C 1697F44753BD 3105A40DE875
Legato=EAE79F5B82AC FA4D3DCB31E3 464F7EBB88D8 4D004FB5A8BB
Accent=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
Rasgueado=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
Tie=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
PickDown=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
PickUp=34EB3915BA68 11120EBA1A73 53AFF85D20DE 55E4514A3B86
""";
}

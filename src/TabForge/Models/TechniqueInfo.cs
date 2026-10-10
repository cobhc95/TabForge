using System.Collections.Frozen;

namespace TabForge.Models;

// Owns: the one table of every note technique: its engraved mark text, its playback effect kinds and the numbers playback applies, its Guitar Pro value
//       identifiers (as alphaTab names them) and its MusicXML spelling, plus the formats and playback it does not support yet.
// Does not own: the persisted names (TechniqueNames), the engraving geometry, the order and arithmetic of the playback steps (the compiler), the file writers.
// Tests: TestTechniqueInfoTable, TestTechniqueCoverage, TestTechniquePlaybackGolden.

/// <summary>What playback does with a technique: which kinds of effect it has. The numbers are the row fields <see cref="TechniqueRow.VelocityFactor"/>, <see cref="TechniqueRow.MaxLengthSlots"/> and <see cref="TechniqueRow.ControllerValue"/>; the compiler holds the order and the casts.</summary>
[Flags]
public enum PlaybackEffect
{
    None = 0, Velocity = 1, Length = 2, Pitch = 4, Controller = 8, Grace = 16, Retrigger = 32, Strum = 64
}

/// <summary>One technique. <see cref="Mark"/> is the short engraved text ("" for none or for a mark drawn as geometry).</summary>
public sealed record TechniqueRow(string Name, string Mark, PlaybackEffect Playback = PlaybackEffect.None, string GpId = "", string MusicXml = "none")
{
    /// <summary>True for a name the importer produces that has no <see cref="TechniqueNames"/> constant (it is kept and drawn, but not authored).</summary>
    public bool ImporterOnly { get; init; }

    /// <summary>The factor playback multiplies the note velocity by (1 = none). Where it applies, and its cast or rounding, are in the compiler.</summary>
    public double VelocityFactor { get; init; } = 1;

    /// <summary>The longest the note sounds, in sixteenth slots (0 = no cap).</summary>
    public double MaxLengthSlots { get; init; }

    /// <summary>The controller value playback sends for the technique (the wah pedal position, 0 to 127); 0 = none.</summary>
    public int ControllerValue { get; init; }

    /// <summary>The MusicXML articulation element the technique is written as ("scoop", "plop", "doit", "falloff"); "" when it is not an articulation.</summary>
    public string MusicXmlArticulation { get; init; } = "";

    /// <summary>Consumer families (see TestTechniqueCoverage) that do not handle the technique, each with the reason.</summary>
    public (string Family, string Reason)[] NotSupported { get; init; } = Array.Empty<(string, string)>();
}

public static class TechniqueInfo
{
    private const string NoMusicXml = "Not representable in MusicXML 3.1: ";
    private const string NoSound = "No sound effect in the playback compiler: ";
    private const string AccentGap = "A beat-only accent: the importer adds this tag from the beat flag, but only the cell accent is drawn, played and written, and the beat flag does not set it (GuitarProBeatReader.ReadCellMarks). Known gap; the fix belongs in the importer.";

    /// <summary>Every technique: one row per <see cref="TechniqueNames"/> constant, then the importer-only names.</summary>
    public static IReadOnlyList<TechniqueRow> All { get; } = new TechniqueRow[]
    {
        new(TechniqueNames.PalmMute, "", PlaybackEffect.Velocity | PlaybackEffect.Length, "", "play/mute palm") { VelocityFactor = 0.92, MaxLengthSlots = 4 },
        new(TechniqueNames.LetRing, "let ring", PlaybackEffect.Length, "", "GP processing instruction letring"),
        new(TechniqueNames.Hopo, "", PlaybackEffect.Velocity | PlaybackEffect.Grace, "", "notations/hammer-on or pull-off") { VelocityFactor = 0.8 },
        new(TechniqueNames.HopoOrigin, "", PlaybackEffect.Velocity | PlaybackEffect.Grace, "", "notations/hammer-on or pull-off") { VelocityFactor = 0.8 },
        new(TechniqueNames.HopoDestination, "", PlaybackEffect.Velocity, "", "notations/hammer-on or pull-off") { VelocityFactor = 0.8 },
        new(TechniqueNames.Bend, "b", PlaybackEffect.Pitch, "", "technical/bend"),
        new(TechniqueNames.LegatoSlide, "/", PlaybackEffect.Pitch, "", "notations/slide"),
        new(TechniqueNames.ShiftSlide, "S", PlaybackEffect.Pitch, "", "notations/slide"),
        new(TechniqueNames.Vibrato, "", PlaybackEffect.Pitch, "", "GP processing instruction vibrato"),
        new(TechniqueNames.WideVibrato, "", PlaybackEffect.Pitch, "", "GP processing instruction vibrato"),
        new(TechniqueNames.TremoloBar, "T", PlaybackEffect.Pitch, "", "GP processing instruction WhammyBar"),
        new(TechniqueNames.Harmonic, "H", PlaybackEffect.None, "HarmonicType.Natural", "technical/harmonic natural")
            { NotSupported = new[] { ("playback", "A natural harmonic sounds at its fret's pitch, which the note already carries; playback has no separate treatment for it.") } },
        new(TechniqueNames.ArtificialHarmonic, "A.H.", PlaybackEffect.None, "HarmonicType.Artificial", "technical/harmonic artificial")
            { NotSupported = new[] { ("playback", "An artificial harmonic sounds at its pitch, which the note already carries; playback has no separate treatment for it.") } },
        new(TechniqueNames.Tapping, "", PlaybackEffect.None, "", "technical/tap")
            { NotSupported = new[] { ("playback", "A tapped note plays as a plain note; the tap has no playback effect yet.") } },
        new(TechniqueNames.Slap, "S", PlaybackEffect.Velocity, "", "none")
            { VelocityFactor = 1.15, NotSupported = new[] { ("musicxml-export", NoMusicXml + "no standard slap element, and the exporter writes no other-technical text or processing instruction for it.") } },
        new(TechniqueNames.Pop, "P", PlaybackEffect.Velocity, "", "none")
            { VelocityFactor = 1.1, NotSupported = new[] { ("musicxml-export", NoMusicXml + "no standard pop element, and the exporter writes no other-technical text or processing instruction for it.") } },
        new(TechniqueNames.Trill, "tr", PlaybackEffect.Retrigger, "", "ornaments/trill-mark"),
        new(TechniqueNames.TremoloPick, "𝄆", PlaybackEffect.Retrigger | PlaybackEffect.Velocity, "", "ornaments/tremolo"),
        new(TechniqueNames.FadeIn, "<", PlaybackEffect.Controller, "Fade.FadeIn", "none")
            { NotSupported = new[] { ("musicxml-export", NoMusicXml + "volume fades are dynamics only, and the exporter writes no processing instruction for a fade.") } },
        new(TechniqueNames.FadeOut, ">", PlaybackEffect.Controller, "Fade.FadeOut", "none")
            { NotSupported = new[] { ("musicxml-export", NoMusicXml + "volume fades are dynamics only, and the exporter writes no processing instruction for a fade.") } },
        new(TechniqueNames.WahOpen, "wah", PlaybackEffect.Controller, "WahPedal.Open", "none")
            { ControllerValue = 110, NotSupported = new[] { ("musicxml-export", NoMusicXml + "no wah-pedal element, and the exporter writes no other-technical text or processing instruction for it.") } },
        new(TechniqueNames.WahClose, "wah", PlaybackEffect.Controller, "WahPedal.Closed", "none")
            { ControllerValue = 10, NotSupported = new[] { ("musicxml-export", NoMusicXml + "no wah-pedal element, and the exporter writes no other-technical text or processing instruction for it.") } },
        new(TechniqueNames.BrushDown, "↓", PlaybackEffect.Strum, "", "GP processing instruction brush"),
        new(TechniqueNames.BrushUp, "↑", PlaybackEffect.Strum, "", "GP processing instruction brush"),
        new(TechniqueNames.ArpeggioDown, "arp↓", PlaybackEffect.Strum, "BrushType.ArpeggioDown", "GP processing instruction brush"),
        new(TechniqueNames.ArpeggioUp, "arp↑", PlaybackEffect.Strum, "BrushType.ArpeggioUp", "GP processing instruction brush"),

        // Importer-only names: read from a file, kept on the note and drawn, with no authoring constant and (unless noted) no MusicXML spelling.
        Extra("PinchHarmonic", "P.H.", PlaybackEffect.None, "HarmonicType.Pinch", "technical/harmonic artificial")
            with { NotSupported = new[] { ("playback", NoSound + "the harmonic sounds at its pitch, which the note already carries.") } },
        Extra("TapHarmonic", "T.H.", PlaybackEffect.None, "HarmonicType.Tap", "technical/harmonic artificial")
            with { NotSupported = new[] { ("playback", NoSound + "the harmonic sounds at its pitch, which the note already carries.") } },
        Extra("SemiHarmonic", "S.H.", PlaybackEffect.Pitch, "HarmonicType.Semi", "technical/harmonic natural"),
        Extra("FeedbackHarmonic", "F.B.", PlaybackEffect.None, "HarmonicType.Feedback", "technical/harmonic natural")
            with { NotSupported = new[] { ("playback", NoSound + "the harmonic sounds at its pitch, which the note already carries.") } },
        Extra("SlideInBelow", "↗", PlaybackEffect.Pitch) with { MusicXmlArticulation = "scoop" },
        Extra("SlideInAbove", "↘", PlaybackEffect.Pitch) with { MusicXmlArticulation = "plop" },
        Extra("SlideOutUp", "↗", PlaybackEffect.Pitch) with { MusicXmlArticulation = "doit" },
        Extra("SlideOutDown", "↘", PlaybackEffect.Pitch) with { MusicXmlArticulation = "falloff" },
        Extra("PickSlideUp", "P.S.↑") with { NotSupported = PickSlideGaps() },
        Extra("PickSlideDown", "P.S.↓") with { NotSupported = PickSlideGaps() },
        Extra("DeadSlapped", "D.S.")
            with { NotSupported = new[] { ("playback", NoSound + "the importer adds Slap and Dead with it, and playback reads those two, so this tag has no effect of its own."), ("musicxml-export", "No element of its own: the dead note is written from Dead, and Slap is not written (see Slap).") } },
        Extra("LeftTap", "")
            with { NotSupported = new[] { ("playback", NoSound + "a left-hand tap plays as a plain note; the fretboard label (TechniqueTag) shows TAP, as for Tapping."), ("musicxml-export", "The exporter writes the tap element from Tapping only, so a left-hand tap is written as a plain note.") } },
        Extra("GraceBefore", "gr", PlaybackEffect.Grace, "GraceType.BeforeBeat")
            with { NotSupported = new[] { ("musicxml-export", "Grace notes are written from the cell's grace flags (IsGrace, GraceBeforeBeat); this tag is not read.") } },
        Extra("GraceOnBeat", "gr", PlaybackEffect.Grace, "GraceType.OnBeat")
            with { NotSupported = new[] { ("musicxml-export", "Grace notes are written from the cell's grace flags (IsGrace, GraceBeforeBeat); this tag is not read.") } },
        Extra("GraceBend", "grb", PlaybackEffect.Grace, "GraceType.BendGrace")
            with { NotSupported = new[] { ("musicxml-export", "The grace bend is written as a bend from its curve (BendPoints); this tag is not read.") } },
        Extra("Ghost", "G") with { VelocityFactor = 0.8, NotSupported = new[] { ("gp-export", "Written from the note's Ghost flag; this tag is not read."), ("musicxml-export", "The ghost notehead is written from the note's Ghost flag; this tag is not read.") } },
        Extra("Dead", "X") with { MaxLengthSlots = 0.16, NotSupported = new[] { ("gp-export", "Written from the note's Dead flag; this tag is not read."), ("musicxml-export", "The dead notehead is written from the note's Dead flag; this tag is not read.") } },
        Extra("Legato", "") with { NotSupported = new[] { ("engraving", "The slur from the beat is not drawn: only LegatoSlide draws a slur."), ("playback", NoSound + "the slur sounds from LegatoSlide only; the fretboard label (TechniqueTag) shows H/P."), ("musicxml-export", "The slur is written from the slide tags only, so a beat tagged Legato alone gets no slur element.") } },
        Extra("Accent", "") with { NotSupported = new[] { ("engraving", AccentGap), ("playback", AccentGap), ("gp-export", AccentGap), ("musicxml-export", AccentGap) } },
        Extra("Rasgueado", "", PlaybackEffect.Strum) with { NotSupported = new[] { ("musicxml-export", NoMusicXml + "no strumming element, and the exporter writes no other-technical text or processing instruction for a rasgueado.") } },
        Extra("Tie", "") with { NotSupported = new[] { ("engraving", "Drawn from the note's Tied flag; this tag is not read."), ("playback", "Played from the note's Tied flag; this tag is not read."), ("gp-export", "Written from the note's Tied flag; this tag is not read.") } },
        Extra("PickDown", "") with { NotSupported = new[] { ("playback", NoSound + "the pick stroke is drawn and written; the fretboard strum direction is its only playback use.") } },
        Extra("PickUp", "") with { NotSupported = new[] { ("playback", NoSound + "the pick stroke is drawn and written; the fretboard strum direction is its only playback use.") } },
    };

    /// <summary>The gaps of the two pick slides (a method, so the table's static initialisation does not read a field not yet set).</summary>
    private static (string Family, string Reason)[] PickSlideGaps() =>
    [
        ("playback", NoSound + "the pick slide plays as a plain note; playback has no pick-slide treatment yet."),
        ("musicxml-export", NoMusicXml + "no pick-slide element, and the exporter writes no other-technical text or processing instruction for it."),
    ];

    /// <summary>The rows that are a MusicXML articulation, in table order.</summary>
    public static IReadOnlyList<TechniqueRow> MusicXmlArticulations { get; } = All.Where(r => r.MusicXmlArticulation.Length > 0).ToArray();

    private static readonly FrozenDictionary<string, TechniqueRow> ByName = All.ToFrozenDictionary(r => r.Name, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> NameByGpId = All.Where(r => r.GpId.Length > 0).ToFrozenDictionary(r => r.GpId, r => r.Name, StringComparer.Ordinal);

    private static TechniqueRow Extra(string name, string mark, PlaybackEffect playback = PlaybackEffect.None, string gpId = "", string musicXml = "none") =>
        new(name, mark, playback, gpId, musicXml) { ImporterOnly = true };

    /// <summary>The row of a persisted technique name (exact case), or null for a name the table does not know.</summary>
    public static TechniqueRow? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>The engraved short text of a technique; "" when it has none or the name is unknown.</summary>
    public static string MarkOf(string name) => ByName.TryGetValue(name, out var row) ? row.Mark : "";

    /// <summary>The velocity factor of a technique (1 when it has none or the name is unknown).</summary>
    public static double VelocityFactorOf(string name) => ByName.TryGetValue(name, out var row) ? row.VelocityFactor : 1;

    /// <summary>The length cap of a technique in sixteenth slots (0 when it has none or the name is unknown).</summary>
    public static double MaxLengthSlotsOf(string name) => ByName.TryGetValue(name, out var row) ? row.MaxLengthSlots : 0;

    /// <summary>The controller value of a technique (0 when it has none or the name is unknown).</summary>
    public static int ControllerValueOf(string name) => ByName.TryGetValue(name, out var row) ? row.ControllerValue : 0;

    /// <summary>The value of enum T (named <paramref name="family"/> in the GpId, such as "HarmonicType") of the first technique in <paramref name="precedence"/> that the tags carry; null when none. The order of the names is the precedence.</summary>
    public static T? GpValue<T>(ISet<string> tags, string family, params string[] precedence) where T : struct, Enum
    {
        foreach (var name in precedence)
            if (tags.Contains(name) && Find(name)?.GpId is { } id && id.StartsWith(family + ".", StringComparison.Ordinal) && Enum.TryParse<T>(id[(family.Length + 1)..], out var value))
                return value;
        return null;
    }

    /// <summary>The technique name for an alphaTab value such as ("HarmonicType", "Natural"), or null when no technique maps to it.</summary>
    public static string? FromGp(string enumName, string value) => NameByGpId.GetValueOrDefault(enumName + "." + value);
}

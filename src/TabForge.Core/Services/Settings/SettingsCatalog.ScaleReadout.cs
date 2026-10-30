namespace TabForge.Services;

// Owns: the Scale readout rows of the settings catalogue (the status bar scale: where it reads, which tracks it listens to, the families, the common-scale
//   preference and the fretboard link). The scale popup in the status bar edits the same stored values.
// Does not own: page layout (SettingsCatalog.Layout), the stored values (ScaleReadoutSettings) or the detection (SectionScaleDetector).
// Tests: TabForge.Avalonia.Tests.SectionScaleDetectorTests.
public static partial class SettingsCatalog
{
    private const string ReadAutoSection = "Auto: this section", ReadWholeSong = "Whole song", ReadSongKey = "Song key only", ReadSelection = "Selection";
    private const string ListenSelected = "This track", ListenPitched = "All pitched tracks";

    private static List<SettingDescriptor> ScaleReadoutRows(AppSettings s)
    {
        var scale = s.Editing!.ScaleReadout;
        var rows = new List<SettingDescriptor>
        {
            Choice(Fretboard, "Scale readout", "scalereadout.readfrom", "Scale readout reads", v => scale.ReadFrom = ReadFromOf(v), () => ReadFromText(scale.ReadFrom),
                new[] { ReadAutoSection, ReadWholeSong, ReadSongKey, ReadSelection },
                "Where the scale in the status bar comes from. Auto: this section finds the likely scale of the section the playhead or cursor is in. Whole song looks at every bar. Selection reads the selected bars. Song key only shows the key signature, without detection.",
                "scale key readout status bar detect auto section whole song key signature selection"),
            Choice(Fretboard, "Scale readout", "scalereadout.listento", "Scale readout tracks to analyse", v => scale.ListenTo = v == ListenPitched ? ScaleListenTo.Pitched : ScaleListenTo.Track,
                () => ScaleListenTo.Normalize(scale.ListenTo) == ScaleListenTo.Pitched ? ListenPitched : ListenSelected, new[] { ListenSelected, ListenPitched },
                "Which tracks the scale is worked out from: this track (the track you are on, default) or all pitched tracks (guitars, bass, keys and other melodic tracks together). Drums and percussion are never included.",
                "scale key readout tracks to analyse listen this track pitched drums percussion"),
            Bool(Fretboard, "Scale readout", "scalereadout.prefercommon", "Prefer common scales when it is close", v => scale.PreferCommon = v, () => scale.PreferCommon,
                "When two scales fit about as well, show the more common one (major or minor before pentatonic or blues, then harmonic and melodic minor, then modes).",
                "scale key readout prefer common popular close tie"),
            Bool(Fretboard, "Scale readout", "scalereadout.keynames", "Use musical key names (E♭ instead of D#)", v => scale.MusicalKeyNames = v, () => scale.MusicalKeyNames,
                "Off (default): the scale readout names keys with sharps only (D# major). On: keys are spelled like their key signature (E♭ major).",
                "scale key readout key names flats sharps musical spelling"),
            Bool(Fretboard, "Scale readout", "scalereadout.fretboard", "Show the readout scale on the fretboard", v => scale.ShowOnFretboard = v, () => scale.ShowOnFretboard,
                "The scale in the status bar also becomes the scale highlight on the fretboard and keyboard, and follows it as the section changes.",
                "scale key readout fretboard highlight keyboard follow"),
        };
        foreach (var family in ScaleFamilies.All)
        {
            var name = family;
            rows.Add(Bool(Fretboard, "Scale readout", "scalereadout.family." + name.ToLowerInvariant().Replace(' ', '-'), "Consider " + name.ToLowerInvariant() + " scales",
                v => SetFamily(scale, name, v), () => ScaleFamilies.Normalize(scale.Families).Contains(name),
                name == ScaleFamilies.Favourites ? "Whether the scale readout also considers the scales you starred as favourites (any key)." : "Whether the scale readout may name a " + name.ToLowerInvariant() + " scale (" + string.Join(", ", ScaleFamilies.ScalesOf(name)) + ").",
                "scale key readout family consider " + name.ToLowerInvariant()));
        }
        return rows;
    }

    private static string ReadFromText(string value) => ScaleReadFrom.Normalize(value) switch
    {
        ScaleReadFrom.Song => ReadWholeSong,
        ScaleReadFrom.Key => ReadSongKey,
        ScaleReadFrom.Selection => ReadSelection,
        _ => ReadAutoSection,
    };

    private static string ReadFromOf(string text) => text switch
    {
        ReadWholeSong => ScaleReadFrom.Song,
        ReadSongKey => ScaleReadFrom.Key,
        ReadSelection => ScaleReadFrom.Selection,
        _ => ScaleReadFrom.Section,
    };

    private static void SetFamily(ScaleReadoutSettings scale, string family, bool on)
    {
        var chosen = ScaleFamilies.Normalize(scale.Families).ToHashSet();
        if (on) chosen.Add(family); else chosen.Remove(family);
        scale.Families = ScaleFamilies.All.Where(chosen.Contains).ToList();
    }
}

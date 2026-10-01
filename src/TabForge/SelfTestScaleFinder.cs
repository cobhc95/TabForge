using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Scale finder and instrument-view regression tests (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestScaleFinder()
    {
        // A one-string melody (open high E string = MIDI 64) spelling E natural minor, ending on E.
        TrackModel Melody(params int[] frets)
        {
            var track = new TrackModel { Kind = TrackKind.Guitar, StringTunings = new() { 64, 59, 55, 50, 45, 40 } };
            var bar = new MeasureModel();
            for (var i = 0; i < frets.Length && i < bar.Cells.Count; i++)
                bar.Cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = frets[i] });
            track.Measures.Add(bar);
            return track;
        }
        var eMinor = Melody(0, 2, 3, 5, 7, 8, 10, 12, 0, 7, 0); // E F# G A B C D E ... E
        var summary = ScaleDetector.Collect(new[] { eMinor }, 0, 0, 0, 15);
        var ranked = ScaleDetector.Rank(summary);
        Check("scale finder counts the notes it analyses", summary.NoteCount == 11, $"{summary.NoteCount} notes");
        Check("scale finder puts E Natural Minor first for an E minor melody ending on E",
            ranked.Count > 0 && ranked[0].Highlight == "E Natural Minor", ranked.Count > 0 ? ranked[0].Highlight : "none");
        Check("scale finder also offers the relative major (several scales can fit)",
            ranked.Take(6).Any(c => c.Highlight == "G Major" && c.Coverage >= 0.999));
        Check("a scale that misses notes ranks below one that fits them all",
            ranked.First(c => c.Highlight == "E Natural Minor").Score > ranked.First(c => c.Highlight == "E Major").Score);

        // Selection only: the first four notes (E F# G A) fit many scales; bars outside are ignored.
        var partial = ScaleDetector.Collect(new[] { eMinor }, 0, 0, 0, 3);
        Check("scale finder searches only the selected cells", partial.NoteCount == 4, $"{partial.NoteCount} notes");

        // Drum tracks never count as pitches.
        var drums = Melody(0, 1, 2);
        drums.Kind = TrackKind.Drums;
        Check("drum tracks are ignored by the scale finder", ScaleDetector.Collect(new[] { drums }, 0, 0, 0, 15).NoteCount == 0);
        Check("no notes gives no scale candidates", ScaleDetector.Rank(new ScaleDetector.NoteSummary()).Count == 0);

        // Instrument view: fretboard out of the box, and a bad stored value falls back to it.
        Check("the instrument panel matches the instrument by default", new AppSettings().Editing.InstrumentView == InstrumentViews.MatchInstrument);
        Check("a full 88-key keyboard is the default keyboard size", new AppSettings().Editing.KeyboardKeys == 88);
        TrackModel T(TrackKind kind, string name, int program, int channel = 0, int strings = 6) =>
            new() { Kind = kind, InstrumentName = name, MidiProgram = program, MidiChannel = channel, StringTunings = Enumerable.Repeat(40, strings).ToList() };
        Visualization.InstrumentKind K(TrackModel t) => Visualization.InstrumentVisualizer.NaturalKind(t);
        Check("a guitar shows a fretboard", K(T(TrackKind.Guitar, "Distortion Guitar", 30)) == Visualization.InstrumentKind.Guitar);
        Check("a 5-string bass shows a bass fretboard", K(T(TrackKind.Bass, "Electric Bass (5 strings)", 33, 1, 5)) == Visualization.InstrumentKind.Bass);
        Check("a piano shows a keyboard", K(T(TrackKind.Keys, "Grand Piano", 0)) == Visualization.InstrumentKind.Keyboard);
        Check("a flute (even on a guitar-type track) shows a keyboard", K(T(TrackKind.Guitar, "Flute", 73)) == Visualization.InstrumentKind.Keyboard);
        Check("an oud and a cello show a fretboard",
            K(T(TrackKind.Guitar, "Oud", 105)) == Visualization.InstrumentKind.Guitar && K(T(TrackKind.Guitar, "Cello", 42)) == Visualization.InstrumentKind.Guitar);
        Check("drums show drum pads", K(T(TrackKind.Drums, "Drum Kit", 0, 9)) == Visualization.InstrumentKind.Drums);
        var old = new AppSettings(); old.Editing.InstrumentView = InstrumentViews.Fretboard;
        var migrated = SettingsMigration.Normalize("{\"Editing\":{\"InstrumentView\":\"Fretboard\"}}", old);
        Check("older settings files move to matching the instrument once", migrated.Editing.InstrumentView == InstrumentViews.MatchInstrument);
        var chosen = new AppSettings(); chosen.Editing.InstrumentView = InstrumentViews.Keyboard;
        Check("a view chosen in a current settings file is kept",
            SettingsMigration.Normalize("{\"Editing\":{\"InstrumentView\":\"Keyboard\",\"InstrumentViewVersion\":1}}", chosen).Editing.InstrumentView == InstrumentViews.Keyboard);
        var bad = new AppSettings(); bad.Editing.InstrumentView = "Theremin";
        Check("an unknown instrument view falls back to matching the instrument",
            SettingsValidator.Normalize(bad).Editing.InstrumentView == InstrumentViews.MatchInstrument);
        Check("the scale finder and instrument view can be bound to keys",
            HotkeyCatalog.All.Any(a => a.Id == "Tools.ScaleFinder") && HotkeyCatalog.All.Any(a => a.Id == "View.InstrumentView"));
        Check("clearing the scale highlight can be bound to a key", HotkeyCatalog.All.Any(a => a.Id == "View.ClearScale"));

        var looks = new AppSettings();
        Check("appearance defaults: shaded blue scale at 100%, white original-brightness fret dots, large numbers, natural spacing, keys match the theme, Explorer opens tabs",
            looks.Editing.ScaleHighlightStyle == ScaleHighlightStyles.Shaded && looks.Editing.ScaleHighlightColour == "Blue"
            && looks.Editing.ScaleHighlightStrength == 100
            && looks.Editing.FretMarkerColour == "White" && looks.Editing.FretMarkerBrightness == FretMarkerLevels.Original
            && looks.Editing.FretNumberSize == FretNumberSizes.Large && looks.Editing.FretStringSpacing == FretStringSpacings.Natural
            && looks.Editing.KeyboardKeyColours == KeyboardKeyStyles.MatchTheme && looks.General.OpenFromExplorer == "A new tab");
        // Ledger lines and staff lines are one unit: one pen, one opacity setting, no separate ledger row.
        var staffColour = System.Windows.Media.Color.FromArgb(64, 0x34, 0x39, 0x40);
        var staffPen = Views.StaffNotationRenderer.StaffLinePen(staffColour);
        Check("ledger lines use exactly the staff lines' pen (same colour with its opacity, same thickness)",
            ReferenceEquals(staffPen, Views.StaffNotationRenderer.StaffLinePen(staffColour))
            && staffPen.Thickness == Views.StaffNotationRenderer.StaffLineThickness
            && staffPen.Brush is System.Windows.Media.SolidColorBrush { Color: var penColour } && penColour == staffColour);
        var staffRows = SettingsCatalog.Build(new AppSettings()).Where(d => d.Key is "score.staffopacity" or "score.ledgeropacity").ToList();
        Check("Preferences has one staff-and-ledger opacity row and no separate ledger-opacity row",
            staffRows.Count == 1 && staffRows[0].Key == "score.staffopacity" && staffRows[0].Title.Contains("ledger", StringComparison.OrdinalIgnoreCase));
        var opacityHolder = new AppSettings();
        SettingsCatalog.Build(opacityHolder).First(d => d.Key == "score.staffopacity").Set(40.0);
        Check("the one opacity setting is stored once and defaults to the staff-line colour as chosen (100%)",
            Math.Abs(opacityHolder.Appearance.StaffLineOpacity - 0.4) < 1e-9 && new AppSettings().Appearance.StaffLineOpacity == 1.0);
        const string oldLedgerFile = "{\"Appearance\":{\"LedgerLineOpacity\":0.6,\"LedgerOpacityVersion\":1,\"StaffLineOpacity\":0.7}}";
        var oldLedgerSettings = SettingsMigration.Normalize(oldLedgerFile, System.Text.Json.JsonSerializer.Deserialize<AppSettings>(oldLedgerFile)!);
        Check("a settings file with the old ledger keys still loads, the staff opacity is kept and the ledger keys are not written again",
            Math.Abs(oldLedgerSettings.Appearance.StaffLineOpacity - 0.7) < 1e-9
            && !System.Text.Json.JsonSerializer.Serialize(oldLedgerSettings).Contains("LedgerLineOpacity", StringComparison.Ordinal));
        Check("saved appearance values are not overwritten by the new defaults",
            SettingsMigration.Normalize("{\"Editing\":{\"InstrumentViewVersion\":1,\"FretMarkerColour\":\"Default\",\"FretMarkerBrightness\":\"Brighter\",\"FretNumberSize\":\"Medium\"}}",
                System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Editing\":{\"InstrumentViewVersion\":1,\"FretMarkerColour\":\"Default\",\"FretMarkerBrightness\":\"Brighter\",\"FretNumberSize\":\"Medium\"}}")!).Editing
                is { FretMarkerColour: "Default", FretMarkerBrightness: "Brighter", FretNumberSize: "Medium" });
        // Scale highlight strength: clamp, default look unchanged, root always stronger, hotkeys exist.
        var lowStrength = new AppSettings(); lowStrength.Editing.ScaleHighlightStrength = -5;
        var highStrength = new AppSettings(); highStrength.Editing.ScaleHighlightStrength = 900;
        SettingsValidator.Normalize(lowStrength); SettingsValidator.Normalize(highStrength);
        Check("scale highlight strength is clamped to 10..150%",
            lowStrength.Editing.ScaleHighlightStrength == 10 && highStrength.Editing.ScaleHighlightStrength == 150);
        Check("scale highlight strength 100% keeps the standard alphas",
            ScaleHighlightStyles.ScaleAlpha(0.28, 1.0, false) == 0.28 && ScaleHighlightStyles.ScaleAlpha(0.5, 1.0, true) == 0.5
            && ScaleHighlightStyles.ScaleAlpha(0.6, 1.0, false) == 0.6 && ScaleHighlightStyles.ScaleAlpha(0.95, 1.0, true) == 0.95);
        var rootStronger = true;
        foreach (var pct in new[] { 10, 20, 50, 100, 120, 150 })
        {
            var k = ScaleHighlightStyles.StrengthFactor(pct);
            foreach (var (other, root) in new[] { (0.28, 0.5), (0.6, 0.95), (0.7, 1.0) })
                rootStronger &= ScaleHighlightStyles.ScaleAlpha(root, k, true) > ScaleHighlightStyles.ScaleAlpha(other, k, false);
        }
        Check("the scale root stays stronger than the other scale notes from 10% to 150%", rootStronger);
        Check("scale highlight brighter / dimmer can be bound to keys",
            HotkeyCatalog.All.Any(a => a.Id == "View.ScaleHighlightBrighter") && HotkeyCatalog.All.Any(a => a.Id == "View.ScaleHighlightDimmer"));
        Check("fret dots at Original are no brighter than the Brighter level",
            FretMarkerLevels.Level(FretMarkerLevels.Brighter) > FretMarkerLevels.Level(FretMarkerLevels.Original));
        var odd = new AppSettings();
        odd.Editing.ScaleHighlightStyle = "Sparkles"; odd.Editing.ScaleHighlightColour = "#FF00FF";
        odd.Editing.FretMarkerColour = "Neon"; odd.Editing.FretMarkerBrightness = "11"; odd.Editing.KeyboardKeyColours = "Pink";
        SettingsValidator.Normalize(odd);
        Check("unknown appearance values fall back to the defaults",
            odd.Editing.ScaleHighlightStyle == ScaleHighlightStyles.Shaded && odd.Editing.ScaleHighlightColour == "Blue"
            && odd.Editing.FretMarkerColour == "White" && odd.Editing.FretMarkerBrightness == FretMarkerLevels.Original
            && odd.Editing.KeyboardKeyColours == KeyboardKeyStyles.MatchTheme);
        // Fret number size: default Large (100%), round trip through JSON, existing values kept, unknown falls back.
        var sizeDefault = new AppSettings();
        var sizeSmall = new AppSettings(); sizeSmall.Editing.FretNumberSize = FretNumberSizes.Small;
        var sizeJson = System.Text.Json.JsonSerializer.Serialize(sizeSmall);
        var sizeLoaded = SettingsMigration.Normalize(sizeJson, System.Text.Json.JsonSerializer.Deserialize<AppSettings>(sizeJson)!);
        var sizeMissingJson = "{\"Editing\":{\"InstrumentViewVersion\":1}}";
        var sizeMissing = SettingsMigration.Normalize(sizeMissingJson, System.Text.Json.JsonSerializer.Deserialize<AppSettings>(sizeMissingJson)!);
        var sizeOdd = new AppSettings(); sizeOdd.Editing.FretNumberSize = "Huge";
        Check("fret number size: default Large (1.0), Medium 0.85, Small 0.75, saved value round-trips, missing key gets the default, unknown falls back",
            sizeDefault.Editing.FretNumberSize == FretNumberSizes.Large && FretNumberSizes.Scale(FretNumberSizes.Medium) == 0.85
            && FretNumberSizes.Scale(FretNumberSizes.Small) == 0.75 && FretNumberSizes.Scale(FretNumberSizes.Large) == 1.0
            && sizeLoaded.Editing.FretNumberSize == FretNumberSizes.Small
            && sizeMissing.Editing.FretNumberSize == FretNumberSizes.Large
            && SettingsValidator.Normalize(sizeOdd).Editing.FretNumberSize == FretNumberSizes.Large);
        var circles = new AppSettings(); circles.Editing.ScaleHighlightStyle = "circles";
        Check("appearance choices are matched without regard to case", SettingsValidator.Normalize(circles).Editing.ScaleHighlightStyle == ScaleHighlightStyles.Circles);

        // Explorer hand-off accepts only a full path to an existing song file.
        var song = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tf-selftest-{Guid.NewGuid():N}.gp5");
        var text = System.IO.Path.ChangeExtension(song, ".txt");
        try
        {
            System.IO.File.WriteAllBytes(song, new byte[] { 1 });
            System.IO.File.WriteAllBytes(text, new byte[] { 1 });
            Check("a song opened from Explorer is accepted by its full path",
                SingleInstanceService.IsOpenableSong(song));
            Check("other files, relative paths and missing files are refused",
                !SingleInstanceService.IsOpenableSong(text) && !SingleInstanceService.IsOpenableSong(System.IO.Path.GetFileName(song))
                && !SingleInstanceService.IsOpenableSong(song + ".missing.gp5") && !SingleInstanceService.IsOpenableSong(""));
        }
        finally
        {
            try { System.IO.File.Delete(song); System.IO.File.Delete(text); } catch (System.IO.IOException) { }
        }
    }
}

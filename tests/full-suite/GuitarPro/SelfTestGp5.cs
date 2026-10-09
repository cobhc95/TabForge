using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestPerNoteDurationPercent()
    {
        Check("per-note duration: alphaTab's GP3-5 garbage (a denormal) keeps 100%",
            GuitarProImporter.SanePerNoteDurationPercent(double.Epsilon, false) is null &&
            GuitarProImporter.SanePerNoteDurationPercent(2.9e-319, false) is null &&
            GuitarProImporter.SanePerNoteDurationPercent(double.NaN, false) is null &&
            GuitarProImporter.SanePerNoteDurationPercent(0.01, false) is null);
        Check("per-note duration: GP3-5 sources never carry one, even a plausible value",
            GuitarProImporter.SanePerNoteDurationPercent(0.5, true) is null);
        Check("per-note duration: a real GPX/.gp 0.5 gives 50%",
            GuitarProImporter.SanePerNoteDurationPercent(0.5, false) == 50);
        var old = new SongProject { ImportedFrom = "x.gp5" };
        var oldTrack = new TrackModel();
        var oldMeasure = new MeasureModel();
        oldMeasure.Cells[0].SoundDurationPercent = 1;
        oldMeasure.Cells[1].SoundDurationPercent = 50;
        oldTrack.Measures.Add(oldMeasure);
        old.Tracks.Add(oldTrack);
        ProjectService.RepairGp3To5PalmMuteDurations(old);
        Check("per-note duration: old .tforge from a GP3-5 import repairs 1% to 100% and keeps 50%",
            oldMeasure.Cells[0].SoundDurationPercent == 100 && oldMeasure.Cells[1].SoundDurationPercent == 50);

        // Optional local check: only where the user's song library exists.
        var referenceA = LocalReferenceSongs.Resolve("reference-a");
        if (referenceA is null) return;
        var song = GuitarProImporter.Import(referenceA);
        var palmMuted = 0;
        var tooShort = 0;
        foreach (var track in song.Tracks)
            foreach (var measure in track.Measures)
                foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    if (cell.Notes.Any(n => TechniqueNames.HasPalmMute(n.Techniques)))
                    {
                        palmMuted++;
                        if (cell.SoundDurationPercent < 5) tooShort++;
                    }
        Check($"per-note duration: imported reference song A has palm mutes ({palmMuted}) and none under 5% ({tooShort})", palmMuted > 0 && tooShort == 0);
    }

    private static void TestGp5EditingSemantics()
    {
        var drumOrigin = new { PercussionArticulation = 46, RealValue = 46 };
        var drumTie = new { IsTieDestination = true, TieOrigin = drumOrigin, PercussionArticulation = 0, RealValue = 0 };
        Check("GP5 drum ties keep their origin's open hi-hat rather than inventing a cymbal",
            GuitarProImporter.DrumPitch(drumTie) == 46);
        Check("GP5 drums with no valid pitch never inherit the kit tuning",
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 0, RealValue = 0 }) == 0);
        Check("GP5 drum articulations take priority over an unrelated sounding pitch",
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 38, RealValue = 49 }) == 38);
        // Guitar Pro 6/7/8 drums: the articulation is an index into the track's own list (GP7/8 drums vanished).
        var kit = new List<object> { new { OutputMidiNumber = 36 }, new { OutputMidiNumber = 38 }, new { OutputMidiNumber = 42 } };
        Check("GP7/8 drum notes resolve their articulation index through the track's articulation list",
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 0, RealValue = 0 }, kit) == 36 &&
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 2, RealValue = 0 }, kit) == 42);
        Check("Guitar Pro's extended drum ids map to their GM sound (92 half-open hi-hat, 91 rim shot, 93 ride edge)",
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 92, RealValue = 0 }) == 46 &&
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 91, RealValue = 0 }) == 38 &&
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 93, RealValue = 0 }) == 51);
        Check("GP3-5 drum hits without articulation or pitch fall back to the fret (the GM drum number)",
            GuitarProImporter.DrumPitch(new { PercussionArticulation = 0, RealValue = 0, Fret = 42 }) == 42);
        var junk = new byte[] { 0x0D, 0x0A, 0x18 }.Concat(System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v5.10")).ToArray();
        Check("stray bytes before a Guitar Pro 3-5 header are skipped",
            GuitarProImporter.WithoutLeadingJunk(junk) is var trimmed && trimmed[0] == 0x18 && trimmed.Length == junk.Length - 2);
        var clean = new byte[] { 0x18 }.Concat(System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v5.10")).ToArray();
        Check("a normal Guitar Pro 3-5 header is left untouched", GuitarProImporter.WithoutLeadingJunk(clean).Length == clean.Length);

        var project = SingleTrack(1, 120);
        var measure = project.Tracks[0].Measures[0];
        var lead = measure.Cells[0];
        lead.DurationDenominator = 4;
        lead.OctaveShiftSemitones = 12;
        lead.SoundDurationPercent = 50;
        lead.Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = 60 });

        measure.Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        var secondVoice = measure.Voice2Cells[0];
        secondVoice.DurationDenominator = 4;
        secondVoice.Notes.Add(new TabNote { StringIndex = 1, Fret = 0, MidiValue = 67 });

        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        Check("two independent voices sound simultaneously", timeline.Notes.Count == 2 &&
            timeline.Notes.All(note => Math.Abs(note.OnsetMs) < 0.01));
        var shifted = timeline.Notes.SingleOrDefault(note => note.Midi == 72);
        Check("octave marking transposes playback and sound-duration percentage gates the note",
            shifted is not null && Math.Abs(shifted.DurationMs - 250) < 0.1);

        var tied = SingleTrack(2, 120);
        tied.Tracks[0].Measures[0].Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        tied.Tracks[0].Measures[1].Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        var origin = tied.Tracks[0].Measures[0].Voice2Cells[12];
        origin.DurationDenominator = 4;
        origin.Notes.Add(new TabNote { StringIndex = 0, MidiValue = 64 });
        var destination = tied.Tracks[0].Measures[1].Voice2Cells[0];
        destination.DurationDenominator = 4;
        destination.Notes.Add(new TabNote { StringIndex = 0, MidiValue = 64, Tied = true });
        var tiedTimeline = MidiTimelineBuilder.Build(tied, new PlaybackOptions());
        Check("voice-2 ties sustain across adjacent bars", tiedTimeline.Notes.Count == 1 && tiedTimeline.TieMerges == 1 &&
            Math.Abs(tiedTimeline.Notes[0].DurationMs - 1000) < 0.1);

        var directions = SingleTrack(4, 120);
        directions.Tracks[0].Measures[0].Directions = "Segno";
        directions.Tracks[0].Measures[2].Directions = "Fine";
        directions.Tracks[0].Measures[3].Directions = "DalSegno";
        var order = PlaybackOrder.Build(directions, new PlaybackOptions());
        Check("Dal Segno repeats from the Segno destination through Fine",
            order.SequenceEqual(new[] { 0, 1, 2, 3, 0, 1, 2 }));

        var coda = SingleTrack(5, 120);
        coda.Tracks[0].Measures[1].Directions = "Coda";
        coda.Tracks[0].Measures[3].Directions = "ToCoda";
        coda.Tracks[0].Measures[4].Directions = "DaCapoAlCoda";
        var codaOrder = PlaybackOrder.Build(coda, new PlaybackOptions());
        Check("Da Capo al Coda jumps to the Coda after To Coda",
            codaOrder.SequenceEqual(new[] { 0, 1, 2, 3, 4, 0, 1, 2, 3, 1, 2, 3, 4 }), string.Join(",", codaOrder));

        // V-25: the play order of D.S. al Fine, D.C. al Coda, a Fine before any jump, and D.S.S. plays them.
        var dsFine = SingleTrack(6, 120);
        dsFine.Tracks[0].Measures[1].Directions = "Segno";
        dsFine.Tracks[0].Measures[3].Directions = "Fine";
        dsFine.Tracks[0].Measures[4].Directions = "DalSegno";
        Check("D.S. al Fine: the D.S. bar ends the first pass, then back to the Segno and stop at Fine",
            PlaybackOrder.Build(dsFine, new PlaybackOptions()).SequenceEqual(new[] { 0, 1, 2, 3, 4, 1, 2, 3 }), string.Join(",", PlaybackOrder.Build(dsFine, new PlaybackOptions())));

        var fineFirst = SingleTrack(4, 120);
        fineFirst.Tracks[0].Measures[1].Directions = "Fine";
        Check("Fine before any jump does not end playback (D-5)",
            PlaybackOrder.Build(fineFirst, new PlaybackOptions()).SequenceEqual(new[] { 0, 1, 2, 3 }));

        var dcCoda = SingleTrack(6, 120);
        dcCoda.Tracks[0].Measures[2].Directions = "ToCoda";
        dcCoda.Tracks[0].Measures[3].Directions = "DaCapoAlCoda";
        dcCoda.Tracks[0].Measures[4].Directions = "Coda";
        var dcCodaOrder = PlaybackOrder.Build(dcCoda, new PlaybackOptions());
        Check("D.C. al Coda: To Coda is ignored on the first pass, taken after the jump",
            dcCodaOrder.SequenceEqual(new[] { 0, 1, 2, 3, 0, 1, 2, 4, 5 }), string.Join(",", dcCodaOrder));

        var dss = SingleTrack(6, 120);
        dss.Tracks[0].Measures[0].Directions = "Segno";
        dss.Tracks[0].Measures[1].Directions = "SegnoSegno";
        dss.Tracks[0].Measures[3].Directions = "Fine";
        dss.Tracks[0].Measures[4].Directions = "DalSegnoSegno";
        var dssOrder = PlaybackOrder.Build(dss, new PlaybackOptions());
        Check("D.S.S. jumps to the double segno", dssOrder.SequenceEqual(new[] { 0, 1, 2, 3, 4, 1, 2, 3 }), string.Join(",", dssOrder));

        var freeTime = SingleTrack(1, 120);
        var freeBar = freeTime.Tracks[0].Measures[0];
        freeBar.FreeTime = true;
        freeBar.Cells[0].DurationDenominator = 1;
        freeBar.Cells[0].Notes.Add(new TabNote { StringIndex = 0, MidiValue = 60 });
        Check("free-time bars are exempt from fixed-meter overflow validation",
            !MusicTime.AnalyzeBar(freeTime, 0).Error);
    }

    private static void TestGp5SvgIcons()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons", "More");
        var files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.svg") : Array.Empty<string>();
        var loader = typeof(SvgIconView).GetMethod("LoadAsset", BindingFlags.NonPublic | BindingFlags.Static);
        var loaded = 0;
        foreach (var file in files)
        {
            try
            {
                var key = "tool:More/" + Path.GetFileNameWithoutExtension(file);
                var asset = loader?.Invoke(null, new object[] { key });
                var shapes = asset?.GetType().GetProperty("Shapes")?.GetValue(asset) as System.Collections.IEnumerable;
                if (shapes is not null && shapes.Cast<object>().Any()) loaded++;
            }
            catch
            {
                // Count the icon as unavailable; the assertion below reports the failed coverage.
            }
        }
        Check("all additional tool SVGs parse into drawable vector shapes", files.Length > 0 && loaded == files.Length,
            $"loaded {loaded}/{files.Length}");
        var traversalAsset = loader?.Invoke(null, new object[] { "tool:../../settings" });
        Check("SVG loading rejects path traversal and resolves only bundled asset identifiers", traversalAsset is null);

        var allIconsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons");
        var paletteFiles = Directory.Exists(allIconsDirectory)
            ? Directory.GetFiles(allIconsDirectory, "*.svg", SearchOption.AllDirectories)
            : Array.Empty<string>();
        var rendered = 0;
        foreach (var file in paletteFiles)
        {
            try
            {
                var relative = Path.GetRelativePath(allIconsDirectory, file)
                    .Replace('\\', '/')[..^4];
                var view = new SvgIconView
                {
                    Icon = "tool:" + relative,
                    IconColor = System.Windows.Media.Color.FromRgb(0xC7, 0xCF, 0xDA),
                    Width = 32,
                    Height = 32
                };
                view.Measure(new Size(32, 32));
                view.Arrange(new Rect(0, 0, 32, 32));
                var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var buffer = new byte[32 * 32 * 4];
                bitmap.CopyPixels(buffer, 32 * 4, 0);
                if (Enumerable.Range(0, 32 * 32).Any(index => buffer[index * 4 + 3] > 0)) rendered++;
            }
            catch
            {
                // Report aggregate visible-icon coverage below.
            }
        }
        Check("all side-panel palette SVGs render visible icons including currentColor artwork",
            paletteFiles.Length > 0 && rendered == paletteFiles.Length, $"rendered {rendered}/{paletteFiles.Length}");

        var transportDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "TransportIcons");
        var transportFiles = Directory.Exists(transportDirectory)
            ? Directory.GetFiles(transportDirectory, "*.svg")
            : Array.Empty<string>();
        var renderedTransport = 0;
        foreach (var file in transportFiles)
        {
            try
            {
                var view = new SvgIconView
                {
                    Icon = Path.GetFileNameWithoutExtension(file),
                    IconColor = System.Windows.Media.Color.FromRgb(0xC7, 0xCF, 0xDA),
                    ShowFrame = false,
                    Width = 32,
                    Height = 32
                };
                view.Measure(new Size(32, 32));
                view.Arrange(new Rect(0, 0, 32, 32));
                var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var buffer = new byte[32 * 32 * 4];
                bitmap.CopyPixels(buffer, 32 * 4, 0);
                if (Enumerable.Range(0, 32 * 32).Any(index => buffer[index * 4 + 3] > 0)) renderedTransport++;
            }
            catch
            {
                // Report aggregate visible-icon coverage below.
            }
        }
        Check("all transport SVGs render visible icons including currentColor artwork",
            transportFiles.Length > 0 && renderedTransport == transportFiles.Length,
            $"rendered {renderedTransport}/{transportFiles.Length}");

        foreach (var id in new[] { "beam_auto", "key_signature", "custom_ntuplet", "octave_8va" })
        foreach (var (pixels, dpi) in new[] { (32, 96d), (64, 192d) })
        {
            try
            {
                var view = new SvgIconView { Icon = "tool:More/" + id, Width = 32, Height = 32 };
                view.Measure(new Size(32, 32));
                view.Arrange(new Rect(0, 0, 32, 32));
                var bitmap = new RenderTargetBitmap(pixels, pixels, dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var buffer = new byte[pixels * pixels * 4];
                bitmap.CopyPixels(buffer, pixels * 4, 0);
                var visible = Enumerable.Range(0, pixels * pixels).Any(index => buffer[index * 4 + 3] > 0);
                Check($"{id} SVG rasterizes at {dpi:0} DPI", visible);
            }
            catch (Exception error)
            {
                Check($"{id} SVG rasterizes at {dpi:0} DPI", false, error.GetBaseException().Message);
            }
        }
    }

    private static void TestRuntimeIconAndResourceKeys()
    {
        var loader = typeof(SvgIconView).GetMethod("LoadAsset", BindingFlags.NonPublic | BindingFlags.Static);
        var paletteKeys = typeof(TabForge.Views.ToolPaletteController)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType.IsArray && field.FieldType.GetElementType()?.Name == "PaletteTool")
            .SelectMany(field => ((System.Collections.IEnumerable?)field.GetValue(null) ?? Array.Empty<object>()).Cast<object>())
            .Select(tool => tool.GetType().GetProperty("Icon")?.GetValue(tool)?.ToString())
            .Where(icon => !string.IsNullOrWhiteSpace(icon))
            .Select(icon => "tool:" + icon)
            .ToArray();
        var transportKeys = new[]
        {
            "rewind_to_beginning", "stop", "play", "pause", "next_section", "click_beat",
            "metronome", "loop", "zoom_minus", "zoom_plus"
        };
        var missingIcons = new List<string>();
        foreach (var key in paletteKeys.Concat(transportKeys))
        {
            try
            {
                var asset = loader?.Invoke(null, new object[] { key });
                var shapes = asset?.GetType().GetProperty("Shapes")?.GetValue(asset) as System.Collections.IEnumerable;
                if (shapes is null || !shapes.Cast<object>().Any()) missingIcons.Add(key);
            }
            catch { missingIcons.Add(key); }
        }
        Check("every statically configured palette and transport icon key resolves",
            paletteKeys.Length > 0 && missingIcons.Count == 0,
            missingIcons.Count == 0 ? $"{paletteKeys.Length + transportKeys.Length} keys" : string.Join(", ", missingIcons));

        var app = Application.Current;
        if (app is null)
        {
            Check("runtime WPF resource keys resolve", false, "Application.Current is unavailable");
            return;
        }

        ThemeService.Apply(new AppSettings().Appearance);
        var namedKeys = new[]
        {
            "PanelBrush", "Panel2Brush", "Panel3Brush", "BorderBrush", "BorderSoftBrush", "TextBrush",
            "MutedBrush", "AccentBrush", "AccentSoftBrush", "ChromeStripBrush", "ChromeBorderBrush",
            "WindowBrush", "PlaybarSurfaceBrush", "PlaybarBorderBrush", "PlaybarMetroBrush", "PlaybarBeatBrush",
            "PlaybarLoopBrush", "PlaybarRewindBrush", "PlaybarRewindBorderBrush", "PlaybarPlayBrush",
            "PlaybarPlayBorderBrush", "PlaybarStopBrush", "PlaybarStopBorderBrush", "PlaybarNextBrush",
            "PlaybarNextBorderBrush", "AddTrackSurfaceBrush", "AddTrackBorderBrush", "TrackAudibleBrush",
            "TrackMutedBrush", "TrackSoloBrush", "TrackSoloActiveBorderBrush", "TrackToggleIdleBrush",
            "PaperDarkBrush", "PaperLightBrush", "ToolButton", "TransportButton", "ArrangementSlider",
            "TimelineScrollBar", "IconPath", "IconPlus", "IconSpeaker", "IconMute", "IconSolo",
            "IconRestore", "IconMaximise"
        };
        var missingResources = namedKeys.Where(key => app.TryFindResource(key) is null).ToList();
        foreach (var type in new[]
                 {
                     typeof(System.Windows.Controls.Button), typeof(System.Windows.Controls.ContextMenu),
                     typeof(System.Windows.Controls.MenuItem), typeof(System.Windows.Controls.Separator),
                     typeof(System.Windows.Controls.Primitives.ScrollBar)
                 })
            if (app.TryFindResource(type) is null) missingResources.Add(type.FullName ?? type.Name);

        foreach (var key in app.Resources.Keys)
            if (app.TryFindResource(key) is null) missingResources.Add(key.ToString() ?? "<null>");

        Check("all dynamic and application-declared WPF resource keys resolve", missingResources.Count == 0,
            missingResources.Count == 0 ? $"{namedKeys.Length} named keys plus application resources" :
            string.Join(", ", missingResources.Distinct(StringComparer.Ordinal)));
    }

    private static void TestSystemBreakPreferences()
    {
        var natural = new[] { 60d, 60d, 60d, 60d };
        var forced = ScorePageLayout.Create(0, 200, natural,
            new[] { false, false, true, false }, new[] { false, false, false, false });
        Check("forced line break starts a new system at its measure", forced.SystemCount == 2 &&
            forced.SystemForMeasure(1) == 0 && forced.SystemForMeasure(2) == 1);

        var prevented = ScorePageLayout.Create(0, 145, natural.Take(3).ToArray(),
            new[] { false, false, false }, new[] { false, true, false });
        Check("prevent-line-break keeps adjacent measures together when a row overflows",
            prevented.SystemCount == 2 && prevented.SystemForMeasure(0) == prevented.SystemForMeasure(1) &&
            prevented.SystemForMeasure(1) != prevented.SystemForMeasure(2));

        var tooNarrow = ScorePageLayout.Create(0, 100, natural.Take(3).ToArray(),
            new[] { false, false, false }, new[] { false, true, false });
        Check("prevent-line-break is ignored when the bars cannot fit the page even at their tightest spacing (nothing runs past the page)",
            tooNarrow.SystemForMeasure(0) != tooNarrow.SystemForMeasure(1));

        var ordinary = Enumerable.Repeat(100d, 5).ToArray();
        var narrow = ScorePageLayout.Create(0, 480, ordinary);
        var wide = ScorePageLayout.Create(0, 700, ordinary);
        Check("automatic composition fits four ordinary measures when their minimum widths fit",
            narrow.SystemCount == 2 && narrow.Systems[0].Measures.Count == 4);
        Check("resizing the available width recomputes measures per system",
            wide.SystemCount == 1 && wide.Systems[0].Measures.Count == 5);

        var weighted = ScorePageLayout.Create(0, 1000, new[] { 100d, 200d });
        var weightedRow = weighted.Systems[0];
        Check("proportional justification gives wider intrinsic measures more expansion",
            Math.Abs(weightedRow.Measures[1].Width / weightedRow.Measures[0].Width - 2) < 0.001 &&
            weightedRow.Width < 1000);

        var defaultAligned = ScorePageLayout.Create(50, 800, new[] { 100d });
        Check("a short system starts at the left margin (next to its clef), not centred",
            defaultAligned.Systems[0].X == 50 && defaultAligned.Systems[0].Width < 800);

        var incomplete = ScorePageLayout.Create(0, 400, Enumerable.Repeat(100d, 5).ToArray());
        Check("the final two-measure system stays compact and leaves unused right-side space",
            incomplete.Systems[^1].Measures.Count == 2 &&
            incomplete.Systems[^1].Width <= 220.001 && incomplete.Systems[^1].Width < 400);

        var mixedDensity = ScorePageLayout.Create(0, 600, new[] { 100d, 100d, 300d, 100d });
        Check("a dense measure changes the automatic break and the non-final system fills the page width",
            mixedDensity.SystemCount == 2 && mixedDensity.Systems[0].Measures.Count == 3 &&
            Math.Abs(mixedDensity.Systems[0].Width - 600) < 0.001 &&
            Math.Abs(mixedDensity.Systems[0].Measures[2].Width / mixedDensity.Systems[0].Measures[0].Width - 3) < 0.001);
        Check("non-final systems leave no empty right-hand page space",
            narrow.Systems[0].Width == 480 && forced.Systems[0].Width == 200);

        var fixedCount = ScorePageLayout.Create(0, 1000, Enumerable.Repeat(100d, 5).ToArray(),
            fixedMeasuresPerSystem: 2);
        Check("fixed bars-per-system is an explicit option alongside automatic composition",
            fixedCount.Systems.Select(system => system.Measures.Count).SequenceEqual(new[] { 2, 2, 1 }));
    }

    // Icon audit (after the assets moved to Assets/Icons): every instrument in the catalogue must have
    // artwork that ships with the build and renders, and the catalogue must come from the manifest.
    private static void TestInstrumentArtwork()
    {
        var entries = TabForge.Services.InstrumentCatalog.All;
        var manifest = Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments", "manifest.json");
        var missing = entries.Where(e => TabForge.Views.InstrumentIcon.Get(e) is null).Select(e => e.Name).ToList();
        Check("the instrument manifest ships with the build", File.Exists(manifest), manifest);
        Check("every catalogue instrument has artwork that loads",
            entries.Count >= 128 && missing.Count == 0,
            $"{entries.Count} instruments, {missing.Count} without artwork: {string.Join(", ", missing.Take(8))}");
    }
}

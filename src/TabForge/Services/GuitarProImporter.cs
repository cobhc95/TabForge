using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Services;

/// <summary>
/// Boundary converter for Guitar Pro files (.gp3/.gp4/.gp5/.gpx/.gp) built on alphaTab.
/// </summary>
/// <remarks>
/// Conversion rules that matter for fidelity:
/// - beats are placed by their real tick position (alphaTab exposes PlayStart), so triplets and
///   short notes never run off the 16-slot editor grid and truncate the rest of the bar;
/// - every voice and every staff of a track is merged into the bar grid instead of only the first;
/// - repeats / alternate endings / sections / tempo / time signature come from the master bars;
/// - ties are folded into the previous note's duration instead of creating a new attack;
/// - reflection is used throughout so a future alphaTab upgrade degrades gracefully.
/// </remarks>
public static class GuitarProImporter
{
    public static readonly string[] SupportedExtensions = FileTypes.GuitarPro;
    private static readonly string[] ImportedSectionColors =
    {
        "#2E74B5", "#3FB950", "#D8A032", "#8B5CF6", "#00A6A6", "#E06C75", "#64748B", "#C45A9A"
    };

    /// <summary>Notes skipped as duplicates in the last import (same string/pitch merged by voices/staves).</summary>
    public static int LastImportSkippedDuplicates { get; private set; }
    /// <summary>Diagnostics: a few of the notes the last import merged as duplicates.</summary>
    public static List<string> LastImportDuplicateSamples { get; } = new();

    /// <summary>alphaTab timing: ticks per quarter note.</summary>
    private const int TicksPerQuarter = 960;
    private const int TicksPerSlot = TicksPerQuarter / MusicTime.SlotsPerQuarter;   // 240

    private static readonly byte[] Gp3To5Signature = System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v");

    /// <summary>
    /// A few stray bytes before a Guitar Pro 3-5 header (e.g. a line break added by a download) made the file
    /// unreadable. When the signature starts within the first 16 bytes and its length byte matches, skip them.
    /// </summary>
    internal static byte[] WithoutLeadingJunk(byte[] data)
    {
        var at = data.AsSpan(0, Math.Min(data.Length, 16 + 1 + Gp3To5Signature.Length)).IndexOf(Gp3To5Signature);
        if (at <= 1) return data; // already at 1 (after the length byte) or not a GP3-5 file
        var lengthByte = at - 1;
        return data[lengthByte] is >= 20 and <= 40 ? data[lengthByte..] : data;
    }

    [ThreadStatic] private static Dictionary<object, GuitarProMixTableScanner.RawMix>? _rawMixes;
    /// <summary>True while importing a Guitar Pro 3-5 file (alphaTab names its tremolo speeds one step off there).</summary>
    [ThreadStatic] private static bool _gp3To5;

    /// <summary>
    /// Re-reads a Guitar Pro 3-5 file that alphaTab rejected and returns the bar it had reached (1-based), or null
    /// when that cannot be told. alphaTab keeps the partly built score, whose tracks hold the bars read so far.
    /// </summary>
    internal static int? LocateFailedBar(byte[] data)
    {
        try
        {
            if (data.AsSpan(0, Math.Min(data.Length, 32)).IndexOf(Gp3To5Signature) < 0) return null;
            var type = typeof(ScoreLoader).Assembly.GetType("AlphaTab.Importer.Gp3To5Importer");
            if (type is null || Activator.CreateInstance(type, true) is not { } importer) return null;
            type.GetMethod("Init")?.Invoke(importer, new object[] { AlphaTab.Io.ByteBuffer.FromBuffer(data), new Settings() });
            try { type.GetMethod("ReadScore")?.Invoke(importer, null); } catch (Exception) { /* expected: this is the failure being located */ }
            var field = type.GetField("_score", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(importer) is not AlphaTab.Model.Score partial) return null;
            var reached = partial.Tracks.Select(t => t.Staves.Count > 0 ? t.Staves[0].Bars.Count : 0).DefaultIfEmpty(0).Max();
            return Math.Max(1, (int)reached);
        }
        catch (Exception) { return null; }
    }

    public static SongProject Import(string path)
    {
        path = FilePathPolicy.ExistingFile(path, "Guitar Pro file", SupportedExtensions);
        // Reset before the embedded-project early return so a lossless .gp load never reports the previous import's duplicates.
        LastImportSkippedDuplicates = 0;
        LastImportDuplicateSamples.Clear();
        // Cooperative limits (A5-07): the ambient guard of a background import; null for synchronous headless use.
        ImportGuard.CheckCurrent();
        var raw = InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "Guitar Pro file");
        // Container / header checks on the raw bytes, before anything unpacks or parses them.
        GuitarProPreParse.Validate(WithoutLeadingJunk(raw));
        ImportGuard.CheckCurrent();
        // A .gp saved by TabForge carries its complete project: load that for a lossless round trip.
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) && GuitarProExporter.TryReadEmbedded(raw) is { } embedded)
            return embedded;

        var data = WithoutLeadingJunk(raw);
        object score;
        // alphaTab's parse is one uninterruptible call: the guard is checked right before and after it.
        try { score = ScoreLoader.LoadScoreFromBytes(data, new Settings()); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        {
            // alphaTab's Guitar Pro 3-5 reader refuses songs over 1,000 bars (a fixed limit in the library).
            if (ex.GetBaseException().Message.Contains("'bar count'", StringComparison.Ordinal))
                throw new InvalidDataException("This Guitar Pro 3-5 file has more than 1,000 bars, which the Guitar Pro reader TabForge uses cannot open yet. Saving it from Guitar Pro 6/7/8 as .gp or .gpx opens it.", ex);
            var barHint = ex.GetBaseException() is IndexOutOfRangeException or ArgumentOutOfRangeException
                ? LocateFailedBar(data) : null;
            if (barHint is int failedBar)
                throw new InvalidDataException($"This Guitar Pro 3-5 file could not be read at or near bar {failedBar}: a second voice with no beats after a two-voice bar, which the Guitar Pro reader TabForge uses rejects (Guitar Pro itself opens it). Adding a rest to that voice in Guitar Pro fixes it.", ex);
            throw new InvalidDataException("This Guitar Pro file is invalid, truncated, or unsupported.", ex);
        }
        ImportGuard.CheckCurrent();
        _gp3To5 =data.AsSpan(0, Math.Min(data.Length, 32)).IndexOf(Gp3To5Signature) >= 0;
        object root = score;
        LastImportSkippedDuplicates = 0;
        LastImportDuplicateSamples.Clear();

        var project = new SongProject
        {
            Title = FirstNonEmpty(GetString(root, "Title"), GetString(root, "Name")) ?? Path.GetFileNameWithoutExtension(path),
            Subtitle = GetString(root, "SubTitle") ?? "",
            Artist = GetString(root, "Artist") ?? "",
            Album = GetString(root, "Album") ?? "",
            MusicAuthor = FirstNonEmpty(GetString(root, "Music"), GetString(root, "Author")) ?? "",
            LyricsAuthor = FirstNonEmpty(GetString(root, "Words"), GetString(root, "WordsAndMusic")) ?? "",
            Copyright = GetString(root, "Copyright") ?? "",
            TabAuthor = GetString(root, "Tab") ?? "",
            Instructions = GetString(root, "Instructions") ?? "",
            Notice = GetString(root, "Notices") ?? "",
            Lyrics = GetString(root, "Lyrics") ?? "",
            Tempo = ReadTempo(root),
            ImportedFrom = path,
            Tracks = new List<TrackModel>()
        };

        var masterBars = AsObjects(Get(root, "MasterBars"))
            .Take(InputLimits.MaxMeasuresPerTrack + 1).ToList();
        if (masterBars.Count > InputLimits.MaxMeasuresPerTrack)
            throw new InvalidDataException("The Guitar Pro file contains too many measures.");
        ReadTimeSignature(root, project);

        var sourceTracks = AsObjects(Get(root, "Tracks")).Take(InputLimits.MaxTracks + 1).ToList();
        if (sourceTracks.Count > InputLimits.MaxTracks)
            throw new InvalidDataException("The Guitar Pro file contains too many tracks.");
        var budget = new ImportBudget();
        var channel = 0;
        // Mix-table transitions / all-tracks flags alphaTab drops (a reference fade-out lives there).
        try { _rawMixes = score is AlphaTab.Model.Score typed ? GuitarProMixTableScanner.Scan(data, typed) : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _rawMixes = null; }
        try
        {
            foreach (var sourceTrack in sourceTracks)
            {
                ImportGuard.CheckCurrent();
                var track = ConvertTrack(masterBars, sourceTrack, channel, budget);
                if (track.Kind != TrackKind.Drums) channel = NextMelodicChannel(channel);
                project.Tracks.Add(track);
            }
        }
        finally { _rawMixes = null; }

        // Repeats / endings / sections / tempo map are master-bar level: apply them to every track.
        ReadMasterBarInfo(masterBars, project, new GuitarProMixTableScanner.TempoRampFinder(data));
        ImportGuard.CheckCurrent();

        if (project.Tracks.Count == 0)
            throw new InvalidDataException("The Guitar Pro file loaded, but contained no tracks.");

        ProjectValidator.Validate(project);
        project.IsDirty = false;
        return project;
    }

    private sealed class ImportBudget
    {
        private long _measures;
        private long _cells;
        private int _cellCalls;
        private long _notes;
        private long _curvePoints;

        public void AddMeasures(int count)
        {
            ImportGuard.CheckCurrent();   // per converted track: cancel, time and memory budget of a background import
            _measures += count;
            if (_measures > InputLimits.MaxTotalMeasures)
                throw new InvalidDataException("The Guitar Pro file contains too many measures overall.");
        }

        public void AddCells(int count)
        {
            if ((++_cellCalls & 63) == 0) ImportGuard.CheckCurrent();   // every 64 bars converted
            _cells += count;
            if (_cells > InputLimits.MaxTotalCells)
                throw new InvalidDataException("The Guitar Pro file contains too many beats overall.");
        }

        public void AddNotes(int count, ref int measureNotes)
        {
            _notes += count;
            measureNotes += count;
            if (_notes > InputLimits.MaxTotalNotes || measureNotes > InputLimits.MaxNotesPerMeasure)
                throw new InvalidDataException("The Guitar Pro file contains too many notes.");
        }

        public void AddCurvePoints(int count)
        {
            _curvePoints += count;
            if (_curvePoints > InputLimits.MaxTotalCurvePoints)
                throw new InvalidDataException("The Guitar Pro file contains too many bend points.");
        }
    }

    // ---------- master bars: repeats, endings, sections, tempo map ----------

    private static void ReadMasterBarInfo(List<object> masterBars, SongProject project, GuitarProMixTableScanner.TempoRampFinder? rampFinder = null)
    {
        var runningTempo = project.Tempo;
        int? carriedTempo = null; // an automation at the very end of the previous bar
        for (var i = 0; i < masterBars.Count; i++)
        {
            var mb = masterBars[i];
            if (mb is null) continue;

            var repeatStart = GetBool(mb, "IsRepeatStart", false);
            var repeatCount = GetInt(mb, "RepeatCount", 0);
            var endings = GetInt(mb, "AlternateEndings", 0);
            var section = Get(mb, "Section");
            var simile = Get(mb, "SimileMark");
            var tempo = GetInt(Get(mb, "TempoAutomation"), "Value", 0);
            var numerator = GetInt(mb, "TimeSignatureNumerator", 4);
            var denominator = GetInt(mb, "TimeSignatureDenominator", 4);
            var key = Get(mb, "KeySignature");
            // alphaTab 1.8: MasterBar.KeySignatureType (Major/Minor). The other two names do not exist there; a minor key used to be imported as major.
            var keyMode = Get(mb, "KeySignatureType") ?? Get(mb, "KeySignatureMode") ?? Get(mb, "KeyMode");
            var isDoubleBar = GetBool(mb, "IsDoubleBar", false);
            var tripletFeel = Get(mb, "TripletFeel")?.ToString() ?? "";
            var freeTime = GetBool(mb, "IsFreeTime", GetBool(mb, "FreeTime", false));
            var sectionText = section is null ? null : FirstNonEmpty(GetString(section, "Text"), GetString(section, "Marker"));

            if (!string.IsNullOrWhiteSpace(sectionText) && !project.Markers.Any(marker => marker.MeasureIndex == i))
            {
                if (project.Markers.Count >= InputLimits.MaxMarkers)
                    throw new InvalidDataException("The Guitar Pro file contains too many markers or sections.");
                if (sectionText.Length > InputLimits.MaxTitleLength)
                    throw new InvalidDataException("The Guitar Pro file contains an overlong section title.");
                var title = sectionText.Trim();
                project.Markers.Add(new MarkerModel
                {
                    MeasureIndex = i,
                    Title = title,
                    ColorHex = ImportedSectionColors[project.Markers.Count % ImportedSectionColors.Length]
                });
            }

            // Guitar Pro tempo automations: one at the bar start is the bar's tempo change; later ones (a mix-table
            // tempo on a later beat) become tempo points inside the bar. The last one carries on to the next bars.
            var automations = AsObjects(Get(mb, "TempoAutomations"))
                .Select(a => (ratio: GetDouble(a, "RatioPosition", 0), value: GetInt(a, "Value", 0)))
                .Where(a => a.value is >= 20 and <= 400).Take(64)
                // Several automations at the same position: the last one written is the one Guitar Pro plays.
                .GroupBy(a => Math.Round(a.ratio, 4)).Select(g => g.Last()).OrderBy(a => a.ratio).ToList();
            // Transition lengths (beats) of these tempo changes, from the raw mix tables (file order).
            var ramps = automations.Select(a => rampFinder?.Next(a.value) ?? 0).ToList();
            if (automations.Count > 0) tempo = automations[0].ratio < 0.001 ? automations[0].value : 0;
            // A tempo automation at the end of the previous bar (position 1.0) starts this bar; it used to be lost.
            if (tempo is < 20 or > 400 && carriedTempo is { } carried) tempo = carried;
            carriedTempo = automations.Where(a => a.ratio >= 0.999).Select(a => (int?)a.value).LastOrDefault();
            int? barTempoChange = tempo is >= 20 and <= 400 && tempo != runningTempo ? tempo : null;
            var barSlots = Math.Max(1, (int)Math.Round(16.0 * numerator / Math.Max(1, denominator)));
            var midBar = new List<TempoPoint>();
            // A bar-start change with a transition is a ramp point at slot 0 (the bar keeps the previous tempo at its start).
            if (barTempoChange is { } rampTarget && automations.Count > 0 && automations[0].ratio < 0.001 && ramps[0] > 0)
            {
                midBar.Add(new TempoPoint(0, rampTarget, ramps[0] * MusicTime.SlotsPerQuarter));
                barTempoChange = null;
                runningTempo = rampTarget;
            }
            if (barTempoChange is { } newTempo) runningTempo = newTempo;
            for (var ai = 0; ai < automations.Count; ai++)
            {
                var (ratio, value) = automations[ai];
                if (ratio < 0.001 || ratio >= 1) continue;
                if (value == runningTempo) continue;
                midBar.Add(new TempoPoint(Math.Round(ratio * barSlots, 3), value, ramps[ai] * MusicTime.SlotsPerQuarter));
                runningTempo = value;
            }
            foreach (var track in project.Tracks)
            {
                if (i >= track.Measures.Count) continue;
                var measure = track.Measures[i];
                measure.RepeatStart = repeatStart;
                if (repeatCount > 0) { measure.RepeatEnd = true; measure.RepeatCount = Math.Clamp(repeatCount, 2, TabForge.Playback.PlaybackOrder.MaxRepeats); }
                if (endings != 0)
                {
                    measure.AlternateEnding = FirstEndingNumber(endings);
                    // Keep every pass of a multi-pass ending (e.g. 1.2.3.); a single ending needs no mask.
                    var mask = endings & 0xFF;
                    measure.AlternateEndingMask = (mask & (mask - 1)) != 0 ? mask : 0;
                }
                // Store every bar whose metre differs from the song default (the first bar's), including a
                // return to 4/4 after a 2/4 pickup; otherwise such bars would inherit the pickup metre.
                if (numerator > 0 && denominator > 0 &&
                    (numerator != project.TimeSignatureNumerator || denominator != project.TimeSignatureDenominator))
                {
                    measure.TimeSigNum = numerator;
                    measure.TimeSigDenom = denominator;
                }
                // Every tempo change against the tempo running at this bar, including a return to the song tempo.
                if (barTempoChange is { } change) measure.TempoChange = change;
                if (midBar.Count > 0) measure.MidBarTempos = midBar.ToList();
                if (key is not null)
                {
                    measure.KeySignature = KeySignatureValue(key);
                    if (key.ToString()!.Contains("Minor", StringComparison.OrdinalIgnoreCase)) measure.KeySignatureMinor = true;
                }
                if (keyMode is not null) measure.KeySignatureMinor = KeyModeIsMinor(keyMode);
                if (isDoubleBar) measure.IsDoubleBar = true;
                if (!string.IsNullOrWhiteSpace(sectionText)) measure.SectionName = sectionText!;
                // alphaTab reports "NoTripletFeel" when there is no swing; the old check looked for
                // "None", so every measure was marked as swung (and, once swing playback existed,
                // would have been played swung). Only the real triplet feels enable it.
                measure.TripletFeelKind = tripletFeel.Contains("Triplet16th", StringComparison.OrdinalIgnoreCase)
                    ? TripletFeels.Sixteenth : tripletFeel.Contains("Triplet8th", StringComparison.OrdinalIgnoreCase)
                        ? TripletFeels.Eighth : TripletFeels.None;
                measure.TripletFeel = measure.TripletFeelKind != TripletFeels.None;
                measure.FreeTime = freeTime;
                // Section markers and system organization are separate the standard concepts. A section start
                // labels its measure; only an explicit system-break flag forces a new engraved row.
                // alphaTab 1.8.4 exposes no per-bar line-break flags (ForceLineBreak/PreventLineBreak
                // never existed on MasterBar), so there is nothing to read here; both stay false.
                // Pickup measures and navigation directions are real score data: keep them instead of
                // silently discarding them (Directions drives nothing yet, but is no longer lost).
                measure.Anacrusis = GetBool(mb, "IsAnacrusis", false);
                var navigation = Get(mb, "Directions");
                if (navigation is string directionText)
                {
                    if (directionText.Length > InputLimits.MaxUserTextLength)
                        throw new InvalidDataException("The Guitar Pro file contains overlong navigation text.");
                    measure.Directions = directionText;
                }
                else if (navigation is IEnumerable nav)
                {
                    var directionTextBuilder = new StringBuilder();
                    var directionCount = 0;
                    foreach (var direction in nav.Cast<object>().Take(InputLimits.MaxNavigationEntriesPerMeasure + 1))
                    {
                        if (++directionCount > InputLimits.MaxNavigationEntriesPerMeasure)
                            throw new InvalidDataException("A Guitar Pro measure contains too many navigation directions.");
                        var text = direction?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var separatorLength = directionTextBuilder.Length == 0 ? 0 : 1;
                        if (directionTextBuilder.Length + separatorLength + text.Length > InputLimits.MaxUserTextLength)
                            throw new InvalidDataException("The Guitar Pro file contains overlong navigation text.");
                        if (separatorLength > 0) directionTextBuilder.Append(',');
                        directionTextBuilder.Append(text);
                    }
                    measure.Directions = directionTextBuilder.ToString();
                }

                switch (simile?.ToString())
                {
                    case "Simple": measure.SimileOneBar = true; break;
                    case "FirstOfDouble":
                    case "SecondOfDouble": measure.SimileTwoBar = true; break;
                }
            }
        }
    }

    private static int FirstEndingNumber(int flags)
    {
        for (var i = 0; i < 16; i++)
            if ((flags & (1 << i)) != 0) return i + 1;
        return 0;
    }

    private static bool KeyModeIsMinor(object keyMode)
        => keyMode is bool minor ? minor : keyMode.ToString()?.Contains("Minor", StringComparison.OrdinalIgnoreCase) == true;

    private static int KeySignatureValue(object key)
    {
        // alphaTab KeySignature enum: Cb=-7 .. C=0 .. C#=7 in the common form; fall back to -7..7.
        var text = key.ToString() ?? "";
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cb"] = -7, ["Gb"] = -6, ["Db"] = -5, ["Ab"] = -4, ["Eb"] = -3, ["Bb"] = -2, ["F"] = -1,
            ["C"] = 0, ["G"] = 1, ["D"] = 2, ["A"] = 3, ["E"] = 4, ["B"] = 5, ["F#"] = 6, ["C#"] = 7
        };
        if (map.TryGetValue(text, out var v)) return v;
        if (text.Contains("Minor", StringComparison.OrdinalIgnoreCase))
        {
            var tonic = text.Replace("Minor", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "", StringComparison.Ordinal);
            var minorToMajor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = "C", ["E"] = "G", ["B"] = "D", ["F#"] = "A", ["C#"] = "E",
                ["G#"] = "B", ["D#"] = "F#", ["A#"] = "C#", ["D"] = "F", ["G"] = "Bb",
                ["C"] = "Eb", ["F"] = "Ab", ["Bb"] = "Db", ["Eb"] = "Gb", ["Ab"] = "Cb",
                ["Db"] = "E", ["Gb"] = "A", ["Cb"] = "D"
            };
            if (minorToMajor.TryGetValue(tonic, out var major) && map.TryGetValue(major, out v)) return v;
        }
        return 0;
    }

    // ---------- tracks ----------

    private static TrackModel ConvertTrack(List<object> masterBars, object sourceTrack, int fallbackChannel, ImportBudget budget)
    {
        var name = GetString(sourceTrack, "Name") ?? "Imported Track";
        var staves = AsObjects(Get(sourceTrack, "Staves")).Take(InputLimits.MaxStringsPerTrack + 1).ToList();
        if (staves.Count > InputLimits.MaxStringsPerTrack)
            throw new InvalidDataException("A Guitar Pro track contains too many staves or strings.");
        var isPercussion = GetBool(sourceTrack, "IsPercussion", false);

        // Tuning: alphaTab exposes it on the staff; fall back to a sane default per track kind.
        var tuning = ReadTuning(staves);

        var maxString = tuning.Count > 0 ? tuning.Max() : 0;
        var lowerName = name.ToLowerInvariant();
        var isDrums = isPercussion || lowerName.Contains("drum") || lowerName.Contains("percussion");
        // The file's own MIDI program is authoritative (0 = Acoustic Grand Piano is a real program).
        // A few strings does not make a bass: mandolin, violin and ukulele have 4.
        var program = Math.Clamp(GetInt(Get(sourceTrack, "PlaybackInfo"), "Program", 0), 0, 127);
        var fretted = !isDrums && tuning.Count > 0;
        var isBass = !isDrums && (lowerName.Contains("bass") || program is >= 32 and <= 39);
        var kind = isDrums ? TrackKind.Drums : isBass ? TrackKind.Bass : fretted ? TrackKind.Guitar : TrackKind.Keys;
        var midiProgram = isDrums ? 0 : program;

        // Guitar Pro stores mixer volume/balance on a 0..16 scale (8 = centre). TabForge used to
        // discard these entirely, so every imported track played at the default volume and centre pan.
        var playbackInfo = Get(sourceTrack, "PlaybackInfo");
        var gpVolume = GetInt(playbackInfo, "Volume", 16);
        var gpBalance = GetInt(playbackInfo, "Balance", 8);

        var result = new TrackModel
        {
            Name = name,
            Kind = kind,
            MidiChannel = isDrums ? 9 : fallbackChannel,
            MidiProgram = midiProgram,
            InstrumentName = isDrums ? GeneralMidi.DrumKit
                : InstrumentNaming.ForStringCount(GeneralMidi.NameOf(midiProgram), kind, tuning.Count),
            ColorHex = ReadColor(sourceTrack),
            MidiOutputDeviceId = -1,
            Volume = Math.Clamp((int)Math.Round(gpVolume * 127.0 / 16.0), 0, 127),
            Pan = Math.Clamp((int)Math.Round(gpBalance * 127.0 / 16.0), 0, 127),
            StringTunings = isDrums ? new List<int> { 49, 42, 48, 38, 43, 36 } : tuning.Count >= 4 ? tuning : DefaultTuning(kind, isBass ? 4 : 6),
            // (no-string instruments get octave-spaced "strings" so every pitch has a readable TAB number)
            Rig = new RigPreset
            {
                Name = isDrums ? "Imported Drum Kit" : isBass ? "Imported Bass" : "Imported Guitar",
                ArticulationMap = isDrums ? "GM Drums" : isBass ? "Generic Bass" : "Generic Guitar"
            }
        };

        // Bars: merge every staff of the track into the same measure grid (nothing is dropped).
        var barByIndex = new Dictionary<int, List<object>>();
        foreach (var staff in staves)
        {
            var staffBars = AsObjects(Get(staff, "Bars"))
                .Take(InputLimits.MaxMeasuresPerTrack + 1).ToList();
            if (staffBars.Count > InputLimits.MaxMeasuresPerTrack)
                throw new InvalidDataException("The Guitar Pro file contains too many measures in a track.");
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

        var barCount = Math.Max(barByIndex.Count, masterBars.Count);
        if (barCount == 0) barCount = 32;
        if (barCount > InputLimits.MaxMeasuresPerTrack)
            throw new InvalidDataException("The Guitar Pro file contains too many measures in a track.");
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

        LinkTieOrigins(result);

        // The song's very first beat carries the track's initial mixer values, not a Mix Table change.
        if (result.Measures.FirstOrDefault()?.Cells.FirstOrDefault(c => c.Notes.Count > 0 || c.IsRest || c.Mix is not null) is { } opening)
            opening.Mix = null;

        // Tuning check against the notes themselves: each note's sounding pitch minus its fret is its
        // string's open pitch. When the file's tuning field was not readable (e.g. Drop D came in as
        // standard), the notes' consistent open pitches win, so the header, fretboard and edits match.
        if (fretted && result.StringTunings.Count > 0)
        {
            var inferred = result.StringTunings.ToList();
            var changed = false;
            for (var s = 0; s < inferred.Count; s++)
            {
                var opens = result.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes)
                    .Where(n => n.StringIndex == s && !n.Dead && n.MidiValue > 0 && result.Capo == 0)
                    .Select(n => n.MidiValue - n.Fret).GroupBy(v => v).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (opens is null || opens.Count() < 3 || opens.Key == inferred[s] || opens.Key is < 12 or > 108) continue;
                inferred[s] = opens.Key;
                changed = true;
            }
            if (changed) result.StringTunings = inferred;
        }

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

    /// <summary>
    /// A tie is stored on its destination note (<see cref="TabNote.Tied"/>); the "Tie" technique marks its origin.
    /// alphaTab leaves the origin unflagged when it cannot link a tie (drum ties, some GP3-5 files), so a re-export
    /// and re-import then "gained" origin flags. Marking the previous note on the same string here makes the two agree.
    /// </summary>
    internal static void LinkTieOrigins(TrackModel track)
    {
        foreach (var voice in new[] { 0, 1 })
        {
            var cells = track.Measures.SelectMany(m => voice == 0 ? m.Cells : m.Voice2Cells).ToList();
            for (var k = 1; k < cells.Count; k++)
                foreach (var destination in cells[k].Notes)
                {
                    if (!destination.Tied || destination.IsGraceNote) continue;
                    for (var j = k - 1; j >= 0 && j >= k - 64; j--)
                    {
                        var origin = cells[j].Notes.FirstOrDefault(n => !n.IsGraceNote && n.StringIndex == destination.StringIndex);
                        if (origin is null) continue;
                        origin.Techniques.Add("Tie");
                        break;
                    }
                }
        }
    }

    private static void ConvertBar(object bar, MeasureModel measure, TrackModel track, ImportBudget budget, ref int measureNotes)
    {
        // GP6/7/8 drum notes point into the track's own articulation list (see DrumPitch).
        var drumArticulations = track.Kind == TrackKind.Drums
            ? AsObjects(Get(Get(Get(bar, "Staff"), "Track"), "PercussionArticulations")).ToList()
            : null;
        var clef = Get(bar, "Clef")?.ToString();
        if (!string.IsNullOrWhiteSpace(clef) && measure.Clef == Clefs.Guitar) measure.Clef = clef!;

        var slots = measure.Cells.Count;
        var tone = GetInt(bar, "KeySignature", int.MinValue);
        if (tone != int.MinValue && measure.KeySignature is null) measure.KeySignature = tone;

        var voiceNumber = 0;
        var beatsInMeasure = 0;
        foreach (var voice in AsObjects(Get(bar, "Voices")))
        {
            if (voiceNumber >= InputLimits.MaxVoicesPerMeasure)
                throw new InvalidDataException("A Guitar Pro measure contains too many voices.");
            // GP tracks can expose two independent voices. Additional alphaTab voice lanes are
            // conservatively merged into voice 2 rather than flattened into voice 1.
            if (voiceNumber > 0 && measure.Voice2Cells.Count == 0)
                budget.AddCells(Math.Max(16, measure.Cells.Count));
            var cells = measure.CellsForVoice(voiceNumber == 0 ? 0 : 1, create: voiceNumber > 0);
            voiceNumber++;
            var lastSlot = -1;
            foreach (var beat in AsObjects(Get(voice, "Beats")))
            {
                if (++beatsInMeasure > InputLimits.MaxBeatsPerMeasure)
                    throw new InvalidDataException("A Guitar Pro measure contains too many beats.");
                var start = BeatStartTicks(beat);
                var graceBeat = Get(beat, "GraceType")?.ToString() is "BeforeBeat" or "OnBeat" or "BendGrace";
                var displayStart = graceBeat ? GetInt(beat, "DisplayStart", start) : start;
                var placementStart = graceBeat ? displayStart : start;
                var tickSlot = placementStart >= 0
                    ? (int)Math.Round(placementStart / (double)TicksPerSlot)
                    : lastSlot + 1;
                // Keep the true tick position when the grid can represent it; otherwise push the beat
                // to the next free slot. Nothing is ever dropped - fidelity beats grid alignment.
                // Bars with more beats than sixteenth cells (32nds, 64ths) get extra cells; the true timing
                // lives in RhythmicPosition. Clamping here used to merge/drop the bar's last notes.
                var slot = Math.Max(0, Math.Max(tickSlot, lastSlot + 1));
                if (!graceBeat)
                    while (slot >= cells.Count && cells.Count < slots * 8) cells.Add(new TabCell());
                // Grace beats use their notated display position to share the principal beat's
                // rhythmic cell; their distinct playback offset remains stored on the grace note.
                // They must not advance the voice cursor before the principal note is merged.
                if (!graceBeat) lastSlot = slot;
                var durationSlots = BeatSlots(beat);

                if (slot >= cells.Count) continue;
                var cell = cells[slot];
                if (placementStart >= 0) cell.RhythmicPosition ??= placementStart / (double)TicksPerSlot;

                // Annotations belong to the beat, not to its notes. Read them first so a comment, chord
                // or lyric on a rest (or an empty beat) is never dropped - real Guitar Pro files put
                // dozens of comments on beats that carry no notes at all.
                ReadAnnotations(beat, cell, budget);

                if (GetBool(beat, "IsRest", false))
                {
                    // A rest may only claim an untouched cell. Previously this overwrote the duration
                    // of a note already placed by another voice, which turned an eighth-note downbeat
                    // into a quarter - the "first note of each bar is too long / notes melt together"
                    // defect (the score looked wrong next to TuxGuitar).
                    if (cell.Notes.Count == 0 && !cell.IsRest)
                    {
                        ApplyDuration(cell, beat);
                        cell.IsRest = true;
                    }
                    continue;
                }

                // The first voice to place a note owns the cell's duration; another voice merges its
                // notes into the same cell without rewriting that duration. A note also replaces an
                // earlier rest in the same cell.
                if (cell.Notes.Count == 0 || cell.Notes.All(existing => existing.IsGraceNote))
                {
                    cell.IsRest = false;
                    ApplyDuration(cell, beat);
                }

                var sourceNotes = AsObjects(Get(beat, "Notes")).Take(InputLimits.MaxNotesPerCell + 1).ToList();
                if (sourceNotes.Count > InputLimits.MaxNotesPerCell)
                    throw new InvalidDataException("A Guitar Pro beat contains too many notes.");
                budget.AddNotes(sourceNotes.Count, ref measureNotes);
                foreach (var sourceNote in sourceNotes)
                {
                    // Fretted instruments number their strings 1..n; piano/keys/other instruments
                    // have no string at all. Those notes must still be imported (their pitch lives
                    // in RealValue), otherwise whole tracks silently lose their content.
                    var tiedDestination = GetBool(sourceNote, "IsTieDestination", false);
                    // The standard drum ties often have no pitch or articulation on the destination at all.
                    // alphaTab retains the original note in TieOrigin; using the drum kit's default
                    // tuning here fabricates a crash cymbal (49) instead of continuing the real hit.
                    var pitchSource = tiedDestination && Get(sourceNote, "TieOrigin") is { } origin
                        ? origin : sourceNote;
                    var gpString = GetInt(pitchSource, "String", 0);
                    var stringIndex = gpString > 0
                        ? Math.Clamp(track.StringTunings.Count - gpString, 0, Math.Max(0, track.StringTunings.Count - 1))
                        : 0;
                    var rawFret = GetInt(pitchSource, "Fret", 0);
                    var fret = Math.Max(0, rawFret);
                    var computed = track.StringTunings.Count > stringIndex ? track.PitchOf(stringIndex, fret) : fret;   // tuning + capo + fret, the shared rule
                    var real = GetInt(pitchSource, "RealValue", 0);
                    // A dead note has fret -1 in alphaTab, so its RealValue is a semitone below the string; it is unpitched,
                    // and the exported file (fret 0) reads back as the open string, so use the string's own pitch.
                    var midi = real > 0 && rawFret >= 0 ? real : computed;
                    if (midi <= 0) midi = computed;
                    // Harmonics: the standard sounding pitch. Natural = the string's harmonic at that fret; artificial,
                    // pinch, tapped, semi = the fretted note plus the harmonic node's interval (12 = octave).
                    // alphaTab's RealValue stacked extra octaves (an A.H. at fret 3 came in 34 semitones up).
                    var harmonic = Get(sourceNote, "HarmonicType")?.ToString();
                    double? harmonicFret = null;
                    if (!string.IsNullOrEmpty(harmonic) && harmonic != "None" && track.Kind != TrackKind.Drums &&
                        stringIndex < track.StringTunings.Count)
                    {
                        var open = track.StringTunings[stringIndex] + Math.Max(0, track.Capo);   // the capo raises the open string
                        var node = Get(sourceNote, "HarmonicValue") is { } hv ? Convert.ToDouble(hv, System.Globalization.CultureInfo.InvariantCulture) : 12;
                        midi = HarmonicMidi(harmonic!, open, fret, node);
                        harmonicFret = Math.Round(node, 1);
                    }
                    // No string in the file (piano, whistle, synth...): place the pitch on an octave "string".
                    if (gpString <= 0 && track.Kind == TrackKind.Keys && midi > 0)
                        (stringIndex, fret) = PlacePitch(track.StringTunings, midi);
                    // Drum notes: the TAB shows the GM percussion number (like the reference) on a line per kit group.
                    if (track.Kind == TrackKind.Drums)
                    {
                        // A percussion note has no string/fret fallback. Prefer its GM articulation
                        // when available, then its sounding value; never synthesize a kit-tuning note.
                        midi = DrumPitch(pitchSource, drumArticulations);
                        // Keep every hit that is in the file: a value outside the GM drum
                        // range (e.g. a "0" some older tabs use) stays visible and silent instead of vanishing.
                        if (midi is < 0 or > 127) continue;
                        stringIndex = DrumLine(midi);
                        fret = midi;
                    }

                    var note = new TabNote
                    {
                        StringIndex = stringIndex,
                        Fret = fret,
                        MidiValue = midi,
                        // alphaTab has no Velocity: loudness is Note.Dynamics (falling back to Beat.Dynamics).
                        Velocity = Dynamics.Clamp(Dynamics.VelocityForAlphaTab(
                            (Get(sourceNote, "Dynamics") ?? Get(beat, "Dynamics"))?.ToString())),
                        // Guitar Pro flags the *destination* note of a tie; playback must sustain the
                        // origin instead of attacking again, so the flag lives on the destination.
                        Tied = tiedDestination
                    };
                    note.HarmonicFret = harmonicFret;
                    ReadTechniques(sourceNote, beat, note);
                    ReadBendPoints(sourceNote, note, budget);
                    ReadSlideTarget(sourceNote, note);
                    ReadCellMarks(sourceNote, beat, cell, note);
                    note.LeftHandFinger = FingerOf(Get(sourceNote, "LeftHandFinger"));
                    note.RightHandFinger = FingerOf(Get(sourceNote, "RightHandFinger"));
                    // The standard per-note duration % (alphaTab keeps it on the note, as a 0..1 fraction).
                    // GP3-5 files carry no such value and alphaTab leaves garbage (a denormal) there for palm-muted
                    // notes, so it is only trusted from GPX / .gp files and only when sane.
                    if (!graceBeat && SanePerNoteDurationPercent(GetDouble(sourceNote, "DurationPercent", 1.0), _gp3To5) is { } durationPercent)
                        cell.SoundDurationPercent = durationPercent;

                    // Merging voices/staves can surface the same note twice (the same string at the same
                    // instant, or the same pitch on a keyboard part). A duplicate is not musical content:
                    // it doubles the velocity and makes the release collide with the retrigger, which
                    // synths swallow. Keep one.
                    // A grace note and the principal note it ornaments are never duplicates (a flam on the snare
                    // used to lose its main hit here).
                    var duplicate = cell.Notes.Any(n => n.IsGraceNote == note.IsGraceNote &&
                        n.StringIndex == note.StringIndex && n.Fret == note.Fret && n.MidiValue == note.MidiValue);
                    if (!duplicate)
                    {
                        if (cell.Notes.Count >= InputLimits.MaxNotesPerCell)
                            throw new InvalidDataException("A Guitar Pro beat contains too many notes.");
                        cell.Notes.Add(note);
                    }
                    else
                    {
                        LastImportSkippedDuplicates++;
                        if (LastImportDuplicateSamples.Count < 12)
                            LastImportDuplicateSamples.Add($"bar{measure.Number} slot{slot} midi{note.MidiValue} grace={note.IsGraceNote}/{cell.Notes.First(n => n.MidiValue == note.MidiValue && n.StringIndex == note.StringIndex).IsGraceNote} start={placementStart} dur={durationSlots} tie={note.Tied}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// GM drum sound of a percussion note. Guitar Pro 3-5 store the GM number itself. Guitar Pro 6/7/8
    /// (.gpx/.gp) store an index into the track's percussion articulation list, whose entry names the
    /// sounding MIDI number (OutputMidiNumber): reading that index as a GM number dropped every GP7/8 drum
    /// note (indices 0..26 fall outside the drum range) and turned GPX drums into wrong or missing hits.
    /// </summary>
    internal static int DrumPitch(object sourceNote, IReadOnlyList<object>? articulations = null)
    {
        var pitchSource = GetBool(sourceNote, "IsTieDestination", false) && Get(sourceNote, "TieOrigin") is { } origin
            ? origin : sourceNote;
        var articulation = GetInt(pitchSource, "PercussionArticulation", -1);
        if (articulations is { Count: > 0 } && articulation >= 0 && articulation < articulations.Count)
        {
            var output = GetInt(articulations[articulation], "OutputMidiNumber", 0);
            if (output is >= 27 and <= 87) return output;
        }
        if (articulation is >= 27 and <= 87) return articulation;
        // the reference's own articulation ids above the GM range (91 snare rim shot, 92 half-open hi-hat,
        // 93 ride edge, 95 splash, 97 crash...): alphaTab's Guitar Pro articulation table gives the MIDI sound.
        if (articulation > 87 && GpArticulations.OutputMidi(articulation) is >= 27 and <= 87 and var known)
            return known;
        var real = GetInt(pitchSource, "RealValue", 0);
        if (real is >= 27 and <= 87) return real;
        // Guitar Pro 3-5 write the GM drum number as the note's fret; many drum hits arrive with neither an
        // articulation nor a sounding value, which dropped up to 40% of a GP3-5 drum part.
        var fret = GetInt(pitchSource, "Fret", 0);
        return fret is >= 27 and <= 87 ? fret : real;
    }

    /// <summary>
    /// Beat start in ticks within the bar. alphaTab exposes several historical names for this; the
    /// most reliable in 1.8.x is PlaybackStart, so try each and fall back to -1 (unknown) rather than
    /// silently pretending every beat starts at zero.
    /// </summary>
    private static int BeatStartTicks(object beat)
    {
        foreach (var name in new[] { "PlaybackStart", "PlayStart", "DisplayStart", "AbsolutePlaybackStart" })
        {
            var value = GetInt(beat, name, int.MinValue);
            if (value != int.MinValue) return value;
        }
        return -1;
    }

    private static void ApplyDuration(TabCell cell, object beat)
    {
        var denominator = DurationToDenominator(Get(beat, "Duration"));
        cell.DurationDenominator = denominator;
        cell.Dots = Math.Clamp(GetInt(beat, "Dots", 0), 0, 2);

        // Tuplets: 3:2 is rendered as a triplet in our model.
        var num = GetInt(beat, "TupletNumerator", 0);
        var den = GetInt(beat, "TupletDenominator", 0);
        if (num == 0) num = GetInt(Get(beat, "Tuplet"), "Numerator", 0);
        if (den == 0) den = GetInt(Get(beat, "Tuplet"), "Denominator", 0);
        cell.IsTriplet = num == 3 && den == 2;
        if (num > 0 && den > 0)
        {
            cell.TupletNumerator = num;
            cell.TupletDenominator = den;
        }
        var soundDuration = GetInt(beat, "DurationPercent", GetInt(beat, "SoundDurationPercent", 100));
        cell.SoundDurationPercent = Math.Clamp(soundDuration, 1, 200);
        cell.OctaveShiftSemitones = ReadOctaveShift(Get(beat, "Ottava") ?? Get(beat, "OctaveShift"));
        // alphaTab's Beat.BeamingMode describes the join between this beat and the NEXT one
        // (ForceSplitToNext, ForceMergeWithNext, ForceSplitOnSecondaryToNext); TabForge stores the
        // override on the beat that starts the group, so a split reads the previous beat's mode.
        var previous = Get(Get(beat, "PreviousBeat"), "BeamingMode")?.ToString() ?? "";
        var own = Get(beat, "BeamingMode")?.ToString() ?? "";
        if (own == "ForceMergeWithNext" || previous == "ForceMergeWithNext") cell.BeamMode = BeamMode.Force;
        else if (previous == "ForceSplitToNext") cell.BeamMode = BeamMode.Break;
        cell.BreakSecondaryBeamBefore = previous == "ForceSplitOnSecondaryToNext";
        // Forced stem direction: Beat.InvertBeamDirection flips the automatic choice;
        // PreferredBeamDirection (Up/Down) is an explicit direction.
        var preferred = Get(beat, "PreferredBeamDirection")?.ToString() ?? "";
        if (preferred == "Up") cell.StemDirection = StemDirection.Up;
        else if (preferred == "Down") cell.StemDirection = StemDirection.Down;
        else if (GetBool(beat, "InvertBeamDirection", false)) cell.StemDirection = StemDirection.Invert;
    }

    private static int ReadOctaveShift(object? value)
    {
        var text = value?.ToString() ?? "";
        if (text.Contains("15ma", StringComparison.OrdinalIgnoreCase) || text.Contains("TwoOctavesAbove", StringComparison.OrdinalIgnoreCase)) return 24;
        if (text.Contains("15mb", StringComparison.OrdinalIgnoreCase) || text.Contains("TwoOctavesBelow", StringComparison.OrdinalIgnoreCase)) return -24;
        if (text.Contains("8va", StringComparison.OrdinalIgnoreCase) || text.Contains("OctaveAbove", StringComparison.OrdinalIgnoreCase)) return 12;
        if (text.Contains("8vb", StringComparison.OrdinalIgnoreCase) || text.Contains("OctaveBelow", StringComparison.OrdinalIgnoreCase)) return -12;
        return 0;
    }

    private static int BeatSlots(object beat)
    {
        var denominator = DurationToDenominator(Get(beat, "Duration"));
        var slots = Math.Max(1, 16 / Math.Clamp(denominator, 1, 64));
        var dots = Math.Clamp(GetInt(beat, "Dots", 0), 0, 2);
        if (dots == 1) slots = (int)Math.Round(slots * 1.5);
        else if (dots >= 2) slots = (int)Math.Round(slots * 1.75);
        var num = GetInt(beat, "TupletNumerator", 0);
        var den = GetInt(beat, "TupletDenominator", 0);
        if (num > 0 && den > 0) slots = Math.Max(1, (int)Math.Round(slots * den / (double)num));
        return Math.Max(1, slots);
    }

    /// <summary>
    /// Beat-level annotations: comment text, chord name, lyrics, fermata and grace notes. Guitar Pro
    /// stores these on the beat, so they must be read even when the beat has no notes (a comment on a
    /// rest is still content), and merged rather than overwritten when voices share a cell.
    /// </summary>
    private static void ReadAnnotations(object beat, TabCell cell, ImportBudget budget)
    {
        ReadBeatPitchEffects(beat, cell, budget);

        var text = GetString(beat, "Text");
        if (!string.IsNullOrWhiteSpace(text)) cell.Text = Merge(cell.Text, text!, InputLimits.MaxUserTextLength);

        // Mix Table points (volume / pan / instrument automations on a beat). The very first beat's
        // values are the track's initial settings, already imported on the track itself.
        foreach (var automation in AsObjects(Get(beat, "Automations")).Take(16))
        {
            var kind = Get(automation, "Type")?.ToString();
            var value = (int)Math.Round(Convert.ToDouble(Get(automation, "Value") ?? 0, System.Globalization.CultureInfo.InvariantCulture));
            if (kind is not ("Volume" or "Balance" or "Instrument")) continue;
            cell.Mix ??= new MixChange();
            switch (kind)
            {
                case "Volume": cell.Mix.Volume = Math.Clamp(value, 0, 16); break;
                case "Balance": cell.Mix.Pan = Math.Clamp(value - 8, -8, 8); break;
                case "Instrument": cell.Mix.Program = Math.Clamp(value, 0, 127); break;
            }
        }
        if (_rawMixes is not null && _rawMixes.TryGetValue(beat, out var raw))
        {
            cell.Mix ??= new MixChange();
            cell.Mix.TransitionBeats = Math.Clamp(raw.TransitionBeats, 0, 64);
            cell.Mix.AllTracks = raw.AllTracks;
            if (raw.Chorus >= 0) cell.Mix.Chorus = Math.Clamp(raw.Chorus, 0, 16);
            if (raw.Reverb >= 0) cell.Mix.Reverb = Math.Clamp(raw.Reverb, 0, 16);
            if (raw.Phaser >= 0) cell.Mix.Phaser = Math.Clamp(raw.Phaser, 0, 16);
            if (raw.Tremolo >= 0) cell.Mix.Tremolo = Math.Clamp(raw.Tremolo, 0, 16);
        }

        if (GetBool(beat, "HasChord", false) || Get(beat, "Chord") is not null)
        {
            var chord = Get(beat, "Chord");
            var name = chord is null ? null : GetString(chord, "Name");
            if (!string.IsNullOrWhiteSpace(name)) cell.ChordName = Merge(cell.ChordName, name!, InputLimits.MaxTitleLength);
        }

        // alphaTab exposes lyrics as a list of lines per beat; keep them all.
        if (Get(beat, "Lyrics") is IEnumerable lines)
        {
            var lyricText = new StringBuilder();
            var lineCount = 0;
            foreach (var lineValue in lines.Cast<object>().Take(InputLimits.MaxLyricsLinesPerBeat + 1))
            {
                if (++lineCount > InputLimits.MaxLyricsLinesPerBeat)
                    throw new InvalidDataException("A Guitar Pro beat contains too many lyric lines.");
                var line = lineValue?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(line)) continue;
                var separatorLength = lyricText.Length == 0 ? 0 : 1;
                if (lyricText.Length + separatorLength + line.Length > InputLimits.MaxLyricsLength)
                    throw new InvalidDataException("A Guitar Pro beat contains overlong lyrics.");
                if (separatorLength > 0) lyricText.Append('\n');
                lyricText.Append(line);
            }
            if (lyricText.Length > 0) cell.Lyrics = Merge(cell.Lyrics, lyricText.ToString(), InputLimits.MaxLyricsLength);
        }

        if (IsSet(Get(beat, "Fermata"))) cell.Fermata = true;

        var grace = Get(beat, "GraceType")?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(grace) && !grace.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            cell.IsGrace = true;
            cell.GraceBeforeBeat = grace.Equals("BeforeBeat", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Per-note marks the engraver draws once for the whole beat: accent, staccato and fades.</summary>
    private static void ReadCellMarks(object sourceNote, object beat, TabCell cell, TabNote note)
    {
        var accent = Get(sourceNote, "Accentuated")?.ToString() ?? "";
        if (accent.Equals("Heavy", StringComparison.OrdinalIgnoreCase)) cell.Accent = 2;
        else if (accent.Equals("Normal", StringComparison.OrdinalIgnoreCase) && cell.Accent < 2) cell.Accent = 1;
        else if (GetBool(sourceNote, "Accent", false) && cell.Accent < 2) cell.Accent = 1;
        if (GetBool(sourceNote, "IsStaccato", false)) cell.Staccato = true;

        var fade = Get(beat, "Fade")?.ToString() ?? "";
        if (GetBool(beat, "FadeIn", false) || fade.Equals("FadeIn", StringComparison.OrdinalIgnoreCase)) note.Techniques.Add("FadeIn");
        else if (fade.Equals("FadeOut", StringComparison.OrdinalIgnoreCase)) note.Techniques.Add("FadeOut");
    }

    /// <summary>Appends a second annotation instead of losing it when two voices share one cell.</summary>
    private static string Merge(string? existing, string addition, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            if (addition.Length > maximumLength) throw new InvalidDataException("The Guitar Pro file contains overlong text.");
            return addition;
        }
        if (existing!.Contains(addition, StringComparison.Ordinal)) return existing;
        if (existing.Length + 3L + addition.Length > maximumLength)
            throw new InvalidDataException("The Guitar Pro file contains overlong text.");
        return existing + " / " + addition;
    }

    private static bool IsSet(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        var text = value.ToString();
        return !string.IsNullOrWhiteSpace(text) && text != "0" && !text.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadTechniques(object sourceNote, object beat, TabNote note)
    {
        var t = note.Techniques;
        if (GetBool(sourceNote, "IsPalmMute", false) || GetBool(beat, "IsPalmMute", false)) t.Add("PalmMute");
        if (GetBool(sourceNote, "IsDead", false)) { t.Add("Dead"); note.Dead = true; }
        if (GetBool(sourceNote, "IsGhost", false)) { t.Add("Ghost"); note.Ghost = true; }
        if (GetBool(sourceNote, "IsLetRing", false) || GetBool(beat, "IsLetRing", false)) t.Add("LetRing");
        if (GetBool(beat, "DeadSlapped", false))
        {
            t.Add("DeadSlapped");
            t.Add("Slap");
            t.Add("Dead");
            note.Dead = true;
        }
        if (GetBool(sourceNote, "IsHammerPullOrigin", false))
        {
            t.Add("HOPO");
            t.Add("HOPOOrigin");
        }
        if (GetBool(sourceNote, "IsHammerPullDestination", false))
        {
            t.Add("HOPO");
            t.Add("HOPODestination");
        }
        if (GetBool(sourceNote, "IsLeftHandTapped", false)) t.Add("LeftTap");
        if (GetBool(sourceNote, "IsTrill", false)) t.Add("Trill");
        if (GetBool(beat, "IsTremolo", false) || GetBool(sourceNote, "IsTremolo", false)) t.Add("TremoloPick");
        if (GetBool(beat, "Slap", false) || GetBool(beat, "IsSlap", false)) t.Add("Slap");
        if (GetBool(beat, "Pop", false) || GetBool(beat, "IsPop", false)) t.Add("Pop");
        // alphaTab exposes the Guitar Pro tapping technique as Beat.Tap (an enum), not IsTap.
        // Keep the older boolean probes as compatibility fallbacks for other parser versions.
        if (IsSet(Get(beat, "Tap")) || GetBool(beat, "IsTap", false) || GetBool(sourceNote, "IsTap", false))
            t.Add("Tapping");
        if (GetBool(beat, "IsAccent", false) || GetBool(sourceNote, "Accent", false))
            note.Techniques.Add("Accent");

        AddEnumTechnique(sourceNote, t, "Vibrato", "Vibrato", "Wide", "WideVibrato");
        AddEnumTechnique(beat, t, "Vibrato", "Vibrato", "Wide", "WideVibrato");
        ReadSlideTechniques(sourceNote, t);
        AddEnumTechnique(sourceNote, t, "HarmonicType", "Harmonic", "Artificial", "ArtificialHarmonic");
        AddEnumTechnique(beat, t, "BrushType", "BrushDown", "Up", "BrushUp");
        switch (Get(beat, "BrushType")?.ToString()) { case "ArpeggioDown": t.Add("ArpeggioDown"); break; case "ArpeggioUp": t.Add("ArpeggioUp"); break; }
        switch (Get(beat, "WahPedal")?.ToString()) { case "Open": t.Add("WahOpen"); break; case "Closed": t.Add("WahClose"); break; }
        AddEnumTechnique(beat, t, "PickStroke", "PickDown", "Up", "PickUp");
        switch (Get(beat, "GraceType")?.ToString())
        {
            case "BeforeBeat": t.Add("GraceBefore"); break;
            case "OnBeat": t.Add("GraceOnBeat"); break;
            case "BendGrace": t.Add("GraceBend"); break;
        }

        var tremoloPicking = Get(beat, "TremoloPicking");
        if (GetBool(beat, "IsTremolo", false) || IsSet(tremoloPicking)) t.Add("TremoloPick");

        // Harmonic kinds are kept distinct for display only: alphaTab's RealValue already contains the
        // harmonic sounding pitch (e.g. a fret-14 artificial harmonic is RealValue 74, fretted 62).
        switch (Get(sourceNote, "HarmonicType")?.ToString())
        {
            case "Natural": t.Add("Harmonic"); break;
            case "Artificial": t.Add("ArtificialHarmonic"); break;
            case "Pinch": t.Add("PinchHarmonic"); break;
            case "Tap": t.Add("TapHarmonic"); break;
            case "Semi": t.Add("SemiHarmonic"); break;
            case "Feedback": t.Add("FeedbackHarmonic"); break;
        }

        if (Get(sourceNote, "BendPoints") is IEnumerable bends && bends.Cast<object>().Any())
        {
            t.Add("Bend");
            note.BendTypeName = Get(sourceNote, "BendType")?.ToString() ?? "";
            note.BendStyleName = Get(sourceNote, "BendStyle")?.ToString() ?? "";
        }
        if (GetBool(sourceNote, "IsTieOrigin", false)) t.Add("Tie");

        if (GetBool(sourceNote, "IsTrill", false))
        {
            note.TrillTargetMidi = GetInt(sourceNote, "TrillValue", 0);
            var trillSpeed = Get(sourceNote, "TrillSpeed");
            note.TrillDurationDenominator = IsSet(trillSpeed) ? DurationToDenominator(trillSpeed) : 0;
        }

        var graceType = Get(beat, "GraceType")?.ToString();
        if (graceType is "BeforeBeat" or "OnBeat" or "BendGrace")
        {
            note.IsGraceNote = true;
            note.GraceBeforeBeat = graceType == "BeforeBeat";
            var playbackStart = GetInt(beat, "PlaybackStart", 0);
            var displayStart = GetInt(beat, "DisplayStart", Math.Max(0, playbackStart));
            note.GraceOnsetOffsetSlots = (playbackStart - displayStart) / (double)TicksPerSlot;
            note.GraceDurationSlots = BeatSlots(beat);
        }

        var whammyType = Get(beat, "WhammyBarType")?.ToString();
        if (GetBool(beat, "HasWhammyBar", false) || IsSet(whammyType) ||
            AsObjects(Get(beat, "WhammyBarPoints")).Any())
        {
            t.Add("TremBar");
            if (!string.IsNullOrWhiteSpace(whammyType) && !whammyType.Equals("None", StringComparison.OrdinalIgnoreCase))
                t.Add("TremBar" + whammyType);
        }
    }

    private static void ReadBeatPitchEffects(object beat, TabCell cell, ImportBudget budget)
    {
        if (Get(beat, "WhammyBarPoints") is IEnumerable points)
        {
            var sourcePoints = points.Cast<object>().Take(InputLimits.MaxCurvePoints + 1).ToList();
            if (sourcePoints.Count > InputLimits.MaxCurvePoints)
                throw new InvalidDataException("A Guitar Pro beat contains too many whammy-bar points.");
            budget.AddCurvePoints(sourcePoints.Count);
            foreach (var point in sourcePoints)
            {
                var offset = GetDouble(point, "Offset", double.NaN);
                var value = GetDouble(point, "Value", double.NaN);
                if (!double.IsFinite(offset) || !double.IsFinite(value)) continue;
                if (!cell.WhammyPoints.Any(existing => Math.Abs(existing.Offset - offset) < 0.001 &&
                                                       Math.Abs(existing.Value - value) < 0.001))
                {
                    if (cell.WhammyPoints.Count >= InputLimits.MaxCurvePoints)
                        throw new InvalidDataException("A Guitar Pro beat contains too many whammy-bar points.");
                    cell.WhammyPoints.Add(new BendPointModel { Offset = offset, Value = value });
                }
            }
        }

        var tremolo = TremoloDenominator(beat, _gp3To5);
        if (tremolo > 0) cell.TremoloPickDenominator = tremolo;

        // Brush/arpeggio spread: Guitar Pro delays each following string by a third of the stroke's tick value
        // (a 1/16 stroke reads 120 ticks and steps 40 ticks per string; a 1/4 stroke 480 and 160).
        var brushTicks = GetDouble(beat, "BrushDuration", 0);
        if (double.IsFinite(brushTicks) && brushTicks > 0)
            cell.BrushStepSlots = Math.Round(brushTicks / 3.0 / TicksPerSlot, 4);
    }

    /// <summary>
    /// Written tremolo-picking speed as a note denominator (8, 16, 32), or 0 for none. Guitar Pro 3-5 files write
    /// the number of slashes (1 = 1/8, 2 = 1/16, 3 = 1/32) and alphaTab exposes that as TremoloPicking.Marks, but
    /// its TremoloSpeed name is one step slow there (1/8 reads as Quarter), which halved the playback speed.
    /// </summary>
    internal static int TremoloDenominator(object beat, bool gp3To5)
    {
        var marks = GetDouble(Get(beat, "TremoloPicking"), "Marks", double.NaN);
        if (gp3To5 && double.IsFinite(marks) && marks >= 1)
            return Math.Clamp(8 << (int)Math.Round(marks - 1), 8, 64);
        var speed = Get(beat, "TremoloSpeed");
        if (IsSet(speed)) return DurationToDenominator(speed);
        if (double.IsFinite(marks) && marks >= 1)
            return Math.Clamp(8 << (int)Math.Round(marks - 1), 8, 64);
        return 0;
    }

    private static void ReadSlideTechniques(object sourceNote, HashSet<string> techniques)
    {
        switch (Get(sourceNote, "SlideInType")?.ToString())
        {
            case "IntoFromBelow": techniques.Add("SlideInBelow"); break;
            case "IntoFromAbove": techniques.Add("SlideInAbove"); break;
        }

        switch (Get(sourceNote, "SlideOutType")?.ToString())
        {
            case "Shift": techniques.Add("ShiftSlide"); break;
            case "Legato": techniques.Add("LegatoSlide"); break;
            case "OutUp": techniques.Add("SlideOutUp"); break;
            case "OutDown": techniques.Add("SlideOutDown"); break;
            case "PickSlideUp": techniques.Add("PickSlideUp"); break;
            case "PickSlideDown": techniques.Add("PickSlideDown"); break;
        }
    }

    /// <summary>Reads a slide's sounding target pitch (the note the slide resolves to), if any.</summary>
    private static void ReadSlideTarget(object sourceNote, TabNote note)
    {
        var target = Get(sourceNote, "SlideTarget");
        if (target is null) return;
        var midi = GetInt(target, "RealValue", 0);
        if (midi > 0 && midi <= 127) note.SlideTargetMidi = midi;
    }

    /// <summary>Reads a bend curve (offset 0..1 of the note, value in semitones) so playback can ramp.</summary>
    private static void ReadBendPoints(object sourceNote, TabNote note, ImportBudget budget)
    {
        if (Get(sourceNote, "BendPoints") is not IEnumerable points) return;
        var sourcePoints = points.Cast<object>().Take(InputLimits.MaxCurvePoints + 1).ToList();
        if (sourcePoints.Count > InputLimits.MaxCurvePoints)
            throw new InvalidDataException("A Guitar Pro note contains too many bend points.");
        budget.AddCurvePoints(sourcePoints.Count);
        foreach (var p in sourcePoints)
        {
            var offset = GetDouble(p, "Offset", double.NaN);
            var value = GetDouble(p, "Value", double.NaN);
            if (!double.IsFinite(offset) || !double.IsFinite(value)) continue;
            note.BendPoints.Add(new BendPointModel { Offset = offset, Value = value });
        }
    }

    private static void AddEnumTechnique(object source, HashSet<string> target, string property, string label, string matchFragment, string matchedLabel)
    {
        var value = Get(source, property);
        if (!IsSet(value)) return;
        var text = value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        target.Add(text.Contains(matchFragment, StringComparison.OrdinalIgnoreCase) ? matchedLabel : label);
    }

    // ---------- helpers ----------

    /// <summary>alphaTab finger (Thumb, IndexFinger, MiddleFinger, AnnularFinger, LittleFinger) as 0..4; null when unknown or none.</summary>
    internal static int? FingerOf(object? finger) => finger?.ToString() switch
    {
        "Thumb" => 0,
        "IndexFinger" => 1,
        "MiddleFinger" => 2,
        "AnnularFinger" => 3,
        "LittleFinger" => 4,
        _ => null
    };

    /// <summary>
    /// Sounding pitch of a harmonic: natural = the string's harmonic at that fret; artificial and pinch =
    /// the fretted note plus the written harmonic interval (a third above the octave is 16, not 12); tapped = the tapped
    /// fret plus an octave; semi = the harmonic on top of the fundamental (playback adds the fundamental separately).
    /// </summary>
    internal static int HarmonicMidi(string harmonicType, int openPitch, int fret, double node)
    {
        // Kept from before: the fifth (7/19) and double octave (5/24) nodes are frets, not semitone intervals.
        var interval = Math.Round(node, 1) is 5 or 7 or 19 or 24 ? ArtificialInterval(node)
            : node >= 1 && node <= 48 ? (int)Math.Round(node) : 12;
        var midi = harmonicType switch
        {
            "Natural" => openPitch + HarmonicInterval(fret),
            "Tap" => openPitch + interval + 12,
            "Artificial" or "Pinch" => openPitch + fret + interval,
            _ => openPitch + fret + ArtificialInterval(node)
        };
        return Math.Clamp(midi, 0, 127);
    }

    /// <summary>Artificial/pinch/tapped harmonics sound an octave above the fretted note unless the node is an explicit fifth (7) or double octave (5).</summary>
    public static int ArtificialInterval(double node) => Math.Round(node, 1) switch
    {
        7 or 19 => 19,
        5 or 24 => 24,
        _ => 12,
    };

    /// <summary>Semitones above the open string sounded by a harmonic touched at the given fret (the standard table).</summary>
    public static int HarmonicInterval(double fret) => Math.Round(fret, 1) switch
    {
        12 or 24 => 12,
        7 or 19 => 19,
        5 => 24,
        4 or 9 or 16 => 28,
        3.2 or 3 => 31,
        2.7 => 34,
        2.4 => 36,
        >= 11.9 and <= 12.1 => 12,
        _ => fret >= 12 ? 12 : fret >= 7 ? 19 : fret >= 5 ? 24 : 28,
    };

    /// <summary>standard drum TAB lines: cymbals top, then hi-hat, high toms, snare, low toms, kick.</summary>
    public static int DrumLine(int midi) => midi switch
    {
        49 or 51 or 52 or 53 or 55 or 57 or 59 => 0, // crash / ride / china / splash
        42 or 44 or 46 => 1,                         // hi-hat
        48 or 50 => 2,                               // high toms
        37 or 38 or 39 or 40 => 3,                   // snare / rim / clap
        41 or 43 or 45 or 47 => 4,                   // floor / low / mid toms
        35 or 36 => 5,                               // kick
        _ => 2,
    };

    private static List<int> ReadTuning(List<object> staves)
    {
        foreach (var staff in staves)
        {
            var tuning = ReadStaffTuning(staff);
            if (tuning.Count >= 4) return tuning;
        }
        return new List<int>();
    }

    private static List<int> ReadStaffTuning(object staff)
    {
        // alphaTab: Staff.StringTuning is a Tuning object whose Tunings list holds the values; Staff.Tuning
        // is the same list. (Reading StringTuning itself yielded nothing, so every file came in as standard.)
        var raw = Get(Get(staff, "StringTuning"), "Tunings") ?? Get(staff, "Tuning") ?? Get(staff, "TuningValues");
        var values = AsObjects(raw).Take(InputLimits.MaxStringsPerTrack + 1)
            .Select(v => ConvertToInt(v, -1)).Where(v => v is >= 12 and <= 108).ToList();
        if (values.Count > InputLimits.MaxStringsPerTrack)
            throw new InvalidDataException("A Guitar Pro track contains too many strings.");
        if (values.Count is >= 4 and <= InputLimits.MaxStringsPerTrack)
        {
            // alphaTab already lists strings high -> low, the same order TabForge uses.
            return values;
        }
        return new List<int>();
    }

    private static string ReadColor(object track)
    {
        var color = Get(track, "Color");
        if (color is AlphaTab.Model.Color c)
        {
            static int Channel(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);
            return $"#{Channel(c.R):X2}{Channel(c.G):X2}{Channel(c.B):X2}";
        }
        var hex = color?.ToString();
        return string.IsNullOrWhiteSpace(hex) ? "#F61A16" : hex!;
    }

    private static int ReadTempo(object score)
    {
        var direct = GetInt(score, "Tempo", 0);
        if (direct is >= 20 and <= 400) return direct;

        foreach (var mb in AsObjects(Get(score, "MasterBars")))
        {
            var value = GetInt(Get(mb, "TempoAutomation"), "Value", 0);
            if (value is >= 20 and <= 400) return value;
        }
        return 120;
    }

    private static void ReadTimeSignature(object score, SongProject project)
    {
        var first = AsObjects(Get(score, "MasterBars")).FirstOrDefault();
        if (first is null) return;
        project.TimeSignatureNumerator = Math.Clamp(GetInt(first, "TimeSignatureNumerator", 4), 1, 32);
        var denominator = GetInt(first, "TimeSignatureDenominator", 4);
        project.TimeSignatureDenominator = InputLimits.IsValidTimeSignatureDenominator(denominator) ? denominator : 4;
    }

    /// <summary>
    /// "Strings" for instruments without a fretboard (piano, whistle, synth): one per octave, so a note
    /// is shown as its semitone within the octave instead of every note collapsing onto fret 0.
    /// </summary>
    private static readonly int[] PitchOnlyStrings = { 96, 84, 72, 60, 48, 36, 24 };

    /// <summary>Highest octave "string" at or below the pitch; fret = semitones above it.</summary>
    private static (int StringIndex, int Fret) PlacePitch(IReadOnlyList<int> strings, int midi)
    {
        for (var s = 0; s < strings.Count; s++)
            if (midi >= strings[s]) return (s, midi - strings[s]);
        return (strings.Count - 1, 0);
    }

    private static List<int> DefaultTuning(TrackKind kind, int count)
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

    private static int NextMelodicChannel(int channel)
    {
        do channel = (channel + 1) % 16; while (channel == 9);
        return channel;
    }

    private static int DurationToDenominator(object? duration)
    {
        if (duration is null) return 16;
        var text = duration.ToString() ?? "";
        if (int.TryParse(text, out var n) && n > 0) return NormalizeDenominator(n);
        return text.ToLowerInvariant() switch
        {
            "whole" => 1,
            "half" => 2,
            "quarter" => 4,
            "eighth" => 8,
            "sixteenth" => 16,
            "thirtysecond" or "thirty-second" => 32,
            "sixtyfourth" or "sixty-fourth" => 64,
            "twohundredfiftysecond" => 64,
            _ => 16
        };
    }

    /// <summary>alphaTab's Duration enum may use tick counts; normalise to note denominators.</summary>
    private static int NormalizeDenominator(int value)
    {
        if (value is 1 or 2 or 4 or 8 or 16 or 32 or 64) return value;
        if (value >= 3840) return 1;
        if (value >= 1920) return 2;
        if (value >= 960) return 4;
        if (value >= 480) return 8;
        if (value >= 240) return 16;
        if (value >= 120) return 32;
        return 64;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> MissingNames = new();

    /// <summary>Diagnostics: "Type.Name" of every property the importer asked for that alphaTab does not have.</summary>
    internal static IReadOnlyCollection<string> MissingPropertyNames => MissingNames.Keys.ToList();

    private static object? Get(object? target, string property)
    {
        if (target is null) return null;
        // Reflection over alphaTab's model tolerates version differences: a missing property, or a
        // getter that throws on this particular object, reads as "absent".
        try
        {
            var info = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            // Record names that do not exist so a self-test can fail on a silently lost feature (Audit 3 M-02).
            if (info is null) MissingNames.TryAdd(target.GetType().Name + "." + property, 0);
            return info?.GetValue(target);
        }
        catch (AmbiguousMatchException) { return null; }
        catch (TargetInvocationException) { return null; }
    }

    private static IEnumerable<object> AsObjects(object? value)
    {
        if (value is null || value is string) yield break;
        if (value is IEnumerable enumerable)
            foreach (var item in enumerable)
                if (item is not null) yield return item;
    }

    private static string? GetString(object target, string property) => Get(target, property)?.ToString();

    private static int GetInt(object? target, string property, int fallback)
    {
        if (target is null) return fallback;
        return ConvertToInt(Get(target, property), fallback);
    }

    /// <summary>
    /// A per-note duration fraction from alphaTab as a percent, or null to keep 100%. Only GPX / .gp can carry one; the value
    /// must be finite and within 0.05..2.0 (the editor allows 1..200%), which rejects alphaTab's garbage for GP3-5 palm mutes.
    /// </summary>
    internal static int? SanePerNoteDurationPercent(double fraction, bool gp3To5)
    {
        if (gp3To5 || !double.IsFinite(fraction) || fraction < 0.05 || fraction > 2.0 || Math.Abs(fraction - 1.0) <= 0.005) return null;
        return Math.Clamp((int)Math.Round(fraction * 100), 5, 200);
    }

    private static double GetDouble(object? target, string property, double fallback)
    {
        var value = Get(target, property);
        if (value is null) return fallback;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; }
    }

    private static int ConvertToInt(object? value, int fallback)
    {
        if (value is null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; }
    }

    private static bool GetBool(object? target, string property, bool fallback)
    {
        var value = Get(target, property);
        if (value is null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; }
    }

    private static bool IsConversionFailure(Exception ex) =>
        ex is FormatException or InvalidCastException or OverflowException;
}

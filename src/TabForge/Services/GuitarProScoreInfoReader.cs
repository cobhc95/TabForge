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
using static TabForge.Services.GuitarProReflection;

namespace TabForge.Services;

// Owns: reading the master-bar information of an imported file: section colours, double bars, tempo ramps, repeat endings.
// Does not own: track and beat conversion.
// Tests: TestGuitarProFiles, TestMixFadeAndAudiblePlayhead.
/// <summary>Reads the score-level facts of a Guitar Pro file: tempo, time signature and the master bars (repeats, endings, sections, tempo map, key signatures).</summary>
internal static class GuitarProScoreInfoReader
{
    internal static readonly string[] ImportedSectionColors =
    {
        "#2E74B5", "#3FB950", "#D8A032", "#8B5CF6", "#00A6A6", "#E06C75", "#64748B", "#C45A9A"
    };


    /// <summary>
    /// True when the last &lt;MasterBar&gt; of a Guitar Pro 7/8 file's score.gpif carries a &lt;DoubleBar/&gt;. The zip was already checked by
    /// <see cref="GuitarProPreParse"/> (entry count and unpacked size); the entry is read once, bounded by the same per-entry cap, and only
    /// searched as bytes. Anything unreadable or not a zip (Guitar Pro 3-6) is simply false.
    /// </summary>
    internal static bool LastMasterBarHasDoubleBar(byte[] data)
    {
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(data, writable: false), System.IO.Compression.ZipArchiveMode.Read);
            var entry = zip.GetEntry("Content/score.gpif");
            if (entry is null || entry.Length > GuitarProPreParse.MaxZipEntryBytes) return false;
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > GuitarProPreParse.MaxZipEntryBytes) return false;
                buffer.Write(chunk, 0, read);
            }
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            var start = bytes.LastIndexOf("<MasterBar>"u8);
            if (start < 0) return false;
            var tail = bytes[start..];
            var end = tail.IndexOf("</MasterBar>"u8);
            return tail[..(end < 0 ? tail.Length : end)].IndexOf("<DoubleBar"u8) >= 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) { return false; }
    }

    /// <summary>The value of the tempo point a linear (progressive) point at automation index <paramref name="index"/> glides to, and the slots in between; null when there is none.</summary>
    internal static (int Value, double Slots)? LinearRampTarget(List<object> masterBars, int bar, List<(double ratio, int value, bool linear)> automations, int index)
    {
        static double SlotsOf(object? mb) => Math.Max(1, 16.0 * GetInt(mb, "TimeSignatureNumerator", 4) / Math.Max(1, GetInt(mb, "TimeSignatureDenominator", 4)));
        var thisSlots = SlotsOf(masterBars[bar]);
        if (index + 1 < automations.Count) return (automations[index + 1].value, (automations[index + 1].ratio - automations[index].ratio) * thisSlots);
        var slots = (1 - automations[index].ratio) * thisSlots;
        for (var j = bar + 1; j < masterBars.Count && j <= bar + 64; j++)
        {
            var next = AsObjects(Get(masterBars[j], "TempoAutomations"))
                .Select(a => (ratio: GetDouble(a, "RatioPosition", 0), value: GetInt(a, "Value", 0)))
                .Where(a => a.value is >= 20 and <= 400).OrderBy(a => a.ratio).Select(a => ((double ratio, int value)?)a).FirstOrDefault();
            if (next is { } found) return (found.value, slots + found.ratio * SlotsOf(masterBars[j]));
            slots += SlotsOf(masterBars[j]);
        }
        return null;
    }

    internal static void ReadMasterBarInfo(List<object> masterBars, SongProject project, bool gp3To5, GuitarProMixTableScanner.TempoRampFinder? rampFinder = null)
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
                    throw new InvalidDataException("The score file contains too many markers or sections.");
                if (sectionText.Length > InputLimits.MaxTitleLength)
                    throw new InvalidDataException("The score file contains an overlong section title.");
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
            var rawAutomations = AsObjects(Get(mb, "TempoAutomations"))
                // IsLinear only from a Guitar Pro 6-8 file: alphaTab's Guitar Pro 3-5 reader marks EVERY mix-table tempo linear (its
                // transitions come from the raw mix tables via rampFinder instead).
                .Select(a => (ratio: GetDouble(a, "RatioPosition", 0), value: GetInt(a, "Value", 0), linear: !gp3To5 && GetBool(a, "IsLinear", false)))
                .Where(a => a.value is >= 20 and <= 400).Take(64)
                // Several automations at the same position: the last one written is the one Guitar Pro plays.
                .GroupBy(a => Math.Round(a.ratio, 4)).Select(g => g.Last()).OrderBy(a => a.ratio).ToList();
            var automations = rawAutomations.Select(a => (a.ratio, a.value)).ToList();
            // Transition lengths (beats) of these tempo changes, from the raw mix tables (file order).
            var ramps = automations.Select(a => (double)(rampFinder?.Next(a.value) ?? 0)).ToList();
            var rampPart = automations.Select(_ => false).ToList();
            // Guitar Pro 7 "progressive" (linear) tempo point: the tempo glides from this point's value to the NEXT tempo point, which may lie in a later bar.
            // In the model that is a step to this value (when it differs from the running tempo) plus a ramp to the next point's value over the distance.
            if (rawAutomations.Any(a => a.linear))
            {
                var expanded = new List<(double ratio, int value)>(); var expandedRamps = new List<double>(); var expandedPart = new List<bool>();
                for (var ai = 0; ai < rawAutomations.Count; ai++)
                {
                    expanded.Add(automations[ai]); expandedRamps.Add(ramps[ai]); expandedPart.Add(false);
                    if (!rawAutomations[ai].linear || LinearRampTarget(masterBars, i, rawAutomations, ai) is not { } target) continue;
                    expanded.Add((automations[ai].ratio, target.Value)); expandedRamps.Add(Math.Max(1.0, target.Slots) / MusicTime.SlotsPerQuarter); expandedPart.Add(true);
                }
                automations = expanded; ramps = expandedRamps; rampPart = expandedPart;
            }
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
                if (ratio < 0.001 && !rampPart[ai] || ratio >= 1) continue;
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
                        throw new InvalidDataException("The score file contains overlong navigation text.");
                    measure.Directions = directionText;
                }
                else if (navigation is IEnumerable nav)
                {
                    var directionTextBuilder = new StringBuilder();
                    var directionCount = 0;
                    foreach (var direction in nav.Cast<object>().Take(InputLimits.MaxNavigationEntriesPerMeasure + 1))
                    {
                        if (++directionCount > InputLimits.MaxNavigationEntriesPerMeasure)
                            throw new InvalidDataException("A score measure contains too many navigation directions.");
                        var text = direction?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var separatorLength = directionTextBuilder.Length == 0 ? 0 : 1;
                        if (directionTextBuilder.Length + separatorLength + text.Length > InputLimits.MaxUserTextLength)
                            throw new InvalidDataException("The score file contains overlong navigation text.");
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

    internal static int FirstEndingNumber(int flags)
    {
        for (var i = 0; i < 16; i++)
            if ((flags & (1 << i)) != 0) return i + 1;
        return 0;
    }

    internal static bool KeyModeIsMinor(object keyMode)
        => keyMode is bool minor ? minor : keyMode.ToString()?.Contains("Minor", StringComparison.OrdinalIgnoreCase) == true;

    internal static int KeySignatureValue(object key)
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

    internal static int ReadTempo(object score)
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

    internal static void ReadTimeSignature(object score, SongProject project)
    {
        var first = AsObjects(Get(score, "MasterBars")).FirstOrDefault();
        if (first is null) return;
        project.TimeSignatureNumerator = Math.Clamp(GetInt(first, "TimeSignatureNumerator", 4), 1, 32);
        var denominator = GetInt(first, "TimeSignatureDenominator", 4);
        project.TimeSignatureDenominator = InputLimits.IsValidTimeSignatureDenominator(denominator) ? denominator : 4;
    }

}

using System.IO;
using AlphaTab.Model;
using TabForge.Models;

namespace TabForge.Services;

// Owns: saving a song as a Guitar Pro 7/8 (.gp) file.
// Does not own: reading files (GuitarProImporter) and the export questions (GpExportPreflight).
// Tests: TestGuitarProFiles, TestGpFidelity.
/// <summary>
/// Saves a song as a Guitar Pro 7/8 file (.gp) via alphaTab's GP7 writer: the reverse of
/// <see cref="GuitarProImporter"/>. Tracks, tunings, capo, MIDI program/channel/volume/pan, metre,
/// tempo changes, repeats, alternate endings, sections, durations, tuplets, rests, ties, dead/ghost
/// notes, palm mute, let ring, vibrato, hammer-ons, slides, harmonics, bends, accents and staccato are
/// written. By default the whole TabForge project (drum presets, score fonts, mix-table ramps, plug-in states...)
/// is also embedded inside the .gp, so reopening it in TabForge restores everything; with embedProject = false
/// the file is clean and those TabForge-only settings are not stored in it.
/// </summary>
public static class GuitarProExporter
{
    private static DynamicValue ToDynamicValue(int index) => index switch
    {
        0 => DynamicValue.PPP, 1 => DynamicValue.PP, 2 => DynamicValue.P, 3 => DynamicValue.MP,
        4 => DynamicValue.MF, 5 => DynamicValue.F, 6 => DynamicValue.FF, _ => DynamicValue.FFF
    };

    /// <param name="embedProject">false: a clean Guitar Pro file with nothing TabForge-specific inside.</param>
    public static void Save(SongProject project, string path, bool embedProject = true)
    {
        var bytes = ToBytes(project, embedProject);
        // Staged beside the destination and swapped in only when complete: a failed write leaves the previous song intact.
        FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes));
    }

    /// <summary>The complete .gp file bytes (built in memory; nothing is written).</summary>
    public static byte[] ToBytes(SongProject project, bool embedProject = true)
    {
        // A6-02: the embedded project is built (and size-checked) first, so an over-limit song fails before anything is exported or written.
        AudioTrackExport.RequireNotation(project, "a score file");
        var embeddedBytes = embedProject ? EmbeddedProjectBytes(project) : null;
        var settings = new AlphaTab.Settings();
        var score = Build(project);
        score.Finish(settings);
        var exporter = new AlphaTab.Exporter.Gp7Exporter();
        var data = exporter.Export(score, settings);
        var bytes = new byte[(int)data.Length];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)data[i];
        // A .gp file is a zip. alphaTab 1.8.4's writer stores bogus CRC-32 values in it (and 0x7fffffff for the
        // directory entry), so the whole archive is rewritten with correct CRCs. TabForge also adds its complete
        // project (all TabForge-only settings) as one extra entry; Guitar Pro ignores it, TabForge reads it back
        // for a lossless round trip.
        List<ZipPart> parts;
        try { parts = ReadZip(bytes); }
        catch (InvalidDataException) { parts = new(); }
        if (parts.Count == 0) return bytes; // not a zip we understand: keep alphaTab's output untouched
        for (var i = 0; i < parts.Count; i++)
            if (parts[i].Name.Equals(ScoreEntry, StringComparison.OrdinalIgnoreCase) && parts[i].Data is { Length: > 0 })
            {
                var patched = GuitarProBendCurve.Patch(parts[i].Data, score);
                if (!ReferenceEquals(patched, parts[i].Data)) parts[i] = parts[i] with { Data = patched };
            }
        using var zipStream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var part in parts)
            {
                var level = part.Name == "VERSION" ? System.IO.Compression.CompressionLevel.NoCompression : System.IO.Compression.CompressionLevel.Optimal;
                var entry = zip.CreateEntry(part.Name, level);
                if (part.Name.EndsWith('/')) continue; // directory entry: no data
                using var entryStream = entry.Open();
                entryStream.Write(part.Data);
            }
            if (embeddedBytes is not null)
            {
                var entry = zip.CreateEntry(EmbeddedProjectEntry, System.IO.Compression.CompressionLevel.Optimal);
                using (var entryStream = entry.Open()) entryStream.Write(embeddedBytes);
                // The integrity record: which score this project was saved with. Another program that edits the score but keeps unknown zip entries
                // would otherwise leave a stale project that wins on open (see EmbeddedBinding).
                var scorePart = parts.FirstOrDefault(p => p.Name.Equals(ScoreEntry, StringComparison.OrdinalIgnoreCase));
                if (scorePart.Data is not null)
                {
                    var bindingEntry = zip.CreateEntry(EmbeddedBindingEntry, System.IO.Compression.CompressionLevel.Optimal);
                    using var bindingStream = bindingEntry.Open();
                    bindingStream.Write(EmbeddedBinding.Serialize(scorePart.Data, embeddedBytes));
                }
            }
        }
        return zipStream.ToArray();
    }

    /// <summary>
    /// The gzip project bytes for the embedded entry, held to the same limit the reader applies (<see cref="InputLimits.MaxTforgeFileBytes"/>).
    /// Over it: an <see cref="InvalidDataException"/> naming the largest plug-in states, in the style of the .tfaudio size message (A5-03).
    /// </summary>
    internal static byte[] EmbeddedProjectBytes(SongProject project)
    {
        try { return ProjectService.PersistBytes(project); }
        catch (InvalidDataException ex) when (ex.Message == ProjectService.SizeLimitMessage)
        {
            var largest = AudioDataFile.LargestPluginStates(project);
            var beatCells = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Count + m.Voice2Cells.Count));
            var notes = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Concat(m.Voice2Cells).Sum(c => c.Notes.Count)));
            throw new InvalidDataException(
                $"The TabForge project saved inside this .gp file would be over its {AudioDataFile.Mb(InputLimits.MaxTforgeFileBytes)} limit, so nothing was saved. "
                + $"The song has {project.Tracks.Count} tracks, {beatCells:N0} beat cells and {notes:N0} notes"
                + (largest.Count > 0 ? $"; the largest plug-in states are {string.Join(", ", largest)}. Remove or reset those plug-ins (or unload large sample sets), then save again." : ". Save it as a clean .gp file instead, or split the song."));
        }
    }

    internal readonly record struct ZipPart(string Name, uint StoredCrc, byte[] Data);

    /// <summary>
    /// Reads a zip's entries straight from its central directory and local headers, inflating each one itself,
    /// without the CRC validation a stock zip reader may apply (alphaTab's writer stores wrong CRCs).
    /// Entries come back in archive order, directory entries with empty data.
    /// </summary>
    internal static List<ZipPart> ReadZip(byte[] zip)
    {
        static int U16(byte[] b, int at) => b[at] | b[at + 1] << 8;
        static int U32(byte[] b, int at) => b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;
        var eocd = -1;
        for (var i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
            if (U32(zip, i) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("No zip end-of-directory record.");
        var count = U16(zip, eocd + 10);
        var pos = U32(zip, eocd + 16);
        var parts = new List<ZipPart>();
        for (var n = 0; n < count; n++)
        {
            if (pos < 0 || pos + 46 > zip.Length || U32(zip, pos) != 0x02014b50) throw new InvalidDataException("Bad zip central directory.");
            var method = U16(zip, pos + 10);
            var storedCrc = (uint)U32(zip, pos + 16);
            var compressedSize = U32(zip, pos + 20);
            var nameLength = U16(zip, pos + 28);
            var extraLength = U16(zip, pos + 30);
            var commentLength = U16(zip, pos + 32);
            var local = U32(zip, pos + 42);
            var name = System.Text.Encoding.UTF8.GetString(zip, pos + 46, nameLength);
            pos += 46 + nameLength + extraLength + commentLength;
            if (local < 0 || local + 30 > zip.Length || U32(zip, local) != 0x04034b50) throw new InvalidDataException("Bad zip local header.");
            var dataStart = local + 30 + U16(zip, local + 26) + U16(zip, local + 28);
            if (compressedSize < 0 || dataStart + (long)compressedSize > zip.Length) throw new InvalidDataException("Zip entry runs past the end of the file.");
            byte[] data;
            if (name.EndsWith('/')) data = Array.Empty<byte>();
            else if (method == 0) data = zip.AsSpan(dataStart, compressedSize).ToArray();
            else if (method == 8)
            {
                using var inflate = new System.IO.Compression.DeflateStream(new MemoryStream(zip, dataStart, compressedSize, writable: false), System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflate.CopyTo(output);
                data = output.ToArray();
            }
            else throw new InvalidDataException($"Unsupported zip compression method {method}.");
            parts.Add(new ZipPart(name, storedCrc, data));
        }
        return parts;
    }

    public const string EmbeddedProjectEntry = "TabForge/project.tforge.gz";
    /// <summary>The integrity record beside the embedded project (<see cref="EmbeddedBinding"/>). Absent in files written before it existed.</summary>
    public const string EmbeddedBindingEntry = "TabForge/binding.json";
    /// <summary>The Guitar Pro score part the binding hashes.</summary>
    public const string ScoreEntry = "Content/score.gpif";

    /// <summary>True when the .gp carries an embedded TabForge project entry (used or not); only the zip directory is read.</summary>
    internal static bool HasEmbeddedEntry(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > InputLimits.MaxGuitarProFileBytes) return false;
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            return zip.GetEntry(EmbeddedProjectEntry) is not null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The TabForge project embedded in a .gp written by TabForge, or null for other .gp files.</summary>
    public static SongProject? TryReadEmbedded(string path)
    {
        // Untrusted input: the same bounded read as any Guitar Pro file, and the zip entry is inflated
        // with a hard cap (its declared size can lie), so a small crafted .gp cannot expand to gigabytes.
        byte[] file;
        try { file = InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "score file"); }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return TryReadEmbedded(file);
    }

    /// <summary>The embedded TabForge project of a .gp already read into memory (bounded), or null.</summary>
    internal static SongProject? TryReadEmbedded(byte[] file) => ReadEmbedded(file, out _);

    /// <summary>
    /// A6-02: the embedded project of a .gp in memory. Null with <paramref name="rejected"/> null: there is none (not a TabForge-written .gp;
    /// the normal Guitar Pro import applies, silently). Null with a reason in <paramref name="rejected"/>: one is present but cannot be used
    /// (over the size limit, damaged, unsupported version); the caller opens the file as plain Guitar Pro and tells the user why.
    /// </summary>
    internal static SongProject? ReadEmbedded(byte[] file, out string? rejected)
    {
        rejected = null;
        System.IO.Compression.ZipArchive zip;
        try { zip = new System.IO.Compression.ZipArchive(new MemoryStream(file, writable: false), System.IO.Compression.ZipArchiveMode.Read); }
        catch (InvalidDataException) { return null; }   // not a zip at all (Guitar Pro 3-5 ...): no embedded project
        using (zip)
        {
            System.IO.Compression.ZipArchiveEntry? entry;
            try { entry = zip.GetEntry(EmbeddedProjectEntry); }
            catch (InvalidDataException) { return null; }   // directory unreadable: the plain import reports the damage
            if (entry is null) return null;
            try
            {
                if (entry.Length > InputLimits.MaxTforgeFileBytes) { rejected = TooBigReason; return null; }
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > InputLimits.MaxTforgeFileBytes) { rejected = TooBigReason; return null; }
                    buffer.Write(chunk, 0, read);
                }
                var projectBytes = buffer.ToArray();
                var stale = EmbeddedBinding.Check(zip, projectBytes);
                if (stale is not null) { rejected = stale; return null; }
                var restored = ProjectService.RestorePersistedBytes(projectBytes);
                if (EmbeddedBinding.LegacyBarCountMismatch(zip, restored)) { rejected = EmbeddedBinding.StaleReason; return null; }
                return restored;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                rejected = ex.Message.Contains("size limit", StringComparison.Ordinal) ? TooBigReason
                    : ex.Message.Contains("unsupported format version", StringComparison.Ordinal) ? "it was saved by a newer or unknown version of TabForge"
                    : "the data is damaged or invalid";
                return null;
            }
        }
    }

    private static readonly string TooBigReason = $"it is over the {AudioDataFile.Mb(InputLimits.MaxTforgeFileBytes)} limit";

    /// <summary>The open notice for an embedded project that was present but rejected (<see cref="ReadEmbedded"/>).</summary>
    internal static string RejectedNotice(string reason) =>
        $"This file's TabForge project data could not be read ({reason}); it was opened as a plain score file"
        + (ReferenceEquals(reason, EmbeddedBinding.StaleReason) ? ", so the score is shown exactly as the file holds it and the older TabForge-only settings were not applied" : "");

    /// <summary>A bar's navigation marks (TabForge's names or Guitar Pro's own) as alphaTab directions, so D.C. / D.S. / Coda / Fine survive a clean .gp.</summary>
    internal static IEnumerable<Direction> GpDirections(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = token switch
            {
                "Segno" => "TargetSegno", "SegnoSegno" => "TargetSegnoSegno", "Coda" => "TargetCoda", "DoubleCoda" => "TargetDoubleCoda",
                "Fine" => "TargetFine", "ToCoda" => "JumpDaCoda", "ToDoubleCoda" => "JumpDaDoubleCoda",
                "DaCapo" or "DaCapoAlCoda" or "DalSegno" or "DalSegnoAlCoda" or "DalSegnoSegno" or "DalSegnoSegnoAlCoda" => "Jump" + token,
                _ => token,
            };
            if (Enum.TryParse<Direction>(name, out var direction)) yield return direction;
        }
    }

    /// <summary>A tempo automation at a position of a bar; a second one at the same position updates the first (a linear flag sticks).</summary>
    private static void AddTempoAutomation(MasterBar bar, double ratio, int bpm, bool linear)
    {
        foreach (var existing in bar.TempoAutomations)
            if (Math.Abs(existing.RatioPosition - ratio) < 1e-6) { existing.Value = bpm; existing.IsLinear |= linear; return; }
        bar.TempoAutomations.Add(new Automation { Type = AutomationType.Tempo, Value = bpm, RatioPosition = ratio, Text = "", IsLinear = linear });
    }

    private static Score Build(SongProject project)
    {
        var score = new Score
        {
            Title = project.Title ?? "", SubTitle = project.Subtitle ?? "", Artist = project.Artist ?? "", Album = project.Album ?? "",
            Music = project.MusicAuthor ?? "", Words = project.LyricsAuthor ?? "", Copyright = project.Copyright ?? "",
            Tab = project.TabAuthor ?? "", Instructions = project.Instructions ?? "", Notices = project.Notice ?? "",
        };
        var barCount = Math.Max(1, project.NotationTracks.Select(t => t.Measures.Count).DefaultIfEmpty(1).Max());
        var markers = project.Markers.ToDictionary(m => m.MeasureIndex, m => m.Title);

        var keys = new List<(int Key, bool Minor)>();
        var pendingTempo = new Dictionary<int, int>();   // a tempo ramp that ends on a bar line: its target, written at the start of the next bar
        var lastKey = project.KeySignature; var lastMinor = project.KeySignatureMinor;
        for (var b = 0; b < barCount; b++)
        {
            var model = project.NotationTracks.FirstOrDefault(t => b < t.Measures.Count)?.Measures[b];
            var mb = new MasterBar
            {
                TimeSignatureNumerator = model?.TimeSigNum ?? project.TimeSignatureNumerator,
                TimeSignatureDenominator = model?.TimeSigDenom ?? project.TimeSignatureDenominator,
                IsRepeatStart = model?.RepeatStart ?? false,
                IsAnacrusis = model?.Anacrusis ?? false,
                RepeatCount = model is { RepeatEnd: true } ? Math.Max(2, model.RepeatCount) : 0,
#pragma warning disable CS0618 // alphaTab 1.8 marks this obsolete, but its GP7 writer still emits the master-bar double bar from it.
                IsDoubleBar = model?.IsDoubleBar ?? false,
#pragma warning restore CS0618
            };
            // A bar without a key of its own is in the song's key, as the score shows it (BarSignatures.KeyAt; MusicXML does the same).
            // Carrying the previous bar's key forward made older songs, whose key change was set on one bar only, export differently.
            lastKey = model?.KeySignature ?? project.KeySignature;
            lastMinor = model?.KeySignatureMinor ?? project.KeySignatureMinor;
            if (model is not null && model.EndingPasses != 0) mb.AlternateEndings = model.EndingPasses;
            var directions = GpDirections(model?.Directions).ToList();
            if (directions.Count > 0)
            {
                var set = new AlphaTab.Core.EcmaScript.Set<Direction>();
                foreach (var direction in directions) set.Add(direction);
                mb.Directions = set;
            }
            if (markers.TryGetValue(b, out var title)) mb.Section = new Section { Text = title, Marker = "" };
            mb.TripletFeel = model?.TripletFeelKind switch
            {
                TripletFeels.Eighth => TripletFeel.Triplet8th, TripletFeels.Sixteenth => TripletFeel.Triplet16th, _ => TripletFeel.NoTripletFeel,
            };
            var tempo = b == 0 ? model?.TempoChange ?? project.Tempo : model?.TempoChange;
            if (tempo is int bpm) AddTempoAutomation(mb, 0, bpm, false);
            if (pendingTempo.TryGetValue(b, out var rampTarget) && model?.TempoChange is null) AddTempoAutomation(mb, 0, rampTarget, false);
            if (model?.MidBarTempos is { Count: > 0 } points)
            {
                var barSlots = Math.Max(1, MusicTime.BarSlots(project, b));
                var startTempo = MusicTime.TempoAt(project, b);
                for (var pi = 0; pi < points.Count; pi++)
                {
                    var point = points[pi];
                    var ratio = Math.Clamp(point.Slot / barSlots, 0, 0.999);
                    if (point.RampSlots <= 0) { AddTempoAutomation(mb, ratio, point.Tempo, false); continue; }
                    // A tempo ramp is Guitar Pro's linear tempo automation: a linear point at the ramp's start (holding the tempo
                    // running there) glides to the NEXT automation, which is written where the ramp ends. The model plays a ramp only until
                    // the next point or the bar's end (MusicTime.OffsetMs / TempoAfter), so a ramp cut short that way glides only part of
                    // the way (to the tempo it has reached) and the next point or bar then starts at the target: written the same way,
                    // the reopened song plays the same tempo curve.
                    var from = MusicTime.TempoAtSlot(model, point.Slot - 1e-6, startTempo);
                    AddTempoAutomation(mb, ratio, from, true);
                    var nextSlot = pi + 1 < points.Count ? points[pi + 1].Slot : double.MaxValue;
                    var endSlot = Math.Min(Math.Min(point.Slot + point.RampSlots, nextSlot), barSlots);
                    var reached = (int)Math.Round(Math.Clamp(from + (point.Tempo - from) * (endSlot - point.Slot) / point.RampSlots, 20, 400));
                    var complete = Math.Abs(reached - point.Tempo) < 1;
                    if (endSlot < barSlots - 1e-6)
                    {
                        // a following point at the very slot the glide ends would overwrite its end value: end the glide a hair earlier
                        var endRatio = (nextSlot <= endSlot + 1e-6 && !complete ? endSlot - 0.02 : endSlot) / barSlots;
                        AddTempoAutomation(mb, endRatio, complete ? point.Tempo : reached, false);
                        continue;
                    }
                    var nextModel = project.NotationTracks.FirstOrDefault(t => b + 1 < t.Measures.Count)?.Measures[b + 1];
                    if (!complete) AddTempoAutomation(mb, 0.999, reached, false);   // the glide ends at the bar line holding what it reached; the next bar starts at the target
                    else if (nextModel is null || nextModel.TempoChange is { } other && other != point.Tempo) { AddTempoAutomation(mb, 0.999, point.Tempo, false); continue; }
                    if (nextModel?.TempoChange is null) pendingTempo[b + 1] = point.Tempo;
                }
            }
            score.AddMasterBar(mb);
            keys.Add((lastKey, lastMinor));
        }

        foreach (var source in project.NotationTracks)
        {
            var drums = source.Kind == TrackKind.Drums || source.MidiChannel == 9;
            // Guitar Pro keeps pitch in tuning + fret and has no playback transposition: the gpif Track <Transpose> is a display-only
            // offset (alphaTab reads it into Staff.DisplayTranspositionPitch, "applies only to rendering"). So a track's transpose goes
            // into the tuning of a guitar or bass (every fret stays as written), or into string + fret when the shifted tuning would leave
            // a sane range or the track is not fretted. The in-memory song is never changed.
            var transpose = drums ? 0 : MixerGroups.Transpose(project, source);
            var tuningShift = TuningCarriesTranspose(source, transpose);
            var bake = tuningShift ? 0 : transpose;
            var track = new Track
            {
                Name = source.Name ?? "",
                ShortName = source.Name is { Length: > 3 } n ? n[..3] : source.Name ?? "",
                PlaybackInfo = new PlaybackInformation
                {
                    Program = Math.Clamp(source.MidiProgram, 0, 127),
                    PrimaryChannel = source.MidiChannel, SecondaryChannel = source.MidiChannel,
                    // Mixer groups baked in. The 0..16 steps are alphaTab's own model; the exact fractions (patch 0002 of TabForge.AlphaTab) are what the gpif stores.
                    Volume = Math.Clamp((int)Math.Round(MixerGroups.Volume(project, source) / 8.0), 0, 16),
                    Balance = Math.Clamp((int)Math.Round(MixerGroups.Pan(project, source) / 8.0), 0, 16),
                    VolumeFraction = GpMixerScale.ToFraction(MixerGroups.Volume(project, source)),
                    BalanceFraction = GpMixerScale.ToFraction(MixerGroups.Pan(project, source)),
                    IsMute = source.Mute, IsSolo = source.Solo,
                },
            };
            if (ColourChooserParse(source.ColorHex) is { } c) track.Color = new AlphaTab.Model.Color((byte)c.R, (byte)c.G, (byte)c.B, 255);
            var staff = new Staff { IsPercussion = drums, Capo = source.Capo, ShowTablature = !drums, ShowStandardNotation = true };
            if (!drums && source.StringTunings.Count > 0)
                staff.StringTuning = new Tuning("", source.StringTunings.Select(v => (double)(v + (tuningShift ? transpose : 0))).ToList(), false);
            track.AddStaff(staff);

            // Drums: one GP articulation per GM sound used (GP7 writes drum notes as articulations).
            var articulations = new Dictionary<int, int>();
            var chordIds = new Dictionary<string, string>();
            // alphaTab chains a voice's first beat to the previous bar's SAME voice: a second voice in only some bars made its
            // Finish throw (ArgumentOutOfRange), so a track with a second voice anywhere carries one in every bar (empty = a rest).
            var hasVoice2 = source.Measures.Any(m => m.Voice2Cells.Any(c => c.Notes.Count > 0 || c.IsRest));

            for (var b = 0; b < barCount; b++)
            {
                var bar = new Bar();
                staff.AddBar(bar);
                if (drums) bar.Clef = Clef.Neutral;
                else if (b < source.Measures.Count) (bar.Clef, bar.ClefOttava) = GpClef(source.Measures[b].Clef, source.Kind == TrackKind.Bass);
                if (b >= source.Measures.Count)
                {
                    for (var v = 0; v < (hasVoice2 ? 2 : 1); v++) { var empty = new Voice(); bar.AddVoice(empty); empty.AddBeat(RestBeat(4)); }
                    continue;
                }
                var measure = source.Measures[b];
                var slots = MusicTime.BarSlots(project, b);
                // An imported song's short bar plays only as long as its longest part (the same rule as the playback compiler), so an
                // empty part of it (another track, or the second voice) is written that long: a whole-bar rest would lengthen the bar.
                var played = project.ImportedFrom is null ? 0 : TabForge.Playback.ScoreToMidiCompiler.ContentSlots(project, b);
                var emptyLength = played > 0.25 && played < slots - 0.01 ? played : slots;
                // Simile marks live on the track's bar in GP7 (a two-bar simile is written as its first and second bar).
                if (measure.SimileOneBar) bar.SimileMark = SimileMark.Simple;
                else if (measure.SimileTwoBar)
                    bar.SimileMark = b > 0 && b - 1 < source.Measures.Count && source.Measures[b - 1].SimileTwoBar && staff.Bars[b - 1].SimileMark == SimileMark.FirstOfDouble
                        ? SimileMark.SecondOfDouble : SimileMark.FirstOfDouble;
                // Voice 1 always; voice 2 when the bar has one.
                foreach (var cells in hasVoice2 ? new[] { measure.Cells, measure.Voice2Cells } : new[] { measure.Cells })
                {
                    var voice = new Voice();
                    bar.AddVoice(voice);
                    var cursor = 0.0;
                    var any = false;
                    for (var i = 0; i < cells.Count; i++)
                    {
                        var cell = cells[i];
                        if (cell.Notes.Count == 0 && !cell.IsRest) continue;
                        var start = cell.RhythmicPosition ?? Math.Max(i, cursor);
                        if (start > cursor + 0.01) foreach (var fill in FillRests(start - cursor)) voice.AddBeat(fill);
                        foreach (var written in BeatsFor(cell, source, drums, track, articulations, staff, chordIds, (transpose, bake))) voice.AddBeat(written);
                        cursor = start + MusicTime.CellSlots(cell);
                        any = true;
                    }
                    // An empty bar is one whole-bar rest (a quarter rest in a 3/4 bar would reopen as a one-beat bar).
                    if (!any) { if (emptyLength == 16) voice.AddBeat(RestBeat(1)); else foreach (var fill in FillRests(emptyLength)) voice.AddBeat(fill); continue; }
                    // A bar that ends early stays short (a pickup, a short last bar): the playback compiler plays an imported song's
                    // incomplete bar only as long as its content (ScoreToMidiCompiler.ContentSlots), so padding it with rests would
                    // lengthen the bar and push every later bar late. A TabForge-written song keeps its empty cells as silence, so it is padded.
                    if (cursor < slots - 0.01 && project.ImportedFrom is null) foreach (var fill in FillRests(slots - cursor)) voice.AddBeat(fill);
                }
            }
            score.AddTrack(track);
        }
        // alphaTab's key setters walk the bars, so keys go on once every track has its bars.
        for (var i = 0; i < keys.Count; i++)
        {
            score.MasterBars[i].KeySignature = (KeySignature)Math.Clamp(keys[i].Key, -7, 7);
            score.MasterBars[i].KeySignatureType = keys[i].Minor ? KeySignatureType.Minor : KeySignatureType.Major;
        }
        return score;
    }

    /// <summary>
    /// A cell that holds grace notes together with its principal notes (how the importer stores an ornamented
    /// beat) becomes two beats: a grace beat, then the principal beat with its own duration and dots. Writing
    /// the whole cell as one grace beat used to drop the principal duration and shift the rest of the bar.
    /// </summary>
    private static IEnumerable<Beat> BeatsFor(TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, Staff staff, Dictionary<string, string> chordIds, (int Sounding, int Bake) shift)
    {
        var graces = cell.Notes.Where(n => n.IsGraceNote).ToList();
        if (graces.Count == 0 || graces.Count == cell.Notes.Count)
        {
            yield return BeatFor(cell, source, drums, track, articulations, staff, chordIds, shift);
            yield break;
        }
        var lead = graces[0];
        var graceSlots = lead.GraceDurationSlots > 0 ? lead.GraceDurationSlots : 1;
        var grace = new TabCell
        {
            Notes = graces.Select(n => n.Clone()).ToList(),
            DurationDenominator = graceSlots >= 2 ? 8 : graceSlots >= 1 ? 16 : graceSlots >= 0.5 ? 32 : 64,
            IsGrace = true,
            GraceBeforeBeat = lead.GraceBeforeBeat,
        };
        yield return BeatFor(grace, source, drums, track, articulations, staff, chordIds, shift);
        var principal = cell.Clone();
        principal.Notes = principal.Notes.Where(n => !n.IsGraceNote).ToList();
        principal.IsGrace = false;
        yield return BeatFor(principal, source, drums, track, articulations, staff, chordIds, shift);
    }

    private static Beat BeatFor(TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, Staff staff, Dictionary<string, string> chordIds, (int Sounding, int Bake) shift)
    {
        var beat = new Beat
        {
            Duration = DurationOf(cell.DurationDenominator),
            Dots = cell.Dots,
            IsEmpty = false,
            Text = cell.Text ?? "",
        };
        if (cell.Tuplet is var (num, den) && num > 0) { beat.TupletNumerator = num; beat.TupletDenominator = den; }
        if (!string.IsNullOrWhiteSpace(cell.Lyrics)) beat.Lyrics = cell.Lyrics.Split('\n').ToList();
        if (!string.IsNullOrWhiteSpace(cell.ChordName))
        {
            if (!chordIds.TryGetValue(cell.ChordName!, out var chordId))
            {
                chordId = "c" + chordIds.Count;
                chordIds[cell.ChordName!] = chordId;
                staff.AddChord(chordId, new Chord { Name = cell.ChordName!, ShowName = true, ShowDiagram = false, ShowFingering = false });
            }
            beat.ChordId = chordId;
        }
        if (cell.Fermata || cell.Notes.Any(n => n.Techniques.Contains("Fermata"))) beat.Fermata = new Fermata { Type = FermataType.Medium, Length = 1 };
        // Tenuto is AccentuationType.Tenuto on the notes (see below).
        // GP7 knows only before-beat and on-beat graces: alphaTab's GP7 writer drops a BendGrace entirely (the grace became
        // a normal 32nd that pushed the principal note late), so a bend grace is written as a before-beat grace; its bend stays on the note.
        var graceTech = cell.Notes.SelectMany(n => n.Techniques).ToList();
        if (cell.IsGrace || graceTech.Contains("GraceBefore"))
            beat.GraceType = cell.IsGrace && !cell.GraceBeforeBeat && !graceTech.Contains("GraceBend") ? GraceType.OnBeat : GraceType.BeforeBeat;
        else if (graceTech.Contains("GraceOnBeat")) beat.GraceType = GraceType.OnBeat;
        else if (graceTech.Contains("GraceBend") && cell.Notes.All(n => n.IsGraceNote)) beat.GraceType = GraceType.BeforeBeat;
        var whammyName = cell.Notes.SelectMany(n => n.Techniques).FirstOrDefault(x => x.StartsWith("TremBar", StringComparison.Ordinal) && x.Length > 7);
        var hasTremBar = cell.Notes.Any(n => n.Techniques.Contains("TremBar"));
        if (cell.WhammyPoints.Count > 0 || hasTremBar || whammyName is not null)
        {
            beat.WhammyBarType = whammyName is not null && Enum.TryParse<WhammyType>(whammyName[7..], out var wt) ? wt : WhammyType.Custom;
            var pts = cell.WhammyPoints.Count > 0 ? cell.WhammyPoints.Select(p => (p.Offset, p.Value)).ToList() : new List<(double, double)> { (0, 0), (30, -4), (60, 0) };
            foreach (var (o, v) in SimplifyWhammy(pts)) beat.AddWhammyBarPoint(new BendPoint(o, v));
        }
        var all = cell.Notes.SelectMany(n => n.Techniques).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Tremolo picking as slashes (1 = 1/8, 2 = 1/16, 3 = 1/32, GP7's range): the TremoloSpeed setter did not round-trip
        // (a 1/16 came back as 1/4 or 1/8 depending on the beat); 1/64 has no GP7 spelling and is written as 1/32.
        if (cell.TremoloPickDenominator > 0 || all.Contains("TremoloPick"))
        {
            var speed = cell.TremoloPickDenominator > 0 ? cell.TremoloPickDenominator : 8;
            beat.TremoloPicking = new TremoloPickingEffect { Marks = speed >= 32 ? 3 : speed >= 16 ? 2 : 1 };
        }
        if (all.Contains("Tapping")) beat.Tap = true;
        if (all.Contains("DeadSlapped")) beat.DeadSlapped = true;
        if (all.Contains("Slap") && !all.Contains("DeadSlapped")) beat.Slap = true;
        if (all.Contains("Pop")) beat.Pop = true;
        if (all.Contains("FadeIn")) beat.Fade = FadeType.FadeIn; else if (all.Contains("FadeOut")) beat.Fade = FadeType.FadeOut;
        if (all.Contains("WahOpen")) beat.WahPedal = WahPedal.Open; else if (all.Contains("WahClose")) beat.WahPedal = WahPedal.Closed;
        // The importer tags an arpeggio stroke with BOTH its Arpeggio* name and a Brush* name, so the arpeggio must be tested first.
        if (all.Contains("ArpeggioDown")) beat.BrushType = BrushType.ArpeggioDown; else if (all.Contains("ArpeggioUp")) beat.BrushType = BrushType.ArpeggioUp;
        else if (all.Contains("BrushDown")) beat.BrushType = BrushType.BrushDown; else if (all.Contains("BrushUp")) beat.BrushType = BrushType.BrushUp;
        // The stroke's spread: the importer reads BrushDuration back as 3 string steps (ticks / 3 / 240 = BrushStepSlots).
        // No spread set: none is written, so the reopened stroke keeps the player's default spread (a fixed 60 ticks made it faster).
        if (beat.BrushType != BrushType.None && double.IsFinite(cell.BrushStepSlots) && cell.BrushStepSlots > 0)
            beat.BrushDuration = Math.Round(cell.BrushStepSlots * 3 * 240);
        if (all.Contains("PickDown")) beat.PickStroke = PickStroke.Down; else if (all.Contains("PickUp")) beat.PickStroke = PickStroke.Up;
        // Legato is a slur from this beat to the next (alphaTab derives the destination from the previous beat); a rasgueado keeps its pattern ("Rasgueado<pattern>"), ii_1 when it has none.
        if (all.Contains("Legato")) beat.IsLegatoOrigin = true;
        if (all.Contains("Rasgueado"))
        {
            var pattern = all.FirstOrDefault(x => x.StartsWith("Rasgueado", StringComparison.OrdinalIgnoreCase) && x.Length > 9);
            beat.Rasgueado = pattern is not null && Enum.TryParse<Rasgueado>(pattern[9..], true, out var parsed) && parsed != Rasgueado.None ? parsed : Rasgueado.Ii;
        }
        beat.Ottava = cell.OctaveShiftSemitones switch
        {
            >= 24 => Ottavia._15ma, >= 12 => Ottavia._8va, <= -24 => Ottavia._15mb, <= -12 => Ottavia._8vb, _ => Ottavia.Regular
        };
        // Mix-table changes (volume, pan, program on a beat) are not written: alphaTab's GP7 writer emits only tempo automations and the track's initial
        // sound (probed with Beat.Automations of type Volume/Balance/Instrument: nothing reaches the gpif). See docs/R5_CAPABILITY (A11).
        if (cell.IsRest || cell.Notes.Count == 0) return beat;
        foreach (var n in cell.Notes)
        {
            var t = n.Techniques;
            var note = new Note
            {
                IsDead = n.Dead, IsGhost = n.Ghost, IsTieDestination = n.Tied || cell.IsTied,
                IsPalmMute = TechniqueNames.HasPalmMute(t), IsLetRing = t.Contains("LetRing"),
                IsStaccato = cell.Staccato && !n.Ghost,   // staccato is an <Accent> bit too: Guitar Pro 8 drops a ghost mark when the note has any <Accent> (re-save probe), so it goes on the plain notes of the chord
                IsHammerPullOrigin = t.Contains("HOPOOrigin") || t.Contains("HOPO") && !t.Contains("HOPODestination"),
                Vibrato = t.Contains("WideVibrato") ? VibratoType.Wide : t.Contains("Vibrato") ? VibratoType.Slight : VibratoType.None,
                // GP keeps one mark of the three per note: heavy accent, accent, tenuto (alphaTab's AccentuationType.Tenuto; the importer reads it back onto the beat).
                // A ghost note carries none of them, nor staccato: Guitar Pro 8 keeps a ghost mark only on a note with no <Accent> element at all (re-saves of a note written
                // with the ghost mark plus each of staccato, accent, heavy accent, tenuto and two pairs kept the mark and dropped the ghost mark every time; the ghost mark
                // alone survived; no ghost note of 294 in real .gp files has an <Accent>), and the ghost mark is the per-note fact. The beat's marks stay on the other
                // notes of the chord; only a beat whose notes are all ghost loses them (GpExportPreflight reports that).
                Accentuated = n.Ghost ? AccentuationType.None : cell.Accent == 2 ? AccentuationType.Heavy : cell.Accent == 1 ? AccentuationType.Normal : cell.Tenuto || t.Contains("Tenuto") ? AccentuationType.Tenuto : AccentuationType.None,
                LeftHandFinger = FingerFor(n.LeftHandFinger), RightHandFinger = FingerFor(n.RightHandFinger),
            };
            if (drums)
            {
                var midi = n.MidiValue > 0 ? n.MidiValue : n.Fret;
                if (!articulations.TryGetValue(midi, out var index))
                {
                    index = track.PercussionArticulations.Count;
                    // the format's own definition for this GM sound (name, staff line, notehead), so GP7/8 draws
                    // and names the drum like its own files; a generic entry only for sounds it does not know.
                    track.PercussionArticulations.Add(GpArticulations.Get(midi) is { } known && (int)known.OutputMidiNumber == midi
                        ? known
                        : new InstrumentArticulation { Id = midi, OutputMidiNumber = midi, ElementType = "Drum", StaffLine = 3 });
                    articulations[midi] = index;
                }
                note.PercussionArticulation = index;
            }
            else
            {
                var (stringIndex, fret) = (n.StringIndex, n.Fret);
                // A Guitar Pro file holds pitch only as string tuning + capo + fret. A note whose sounding pitch is not that (a track whose
                // tuning the source did not give, so the pitch came from the file's own note value) is written where it sounds right: it
                // used to reopen 16 semitones low. Tie destinations and harmonics carry their origin's or the harmonic's pitch, so they stay.
                if (n.MidiValue > 0 && !n.Dead && !n.Tied && !cell.IsTied && n.HarmonicFret is null && !t.Any(x => x.Contains("Harmonic", StringComparison.Ordinal))
                    && source.StringTunings.Count > 0 && source.PitchOf(stringIndex, fret) is var written && written != n.MidiValue)
                    (stringIndex, fret) = TransposedPosition(source, stringIndex, fret, n.MidiValue - written);
                if (shift.Bake != 0) (stringIndex, fret) = TransposedPosition(source, stringIndex, fret, shift.Bake);
                note.String = Math.Max(1, source.StringTunings.Count - stringIndex);
                note.Fret = fret;
            }
            if (t.Contains("ShiftSlide")) note.SlideOutType = SlideOutType.Shift;
            else if (t.Contains("LegatoSlide") || t.Contains("Slide")) note.SlideOutType = SlideOutType.Legato;
            else if (t.Contains("SlideOutDown")) note.SlideOutType = SlideOutType.OutDown;
            else if (t.Contains("SlideOutUp")) note.SlideOutType = SlideOutType.OutUp;
            else if (t.Contains("PickSlideDown")) note.SlideOutType = SlideOutType.PickSlideDown;
            else if (t.Contains("PickSlideUp")) note.SlideOutType = SlideOutType.PickSlideUp;
            if (t.Contains("SlideInBelow")) note.SlideInType = SlideInType.IntoFromBelow;
            else if (t.Contains("SlideInAbove")) note.SlideInType = SlideInType.IntoFromAbove;
            // Loudness through the shared dynamics table (ppp..fff); a beat carries the first note's dynamic.
            var dynamic = ToDynamicValue(TabForge.Models.Dynamics.NearestIndex(n.Velocity));
            note.Dynamics = dynamic;
            if (ReferenceEquals(n, cell.Notes[0])) beat.Dynamics = dynamic;
            if (t.Contains("PinchHarmonic")) { note.HarmonicType = HarmonicType.Pinch; note.HarmonicValue = n.HarmonicFret ?? 12; }
            else if (t.Contains("ArtificialHarmonic")) { note.HarmonicType = HarmonicType.Artificial; note.HarmonicValue = n.HarmonicFret ?? 12; }
            else if (t.Contains("TapHarmonic")) { note.HarmonicType = HarmonicType.Tap; note.HarmonicValue = n.HarmonicFret ?? 12; }
            else if (t.Contains("SemiHarmonic")) { note.HarmonicType = HarmonicType.Semi; note.HarmonicValue = n.HarmonicFret ?? 12; }
            else if (t.Contains("FeedbackHarmonic")) { note.HarmonicType = HarmonicType.Feedback; note.HarmonicValue = n.HarmonicFret ?? 12; }
            else if (t.Contains("Harmonic")) { note.HarmonicType = HarmonicType.Natural; note.HarmonicValue = n.HarmonicFret ?? n.Fret; }
            if (t.Contains("LeftTap")) note.IsLeftHandTapped = true;
            if (t.Contains("Trill"))
            {
                note.TrillValue = Math.Clamp((n.TrillTargetMidi > 0 ? n.TrillTargetMidi : n.MidiValue + 2) + shift.Sounding, 0, 127);   // an absolute pitch: follows the track transpose
                note.TrillSpeed = DurationOf(n.TrillDurationDenominator > 0 ? n.TrillDurationDenominator : 16);
            }
            foreach (var (offset, value) in SimplifyBend(n.BendPoints.Select(p => (p.Offset <= 1.0 ? p.Offset * 60 : p.Offset, p.Value)).ToList()))
                note.AddBendPoint(new BendPoint(offset, value));
            // alphaTab writes only a midpoint or peak middle stretch; the curve's own fit is written into the file afterwards (GuitarProBendCurve.Patch).
            if (!drums && GuitarProBendCurve.Fit(GuitarProBendCurve.Normalise(n.BendPoints.Select(p => (p.Offset, p.Value)))) is { } bendFit) GuitarProBendCurve.Plan(note, bendFit);
            beat.AddNote(note);
        }
        if (cell.Notes.Any(n => TechniqueNames.HasPalmMute(n.Techniques))) beat.IsPalmMute = true;
        return beat;
    }

    /// <summary>
    /// The Guitar Pro clef for a TabForge bar clef (a bar's clef can change anywhere in a track): F4 for bass, C3 for alto, C4 for tenor
    /// and G2 for everything else (the guitar clef is G2 with an 8vb mark). The importer reads the clef and its octave mark back
    /// (G2 + 8vb is the guitar clef, plain G2 the treble clef), so a clef is stable over any number of round trips.
    /// </summary>
    internal static (Clef Clef, Ottavia Ottava) GpClef(string? clef, bool bassTrack)
    {
        var value = (clef ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (value.Length == 0 || value == "g8" || value.StartsWith("guitar", StringComparison.Ordinal)) return (Clef.G2, Ottavia._8vb);   // TabForge's guitar clef: G2 written an octave down
        if (value.Contains("bass", StringComparison.Ordinal) || value[0] == 'f') return (Clef.F4, bassTrack && value == "f4" ? Ottavia._8vb : Ottavia.Regular);
        if (value.Contains("tenor", StringComparison.Ordinal) || value == "c4") return (Clef.C4, Ottavia.Regular);
        if (value.Contains("alto", StringComparison.Ordinal) || value[0] == 'c') return (Clef.C3, Ottavia.Regular);
        if (value == "neutral") return (Clef.Neutral, Ottavia.Regular);
        return (Clef.G2, Ottavia.Regular);
    }

    /// <summary>True when a guitar or bass track's transpose is written as a shifted tuning (every string, frets unchanged): the shifted
    /// strings must stay between C0 (12) and 127. Otherwise (keys, extreme shifts) the notes are moved on string + fret instead.</summary>
    internal static bool TuningCarriesTranspose(TrackModel source, int semitones) =>
        semitones != 0 && source.Kind is TrackKind.Guitar or TrackKind.Bass && source.MidiChannel != 9 && source.StringTunings.Count > 0
        && source.StringTunings.Min() + semitones >= 12 && source.StringTunings.Max() + semitones <= 127;

    /// <summary>The string and fret that sound <paramref name="semitones"/> above the given position: the same string when the fret stays
    /// at or above 0, otherwise the string that reaches the pitch with the lowest fret (a pitch below the lowest open string stays
    /// on it at fret 0).</summary>
    internal static (int StringIndex, int Fret) TransposedPosition(TrackModel source, int stringIndex, int fret, int semitones)
    {
        if (source.StringTunings.Count == 0) return (stringIndex, Math.Max(0, fret + semitones));
        stringIndex = Math.Clamp(stringIndex, 0, source.StringTunings.Count - 1);
        var target = source.PitchOf(stringIndex, fret) + semitones;
        if (source.FretOf(stringIndex, target) >= 0) return (stringIndex, source.FretOf(stringIndex, target));
        var best = -1;
        for (var s = 0; s < source.StringTunings.Count; s++)
            if (source.FretOf(s, target) >= 0 && (best < 0 || source.FretOf(s, target) < source.FretOf(best, target))) best = s;
        return best < 0 ? (stringIndex, 0) : (best, source.FretOf(best, target));
    }

    /// <summary>
    /// Guitar Pro 7 stores a bend as origin / middle / destination only. A multi-point (GP3-5 style) bend that
    /// alphaTab classifies as Custom is dropped by its writer, so reduce it to the nearest representable
    /// shape: prebend (release), or bend (release) with the highest point kept as the middle. Bends alphaTab
    /// already understands are written unchanged.
    /// </summary>
    internal static List<(double Offset, double Value)> SimplifyBend(List<(double Offset, double Value)> points)
    {
        if (points.Count < 2) return points;
        var probe = new Note();
        foreach (var (offset, value) in points) probe.AddBendPoint(new BendPoint(offset, value));
        if (probe.BendType != BendType.Custom) return points;
        var first = Math.Max(0, points[0].Value);
        var last = Math.Max(0, points[^1].Value);
        var peakPoint = points.MaxBy(p => p.Value);
        var peak = Math.Max(first, Math.Max(last, peakPoint.Value));
        if (first > 0)
            return last < first
                ? new() { (0, first), (60, last) }
                : new() { (0, first), (60, peak) };
        if (last < peak)
            return new() { (0, 0), (Math.Clamp(peakPoint.Offset, 10, 50), peak), (60, last) };
        return new() { (0, 0), (60, peak) };
    }

    /// <summary>
    /// alphaTab's Guitar Pro 7 writer keeps a whammy curve of up to four points (dive, dip, hold, predive...)
    /// and drops anything longer without a trace. A longer curve is reduced to its start, its deepest or
    /// highest interior point and its end, which every reader understands as a dip.
    /// </summary>
    internal static List<(double Offset, double Value)> SimplifyWhammy(List<(double Offset, double Value)> points)
    {
        if (points.Count <= 4) return points;
        // Keep the interior peak above both ends and the interior dip below both ends (in time order), so a curve that rises
        // and then dives keeps both extremes; a plain dip or rise keeps its single extreme (the deepest/highest point).
        var inner = points.Skip(1).Take(points.Count - 2).ToList();
        var high = inner.MaxBy(p => p.Value); var low = inner.MinBy(p => p.Value);
        var ends = new[] { points[0].Value, points[^1].Value };
        var extremes = new List<(double Offset, double Value)>();
        if (high.Value > ends.Max()) extremes.Add(high);
        if (low.Value < ends.Min()) extremes.Add(low);
        if (extremes.Count == 0) extremes.Add(inner.MaxBy(p => Math.Abs(p.Value)));
        var kept = extremes.OrderBy(p => p.Offset).Select(p => (Math.Clamp(p.Offset, 10, 50), p.Value)).ToList();
        if (kept.Count == 2 && kept[0].Item1 >= kept[1].Item1)
        {
            var first = Math.Min(kept[0].Item1, 49);   // both clamped to 50 would share an offset
            kept[0] = (first, kept[0].Value); kept[1] = (first + 1, kept[1].Value);
        }
        return new List<(double, double)> { points[0] }.Concat(kept).Append(points[^1]).ToList();
    }

    /// <summary>The model's finger number (0 thumb, 1 index, 2 middle, 3 ring, 4 little; null = not written) as alphaTab's Fingers value.</summary>
    internal static Fingers FingerFor(int? finger) => finger switch
    {
        0 => Fingers.Thumb, 1 => Fingers.IndexFinger, 2 => Fingers.MiddleFinger, 3 => Fingers.AnnularFinger, 4 => Fingers.LittleFinger, _ => Fingers.Unknown,
    };

    private static Duration DurationOf(int denominator) => denominator switch
    {
        1 => Duration.Whole, 2 => Duration.Half, 4 => Duration.Quarter, 8 => Duration.Eighth,
        16 => Duration.Sixteenth, 32 => Duration.ThirtySecond, _ => Duration.SixtyFourth,
    };

    private static Beat RestBeat(int denominator) => new() { Duration = DurationOf(denominator), IsEmpty = false };

    // Largest-first rests filling a gap given in sixteenth slots.
    private static IEnumerable<Beat> FillRests(double slots)
    {
        foreach (var (size, den) in new[] { (16.0, 1), (8.0, 2), (4.0, 4), (2.0, 8), (1.0, 16), (0.5, 32), (0.25, 64) })
            while (slots >= size - 0.001) { yield return RestBeat(den); slots -= size; }
    }

    private static TabForge.Models.Rgba? ColourChooserParse(string? hex) =>
        TabForge.Models.ColourHex.TryParseStrict(hex, out var c) ? c : null;
}

using System.IO;
using AlphaTab.Model;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// Saves a song as a Guitar Pro 7/8 file (.gp) via alphaTab's GP7 writer: the reverse of
/// <see cref="GuitarProImporter"/>. Tracks, tunings, capo, MIDI program/channel/volume/pan, metre,
/// tempo changes, repeats, alternate endings, sections, durations, tuplets, rests, ties, dead/ghost
/// notes, palm mute, let ring, vibrato, hammer-ons, slides, harmonics, bends, accents and staccato are
/// written. TabForge-only settings (drum presets, score fonts, mix-table ramps...) stay in .tforge.
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
                using var entryStream = entry.Open();
                entryStream.Write(embeddedBytes);
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
            throw new InvalidDataException(
                $"The TabForge project saved inside this Guitar Pro file would be over its {AudioDataFile.Mb(InputLimits.MaxTforgeFileBytes)} limit, so nothing was saved. "
                + (largest.Count > 0 ? $"The largest plug-in states: {string.Join(", ", largest)}. " : "")
                + "Remove or reset those plug-ins (or unload large sample sets), then save again.");
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

    /// <summary>The TabForge project embedded in a .gp written by TabForge, or null for other .gp files.</summary>
    public static SongProject? TryReadEmbedded(string path)
    {
        // Untrusted input: the same bounded read as any Guitar Pro file, and the zip entry is inflated
        // with a hard cap (its declared size can lie), so a small crafted .gp cannot expand to gigabytes.
        byte[] file;
        try { file = InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "Guitar Pro file"); }
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
                return ProjectService.RestorePersistedBytes(buffer.ToArray());
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
        $"This file's TabForge project data could not be read ({reason}); it was opened as a plain Guitar Pro file";

    /// <summary>A bar's navigation marks (TabForge's names or Guitar Pro's own) as alphaTab directions, so D.C. / D.S. / Coda / Fine survive a clean .gp.</summary>
    private static IEnumerable<Direction> GpDirections(string? text)
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

    private static Score Build(SongProject project)
    {
        var score = new Score
        {
            Title = project.Title ?? "", SubTitle = project.Subtitle ?? "", Artist = project.Artist ?? "", Album = project.Album ?? "",
            Music = project.MusicAuthor ?? "", Words = project.LyricsAuthor ?? "", Copyright = project.Copyright ?? "",
            Tab = project.TabAuthor ?? "", Instructions = project.Instructions ?? "", Notices = project.Notice ?? "",
        };
        var barCount = project.Tracks.Count == 0 ? 1 : project.Tracks.Max(t => t.Measures.Count);
        var markers = project.Markers.ToDictionary(m => m.MeasureIndex, m => m.Title);

        var keys = new List<(int Key, bool Minor)>();
        var lastKey = project.KeySignature; var lastMinor = project.KeySignatureMinor;
        for (var b = 0; b < barCount; b++)
        {
            var model = project.Tracks.FirstOrDefault(t => b < t.Measures.Count)?.Measures[b];
            var mb = new MasterBar
            {
                TimeSignatureNumerator = model?.TimeSigNum ?? project.TimeSignatureNumerator,
                TimeSignatureDenominator = model?.TimeSigDenom ?? project.TimeSignatureDenominator,
                IsRepeatStart = model?.RepeatStart ?? false,
                RepeatCount = model is { RepeatEnd: true } ? Math.Max(2, model.RepeatCount) : 0,
#pragma warning disable CS0618 // alphaTab 1.8 marks this obsolete, but its GP7 writer still emits the master-bar double bar from it.
                IsDoubleBar = model?.IsDoubleBar ?? false,
#pragma warning restore CS0618
            };
            if (model?.KeySignature is int ks) lastKey = ks;
            if (model?.KeySignatureMinor is bool km) lastMinor = km;
            if (model is not null && model.EndingPasses != 0) mb.AlternateEndings = model.EndingPasses;
            var directions = GpDirections(model?.Directions).ToList();
            if (directions.Count > 0)
            {
                var set = new AlphaTab.Core.EcmaScript.Set<Direction>();
                foreach (var direction in directions) set.Add(direction);
                mb.Directions = set;
            }
            if (markers.TryGetValue(b, out var title)) mb.Section = new Section { Text = title, Marker = "" };
            var tempo = b == 0 ? model?.TempoChange ?? project.Tempo : model?.TempoChange;
            if (tempo is int bpm) mb.TempoAutomations.Add(new Automation { Type = AutomationType.Tempo, Value = bpm, RatioPosition = 0, Text = "" });
            if (model?.MidBarTempos is { Count: > 0 } points)
            {
                var barSlots = Math.Max(1, MusicTime.BarSlots(project, b));
                foreach (var point in points)
                    mb.TempoAutomations.Add(new Automation { Type = AutomationType.Tempo, Value = point.Tempo, RatioPosition = Math.Clamp(point.Slot / barSlots, 0, 0.999), Text = "" });
            }
            score.AddMasterBar(mb);
            keys.Add((lastKey, lastMinor));
        }

        foreach (var source in project.Tracks)
        {
            var drums = source.Kind == TrackKind.Drums || source.MidiChannel == 9;
            var track = new Track
            {
                Name = source.Name ?? "",
                ShortName = source.Name is { Length: > 3 } n ? n[..3] : source.Name ?? "",
                PlaybackInfo = new PlaybackInformation
                {
                    Program = Math.Clamp(source.MidiProgram, 0, 127),
                    PrimaryChannel = source.MidiChannel, SecondaryChannel = source.MidiChannel,
                    Volume = Math.Clamp((int)Math.Round(MixerGroups.Volume(project, source) / 8.0), 0, 16), // mixer groups baked in
                    Balance = Math.Clamp((int)Math.Round(MixerGroups.Pan(project, source) / 8.0), 0, 16),
                    IsMute = source.Mute, IsSolo = source.Solo,
                },
            };
            if (ColourChooserParse(source.ColorHex) is { } c) track.Color = new AlphaTab.Model.Color((byte)c.R, (byte)c.G, (byte)c.B, 255);
            var staff = new Staff { IsPercussion = drums, Capo = source.Capo, ShowTablature = !drums, ShowStandardNotation = true };
            if (!drums && source.StringTunings.Count > 0)
                staff.StringTuning = new Tuning("", source.StringTunings.Select(v => (double)v).ToList(), false);
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
                if (b >= source.Measures.Count)
                {
                    for (var v = 0; v < (hasVoice2 ? 2 : 1); v++) { var empty = new Voice(); bar.AddVoice(empty); empty.AddBeat(RestBeat(4)); }
                    continue;
                }
                var measure = source.Measures[b];
                var slots = MusicTime.BarSlots(project, b);
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
                        foreach (var written in BeatsFor(cell, source, drums, track, articulations, staff, chordIds)) voice.AddBeat(written);
                        cursor = start + MusicTime.CellSlots(cell);
                        any = true;
                    }
                    if (!any) { voice.AddBeat(RestBeat(slots >= 16 ? 1 : 4)); continue; }
                    if (cursor < slots - 0.01) foreach (var fill in FillRests(slots - cursor)) voice.AddBeat(fill);
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
    private static IEnumerable<Beat> BeatsFor(TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, Staff staff, Dictionary<string, string> chordIds)
    {
        var graces = cell.Notes.Where(n => n.IsGraceNote).ToList();
        if (graces.Count == 0 || graces.Count == cell.Notes.Count)
        {
            yield return BeatFor(cell, source, drums, track, articulations, staff, chordIds);
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
        yield return BeatFor(grace, source, drums, track, articulations, staff, chordIds);
        var principal = cell.Clone();
        principal.Notes = principal.Notes.Where(n => !n.IsGraceNote).ToList();
        principal.IsGrace = false;
        yield return BeatFor(principal, source, drums, track, articulations, staff, chordIds);
    }

    private static Beat BeatFor(TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, Staff staff, Dictionary<string, string> chordIds)
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
        // Tenuto: alphaTab's model has no tenuto flag (only Note.Accentuated / IsStaccato), so it cannot be written to .gp.
        var graceTech = cell.Notes.SelectMany(n => n.Techniques).ToList();
        if (cell.IsGrace || graceTech.Contains("GraceBefore"))
            beat.GraceType = graceTech.Contains("GraceBend") ? GraceType.BendGrace : cell.IsGrace && !cell.GraceBeforeBeat ? GraceType.OnBeat : GraceType.BeforeBeat;
        else if (graceTech.Contains("GraceOnBeat")) beat.GraceType = GraceType.OnBeat;
        else if (graceTech.Contains("GraceBend")) beat.GraceType = GraceType.BendGrace;
        var whammyName = cell.Notes.SelectMany(n => n.Techniques).FirstOrDefault(x => x.StartsWith("TremBar", StringComparison.Ordinal) && x.Length > 7);
        var hasTremBar = cell.Notes.Any(n => n.Techniques.Contains("TremBar"));
        if (cell.WhammyPoints.Count > 0 || hasTremBar || whammyName is not null)
        {
            beat.WhammyBarType = whammyName is not null && Enum.TryParse<WhammyType>(whammyName[7..], out var wt) ? wt : WhammyType.Custom;
            var pts = cell.WhammyPoints.Count > 0 ? cell.WhammyPoints.Select(p => (p.Offset, p.Value)).ToList() : new List<(double, double)> { (0, 0), (30, -4), (60, 0) };
            foreach (var (o, v) in SimplifyWhammy(pts)) beat.AddWhammyBarPoint(new BendPoint(o, v));
        }
        var all = cell.Notes.SelectMany(n => n.Techniques).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cell.TremoloPickDenominator > 0 || all.Contains("TremoloPick"))
            beat.TremoloSpeed = DurationOf(cell.TremoloPickDenominator > 0 ? cell.TremoloPickDenominator : 8);
        if (all.Contains("Tapping")) beat.Tap = true;
        if (all.Contains("Slap")) beat.Slap = true;
        if (all.Contains("Pop")) beat.Pop = true;
        if (all.Contains("FadeIn")) beat.Fade = FadeType.FadeIn; else if (all.Contains("FadeOut")) beat.Fade = FadeType.FadeOut;
        if (all.Contains("WahOpen")) beat.WahPedal = WahPedal.Open; else if (all.Contains("WahClose")) beat.WahPedal = WahPedal.Closed;
        if (all.Contains("BrushDown")) beat.BrushType = BrushType.BrushDown; else if (all.Contains("BrushUp")) beat.BrushType = BrushType.BrushUp;
        else if (all.Contains("ArpeggioDown")) beat.BrushType = BrushType.ArpeggioDown; else if (all.Contains("ArpeggioUp")) beat.BrushType = BrushType.ArpeggioUp;
        if (beat.BrushType != BrushType.None) beat.BrushDuration = 60;
        if (all.Contains("PickDown")) beat.PickStroke = PickStroke.Down; else if (all.Contains("PickUp")) beat.PickStroke = PickStroke.Up;
        beat.Ottava = cell.OctaveShiftSemitones switch
        {
            >= 24 => Ottavia._15ma, >= 12 => Ottavia._8va, <= -24 => Ottavia._15mb, <= -12 => Ottavia._8vb, _ => Ottavia.Regular
        };
        if (cell.IsRest || cell.Notes.Count == 0) return beat;
        foreach (var n in cell.Notes)
        {
            var t = n.Techniques;
            var note = new Note
            {
                IsDead = n.Dead, IsGhost = n.Ghost, IsTieDestination = n.Tied || cell.IsTied,
                IsPalmMute = TechniqueNames.HasPalmMute(t), IsLetRing = t.Contains("LetRing"),
                IsStaccato = cell.Staccato,
                IsHammerPullOrigin = t.Contains("HOPOOrigin") || t.Contains("HOPO") && !t.Contains("HOPODestination"),
                Vibrato = t.Contains("WideVibrato") ? VibratoType.Wide : t.Contains("Vibrato") ? VibratoType.Slight : VibratoType.None,
                Accentuated = cell.Accent == 2 ? AccentuationType.Heavy : cell.Accent == 1 ? AccentuationType.Normal : AccentuationType.None,
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
                note.String = Math.Max(1, source.StringTunings.Count - n.StringIndex);
                note.Fret = n.Fret;
            }
            if (t.Contains("ShiftSlide")) note.SlideOutType = SlideOutType.Shift;
            else if (t.Contains("LegatoSlide") || t.Contains("Slide")) note.SlideOutType = SlideOutType.Legato;
            else if (t.Contains("SlideOutDown")) note.SlideOutType = SlideOutType.OutDown;
            else if (t.Contains("SlideOutUp")) note.SlideOutType = SlideOutType.OutUp;
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
                note.TrillValue = n.TrillTargetMidi > 0 ? n.TrillTargetMidi : n.MidiValue + 2;
                note.TrillSpeed = DurationOf(n.TrillDurationDenominator > 0 ? n.TrillDurationDenominator : 16);
            }
            foreach (var (offset, value) in SimplifyBend(n.BendPoints.Select(p => (p.Offset <= 1.0 ? p.Offset * 60 : p.Offset, p.Value)).ToList()))
                note.AddBendPoint(new BendPoint(offset, value));
            beat.AddNote(note);
        }
        if (cell.Notes.Any(n => TechniqueNames.HasPalmMute(n.Techniques))) beat.IsPalmMute = true;
        return beat;
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
        var interior = points.Skip(1).Take(points.Count - 2).MaxBy(p => Math.Abs(p.Value));
        return new() { points[0], (Math.Clamp(interior.Offset, 10, 50), interior.Value), points[^1] };
    }

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

    private static System.Windows.Media.Color? ColourChooserParse(string? hex) =>
        TabForge.Views.ColourChooser.TryParse(hex, out var c) ? c : null;
}

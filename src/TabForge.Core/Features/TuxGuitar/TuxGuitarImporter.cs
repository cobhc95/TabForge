using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Plugins;

namespace TabForge.Features.TuxGuitar;

// Owns: reading a TuxGuitar file (.tg: a zip holding content.xml) into a song, with the fidelity rules for ticks, voices and tracks.
// Does not own: writing the file (TuxGuitarExporter) and the worker process (ImportWorker).
// Tests: TuxGuitarTests.
public static class TuxGuitarImporter
{
    public const string Extension = ".tg";
    private const string Invalid = "This score file is invalid, truncated, or unsupported.";
    private const long MaxXmlBytes = 64L * 1024 * 1024;
    private const int MaxZipEntries = 1024;
    private static readonly int[] DrumTunings = { 49, 42, 48, 38, 43, 36 };
    private static readonly int[] PitchLadder = { 96, 84, 72, 60, 48, 36, 24 };

    public static SongProject Import(string path, Services.ImportContext? context = null)
    {
        path = Services.FilePathPolicy.ExistingFile(path, "score file", Extension);
        context ??= new Services.ImportContext();
        context.Check();
        var raw = Services.InputLimits.ReadBoundedBytes(path, Services.InputLimits.MaxTforgeFileBytes, "score file");
        return ImportBytes(raw, path, context);
    }

    internal static SongProject ImportBytes(byte[] raw, string path, Services.ImportContext? context = null)
    {
        context ??= new Services.ImportContext();
        if (raw.LongLength > Services.InputLimits.MaxTforgeFileBytes)
            throw new InvalidDataException("The score file exceeds the supported size limit.");
        context.Check();
        var xmlBytes = Unpack(raw, context);
        context.Check();
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaxXmlBytes,
            };
            using var reader = XmlReader.Create(new MemoryStream(xmlBytes, writable: false), settings);
            doc = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or IOException)
        {
            throw new InvalidDataException(Invalid, ex);
        }
        context.Check();
        var project = Convert(doc, path, context);
        Services.ProjectValidator.Validate(project);
        project.IsDirty = false;
        return project;
    }

    private static byte[] Unpack(byte[] raw, Services.ImportContext context)
    {
        if (raw.Length < 4 || raw[0] != (byte)'P' || raw[1] != (byte)'K' || raw[2] != 3 || raw[3] != 4)
            throw new InvalidDataException(Invalid);
        try
        {
            var eocd = -1;
            for (var i = raw.Length - 22; i >= Math.Max(0, raw.Length - 22 - 65_535); i--)
            {
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i, 4)) == 0x06054b50) { eocd = i; break; }
            }
            if (eocd < 0) throw new InvalidDataException(Invalid);
            var entries = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(eocd + 10, 2));
            if (entries > MaxZipEntries)
                throw new InvalidDataException(Invalid);
            using var zip = new ZipArchive(new MemoryStream(raw, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries) throw new InvalidDataException(Invalid);
            ZipArchiveEntry? content = null;
            foreach (var entry in zip.Entries)
            {
                context.Check();
                if (!Services.FilePathPolicy.IsSafeArchiveEntryName(entry.FullName)) continue;
                if (entry.FullName.Equals("content.xml", StringComparison.OrdinalIgnoreCase)) content = entry;
            }
            if (content is null) throw new InvalidDataException(Invalid);
            if (content.Length > MaxXmlBytes) throw new InvalidDataException(Invalid);
            using var stream = content.Open();
            using var output = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > MaxXmlBytes) throw new InvalidDataException(Invalid);
                output.Write(chunk, 0, read);
            }
            return output.ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException(Invalid, ex);
        }
    }

    private sealed class ChannelInfo
    {
        public int Bank;
        public int Program;
        public int Volume = 100;
        public int Balance = 64;
        public int Chorus;
        public int Reverb;
        public int Phaser;
        public int Tremolo;
        public string Name = "";
        public int GmChannel = -1;
    }

    private sealed class HeaderInfo
    {
        public int Num = 4;
        public int Denom = 4;
        public int Tempo = 120;
        public bool RepeatOpen;
        public bool RepeatClose;
        public int RepeatCloseCount = 2;
        public int AltMask;
        public string Marker = "";
        public string MarkerColor = NamedColourSwatches.SectionDefaultColour;
        public bool HasMarker;
        public string TripletFeel = TripletFeels.None;
    }

    private static SongProject Convert(XDocument doc, string path, Services.ImportContext context)
    {
        var root = doc.Root ?? throw new InvalidDataException(Invalid);
        var song = root.Element("TGSong") ?? throw new InvalidDataException(Invalid);
        var channels = ReadChannels(song);
        var headers = ReadHeaders(song);
        if (headers.Count == 0) headers.Add(new HeaderInfo());
        if (headers.Count > Services.InputLimits.MaxMeasuresPerTrack)
            throw new InvalidDataException("The score file contains too many measures.");
        var trackElements = song.Elements("TGTrack").ToList();
        if (trackElements.Count > Services.InputLimits.MaxTracks)
            throw new InvalidDataException("The score file contains too many tracks.");
        if (trackElements.Count == 0)
            throw new InvalidDataException("The score file loaded, but contained no tracks.");

        var project = new SongProject
        {
            Title = CleanTitle(Text(song, "name")) ?? System.IO.Path.GetFileNameWithoutExtension(path),
            Artist = CleanText(Text(song, "artist")),
            Album = CleanText(Text(song, "album")),
            MusicAuthor = CleanText(Text(song, "author")),
            Copyright = CleanText(Text(song, "copyright")),
            TabAuthor = CleanText(Text(song, "writer")),
            LyricsAuthor = CleanText(Text(song, "transcriber")),
            Instructions = CleanText(Text(song, "comments")),
            Tempo = Math.Clamp(headers[0].Tempo, Services.InputLimits.MinTempo, Services.InputLimits.MaxTempo),
            TimeSignatureNumerator = Math.Clamp(headers[0].Num, 1, Services.InputLimits.MaxTimeSignatureNumerator),
            TimeSignatureDenominator = Math.Clamp(headers[0].Denom, 1, Services.InputLimits.MaxTimeSignatureDenominator),
            ImportedFrom = path,
        };
        if (project.TimeSignatureNumerator is < 1 or > 32) project.TimeSignatureNumerator = 4;
        if (project.TimeSignatureDenominator is < 1 or > 64) project.TimeSignatureDenominator = 4;

        var warnings = new List<string>();
        long totalNotes = 0;
        long totalMeasures = 0;
        var lastTempo = project.Tempo;

        for (var ti = 0; ti < trackElements.Count; ti++)
        {
            context.Check();
            var trackEl = trackElements[ti];
            var track = ConvertTrack(trackEl, ti, channels, headers, project, context, warnings, ref totalNotes);
            project.Tracks.Add(track);
            totalMeasures += track.Measures.Count;
            if (totalMeasures > Services.InputLimits.MaxTotalMeasures)
                throw new InvalidDataException("The score file contains too many measures.");
            if (totalNotes > Services.InputLimits.MaxTotalNotes)
                throw new InvalidDataException("The score file contains too many notes.");
        }

        foreach (var track in project.Tracks)
        {
            InferSlides(track);
            var highest = 0;
            foreach (var m in track.Measures)
                foreach (var c in m.Cells.Concat(m.Voice2Cells))
                    foreach (var n in c.Notes)
                        if (n.Fret > highest) highest = n.Fret;
            track.NumberOfFrets = Math.Clamp(Math.Max(24, highest), 12, 36);
        }

        foreach (var (header, idx) in headers.Select((h, i) => (h, i)))
        {
            if (header.HasMarker && !string.IsNullOrWhiteSpace(header.Marker))
            {
                if (project.Markers.Count >= Services.InputLimits.MaxMarkers) break;
                project.Markers.Add(new MarkerModel { MeasureIndex = idx, Title = CleanText(header.Marker), ColorHex = header.MarkerColor });
            }
        }

        if (warnings.Count > 0)
            context.TuxGuitarNotice = string.Join("; ", warnings.Distinct().Take(8));
        FixFirstTempo(project, headers);
        return project;
    }

    private static void FixFirstTempo(SongProject project, List<HeaderInfo> headers)
    {
        var last = project.Tempo;
        for (var i = 0; i < headers.Count && i < project.Tracks[0].Measures.Count; i++)
        {
            var tempo = Math.Clamp(headers[i].Tempo, Services.InputLimits.MinTempo, Services.InputLimits.MaxTempo);
            if (i == 0) { last = tempo; continue; }
            if (tempo != last)
            {
                foreach (var track in project.Tracks)
                    if (i < track.Measures.Count) track.Measures[i].TempoChange = tempo;
                last = tempo;
            }
        }
    }

    private static Dictionary<int, ChannelInfo> ReadChannels(XElement song)
    {
        var map = new Dictionary<int, ChannelInfo>();
        foreach (var c in song.Elements("TGChannel"))
        {
            var id = Int(c, "id", 0);
            if (id <= 0) continue;
            var info = new ChannelInfo
            {
                Bank = Int(c, "bank", 0),
                Program = Int(c, "program", 0),
                Volume = Int(c, "volume", 100),
                Balance = Int(c, "balance", 64),
                Chorus = Int(c, "chorus", 0),
                Reverb = Int(c, "reverb", 0),
                Phaser = Int(c, "phaser", 0),
                Tremolo = Int(c, "tremolo", 0),
                Name = CleanText((string?)c.Element("name")?.Value ?? ""),
            };
            foreach (var p in c.Elements("TGChannelParameter"))
            {
                var key = (string?)p.Attribute("key") ?? "";
                if (key == "gm-channel-1" && int.TryParse((string?)p.Attribute("value"), out var v)) info.GmChannel = v;
                if (key == "gm-channel-2" && int.TryParse((string?)p.Attribute("value"), out var v2) && info.GmChannel < 0) info.GmChannel = v2;
            }
            map[id] = info;
        }
        return map;
    }

    private static List<HeaderInfo> ReadHeaders(XElement song)
    {
        var list = new List<HeaderInfo>();
        foreach (var h in song.Elements("TGMeasureHeader"))
        {
            var info = new HeaderInfo();
            var ts = h.Element("timeSignature");
            if (ts is not null)
            {
                info.Num = IntAttr(ts, "numerator", 4);
                info.Denom = IntAttr(ts, "denominator", 4);
            }
            info.Tempo = Int(h, "tempo", 120);
            info.RepeatOpen = h.Element("repeatOpen") is not null;
            var close = h.Element("repeatClose");
            if (close is not null)
            {
                info.RepeatClose = true;
                info.RepeatCloseCount = Math.Clamp(Int(close, null, 2), 2, 8);
            }
            var alt = h.Element("repeatAlternative");
            if (alt is not null)
            {
                var bits = 0;
                foreach (var a in alt.Elements("alternative"))
                    if (int.TryParse(a.Value.Trim(), out var n) && n is >= 1 and <= 8) bits |= 1 << (n - 1);
                info.AltMask = bits;
            }
            var marker = h.Element("marker");
            if (marker is not null && !string.IsNullOrWhiteSpace(marker.Value))
            {
                info.HasMarker = true;
                info.Marker = marker.Value;
                var r = MarkerInt(marker, "R", 0);
                var g = MarkerInt(marker, "G", 0);
                var b = MarkerInt(marker, "B", 255);
                info.MarkerColor = ColourHex.Format((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
            }
            var feel = (string?)h.Element("tripletFeel")?.Value;
            if (feel is not null)
            {
                if (feel.Equals("eighth", StringComparison.OrdinalIgnoreCase)) info.TripletFeel = TripletFeels.Eighth;
                else if (feel.Equals("sixteenth", StringComparison.OrdinalIgnoreCase)) info.TripletFeel = TripletFeels.Sixteenth;
            }
            list.Add(info);
        }
        return list;
    }

    private static TrackModel ConvertTrack(XElement trackEl, int index, Dictionary<int, ChannelInfo> channels, List<HeaderInfo> headers, SongProject project, Services.ImportContext context, List<string> warnings, ref long totalNotes)
    {
        var name = TrackName((string?)trackEl.Element("name")?.Value);
        var channelId = Int(trackEl, "channelId", 0);
        channels.TryGetValue(channelId, out var channel);
        var offsetText = (string?)trackEl.Element("offset")?.Value;
        var offset = int.TryParse(offsetText?.Trim(), out var parsedOffset) ? parsedOffset : 0;
        var isDrums = channel is not null && (channel.Bank == 128 || channel.GmChannel == 9);
        var tunings = trackEl.Elements("TGString").Select(e => int.TryParse(e.Value.Trim(), out var v) ? v : -1).ToList();
        if (tunings.Count > Services.InputLimits.MaxStringsPerTrack)
            throw new InvalidDataException("A score track contains too many strings.");
        var program = channel is not null ? Math.Clamp(channel.Program, 0, MidiLimits.Max) : 25;
        var lower = name.ToLowerInvariant();
        var isBass = !isDrums && (lower.Contains("bass", StringComparison.Ordinal) || program is >= 32 and <= 39);
        List<int> finalTunings;
        TrackKind kind;
        if (isDrums)
        {
            kind = TrackKind.Drums;
            finalTunings = new List<int>(DrumTunings);
        }
        else
        {
            var usable = tunings.Where(v => v is >= 0 and <= 127).ToList();
            if (usable.Count == 0) usable = tunings.Count == 0 ? new() : tunings.Select(v => Math.Clamp(v, 0, 127)).ToList();
            finalTunings = usable.Count >= 1 ? usable : (isBass ? new() { 43, 38, 33, 28 } : new() { 64, 59, 55, 50, 45, 40 });
            var ladder = finalTunings.Count >= 4 && Enumerable.Range(1, finalTunings.Count - 1).All(i => finalTunings[i - 1] - finalTunings[i] == 12) && finalTunings.SequenceEqual(PitchLadder);
            var pitchOnly = ladder || finalTunings.Count >= 4 && Enumerable.Range(1, finalTunings.Count - 1).All(i => finalTunings[i - 1] - finalTunings[i] == 12);
            kind = isBass ? TrackKind.Bass : pitchOnly ? TrackKind.Keys : TrackKind.Guitar;
        }

        if (channel is not null && (channel.Phaser != 0 || channel.Tremolo != 0))
            warnings.Add("mixer phaser and tremolo values are not kept");

        var track = new TrackModel
        {
            Name = name,
            Kind = kind,
            MidiChannel = isDrums ? 9 : channel?.GmChannel is >= 0 and <= 15 ? channel.GmChannel : NextFreeChannel(project),
            MidiProgram = isDrums ? 0 : program,
            MidiBankMsb = channel is not null && !isDrums && channel.Bank is >= 0 and <= 127 ? channel.Bank : -1,
            InstrumentName = isDrums ? Services.GeneralMidi.DrumKit : (string.IsNullOrWhiteSpace(channel?.Name) ? Services.InstrumentNaming.ForStringCount(Services.GeneralMidi.NameOf(program), kind, finalTunings.Count) : channel!.Name),
            ColorHex = ReadColor(trackEl),
            Volume = Math.Clamp(channel?.Volume ?? 100, 0, 127),
            Pan = Math.Clamp(channel?.Balance ?? 64, 0, 127),
            Chorus = Math.Clamp(channel?.Chorus ?? 0, 0, 127),
            Reverb = Math.Clamp(channel?.Reverb ?? 0, 0, 127),
            StringTunings = finalTunings,
            Rig = new RigPreset { Name = isDrums ? "Imported Drum Kit" : isBass ? "Imported Bass" : "Imported Guitar", ArticulationMap = isDrums ? "GM Drums" : isBass ? "Generic Bass" : "Generic Guitar" },
        };
        if (offset > 0) track.Capo = Math.Clamp(offset, 0, 127);
        else if (offset < 0) track.Transpose = Math.Clamp(offset, -24, 24);
        var maxFretText = (string?)trackEl.Attribute("maxFret");
        if (int.TryParse(maxFretText, out var mf)) track.NumberOfFrets = Math.Clamp(mf, 12, 36);

        var measures = trackEl.Elements("TGMeasure").ToList();
        var barCount = Math.Max(measures.Count, headers.Count);
        if (barCount > Services.InputLimits.MaxMeasuresPerTrack)
            throw new InvalidDataException("The score file contains too many measures in a track.");
        var lastClef = Clefs.Guitar;
        int? lastKey = null;
        for (var i = 0; i < barCount; i++)
        {
            context.Check();
            var header = i < headers.Count ? headers[i] : null;
            var measure = new MeasureModel { Number = i + 1, Clef = lastClef };
            if (header is not null)
            {
                measure.TimeSigNum = header.Num;
                measure.TimeSigDenom = header.Denom;
                measure.RepeatStart = header.RepeatOpen;
                measure.RepeatEnd = header.RepeatClose;
                if (header.RepeatClose) measure.RepeatCount = header.RepeatCloseCount;
                if (header.AltMask != 0)
                {
                    measure.AlternateEndingMask = header.AltMask;
                    measure.AlternateEnding = FirstEnding(header.AltMask);
                }
                if (header.TripletFeel == TripletFeels.Eighth) { measure.TripletFeel = true; measure.TripletFeelKind = TripletFeels.Eighth; }
                else if (header.TripletFeel == TripletFeels.Sixteenth) { measure.TripletFeel = true; measure.TripletFeelKind = TripletFeels.Sixteenth; }
            }
            if (i < measures.Count)
            {
                var mEl = measures[i];
                var clef = (string?)mEl.Element("clef")?.Value;
                if (!string.IsNullOrWhiteSpace(clef)) { lastClef = MapClef(clef); measure.Clef = lastClef; }
                var key = mEl.Element("keySignature");
                if (key is not null && int.TryParse(key.Value.Trim(), out var kv)) { lastKey = Math.Clamp(kv, -7, 7); measure.KeySignature = lastKey; }
                else if (i == 0) measure.KeySignature = lastKey;
                ConvertBeats(mEl, measure, track, context, warnings, ref totalNotes);
            }
            track.Measures.Add(measure);
        }
        return track;
    }

    private static void ConvertBeats(XElement measureEl, MeasureModel measure, TrackModel track, Services.ImportContext context, List<string> warnings, ref long totalNotes)
    {
        var beats = measureEl.Elements("TGBeat").ToList();
        if (beats.Count > Services.InputLimits.MaxBeatsPerMeasure)
        {
            warnings.Add("a bar holds more beats than are kept");
            beats = beats.Take(Services.InputLimits.MaxBeatsPerMeasure).ToList();
        }
        double pos0 = 0, pos1 = 0;
        foreach (var beat in beats)
        {
            context.Check();
            var text = (string?)beat.Element("text")?.Value;
            var pick = (string?)beat.Element("pickStroke")?.Value;
            var strokeEl = beat.Element("stroke");
            var strokeDir = (string?)strokeEl?.Attribute("direction");
            var strokeValue = strokeEl is not null && int.TryParse((string?)strokeEl.Attribute("value"), out var sv) ? sv : 0;
            var voices = beat.Elements("voice").ToList();
            for (var vi = 0; vi < voices.Count && vi < 2; vi++)
            {
                var voiceEl = voices[vi];
                var durationEl = voiceEl.Element("duration");
                if (durationEl is null) continue;
                var (denom, dots, enters, times) = ReadDuration(durationEl);
                var slotDouble = vi == 0 ? pos0 : pos1;
                var cell = CellAt(measure, vi, slotDouble);
                if (cell is null) { warnings.Add("a bar holds more events than are kept"); continue; }
                cell.DurationDenominator = denom;
                cell.Dots = dots;
                cell.IsTriplet = enters == 3 && times == 2;
                cell.TupletNumerator = enters == 1 && times == 1 ? 0 : enters;
                cell.TupletDenominator = enters == 1 && times == 1 ? 0 : times;
                if (Math.Abs(slotDouble - Math.Round(slotDouble)) > 0.01) cell.RhythmicPosition = slotDouble;
                var notes = voiceEl.Elements("note").ToList();
                var hasNotes = notes.Count > 0;
                if (!string.IsNullOrWhiteSpace(text)) cell.Text = CleanText(text);
                var accent = 0;
                foreach (var noteEl in notes)
                {
                    var note = ConvertNote(noteEl, track, cell, warnings, ref accent, ref totalNotes);
                    if (note is not null) cell.Notes.Add(note);
                }
                if (accent > 0) cell.Accent = Math.Max(cell.Accent, accent);
                if (!string.IsNullOrWhiteSpace(pick))
                {
                    var pickTech = pick.Equals("up", StringComparison.OrdinalIgnoreCase) ? "PickUp" : "PickDown";
                    foreach (var n in cell.Notes.Where(n => !n.IsGraceNote)) n.Techniques.Add(pickTech);
                }
                if (strokeEl is not null && strokeDir is not null)
                {
                    var strokeTech = strokeDir.Equals("up", StringComparison.OrdinalIgnoreCase) ? "BrushUp" : "BrushDown";
                    foreach (var n in cell.Notes.Where(n => !n.IsGraceNote)) n.Techniques.Add(strokeTech);
                    cell.BrushStepSlots = strokeValue;
                }
                if (cell.Notes.Count == 0 && !cell.HasAnnotation)
                {
                    if (vi == 0) cell.IsRest = true;
                    else if (vi != 0 && voiceEl.Attribute("empty")?.Value != "true") cell.IsRest = true;
                }
                var advance = DurationSlots(denom, dots, enters, times);
                if (vi == 0) pos0 += advance; else pos1 += advance;
            }
        }
    }

    private static TabNote? ConvertNote(XElement noteEl, TrackModel track, TabCell cell, List<string> warnings, ref int accent, ref long totalNotes)
    {
        var stringNum = IntAttr(noteEl, "string", 0);
        var fret = IntAttr(noteEl, "value", 0);
        var velocity = IntAttr(noteEl, "velocity", 95);
        var tied = (string?)noteEl.Attribute("tiedNote") == "true";
        var stringIndex = stringNum - 1;
        if (stringIndex < 0 || (track.StringTunings.Count > 0 && stringIndex >= track.StringTunings.Count))
        {
            warnings.Add("a few notes outside the string range are skipped");
            return null;
        }
        if (totalNotes >= Services.InputLimits.MaxTotalNotes) throw new InvalidDataException("The score file contains too many notes.");
        totalNotes++;
        if (track.Kind == TrackKind.Drums)
        {
            stringIndex = Math.Clamp(stringIndex, 0, Math.Max(0, track.StringTunings.Count - 1));
        }
        var note = new TabNote { StringIndex = stringIndex, Fret = Math.Clamp(fret, 0, Services.InputLimits.MaxFrets), Velocity = Math.Clamp(velocity, 0, 127), Tied = tied };
        note.MidiValue = track.IsDrumTrack ? fret : track.PitchOf(stringIndex, note.Fret);
        foreach (var child in noteEl.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "accentuatedNote": accent = Math.Max(accent, 1); break;
                case "heavyAccentuatedNote": accent = Math.Max(accent, 2); break;
                case "deadNote": note.Dead = true; break;
                case "palmMute": note.Techniques.Add("PalmMute"); break;
                case "letRing": note.Techniques.Add("LetRing"); break;
                case "hammer": note.Techniques.Add("HOPO"); break;
                case "slide": note.Techniques.Add("Slide"); break;
                case "vibrato": note.Techniques.Add("Vibrato"); break;
                case "staccato": cell.Staccato = true; note.Techniques.Add("Staccato"); break;
                case "tapping": note.Techniques.Add("Tapping"); break;
                case "popping": note.Techniques.Add("Pop"); break;
                case "slapping": note.Techniques.Add("Slap"); break;
                case "fadeIn": note.Techniques.Add("FadeIn"); break;
                case "harmonic":
                    var hType = (string?)child.Attribute("type") ?? "";
                    var hData = IntAttr(child, "data", 0);
                    if (hType == "P.H") { note.Techniques.Add("PinchHarmonic"); if (hData > 0) note.HarmonicFret = hData; }
                    else if (hType == "A.H" || hType == "Artificial") { note.Techniques.Add("ArtificialHarmonic"); if (hData > 0) note.HarmonicFret = hData; }
                    else { note.Techniques.Add("Harmonic"); note.HarmonicFret = hData > 0 ? hData : 12; }
                    break;
                case "bend":
                    note.Techniques.Add("Bend");
                    foreach (var p in child.Elements("point"))
                    {
                        var pos = IntAttr(p, "position", 0);
                        var val = IntAttr(p, "value", 0);
                        note.BendPoints.Add(new BendPointModel { Offset = pos * 5, Value = val });
                    }
                    break;
                case "grace":
                    var graceFret = IntAttr(child, "fret", fret);
                    var onBeat = (string?)child.Attribute("onBeat") == "true";
                    var dead = (string?)child.Attribute("dead") == "true";
                    var grace = new TabNote { StringIndex = stringIndex, Fret = Math.Clamp(graceFret, 0, Services.InputLimits.MaxFrets), IsGraceNote = true, GraceBeforeBeat = !onBeat, Dead = dead, Velocity = IntAttr(child, "dynamic", 95) };
                    grace.MidiValue = track.IsDrumTrack ? graceFret : track.PitchOf(stringIndex, grace.Fret);
                    grace.Techniques.Add(onBeat ? "GraceOnBeat" : "GraceBefore");
                    var transition = (string?)child.Attribute("transition") ?? "";
                    if (transition == "slide") grace.Techniques.Add("Slide");
                    else if (transition == "bend") grace.Techniques.Add("Bend");
                    cell.Notes.Add(grace);
                    totalNotes++;
                    break;
                case "trill":
                    note.Techniques.Add("Trill");
                    var trillFret = IntAttr(child, "fret", fret + 1);
                    note.TrillDurationDenominator = IntAttr(child, "duration", 8);
                    note.TrillTargetMidi = track.IsDrumTrack ? trillFret : track.PitchOf(stringIndex, Math.Clamp(trillFret, 0, Services.InputLimits.MaxFrets));
                    break;
                case "tremoloPicking":
                    note.Techniques.Add("TremoloPick");
                    cell.TremoloPickDenominator = IntAttr(child, "duration", 16);
                    break;
            }
        }
        return note;
    }

    private static (int Denom, int Dots, int Enters, int Times) ReadDuration(XElement durationEl)
    {
        var denom = IntAttr(durationEl, "value", 4);
        denom = denom is 1 or 2 or 4 or 8 or 16 or 32 or 64 ? denom : 4;
        var dotted = (string?)durationEl.Attribute("dotted");
        var dots = dotted == "dotted" ? 1 : dotted == "doubleDotted" ? 2 : 0;
        var div = durationEl.Element("divisionType");
        var enters = div is not null && int.TryParse((string?)div.Attribute("enters"), out var en) ? en : 1;
        var times = div is not null && int.TryParse((string?)div.Attribute("times"), out var ti) ? ti : 1;
        if (enters < 1) enters = 1;
        if (times < 1) times = 1;
        return (denom, dots, enters, times);
    }

    private static double DurationSlots(int denom, int dots, int enters, int times)
    {
        var baseSlots = 16.0 / denom;
        if (dots == 1) baseSlots *= 1.5;
        else if (dots == 2) baseSlots *= 1.75;
        if (enters != 1 || times != 1) baseSlots *= (double)times / enters;
        return baseSlots;
    }

    private static TabCell? CellAt(MeasureModel measure, int voice, double slotDouble)
    {
        var idx = (int)Math.Round(slotDouble);
        if (idx < 0) return null;
        if (idx > Services.InputLimits.MaxCellsPerMeasure - 1) return null;
        if (voice == 0)
        {
            while (measure.Cells.Count <= idx)
            {
                if (measure.Cells.Count >= Services.InputLimits.MaxCellsPerMeasure) return null;
                measure.Cells.Add(new TabCell());
            }
            return measure.Cells[idx];
        }
        else
        {
            var cells = measure.CellsForVoice(1, create: true);
            while (cells.Count <= idx)
            {
                if (cells.Count >= Services.InputLimits.MaxCellsPerMeasure) return null;
                cells.Add(new TabCell());
            }
            return cells[idx];
        }
    }

    private static void InferSlides(TrackModel track)
    {
        foreach (var measure in track.Measures)
        {
            var cells = measure.Cells.ToList();
            for (var i = 0; i < cells.Count; i++)
            {
                foreach (var note in cells[i].Notes)
                {
                    if (!note.Techniques.Contains("Slide")) continue;
                    if (note.SlideTargetMidi != 0) continue;
                    for (var j = i + 1; j < cells.Count; j++)
                    {
                        var next = cells[j].Notes.FirstOrDefault(n => n.StringIndex == note.StringIndex && !n.IsGraceNote && !n.Dead);
                        if (next is not null) { note.SlideTargetMidi = next.MidiValue; break; }
                    }
                }
            }
        }
    }

    private static string MapClef(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "bass" => Clefs.Bass,
            "alto" => Clefs.Alto,
            "tenor" => Clefs.Alto,
            "percussion" => Clefs.Percussion,
            "treble" => Clefs.Guitar,
            "guitar" => Clefs.Guitar,
            _ => raw,
        };
    }

    private static int NextFreeChannel(SongProject project)
    {
        var used = new HashSet<int>(project.Tracks.Select(t => t.MidiChannel));
        for (var c = 0; c < 16; c++)
            if (!used.Contains(c) && c != 9) return c;
        return 0;
    }

    private static string ReadColor(XElement trackEl)
    {
        var color = trackEl.Element("color");
        if (color is null) return NamedColourSwatches.TrackDefaultColour;
        var r = MarkerInt(color, "R", 0);
        var g = MarkerInt(color, "G", 0);
        var b = MarkerInt(color, "B", 0);
        return ColourHex.Format((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }

    private static int FirstEnding(int mask)
    {
        for (var n = 0; n < 8; n++) if ((mask & (1 << n)) != 0) return n + 1;
        return 0;
    }

    private static string? Text(XElement el, string name) => (string?)el.Element(name)?.Value;
    private static string CleanText(string? raw)
    {
        var s = new string((raw ?? "").Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t').ToArray());
        if (s.Length > Services.InputLimits.MaxUserTextLength) s = s[..Services.InputLimits.MaxUserTextLength];
        return s.Trim();
    }
    private static string TrackName(string? raw)
    {
        var name = new string((raw ?? "").Where(c => !char.IsControl(c)).ToArray());
        if (name.Length > Services.InputLimits.MaxTitleLength) name = name[..Services.InputLimits.MaxTitleLength];
        return string.IsNullOrWhiteSpace(name) ? "Imported Track" : name;
    }
    private static string? CleanTitle(string? raw)
    {
        var s = CleanText(raw);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
    private static int Int(XElement el, string? name, int fallback)
    {
        var e = name is null ? el : el.Element(name);
        if (e is null) return fallback;
        return int.TryParse(e.Value.Trim(), out var v) ? v : fallback;
    }
    private static int IntAttr(XElement el, string name, int fallback)
    {
        var a = (string?)el.Attribute(name);
        return int.TryParse(a, out var v) ? v : fallback;
    }
    private static int MarkerInt(XElement el, string name, int fallback)
    {
        var a = (string?)el.Attribute(name);
        return int.TryParse(a, out var v) ? v : fallback;
    }
}

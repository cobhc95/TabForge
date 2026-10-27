using System.Globalization;
using System.Xml.Linq;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the per-note and per-beat data MusicXML has no field for (exact loudness and pitch, the full technique set, dead notes, bend curves, slide and trill targets,
//   sound length, whammy curve, mix changes, beam and stem), written as processing instructions inside the note and read back onto the notes the score reader built.
// Does not own: the other extras (MusicXmlExtras) or the score reading (alphaTab).
// Tests: MusicXmlRoundTripTests.
internal static class MusicXmlSidecar
{
    // ---- the per-note data MusicXML has no field for, as a processing instruction inside the note (every reader skips it) ----
    internal const string NoteTarget = "TabForge";

    /// <summary>The note's exact loudness, technique set, dead flag, bend curve and slide / trill targets, as "key=value" words.</summary>
    internal static string NoteData(TabNote note)
    {
        var words = new List<string> { "v=" + note.Velocity.ToString(CultureInfo.InvariantCulture), "m=" + note.MidiValue.ToString(CultureInfo.InvariantCulture) };
        if (note.Techniques.Count > 0) words.Add("t=" + string.Join(",", note.Techniques.OrderBy(x => x, StringComparer.Ordinal)));
        words.Add("f=" + note.Fret.ToString(CultureInfo.InvariantCulture));
        if (note.Tied) words.Add("tied=1");
        if (note.Dead) words.Add("dead=1");
        if (note.Ghost) words.Add("ghost=1");
        if (note.Silent) words.Add("silent=1");
        if (note.BendPoints.Count > 0) words.Add("bend=" + string.Join(",", note.BendPoints.Select(p => Num(p.Offset) + ":" + Num(p.Value))));
        if (note.BendTypeName.Length > 0) words.Add("bt=" + note.BendTypeName);
        if (note.BendStyleName.Length > 0) words.Add("bs=" + note.BendStyleName);
        if (note.SlideTargetMidi > 0) words.Add("sl=" + note.SlideTargetMidi.ToString(CultureInfo.InvariantCulture));
        if (note.TrillTargetMidi > 0) words.Add("tr=" + note.TrillTargetMidi.ToString(CultureInfo.InvariantCulture));
        if (note.TrillDurationDenominator > 0) words.Add("trd=" + note.TrillDurationDenominator.ToString(CultureInfo.InvariantCulture));
        if (note.IsGraceNote) words.Add($"go={Num(note.GraceOnsetOffsetSlots)} gd={Num(note.GraceDurationSlots)} gb={(note.GraceBeforeBeat ? 1 : 0)}");
        return string.Join(" ", words);
    }

    /// <summary>Puts the data of <see cref="NoteData"/> back on the note the file's reader built (what the standard elements carried is replaced by the exact values).</summary>
    private static void ApplyNote(TabNote note, string data, bool hasString)
    {
        foreach (var word in data.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = word.IndexOf('=');
            if (eq <= 0) continue;
            var key = word[..eq]; var value = word[(eq + 1)..];
            var number = Number(value);
            switch (key)
            {
                case "v" when number is { } v: note.Velocity = (int)Math.Clamp(v, 0, MidiLimits.Max); break;
                case "m" when number is { } m: note.MidiValue = (int)Math.Clamp(m, 0, MidiLimits.Max); break;
                case "t":
                    var keep = note.Techniques.Contains("Tie");   // the tie origin mark is derived from the tie destinations when the bars are read
                    note.Techniques = new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                    if (keep) note.Techniques.Add("Tie");
                    break;
                case "f" when hasString && number is { } f: note.Fret = (int)Math.Max(0, f); break;
                case "tied": note.Tied = value == "1"; break;
                case "dead": note.Dead = value == "1"; break;
                case "ghost": note.Ghost = value == "1"; break;
                case "silent": note.Silent = value == "1"; break;
                case "bend": note.BendPoints = BendPoints(value); break;
                case "bt": note.BendTypeName = value; break;
                case "bs": note.BendStyleName = value; break;
                case "sl" when number is { } sl: note.SlideTargetMidi = (int)sl; break;
                case "tr" when number is { } tr: note.TrillTargetMidi = (int)tr; break;
                case "trd" when number is { } trd: note.TrillDurationDenominator = (int)trd; break;
                case "go" when number is { } go: note.GraceOnsetOffsetSlots = go; break;
                case "gd" when number is { } gd: note.GraceDurationSlots = gd; break;
                case "gb": note.GraceBeforeBeat = value == "1"; break;
            }
        }
    }

    private static List<BendPointModel> BendPoints(string value)
    {
        var points = new List<BendPointModel>();
        foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(':');
            if (parts.Length == 2 && Number(parts[0]) is { } offset && Number(parts[1]) is { } amount) points.Add(new BendPointModel { Offset = offset, Value = amount });
        }
        return points;
    }

    /// <summary>
    /// Walks each part's notes in time (durations, chords, backups, forwards) and gives every note that carries TabForge data its exact values. A note is found by its bar,
    /// voice, start slot and string (a grace note by its flag); a note the reader did not build is skipped, nothing is added.
    /// </summary>
    internal static void Apply(XElement root, SongProject project)
    {
        var parts = root.Elements("part").ToList();
        if (parts.Count != project.Tracks.Count) return;
        // alphaTab carries an octave shift from one part into the next; TabForge writes every shift of a beat in its data, so its own files start from none.
        var ownFile = root.Element("identification")?.Element("encoding")?.Element("software")?.Value == "TabForge";
        for (var p = 0; p < parts.Count; p++)
        {
            var track = project.Tracks[p];
            if (ownFile)
                foreach (var cell in track.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells))) cell.OctaveShiftSemitones = 0;
            var bar = -1;
            foreach (var measure in parts[p].Elements("measure"))
            {
                bar++;
                if (bar >= track.Measures.Count) break;
                var model = track.Measures[bar];
                var position = 0; var lastStart = 0;
                var used = new HashSet<TabNote>();
                var counted = new Dictionary<(int Voice, int Cell, bool Grace), int>();
                foreach (var element in measure.Elements())
                {
                    if (element.Name.LocalName == "backup") position -= Divisions(element);
                    else if (element.Name.LocalName == "forward") position += Divisions(element);
                    else if (element.Name.LocalName == "note")
                    {
                        var grace = element.Element("grace") is not null;
                        var chord = element.Element("chord") is not null;
                        var start = chord ? lastStart : position;
                        if (!chord) lastStart = start;
                        if (!chord && !grace) position += Divisions(element);
                        if (!chord && !grace && element.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(n => n.Target == BeatTarget) is { } beat
                            && FindCell(element, model, start) is { } beatCell)
                            ApplyBeat(beatCell, beat.Data);
                        if (element.Element("rest") is null && element.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(n => n.Target == NoteTarget) is { } pi
                            && FindNote(element, model, start, grace, used, counted) is { } target)
                        {
                            used.Add(target);
                            ApplyNote(target, pi.Data, element.Element("notations")?.Element("technical")?.Element("string") is not null);
                        }
                    }
                }
            }
        }
    }

    /// <summary>The model beat a MusicXML note (or rest) started: the cell of its voice that begins nearest its slot.</summary>
    private static TabCell? FindCell(XElement element, MeasureModel model, int start)
    {
        var cells = int.TryParse(element.Element("voice")?.Value, out var number) && number > 1 ? model.Voice2Cells : model.Cells;
        var slot = start / (double)SlotDivisions;
        TabCell? best = null; var bestGap = 0.2;
        for (var c = 0; c < cells.Count; c++)
        {
            if (cells[c].Notes.Count == 0 && !cells[c].IsRest) continue;
            var gap = Math.Abs((cells[c].RhythmicPosition ?? c) - slot);
            if (gap < bestGap) { best = cells[c]; bestGap = gap; }
        }
        return best;
    }

    internal const string BarTarget = "TabForgeBar";

    /// <summary>The bar's tempo changes inside it (slot:tempo:ramp) and its triplet feel, which MusicXML can only place by a direction between two notes or a swing mark; null when the bar has neither.</summary>
    internal static string? BarData(MeasureModel bar)
    {
        var words = new List<string>();
        if (bar.MidBarTempos is { Count: > 0 } points)
            words.Add("mid=" + string.Join(",", points.Select(p => Num(p.Slot) + ":" + p.Tempo.ToString(CultureInfo.InvariantCulture) + ":" + Num(p.RampSlots))));
        if (bar.TripletFeelKind != TripletFeels.None || bar.TripletFeel) words.Add("tf=" + bar.TripletFeelKind + (bar.TripletFeel ? ";1" : ";0"));
        return words.Count == 0 ? null : string.Join(" ", words);
    }

    internal static void ApplyBar(MeasureModel bar, string data)
    {
        foreach (var word in data.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith("mid=", StringComparison.Ordinal))
            {
                var points = new List<TempoPoint>();
                foreach (var item in word[4..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = item.Split(':');
                    if (parts.Length == 3 && Number(parts[0]) is { } slot && Number(parts[1]) is { } tempo && Number(parts[2]) is { } ramp) points.Add(new TempoPoint(slot, (int)tempo, ramp));
                }
                if (points.Count > 0) bar.MidBarTempos = points;
            }
            else if (word.StartsWith("tf=", StringComparison.Ordinal))
            {
                var parts = word[3..].Split(';');
                bar.TripletFeelKind = parts[0];
                bar.TripletFeel = parts.Length > 1 && parts[1] == "1";
            }
        }
    }

    internal const string BeatTarget = "TabForgeBeat";

    /// <summary>The beat's own sound length, whammy curve, octave shift, mix change, beam and stem override as "key=value" words; null when all are the defaults.</summary>
    internal static string? BeatData(TabCell cell)
    {
        var words = new List<string>();
        if (cell.SoundDurationPercent != 100) words.Add("snd=" + cell.SoundDurationPercent.ToString(CultureInfo.InvariantCulture));
        if (cell.WhammyPoints.Count > 0) words.Add("wh=" + string.Join(",", cell.WhammyPoints.Select(p => Num(p.Offset) + ":" + Num(p.Value))));
        if (cell.OctaveShiftSemitones != 0) words.Add("oct=" + cell.OctaveShiftSemitones.ToString(CultureInfo.InvariantCulture));
        if (cell.BeamMode != BeamMode.Auto) words.Add("beam=" + cell.BeamMode);
        if (cell.BreakSecondaryBeamBefore) words.Add("brk=1");
        if (cell.StemDirection != StemDirection.Auto) words.Add("stem=" + cell.StemDirection);
        if (cell.BrushStepSlots != 0) words.Add("brush=" + Num(cell.BrushStepSlots));
        if (cell.TremoloPickDenominator != 0) words.Add("trem=" + cell.TremoloPickDenominator.ToString(CultureInfo.InvariantCulture));
        if (cell.Mix is { } mix)
        {
            string Part(string key, int? value) => value is { } v ? key + v.ToString(CultureInfo.InvariantCulture) + ";" : "";
            words.Add("mix=" + Part("p", mix.Program) + Part("v", mix.Volume) + Part("n", mix.Pan) + Part("c", mix.Chorus) + Part("r", mix.Reverb) + Part("h", mix.Phaser)
                + Part("m", mix.Tremolo) + Part("t", mix.Tempo) + "b" + mix.TransitionBeats.ToString(CultureInfo.InvariantCulture) + ";a" + (mix.AllTracks ? 1 : 0));
        }
        return words.Count == 0 ? null : string.Join(" ", words);
    }

    private static void ApplyBeat(TabCell cell, string data)
    {
        foreach (var word in data.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = word.IndexOf('=');
            if (eq <= 0) continue;
            var key = word[..eq]; var value = word[(eq + 1)..];
            var number = Number(value);
            switch (key)
            {
                case "snd" when number is { } snd: cell.SoundDurationPercent = (int)snd; break;
                case "wh": cell.WhammyPoints = BendPoints(value); break;
                case "oct" when number is { } oct: cell.OctaveShiftSemitones = (int)oct; break;
                case "beam" when Enum.TryParse<BeamMode>(value, out var beam): cell.BeamMode = beam; break;
                case "brk": cell.BreakSecondaryBeamBefore = value == "1"; break;
                case "stem" when Enum.TryParse<StemDirection>(value, out var stem): cell.StemDirection = stem; break;
                case "brush" when number is { } brush: cell.BrushStepSlots = brush; break;
                case "trem" when number is { } trem: cell.TremoloPickDenominator = (int)trem; break;
                case "mix": cell.Mix = ReadMix(value); break;
            }
        }
    }

    private static MixChange ReadMix(string value)
    {
        var mix = new MixChange();
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 2 || !int.TryParse(part[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) continue;
            switch (part[0])
            {
                case 'p': mix.Program = n; break;
                case 'v': mix.Volume = n; break;
                case 'n': mix.Pan = n; break;
                case 'c': mix.Chorus = n; break;
                case 'r': mix.Reverb = n; break;
                case 'h': mix.Phaser = n; break;
                case 'm': mix.Tremolo = n; break;
                case 't': mix.Tempo = n; break;
                case 'b': mix.TransitionBeats = n; break;
                case 'a': mix.AllTracks = n == 1; break;
            }
        }
        return mix;
    }

    /// <summary>The model note a MusicXML note became: the cell that starts at its slot in its voice, then the note on its string (or, with no string, the next note of the cell in written order).</summary>
    private static TabNote? FindNote(XElement element, MeasureModel model, int start, bool grace, HashSet<TabNote> used, Dictionary<(int Voice, int Cell, bool Grace), int> counted)
    {
        var voice = int.TryParse(element.Element("voice")?.Value, out var number) && number > 1 ? 2 : 1;
        var cells = voice == 1 ? model.Cells : model.Voice2Cells;
        var slot = start / (double)SlotDivisions;
        var index = GuitarProBarConverter.NearestNoteCell(cells, slot);
        if (index < 0)
        {
            // An on-beat grace note takes its time from its principal note, which alphaTab then starts late; the file's own slot is where TabForge had it.
            index = GuitarProBarConverter.NearestNoteCell(cells, slot, 0.75);
            if (index < 0 || !cells[index].Notes.Any(n => n.IsGraceNote)) return null;
            cells[index].RhythmicPosition = slot;
        }
        var candidates = cells[index].Notes.Where(n => n.IsGraceNote == grace && !used.Contains(n)).ToList();
        if (int.TryParse(element.Element("notations")?.Element("technical")?.Element("string")?.Value, out var str))
            return candidates.FirstOrDefault(n => n.StringIndex == str - 1);
        var seen = counted.GetValueOrDefault((voice, index, grace));
        counted[(voice, index, grace)] = seen + 1;
        var all = cells[index].Notes.Where(n => n.IsGraceNote == grace).ToList();
        return seen < all.Count ? all[seen] : null;
    }

    /// <summary>Divisions per sixteenth-note slot in the files TabForge writes (240 per quarter note).</summary>
    private const int SlotDivisions = 60;

    private static int Divisions(XElement element) => int.TryParse(element.Element("duration")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
}

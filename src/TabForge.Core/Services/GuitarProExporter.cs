using System.IO;
using AlphaTab.Model;
using TabForge.Models;

namespace TabForge.Services;

// Owns: saving a song as a Guitar Pro 7/8 (.gp) file.
// Does not own: reading files (GuitarProImporter), the embedded project and zip reads (GuitarProExporter.Embed.cs) and the export questions (GpExportPreflight).
// Tests: no dedicated test class.
/// <summary>
/// Saves a song as a Guitar Pro 7/8 file (.gp) via alphaTab's GP7 writer: the reverse of
/// <see cref="GuitarProImporter"/>. Tracks, tunings, capo, MIDI program/channel/volume/pan, metre,
/// tempo changes, repeats, alternate endings, sections, durations, tuplets, rests, ties, dead/ghost
/// notes, palm mute, let ring, vibrato, hammer-ons, slides, harmonics, bends, accents and staccato are
/// written. By default the whole TabForge project (drum presets, score fonts, mix-table ramps, plug-in states...)
/// is also embedded inside the .gp, so reopening it in TabForge restores everything; with embedProject = false
/// the file is clean and those TabForge-only settings are not stored in it.
/// </summary>
public static partial class GuitarProExporter
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
        // The embedded project is built (and size-checked) first, so an over-limit song fails before anything is exported or written.
        AudioTrackExport.RequireNotation(project, "a score file");
        var embeddedBytes = embedProject ? EmbeddedProjectBytes(project) : null;
        var mixPoints = new List<GpifAutomationWriter.MixPoint>();
        var (score, data) = AlphaTabBoundary.WithModelLock(() =>   // alphaTab's id counters are static (see ModelLock)
        {
            var settings = new AlphaTab.Settings();
            var built = Build(project, mixPoints);
            built.Finish(settings);
            return (built, new AlphaTab.Exporter.Gp7Exporter().Export(built, settings));
        });
        var bytes = new byte[(int)data.Length];   // the copy is plain data: outside the lock
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)data[i];
        // A .gp file is a zip. alphaTab 1.8.4's writer stores bogus CRC-32 values in it (and 0x7fffffff for the
        // directory entry), so the whole archive is rewritten with correct CRCs. TabForge also adds its complete
        // project (all TabForge-only settings) as one extra entry; Guitar Pro ignores it, TabForge reads it back
        // for a lossless round trip.
        List<ZipPart> parts;
        try { parts = ReadZip(bytes); }
        catch (InvalidDataException) { parts = new(); } // Not logged: not a zip we understand: alphaTab output kept untouched
        if (parts.Count == 0) return bytes; // not a zip we understand: keep alphaTab's output untouched
        for (var i = 0; i < parts.Count; i++)
            if (parts[i].Name.Equals(ScoreEntry, StringComparison.OrdinalIgnoreCase) && parts[i].Data is { Length: > 0 })
            {
                var patched = GuitarProBendCurve.Patch(parts[i].Data, score);
                patched = GpifAutomationWriter.Patch(patched, mixPoints);   // alphaTab's writer emits no volume / pan changes
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

    private static Score Build(SongProject project, List<GpifAutomationWriter.MixPoint> mixPoints)
    {
        var score = new Score
        {
            Title = project.Title ?? "", SubTitle = project.Subtitle ?? "", Artist = project.Artist ?? "", Album = project.Album ?? "",
            Music = project.MusicAuthor ?? "", Words = project.LyricsAuthor ?? "", Copyright = project.Copyright ?? "",
            Tab = project.TabAuthor ?? "", Instructions = project.Instructions ?? "", Notices = project.Notice ?? "",
        };
        var barCount = Math.Max(1, project.NotationTracks.Select(t => t.Measures.Count).DefaultIfEmpty(1).Max());
        var markers = project.Markers.ToDictionary(m => m.MeasureIndex, m => m.Title);
        var keys = BuildMasterBars(project, score, barCount, markers);

        foreach (var source in project.NotationTracks)
            score.AddTrack(BuildTrack(project, score, source, barCount, mixPoints));
        // alphaTab's key setters walk the bars, so keys go on once every track has its bars.
        for (var i = 0; i < keys.Count; i++)
        {
            score.MasterBars[i].KeySignature = (KeySignature)Math.Clamp(keys[i].Key, -7, 7);
            score.MasterBars[i].KeySignatureType = keys[i].Minor ? KeySignatureType.Minor : KeySignatureType.Major;
        }
        return score;
    }

    /// <summary>One master bar per bar: time signature, repeats, endings, directions, section, triplet feel and tempo automations. Returns the key of each bar.</summary>
    private static List<(int Key, bool Minor)> BuildMasterBars(SongProject project, Score score, int barCount, Dictionary<int, string> markers)
    {
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
            AddMidBarTempos(project, mb, b, model, pendingTempo);
            score.AddMasterBar(mb);
            keys.Add((lastKey, lastMinor));
        }
        return keys;
    }

    /// <summary>The tempo automations inside a bar: instant changes, and ramps as linear automations that end where the ramp ends.</summary>
    private static void AddMidBarTempos(SongProject project, MasterBar mb, int b, MeasureModel? model, Dictionary<int, int> pendingTempo)
    {
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
    }

    /// <summary>One track: its playback information, staff and tuning, and every bar with its voices and beats.</summary>
    private static Track BuildTrack(SongProject project, Score score, TrackModel source, int barCount, List<GpifAutomationWriter.MixPoint> mixPoints)
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
                Program = Math.Clamp(source.MidiProgram, 0, MidiLimits.Max),
                PrimaryChannel = source.MidiChannel, SecondaryChannel = source.MidiChannel,
                // Mixer groups baked in. The 0..16 steps are alphaTab's own model; the exact fractions (patch 0002 of TabForge.AlphaTab) are what the gpif stores.
                Volume = GpMixerScale.MixerToStep(MixerGroups.Volume(project, source)),
                Balance = GpMixerScale.MixerToStep(MixerGroups.Pan(project, source)),
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
        var customByCurve = new Dictionary<string, bool>();   // this export's memo of SimplifyBend's alphaTab probe
        // alphaTab chains a voice's first beat to the previous bar's SAME voice: a second voice in only some bars made its
        // Finish throw (ArgumentOutOfRange), so a track with a second voice anywhere carries one in every bar (empty = a rest).
        var hasVoice2 = source.Measures.Any(m => BarTiming.HasSecondaryNotes(m.Voice2Cells));
        var state = new TrackExportState(project, score, source, staff, track, drums, hasVoice2, mixPoints, articulations, chordIds, customByCurve, transpose, bake);
        for (var b = 0; b < barCount; b++) AddBar(state, b);
        return track;
    }

    /// <summary>What the bars of one track share while it is written.</summary>
    private sealed record TrackExportState(SongProject Project, Score Score, TrackModel Source, Staff Staff, Track Track, bool Drums, bool HasVoice2, List<GpifAutomationWriter.MixPoint> MixPoints,
        Dictionary<int, int> Articulations, Dictionary<string, string> ChordIds, Dictionary<string, bool> CustomByCurve, int Transpose, int Bake);

    /// <summary>One bar of a track: clef, simile mark and its voices (a bar past the end of the track is a rest).</summary>
    private static void AddBar(TrackExportState s, int b)
    {
        var project = s.Project; var source = s.Source; var staff = s.Staff; var drums = s.Drums; var hasVoice2 = s.HasVoice2;
        var bar = new Bar();
        staff.AddBar(bar);
        if (drums) bar.Clef = Clef.Neutral;
        else if (b < source.Measures.Count) (bar.Clef, bar.ClefOttava) = GpClef(source.Measures[b].Clef, source.Kind == TrackKind.Bass);
        if (b >= source.Measures.Count)
        {
            for (var v = 0; v < (hasVoice2 ? 2 : 1); v++) { var empty = new Voice(); bar.AddVoice(empty); empty.AddBeat(RestBeat(4)); }
            return;
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
        // A rest-only second voice is never written (it is empty in Guitar Pro): the bar gets an empty voice instead.
        var voice2 = BarTiming.HasSecondaryNotes(measure.Voice2Cells) ? measure.Voice2Cells : new List<TabCell>();
        foreach (var cells in hasVoice2 ? new[] { measure.Cells, voice2 } : new[] { measure.Cells })
            AddVoice(s, bar, cells, b, slots, emptyLength);
    }

    /// <summary>One voice of a bar: its beats with rests filled in between, an empty bar as a whole-bar rest.</summary>
    private static void AddVoice(TrackExportState s, Bar bar, List<TabCell> cells, int b, int slots, double emptyLength)
    {
        var project = s.Project; var source = s.Source; var staff = s.Staff; var track = s.Track; var drums = s.Drums; var score = s.Score;
        var voice = new Voice();
        bar.AddVoice(voice);
        var cursor = 0.0;
        var any = false;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var start = cell.RhythmicPosition ?? Math.Max(i, cursor);
            GpifAutomationWriter.Collect(s.MixPoints, cell.Mix, score.Tracks.Count, project.NotationTracks.Count(), b, start / Math.Max(1, slots));
            if (cell.Notes.Count == 0 && !cell.IsRest) continue;
            if (start > cursor + 0.01) foreach (var fill in FillRests(start - cursor)) voice.AddBeat(fill);
            foreach (var written in BeatsFor(cell, source, drums, track, s.Articulations, staff, s.ChordIds, s.CustomByCurve, (s.Transpose, s.Bake))) voice.AddBeat(written);
            cursor = start + MusicTime.CellSlots(cell);
            any = true;
        }
        // An empty bar is one whole-bar rest (a quarter rest in a 3/4 bar would reopen as a one-beat bar).
        if (!any) { if (emptyLength == 16) voice.AddBeat(RestBeat(1)); else foreach (var fill in FillRests(emptyLength)) voice.AddBeat(fill); return; }
        // A bar that ends early stays short (a pickup, a short last bar): the playback compiler plays an imported song's
        // incomplete bar only as long as its content (ScoreToMidiCompiler.ContentSlots), so padding it with rests would
        // lengthen the bar and push every later bar late. A TabForge-written song keeps its empty cells as silence, so it is padded.
        if (cursor < slots - 0.01 && project.ImportedFrom is null) foreach (var fill in FillRests(slots - cursor)) voice.AddBeat(fill);
    }
}

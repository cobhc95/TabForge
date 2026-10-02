using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>The paste choices after remembered answers and the dialog, with the recommended option for anything not asked.</summary>
public sealed record ResolvedPaste(BeatPasteMode Beats, OctavePolicy Octave, BarsOntoNotesAnswer Bars, bool CopyBarSettings, DrumPastePolicy Drums,
    PasteMappingMode Mapping = PasteMappingMode.KeepPitch, int OctaveSemitones = 0)
{
    public static ResolvedPaste From(PasteAnswers a) => new(
        a.BeatsOntoNotes == BeatsOntoNotesAnswer.Insert ? BeatPasteMode.Insert : BeatPasteMode.Replace,
        a.Octave == OctaveAnswer.ShiftOctave ? OctavePolicy.ShiftByOctave : OctavePolicy.KeepExactPitch,
        a.BarsOntoNotes ?? BarsOntoNotesAnswer.Overwrite,
        a.BarSettings != BarSettingsAnswer.KeepTarget,
        a.Drums == DrumsAnswer.DontPaste ? DrumPastePolicy.DontPaste : DrumPastePolicy.RhythmOnOneSound);
}

/// <summary>Takes the recommended answers without asking (headless use).</summary>
public sealed class RecommendedPasteAnswers : IPasteQuestionAsker
{
    public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions) => new();
}

/// <summary>Where a paste lands: the active track and voice, and the cursor (or the selection start).</summary>
public sealed record PasteTarget(int TrackIndex, int Voice, int Bar, int Cell);

/// <summary>Result of <see cref="EditCommands.Paste"/>. When <see cref="Changed"/> is false the project is unchanged.</summary>
public sealed class PasteOutcome
{
    public bool Changed { get; init; }
    public bool Cancelled { get; init; }
    /// <summary>Status-bar text: what was pasted and what was left out, or why nothing was pasted.</summary>
    public string Status { get; init; } = "";
    public int FirstBar { get; init; } = -1;
    public int LastBar { get; init; } = -1;
    /// <summary>Old-to-new bar map of a structural insert (markers/sections/playback follow it); null for in-place pastes.</summary>
    public int[]? BarMap { get; init; }
    /// <summary>The questions this paste raised, and those put to the asker (the rest were remembered).</summary>
    public IReadOnlyList<PasteQuestion> Needed { get; init; } = Array.Empty<PasteQuestion>();
    public IReadOnlyList<PasteQuestion> Asked { get; init; } = Array.Empty<PasteQuestion>();
    public NoteMapReport? Mapping { get; init; }
    public int DroppedBeats { get; init; }
    public int TracksLeftOut { get; init; }
}

// Copy / cut / paste of score clips (COPY_PASTE_DESIGN.md chunk C4): NoteMapper maps, BarGrid places.
public static partial class EditCommands
{
    /// <summary>
    /// Pastes <paramref name="clip"/> at <paramref name="target"/>: works out which questions apply, takes remembered answers from
    /// <paramref name="settings"/>, asks the rest through <paramref name="asker"/> (cancel = no change), stores the answers the user
    /// asked to remember (the caller saves the settings), maps the notes to the target instrument(s) and places them.
    /// Pure model code; the caller runs it through <c>DocumentEdits.Run</c> for one undo step.
    /// </summary>
    public static PasteOutcome Paste(SongProject p, ScoreClip clip, PasteTarget target, EditingSettings settings, IPasteQuestionAsker asker)
    {
        if (target.TrackIndex < 0 || target.TrackIndex >= p.Tracks.Count) return new PasteOutcome { Status = "No track to paste into." };
        if (clip.Tracks.Count == 0) return new PasteOutcome { Status = ClipboardService.NotTabForgeNotesMessage };
        return clip.Kind == ScoreClipKind.Beats
            ? PasteBeats(p, clip, target, settings, asker)
            : PasteBars(p, clip, target, settings, asker);
    }

    /// <summary>Runs a paste as one edit through <see cref="Documents.DocumentEdits.Run"/>: one undo step (none when nothing changed), the dirty flag and one timeline invalidation. Returns the outcome either way.</summary>
    public static PasteOutcome RunPaste(Documents.DocumentSession document, ScoreClip clip, PasteTarget target, EditingSettings settings, IPasteQuestionAsker asker) =>
        EditorGuard.Blocks(document.Project, target.TrackIndex) ? new PasteOutcome { Status = EditorGuard.Hint } : RunPaste(document, p => FillPasted(p, Paste(p, clip, target, settings, asker), target, settings));

    /// <summary>With the rest fill on, a paste that leaves a bar short gets the remainder filled with rests (pasted notes are never dropped; an overfull bar shows red).</summary>
    private static PasteOutcome FillPasted(SongProject p, PasteOutcome outcome, PasteTarget target, EditingSettings settings)
    {
        if (settings.FillBarsWithRests && outcome.Changed && outcome.FirstBar >= 0) BarFill.FillBars(p, target.TrackIndex, outcome.FirstBar, outcome.LastBar);
        return outcome;
    }

    /// <summary><see cref="RunPaste(Documents.DocumentSession, ScoreClip, PasteTarget, EditingSettings, IPasteQuestionAsker)"/> for Paste Special.</summary>
    public static PasteOutcome RunPasteSpecial(Documents.DocumentSession document, ScoreClip clip, PasteTarget target, PasteSpecialOptions options, EditingSettings settings) =>
        EditorGuard.Blocks(document.Project, target.TrackIndex) ? new PasteOutcome { Status = EditorGuard.Hint } : RunPaste(document, p => FillPasted(p, PasteSpecial(p, clip, target, options, settings), target, settings));

    private static PasteOutcome RunPaste(Documents.DocumentSession document, Func<SongProject, PasteOutcome> paste)
    {
        var last = new PasteOutcome();
        Documents.DocumentEdits.Run<PasteOutcome>(document, p => (last = paste(p)).Changed ? last : null);
        return last;
    }

    // ---------- beats ----------

    private static PasteOutcome PasteBeats(SongProject p, ScoreClip clip, PasteTarget target, EditingSettings settings, IPasteQuestionAsker asker,
        ResolvedPaste? explicitChoice = null, int copies = 1)
    {
        var source = clip.Tracks[0];
        if (source.Events.Count == 0) return new PasteOutcome { Status = "Nothing to paste." };
        var track = p.Tracks[target.TrackIndex];
        var voice = Math.Clamp(target.Voice, 0, 1);
        var anchor = BarGrid.AnchorAt(p, target.TrackIndex, voice, target.Bar, target.Cell);
        var length = Math.Max(source.LengthSlots, source.Events.Max(e => e.OffsetSlots + MusicTime.CellSlots(e.Cell)));
        var from = InstrumentLayout.Of(source.ToTrackModel());
        var to = InstrumentLayout.Of(track);
        var beats = source.Events.Select(e => e.Cell).ToList();

        var needed = MapperQuestions(from, to, beats);
        if (HasNotesInSpan(p, target.TrackIndex, voice, anchor, anchor + length)) needed.Add(PasteQuestion.BeatsOntoNotes);
        if (!Resolve(needed, settings, asker, explicitChoice, out var choice, out var asked))
            return new PasteOutcome { Cancelled = true, Status = "Paste cancelled", Needed = needed, Asked = asked };

        var mapped = NoteMapper.Map(from, to, beats, MapOptions(choice));
        if (mapped.Report.Kind == NoteMapKind.Refused || mapped.Beats.Count != beats.Count)
            return new PasteOutcome { Status = RefusedText(from, to), Needed = needed, Asked = asked, Mapping = mapped.Report };
        var sameTrack = source.SourceTrackId != Guid.Empty && source.SourceTrackId == track.Id;
        var run = new BeatRun(source.Events.Select((e, i) => new ClipBeat(e.OffsetSlots, Prepare(mapped.Beats[i], sameTrack))).ToList(), length);
        var placed = BarGrid.PlaceBeats(p, target.TrackIndex, voice, anchor, run, choice.Beats);
        if (!placed.Ok)
            return new PasteOutcome { Status = placed.Error ?? "Nothing pasted.", Needed = needed, Asked = asked, Mapping = mapped.Report };

        var status = $"Pasted {Plural(beats.Count, "beat")} at bar {target.Bar + 1}" + (copies > 1 ? $" ({copies} copies)" : "") + (choice.Beats == BeatPasteMode.Insert ? " (inserted)" : "")
            + Tail(mapped.Report, placed.DroppedBeats, placed.BarsAppended, 0);
        return new PasteOutcome
        {
            Changed = true, Status = status, FirstBar = placed.FirstBar, LastBar = placed.LastBar, BarMap = placed.BarMap,
            Needed = needed, Asked = asked, Mapping = mapped.Report, DroppedBeats = placed.DroppedBeats
        };
    }

    /// <summary>True when a beat with notes in one voice of a track sounds inside [from, to) (rests and empty cells do not count).</summary>
    private static bool HasNotesInSpan(SongProject p, int trackIndex, int voice, double from, double to)
    {
        var track = p.Tracks[trackIndex];
        var (firstBar, _) = BarGrid.Locate(p, from);
        firstBar = Math.Max(0, firstBar - 1); // a note from the previous bar can ring into the span
        var (lastBar, _) = BarGrid.Locate(p, Math.Max(from, to - 1e-3));
        for (var bar = firstBar; bar <= lastBar && bar < track.Measures.Count; bar++)
        {
            var cells = track.Measures[bar].CellsForVoice(voice);
            if (cells.Count == 0) continue;
            var start = BarGrid.BarStart(p, bar);
            var onsets = BarGrid.Onsets(cells);
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Notes.Count == 0) continue;
                var s = start + onsets[i];
                var e = s + MusicTime.CellSlots(cells[i]);
                if (s < to - 1e-6 && e > from + 1e-6) return true;
            }
        }
        return false;
    }

    // ---------- bars ----------

    private static PasteOutcome PasteBars(SongProject p, ScoreClip clip, PasteTarget target, EditingSettings settings, IPasteQuestionAsker asker,
        ResolvedPaste? explicitChoice = null, int copies = 1)
    {
        // A one-track score copy lands on the active track; a multi-track clip follows TimelineClips.MapTracks (i -> i within the song).
        var map = clip.Tracks.Count == 1 ? Enumerable.Repeat(-1, p.Tracks.Count).ToArray() : TimelineClips.MapTracks(clip, p, target.TrackIndex);
        if (clip.Tracks.Count == 1) map[target.TrackIndex] = 0;
        return PasteBars(p, clip, target.Bar, map, explicitChoice?.Bars, settings, asker, explicitChoice, copies);
    }

    /// <summary>
    /// Bars paste with an explicit project-track -> clip-track map (-1 = none). <paramref name="forced"/> skips Q3 (the timeline's
    /// "Paste before area" is an insert by definition); otherwise Q3 is asked when a target bar has notes. Q2/Q4/Q5 as they apply.
    /// </summary>
    internal static PasteOutcome PasteBars(SongProject p, ScoreClip clip, int atBar, int[] map, BarsOntoNotesAnswer? forced,
        EditingSettings settings, IPasteQuestionAsker asker, ResolvedPaste? explicitChoice = null, int copies = 1)
    {
        if (clip.Kind != ScoreClipKind.Bars || clip.Tracks.Count == 0) return new PasteOutcome { Status = "Only whole bars can be pasted here." };
        var count = clip.Tracks.Max(t => t.Bars.Count);
        if (count == 0) return new PasteOutcome { Status = "Nothing to paste." };
        var at = Math.Max(0, atBar);
        var pairs = new List<(ScoreClipTrack Source, int Target)>();
        for (var t = 0; t < p.Tracks.Count && t < map.Length; t++)
            if (map[t] >= 0 && map[t] < clip.Tracks.Count) pairs.Add((clip.Tracks[map[t]], t));
        var leftOut = clip.Tracks.Count - pairs.Select(x => x.Source).Distinct().Count();
        if (pairs.Count == 0) return new PasteOutcome { Status = "Nothing to paste into." };

        var needed = new List<PasteQuestion>();
        foreach (var (source, index) in pairs)
            needed.AddRange(MapperQuestions(InstrumentLayout.Of(source.ToTrackModel()), InstrumentLayout.Of(p.Tracks[index]),
                source.Bars.SelectMany(b => b.Cells.Concat(b.Voice2Cells))));
        if (forced is null && pairs.Any(x => TargetBarsHaveNotes(p, x.Target, at, count))) needed.Add(PasteQuestion.BarsOntoNotes);
        if (CarriesOtherSettings(p, clip, at, count)) needed.Add(PasteQuestion.BarSettings);
        needed = needed.Distinct().OrderBy(q => (int)q).ToList();
        if (!Resolve(needed, settings, asker, explicitChoice, out var choice, out var asked))
            return new PasteOutcome { Cancelled = true, Status = "Paste cancelled", Needed = needed, Asked = asked };
        // Empty (or rests-only) target bars: no question, the bars go straight in.
        var mode = forced ?? (needed.Contains(PasteQuestion.BarsOntoNotes) ? choice.Bars : BarsOntoNotesAnswer.Overwrite);

        var report = new NoteMapReport();
        var trackBars = new List<TrackBars>();
        foreach (var (source, index) in pairs)
        {
            var from = InstrumentLayout.Of(source.ToTrackModel());
            var to = InstrumentLayout.Of(p.Tracks[index]);
            var sameTrack = source.SourceTrackId != Guid.Empty && source.SourceTrackId == p.Tracks[index].Id;
            var bars = source.Bars.Select(ProjectService.CloneMeasure).ToList();
            var refused = false;
            for (var voice = 0; voice < 2 && !refused; voice++)
            {
                var cells = bars.SelectMany(b => b.CellsForVoice(voice)).ToList();
                if (cells.Count == 0) continue;
                var mapped = NoteMapper.Map(from, to, cells, MapOptions(choice));
                Merge(report, mapped.Report);
                if (mapped.Report.Kind == NoteMapKind.Refused || mapped.Beats.Count != cells.Count) { refused = true; break; }
                var n = 0;
                foreach (var bar in bars)
                {
                    var list = bar.CellsForVoice(voice);
                    for (var i = 0; i < list.Count; i++) list[i] = Prepare(mapped.Beats[n++], sameTrack);
                }
            }
            if (refused)
            {
                if (pairs.Count == 1) return new PasteOutcome { Status = RefusedText(from, to), Needed = needed, Asked = asked, Mapping = report };
                leftOut++;
                continue;
            }
            trackBars.Add(new TrackBars(index, bars));
        }
        if (trackBars.Count == 0) return new PasteOutcome { Status = "Nothing pasted.", Needed = needed, Asked = asked, Mapping = report };

        var span = Math.Max(1, count / Math.Max(1, copies));   // "after" means after the copied bars, however many times they repeat
        var placed = mode switch
        {
            BarsOntoNotesAnswer.InsertBefore => BarGrid.InsertBars(p, at, trackBars, choice.CopyBarSettings),
            BarsOntoNotesAnswer.InsertAfter => BarGrid.InsertBars(p, at + span, trackBars, choice.CopyBarSettings),
            _ => BarGrid.OverwriteBars(p, at, trackBars, choice.CopyBarSettings)
        };
        if (!placed.Ok) return new PasteOutcome { Status = placed.Error ?? "Nothing pasted.", Needed = needed, Asked = asked, Mapping = report };

        var verb = mode switch
        {
            BarsOntoNotesAnswer.InsertBefore => $"Inserted {Plural(count, "bar")} before bar {at + 1}",
            BarsOntoNotesAnswer.InsertAfter => $"Inserted {Plural(count, "bar")} after bar {at + span}",
            _ => $"Pasted {Plural(count, "bar")} at bar {at + 1}"
        } + (copies > 1 ? $" ({copies} copies)" : "");
        return new PasteOutcome
        {
            Changed = true, Status = verb + Tail(report, placed.DroppedBeats, placed.BarsAppended, leftOut),
            FirstBar = placed.FirstBar, LastBar = placed.LastBar, BarMap = placed.BarMap,
            Needed = needed, Asked = asked, Mapping = report, DroppedBeats = placed.DroppedBeats, TracksLeftOut = leftOut
        };
    }

    /// <summary>Any note in either voice of bars [at, at + count) of a track (bars past the end, rests and empty bars do not count).</summary>
    private static bool TargetBarsHaveNotes(SongProject p, int trackIndex, int at, int count)
    {
        var measures = p.Tracks[trackIndex].Measures;
        for (var bar = at; bar < at + count && bar < measures.Count; bar++)
            if (measures[bar].Cells.Any(c => c.Notes.Count > 0) || measures[bar].Voice2Cells.Any(c => c.Notes.Count > 0)) return true;
        return false;
    }

    /// <summary>True when copying the clip's bar settings would change the target bars (time signature, key, tempo, feel, ...).</summary>
    private static bool CarriesOtherSettings(SongProject p, ScoreClip clip, int at, int count)
    {
        var bars = clip.Tracks[0].Bars;
        var measures = p.MasterBarTrack?.Measures;
        for (var i = 0; i < count && i < bars.Count; i++)
        {
            var bar = bars[i];
            var targetBar = measures is { Count: > 0 } ? measures[Math.Min(at + i, measures.Count - 1)] : null;
            var targetMeter = (targetBar?.TimeSigNum ?? p.TimeSignatureNumerator, targetBar?.TimeSigDenom ?? p.TimeSignatureDenominator);
            if (clip.TimeSignatureOf(i) != targetMeter) return true;
            var clipKey = (bar.KeySignature ?? clip.SongKeySignature, bar.KeySignatureMinor ?? clip.SongKeySignatureMinor);
            var targetKey = (targetBar?.KeySignature ?? p.KeySignature, targetBar?.KeySignatureMinor ?? p.KeySignatureMinor);
            if (clipKey != targetKey) return true;
            if (bar.TempoChange is not null && bar.TempoChange != targetBar?.TempoChange) return true;
            if (bar.MidBarTempos is { Count: > 0 } && !(targetBar?.MidBarTempos?.SequenceEqual(bar.MidBarTempos) ?? false)) return true;
            if (targetBar is not null && (bar.TripletFeel != targetBar.TripletFeel || bar.TripletFeelKind != targetBar.TripletFeelKind
                || bar.FreeTime != targetBar.FreeTime || bar.Anacrusis != targetBar.Anacrusis)) return true;
        }
        return false;
    }

    // ---------- shared ----------

    /// <summary>NoteMapper's own question flags -> the dialog's questions (Q2 octave, Q5 drums).</summary>
    private static List<PasteQuestion> MapperQuestions(InstrumentLayout from, InstrumentLayout to, IEnumerable<TabCell> beats)
    {
        var q = NoteMapper.Questions(from, to, beats);
        var result = new List<PasteQuestion>();
        if (q.HasFlag(PasteQuestions.Octave)) result.Add(PasteQuestion.Octave);
        if (q.HasFlag(PasteQuestions.Drums)) result.Add(PasteQuestion.Drums);
        return result;
    }

    /// <summary>Remembered answers first, then the asker for the rest, then the remembered ticks stored. False = cancelled.</summary>
    private static bool Resolve(List<PasteQuestion> needed, EditingSettings settings, IPasteQuestionAsker asker, ResolvedPaste? explicitChoice,
        out ResolvedPaste choice, out IReadOnlyList<PasteQuestion> asked)
    {
        if (explicitChoice is not null)   // Paste Special: every answer is given, nothing is asked or remembered
        {
            choice = explicitChoice;
            asked = Array.Empty<PasteQuestion>();
            return true;
        }
        var toAsk = PasteQuestionInfo.StillToAsk(settings, needed);
        asked = toAsk;
        var answers = PasteQuestionInfo.Stored(settings);
        if (toAsk.Count > 0)
        {
            var reply = asker.Ask(toAsk);
            if (reply is null) { choice = ResolvedPaste.From(new PasteAnswers()); return false; }
            answers = answers.Merge(reply);
            PasteQuestionInfo.Remember(settings, reply, toAsk);
        }
        choice = ResolvedPaste.From(answers);
        return true;
    }

    private static NoteMapOptions MapOptions(ResolvedPaste c) => new()
    {
        Octave = c.Octave, Drums = c.Drums, Mode = c.Mapping, OctaveSemitones = c.OctaveSemitones
    };

    /// <summary>Lyrics never travel; mix-table changes only within the same track (they carry that track's program/volume/pan).</summary>
    private static TabCell Prepare(TabCell cell, bool sameTrack)
    {
        cell.Lyrics = "";
        if (!sameTrack) cell.Mix = null;
        return cell;
    }

    private static string RefusedText(InstrumentLayout from, InstrumentLayout to) =>
        from.IsDrums != to.IsDrums ? (to.IsDrums ? "Not pasted: pitched notes onto a drum track" : "Not pasted: drum notes onto a pitched track")
        : "Nothing pasted.";

    private static void Merge(NoteMapReport into, NoteMapReport from)
    {
        into.Kind = from.Kind == NoteMapKind.Refused || into.Kind == NoteMapKind.Passthrough ? from.Kind : into.Kind;
        if (into.OctaveShift == 0) into.OctaveShift = from.OctaveShift;
        into.NotesIn += from.NotesIn; into.NotesPlaced += from.NotesPlaced; into.NotesRefretted += from.NotesRefretted;
        into.NotesOctaveFolded += from.NotesOctaveFolded; into.NotesMerged += from.NotesMerged; into.TechniquesDropped += from.TechniquesDropped;
        into.LeftOut.AddRange(from.LeftOut);
    }

    private static string Tail(NoteMapReport report, int droppedBeats, int barsAppended, int tracksLeftOut)
    {
        var parts = new List<string>();
        var summary = report.Summary();
        if (summary.Length > 0) parts.Add(summary);
        if (droppedBeats > 0) parts.Add($"{Plural(droppedBeats, "beat")} did not fit the bar and {(droppedBeats == 1 ? "was" : "were")} left out");
        if (barsAppended > 0) parts.Add($"{Plural(barsAppended, "bar")} added at the end");
        if (tracksLeftOut > 0) parts.Add($"{Plural(tracksLeftOut, "track")} left out");
        return parts.Count == 0 ? "" : "; " + string.Join("; ", parts);
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    // ---------- cut ----------

    /// <summary>
    /// Clears what a cut took (design 3.7, owner decision): a Bars clip empties both voices of those bars on this track (bars stay);
    /// a Beats clip clears the selected beats of one voice to rests without moving the beats after them. Returns true when anything changed.
    /// </summary>
    public static bool CutClear(SongProject p, ScoreClipKind kind, int trackIndex, int voice, int startBar, int startCell, int endBar, int endCell)
    {
        if (trackIndex < 0 || trackIndex >= p.Tracks.Count) return false;
        var track = p.Tracks[trackIndex];
        if (endBar < startBar || (endBar == startBar && endCell >= 0 && endCell < startCell))
            (startBar, startCell, endBar, endCell) = (endBar, Math.Max(0, endCell), startBar, startCell);
        var changed = false;
        if (kind == ScoreClipKind.Bars)
        {
            for (var bar = Math.Max(0, startBar); bar <= endBar && bar < track.Measures.Count; bar++)
            {
                changed |= EmptyBar(track.Measures[bar].Cells);
                changed |= EmptyBar(track.Measures[bar].Voice2Cells);
            }
            return changed;
        }
        for (var bar = Math.Max(0, startBar); bar <= endBar && bar < track.Measures.Count; bar++)
        {
            var cells = track.Measures[bar].CellsForVoice(voice);
            if (cells.Count == 0) continue;
            var before = BarGrid.Onsets(cells);
            var cleared = new bool[cells.Count];
            for (var i = 0; i < cells.Count; i++)
            {
                var inRange = (bar > startBar || i >= startCell) && (bar < endBar || endCell < 0 || i <= endCell);
                if (!inRange || !IsBeat(cells[i])) continue;
                cells[i] = new TabCell();
                cleared[i] = true;
                changed = true;
            }
            // Beats after a cleared one keep their onsets (the grid rule places a beat at max(index, previous end)).
            var after = BarGrid.Onsets(cells);
            for (var i = 0; i < cells.Count; i++)
                if (!cleared[i] && IsBeat(cells[i]) && Math.Abs(after[i] - before[i]) > 1e-6)
                    cells[i].RhythmicPosition = before[i];
        }
        return changed;
    }

    private static bool IsBeat(TabCell cell) => cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation;
}

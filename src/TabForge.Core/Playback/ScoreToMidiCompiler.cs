using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the score-to-MIDI compile: repeat order, tempo and time signature, note events, tie merging, techniques, metronome and count-in.
// Does not own: devices, transport, threads and the UI (see the summary).
// Tests: no dedicated test class.
// Long on purpose: the compile is one pass whose partial files share its state.

/// <summary>
/// Compiles a score into an absolute-time MIDI timeline.
/// Responsibilities: repeat order, tempo/time-signature mapping, channel allocation, note events,
/// tie merging, technique-to-MIDI mapping, the metronome and the count-in.
/// It does not know about devices (beyond device ids), transport, threads or the UI.
/// </summary>
internal sealed partial class ScoreToMidiCompiler
{
    // +/-12: wide enough for 1.5-2 step bends, dive bombs and multi-fret slides (a +/-2 range capped
    // every slide or bend beyond one whole step, e.g. 1 1/2 bends and 3->7 slides sounded short).
    private const int PitchBendRangeSemitones = 12;
    // the reference's own vibrato depths: note vibrato +/-0.38 semitone, wide vibrato +/-0.94.
    private const double NormalVibratoSemitones = 0.38;
    private const double WideVibratoSemitones = 0.94;
    private const double VibratoRateHz = 5.5;
    private const double VibratoSampleMs = 20;
    private const double VibratoFadeMs = 70;
    private const double FadeInSampleMs = 40;
    private readonly SongProject _project;
    private readonly PlaybackOptions _opt;
    private readonly IReadOnlyList<int>? _playbackOrder;
    private readonly CancellationToken _cancel;   // checked once per performed bar, so a superseded compile stops within a bar
    private readonly double _speedScale;
    private readonly ScoreTimeline _timeline = new();

    private List<TrackModel> _players = new();
    private int[] _channels = Array.Empty<int>();
    private int _metronomeDevice = -1;
    private int _metronomePairId;

    /// <summary>Last emitted note on each (track, voice, string or drum piece, see <see cref="OpenKey"/>); a tied note attaches to it instead of attacking.</summary>
    private readonly Dictionary<(int track, int voice, int str), OpenNote> _open = new();
    private readonly Dictionary<(int track, int voice), int> _lastSourceBar = new();
    private readonly Dictionary<(int track, int voice), List<int>> _laneNotes = new();

    private sealed class OpenNote
    {
        public NoteEvent Note = null!;
        public int OffIndex = -1;
        /// <summary>Pitch this note slides to (0 = no slide): a tie onto that pitch continues it without re-attack.</summary>
        public int SlideTarget;
        /// <summary>The slide is a legato slide: the target note is not picked again (a tie-like continuation).</summary>
        public bool Legato;
        /// <summary>Index of the pitch-wheel reset that ends a slide held into tied notes (-1 = none yet).</summary>
        public int HoldResetIndex = -1;
        /// <summary>Index of the pitch-wheel reset that ends this note's own bend / vibrato curve (-1 = none): a tie that carries the curve on reuses it.</summary>
        public int CurveEndIndex = -1;
        /// <summary>Where the note ends as written, before dead / palm-mute / staccato cuts: a tie that follows from there is still contiguous.</summary>
        public double WrittenEndMs;
    }

    public ScoreToMidiCompiler(SongProject project, PlaybackOptions opt, IReadOnlyList<int>? playbackOrder = null, CancellationToken cancel = default)
    {
        _project = project;
        _opt = opt;
        _playbackOrder = playbackOrder;
        _cancel = cancel;
        _speedScale = 1.0 / Math.Clamp(opt.Speed <= 0 ? 1.0 : opt.Speed, 0.25, 4.0);
    }

    public ScoreTimeline Build()
    {
        var order = _playbackOrder?.ToList() ?? PlaybackOrder.Build(_project, _opt);
        if (order.Count == 0) return _timeline;

        SelectPlayersAndChannels();

        // Per-source-bar caches: the loop below visits each performed bar (repeats included), so a
        // bar's slot count and written-content length are worked out once per source bar, not per repeat.
        // The project is immutable for the whole Build, so the caches never go stale.
        var trackIndexByTrack = new Dictionary<TrackModel, int>(ReferenceEqualityComparer.Instance);
        for (var ti = 0; ti < _project.Tracks.Count; ti++) trackIndexByTrack[_project.Tracks[ti]] = ti;
        var slotsCache = new Dictionary<int, int>();
        var contentCache = new Dictionary<int, double>();

        var cursorMs = 0.0;
        if (_opt.CountIn) for (var bar = Math.Clamp(_opt.CountInBars, 1, 4); bar > 0; bar--) cursorMs += CountInMs(order[0], cursorMs);   // every count-in bar clicks
        _timeline.CountInMs = cursorMs;

        var firstBarMs = MusicTime.BarMs(_project, order[0], _speedScale);
        var firstSlots = Math.Max(1, MusicTime.BarSlots(_project, order[0]));
        var startSlot = Math.Clamp(_opt.StartCell, 0, firstSlots - 1);
        _timeline.PlayFromMs = cursorMs + FermataSpan.Warp(
            FermataTime.Spans(_project, order[0], _speedScale, MusicTime.TempoAt(_project, order[0])), startSlot * (firstBarMs / firstSlots));

        ChaseBarsBeforeStart();
        EmitChannelSetup(_timeline.PlayFromMs);
        ApplyChase();   // before any note is emitted: it appends to the setup, so no recorded event index moves

        var firstPerformedBar = true;
        // Tempo carries on in playback order until the next change (the reference semantics), so repeats and
        // jumps keep whatever tempo was last set; playback starting mid-song begins at the tempo in force there.
        var runningTempo = MusicTime.TempoAt(_project, order[0]);
        foreach (var barIndex in order)
        {
            _cancel.ThrowIfCancellationRequested();
            var measure = MusicTime.BarOf(_project, barIndex);
            if (measure?.TempoChange is { } change) runningTempo = Math.Clamp(change, 20, 400);
            var tempo = runningTempo;
            if (!slotsCache.TryGetValue(barIndex, out var slots))
            {
                slots = Math.Max(1, MusicTime.BarSlots(_project, barIndex));
                slotsCache[barIndex] = slots;
            }
            _barTempo = (measure, tempo);
            var barMs = MusicTime.OffsetMs(measure, slots, tempo, _speedScale);
            // A fermata on any track holds the beat for every track: the bar is longer by the hold and all later events move with it.
            _barHolds = FermataTime.Spans(_project, barIndex, _speedScale, tempo);
            var holdMs = FermataSpan.TotalExtraMs(_barHolds);
            runningTempo = MusicTime.TempoAfter(_project, barIndex, tempo);
            var barStart = cursorMs;
            var num = measure?.TimeSigNum ?? _project.TimeSignatureNumerator;
            var performedMs = PerformedBarMs(barIndex, slots, barMs, contentCache, out var contentSlots);
            var baseMs = performedMs;
            performedMs += holdMs;
            _timeline.Bars.Add(new ScoreBar(barIndex, barStart, barStart + performedMs,
                baseMs < barMs ? Math.Max(1, (int)Math.Ceiling(contentSlots - 0.001)) : slots, tempo, _barHolds));
            if (_opt.Metronome) EmitMetronome(barStart, baseMs < barMs ? baseMs : barMs, num);

            EmitBarOfEveryTrack(trackIndexByTrack, barIndex, barStart, barMs, slots, firstPerformedBar ? startSlot : 0);
            firstPerformedBar = false;
            cursorMs += performedMs;
        }

        _timeline.TotalMs = cursorMs;
        EmitClipNotes();
        EmitFadeInEnvelopes();
        SustainResolver.Resolve(_timeline, _channels, _opt.LetRingCapMs);
        return _timeline;
    }

    /// <summary>The tracks that sound (mute / solo respected), their channels and the metronome device.</summary>
    private void SelectPlayersAndChannels()
    {
        _players = _opt.RespectMuteSolo
            ? _project.Tracks.Where(t => MixerGroups.IsAudible(_project, t)
                && !MixerGroups.IsSilentRoute(t)).ToList()
            : _project.Tracks.ToList();
        _channels = ChannelAllocator.Assign(_project);
        _effectChannels = ChannelAllocator.AssignEffect(_project, _channels);
        _metronomeDevice = _players.FirstOrDefault(t => !t.IsAudio)?.MidiOutputDeviceId ?? -1;   // never an audio track's device
    }

    /// <summary>
    /// How long a bar lasts before fermata holds: the reference plays an incomplete bar (every track's notes end early) only as long as its content,
    /// so the next bar follows on without a gap. Complete bars and empty bars are unchanged.
    /// </summary>
    private double PerformedBarMs(int barIndex, int slots, double barMs, Dictionary<int, double> contentCache, out double contentSlots)
    {
        // Only for Guitar Pro imports: in TabForge-authored bars empty cells are intentional silence.
        if (_project.ImportedFrom is null) contentSlots = 0;
        else if (!contentCache.TryGetValue(barIndex, out contentSlots))
        {
            contentSlots = ContentSlots(barIndex);
            contentCache[barIndex] = contentSlots;
        }
        return contentSlots > 0.25 && contentSlots < slots - 0.01 ? barMs * contentSlots / slots : barMs;
    }

    /// <summary>Emits the performed bar for every sounding track; a simile bar repeats the notes of the bar one or two before it.</summary>
    private void EmitBarOfEveryTrack(Dictionary<TrackModel, int> trackIndexByTrack, int barIndex, double barStart, double barMs, int slots, int skipSlots)
    {
        foreach (var track in _players)
        {
            if (track.IsAudio) continue;   // no notation: its MIDI clips are emitted by EmitClipNotes
            if (!trackIndexByTrack.TryGetValue(track, out var trackIndex)) continue;
            if (barIndex >= track.Measures.Count) continue;
            var bar = track.Measures[barIndex];
            if (bar.SimileOneBar && barIndex > 0) { EmitMeasure(track, trackIndex, barIndex, barIndex - 1, barStart, barMs, slots, skipSlots); continue; }
            if (bar.SimileTwoBar && barIndex > 1) { EmitMeasure(track, trackIndex, barIndex, barIndex - 2, barStart, barMs, slots, skipSlots); continue; }
            EmitMeasure(track, trackIndex, barIndex, barIndex, barStart, barMs, slots, skipSlots);
        }
    }

    // ---------- bars / beats ----------

    /// <summary>
    /// Written length of bar <paramref name="barIndex"/> across all tracks (sixteenth slots): the latest
    /// end of any note or rest. 0 when no track has content; the full bar when any track fills it.
    /// </summary>
    private double ContentSlots(int barIndex) => ContentSlots(_project, barIndex);

    /// <inheritdoc cref="ContentSlots(int)"/>
    /// <remarks>Shared with the clean .gp export, which writes an imported song's short bar no longer than this.</remarks>
    internal static double ContentSlots(SongProject project, int barIndex)
    {
        var end = 0.0;
        foreach (var track in project.Tracks)
        {
            if (barIndex >= track.Measures.Count) continue;
            var m = track.Measures[barIndex];
            if (m.SimileOneBar || m.SimileTwoBar) return double.MaxValue;
            foreach (var cells in new[] { m.Cells, m.Voice2Cells })
            {
                var cursor = 0.0;
                for (var i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    if (MusicTime.HeldSlots(c) == 0) continue;   // a grace or annotation-only beat takes no bar time
                    var start = c.RhythmicPosition ?? Math.Max(i, cursor);
                    cursor = Math.Max(cursor, start + MusicTime.HeldSlots(c));
                }
                end = Math.Max(end, cursor);
            }
        }
        return end;
    }

    /// <summary>The bar being performed (its mid-bar tempo points) and the tempo it starts with.</summary>
    private (MeasureModel? Bar, int Tempo) _barTempo;

    /// <summary>Milliseconds the current note was started early for a slide in (0 = none).</summary>
    private double _slideInLeadMs;

    /// <summary>Milliseconds from the performed bar's start to a slot, through its mid-bar tempo changes.</summary>
    private double At(double slot) => FermataSpan.Warp(_barHolds, MusicTime.OffsetMs(_barTempo.Bar, slot, _barTempo.Tempo, _speedScale));

    /// <summary>The performed bar's fermata holds (null when none), in the bar's own unheld milliseconds.</summary>
    private FermataSpan[]? _barHolds;

    private void EmitMeasure(TrackModel track, int trackIndex, int playBar, int sourceBar, double barStart, double barMs, int slots, int skipSlots)
    {
        if (sourceBar < 0 || sourceBar >= track.Measures.Count) return;
        var measure = track.Measures[sourceBar];
        var slotMs = barMs / slots;
        var varying = _barTempo.Bar?.MidBarTempos is { Count: > 0 };
        var mapped = varying || _barHolds is not null;   // tempo changes or a fermata hold: times come from the map, not the linear grid

        // Voice lanes have independent rhythm and independent tie chains while sharing the same
        // performed bar and score coordinates.
        var voices = new List<(List<TabCell> Cells, int Index)> { (measure.Cells, 0) };
        if (measure.Voice2Cells.Count > 0) voices.Add((measure.Voice2Cells, 1));
        foreach (var (cells, voiceIndex) in voices)
        {
            var lane = (trackIndex, voiceIndex);
            if (_lastSourceBar.TryGetValue(lane, out var last) && sourceBar != last + 1)
                foreach (var key in _open.Keys.Where(k => k.track == trackIndex && k.voice == voiceIndex).ToList()) _open.Remove(key);
            _lastSourceBar[lane] = sourceBar;
            var starts = MusicTime.Onsets(cells);
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var start = starts[i];
                if (start >= slots) continue;   // beyond the bar's end: not played
                if (cell.Mix is { IsEmpty: false } mix)
                {
                    if (start < skipSlots) ChaseMix(mix, trackIndex);
                    else EmitMix(mix, trackIndex, barStart + (mapped ? At(start) : start * slotMs), slotMs * 4);
                }
                if (start < skipSlots || cell.Notes.Count == 0) continue;
                var slotsForCell = MusicTime.IsGraceOnly(cell) ? GraceSlots : MusicTime.ConsumeSlots(cell);   // a grace beat sounds briefly and takes no bar time
                var (swingOnset, swingDuration) = SwingFor(measure, cells, starts, i);
                var onsetSlot = start + swingOnset;
                var endSlot = onsetSlot + slotsForCell + swingDuration;
                // Constant tempo: the linear grid (unchanged). Mid-bar tempo changes: the tempo map.
                var onset = mapped ? At(onsetSlot) : onsetSlot * slotMs;
                var length = mapped ? At(endSlot) - At(onsetSlot) : (slotsForCell + swingDuration) * slotMs;
                var localSlotMs = varying ? MusicTime.SlotsToMsAt(1, TempoAtSlot(onsetSlot), _speedScale) : slotMs;
                EmitBeat(track, trackIndex, voiceIndex, playBar, i, cell, barStart + onset, length, localSlotMs);
            }
        }
    }

    private int TempoAtSlot(double slot) => MusicTime.TempoAtSlot(_barTempo.Bar, slot, _barTempo.Tempo);

    /// <summary>Slots the off-beat eighth of a swung pair moves later (eighth→2/3 of a quarter).</summary>
    private const double SwingPushSlots = 2.0 / 3.0;

    private static bool IsPlainEighth(TabCell c) =>
        c.Notes.Count > 0 && !c.IsRest && !MusicTime.IsGraceOnly(c) && c.DurationDenominator == 8 && c.Dots == 0 && !c.IsTriplet;

    private static bool IsPlainSixteenth(TabCell c) =>
        c.Notes.Count > 0 && !c.IsRest && !MusicTime.IsGraceOnly(c) && c.DurationDenominator == 16 && c.Dots == 0 && c.Tuplet.Numerator == 0;

    /// <summary>
    /// Triplet feel (swing) for a plain eighth note: the first of a beat's pair is lengthened and the
    /// second is pushed late and shortened, so the pair spans exactly one beat.
    /// </summary>
    private static (double onsetSlots, double durationSlots) SwingFor(MeasureModel measure,
        IReadOnlyList<TabCell> cells, double[] starts, int i)
    {
        var feel = TripletFeels.Effective(measure);
        if (feel == TripletFeels.None || i < 0 || i >= cells.Count) return (0, 0);
        if (feel == TripletFeels.Sixteenth)
        {
            if (!IsPlainSixteenth(cells[i])) return (0, 0);
            var pairStart = Math.Floor(starts[i] + OnsetTolerance) % 2 == 0;
            if (!HasPartner(cells, starts, starts[i] + (pairStart ? 1 : -1), IsPlainSixteenth)) return (0, 0);
            var push = 1.0 / 3.0;
            return pairStart ? (0, push) : (push, -push);
        }
        if (!IsPlainEighth(cells[i])) return (0, 0);
        var offset = (starts[i] + OnsetTolerance) % 4;   // a quarter-note beat is 4 slots; eighths sit at 0 and 2
        if (offset < 1)
            return HasPartner(cells, starts, starts[i] + 2, IsPlainEighth) ? (0, SwingPushSlots) : (0, 0);
        if (offset is >= 2 and < 3)
            return HasPartner(cells, starts, starts[i] - 2, IsPlainEighth) ? (SwingPushSlots, -SwingPushSlots) : (0, 0);
        return (0, 0);
    }

    /// <summary>Slack when matching a swing partner by onset (slots).</summary>
    private const double OnsetTolerance = 0.01;

    private static bool HasPartner(IReadOnlyList<TabCell> cells, double[] starts, double slot, Func<TabCell, bool> plain)
    {
        for (var j = 0; j < cells.Count; j++)
            if (Math.Abs(starts[j] - slot) < OnsetTolerance && plain(cells[j])) return true;
        return false;
    }

    private void EmitBeat(TrackModel track, int trackIndex, int voiceIndex, int playBar, int cellIndex, TabCell cell, double onset, double noteMs, double slotMs)
    {
        var spreadStep = 0.0;
        var arpeggio = cell.Notes.Any(n => n.Techniques.Contains("ArpeggioDown") || n.Techniques.Contains("ArpeggioUp"));
        var brush = cell.Notes.Any(n => n.Techniques.Contains("BrushDown") || n.Techniques.Contains("BrushUp") || n.Techniques.Contains("Rasgueado"));
        if (cell.Notes.Count > 1)
        {
            spreadStep = arpeggio ? 28 : brush ? 12 : 0;
            // The written stroke speed sets the spread (a 1/16 stroke steps 1/24 beat per string, 1/4 steps 1/6).
            if ((arpeggio || brush) && double.IsFinite(cell.BrushStepSlots) && cell.BrushStepSlots > 0)
                spreadStep = Math.Min(cell.BrushStepSlots * slotMs, noteMs * 0.9 / (cell.Notes.Count - 1));
        }
        // Guitar Pro 5 (the reference; Help > Stroke): the downstroke goes from the bass string to the highest string, the upstroke from
        // the highest string to the bass string. GP5 has no separate arpeggio effect, so arpeggio up/down keep the stroke direction.
        // The order follows PITCH, not string index: keyboards have no real strings (keys tracks put chord notes on octave "strings"),
        // and on a guitar high pitch = low StringIndex in normal tunings, so guitars sound exactly as before. Equal pitches keep string order.
        var upStroke = cell.Notes.Any(n => n.Techniques.Contains("ArpeggioUp") || n.Techniques.Contains("BrushUp"));
        // Single notes need no ordering: skip the OrderBy allocation entirely (same sequence).
        List<TabNote> ordered;
        if (cell.Notes.Count <= 1) ordered = cell.Notes;
        else ordered = !(arpeggio || brush) ? cell.Notes.OrderBy(n => n.StringIndex).ToList()
            : upStroke ? cell.Notes.OrderByDescending(n => n.MidiValue).ThenBy(n => n.StringIndex).ToList()
            : cell.Notes.OrderBy(n => n.MidiValue).ThenByDescending(n => n.StringIndex).ToList();

        var index = 0;
        foreach (var note in ordered)
        {
            EmitNote(track, trackIndex, voiceIndex, playBar, cellIndex, cell, note, onset + index * spreadStep, noteMs, slotMs);
            index++;
        }
    }


    /// <summary>Each track's second (effect) channel for bent / whammied notes, -1 when it has none (see <see cref="ChannelAllocator.AssignEffect"/>).</summary>
    private int[] _effectChannels = Array.Empty<int>();
    private int EffectChannelOf(int trackIndex) => trackIndex >= 0 && trackIndex < _effectChannels.Length ? _effectChannels[trackIndex] : -1;

    /// <summary>Last value each mix-table controller was set to, per track, so a ramp starts where the previous change ended.</summary>
    private readonly Dictionary<(int Track, int Controller), int> _mixLevels = new();

    /// <summary>
    /// Mix Table point: program change and controller values (volume CC7, pan CC10, chorus CC93,
    /// reverb CC91, phaser CC95, tremolo CC92), on this track or every track, immediately or ramped
    /// over the transition beats. These are ordinary channel-state events, so seeks and loops restore them.
    /// </summary>
    private void EmitMix(MixChange mix, int trackIndex, double atMs, double beatMs)
    {
        var targets = mix.AllTracks ? Enumerable.Range(0, _project.Tracks.Count) : new[] { trackIndex };
        foreach (var index in targets)
        {
            if (index < 0 || index >= _project.Tracks.Count) continue;
            var target = _project.Tracks[index];
            var device = target.MidiOutputDeviceId;
            var channel = _channels.Length > index ? _channels[index] : target.MidiChannel;
            if (channel < 0) continue;   // an audio track without a channel has nothing to adjust
            var effect = EffectChannelOf(index);
            // Mix changes reach the track's effect channel too, so bent notes keep the track's sound and level.
            void AddBoth(int statusBase, int data1, int data2, double at)
            {
                Add(device, channel, statusBase, data1, data2, at, index);
                if (effect >= 0) Add(device, effect, statusBase, data1, data2, at, index);
            }
            if (mix.Program is int program && channel != ChannelAllocator.PercussionChannel)
                AddBoth(0xC0, Math.Clamp(program, 0, MidiLimits.Max), 0, atMs);
            void Controller(int number, int? step, int current, bool pan = false)
            {
                if (step is not int value) return;
                // Mixer groups scale mix-table volume and offset its pan, like the track's own settings.
                var levels = MixerGroups.LevelsFor(_project, target);
                var end = pan ? Math.Clamp(64 + value * 8 + levels.Pan, 0, MidiLimits.Max)
                    : number == 7 ? Math.Clamp((int)Math.Round(value * 8 * levels.Volume / 100.0), 0, MidiLimits.Max)
                    : Math.Clamp(value * 8, 0, MidiLimits.Max);
                var ramp = Math.Max(0, mix.TransitionBeats);
                // Ramps used to start from the track's base level, jumping back after an earlier change.
                if (_mixLevels.TryGetValue((index, number), out var last)) current = last;
                _mixLevels[(index, number)] = end;
                if (ramp == 0) { AddBoth(0xB0, number, end, atMs); return; }
                var steps = ramp * 4;
                for (var s = 1; s <= steps; s++)
                    AddBoth(0xB0, number, (int)Math.Round(current + (end - current) * s / (double)steps), atMs + beatMs * ramp * s / steps);
            }
            Controller(7, mix.Volume, target.Volume);
            Controller(10, mix.Pan, target.Pan, pan: true);
            Controller(93, mix.Chorus, target.Chorus);
            Controller(91, mix.Reverb, 40);
            Controller(95, mix.Phaser, 0);
            Controller(92, mix.Tremolo, 0);
        }
    }

    private void Add(int device, int channel, int statusBase, int data1, int data2, double timeMs, int trackIndex)
    {
        if (_chasing) { RecordChase(channel, statusBase, data1, data2, trackIndex); return; }
        if (timeMs < 0) timeMs = 0;
        _timeline.Events.Add(new ScoreEvent
        {
            TimeMs = timeMs, DeviceId = device, Channel = channel,
            Status = statusBase | (channel & 0x0F), Data1 = data1, Data2 = data2, TrackIndex = trackIndex
        });
    }
}

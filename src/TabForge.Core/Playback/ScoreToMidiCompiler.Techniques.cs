using System.Linq;
using TabForge.Models;
using TabForge.Services;
namespace TabForge.Playback;
// Owns: the per-note output for the score: mutes, grace notes, tremolo picking, trills, bends, slides, vibrato, whammy, expression events and fade-in envelopes.
// Does not own: the bar and cell walk that calls it (ScoreToMidiCompiler.cs) or the scheduler's timing (PlaybackEngine.Scheduler.cs).
// Tests: TiedBendPitchCurveTests, LegatoChainMergeTests, TechniqueExportAuditTests.
// Long on purpose: one part of the compiler's partial class; the technique rules share the compiler's state.
internal sealed partial class ScoreToMidiCompiler
{
    private void EmitNote(TrackModel track, int trackIndex, int voiceIndex, int playBar, int cellIndex, TabCell cell, TabNote note, double onset, double noteMs, double slotMs)
    {
        var device = OutputDeviceOf(track);
        var channel = _channels.Length > trackIndex ? _channels[trackIndex] : track.MidiChannel;
        // The reference: bent / whammied notes play on the track's effect channel, so their pitch wheel never detunes the other notes of the track.
        if (ChannelAllocator.UsesEffectChannel(note, cell) && EffectChannelOf(trackIndex) is >= 0 and var effectChannel) channel = effectChannel;
        var t = note.Techniques;

        var midi = note.MidiValue;
        if (midi <= 0 && note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count)
            midi = track.PitchOf(note.StringIndex, note.Fret);
        midi = Math.Clamp(midi + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, MidiLimits.Max);
        // No harmonic transposition here: alphaTab's RealValue is already the sounding pitch.

        ApplyGraceTiming(trackIndex, voiceIndex, cell, note, slotMs, ref onset, ref noteMs, out var importedGraceNotes, out var graceDelaySlots);
        var velocity = ComputeVelocity(note, cell, t, importedGraceNotes);
        _noteArticulation = ArticulationFor(track, trackIndex, cell, note, t, channel);   // the map's message and the techniques it covers
        var length = SoundingLengthMs(cell, note, t, noteMs, slotMs);

        var writtenEndMs = onset + noteMs;   // before the cuts above: a tie continues from the written end
        _slideInLeadMs = 0;
        var key = OpenKey(track, trackIndex, voiceIndex, note, midi);
        var tied = cell.IsTied || note.Tied;
        // A grace note, a tremolo / trill or a semi harmonic is always picked itself: merging would lose its attacks.
        var mergeable = !note.IsGraceNote && !t.Contains("TremoloPick") && !t.Contains("Trill") && !t.Contains("SemiHarmonic");
        // Legato slide: one pick, then the pitch slides to the target, which is not re-attacked.
        if (mergeable && !tied && _open.TryGetValue(key, out var legatoOrigin) && legatoOrigin.Legato && !legatoOrigin.Note.Dead && legatoOrigin.SlideTarget == midi &&
            CanContinue(legatoOrigin, midi, WheelPeakSemitones(cell, note, t), onset, slotMs) && !ArticulationSuppressed(TechniqueNames.LegatoSlide))
        {
            tied = true;
            legatoOrigin.Legato = false;   // only the immediate slide destination merges (its own slide may set it again); later same-pitch notes attack
        }

        // Tie continuation: sustain the previous note of the same string instead of attacking again.
        // A tie is only valid when the destination is contiguous with the origin (the origin is still
        // sounding); anything else is a malformed/imported tie and must attack normally.
        // A palm-muted / staccato origin still merges: a Guitar Pro tie never re-attacks, whatever the origin's cut length.
        if (mergeable && tied && TryMergeTie(track, playBar, cellIndex, cell, note, midi, onset, length, writtenEndMs, tied, key, t, channel, device, trackIndex, slotMs)) return;
        if (tied && mergeable) _timeline.TieOrphans++;   // a tremolo, trill or grace destination is picked on purpose

        ApplySlideInLead(trackIndex, voiceIndex, note, t, channel, ref onset, ref length);
        var soundMidi = ApplyArticulation(track, trackIndex, channel, device, onset, ref velocity, midi);   // the instrument's articulation map, if any

        var noteEvent = new NoteEvent
        {
            UsesEffectChannel = ChannelAllocator.UsesEffectChannel(note, cell), Channel = channel,
            OnsetMs = onset, DurationMs = length, TrackIndex = trackIndex, Bar = playBar, Cell = cellIndex, VoiceIndex = voiceIndex,
            StringIndex = note.StringIndex, Fret = note.Fret, Midi = midi, Velocity = velocity,
            Dead = note.Dead, Ghost = note.Ghost, LetRing = t.Contains("LetRing"),
            FadeIn = t.Contains("FadeIn"), ChordName = cell.ChordName,
            Technique = TechniqueTag.From(t),
            Strum = TechniqueTag.StrumOf(cell.Notes),
            TechniqueMarker = TechniqueMarker.Build(note),
        };
        LaneNotes(trackIndex, voiceIndex).Add(_timeline.Notes.Count);
        _timeline.Notes.Add(noteEvent);

        // Tremolo picking / trill: several attacks inside the written value.
        var tremoloDenominator = Math.Max(cell.TremoloPickDenominator, note.TrillDurationDenominator);
        var repeats = RepetitionCount(length, slotMs, tremoloDenominator);
        if ((t.Contains("TremoloPick") || t.Contains("Trill")) && repeats > 1 && length / repeats >= 30 && !SkipUnmappedArticulation)
        {
            EmitRepeatedAttacks(noteEvent, track, playBar, cellIndex, cell, note, soundMidi, velocity, onset, length, repeats, tremoloDenominator, slotMs, key, t, channel, device, trackIndex);
            return;
        }

        EmitAttackAndExpression(noteEvent, track, playBar, cellIndex, cell, note, soundMidi, velocity, onset, length, writtenEndMs, key, t, channel, device, trackIndex);
    }

    /// <summary>
    /// Grace notes: "before the beat" is played ahead of the beat, out of the previous beat, and the principal note stays on the beat;
    /// "on the beat" sounds on the beat and the principal note starts one grace length later (its length shortened by as much).
    /// </summary>
    private void ApplyGraceTiming(int trackIndex, int voiceIndex, TabCell cell, TabNote note, double slotMs, ref double onset, ref double noteMs, out TabNote[] importedGraceNotes, out double graceDelaySlots)
    {
        importedGraceNotes = cell.Notes.Where(candidate => candidate.IsGraceNote).ToArray();
        // A before-the-beat grace needs room before the beat: at the very start of the song there is none, so it
        // plays on the beat instead (delaying the principal note like an on-the-beat grace) rather than before time zero.
        var beforeBeatFits = onset >= Math.Max(20, GraceSlots * slotMs);
        graceDelaySlots = importedGraceNotes.Select(g => GraceDelaySlots(g, beforeBeatFits)).DefaultIfEmpty(0).Max();
        // An imported on-beat grace shares the cell of its principal note, which the file already starts after the grace: the beat starts one grace earlier,
        // so imported and hand-made graces take the one rule below. (With no room before the beat the principal is simply delayed.)
        if (beforeBeatFits && graceDelaySlots > 0 && PrincipalFollowsGrace(cell))
        {
            onset -= graceDelaySlots * slotMs;
            noteMs += graceDelaySlots * slotMs;
        }
        if (note.IsGraceNote)
        {
            var graceMs = Math.Max(20, GraceSlots * slotMs);
            noteMs = graceMs;
            if (note.GraceBeforeBeat && beforeBeatFits)
            {
                onset -= graceMs;
                TrimPreviousBeat(trackIndex, voiceIndex, onset);
            }
            else if (!note.GraceBeforeBeat) onset += Math.Max(0, note.GraceOnsetOffsetSlots) * slotMs;   // stacked graces step on by their own offsets
        }
        else if (graceDelaySlots > 0)
        {
            var delayMs = graceDelaySlots * slotMs;
            onset += delayMs;
            noteMs = Math.Max(20, noteMs - delayMs);
        }
    }

    /// <summary>How long the note sounds: the written length scaled by the cell, cut for staccato, dead and palm-muted notes (unless a map covers the cut technique).</summary>
    private double SoundingLengthMs(TabCell cell, TabNote note, HashSet<string> t, double noteMs, double slotMs)
    {
        var length = noteMs * Math.Clamp(cell.SoundDurationPercent, 1, 200) / 100.0;
        if (cell.Staccato) length *= 0.5;
        // Dead notes are a short muted click; ghost notes (parenthesised) are just softer, full length.
        if (note.Dead && !ArticulationSuppressed("Dead")) length = Math.Min(length, slotMs * TechniqueInfo.MaxLengthSlotsOf("Dead"));   // 0.04 beat
        // Palm mute: a palm-muted note is capped at a quarter note (min(quarter, written)) so it
        // chugs instead of ringing the full written value.
        if (TechniqueNames.HasPalmMute(t) && !ArticulationSuppressed(TechniqueNames.PalmMute)) length = Math.Min(length, TechniqueInfo.MaxLengthSlotsOf(TechniqueNames.PalmMute) * slotMs);
        return Math.Max(20, length);
    }

    /// <summary>
    /// Slide in: the slide starts before the beat, so the note attacks a little early at the lower/higher
    /// pitch and reaches its own pitch on the beat.
    /// </summary>
    private void ApplySlideInLead(int trackIndex, int voiceIndex, TabNote note, HashSet<string> t, int channel, ref double onset, ref double length)
    {
        if (!(t.Contains("SlideInBelow") || t.Contains("SlideInAbove")) || note.IsGraceNote || channel == ChannelAllocator.PercussionChannel) return;
        if (ArticulationSuppressed("Slide")) return;   // the map's keyswitch tells it, no extra slide-in attack
        var lead = Math.Min(Math.Min(120, length * 0.25), Math.Max(0, onset));   // never starts before time zero
        onset -= lead;
        length += lead;
        _slideInLeadMs = lead;
        TrimPreviousBeat(trackIndex, voiceIndex, onset);
    }

    /// <summary>The note-on and note-off (plus a semi harmonic fundamental), the tie origin entry and the pitch and expression events of one melodic note.</summary>
    private void EmitAttackAndExpression(NoteEvent noteEvent, TrackModel track, int playBar, int cellIndex, TabCell cell, TabNote note, int midi, int velocity, double onset, double length, double writtenEndMs,
        (int track, int voice, int str) key, HashSet<string> t, int channel, int device, int trackIndex)
    {
        Add(device, channel, 0x90, midi, velocity, onset, trackIndex);
        var offIndex = _timeline.Events.Count;
        noteEvent.OffEventIndices.Add(offIndex);
        noteEvent.SustainOffEventIndices.Add(offIndex);
        Add(device, channel, 0x80, midi, 0, onset + length, trackIndex);

        // Semi harmonic: the fundamental sounds together with the harmonic.
        if (t.Contains("SemiHarmonic") && channel != ChannelAllocator.PercussionChannel && !ArticulationSuppressed(TechniqueNames.Harmonic) &&
            note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count)
        {
            var fundamental = WrittenMidi(track, cell, note);
            if (fundamental != midi)
            {
                Add(device, channel, 0x90, fundamental, velocity, onset, trackIndex);
                var fundamentalOff = _timeline.Events.Count;
                noteEvent.OffEventIndices.Add(fundamentalOff);
                noteEvent.SustainOffEventIndices.Add(fundamentalOff);
                Add(device, channel, 0x80, fundamental, 0, onset + length, trackIndex);
            }
        }

        // Any sounding note but a grace note can be the origin of a later tie on the same string.
        if (!note.IsGraceNote) _open[key] = new OpenNote { Note = noteEvent, OffIndex = offIndex, WrittenEndMs = writtenEndMs };

        if (channel != ChannelAllocator.PercussionChannel)
        {
            var target = PitchTarget(track, playBar, cellIndex, cell, note, midi, t);
            if (ArticulationSuppressed("Slide") || ArticulationSuppressed(TechniqueNames.LegatoSlide)) target = null;   // the map replaces the slide
            if (target is int slideTo && !note.IsGraceNote && _open.TryGetValue(key, out var slidOpen))
            {
                slidOpen.SlideTarget = slideTo;
                slidOpen.Legato = t.Contains("LegatoSlide");   // a grace slide is quick: it never opens a merge, the principal is still picked
            }
            var curveEnd = EmitPitchExpression(note, cell, t, midi, target, onset, length, device, channel, trackIndex, 0, -1, false, out var held);
            if (!note.IsGraceNote)
            {
                if (curveEnd >= 0) _open[key].CurveEndIndex = curveEnd;
                _open[key].HeldSemitones = held;
            }
            EmitExpressions(t, midi, onset, length, device, channel, trackIndex,
                emitLegacyGrace: ShouldEmitLegacyGrace(cell, note));
        }
    }

    private static int ComputeVelocity(TabNote note, TabCell cell, HashSet<string> t, TabNote[] importedGraceNotes)
    {
        var velocity = Dynamics.Clamp(note.Velocity);
        // A hammer-on / pull-off transition on the grace note plays the principal note legato (softer, no new pick).
        if (!note.IsGraceNote && importedGraceNotes.Any(g => g.Techniques.Contains("HOPO") || g.Techniques.Contains("HOPOOrigin")))
            velocity = Math.Max(1, (int)(velocity * TechniqueInfo.VelocityFactorOf(TechniqueNames.Hopo)));
        // A dead note keeps the full velocity (it is only very short), a ghost note and a
        // hammered-on / pulled-off note play at 80 % (95 -> 76).
        if (note.Ghost) velocity = Math.Max(1, (int)(velocity * TechniqueInfo.VelocityFactorOf("Ghost")));
        if (t.Contains(TechniqueNames.HopoDestination)) velocity = Math.Max(1, (int)(velocity * TechniqueInfo.VelocityFactorOf(TechniqueNames.HopoDestination)));
        // Palm mute mostly shortens the note (below); only a slight level drop, so palm-muted rhythm parts
        // stay level with open playing (the old 72% made them much quieter than the reference).
        if (TechniqueNames.HasPalmMute(t)) velocity = Math.Max(1, (int)Math.Round(velocity * TechniqueInfo.VelocityFactorOf(TechniqueNames.PalmMute)));
        if (t.Contains("Slap")) velocity = Math.Min(MidiLimits.Max, (int)(velocity * TechniqueInfo.VelocityFactorOf(TechniqueNames.Slap)));
        if (t.Contains("Pop")) velocity = Math.Min(MidiLimits.Max, (int)(velocity * TechniqueInfo.VelocityFactorOf(TechniqueNames.Pop)));
        if (cell.Accent == 1) velocity = Math.Min(MidiLimits.Max, velocity + 22);
        else if (cell.Accent == 2) velocity = Math.Min(MidiLimits.Max, velocity + 40);
        return velocity;
    }

    /// <summary>Merges a tied note into the still-sounding origin on its string instead of attacking again; false when no valid origin is open.</summary>
    private bool TryMergeTie(TrackModel track, int playBar, int cellIndex, TabCell cell, TabNote note, int midi, double onset, double length, double writtenEndMs, bool tied,
        (int track, int voice, int str) key, HashSet<string> t, int channel, int device, int trackIndex, double slotMs)
    {
        if (!(_open.TryGetValue(key, out var open) && (open.Note.Midi == midi || open.SlideTarget == midi) && CanContinue(open, midi, WheelPeakSemitones(cell, note, t), onset, slotMs))) return false;
        // The merged beat has no note of its own, so the origin remembers it: the score lights it when the playhead reaches it.
        (open.Note.Continuations ??= new List<NoteContinuation>()).Add(new NoteContinuation { OnsetMs = onset, EndMs = Math.Max(onset + length, writtenEndMs), Bar = playBar, Cell = cellIndex, StringIndex = note.StringIndex });
        // A tie from a dead note inherits it: the muted click stays as short as written and the tie adds no pitched sound (nor its own slide or vibrato: Guitar Pro plays the chain silent after a muted hit).
        if (open.Note.Dead)
        {
            open.WrittenEndMs = writtenEndMs;
            _timeline.TieMerges++;
            return true;
        }
        var end = onset + length;
        var previousEnd = open.Note.EndMs;
        // An imported tie destination sounds its chain's root pitch but is written on its own fret: a slide that ends on that fret lands there.
        var landed = tied && open.SlideTarget != 0 && WrittenMidi(track, cell, note) == open.SlideTarget ? open.SlideTarget : midi;
        channel = open.Note.Channel >= 0 ? open.Note.Channel : channel;   // the wheel of the channel the sustained note plays on
        if (open.OffIndex >= 0 && open.OffIndex < _timeline.Events.Count)
            _timeline.Events[open.OffIndex].TimeMs = Math.Max(_timeline.Events[open.OffIndex].TimeMs, end);
        open.Note.DurationMs = Math.Max(open.Note.DurationMs, end - open.Note.OnsetMs);
        open.WrittenEndMs = writtenEndMs;
        // Slide into a tied note (the reference "7\(5)"): the string keeps sounding at the slid-to pitch.
        // Hold the wheel at the slide offset through the tie instead of snapping back to the origin.
        if (open.SlideTarget == landed && open.Note.Midi != landed && channel != ChannelAllocator.PercussionChannel)
        {
            var hold = PitchWheelForSemitones(open.HeldSemitones);
            if (open.HoldResetIndex < 0)
            {
                AddPitchWheel(device, channel, hold, previousEnd + 0.01, trackIndex);
                open.HoldResetIndex = _timeline.Events.Count;
                AddPitchWheel(device, channel, PitchWheelForSemitones(0), end, trackIndex);
            }
            else if (open.HoldResetIndex < _timeline.Events.Count)
                _timeline.Events[open.HoldResetIndex].TimeMs = Math.Max(_timeline.Events[open.HoldResetIndex].TimeMs, end);
        }
        // The merged note keeps its own slide and pitch effects, around the pitch the string already sounds at.
        if (channel != ChannelAllocator.PercussionChannel)
        {
            var ownTarget = PitchTarget(track, playBar, cellIndex, cell, note, landed, t);
            if (ArticulationSuppressed("Slide") || ArticulationSuppressed(TechniqueNames.LegatoSlide)) ownTarget = null;
            if (ownTarget is int slideTo)
            {
                open.SlideTarget = slideTo;
                open.Legato = t.Contains("LegatoSlide");
            }
            var originReset = open.HoldResetIndex < 0 ? open.CurveEndIndex : -1;   // after a held slide the origin's reset is not the chain's wheel end
            var held = open.HeldSemitones;   // where the string is bent at the join: the continuation carries it on, never resets to centre
            // A legato slide destination is not a tie: its curve still adds the slide's pitch offset to its own bend.
            var curveEnd = EmitPitchExpression(note, cell, t, landed, ownTarget, onset, length, device, channel, trackIndex, held, originReset,
                cell.IsTied || note.Tied, out var nextHeld);
            open.HeldSemitones = nextHeld;
            if (curveEnd >= 0)
            {
                open.HoldResetIndex = -1;   // its curve ends the wheel itself; a later link holds again
                open.CurveEndIndex = curveEnd;
            }
            else if (originReset >= 0 && originReset < _timeline.Events.Count && _timeline.Events[originReset].Status == (0xE0 | (channel & 0x0F)))
                _timeline.Events[originReset].TimeMs = Math.Max(_timeline.Events[originReset].TimeMs, end);   // no curve of its own: the origin's reset moves to the chain's real end
        }
        _timeline.TieMerges++;
        return true;
    }

    /// <summary>Tremolo picking / trill: several attacks inside the written value, then the pitch and expression events.</summary>
    private void EmitRepeatedAttacks(NoteEvent noteEvent, TrackModel track, int playBar, int cellIndex, TabCell cell, TabNote note, int midi, int velocity, double onset, double length, int repeats, int tremoloDenominator, double slotMs,
        (int track, int voice, int str) key, HashSet<string> t, int channel, int device, int trackIndex)
    {
        // The written speed is the rate (a 1/16 tremolo or trill repeats every sixteenth), not the note length split evenly.
        var sub = tremoloDenominator > 0 ? slotMs * 16 / tremoloDenominator : length / repeats;
        var trillTarget = note.TrillTargetMidi > 0
            ? Math.Clamp(note.TrillTargetMidi + MixerGroups.Transpose(_project, track), 0, MidiLimits.Max)
            : Math.Min(MidiLimits.Max, midi + 2);
        for (var k = 0; k < repeats; k++)
        {
            var pitch = t.Contains("Trill") ? (k % 2 == 0 ? midi : trillTarget) : midi;
            var vel = t.Contains("TremoloPick") ? velocity * 3 / 4 : velocity;
            var s = onset + k * sub;
            Add(device, channel, 0x90, pitch, vel, s, trackIndex);
            var repeatedOffIndex = _timeline.Events.Count;
            noteEvent.OffEventIndices.Add(repeatedOffIndex);
            if (k == repeats - 1) noteEvent.SustainOffEventIndices.Add(repeatedOffIndex);
            Add(device, channel, 0x80, pitch, 0, s + sub * 0.72, trackIndex);
        }
        if (channel != ChannelAllocator.PercussionChannel)
        {
            var target = PitchTarget(track, playBar, cellIndex, cell, note, midi, t);
            if (ArticulationSuppressed("Slide") || ArticulationSuppressed(TechniqueNames.LegatoSlide)) target = null;
            EmitPitchExpression(note, cell, t, midi, target, onset, length, device, channel, trackIndex, 0, -1, false, out _);
            EmitExpressions(t, midi, onset, length, device, channel, trackIndex,
                emitLegacyGrace: ShouldEmitLegacyGrace(cell, note));
        }
        _open.Remove(key);
    }

    /// <summary>
    /// True when a note of pitch <paramref name="midi"/> starting at <paramref name="onset"/> may continue the origin instead of attacking: a dead origin only continues through a tie (its click stays),
    /// the pitch plus the note's own bend, vibrato or slide curve stays inside the wheel range, and it follows straight on from the origin's written end (not only its cut length).
    /// </summary>
    private static bool CanContinue(OpenNote origin, int midi, double curvePeakSemitones, double onset, double slotMs) =>
        Math.Abs(midi - origin.Note.Midi) + curvePeakSemitones <= PitchBendRangeSemitones &&
        onset - Math.Max(origin.Note.EndMs, origin.WrittenEndMs) <= Math.Max(5.0, slotMs * 0.5);

    /// <summary>Pitch the note moves to: a grace note's transition, else its slide; null when it stays.</summary>
    private int? PitchTarget(TrackModel track, int playBar, int cellIndex, TabCell cell, TabNote note, int midi, HashSet<string> t) =>
        GraceTransitionTarget(track, cell, note, midi, t) ?? GetSlideTarget(track, playBar, cellIndex, cell, note, midi, t);

    /// <summary>Length of a grace note: an eighth of a beat (half a sixteenth slot).</summary>
    private const double GraceSlots = 0.5;

    /// <summary>How far a grace note pushes the principal note later: only an on-the-beat grace does.</summary>
    private static double GraceDelaySlots(TabNote graceNote, bool beforeBeatFits) =>
        (graceNote.GraceBeforeBeat && beforeBeatFits) || graceNote.Dead ? 0 : GraceSlots;

    /// <summary>A cell with an imported on-beat grace (it has its written length) and its principal note: the file already starts that note after the grace.</summary>
    private static bool PrincipalFollowsGrace(TabCell cell) =>
        cell.Notes.Any(n => n is { IsGraceNote: true, GraceBeforeBeat: false, GraceDurationSlots: > 0 }) && cell.Notes.Any(n => !n.IsGraceNote);

    /// <summary>
    /// Grace-note transition: a grace note that slides or bends (grace bend) moves to the principal note's pitch.
    /// </summary>
    private int? GraceTransitionTarget(TrackModel track, TabCell cell, TabNote note, int midi, HashSet<string> t)
    {
        if (!note.IsGraceNote || note.Dead) return null;
        if (!(t.Contains("GraceBend") || t.Contains("Slide") || t.Contains("ShiftSlide") || t.Contains("LegatoSlide"))) return null;
        var principal = cell.Notes.FirstOrDefault(n => !n.IsGraceNote && n.StringIndex == note.StringIndex) ?? cell.Notes.FirstOrDefault(n => !n.IsGraceNote);
        if (principal is null) return null;
        var target = NoteMidi(track, cell, principal);
        return target != midi ? target : null;
    }

    private const int TrimLaneBeats = 3;   // the lane's latest beats searched for notes still sounding at a before-the-beat grace

    /// <summary>The timeline indexes of a lane's notes, in play order: the previous beat is found without walking other tracks' notes.</summary>
    private List<int> LaneNotes(int trackIndex, int voiceIndex)
    {
        if (!_laneNotes.TryGetValue((trackIndex, voiceIndex), out var lane)) _laneNotes[(trackIndex, voiceIndex)] = lane = new List<int>();
        return lane;
    }

    /// <summary>Ends the previous beat's notes in this voice at <paramref name="atMs"/> so a before-the-beat grace does not sound over them.</summary>
    private void TrimPreviousBeat(int trackIndex, int voiceIndex, double atMs)
    {
        if (!_laneNotes.TryGetValue((trackIndex, voiceIndex), out var lane)) return;
        var beats = 0;
        double? beatOnset = null;
        for (var i = lane.Count - 1; i >= 0; i--)
        {
            var previous = _timeline.Notes[lane[i]];
            if (beatOnset is null || Math.Abs(previous.OnsetMs - beatOnset.Value) > 1) { if (++beats > TrimLaneBeats) break; beatOnset = previous.OnsetMs; }
            if (previous.OnsetMs >= atMs - 1 || previous.EndMs <= atMs) continue;
            foreach (var index in previous.OffEventIndices)
                SustainResolver.LimitOffTime(_timeline, index, atMs);
            previous.DurationMs = atMs - previous.OnsetMs;
        }
    }

    private static bool ShouldEmitLegacyGrace(TabCell cell, TabNote note) =>
        cell.IsGrace && !cell.Notes.Any(candidate => candidate.IsGraceNote) &&
        ReferenceEquals(note, cell.Notes.FirstOrDefault());

    private int? GetSlideTarget(TrackModel track, int playBar, int cellIndex, TabCell cell, TabNote note, int midi,
        HashSet<string> techniques)
    {
        var slide = techniques.Contains("Slide") || techniques.Contains("ShiftSlide") ||
                    techniques.Contains("LegatoSlide");
        if (!slide) return null;
        var target = note.SlideTargetMidi > 0
            ? Math.Clamp(note.SlideTargetMidi + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, MidiLimits.Max)
            : NextNoteMidi(track, playBar, cellIndex, note.StringIndex);
        return target is int value && value != midi ? value : null;
    }

    private static int RepetitionCount(double length, double slotMs, int denominator)
    {
        if (denominator <= 0) return 4;
        var subdivisionMs = slotMs * 16 / denominator;
        return subdivisionMs <= 0 ? 1 : Math.Clamp((int)Math.Round(length / subdivisionMs), 1, 64);
    }

    /// <summary>Pitch of the next note on the same string (fallback slide target), or null.</summary>
    private int? NextNoteMidi(TrackModel track, int measureIndex, int cellIndex, int stringIndex)
    {
        if (measureIndex < 0 || measureIndex >= track.Measures.Count) return null;
        var measure = track.Measures[measureIndex];
        for (var c = cellIndex + 1; c < measure.Cells.Count; c++)
        {
            var n = measure.Cells[c].Notes.FirstOrDefault(x => x.StringIndex == stringIndex);
            if (n is not null) return NoteMidi(track, measure.Cells[c], n);
        }
        var next = measureIndex + 1;
        if (next >= track.Measures.Count) return null;
        foreach (var c in track.Measures[next].Cells)
            if (c.Notes.FirstOrDefault(x => x.StringIndex == stringIndex) is { } n) return NoteMidi(track, c, n);
        return null;
    }

    /// <summary>Key of the note a tie continues: its string, or its drum piece on a drum track (pieces share string 0, and a tie links the same piece).</summary>
    private static (int track, int voice, int str) OpenKey(TrackModel track, int trackIndex, int voiceIndex, TabNote note, int midi) =>
        (trackIndex, voiceIndex, track.IsDrumTrack ? DrumKeyBase + midi : note.StringIndex);

    private const int DrumKeyBase = 1000;

    /// <summary>The pitch the note is written at (its string and fret), whatever pitch the file gives a tie destination.</summary>
    private int WrittenMidi(TrackModel track, TabCell cell, TabNote note) =>
        note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
            ? Math.Clamp(track.PitchOf(note.StringIndex, note.Fret) + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, MidiLimits.Max)
            : Math.Clamp(note.MidiValue + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, MidiLimits.Max);

    /// <summary>The sounding pitch of a note of <paramref name="cell"/> (transposition and the cell's 8va shift included).</summary>
    private int NoteMidi(TrackModel track, TabCell cell, TabNote note)
    {
        var midi = note.MidiValue > 0 ? note.MidiValue
            : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count ? track.PitchOf(note.StringIndex, note.Fret)
            : note.Fret;
        return Math.Clamp(midi + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, MidiLimits.Max);
    }

    /// <summary>Non-pitch expressions and grace attacks for one melodic note.</summary>
    private void EmitExpressions(HashSet<string> t, int midi, double onset, double length, int device, int channel,
        int trackIndex, bool emitLegacyGrace)
    {
        if (t.Contains("FadeOut"))
        {
            // A ramp from full to silent across the note, then full level again the instant it ends: without the reset every
            // later note on the channel stayed silent (expression left at 0).
            var span = Math.Max(1, length);
            for (var at = 0.0; at < span; at += FadeInSampleMs)
                Add(device, channel, 0xB0, 11, (int)Math.Round(MidiLimits.Max * (1 - Math.Clamp(at / span, 0, 1))), onset + at, trackIndex);
            Add(device, channel, 0xB0, 11, MidiLimits.Max, onset + span, trackIndex);
        }
        // The mod wheel is put back to zero when the note ends, or every later note on the channel keeps the wah's vibrato.
        if (t.Contains("WahOpen") || t.Contains("WahClose"))
        {
            Add(device, channel, 0xB0, 1, TechniqueInfo.ControllerValueOf(t.Contains("WahOpen") ? TechniqueNames.WahOpen : TechniqueNames.WahClose), onset, trackIndex);
            Add(device, channel, 0xB0, 1, 0, onset + Math.Max(1, length), trackIndex);
        }
        // (Skipped when there is no room before the beat: both events would clamp to time zero and the off would sort before its on.)
        // The grace's on must stay strictly before its off (onset - 2): g >= 10 and onset - g >= 0.
        if (emitLegacyGrace && (t.Contains("GraceBefore") || t.Contains("GraceOnBeat")) && onset >= 10)
        {
            var g = Math.Min(Math.Max(10, Math.Min(90, length * 0.25)), onset);
            Add(device, channel, 0x90, Math.Max(0, midi - 1), Math.Max(1, 90 / 2), onset - g, trackIndex);
            Add(device, channel, 0x80, Math.Max(0, midi - 1), 0, onset - 2, trackIndex);
        }
    }

    /// <summary>
    /// Fade-in is scheduled after note compilation so tie destinations have already extended their
    /// origin NoteEvent. MIDI expression is stepped at a short fixed interval; it changes loudness
    /// without retriggering the sustained note.
    /// </summary>
    private void EmitFadeInEnvelopes()
    {
        var envelopes = _timeline.Notes.Where(note => note.FadeIn && note.DurationMs > 0)
            .Select(note =>
            {
                var trackIndex = note.TrackIndex;
                var track = _project.Tracks[trackIndex];
                var channel = note.Channel >= 0 ? note.Channel : _channels.Length > trackIndex ? _channels[trackIndex] : track.MidiChannel;
                return (Note: note, Track: track, Channel: channel);
            })
            .GroupBy(item => (item.Note.TrackIndex, item.Channel, item.Note.OnsetMs, item.Note.EndMs))
            .Select(group => group.First());

        foreach (var envelope in envelopes)
        {
            var start = envelope.Note.OnsetMs;
            var end = Math.Max(start + 1, envelope.Note.EndMs);
            var duration = end - start;
            Add(OutputDeviceOf(envelope.Track), envelope.Channel, 0xB0, 11, 0, start, envelope.Note.TrackIndex);
            for (var at = start + FadeInSampleMs; at < end; at += FadeInSampleMs)
            {
                var level = (int)Math.Round(MidiLimits.Max * Math.Clamp((at - start) / duration, 0, 1));
                Add(OutputDeviceOf(envelope.Track), envelope.Channel, 0xB0, 11, level, at, envelope.Note.TrackIndex);
            }
            Add(OutputDeviceOf(envelope.Track), envelope.Channel, 0xB0, 11, MidiLimits.Max, end, envelope.Note.TrackIndex);
        }
    }
}

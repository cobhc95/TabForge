using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// ScoreToMidiCompiler: note emission and technique-to-MIDI mapping (dead/ghost/palm mute, grace, slides,
// tremolo picking, trills, bends, vibrato, whammy, expressions, fade-in).
internal sealed partial class ScoreToMidiCompiler
{
    private void EmitNote(TrackModel track, int trackIndex, int voiceIndex, int playBar, int cellIndex, TabCell cell, TabNote note, double onset, double noteMs, double slotMs)
    {
        var device = track.MidiOutputDeviceId;
        var channel = _channels.Length > trackIndex ? _channels[trackIndex] : track.MidiChannel;
        // The reference: bent / whammied notes play on the track's effect channel, so their pitch wheel never detunes the other notes of the track.
        if (ChannelAllocator.UsesEffectChannel(note, cell) && EffectChannelOf(trackIndex) is >= 0 and var effectChannel) channel = effectChannel;
        var t = note.Techniques;

        var midi = note.MidiValue;
        if (midi <= 0 && note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count)
            midi = track.PitchOf(note.StringIndex, note.Fret);
        midi = Math.Clamp(midi + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, 127);
        // No harmonic transposition here: alphaTab's RealValue is already the sounding pitch.

        // Grace notes: "before the beat" is played ahead of the beat, out of the previous beat, and the
        // principal note stays on the beat; "on the beat" takes a short slice off the principal note.
        var importedGraceNotes = cell.Notes.Where(candidate => candidate.IsGraceNote).ToArray();
        // A before-the-beat grace needs room before the beat: at the very start of the song there is none, so it
        // plays on the beat instead (taking a slice off the principal note) rather than before time zero.
        var beforeBeatFits = onset >= Math.Max(20, GraceSlots * slotMs);
        var graceDelaySlots = importedGraceNotes.Select(g => GraceDelaySlots(g, beforeBeatFits)).DefaultIfEmpty(0).Max();
        if (note.IsGraceNote)
        {
            var graceMs = Math.Max(20, GraceSlots * slotMs);
            noteMs = graceMs;
            if (note.GraceBeforeBeat && beforeBeatFits)
            {
                onset -= graceMs;
                TrimPreviousBeat(trackIndex, voiceIndex, onset);
            }
        }
        else if (graceDelaySlots > 0)
        {
            var delayMs = graceDelaySlots * slotMs;
            onset += delayMs;
            noteMs = Math.Max(20, noteMs - delayMs);
        }

        var velocity = Dynamics.Clamp(note.Velocity);
        // A hammer-on / pull-off transition on the grace note plays the principal note legato (softer, no new pick).
        if (!note.IsGraceNote && importedGraceNotes.Any(g => g.Techniques.Contains("HOPO") || g.Techniques.Contains("HOPOOrigin")))
            velocity = Math.Max(1, (int)(velocity * 0.8));
        // A dead note keeps the full velocity (it is only very short), a ghost note and a
        // hammered-on / pulled-off note play at 80 % (95 -> 76).
        if (note.Ghost) velocity = Math.Max(1, (int)(velocity * 0.8));
        if (t.Contains("HOPODestination")) velocity = Math.Max(1, (int)(velocity * 0.8));
        // Palm mute mostly shortens the note (below); only a slight level drop, so palm-muted rhythm parts
        // stay level with open playing (the old 72% made them much quieter than the reference).
        if (TechniqueNames.HasPalmMute(t)) velocity = Math.Max(1, (int)Math.Round(velocity * 0.92));
        if (t.Contains("Slap")) velocity = Math.Min(127, (int)(velocity * 1.15));
        if (t.Contains("Pop")) velocity = Math.Min(127, (int)(velocity * 1.1));
        if (cell.Accent == 1) velocity = Math.Min(127, velocity + 22);
        else if (cell.Accent == 2) velocity = Math.Min(127, velocity + 40);

        var length = noteMs * Math.Clamp(cell.SoundDurationPercent, 1, 200) / 100.0;
        if (cell.Staccato) length *= 0.5;
        // Dead notes are a short muted click; ghost notes (parenthesised) are just softer, full length.
        if (note.Dead) length = Math.Min(length, slotMs * 0.16);   // 0.04 beat
        // Palm mute: a palm-muted note is capped at a quarter note (min(quarter, written)) so it
        // chugs instead of ringing the full written value.
        if (TechniqueNames.HasPalmMute(t)) length = Math.Min(length, 4 * slotMs);
        length = Math.Max(20, length);

        var key = (trackIndex, voiceIndex, note.StringIndex);
        var tied = cell.IsTied || note.Tied;
        // Legato slide: one pick, then the pitch slides to the target, which is not re-attacked.
        if (!tied && _open.TryGetValue(key, out var legatoOrigin) && legatoOrigin.Legato && legatoOrigin.SlideTarget == midi &&
            legatoOrigin.Note.Midi != midi && onset - legatoOrigin.Note.EndMs <= Math.Max(5.0, slotMs * 0.5))
            tied = true;

        // Tie continuation: sustain the previous note of the same string instead of attacking again.
        // A tie is only valid when the destination is contiguous with the origin (the origin is still
        // sounding); anything else is a malformed/imported tie and must attack normally.
        if (tied && _open.TryGetValue(key, out var open) && (open.Note.Midi == midi || open.SlideTarget == midi))
        {
            var gap = onset - open.Note.EndMs;
            var contiguous = gap <= Math.Max(5.0, slotMs * 0.5);
            if (contiguous)
            {
                var end = onset + length;
                var previousEnd = open.Note.EndMs;
                if (open.OffIndex >= 0 && open.OffIndex < _timeline.Events.Count)
                    _timeline.Events[open.OffIndex].TimeMs = Math.Max(_timeline.Events[open.OffIndex].TimeMs, end);
                open.Note.DurationMs = Math.Max(open.Note.DurationMs, end - open.Note.OnsetMs);
                // Slide into a tied note (the reference "7\(5)"): the string keeps sounding at the slid-to pitch.
                // Hold the wheel at the slide offset through the tie instead of snapping back to the origin.
                if (open.SlideTarget == midi && open.Note.Midi != midi && channel != ChannelAllocator.PercussionChannel)
                {
                    channel = open.Note.Channel >= 0 ? open.Note.Channel : channel;   // the wheel of the channel the slid note plays on
                    var hold = PitchWheelForSemitones(midi - open.Note.Midi);
                    if (open.HoldResetIndex < 0)
                    {
                        AddPitchWheel(device, channel, hold, previousEnd + 0.01, trackIndex);
                        open.HoldResetIndex = _timeline.Events.Count;
                        AddPitchWheel(device, channel, PitchWheelForSemitones(0), end, trackIndex);
                    }
                    else if (open.HoldResetIndex < _timeline.Events.Count)
                        _timeline.Events[open.HoldResetIndex].TimeMs = Math.Max(_timeline.Events[open.HoldResetIndex].TimeMs, end);
                }
                _timeline.TieMerges++;
                return;
            }
        }
        if (tied) _timeline.TieOrphans++;

        // Slide in: the slide starts before the beat, so the note attacks a little early at the lower/higher
        // pitch and reaches its own pitch on the beat.
        _slideInLeadMs = 0;
        if ((t.Contains("SlideInBelow") || t.Contains("SlideInAbove")) && !note.IsGraceNote && channel != ChannelAllocator.PercussionChannel)
        {
            var lead = Math.Min(Math.Min(120, length * 0.25), Math.Max(0, onset));   // never starts before time zero
            onset -= lead;
            length += lead;
            _slideInLeadMs = lead;
            TrimPreviousBeat(trackIndex, voiceIndex, onset);
        }

        var noteEvent = new NoteEvent
        {
            UsesEffectChannel = ChannelAllocator.UsesEffectChannel(note, cell), Channel = channel,
            OnsetMs = onset, DurationMs = length, TrackIndex = trackIndex, Bar = playBar, Cell = cellIndex, VoiceIndex = voiceIndex,
            StringIndex = note.StringIndex, Fret = note.Fret, Midi = midi, Velocity = velocity,
            Dead = note.Dead, Ghost = note.Ghost, LetRing = t.Contains("LetRing"),
            FadeIn = t.Contains("FadeIn"), ChordName = cell.ChordName,
            Technique = TechniqueTag.From(t)
        };
        _timeline.Notes.Add(noteEvent);

        // Tremolo picking / trill: several attacks inside the written value.
        var tremoloDenominator = Math.Max(cell.TremoloPickDenominator, note.TrillDurationDenominator);
        var repeats = RepetitionCount(length, slotMs, tremoloDenominator);
        if ((t.Contains("TremoloPick") || t.Contains("Trill")) && repeats > 1 && length / repeats >= 30)
        {
            // The written speed is the rate (a 1/16 tremolo or trill repeats every sixteenth), not the note length split evenly.
            var sub = tremoloDenominator > 0 ? slotMs * 16 / tremoloDenominator : length / repeats;
            var trillTarget = note.TrillTargetMidi > 0
                ? Math.Clamp(note.TrillTargetMidi + MixerGroups.Transpose(_project, track), 0, 127)
                : Math.Min(127, midi + 2);
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
                var target = GraceTransitionTarget(track, cell, note, midi, t) ?? GetSlideTarget(track, playBar, cellIndex, note, midi, t);
                EmitPitchExpression(note, cell, t, midi, target, onset, length, device, channel, trackIndex);
                EmitExpressions(t, midi, onset, length, device, channel, trackIndex,
                    emitLegacyGrace: ShouldEmitLegacyGrace(cell, note));
            }
            _open.Remove(key);
            return;
        }

        Add(device, channel, 0x90, midi, velocity, onset, trackIndex);
        var offIndex = _timeline.Events.Count;
        noteEvent.OffEventIndices.Add(offIndex);
        noteEvent.SustainOffEventIndices.Add(offIndex);
        Add(device, channel, 0x80, midi, 0, onset + length, trackIndex);

        // Semi harmonic: the fundamental sounds together with the harmonic.
        if (t.Contains("SemiHarmonic") && channel != ChannelAllocator.PercussionChannel &&
            note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count)
        {
            var fundamental = Math.Clamp(track.PitchOf(note.StringIndex, note.Fret) + MixerGroups.Transpose(_project, track) + cell.OctaveShiftSemitones, 0, 127);
            if (fundamental != midi)
            {
                Add(device, channel, 0x90, fundamental, velocity, onset, trackIndex);
                var fundamentalOff = _timeline.Events.Count;
                noteEvent.OffEventIndices.Add(fundamentalOff);
                noteEvent.SustainOffEventIndices.Add(fundamentalOff);
                Add(device, channel, 0x80, fundamental, 0, onset + length, trackIndex);
            }
        }

        // Any sounding note can be the origin of a later tie on the same string.
        _open[key] = new OpenNote { Note = noteEvent, OffIndex = offIndex };

        if (channel != ChannelAllocator.PercussionChannel)
        {
            var target = GraceTransitionTarget(track, cell, note, midi, t) ?? GetSlideTarget(track, playBar, cellIndex, note, midi, t);
            if (target is int slideTo && _open.TryGetValue(key, out var slidOpen))
            {
                slidOpen.SlideTarget = slideTo;
                slidOpen.Legato = t.Contains("LegatoSlide") && !note.IsGraceNote;   // a grace slide is quick and the principal is still picked
            }
            EmitPitchExpression(note, cell, t, midi, target, onset, length, device, channel, trackIndex);
            EmitExpressions(t, midi, onset, length, device, channel, trackIndex,
                emitLegacyGrace: ShouldEmitLegacyGrace(cell, note));
        }
    }

    /// <summary>Length of a grace note: an eighth of a beat (half a sixteenth slot).</summary>
    private const double GraceSlots = 0.5;

    /// <summary>How far a grace note pushes the principal note later: only an on-the-beat grace does.</summary>
    private static double GraceDelaySlots(TabNote graceNote, bool beforeBeatFits) =>
        (graceNote.GraceBeforeBeat && beforeBeatFits) || graceNote.Dead ? 0 : GraceSlots;

    /// <summary>
    /// Grace-note transition: a grace note that slides or bends (grace bend) moves to the principal note's pitch.
    /// </summary>
    private int? GraceTransitionTarget(TrackModel track, TabCell cell, TabNote note, int midi, HashSet<string> t)
    {
        if (!note.IsGraceNote || note.Dead) return null;
        if (!(t.Contains("GraceBend") || t.Contains("Slide") || t.Contains("ShiftSlide") || t.Contains("LegatoSlide"))) return null;
        var principal = cell.Notes.FirstOrDefault(n => !n.IsGraceNote && n.StringIndex == note.StringIndex) ?? cell.Notes.FirstOrDefault(n => !n.IsGraceNote);
        if (principal is null) return null;
        var target = NoteMidi(track, principal);
        return target != midi ? target : null;
    }

    /// <summary>Ends the previous beat's notes in this voice at <paramref name="atMs"/> so a before-the-beat grace does not sound over them.</summary>
    private void TrimPreviousBeat(int trackIndex, int voiceIndex, double atMs)
    {
        for (var i = _timeline.Notes.Count - 1; i >= 0 && i >= _timeline.Notes.Count - 16; i--)
        {
            var previous = _timeline.Notes[i];
            if (previous.TrackIndex != trackIndex || previous.VoiceIndex != voiceIndex) continue;
            if (previous.OnsetMs >= atMs - 1 || previous.EndMs <= atMs) continue;
            foreach (var index in previous.OffEventIndices)
                SustainResolver.LimitOffTime(_timeline, index, atMs);
            previous.DurationMs = atMs - previous.OnsetMs;
        }
    }

    private static bool ShouldEmitLegacyGrace(TabCell cell, TabNote note) =>
        cell.IsGrace && !cell.Notes.Any(candidate => candidate.IsGraceNote) &&
        ReferenceEquals(note, cell.Notes.FirstOrDefault());

    private int? GetSlideTarget(TrackModel track, int playBar, int cellIndex, TabNote note, int midi,
        HashSet<string> techniques)
    {
        var slide = techniques.Contains("Slide") || techniques.Contains("ShiftSlide") ||
                    techniques.Contains("LegatoSlide");
        if (!slide) return null;
        var target = note.SlideTargetMidi > 0
            ? Math.Clamp(note.SlideTargetMidi + MixerGroups.Transpose(_project, track), 0, 127)
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
            if (n is not null) return NoteMidi(track, n);
        }
        var next = measureIndex + 1;
        if (next >= track.Measures.Count) return null;
        foreach (var n in track.Measures[next].Cells.SelectMany(c => c.Notes).Where(x => x.StringIndex == stringIndex))
            return NoteMidi(track, n);
        return null;
    }

    private int NoteMidi(TrackModel track, TabNote note)
    {
        var midi = note.MidiValue > 0 ? note.MidiValue
            : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count ? track.PitchOf(note.StringIndex, note.Fret)
            : note.Fret;
        return Math.Clamp(midi + MixerGroups.Transpose(_project, track), 0, 127);
    }

    /// <summary>
    /// Combines compatible note pitch effects into one pitch-wheel stream. MIDI has one wheel per
    /// channel, so independent bend/slide/vibrato streams must be summed rather than sent as
    /// competing events that overwrite each other.
    /// </summary>
    private void EmitPitchExpression(TabNote note, TabCell cell, HashSet<string> techniques, int midi, int? slideTarget,
        double onset, double length, int device, int channel, int trackIndex)
    {
        var vibrato = techniques.Contains("Vibrato") || techniques.Contains("WideVibrato");
        var genericBend = techniques.Contains("Bend") && note.BendPoints.Count == 0;
        var tremBar = techniques.Contains("TremBar") || techniques.Contains("TremBarWide");
        var whammyPoints = cell.WhammyPoints
            .Select(point => (Fraction: Math.Clamp(point.Offset <= 1.0 ? point.Offset : point.Offset / 60.0, 0, 1),
                Semitones: Math.Clamp(point.Value / 2.0, -12.0, 12.0)))
            .OrderBy(point => point.Fraction).ToArray();
        var slideIn = techniques.Contains("SlideInBelow") || techniques.Contains("SlideInAbove");
        var slideOut = techniques.Contains("SlideOutUp") || techniques.Contains("SlideOutDown");
        if (!vibrato && !genericBend && !tremBar && !slideIn && !slideOut &&
            whammyPoints.Length == 0 && note.BendPoints.Count == 0 && slideTarget is null) return;

        var duration = Math.Max(0, length);
        var end = onset + duration;
        var times = new SortedSet<double> { onset, end };
        for (var at = onset + VibratoSampleMs; at < end; at += VibratoSampleMs) times.Add(at);
        var bendPoints = BendCurve(note);
        foreach (var point in bendPoints) times.Add(onset + duration * point.Fraction);
        foreach (var point in whammyPoints) times.Add(onset + duration * point.Fraction);
        // standard slide: hold the fretted pitch, then move to the target in the last part of the note.
        const double SlideStart = 0.55, SlideEnd = 0.95;
        if (slideTarget is not null) { times.Add(onset + duration * SlideStart); times.Add(onset + duration * SlideEnd); }
        if (genericBend) { times.Add(onset + duration * 0.35); times.Add(onset + duration * 0.9); }
        if (tremBar) times.Add(onset + duration * 0.5);
        if (slideIn) times.Add(onset + Math.Min(120, duration * 0.25));
        if (slideOut) { times.Add(onset + duration * SlideOutStart); times.Add(onset + duration * 0.97); }

        var vibratoStart = onset + Math.Min(55, duration * 0.12);
        var vibratoDepth = techniques.Contains("WideVibrato") ? WideVibratoSemitones : NormalVibratoSemitones;
        var slideInDuration = _slideInLeadMs > 0 ? _slideInLeadMs : Math.Max(1, Math.Min(120, duration * 0.25));
        const double SlideInSemitones = 3, SlideOutSemitones = 5; // The reference slides in from ~3 frets, falls/rises ~5, progressively from 25% of the note

        foreach (var at in times)
        {
            var fraction = duration <= 0 ? 1 : Math.Clamp((at - onset) / duration, 0, 1);
            var semitones = bendPoints.Length > 0
                ? BendAt(bendPoints, fraction)
                : genericBend ? GenericBendAt(fraction) : 0;
            if (whammyPoints.Length > 0) semitones += BendAt(whammyPoints, fraction);

            // A target slide, a fretted bend and a separate slide ornament can coexist; each is
            // expressed as a delta around the note's imported sounding pitch.
            if (slideTarget is int target && fraction < 1)
                semitones += (target - midi) * Math.Clamp((fraction - SlideStart) / (SlideEnd - SlideStart), 0, 1);
            if (tremBar)
            {
                if (whammyPoints.Length == 0)
                    semitones += TremBarAt(techniques, fraction);
            }
            if (techniques.Contains("SlideInBelow"))
                semitones -= SlideInSemitones * Math.Max(0, 1 - (at - onset) / slideInDuration);
            if (techniques.Contains("SlideInAbove"))
                semitones += SlideInSemitones * Math.Max(0, 1 - (at - onset) / slideInDuration);
            if (techniques.Contains("SlideOutUp"))
                semitones += SlideOutSemitones * Math.Clamp((fraction - SlideOutStart) / (1 - SlideOutStart), 0, 1);
            if (techniques.Contains("SlideOutDown"))
                semitones -= SlideOutSemitones * Math.Clamp((fraction - SlideOutStart) / (1 - SlideOutStart), 0, 1);

            if (vibrato && at < end)
            {
                var attack = Math.Clamp((at - vibratoStart) / VibratoFadeMs, 0, 1);
                var release = Math.Clamp((end - at) / VibratoFadeMs, 0, 1);
                semitones += Math.Sin(2 * Math.PI * VibratoRateHz * (at - vibratoStart) / 1000.0) *
                             vibratoDepth * Math.Min(attack, release);
            }

            // All generated curves return to the unbent sounding pitch, even when the imported
            // bend holds its peak through the written note end.
            if (Math.Abs(at - end) < 0.001) semitones = 0;
            AddPitchWheel(device, channel, PitchWheelForSemitones(semitones), at, trackIndex);
        }
    }

    /// <summary>
    /// The note's bend as (fraction of the note, semitones) points. the standard plain bend (0 at the start to the
    /// target at the end of the curve) reaches its target by the middle of the note and holds it, so it does not
    /// sound late and sluggish; curves the file draws itself (release, pre-bend, custom) keep their own points.
    /// </summary>
    internal static (double Fraction, double Semitones)[] BendCurve(TabNote note)
    {
        var points = note.BendPoints
            .Select(point => (Fraction: Math.Clamp(point.Offset <= 1.0 ? point.Offset : point.Offset / 60.0, 0, 1),
                Semitones: Math.Clamp(point.Value / 2.0, -12.0, 12.0)))
            .OrderBy(point => point.Fraction).ToArray();
        if (note.BendTypeName == "Bend" && points.Length == 2 && points[0].Fraction <= 0 && points[0].Semitones == 0 &&
            points[1].Fraction >= 0.99)
            points[1].Fraction = BendReachedAt;
        return points;
    }

    private const double BendReachedAt = 0.5;
    private const double SlideOutStart = 0.25;

    private static double BendAt((double Fraction, double Semitones)[] points, double fraction)
    {
        if (fraction < points[0].Fraction) return points[0].Fraction <= 0 ? points[0].Semitones : 0;
        for (var index = 1; index < points.Length; index++)
        {
            if (fraction > points[index].Fraction) continue;
            var previous = points[index - 1];
            var current = points[index];
            var span = current.Fraction - previous.Fraction;
            var position = span <= 0 ? 1 : (fraction - previous.Fraction) / span;
            return previous.Semitones + (current.Semitones - previous.Semitones) * position;
        }
        return points[^1].Semitones;
    }

    private static double GenericBendAt(double fraction) => fraction switch
    {
        < 0.35 => 0,
        < 0.9 => 0.5 * (1 - (fraction - 0.35) / 0.55),
        _ => 0
    };

    private static double TremBarAt(HashSet<string> techniques, double fraction)
    {
        if (techniques.Contains("TremBarDive")) return -2 * fraction;
        if (techniques.Contains("TremBarDip")) return -2 * (1 - Math.Abs(2 * fraction - 1));
        if (techniques.Contains("TremBarHold")) return -2 * Math.Min(1, fraction * 4);
        if (techniques.Contains("TremBarPredive") || techniques.Contains("TremBarPrediveDive"))
            return -2 * Math.Min(1, 0.5 + fraction * 2);
        var depth = techniques.Contains("TremBarWide") ? 1.0 : 0.5;
        return fraction < 0.5 ? -depth * (1 - fraction * 2) : 0;
    }

    private static int PitchWheelForSemitones(double semitones)
    {
        var unitsPerSemitone = 8192.0 / PitchBendRangeSemitones;
        return Math.Clamp(8192 + (int)Math.Round(semitones * unitsPerSemitone), 0, 16383);
    }

    private void AddPitchWheel(int device, int channel, int wheel, double atMs, int trackIndex) =>
        Add(device, channel, 0xE0, wheel & 0x7F, (wheel >> 7) & 0x7F, atMs, trackIndex);

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
                Add(device, channel, 0xB0, 11, (int)Math.Round(127 * (1 - Math.Clamp(at / span, 0, 1))), onset + at, trackIndex);
            Add(device, channel, 0xB0, 11, 127, onset + span, trackIndex);
        }
        // The mod wheel is put back to zero when the note ends, or every later note on the channel keeps the wah's vibrato.
        if (t.Contains("WahOpen") || t.Contains("WahClose"))
        {
            Add(device, channel, 0xB0, 1, t.Contains("WahOpen") ? 110 : 10, onset, trackIndex);
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
            Add(envelope.Track.MidiOutputDeviceId, envelope.Channel, 0xB0, 11, 0, start, envelope.Note.TrackIndex);
            for (var at = start + FadeInSampleMs; at < end; at += FadeInSampleMs)
            {
                var level = (int)Math.Round(127 * Math.Clamp((at - start) / duration, 0, 1));
                Add(envelope.Track.MidiOutputDeviceId, envelope.Channel, 0xB0, 11, level, at, envelope.Note.TrackIndex);
            }
            Add(envelope.Track.MidiOutputDeviceId, envelope.Channel, 0xB0, 11, 127, end, envelope.Note.TrackIndex);
        }
    }
}

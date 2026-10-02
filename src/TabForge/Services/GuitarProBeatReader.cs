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
using static TabForge.Services.GuitarProBarConverter;
using static TabForge.Services.GuitarProImporter;
using static TabForge.Services.GuitarProReflection;

namespace TabForge.Services;

// Owns: converting one imported beat's annotations, cell marks and note techniques into the song model.
// Does not own: bar structure and the file reading.
// Tests: TestGuitarProFiles, TestTupletImport.
/// <summary>Reads the expression of a beat and its notes: marks, techniques, slides, bends, pitch effects, mix changes and annotations.</summary>
internal static class GuitarProBeatReader
{
    /// <summary>
    /// Beat-level annotations: comment text, chord name, lyrics, fermata and grace notes. Guitar Pro
    /// stores these on the beat, so they must be read even when the beat has no notes (a comment on a
    /// rest is still content), and merged rather than overwritten when voices share a cell.
    /// </summary>
    internal static void ReadAnnotations(object beat, TabCell cell, ImportBudget budget)
    {
        ReadBeatPitchEffects(beat, cell, budget);

        var text = GetString(beat, "Text");
        if (!string.IsNullOrWhiteSpace(text)) cell.Text = Merge(cell.Text, text!, InputLimits.MaxUserTextLength);

        // Mix Table points (volume / pan / instrument automations on a beat). The very first beat's
        // values are the track's initial settings, already imported on the track itself.
        foreach (var automation in AsObjects(Get(beat, "Automations")).Take(16))
        {
            var kind = Get(automation, "Type")?.ToString();
            var value = (int)Math.Round(Convert.ToDouble(Get(automation, "Value") ?? 0, System.Globalization.CultureInfo.InvariantCulture));
            if (kind is not ("Volume" or "Balance" or "Instrument")) continue;
            cell.Mix ??= new MixChange();
            switch (kind)
            {
                case "Volume": cell.Mix.Volume = Math.Clamp(value, 0, 16); break;
                case "Balance": cell.Mix.Pan = Math.Clamp(value - 8, -8, 8); break;
                case "Instrument": cell.Mix.Program = Math.Clamp(value, 0, 127); break;
            }
        }
        if (budget.Context.RawMixes is { } rawMixes && rawMixes.TryGetValue(beat, out var raw))
        {
            cell.Mix ??= new MixChange();
            cell.Mix.TransitionBeats = Math.Clamp(raw.TransitionBeats, 0, 64);
            cell.Mix.AllTracks = raw.AllTracks;
            if (raw.Chorus >= 0) cell.Mix.Chorus = Math.Clamp(raw.Chorus, 0, 16);
            if (raw.Reverb >= 0) cell.Mix.Reverb = Math.Clamp(raw.Reverb, 0, 16);
            if (raw.Phaser >= 0) cell.Mix.Phaser = Math.Clamp(raw.Phaser, 0, 16);
            if (raw.Tremolo >= 0) cell.Mix.Tremolo = Math.Clamp(raw.Tremolo, 0, 16);
        }

        if (GetBool(beat, "HasChord", false) || Get(beat, "Chord") is not null)
        {
            var chord = Get(beat, "Chord");
            var name = chord is null ? null : GetString(chord, "Name");
            if (!string.IsNullOrWhiteSpace(name)) cell.ChordName = Merge(cell.ChordName, name!, InputLimits.MaxTitleLength);
        }

        // alphaTab exposes lyrics as a list of lines per beat; keep them all.
        if (Get(beat, "Lyrics") is IEnumerable lines)
        {
            var lyricText = new StringBuilder();
            var lineCount = 0;
            foreach (var lineValue in lines.Cast<object>().Take(InputLimits.MaxLyricsLinesPerBeat + 1))
            {
                if (++lineCount > InputLimits.MaxLyricsLinesPerBeat)
                    throw new InvalidDataException("A score beat contains too many lyric lines.");
                var line = lineValue?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(line)) continue;
                var separatorLength = lyricText.Length == 0 ? 0 : 1;
                if (lyricText.Length + separatorLength + line.Length > InputLimits.MaxLyricsLength)
                    throw new InvalidDataException("A score beat contains overlong lyrics.");
                if (separatorLength > 0) lyricText.Append('\n');
                lyricText.Append(line);
            }
            if (lyricText.Length > 0) cell.Lyrics = Merge(cell.Lyrics, lyricText.ToString(), InputLimits.MaxLyricsLength);
        }

        if (IsSet(Get(beat, "Fermata"))) cell.Fermata = true;

        var grace = Get(beat, "GraceType")?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(grace) && !grace.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            cell.IsGrace = true;
            cell.GraceBeforeBeat = grace.Equals("BeforeBeat", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Per-note marks the engraver draws once for the whole beat: accent, staccato and fades.</summary>
    internal static void ReadCellMarks(object sourceNote, object beat, TabCell cell, TabNote note)
    {
        var accent = Get(sourceNote, "Accentuated")?.ToString() ?? "";
        if (accent.Equals("Heavy", StringComparison.OrdinalIgnoreCase)) cell.Accent = 2;
        else if (accent.Equals("Normal", StringComparison.OrdinalIgnoreCase) && cell.Accent < 2) cell.Accent = 1;
        else if (GetBool(sourceNote, "Accent", false) && cell.Accent < 2) cell.Accent = 1;
        if (GetBool(sourceNote, "IsStaccato", false)) cell.Staccato = true;
        if (accent.Equals("Tenuto", StringComparison.OrdinalIgnoreCase)) cell.Tenuto = true;

        var fade = Get(beat, "Fade")?.ToString() ?? "";
        if (GetBool(beat, "FadeIn", false) || fade.Equals("FadeIn", StringComparison.OrdinalIgnoreCase)) note.Techniques.Add("FadeIn");
        else if (fade.Equals("FadeOut", StringComparison.OrdinalIgnoreCase)) note.Techniques.Add("FadeOut");
    }

    /// <summary>Appends a second annotation instead of losing it when two voices share one cell.</summary>
    internal static string Merge(string? existing, string addition, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            if (addition.Length > maximumLength) throw new InvalidDataException("The score file contains overlong text.");
            return addition;
        }
        if (existing!.Contains(addition, StringComparison.Ordinal)) return existing;
        if (existing.Length + 3L + addition.Length > maximumLength)
            throw new InvalidDataException("The score file contains overlong text.");
        return existing + " / " + addition;
    }

    internal static bool IsSet(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        var text = value.ToString();
        return !string.IsNullOrWhiteSpace(text) && text != "0" && !text.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    internal static void ReadTechniques(object sourceNote, object beat, TabNote note)
    {
        var t = note.Techniques;
        // Palm mute is a note property in every Guitar Pro format; alphaTab's beat-level flag is "any note is muted", which would mute the whole chord.
        if (GetBool(sourceNote, "IsPalmMute", false)) t.Add("PalmMute");
        if (GetBool(sourceNote, "IsDead", false)) { t.Add("Dead"); note.Dead = true; }
        if (GetBool(sourceNote, "IsGhost", false)) { t.Add("Ghost"); note.Ghost = true; }
        if (GetBool(sourceNote, "IsLetRing", false) || GetBool(beat, "IsLetRing", false)) t.Add("LetRing");
        if (GetBool(beat, "DeadSlapped", false))
        {
            t.Add("DeadSlapped");
            t.Add("Slap");
            t.Add("Dead");
            note.Dead = true;
        }
        if (GetBool(sourceNote, "IsHammerPullOrigin", false))
        {
            t.Add("HOPO");
            t.Add("HOPOOrigin");
        }
        if (GetBool(sourceNote, "IsHammerPullDestination", false))
        {
            t.Add("HOPO");
            t.Add("HOPODestination");
        }
        if (GetBool(sourceNote, "IsLeftHandTapped", false)) t.Add("LeftTap");
        if (GetBool(sourceNote, "IsTrill", false)) t.Add("Trill");
        if (GetBool(beat, "IsTremolo", false) || GetBool(sourceNote, "IsTremolo", false)) t.Add("TremoloPick");
        if (GetBool(beat, "Slap", false) || GetBool(beat, "IsSlap", false)) t.Add("Slap");
        if (GetBool(beat, "Pop", false) || GetBool(beat, "IsPop", false)) t.Add("Pop");
        // alphaTab exposes the Guitar Pro tapping technique as Beat.Tap (an enum), not IsTap.
        // Keep the older boolean probes as compatibility fallbacks for other parser versions.
        if (IsSet(Get(beat, "Tap")) || GetBool(beat, "IsTap", false) || GetBool(sourceNote, "IsTap", false))
            t.Add("Tapping");
        if (GetBool(beat, "IsAccent", false) || GetBool(sourceNote, "Accent", false))
            note.Techniques.Add("Accent");

        AddEnumTechnique(sourceNote, t, "Vibrato", "Vibrato", "Wide", "WideVibrato");
        AddEnumTechnique(beat, t, "Vibrato", "Vibrato", "Wide", "WideVibrato");
        ReadSlideTechniques(sourceNote, t);
        AddEnumTechnique(sourceNote, t, "HarmonicType", "Harmonic", "Artificial", "ArtificialHarmonic");
        AddEnumTechnique(beat, t, "BrushType", "BrushDown", "Up", "BrushUp");
        switch (Get(beat, "BrushType")?.ToString()) { case "ArpeggioDown": t.Add("ArpeggioDown"); break; case "ArpeggioUp": t.Add("ArpeggioUp"); break; }
        switch (Get(beat, "WahPedal")?.ToString()) { case "Open": t.Add("WahOpen"); break; case "Closed": t.Add("WahClose"); break; }
        AddEnumTechnique(beat, t, "PickStroke", "PickDown", "Up", "PickUp");
        // A legato slur starts on this beat (the next beat is its destination); a rasgueado is a strumming pattern, kept as "Rasgueado" (the first pattern, ii_1) or "Rasgueado" plus "Rasgueado<pattern>".
        if (GetBool(beat, "IsLegatoOrigin", false)) t.Add("Legato");
        if (GetBool(beat, "HasRasgueado", false) && Get(beat, "Rasgueado")?.ToString() is { Length: > 0 } rasgueado && rasgueado != "None") { t.Add("Rasgueado"); if (rasgueado != "Ii") t.Add("Rasgueado" + rasgueado); }
        switch (Get(beat, "GraceType")?.ToString())
        {
            case "BeforeBeat": t.Add("GraceBefore"); break;
            case "OnBeat": t.Add("GraceOnBeat"); break;
            case "BendGrace": t.Add("GraceBend"); break;
        }

        var tremoloPicking = Get(beat, "TremoloPicking");
        if (GetBool(beat, "IsTremolo", false) || IsSet(tremoloPicking)) t.Add("TremoloPick");

        // Harmonic kinds are kept distinct for display only: alphaTab's RealValue already contains the
        // harmonic sounding pitch (e.g. a fret-14 artificial harmonic is RealValue 74, fretted 62).
        switch (Get(sourceNote, "HarmonicType")?.ToString())
        {
            case "Natural": t.Add("Harmonic"); break;
            case "Artificial": t.Add("ArtificialHarmonic"); break;
            case "Pinch": t.Add("PinchHarmonic"); break;
            case "Tap": t.Add("TapHarmonic"); break;
            case "Semi": t.Add("SemiHarmonic"); break;
            case "Feedback": t.Add("FeedbackHarmonic"); break;
        }

        if (Get(sourceNote, "BendPoints") is IEnumerable bends && bends.Cast<object>().Any())
        {
            t.Add("Bend");
            note.BendTypeName = Get(sourceNote, "BendType")?.ToString() ?? "";
            note.BendStyleName = Get(sourceNote, "BendStyle")?.ToString() ?? "";
        }
        if (GetBool(sourceNote, "IsTieOrigin", false)) t.Add("Tie");

        if (GetBool(sourceNote, "IsTrill", false))
        {
            note.TrillTargetMidi = GetInt(sourceNote, "TrillValue", 0);
            var trillSpeed = Get(sourceNote, "TrillSpeed");
            note.TrillDurationDenominator = IsSet(trillSpeed) ? DurationToDenominator(trillSpeed) : 0;
        }

        var graceType = Get(beat, "GraceType")?.ToString();
        if (graceType is "BeforeBeat" or "OnBeat" or "BendGrace")
        {
            note.IsGraceNote = true;
            note.GraceBeforeBeat = graceType == "BeforeBeat";
            var playbackStart = GetInt(beat, "PlaybackStart", 0);
            var displayStart = GetInt(beat, "DisplayStart", Math.Max(0, playbackStart));
            note.GraceOnsetOffsetSlots = (playbackStart - displayStart) / (double)TicksPerSlot;
            note.GraceDurationSlots = BeatSlots(beat);
        }

        var whammyType = Get(beat, "WhammyBarType")?.ToString();
        if (GetBool(beat, "HasWhammyBar", false) || IsSet(whammyType) ||
            AsObjects(Get(beat, "WhammyBarPoints")).Any())
        {
            t.Add("TremBar");
            if (!string.IsNullOrWhiteSpace(whammyType) && !whammyType.Equals("None", StringComparison.OrdinalIgnoreCase))
                t.Add("TremBar" + whammyType);
        }
    }

    internal static void ReadBeatPitchEffects(object beat, TabCell cell, ImportBudget budget)
    {
        if (Get(beat, "WhammyBarPoints") is IEnumerable points)
        {
            var sourcePoints = points.Cast<object>().Take(InputLimits.MaxCurvePoints + 1).ToList();
            if (sourcePoints.Count > InputLimits.MaxCurvePoints)
                throw new InvalidDataException("A score beat contains too many whammy-bar points.");
            budget.AddCurvePoints(sourcePoints.Count);
            foreach (var point in sourcePoints)
            {
                var offset = GetDouble(point, "Offset", double.NaN);
                var value = GetDouble(point, "Value", double.NaN);
                if (!double.IsFinite(offset) || !double.IsFinite(value)) continue;
                if (!cell.WhammyPoints.Any(existing => Math.Abs(existing.Offset - offset) < 0.001 &&
                                                       Math.Abs(existing.Value - value) < 0.001))
                {
                    if (cell.WhammyPoints.Count >= InputLimits.MaxCurvePoints)
                        throw new InvalidDataException("A score beat contains too many whammy-bar points.");
                    cell.WhammyPoints.Add(new BendPointModel { Offset = offset, Value = value });
                }
            }
        }

        var tremolo = TremoloDenominator(beat, budget.Context.Gp3To5);
        if (tremolo > 0) cell.TremoloPickDenominator = tremolo;

        // Brush/arpeggio spread: Guitar Pro delays each following string by a third of the stroke's tick value
        // (a 1/16 stroke reads 120 ticks and steps 40 ticks per string; a 1/4 stroke 480 and 160).
        var brushTicks = GetDouble(beat, "BrushDuration", 0);
        if (double.IsFinite(brushTicks) && brushTicks > 0)
            cell.BrushStepSlots = Math.Round(brushTicks / 3.0 / TicksPerSlot, 4);
    }

    internal static void ReadSlideTechniques(object sourceNote, HashSet<string> techniques)
    {
        switch (Get(sourceNote, "SlideInType")?.ToString())
        {
            case "IntoFromBelow": techniques.Add("SlideInBelow"); break;
            case "IntoFromAbove": techniques.Add("SlideInAbove"); break;
        }

        switch (Get(sourceNote, "SlideOutType")?.ToString())
        {
            case "Shift": techniques.Add("ShiftSlide"); break;
            case "Legato": techniques.Add("LegatoSlide"); break;
            case "OutUp": techniques.Add("SlideOutUp"); break;
            case "OutDown": techniques.Add("SlideOutDown"); break;
            case "PickSlideUp": techniques.Add("PickSlideUp"); break;
            case "PickSlideDown": techniques.Add("PickSlideDown"); break;
        }
    }

    /// <summary>Reads a slide's sounding target pitch (the note the slide resolves to), if any.</summary>
    internal static void ReadSlideTarget(object sourceNote, TabNote note)
    {
        var target = Get(sourceNote, "SlideTarget");
        if (target is null) return;
        // The fretted pitch of the target: a harmonic target's sounding pitch (RealValue) is octaves above where the slide ends.
        var midi = GetInt(target, "RealValueWithoutHarmonic", GetInt(target, "RealValue", 0));
        if (midi > 0 && midi <= 127) note.SlideTargetMidi = midi;
    }

    /// <summary>Reads a bend curve (offset 0..1 of the note, value in semitones) so playback can ramp.</summary>
    internal static void ReadBendPoints(object sourceNote, TabNote note, ImportBudget budget)
    {
        if (Get(sourceNote, "BendPoints") is not IEnumerable points) return;
        var sourcePoints = points.Cast<object>().Take(InputLimits.MaxCurvePoints + 1).ToList();
        if (sourcePoints.Count > InputLimits.MaxCurvePoints)
            throw new InvalidDataException("A score note contains too many bend points.");
        budget.AddCurvePoints(sourcePoints.Count);
        foreach (var p in sourcePoints)
        {
            var offset = GetDouble(p, "Offset", double.NaN);
            var value = GetDouble(p, "Value", double.NaN);
            if (!double.IsFinite(offset) || !double.IsFinite(value)) continue;
            // a file's flat middle stretch is two points; one that starts and ends at the same offset is a single point
            if (note.BendPoints.Count > 0 && note.BendPoints[^1].Offset == offset && note.BendPoints[^1].Value == value) continue;
            note.BendPoints.Add(new BendPointModel { Offset = offset, Value = value });
        }
    }

    internal static void AddEnumTechnique(object source, HashSet<string> target, string property, string label, string matchFragment, string matchedLabel)
    {
        var value = Get(source, property);
        if (!IsSet(value)) return;
        var text = value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        target.Add(text.Contains(matchFragment, StringComparison.OrdinalIgnoreCase) ? matchedLabel : label);
    }

    // ---------- helpers ----------

}

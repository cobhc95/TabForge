using TabForge.Models;

namespace TabForge.Services;

// Owns: the paste that repeats a clip end to end with the Paste Special choices (PasteSpecialOptions) without asking.
// Does not own: the normal paste questions (PasteCommands.Paste.cs) or the dialog.
// Tests: no dedicated test class.

// Paste Special (design chunk C6): the normal paste pipeline with explicit answers and a repeat count.
public static partial class PasteCommands
{
    private const int MaxPasteSpecialEvents = 65536;

    /// <summary>
    /// Pastes <paramref name="clip"/> <see cref="PasteSpecialOptions.Repeat"/> times end to end with the explicit choices
    /// (nothing is asked and no answer is remembered). Model only; the caller runs it through <c>IEditTarget.Run</c> for one undo step.
    /// </summary>
    public static PasteOutcome PasteSpecial(SongProject p, ScoreClip clip, PasteTarget target, PasteSpecialOptions options, EditingSettings settings)
    {
        options = options.Normalized();
        if (target.TrackIndex < 0 || target.TrackIndex >= p.Tracks.Count) return new PasteOutcome { Status = "No track to paste into." };
        if (clip.Tracks.Count == 0) return new PasteOutcome { Status = ClipboardService.NotTabForgeNotesMessage };
        var repeated = Repeated(clip, options.Repeat);
        if (repeated is null) return new PasteOutcome { Status = "Too much to paste: lower the repeat count." };

        var drums = PasteQuestionInfo.Stored(settings).Drums == DrumsAnswer.DontPaste ? DrumPastePolicy.DontPaste : DrumPastePolicy.RhythmOnOneSound;
        var choice = new ResolvedPaste(options.BeatMode, OctavePolicy.KeepExactPitch, options.BarsMode, options.CopyBarSettings, drums,
            options.KeepStringAndFret ? PasteMappingMode.KeepStringAndFret : PasteMappingMode.KeepPitch,
            options.KeepStringAndFret ? 0 : options.OctaveShift * 12);
        var none = new RecommendedPasteAnswers();
        return clip.Kind == ScoreClipKind.Beats
            ? PasteBeats(p, repeated, target, settings, none, choice, options.Repeat)
            : PasteBars(p, repeated, target, settings, none, choice, options.Repeat);
    }

    /// <summary>The clip laid end to end <paramref name="times"/> times (the clip itself when 1); null when it would be too large.</summary>
    internal static ScoreClip? Repeated(ScoreClip clip, int times)
    {
        if (times <= 1) return clip;
        var copy = new ScoreClip
        {
            Format = clip.Format, Version = clip.Version, Kind = clip.Kind, SourceSongId = clip.SourceSongId,
            SourceTrackCount = clip.SourceTrackCount, SongTimeSignatureNumerator = clip.SongTimeSignatureNumerator,
            SongTimeSignatureDenominator = clip.SongTimeSignatureDenominator, SongKeySignature = clip.SongKeySignature,
            SongKeySignatureMinor = clip.SongKeySignatureMinor, StartTempo = clip.StartTempo
        };
        foreach (var source in clip.Tracks)
        {
            if ((long)source.Events.Count * times > MaxPasteSpecialEvents || (long)source.Bars.Count * times > InputLimits.MaxMeasuresPerTrack) return null;
            var length = source.Events.Count == 0 ? source.LengthSlots
                : Math.Max(source.LengthSlots, source.Events.Max(e => e.OffsetSlots + MusicTime.CellSlots(e.Cell)));
            var track = new ScoreClipTrack
            {
                SourceTrackId = source.SourceTrackId, SourceTrackIndex = source.SourceTrackIndex, Name = source.Name,
                InstrumentName = source.InstrumentName, TrackKind = source.TrackKind, IsDrums = source.IsDrums, MidiChannel = source.MidiChannel,
                StringTunings = source.StringTunings.ToList(), Capo = source.Capo, NumberOfFrets = source.NumberOfFrets,
                DrumMapPreset = source.DrumMapPreset, Voice = source.Voice, LengthSlots = length * times
            };
            for (var i = 0; i < times; i++)
            {
                foreach (var bar in source.Bars) track.Bars.Add(ProjectService.CloneMeasure(bar));
                foreach (var e in source.Events) track.Events.Add(new ScoreClipEvent { OffsetSlots = e.OffsetSlots + i * length, Cell = e.Cell.Clone() });
            }
            copy.Tracks.Add(track);
        }
        return copy;
    }
}

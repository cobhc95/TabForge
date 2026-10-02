using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// What the Paste Special dialog chooses (docs/COPY_PASTE_DESIGN.md 3.6): every answer is explicit, so no question is asked.
/// <see cref="BeatMode"/> applies to beats clips, <see cref="BarsMode"/> to bars clips.
/// </summary>
public sealed record PasteSpecialOptions(
    int Repeat = 1,
    BeatPasteMode BeatMode = BeatPasteMode.Replace,
    BarsOntoNotesAnswer BarsMode = BarsOntoNotesAnswer.Overwrite,
    int OctaveShift = 0,
    bool KeepStringAndFret = false,
    bool CopyBarSettings = true)
{
    public const int MaxRepeat = 99;
    public const int MaxOctaveShift = 2;

    /// <summary>The same choices with the repeat count in 1..99 and the octave shift in -2..+2.</summary>
    public PasteSpecialOptions Normalized() => this with
    {
        Repeat = Math.Clamp(Repeat, 1, MaxRepeat),
        OctaveShift = Math.Clamp(OctaveShift, -MaxOctaveShift, MaxOctaveShift)
    };
}

// Paste Special (design chunk C6): the normal paste pipeline with explicit answers and a repeat count.
public static partial class EditCommands
{
    private const int MaxPasteSpecialEvents = 65536;

    /// <summary>
    /// Pastes <paramref name="clip"/> <see cref="PasteSpecialOptions.Repeat"/> times end to end with the explicit choices
    /// (nothing is asked and no answer is remembered). Model only; wrap in <see cref="PasteSpecialWithUndo"/> for one undo step.
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

    /// <summary><see cref="PasteSpecial"/> as exactly one undo step (no step when nothing changed), however many copies.</summary>
    public static PasteOutcome PasteSpecialWithUndo(UndoController undo, SongProject p, ScoreClip clip, PasteTarget target,
        PasteSpecialOptions options, EditingSettings settings, out UndoCapture? capture)
    {
        capture = null;
        using var timeline = p.BeginTimelineBatch();   // one invalidation for the whole paste (see PasteWithUndo)
        var transaction = undo.BeginTransaction(p);
        PasteOutcome outcome;
        try { outcome = PasteSpecial(p, clip, target, options, settings); }
        catch { undo.Cancel(transaction); throw; }
        if (outcome.Changed) { capture = undo.Commit(transaction); p.MarkTimelineChanged(); }
        else undo.Cancel(transaction);
        return outcome;
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

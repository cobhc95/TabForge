namespace TabForge.Services;

/// <summary>The questions a paste can ask (docs/COPY_PASTE_DESIGN.md, Owner decisions Q1..Q5).</summary>
public enum PasteQuestion { BeatsOntoNotes, Octave, BarsOntoNotes, BarSettings, Drums }

/// <summary>Q1: pasting beats onto existing notes.</summary>
public enum BeatsOntoNotesAnswer { Replace, Insert }
/// <summary>Q2: pasting between instruments of different range.</summary>
public enum OctaveAnswer { KeepPitch, ShiftOctave }
/// <summary>Q3: pasting whole bars onto bars that have notes.</summary>
public enum BarsOntoNotesAnswer { Overwrite, InsertBefore, InsertAfter }
/// <summary>Q4: the copied bars carry bar settings.</summary>
public enum BarSettingsAnswer { CopySettings, KeepTarget }
/// <summary>Q5: pitched instrument onto a drum track (or the reverse).</summary>
public enum DrumsAnswer { RhythmOntoOneSound, DontPaste }

/// <summary>
/// What the user chose. Only the questions that were asked have an answer (null otherwise); the matching
/// <c>Remember*</c> flag says the "Remember my choice" box was ticked for that question.
/// </summary>
public sealed record PasteAnswers(
    BeatsOntoNotesAnswer? BeatsOntoNotes = null, bool RememberBeatsOntoNotes = false,
    OctaveAnswer? Octave = null, bool RememberOctave = false,
    BarsOntoNotesAnswer? BarsOntoNotes = null, bool RememberBarsOntoNotes = false,
    BarSettingsAnswer? BarSettings = null, bool RememberBarSettings = false,
    DrumsAnswer? Drums = null, bool RememberDrums = false)
{
    public bool IsRemembered(PasteQuestion question) => question switch
    {
        PasteQuestion.BeatsOntoNotes => RememberBeatsOntoNotes,
        PasteQuestion.Octave => RememberOctave,
        PasteQuestion.BarsOntoNotes => RememberBarsOntoNotes,
        PasteQuestion.BarSettings => RememberBarSettings,
        _ => RememberDrums,
    };

    public bool Has(PasteQuestion question) => question switch
    {
        PasteQuestion.BeatsOntoNotes => BeatsOntoNotes is not null,
        PasteQuestion.Octave => Octave is not null,
        PasteQuestion.BarsOntoNotes => BarsOntoNotes is not null,
        PasteQuestion.BarSettings => BarSettings is not null,
        _ => Drums is not null,
    };

    /// <summary>Answers for the questions present in either record (answers in <paramref name="other"/> win).</summary>
    public PasteAnswers Merge(PasteAnswers other) => new(
        other.BeatsOntoNotes ?? BeatsOntoNotes, other.BeatsOntoNotes is null ? RememberBeatsOntoNotes : other.RememberBeatsOntoNotes,
        other.Octave ?? Octave, other.Octave is null ? RememberOctave : other.RememberOctave,
        other.BarsOntoNotes ?? BarsOntoNotes, other.BarsOntoNotes is null ? RememberBarsOntoNotes : other.RememberBarsOntoNotes,
        other.BarSettings ?? BarSettings, other.BarSettings is null ? RememberBarSettings : other.RememberBarSettings,
        other.Drums ?? Drums, other.Drums is null ? RememberDrums : other.RememberDrums);
}

/// <summary>Asks the user the given questions in one dialog. Returns null when the user cancels the paste.</summary>
public interface IPasteQuestionAsker
{
    PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions);
}

/// <summary>One choice of a question; <see cref="Id"/> is the enum member name (also what the setting stores).</summary>
public sealed record PasteOption(string Id, string Label);

/// <summary>Wording, options and settings mapping of the paste questions. No WPF here, so it is testable headless.</summary>
public static class PasteQuestionInfo
{
    public const string Ask = "Ask";
    public const string AskLabel = "Ask every time";

    public static readonly IReadOnlyList<PasteQuestion> All = Enum.GetValues<PasteQuestion>();

    public static string Title(PasteQuestion q) => q switch
    {
        PasteQuestion.BeatsOntoNotes => "Pasting beats onto existing notes",
        PasteQuestion.Octave => "Pasting between instruments of different range",
        PasteQuestion.BarsOntoNotes => "Pasting whole bars onto bars that have notes",
        PasteQuestion.BarSettings => "The copied bars carry bar settings (time signature, key, tempo and similar)",
        _ => "Pasting a pitched instrument onto a drum track (or the reverse)",
    };

    /// <summary>The options, recommended one first.</summary>
    public static IReadOnlyList<PasteOption> Options(PasteQuestion q) => q switch
    {
        PasteQuestion.BeatsOntoNotes => new PasteOption[]
        {
            new(nameof(BeatsOntoNotesAnswer.Replace), "Replace the notes at the cursor"),
            new(nameof(BeatsOntoNotesAnswer.Insert), "Insert and push the following notes along"),
        },
        PasteQuestion.Octave => new PasteOption[]
        {
            new(nameof(OctaveAnswer.KeepPitch), "Keep the exact pitch"),
            new(nameof(OctaveAnswer.ShiftOctave), "Shift by an octave automatically"),
        },
        PasteQuestion.BarsOntoNotes => new PasteOption[]
        {
            new(nameof(BarsOntoNotesAnswer.Overwrite), "Overwrite"),
            new(nameof(BarsOntoNotesAnswer.InsertBefore), "Insert before"),
            new(nameof(BarsOntoNotesAnswer.InsertAfter), "Insert after"),
        },
        PasteQuestion.BarSettings => new PasteOption[]
        {
            new(nameof(BarSettingsAnswer.CopySettings), "Copy the bar settings"),
            new(nameof(BarSettingsAnswer.KeepTarget), "Keep the target's settings"),
        },
        _ => new PasteOption[]
        {
            new(nameof(DrumsAnswer.RhythmOntoOneSound), "Paste the rhythm onto one drum sound"),
            new(nameof(DrumsAnswer.DontPaste), "Don't paste"),
        },
    };

    public static string SettingKey(PasteQuestion q) => q switch
    {
        PasteQuestion.BeatsOntoNotes => "editing.paste.beats",
        PasteQuestion.Octave => "editing.paste.octave",
        PasteQuestion.BarsOntoNotes => "editing.paste.bars",
        PasteQuestion.BarSettings => "editing.paste.barsettings",
        _ => "editing.paste.drums",
    };

    /// <summary>The stored value ("Ask" or an option id) of a question.</summary>
    public static string Get(EditingSettings s, PasteQuestion q) => q switch
    {
        PasteQuestion.BeatsOntoNotes => s.PasteBeatsOntoNotes,
        PasteQuestion.Octave => s.PasteOctave,
        PasteQuestion.BarsOntoNotes => s.PasteBarsOntoNotes,
        PasteQuestion.BarSettings => s.PasteBarSettings,
        _ => s.PasteDrums,
    };

    /// <summary>Stores a value; anything that is not one of the question's option ids becomes "Ask".</summary>
    public static void Set(EditingSettings s, PasteQuestion q, string? value)
    {
        var v = Normalize(q, value);
        switch (q)
        {
            case PasteQuestion.BeatsOntoNotes: s.PasteBeatsOntoNotes = v; break;
            case PasteQuestion.Octave: s.PasteOctave = v; break;
            case PasteQuestion.BarsOntoNotes: s.PasteBarsOntoNotes = v; break;
            case PasteQuestion.BarSettings: s.PasteBarSettings = v; break;
            default: s.PasteDrums = v; break;
        }
    }

    public static string Normalize(PasteQuestion q, string? value) =>
        Options(q).FirstOrDefault(o => string.Equals(o.Id, value, StringComparison.OrdinalIgnoreCase))?.Id ?? Ask;

    /// <summary>The remembered answers (questions set to "Ask" have no answer).</summary>
    public static PasteAnswers Stored(EditingSettings s) => FromIds(q => Get(s, q), q => true);

    /// <summary>Writes the answers whose "Remember" flag is set (only for questions in <paramref name="asked"/>).</summary>
    public static void Remember(EditingSettings s, PasteAnswers answers, IEnumerable<PasteQuestion> asked)
    {
        foreach (var q in asked)
            if (answers.Has(q) && answers.IsRemembered(q)) Set(s, q, IdOf(answers, q));
    }

    /// <summary>The option id chosen for a question, or null when it has no answer.</summary>
    public static string? IdOf(PasteAnswers a, PasteQuestion q) => q switch
    {
        PasteQuestion.BeatsOntoNotes => a.BeatsOntoNotes?.ToString(),
        PasteQuestion.Octave => a.Octave?.ToString(),
        PasteQuestion.BarsOntoNotes => a.BarsOntoNotes?.ToString(),
        PasteQuestion.BarSettings => a.BarSettings?.ToString(),
        _ => a.Drums?.ToString(),
    };

    /// <summary>Builds answers from option ids per question (null / "Ask" / unknown: no answer) with a remember flag per question.</summary>
    public static PasteAnswers FromIds(Func<PasteQuestion, string?> id, Func<PasteQuestion, bool> remember)
    {
        T? Pick<T>(PasteQuestion q) where T : struct, Enum =>
            Enum.TryParse<T>(Normalize(q, id(q)), out var v) && Normalize(q, id(q)) != Ask ? v : null;
        return new PasteAnswers(
            Pick<BeatsOntoNotesAnswer>(PasteQuestion.BeatsOntoNotes), remember(PasteQuestion.BeatsOntoNotes),
            Pick<OctaveAnswer>(PasteQuestion.Octave), remember(PasteQuestion.Octave),
            Pick<BarsOntoNotesAnswer>(PasteQuestion.BarsOntoNotes), remember(PasteQuestion.BarsOntoNotes),
            Pick<BarSettingsAnswer>(PasteQuestion.BarSettings), remember(PasteQuestion.BarSettings),
            Pick<DrumsAnswer>(PasteQuestion.Drums), remember(PasteQuestion.Drums));
    }

    /// <summary>The questions of <paramref name="needed"/> that are not yet remembered in the settings, in display order.</summary>
    public static List<PasteQuestion> StillToAsk(EditingSettings s, IEnumerable<PasteQuestion> needed) =>
        needed.Distinct().Where(q => Get(s, q) == Ask).OrderBy(q => (int)q).ToList();
}

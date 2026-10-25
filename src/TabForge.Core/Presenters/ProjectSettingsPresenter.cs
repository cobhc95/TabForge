// Owns: the project settings dialog's logic without UI types: the key and time-signature choices, the checks on tempo and beats per bar, the edited values as a result, the read-only summary (tracks, bars, sections, approximate length) and the list of pending changes against the song as it was opened.
// Does not own: the window and its tabs, the text boxes and combo boxes, the discard prompt, or applying the result to the song with undo (the caller does that).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>Edited values from the project settings dialog; the caller applies them with undo.</summary>
public sealed record ProjectSettingsResult(
    string Title, string Subtitle, string Artist, string Album,
    string MusicAuthor, string LyricsAuthor, string TabAuthor, string Copyright,
    int Tempo, int TimeSigNum, int TimeSigDenom, int KeySignature, bool KeyMinor, bool GrayInactiveVoice,
    string Instructions, string Notice, string Lyrics);

/// <summary>
/// The dialog's raw editor values: text boxes as typed, combo box indexes (<see cref="SignatureChoices.Denominators"/>,
/// <see cref="SignatureChoices.KeyNames"/>) and check boxes.
/// </summary>
public sealed record ProjectSettingsDraft(
    string Title, string Subtitle, string Artist, string Album,
    string MusicAuthor, string LyricsAuthor, string TabAuthor, string Copyright,
    string TempoText, string BeatsPerBarText, int DenominatorIndex, int KeyIndex, bool KeyMinor, bool GrayInactiveVoice,
    string Instructions, string Notice, string Lyrics);

/// <summary>Which editor a problem belongs to.</summary>
public enum ProjectSettingsField { Tempo, BeatsPerBar }

/// <summary>Why a draft cannot be applied: the message to show and the field to focus.</summary>
public sealed record ProjectSettingsProblem(string Message, ProjectSettingsField Field);

/// <summary>
/// Project settings state. Built from the song's values as opened (<paramref name="original"/>) plus its track, bar and section
/// counts. Thread: UI thread only (it is driven by the project settings window).
/// </summary>
public sealed class ProjectSettingsPresenter
{
    /// <summary>The key index that means C major (no sharps or flats); <see cref="SignatureChoices.KeyNames"/> is ordered from seven flats to seven sharps.</summary>
    private const int KeyIndexOffset = 7;
    private const int MinTempo = 20;
    private const int MaxTempo = 400;
    private const int MaxBeatsPerBar = 32;

    private readonly ProjectSettingsResult _original;

    public ProjectSettingsPresenter(ProjectSettingsResult original, int trackCount, int barCount, int sectionCount)
    {
        _original = original;
        TrackCount = trackCount;
        BarCount = barCount;
        SectionCount = sectionCount;
    }

    /// <summary>The number of tracks in the song.</summary>
    public int TrackCount { get; }

    /// <summary>The number of bars in the longest track.</summary>
    public int BarCount { get; }

    /// <summary>The number of sections (markers) in the song.</summary>
    public int SectionCount { get; }

    /// <summary>The approximate length, "m:ss", at the starting tempo and time signature without repeats; "—" when it is zero.</summary>
    public string LengthText
    {
        get
        {
            var beatsPerBar = _original.TimeSigNum * 4.0 / Math.Max(1, _original.TimeSigDenom);
            var seconds = _original.Tempo > 0 ? BarCount * beatsPerBar * 60.0 / _original.Tempo : 0;
            return seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss") : "—";
        }
    }

    /// <summary>The <see cref="SignatureChoices.KeyNames"/> index for a key signature in sharps (positive) or flats (negative), clamped to the list.</summary>
    public static int KeyIndexOf(int keySignature) => Math.Clamp(keySignature + KeyIndexOffset, 0, SignatureChoices.KeyNames.Count - 1);

    /// <summary>The first problem that stops the draft being applied, or null when it can be applied.</summary>
    public ProjectSettingsProblem? FindProblem(ProjectSettingsDraft draft)
    {
        if (!int.TryParse(draft.TempoText.Trim(), out var bpm) || bpm is < MinTempo or > MaxTempo)
            return new ProjectSettingsProblem($"Tempo must be a whole number from {MinTempo} to {MaxTempo}.", ProjectSettingsField.Tempo);
        if (!int.TryParse(draft.BeatsPerBarText.Trim(), out var beats) || beats is < 1 or > MaxBeatsPerBar)
            return new ProjectSettingsProblem($"Beats per bar must be from 1 to {MaxBeatsPerBar}.", ProjectSettingsField.BeatsPerBar);
        return null;
    }

    /// <summary>The edited values with text trimmed. Call only when <see cref="FindProblem"/> returned null.</summary>
    public ProjectSettingsResult Build(ProjectSettingsDraft draft) => new(
        draft.Title.Trim(), draft.Subtitle.Trim(), draft.Artist.Trim(), draft.Album.Trim(),
        draft.MusicAuthor.Trim(), draft.LyricsAuthor.Trim(), draft.TabAuthor.Trim(), draft.Copyright.Trim(),
        int.Parse(draft.TempoText.Trim()), int.Parse(draft.BeatsPerBarText.Trim()), SignatureChoices.Denominators[draft.DenominatorIndex],
        draft.KeyIndex - KeyIndexOffset, draft.KeyMinor, draft.GrayInactiveVoice,
        draft.Instructions, draft.Notice, draft.Lyrics);

    /// <summary>One line per field the draft changes from the song as opened ("Title: A → B"), in dialog order.</summary>
    public List<string> PendingChanges(ProjectSettingsDraft draft)
    {
        var list = new List<string>();
        void Diff(string label, string before, string after)
        {
            if (!string.Equals(before ?? "", after, StringComparison.Ordinal)) list.Add($"{label}: {(string.IsNullOrEmpty(before) ? "(empty)" : before)} → {(string.IsNullOrEmpty(after) ? "(empty)" : after)}");
        }
        var o = _original;
        Diff("Title", o.Title, draft.Title.Trim()); Diff("Subtitle", o.Subtitle, draft.Subtitle.Trim());
        Diff("Artist", o.Artist, draft.Artist.Trim()); Diff("Album", o.Album, draft.Album.Trim());
        Diff("Music by", o.MusicAuthor, draft.MusicAuthor.Trim()); Diff("Words by", o.LyricsAuthor, draft.LyricsAuthor.Trim());
        Diff("Tabbed by", o.TabAuthor, draft.TabAuthor.Trim()); Diff("Copyright", o.Copyright, draft.Copyright.Trim());
        Diff("Tempo", o.Tempo.ToString(), draft.TempoText.Trim());
        Diff("Time signature", $"{o.TimeSigNum}/{o.TimeSigDenom}", $"{draft.BeatsPerBarText.Trim()}/{SignatureChoices.Denominators[draft.DenominatorIndex]}");
        Diff("Key", SignatureChoices.KeyNames[KeyIndexOf(o.KeySignature)] + (o.KeyMinor ? " minor" : " major"),
            SignatureChoices.KeyNames[draft.KeyIndex] + (draft.KeyMinor ? " minor" : " major"));
        if (o.GrayInactiveVoice != draft.GrayInactiveVoice) list.Add($"Grey inactive voice: {(draft.GrayInactiveVoice ? "On" : "Off")}");
        if ((o.Instructions ?? "") != draft.Instructions) list.Add("Instructions edited");
        if ((o.Notice ?? "") != draft.Notice) list.Add("Notice edited");
        if ((o.Lyrics ?? "") != draft.Lyrics) list.Add("Lyrics edited");
        return list;
    }
}

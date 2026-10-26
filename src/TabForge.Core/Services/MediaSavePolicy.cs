using System.Runtime.CompilerServices;
using TabForge.Documents;

namespace TabForge.Services;

/// <summary>The "External media when saving" setting.</summary>
public enum ExternalMediaChoice { Ask, AlwaysCopy, NeverCopy }

/// <summary>What a save does about external media.</summary>
public enum MediaSaveAction { Nothing, Ask, Copy }

// Owns: when a save asks about (or does) copying external media: the setting's text, the trigger rules, and which files the song already
//   knew at its last save or open (so only a newly added file asks again).
// Does not own: the plan (MediaCollectPlan), the dialogs, the setting's storage (GeneralSettings).
// Tests: TabForge.Avalonia.Tests.MediaSavePolicyTests.
/// <summary>The save-prompt trigger rules and the per-song memory of known media files.</summary>
public sealed class MediaSavePolicy
{
    public const string AskText = "Ask", AlwaysText = "Always copy", NeverText = "Never copy";
    public static string[] ChoiceTexts => new[] { AskText, AlwaysText, NeverText };

    public static ExternalMediaChoice Parse(string? text) =>
        string.Equals(text, AlwaysText, StringComparison.OrdinalIgnoreCase) ? ExternalMediaChoice.AlwaysCopy
        : string.Equals(text, NeverText, StringComparison.OrdinalIgnoreCase) ? ExternalMediaChoice.NeverCopy : ExternalMediaChoice.Ask;

    public static string TextOf(ExternalMediaChoice choice) => choice switch { ExternalMediaChoice.AlwaysCopy => AlwaysText, ExternalMediaChoice.NeverCopy => NeverText, _ => AskText };

    /// <summary>
    /// Nothing external: nothing. Never: nothing. Always: copy. Ask: only on the first save or a Save As, or when a file was added that the song did not know
    /// at its last save (or open); every other save is silent.
    /// </summary>
    public static MediaSaveAction Decide(ExternalMediaChoice choice, int externalCount, bool firstSaveOrSaveAs, bool newExternalFile)
    {
        if (externalCount <= 0 || choice == ExternalMediaChoice.NeverCopy) return MediaSaveAction.Nothing;
        if (choice == ExternalMediaChoice.AlwaysCopy) return MediaSaveAction.Copy;
        return firstSaveOrSaveAs || newExternalFile ? MediaSaveAction.Ask : MediaSaveAction.Nothing;
    }

    // Key: the document (weak, so a closed song drops its entry); value: full paths known at the last save or open; replaced whole at each save, so it cannot grow.
    private readonly ConditionalWeakTable<SongDocument, HashSet<string>> _known = new();

    /// <summary>Remembers the media files the song names now, unless it already remembers some (call when the song is opened or created).</summary>
    public void Observe(SongDocument document)
    {
        if (!_known.TryGetValue(document, out _)) _known.Add(document, Names(document));
    }

    /// <summary>The song was saved: its current files are known now (a file the person chose to keep linked is not asked about again).</summary>
    public void Saved(SongDocument document) => _known.AddOrUpdate(document, Names(document));

    /// <summary>True when one of <paramref name="externalSources"/> was not known at the last save or open.</summary>
    public bool HasNewFile(SongDocument document, IEnumerable<string> externalSources) =>
        !_known.TryGetValue(document, out var known) || externalSources.Any(s => !known.Contains(s));

    private static HashSet<string> Names(SongDocument document)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in MediaStoredPaths.AudioClips(document.Project))
            if (MediaStoredPaths.Resolve(clip.File, document.Media.BaseDirectory) is { } full) set.Add(full);
        return set;
    }
}

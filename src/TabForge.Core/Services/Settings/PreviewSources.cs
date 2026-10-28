namespace TabForge.Services;

// Owns: the three choices of "Preview notes with" and how they sit on AudioSettings (PreviewNotes = on/off, PreviewWith = the sound when on).
// Does not own: the sound itself (the host's NotePreviewSound) or the settings row (SettingsCatalog.Audio).
// Tests: TestSettingsFileSplitSnapshots.
public static class PreviewSources
{
    public const string Track = "Track instrument";
    public const string Builtin = "Built-in synth";
    public const string Off = "Off";
    public static string[] All => new[] { Track, Builtin, Off };

    /// <summary>The choice the settings hold; a file saved with the old switch off (PreviewNotes false) reads as Off.</summary>
    public static string Get(AudioSettings audio) => !audio.PreviewNotes ? Off : audio.PreviewWith == Builtin ? Builtin : Track;

    /// <summary>Stores a choice; anything unknown means the track's instrument.</summary>
    public static void Set(AudioSettings audio, string choice)
    {
        audio.PreviewNotes = choice != Off;
        if (choice != Off) audio.PreviewWith = choice == Builtin ? Builtin : Track;
    }
}

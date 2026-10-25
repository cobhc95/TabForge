namespace TabForge.Playback;

// Owns: reading the count-in sound setting ("accent,click" note numbers, or a preset name) into the accent and click notes the engine plays.
// Does not own: the stored setting (AudioSettings.CountInSound), the sound presets (the metronome preset menu), the click output (PlaybackEngine).
// Tests: CountInSoundNotesTests.
public static class CountInSoundNotes
{
    /// <summary>The notes of a custom "accent,click" setting; (-1, -1) for a preset name or anything else, which keeps the metronome's own notes.</summary>
    public static (int AccentNote, int ClickNote) Parse(string sound)
    {
        var parts = sound.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var accent) && int.TryParse(parts[1], out var click)) return (accent, click);
        return (-1, -1);
    }
}

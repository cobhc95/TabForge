using System.Collections.Generic;
using System.Linq;

namespace TabForge.Services;

// Owns: the click sound presets the metronome and count-in settings offer (the accent and regular click note pairs, and the text each count-in
//     sound is stored as), the preset a stored sound maps to, and writing a chosen preset into the audio settings.
// Does not own: the settings popups (Avalonia Views/TransportPopups), the notes the engine plays (PlaybackOptions), the settings file (AppSettingsStore).
// Tests: TabForge.Avalonia.Tests.TransportSettingsTests.
// Threads: the tables are immutable; any thread may read them.
public static class ClickSoundPresets
{
    /// <summary>The note value of the custom entry: no preset notes, so choosing it leaves the settings' notes as they are.</summary>
    public const int CustomNote = -1;

    /// <summary>The stored count-in sound that follows the metronome's notes.</summary>
    public const string SameAsMetronome = "same";

    /// <summary>A metronome click sound: its label and its accent and regular click MIDI notes.</summary>
    public sealed record MetronomeSound(string Label, int Accent, int Click);

    /// <summary>A count-in click sound: its label and the text the audio settings store for it.</summary>
    public sealed record CountInSound(string Label, string Stored);

    /// <summary>The metronome sounds in menu order; the last entry is the custom one.</summary>
    public static readonly IReadOnlyList<MetronomeSound> Metronome = new[]
    {
        new MetronomeSound("Classic metronome", 33, 34),
        new MetronomeSound("Woodblock", 76, 77),
        new MetronomeSound("Side stick", 37, 37),
        new MetronomeSound("Hand clap", 39, 39),
        new MetronomeSound("Custom MIDI notes", CustomNote, CustomNote),
    };

    /// <summary>The count-in sounds in menu order.</summary>
    public static readonly IReadOnlyList<CountInSound> CountIn = new[]
    {
        new CountInSound("Same as metronome", SameAsMetronome),
        new CountInSound("Woodblock", "76,77"),
        new CountInSound("Side stick", "37,37"),
        new CountInSound("Hand clap", "39,39"),
        new CountInSound("Cowbell", "56,56"),
        new CountInSound("Classic metronome", "33,34"),
    };

    /// <summary>The metronome sound whose notes the settings hold; the custom entry when no preset matches them.</summary>
    public static int MetronomeIndexOf(TabForge.Services.AudioSettings audio)
    {
        var index = Metronome.ToList().FindIndex(sound => sound.Accent == audio.MetronomeAccent && sound.Click == audio.MetronomeClick);
        return index < 0 ? Metronome.Count - 1 : index;
    }

    /// <summary>The count-in sound the settings name; the first entry when the stored text matches none.</summary>
    public static int CountInIndexOf(TabForge.Services.AudioSettings audio)
    {
        var index = CountIn.ToList().FindIndex(sound => sound.Stored == audio.CountInSound);
        return index < 0 ? 0 : index;
    }

    /// <summary>Writes a metronome sound's notes into the settings; the custom entry changes nothing.</summary>
    public static void ApplyMetronome(TabForge.Services.AudioSettings audio, int index)
    {
        var sound = Metronome[index];
        if (sound.Accent == CustomNote) return;
        audio.MetronomeAccent = sound.Accent;
        audio.MetronomeClick = sound.Click;
    }

    /// <summary>Stores a count-in sound in the settings.</summary>
    public static void ApplyCountIn(TabForge.Services.AudioSettings audio, int index) => audio.CountInSound = CountIn[index].Stored;
}

using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the click sound presets the metronome and count-in settings offer: a stored note pair maps to its preset, an unknown pair maps to the
/// custom entry, and a chosen preset writes its notes back.
/// Tests: ClickSoundPresets.
/// </summary>
public class TransportSettingsTests
{
    [Fact]
    public void ClickSoundPresetsMapStoredNotesAndWriteThemBack()
    {
        var audio = new AudioSettings { MetronomeAccent = 76, MetronomeClick = 77 };
        Assert.Equal("Woodblock", ClickSoundPresets.Metronome[ClickSoundPresets.MetronomeIndexOf(audio)].Label);

        audio.MetronomeAccent = 60;
        var custom = ClickSoundPresets.Metronome.Count - 1;
        Assert.Equal(custom, ClickSoundPresets.MetronomeIndexOf(audio));

        ClickSoundPresets.ApplyMetronome(audio, custom);
        Assert.Equal(60, audio.MetronomeAccent);

        ClickSoundPresets.ApplyMetronome(audio, 1);
        Assert.Equal((76, 77), (audio.MetronomeAccent, audio.MetronomeClick));
    }
}

using System;
using System.Linq;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the rows of the metronome and count-in settings popups (the WPF MetronomeSettingsPopup and CountInSettingsPopup): the volumes, the click
//     sound, the boost, the subdivision, the count-in length and the count-in checks with their helper texts. Every change writes the audio
//     settings at once and calls the change callback.
// Does not own: the popup's look and opening (TransportPopupFrame), the sound presets (ClickSoundPresets), saving and the engine (the callback).
// Tests: TabForge.Avalonia.Tests.TransportSettingsPopupTests.
public static class ClickSettingsPopups
{
    public const string MetronomeTitle = "METRONOME SETTINGS";
    public const string CountInTitle = "COUNT-IN SETTINGS";
    private const string MetronomeCloseTip = "Close metronome settings";
    private const string MetronomePowerName = "Metronome on or off";
    private const string CountInPowerName = "Count-in on or off";
    private const string CountInCloseTip = "Close count-in settings";
    private const string MasterVolumeText = "Metronome volume";
    private const string AccentVolumeText = "Accent / first beat";
    private const string ClickVolumeText = "Regular click";
    private const string ClickSoundText = "Click sound";
    private const string BoostedText = "Boosted (layered, much louder)";
    private const string SubdivisionText = "Subdivision";
    private const string MetronomeHelpText = "Settings apply during playback";
    private const string CountInVolumeText = "Volume";
    private const string LengthText = "Length";
    private const string SongStartText = "Only when starting from bar 1";
    private const string EachSectionText = "Also before every section while playing";
    private const string EachLoopText = "Also before every loop repeat";
    private const string CountInToggleHelpText = "Toggling count-in during playback applies next time you press Play.";
    private const string CountInBoostHelpText = "Boosted like the metronome when its Boosted option is on.";
    private const double FirstSliderTopMargin = 2;
    private const double FirstCountInCheckTopMargin = 8;
    private const double CountInCheckTopMargin = 4;
    private const double CountInFirstHelperTopMargin = 6;

    private static readonly string[] SubdivisionLabels = { "Quarter beats", "Eighth notes", "Eighth-note triplets", "Sixteenth notes" };
    private static readonly string[] LengthLabels = { "1 bar", "2 bars", "3 bars", "4 bars" };
    private const int MaxSubdivision = 4;
    private const int MaxCountInBars = 4;

    /// <summary>The metronome popup over <paramref name="audio"/>; <paramref name="changed"/> runs after every change.</summary>
    public static TransportPopupFrame Metronome(AudioSettings audio, Action changed)
    {
        var defaults = new AudioSettings();
        var frame = new TransportPopupFrame(MetronomeTitle, MetronomeCloseTip, TransportPopupFrame.WideWidth);
        frame.AddPower(MetronomePowerName, audio.Metronome, value => audio.Metronome = value, changed);
        frame.AddSlider(MasterVolumeText, audio.MetronomeVolume, value => audio.MetronomeVolume = value, changed, FirstSliderTopMargin, defaults.MetronomeVolume);
        frame.AddSlider(AccentVolumeText, audio.MetronomeAccentVolume, value => audio.MetronomeAccentVolume = value, changed, defaultValue: defaults.MetronomeAccentVolume);
        frame.AddSlider(ClickVolumeText, audio.MetronomeClickVolume, value => audio.MetronomeClickVolume = value, changed, defaultValue: defaults.MetronomeClickVolume);
        frame.AddChoice(ClickSoundText, ClickSoundPresets.Metronome.Select(sound => sound.Label), ClickSoundPresets.MetronomeIndexOf(audio),
            index => { ClickSoundPresets.ApplyMetronome(audio, index); changed(); });
        frame.AddCheck(BoostedText, audio.MetronomeBoost, value => audio.MetronomeBoost = value, changed);
        frame.AddChoice(SubdivisionText, SubdivisionLabels, Math.Clamp(audio.MetronomeSubdivision, 1, MaxSubdivision) - 1,
            index => { audio.MetronomeSubdivision = index + 1; changed(); });
        frame.AddHelper(MetronomeHelpText);
        return frame;
    }

    /// <summary>The count-in popup over <paramref name="audio"/>; <paramref name="changed"/> runs after every change.</summary>
    public static TransportPopupFrame CountIn(AudioSettings audio, Action changed)
    {
        var defaults = new AudioSettings();
        var frame = new TransportPopupFrame(CountInTitle, CountInCloseTip, TransportPopupFrame.NarrowWidth);
        frame.AddPower(CountInPowerName, audio.CountIn, value => audio.CountIn = value, changed);
        frame.AddSlider(CountInVolumeText, audio.CountInVolume, value => audio.CountInVolume = value, changed, 0, defaults.CountInVolume);
        frame.AddChoice(LengthText, LengthLabels, Math.Clamp(audio.CountInBars, 1, MaxCountInBars) - 1, index => { audio.CountInBars = index + 1; changed(); });
        frame.AddChoice(ClickSoundText, ClickSoundPresets.CountIn.Select(sound => sound.Label), ClickSoundPresets.CountInIndexOf(audio),
            index => { ClickSoundPresets.ApplyCountIn(audio, index); changed(); });
        frame.AddCheck(SongStartText, audio.CountInOnlyAtSongStart, value => audio.CountInOnlyAtSongStart = value, changed, FirstCountInCheckTopMargin);
        frame.AddCheck(EachSectionText, audio.CountInEachSection, value => audio.CountInEachSection = value, changed, CountInCheckTopMargin);
        frame.AddCheck(EachLoopText, audio.LoopCountInEachLoop, value => audio.LoopCountInEachLoop = value, changed, CountInCheckTopMargin);
        frame.AddHelper(CountInToggleHelpText, CountInFirstHelperTopMargin);
        frame.AddHelper(CountInBoostHelpText);
        return frame;
    }
}

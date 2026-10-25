// Owns: the Mixer strip rules that need no WPF types: the pan range text, the semitone text and range of the pitch and transpose steppers, the pan and volume fader mapping, the
//       MIDI sound enabled rule, a strip's accessible name with its mute and solo state, the Master labels and the empty-song message.
// Does not own: the rows, sliders and buttons (the views in Views/MixerWindow*.cs), the meters, the strip order (TrackOrdering.Layout)
//       and the edits the sliders make.
// Tests: no dedicated test class.
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>The Mixer window's strip texts and rules, so the view and the tests read one definition.</summary>
public static class MixerStripPresenter
{
    /// <summary>The pan range named in the pan hints: -64 left, 0 centre, +63 right (the minus is U+2212, the ellipses U+2026).</summary>
    public const string PanRange = "−64 left … 0 centre … +63 right";

    private const string NamePrefix = "Mixer strip: ";

    /// <summary>The Master row's name and its subtitle.</summary>
    public const string MasterName = "Master";
    public const string MasterSubtitle = "whole mix";

    /// <summary>The message in place of the strips when the song has no tracks.</summary>
    public const string NoTracksMessage = "This song has no tracks.";

    /// <summary>The pitch or transpose text: "0 st" at zero, otherwise signed, e.g. "+3 st" or "-5 st".</summary>
    public static string SemitoneText(int semitones) => semitones == 0 ? "0 st" : $"{semitones:+0;-0} st";

    /// <summary>The lowest and highest pitch or transpose, in semitones.</summary>
    public const int SemitoneMin = -24, SemitoneMax = 24;

    /// <summary>The pitch or transpose after one stepper press, kept inside <see cref="SemitoneMin"/>..<see cref="SemitoneMax"/>.</summary>
    public static int StepSemitones(int current, int delta) => Math.Clamp(current + delta, SemitoneMin, SemitoneMax);

    /// <summary>The values a double-click on a slider resets to, declared once for the track list, the mixer and the popups: pan 0 (centre, stored as 64), track and master volume 100, the group's volume 100 % (its tracks unchanged), reverb 24 and chorus 0 sends.</summary>
    public const int VolumeDefault = 100, PanDefault = 0, MasterVolumeDefault = 100, GroupVolumeDefault = 100, ReverbDefault = 24, ChorusDefault = 0;

    /// <summary>The pan slider value (-64 left .. +63 right) for a model pan of 0..127.</summary>
    public static int PanSlider(int pan) => Math.Clamp(pan, 0, MidiLimits.Max) - 64;

    /// <summary>The model pan (0..127) for a pan slider value.</summary>
    public static int PanFromSlider(int slider) => Math.Clamp(64 + slider, 0, MidiLimits.Max);

    /// <summary>A track volume kept to the model range 0..127.</summary>
    public static int ClampVolume(int volume) => Math.Clamp(volume, 0, MidiLimits.Max);

    /// <summary>Units one mouse-wheel notch moves a fader.</summary>
    public const int WheelStep = 1, WheelStepWithCtrl = 8;

    /// <summary>The fader value after one wheel notch (up when <paramref name="up"/>): 1 unit, 8 with Ctrl, kept to the fader's range.</summary>
    public static double WheelValue(double value, double min, double max, bool up, bool ctrl)
    {
        var step = ctrl ? WheelStepWithCtrl : WheelStep;
        return Math.Clamp(Math.Round(value) + (up ? step : -step), min, max);
    }

    /// <summary>A track's MIDI sound box is enabled only while its FX chain (the plug-ins source) is on.</summary>
    public static bool MidiSoundEnabled(string soundSource) => soundSource == SoundSources.Plugins;

    /// <summary>The accessible name of a track's strip, with ", muted" and ", solo" when those are on.</summary>
    public static string StripName(string trackName, bool muted, bool soloed) =>
        $"{NamePrefix}{trackName}{(muted ? ", muted" : "")}{(soloed ? ", solo" : "")}";

    /// <summary>The strips are replaced by the no-tracks message when the song has no tracks.</summary>
    public static bool ShowsNoTracksMessage(SongProject project) => project.Tracks.Count == 0;
}

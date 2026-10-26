// Owns: the audio clip properties dialog's logic without UI types: its texts, the three sliders (range, step, reset value, how a value
//       reads) and writing the edited values back to the clip on OK.
// Does not own: the window (Avalonia ClipPropertiesWindow) or the clip's playback.
// Tests: ClipPropertiesTests, TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
using System.Globalization;
using TabForge.Models;

namespace TabForge.Presenters;

public static class ClipPropertiesPresenter
{
    public const string Title = "Audio clip properties";
    public const string NameLabel = "Name";
    public const string MutedLabel = "Muted";
    public const string ResetTip = "Double-click to reset";
    public const string OkButton = "OK";
    public const string CancelButton = "Cancel";

    /// <summary>One slider row: its label, range, step, the value a double-click restores and how a value reads.</summary>
    public sealed record SliderRow(string Label, double Minimum, double Maximum, double Step, double Reset, Func<double, string> Format);

    public static readonly SliderRow Volume = new("Volume", -36, 12, 0.5, 0, v => $"{v:+0.0;-0.0;0.0} dB");
    public static readonly SliderRow Pitch = new("Pitch", -12, 12, 1, 0, v => $"{v:+0;-0;0} st");
    public static readonly SliderRow Speed = new("Speed", 0.5, 2, 0.05, 1, v => $"{v.ToString("0.00", CultureInfo.CurrentCulture)}×");

    public static string Note(AudioClip clip) => $"{clip.File}\nPitch and speed change independently; the file itself is not changed.";

    /// <summary>Writes the dialog's values to the clip; a blank name keeps the clip's name.</summary>
    public static void Apply(AudioClip clip, string name, double gainDb, double pitch, double speed, bool muted)
    {
        if (name.Trim().Length > 0) clip.Name = name.Trim();
        clip.GainDb = gainDb;
        clip.Pitch = pitch;
        clip.Speed = speed;
        clip.Muted = muted;
    }
}

// Owns: the View and tempo bar's texts: the four button names and tooltips, each caption from the values, the time and key tooltip, and the full key name.
// Does not own: the values or how they are applied (the app's IViewTempoHost), the buttons, menus or popups (ViewTempoBar and its Avalonia twin).
// Tests: PanelBarsTests, TabForge.Avalonia.Tests.PanelBarsTests.
using System.Globalization;

namespace TabForge.Presenters;

/// <summary>The texts of the View and tempo bar: zoom, playback speed, tempo, and time signature with key.</summary>
public static class ViewTempoBarPresenter
{
    public const int Zoom = 0, Speed = 1, Tempo = 2, TimeKey = 3;

    /// <summary>The screen-reader name of each button, by kind.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "Zoom", "Playback speed", "Tempo", "Time signature and key" };

    /// <summary>The tooltip of a button before values are known (time and key gets its own from <see cref="TimeKeyTooltip"/>).</summary>
    public static string Tooltip(int kind) => kind switch
    {
        Zoom => "Score zoom: click for presets, right-click or double-click to fine-tune",
        Speed => "Playback speed: click for presets, right-click or double-click to fine-tune",
        Tempo => "Tempo (BPM): click for presets, right-click or double-click to type",
        _ => "Time signature and key: click for presets, right-click or double-click to type",
    };

    /// <summary>The four captions: "Fit" for any fit zoom else the zoom text, a whole speed percent, the BPM, and "time key".</summary>
    public static string[] Captions(string zoomText, double speedPercent, int tempo, string timeText, string keyText) => new[]
    {
        zoomText.StartsWith("Fit", StringComparison.OrdinalIgnoreCase) ? "Fit" : zoomText,
        string.Format(CultureInfo.InvariantCulture, "{0:0}%", speedPercent),
        tempo.ToString(CultureInfo.InvariantCulture),
        $"{timeText} {keyText}",
    };

    public static string TimeKeyTooltip(string timeText, string keyText) =>
        $"Time signature and key: {timeText}, {FullKey(keyText)}. Click for presets, right-click or double-click to type";

    /// <summary>The full name of a short key: "C" is "C major", "Am" is "A minor".</summary>
    public static string FullKey(string shortKey) =>
        shortKey.EndsWith('m') ? shortKey[..^1] + " minor" : shortKey + " major";
}

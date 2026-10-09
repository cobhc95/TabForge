using TabForge.Services;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// The keyboard pane's natural key height follows the key width (white keys at most 24 px, height 5.5 x width,
/// clamped to 60-120 px) for every standard keyboard size, and the keyboard and fretboard heights are stored apart.
/// </summary>
public static partial class SelfTest
{
    private static void TestKeyboardPaneSize()
    {
        foreach (var keys in new[] { 88, 76, 61, 49, 37, 25 })
        foreach (var width in new[] { 600.0, 1200.0, 2400.0 })
        {
            var whites = KeyboardPaneSizing.WhiteKeysFor(keys);
            var keyWidth = KeyboardPaneSizing.WhiteKeyWidth(width, whites);
            var height = KeyboardPaneSizing.HeightFor(width, whites);
            var tag = $"{keys} keys at {width:0} px";
            Check($"keyboard {tag}: white key at most 24 px wide", keyWidth <= 24 + 1e-9, $"{keyWidth:0.0} px");
            Check($"keyboard {tag}: height within 60-120 px", height >= 60 && height <= 120, $"{height:0.0} px");
            var unclamped = keyWidth * KeyboardPaneSizing.KeyHeightRatio;
            if (unclamped is >= 60 and <= 120)
                Check($"keyboard {tag}: height is 5.5 x key width", Math.Abs(height - unclamped) < 1e-9, $"{height:0.0} vs {unclamped:0.0}");
            Check($"keyboard {tag}: the drawn height equals the natural height inside the pane", KeyboardPaneSizing.KeyHeightIn(height) == height);
        }
        Check("keyboard: 88 keys at 1200 px reach the 120 px cap", KeyboardPaneSizing.HeightFor(1200, KeyboardPaneSizing.WhiteKeysFor(88)) == 120);
        Check("keyboard: 88 keys at 600 px are about 63 px tall", Math.Abs(KeyboardPaneSizing.HeightFor(600, KeyboardPaneSizing.WhiteKeysFor(88)) - 600.0 / 52 * 5.5) < 1e-9);
        Check("keyboard: 88 keys have 52 white keys", KeyboardPaneSizing.WhiteKeysFor(88) == 52);
        Check("keyboard: 25 keys have 15 white keys", KeyboardPaneSizing.WhiteKeysFor(25) == 15);

        // Each view keeps its own height: the keyboard value and the fretboard value are stored apart and both survive validation.
        var settings = new AppSettings();
        settings.Appearance.InstrumentPaneHeight = 240;
        settings.Appearance.KeyboardPaneHeight = 95;
        var kept = SettingsValidator.Normalize(settings).Appearance;
        Check("pane heights: the fretboard and keyboard heights are stored apart", kept.InstrumentPaneHeight == 240 && kept.KeyboardPaneHeight == 95,
            $"fretboard {kept.InstrumentPaneHeight}, keyboard {kept.KeyboardPaneHeight}");
        settings.Appearance.KeyboardPaneHeight = double.NaN;
        Check("pane heights: a non-finite keyboard height is reset to 0", SettingsValidator.Normalize(settings).Appearance.KeyboardPaneHeight == 0);
    }
}

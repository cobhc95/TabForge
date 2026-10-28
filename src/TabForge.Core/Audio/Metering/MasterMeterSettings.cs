namespace TabForge.Audio.Metering;

// Owns: what the master meter keeps in the app settings (the reading, the number, the peak hold) and the choices behind them.
// Does not own: loading and saving (AppSettingsStore), the Preferences rows (MasterMeterFeatureSettings), the meter itself (MasterMeterModel, Views/Meters).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
/// <summary>The master level meter's saved choices. App-wide: never part of a song.</summary>
public sealed class MasterMeterSettings
{
    public const string Peak = "Peak", Rms = "RMS", Loudness = "Loudness";
    public static IReadOnlyList<string> Modes { get; } = Array.AsReadOnly(new[] { Peak, Rms, Loudness });

    /// <summary>What the bars and the number show: <see cref="Peak"/> (dBFS), <see cref="Rms"/> (dB) or <see cref="Loudness"/> (short-term LUFS).</summary>
    public string Mode { get; set; } = Peak;

    /// <summary>The small number under the bars.</summary>
    public bool ShowNumber { get; set; } = true;

    /// <summary>How long the peak-hold line stays, in seconds; 0 = no hold line.</summary>
    public int HoldSeconds { get; set; } = 1;

    public static IReadOnlyList<int> HoldChoices { get; } = Array.AsReadOnly(new[] { 1, 3, 0 });

    /// <summary>The menu and Preferences wording of a mode.</summary>
    public static string ModeLabel(string mode) => mode switch
    {
        Rms => "RMS (dB)",
        Loudness => "Loudness short-term (LUFS)",
        _ => "Peak (dBFS)",
    };

    public static string HoldLabel(int seconds) => seconds <= 0 ? "Off" : seconds + " s";

    /// <summary>The mode a label (or a stored name) names; <see cref="Peak"/> for anything else.</summary>
    public static string ParseMode(string? text) => Modes.FirstOrDefault(m => m == text || ModeLabel(m) == text) ?? Peak;

    /// <summary>The hold a label names; the default (1 s) for anything else.</summary>
    public static int ParseHold(string? text) => HoldChoices.FirstOrDefault(h => HoldLabel(h) == text, 1);

    /// <summary>Brings loaded values back to valid ones.</summary>
    public void Normalize()
    {
        Mode = ParseMode(Mode);
        if (!HoldChoices.Contains(HoldSeconds)) HoldSeconds = 1;
    }
}

using TabForge.Audio.Metering;

namespace TabForge.Services;

// Owns: the Master meter group of the settings catalogue (Audio & Plug-ins page) and its settings bounds.
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings.MasterMeter).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
public static partial class SettingsCatalog
{
    internal const string MasterMeterGroup = "Master meter";
    internal const string MasterMeterModeKey = "meter.mode", MasterMeterNumberKey = "meter.number", MasterMeterHoldKey = "meter.hold";

    internal static List<SettingDescriptor> MasterMeterRows(AppSettings s)
    {
        var meter = s.MasterMeter ??= new MasterMeterSettings();
        return new List<SettingDescriptor>
        {
            Choice(AudioVst, MasterMeterGroup, MasterMeterModeKey, "Master meter: reading", v => meter.Mode = MasterMeterSettings.ParseMode(v), () => MasterMeterSettings.ModeLabel(meter.Mode),
                MasterMeterSettings.Modes.Select(MasterMeterSettings.ModeLabel).ToArray(),
                "What the small level meter in the transport bar shows: the peak level in dBFS, the RMS level in dB, or the short-term loudness in LUFS (EBU R128, K-weighted, 3 second window). Also in the meter's right-click menu.",
                "master meter level peak rms loudness lufs dbfs r128 transport bar"),
            Bool(AudioVst, MasterMeterGroup, MasterMeterNumberKey, "Master meter: show the number", v => meter.ShowNumber = v, () => meter.ShowNumber,
                "Shows the current reading as a small number under the master meter's bars.", "master meter number reading text show hide transport bar"),
            Choice(AudioVst, MasterMeterGroup, MasterMeterHoldKey, "Master meter: peak hold", v => meter.HoldSeconds = MasterMeterSettings.ParseHold(v), () => MasterMeterSettings.HoldLabel(meter.HoldSeconds),
                MasterMeterSettings.HoldChoices.Select(MasterMeterSettings.HoldLabel).ToArray(),
                "How long the line above each master meter bar stays at the highest level: 1 s, 3 s, or Off for no line.", "master meter peak hold line seconds off transport bar"),
        };
    }
}

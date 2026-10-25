using TabForge.LiveInput;

namespace TabForge.Services;

// Owns: the live input row of the settings catalogue (Audio & Plug-ins page); LiveInputFeatureModule hands it to the catalogue.
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (PluginSettings.LiveInput).
// Tests: TestSettingsFileSplitSnapshots, LiveInputStripTests.
public static partial class SettingsCatalog
{
    internal const string LiveInputGroup = "Live input";
    internal const string TunerReferenceKey = "live.tuner.a4", TunerWindowKey = "live.tuner.window";

    internal static List<SettingDescriptor> LiveInputRows(AppSettings s)
    {
        var live = s.Plugins.LiveInput ??= new LiveInputSettings();
        return new List<SettingDescriptor>
        {
            Choice(AudioVst, LiveInputGroup, "live.restore", "Live input: restore the last FX chain", v => live.Restore = v, () => live.Restore, LiveInputSettings.RestoreChoices,
                "What happens when live input is turned on for the first time after starting the program and a live FX chain was saved: Ask (a question with \"Remember my answer\"), Always (load it) or Never (start with an empty chain).",
                "live input microphone guitar monitor fx chain restore previous ask always never remember"),
            Int(AudioVst, LiveInputGroup, TunerReferenceKey, "Tuner: A4 reference pitch", v => live.TunerReferenceHz = v, () => live.TunerReferenceHz,
                LiveStripRules.MinReferenceHz, LiveStripRules.MaxReferenceHz,
                "The frequency the live input tuner takes for the note A4. 440 is the usual pitch; 415 to 466 covers baroque and other tunings.",
                "live input tuner a4 reference pitch concert 440 hz tuning", unit: "Hz"),
            Choice(AudioVst, LiveInputGroup, TunerWindowKey, "Tuner: analysis window", v => live.TunerWindowMs = LiveStripRules.ParseWindow(v), () => LiveStripRules.WindowText(live.TunerWindowMs),
                LiveStripRules.WindowChoicesMs.Select(LiveStripRules.WindowText).ToArray(),
                "How much sound the live input tuner listens to at a time. Short windows answer faster; longer windows (100 ms or more) hear low notes such as a bass guitar's lowest string. The same choice is in the tuner's window menu.",
                "live input tuner window ms milliseconds bass low note latency"),
        };
    }
}

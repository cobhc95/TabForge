using TabForge.LiveInput;

namespace TabForge.Services;

// Owns: the live input row of the settings catalogue (Audio & Plug-ins page); LiveInputFeatureModule hands it to the catalogue.
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (PluginSettings.LiveInput).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots, TestLiveInput.
public static partial class SettingsCatalog
{
    internal const string LiveInputGroup = "Live input";

    internal static List<SettingDescriptor> LiveInputRows(AppSettings s)
    {
        var live = s.Plugins.LiveInput ??= new LiveInputSettings();
        return new List<SettingDescriptor>
        {
            Choice(AudioVst, LiveInputGroup, "live.restore", "Live input: restore the last FX chain", v => live.Restore = v, () => live.Restore, LiveInputSettings.RestoreChoices,
                "What happens when live input is turned on for the first time after starting the program and a live FX chain was saved: Ask (a question with \"Remember my answer\"), Always (load it) or Never (start with an empty chain).",
                "live input microphone guitar monitor fx chain restore previous ask always never remember"),
        };
    }
}

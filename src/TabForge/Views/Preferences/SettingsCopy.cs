using TabForge.Services;

namespace TabForge.Views;

// Owns: the sections that Reset all and Import copy into the Settings window's staged settings beyond the ones PreferencesWindow lists:
//   the feature sections, the Band view's remembered layout rules, and the plug-in section's rows (never its approval, trust, quarantine, scan-cache or chain lists).
// Does not own: validation (SettingsFileService / SettingsValidator) or the staging itself (PreferencesWindow).
// Tests: TestSettingsResetAndImportCoverEveryRow.
// Live input settings (LiveInput) are a plug-in section row, copied with the rest of PluginSettings.
internal static class SettingsCopy
{
    public static void CopyRowSections(AppSettings copy, AppSettings target)
    {
        target.PreferredContinuousScoreView = copy.PreferredContinuousScoreView;
        target.PreferredHorizontalScoreView = copy.PreferredHorizontalScoreView;
        target.Render = copy.Render;
        target.Video = copy.Video;
        target.LiveVideo = copy.LiveVideo;
        target.Learn = copy.Learn;
        // The Band view's remembered layout rules are not catalogue rows.
        var band = (copy.Timeline ??= new TimelineSettings()).Band ??= new BandSettings();
        var mine = (target.Timeline ??= new TimelineSettings()).Band ??= new BandSettings();
        mine.Fretboards = band.Fretboards; mine.SystemsPerRow = band.SystemsPerRow; mine.MainSystemsPerRow = band.MainSystemsPerRow; mine.Resets++;
        var from = copy.Plugins ?? new PluginSettings();
        var to = target.Plugins ??= new PluginSettings();
        to.Driver = from.Driver;
        to.Device = from.Device;
        to.InputDevice = from.InputDevice;
        to.SampleRate = from.SampleRate;
        to.BufferSize = from.BufferSize;
        to.FollowWindowsVolume = from.FollowWindowsVolume;
        to.LiveLimiter = from.LiveLimiter;
        to.PlayAllThroughEngine = from.PlayAllThroughEngine;
        to.AutoGmSound = from.AutoGmSound;
        to.AutoPitchMatch = from.AutoPitchMatch;
        to.WindowsMidiLatencyMs = from.WindowsMidiLatencyMs;
        to.RecordingOffsetMs = from.RecordingOffsetMs;
        to.AsioInputsEnabled = from.AsioInputsEnabled;
        to.AsioInputChannel = from.AsioInputChannel;
        to.AsioInputLastChannel = from.AsioInputLastChannel;
        to.HiddenInputs = InputsShownDialog.Copy(from.HiddenInputs);
        to.UseAllInputs = from.UseAllInputs;
        to.ProtectFromPops = from.ProtectFromPops;
        to.AsioOutputChannel = from.AsioOutputChannel;
        to.AsioOutputLastChannel = from.AsioOutputLastChannel;
        to.Folders = new List<string>(from.Folders);
        to.CommonFolders = new List<string>(from.CommonFolders);
        to.ScanStandardFolders = from.ScanStandardFolders;
        to.RememberScan = from.RememberScan;
        to.DockPluginWindows = from.DockPluginWindows;
        to.PluginWindowsOnTop = from.PluginWindowsOnTop;
        to.SeparateProcessPerPlugin = from.SeparateProcessPerPlugin;
        to.LiveInput = from.LiveInput;
    }
}

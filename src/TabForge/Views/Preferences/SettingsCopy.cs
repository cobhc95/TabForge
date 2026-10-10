using TabForge.Services;

namespace TabForge.Views;

// Owns: the sections that Reset all and Import copy into the Settings window's staged settings beyond the ones PreferencesWindow lists:
//   the feature sections, and the plug-in section's rows (never its approval, trust, quarantine, scan-cache or chain lists).
// Does not own: validation (SettingsFileService / SettingsValidator) or the staging itself (PreferencesWindow).
// Tests: TestSettingsResetAndImportCoverEveryRow.
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
        to.AsioOutputChannel = from.AsioOutputChannel;
        to.AsioOutputLastChannel = from.AsioOutputLastChannel;
        to.Folders = new List<string>(from.Folders);
        to.CommonFolders = new List<string>(from.CommonFolders);
        to.ScanStandardFolders = from.ScanStandardFolders;
        to.RememberScan = from.RememberScan;
        to.DockPluginWindows = from.DockPluginWindows;
        to.PluginWindowsOnTop = from.PluginWindowsOnTop;
        to.SeparateProcessPerPlugin = from.SeparateProcessPerPlugin;
    }
}

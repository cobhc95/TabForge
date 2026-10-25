// Owns: the "update available" and "up to date" window's rules without UI types: its title, heading and detail texts, the opt-out
//       box's texts and starting value, the privacy line, and which buttons exist with which one is the default.
// Does not own: the window and its controls (Views/UpdateAvailableWindow.cs, TabForge.Avalonia/Views/UpdateAvailableWindow.cs), the
//       update check and the reply (UpdateService), and opening the release page.
// Tests: UpdateAvailableWindowTests.
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>The "update available" window's texts and button rules. A null release means the installed version is the latest.</summary>
public sealed class UpdateAvailablePresenter
{
    public const string CheckBoxText = "Check for updates automatically (once a day)";
    public const string CheckBoxToolTip = "Also in Settings > General > Updates. Help > Check for updates always works.";
    public const string PrivacyText = "One anonymous request to GitHub; nothing is downloaded or installed by TabForge.";
    public const string DownloadButtonText = "Open download page";
    public const string LaterButtonText = "Later";
    public const string OkButtonText = "OK";

    public UpdateAvailablePresenter(ReleaseInfo? release, string currentVersion, bool checkAutomatically)
    {
        Release = release;
        CurrentVersion = currentVersion;
        CheckAutomatically = checkAutomatically;
    }

    /// <summary>The newer release, or null when the installed version is the latest.</summary>
    public ReleaseInfo? Release { get; }

    public string CurrentVersion { get; }

    /// <summary>The opt-out box's starting value: the automatic check setting.</summary>
    public bool CheckAutomatically { get; }

    public bool IsUpToDate => Release is null;

    public string Title => IsUpToDate ? "No update available" : "Update available";

    public string Heading => Release is { } release ? $"TabForge {release.Version} is available" : "TabForge is up to date";

    public string Detail => Release is null
        ? $"You have the latest version ({CurrentVersion})."
        : $"You have {CurrentVersion}. The download page on GitHub has the installer, the portable zip and what changed.";

    /// <summary>The download button exists only when there is a newer release.</summary>
    public bool ShowsDownloadButton => !IsUpToDate;

    public bool DownloadIsDefault => !IsUpToDate;

    public string CloseButtonText => IsUpToDate ? OkButtonText : LaterButtonText;

    public bool CloseIsDefault => IsUpToDate;
}

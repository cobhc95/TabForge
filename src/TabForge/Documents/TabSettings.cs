using System.Text.Json.Serialization;

namespace TabForge.Documents;

/// <summary>
/// User-adjustable behaviour and styling for the title-bar tab strip. Serialized with the rest of the
/// app settings, so every property needs a safe default for older settings files.
/// </summary>
public sealed class TabSettings
{
    /// <summary>Rounded top corners (browser look) or flat rectangles.</summary>
    public string Style { get; set; } = TabStyles.Rounded;

    /// <summary>"Fit" shrinks every tab so they share the strip; "Content" sizes each tab to its title.</summary>
    public string WidthMode { get; set; } = TabWidthModes.Fit;

    /// <summary>When the per-tab close button is shown.</summary>
    public string CloseButton { get; set; } = CloseButtonModes.ActiveAndHover;

    /// <summary>Double-clicking a tab closes it (browser "double-click to close" behaviour).</summary>
    public bool CloseOnDoubleClick { get; set; } = true;

    /// <summary>What a middle-click on a tab does.</summary>
    public string MiddleClick { get; set; } = MiddleClickActions.Close;

    /// <summary>What happens when the last tab is closed.</summary>
    public string LastTabClosed { get; set; } = LastTabActions.NewTab;

    /// <summary>Where a new tab opens relative to the active one.</summary>
    public string NewTabPosition { get; set; } = NewTabPositions.AfterCurrent;

    /// <summary>Ctrl+O replaces the active tab instead of opening another tab.</summary>
    public bool OpenInCurrentTab { get; set; } = true;

    /// <summary>What happens to playback in other tabs when a different tab is opened or selected.</summary>
    public string PlaybackOnTabSwitch { get; set; } = TabPlaybackActions.ContinuePlayingPrevious;

    /// <summary>Dragging a tab out of the strip opens it in a new window.</summary>
    public bool DetachToNewWindow { get; set; } = true;

    /// <summary>Dragging a tab onto another TabForge window merges it there.</summary>
    public bool MergeAcrossWindows { get; set; } = true;

    /// <summary>Show a playing badge on the tab whose score is currently playing.</summary>
    public bool ShowPlayingIndicator { get; set; } = true;

    /// <summary>Middle-click on empty title-bar space opens a new tab.</summary>
    public bool MiddleClickTitleBarNewTab { get; set; } = true;

    /// <summary>Upper bound on a single tab's width, in device-independent pixels.</summary>
    public double MaxTabWidth { get; set; } = 320;

    /// <summary>Lower bound on a tab's width when tabs share the strip.</summary>
    public double MinTabWidth { get; set; } = 120;

    /// <summary>Tab height in device-independent pixels.</summary>
    public double TabHeight { get; set; } = 36;

    /// <summary>Tab title font size.</summary>
    public double TabFontSize { get; set; } = 13;

    public TabSettings Clone() => (TabSettings)MemberwiseClone();

    [JsonIgnore] public bool IsRounded => !string.Equals(Style, TabStyles.Square, StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool ShrinkToFit => !string.Equals(WidthMode, TabWidthModes.Content, StringComparison.OrdinalIgnoreCase);
}

public static class TabStyles
{
    public const string Rounded = "Rounded";
    public const string Square = "Square";
}

public static class TabWidthModes
{
    public const string Fit = "Fit";
    public const string Content = "Content";
}

public static class CloseButtonModes
{
    public const string Always = "Always";
    public const string ActiveAndHover = "ActiveAndHover";
    public const string ActiveOnly = "ActiveOnly";
    public const string Never = "Never";
}

public static class MiddleClickActions
{
    public const string Close = "Close";
    public const string Duplicate = "Duplicate";
    public const string NewTab = "NewTab";
    public const string Nothing = "Nothing";
}

public static class LastTabActions
{
    public const string NewTab = "NewTab";
    public const string CloseWindow = "CloseWindow";
}

public static class NewTabPositions
{
    public const string AfterCurrent = "AfterCurrent";
    public const string AtEnd = "AtEnd";
}

public static class TabPlaybackActions
{
    public const string ContinuePlayingPrevious = "Continue playing previous tab";
    public const string PausePrevious = "Pause previous tab";
    public const string StopPrevious = "Stop previous tab";

    public static TabPlaybackAction Resolve(string? value) =>
        string.Equals(value, PausePrevious, StringComparison.OrdinalIgnoreCase) ? TabPlaybackAction.Pause :
        string.Equals(value, StopPrevious, StringComparison.OrdinalIgnoreCase) ? TabPlaybackAction.Stop :
        TabPlaybackAction.Continue;
}

public enum TabPlaybackAction { Continue, Pause, Stop }

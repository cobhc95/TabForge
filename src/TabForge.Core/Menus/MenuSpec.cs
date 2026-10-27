// Owns: the row shape every right-click menu is built from (plain data: caption, shortcut text, tick, one-of-several choice, the Preferences row it mirrors, submenus, separators).
// Does not own: the menu contents (TimelineMenus, TrackRowMenus, ScoreMenus, and the instrument and band menus) or the toolkit builders (Avalonia ContextMenuBuilder).
// Tests: SelfTestMenuGolden, SelfTestTrackRowMenu, TabForge.Avalonia.Tests.ContextMenuTests.

namespace TabForge.Menus;

/// <summary>One entry of a right-click menu as plain data (a separator has <see cref="IsSeparator"/>). Built by
/// <see cref="TimelineMenus"/>, <see cref="TrackRowMenus"/>, <see cref="ScoreMenus"/> and the instrument and band menus, turned into
/// toolkit items by each host, and enumerated by the self-test.</summary>
public sealed class MenuSpec
{
    public string Header = "";
    public string Shortcut = "";
    public string? ToolTip;
    public bool Enabled = true;
    public bool Checkable;
    public bool Checked;
    public bool IsSeparator;
    /// <summary>A grey information line (never clickable).</summary>
    public bool IsLabel;
    public TimelineCommand Command;
    public List<MenuSpec>? Children;
    /// <summary>A one-of-several choice: drawn as a dot, not a tick.</summary>
    public bool Radio;
    /// <summary>The Preferences row (<c>SettingsCatalog</c> key) this setting-like item mirrors; every checkable item has this or <see cref="NoSetting"/>.</summary>
    public string? SettingKey;
    /// <summary>Why a checkable item has no Preferences row: it is state of one track, bar, section, clip or the selection, not a preference.</summary>
    public string? NoSetting;
    /// <summary>Command id and argument of the menus that are not the timeline's (fretboard, score).</summary>
    public string? Id;
    public string? Arg;
    /// <summary>Bars (first, last) the timeline outlines in red while the pointer is on this row.</summary>
    public (int First, int Last)? HoverBars;

    public static MenuSpec Separator() => new() { IsSeparator = true };
}

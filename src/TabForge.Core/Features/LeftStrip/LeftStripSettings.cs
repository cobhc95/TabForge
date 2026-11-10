namespace TabForge.Features.LeftStrip;

// Owns: the saved state of the experimental left strip: shown or hidden, the Composer panel open or closed, vertical or beside the menus, the
//   menu-bar toggle icon shown, and whether the strip shows on start; and the start rules read from them.
// Does not own: loading and saving the settings file (AppSettingsStore), the strip's controls (Avalonia LeftStripFeature).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests.
public sealed class LeftStripSettings
{
    /// <summary>The strip was shown when last left. Fresh settings: shown.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The Composer panel was open when last left. Fresh settings: closed.</summary>
    public bool ComposerOpen { get; set; }

    /// <summary>The strip sits as a row beside the menus instead of a vertical strip at the window's left.</summary>
    public bool BesideMenus { get; set; }

    /// <summary>The strip's toggle icon shows at the left end of the menu bar (and the mode bar).</summary>
    public bool ShowButton { get; set; } = true;

    /// <summary>The strip shows when TabForge starts (as it was left); off, it starts hidden.</summary>
    public bool ShowOnStart { get; set; } = true;

    /// <summary>Whether the strip is shown when the window opens.</summary>
    public bool ShownAtStart => ShowOnStart && Visible;

    /// <summary>Whether the Composer panel is open when the window opens: only beside a shown strip.</summary>
    public bool ComposerOpenAtStart => ShownAtStart && ComposerOpen;
}

using System.Collections.Generic;

namespace TabForge.Features.LeftStrip;

// Owns: the saved state of the left strip: shown or hidden, the Composer panel open or closed, left / right / beside the menus, the
//   icons on it, their size, tooltips and labels, the menu-bar toggle icon shown, and whether the strip shows on start; and the start rules read from them.
// Does not own: loading and saving the settings file (AppSettingsStore), the strip's controls (Avalonia LeftStripFeature).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests.
public sealed class LeftStripSettings
{
    /// <summary>The strip was shown when last left. Fresh settings: shown.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The Composer panel was open when last left. Fresh settings: closed.</summary>
    public bool ComposerOpen { get; set; }

    /// <summary>The strip sits as a row beside the menus instead of a vertical strip.</summary>
    public bool BesideMenus { get; set; }

    /// <summary>The vertical strip sits at the right of the score instead of the left (unused while beside the menus).</summary>
    public bool RightSide { get; set; }

    /// <summary>The icon ids on the strip in order ("Separator" for a line); null is the default strip.</summary>
    public List<string>? Items { get; set; }

    /// <summary>Large icons (the default) instead of the normal size.</summary>
    public bool Large { get; set; } = true;

    /// <summary>The icons show their tooltips.</summary>
    public bool Tooltips { get; set; } = true;

    /// <summary>The icons show their labels.</summary>
    public bool Labels { get; set; }

    /// <summary>The strip's toggle icon shows at the left end of the menu bar (and the mode bar).</summary>
    public bool ShowButton { get; set; } = true;

    /// <summary>The strip shows when TabForge starts (as it was left); off, it starts hidden.</summary>
    public bool ShowOnStart { get; set; } = true;

    /// <summary>Whether the strip is shown when the window opens.</summary>
    public bool ShownAtStart => ShowOnStart && Visible;

    /// <summary>Whether the Composer panel is open when the window opens: as it was left, shown strip or not.</summary>
    public bool ComposerOpenAtStart => ComposerOpen;
}

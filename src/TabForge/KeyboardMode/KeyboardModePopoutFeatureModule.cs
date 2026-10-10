using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.KeyboardMode;

// Owns: the pop-out's rows in the central tables: the View menu row and the hotkey row of View.LearnPopout, and its command.
// Does not own: the window or its lifetime (KeyboardModePopout) or the main window's handler (MainWindow.KeyboardMode.cs).
// Tests: TestFeatureModuleContributions, TestKeyboardModePopout.
public sealed class KeyboardModePopoutFeatureModule : IFeatureModule
{
    public const string CommandId = "View.LearnPopout";

    public string Name => "Keyboard mode pop-out";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new(CommandId, HotkeyCatalog.CategoryView, "Keyboard mode: view in its own window", "Ctrl+Alt+O",
            "Open the falling-notes view in its own window, or bring it back (Keyboard mode on; F11 in the window toggles full screen, Esc leaves it)."), "Learn.WaitSkip"),
    };

    public IReadOnlyList<FeatureMenuRow> MenuRows { get; } = new FeatureMenuRow[]
    {
        new("_View", "View.LearnMode", "Keyboard mode: view in its own window", CommandId),
    };

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        var learn = host as IKeyboardModeCommandHost ?? throw new InvalidOperationException("The host does not implement IKeyboardModeCommandHost.");
        yield return new(CommandId, () => (learn.WaitCommands as KeyboardModeFrameController)?.RequestPopout());
    }
}

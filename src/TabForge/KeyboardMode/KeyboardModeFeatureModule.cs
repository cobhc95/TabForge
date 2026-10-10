using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.KeyboardMode;

/// <summary>What the main window offers the Keyboard mode command.</summary>
public interface IKeyboardModeCommandHost : IFeatureHost
{
    /// <summary>Shows or hides the Keyboard mode pane.</summary>
    void ToggleKeyboardMode();
    /// <summary>The wait mode commands (the controller).</summary>
    IKeyboardModeWaitCommands WaitCommands { get; }
}

/// <summary>The commands of wait mode.</summary>
public interface IKeyboardModeWaitCommands
{
    /// <summary>Turns "wait for the right notes" on or off.</summary>
    void ToggleWait();
    /// <summary>Gives up the chord the song waits for.</summary>
    void SkipWait();
    /// <summary>Shows <paramref name="step"/> more seconds of notes above the keys (negative: fewer, longer notes).</summary>
    void StepLookAhead(int step);
}

// Owns: everything Keyboard mode adds to the central tables: its Preferences group and rows, the View.LearnMode, wait, skip and look-ahead hotkey rows and commands, the View menu row and the settings bounds.
// Does not own: the pane (KeyboardModeFrameController, KeyboardModeView), the dock row (DockPaneTable) or the window's handler (MainWindow.KeyboardMode.cs).
// Tests: TestFeatureModuleContributions, TestKeyboardModeNoteStream, TestMainMenuTreeGolden, TestDockPaneTable.
public sealed class KeyboardModeFeatureModule : IFeatureModule
{
    public const string CommandId = "View.LearnMode";
    public const string WaitCommandId = "Learn.ToggleWait";
    public const string SkipCommandId = "Learn.WaitSkip";
    public const string LongerCommandId = "Learn.LookAheadLonger", ShorterCommandId = "Learn.LookAheadShorter";

    public string Name => "Keyboard mode";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new(CommandId, HotkeyCatalog.CategoryView, "Show / hide Keyboard mode (experimental)", "Ctrl+Alt+L",
            "Show or hide the Keyboard mode pane: the notes of the selected track fall onto a key strip in time with playback (same as View > Keyboard mode (experimental))."), "Band.ResetRowHeights"),
        new(new(WaitCommandId, HotkeyCatalog.CategoryView, "Keyboard mode: wait for the right notes", "Ctrl+Alt+W",
            "Turn on or off the pause at each chord in Keyboard mode until its notes are pressed (the Preferences setting of the same name)."), CommandId),
        new(new(SkipCommandId, HotkeyCatalog.CategoryView, "Keyboard mode: skip the awaited chord", "Ctrl+Alt+Q",
            "Give up the chord the song is waiting for (a miss) and carry on."), WaitCommandId),
        new(new(LongerCommandId, HotkeyCatalog.CategoryView, "Keyboard mode: more seconds ahead (shorter notes)", "Ctrl+Alt+OemPlus",
            "Show one more second of notes above the keys in Keyboard mode: the notes get shorter and you see further ahead (the + of its control bar)."), SkipCommandId),
        new(new(ShorterCommandId, HotkeyCatalog.CategoryView, "Keyboard mode: fewer seconds ahead (longer notes)", "Ctrl+Alt+OemMinus",
            "Show one second fewer of notes above the keys in Keyboard mode: the notes get longer and easier to read (the - of its control bar)."), LongerCommandId),
    };

    public IReadOnlyList<FeatureMenuRow> MenuRows { get; } = new FeatureMenuRow[]
    {
        new("_View", "View.Mixer", "Keyboard mode (experimental)", CommandId),
    };

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.Fretboard, "Keyboard mode (experimental)", "learn.lookahead learn.sizekeys* learn.playalong learn.wait* learn.hands learn.timing* learn.notenames* learn.fingers*", "Practice aids"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.KeyboardModeRows(settings);

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        var learn = host as IKeyboardModeCommandHost ?? throw new InvalidOperationException("The host does not implement IKeyboardModeCommandHost.");
        yield return new(CommandId, learn.ToggleKeyboardMode);
        yield return new(WaitCommandId, () => learn.WaitCommands.ToggleWait());
        yield return new(SkipCommandId, () => learn.WaitCommands.SkipWait());
        yield return new(LongerCommandId, () => learn.WaitCommands.StepLookAhead(1));
        yield return new(ShorterCommandId, () => learn.WaitCommands.StepLookAhead(-1));
    }

    public void Normalize(AppSettings settings)
    {
        var value = settings.Learn ??= new KeyboardModeSettings();
        value.LookAheadSeconds = KeyboardModeSettings.NormalizeLookAhead(value.LookAheadSeconds);
        value.KeyboardSize = KeyboardModeSettings.NormalizeSize(value.KeyboardSize, KeyboardModeSettings.SizeFull);
        value.MidiInput = KeyboardModeSettings.NormalizeMidiInput(value.MidiInput);
        value.Hands = KeyboardModeSettings.NormalizeHands(value.Hands);
        value.TimingTolerance = KeyboardModeSettings.NormalizeTiming(value.TimingTolerance);
    }
}

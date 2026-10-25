using System.Collections.Generic;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.KeyboardMode;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: View > Keyboard mode (the View.LearnMode command): enters the Keyboard mode layout (the arrangement KeyboardModeLayoutSwap builds for the
//   saved size, with the learn pane shown), or puts back the arrangement it replaced; the check of its row; the "on" notice; the pane's X
//   button (LeaveFromPane, which leaves the mode); and the saves made while the mode is on, which store the arrangement it replaced.
// Does not own: the layout maths (KeyboardModeLayoutSwap in Core), the dock tree (DockWorkspaceController), the keyboard pane (registered in
//   MainWindow), the menu rows (MainMenuTable), the keyboard view and its pop-out window (MainWindowDocumentPanes).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (the command is handled by the menu).
/// <summary>The View > Keyboard mode toggle, bound to the window, its dock, the saved settings and the top bar.</summary>
public sealed class MainWindowLearnMode
{
    /// <summary>The View menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { CommandId, ToggleWaitCommandId, WaitSkipCommandId, LookAheadLongerCommandId, LookAheadShorterCommandId };

    private const string CommandId = "View.LearnMode";

    /// <summary>The Keyboard mode hotkeys: waiting for the right notes, skipping the awaited chord and the look-ahead steps.</summary>
    private const string ToggleWaitCommandId = "Learn.ToggleWait";
    private const string WaitSkipCommandId = "Learn.WaitSkip";
    private const string LookAheadLongerCommandId = "Learn.LookAheadLonger";
    private const string LookAheadShorterCommandId = "Learn.LookAheadShorter";

    /// <summary>The status notice shown while the mode is on.</summary>
    private const string OnNotice = "Keyboard mode on";

    private readonly Window _window;
    private readonly DockWorkspaceController _dock;
    private readonly MainWindowSettings? _settings;
    private readonly NotificationBar? _topBar;
    private readonly Action? _refitTrackList;

    /// <summary>The arrangement the Keyboard mode layout replaced; null while the mode is off.</summary>
    private DockWorkspaceState? _arrangementBeforeLearn;

    private MainWindowLearnMode(Window window, DockWorkspaceController dock, MainWindowSettings? settings, NotificationBar? topBar, Action? refitTrackList)
    {
        _window = window;
        _dock = dock;
        _settings = settings;
        _topBar = topBar;
        _refitTrackList = refitTrackList;
    }

    /// <param name="commands">Receives the View.LearnMode handler.</param>
    /// <param name="window">The window whose View menu row shows the check.</param>
    /// <param name="dock">The dock the Keyboard mode layout is applied to.</param>
    /// <param name="settings">The saved keyboard size and layout store; null uses the full size and saves nothing.</param>
    /// <param name="topBar">The top bar that shows the on message (sticky Info); null shows nothing.</param>
    /// <param name="panes">The panes whose Keyboard mode bar the wait and look-ahead hotkeys drive; null leaves them unwired (the Compose call leaves it unset).</param>
    /// <param name="refitTrackList">Refits the track list after a layout is applied (the timeline fit, replacing a user splitter); null leaves it unfitted.</param>
    /// <returns>The mode, whose <see cref="LeaveFromPane"/> the Keyboard mode pane's X button calls.</returns>
    public static MainWindowLearnMode Register(MainWindowCommands commands, Window window, DockWorkspaceController dock, MainWindowSettings? settings, NotificationBar? topBar, MainWindowDocumentPanes? panes = null, Action? refitTrackList = null)
    {
        var learn = new MainWindowLearnMode(window, dock, settings, topBar, refitTrackList);
        panes?.AttachLearnSettings(settings?.Settings, settings is null ? null : () => settings.SaveSettings());
        commands.Add(CommandId, learn.Toggle);
        commands.Add(ToggleWaitCommandId, () => ToggleWait(panes));
        commands.Add(WaitSkipCommandId, () => SkipWait(panes));
        commands.Add(LookAheadLongerCommandId, () => StepLookAhead(panes, 1));
        commands.Add(LookAheadShorterCommandId, () => StepLookAhead(panes, -1));
        // The menu row is built after Register, so its check takes the mode's state when the window opens.
        window.Opened += (_, _) => MainWindowViewMenu.SetRowChecks(window, CommandId, learn.IsOn);
        // Attach saved the swapped arrangement first (the subscription order); this save puts the real one back, so it is never persisted.
        dock.LayoutChanged += _ => { if (learn.RealLayout is { } real) settings?.SaveLayout(DockWorkspaceRules.Capture(real)); };
        return learn;
    }

    /// <summary>The arrangement the mode puts back, as a copy; null while the mode is off.</summary>
    public DockWorkspaceState? RealLayout => _arrangementBeforeLearn is null ? null : DockLayoutTree.Clone(_arrangementBeforeLearn);

    /// <summary>True while keyboard mode shows its layout; the instrument panel stays closed then.</summary>
    public bool IsOn => _arrangementBeforeLearn is not null;

    /// <summary>The saved keyboard size text, as Keyboard mode settings store it.</summary>
    private string SavedSize => _settings?.Settings.Learn?.KeyboardSize ?? KeyboardModeSettings.SizeFull;

    /// <summary>The Keyboard mode pane's X button: leaves the mode and puts the arrangement back, as the pane's exit request does in WPF.</summary>
    public void LeaveFromPane()
    {
        if (IsOn) Toggle();
    }

    /// <summary>View > Keyboard mode: enters the Keyboard mode layout, or puts back the arrangement it replaced.</summary>
    private void Toggle()
    {
        if (IsOn) Exit();
        else Enter();
        MainWindowViewMenu.SetRowChecks(_window, CommandId, IsOn);
    }

    /// <summary>Learn.ToggleWait: the song stops at each chord until its notes are pressed, or carries on — the shared engine (both bars follow).</summary>
    private static void ToggleWait(MainWindowDocumentPanes? panes)
    {
        if (panes?.LearnEngine is { } engine) engine.ToggleWait();
        else if (panes?.KeyboardMode?.Bar is { } bar) bar.WaitOn = !bar.WaitOn;
    }

    /// <summary>Learn.WaitSkip: gives up the awaited chord (a miss) and carries on.</summary>
    private static void SkipWait(MainWindowDocumentPanes? panes)
    {
        if (panes?.LearnEngine is { } engine) engine.SkipWait();
        else panes?.KeyboardMode?.Bar.SkipAwaited();
    }

    /// <summary>Learn.LookAheadLonger/Shorter: shows one more (or fewer) seconds above the keys.</summary>
    private static void StepLookAhead(MainWindowDocumentPanes? panes, int step)
    {
        if (panes?.LearnEngine is { } engine) engine.StepLookAhead(step);
        else panes?.KeyboardMode?.Bar.BumpLookAhead(step);
    }

    private void Enter()
    {
        _arrangementBeforeLearn = DockLayoutTree.Clone(_dock.State);
        // Standing rule: never a fretboard in keyboard mode; the flag keeps every later show path closed too.
        _dock.SuppressInstrumentPanel = true;
        _dock.ApplyLayout(KeyboardModeLayoutSwap.Build(_arrangementBeforeLearn, KeyboardModeSettings.ParseSize(SavedSize)));
        _refitTrackList?.Invoke();
        _topBar?.Show(OnNotice, NotificationKind.Info, source: NotificationBar.LearnSource);
    }

    private void Exit()
    {
        if (_arrangementBeforeLearn is not { } before) return;
        _arrangementBeforeLearn = null;
        // Cleared before the restore, so the exact prior arrangement (fretboard included) comes back.
        _dock.SuppressInstrumentPanel = false;
        _dock.ApplyLayout(before);
        _refitTrackList?.Invoke();
        _topBar?.HideSticky(NotificationBar.LearnSource);
    }
}

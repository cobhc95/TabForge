using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace TabForge.Avalonia;

// Owns: opening the fretboard's scale popup from the fretboard / keyboard pane's Scales button and from the Tools menu entry (the same command id and shortcut as before):
//   it asks the status bar's scale readout, which holds the one shared pick, to open its popup anchored to the Scales button, or to the main window when no instrument pane is shown.
// Does not own: the popup body (ScalePanel), the detection and picks (StatusScaleReadout, ScaleReadoutEngine), the menu rows (FretboardContextMenu, MainMenuTable).
// Tests: TabForge.Avalonia.Tests.FretboardScalePopupTests.
// Threads: UI thread only.
public sealed class MainWindowScaleFinder
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.ScaleFinder" };

    private readonly Window _owner;

    /// <param name="owner">The main window; the popup anchors to it when no Scales button is on screen.</param>
    public MainWindowScaleFinder(Window owner) => _owner = owner;

    /// <summary>The status bar scale readout that holds the shared pick and opens the popup (set once the status bar exists).</summary>
    internal StatusScaleReadout? Scale { get; set; }

    /// <summary>The control the popup opens above: the Scales button of the shown instrument pane, or null for the window.</summary>
    internal Func<Control?>? Anchor { get; set; }

    /// <summary>Registers the Tools menu's Scale finder command.</summary>
    public void Register(MainWindowCommands commands) => commands.Add("Tools.ScaleFinder", () => Open());

    /// <summary>Opens the popup above the Scales button (or the window).</summary>
    /// <returns>False when nothing opened (no readout yet, or a drum track).</returns>
    public bool Open() => Scale is { } scale && scale.OpenForFretboard(Anchor?.Invoke() is { IsVisible: true } anchor ? anchor : _owner);

    /// <summary>Opens the popup (the Scales button's callback).</summary>
    public System.Threading.Tasks.Task OpenAsync()
    {
        Open();
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

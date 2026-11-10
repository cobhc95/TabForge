using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Features.Help;

// Owns: the Help.Open command (F1, Help > Help): opens the Help window over the main window, or brings the open one to the front instead of opening a second;
//   the feature's one HelpContent (chapter cache and search index) and the one window.
// Does not own: the window (HelpWindow), the chapters and search (HelpContent, Core Features/Help), the menu row (MainMenuTable), the key (HotkeyCatalog).
// Tests: TabForge.Avalonia.Tests.HelpWindowTests.
public sealed class HelpFeature
{
    public const string OpenId = "Help.Open";

    /// <summary>The commands the Help registers (the menu tests discover this list).</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { OpenId };

    private readonly Window _owner;
    private readonly AppSettings? _sizing;
    private readonly Action? _save;
    private readonly HelpContent _content = new();
    private HelpWindow? _window;

    internal HelpFeature(Window owner, AppSettings? sizing, Action? save)
    {
        _owner = owner;
        _sizing = sizing;
        _save = save;
    }

    /// <summary>Registers Help.Open; one line in the window's command setup.</summary>
    public static void Register(MainWindowCommands commands, Window owner, MainWindowSettings? settings)
    {
        var feature = new HelpFeature(owner, settings?.Settings, () => settings?.SaveWindowPlacement());
        commands.Add(OpenId, () => feature.Open());
    }

    /// <summary>Opens the window, or brings the open one forward (restoring it when minimised).</summary>
    internal HelpWindow Open()
    {
        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return open;
        }
        var window = _window = new HelpWindow(_content, _sizing, _save);
        window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
        window.Show(_owner);
        return window;
    }

    /// <summary>The window on show, or null (for tests).</summary>
    internal HelpWindow? Current => _window;

    /// <summary>The feature's content (for tests).</summary>
    internal HelpContent Content => _content;
}

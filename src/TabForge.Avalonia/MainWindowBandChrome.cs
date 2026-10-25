using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Band view's part of the header strip the Band view and compact mode share (its handlers: a capsule toggles that track's card, the
//   caption opens the Band view's menu under it, a right-click opens it at the pointer, the close button leaves the Band view), and the Band view's
//   layout choices (fretboard above or beside, colour frame, Reset view), kept in the timeline settings' band block and applied to the cards.
// Does not own: the layout and the dock (MainWindowBandView), the cards (BandView), the strip and the hiding both modes share (MainWindowHeaderModes),
//   the menu's items (BandViewMenu).
// Tests: TabForge.Avalonia.Tests.BandViewTests, TabForge.Avalonia.Tests.CompactBandHeaderTests.
internal sealed class MainWindowBandChrome
{
    /// <summary>The hover text of a band capsule, with the track's name in place of {0}.</summary>
    private const string PillToolTipFormat = "Show or hide the card of {0}";

    private readonly Window _window;
    private readonly MainWindowHeaderModes _header;
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowSettings? _settings;
    private readonly BandSettings _sessionBand = new();
    private readonly Action _close;

    /// <param name="close">Runs when the strip's close button is pressed: the layout owner leaves the Band view.</param>
    public MainWindowBandChrome(Window window, MainWindowHeaderModes header, MainWindowDocumentPanes panes, MainWindowSettings? settings, Action close)
    {
        _window = window;
        _header = header;
        _panes = panes;
        _settings = settings;
        _close = close;
    }

    /// <summary>The band block the layout choices live in: the saved settings, or this session's own when the window runs without settings.</summary>
    private BandSettings Band => _settings?.Settings.Timeline.Band ?? _sessionBand;

    /// <summary>Whether the Band view's header mode is on.</summary>
    public bool IsOn => _header.IsOn(HeaderMode.Band);

    /// <summary>
    /// Turns the Band view's header mode on for the Band view the dock just made, with the saved layout choices applied to its cards.
    /// <paramref name="layoutBefore"/> is the dock as it was before the Band layout replaced it.
    /// </summary>
    public void Enter(DockWorkspaceState layoutBefore)
    {
        if (_panes.Band is null) return;
        _panes.Band.ApplyChoices(Choices());
        _header.Enter(HeaderMode.Band, Handlers(), layoutBefore);
    }

    /// <summary>Turns the Band view's header mode off; the strip and the shared hiding go to the other mode still on, if any.</summary>
    public void Leave() => _header.Leave(HeaderMode.Band);

    private HeaderModeHandlers Handlers() => new(
        () => _panes.Band?.Song,
        index => _panes.Band?.IsShown(index) ?? false,
        index => _panes.Band?.ToggleShown(index),
        name => string.Format(PillToolTipFormat, name),
        OpenMenu,
        _close);

    private void OpenMenu(Control anchor, PlacementMode placement)
    {
        var hotkeys = HotkeyTip.GetSource(_window)?.Invoke() ?? new HotkeySettings();
        var menu = BandViewMenu.Build(Choices(), RunOption, _window, hotkeys);
        menu.Placement = placement;
        menu.Open(anchor);
    }

    private BandLayoutChoices Choices() => new(Band.FretboardAbove, Band.ColourFrame);

    /// <summary>Runs the option the Band view's menu picked, then applies and saves the choices it changed.</summary>
    private void RunOption(string option)
    {
        switch (option)
        {
            case BandViewMenu.FretboardBesideOption: Band.FretboardAbove = false; break;
            case BandViewMenu.FretboardAboveOption: Band.FretboardAbove = true; break;
            case BandViewMenu.ColourFrameOption: Band.ColourFrame = !Band.ColourFrame; break;
            case BandViewMenu.ResetOption:
                Band.FretboardAbove = true;
                Band.ColourFrame = true;
                Band.Fretboards = BandChoices.FretboardsFirst;
                _panes.Band?.ResetView();
                break;
        }
        _panes.Band?.ApplyChoices(Choices());
        _settings?.SaveSettings();
    }
}

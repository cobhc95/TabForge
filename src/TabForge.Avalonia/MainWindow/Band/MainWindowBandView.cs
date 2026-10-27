using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Avalonia;

// Owns: View > Band view (the View.BandView command): enters the Band layout (and the Band view's header mode: the track strip, the hidden menu
//   row, the compact timeline), or puts back the exact panels it replaced (the Compose layout when the app started in Band), or closes the Band panel
//   when it was docked by hand in another layout; the check of its row; and the exit of the header mode when the Band panel is closed by hand.
// Does not own: the layouts and their rules (DockLayoutCatalog, DockWorkspaceRules), the dock tree (DockWorkspaceController), the Band pane
//   (registered in MainWindow), the menu rows (MainMenuTable), the Keyboard mode layout swap, the header mode's chrome (MainWindowBandChrome,
//   MainWindowHeaderModes).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests, TabForge.Avalonia.Tests.BandViewTests, TabForge.Avalonia.Tests.CompactBandHeaderTests.
/// <summary>The View > Band view toggle, bound to the window, its dock and the saved layout choice.</summary>
public sealed class MainWindowBandView
{
    /// <summary>The View menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { CommandId }.Concat(FeatureHotkeyIds.BandViewRowIds()).ToArray();

    private const string CommandId = "View.BandView";
    private const string BandPanelId = "band";
    private const string BandLayoutName = "Band";
    private const string ReturnLayoutName = "Default";

    private readonly Window _window;
    private readonly DockWorkspaceController _dock;
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowHeaderModes _header;
    private readonly MainWindowSettings? _settings;
    private readonly MainWindowBandChrome _chrome;
    private string? _lastLayoutWithoutSettings;
    private (string? Name, DockWorkspaceState State)? _layoutBeforeBand;

    private MainWindowBandView(Window window, DockWorkspaceController dock, MainWindowDocumentPanes panes, MainWindowHeaderModes header, MainWindowSettings? settings)
    {
        _window = window;
        _dock = dock;
        _panes = panes;
        _header = header;
        _settings = settings;
        _chrome = new MainWindowBandChrome(window, header, panes, settings, () => Toggle());
    }

    /// <param name="commands">Receives the View.BandView handler.</param>
    /// <param name="window">The window whose View menu row shows the check.</param>
    /// <param name="dock">The dock the Band layout is applied to.</param>
    /// <param name="panes">The panes the Band view's cards and the Arrangement come from.</param>
    /// <param name="header">The header strip both header modes share, and the hiding they hold.</param>
    /// <param name="settings">The saved layouts, the last layout name and the band choices; null keeps them for this session.</param>
    /// <param name="fretboardMenu">The window's main-mode fretboard menu, taken by every card's board (null leaves the cards without one).</param>
    /// <param name="findScale">What the cards' menu "Find scale…" row runs: the window's scale finder (null keeps the fretboard pane's own).</param>
    public static void Register(MainWindowCommands commands, Window window, DockWorkspaceController dock, MainWindowDocumentPanes panes, MainWindowHeaderModes header, MainWindowSettings? settings, Action<string> status,
        FretboardContextMenu? fretboardMenu = null, Action? findScale = null)
    {
        var band = new MainWindowBandView(window, dock, panes, header, settings);
        commands.Add(CommandId, band.Toggle);
        RegisterBandHotkeys(commands, panes, settings, status);
        // The menu row is built after Register, so its check takes the panel's state when the window opens.
        window.Opened += (_, _) => MainWindowViewMenu.SetRowChecks(window, CommandId, dock.IsPanelVisible(BandPanelId));
        dock.LayoutChanged += _ => band.OnDockChanged();
        FollowSettingsFor(panes.Band, settings);
        panes.BandCreated += view => FollowSettingsFor(view, settings);
        ScoreMenuFor(panes.Band, window, settings, status);
        panes.BandCreated += view => ScoreMenuFor(view, window, settings, status);
        FretboardFor(panes.Band, settings, fretboardMenu, findScale);
        panes.BandCreated += view => FretboardFor(view, settings, fretboardMenu, findScale);
        if (settings is not null)
        {
            // The Fretboard settings' rows act on the cards' boards too (the same menu the main pane shows).
            void ApplyLook(TabForge.Services.AppSettings live)
            {
                FretboardFor(panes.Band, settings, fretboardMenu, findScale);
                panes.Band?.ApplyPlayheadLine();   // the "Playhead line" row; repaints a card only when its reach changed
                panes.Band?.ApplyRowMinHeight();   // the "Band row minimum height" row; fits again only when it changed
            }
            settings.SettingsChanged += ApplyLook;
            window.Closed += (_, _) => settings.SettingsChanged -= ApplyLook;
        }
    }

    /// <summary>
    /// Gives the Band view's cards their boards: the window's main-mode fretboard menu (each card names its own track in it), what its
    /// "Find scale…" row runs, and the look the Fretboard settings describe.
    /// </summary>
    private static void FretboardFor(BandView? view, MainWindowSettings? settings, FretboardContextMenu? menu, Action? findScale)
    {
        if (view is null) return;
        view.FindScale = findScale;
        view.FretboardMenu = menu;
        view.InstrumentLook = FretboardSettingsBinding.Look.From(settings?.Settings ?? SettingsDefaults.Fallback());
    }

    /// <summary>The cards' score menu acts on the Band view's own score settings (the timeline settings' band block), saved with the settings.</summary>
    private static void ScoreMenuFor(BandView? view, Window window, MainWindowSettings? settings, Action<string> status)
    {
        if (view is null) return;
        var sessionBand = new BandSettings();
        BandSettings Band() => settings is null ? sessionBand : settings.Settings.Timeline.Band ??= new BandSettings();
        view.ScoreMenu = new BandScoreMenu(view, Band, () => settings?.SaveSettings(),
            () => MainWindowSettings.ShowPreferencesPage(settings, window, SettingsCatalog.Timeline, BandScoreMenu.SettingsRow), status);
        view.FretboardSize = new BandFretboardSize(Band, () => settings?.SaveSettings());
    }

    /// <summary>The score menu of the Band view while its panel shows (the zoom hotkeys act on its cards then), else null (they act on the main score).</summary>
    public static BandScoreMenu? ActiveScoreMenu(DockWorkspaceController dock, MainWindowDocumentPanes panes) =>
        dock.IsPanelVisible(BandPanelId) ? panes.Band?.ScoreMenu : null;

    /// <summary>The last layout name: the saved one, or the session's own when the window runs without settings.</summary>
    private string? LastLayout
    {
        get => _settings is null ? _lastLayoutWithoutSettings : _settings.Settings.LastLayout;
        set
        {
            if (_settings is null) _lastLayoutWithoutSettings = value;
            else _settings.Settings.LastLayout = value;
        }
    }

    private bool IsBandLayoutChosen => string.Equals(LastLayout, BandLayoutName, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<SavedLayout> SavedLayouts => _settings?.Settings.SavedLayouts ?? Enumerable.Empty<SavedLayout>();

    /// <summary>View > Band view: enters the Band layout, or goes back to the exact panels it replaced.</summary>
    private void Toggle()
    {
        // Perf E2b (H4/H5): the entry applies a layout + hides N side panels; batch into one Show (was N rebuilds).
        // The header chrome enters after the batch (as before, when Shows ran immediately): it needs the Band view
        // to exist (created eagerly below) and the dock to show it.
        DockWorkspaceState? layoutBeforeBand = null;
        using (_dock.DeferRefresh())
        {
            if (_dock.IsPanelVisible(BandPanelId))
            {
                if (IsBandLayoutChosen) LeaveBandLayoutBatched();
                else _dock.SetPanelVisible(BandPanelId, false);   // docked by hand in another layout: only close it
            }
            else
            {
                // Perf E2b (H4/H5): the batched Show runs at the end of Toggle, but the header chrome needs the
                // Band view to exist now (was created by the immediate Show before). Create it eagerly; the Show reuses it.
                _ = _panes.NewBand();
                // The current panels as they are (the first-start layout has no name), not the named layout rebuilt.
                _layoutBeforeBand = IsBandLayoutChosen ? null : (LastLayout, _dock.State);
                layoutBeforeBand = _dock.SnapshotLayout();
                SwitchTo(BandLayoutName);
                _header.HideSidePanelsHeldByModes();
            }
        }
        if (layoutBeforeBand is not null) _chrome.Enter(layoutBeforeBand);
        MainWindowViewMenu.SetRowChecks(_window, CommandId, _dock.IsPanelVisible(BandPanelId));
    }

    /// <summary>
    /// The Band panel was closed by hand (its tab's Close or the Panels menu): the panels the Band layout replaced come back, so the workspace is
    /// never left as the Band layout's remainder (the arrangement alone, with the score and the fretboard gone and empty space under the panes).
    /// DockLayoutController.RefreshDockPanelsMenu restores the same layout once the workspace holds no score, no Band view and no Keyboard mode.
    /// </summary>
    private void OnDockChanged()
    {
        if (!_chrome.IsOn || _dock.IsPanelVisible(BandPanelId)) return;
        if (_layoutBeforeBand is not null || DockLayoutCatalog.NeedsScoreBack(_dock.State)) LeaveBandLayout();
        else _chrome.Leave();
        MainWindowViewMenu.SetRowChecks(_window, CommandId, false);
    }

    /// <summary>
    /// Puts back the panels the Band layout replaced, or the Compose layout when the app started in Band. The side panels compact mode hid stay
    /// hidden while it is still on, and a timeline still held compact is fitted again.
    /// </summary>
    private void LeaveBandLayout()
    {
        // Perf E2b (H4/H5): Leave also applies a layout + re-hides held panels; batch into one Show.
        using (_dock.DeferRefresh()) LeaveBandLayoutBatched();
    }

    /// <summary>Perf E2b (H4/H5): the batched body of <see cref="LeaveBandLayout"/> (called inside a refresh scope by Toggle).</summary>
    private void LeaveBandLayoutBatched()
    {
        _chrome.Leave();
        if (_layoutBeforeBand is { } before)
        {
            _layoutBeforeBand = null;
            LastLayout = before.Name;
            _dock.ApplyLayout(before.State);
            _settings?.SaveSettings();
        }
        else if (DockLayoutCatalog.FindSaved(SavedLayouts, ReturnLayoutName) is not null) SwitchTo(ReturnLayoutName);
        else
        {
            // No panels kept before the Band view: the default workspace with the fretboard above the score (not Compose's score-above).
            LastLayout = ReturnLayoutName;
            _dock.ApplyLayout(DockLayoutCatalog.InstrumentTopLayout());
            _settings?.SaveSettings();
        }
        _header.HideSidePanelsHeldByModes();
        if (_header.AnyOn) _header.RefitTimeline();
    }

    // The Band view's own follow (when it does not follow like the score), reused every frame so a frame allocates nothing; UI thread only.
    private static readonly FollowSettings BandOwnFollow = new();
    private static readonly FollowSettings DefaultFollow = new();

    /// <summary>The Band view follows the playhead with the score's Follow settings (Follow like the score), else with its own smooth / page choice; read at each frame (so a change applies at once).</summary>
    private static void FollowSettingsFor(BandView? view, MainWindowSettings? settings)
    {
        if (view is null) return;
        view.FollowSource = () =>
        {
            var follow = settings?.Settings.Follow ?? DefaultFollow;
            var band = settings?.Settings.Timeline?.Band;
            if (band is null || band.FollowsScore) return follow;
            BandOwnFollow.FollowPlayback = follow.FollowPlayback;
            BandOwnFollow.Mode = band.SmoothFollow ? FollowModes.Smooth : FollowModes.Jump;
            return BandOwnFollow;
        };
    }

    /// <summary>The Band view's hotkeys (Band.*): each acts on the Band view showing now (nothing when it is closed), saves the band settings and reports on the status notice.</summary>
    private static void RegisterBandHotkeys(MainWindowCommands commands, MainWindowDocumentPanes panes, MainWindowSettings? settings, Action<string> status)
    {
        BandSettings Band()
        {
            var timeline = settings?.Settings.Timeline;
            if (timeline is null) return new BandSettings();
            return timeline.Band ??= new BandSettings();
        }
        void Remember(BandSettings band, string text, Action? apply = null)
        {
            settings?.SaveSettings();
            apply?.Invoke();
            status(text);
        }
        // Rows per screen is saved for the settings (1 to 5); the cards size themselves to the view, so a change takes no relayout here.
        void ChangeRows(int delta)
        {
            var band = Band();
            band.RowsPerScreen = Math.Clamp(band.RowsPerScreen + delta, RowsPerScreenMin, RowsPerScreenMax);
            Remember(band, $"Band view: {band.RowsPerScreen} row{(band.RowsPerScreen == 1 ? "" : "s")} per screen");
        }
        commands.Add(FeatureHotkeyIds.BandToggleTrackRow, () => { var view = panes.Band; if (view is not null) view.ToggleShown(view.SelectedTrackIndex); });
        commands.Add(FeatureHotkeyIds.BandRowsMore, () => ChangeRows(1));
        commands.Add(FeatureHotkeyIds.BandRowsFewer, () => ChangeRows(-1));
        commands.Add(FeatureHotkeyIds.BandCycleLaneContent, () =>
        {
            var band = Band();
            band.LaneContent = BandChoices.Next(BandChoices.Contents, BandChoices.NormalizeContent(band.LaneContent));
            Remember(band, "Band view lanes: " + band.LaneContent, () => panes.Band?.ApplyScoreSettings());
        });
        commands.Add(FeatureHotkeyIds.BandCycleLaneLayout, () =>
        {
            var band = Band();
            band.LaneLayout = BandChoices.Next(BandChoices.Layouts, BandChoices.NormalizeLayout(band.LaneLayout));
            Remember(band, "Band view lane layout: " + band.LaneLayout, () => panes.Band?.ApplyScoreSettings());
        });
        commands.Add(FeatureHotkeyIds.BandCycleInstrumentSize, () =>
        {
            var band = Band();
            band.InstrumentSize = BandChoices.Next(BandChoices.Sizes, BandChoices.NormalizeSize(band.InstrumentSize));
            Remember(band, "Band view instruments: " + band.InstrumentSize);
        });
        commands.Add(FeatureHotkeyIds.BandToggleSmoothFollow, () =>
        {
            var band = Band();
            band.FollowLikeScore = !band.FollowsScore;
            Remember(band, "Band view follow: " + (band.FollowsScore ? "like the score" : band.SmoothFollow ? "smooth" : "page by page"));
        });
        commands.Add(FeatureHotkeyIds.BandResetRowHeights, () =>
        {
            var band = Band();
            Remember(band, "Band view: row heights reset", () => panes.Band?.ResetRowHeights());
        });
        commands.Add(FeatureHotkeyIds.BandToggleFretboardAbove, () =>
        {
            var band = Band();
            band.FretboardAbove = !band.FretboardAbove;
            Remember(band, "Band view: fretboard " + (band.FretboardAbove ? "above" : "beside") + " the tab",
                () => panes.Band?.ApplyChoices(new BandLayoutChoices(band.FretboardAbove, band.ColourFrame)));
        });
        commands.Add(FeatureHotkeyIds.BandToggleColourFrame, () =>
        {
            var band = Band();
            band.ColourFrame = !band.ColourFrame;
            Remember(band, "Band view track colour frame: " + (band.ColourFrame ? "on" : "off"),
                () => panes.Band?.ApplyChoices(new BandLayoutChoices(band.FretboardAbove, band.ColourFrame)));
        });
    }

    /// <summary>The fewest and most Band rows on the screen at once.</summary>
    private const int RowsPerScreenMin = 1, RowsPerScreenMax = 5;

    /// <summary>Applies a built-in or saved layout by name and records it as the last layout.</summary>
    private void SwitchTo(string name)
    {
        if (DockLayoutCatalog.Resolve(SavedLayouts, name) is not { } layout) return;
        LastLayout = layout.Name;
        _dock.ApplyLayout(layout.State);
        _settings?.SaveSettings();
    }
}

using System;
using System.Collections.Generic;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the score right-click menu of a Band card: its state (read from the band settings, not the card's view) and what each row does. Show
//   standard notation sets the band lane content (Tab / Both), the outline and the page layout rows set the band's own outline and layout, Zoom in
//   and out change the band lane zoom (the value Ctrl+wheel changes in the Band view), and Score settings... opens Preferences at the
//   Band view group. Every change is saved and applied to all cards; none touches the main score's settings.
// Does not own: the rows (Core ScoreMenus), the stored values (BandSettings), the cards (BandView, BandTrackPanel), the Preferences window.
// Tests: TabForge.Avalonia.Tests.BandScoreMenuTests.
public sealed class BandScoreMenu : IMenuCommandRunner
{
    private readonly Func<BandSettings> _band;
    private readonly Action _save;
    private readonly Action _openSettings;
    private readonly Action<string> _status;
    private static readonly HashSet<string> Ids = new()
    {
        ScoreMenus.NotationId, ScoreMenus.OutlineId, ScoreMenus.ZoomInId, ScoreMenus.ZoomOutId, ScoreMenus.FitWidthId, ScoreMenus.SettingsId,
        ContextMenuRows.ScoreLayoutIdPrefix + "page", ContextMenuRows.ScoreLayoutIdPrefix + "continuous",
        ContextMenuRows.ScoreLayoutIdPrefix + "vertical", ContextMenuRows.ScoreLayoutIdPrefix + "horizontal",
    };

    /// <summary>The status line after a zoom change, with the percentage in {0}.</summary>
    private const string ZoomStatusFormat = "Band view lane zoom: {0}%";

    private readonly BandView _view;

    /// <summary>The Preferences row Score settings... scrolls to: the first row of the Band view group.</summary>
    public const string SettingsRow = "band.lanecontent";

    /// <param name="band">The band settings block the menu reads and writes.</param>
    /// <param name="save">Saves the settings after a change.</param>
    /// <param name="openSettings">Opens Preferences at the Band view group.</param>
    /// <param name="status">Shows a line in the status notice (the lane zoom after a zoom row).</param>
    public BandScoreMenu(BandView view, Func<BandSettings> band, Action save, Action openSettings, Action<string> status)
    {
        _view = view;
        _band = band;
        _save = save;
        _openSettings = openSettings;
        _status = status;
    }

    /// <summary>Gives a card's score this menu and the band settings' look.</summary>
    public void Bind(BandTrackPanel panel)
    {
        panel.Staff.ContextMenus.Commands = this;
        panel.Staff.MenuStateSource = State;
        Apply(panel);
    }

    /// <summary>The band lane zoom (1 = the zoom at which three cards fit).</summary>
    public double LaneZoom => BandChoices.ClampZoom(_band().LaneZoom);

    /// <summary>The menu state the band settings hold.</summary>
    public ScoreEmptyState State()
    {
        var band = _band();
        return new ScoreEmptyState(OverBeat: false, CanPaste: false, ShowNotation: BandChoices.NormalizeContent(band.LaneContent) != BandChoices.Tab,
            Continuous: band.ContinuousLayout, Horizontal: BandChoices.NormalizeLayout(band.LaneLayout) == BandChoices.Horizontal, ShowOutline: band.ShowNoteOutline);
    }

    /// <summary>Draws a card's score as the band settings say: content, outline, page layout and lane zoom.</summary>
    public void Apply(BandTrackPanel panel)
    {
        var band = _band();
        var staff = panel.Staff;
        staff.Notation = BandChoices.NormalizeContent(band.LaneContent) switch
        {
            BandChoices.Notation => NotationMode.StaffOnly,
            BandChoices.Both => NotationMode.TabAndStaff,
            _ => NotationMode.TabOnly,
        };
        staff.Probe.Appearance.ShowNoteOutline = band.ShowNoteOutline;
        staff.Probe.Appearance.CenterSystems = band.ContinuousLayout;
        staff.Probe.HorizontalScroll = BandChoices.NormalizeLayout(band.LaneLayout) == BandChoices.Horizontal;
        staff.Probe.Appearance.PlayheadFullRow = BandChoices.NormalizePlayhead(band.PlayheadLine) == BandChoices.FullRow;
        staff.Refresh();
    }

    /// <summary>Applies only the "Playhead line" row to a card (a Preferences Apply); the card repaints only when the line's reach changed.</summary>
    public void ApplyPlayheadLine(BandTrackPanel panel)
    {
        var full = BandChoices.NormalizePlayhead(_band().PlayheadLine) == BandChoices.FullRow;
        if (panel.Staff.Probe.Appearance.PlayheadFullRow == full) return;
        panel.Staff.Probe.Appearance.PlayheadFullRow = full;
        panel.Staff.Refresh();
    }

    /// <summary>Ctrl+wheel over the cards: one lane zoom step in (up) or out, the same value the menu's Zoom rows change.</summary>
    public void WheelZoom(bool zoomIn) => Run(zoomIn ? ScoreMenus.ZoomInId : ScoreMenus.ZoomOutId);

    public bool Handles(string commandId) => Ids.Contains(commandId);

    public void Run(string commandId, string? argument = null)
    {
        var band = _band();
        switch (commandId)
        {
            case ScoreMenus.SettingsId: _openSettings(); return;
            case ScoreMenus.NotationId: band.LaneContent = BandChoices.NormalizeContent(band.LaneContent) == BandChoices.Tab ? BandChoices.Both : BandChoices.Tab; break;
            case ScoreMenus.OutlineId: band.ShowNoteOutline = !band.ShowNoteOutline; break;
            case ScoreMenus.ZoomInId: band.LaneZoom = BandChoices.ClampZoom(band.LaneZoom * BandChoices.LaneZoomStep); break;
            case ScoreMenus.ZoomOutId: band.LaneZoom = BandChoices.ClampZoom(band.LaneZoom / BandChoices.LaneZoomStep); break;
            case ScoreMenus.FitWidthId: band.LaneZoom = 1; break;
            case var id when id == ContextMenuRows.ScoreLayoutIdPrefix + "page": band.ContinuousLayout = false; break;
            case var id when id == ContextMenuRows.ScoreLayoutIdPrefix + "continuous": band.ContinuousLayout = true; break;
            case var id when id == ContextMenuRows.ScoreLayoutIdPrefix + "vertical": band.LaneLayout = BandChoices.Vertical; break;
            case var id when id == ContextMenuRows.ScoreLayoutIdPrefix + "horizontal": band.LaneLayout = BandChoices.Horizontal; break;
            default: throw new InvalidOperationException($"No handler for command '{commandId}'.");
        }
        _save();
        _view.ApplyScoreSettings();
        if (commandId is ScoreMenus.ZoomInId or ScoreMenus.ZoomOutId or ScoreMenus.FitWidthId) _status(string.Format(ZoomStatusFormat, (int)Math.Round(LaneZoom * 100)));
    }
}

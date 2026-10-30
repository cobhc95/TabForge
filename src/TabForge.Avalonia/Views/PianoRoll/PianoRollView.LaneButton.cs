using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Automation;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Menus;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the lane button at the left end of the lane strip under the grid: a toolbar-size dropdown button (PianoRollToolbarLayout, PianoRollToolLook's
//   field look: a filled, bordered button, the accent edge while open or focused) labelled "LANE: <NAME>" (the compact mode caption's DropdownCaption: upper case, drawn chevron after the label), in the accent-tinted look
//   while a controller lane is chosen, and the menu it opens (PianoRollControllers.Lanes, the standard tick on the current lane, "+ Any CC...", opening
//   under the button, left-aligned). The button is never disabled: on a tab track its menu lists the controller rows disabled under a note line. The menu takes
//   the button's theme (its logical parent is the button).
// Does not own: switching the lane (ShowLane, through the PianoRoll.Lane command), the strip's paint, its collapse (PianoRollSurface, and the chevron
//   of PianoRollView.LaneToggle).
// Tests: TabForge.Avalonia.Tests.PianoRollLaneButtonTests, PianoRollLaneRealClickTests.
public sealed partial class PianoRollView
{
    private readonly Button _laneButton = new() { Name = "PianoRollLaneButton" };
    private readonly TextBlock _laneText = DropdownCaption.Caption("");
    private const double None = 0;
    private ContextMenu? _laneList;
    private Action _laneRepaint = () => { };

    /// <summary>The lane picker's tooltip and its automation name.</summary>
    internal const string LaneTip = "Choose what the lane below the notes shows (velocity, volume, pan, program change…)", LanePickerName = "Lane picker";

    /// <summary>The menu the lane button opened, while it is open (a test reads its rows and theme).</summary>
    internal ContextMenu? LaneMenu => _laneList;

    internal Control BuildLaneButton()
    {
        _laneText.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
        _laneButton.Content = DropdownCaption.Content(_laneText);   // the compact mode caption's upper-case text and drawn chevron
        PianoRollToolbarLayout.StyleButton(_laneButton);   // the toolbar's control height, least width and centred content
        _laneButton.MinWidth = PianoRollSurface.KeyStripSize;   // as wide as the key strip; a longer name widens it
        _laneButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _laneButton.HorizontalAlignment = HorizontalAlignment.Left;
        _laneButton.VerticalAlignment = VerticalAlignment.Bottom;
        _laneRepaint = PianoRollToolLook.Apply(_laneButton, () => _laneList?.IsOpen == true, () => ShownLane != PianoRollControllers.Velocity, field: true);
        ToolTip.SetTip(_laneButton, LaneTip);
        AutomationProperties.SetName(_laneButton, LanePickerName);
        _laneButton.Click += (_, _) => OpenLaneList();
        PlaceLaneButton();
        RefreshLaneButton();
        return _laneButton;
    }

    /// <summary>The label follows the lane shown. The button is never disabled (a disabled control lets its press fall through to the strip).</summary>
    internal void RefreshLaneButton()
    {
        _laneText.Text = PianoRollControllers.ButtonName(ShownLane).ToUpperInvariant();
        _laneRepaint();
    }

    /// <summary>Keeps the button and the chevron on the strip (the strip sits above the lane area, which may be collapsed).</summary>
    private void PlaceLaneButton()
    {
        _laneButton.SetValue(Layoutable.MarginProperty, UiMetrics.Sides(None, None, None, Surface.LaneShown ? Surface.Lane : None, 1));
        PlaceLaneToggle();
    }

    /// <summary>The menu rows: every lane (the standard tick on the current one) and "+ Any CC...". On a tab track the wrapped note line comes first and
    /// the controller rows and "+ Any CC..." are disabled (only Velocity is chosen there).</summary>
    internal List<MenuSpec> LaneRows()
    {
        var rows = new List<MenuSpec>();
        foreach (var (id, name) in PianoRollControllers.Lanes)
            rows.Add(new MenuSpec
            {
                Id = LaneCommand, Arg = id.ToString(CultureInfo.InvariantCulture), Header = name,
                Checkable = true, Checked = id == ShownLane, NoSetting = "the lane the piano roll shows",
            });
        rows.Add(MenuSpec.Separator());
        rows.Add(new MenuSpec { Id = LaneCommand, Arg = AnyCc, Header = "+ Any CC...", NoSetting = "the lane the piano roll shows" });
        return rows;
    }

    private void OpenLaneList()
    {
        if (Presenter.Track is null) return;
        _laneList = ContextMenuBuilder.Build(LaneRows(), this);
        UiMetrics.PlaceUnderLeft(_laneList);
        _laneList.PlacementTarget = _laneButton;
        _laneList.Opened += (_, _) => _laneRepaint();
        _laneList.Closed += (_, _) => { _laneRepaint(); Surface.Focus(); };
        _laneList.Open(_laneButton);
    }
}

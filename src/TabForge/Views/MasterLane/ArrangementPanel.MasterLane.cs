using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views;

// Owns: the track list's half of the master track lane: its shown state, the "Master" row, the settings it applies and the
//   "Show master track" menu item and command.
// Does not own: the lane's rules (MasterLane.cs) and the timeline's half (TrackTimeline.MasterLane.cs).
// Tests: TestMasterLane.

public sealed partial class ArrangementPanel
{
    private bool _masterLaneShown;
    private Border? _masterRow;
    private TimelineSettings? _masterSettings;
    private Action? _masterSettingChanged;

    /// <summary>The overview lane above track 1 (Preferences > Timeline &amp; Tracks, and the track list's right-click menu).</summary>
    public bool ShowMasterLane
    {
        get => _masterLaneShown;
        set
        {
            if (_masterLaneShown == value) return;
            _masterLaneShown = value;
            MarkMasterLane();
            RefreshMasterLane();
        }
    }

    /// <summary>Rebuilds the rows and lanes after the lane appeared, went or changed height.</summary>
    private void RefreshMasterLane()
    {
        if (_project is null) return;
        RebuildControls();
        UpdateDragVisual();   // a rebuild drops the selected row's highlight
        RefreshTimelineExtent();
        _timeline.InvalidateMeasure();
        _timeline.InvalidateVisual();
        RefreshTimelineGeometry();
        LayoutPlayhead();
    }

    /// <summary>The track list's half of the lane (null when hidden): test and render hook.</summary>
    internal FrameworkElement? MasterLaneRow => _masterRow;

    /// <summary>Applies the three settings; <paramref name="changed"/> runs after the menu item changed the setting (the host saves and re-fits).</summary>
    public void ApplyMasterLane(TimelineSettings settings, Action? changed = null)
    {
        _masterSettings = settings;
        _masterSettingChanged = changed ?? _masterSettingChanged;
        _timeline.MasterLaneByTrack = settings.MasterTrackColours;
        // The default colour follows the theme (light lines on dark, dark lines on light); any other colour is used as chosen.
        _timeline.MasterLaneColour = !SettingsColor.IsValid(settings.MasterTrackColour) || string.Equals(settings.MasterTrackColour, MasterLane.DefaultColour, StringComparison.OrdinalIgnoreCase)
            ? null : (Color)ColorConverter.ConvertFromString(settings.MasterTrackColour);
        _timeline.MasterLaneOpacity = Math.Clamp(settings.MasterTrackOpacityPercent, MasterLane.MinOpacityPercent, MasterLane.MaxOpacityPercent) / 100.0;
        ShowMasterLane = settings.ShowMasterTrack;
    }

    /// <summary>The bound song shows the lane or not, as this panel does.</summary>
    private void MarkMasterLane() { if (_project is { } p) MasterLane.SetShown(p, _masterLaneShown, _compactMode); }

    /// <summary>Shows or hides the lane from a menu and stores the setting.</summary>
    internal void SetMasterLaneFromMenu(bool on)
    {
        if (_masterSettings is { } settings) settings.ShowMasterTrack = on;
        ShowMasterLane = on;
        _masterSettingChanged?.Invoke();
    }

    /// <summary>Runs a list-wide view command of the track row menu; false when the command is not one.</summary>
    internal bool RunViewCommand(MenuSpec spec)
    {
        if (spec.Id != MasterLane.CommandId) return false;
        SetMasterLaneFromMenu(!spec.Checked);   // the spec carries the current state
        return true;
    }

    private MenuItem MasterLaneItem()
    {
        var shown = _masterLaneShown;
        var item = new MenuItem
        {
            Header = MasterLane.MenuHeader, IsCheckable = true, IsChecked = shown,
            Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
            ToolTip = "Show an overview lane above the first track: one thin line per track, filled where that track plays"
        };
        item.Click += (_, _) => SetMasterLaneFromMenu(!shown);
        return item;
    }

    /// <summary>The "Master" row above track 1 (null when the lane is hidden).</summary>
    private Border? BuildMasterLaneRow()
    {
        _masterRow = null;
        if (!MasterLane.IsShown(_project)) return null;
        var text = new TextBlock
        {
            Text = MasterLane.Label, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        var row = new Border { Height = MasterLane.OffsetOf(_project), BorderThickness = RowBorder, Child = MasterLaneControls(text), Focusable = false };
        row.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        row.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        AutomationProperties.SetName(row, "Master track overview");
        TrackRowHover.AttachMaster(row);
        return _masterRow = row;
    }

    private void SizeMasterLaneRow() { if (_masterRow is { } row) row.Height = MasterLane.OffsetOf(_project); }
}

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the layout mode of the mixer (Wide/Medium vs Compact) chosen from the rows viewport width, and the compact row menu
//     that keeps the controls the rows drop (MIDI, pitch, the full subtitle).
// Does not own: the columns (MixerColumnLayout), the rows (MixerWindow.cs) or the values (the song and its presenters).
// Tests: TabForge.Avalonia.Tests.MixerColumnLayoutTests.
public sealed partial class MixerWindow
{
    private bool _compact;
    private ScrollViewer? _rowsViewport;
    private bool _evaluating;
    private TextBlock? _topBarLabel;
    private readonly List<CheckBox> _topBarChecks = new();
    private ScrollViewer? _topBarScroll;
    private Button? _topBarMenu;
    private bool _updatingSizeLimits;

    /// <summary>The Mixer's inner minimum height, before the UI scale.</summary>
    private const double MinInnerHeight = 320;

    /// <summary>Recomputes the window's outer-DIP minimums when the UI scale changes; the content is measured at 1/scale of them.</summary>
    private void ApplyUiScale(double scale)
    {
        var outer = WindowUiScale.ToOuter(new Size(MixerColumnLayout.MinCompactWidth, MinInnerHeight), scale);
        _updatingSizeLimits = true;
        try { MinWidth = outer.Width; MinHeight = outer.Height; }
        finally { _updatingSizeLimits = false; }
    }

    /// <summary>The saved size clamped to this screen and raised to the minimums, else the working-area default (in outer DIPs).</summary>
    private void ApplyInitialSize(AppSettings? sizing)
    {
        if (sizing is not null && double.IsFinite(sizing.MixerWindowWidth) && double.IsFinite(sizing.MixerWindowHeight)
            && sizing.MixerWindowWidth > 0 && sizing.MixerWindowHeight > 0)
        {
            var remembered = WindowUiScale.ClampRememberedSize(
                new Size(sizing.MixerWindowWidth, sizing.MixerWindowHeight), new Size(MinWidth, MinHeight), this);
            _openingSize = remembered;
            Width = remembered.Width;
            Height = remembered.Height;
            return;
        }
        var scale = WindowUiScale.CurrentScale(this);
        var preferred = WindowUiScale.DefaultSize(this, scale, 1100, 700);
        var fit = WindowUiScale.ClampToWorkingArea(
            new Size(Math.Max(preferred.Width, MinWidth), Math.Max(preferred.Height, MinHeight)), this);
        _openingSize = fit;
        Width = fit.Width;
        Height = fit.Height;
    }

    /// <summary>Test seam: the layout mode chosen from the rows viewport width.</summary>
    internal bool IsCompact => _compact;

    /// <summary>
    /// Follows the rows host's own bounds, the width the window offers the rows whether or not its content fits, so the mode
    /// changes both ways with the window. The hook is detached when the window closes.
    /// </summary>
    private void AttachCompactLayout()
    {
        if (_rowsViewport is null) return;
        _rowsViewport.SizeChanged += OnRowsViewportSizeChanged;
        Closed += OnClosedDetachCompactLayout;
    }

    private void OnRowsViewportSizeChanged(object? sender, SizeChangedEventArgs e) => EvaluateCompact();

    private void OnClosedDetachCompactLayout(object? sender, EventArgs e)
    {
        if (_rowsViewport is not null) _rowsViewport.SizeChanged -= OnRowsViewportSizeChanged;
        Closed -= OnClosedDetachCompactLayout;
    }

    /// <summary>Compact when the rows viewport cannot hold the Medium columns; rebuilds the strips when the mode changes.</summary>
    private void EvaluateCompact()
    {
        if (_evaluating) return;
        // The scroll content's width follows the rows (and is floored by their minimum), so it cannot tell when the window grows
        // again; the rows ScrollViewer's own bounds is the width the window offers, and one threshold applies both ways.
        var available = _rowsViewport?.Bounds.Width ?? 0;
        if (available <= 0) return;
        var compact = available < MixerColumnLayout.MinMediumWidth;
        if (compact == _compact) return;
        _compact = compact;
        ApplyTopBarMode();
        _evaluating = true; // Rebuild evaluates again at its end; that nested call must not rebuild a second time
        try { Rebuild(); }
        finally { _evaluating = false; }
    }

    /// <summary>Compact keeps the options bar on one line: the track-list toggles move into a "..." menu and the row scrolls sideways when it cannot fit.</summary>
    private void ApplyTopBarMode()
    {
        if (_topBarLabel is null || _topBarScroll is null || _topBarMenu is null) return;
        _topBarLabel.IsVisible = !_compact;
        foreach (var box in _topBarChecks) box.IsVisible = !_compact;
        // Disabled keeps today's wrapping; Auto measures the row on one line and scrolls it instead of clipping it.
        _topBarScroll.HorizontalScrollBarVisibility = _compact ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _topBarMenu.IsVisible = _compact;
    }

    /// <summary>The compact bar's "..." button; its menu holds the three track-list toggles from the checkboxes themselves.</summary>
    private Button TopBarMenuButton()
    {
        var menu = MenuStability.CreateContextMenu();
        menu.Opening += (_, _) => { menu.Items.Clear(); FillTopBarMenu(menu); };
        var button = new Button
        {
            Name = "TopBarMenu", Content = "…", Height = 22, MinWidth = 22, Padding = new Thickness(4, 0), Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, IsVisible = false, ContextMenu = menu,
        };
        ToolTip.SetTip(button, "Track-list columns: volume, pan and groups");
        AutomationProperties.SetName(button, "Track-list columns");
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    /// <summary>The track-list toggles as check items; each drives the bar's own checkbox, so menu and checkbox share one handler and one undo step.</summary>
    private void FillTopBarMenu(ContextMenu menu)
    {
        foreach (var box in _topBarChecks)
        {
            var target = box;
            var item = new MenuItem { Header = target.Content, ToggleType = MenuItemToggleType.CheckBox, IsChecked = target.IsChecked == true };
            item.Click += (_, _) => target.IsChecked = target.IsChecked != true;
            menu.Items.Add(item);
        }
    }

    /// <summary>The row-end "..." button of a track or group row; its menu is built on open from the row's current values.</summary>
    private Button RowMenuButton(Action<ContextMenu> fill, string tip)
    {
        var menu = MenuStability.CreateContextMenu();
        menu.Opening += (_, _) => { menu.Items.Clear(); fill(menu); };
        var button = new Button
        {
            Name = "RowMenu", Content = "…", Width = 20, Height = 22, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    /// <summary>A track's compact menu: the MIDI checkbox, the pitch controls and the full subtitle, from the row's current values.</summary>
    private static void FillTrackRowMenu(ContextMenu menu, CheckBox midi, Func<int> pitch, Action<int> setPitch, string subtitle)
    {
        var midiItem = new MenuItem
        {
            Header = "MIDI sound", ToggleType = MenuItemToggleType.CheckBox, IsChecked = midi.IsChecked == true, IsEnabled = midi.IsEnabled,
        };
        // The item drives the row's own checkbox, so menu and checkbox share one handler and one undo step.
        midiItem.Click += (_, _) => midi.IsChecked = midi.IsChecked != true;
        menu.Items.Add(midiItem);
        menu.Items.Add(new Separator());
        AddPitchItems(menu, pitch, setPitch);
        AddSubtitleItem(menu, subtitle);
    }

    /// <summary>A group's compact menu: its pitch controls and the full subtitle.</summary>
    private static void FillGroupRowMenu(ContextMenu menu, Func<int> pitch, Action<int> setPitch, string subtitle)
    {
        AddPitchItems(menu, pitch, setPitch);
        AddSubtitleItem(menu, subtitle);
    }

    /// <summary>The pitch items: - / + / Reset (the stepper's own step rule) and the current value as disabled information.</summary>
    private static void AddPitchItems(ContextMenu menu, Func<int> pitch, Action<int> setPitch)
    {
        var down = new MenuItem { Header = "-" };
        down.Click += (_, _) => setPitch(MixerStripPresenter.StepSemitones(pitch(), -1));
        var up = new MenuItem { Header = "+" };
        up.Click += (_, _) => setPitch(MixerStripPresenter.StepSemitones(pitch(), 1));
        var reset = new MenuItem { Header = "Reset" };
        reset.Click += (_, _) => { if (pitch() != 0) setPitch(0); };
        menu.Items.Add(down);
        menu.Items.Add(up);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = $"Pitch: {MixerStripPresenter.SemitoneText(pitch())}", IsEnabled = false });
    }

    private static void AddSubtitleItem(ContextMenu menu, string subtitle)
    {
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = subtitle, IsEnabled = false });
    }
}

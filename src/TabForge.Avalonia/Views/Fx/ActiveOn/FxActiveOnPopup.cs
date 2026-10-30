using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the "Active on" popup of one plug-in or container: title, subtitle, the Driver type and Output device drop-downs, the "Right now" line and the
//   Clear rule / Cancel / Done buttons, in the app's panel look (theme brushes). Done and Clear apply as one change; Esc, Cancel and a click outside change nothing.
// Does not own: the choices and the answer (FxActiveOnPopupModel), the device names (FxActiveOnDevices), the two ways in (FxChainWindow.ActiveOn).
// Tests: FxActiveOnTests.
// Threads: UI thread; the device rows come from cached lists and are read once more when a first listing settles (never blocks the UI).
internal sealed class FxActiveOnPopup
{
    private readonly FxActiveOnPopupModel _model;
    private readonly Func<ActiveOnDriverKind?, Task>? _settled;
    private readonly Border _panel;
    private readonly ComboBox _drivers = new() { Name = "ActiveOnDrivers", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _devices = new() { Name = "ActiveOnDevices", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { Name = "ActiveOnStatus", TextWrapping = TextWrapping.Wrap };
    private readonly Button _done = new() { Name = "ActiveOnDone", Content = ActiveOnPresenter.DoneText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
    private readonly Button _cancel = new() { Name = "ActiveOnCancel", Content = ActiveOnPresenter.CancelText, MinWidth = DialogMetrics.PromptButtonMinWidth };
    private readonly Button _clear = new() { Name = "ActiveOnClear", Content = ActiveOnPresenter.ClearText };
    private readonly Popup _popup;
    private bool _rebuilding, _closed;

    public FxActiveOnPopup(Control anchor, ActiveOnRule? initial, string? subject,
        Func<ActiveOnDriverKind?, IReadOnlyList<FxActiveOnDevice>> devices, Func<ActiveOnOutput> output,
        Func<ActiveOnDriverKind?, Task>? settled, Action<ActiveOnRule?> applied, PlacementMode placement = PlacementMode.Bottom)
    {
        _settled = settled;
        _model = new FxActiveOnPopupModel(initial, subject, devices, output, applied);
        _panel = Build();
        _popup = DialogMetrics.AnchoredPopup(anchor, _panel, placement);
        _popup.Opened += (_, _) => _drivers.Focus();
        _popup.Closed += (_, _) =>
        {
            _closed = true;
            if (!_model.Finished) _model.Cancel();   // a click outside is a Cancel
            ((ISetLogicalParent)_popup).SetParent(null);
        };
        ((ISetLogicalParent)_popup).SetParent(anchor);   // takes the anchor's theme and resources, as a flyout does (without it the panel is unthemed)
        Fill();
    }

    internal FxActiveOnPopupModel Model => _model;
    internal Popup Popup => _popup;
    internal Border Panel => _panel;
    internal ComboBox DriverBox => _drivers;
    internal ComboBox DeviceBox => _devices;
    internal TextBlock StatusText => _status;
    internal Button DoneButton => _done;
    internal Button CancelButton => _cancel;
    internal Button ClearButton => _clear;

    /// <summary>Shows the popup and reads the device rows again once a first listing settled.</summary>
    public void Open()
    {
        _popup.IsOpen = true;
        if (_settled is not null) _ = RefreshWhenListedAsync();
    }

    public void Close()
    {
        if (!_closed) _popup.IsOpen = false;
    }

    /// <summary>Test seam: picks a driver kind as a click does.</summary>
    internal void ChooseDriver(ActiveOnDriverKind? kind)
    {
        var index = _model.DriverChoices.ToList().FindIndex(c => c.Kind == kind);
        if (index >= 0) _drivers.SelectedIndex = index;
    }

    /// <summary>Test seam: picks an output device by name (null: any device).</summary>
    internal void ChooseDevice(string? name)
    {
        var index = _model.DeviceChoices.ToList().FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _devices.SelectedIndex = index;
    }

    private Border Build()
    {
        var title = new TextBlock { Text = ActiveOnPresenter.Title, Name = "ActiveOnTitle", FontWeight = FontWeight.SemiBold, FontSize = DialogMetrics.ActiveOnTitleSize };
        var subtitle = new TextBlock
        {
            Text = _model.Subtitle, Name = "ActiveOnSubtitle", TextWrapping = TextWrapping.Wrap, FontSize = DialogMetrics.ActiveOnSubtitleSize,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
        }.WithMargin(DialogMetrics.ActiveOnSubtitleMargin);
        _status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        _status.WithMargin(DialogMetrics.ActiveOnStatusMargin);
        AutomationProperties.SetName(_drivers, ActiveOnPresenter.DriverSection);
        AutomationProperties.SetName(_devices, ActiveOnPresenter.DeviceSection);
        AutomationProperties.SetName(_done, ActiveOnPresenter.DoneText);
        AutomationProperties.SetName(_cancel, ActiveOnPresenter.CancelText);
        AutomationProperties.SetName(_clear, ActiveOnPresenter.ClearText);
        _done.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        _cancel.Classes.Add(AvaloniaDefaultLooks.ButtonLook);
        _clear.Classes.Add(AvaloniaDefaultLooks.ButtonLook);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogMetrics.ActiveOnButtonGap, Children = { _cancel, _done } };
        DockPanel.SetDock(right, Dock.Right);
        DockPanel.SetDock(_clear, Dock.Left);
        var buttons = new DockPanel { LastChildFill = false, Children = { _clear, right } }.WithMargin(DialogMetrics.ActiveOnButtonRowMargin);

        var body = new StackPanel();
        body.Children.Add(title);
        body.Children.Add(subtitle);
        body.Children.Add(Label(ActiveOnPresenter.DriverSection, "ActiveOnDriverSection"));
        body.Children.Add(_drivers);
        body.Children.Add(Label(ActiveOnPresenter.DeviceSection, "ActiveOnDeviceSection"));
        body.Children.Add(_devices);
        body.Children.Add(_status);
        body.Children.Add(buttons);

        var panel = new Border
        {
            Width = DialogMetrics.ActiveOnPopupWidth, CornerRadius = new CornerRadius(DialogMetrics.ActiveOnPopupCorner),
            BorderThickness = new(DialogMetrics.ActiveOnPopupBorder), Child = body, Focusable = true, Name = "ActiveOnPanel",
            [!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"),
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
        };
        panel.WithPadding(DialogMetrics.ActiveOnPopupPadding, DialogMetrics.ActiveOnPopupPadding);
        AutomationProperties.SetName(panel, ActiveOnPresenter.Title);

        _drivers.SelectionChanged += (_, _) => OnDriverPicked();
        _devices.SelectionChanged += (_, _) => OnDevicePicked();
        _done.Click += (_, _) => { _model.Done(); Close(); };
        _cancel.Click += (_, _) => { _model.Cancel(); Close(); };
        _clear.Click += (_, _) => { _model.Clear(); Close(); };
        panel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return panel;
    }

    private static TextBlock Label(string text, string name) =>
        new TextBlock { Text = text, Name = name, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") }.WithMargin(DialogMetrics.ActiveOnSectionMargin);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is ComboBox { IsDropDownOpen: true }) return;   // the open list keeps Esc and Enter
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _model.Cancel();
            Close();
        }
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            e.Handled = true;
            _model.Done();
            Close();
        }
    }

    private async Task RefreshWhenListedAsync()
    {
        try { await _settled!(_model.DriverKind); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }   // a listing fault: the rows stay as they are
        if (_closed) return;
        _model.Refresh();
        FillDevices();
    }

    private void Fill()
    {
        _rebuilding = true;
        try
        {
            _drivers.ItemsSource = _model.DriverChoices.Select(c => c.Label).ToList();
            _drivers.SelectedIndex = _model.DriverIndex;
        }
        finally { _rebuilding = false; }
        FillDevices();
    }

    private void FillDevices()
    {
        _rebuilding = true;
        try
        {
            _devices.ItemsSource = _model.DeviceChoices.Select(c => c.Label).ToList();
            _devices.SelectedIndex = _model.DeviceIndex;
        }
        finally { _rebuilding = false; }
        _status.Text = _model.Status;
    }

    private void OnDriverPicked()
    {
        if (_rebuilding || _drivers.SelectedIndex < 0) return;
        _model.DriverKind = _model.DriverChoices[_drivers.SelectedIndex].Kind;
        FillDevices();
    }

    private void OnDevicePicked()
    {
        if (_rebuilding || _devices.SelectedIndex < 0) return;
        _model.DeviceName = _model.DeviceChoices[_devices.SelectedIndex].Name;
        _status.Text = _model.Status;
    }
}

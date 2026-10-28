using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Meters;

// Owns: the master level meter of the transport bar: one drawn control (a rounded square like the transport buttons, two bars, the clip light, the hold
//   lines and the small number), its tooltip and its clicks (a left click resets the clip light, a double click asks for the mixer, a right click opens the menu).
// Does not own: the numbers (MasterMeterModel, fed by MasterMeterWiring), the right-click popup (TransportPopups/MasterMeterPopup), the place in the bar (Platform/TransportBarView).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
// Thread: UI thread. A tick repaints only this control (InvalidateVisual) and only when something on it changed; nothing here touches layout.
/// <summary>The transport bar master meter, the same size as a transport button.</summary>
public sealed partial class MasterMeterView : Control
{
    public const string AutomationName = "Master meter";

    private readonly AvaloniaDrawContext _draw = new();
    private readonly MasterMeterSettings _fallback = new();
    private Func<MasterMeterSettings> _settings;
    private (string Mode, bool ShowNumber) _tipFor;
    private bool _hovered;

    public MasterMeterView()
    {
        _settings = () => _fallback;
        Model = new MasterMeterModel(() => _settings());
        Width = UiMetrics.TransportButtonSize;
        Height = UiMetrics.TransportButtonSize;
        this.WithMargin(1, 1, 1, 1);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        // The right press opens the own menu of the meter: it must not also open the context menu of the pane under it.
        ContextRequested += (_, e) => e.Handled = true;
        DoubleTapped += (_, _) => OpenMixer?.Invoke();
        global::Avalonia.Automation.AutomationProperties.SetName(this, AutomationName);
        ShowTip();
    }

    public MasterMeterModel Model { get; }

    /// <summary>Opens the mixer (a double click); null: the window has no mixer command.</summary>
    public Action? OpenMixer { get; set; }

    /// <summary>The settings object the meter reads at each tick and the menu edits (default: a private default set).</summary>
    public Func<MasterMeterSettings> Settings { get => _settings; set { _settings = value; ShowTip(); InvalidateVisual(); } }

    /// <summary>Runs after the menu changed a choice: the window saves the settings.</summary>
    public Action? Changed { get; set; }

    /// <summary>The latest reading of the engine; null: no engine.</summary>
    public Func<MasterMeterReading?>? Source { get; set; }

    /// <summary>One tick (about every 33 ms while the meter is on screen): reads, feeds the model and repaints this control when something changed.</summary>
    public void Tick(double nowMs)
    {
        if (Model.Feed(Source?.Invoke(), nowMs)) InvalidateVisual();
        var settings = _settings();
        if (_tipFor != (settings.Mode, settings.ShowNumber)) ShowTip();
    }

    /// <summary>After a menu choice or a Preferences change: repaints with the new choices at once and keeps the tooltip in line.</summary>
    public void Refresh()
    {
        ShowTip();
        InvalidateVisual();
    }

    private void ShowTip()
    {
        var settings = _settings();
        _tipFor = (settings.Mode, settings.ShowNumber);
        ToolTip.SetTip(this, MasterMeterModel.Tip(MasterMeterSettings.ParseMode(settings.Mode), settings.ShowNumber));
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hovered = true; InvalidateVisual(); }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hovered = false; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            if (Model.ResetClip()) InvalidateVisual();
        }
        else if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            TransportPopups.MasterMeterPopup.Create(_settings(), () => { Refresh(); Changed?.Invoke(); }, () => { Model.ResetClip(); InvalidateVisual(); }).OpenAbove(this);
        }
    }
}

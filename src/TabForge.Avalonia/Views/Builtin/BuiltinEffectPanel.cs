using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Audio.Contracts;
using TabForge.Audio.Contracts.Builtin;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: what the editors of the built-in effects (EQ, Compressor, Limiter, Delay, Noise Gate) share: a panel that the FX chain window embeds (BuiltinEditorHost) or a BuiltinEditorWindow holds (title, preferred size),
//   the poll of the effect parameters at ChromeMetrics.BuiltinPollMs (about 30 times a second, the master meter's rate: the readouts move with the sound) (one question at a time, asked again after FxParameterPane.LostAfterTicks), handing each answer to the
//   registered controls and to the window (meters), sending a changed value to the engine, and applying a partial state (a factory preset) as one undo step.
// Does not own: the controls (BuiltinParamControls), the graphs and meters, each effect window (EqEditor and the others), the effects (TabForge.AudioEngine.Builtin).
// Tests: BuiltinEditorWindowTests.
// Threads: UI thread only. The poll tick asks the engine; nothing here waits for it.
public abstract class BuiltinEffectPanel : UserControl
{
    /// <summary>The size of a window: the opening size and the smallest.</summary>
    protected readonly record struct Size2(double Width, double Height, double MinWidth, double MinHeight);

    private readonly IFxChainHost _host;
    private readonly IUiThread _ui = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();
    private readonly IUiTimer _poll;
    private readonly List<IParamControl> _controls = [];
    private readonly TextBlock _notice = new() { Name = "FxNotice", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
    private int _waiting;   // ticks the last question is out (0: none)

    /// <summary>The chain slot this editor edits.</summary>
    public PluginSlot Slot { get; }

    /// <summary>The title of a window that holds this editor.</summary>
    public string EffectTitle { get; }

    /// <summary>The opening size and the smallest size (the window opens at it; the embedding area gives the editor its size and scrolls when smaller).</summary>
    public (double Width, double Height, double MinWidth, double MinHeight) PreferredSize { get; }

    protected TrackModel Track { get; }
    protected IReadOnlyList<BuiltinParameter> Descriptors { get; }

    protected BuiltinEffectPanel(IFxChainHost host, TrackModel track, PluginSlot slot, string effectName, IReadOnlyList<BuiltinParameter> descriptors, Size2 size)
    {
        (_host, Track, Slot, Descriptors) = (host, track, slot, descriptors);
        Styles.Add(TrackPillButton.IdleLook());
        EffectTitle = $"{effectName} — {track.Name}";
        PreferredSize = (size.Width, size.Height, size.MinWidth, size.MinHeight);
        (MinWidth, MinHeight) = (size.MinWidth, size.MinHeight);
        FontSize = DialogMetrics.FontSize;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _poll = _ui.CreateTimer(TimeSpan.FromMilliseconds(HasReadouts ? ChromeMetrics.BuiltinPollMs : ChromeMetrics.ParamPollMs));
        _poll.Tick += (_, _) => Ask();
        AttachedToVisualTree += (_, _) =>
        {
            if (_host.Engine is { } engine) engine.ParametersReceived += OnParameters;
            Ask();
            _poll.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _poll.Stop();
            if (_host.Engine is { } e) e.ParametersReceived -= OnParameters;
        };
        if (_host.Engine is not { IsRunning: true }) _notice.Text = "The audio engine is not running: changes apply when it starts.";
    }

    /// <summary>Puts the editor together: <paramref name="body"/> fills it, the engine notice sits under it.</summary>
    protected void Frame(Control body)
    {
        DockPanel.SetDock(_notice, Dock.Bottom);
        Content = DialogMetrics.Inset(new DockPanel { Children = { _notice, body } });
    }

    protected BuiltinParameter Param(uint id) => Descriptors.First(p => p.Id == id);

    protected T Register<T>(T control) where T : IParamControl
    {
        _controls.Add(control);
        return control;
    }

    /// <summary>A slider row for a parameter (label, slider, value text); the label column is the full width unless <paramref name="compact"/>.</summary>
    protected ParamSlider Slider(uint id, string label, string tip, ParamScale scale = ParamScale.Linear, bool compact = false, Func<double, string>? text = null) =>
        Register(new ParamSlider(Param(id), label, tip, scale, compact, text, Send));

    protected ParamChoice Choice(uint id, string label, string tip, IReadOnlyList<string> names, bool compact = false) =>
        Register(new ParamChoice(Param(id), label, tip, names, compact, Send));

    protected ParamSwitch Switch(uint id, string text, string tip) => Register(new ParamSwitch(Param(id), text, tip, Send));

    protected void Send(uint id, double value) => _host.Engine?.SetParameter(Track, Slot, id, value);

    /// <summary>The engine answered: the values to show in the controls and the readouts to show in the meters.</summary>
    protected abstract void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId);

    /// <summary>True when the editor shows live meters (polled at the fast rate); other editors poll slowly.</summary>
    protected virtual bool HasReadouts => false;

    private void Ask()
    {
        if (!IsEffectivelyVisible) return;
        if (_host.Engine is not { IsRunning: true } engine) return;
        if (_waiting > 0 && ++_waiting <= FxParameterPane.LostAfterTicks) return;
        _waiting = 1;
        engine.RequestParameters(Track, Slot);
    }

    private void OnParameters(int engineSlot, int index, IReadOnlyList<PluginParameterInfo> list)
    {
        if (_host.Engine is not { } engine || engineSlot != engine.SlotOf(Track) || index != Track.Rig.Plugins.IndexOf(Slot)) return;
        _waiting = 0;
        var byId = list.ToDictionary(p => p.Id);
        foreach (var control in _controls)
            if (byId.TryGetValue(control.Id, out var p)) control.Show(p.Value);
        ShowValues(byId);
    }

    /// <summary>Applies a partial built-in state (a factory preset) to the engine and keeps it in the slot, as one undo step.</summary>
    protected void ApplyState(byte[] partial)
    {
        _host.Edit(Track, () =>
        {
            Slot.State = Merged(Slot.State, partial);
            _host.Engine?.SetState(Track, Slot, Convert.ToBase64String(partial));
        });
        if (_host.Engine is { IsRunning: true } engine) engine.RequestParameters(Track, Slot);
    }

    private static string Merged(string? state, byte[] partial)
    {
        BuiltinState parsed;
        try { parsed = state is { Length: > 0 } ? BuiltinState.Parse(Convert.FromBase64String(state)) : new BuiltinState(); }
        catch (Exception ex) when (ex is FormatException or System.IO.InvalidDataException) { parsed = new BuiltinState(); } // Not logged: an unreadable old state is replaced
        var add = BuiltinState.Parse(partial);
        foreach (var (key, value) in add.Values) parsed.Values[key] = value;
        foreach (var (key, value) in add.Text) parsed.Text[key] = value;
        return Convert.ToBase64String(parsed.ToBytes());
    }

    /// <summary>A meter row: the name, the bar and the value text.</summary>
    protected static Grid MeterRow(string label, MeterBar bar, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{DialogMetrics.FxMeterLabelWidth},*,{DialogMetrics.FxValueWidth}") };
        Grid.SetColumn(bar, 1);
        Grid.SetColumn(value, 2);
        bar.VerticalAlignment = VerticalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(bar);
        grid.Children.Add(value);
        return grid.Spaced(DialogMetrics.FxRowGap);
    }

    /// <summary>The 0..1 position of a level in dB on a bar that runs from <paramref name="floorDb"/> to 0 dB.</summary>
    protected static double Fraction(double db, double floorDb) => (db - floorDb) / -floorDb;

    /// <summary>A row of controls side by side with the standard gap.</summary>
    protected static WrapPanel Row(params Control[] items)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) row.Children.Add(row.Children.Count == 0 ? item : item.WithMargin(DialogMetrics.FxButtonGap));
        return row.Spaced(DialogMetrics.FxRowGap);
    }
}

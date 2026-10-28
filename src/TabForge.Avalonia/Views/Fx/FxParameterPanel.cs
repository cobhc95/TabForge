using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the controls of the generic plug-in parameter panel: the search box, Undo and Redo, one slider row per parameter (name, slider, value with its units), the
//   "Show more" button, and the notice when the plug-in lists none.
// Does not own: the rules (FxParameterModel), the polling and the engine calls (FxParameterPane).
// Tests: TabForge.Avalonia.Tests.FxParameterTests.
// Threads: UI thread only.
public sealed class FxParameterPanel : DockPanel
{
    private readonly FxParameterModel _model;
    private readonly Action<uint, double> _apply;
    private readonly TextBox _search = new() { Watermark = "Search parameters", Width = ChromeMetrics.ParamSearchWidth, Name = "ParameterSearch" };
    private readonly Button _undo = Look(new Button { Content = "Undo", Name = "ParameterUndo", IsEnabled = false });
    private readonly Button _redo = Look(new Button { Content = "Redo", Name = "ParameterRedo", IsEnabled = false });
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Name = "ParameterCount", [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
    private readonly StackPanel _rows = new() { Name = "ParameterRows" };
    private readonly Button _more = Look(new Button { Name = "ParameterMore", HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false });
    private readonly TextBlock _empty = new() { Name = "ParameterNotice", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
    private readonly Dictionary<uint, Row> _byId = new();
    private bool _building;
    private (uint Id, double Before)? _gesture;

    private sealed record Row(PluginParameterInfo Info, Slider Slider, TextBlock Value);

    /// <param name="model">The rules and the values the panel shows.</param>
    /// <param name="apply">Sends a parameter value to the plug-in (a live change while a slider moves, and the value an undo or redo restores).</param>
    public FxParameterPanel(FxParameterModel model, Action<uint, double> apply)
    {
        (_model, _apply) = (model, apply);
        Name = "ParameterPanel";
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Name = "ParameterBar" };
        _search.VerticalAlignment = VerticalAlignment.Center;
        foreach (var part in new Control[] { _search, _undo, _redo, _count })
        {
            part.WithMargin(0, 0, ChromeMetrics.ParamGap, ChromeMetrics.ParamGap);
            bar.Children.Add(part);
        }
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        _rows.Children.Add(_empty);
        _rows.Children.Add(_more);
        Children.Add(new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Name = "ParameterScroll" });
        HotkeyTip.Set(_undo, "Edit.Undo", "Take back the last parameter change");
        HotkeyTip.Set(_redo, "Edit.Redo", "Do the last undone parameter change again");
        _search.TextChanged += (_, _) => { if (_building) return; _model.Search = _search.Text ?? ""; Rebuild(); };
        _undo.Click += (_, _) => Step(_model.Undo());
        _redo.Click += (_, _) => Step(_model.Redo());
        _more.Click += (_, _) => { _model.ShowMore(); Rebuild(); };
        KeyDown += OnKey;
    }

    /// <summary>The search box text.</summary>
    public string SearchText { get => _search.Text ?? ""; set => _search.Text = value; }
    public int RowCount => _byId.Count;
    public string NoticeText => _empty.IsVisible ? _empty.Text ?? "" : "";
    public string CountText => _count.Text ?? "";
    public bool CanUndo => _undo.IsEnabled;
    public bool CanRedo => _redo.IsEnabled;
    public bool MoreVisible => _more.IsVisible;

    /// <summary>The slider of a parameter (null when its row is not shown); the tests move it.</summary>
    public Slider? SliderOf(uint id) => _byId.TryGetValue(id, out var row) ? row.Slider : null;
    /// <summary>The value text on the row of a parameter.</summary>
    public string? ValueTextOf(uint id) => _byId.TryGetValue(id, out var row) ? row.Value.Text : null;
    /// <summary>Presses Undo (a test or a key).</summary>
    public void UndoLast() => Step(_model.Undo());
    /// <summary>Presses Redo (a test or a key).</summary>
    public void RedoLast() => Step(_model.Redo());

    /// <summary>Empties the panel for another plug-in.</summary>
    public void Clear()
    {
        _building = true;
        try { _search.Text = ""; }
        finally { _building = false; }
        _gesture = null;
        _model.Reset();
        Rebuild();
    }

    /// <summary>Takes a new reading of the parameters: the rows are built again when the list changed, else only the values move (the slider being dragged stays where the hand holds it).</summary>
    public void Update(IReadOnlyList<PluginParameterInfo> list)
    {
        var rebuilt = _model.Load(list, _gesture?.Id);
        if (rebuilt || _byId.Count == 0) { Rebuild(); return; }
        var byId = list.ToDictionary(p => p.Id);
        _building = true;
        try
        {
            foreach (var (id, row) in _byId.ToList())
            {
                if (byId.TryGetValue(id, out var held) && id == _gesture?.Id)   // being dragged: the slider stays, the text follows what the plug-in reads for it
                {
                    if (held.Text.Length > 0 && Math.Abs(held.Value - _model.ValueOf(held)) <= FxParameterModel.StepOf(held)) row.Value.Text = held.Text;
                    continue;
                }
                if (!byId.TryGetValue(id, out var info)) continue;
                _byId[id] = row with { Info = info };
                Show(_byId[id], info);
            }
        }
        finally { _building = false; }
        RefreshButtons();
    }

    private void Rebuild()
    {
        _building = true;
        try
        {
            _rows.Children.Clear();
            _byId.Clear();
            var visible = _model.Visible();
            _empty.IsVisible = visible.Count == 0;
            _empty.Text = _model.All.Count == 0 ? "No parameters to show: they appear when the chain is on and the plug-in lists some." : "No parameter matches the search.";
            _rows.Children.Add(_empty);
            foreach (var info in visible) _rows.Children.Add(BuildRow(info));
            _more.IsVisible = _model.Hidden > 0;
            _more.Content = $"Show {Math.Min(FxParameterModel.PageSize, _model.Hidden)} more";
            _rows.Children.Add(_more);
            _count.Text = _model.All.Count == 0 ? "" : $"{_model.Matches().Count} of {_model.All.Count}";
        }
        finally { _building = false; }
        RefreshButtons();
    }

    private Control BuildRow(PluginParameterInfo info)
    {
        var name = new TextBlock { Text = info.Module.Length > 0 ? $"{info.Module} / {info.Name}" : info.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, name.Text);
        var step = FxParameterModel.StepOf(info);
        var slider = new Slider
        {
            Minimum = info.Min, Maximum = Math.Max(info.Max, info.Min + step), SmallChange = step, LargeChange = step * 10,
            IsSnapToTickEnabled = info.Stepped, TickFrequency = info.Stepped ? 1 : 0, IsEnabled = !info.ReadOnly, Tag = info.Id, MinWidth = ChromeMetrics.ParamSliderMinWidth, VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = global::Avalonia.Media.TextAlignment.Right, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
        var row = new Row(info, slider, value);
        _byId[info.Id] = row;
        Show(row, info);
        slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) OnSlider(info.Id); };
        slider.AddHandler(PointerPressedEvent, (_, _) => _gesture = (info.Id, _model.ValueOf(info)), RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(PointerReleasedEvent, (_, _) => EndGesture(), RoutingStrategies.Bubble, handledEventsToo: true);
        slider.PointerCaptureLost += (_, _) => EndGesture();
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse($"{ChromeMetrics.ParamNameWidth},*,{ChromeMetrics.ParamValueWidth}"), Tag = info.Id, Name = "ParameterRow" };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid.WithMargin(0, 0, ChromeMetrics.ParamGap, ChromeMetrics.ParamGap);
    }

    private void Show(Row row, PluginParameterInfo info)
    {
        var v = _model.ValueOf(info);
        row.Slider.Value = Math.Clamp(v, row.Slider.Minimum, row.Slider.Maximum);
        row.Value.Text = FxParameterModel.ValueText(info, v);
    }

    private void OnSlider(uint id)
    {
        if (_building || !_byId.TryGetValue(id, out var row)) return;
        var before = _model.ValueOf(row.Info);
        var now = row.Slider.Value;
        if (now.Equals(before)) return;
        _model.Hold(id, now);
        row.Value.Text = FxParameterModel.ValueText(row.Info, now);
        _apply(id, now);
        if (_gesture is null) { _model.Commit(id, before, now); RefreshButtons(); }   // a key or wheel step is a change of its own
    }

    private void EndGesture()
    {
        if (_gesture is not { } g) return;
        _gesture = null;
        if (_byId.TryGetValue(g.Id, out var row)) _model.Commit(g.Id, g.Before, _model.ValueOf(row.Info));
        RefreshButtons();
    }

    private void Step((uint Id, double Value)? step)
    {
        if (step is not { } s) return;
        _apply(s.Id, s.Value);
        if (_byId.TryGetValue(s.Id, out var row))
        {
            _building = true;
            try { Show(row, row.Info); }
            finally { _building = false; }
        }
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        _undo.IsEnabled = _model.CanUndo;
        _redo.IsEnabled = _model.CanRedo;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        switch (CommandOf(e.Key, e.KeyModifiers))
        {
            case UndoCommand: Step(_model.Undo()); break;
            case RedoCommand: Step(_model.Redo()); break;
            default: return;
        }
        e.Handled = true;
    }

    private const string UndoCommand = "Edit.Undo", RedoCommand = "Edit.Redo";

    /// <summary>Edit.Undo or Edit.Redo when the key press is one of that command's bindings (the live hotkey settings, so a rebind works); else null.</summary>
    private string? CommandOf(Key key, KeyModifiers modifiers)
    {
        if (MainWindowKeyboard.GestureOf(key, modifiers) is not { } gesture) return null;
        var keys = HotkeyTip.GetSource(this)?.Invoke() ?? new HotkeySettings();
        foreach (var id in new[] { UndoCommand, RedoCommand })
            if (HotkeyCatalog.Canonical(HotkeyCatalog.GestureFor(keys, id)) == gesture || HotkeyCatalog.Canonical(HotkeyCatalog.GestureFor(keys, id, 2)) == gesture) return id;
        return null;
    }

    private static Button Look(Button button)
    {
        button.Classes.Add(AvaloniaDefaultLooks.ButtonLook);
        button.VerticalAlignment = VerticalAlignment.Center;
        return button.WithPadding(10, 3);
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the editor column of the MIDI processing window: the header (icon, name, preset row), the description, and one framed card per
//   parameter section (its icon and heading over a MidiCardPanel of the section's rows), the ShowWhen visibility, the search dimming and
//   the jump highlight of a parameter.
// Does not own: the parameter controls (MidiParamEditors, MidiNoteMapRows), the adaptive row layout (MidiCardLayout), the preset rows (MidiPresetBars).
// Tests: MidiProcessingWindowTests, MidiClipLayoutTests, MidiSmallLayoutTests.
public sealed partial class MidiProcessingWindow
{
    private void ShowSelected()
    {
        var selected = Selected;
        UpdateChainMenu(selected);
        _presets?.Show(selected);
        _params.Children.Clear(); _elems.Clear(); _mapRows = null;
        _editors?.Reset();
        _learn.Stop();
        _parameterNote.IsVisible = false;
        _status.Text = MidiProcessingListPresenter.StatusText(Procs.Count);
        if (selected is null || MidiProcessorCatalog.Find(selected.Type) is not { } info)
        {
            (_title.Text, _description.Text) = MidiProcessingListPresenter.UnknownDetail(selected?.Type, _slot?.Type != PluginSlotType.Instrument && Procs.Count > 0);
            _titleIcon.Content = selected is null ? null : MidiIcons.Icon(MidiIcons.ForType(selected.Type), DialogMetrics.MidiHeaderIconSize, "MutedBrush");
            UpdateWatch();
            return;
        }
        _title.Text = info.Name;
        _titleIcon.Content = MidiIcons.Icon(MidiIcons.ForType(info.Type), DialogMetrics.MidiHeaderIconSize, "AccentBrush");
        _description.Text = info.Description;
        var values = MidiProcessorCatalog.ParseParams(selected, info);
        _editors!.Viewer = _detailViewer;
        if (info.Type == "noteMap") _mapRows = new MidiNoteMapRows(this, values, (key, name) => { _edit(() => { values[key] = name; selected.Params = values.ToJsonString(); }); _mapRows?.Refresh(); }) { Viewer = _detailViewer };
        _sections.Clear();
        _shownValues = values;
        _editors.Changed = ApplyVisibility;
        var groups = new[] { "Applies to" }.Concat(info.Params.Select(p => p.Group).Where(g => g != "Applies to").Distinct()).ToList();
        foreach (var group in groups)
        {
            var ps = info.Params.Where(p => p.Group == group).ToList();
            if (ps.Count == 0) continue;
            var rows = new MidiCardPanel { Name = "CardRows" };
            foreach (var p in ps)
            {
                var content = p.Kind == MidiParamKind.MapPreset && _mapRows is not null ? _mapRows.Row(p) : _editors!.Build(selected, values, p);
                var box = new Border { Child = content, BorderThickness = DialogMetrics.MidiFrameBorder, CornerRadius = new CornerRadius(DialogMetrics.MidiFrameCorner), BorderBrush = AvaloniaPalette.TransparentBrush };
                MidiCardPanel.SetHalf(box, p.Kind is MidiParamKind.Number or MidiParamKind.Check);
                _elems[p.Key] = (box, p);
                rows.Children.Add(box);
            }
            var card = Card(group, rows);
            _params.Children.Add(card);
            _sections.Add((card, rows));
        }
        ApplyVisibility();
        ApplyFilter();
        UpdateWatch();
        if (_jumpKey is { } key) { _jumpKey = null; Highlight(key); }
    }

    /// <summary>One parameter section as a framed card: the section's icon and heading over its rows.</summary>
    private static Border Card(string section, MidiCardPanel rows)
    {
        var icon = MidiIcons.Icon(MidiIcons.ForSection(section), DialogMetrics.MidiIconSize, "AccentBrush");
        icon.WithMargin(DialogMetrics.MidiIconGap);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var title = new TextBlock { Text = section, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Name = "CardHeading" };
        title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
        DockPanel.SetDock(icon, Dock.Left);
        var head = new DockPanel { Children = { icon, title } };
        head.WithMargin(DialogMetrics.MidiCardHeadGap);
        var card = new Border
        {
            Name = "ParamCard", Child = new StackPanel { Children = { head, rows } }, BorderThickness = DialogMetrics.MidiFrameBorder, CornerRadius = new CornerRadius(DialogMetrics.MidiCardCorner),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"), [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush"),
        };
        card.WithPadding(DialogMetrics.MidiCardPad);
        card.WithMargin(DialogMetrics.MidiCardGap);
        return card;
    }

    /// <summary>Shows each parameter whose ShowWhen holds for the stored values (hidden ones keep their value) and hides a card whose parameters are all hidden.</summary>
    private void ApplyVisibility()
    {
        if (_shownValues is not { } values) return;
        int Read(string key) => values[key] is { } node && double.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? (int)Math.Round(number) : 0;
        foreach (var (box, p) in _elems.Values)
            if (p.ShowWhen is { } when) box.IsVisible = when.IsShown(Read);
        foreach (var (card, rows) in _sections)
        {
            card.IsVisible = rows.Children.Any(c => c.IsVisible);
            rows.InvalidateMeasure();
        }
    }

    private static string ParamText(MidiParamInfo p) =>
        $"{p.Label} {p.Tip} {(p.Choices is null ? "" : string.Join(' ', p.Choices.Select(c => c.Item2)))} {p.Key}".ToLowerInvariant();

    /// <summary>While a search is active, parameters that match it get an accent outline and the others are dimmed (no rebuild, no timer).</summary>
    private void ApplyFilter()
    {
        foreach (var (box, p) in _elems.Values)
        {
            var match = _queryWords.Length > 0 && _queryWords.All(w => ParamText(p).Contains(w, StringComparison.Ordinal));
            box.Opacity = _queryWords.Length == 0 || match ? 1 : DimmedOpacity;
            if (match) box[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
            else box.BorderBrush = AvaloniaPalette.TransparentBrush;
        }
    }

    /// <summary>Brings a parameter into view with an accent outline (the next search or selection clears it).</summary>
    private void Highlight(string key)
    {
        if (!_elems.TryGetValue(key, out var entry)) return;
        entry.Box.Opacity = 1;
        entry.Box[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        entry.Box.BorderThickness = DialogMetrics.MidiHighlightBorder;
        AvaloniaUiThread.ForApplication().Post(() => entry.Box.BringIntoView(), UiPriority.Loaded);
    }
}

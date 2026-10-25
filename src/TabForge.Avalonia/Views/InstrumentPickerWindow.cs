using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Choose instrument: a search box, one header and tile row per family, and Select / Cancel. Clicking a tile chooses it, a double-click or
/// Select closes with its name, Enter in the search box picks the first match, Cancel and Esc close with null.
/// Tiles: the current sound's family (else the first) is built with the window; the others are queued and built <see cref="TilesPerSlice"/> at a time in background
/// slices on the UI thread. A search change or the close cancels the queued slice (its generation is no longer current), and a search
/// posts a fresh chain. A tile built late takes the current search filter when it is created.
/// With a preview hook (the track icon run) every tile click also previews that sound live on the track; the close answer
/// still decides (Select keeps it, Cancel drops it). The current tile is selected and, once the window is laid out, scrolled into view. A route note (the track plays through a plug-in) shows above the search. Each family header is an InstrumentFamilyHeader.
/// Owns: the window's controls, their layout, the tile build queue and keeping them in step with the presenter. Does not own: the rules
/// (<see cref="InstrumentPickerPresenter"/>) or the catalogue and its icons (Core InstrumentCatalog supplies each tile's badge by name; a name outside it shows the name only).
/// Tests: TabForge.Avalonia.Tests.ScoreTextStyleAndInstrumentPickerTests and InstrumentPickerTileBuildTests.
/// </summary>
public sealed class InstrumentPickerWindow : Window
{
    /// <summary>Tiles built per background slice after the first family.</summary>
    internal const int TilesPerSlice = 12;

    private readonly InstrumentPickerPresenter _presenter;
    private readonly Action<string>? _onPreview;
    private readonly IUiThread _ui = AvaloniaUiThread.ForApplication();
    private readonly TextBox _search = new() { Watermark = InstrumentPickerPresenter.SearchHint, Margin = new Thickness(0, 0, 0, 10), FontSize = 14 };
    // Tiles built so far, by catalogue index. Refresh shows or hides each one by the search.
    private readonly Dictionary<int, Border> _tileByIndex = new();
    // Tiles not built yet, in family order, each with the panel it joins.
    private readonly Queue<(int Index, WrapPanel Panel)> _unbuilt = new();
    private readonly List<(string Family, InstrumentFamilyHeader Header, WrapPanel Panel)> _headers = new();
    private Border? _chosenTile;
    // Every posted slice runs only while its generation is still current: a new slice chain or the close bumps it, cancelling the queued slice.
    private int _sliceGeneration;
    // A tile is a 56 DIP badge above the name, as the WPF picker's.
    private const double TileWidth = 112, BadgeSize = 56, TileNameMaxHeight = 30;

    /// <summary>Sample sounds for the preview: two families and a drum kit.</summary>
    public static IReadOnlyList<(string Name, string Family, int Program, bool IsDrumKit)> SampleEntries { get; } =
    [
        ("Grand Piano", "Piano", 0, false), ("Electric Piano", "Piano", 4, false),
        ("Distortion Guitar", "Guitar", 30, false), ("Clean Electric Guitar", "Guitar", 27, false),
        ("Electric Bass (Pick)", "Bass", 34, false), ("Fretless Bass", "Bass", 35, false),
        ("Drum Kit (Standard)", "Drum kits", 0, true),
    ];

    /// <summary>Every sound in the shared catalogue, in catalogue order: the list the track's Choose instrument picker shows.</summary>
    public static IReadOnlyList<(string Name, string Family, int Program, bool IsDrumKit)> CatalogueEntries() =>
        InstrumentCatalog.All.Select(e => (e.Name, e.Category, e.Program, e.IsDrumKit)).ToList();

    public InstrumentPickerWindow(IReadOnlyList<(string Name, string Family, int Program, bool IsDrumKit)> entries, string? current, bool focusFamily = false, Action<string>? onPreview = null, string? routeNote = null)
    {
        _presenter = new InstrumentPickerPresenter(entries, current, focusFamily);
        _onPreview = onPreview;
        Title = InstrumentPickerPresenter.Title;
        Width = 760; Height = 620; MinWidth = 480; MinHeight = 360; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var host = new StackPanel();
        var firstFamily = _presenter.CurrentFamily ?? _presenter.Families.FirstOrDefault();
        foreach (var family in _presenter.Families) host.Children.AddRange(FamilyRows(family, buildNow: family == firstFamily));
        var ok = new Button { Content = InstrumentPickerPresenter.SelectButton, Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = InstrumentPickerPresenter.CancelButton, Width = 90, IsCancel = true };
        ok.Click += (_, _) => Close(_presenter.Chosen);
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = DialogMetrics.FooterGap, Children = { ok, cancel } };
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var layout = new DockPanel { Margin = new Thickness(14), Children = { _search, buttons, new ScrollViewer { Content = host } } };
        if (routeNote is not null) layout.Children.Insert(0, RouteNote(routeNote));
        Content = layout;

        _search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { Refresh(); RestartSlices(); } };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || _presenter.FirstMatch() is not (>= 0 and var first)) return;
            Choose(first);
            Close(_presenter.Chosen);
        };
        Closed += (_, _) => _sliceGeneration++;
        Opened += (_, _) => _ui.Post(ScrollToChosen, UiPriority.Loaded);
        Refresh();
        RestartSlices();
    }

    private IEnumerable<Control> FamilyRows(string family, bool buildNow)
    {
        var header = new InstrumentFamilyHeader(family, _presenter.FocusFamily ? () => { _presenter.ToggleFamily(family); Refresh(); } : null);
        var panel = new WrapPanel();
        for (var i = 0; i < _presenter.Entries.Count; i++)
        {
            if (_presenter.Entries[i].Family != family) continue;
            if (buildNow) panel.Children.Add(Tile(i));
            else _unbuilt.Enqueue((i, panel));
        }
        _headers.Add((family, header, panel));
        return [header, panel];
    }

    /// <summary>Builds up to <paramref name="count"/> queued tiles into their family panels, in catalogue order within each family.</summary>
    private void BuildTiles(int count)
    {
        for (; count > 0 && _unbuilt.Count > 0; count--)
        {
            var (index, panel) = _unbuilt.Dequeue();
            panel.Children.Add(Tile(index));
        }
    }

    /// <summary>
    /// Cancels the queued slice, then posts a fresh chain of slices at background priority while tiles remain unbuilt. Key: the slice
    /// generation. Ceiling: one chain at a time, since each slice posts only the next one of its own generation. Thread: UI thread.
    /// </summary>
    private void RestartSlices()
    {
        var generation = ++_sliceGeneration;
        if (_unbuilt.Count > 0) _ui.Post(() => BuildSlice(generation), UiPriority.Background);
    }

    private void BuildSlice(int generation)
    {
        if (generation != _sliceGeneration) return;
        BuildTiles(TilesPerSlice);
        Refresh();
        if (_unbuilt.Count > 0) _ui.Post(() => BuildSlice(generation), UiPriority.Background);
    }

    /// <summary>Scrolls the current tile into view once the window has been laid out (its family is built with the window, the others stay collapsed).</summary>
    private void ScrollToChosen()
    {
        UpdateLayout();
        _chosenTile?.BringIntoView();
    }

    private static TextBlock RouteNote(string text)
    {
        var note = new TextBlock { Name = "RouteNote", Text = text, TextWrapping = TextWrapping.Wrap, Margin = DialogMetrics.RowMargin };
        note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        DockPanel.SetDock(note, Dock.Top);
        return note;
    }

    private Border Tile(int index)
    {
        var tile = new Border
        {
            Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(3), CornerRadius = new CornerRadius(6), Background = AvaloniaPalette.TransparentBrush,
            Child = new StackPanel
            {
                Width = TileWidth,
                Children =
                {
                    new InstrumentBadgeView(BadgeSize) { Entry = InstrumentCatalog.Find(_presenter.Entries[index].Name) },
                    new TextBlock { Text = _presenter.Entries[index].Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                        FontSize = 11, Margin = new Thickness(0, 4, 0, 0), MaxHeight = TileNameMaxHeight },
                },
            },
        };
        // Transparent at rest, so the whole tile rectangle takes the pointer. The hover shade and the press shade paint over it;
        // the chosen tile keeps its accent through the pointer.
        HoverShade.Wire(tile, fill => { if (_chosenTile != tile) tile.Background = fill; }, () => AvaloniaPalette.TransparentBrush);
        ToolTip.SetTip(tile, _presenter.TipOf(index));
        tile.PointerPressed += (_, e) =>
        {
            Choose(index);
            _onPreview?.Invoke(_presenter.Entries[index].Name);
            if (e.ClickCount == 2) Close(_presenter.Chosen);
        };
        _tileByIndex[index] = tile;
        if (_presenter.IsChosen(index)) Choose(index);
        return tile;
    }

    /// <summary>Chooses a sound: the presenter's choice, and the accent on its tile when that tile is built.</summary>
    private void Choose(int index)
    {
        if (_chosenTile is not null) _chosenTile.Background = AvaloniaPalette.TransparentBrush;
        _presenter.Choose(index);
        _chosenTile = _tileByIndex.GetValueOrDefault(index);
        if (_chosenTile is not null) _chosenTile[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoft");
    }

    private void Refresh()
    {
        _presenter.SetSearch(_search.Text);
        foreach (var (index, tile) in _tileByIndex) tile.IsVisible = _presenter.Matches(index);
        foreach (var (family, header, panel) in _headers)
        {
            var state = _presenter.StateOf(family);
            header.IsVisible = state.HeaderVisible;
            panel.IsVisible = state.TilesVisible;
            header.Show(state.Open, state.Count);
        }
    }
}

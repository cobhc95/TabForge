using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia.Views.Dialogs;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Mix Table (F10): one tab per track that changes at the beat (red dot), plus the track it was opened from (hollow dot when it has no change),
/// each with its own instrument, volume, pan, effects, transition and All tracks; the tempo below the tabs is shared by every track.
/// When the tabs do not fit, ‹ › (or the mouse wheel) scroll the strip and ▾ lists every tab. OK closes with a <see cref="MixTableOutcome"/>
/// for every tab, Clear with the selected tab's mix point removed, Cancel and Esc with null.
/// Owns: the window's controls, the tab strip and its scrolling, and the enabling from the ticks. Does not own: the start values, labels and
/// per-tab result (MixTablePresenter), which tracks get a tab (BeatMix.TracksAt) or the write (MainWindowMixTable).
/// Sample state: the preview has no document, so <see cref="SamplePresenter"/> stands for the selected beat (no mix point, track values).
/// Tests: TabForge.Avalonia.Tests.MixTableTabsTests.
/// </summary>
public sealed class MixTableWindow : Window
{
    private readonly IReadOnlyList<MixTableTab> _tabs;
    private readonly List<Page> _pages = [];
    private readonly List<Border> _tabButtons = [];
    private int _hovered = -1;
    private readonly ContentControl _pageHost = new();
    private readonly ScrollViewer _strip = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button _left = new(), _right = new(), _list = new();
    private readonly CheckBox _tempoCheck = new();
    private readonly TextBox _tempo = new() { Width = DialogMetrics.MixTempoBoxWidth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _tempoTransition = new() { Width = DialogMetrics.MixTransitionWidth };
    private readonly int _tempoTransitionStart;
    private int _selected;

    /// <summary>A single-tab table (the preview, and callers with one track).</summary>
    public MixTableWindow(MixTablePresenter presenter) : this(new[] { new MixTableTab(0, 0, "Track", false, presenter) }, 0, 0) { }

    /// <param name="tabs">The tabs in track order; never empty.</param>
    /// <param name="selected">The tab of the track the table was opened from.</param>
    /// <param name="tempoTransitionBeats">The beats the beat's tempo takes to arrive (0 = immediately).</param>
    public MixTableWindow(IReadOnlyList<MixTableTab> tabs, int selected, int tempoTransitionBeats)
    {
        if (tabs.Count == 0) throw new ArgumentException("The Mix Table needs at least one tab.", nameof(tabs));
        _tabs = tabs;
        _selected = Math.Clamp(selected, 0, tabs.Count - 1);
        _tempoTransitionStart = Math.Clamp(tempoTransitionBeats, 0, MixTablePresenter.MaxTransitionBeats);
        Title = MixTablePresenter.Title;
        Width = DialogMetrics.MixWindowWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.UseWindowColours();

        foreach (var tab in tabs) _pages.Add(new Page(tab));

        var root = new StackPanel().WithMargin(DialogMetrics.MixWindowPad);
        root.Children.Add(Header());
        root.Children.Add(TabStrip());
        root.Children.Add(_pageHost);
        root.Children.Add(TempoSection());
        root.Children.Add(ButtonRow());
        Content = root;
        Select(_selected);
        Opened += (_, _) => { BringIntoView(_selected); UpdateArrows(); };
    }

    /// <summary>The sample beat the preview opens with: no mix point, the full demo song's first track and tempo. Built once, on first use (never at startup).</summary>
    public static MixTablePresenter SamplePresenter() => _sample ??= CreateSample();

    private static MixTablePresenter? _sample;

    private static MixTablePresenter CreateSample() =>
        FullDemoSongFactory.Create() is { } song && song.Tracks[0] is { } track
            ? new(null, track.MidiProgram, track.Volume, track.Pan, null, song.Tempo, Enumerable.Range(0, 128).Select(p => (p, GeneralMidi.NameOf(p), false)))
            : throw new InvalidOperationException("The demo song has no tracks.");

    /// <summary>The selected tab (for tests).</summary>
    internal int SelectedTab => _selected;

    /// <summary>The tab buttons (for tests).</summary>
    internal IReadOnlyList<Border> TabButtons => _tabButtons;

    /// <summary>Whether a tab shows as selected (for tests).</summary>
    internal bool IsShownSelected(int index) => _tabButtons[index].Tag is true;

    private Control Header() => new StackPanel
    {
        Margin = DialogMetrics.MixHeaderMargin,
        Children =
        {
            new TextBlock { Text = MixTablePresenter.Title, FontSize = DialogMetrics.MixTitleSize, FontWeight = FontWeight.SemiBold },
            ToolWindowParts.Small(MixTableRows.Hint),
        },
    };

    // ── Tab strip ───────────────────────────────────────────────────────────

    private Control TabStrip()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _tabs.Count; i++)
        {
            var index = i;
            // A tab in the track's own colour: a strip along its foot, and the selected tab shaded with it (the accent when the track has none).
            var button = new Border
            {
                Child = TabLabel(_tabs[i]),
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium, ChromeMetrics.RadiusMedium, 0, 0),
                BorderThickness = DialogMetrics.MixTabStripBorder,
            }.WithPadding(DialogMetrics.MixTabPad).WithMargin(DialogMetrics.MixTabGap);
            ToolTip.SetTip(button, _tabs[i].HasChange ? $"{_tabs[i].Name} changes at this beat" : $"{_tabs[i].Name}: no change at this beat");
            global::Avalonia.Automation.AutomationProperties.SetName(button, _tabs[i].Name);
            button.PointerPressed += (_, e) => { Select(index); e.Handled = true; };
            button.PointerEntered += (_, _) => { _hovered = index; Paint(index); };
            button.PointerExited += (_, _) => { if (_hovered == index) _hovered = -1; Paint(index); };
            _tabButtons.Add(button);
            row.Children.Add(button);
        }
        _strip.Content = row;
        _strip.ScrollChanged += (_, _) => UpdateArrows();
        _strip.PointerWheelChanged += (_, e) =>
        {
            _strip.Offset = new Vector(Math.Clamp(_strip.Offset.X - (e.Delta.Y + e.Delta.X) * DialogMetrics.MixTabScrollStep / 2, 0, MaxOffset()), 0);
            e.Handled = true;
        };

        StripButton(_left, "‹", "Scroll the track tabs left", () => ScrollBy(-DialogMetrics.MixTabScrollStep));
        StripButton(_right, "›", "Scroll the track tabs right", () => ScrollBy(DialogMetrics.MixTabScrollStep));
        StripButton(_list, "▾", "List every track tab", ShowList);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") }.WithMargin(DialogMetrics.MixTabStripMargin);
        Grid.SetColumn(_left, 0); Grid.SetColumn(_strip, 1); Grid.SetColumn(_right, 2); Grid.SetColumn(_list, 3);
        grid.Children.Add(_left); grid.Children.Add(_strip); grid.Children.Add(_right); grid.Children.Add(_list);
        var rule = new Border { Height = DialogMetrics.MixTabRule, [!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush") };
        return new StackPanel { Children = { grid, rule } };
    }

    private static Control TabLabel(MixTableTab tab) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Children = { Dot(tab.HasChange), new TextBlock { Text = tab.Name, VerticalAlignment = VerticalAlignment.Center } },
    };

    /// <summary>The change dot: filled red where the track changes at this beat, a hollow ring where it does not.</summary>
    private static Ellipse Dot(bool filled)
    {
        var dot = new Ellipse { Width = DialogMetrics.MixTabDot, Height = DialogMetrics.MixTabDot, VerticalAlignment = VerticalAlignment.Center, StrokeThickness = DialogMetrics.MixTabDotStroke }.WithMargin(DialogMetrics.MixTabDotGap);
        if (filled) dot[!Shape.FillProperty] = new DynamicResourceExtension("Danger");
        else dot[!Shape.StrokeProperty] = new DynamicResourceExtension("TextMuted");
        return dot;
    }

    private void StripButton(Button button, string glyph, string tip, Action click)
    {
        button.Content = glyph;
        button.Width = DialogMetrics.MixTabArrowWidth;
        button.Height = DialogMetrics.MixTabArrowHeight;
        button.WithPadding(DialogMetrics.MixTabArrowPad).WithMargin(DialogMetrics.MixTabArrowMargin);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(button, tip);
        global::Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
    }

    private double MaxOffset() => Math.Max(0, _strip.Extent.Width - _strip.Viewport.Width);

    private void ScrollBy(double delta) => _strip.Offset = new Vector(Math.Clamp(_strip.Offset.X + delta, 0, MaxOffset()), 0);

    private void UpdateArrows()
    {
        var overflow = MaxOffset() > 0.5;
        _left.IsVisible = _right.IsVisible = _list.IsVisible = overflow;
        _left.IsEnabled = _strip.Offset.X > 0.5;
        _right.IsEnabled = _strip.Offset.X < MaxOffset() - 0.5;
    }

    private void ShowList()
    {
        var menu = new MenuFlyout();
        for (var i = 0; i < _tabs.Count; i++)
        {
            var index = i;
            // The row carries the track's colour as a bar, like the tab's strip; the dot keeps meaning "changes here".
            var bar = new Border { Width = DialogMetrics.MixTabStrip, Height = DialogMetrics.MixTabListBar, VerticalAlignment = VerticalAlignment.Center }.WithMargin(DialogMetrics.MixTabDotGap);
            if (TrackColour(_tabs[i].ColorHex) is { } colour) bar.Background = new SolidColorBrush(colour);
            else bar[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
            var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { bar, TabLabel(_tabs[i]) } };
            var item = new MenuItem { Header = header, Icon = i == _selected ? new TextBlock { Text = "✓" } : null };
            item.Click += (_, _) => Select(index);
            menu.Items.Add(item);
        }
        menu.ShowAt(_list);
    }

    private void Select(int index)
    {
        _selected = index;
        for (var i = 0; i < _tabButtons.Count; i++) Paint(i);
        _pageHost.Content = _pages[index].Root;
        BringIntoView(index);
    }

    /// <summary>Colours a tab: the track colour as its strip, and as its shade when selected (lighter under the pointer).</summary>
    private void Paint(int index)
    {
        var tab = _tabButtons[index];
        var selected = index == _selected;
        tab.Tag = selected;
        var colour = TrackColour(_tabs[index].ColorHex);
        tab.BorderBrush = colour is { } c ? new SolidColorBrush(c) : null;
        if (colour is null) tab[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        var shade = selected ? DialogMetrics.MixTabSelectedShade : index == _hovered ? DialogMetrics.MixTabHoverShade : 0;
        if (shade <= 0) tab.Background = null;
        else if (colour is { } fill) tab.Background = new SolidColorBrush(fill, shade);
        else { tab[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush"); }
        if (tab.Child is StackPanel { Children.Count: 2 } label && label.Children[1] is TextBlock name)
        {
            name.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(selected ? "TextPrimary" : "TextSecondary");
        }
    }

    private static Color? TrackColour(string? hex) => ColourHex.TryParse(hex ?? "", out var colour) ? AvaloniaPalette.ColorOf(colour.ToUi()) : null;

    /// <summary>Scrolls the strip so the tab is fully visible (a tab picked from the list, or the opened track's tab at the start).</summary>
    private void BringIntoView(int index)
    {
        if (index < 0 || index >= _tabButtons.Count || _strip.Viewport.Width <= 0) return;
        var button = _tabButtons[index];
        var left = button.Bounds.X;
        var right = left + button.Bounds.Width;
        if (left < _strip.Offset.X) _strip.Offset = new Vector(left, 0);
        else if (right > _strip.Offset.X + _strip.Viewport.Width) _strip.Offset = new Vector(Math.Min(MaxOffset(), right - _strip.Viewport.Width), 0);
        UpdateArrows();
    }

    // ── Shared tempo ────────────────────────────────────────────────────────

    private Control TempoSection()
    {
        var presenter = _tabs[_selected].Presenter;
        _tempoCheck.Content = MixTablePresenter.TempoLabel;
        _tempo.Text = presenter.TempoStartText;
        var readout = MixTableRows.Readout();
        readout.Text = MixTableRows.TempoText(_tempo.Text);
        _tempo.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) readout.Text = MixTableRows.TempoText(_tempo.Text ?? ""); };
        foreach (var label in presenter.TransitionLabels) _tempoTransition.Items.Add(label);
        _tempoTransition.SelectedIndex = _tempoTransitionStart;
        var editor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogMetrics.MixTempoGap, Children = { _tempo, _tempoTransition } };
        var row = MixTableRows.Row(_tempoCheck, editor, readout, presenter.TempoOn);
        var note = ToolWindowParts.Small("The tempo is shared by every track.");
        note.Margin = DialogMetrics.MixTabNoteMargin;
        return new StackPanel { Children = { MixTableRows.Section("Tempo", row), note } };
    }

    // ── Buttons and result ──────────────────────────────────────────────────

    private DockPanel ButtonRow()
    {
        var clear = DialogButtons.Action(MixTablePresenter.ClearButton);
        ToolTip.SetTip(clear, "Removes the selected track's change at this beat");
        var ok = DialogButtons.Ok(MixTablePresenter.OkButton, name: null);
        var cancel = DialogButtons.Cancel(MixTablePresenter.CancelButton, name: null);
        clear.Click += (_, _) => Close(Outcome(clearSelected: true));
        ok.Click += (_, _) => Close(Outcome(clearSelected: false));
        cancel.Click += (_, _) => Close(null);
        DockPanel.SetDock(clear, Dock.Left);
        var right = DialogButtons.Row(ok, cancel);
        return new DockPanel { Margin = DialogMetrics.MixFooterMargin, Children = { clear, right } };
    }

    /// <summary>Every tab's mix point and the shared tempo. Clear empties the selected tab's mix point and leaves the rest as they stand.</summary>
    internal MixTableOutcome Outcome(bool clearSelected)
    {
        var mixes = new List<(MixTableTab, MixChange?)>();
        for (var i = 0; i < _tabs.Count; i++)
            mixes.Add((_tabs[i], clearSelected && i == _selected ? null : _pages[i].Mix()));
        var tempo = _tabs[_selected].Presenter.Build(_pages[_selected].Edits() with { TempoOn = _tempoCheck.IsChecked == true, TempoText = _tempo.Text ?? "" }).Tempo;
        return new MixTableOutcome(mixes, tempo, Math.Max(0, _tempoTransition.SelectedIndex));
    }

    /// <summary>One track's controls: Sound, Effects, its Transition and All tracks.</summary>
    private sealed class Page
    {
        private readonly MixTablePresenter _presenter;
        private readonly CheckBox _instrumentCheck = new();
        private readonly ComboBox _instrument = new() { Width = DialogMetrics.MixInstrumentWidth };
        private readonly List<(CheckBox Check, Slider Slider)> _scales = [];
        private readonly ComboBox _transition = new() { Width = DialogMetrics.MixTransitionWidth };
        private readonly CheckBox _allTracks = new();
        private readonly bool _drums;
        private readonly int _kitProgram;

        public Page(MixTableTab tab)
        {
            _presenter = tab.Presenter;
            _drums = tab.IsDrums;
            var start = _presenter.InstrumentStartIndex;
            _kitProgram = start >= 0 && start < _presenter.InstrumentChoices.Count ? _presenter.InstrumentChoices[start].Program : 0;
            var stack = new StackPanel();
            stack.Children.Add(Sections());
            stack.Children.Add(OptionsRow());
            if (!tab.HasChange)
                stack.Children.Add(ToolWindowParts.Small($"{tab.Name} has no change at this beat. Tick a row to add one.").WithMargin(DialogMetrics.MixTabNoteMargin));
            Root = stack;
        }

        public Control Root { get; }

        private Control Sections()
        {
            _instrumentCheck.Content = MixTablePresenter.InstrumentLabel;
            // A drum track's program picks a drum kit, not a melody instrument: it reads as the kit (program 0 is the standard kit, never "Grand Piano").
            if (_drums) { _instrument.Items.Add(_kitProgram == 0 ? "Standard drum kit" : $"Drum kit {_kitProgram + 1}"); _instrument.SelectedIndex = 0; }
            else
            {
                foreach (var choice in _presenter.InstrumentChoices) _instrument.Items.Add(choice.Label);
                _instrument.SelectedIndex = _presenter.InstrumentStartIndex;
            }
            _instrument.HorizontalAlignment = HorizontalAlignment.Stretch;
            var instrumentRow = MixTableRows.Row(_instrumentCheck, _instrument, MixTableRows.Readout(), _presenter.InstrumentOn);

            var scaleFields = new (string Label, MixFieldValue Field)[]
            {
                (MixTablePresenter.VolumeLabel, _presenter.Volume), (MixTablePresenter.PanLabel, _presenter.Pan),
                (MixTablePresenter.ChorusLabel, _presenter.Chorus), (MixTablePresenter.ReverbLabel, _presenter.Reverb),
                (MixTablePresenter.PhaserLabel, _presenter.Phaser), (MixTablePresenter.TremoloLabel, _presenter.Tremolo),
            };
            var rows = new List<Control>();
            foreach (var (label, field) in scaleFields)
            {
                var isPan = label == MixTablePresenter.PanLabel;
                var readout = MixTableRows.Readout();
                var slider = MixTableRows.Scale(isPan ? MixTablePresenter.PanMin : MixTablePresenter.ScaleMin, isPan ? MixTablePresenter.PanMax : MixTablePresenter.ScaleMax,
                    field.Value, readout, isPan ? MixTableRows.PanText : MixTablePresenter.ValueText);
                // Pan's centre and the effects' off are 0; the volume has no fixed default (it is the track's own), so it keeps none.
                if (label != MixTablePresenter.VolumeLabel) AvaloniaSliderReset.OnDoubleTap(slider, 0);
                var check = new CheckBox { Content = label };
                rows.Add(MixTableRows.Row(check, slider, readout, field.On));
                _scales.Add((check, slider));
            }
            return new StackPanel
            {
                Children =
                {
                    MixTableRows.Section("Sound", instrumentRow, rows[0], rows[1]),
                    MixTableRows.Section("Effects", rows[2], rows[3], rows[4], rows[5]),
                },
            };
        }

        private StackPanel OptionsRow()
        {
            foreach (var label in _presenter.TransitionLabels) _transition.Items.Add(label);
            _transition.SelectedIndex = _presenter.TransitionStartIndex;
            _allTracks.Content = MixTablePresenter.AllTracksLabel;
            _allTracks.IsChecked = _presenter.AllTracksStart;
            _allTracks.Margin = DialogMetrics.MixAllTracksMargin;
            _allTracks.VerticalAlignment = VerticalAlignment.Center;
            return new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = DialogMetrics.MixOptionsMargin,
                Children = { ToolWindowParts.Label(MixTablePresenter.TransitionLabel), _transition, _allTracks },
            };
        }

        public MixTableEdits Edits() => new(
            InstrumentOn: _instrumentCheck.IsChecked == true, InstrumentIndex: _instrument.SelectedIndex,
            Volume: Picked(0), Pan: Picked(1), Chorus: Picked(2), Reverb: Picked(3), Phaser: Picked(4), Tremolo: Picked(5),
            TempoOn: false, TempoText: "", TransitionIndex: _transition.SelectedIndex, AllTracks: _allTracks.IsChecked == true);

        /// <summary>The tab's mix point, or null when nothing is ticked.</summary>
        public MixChange? Mix()
        {
            var mix = _presenter.Build(Edits()).Mix;
            if (mix is not null && _drums) mix.Program = _instrumentCheck.IsChecked == true ? _kitProgram : null;   // the kit stays as it was
            return mix is { IsEmpty: false } ? mix : null;
        }

        private MixFieldValue Picked(int index) => new(_scales[index].Check.IsChecked == true, (int)_scales[index].Slider.Value);
    }
}

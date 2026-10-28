using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's toolbar (as the mockup lays it out): the six tools (keys 1-6), Legato, the chord stamp and the tab voice, the snap magnet (the transport's
//       own SnapSettings: toggling either lights both; right-click opens the snap settings) with the grid value (straight and triplet), Quantize and
//       its options popup (strength, swing, starts / ends), zoom out / in / fit, Audition, Follow, the scale highlight and row fold, step input,
//       and the track chip.
// Does not own: the snap settings (TransportBarView holds them), the quantize edit (PianoRollPresenter) or the zoom (PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollView
{
    // Retired 26 Oct (tombstone): PillsRoom, the room the toolbar kept at its right end for the Piano / Tab pills that floated there (they sit in the menu row now).
    private const double ToolSize = 30, ToolHeightSize = 28, GroupGap = 2, GroupPad = 6, GridComboSize = 76, PopupSize = 240, ChipDot = 9;

    private readonly Dictionary<PianoRollTool, ToggleButton> _toolButtons = new();
    private readonly ToggleButton _magnet = new();
    private readonly ComboBox _grid = new() { ItemsSource = SnapSettings.Grids };
    private readonly ToggleButton _audition = new() { IsChecked = true };
    private readonly ToggleButton _followButton = new() { IsChecked = true };
    private readonly Popup _quantizePopup = new() { IsLightDismissEnabled = true };
    private readonly Ellipse _chipDot = new();
    private readonly TextBlock _chipName = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _chipKind = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _source = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private List<(PianoRollSourceChoice Choice, string Label)> _sourceChoices = new();
    private bool _follow = true;
    private bool _refreshing;

    private static readonly IReadOnlyList<(PianoRollTool Tool, string Icon, string Tip)> Tools = new[]
    {
        (PianoRollTool.Select, IconGeometry.ToolDraw, "Edit (1): drag on empty grid draws a note (sideways = length, up / down = pitch); click a note to select it (Ctrl toggles, Shift adds), drag it to move, an edge to resize, its top edge for velocity; double-click deletes. Right-drag draws the selection box"),
        // Retired 26 Oct (tombstone): the separate Select (1) and Draw (2) buttons; both are the Edit tool now.
        (PianoRollTool.Erase, IconGeometry.ToolErase, "Erase (2): click deletes a note, a box deletes the notes in it"),
        (PianoRollTool.Split, IconGeometry.ToolSplit, "Split (3): click cuts a note at the snap point"),
        (PianoRollTool.Mute, IconGeometry.ToolMute, "Mute (4): click mutes or unmutes a note (a muted note is hollow and silent; a tab stays as written)"),
        (PianoRollTool.Glue, IconGeometry.ToolGlue, "Glue (5): click a note to join it with the next note of its pitch"),
        // Retired 26 Oct (tombstone): the Velocity tool button (key 7). Alt-drag a note, or drag the bars in the velocity lane.
    };

    private readonly ToggleButton _scale = new();
    private readonly ComboBox _fold = new() { ItemsSource = PianoRollScale.Folds };
    private readonly ToggleButton _step = new();
    private readonly ToggleButton _names = new() { Content = "Names" };
    private readonly ToggleButton _ghosts = new() { Content = "Ghosts" };
    private readonly ComboBox _newLength = new() { ItemsSource = PianoRollSnap.Lengths };
    private readonly ComboBox _chord = new() { ItemsSource = PianoRollEdits.ChordNames, SelectedIndex = 0 };
    private readonly ComboBox _voice = new() { ItemsSource = new[] { "Voice 1", "Voice 2" }, SelectedIndex = 0 };

    private static Control Icon(string data) => MenuRowToggles.Icon(data, IconSizing.Current);

    private static T Sized<T>(T button) where T : Control
    {
        button.MinWidth = ToolSize;
        button.MinHeight = ToolHeightSize;
        return button;
    }

    private static StackPanel Group(params Control[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GroupGap, VerticalAlignment = VerticalAlignment.Center }.WithMargin(0, 0, GroupPad, 0);
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private Control BuildToolbar()
    {
        var tools = new List<Control>();
        foreach (var (tool, icon, tip) in Tools)
        {
            var button = Sized(new ToggleButton { Content = Icon(icon), Focusable = false });
            PianoRollToolLook.Apply(button);
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => { Presenter.Tool = tool; RefreshTools(); Surface.Focus(); };
            _toolButtons[tool] = button;
            tools.Add(button);
        }
        var legato = Command(IconGeometry.ToolLegato, "Legato (Ctrl+L): each selected note (or every note) lasts to the next one", () => Surface.Run(Presenter.LegatoAsync()));
        Sized(_magnet).Content = Icon(IconGeometry.Magnet);
        _magnet.Focusable = false;
        ToolTip.SetTip(_magnet, "Snap (shared with the timeline magnet). Right-click: snap settings");
        _magnet.Click += (_, _) => { _host.ToggleSnap(); RefreshTools(); };
        _magnet.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right) { _host.OpenSnapSettings(); e.Handled = true; } };
        _grid.MinWidth = GridComboSize;
        _grid.Focusable = false;
        ToolTip.SetTip(_grid, "Grid: Bar to 1/32 and the triplets 1/4T, 1/8T, 1/16T (the same snap grid as the timeline)");
        _grid.SelectionChanged += (_, _) => { if (!_refreshing && _grid.SelectedItem is string g && _host.Snap() is { } snap) { snap.Grid = g; Surface.InvalidateVisual(); } };
        var quantize = Sized(new Button { Focusable = false, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GroupGap, Children = { Icon(IconGeometry.Quantize), new TextBlock { Text = "Quantize", VerticalAlignment = VerticalAlignment.Center } } } });
        ToolTip.SetTip(quantize, "Quantize selected notes (or all) to the snap grid (Q)");
        quantize.Click += (_, _) => Quantize();
        var options = Sized(new Button { Content = "▾", Focusable = false });
        ToolTip.SetTip(options, "Quantize options");
        _quantizePopup.PlacementTarget = options;
        _quantizePopup.Child = BuildQuantizePanel();
        _quantizePopup.Closed += (_, _) => _host.SaveSettings();
        options.Click += (_, _) => OpenQuantizeOptions();
        var zoomOut = Command(IconGeometry.ZoomOut, "Zoom out (Ctrl+wheel)", () => Surface.ZoomOut());
        var zoomIn = Command(IconGeometry.ZoomIn, "Zoom in (Ctrl+wheel)", () => Surface.ZoomIn());
        var fit = Command(IconGeometry.ZoomFit, "Zoom to fit notes (F)", () => Surface.ZoomToFit());
        Sized(_audition).Content = Icon(IconGeometry.Speaker);
        _audition.Focusable = false;
        ToolTip.SetTip(_audition, "Audition notes while editing");
        _audition.Click += (_, _) => Surface.AuditionOn = _audition.IsChecked == true;
        Sized(_followButton).Content = Icon(IconGeometry.Follow);
        _followButton.Focusable = false;
        ToolTip.SetTip(_followButton, "Follow playhead (same as the score's auto-scroll)");
        _followButton.Click += (_, _) => _follow = _followButton.IsChecked == true;
        BuildModeControls();
        _chipDot.Width = ChipDot;
        _chipDot.Height = ChipDot;
        _chipDot.VerticalAlignment = VerticalAlignment.Center;
        _chipKind[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GroupPad, VerticalAlignment = VerticalAlignment.Center, Children = { _chipDot, _chipName, _chipKind, _source } };
        ToolTip.SetTip(_source, "What the piano roll edits on this track: its written tab, or a lane of MIDI takes (every take on the lane). Takes never touch the tab.");
        _source.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _source.SelectedIndex >= 0 && _source.SelectedIndex < _sourceChoices.Count) UseSource(_sourceChoices[_source.SelectedIndex].Choice);
        };
        var bar = new DockPanel { LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Group(tools.ToArray()));
        left.Children.Add(Group(legato, _chord, _voice));
        left.Children.Add(Group(_magnet, _grid, _newLength));
        left.Children.Add(Group(quantize, options, _quantizePopup));
        left.Children.Add(Group(zoomOut, zoomIn, fit));
        left.Children.Add(Group(_audition, _followButton, _scale, _fold, _step, _names, _ghosts));
        SetDock(left, Dock.Left);
        SetDock(chip, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(chip);
        return new Border { Child = bar }.WithPadding(GroupPad, GroupGap);
    }

    private void BuildModeControls()
    {
        var editing = _host.Editing();
        Sized(_scale).Content = Icon(IconGeometry.ScaleRows);
        _scale.Focusable = false;
        _scale.IsChecked = editing.PianoRollScaleShown;
        ToolTip.SetTip(_scale, "Scale highlight: tint the rows of the scale chosen in Settings > Scale highlight");
        _scale.Click += (_, _) => { _host.Editing().PianoRollScaleShown = _scale.IsChecked == true; _host.SaveSettings(); RefreshDerived(); };
        _fold.Focusable = false;
        _fold.MinWidth = GridComboSize;
        _fold.SelectedItem = editing.PianoRollFold;
        ToolTip.SetTip(_fold, "Fold rows: None shows every pitch, Scale only the scale's pitches, Used only the pitches the notes use");
        _fold.SelectionChanged += (_, _) => { if (!_refreshing && _fold.SelectedItem is string fold) { _host.Editing().PianoRollFold = fold; _host.SaveSettings(); RefreshDerived(); } };
        Sized(_step).Content = Icon(IconGeometry.StepInput);
        _step.Focusable = false;
        ToolTip.SetTip(_step, "Step input: each piano key (or MIDI key) adds a note of the snap length at the step cursor and moves it on; Right / Left move it, Backspace steps back");
        _step.Click += (_, _) =>
        {
            Surface.StepInput = _step.IsChecked == true;
            if (Surface.StepInput) Surface.StepBeat = PlayheadBeat() ?? 0;
            _stepOn = Surface.StepInput;
            Surface.InvalidateVisual();
            Surface.Focus();
        };
        _newLength.Focusable = false;
        _newLength.MinWidth = GridComboSize;
        _newLength.SelectedItem = editing.PianoRollNoteLength;
        ToolTip.SetTip(_newLength, "Length of new notes: Grid follows the snap value; Last used repeats the last drawn length");
        _newLength.SelectionChanged += (_, _) => { if (_newLength.SelectedItem is string choice) { _host.Editing().PianoRollNoteLength = choice; Surface.NoteLengthChoice = choice; _host.SaveSettings(); } };
        Sized(_ghosts).Focusable = false;
        _ghosts.IsChecked = editing.PianoRollGhosts;
        ToolTip.SetTip(_ghosts, "Ghost notes: show the other takes and tracks faintly behind this track's notes (read-only)");
        _ghosts.Click += (_, _) => { _host.Editing().PianoRollGhosts = _ghosts.IsChecked == true; _host.SaveSettings(); RefreshDerived(); Surface.InvalidateVisual(); };
        Sized(_names).Focusable = false;
        ToolTip.SetTip(_names, "Key strip: note names (drum names on a drum track) instead of piano keys; saved with this track. Also on the key strip's right-click menu");
        _names.Click += (_, _) => SetNoteNames(_names.IsChecked == true);
        _chord.Focusable = false;
        _chord.MinWidth = GridComboSize;
        ToolTip.SetTip(_chord, "Chord stamp: Draw and step input add this chord on the note you place (Off: single notes)");
        _chord.SelectionChanged += (_, _) => Surface.Chord = _chord.SelectedItem as string ?? "Off";
        _voice.Focusable = false;
        _voice.MinWidth = GridComboSize;
        ToolTip.SetTip(_voice, "The tab voice new notes go into (the second voice's notes have a dashed inner edge)");
        _voice.SelectionChanged += (_, _) => Surface.DrawVoice = Math.Max(0, _voice.SelectedIndex);
    }

    private Button Command(string icon, string tip, Action run)
    {
        var button = Sized(new Button { Content = Icon(icon), Focusable = false });
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => run();
        return button;
    }

    private Control BuildQuantizePanel()
    {
        var editing = _host.Editing();
        var strength = new Slider { Minimum = 0, Maximum = 100, Value = editing.PianoRollQuantizeStrength, MinWidth = PopupSize / 2 };
        var strengthText = new TextBlock { Text = editing.PianoRollQuantizeStrength + "%", VerticalAlignment = VerticalAlignment.Center };
        strength.ValueChanged += (_, e) => { _host.Editing().PianoRollQuantizeStrength = (int)Math.Round(e.NewValue); strengthText.Text = (int)Math.Round(e.NewValue) + "%"; };
        var swing = new Slider { Minimum = 0, Maximum = 100, Value = editing.PianoRollQuantizeSwing, MinWidth = PopupSize / 2 };
        var swingText = new TextBlock { Text = editing.PianoRollQuantizeSwing + "%", VerticalAlignment = VerticalAlignment.Center };
        swing.ValueChanged += (_, e) => { _host.Editing().PianoRollQuantizeSwing = (int)Math.Round(e.NewValue); swingText.Text = (int)Math.Round(e.NewValue) + "%"; };
        var starts = new CheckBox { Content = "Starts", IsChecked = editing.PianoRollQuantizeStarts };
        starts.IsCheckedChanged += (_, _) => _host.Editing().PianoRollQuantizeStarts = starts.IsChecked == true;
        var ends = new CheckBox { Content = "Ends", IsChecked = editing.PianoRollQuantizeEnds };
        ends.IsCheckedChanged += (_, _) => _host.Editing().PianoRollQuantizeEnds = ends.IsChecked == true;
        StackPanel Row(string label, params Control[] items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GroupPad, Children = { new TextBlock { Text = label, MinWidth = GridComboSize, VerticalAlignment = VerticalAlignment.Center } } };
            foreach (var item in items) row.Children.Add(item);
            return row;
        }
        var hint = new TextBlock { Text = "Acts on the selection, or every note when none is selected.", TextWrapping = TextWrapping.Wrap, MaxWidth = PopupSize };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var panel = new StackPanel
        {
            Spacing = GroupPad, MinWidth = PopupSize,
            Children = { Row("Grid", new TextBlock { Text = "Follows snap" }), Row("Strength", strength, strengthText), Row("Swing", swing, swingText), Row("Move", starts, ends), hint },
        };
        var border = new Border { Child = panel }.WithPadding(GroupPad * 2, GroupPad * 2);
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("RaisedBackground");
        return border;
    }

    /// <summary>Opens the quantize options under the ▾ button (the menu's Quantize settings… row too).</summary>
    internal void OpenQuantizeOptions() => _quantizePopup.IsOpen = true;

    /// <summary>Q, the toolbar button and the menu: quantizes the selection (or all) with the options, to the snap grid.</summary>
    internal void Quantize()
    {
        var editing = _host.Editing();
        var grid = _host.Snap()?.Grid ?? "1/16";
        Surface.Run(Presenter.QuantizeAsync(grid, editing.PianoRollQuantizeStrength / 100.0, editing.PianoRollQuantizeStarts, editing.PianoRollQuantizeEnds, editing.PianoRollQuantizeSwing));
    }

    /// <summary>Lights the tool in use, the magnet and the grid value from the shared snap settings, and shows the track chip.</summary>
    internal void RefreshTools()
    {
        _refreshing = true;
        try
        {
            foreach (var (tool, button) in _toolButtons) button.IsChecked = tool == Presenter.Tool;
            var snap = _host.Snap();
            _magnet.IsChecked = snap?.Enabled == true;
            _magnet.IsEnabled = snap is not null;
            _grid.SelectedItem = snap?.Grid;
            _grid.IsEnabled = snap is not null;
            var track = Presenter.Track;
            _chipName.Text = track?.Name ?? "";
            _chipKind.Text = Presenter.SourceLabel;
            _sourceChoices = Presenter.SourceOptions();
            _source.ItemsSource = _sourceChoices.Select(option => option.Label).ToList();
            var now = Presenter.IsTab ? PianoRollSourceChoice.OnTab : Presenter.Choice;
            _source.SelectedIndex = _sourceChoices.FindIndex(option => option.Choice == now);
            _source.IsVisible = _sourceChoices.Count > 1;
            _voice.IsVisible = Presenter.IsTab;
            _fold.SelectedItem = _host.Editing().PianoRollFold;
            _chipDot.Fill = TrackColour() is { } colour ? new SolidColorBrush(AvaloniaPalette.ColorOf(colour)) : null;
        }
        finally { _refreshing = false; }
    }
}

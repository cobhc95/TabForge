using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's toolbar in two rows. Row 1 flows whole groups, each under a tiny upper-case caption: TOOL (the six tools keys 1-6 and Legato), SNAP (the magnet,
//       the transport's own SnapSettings: toggling either lights both, right-click opens the snap settings; the note value; the new-note length; Quantize and its options
//       popup), SCALE (the scale button naming the scale, which opens the master scale panel, and Lock to scale), STEP INPUT (the toggle and, while on, its scale filter) and
//       NEW NOTES (the chord stamp and the tab voice). Row 2 holds the selected-note values and, at the right, Audition, Follow playback, zoom, Ghosts and the track chip.
// Does not own: the snap settings (TransportBarView holds them), the quantize edit (PianoRollPresenter), the zoom (PianoRollSurface), the scale controls (PianoRollView.ToolbarScale).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests, PianoRollToolbarLayoutTests.
public sealed partial class PianoRollView
{
    // Retired 26 Oct (tombstone): PillsRoom, the room the toolbar kept at its right end for the Piano / Tab pills that floated there (they sit in the menu row now).
    private const double PopupSize = 240;

    private readonly Dictionary<PianoRollTool, ToggleButton> _toolButtons = new();
    private readonly ToggleButton _magnet = new();
    private readonly ComboBox _grid = new() { ItemsSource = SnapSettings.Grids };
    private readonly ToggleButton _audition = new() { IsChecked = true };
    private readonly ToggleButton _followButton = new();
    private readonly Popup _quantizePopup = new() { IsLightDismissEnabled = true };
    private readonly Ellipse _chipDot = new();
    private readonly TextBlock _chipName = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _chipKind = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _source = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private List<(PianoRollSourceChoice Choice, string Label)> _sourceChoices = new();
    private bool _refreshing;

    private static readonly IReadOnlyList<(PianoRollTool Tool, string Icon, string Tip)> Tools = new[]
    {
        (PianoRollTool.Pointer, IconGeometry.ToolSelect, "Select (V): click and drag to select, move and resize notes. Nothing is written."),
        (PianoRollTool.Select, IconGeometry.ToolDraw, "Write (1): click or drag to write notes; right-drag selects."),
        // Retired 26 Oct (tombstone): the separate Select (1) and Draw (2) buttons; both are the Edit tool now.
        (PianoRollTool.Erase, IconGeometry.ToolErase, "Erase (2): click deletes a note, a box deletes the notes in it"),
        (PianoRollTool.Split, IconGeometry.ToolSplit, "Split (3): click cuts a note at the snap point; S cuts the selected notes at the dashed line"),
        (PianoRollTool.Mute, IconGeometry.ToolMute, "Mute (4): click mutes or unmutes a note (a muted note is hollow and silent; a tab stays as written)"),
        (PianoRollTool.Glue, IconGeometry.ToolGlue, "Glue (5): click a note to join it with the next note of its pitch"),
        // Retired 26 Oct (tombstone): the Velocity tool button (key 7). Alt-drag a note, or drag the bars in the velocity lane.
    };

    private readonly Button _scaleButton = new() { Name = "PianoRollScaleButton" };
    private readonly ToggleButton _lockScale = new() { Name = "PianoRollLockScale", Content = "Lock to scale" };
    private PianoRollScaleHost? _scaleHost;
    private readonly ToggleButton _step = new() { Name = "PianoRollStepInput" };
    private readonly ComboBox _stepFilter = new() { Name = "PianoRollStepFilter", ItemsSource = PianoRollScaleLock.StepFilters, IsVisible = false };
    private readonly ToggleButton _names = new() { Name = "PianoRollKeyNames", Content = "Names" };
    private readonly ToggleButton _ghosts = new() { Content = "Ghosts" };
    private readonly ComboBox _newLength = new() { Name = "PianoRollNewLength", ItemsSource = PianoRollSnap.Lengths };
    private readonly ComboBox _chord = new() { Name = "PianoRollChord", ItemsSource = PianoRollEdits.ChordNames, SelectedIndex = 0 };
    private readonly ComboBox _voice = new() { Name = "PianoRollVoice", ItemsSource = new[] { "Voice 1", "Voice 2" }, SelectedIndex = 0 };

    private static Control Icon(string data) => MenuRowToggles.Icon(data, IconSizing.Current);

    private Control BuildToolbar()
    {
        var tools = new List<Control>();
        foreach (var (tool, icon, tip) in Tools)
        {
            var button = PianoRollToolbarLayout.StyleButton(new ToggleButton { Content = Icon(icon), Focusable = false });
            PianoRollToolLook.Apply(button);
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => { Presenter.Tool = tool; if (tool == PianoRollTool.Glue && Presenter.Selected.Count >= 2) Surface.GlueSelection(); RefreshTools(); Surface.Focus(); };
            AutomationProperties.SetName(button, tip.Substring(0, tip.IndexOf(' ')));
            _toolButtons[tool] = button;
            tools.Add(button);
        }
        var legato = Command(IconGeometry.ToolLegato, "Legato (Ctrl+L): each selected note (or every note) lasts to the next one", () => Surface.Run(Presenter.LegatoAsync()));
        // Row 2's audition group is configured here with the other toggles but parented by BuildInfoLine, which runs right after this.
        PianoRollToolbarLayout.StyleButton(_magnet).Content = Icon(IconGeometry.Magnet);
        _magnet.Focusable = false;
        TabForge.Avalonia.Platform.Controls.HotkeyTip.Set(_magnet, "PianoRoll.Snap", "Snap (shared with the timeline magnet). Right-click: snap settings");
        _magnet.Click += (_, _) => ToggleSnapNow();
        _magnet.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right) { _host.OpenSnapSettings(); e.Handled = true; } };
        PianoRollToolbarLayout.StyleCombo(_grid);
        _grid.Focusable = false;
        ToolTip.SetTip(_grid, "Note value of the snap grid: Bar down to 1/32 and the triplets 1/4T, 1/8T, 1/16T (the same snap grid as the timeline)");
        AutomationProperties.SetName(_grid, "Snap note value");
        _grid.SelectionChanged += (_, _) => { if (!_refreshing && _grid.SelectedItem is string g && _host.Snap() is { } snap) { snap.Grid = g; Surface.InvalidateVisual(); } };
        var quantize = PianoRollToolbarLayout.StyleButton(new Button { Focusable = false, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = PianoRollToolbarLayout.InnerGap, Children = { Icon(IconGeometry.Quantize), new TextBlock { Text = "Quantize", VerticalAlignment = VerticalAlignment.Center } } } });
        ToolTip.SetTip(quantize, "Quantize selected notes (or all) to the snap grid (Q)");
        quantize.Click += (_, _) => Quantize();
        var options = PianoRollToolbarLayout.StyleButton(new Button { Content = "▾", Focusable = false });
        ToolTip.SetTip(options, "Quantize options");
        _quantizePopup.PlacementTarget = options;
        _quantizePopup.Child = BuildQuantizePanel();
        _quantizePopup.Closed += (_, _) => _host.SaveSettings();
        options.Click += (_, _) => OpenQuantizeOptions();
        PianoRollToolbarLayout.StyleButton(_audition).Content = Icon(IconGeometry.Speaker);
        _audition.Focusable = false;
        ToolTip.SetTip(_audition, "Audition notes while editing");
        _audition.Click += (_, _) => Surface.AuditionOn = _audition.IsChecked == true;
        PianoRollToolbarLayout.StyleButton(_followButton).Content = Icon(IconGeometry.Follow);
        _followButton.Focusable = false;
        _followButton.IsChecked = _host.Follow().FollowPlayback;
        ToolTip.SetTip(_followButton, "Follow playback: on keeps the playhead in view while playing, off lets you scroll freely");
        _followButton.Click += (_, _) => { _host.Follow().FollowPlayback = _followButton.IsChecked == true; _host.SaveSettings(); };
        BuildModeControls();
        _chipDot.Width = PianoRollToolbarLayout.ChipDot;
        _chipDot.Height = PianoRollToolbarLayout.ChipDot;
        _chipDot.VerticalAlignment = VerticalAlignment.Center;
        _chipKind[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        PianoRollToolbarLayout.StyleCombo(_source);
        ToolTip.SetTip(_source, "What the piano roll edits on this track: its written tab, or a lane of MIDI takes (every take on the lane). Takes never touch the tab.");
        _source.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _source.SelectedIndex >= 0 && _source.SelectedIndex < _sourceChoices.Count) UseSource(_sourceChoices[_source.SelectedIndex].Choice);
        };
        // The buttons are given the same look as the tools (their content is final here): the theme's own button fills showed grey when lit or pressed.
        // The options button keeps the accent edge while its popup is open, as the dropdowns do.
        foreach (var button in new Button[] { quantize, _magnet, _audition, _followButton, _scaleButton, _lockScale, _step, _names, _ghosts })
            PianoRollToolLook.Apply(button);
        PianoRollToolLook.Apply(options, _quantizePopup);
        // Row 1: whole groups under their captions, wrapping group by group at narrow widths.
        var captions = PianoRollToolbarLayout.GroupCaptions;
        var flow = PianoRollToolbarLayout.Flow();
        flow.Children.Add(PianoRollToolbarLayout.Group(captions[0], false, PianoRollToolbarLayout.Frame(tools.ToArray()), PianoRollToolbarLayout.Frame(legato)));
        flow.Children.Add(PianoRollToolbarLayout.Group(captions[1], true, PianoRollToolbarLayout.Frame(_magnet, _grid, _newLength), PianoRollToolbarLayout.Frame(quantize, options, _quantizePopup)));
        flow.Children.Add(PianoRollToolbarLayout.Group(captions[2], true, PianoRollToolbarLayout.Frame(_scaleButton, _lockScale)));
        flow.Children.Add(PianoRollToolbarLayout.Group(captions[3], true, PianoRollToolbarLayout.Frame(_step, _stepFilter)));
        flow.Children.Add(PianoRollToolbarLayout.Group(captions[4], true, PianoRollToolbarLayout.Frame(_chord, _voice)));
        var row = PianoRollToolbarLayout.FillRow(PianoRollToolbarLayout.Row1Name, flow, null);
        return new Border { Child = row }.WithPadding(PianoRollToolbarLayout.GroupGap, PianoRollToolbarLayout.InnerGap);
    }

    /// <summary>Row 2's right end: Audition, Follow playback with the zoom buttons, Ghosts and the track chip (built by BuildToolbar).</summary>
    private Control BuildRowTwoRight()
    {
        var zoomOut = Command(IconGeometry.ZoomOut, "Zoom out (Ctrl+wheel)", () => Surface.ZoomOut());
        var zoomIn = Command(IconGeometry.ZoomIn, "Zoom in (Ctrl+wheel)", () => Surface.ZoomIn());
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = PianoRollToolbarLayout.InnerGap, VerticalAlignment = VerticalAlignment.Center, Children = { _chipDot, _chipName, _chipKind, _source } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = PianoRollToolbarLayout.GroupGap, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(PianoRollToolbarLayout.Frame(_audition));
        right.Children.Add(PianoRollToolbarLayout.Frame(_followButton, zoomOut, zoomIn));
        right.Children.Add(PianoRollToolbarLayout.Frame(_ghosts));
        right.Children.Add(PianoRollToolbarLayout.Frame(chip));
        return right;
    }

    private void BuildModeControls()
    {
        var editing = _host.Editing();
        BuildScaleControls(editing);
        PianoRollToolbarLayout.StyleCombo(_newLength);
        _newLength.Focusable = false;
        _newLength.SelectedItem = editing.PianoRollNoteLength;
        PrefixSelection(_newLength, "New length: ");
        ToolTip.SetTip(_newLength, "Length of new notes: Grid follows the snap value; Last used repeats the last drawn length");
        AutomationProperties.SetName(_newLength, "Length of new notes");
        _newLength.SelectionChanged += (_, _) => { if (_newLength.SelectedItem is string choice) { _host.Editing().PianoRollNoteLength = choice; Surface.NoteLengthChoice = choice; _host.SaveSettings(); } };
        PianoRollToolbarLayout.StyleButton(_ghosts).Focusable = false;
        _ghosts.IsChecked = editing.PianoRollGhosts;
        ToolTip.SetTip(_ghosts, "Ghost notes: show the other takes and tracks faintly behind this track's notes (read-only)");
        _ghosts.Click += (_, _) => { _host.Editing().PianoRollGhosts = _ghosts.IsChecked == true; _host.SaveSettings(); RefreshDerived(); Surface.InvalidateVisual(); };
        PianoRollToolbarLayout.StyleButton(_names).Focusable = false;
        ToolTip.SetTip(_names, "Key strip: note names (drum names on a drum track) instead of piano keys; saved with this track. The Piano / Names pair sits above the key strip");
        _names.Click += (_, _) => SetNoteNames(_names.IsChecked == true);
        PianoRollToolbarLayout.StyleCombo(_chord);
        _chord.Focusable = false;
        PrefixSelection(_chord, "Chord: ");
        ToolTip.SetTip(_chord, "Chord stamp: Draw and step input add this chord on the note you place (Off: single notes)");
        AutomationProperties.SetName(_chord, "Chord stamp");
        _chord.SelectionChanged += (_, _) => Surface.Chord = _chord.SelectedItem as string ?? "Off";
        PianoRollToolbarLayout.StyleCombo(_voice);
        _voice.Focusable = false;
        ToolTip.SetTip(_voice, "The tab voice new notes go into (the second voice's notes have a dashed inner edge). Shown on tab tracks only");
        AutomationProperties.SetName(_voice, "Tab voice");
        _voice.SelectionChanged += (_, _) => Surface.DrawVoice = Math.Max(0, _voice.SelectedIndex);
    }

    /// <summary>Makes the closed box read 'Prefix: choice' while the open list keeps the bare choices.</summary>
    private static void PrefixSelection(ComboBox combo, string prefix) =>
        combo.SelectionBoxItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) => new TextBlock { Text = prefix + item, VerticalAlignment = VerticalAlignment.Center });

    private Button Command(string icon, string tip, Action run)
    {
        var button = PianoRollToolbarLayout.StyleButton(new Button { Content = Icon(icon), Focusable = false });
        PianoRollToolLook.Apply(button);
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
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = PianoRollToolbarLayout.GroupGap, Children = { new TextBlock { Text = label, MinWidth = PianoRollToolbarLayout.ComboMinWidth, VerticalAlignment = VerticalAlignment.Center } } };
            foreach (var item in items) row.Children.Add(item);
            return row;
        }
        var hint = new TextBlock { Text = "Acts on the selection, or every note when none is selected.", TextWrapping = TextWrapping.Wrap, MaxWidth = PopupSize };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var panel = new StackPanel
        {
            Spacing = PianoRollToolbarLayout.GroupGap, MinWidth = PopupSize,
            Children = { Row("Grid", new TextBlock { Text = "Follows snap" }), Row("Strength", strength, strengthText), Row("Swing", swing, swingText), Row("Move", starts, ends), hint },
        };
        var border = new Border { Child = panel }.WithPadding(PianoRollToolbarLayout.GroupGap * 2, PianoRollToolbarLayout.GroupGap * 2);
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
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

    /// <summary>The magnet button's action and the S key: flips the shared snap and relights the button.</summary>
    internal void ToggleSnapNow() { _host.ToggleSnap(); RefreshTools(); }

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
            _followButton.IsChecked = _host.Follow().FollowPlayback;
            RefreshScaleControls();
            _chipDot.Fill = TrackColour() is { } colour ? new SolidColorBrush(AvaloniaPalette.ColorOf(colour)) : null;
        }
        finally { _refreshing = false; }
        _scaleHost?.Notify();   // an open scale panel follows the toolbar
    }
}

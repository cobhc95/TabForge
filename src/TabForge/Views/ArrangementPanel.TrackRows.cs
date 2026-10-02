using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// ArrangementPanel: track tint and building the per-track control rows.
public sealed partial class ArrangementPanel
{
    /// <summary>The track colour as a subtle row background (darker in the dark theme), or transparent.</summary>
    internal static Color? TintColour(TrackModel? track, VisualOptions options)
    {
        var tint = options.TrackTint;
        if (track is null || !track.TintRow || tint <= 0) return null;
        var colour = Draw.Tame(ParseColour(track.ColorHex));
        if (!VisualTheme.IsLight) colour = FretboardRenderer.Blend(colour, Colors.Black, 0.35);
        // On a light background the same alpha reads much stronger than on a dark one: softer there.
        var strength = Math.Clamp(tint, 0, 0.6) * (VisualTheme.IsLight ? 0.6 : 1);
        return Color.FromArgb((byte)Math.Round(255 * strength), colour.R, colour.G, colour.B);
    }

    /// <summary>Opacity of a muted track row's name and controls for a dimming strength of 0..1.</summary>
    internal static double MutedRowOpacity(double dim) => 1 - 0.9 * Math.Clamp(dim, 0, 1);

    private readonly Dictionary<TrackModel, Dictionary<string, FrameworkElement>> _trackCells = new();

    private void ApplyRowDim(SongProject project, TrackModel track, Dictionary<string, FrameworkElement> cells)
    {
        var opacity = track.Mute && !MixerGroups.IsAudible(project, track) && ViewOptions.MutedDim > 0 ? MutedRowOpacity(ViewOptions.MutedDim) : 1;
        foreach (var (key, cell) in cells)
            if (key is not ("mute" or "solo")) cell.Opacity = opacity;
    }

    /// <summary>True while a group mute/solo reports its mix change (the host then skips the full mix refresh; the mute/solo path carries the sound).</summary>
    internal bool InMuteSoloGesture { get; private set; }

    /// <summary>Mute/solo click: every row's dimming and the timeline lanes follow the new state at once, without rebuilding the list.</summary>
    internal void ApplyMuteVisualsNow()
    {
        if (_project is not { } project) return;
        foreach (var (track, cells) in _trackCells) ApplyRowDim(project, track, cells);
        _timeline.InvalidateVisual();
    }

    /// <summary>Test probe: the opacity of a track row's name cell.</summary>
    internal double RowNameOpacityForTest(TrackModel track) => _trackCells.TryGetValue(track, out var cells) && cells.TryGetValue("name", out var name) ? name.Opacity : -1;

    private Brush TintBrush(int index) =>
        _project is { } p && index >= 0 && index < p.Tracks.Count && TintColour(p.Tracks[index], ViewOptions) is { } c ? Draw.Solid(c) : Brushes.Transparent;

    // Volume / pan sliders of the track and group rows with their model value, for SyncMixValues (no rebuild).
    private readonly List<(Slider Slider, Func<double> Model)> _mixSliders = new();

    /// <summary>
    /// A mix value changed elsewhere (a Mixer drag): moves the matching track-list sliders to the model value in place.
    /// Cheap (no rebuild, no layout pass of its own); the sliders' change handlers see the model already equal and write nothing.
    /// </summary>
    public void SyncMixValues()
    {
        foreach (var (slider, model) in _mixSliders)
        {
            var v = Math.Clamp(model(), slider.Minimum, slider.Maximum);
            if (slider.Value != v && !slider.IsMouseCaptured) slider.Value = v;
        }
    }

    private void RebuildControls()
    {
        using var slowTrace = TabForge.Views.SlowTrace.Measure("track rows rebuild", 0);
        _controls.Children.Clear();
        _mixSliders.Clear();
        _inputMeters.Clear();
        _rowGrids.Clear();
        _trackCells.Clear();
        _columnRowCells.Clear();
        _rowSeparators.Clear();
        _trackRows.Clear();
        _renameStarters.Clear();
        _rowTransforms.Clear();
        var project = _project;
        if (project is null) return;

        var showGroups = ShowsGroups(project);
        for (var i = 0; i < project.Tracks.Count; i++)
        {
            var track = project.Tracks[i];
            var index = i;
            if (showGroups && StartsGroup(project, i)) _controls.Children.Add(GroupHeader(project, i));
            var row = new TrackRowBorder
            {
                Height = RowHeightOf(project, track),
                Visibility = IsCollapsed(project, i) ? Visibility.Collapsed : Visibility.Visible,
                BorderBrush = (Brush)Application.Current.FindResource("BorderSoftBrush"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = TintBrush(i),
                Cursor = Cursors.Arrow, Focusable = true, FocusVisualStyle = null,
                RenderTransform = new TranslateTransform()
            };
            _rowTransforms.Add((TranslateTransform)row.RenderTransform);
            System.Windows.Automation.AutomationProperties.SetName(row, $"Track {index + 1}: {track.Name}{(track.Mute ? ", muted" : "")}{(track.Solo ? ", solo" : "")}");
            var grid = new Grid { Margin = new Thickness(RowGridLeft, 0, RowGridRight, 0) };
            AddColumnDefinitions(grid);
            _rowGrids.Add(grid);
            var cells = new Dictionary<string, FrameworkElement>();

            // Record-arm (where the colour square was); the track colour is on the number's right-click menu.
            var arm = new RecordArmButton { Armed = track.RecordArm, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            arm.Toggled += (_, _) => ArmRequested?.Invoke(this, index);
            void ColourMenu(FrameworkElement target, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)), PlacementTarget = target };
                foreach (var (colourName, hex) in TrackColourPalette)
                {
                    var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = ParseBrush(hex, Brushes.Gray) };
                    var item = new MenuItem { Header = colourName, Icon = swatch, IsCheckable = true,
                        IsChecked = string.Equals(track.ColorHex, hex, StringComparison.OrdinalIgnoreCase),
                        Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
                    item.Click += (_, _) =>
                    {
                        TrackEditRequested?.Invoke(new TrackEditRequest(index, TrackEditKind.SetColor, hex));
                        TrackColorChanged?.Invoke(this, EventArgs.Empty);
                    };
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            }
            var settings = new Button
            {
                Width = 18, Height = 18, Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.FindResource("TransportButton"),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)Application.Current.FindResource("IconCog"),
                    Style = (Style)Application.Current.FindResource("IconPath"),
                    Width = 13, Height = 13, Stretch = Stretch.Uniform
                }
            };
            TooltipShortcuts.Bind(settings, "Track properties (instrument, tuning, mixer, details)", "Track.Properties");
            System.Windows.Automation.AutomationProperties.SetName(settings, $"Track {index + 1} properties");
            settings.Click += (_, _) => TrackOptionsRequested?.Invoke(this, index);
            cells["colour"] = arm;
            cells["settings"] = settings;

            var handle = BuildTrackHandle(index, track);
            var handleElement = (FrameworkElement)handle;
            handleElement.ToolTip = "Drag to reorder · right-click to change the track colour";
            handleElement.MouseRightButtonUp += (_, e) => ColourMenu(handleElement, e);
            cells["number"] = handleElement;

            var name = new TextBox
            {
                // Left-aligned so it is only as wide as its text: double-clicking the text renames,
                // double-clicking anywhere else on the row opens the track properties.
                Text = track.Name, Margin = new Thickness(0, 2, 4, 2), Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Double-click to edit; drag to reorder",
                IsReadOnly = true, Cursor = Cursors.Arrow, Focusable = false,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0), Effect = null
            };
            void BeginRename()
            {
                _editingTrackName = name;
                _editingTrackModel = track;
                name.Focusable = true;
                name.HorizontalAlignment = HorizontalAlignment.Stretch;
                name.IsReadOnly = false;
                name.Cursor = Cursors.IBeam;
                name.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "WindowBrush");
                name.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");
                name.BorderThickness = new Thickness(1);
                name.Focus();
                name.SelectAll();
            }
            name.MouseDoubleClick += (_, e) => { BeginRename(); e.Handled = true; };
            _renameStarters[track] = BeginRename;
            name.LostFocus += (_, _) =>
            {
                if (!name.IsReadOnly) FinishTrackNameEdit(name, track, commit: true);
            };
            name.PreviewKeyDown += (_, e) =>
            {
                if (name.IsReadOnly) return;
                if (e.Key == Key.Enter)
                {
                    FinishTrackNameEdit(name, track, commit: true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    FinishTrackNameEdit(name, track, commit: false);
                    e.Handled = true;
                }
            };
            cells["name"] = name;

            // Split FX button: "FX" opens the chain, the power part switches chain / Windows MIDI.
            var fx = new FxSplitButton
            {
                ChainOn = track.SoundSource == SoundSources.Plugins, PluginCount = track.Rig.Plugins.Count,
                Faulted = IsChainFaulted(track.Rig, QuarantinedPlugins?.Invoke()),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 50, Height = 22
            };
            fx.OpenChain += (_, _) => FxChainRequested?.Invoke(this, index);
            fx.TogglePower += (_, _) => FxPowerRequested?.Invoke(this, index);
            cells["fx"] = fx;

            cells["mute"] = ToggleIconButton("IconMute", track.Mute, () =>
            {
                TrackEditRequested?.Invoke(new TrackEditRequest(index, TrackEditKind.ToggleMute));
                ApplyMuteVisualsNow();
                MuteSoloChanged?.Invoke(this, EventArgs.Empty);
            }, "Mute track");
            cells["solo"] = ToggleIconButton("IconSolo", track.Solo, () =>
            {
                TrackEditRequested?.Invoke(new TrackEditRequest(index, TrackEditKind.ToggleSolo));
                ApplyMuteVisualsNow();
                MuteSoloChanged?.Invoke(this, EventArgs.Empty);
            }, "Solo track");

            // Full-resolution volume: 0-127 (default 100), one unit per step; the wheel moves 1 (Ctrl: 8).
            var volume = new Slider
            {
                Minimum = 0, Maximum = 127, Value = Math.Clamp(track.Volume, 0, 127), IsSnapToTickEnabled = true, TickFrequency = 1,
                SmallChange = 1, LargeChange = 8, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.FindResource("ArrangementSlider"),
            };
            volume.ValueChanged += (_, e) =>
            {
                var midi = Math.Clamp((int)Math.Round(e.NewValue), 0, 127);
                if (track.Volume == midi) return;
                track.Volume = midi;
                MixChanged?.Invoke(this, EventArgs.Empty);
            };
            AttachMixEditGestures(volume);
            _mixSliders.Add((volume, () => track.Volume));
            AttachSmoothDrag(volume, 100);
            AttachWheelStep(volume);
            FrameworkElement volumeControl = volume;
            if (VolumeKnobs)
            {
                var knob = new KnobControl
                {
                    Minimum = 0, Maximum = 127, DefaultValue = 100, Value = track.Volume,
                    Label = "Volume", Format = v => $"{(int)v}", HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0)
                };
                knob.ValueChanged += (_, e) => { track.Volume = (int)e.NewValue; MixChanged?.Invoke(this, EventArgs.Empty); };
                knob.EditStarted += (_, _) => MixEditStarting?.Invoke(this, EventArgs.Empty);
                knob.EditEnded += (_, _) => MixEditEnded?.Invoke(this, EventArgs.Empty);
                volumeControl = knob;
            }
            cells["volume"] = volumeControl;

            FrameworkElement panControl;
            if (PanKnobs)
            {
                var knob = new KnobControl
                {
                    Minimum = 0, Maximum = 127, DefaultValue = 64, Origin = 64, Value = track.Pan,
                    Label = "Pan", Format = PanText, Parse = KnobValueParser.ParsePan, HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0)
                };
                knob.ValueChanged += (_, e) => { track.Pan = (int)e.NewValue; MixChanged?.Invoke(this, EventArgs.Empty); };
                knob.EditStarted += (_, _) => MixEditStarting?.Invoke(this, EventArgs.Empty);
                knob.EditEnded += (_, _) => MixEditEnded?.Invoke(this, EventArgs.Empty);
                panControl = knob;
            }
            else
            {
                // Full-resolution pan: -64 (left) .. 0 (centre) .. +63 (right), one MIDI unit per step.
                var pan = new Slider
                {
                    Minimum = -64, Maximum = 63, Value = Math.Clamp(track.Pan, 0, 127) - 64, IsSnapToTickEnabled = true, TickFrequency = 1,
                    SmallChange = 1, LargeChange = 8, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.FindResource("ArrangementSlider"),
                };
                pan.ValueChanged += (_, e) =>
                {
                    var midi = Math.Clamp(64 + (int)Math.Round(e.NewValue), 0, 127);
                    if (track.Pan == midi) return;
                    track.Pan = midi;
                    MixChanged?.Invoke(this, EventArgs.Empty);
                };
                AttachMixEditGestures(pan);
                _mixSliders.Add((pan, () => Math.Clamp(track.Pan, 0, 127) - 64));
                AttachSmoothDrag(pan, 0);
                AttachWheelStep(pan);
                panControl = pan;
            }
            panControl.ContextMenu = PanContextMenu(track, panControl);
            cells["pan"] = panControl;

            FrameworkElement instrument = track.IsAudio ? AudioKindCell() : InstrumentButton(track, selected =>
            {
                TrackEditRequested?.Invoke(new TrackEditRequest(index, TrackEditKind.SelectInstrument, selected));
                ProjectEdited?.Invoke(this, EventArgs.Empty);
            });
            cells["instrument"] = instrument;
            // An explicitly muted track reads grey: its name and controls are dimmed (the M box stays full strength, red).
            _trackCells[track] = cells;
            ApplyRowDim(project, track, cells);
            PlaceCells(grid, cells);

            if (HasAudioLane(track))
            {
                // The row's controls on top; each clip lane's strip (play button, input, meter) underneath.
                grid.Height = track.IsAudio ? AudioControlsHeight : TrackRowHeight;
                grid.VerticalAlignment = VerticalAlignment.Top;
                var stack = new StackPanel();
                stack.Children.Add(grid);
                for (var lane = 0; lane < LaneCountOf(track); lane++) stack.Children.Add(AudioLaneStrip(index, track, lane));
                row.Child = stack;
            }
            else row.Child = grid;
            row.PreviewMouseLeftButtonDown += (_, e) =>
            {
                var source = e.OriginalSource as DependencyObject;
                if (e.ClickCount > 1 && IsInside(source, name)) return;
                if (e.ClickCount > 1 && !IsInteractiveTrackControl(source, row))
                {
                    // Double-click anywhere on the row except the name text or a control: properties.
                    TrackOptionsRequested?.Invoke(this, index);
                    e.Handled = true;
                    return;
                }
                if (IsInside(source, arm) || IsInteractiveTrackControl(source, row)) return;
                BeginControlTrackDrag(index, row, e.GetPosition(_controls));
                FocusTrackRow(index);
                e.Handled = true;
            };
            row.MouseMove += (_, e) => UpdateControlTrackDrag(index, e);
            row.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (_dragCaptureRow == row) EndControlTrackDrag(row, e);
            };
            row.LostMouseCapture += (_, _) => CancelControlTrackDrag(row);
            row.PreviewMouseRightButtonUp += (_, e) =>
            {
                // A right-click on a control (knob type-in, pan menu, slider, buttons) or on the number (colour menu)
                // belongs to that control; only the row's own background opens Track properties.
                var src = e.OriginalSource as DependencyObject;
                if (IsInside(src, arm) || IsInside(src, handleElement) || IsInteractiveTrackControl(src, row)) return;
                if (TrackRowMenuRequested is { } menuRequested)
                {
                    TrackSelected?.Invoke(this, index);
                    // The row takes the keyboard focus before the menu opens: focus moved afterwards would close the menu at once.
                    // When the menu closes the focus returns to the row, so the track-row hotkeys act on it.
                    if (index < _trackRows.Count) _trackRows[index].Focus();   // the selection may have rebuilt the rows
                    menuRequested(this, index);
                    e.Handled = true;
                    return;
                }
                if (track.IsAudio) { ShowAudioRowMenu(index, row); e.Handled = true; return; }   // properties, or convert to an instrument track
                TrackOptionsRequested?.Invoke(this, index);
                e.Handled = true;
            };
            _trackRows.Add(row);
            _controls.Children.Add(row);
        }
        if (BuildAddLane() is { } addLane) _controls.Children.Add(addLane);   // rows end flush against it
    }
}

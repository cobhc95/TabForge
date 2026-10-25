using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// The controls of each armed track's sub-row in the track list: the monitor toggle (a speaker, accent when on) and the input picker, placed at
/// the positions <see cref="ArmedRowPainter"/> gives. While a track is armed it also polls the input peak of the armed tracks and gives it to the
/// header's meter, at about 30 Hz and only while something is armed.
/// Owns: the two controls of each armed track, their positions, the monitor and input edits (one undo step each, without a timeline change) and the
/// level poll.
/// Does not own: the sub-row's drawing and sizes (ArmedRowPainter), the arm itself (the arm cell and the arm key), the engine sync (the window
/// subscribes to <see cref="SettingChanged"/> and sets <see cref="InputLevelOf"/>), the track rows' geometry (TimelineView).
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public sealed class ArmedRowLayer : Canvas
{
    /// <summary>The level poll's period: about 30 Hz, enough for a meter. The poll runs only while a track is armed and the layer is attached.</summary>
    public static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(UiTiming.ArmedRowMeterIntervalMs);

    /// <summary>A level change smaller than this is not repainted.</summary>
    public const double LevelStep = 0.005;

    private const double MonitorGlyphSize = 12;

    private readonly TimelineView _timeline;
    private readonly TrackHeaderColumn _header;
    private IEditableDocument _document;
    private bool _attached;
    private readonly SharedMeterTick _meter;
    private readonly bool _ownsMeter;   // true when no shared tick was handed in (tests): this layer made it and disposes it on detach
    private bool _metered;   // true while this layer rides the shared meter tick
    // Listens on the DOCUMENT while attached: an undo or redo of an arm change replaces the tracks, and the controls follow it without waiting for a reload.
    private readonly DocumentWatch _watch = new();

    // The two controls of each armed track. Key: the armed track's id (an undo replaces the TrackModel, never the id); ceiling: the armed tracks (removed when the track disarms); thread: UI only.
    private readonly Dictionary<Guid, (ToggleButton Monitor, ComboBox Input)> _controls = new();

    /// <param name="document">The shown song's document: the edits the controls make go to it.</param>
    /// <param name="ui">The UI thread (kept for the call shape; the level poll rides the shared meter tick).</param>
    /// <param name="meter">The app's shared meter tick; null gives the layer its own (disposed when the layer detaches).</param>
    public ArmedRowLayer(TimelineView timeline, TrackHeaderColumn header, IEditableDocument document, IUiThread ui, SharedMeterTick? meter = null)
    {
        _ownsMeter = meter is null;
        _meter = meter ?? new SharedMeterTick();
        _timeline = timeline;
        _header = header;
        _document = document;
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _watch.ProjectChanged = (_, _) => Sync();
        _watch.Restored = Sync;
    }

    /// <summary>Raised after a monitor or input edit changed the song; the window pushes the engine's mix and routing.</summary>
    public event Action? SettingChanged;

    /// <summary>The input meter fraction (0..1) of a track, polled while it is armed; null shows no level. The window sets it from the engine peak.</summary>
    public Func<TrackModel, double>? InputLevelOf { get; set; }

    /// <summary>Shows <paramref name="document"/>'s armed tracks from now on.</summary>
    public void Show(IEditableDocument document)
    {
        _document = document;
        if (_attached) _watch.Bind(document);
        Sync();
    }

    /// <summary>
    /// Brings the controls and the poll in line with the song: a control per armed track, placed on its sub-row, the layer over the track rows only
    /// (under the ruler and the sections, above the Add track strip), and the poll running only while something is armed. Runs after each reload,
    /// scroll and layout pass; it writes a property only when its value changed.
    /// </summary>
    public void Sync()
    {
        var project = _document.Project;
        IsVisible = _header.IsVisible;
        var top = _timeline.TrackRowsTop;
        var origin = new Thickness(0, top, 0, 0);
        if (Margin != origin) Margin = origin;
        if (Width != _header.ColumnWidth) Width = _header.ColumnWidth;
        var height = Math.Max(0, _timeline.TrackRowsBottom - top);
        if (Height != height) Height = height;
        var armed = project.Tracks.Where(track => track.RecordArm).ToList();
        foreach (var id in _controls.Keys.Where(id => !armed.Any(track => track.Id == id)).ToList()) Remove(id);
        foreach (var track in armed.Where(track => !_controls.ContainsKey(track.Id))) Add(track);
        foreach (var track in armed) Place(project, track, _controls[track.Id]);
        UpdateMeterTimer(armed.Count > 0);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _watch.Bind(_document);
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _watch.Dispose();
        UpdateMeterTimer(anyArmed: false);
        if (_ownsMeter) _meter.Dispose();
    }

    // The track of the shown song with this id now; an undo or redo may have replaced the object a control was made for.
    private TrackModel? Current(Guid id) => _document.Project.Tracks.FirstOrDefault(track => track.Id == id);

    private void Add(TrackModel track)
    {
        var id = track.Id;
        var glyph = new Path { Data = Geometry.Parse(IconGeometry.Speaker), Stretch = Stretch.Uniform, Width = MonitorGlyphSize, Height = MonitorGlyphSize };
        var monitor = new ToggleButton
        {
            Width = ArmedRowPainter.MonitorWidth, Height = ArmedRowPainter.MonitorHeight, Padding = new Thickness(0), IsChecked = track.MonitorInput,
            Content = glyph,
        };
        ToolTip.SetTip(monitor, "Monitor the input: you hear it live while armed");
        // Flat chrome like the other row buttons: transparent at rest, the hover and press surfaces under the pointer.
        monitor.Background = Brushes.Transparent;
        monitor.BorderThickness = new Thickness(0);
        monitor.PointerEntered += (_, _) => SetSurface(monitor, "HoverSurface");
        monitor.PointerExited += (_, _) => SetSurface(monitor, null);
        monitor.PointerPressed += (_, _) => SetSurface(monitor, "PressSurface");
        monitor.PointerReleased += (_, _) => SetSurface(monitor, monitor.IsPointerOver ? "HoverSurface" : null);
        PaintMonitor(monitor, glyph);
        monitor.IsCheckedChanged += (_, _) =>
        {
            PaintMonitor(monitor, glyph);
            SetMonitor(id, monitor.IsChecked == true);
        };
        var input = AudioInputPicker.Bind(new ComboBox { Width = ArmedRowPainter.InputWidth, Height = ArmedRowPainter.InputHeight, FontSize = ArmedRowPainter.TextSize }, () => Current(id)?.AudioInput ?? "");
        ToolTip.SetTip(input, "Input recorded and monitored when armed: an audio input, or MIDI (played live through the track and recorded as a MIDI clip)");
        input.SelectionChanged += (_, _) =>
        {
            if (input.SelectedItem is AudioInputChoice choice && choice.Id != Current(id)?.AudioInput) SetInput(id, choice.Id);
        };
        Children.Add(monitor);
        Children.Add(input);
        _controls[id] = (monitor, input);
    }

    private void Remove(Guid id)
    {
        if (!_controls.Remove(id, out var controls)) return;
        Children.Remove(controls.Monitor);
        Children.Remove(controls.Input);
    }

    /// <summary>The monitor button's surface: the named palette surface, or transparent at rest.</summary>
    private static void SetSurface(Button monitor, string? surface)
    {
        if (surface is null) monitor.Background = Brushes.Transparent;
        else monitor[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension(surface);
    }

    /// <summary>The speaker is the accent colour while monitoring is on and the muted text colour while it is off.</summary>
    private static void PaintMonitor(ToggleButton monitor, Path glyph) =>
        glyph[!Shape.FillProperty] = new DynamicResourceExtension(monitor.IsChecked == true ? "IconActive" : "IconDisabled");

    private void Place(SongProject project, TrackModel track, (ToggleButton Monitor, ComboBox Input) controls)
    {
        var row = project.Tracks.IndexOf(track);
        if (row < 0) return;
        // An undo may have changed the track's monitor or input: the controls show the current track's values (their change handlers find nothing to edit).
        if (controls.Monitor.IsChecked != track.MonitorInput) controls.Monitor.IsChecked = track.MonitorInput;
        if ((controls.Input.SelectedItem as AudioInputChoice)?.Id != track.AudioInput) AudioInputPicker.Refresh(controls.Input, track.AudioInput);
        // The strip sits at the bottom of the track's base band: under it when it has no lanes, inside the last lane band otherwise.
        var subTop = ArmedRowPainter.StripTopOf(track, _timeline.RowTop(row) + _timeline.BaseBandHeightOf(row)) - _timeline.TrackRowsTop;
        SetPosition(controls.Monitor, ArmedRowPainter.MonitorLeft, subTop + (ArmedRowPainter.SubRowHeight - ArmedRowPainter.MonitorHeight) / 2);
        SetPosition(controls.Input, ArmedRowPainter.InputLeft, subTop + (ArmedRowPainter.SubRowHeight - ArmedRowPainter.InputHeight) / 2);
    }

    private static void SetPosition(Control control, double left, double top)
    {
        if (Canvas.GetLeft(control) != left) Canvas.SetLeft(control, left);
        if (Canvas.GetTop(control) != top) Canvas.SetTop(control, top);
    }

    private void SetMonitor(Guid id, bool on)
    {
        if (Current(id) is not { } track || track.MonitorInput == on) return;
        // Monitoring is not song content: it is saved with the track but never an undo step.
        track.MonitorInput = on;
        _document.Project.IsDirty = true;
        AnnounceSettingChanged();
    }

    private void SetInput(Guid id, string input)
    {
        if (Current(id) is not { } track || track.AudioInput == input) return;
        // The chosen input is monitoring state: saved, but never an undo step.
        track.AudioInput = input;
        _document.Project.IsDirty = true;
        AnnounceSettingChanged();
    }

    // The sub-row's label follows the input (audio or MIDI), so the header repaints with the change.
    private void AnnounceSettingChanged()
    {
        _header.InvalidateVisual();
        SettingChanged?.Invoke();
    }

    private void PollLevels()
    {
        if (!_attached || !IsVisible || InputLevelOf is null) return;
        var tracks = _document.Project.Tracks;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (!_controls.ContainsKey(track.Id)) continue;
            var peak = InputLevelOf(track);
            if (Math.Abs(peak - _header.InputLevelOf(track)) >= LevelStep) _header.ShowInputLevel(track, peak);
        }
    }

    private void UpdateMeterTimer(bool anyArmed)
    {
        // Gated on activity (armed and attached) and shared with the tuner: one 33 ms tick while anyone listens.
        var running = anyArmed && _attached;
        if (running == _metered) return;
        _metered = running;
        if (running) _meter.Subscribe(PollLevels);
        else _meter.Unsubscribe(PollLevels);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// The controls of each armed track's sub-row in the track list: the monitor toggle (a speaker, accent when on) and the input picker, joined as one pill
/// (<see cref="ArmedInputPill"/>) and placed at the positions <see cref="ArmedRowPainter"/> gives. While a track is armed it also polls the input peak of the armed tracks and gives it to the
/// header's meter: audio peaks at about 30 Hz while an audio-input track is armed, and a MIDI input's note velocity on display frames (the screen's refresh) that run only
/// while a bar or tick is still falling (a note starts them; idle runs none).
/// Owns: the two controls of each armed track, their positions, the monitor and input edits (one undo step each, without a timeline change) and the
/// level poll.
/// Does not own: the sub-row's drawing and sizes (ArmedRowPainter), the arm itself (the arm cell and the arm key), the engine sync (the window
/// subscribes to <see cref="SettingChanged"/> and sets <see cref="InputLevelOf"/>), the track rows' geometry (TimelineView).
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests, ArmedRowMidiMeterTests.
/// </summary>
public sealed class ArmedRowLayer : Canvas
{
    /// <summary>The level poll's period: about 30 Hz, enough for a meter. The poll runs only while a track is armed and the layer is attached.</summary>
    public static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(UiTiming.ArmedRowMeterIntervalMs);

    /// <summary>A level change smaller than this is not repainted.</summary>
    public const double LevelStep = 0.005;

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
    private readonly Dictionary<Guid, (ToggleButton Monitor, TrackInputPicker Input)> _controls = new();
    // The velocity meter of each armed MIDI track, moved by the meter tick. Key: the armed track's id; ceiling: the armed tracks (removed with the controls); thread: UI only.
    private readonly Dictionary<Guid, MidiVelocityMeter> _velocity = new();
    private readonly IUiThread _ui;
    private readonly Action _startFrames, _wake;
    private readonly Action<TimeSpan> _onFrame;
    private Action? _unlisten;   // ends the note subscription made through NoteSource
    private Func<Action, Action>? _noteSource;
    private int _wakePending;   // 1 from a note heard on the driver thread until the UI thread has started the frames: one post per wake, never per note
    private bool _framing;   // true while a frame is requested or running for the MIDI meters
    private readonly Func<IReadOnlyList<string>> _midiDevices;

    /// <param name="document">The shown song's document: the edits the controls make go to it.</param>
    /// <param name="ui">The UI thread (the input pickers' device poll; the level poll rides the shared meter tick).</param>
    /// <param name="meter">The app's shared meter tick; null gives the layer its own (disposed when the layer detaches).</param>
    /// <param name="midiDevices">Lists the MIDI input devices present now (the input pickers poll it on a worker); null: none.</param>
    public ArmedRowLayer(TimelineView timeline, TrackHeaderColumn header, IEditableDocument document, IUiThread ui, SharedMeterTick? meter = null,
        Func<IReadOnlyList<string>>? midiDevices = null)
    {
        _ui = ui;
        _startFrames = StartFrames;
        _wake = Wake;
        _onFrame = OnFrame;
        _midiDevices = midiDevices ?? (() => Array.Empty<string>());
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
        Styles.Add(ArmedInputPill.Build());
    }

    /// <summary>Raised after a monitor or input edit changed the song; the window pushes the engine's mix and routing.</summary>
    public event Action? SettingChanged;

    /// <summary>The input meter fraction (0..1) of a track, polled while it is armed; null shows no level. The window sets it from the engine peak.</summary>
    public Func<TrackModel, double>? InputLevelOf { get; set; }

    /// <summary>The latest MIDI note-on a track heard from its chosen inputs, packed (<see cref="MidiNoteSlot.Packed"/>); null or 0 shows no velocity. The window sets it from the MIDI monitor.</summary>
    public Func<TrackModel, long>? MidiNoteOf { get; set; }

    /// <summary>
    /// Subscribes a wake-up to the notes MIDI-armed tracks hear (returns the way to unsubscribe); the window sets it. A note then starts the meter's display frames,
    /// which run only while a bar or tick is still falling.
    /// </summary>
    public Func<Action, Action>? NoteSource
    {
        get => _noteSource;
        set { Unlisten(); _noteSource = value; if (_attached) Listen(); }
    }

    /// <summary>Asks for the next display frame (the screen's refresh); null: the shown window's <see cref="TopLevel.RequestAnimationFrame"/>. Tests replace it.</summary>
    public Action<Action<TimeSpan>>? RequestFrame { get; set; }

    /// <summary>The clock of the velocity meter's fall, in milliseconds; tests replace it.</summary>
    public Func<double> NowMs { get; set; } = () => Environment.TickCount64;

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
        UpdateMeterTimer(armed.Any(track => AudioInputs.HasAudio(track.AudioInput)));   // the audio peaks have no event: polled on the shared tick; MIDI rides display frames
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Listen();
        _watch.Bind(_document);
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Unlisten();
        _watch.Dispose();
        UpdateMeterTimer(anyArmed: false);
        if (_ownsMeter) _meter.Dispose();
    }

    // The track of the shown song with this id now; an undo or redo may have replaced the object a control was made for.
    private TrackModel? Current(Guid id) => _document.Project.Tracks.FirstOrDefault(track => track.Id == id);

    private void Add(TrackModel track)
    {
        var id = track.Id;
        var monitor = new ToggleButton { Width = ArmedRowPainter.MonitorWidth, IsChecked = track.MonitorInput };
        ToolTip.SetTip(monitor, "Monitor the input: you hear it live while armed");
        monitor.WithNoMargin();
        monitor.IsCheckedChanged += (_, _) => SetMonitor(id, monitor.IsChecked == true);
        var input = new TrackInputPicker(() => Current(id)?.AudioInput ?? "", () => Current(id)?.MidiInputs ?? new(), pick => SetInput(id, pick), list => SetMidiInputs(id, list),
            _ui, _midiDevices)
        {
            Width = ArmedRowPainter.InputWidth, FontSize = ArmedRowPainter.TextSize,
        };
        input.WithNoMargin();
        ArmedInputPill.Join(monitor, input);   // one joined pill: the monitor toggle as its left half, the input selector as its right half
        Children.Add(monitor);
        Children.Add(input);   // after the monitor, so the shared divider is the input half's left edge
        _controls[id] = (monitor, input);
    }

    private void Remove(Guid id)
    {
        if (!_controls.Remove(id, out var controls)) return;
        _velocity.Remove(id);
        Children.Remove(controls.Monitor);
        Children.Remove(controls.Input);
    }

    private void Place(SongProject project, TrackModel track, (ToggleButton Monitor, TrackInputPicker Input) controls)
    {
        var row = project.Tracks.IndexOf(track);
        if (row < 0) return;
        // An undo may have changed the track's monitor or input: the controls show the current track's values (their change handlers find nothing to edit).
        if (controls.Monitor.IsChecked != track.MonitorInput) controls.Monitor.IsChecked = track.MonitorInput;
        controls.Input.Refresh();
        // The strip sits directly under the main line, the same offset in every track state; the pill is centred in it.
        var subTop = _timeline.ArmedStripTopOf(row) - _timeline.TrackRowsTop;
        var pill = ArmedRowPainter.PillHeightOf(_timeline.RowHeight);
        ArmedInputPill.Fit(controls.Monitor, controls.Input, pill);
        SetPosition(controls.Monitor, ArmedRowPainter.MonitorLeft, subTop + (ArmedRowPainter.SubRowHeight - pill) / 2);
        SetPosition(controls.Input, ArmedRowPainter.InputLeft, subTop + (ArmedRowPainter.SubRowHeight - pill) / 2);
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

    private void SetMidiInputs(Guid id, IReadOnlyList<string> devices)
    {
        if (Current(id) is not { } track || track.MidiInputs.SequenceEqual(devices)) return;
        // Like the audio input: monitoring state, saved with the track, never an undo step.
        track.MidiInputs = devices.ToList();
        _document.Project.IsDirty = true;
        AnnounceSettingChanged();
    }

    // The sub-row's label follows the input (audio or MIDI), so the header repaints with the change.
    private void AnnounceSettingChanged()
    {
        _header.InvalidateVisual();
        SettingChanged?.Invoke();
    }

    internal void PollLevels()
    {
        if (!_attached || !IsVisible || (InputLevelOf is null && MidiNoteOf is null)) return;
        var tracks = _document.Project.Tracks;
        var now = NowMs();
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            if (!_controls.ContainsKey(track.Id)) continue;
            var peak = InputLevelOf?.Invoke(track) ?? 0;
            var hold = 0.0;
            if (MidiNoteOf is not null && AudioInputs.TakesMidi(track))
            {
                // A MIDI input's meter shows the velocity of its notes: it jumps on a note-on, holds a tick, then falls; at rest it changes nothing, so nothing repaints.
                if (!_velocity.TryGetValue(track.Id, out var meter)) _velocity[track.Id] = meter = new MidiVelocityMeter();
                meter.Update(MidiNoteOf(track), now);
                peak = Math.Max(peak, meter.Level);
                hold = meter.Hold;
            }
            if (Moved(_header.InputLevelOf(track), peak) || Moved(_header.InputHoldOf(track), hold)) _header.ShowInputLevel(track, peak, hold);
        }
    }

    // A change of at least LevelStep, or the fall's last bit to rest at 0.
    private static bool Moved(double shown, double now) => Math.Abs(now - shown) >= LevelStep || (now == 0 && shown != 0);

    private void Listen()
    {
        _unlisten ??= _noteSource?.Invoke(_wake);
    }

    private void Unlisten()
    {
        _unlisten?.Invoke();
        _unlisten = null;
    }

    // MIDI driver thread: nothing that waits or allocates. Only the first note of a wake posts; the notes after it find the post pending.
    private void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 0) _ui.Post(_startFrames, UiPriority.Render);
    }

    // UI thread: begins the display frames unless they already run.
    private void StartFrames()
    {
        Volatile.Write(ref _wakePending, 0);
        if (!_attached || _framing) return;
        _framing = true;
        Request();
    }

    private void Request()
    {
        if (RequestFrame is { } custom) custom(_onFrame);
        else if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(_onFrame);
        else _framing = false;
    }

    // One display frame: move the bars, and ask for the next frame only while a bar or tick still moves (idle: no frames, no repaint).
    private void OnFrame(TimeSpan time)
    {
        PollLevels();
        if (_attached && IsVisible && AnyVelocityMoving()) Request();
        else _framing = false;
    }

    private bool AnyVelocityMoving()
    {
        foreach (var meter in _velocity.Values) if (!meter.IsIdle) return true;
        return false;
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

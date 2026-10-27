using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>What the piano roll needs from the window: the song, the playback, the shared snap settings, the loop, the sounds, the settings and the
/// refresh after an edit. Every member has a harmless default, so a test can build the view with only a song.</summary>
public sealed class PianoRollHost
{
    public Func<SongDocument?> Document { get; init; } = () => null;
    public Func<AvaloniaPlaybackSession?> Session { get; init; } = () => null;
    /// <summary>The transport's snap settings (the same instance the timeline magnet toggles).</summary>
    public Func<SnapSettings?> Snap { get; init; } = () => null;
    public Action ToggleSnap { get; init; } = () => { };
    public Action OpenSnapSettings { get; init; } = () => { };
    /// <summary>Sounds a pitch on the track's sound (the score's note audition path).</summary>
    public Action<TrackModel, int> Audition { get; init; } = (_, _) => { };
    public Func<EditingSettings> Editing { get; init; } = () => new EditingSettings();
    /// <summary>The appearance settings: the Playback highlight colours the playback needle and the ruler marker.</summary>
    public Func<AppearanceSettings> Appearance { get; init; } = () => new AppearanceSettings();
    /// <summary>Saves the settings quietly (never the full broadcast); called once a gesture ends.</summary>
    public Action SaveSettings { get; init; } = () => { };
    /// <summary>Sets the loop region (start bar, start slot, end bar, end slot or -1 for the bar's end) as one undo step; true when a box selection made
    /// it (the white selection goes).</summary>
    public Action<int, int, int, int, bool> MakeLoop { get; init; } = (_, _, _, _, _) => { };
    /// <summary>After a committed edit (true for a tab track): the score, the timeline and the engine follow.</summary>
    public Action<bool> AfterEdit { get; init; } = _ => { };
    /// <summary>The window's commands (Clear loop, Show in score mode).</summary>
    public IMenuCommandRunner Commands { get; init; } = ContextMenuHost.NoCommands;
    /// <summary>Asks "Edit the tab of (track)?"; true goes ahead.</summary>
    public Func<string, Task<bool>> ConfirmTab { get; init; } = _ => Task.FromResult(true);
    /// <summary>Asks for a value (title, label, default); null when cancelled.</summary>
    public Func<string, string, string, Task<string?>> AskText { get; init; } = (_, _, initial) => Task.FromResult<string?>(initial);
    public Action<string> Report { get; init; } = _ => { };
    /// <summary>Clears the loop region as a timeline ruler click does (one undo step; the Loop button and the white selection stay).</summary>
    public Action ClearLoop { get; init; } = () => { };
    /// <summary>The shared MIDI input (step input listens to it while it is on); null without an engine.</summary>
    public Func<TabForge.Audio.MidiInputHub?> MidiInput { get; init; } = () => null;
    /// <summary>True when MIDI input monitoring already sounds the keyboard for the track (step input then does not audition the key itself).</summary>
    public Func<TabForge.Models.TrackModel, bool> MonitorSounds { get; init; } = _ => false;
    /// <summary>The window's answer to "clips now overlap on a lane" (setting, prompt, Remember); null leaves a take that grew over a clip as it is.</summary>
    public Func<ClipOverlapPolicy?> ClipOverlap { get; init; } = () => null;
    /// <summary>Scrolls the main timeline so the given song second is at its left edge (the piano roll was scrolled by hand).</summary>
    public Action<double> ScrollTimeline { get; init; } = _ => { };
}

// Owns: the piano roll pane that replaces the score for one track: the toolbar, the info line, the surface and its scroll bars, and the wiring of
//       the surface to the window (the playhead and its follow, a ruler click as a seek, a drag or box selection as the loop region, the snap
//       magnet, audition, the velocity lane setting, the refresh after an edit, an undo or another view's edit).
// Does not own: drawing and gestures (PianoRollSurface), notes and commits (PianoRollPresenter), which view the pane shows (EditorPaneSwitch).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollView : DockPanel
{
    private readonly PianoRollHost _host;
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical };
    private AvaloniaPlaybackSession? _session;
    private SongDocument? _document;
    private bool _syncingBars;
    private bool _stale;
    private bool _centrePending;

    public PianoRollView(PianoRollHost host)
    {
        _host = host;
        Presenter = new PianoRollPresenter(() => _document, name => _host.ConfirmTab(name));
        Presenter.Committed += tab => _host.AfterEdit(tab);
        Presenter.Overlaps = () => _host.ClipOverlap();
        Surface = new PianoRollSurface(Presenter)
        {
            Snap = host.Snap,
            PlayheadBeat = PlayheadBeat,
            LoopSpan = LoopSpan,
            TrackColour = TrackColour,
            Audition = pitch => { if (Presenter.Track is { } track) _host.Audition(track, pitch); },
            Seek = Seek,
            MakeLoop = MakeLoop,
            LaneChanged = SaveLane,
            ViewChanged = SyncScrollBars,
            SelectionChanged = RefreshInfo,
            Report = host.Report,
            QuantizeRequested = Quantize,
            LoopOn = () => _document?.Selection.LoopRepeat == true,
            Sections = () => _sections,
            LaneMenuRequested = OpenLaneMenu,
            Playing = () => _session?.IsPlaying == true,
            NeedleOn = () => host.Editing().PianoRollPlaybackNeedle,
            PlayheadColour = light => PlaybackHighlightColour.Of(host.Appearance(), AvaloniaPalette.Get("PianoRollPlayheadMarker", light)),
            ClearLoop = () => _host.ClearLoop(),   // the release repaints the surface
            ValuePreview = value => { _velocity.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture); _velocity.FontStyle = global::Avalonia.Media.FontStyle.Normal; },
            UserScrolled = TellTimeline,
        };
        Presenter.Reloaded += RefreshDerived;
        Surface.ToolChanged = RefreshTools;
        var editing = host.Editing();
        Surface.Lane = Math.Max(PianoRollSurface.MinLane, editing.PianoRollVelocityLane);
        Surface.LaneShown = editing.PianoRollVelocityLaneShown;
        _horizontal.Scroll += (_, _) => { if (_syncingBars) return; Surface.ScrollTo(_horizontal.Value, Surface.ScrollY); TellTimeline(); };
        _vertical.Scroll += (_, _) => { if (!_syncingBars) Surface.ScrollTo(Surface.ScrollX, _vertical.Value); };
        var top = new StackPanel { Orientation = Orientation.Vertical, Children = { BuildToolbar(), BuildInfoLine() } };
        SetDock(top, Dock.Top);
        SetDock(_horizontal, Dock.Bottom);
        SetDock(_vertical, Dock.Right);
        Children.Add(top);
        Children.Add(_horizontal);
        Children.Add(_vertical);
        Children.Add(Surface);
        AttachMenu();
        Surface.SizeChanged += (_, _) => { if (_centrePending && Surface.Bounds.Height > 0) { _centrePending = false; Surface.CentreOnNotes(); } SyncScrollBars(); };
    }

    public PianoRollPresenter Presenter { get; }

    public PianoRollSurface Surface { get; }

    /// <summary>Shows track <paramref name="trackIndex"/> of <paramref name="document"/>; <paramref name="newVisit"/> when the pane just switched to
    /// the piano roll (the tab prompt asks again).</summary>
    public void Show(SongDocument document, int trackIndex, bool newVisit)
    {
        var otherSong = !ReferenceEquals(document, _document);
        if (otherSong)
        {
            if (_document is not null) { _document.TimelineMarked -= OnSongChanged; _document.Restored -= OnRestored; }
            _document = document;
            document.TimelineMarked += OnSongChanged;
            document.Restored += OnRestored;
        }
        var otherTrack = trackIndex != Presenter.TrackIndex;
        if (newVisit) Presenter.BeginVisit();
        Presenter.Show(trackIndex);
        _stale = false;
        BindSession(_host.Session());
        ListenForKeys(true);   // the live keys (and step input) hear the MIDI keyboard while the piano roll shows
        if (otherSong || otherTrack)
        {
            _centrePending = Surface.Bounds.Height <= 0;   // not laid out yet: centre once it has a size
            if (!_centrePending) Surface.CentreOnNotes();
        }
        RefreshTools();
        RefreshInfo();
        SyncScrollBars();
        Surface.InvalidateVisual();
        Surface.Playhead.InvalidateVisual();
    }

    // Another view's edit, or this view's own commit (its reload runs right after): the notes are read again when no gesture holds them.
    private void OnSongChanged(SongProject _)
    {
        if (Presenter.IsCommitting) return;
        _stale = true;
        Dispatch();
    }

    private void OnRestored()
    {
        _stale = true;
        Dispatch();
    }

    // The reload waits for the gesture or the tab prompt in progress; it runs on the next pointer release otherwise.
    private void Dispatch()
    {
        if (!_stale || Surface.Busy || !IsEffectivelyVisible) return;
        _stale = false;
        Presenter.Reload();
        RefreshInfo();
        SyncScrollBars();
        Surface.InvalidateVisual();
    }

    private bool _followingTimeline;

    /// <summary>The main timeline scrolled: the piano roll shows the same song second at its left edge, at its own zoom.</summary>
    public void FollowTimeline(double leftSec)
    {
        if (!IsEffectivelyVisible) return;
        _followingTimeline = true;
        try { Surface.ScrollTo(Presenter.Map.BeatsAt(Math.Max(0, leftSec)) * Surface.PixelsPerBeat, Surface.ScrollY); }
        finally { _followingTimeline = false; }
    }

    // The piano roll was scrolled by hand: the timeline follows (never while the piano roll is following it).
    private void TellTimeline()
    {
        if (!_followingTimeline) _host.ScrollTimeline(Presenter.Map.SecondsAt(Surface.ScrollX / Surface.PixelsPerBeat));
    }

    /// <summary>The transport's magnet or snap settings changed: the mirror magnet, the grid value and the grid lines follow.</summary>
    public void RefreshSnap()
    {
        RefreshTools();
        Surface.InvalidateVisual();
    }

    /// <summary>The "Playback needle" setting changed (Preferences): the live layer repaints alone, so a stopped needle appears or goes at once.</summary>
    public void RefreshNeedle() { Surface.Playhead.ResetColour(); Surface.Playhead.InvalidateVisual(); }   // also when the Playback highlight or the accent changed

    /// <summary>Reads the track again when the song changed while the pane was hidden or busy (the switch calls it when it shows the pane).</summary>
    public void RefreshIfStale() => Dispatch();

    private void BindSession(AvaloniaPlaybackSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        if (_session is not null) { _session.PlayheadFrame -= OnFrame; _session.PlayheadBarChanged -= OnFrame; }
        _session = session;
        if (session is null) return;
        session.PlayheadFrame += OnFrame;
        session.PlayheadBarChanged += OnFrame;
    }

    // Every playback frame repaints only the playhead layer; following scrolls a page when the line leaves the view.
    private void OnFrame()
    {
        if (!IsEffectivelyVisible) return;
        Surface.Playhead.InvalidateVisual();
        if (_follow && _session is { IsPlaying: true } && PlayheadBeat() is { } beat) Surface.Reveal(beat);
    }

    /// <summary>The playhead's beat on the piano roll's clock, or null with no playback.</summary>
    private double? PlayheadBeat()
    {
        if (_session is not { } session || _document is null) return null;
        var map = Presenter.Map;
        if (session.IsPlaying && !Presenter.IsTab) return map.BeatsAt(session.PerformedSec);
        var position = session.Playhead;
        foreach (var bar in map.Bars)
            if (bar.Bar == position.Bar)
            {
                var slots = Math.Max(1, MusicTime.BarSlots(_document.Project, bar.Bar));
                return bar.StartBeat + Math.Clamp(Math.Max(position.BarFraction, position.Cell / (double)slots), 0, 1) * bar.Beats;
            }
        return position.Bar <= 0 ? 0 : null;   // a song never played yet: the playhead is at the start
    }

    private UiColor? TrackColour() =>
        Presenter.Track is { } track && global::Avalonia.Media.Color.TryParse(track.ColorHex, out _) ? TimelineView.TamedTrackColour(track.ColorHex, default) : null;

    /// <summary>A ruler click: the playhead goes to <paramref name="beat"/> (the played pass of a repeat on the performed clock).</summary>
    private void Seek(double beat)
    {
        if (_session is not { } session || _document is null) return;
        var bar = Presenter.Map.BarAtBeat(beat);
        if (bar.Bar < 0 || bar.Bar >= AudioClipTimelineRules.BarCount(_document.Project)) return;
        if (!Presenter.IsTab)
        {
            session.Seek(RepeatMap.FromPerformed(SongExtent.Timing(_document.Project).Performed, Presenter.Map.SecondsAt(beat)));
            return;
        }
        var slots = Math.Max(1, MusicTime.BarSlots(_document.Project, bar.Bar));
        session.Seek(bar.Bar, (int)Math.Round((beat - bar.StartBeat) / Math.Max(1e-9, bar.Beats) * slots));
    }

    private void SaveLane()
    {
        var editing = _host.Editing();
        editing.PianoRollVelocityLane = Surface.Lane;
        editing.PianoRollVelocityLaneShown = Surface.LaneShown;
        _host.SaveSettings();
        SyncScrollBars();
    }

    private void SyncScrollBars()
    {
        _syncingBars = true;
        try
        {
            _horizontal.Maximum = Math.Max(0, Surface.ContentWidth - Surface.ViewportWidth);
            _horizontal.ViewportSize = Surface.ViewportWidth;
            _horizontal.Value = Surface.ScrollX;
            _vertical.Maximum = Math.Max(0, Surface.ContentHeight - Surface.ViewportHeight);
            _vertical.ViewportSize = Surface.ViewportHeight;
            _vertical.Value = Surface.ScrollY;
        }
        finally { _syncingBars = false; }
    }
}

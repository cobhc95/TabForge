using System;
using System.Globalization;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;

namespace TabForge.Avalonia;

// Owns: the View and tempo bar's readout following the shown tab: tempo, time signature and key of the shown song, and the score zoom
//   once FollowScore has given it the score view. Refreshed when the tab changes, after each edit of the shown song's timeline (tempo,
//   time signature and key edits all mark it), and after each zoom change. Also the left-click actions of the zoom button (its preset menu, which sets the score zoom),
//   the tempo button (its preset menu, which sets the shown song's tempo as one undo step) and the time / key button (its menu of the
//   time and key signature commands), the commands run through the score's command host; the fine-tune popups (ViewTempoPopups) of zoom,
//   speed and tempo, and the zoom and speed wheel steps.
// Does not own: the tabs (MainWindowTabs), the readout's text (StatusTextPresenter), the bar's drawing (ViewTempoBarView), the edits,
//   the zoom itself (ScorePreviewView), the commands' bodies (MainWindowBarTools), the menus' items (ViewTempoMenus).
// Tests: TabForge.Avalonia.Tests.MainWindowTempoBarTests, TabForge.Avalonia.Tests.ViewTempoBarButtonTests, TabForge.Avalonia.Tests.ViewTempoMenusTests.
/// <summary>UI thread only.</summary>
public sealed class MainWindowTempoBar : System.IDisposable
{
    private readonly MainWindowTabs _tabs;
    private readonly ViewTempoBarView _bar;
    private readonly DocumentWatch _watch = new();
    private ScorePreviewView? _score;
    private TabForge.Avalonia.Transport.AvaloniaPlaybackSessions? _playback;
    private TabForge.Avalonia.Transport.LiveTempo? _live;

    /// <summary>Stops following tab switches, the shown song's edits and the score's zoom.</summary>
    public void Dispose()
    {
        _tabs.ActiveChanged -= Refresh;
        _watch.Dispose();
        if (_score is not null) { _score.ZoomChanged -= Refresh; _score.CursorChanged -= Refresh; }
        if (_live is not null) _live.Changed -= Refresh;
        _score = null;
    }

    public MainWindowTempoBar(MainWindowTabs tabs, ViewTempoBarView bar)
    {
        _tabs = tabs;
        _bar = bar;
        _watch.Marked = _ => Refresh();
        _watch.ProjectChanged = (_, _) => Refresh();
        tabs.ActiveChanged += Refresh;
        _bar.SetPressed(ViewTempoBarPresenter.Tempo, ShowTempoMenu);
        _bar.SetFineTune(ViewTempoBarPresenter.Tempo, () => ViewTempoPopups.Tempo(() => _tabs.Active.Project.Tempo, SetTempo));
        Refresh();
    }

    /// <summary>Shows the score's zoom in the readout from now on, refreshes it at each zoom change, and wires the zoom and time / key buttons.</summary>
    public void FollowScore(ScorePreviewView score)
    {
        if (_score is not null) { _score.ZoomChanged -= Refresh; _score.CursorChanged -= Refresh; }
        _score = score;
        score.ZoomChanged += Refresh;
        score.CursorChanged += Refresh;
        var host = score.ContextMenus;
        void Run(string commandId)
        {
            if (host.Commands.Handles(commandId)) host.Commands.Run(commandId);
        }
        _bar.SetPressed(ViewTempoBarPresenter.Zoom, () => ShowMenu(ViewTempoBarPresenter.Zoom, ViewTempoMenus.Zoom(score.Zoom, score.SetZoom)));
        _bar.SetPressed(ViewTempoBarPresenter.TimeKey, () => ShowMenu(ViewTempoBarPresenter.TimeKey, ViewTempoMenus.TimeKey(Run)));
        _bar.SetFineTune(ViewTempoBarPresenter.Zoom, () => ViewTempoPopups.Zoom(() => score.Zoom * 100, percent => score.SetZoom(percent / 100), null));
        _bar.SetWheel(ViewTempoBarPresenter.Zoom, up => Run(up ? "View.ZoomIn" : "View.ZoomOut"));
        Refresh();
    }

    /// <summary>Wires the speed button to the shown song's playback session: its menu sets that session's speed and the readout shows it.</summary>
    public void FollowPlayback(TabForge.Avalonia.Transport.AvaloniaPlaybackSessions playback)
    {
        _playback = playback;
        _bar.SetPressed(ViewTempoBarPresenter.Speed, () => ShowMenu(ViewTempoBarPresenter.Speed, ViewTempoMenus.Speed(CurrentSpeed(), SetSpeed)));
        _bar.SetFineTune(ViewTempoBarPresenter.Speed, () => ViewTempoPopups.Speed(() => CurrentSpeed() * 100, percent => SetSpeed(percent / 100)));
        _bar.SetWheel(ViewTempoBarPresenter.Speed, up => SetSpeed(Math.Clamp(CurrentSpeed() * 100 + (up ? ViewTempoPopups.WheelSpeedStep : -ViewTempoPopups.WheelSpeedStep), ViewTempoPopups.MinimumSpeedPercent, ViewTempoPopups.MaximumSpeedPercent) / 100));
        Refresh();
    }

    /// <summary>Shows the playing tempo in the readout from now on and refreshes it when it changes.</summary>
    public void FollowTempo(TabForge.Avalonia.Transport.LiveTempo live)
    {
        if (_live is not null) _live.Changed -= Refresh;
        _live = live;
        live.Changed += Refresh;
        Refresh();
    }

    private double CurrentSpeed() => _playback?.Existing(_tabs.Active)?.Speed ?? 1;

    private void SetSpeed(double speed)
    {
        _playback?.For(_tabs.Active)?.SetSpeed(speed);
        Refresh();
    }

    /// <summary>Shows the shown song's readout and watches that song's timeline for edits.</summary>
    public void Refresh()
    {
        _watch.Bind(_tabs.Active);
        var song = _tabs.Active.Project;
        var (bar, cell) = _score is null ? (0, 0) : (_score.Editor.SelectedMeasure, _score.Editor.SelectedCell);
        var readout = StatusTextPresenter.Compose(new StatusTextInput(song, song.Tracks.Count > 0 ? song.Tracks[0] : null, false, -1, 0, bar, cell, 4, 0, false));
        var tempo = _live?.Playing ?? PlaybackTempo.AtCell(song, bar, cell);   // the playing note's tempo, else the cursor's
        var zoomText = string.Format(CultureInfo.InvariantCulture, "{0:0}%", (_score?.Zoom ?? 1) * 100);
        _bar.Show(zoomText, CurrentSpeed() * 100, tempo, readout.TimeSignatureText, readout.KeyText);
    }

    private void ShowTempoMenu() => ShowMenu(ViewTempoBarPresenter.Tempo, ViewTempoMenus.Tempo(_tabs.Active.Project.Tempo, SetTempo));

    /// <summary>Sets the shown song's tempo as one undo step; the readout follows through the timeline mark the edit raises.</summary>
    private void SetTempo(int bpm) => SetTempo(_tabs.Active, bpm);

    /// <summary>Sets <paramref name="document"/>'s tempo as one undo step; false when it already holds <paramref name="bpm"/> (no step, redo kept).</summary>
    internal static bool SetTempo(IEditableDocument document, int bpm) =>
        DocumentEdits.Run(document, project =>
        {
            if (project.Tempo == bpm) return false;
            project.Tempo = bpm;
            return true;
        }, label: "Set tempo").Changed;

    private void ShowMenu(int kind, ContextMenu menu) => menu.Open(_bar.Buttons[kind]);
}

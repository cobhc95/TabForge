using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: everything that keeps the shown document's SelectionModel (SongDocument.Selection) in step: the score editor (SelectionSync), the timeline's loop
//   marks and the playback transport's loop range and Loop flag (both follow the model's loop region and LoopRepeat); a released score selection setting the
//   loop; the loop commands (toggle, loop a section or the selected bars, clear the loop), Select all and the stepped Escape (white first, then the loop as
//   one undo step), and the window's subscription to the shared playback (Dispose ends it).
// Does not own: the selection and loop data and rules (Core SelectionModel, DocumentEdits.Escape/EditLoop), the score's or the timeline's pointer handling
//   (ScorePreviewEditor, TimelineInputController), the engine's wrap at the loop end (PlaybackEngine) or the loop button's drawing (TransportBarView).
// Tests: TabForge.Avalonia.Tests.SelectionLoopLinkTests, LoopModelTests.
/// <summary>
/// The loop region (made on the ruler or by a released score selection) shows on the timeline as an orange bracket and band, and a running or next loop
/// wraps within it while the Loop button is on. With no region the loop wraps the whole song. UI thread only.
/// </summary>
public sealed class SelectionLoopLink : IDisposable
{
    private readonly MainWindowTabs _tabs;
    private readonly ScorePreviewView _score;
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly SelectionSync _sync;
    private readonly ConditionalWeakTable<SongDocument, List<StaffWire>> _staffWires = new();   // a card's staff wiring per document it was attached with; a closed song drops out
    private ScorePreviewView? _pressed;
    private ArrangementView? _arrangement;
    private (LoopRegionSnapshot Loop, (int, int)? NoArea, bool Looping)? _shown;   // what the transport and the timeline were last given; a change that leaves it alone does nothing
    private (int Start, int End)? _noAreaLoop;   // the bars a loop without an area covers ("loops what is playing"); null: the whole song. UI thread only
    private Action<string> _notice = _ => { };
    private (SelectionSnapshot Sel, BarHighlightSnapshot Highlight, LoopRegionSnapshot Loop)? _pre;   // the selections as they were before a score selection began (for its one undo step)
    private SelectionSnapshot? _scoreBefore;   // the model's selection with the score notes in it, as the score last had it
    private SelectionModel? _clipsWatched;   // the shown model: selecting clips deselects the score notes (one selection at a time)
    private void OnClipsChanged(object? sender, SelectionOrigin origin)
    {
        // Only a score selection the user made is cleared and recorded; the clips' own bar highlight in the score (SelectionSync) is not one, and recording it would end redo.
        if (_clipsWatched is not { ClipCount: > 0 } model || !model.HasScoreSelection || !ActiveScore.Editor.HasSelection) return;
        var before = _scoreBefore;
        ActiveScore.Editor.ClearSelection();
        DocumentEdits.RecordSwap(Active, "Select clips", null, null, before);
    }
    private BarHighlight? _mirrored;   // the shown document's cell highlight whose scattered cells the main score mirrors

    public SelectionLoopLink(MainWindowTabs tabs, ScorePreviewView score, AvaloniaPlaybackSessions? playback)
    {
        _tabs = tabs;
        _score = score;
        _playback = playback;
        _sync = new SelectionSync(Model, score.Editor, _ => { if (!_tabs.IsTearCopy) SyncAreaVisuals(); });   // a torn-off copy mid-drag shares the song: only the owning window drives the loop
        Wire(score, _sync, () => Active);
        WatchHighlight();
    }

    /// <summary>
    /// Gives a Band card's staff the main score's selection code path: it pushes into and follows the <paramref name="document"/>'s one model like the score does,
    /// and while it was pressed last it is the score the clipboard, Select all and Escape act on.
    /// </summary>
    public void AttachStaff(ScorePreviewView staff, SongDocument document)
    {
        var sync = new SelectionSync(document.Selection, staff.Editor, _ => { if (ReferenceEquals(document, Active) && !_tabs.IsTearCopy) SyncAreaVisuals(); });
        _staffWires.GetOrCreateValue(document).Add(new StaffWire(staff, sync, Wire(staff, sync, () => document)));
    }

    /// <summary>Ends a card staff's selection wiring (its card was dropped or closed): the staff no longer follows the model, and the link lets go of it.</summary>
    public void DetachStaff(ScorePreviewView staff)
    {
        foreach (var (_, wires) in _staffWires)
        {
            var index = wires.FindIndex(wire => ReferenceEquals(wire.View, staff));
            if (index < 0) continue;
            wires[index].Dispose();
            wires.RemoveAt(index);
            return;
        }
    }

    /// <summary>How many card staffs are wired now (the wiring a closed card gave back is not counted).</summary>
    internal int StaffWireCount => _staffWires.Sum(entry => entry.Value.Count);

    // One card staff's wiring: its sync and the handlers on the view, undone together.
    private sealed record StaffWire(ScorePreviewView View, SelectionSync Sync, Action Unwire) : IDisposable
    {
        public void Dispose()
        {
            Unwire();
            Sync.Dispose();
        }
    }

    // The selection wiring of one score view (the main score or a card's staff) to its sync, reading the song the view shows from document; the returned action undoes it.
    private Action Wire(ScorePreviewView view, SelectionSync sync, Func<SongDocument> document)
    {
        EventHandler changed = (_, _) => { if (_pre is null || !sync.Model.HasScoreSelection) _pre = (sync.Model.Snapshot(), document().Highlight.Snapshot(), document().Selection.LoopSnapshot()); sync.PushFromEditor(); if (view.Editor.HasSelection) _scoreBefore = sync.Model.Snapshot(); };
        Action focused = () => { _pressed = view; sync.Model.SetRows(Array.Empty<Guid>()); sync.Model.SetFocus(FocusTarget.ScoreSelection); };   // a press on the score or keyboard focus in it: the edit keys act on its selection
        Action cursor = () => sync.Reconcile(BarRangeEditor.MaxMeasures(document().Project), document().Project.Tracks.Count);
        Action released = () => LoopScoreSelection(document());   // re-enabled 25 Oct: sets the loop region only, never turns Loop on
        view.Editor.SelectionChanged += changed;
        view.Focused += focused;
        view.CursorChanged += cursor;
        Action clicked = () => ClearScoreClick(view, document());   // a plain click: the white and a note-made loop go, the cursor stays
        view.SelectionReleased += released;
        view.PlainClicked += clicked;
        return () =>
        {
            view.Editor.SelectionChanged -= changed;
            view.Focused -= focused;
            view.CursorChanged -= cursor;
            view.SelectionReleased -= released;
            view.PlainClicked -= clicked;
            if (ReferenceEquals(_pressed, view)) _pressed = null;
        };
    }

    /// <summary>The score view the edit commands act on: the one pressed last while it shows (a Band card's staff or the main score), else the main score.</summary>
    public ScorePreviewView ActiveScore => _pressed is { IsEffectivelyVisible: true } pressed ? pressed : _score;

    /// <summary>Ends the subscription to the shared playback. The window calls it on close: a closed window has no tabs left to read, so it must not
    /// keep reacting to another window's play or pause.</summary>
    public void Dispose()
    {
        _sync.Dispose();
        if (_clipsWatched is { } clipsModel) clipsModel.Changed -= OnClipsChanged;
        if (_mirrored is { } watched) watched.Changed -= OnHighlightChanged;
        foreach (var (_, wires) in _staffWires) foreach (var wire in wires) wire.Dispose();
    }

    /// <summary>The shown document's selection and loop region, which both views read and write.</summary>
    public SelectionModel Model => Active.Selection;

    /// <summary>The audio settings the loop settings live in ("forget the area when loop is turned off", "loop button loops what is playing"); null uses the defaults.</summary>
    public Func<TabForge.Services.AudioSettings?> Audio { get; set; } = () => null;

    private TabForge.Services.AudioSettings AudioOrDefaults => Audio() ?? new TabForge.Services.AudioSettings();

    /// <summary>The loop settings' area line: the region's bars, or that none is picked.</summary>
    public string AreaText => LoopSettingsRules.AreaText(Model.HasLoop, LoopOn, LoopedRange.Start, LoopedRange.End);

    /// <summary>The shortest loop a recording splits on (below it the take is one pass).</summary>
    private const double LoopMinimumSec = 0.05;

    /// <summary>
    /// The loop range in song seconds while the shown song loops, or null when the loop is off: a mid-bar start cell is honoured and the end is
    /// the end of the last looped bar, so a recording can be cut into one take per pass.
    /// </summary>
    public (double Start, double End)? LoopSeconds
    {
        get
        {
            if (!LoopOn) return null;
            var project = Active.Project;
            var (startBar, endBar) = LoopedRange;
            var startCell = Model.HasLoop ? Math.Max(0, Model.LoopStartCell) : 0;
            var slots = Math.Max(1, MusicTime.BarSlots(project, startBar));
            var start = AudioClipTimelineRules.SecondsAtBar(project, startBar, startCell / (double)slots);
            var endCell = Model.HasLoop ? Model.LoopEndCell : -1;   // a note-made loop ends after its last cell, as playback wraps (PlaybackOrder.LoopBounds)
            var endSlots = Math.Max(1, MusicTime.BarSlots(project, endBar));
            var end = endCell < 0 ? AudioClipTimelineRules.SecondsAtBar(project, endBar + 1, 0)
                : AudioClipTimelineRules.SecondsAtBar(project, endBar, Math.Min(endSlots, endCell + 1) / (double)endSlots);
            return (start, Math.Max(start + LoopMinimumSec, end));
        }
    }

    // The bars the loop wraps within: the region, the no-region loop's bars, or the whole song.
    private (int Start, int End) LoopedRange =>
        Model.LoopBars ?? _noAreaLoop ?? (0, Math.Max(0, BarRangeEditor.MaxMeasures(Active.Project) - 1));

    private SongDocument Active => _tabs.Active;
    private AvaloniaPlaybackSession? Session => _playback?.For(Active);

    /// <summary>Shows the loop region in <paramref name="arrangement"/> from now on.</summary>
    public void AttachArrangement(ArrangementView arrangement)
    {
        if (_arrangement is { } old)
        {
            if (ReferenceEquals(old, arrangement)) return;   // attached already
            old.Input.CellClicked -= GoToCell;
        }
        _arrangement = arrangement;
        arrangement.Input.CellClicked += GoToCell;
        _shown = null;
        SyncAreaVisuals();
    }

    /// <summary>A plain click on a lane cell or a clip: the score shows that track and puts its cursor on the bar's first cell, scrolled into view. The selection and the loop stay as they are.</summary>
    private void GoToCell(int row, int bar)
    {
        if (row >= Active.Project.Tracks.Count || bar >= _score.Editor.BarCount) return;
        Active.TrackIndex = row;
        _score.ShowTrack(row);
        UpdateMirror(true);
        _score.Editor.SetPosition(bar, 0, _score.Editor.SelectedString);
        _score.RevealBar(bar, _score.Follow.MarginPercent);
        _score.InvalidateVisual();
    }

    /// <summary>
    /// The shown tab changed: the views follow that document's own selection and loop region (they come back as they were left).
    /// <paramref name="showDocument"/> shows the document in the score and the panes while the score's selection echoes are ignored.
    /// </summary>
    public void TabChanged(Action showDocument)
    {
        _sync.Bind(Model, showDocument);
        _noAreaLoop = null;   // a no-region loop belongs to the song it was started on
        WatchHighlight();
        _shown = null;
        SyncAreaVisuals();
    }

    // Follows the shown document's cell highlight (re-pointed on a tab switch) so scattered white cells show in the score.
    private void WatchHighlight()
    {
        if (!ReferenceEquals(_clipsWatched, Active.Selection))
        {
            if (_clipsWatched is { } oldModel) oldModel.Changed -= OnClipsChanged;
            _clipsWatched = Active.Selection;
            _clipsWatched.Changed += OnClipsChanged;
        }
        if (ReferenceEquals(_mirrored, Active.Highlight)) { UpdateMirror(false); return; }
        if (_mirrored is { } old) old.Changed -= OnHighlightChanged;
        _mirrored = Active.Highlight;
        _mirrored.Changed += OnHighlightChanged;
        UpdateMirror(false);
    }

    // On a highlight change only: scattered cells light the shown track's notes as display only (the editing selection is cleared, the loop untouched);
    // a single block also lights its bars but leaves the editing selection alone. Esc and a cleared highlight leave no ranges.
    private void OnHighlightChanged() => UpdateMirror(true);

    /// <summary>Re-lights the mirror for the shown track (after the score shows another track); the editing selection is left alone.</summary>
    public void RefreshMirror() => UpdateMirror(false);

    // userChange is false on a tab switch / restore / track switch: the editing selection comes back as it was left and is never cleared there.
    private void UpdateMirror(bool userChange)
    {
        // 25 Oct: one selection at a time. A new timeline white selection deselects the score notes (the loop and its tint stay); no mirror.
        if (userChange && Active.Highlight.Has && ActiveScore.Editor.HasSelection)
        {
            Active.Highlight.PendingSelection ??= _scoreBefore;
            ActiveScore.Editor.ClearSelection();
        }
    }

    // ---- commands ----

    /// <summary>True while the shown song's Loop button is on (the model's LoopRepeat; the transport follows it).</summary>
    public bool LoopOn => Model.LoopRepeat;

    /// <summary>The Loop button and Transport.Loop: repeat on or off, keeping the region (with none the whole song, or the playing section, bar or song when
    /// "Loop button loops what is playing" is on), with the status line saying which bars loop.</summary>
    public void ToggleLoop()
    {
        SetLoopActive(!LoopOn);
        _notice(LoopSettingsRules.StatusText(LoopOn, LoopedRange.Start, LoopedRange.End));
    }

    /// <summary>Loops the bars of the section at <paramref name="markerIndex"/> as one undo step (turns the loop off when that section already loops).</summary>
    public void LoopSection(int markerIndex)
    {
        var project = Active.Project;
        if (markerIndex < 0 || markerIndex >= project.Markers.Count || !SectionMenuEdits.TryBounds(project, project.Markers[markerIndex], out var start, out var end)) return;
        if (Model.LoopBars == (start, end - 1) && LoopOn) { SetLoopActive(false); return; }
        LoopBars(start, end - 1, "Loop section");
    }

    /// <summary>Loops bars <paramref name="start"/> to <paramref name="end"/> with the Loop button on, as one undo step.</summary>
    public void LoopBars(int start, int end, string label = "Loop bars") =>
        DocumentEdits.EditLoop(Active, model => model.SetLoop(start, end) | model.SetLoopRepeat(true), label);

    /// <summary>
    /// A piano-roll loop: a ruler drag (<paramref name="fromSelection"/> false) or a released box selection (true: the white selection goes too, as
    /// after a score drag) sets the loop region to bars <paramref name="start"/> to <paramref name="end"/> as one undo step. The Loop button stays.
    /// The cells are sixteenth slots (as playback reads them), so a loop can start and end inside a bar (<paramref name="endCell"/> -1: the bar's end).
    /// </summary>
    public void LoopPianoRollSpan(int start, int end, bool fromSelection, int startCell = 0, int endCell = -1)
    {
        if (_tabs.IsTearCopy) return;
        var document = Active;
        var (selection, highlight, loop) = (Model.Snapshot(), document.Highlight.Snapshot(), Model.LoopSnapshot());
        Model.SetLoop(start, end, startCell, endCell);
        if (fromSelection)
        {
            document.Highlight.Clear();
            Model.Clear(SelectionOrigin.Editor);
            Model.SetClips(Array.Empty<TimelineClipSelection>());
            if (ActiveScore.Editor.HasSelection) ActiveScore.Editor.ClearSelection();
            DocumentEdits.RecordSwap(document, "Select notes", highlight, loop, selection);
        }
        else DocumentEdits.RecordLoop(document, loop, "Loop");
        _shown = null;
        SyncAreaVisuals();
    }

    /// <summary>The ruler menu's Loop selected bars: the white selection's bar span (score notes or selected clips); nothing without one.</summary>
    public void LoopSelectedBars()
    {
        if (SelectedSpan() is { } span) LoopBars(span.Start, span.End, "Loop selected bars");
    }

    /// <summary>The white selection's bar span, ignoring the loop region; null when nothing is selected.</summary>
    public BarSpan? SelectedSpan() =>
        Model.BarRange is { } score ? new BarSpan(score.Start, score.End, null, false) : SelectionBars.ClipSpan(Model, Active.Project);

    /// <summary>Drops the loop region and turns Loop off as one undo step; playback carries straight on.</summary>
    public void ClearLoopArea()
    {
        var fromScore = Model.LoopFromScore;
        if (fromScore && ActiveScore.Editor.HasSelection) ActiveScore.Editor.ClearSelection();   // a score loop's selected notes go with it
        if (DocumentEdits.EditLoop(Active, model => model.ClearLoop(), "Clear loop")) _notice(LoopAreaClearedText);
    }

    private const string LoopAreaClearedText = "Loop cleared";

    /// <summary>
    /// Escape, Guitar Pro style: the white selection first (clips, the score selection, track rows), then once nothing is selected the loop region with
    /// Loop off (one undo step; playback carries on). False when nothing was left to clear.
    /// </summary>
    public bool ClearSelection()
    {
        var result = DocumentEdits.Escape(Active);
        if (result == EscapeResult.Loop) _notice(LoopAreaClearedText);
        if (result != EscapeResult.Nothing) { _arrangement?.Timeline.InvalidateVisual(); return true; }
        if (!ActiveScore.Editor.HasSelection) return false;
        ActiveScore.Editor.ClearSelection();
        return true;
    }

    // A score selection was released: its beats become the loop with Loop on, unless a loop is playing (it stays put).
    private void LoopScoreSelection(SongDocument document)
    {
        if (!ReferenceEquals(document, Active) || _tabs.IsTearCopy) return;
        document.Selection.LoopScoreSelection(Session?.IsPlaying == true);
        // One selection at a time: the score drag clears the timeline white (grey cells; the clips went at the press).
        if (_pre is { } pre)
        {
            document.Highlight.Clear();
            DocumentEdits.RecordSwap(document, "Select notes", pre.Highlight, pre.Loop, pre.Sel);
        }
        _pre = null;
        _shown = null;   // the release is a settled moment: push the loop to the timeline and redraw the ruler and lanes now, not at the next wrap
        SyncAreaVisuals();
    }

    // A plain score click clears the white (score notes and clips) and a loop a score selection made (one undo step); the cursor stays where the click put it.
    private void ClearScoreClick(ScorePreviewView view, SongDocument document)
    {
        if (_tabs.IsTearCopy) return;
        document.Selection.Clear(SelectionOrigin.Editor);
        if (view.Editor.HasSelection) view.Editor.ClearSelection();
        // reverted 25 Oct: a plain score click clears the score selection only; the loop stays
    }

    /// <summary>Edit.SelectAll: every bar of the shown track, in both views (or every note of a focused piano roll, asked first).</summary>
    public void SelectAll()
    {
        if (SelectAllFirst?.Invoke() == true) return;
        ActiveScore.SelectAll();
    }

    /// <summary>Asked before Select all selects the score (the piano roll with focus selects its notes); true when it did.</summary>
    public Func<bool>? SelectAllFirst { get; set; }

    /// <summary>Adds the loop and selection commands to <paramref name="commands"/> (the section row reads the section clicked in <paramref name="menus"/>) and the Escape key to <paramref name="window"/>.</summary>
    /// <param name="notice">Shows the status line after the loop button and Clear loop.</param>
    public void Register(MainWindowCommands commands, Window window, ContextMenuHost menus, Action<string> notice)
    {
        _notice = notice;
        commands.Add("Transport.Loop", ToggleLoop);
        commands.Add("Transport.ClearLoopArea", ClearLoopArea);
        commands.Add("Transport.LoopSelectedBars", LoopSelectedBars);
        commands.Add("Selection.Clear", () => ClearSelection());   // the selection menu's Clear selection row
        commands.Add(ContextMenuRows.LoopSectionId, () => LoopSection(menus.ClickedSection));
        commands.Add("Edit.SelectAll", SelectAll);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled && e.KeyModifiers == KeyModifiers.None && ClearSelection()) e.Handled = true;
        };
    }

    /// <summary>Turns the Loop button on or off (the region stays); with no region and the setting to loop what is playing it loops the playing section or bar.</summary>
    public void SetLoopActive(bool loop)
    {
        // Without a region the loop covers the whole song, or (opt-in) the section, bar or song being played.
        var audio = AudioOrDefaults;
        var session = Session;
        _noAreaLoop = loop && !Model.HasLoop && audio.LoopButtonLoopsSection
            ? LoopSettingsRules.DefaultArea(Active.Project, audio.LoopDefaultScope, session is { IsPlaying: true } && session.Transport.PlayheadBar >= 0 ? session.Transport.PlayheadBar : _score.Editor.SelectedMeasure)
            : null;
        if (!Model.SetLoopRepeat(loop)) SyncAreaVisuals();   // a change reaches SyncAreaVisuals through the model's Changed
        if (!loop && audio.LoopClearAreaOnDisable && Model.HasLoop) DocumentEdits.EditLoop(Active, model => model.ClearLoop(), "Clear loop");
    }

    /// <summary>Hands the loop region (or, with none, the whole song) and the Loop flag to the transport and draws the region on the timeline; does nothing when they did not change.</summary>
    public void SyncAreaVisuals()
    {
        var model = Model;
        var looping = model.LoopRepeat;
        var noAreaLoop = looping && !model.HasLoop ? _noAreaLoop : null;
        var next = (model.LoopSnapshot(), noAreaLoop, looping);
        if (_shown == next) return;
        var before = _shown?.Item1;
        _shown = next;
        // A ruler/menu loop replaces a score loop entirely: the score loop's selected notes go (the timeline white is independent).
        if (model.HasLoop && !model.LoopFromScore && before is { } old && old.Has && old.FromScore && ActiveScore.Editor.HasSelection) ActiveScore.Editor.ClearSelection();
        // One push of the whole loop to the transport and one to the timeline: both only mirror the model, nothing writes back.
        Session?.ApplyLoop(model.HasLoop ? new PlaybackLoop(looping, model.LoopStartBar, model.LoopEndBar, model.LoopStartCell, model.LoopEndCell)
            : noAreaLoop is { } bars ? new PlaybackLoop(looping, bars.Item1, bars.Item2, 0, -1) : PlaybackLoop.Off with { On = looping });
        if (_arrangement?.Timeline is not { } timeline) return;
        timeline.LoopMarks = model.HasLoop ? (model.LoopStartBar, model.LoopEndBar, looping) : noAreaLoop is { } shown ? (shown.Item1, shown.Item2, looping) : null;
        timeline.InvalidateVisual();
    }
}

using System.Windows;
using System.Windows.Controls;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>What the timeline menus need from their window.</summary>
internal interface ITimelineMenuHost
{
    /// <summary>The song the window shows now.</summary>
    DocumentSession Document { get; }
    AppSettings Settings { get; }
    bool LoopOn { get; }
    /// <summary>The raw index of the selected track row (-1 when none).</summary>
    int SelectedTrackRow { get; }
    TrackModel? SelectedTrack { get; }
    /// <summary>The bar the keyboard menu opens on when no bars are selected: the playing bar while playing, else the cursor bar.</summary>
    int KeyboardMenuBar { get; }
    string MenuKey(string id);
    ContextMenu NewMenu(string name, IEnumerable<MenuSpec> specs, Action<TimelineCommand> run);
    void OpenMenu(ContextMenu menu, Point? anchor, bool fromKeyboard);
    Point? BarAnchor(int bar);
    void OpenSettings(string category, string row);
    void SetLoopActive(bool loop);
    void ApplyLoopRange(int start, int end, SelectionScope scope);
    void SyncAreaVisuals();
    void SetStatus(string text);
    void BeginAreaMove(int start, int end);
    void ToggleTrackLines();
    void ResetTrackListHeight();
    /// <summary>After the menu is on screen: selects the track and places the caret on the bar (-1: leave as is), never seeking playback.</summary>
    void PlaceCaretAfterOpen(int trackIndex, int bar);
    void Refresh(EditViews views);
    void AddSectionAt(int bar);
    void RenameSection(MarkerModel marker);
    void GoToSection(MarkerModel marker);
    /// <summary>The audio files the user picks for a lane, or null when cancelled.</summary>
    string[]? PickAudioFiles();
}

// Owns: building the timeline's right-click menus (selected bars, a bar, a section, a clip or an empty lane) from their MenuSpec trees and running the chosen command on the document.
// Does not own: the menu contents (TimelineMenus), the WPF menu (the host), the commands' work (SectionEditFlow, ClipEditController, ArrangementController) or the section lane's own "Add section" item.
// Needs from its host: ITimelineMenuHost; every command takes the host's Document.
// Tests: TestTimelineContextMenus, TestSelectionScope, TestSelectionClipboardMatrix.
internal sealed class TimelineMenuController
{
    private readonly ITimelineMenuHost _host;
    private readonly SelectionModel _selection;
    private readonly SelectionLoopController _selLoop;
    private readonly SectionEditFlow _sections;
    private readonly ArrangementController _arrangement;
    private readonly ClipEditController _clips;

    public TimelineMenuController(ITimelineMenuHost host, SelectionModel selection, SelectionLoopController selLoop,
        SectionEditFlow sections, ArrangementController arrangement, ClipEditController clips)
    {
        _host = host; _selection = selection; _selLoop = selLoop; _sections = sections; _arrangement = arrangement; _clips = clips;
    }

    private DocumentSession Doc => _host.Document;
    private SongProject Project => Doc.Project;
    private int MaxMeasures => BarRangeEditor.MaxMeasures(Project);
    private List<(int Start, int End)> SkipRanges => Doc.SkipRanges;

    /// <summary>The selection menu (Copy / Cut / Paste / Delete on top, the rest in submenus).</summary>
    public ContextMenu BuildSelectionMenu()
    {
        var (s, e) = (_selLoop.StartBar, _selLoop.EndBar);
        var label = s == e ? $"bar {s + 1}" : $"bars {s + 1}-{e + 1}";
        var skipped = SkipRanges.Any(r => r.Start == s && r.End == e);
        var state = new SelectionMenuState(s == e ? $"Bar {s + 1} selected" : $"Bars {s + 1}-{e + 1} selected",
            TimelineClips.CanPasteOnTimeline(ClipboardService.Shared.TryGetClip(out _)), _host.LoopOn, skipped, SkipRanges.Count > 0,
            EmptyBars.InRange(Project, s, e).Count);
        var doc = Doc;
        var skip = doc.SkipRanges;
        return _host.NewMenu("Arrangement timeline selection options", TimelineMenus.Selection(state, _host.MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.TimelineSettings: _host.OpenSettings(SettingsCatalog.Timeline, TimelineMenus.TimelineSettingsRow); break;
                case TimelineCommand.CopySelection: _sections.CopyArea(doc, s, e, _selection.ScopeTrack); break;
                case TimelineCommand.CutSelection: _sections.CutArea(doc, s, e, _selection.ScopeTrack); break;
                case TimelineCommand.PasteSelection: _sections.PasteAreaAt(doc, s); break;
                case TimelineCommand.DeleteSelection: _sections.Range.Delete(doc, s, e, _selection.Scope == SelectionScope.AllTracks); break;
                case TimelineCommand.DeleteEmptyBars: _sections.DeleteEmptyInRange(doc, s, e); break;
                case TimelineCommand.LoopSelection: _host.SetLoopActive(!_host.LoopOn); break;
                case TimelineCommand.MoveSelection: _host.BeginAreaMove(s, e); break;
                case TimelineCommand.SkipSelection:
                    if (skipped) skip.RemoveAll(r => r.Start == s && r.End == e);
                    else skip.Add((s, e));
                    _host.SyncAreaVisuals();
                    _host.SetStatus(skipped ? $"Playing {label} again" : $"Skipping {label} during playback");
                    break;
                case TimelineCommand.PlaySkippedAgain: skip.Clear(); _host.SyncAreaVisuals(); break;
                case TimelineCommand.ClearSelection: _selection.Clear(SelectionOrigin.Command); break;
            }
        });
    }

    /// <summary>
    /// Shift+F10 / the Menu key on the timeline: the selection menu when bars are selected, otherwise the bar menu for the playhead's bar
    /// (while playing) or the current bar of the selected track, at that bar's top-left with the first item focused.
    /// </summary>
    public void ShowFromKeyboard()
    {
        if (MaxMeasures == 0) return;
        var bar = _selLoop.HasArea ? _selLoop.StartBar : _host.KeyboardMenuBar;
        ShowBarMenu(bar, Math.Max(0, _host.SelectedTrackRow), fromKeyboard: true);
    }

    public void ShowBarMenu(int bar, int trackIndex, bool fromKeyboard = false)
    {
        using var slowTrace = SlowTrace.Measure("timeline menu build+open", 0);
        var tracks = Project.Tracks;
        var hasTrack = trackIndex >= 0 && trackIndex < tracks.Count;
        bar = MaxMeasures > 0 ? Math.Clamp(bar, 0, MaxMeasures - 1) : -1;
        // The menu opens first; the track switch and caret placement (score relayout) follow once it is on screen.
        // Placing the caret never seeks or rebuilds the engine (notably for the section-lock menu item).
        _host.PlaceCaretAfterOpen(hasTrack ? trackIndex : -1, bar);

        // Inside the selected bars: the selection menu only. Outside it: the single-bar menu, and the selection stays.
        Point? anchor = fromKeyboard && bar >= 0 ? _host.BarAnchor(bar) : null;
        if (_selLoop.Contains(bar)) { _host.OpenMenu(BuildSelectionMenu(), anchor, fromKeyboard); return; }

        var doc = Doc;
        var selectedTrack = hasTrack ? tracks[trackIndex] : _host.SelectedTrack;
        var hasBar = bar >= 0;
        var section = hasBar ? _arrangement.SectionAt(Project, bar) : null;
        var barsClip = ClipboardService.Shared.TryGetClip(out _);
        var canPasteBars = TimelineClips.CanPasteOnTimeline(barsClip);
        var state = new BarMenuState(hasBar, selectedTrack is not null, tracks.Count,
            selectedTrack is not null && selectedTrack.Measures.Count > 1, MaxMeasures > 1,
            section is not null, section?.LockPosition ?? false, canPasteBars, canPasteBars && barsClip!.Tracks.Count > 1, _host.Settings.Timeline.ShowTrackLines);
        var menu = _host.NewMenu("Arrangement timeline options", TimelineMenus.Bar(state, _host.MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.TimelineSettings: _host.OpenSettings(SettingsCatalog.Timeline, TimelineMenus.TimelineSettingsRow); break;
                case TimelineCommand.ToggleTrackLines: _host.ToggleTrackLines(); break;
                case TimelineCommand.ResetTrackListHeight: _host.ResetTrackListHeight(); break;
                case TimelineCommand.CopyBar: _sections.CopyBar(doc, bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.CopyBarAllTracks: _sections.CopyBar(doc, bar, selectedTrack, allTracks: true); break;
                case TimelineCommand.CopySection: _sections.CopySectionAt(doc, bar); break;
                case TimelineCommand.PasteBar: _sections.PasteBar(doc, bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.PasteBarAllTracks: _sections.PasteBar(doc, bar, selectedTrack, allTracks: true); break;
                case TimelineCommand.PasteSectionHere: _sections.PasteSectionAt(doc, bar); break;
                case TimelineCommand.InsertBarBefore: _sections.InsertBar(doc, bar); break;
                case TimelineCommand.InsertBarAfter: _sections.InsertBar(doc, bar + 1); break;
                case TimelineCommand.DeleteBar or TimelineCommand.DeleteBarAllTracks: _sections.Range.Delete(doc, bar, bar); break;   // the same prompt as a selection
                case TimelineCommand.ToggleSectionLockAtBar when section is not null: ToggleLock(doc, section); break;
            }
        });
        _host.OpenMenu(menu, anchor, fromKeyboard);
    }

    /// <summary>Right-click on a section: its menu (add, copy, cut, paste, duplicate, delete, loop, rename, go to, lock).</summary>
    public void ShowSectionMenu(int markerIndex, int? clickedBar = null)
    {
        var markers = Project.Markers.OrderBy(marker => marker.MeasureIndex).ToList();
        if (markerIndex < 0 || markerIndex >= markers.Count) return;
        var doc = Doc;
        var marker = markers[markerIndex];
        var sectionLastBar = Math.Max(marker.MeasureIndex, SectionLayout.End(markers, markerIndex, MaxMeasures) - 1);
        var sectionLooped = _host.LoopOn && _selLoop.StartBar == marker.MeasureIndex && _selLoop.EndBar == sectionLastBar;
        int? addAt = clickedBar is int atBar && atBar != marker.MeasureIndex ? atBar : null;
        var state = new SectionMenuState(addAt, TimelineClips.CanPasteOnTimeline(ClipboardService.Shared.TryGetClip(out _)),
            sectionLooped, marker.LockPosition);
        var menu = _host.NewMenu("Section options", TimelineMenus.Section(state, _host.MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.SectionSettings: _host.OpenSettings(SettingsCatalog.Timeline, TimelineMenus.SectionSettingsRow); break;
                case TimelineCommand.AddSectionHere when addAt is int at: _host.AddSectionAt(at); break;
                case TimelineCommand.CopySectionMenu: _sections.CopySection(doc, marker); break;
                case TimelineCommand.CutSection: _sections.CutSection(doc, marker); break;
                case TimelineCommand.PasteSectionAfter: _sections.PasteSectionAfter(doc, marker); break;
                case TimelineCommand.DuplicateSection: _sections.DuplicateSection(doc, marker); break;
                case TimelineCommand.DeleteSection: _sections.DeleteSection(doc, marker, confirm: true); break;
                case TimelineCommand.LoopSection:
                    if (sectionLooped) { _host.SetLoopActive(false); break; } // ticked: clicking again turns the loop off
                    _host.SetLoopActive(true);
                    _host.ApplyLoopRange(marker.MeasureIndex, sectionLastBar, SelectionScope.AllTracks);
                    break;
                case TimelineCommand.RenameSection: _host.RenameSection(marker); break;
                case TimelineCommand.GoToSection: _host.GoToSection(marker); break;
                case TimelineCommand.ToggleSectionLock: ToggleLock(doc, marker); break;
            }
        });
        _host.OpenMenu(menu, null, fromKeyboard: false);
    }

    private void ToggleLock(DocumentSession doc, MarkerModel section)
    {
        DocumentEdits.Run(doc, _ => { section.LockPosition = !section.LockPosition; return true; });
        _host.Refresh(EditViews.Arrangement | EditViews.Markers);
    }

    /// <summary>Right-click on a clip or an empty lane: copy, cut, paste, duplicate, split, glue, fades, mute, properties, write as notation, add audio file.</summary>
    public void ShowClipMenu(int trackIndex, AudioClip? clip, double sec)
    {
        if (trackIndex < 0 || trackIndex >= Project.Tracks.Count) return;
        var doc = Doc;
        var track = Project.Tracks[trackIndex];
        var lane = _clips.LaneCursor is { } cursor && ReferenceEquals(cursor.Track, track) ? cursor.Lane : clip?.Lane ?? 0;
        var state = new ClipMenuState(clip is not null, clip?.IsMidi ?? false, ClipClipboard.HasClip, clip?.Muted ?? false);
        var menu = _host.NewMenu(clip is null ? "Empty lane options" : "Clip options", TimelineMenus.Clip(state, _host.MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.ClipCopy when clip is not null: ClipClipboard.Copy(clip); break;
                case TimelineCommand.ClipCut when clip is not null:
                    ClipClipboard.Copy(clip);
                    _clips.Remove(doc, track, clip, null, clearSelection: false);
                    break;
                case TimelineCommand.ClipPaste: _clips.Paste(doc, track, lane, sec); break;
                case TimelineCommand.ClipDuplicate when clip is not null: _clips.Duplicate(doc, track, clip); break;
                case TimelineCommand.ClipSplit when clip is not null: _clips.SplitAt(doc, track, clip, sec); break;
                case TimelineCommand.ClipGlue when clip is not null: _clips.Glue(doc, track, clip); break;
                case TimelineCommand.ClipFadeReset when clip is not null: _clips.ResetFades(doc, clip); break;
                case TimelineCommand.ClipDelete when clip is not null: _clips.Remove(doc, track, clip, null, clearSelection: true); break;
                case TimelineCommand.ClipMute when clip is not null: _clips.Edit(doc, () => clip.Muted = !clip.Muted); break;
                case TimelineCommand.ClipProperties when clip is not null: _clips.EditProperties(doc, clip); break;
                case TimelineCommand.ClipWriteNotation when clip is not null: _clips.WriteNotation(doc, track, clip); break;
                case TimelineCommand.ClipAddAudioFile:
                    if (_host.PickAudioFiles() is { } files) _clips.AddAudioFiles(doc, trackIndex, sec, files);
                    break;
            }
        });
        _host.OpenMenu(menu, null, fromKeyboard: false);
    }
}

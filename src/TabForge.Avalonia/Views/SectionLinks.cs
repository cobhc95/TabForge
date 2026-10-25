using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: sections as links: jumping to a section (cursor to its first bar and cell, the score and the timeline scrolled to it, playback seeked to it,
//   Ctrl also selecting its bars) from the Sections panel, a section strip on the arrangement timeline and a section name in the score; and keeping
//   the Sections panel on the shown song with the current section (the one holding the playhead while playing, else the cursor) highlighted, and
//   flashing the jumped-to section in the strip and the panel.
// Does not own: the panel's drawing (SectionsPanelView), the strip gestures (TimelineInputController), the heading hit test (Core ScoreSectionLinks),
//   the Follow margin setting (the score view holds it).
// Tests: TabForge.Avalonia.Tests.SectionLinkTests.
/// <summary>UI thread only.</summary>
public sealed class SectionLinks : IDisposable
{
    private readonly MainWindowTabs _songs;
    private readonly ScorePreviewView _score;
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly Func<ArrangementView?> _arrangement;
    private readonly List<SectionsPanelView> _panels = new();
    private readonly DocumentWatch _watch = new();
    private readonly List<TimelineInputController> _followed = new();
    private AvaloniaPlaybackSession? _watchedSession;
    private string _shownSignature = "";

    public SectionLinks(MainWindowTabs songs, ScorePreviewView score, AvaloniaPlaybackSessions? playback, Func<ArrangementView?> arrangement)
    {
        _songs = songs;
        _score = score;
        _playback = playback;
        _arrangement = arrangement;
        score.SectionHeadingClicked += Jump;
        score.CursorChanged += Refresh;
        _watch.Marked = _ => Refresh();
        _watch.ProjectChanged = (_, _) => Refresh();
        songs.ActiveChanged += Rewatch;
    }

    /// <summary>Stops following: the score, the tab changes, the document and the playhead no longer reach this object.</summary>
    public void Dispose()
    {
        _score.SectionHeadingClicked -= Jump;
        foreach (var input in _followed) input.SectionClicked -= OnSectionClicked;
        _followed.Clear();
        _score.CursorChanged -= Refresh;
        _songs.ActiveChanged -= Rewatch;
        if (_watchedSession is not null) _watchedSession.PlayheadBarChanged -= Refresh;
        _watchedSession = null;
        _watch.Dispose();
    }

    /// <summary>Makes a Sections panel that follows the shown song and jumps on a click; it stops following when it leaves the window.</summary>
    public SectionsPanelView NewPanel()
    {
        var panel = new SectionsPanelView(Sorted());
        panel.JumpRequested += Jump;
        _panels.Add(panel);
        panel.AttachedToVisualTree += (_, _) => { if (!_panels.Contains(panel)) _panels.Add(panel); Rewatch(); };
        panel.DetachedFromVisualTree += (_, _) => { _panels.Remove(panel); Rewatch(); };
        Rewatch();
        return panel;
    }

    /// <summary>Makes a section strip click on <paramref name="arrangement"/> jump to its section.</summary>
    public void Follow(ArrangementView arrangement)
    {
        var input = arrangement.Input;
        if (_followed.Contains(input)) return;
        _followed.Add(input);
        input.SectionClicked += OnSectionClicked;
    }

    private void OnSectionClicked(int index, bool selectRange)
    {
        var sorted = Sorted();
        if (index >= 0 && index < sorted.Count) Jump(sorted[index], selectRange);
    }

    /// <summary>Goes to <paramref name="section"/>: cursor, score, timeline and playback; <paramref name="selectRange"/> also selects its bars.</summary>
    public void Jump(MarkerModel section, bool selectRange)
    {
        // While recording, a section jump never moves the transport (the take stays anchored where recording began).
        if (_arrangement()?.Transport.Recording == true) return;
        var sorted = Sorted();
        var index = sorted.ToList().FindIndex(m => ReferenceEquals(m, section));
        var barCount = _score.Editor.BarCount;
        if (index < 0 || barCount == 0) return;
        var first = Math.Clamp(section.MeasureIndex, 0, barCount - 1);
        var last = Math.Max(first, SectionLayout.End(sorted, index, barCount) - 1);
        _score.Editor.ClearSelection();
        _score.Editor.SetPosition(first, 0, _score.Editor.SelectedString);
        if (selectRange) _score.SelectBars(first, last);
        _score.RevealBar(first, _score.Follow.MarginPercent);
        _arrangement()?.RevealSection(first, last, selectRange);
        _playback?.For(_songs.Active)?.Seek(first, 0);
        // The jump lands on the section in both places: its strip block and its Sections row flash.
        _arrangement()?.FlashSection(section);
        foreach (var panel in _panels) panel.FlashSection(section);
        Refresh();
    }

    /// <summary>The section holding the playhead while playing, else the cursor; null before the first section.</summary>
    public MarkerModel? Current()
    {
        if (_songs.Songs.Count == 0) return null;   // a window whose last tab moved away has no song
        var transport = _playback?.Existing(_songs.Active)?.Transport;
        var bar = transport is { IsPlaying: true, IsPaused: false, PlayheadBar: >= 0 } ? transport.PlayheadBar : _score.Editor.SelectedMeasure;
        return Sorted().LastOrDefault(m => m.MeasureIndex <= bar);
    }

    /// <summary>Brings every panel to the shown song's sections and the current section.</summary>
    public void Refresh()
    {
        var sorted = Sorted();
        var signature = string.Join("|", sorted.Select(m => $"{m.MeasureIndex}:{m.Title}:{m.ColorHex}:{m.LockPosition}"));
        var current = Current();
        foreach (var panel in _panels)
        {
            if (signature != _shownSignature) panel.Rebuild(sorted);
            panel.Highlight(current);
        }
        _shownSignature = signature;
    }

    private IReadOnlyList<MarkerModel> Sorted() =>
        _songs.Songs.Count == 0 ? [] : SectionListPresenter.InBarOrder(_songs.Active.Project.Markers);   // a window whose last tab moved away has no song

    // The edits and the playhead of the shown song drive the panels; a tab change moves both subscriptions to the new song.
    private void Rewatch()
    {
        if (_watchedSession is not null) _watchedSession.PlayheadBarChanged -= Refresh;
        var hasSong = _panels.Count > 0 && _songs.Songs.Count > 0;
        _watch.Bind(!hasSong ? null : _songs.Active);
        _watchedSession = !hasSong ? null : _playback?.For(_songs.Active);
        if (_watchedSession is not null) _watchedSession.PlayheadBarChanged += Refresh;
        _shownSignature = "";
        Refresh();
    }
}

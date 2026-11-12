using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: what every clip or lane edit needs after it landed, in the clip-edit order: the song grows to hold its clips (with 2 bars of headroom when the
//   edit did not fit the song itself), the "Remove empty clip lanes automatically" setting compacts every track's empty lanes (ClipLanes.Compact skips
//   armed tracks), the engine is brought in line down its normal sync path, and the rows are laid out again; the same after a section move (engine sync
//   and the live arrangement refresh); and the window wiring of each arrangement to it (its ClipEditLanded and SectionMoved events, its clip-overlap
//   policy and its section-move prompts). The window's one finisher is MainWindowDocumentPanes.ClipEdits; edits that land outside the timeline
//   (a recording, a piano-roll edit, collected media) call Finish on it.
//   Rule kept from before (a known hazard, see the cleanup report): with no arrangement pane the fix-up does not run, so such an edit neither grows the
//   song nor compacts lanes until the next timeline edit.
// Does not own: the edit itself (MainWindowClipKeys, TimelineClipGestureController), the lane rules (ClipLanes in Core), the engine sync
//   (AvaloniaPlaybackSessions/AvaloniaEngineRouting), the rows and body drawing (ArrangementView, TimelineView).
// Tests: TabForge.Avalonia.Tests.ClipEditFinishCharacterizationTests, RecordingLaneLifecycleTests.
internal sealed class ClipEditFinisher
{
    private static readonly TimelineSettings Defaults = new();
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowSettings? _settings;
    private readonly AvaloniaPlaybackSessions? _playback;

    private ClipEditFinisher(MainWindowDocumentPanes panes, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback)
    {
        _panes = panes;
        _settings = settings;
        _playback = playback;
    }

    /// <summary>Makes the window's finisher (<see cref="MainWindowDocumentPanes.ClipEdits"/>) and hooks the existing arrangement and every later one;
    /// null settings keep the default (empty lanes are compacted).</summary>
    public static ClipEditFinisher Register(MainWindowDocumentPanes panes, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback, Action<string>? notice = null, Window? owner = null)
    {
        var finisher = new ClipEditFinisher(panes, settings, playback);
        panes.ClipEdits = finisher;
        void Attach(ArrangementView arrangement)
        {
            arrangement.Timeline.ClipEditLanded += document => AfterEdit(arrangement, document, settings, playback);
            if (notice is not null) arrangement.Timeline.ClipNotice += notice;
            arrangement.Timeline.ClipOverlap = OverlapPolicy(settings, owner);
            arrangement.Input.SectionMoved += (_, mapping) => AfterSectionMove(arrangement.Document, mapping, settings, playback);
            WireSectionAudio(arrangement, settings, owner);
        }
        panes.OnArrangement(Attach);
        return finisher;
    }

    /// <summary>An edit of <paramref name="document"/> landed outside the timeline (a recording, a piano-roll edit, collected media): the same fix-up as a
    /// timeline clip edit, on the shown arrangement. Nothing runs while no arrangement exists (today's rule).</summary>
    public void Finish(IEditableDocument document)
    {
        if (_panes.Arrangement is { } arrangement) AfterEdit(arrangement, document, _settings, _playback);
    }

    /// <summary>The "Clips overlap" decision of one arrangement: the setting is read at each check and a ticked Remember is saved at once. Without settings or a window overlaps are left as they are.</summary>
    internal static ClipOverlapPolicy? OverlapPolicy(MainWindowSettings? settings, Window? owner)
    {
        if (settings is null || owner is null) return null;
        return new ClipOverlapPolicy
        {
            Setting = () => settings.Settings.Editing.ClipOverlap,
            Remember = value => { settings.Settings.Editing.ClipOverlap = value; settings.SaveSettings(); },
            Prompt = question => ClipOverlapPromptWindow.AskAsync(owner, question),
        };
    }

    /// <summary>Connects a section drop to the "Audio crosses this section" prompt and its setting; the setting is read at each drop and a ticked Remember is saved at once. Without a window the crossing audio is left where it is.</summary>
    private static void WireSectionAudio(ArrangementView arrangement, MainWindowSettings? settings, Window? owner)
    {
        if (settings is null || owner is null) return;
        arrangement.Input.Prompts = new SectionMovePrompts
        {
            AudioSetting = () => settings.Settings.Editing.SectionMoveAudio,
            AudioRemember = value => { settings.Settings.Editing.SectionMoveAudio = value; settings.SaveSettings(); },
            GapClosePrompt = (first, last, contents) => GapClosePromptWindow.AskAsync(owner, first, last, contents),
            AudioPrompt = crossing => SectionAudioPromptWindow.AskAsync(owner, arrangement.Document.Project, crossing),
            StretchSetting = () => settings.Settings.Editing.SectionStretch,
            StretchRemember = value => { settings.Settings.Editing.SectionStretch = value; settings.SaveSettings(); },
            StretchPrompt = question => SectionStretchPromptWindow.AskAsync(owner, question),
            StretchBackSetting = () => settings.Settings.Editing.SectionStretchBack,
            StretchBackRemember = value => { settings.Settings.Editing.SectionStretchBack = value; settings.SaveSettings(); },
            StretchBackPrompt = question => SectionStretchPromptWindow.AskAsync(owner, question),
        };
    }

    /// <summary>The setting is read live, so toggling it in Preferences applies to the next edit without rebuilding the hook.</summary>
    internal static bool CompactsEmptyLanes(MainWindowSettings? settings) =>
        (settings?.Settings.Timeline ?? Defaults).AutoRemoveEmptyLanes;

    /// <summary>The empty bars the post-edit growth leaves after the material, so a clip dropped, moved or nudged on can land past it.</summary>
    private const int GrowthHeadroomBars = 2;

    /// <summary>One clip/lane edit landed: grow the song to hold every clip (with the headroom), compact empty lanes when the setting says
    /// so, sync the engine, and lay the rows out again.</summary>
    internal static void AfterEdit(ArrangementView arrangement, IEditableDocument document, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback)
    {
        ClipAutoBars.GrowToClips(document.Project, GrowthHeadroomBars);   // recorded or pasted material past the end; the bars count as auto-added
        if (CompactsEmptyLanes(settings))
            foreach (var track in document.Project.Tracks) ClipLanes.Compact(track);
        if (document is SongDocument song)
            playback?.SyncEngine(song, settings?.Settings.Plugins ?? new PluginSettings());
        arrangement.ReloadRows(document.Project);   // the row source: a new track or a changed row height re-fits the track list now, not at the next layout
    }

    /// <summary>A section drop committed: the moved clips reach the engine down the normal path, and a playing song takes the new
    /// order from the next bar (no restart). The rows are already reloaded by the drop itself.</summary>
    internal static void AfterSectionMove(IEditableDocument document, int[] baseToCurrentBar, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback)
    {
        if (document is not SongDocument song) return;
        playback?.SyncEngine(song, settings?.Settings.Plugins ?? new PluginSettings());
        if (playback?.Existing(song) is { IsPlaying: true } session)
            session.RefreshArrangementLive(song.Project, baseToCurrentBar);
    }
}

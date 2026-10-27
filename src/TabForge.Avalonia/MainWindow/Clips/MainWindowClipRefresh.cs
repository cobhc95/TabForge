using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: what every clip or lane edit needs after it landed, in the clip-edit order: the "Remove empty clip lanes
//   automatically" setting compacts every track's empty lanes (ClipLanes.Compact skips armed tracks), and the engine is brought in line down
//   its normal sync path, so a fade/move/trim/recording landing is heard without waiting for the next play.
// Does not own: the edit itself (MainWindowClipKeys, TimelineClipGestureController), the lane rules (ClipLanes in Core), the engine sync
//   (AvaloniaPlaybackSessions/AvaloniaEngineRouting), the rows and body drawing (ArrangementView, TimelineView).
// Tests: TabForge.Avalonia.Tests.RecordingLaneLifecycleTests.
internal static class MainWindowClipRefresh
{
    private static readonly TimelineSettings Defaults = new();

    /// <summary>Hooks the existing arrangement and every later one; null settings keep the default (empty lanes are compacted).</summary>
    public static void Register(MainWindowDocumentPanes panes, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback, Action<string>? notice = null, Window? owner = null)
    {
        void Attach(ArrangementView arrangement)
        {
            arrangement.Timeline.ClipEdited = document => AfterEdit(arrangement, document, settings, playback);
            arrangement.Timeline.ClipNotice = notice;
            arrangement.Timeline.ClipOverlap = OverlapPolicy(settings, owner);
            arrangement.Input.SectionMoved += (_, mapping) => AfterSectionMove(arrangement.Document, mapping, settings, playback);
            WireSectionAudio(arrangement, settings, owner);
        }
        if (panes.Arrangement is { } existing) Attach(existing);
        panes.ArrangementCreated += Attach;
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
        arrangement.Input.SectionAudioSetting = () => settings.Settings.Editing.SectionMoveAudio;
        arrangement.Input.SectionAudioRemember = value => { settings.Settings.Editing.SectionMoveAudio = value; settings.SaveSettings(); };
        arrangement.Input.GapClosePrompt = (first, last, contents) => GapClosePromptWindow.AskAsync(owner, first, last, contents);
        arrangement.Input.SectionAudioPrompt = crossing => SectionAudioPromptWindow.AskAsync(owner, arrangement.Document.Project, crossing);
        arrangement.Input.SectionStretchSetting = () => settings.Settings.Editing.SectionStretch;
        arrangement.Input.SectionStretchRemember = value => { settings.Settings.Editing.SectionStretch = value; settings.SaveSettings(); };
        arrangement.Input.SectionStretchPrompt = question => SectionStretchPromptWindow.AskAsync(owner, question);
        arrangement.Input.SectionStretchBackSetting = () => settings.Settings.Editing.SectionStretchBack;
        arrangement.Input.SectionStretchBackRemember = value => { settings.Settings.Editing.SectionStretchBack = value; settings.SaveSettings(); };
        arrangement.Input.SectionStretchBackPrompt = question => SectionStretchPromptWindow.AskAsync(owner, question);
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

using System;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: what every clip or lane edit needs after it landed, in the WPF ClipEditController.Changed order: the "Remove empty clip lanes
//   automatically" setting compacts every track's empty lanes (ClipLanes.Compact skips armed tracks), and the engine is brought in line down
//   its normal sync path, so a fade/move/trim/recording landing is heard without waiting for the next play.
// Does not own: the edit itself (MainWindowClipKeys, TimelineClipGestureController), the lane rules (ClipLanes in Core), the engine sync
//   (AvaloniaPlaybackSessions/AvaloniaEngineRouting), the rows and body drawing (ArrangementView, TimelineView).
// Tests: TabForge.Avalonia.Tests.RecordingLaneLifecycleTests.
internal static class MainWindowClipRefresh
{
    private static readonly TimelineSettings Defaults = new();

    /// <summary>Hooks the existing arrangement and every later one; null settings keep the default (empty lanes are compacted).</summary>
    public static void Register(MainWindowDocumentPanes panes, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback, Action<string>? notice = null)
    {
        void Attach(ArrangementView arrangement)
        {
            arrangement.Timeline.ClipEdited = document => AfterEdit(arrangement, document, settings, playback);
            arrangement.Timeline.ClipNotice = notice;
            arrangement.Input.SectionMoved += (_, mapping) => AfterSectionMove(arrangement.Document, mapping, settings, playback);
        }
        if (panes.Arrangement is { } existing) Attach(existing);
        panes.ArrangementCreated += Attach;
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
        arrangement.Timeline.Reload(document.Project);
        arrangement.Header.Reload(document.Project);
    }

    /// <summary>A section drop committed: the moved clips reach the engine down the normal path, and a playing song takes the new
    /// order from the next bar (no restart), as the WPF section move does. The rows are already reloaded by the drop itself.</summary>
    internal static void AfterSectionMove(IEditableDocument document, int[] baseToCurrentBar, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback)
    {
        if (document is not SongDocument song) return;
        playback?.SyncEngine(song, settings?.Settings.Plugins ?? new PluginSettings());
        if (playback?.Existing(song) is { IsPlaying: true } session)
            session.RefreshArrangementLive(song.Project, baseToCurrentBar);
    }
}

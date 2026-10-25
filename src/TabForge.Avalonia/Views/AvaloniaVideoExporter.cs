using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Video;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Views;

/// <summary>What the Export video dialog hands over: the compiled song, the range, the view, the chosen file and the frame rate.</summary>
public sealed record VideoExportPlan(ScoreTimeline Timeline, double StartMs, double EndMs, VideoViewSpec Spec, string Path, int Fps);

// Owns: the Avalonia side of File > Export video: the request the shared VideoExportFlow (Core) runs, with this app's frame source
//   (AvaloniaVideoFrameSource) and UI thread; stopping the song first, re-syncing its engine after, and the missing plug-ins question.
// Does not own: the export steps (VideoExportFlow), the frames (AvaloniaVideoFrameSource), the dialog and its choices (VideoExportWindow).
// Tests: TabForge.Avalonia.Tests.VideoExportParityTests.
/// <summary>Runs one video export of a song through the window's engine.</summary>
public sealed class AvaloniaVideoExporter
{
    private const string NoEngineText = "Video export needs the audio engine.";

    private readonly AvaloniaPlaybackSessions _playback;
    private readonly AppSettings _settings;

    public AvaloniaVideoExporter(AvaloniaPlaybackSessions playback, AppSettings settings)
    {
        _playback = playback; _settings = settings;
    }

    /// <summary>The engine the sound renders through; null when the window has none.</summary>
    public AudioEngineClient? Engine => _playback.Engine;

    /// <summary>The request for <paramref name="plan"/>, drawn by the Avalonia frame source on <paramref name="ui"/>.</summary>
    public static VideoExportRequest Request(SongProject project, AppSettings settings, AudioEngineClient engine, VideoExportPlan plan, IUiThread ui,
        Action? restore, Func<IReadOnlyList<string>, Task<bool>>? confirmIncomplete, PluginSettings? plugins = null) => new()
    {
        Project = project, Settings = settings, Engine = engine, Timeline = plan.Timeline, StartMs = plan.StartMs, EndMs = plan.EndMs, Spec = plan.Spec,
        Path = plan.Path, Fps = plan.Fps, Ui = ui, Plugins = plugins, Restore = restore, ConfirmIncomplete = confirmIncomplete,
        OpenFrames = () => new AvaloniaVideoFrameSource(project, plan.Timeline, plan.Spec, settings.Appearance),
    };

    /// <summary>Stops the song, exports it and re-syncs its engine. Throws what the flow throws (cancel, render or encoder failures).</summary>
    public Task<VideoExportResult> Run(SongDocument document, VideoExportPlan plan, Window owner, IProgress<VideoExportProgress> progress, CancellationToken cancel)
    {
        if (Engine is not { } engine) throw new InvalidOperationException(NoEngineText);
        _playback.Existing(document)?.Stop();
        var plugins = _settings.Plugins;
        var request = Request(document.Project, _settings, engine, plan, AvaloniaUiThread.ForApplication(), () => _playback.SyncEngine(document, plugins),
            missing => AskExportWithout(owner, missing));
        return VideoExportFlow.RunAsync(request, progress, cancel);
    }

    private static async Task<bool> AskExportWithout(Window owner, IReadOnlyList<string> missing) =>
        await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), owner, VideoExportPresenter.MissingPluginsMessage(missing), VideoExportPresenter.MissingPluginsTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxAnswer.No) == MessageBoxAnswer.Yes;
}

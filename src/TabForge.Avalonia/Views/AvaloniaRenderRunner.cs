using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Rendering;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>What the Render dialog hands over: the compiled song, the range in song milliseconds and the files to write.</summary>
public sealed record RenderPlan(ScoreTimeline Timeline, double StartMs, double EndMs, string? MasterFile, IReadOnlyDictionary<TrackModel, string> Stems, SongProject? Project = null);

// Owns: the Avalonia side of File > Render: the request the shared RenderJob (Core) runs, built from the song, the dialog's plan and the
// shared audio settings; stopping the song's playback first, re-syncing its engine after, and the
//   question about plug-ins that did not load.
// Does not own: the render (RenderJob, the engine), the dialog's controls and plan (RenderWindow), the engine routing (EngineSongSync, AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.RenderParityTests.
/// <summary>Runs one render of a song through the window's engine.</summary>
public sealed class AvaloniaRenderRunner
{
    private const string MissingPluginsText = "Some plug-ins are not loaded, so the render would sound different from the song:\n\n{0}\n\nRender anyway without them?";

    private readonly AvaloniaPlaybackSessions _playback;
    private readonly AppSettings _settings;

    public AvaloniaRenderRunner(AvaloniaPlaybackSessions playback, AppSettings settings)
    {
        _playback = playback; _settings = settings;
    }

    /// <summary>The engine renders run through; null when the window has none (Render then says it needs the audio engine).</summary>
    public AudioEngineClient? Engine => _playback.Engine;

    /// <summary>The request the dialog builds for the same song, plan and settings (master level from the audio settings).</summary>
    public static RenderRequest Request(SongProject project, RenderPlan plan, RenderSettings settings, PluginSettings plugins, int masterPercent,
        Action? restore, Func<IReadOnlyList<string>, Task<bool>>? confirmIncomplete) => new()
    {
        Project = plan.Project ?? project, Plugins = plugins, MasterPercent = masterPercent, Settings = settings, Timeline = plan.Timeline,
        StartMs = plan.StartMs, EndMs = plan.EndMs, MasterFile = plan.MasterFile, Stems = plan.Stems, Restore = restore, ConfirmIncomplete = confirmIncomplete,
    };

    /// <summary>Stops the song, renders it and re-syncs its engine. Throws RenderException (cancelled or failed) as the job does.</summary>
    public Task<RenderResult> Run(SongDocument document, RenderPlan plan, RenderSettings settings, Window owner, IProgress<RenderProgressInfo> progress, CancellationToken cancel)
    {
        if (Engine is not { } engine) throw new RenderException("Rendering needs the audio engine.");
        _playback.Existing(document)?.Stop();
        var plugins = _settings.Plugins;
        var request = Request(document.Project, plan, settings, plugins, _settings.Audio.MasterVolume, () => _playback.SyncEngine(document, plugins),
            missing => AskRenderWithout(owner, missing));
        return RenderJob.RunAsync(request, progress, cancel, engine);
    }

    private static async Task<bool> AskRenderWithout(Window owner, IReadOnlyList<string> missing)
    {
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, MissingPluginsText, string.Join("\n", missing.Select(m => "• " + m)));
        return await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), owner, text, VideoExportPresenter.MissingPluginsTitle, MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxAnswer.No) == MessageBoxAnswer.Yes;
    }
}

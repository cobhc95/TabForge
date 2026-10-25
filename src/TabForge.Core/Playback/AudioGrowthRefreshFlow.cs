using System.Diagnostics;
using TabForge.Models;

namespace TabForge.Playback;

// Owns: bounded background compilation for a reserved audio-growth refresh.
// Does not own: reservation state, timeline publication, MIDI output, or live project mutation.
// Tests: no dedicated test class.
internal interface IAudioGrowthRefreshHost
{
    void CompleteAudioGrowthRefresh(AudioGrowthReservation reservation, ArrangementRefreshCompiler.Plan? plan,
        ScoreTimeline? loopTimeline, string? failure);
}

internal sealed record AudioGrowthReservation(
    int Generation, int Sequence, ScoreTimeline Timeline, PlaybackOptions Options, ScoreBar ActiveBar, SongProject Project,
    SongSnapshot Snapshot);   // Snapshot: the copy the growth refresh compiles from, taken on the owner thread; the swapped-in timeline is published with it

internal sealed class AudioGrowthRefreshFlow
{
    private const int CompileLimitMs = 15_000;
    private readonly IAudioGrowthRefreshHost _host;

    public AudioGrowthRefreshFlow(IAudioGrowthRefreshHost host) => _host = host;

    public void Start(AudioGrowthReservation reservation, int[] baseToCurrentBar)
    {
        var completion = CompleteAsync(reservation, baseToCurrentBar);
        _ = completion.ContinueWith(task =>
        {
            var error = task.Exception?.GetBaseException();
            Services.Trace.Error(Services.Trace.Playback, $"Audio growth arrangement refresh failed: {error}");
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task CompleteAsync(AudioGrowthReservation reservation, int[] baseToCurrentBar)
    {
        Task<(ArrangementRefreshCompiler.Plan Plan, ScoreTimeline? Loop)>? compile = null;
        try { compile = Task.Run(() => Build(reservation, baseToCurrentBar)); }
        catch (Exception ex) // Not logged: playback path: no logging on this path
        {
            _host.CompleteAudioGrowthRefresh(reservation, null, null, ex.Message);
            if (ex is OutOfMemoryException) throw;
            return;
        }

        try
        {
            if (!ReferenceEquals(await Task.WhenAny(compile, Task.Delay(CompileLimitMs)).ConfigureAwait(false), compile))
            {
                _host.CompleteAudioGrowthRefresh(reservation, null, null, "timeline compilation exceeded its bounded wait");
                ObserveLateFailure(compile);
                return;
            }

            var result = await compile.ConfigureAwait(false);
            _host.CompleteAudioGrowthRefresh(reservation, result.Plan, result.Loop, null);
        }
        catch (Exception ex)
        {
            if (compile is not null) ObserveLateFailure(compile);
            _host.CompleteAudioGrowthRefresh(reservation, null, null, ex.Message);
            if (ex is OutOfMemoryException) throw;
        }
    }

    private static void ObserveLateFailure(Task task) => _ = task.ContinueWith(
        completed => _ = completed.Exception, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static (ArrangementRefreshCompiler.Plan Plan, ScoreTimeline? Loop) Build(
        AudioGrowthReservation reservation, int[] baseToCurrentBar)
    {
        IScoreQuery source = reservation.Snapshot;
        var plan = source.CompileFuture(reservation.Timeline, reservation.Options, baseToCurrentBar, reservation.ActiveBar, null);
        var wholeLoop = reservation.Options.Loop
            ? source.CompileLoop(baseToCurrentBar, plan)
            : null;
        return (plan, wholeLoop);
    }
}

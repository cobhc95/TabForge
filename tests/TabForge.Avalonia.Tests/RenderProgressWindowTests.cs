using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the progress window's Cancel rule, that cancelling stops the job's token and the job's cancellation reaches the caller.
/// Does not own: the render job itself (AudioEngineClient.RenderAsync) or the progress text.
/// Tests: this file (run with --only through dotnet test --filter RenderProgressWindowTests).
/// </summary>
public sealed class RenderProgressWindowTests
{
    [AvaloniaFact]
    public async Task CancelStopsTheJobToken()
    {
        var window = new RenderProgressWindow("Render to file");
        var running = window.RunAsync<bool>(async (_, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return true;
        });
        window.RequestCancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => running);
    }
}

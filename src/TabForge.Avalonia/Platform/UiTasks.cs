using TabForge.Avalonia.Views;
using System;
using System.Threading.Tasks;

namespace TabForge.Avalonia.Platform;

// Owns: observing a task that a UI command starts and does not wait for, so a failure is reported and never left unobserved.
// Does not own: the status notice (the caller passes it as report) or the work itself. There is no console fallback: a GUI app has no console.
// Tests: TabForge.Avalonia.Tests.UiTasksTests.
/// <summary>Fire-and-forget with a report. UI thread: the report runs on the context the call started on.</summary>
public static class UiTasks
{
    // async void: the caller cannot wait; every exception is caught and reported inside.
    /// <summary>Awaits <paramref name="task"/> and reports "{what} failed: message" through <paramref name="report"/>, which the caller supplies.</summary>
    public static async void Forget(Task task, string what, Action<string> report)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            // Reported, not swallowed: the command's caller cannot await, so this is the only place the failure surfaces.
            report.Error($"{what} failed: {ex.Message}");
        }
    }
}

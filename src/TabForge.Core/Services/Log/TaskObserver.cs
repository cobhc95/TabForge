namespace TabForge.Services;

// Owns: observing a task that a command starts and does not wait for, so a failure is logged to the "engine" trace log and never left unobserved (the Core's counterpart of UiTasks in the app).
// Does not own: the work itself or any status notice (the app's UiTasks reports to the caller's status line).
// Tests: no dedicated test class.
internal static class TaskObserver
{
    // async void: the caller cannot wait; every exception is caught and logged inside.
    /// <summary>Awaits <paramref name="task"/> and logs "{what} failed: message" when it throws.</summary>
    public static async void Observe(Task task, string what)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Trace.Error(Trace.Engine, $"{what} failed: {ex.Message}");
        }
    }
}

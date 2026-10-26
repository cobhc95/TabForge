// Owns: the two modal-dialog launcher shapes the app's views use: a dialog whose answer is the value it closes with (Ask), and a dialog whose
//   answer is a property it sets before closing (AskFrom), plus the fire-and-forget launch that reports a failure instead of losing it.
// Does not own: the dialogs themselves (each window builds its content and answer), what the answer means (the caller), or the status notice
//   (the caller supplies the report). The owner is always passed through unchanged; a null owner is shown non-modally because Avalonia's
//   ShowDialog needs an owner, and no converted site passes a null owner today.
// Tests: TabForge.Avalonia.Tests.DialogLauncherTests.
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views.Dialogs;

/// <summary>Opens a dialog modally over its owner and gives back the dialog's answer.</summary>
public static class DialogLauncher
{
    /// <summary>The report text <see cref="FireAndReport"/> uses when the dialog could not be shown.</summary>
    private const string FireAndReportWhat = "The dialog";

    /// <summary>
    /// Shows a dialog built by <paramref name="create"/> modally over <paramref name="owner"/> and returns the value it closed with (the
    /// dialog's <c>Close(value)</c> answer). With no owner the window is shown non-modally and the default answer is returned:
    /// Avalonia's modal show requires an owner, and a null owner is the caller having no window to open over. The show's task is returned
    /// directly (not built by an async state machine), so a caller that discards it keeps the same unobserved-task behaviour as a bare
    /// <c>ShowDialog</c> call.
    /// </summary>
    public static Task<TResult?> Ask<TWindow, TResult>(Window? owner, Func<TWindow> create)
        where TWindow : Window
    {
        var window = create();
        return owner is null ? ShownWithoutOwner<TResult>(window) : ShowModal<TResult>(window, owner);
    }

    /// <summary>
    /// Shows a dialog built by <paramref name="create"/> modally over <paramref name="owner"/> and returns the answer its property
    /// <paramref name="result"/> reads after it closes (a dialog that sets its answer instead of closing with one). The no-owner case is
    /// the same as <see cref="Ask{TWindow,TResult}"/>: shown non-modally, with the default answer, because Avalonia's modal show needs an owner.
    /// </summary>
    public static Task<TResult?> AskFrom<TWindow, TResult>(Window? owner, Func<TWindow> create, Func<TWindow, TResult> result)
        where TWindow : Window
    {
        var window = create();
        return owner is null ? ShownWithoutOwner<TResult>(window) : ReadAfterClose(window, owner, result);
    }

    // The null-owner case: show the window non-modally now (Avalonia's modal show needs an owner) and give the default answer.
    private static Task<TResult?> ShownWithoutOwner<TResult>(Window window)
    {
        window.Show();
        return Task.FromResult<TResult?>(default);
    }

    private static async Task<TResult?> ShowModal<TResult>(Window window, Window owner)
    {
        PrepareOwned(window);
        return await window.ShowDialog<TResult>(owner);
    }

    private static async Task<TResult?> ReadAfterClose<TWindow, TResult>(TWindow window, Window owner, Func<TWindow, TResult> result)
        where TWindow : Window
    {
        PrepareOwned(window);
        await window.ShowDialog(owner);
        return result(window);
    }

    /// <summary>An owned dialog shows no taskbar button of its own (one taskbar entry, the owner's).</summary>
    private static void PrepareOwned(Window window) => window.ShowInTaskbar = false;

    /// <summary>
    /// Shows a dialog built by <paramref name="create"/> modally over <paramref name="owner"/> without waiting for its answer. The window's
    /// task is observed, so a failure is reported through <paramref name="report"/> instead of being lost (the same way <see cref="UiTasks"/>
    /// observes a UI command). With no owner the window is shown non-modally, as in <see cref="Ask{TWindow,TResult}"/>.
    /// </summary>
    public static void FireAndReport(Window? owner, Func<Window> create, Action<string> report)
    {
        var window = create();
        if (owner is null) { window.Show(); return; }
        PrepareOwned(window);
        UiTasks.Forget(window.ShowDialog(owner), FireAndReportWhat, report);
    }
}

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Dialogs;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the two dialog-launcher shapes' contract: the modal answer a dialog closes with is returned, the answer property a dialog sets is read
/// after it closes, and a null owner still shows the dialog (non-modally, as Avalonia's modal show needs an owner).
/// Does not own: the dialogs themselves or what their answers mean (the callers).
/// Tests: this class.
/// </summary>
public class DialogLauncherTests
{
    [AvaloniaFact]
    public async Task AskReturnsTheValueTheDialogClosedWith()
    {
        var owner = new Window { Width = 300, Height = 200 };
        owner.Show();
        ProbeDialog? dialog = null;
        var answer = DialogLauncher.Ask<ProbeDialog, int?>(owner, () => dialog = new ProbeDialog());
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(dialog);
        Assert.True(dialog!.IsVisible, "the dialog is shown modally over its owner");
        Assert.False(dialog.ShowInTaskbar, "an owned dialog shows no taskbar button of its own");
        Assert.Same(owner, dialog.Owner);
        dialog.Close(42);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(42, await answer);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task AskFromReadsTheAnswerPropertyAfterTheDialogCloses()
    {
        var owner = new Window { Width = 300, Height = 200 };
        owner.Show();
        ProbeDialog? dialog = null;
        var answer = DialogLauncher.AskFrom<ProbeDialog, int?>(owner, () => dialog = new ProbeDialog(), shown => shown.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(dialog);
        dialog!.Value = 7;
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7, await answer);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task AskWithoutAnOwnerStillShowsTheDialog()
    {
        ProbeDialog? dialog = null;
        var answer = await DialogLauncher.Ask<ProbeDialog, int?>(null, () => dialog = new ProbeDialog());
        Assert.NotNull(dialog);
        Assert.True(dialog!.IsVisible, "without an owner the dialog is still shown, non-modally");
        Assert.Null(answer);
        dialog.Close();
    }

    /// <summary>A dialog with no content whose answer is whatever it is closed with, or its <see cref="Value"/> property.</summary>
    private sealed class ProbeDialog : Window
    {
        public int? Value { get; set; }
    }
}

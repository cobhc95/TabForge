using System.Diagnostics;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestReusableBarRangePrompt() => RunInWindowFixture((owner, _) =>
    {
        var previousCapture = DialogHost.Capture;
        DialogHost.Capture = null;
        var hostA = new PromptReuseHost(owner, "Ctrl+Delete");
        var promptA = new BarRangePrompt(hostA);
        Window? ownerB = null;
        BarRangePrompt? promptB = null;
        Window? ownerC = null;
        BarRangePrompt? promptC = null;
        var cancelOwnerClosing = true;
        CancelEventHandler cancelClose = (_, args) => args.Cancel = cancelOwnerClosing;
        try
        {
            var nestedRejected = false;
            var first = ShowPrompt(owner, promptA, "Bars 2-3 are selected. What should Delete do?", BarRangeAction.Clear, allTracks: false, dialog =>
            {
                var labels = Logical<RadioButton>(dialog).Select(button => button.Content?.ToString() ?? "").ToArray();
                Check("bar prompt reuse: first real modal show uses current wording, keys and preselection",
                    Logical<TextBlock>(dialog).Any(block => block.Text.Contains("Bars 2-3 are selected"))
                    && labels.Any(label => label.Contains("Ctrl+Delete"))
                    && dialog.SelectedChoice == Array.IndexOf(Enum.GetValues<BarRangeAction>(), BarRangeAction.Clear)
                    && dialog.SelectedScope == 1 && dialog.Result == MessageBoxResult.Cancel && !dialog.RememberChoice,
                    string.Join(" | ", labels));
                nestedRejected = promptA.Ask("nested", BarRangeAction.InsertAfter, true) is null;
                Check("bar prompt reuse: a reentrant request is rejected without opening another window",
                    nestedRejected && Application.Current!.Windows.OfType<ThemedConfirmDialog>().Count(window => window.IsVisible) == 1);
                dialog.PickForTest(1, 0);
                dialog.RememberForTest(true);
                dialog.AnswerForTest(MessageBoxResult.Yes);
            });
            Check("bar prompt reuse: Yes returns the chosen action, scope and remember state after Hide",
                first.Error is null && first.Answer == new BarRangeAnswer(BarRangeAction.Remove, true, true)
                && first.Dialog is { IsVisible: false }, first.Error?.ToString());

            hostA.DeleteGesture = "Alt+Delete";
            var second = ShowPrompt(owner, promptA, "Bars 8-9 are selected. What should Delete do?", BarRangeAction.InsertAfter, allTracks: true, dialog =>
            {
                var labels = Logical<RadioButton>(dialog).Select(button => button.Content?.ToString() ?? "").ToArray();
                Check("bar prompt reuse: each show refreshes wording, keys, preselection, scope and remembered flag",
                    ReferenceEquals(dialog, first.Dialog)
                    && Logical<TextBlock>(dialog).Any(block => block.Text.Contains("Bars 8-9 are selected"))
                    && labels.Any(label => label.Contains("Alt+Delete"))
                    && dialog.SelectedChoice == Array.IndexOf(Enum.GetValues<BarRangeAction>(), BarRangeAction.InsertAfter)
                    && dialog.SelectedScope == 0 && dialog.Result == MessageBoxResult.Cancel && !dialog.RememberChoice,
                    string.Join(" | ", labels));
                DialogHost.PressCancel(dialog);
            });
            Check("bar prompt reuse: Cancel after a prior Yes leaves no answer and keeps the same hidden window",
                second.Error is null && second.Answer is null && ReferenceEquals(second.Dialog, first.Dialog)
                && second.Dialog is { IsVisible: false }, second.Error?.ToString());

            var escape = ShowPrompt(owner, promptA, "Bars 10-10 are selected. What should Delete do?", BarRangeAction.Clear, allTracks: false, dialog =>
            {
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                dialog.RaiseEvent(key);
            });
            Check("bar prompt reuse: Esc cancels the visible modal and leaves it reusable",
                escape.Error is null && escape.Answer is null && ReferenceEquals(escape.Dialog, first.Dialog) && escape.Dialog is { IsVisible: false });

            var close = ShowPrompt(owner, promptA, "Bars 11-11 are selected. What should Delete do?", BarRangeAction.Remove, allTracks: true, dialog =>
            {
                var button = Logical<Button>(dialog).FirstOrDefault(candidate => candidate.Content as string == "×");
                button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Check("bar prompt reuse: the title-bar X cancels without closing the reusable window",
                close.Error is null && close.Answer is null && ReferenceEquals(close.Dialog, first.Dialog) && close.Dialog is { IsVisible: false });

            ThemedConfirmDialog? adopted = null;
            MessageBoxResult? adoptedResult = null;
            DialogHost.Capture = window =>
            {
                adopted = (ThemedConfirmDialog)window;
                adopted.Owner = null;
                adopted.Opacity = 0;
                adopted.ShowActivated = false;
                adopted.ShowInTaskbar = false;
                adopted.WindowStartupLocation = WindowStartupLocation.Manual;
                adopted.Left = -20000;
                adopted.Top = -20000;
                adopted.ShowModeless(result => adoptedResult = result);
                return null;
            };
            var adoptedAnswer = promptA.Ask("Captured once", BarRangeAction.Clear, allTracks: false);
            adopted?.Close();
            Check("bar prompt reuse: capture adoption uses a one-shot modeless dialog and closes safely",
                adopted is { IsVisible: false } && adoptedAnswer is null && adoptedResult == MessageBoxResult.Cancel);
            DialogHost.Capture = null;

            var legacy = ShowLegacyPrompt(owner);
            Check("bar prompt reuse: the original public dialog constructor retains one-shot modal Yes semantics",
                legacy.Error is null && legacy.Result == true && legacy.Dialog?.Result == MessageBoxResult.Yes
                && legacy.Dialog is { IsVisible: false }, legacy.Error?.ToString());

            var secondOwner = new Window
            {
                Owner = owner,
                Width = 200,
                Height = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000
            };
            ownerB = secondOwner;
            secondOwner.Show();
            var hostB = new PromptReuseHost(secondOwner, "Ctrl+Shift+Delete");
            promptB = new BarRangePrompt(hostB);
            var dialogClosed = false;
            var otherOwner = ShowPrompt(secondOwner, promptB, "Bars 4-4 are selected. What should Delete do?", BarRangeAction.Remove, allTracks: false, dialog =>
            {
                Check("bar prompt reuse: a second owner gets an independent visible prompt",
                    !ReferenceEquals(dialog, first.Dialog) && ReferenceEquals(dialog.Owner, secondOwner));
                dialog.Closed += (_, _) => dialogClosed = true;
                DialogHost.PressCancel(dialog);
            });
            Check("bar prompt reuse: second-owner cancel is independent", otherOwner.Error is null && otherOwner.Answer is null);
            secondOwner.Close();
            SettleLifetimeDispatcher();
            Check("bar prompt reuse: closing an owner removes its cached hidden prompt from application windows",
                dialogClosed && otherOwner.Dialog is { IsVisible: false }
                && !Application.Current!.Windows.OfType<Window>().Any(window => ReferenceEquals(window, otherOwner.Dialog)));

            var thirdOwner = new Window
            {
                Owner = owner, Width = 200, Height = 100, Opacity = 0, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
            };
            ownerC = thirdOwner;
            thirdOwner.Show();
            var hostC = new PromptReuseHost(thirdOwner, "Shift+Delete");
            promptC = new BarRangePrompt(hostC);
            thirdOwner.Closing += cancelClose;
            var cancelledDialogClosed = false;
            var cancelledHandle = IntPtr.Zero;
            var cancelledOwnerClose = ShowPrompt(thirdOwner, promptC, "Bars 5-5 are selected. What should Delete do?", BarRangeAction.Clear, allTracks: true, dialog =>
            {
                cancelledHandle = new WindowInteropHelper(dialog).Handle;
                dialog.Closed += (_, _) => cancelledDialogClosed = true;
                thirdOwner.Close();
            });
            Check("bar prompt reuse: a cancelled owner close closes its visible prompt without an answer",
                cancelledOwnerClose.Error is null && cancelledOwnerClose.Answer is null && cancelledDialogClosed
                && thirdOwner.IsVisible && cancelledOwnerClose.Dialog is { IsVisible: false }
                && !Application.Current!.Windows.OfType<Window>().Any(window => ReferenceEquals(window, cancelledOwnerClose.Dialog)));

            var freshPrompt = ShowPrompt(thirdOwner, promptC, "Bars 6-6 are selected. What should Delete do?", BarRangeAction.InsertAfter, allTracks: false, dialog =>
            {
                var freshHandle = new WindowInteropHelper(dialog).Handle;
                Check("bar prompt reuse: a cancelled owner close creates a fresh prompt window",
                    !ReferenceEquals(dialog, cancelledOwnerClose.Dialog) && cancelledHandle != IntPtr.Zero && freshHandle != IntPtr.Zero
                    && dialog.SelectedChoice == Array.IndexOf(Enum.GetValues<BarRangeAction>(), BarRangeAction.InsertAfter));
                DialogHost.PressCancel(dialog);
            });
            Check("bar prompt reuse: the fresh prompt has no stale answer from the cancelled close",
                freshPrompt.Error is null && freshPrompt.Answer is null && freshPrompt.Dialog is { IsVisible: false });

            cancelOwnerClosing = false;
            thirdOwner.Closing -= cancelClose;
            var freshClosed = false;
            freshPrompt.Dialog!.Closed += (_, _) => freshClosed = true;
            thirdOwner.Close();
            SettleLifetimeDispatcher();
            Check("bar prompt reuse: closing an owner removes its newly cached hidden prompt",
                freshClosed && !Application.Current!.Windows.OfType<Window>().Any(window => ReferenceEquals(window, freshPrompt.Dialog)));
        }
        catch (Exception ex) { Check("bar prompt reuse: modal owner lifecycle scenarios complete", false, ex.ToString()); }
        finally
        {
            promptA.Dispose();
            promptB?.Dispose();
            promptC?.Dispose();
            if (ownerB?.IsVisible == true) ownerB.Close();
            cancelOwnerClosing = false;
            if (ownerC is not null) ownerC.Closing -= cancelClose;
            if (ownerC?.IsVisible == true) ownerC.Close();
            DialogHost.Capture = previousCapture;
        }
    });

    private static (BarRangeAnswer? Answer, ThemedConfirmDialog? Dialog, Exception? Error) ShowPrompt(
        Window owner, BarRangePrompt prompt, string text, BarRangeAction preselect, bool allTracks, Action<ThemedConfirmDialog> answer)
    {
        ThemedConfirmDialog? seen = null;
        Exception? error = null;
        var started = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(1) };
        EventHandler tick = (_, _) =>
        {
            var dialog = Application.Current!.Windows.OfType<ThemedConfirmDialog>()
                .FirstOrDefault(window => window.IsVisible && ReferenceEquals(window.Owner, owner));
            if (dialog is not null)
            {
                seen = dialog;
                timer.Stop();
                try { answer(dialog); }
                catch (Exception ex)
                {
                    error = ex;
                    if (dialog.IsVisible) DialogHost.PressCancel(dialog);
                }
            }
            else if (started.Elapsed > TimeSpan.FromSeconds(5))
            {
                error = new TimeoutException("The owner-scoped prompt did not become visible within five seconds.");
                timer.Stop();
            }
        };
        timer.Tick += tick;
        timer.Start();
        BarRangeAnswer? result = null;
        try { result = prompt.Ask(text, preselect, allTracks); }
        catch (Exception ex) { error = ex; }
        finally { timer.Stop(); timer.Tick -= tick; }
        return (result, seen, error);
    }

    private static (bool? Result, ThemedConfirmDialog? Dialog, Exception? Error) ShowLegacyPrompt(Window owner)
    {
        var dialog = new ThemedConfirmDialog("Legacy prompt", "Choose an option.", yesText: "Yes", noText: "No") { Owner = owner };
        Exception? error = null;
        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(1) };
        EventHandler tick = (_, _) =>
        {
            if (!dialog.IsVisible) return;
            timer.Stop();
            try { Logical<Button>(dialog).First(button => Equals(button.Content, "Yes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            catch (Exception ex) { error = ex; if (dialog.IsVisible) dialog.Close(); }
        };
        timer.Tick += tick;
        timer.Start();
        bool? result = null;
        try { result = DialogHost.ShowModal(dialog); }
        catch (Exception ex) { error = ex; }
        finally { timer.Stop(); timer.Tick -= tick; }
        return (result, dialog, error);
    }

    private sealed class PromptReuseHost : IBarRangePromptHost
    {
        public PromptReuseHost(Window owner, string deleteGesture)
        {
            Owner = owner;
            DeleteGesture = deleteGesture;
        }

        public Window Owner { get; }
        public AppSettings Settings { get; } = new();
        public string DeleteGesture
        {
            set => Settings.Hotkeys.Bindings["Range.Remove"] = value;
        }
    }
}

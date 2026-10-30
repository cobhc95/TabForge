using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform;
using TabForge.Services.Reset;

namespace TabForge.Avalonia.Views;

// Owns: the two-step "Reset entire app" dialog: the tick list with counts, Select all/none, Back up first, the typed RESET
//   gate, Back, and the backup that must finish before the dialog returns true.
// Does not own: building the plan (ResetAppPlan), the wipe (ResetAppWipe) or closing the app (PreferencesWindow).
// Tests: ResetAppDialogTests.
/// <summary>The owner's two-step confirmation for wiping the app stores; true when the user confirmed and any backup succeeded.</summary>
internal sealed class ResetAppWindow : Window
{
    private readonly ResetAppSelection _selection;
    private readonly ContentControl _host = new();
    private readonly StackPanel _stepOne;
    private readonly StackPanel _stepTwo;
    private readonly Button _wipe;
    private readonly Button _confirm;
    private readonly Button _back;
    private readonly TextBox _typed = new() { Name = "ResetConfirmBox", Watermark = ResetAppPrompt.ConfirmWord };
    private readonly TextBlock _status = ToolWindowParts.Status("ResetAppStatus");
    private readonly TextBlock _statusTwo = ToolWindowParts.Status("ResetAppStatusTwo");

    public ResetAppWindow(ResetAppPlan plan, bool allChecked = true)
    {
        _selection = new ResetAppSelection(plan, allChecked);
        Title = "Reset entire app";
        PromptChrome.Setup(this, Title, DialogMetrics.ResetAppWidth);
        SizeToContent = SizeToContent.Manual;
        Height = DialogMetrics.ResetAppHeight;
        MinHeight = DialogMetrics.ResetAppMinHeight;
        CanResize = true;

        _wipe = ActionButton("WipeButton", _selection.WipeText, danger: true);
        _wipe.Click += (_, _) => ShowStep(two: true);
        _wipe.IsEnabled = _selection.CanWipe;
        _selectAll = LinkButton("ResetAppSelectAll", "Select all");
        _selectNone = LinkButton("ResetAppSelectNone", "Select none");
        _selectAll.Click += (_, _) => { _selection.SelectAll(); SyncTicks(); };
        _selectNone.Click += (_, _) => { _selection.SelectNone(); SyncTicks(); };

        var backup = new CheckBox { Name = "ResetAppBackup", Content = "Back up first", IsChecked = _selection.BackUpFirst };
        AutomationProperties.SetName(backup, "Back up first");
        backup.IsCheckedChanged += (_, _) => _selection.BackUpFirst = backup.IsChecked == true;
        var backupHint = ToolWindowParts.Small("Writes a zip of the ticked items to Documents/TabForge-backups before wiping.");

        _stepOne = new StackPanel { Name = "ResetAppStepOne", Spacing = DialogMetrics.ResetAppSpacing };
        _stepOne.Children.Add(PromptChrome.Heading("Reset entire app"));
        _stepOne.Children.Add(PromptChrome.Muted("ResetAppIntro", "This permanently deletes what you tick below. Your songs are never touched."));
        _stepOne.Children.Add(new ScrollViewer { MaxHeight = DialogMetrics.ResetAppListMaxHeight, Content = ItemList() });
        _stepOne.Children.Add(ToolWindowParts.Row(_selectAll, _selectNone));
        _stepOne.Children.Add(_status);
        _stepOne.Children.Add(PromptChrome.Footer(backup, backupHint, PromptChrome.Action("ResetAppCancel", "Cancel", primary: false, isCancel: true), _wipe));

        _back = PromptChrome.Action("ResetAppBack", "Back", primary: false);
        _back.Click += (_, _) => ShowStep(two: false);
        _confirm = ActionButton("ResetAppConfirm", "Wipe and close", danger: true);
        _confirm.IsEnabled = false;
        _confirm.Click += (_, _) => _ = FinishAsync();
        _typed.TextChanged += (_, _) => _confirm.IsEnabled = ResetAppPrompt.CanWipe(_typed.Text);

        _stepTwo = new StackPanel { Name = "ResetAppStepTwo", Spacing = DialogMetrics.ResetAppSpacing, IsVisible = false };
        _stepTwo.Children.Add(PromptChrome.Heading("Are you sure?"));
        _stepTwo.Children.Add(PromptChrome.Muted("ResetAppSureText", "The ticked stores are wiped after TabForge closes. Your songs, projects, samples and documents are never touched."));
        _stepTwo.Children.Add(Group("Will be wiped", ResetAppPrompt.WipedText(_selection.CheckedPlanItems)));
        _stepTwo.Children.Add(Group("Kept", ResetAppPrompt.KeptText(_selection.KeptPlanItems)));
        _stepTwo.Children.Add(new TextBlock { Text = "Type RESET to confirm:" }.WithMargin(DialogMetrics.ResetAppConfirmGap));
        _stepTwo.Children.Add(_typed);
        _stepTwo.Children.Add(_statusTwo);
        _stepTwo.Children.Add(PromptChrome.Footer(null, null, _back, _confirm));

        _host.Content = _stepOne;
        Content = DialogMetrics.Inset(_host);
        ShowStep(two: false);
    }

    private readonly Button _selectAll;
    private readonly Button _selectNone;

    /// <summary>The ticks that will be written to the pending-wipe marker (only valid after the dialog returned true).</summary>
    public IReadOnlyList<ResetAppItemKind> SelectedKinds => _selection.CheckedItems;
    /// <summary>True when a backup zip was written before the dialog returned true.</summary>
    public bool BackupWritten { get; private set; }
    /// <summary>The primary button's text, for tests and accessibility.</summary>
    public string WipeText => _wipe.Content as string ?? "";

    private Control ItemList()
    {
        var list = new StackPanel { Name = "ResetAppItems", Spacing = DialogMetrics.ResetAppSpacing };
        foreach (var item in _selection.Items)
        {
            var check = new CheckBox { IsChecked = _selection.IsChecked(item.Kind), Content = new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap } };
            AutomationProperties.SetName(check, item.Title);
            check.IsCheckedChanged += (_, _) => { _selection.SetChecked(item.Kind, check.IsChecked == true); SyncTicks(); };
            list.Children.Add(check);
            list.Children.Add(ToolWindowParts.Small(item.CountText + " — " + item.Description));
        }
        return list;
    }

    private static Control Group(string title, string text) =>
        ToolWindowParts.Group(title, new ScrollViewer { MaxHeight = DialogMetrics.ResetAppListMaxHeight, Content = ToolWindowParts.Small(text) });

    private void ShowStep(bool two)
    {
        _host.Content = two ? _stepTwo : _stepOne;
        _stepOne.IsVisible = !two;
        _stepTwo.IsVisible = two;
        if (two)
        {
            SyncTicks();
            _statusTwo.Text = "";
            _typed.Text = "";
            _confirm.IsEnabled = false;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _typed.Focus());
        }
    }

    private void SyncTicks()
    {
        _wipe.Content = _selection.WipeText;
        _wipe.IsEnabled = _selection.CanWipe;
        if (_stepTwo.IsVisible)
        {
            // The lists in step two always reflect the ticks as they are now (Back lets the user change them).
            _stepTwo.Children[2] = Group("Will be wiped", ResetAppPrompt.WipedText(_selection.CheckedPlanItems));
            _stepTwo.Children[3] = Group("Kept", ResetAppPrompt.KeptText(_selection.KeptPlanItems));
        }
    }

    private async Task FinishAsync()
    {
        if (!_selection.CanWipe || !ResetAppPrompt.CanWipe(_typed.Text)) return;
        _confirm.IsEnabled = false;
        _back.IsEnabled = false;
        var status = _stepTwo.IsVisible ? _statusTwo : _status;
        if (_selection.BackUpFirst)
        {
            status.Text = "Writing the backup…";
            try
            {
                var plan = new ResetAppPlan(_selection.Plan.Folders, _selection.CheckedPlanItems);
                var result = await Task.Run(() => ResetAppBackup.Write(plan, _selection.Plan.Folders.Documents, DateTime.Now));
                BackupWritten = result.Ok;
                status.Text = result.Skipped.Count == 0 ? "Backup written." : "Backup written; some files were skipped.";
            }
            catch (Exception ex)
            {
                status.Text = "The backup could not be written: " + ex.Message;
                _confirm.IsEnabled = true;
                _back.IsEnabled = true;
                return;
            }
        }
        Close(true);
    }

    private static Button ActionButton(string name, string text, bool danger)
    {
        var button = new Button
        {
            Name = name, Content = text, MinWidth = DialogMetrics.PromptActionMinWidth, Height = DialogMetrics.PromptButtonHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
        }.WithPadding(DialogMetrics.PromptActionPadding);
        if (danger) button.Classes.Add(AvaloniaFormControlStyles.DangerButtonClass);
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button LinkButton(string name, string text)
    {
        var button = new Button { Name = name, Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("PrefsActionReset");
        AutomationProperties.SetName(button, text);
        return button;
    }
}

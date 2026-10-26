using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Shortcuts page of the Settings window: the preset box and two editable slots per command.
/// Owns: the rows (two slot buttons with Clear, Reassign, Reset, feedback, CUSTOM tag), turning key events into the catalogue key name and
/// modifiers, and refreshing the texts after every change.
/// Does not own: any shortcut rule (capture, conflicts, reassign, clear, reset, customized mark: HotkeyEditPresenter) or the settings dirty flag
/// (the window passes <c>changed</c>).
/// Tests: TabForge.Avalonia.Tests.PreferencesPagesTests.
/// </summary>
internal sealed class ShortcutsPage
{
    private sealed record Row(HotkeyAction Action, Button[] Slots, TextBlock Feedback, Button Reassign, Button Reset, TextBlock CustomTag);

    private const double PresetBoxWidth = 250;
    private const double PresetRowSpacing = 10;
    private const int FirstRowCount = 20;
    private const int BatchRowCount = 15;

    private readonly IUiThread _ui = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();   // builds the remaining cards in background batches
    private readonly HotkeySettings _settings;
    private readonly Action _changed;
    private readonly HotkeyEditPresenter _edit;
    private readonly List<Row> _rows = new();
    private readonly TextBlock _status = new TextBlock { FontSize = 11, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, IsVisible = false, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
    private IReadOnlySet<string>? _scope;   // the command ids this page shows; null = all
    private ComboBox? _preset;
    private bool _refreshing;

    internal ShortcutsPage(HotkeySettings settings, Action changed)
    {
        _settings = settings;
        _changed = changed;
        _edit = new HotkeyEditPresenter(settings);
    }

    /// <summary>
    /// The cards of the page: the Commands card (preset box and Reset category), then one card per category. With <paramref name="actionIds"/> (a search)
    /// only those commands are built, the Commands card only when <paramref name="commands"/>, and every caption is "Shortcuts › ...".
    /// </summary>
    internal IEnumerable<Control> Cards(IReadOnlySet<string>? actionIds = null, bool commands = true)
    {
        var presetEntry = SettingsCatalog.Special(SettingsCatalog.ActionPreset);
        var resetEntry = SettingsCatalog.Special(SettingsCatalog.ActionResetCategory);
        _scope = actionIds;
        string Caption(string title) => actionIds is null ? title : $"{presetEntry.Page} › {title}";
        if (commands)
        {
            var preset = new ComboBox { Width = PresetBoxWidth, ItemsSource = HotkeyPresets.Names.Append(HotkeyPresets.Custom).ToList(), SelectedItem = HotkeyPresets.Describe(_settings) };
            AutomationProperties.SetName(preset, presetEntry.Label);
            ToolTip.SetTip(preset, presetEntry.Tip);
            preset.SelectionChanged += (_, _) =>
            {
                if (_refreshing || preset.SelectedItem is not string chosen || chosen == HotkeyPresets.Custom || chosen == HotkeyPresets.Describe(_settings)) return;
                HotkeyPresets.Apply(_settings, chosen);
                Changed();
            };
            _preset = preset;
            var resetCategory = new Button { Content = resetEntry.Label, HorizontalAlignment = HorizontalAlignment.Left };
            resetCategory.Classes.Add("PrefsActionButton");
            resetCategory.Classes.Add("PrefsActionSecondary");
            AutomationProperties.SetName(resetCategory, resetEntry.Label);
            ToolTip.SetTip(resetCategory, resetEntry.Tip);
            resetCategory.Click += (_, _) => ResetCategories();
            var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = PresetRowSpacing };
            presetRow.Children.Add(new TextBlock { Text = presetEntry.Label, VerticalAlignment = VerticalAlignment.Center });
            presetRow.Children.Add(preset);
            presetRow.Children.Add(resetCategory);
            var top = new StackPanel { Spacing = 8 };
            top.Children.Add(presetRow);
            top.Children.Add(_status);
            yield return PreferencesSpecialPages.Card(Caption(presetEntry.Section), top);
        }
        var pending = new Queue<(StackPanel Stack, HotkeyAction Action)>();
        var cards = new List<Control>();
        foreach (var group in HotkeyCatalog.All.Where(a => actionIds is null || actionIds.Contains(a.Id)).GroupBy(a => a.Category))
        {
            var stack = new StackPanel { Spacing = 8 };
            foreach (var action in group) pending.Enqueue((stack, action));
            cards.Add(PreferencesSpecialPages.Card(Caption(group.Key), stack));
        }
        BuildRows(pending, FirstRowCount);
        foreach (var card in cards) yield return card;
        if (pending.Count > 0) _ui.Post(() => BuildBatch(pending, cards[0]), UiPriority.Background);
    }

    /// <summary>Builds up to <paramref name="count"/> queued rows into their category stacks.</summary>
    private void BuildRows(Queue<(StackPanel Stack, HotkeyAction Action)> pending, int count)
    {
        for (; count > 0 && pending.Count > 0; count--)
        {
            var (stack, action) = pending.Dequeue();
            stack.Children.Add(BuildRow(action));
        }
    }

    /// <summary>
    /// Builds one batch of rows on the UI thread, then posts the next. The page is rebuilt (search, page switch) by creating a new
    /// ShortcutsPage whose cards replace these; the key of the build is this page's queue, and a batch whose cards have been removed from the window
    /// (no parent) drops the rest of its queue.
    /// </summary>
    private void BuildBatch(Queue<(StackPanel Stack, HotkeyAction Action)> pending, Control anchor)
    {
        if (anchor.Parent is null) { pending.Clear(); return; }
        BuildRows(pending, BatchRowCount);
        if (pending.Count > 0) _ui.Post(() => BuildBatch(pending, anchor), UiPriority.Background);
    }

    private Control BuildRow(HotkeyAction action)
    {
        var slots = new[] { SlotButton(action, 1), SlotButton(action, 2) };
        var feedback = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("HotkeyFeedbackText") };
        var reassign = SmallButton(SettingsCatalog.ShortcutReassign, SettingsCatalog.ShortcutReassignLabel(action), () => { if (_edit.Reassign(action.Id)) Changed(); });
        reassign.IsVisible = false;
        var reset = SmallButton(SettingsCatalog.ShortcutReset, SettingsCatalog.ShortcutResetLabel(action), () => { _edit.Reset(action.Id); Changed(); });
        var tag = new TextBlock { Text = "CUSTOM", FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("HotkeyCustomTag") };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right };
        for (var slot = 1; slot <= 2; slot++)
        {
            var captured = slot;
            controls.Children.Add(slots[slot - 1]);
            controls.Children.Add(SmallButton(SettingsCatalog.ShortcutClear, SettingsCatalog.ShortcutClearLabel(action, slot), () => { _edit.Clear(action.Id, captured); Changed(); }));
        }
        controls.Children.Add(reassign);
        controls.Children.Add(reset);
        var right = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(controls);
        right.Children.Add(feedback);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = action.Name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(tag);
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(title);
        if (action.Description.Length > 0) label.Children.Add(ToolWindowParts.Small(action.Description));
        label.Children.Add(ToolWindowParts.Small(action.Category));
        // Each command row has 12 DIP of space above and below its key boxes, as the row (pitch about 58 DIP).
        const double commandRowVerticalMargin = 12;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), Name = "Shortcut", Margin = new Thickness(0, commandRowVerticalMargin) };
        grid.Children.Add(label);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        _rows.Add(new Row(action, slots, feedback, reassign, reset, tag));
        RefreshRow(_rows[^1]);
        return grid;
    }

    private Button SlotButton(HotkeyAction action, int slot)
    {
        var button = new Button { MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Classes.Add("PrefsActionButton");
        button.Classes.Add("PrefsActionSecondary");
        AutomationProperties.SetName(button, SettingsCatalog.ShortcutSlotLabel(action, slot));
        ToolTip.SetTip(button, $"Hotkey {slot}: click, then press the key combination to assign.");
        button.Click += (_, _) => Begin(action, slot);
        // Tunnelled so the Button never turns Enter or Space into a click while a shortcut is being captured.
        button.AddHandler(InputElement.KeyDownEvent, (_, e) => OnKey(action.Id, e), RoutingStrategies.Tunnel);
        button.LostFocus += (_, _) => { if (_edit.RecordingActionId == action.Id) CancelCapture(); };
        return button;
    }

    private static Button SmallButton(string text, string name, Action click)
    {
        var button = new Button { Content = text };
        button.Classes.Add("PrefsActionButton");
        if (text is SettingsCatalog.ShortcutClear or SettingsCatalog.ShortcutReset) button.Classes.Add("PrefsActionReset");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private void Begin(HotkeyAction action, int slot)
    {
        _edit.Begin(action.Id, slot);
        RefreshAll();
        var row = _rows.First(r => r.Action.Id == action.Id);
        row.Slots[slot - 1].Content = "Press a key…";
        row.Feedback.Text = HotkeyEditPresenter.PressPrompt;
        row.Feedback.IsVisible = true;
    }

    private void OnKey(string actionId, KeyEventArgs e)
    {
        if (_edit.RecordingActionId != actionId) return;
        e.Handled = true;
        var slot = _edit.RecordingSlot;
        var modifiers = HotkeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Windows;
        switch (_edit.KeyPressed(e.Key.ToString(), modifiers))
        {
            case HotkeyKeyOutcome.Cancelled:
                RefreshAll();
                break;
            case HotkeyKeyOutcome.Conflict:
                var row = _rows.First(r => r.Action.Id == actionId);
                row.Feedback.Text = _edit.Message;
                row.Feedback.IsVisible = true;
                row.Reassign.IsVisible = true;
                row.Slots[slot - 1].Content = "Conflict";
                break;
            case HotkeyKeyOutcome.Assigned:
                Changed();
                break;
        }
    }

    /// <summary>Restores the default bindings of the commands this page shows (all of them, or only the searched ones).</summary>
    private void ResetCategories()
    {
        foreach (var action in HotkeyCatalog.All.Where(a => _scope is null || _scope.Contains(a.Id))) _settings.Reset(action.Id);
        _changed();
        RefreshAll();
    }

    private void CancelCapture()
    {
        _edit.Cancel();
        RefreshAll();
    }

    private void Changed()
    {
        _changed();
        _status.Text = _edit.Message;
        _status.IsVisible = true;
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var row in _rows)
        {
            RefreshRow(row);
            row.Feedback.IsVisible = false;
            row.Reassign.IsVisible = false;
        }
        if (_preset is null) return;
        _refreshing = true;
        _preset.SelectedItem = HotkeyPresets.Describe(_settings);
        _refreshing = false;
    }

    private void RefreshRow(Row row)
    {
        for (var slot = 1; slot <= 2; slot++)
        {
            var gesture = HotkeyCatalog.GestureFor(_settings, row.Action.Id, slot);
            row.Slots[slot - 1].Content = string.IsNullOrWhiteSpace(gesture) ? "Unbound" : HotkeyCatalog.Display(gesture);
        }
        var customized = _edit.IsCustomized(row.Action);
        row.CustomTag.IsVisible = customized;
        row.Reset.IsVisible = customized;
    }
}

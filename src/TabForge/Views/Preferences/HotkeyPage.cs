using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Services;
using WpfPath = System.Windows.Shapes.Path;
using static TabForge.Views.PreferencesCards;

namespace TabForge.Views;

internal sealed record HotkeyControl(HotkeyAction Action, Button Gesture, Button Gesture2, TextBlock Feedback, Button Reassign, Button Reset)
{
    internal Button Slot(int slot) => slot == 2 ? Gesture2 : Gesture;
}

/// <summary>The Shortcuts page and the shortcut rows of search results: two shortcut slots per command (Hotkey 1 and Hotkey 2), shortcut capture, conflicts across both slots and per-row resets.</summary>
internal sealed class HotkeyPage
{
    private readonly IPreferencesHost _host;
    private readonly Window _window;
    private readonly List<HotkeyControl> _hotkeyControls = new();
    private string? _recordingActionId;
    private int _recordingSlot = 1;
    private (string ActionId, int Slot, string Gesture, string ConflictingActionId, int ConflictingSlot)? _pendingConflict;
    private static readonly Dictionary<string, string> HotkeySearchIndex = new(StringComparer.Ordinal);

    internal HotkeyPage(IPreferencesHost host)
    {
        _host = host;
        _window = host.Dialog;
    }

    internal void Clear() => _hotkeyControls.Clear();

    internal void RefreshResetButtons()
    {
        foreach (var row in _hotkeyControls)
            row.Reset.Visibility = IsHotkeyCustomized(row.Action) ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button CreateButton(string text, string styleKey, string? tooltip = null) => PreferencesCards.CreateButton(_window, text, styleKey, tooltip);

    private int _buildGeneration;

    internal void BuildPage(string? categoryFilter)
    {
        var matching = HotkeyCatalog.All.Where(action => categoryFilter is null ||
                categoryFilter.Equals(SettingsCatalog.Hotkeys, StringComparison.OrdinalIgnoreCase) ||
                action.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var reset = CreateButton("Reset category", "SecondaryActionButton", "Restore default shortcuts for the displayed category.");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Click += (_, _) => _host.ResetHotkeyCategory(matching.Select(action => action.Category).Distinct().ToList());
        // Preset: switching really rebinds every key to that application's layout. Editing any key
        // afterwards turns the layout into "Custom" (your changes on top of the chosen base).
        var preset = new ComboBox { Width = 250, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Keyboard layout preset", SelectedValuePath = "Tag" };
        foreach (var name in HotkeyPresets.Names)
        {
            var tip = HotkeyPresets.Tooltip(name);
            preset.Items.Add(new ComboBoxItem { Content = HotkeyPresets.DisplayName(name), Tag = name, ToolTip = tip.Length == 0 ? null : tip });
        }
        preset.Items.Add(new ComboBoxItem { Content = HotkeyPresets.Custom, Tag = HotkeyPresets.Custom });
        preset.SelectedValue = HotkeyPresets.Describe(_host.Settings.Hotkeys);
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedValue is not string chosen || chosen == HotkeyPresets.Custom ||
                chosen == HotkeyPresets.Describe(_host.Settings.Hotkeys)) return;
            HotkeyPresets.Apply(_host.Settings.Hotkeys, chosen);
            _host.SettingChanged();
            _host.RebuildPage();
        };
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(new TextBlock { Text = "Preset", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        presetRow.Children.Add(preset);
        presetRow.Children.Add(reset);
        _host.AddTopCard(GroupCardWithChildren("Commands", presetRow), 1);
        // Progressive: the first groups appear at once, the rest are added one group per idle slice, so the page opens
        // without a pause even on slow machines. A rebuild (another page, a search) bumps the generation and stops the rest.
        var generation = ++_buildGeneration;
        var groups = matching.GroupBy(action => action.Category).ToList();
        void AddGroup(IGrouping<string, HotkeyAction> group)
        {
            var card = GroupCard(group.Key);
            foreach (var action in group) AddCardContent(card, HotkeyRow(action, false));
            _host.AddTopCard(card, 1);
        }
        const int immediate = 2;
        foreach (var group in groups.Take(immediate)) AddGroup(group);
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        void Next(int index)
        {
            if (index >= groups.Count || generation != _buildGeneration) return;
            AddGroup(groups[index]);
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Next(index + 1));
        }
        if (groups.Count > immediate) dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Next(immediate));
        if (matching.Count == 0)
            _host.AddTopCard(InformationCard("No matching shortcuts", "Choose another category to view its keyboard commands."), 1);
    }

    private Border GroupCardWithChildren(string title, FrameworkElement child)
    {
        var card = GroupCard(title);
        AddCardContent(card, child);
        return card;
    }

    internal Border HotkeyRow(HotkeyAction action, bool searchResult)
    {
        var customized = IsHotkeyCustomized(action);
        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement name;
        if (searchResult)
        {
            var nameButton = CreateButton(action.Name, "SearchResultTitleButton", "Open _window command on the Hotkeys page.");
            nameButton.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(nameButton, action.Name);
            nameButton.Click += (_, _) => _host.NavigateToHotkey(action.Id);
            name = nameButton;
        }
        else name = new TextBlock { Text = action.Name, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        labelStack.Children.Add(name);
        labelStack.Children.Add(Note(action.Description, 12, new Thickness(0, 3, 8, 0)));
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        tags.Children.Add(Note(action.Category, 11));
        if (customized) tags.Children.Add(new TextBlock { Text = "CUSTOM", FontSize = Services.ThemeService.MinFontSize, Foreground = Brush("#6DBBFF"), Margin = new Thickness(8, 0, 0, 0) });
        labelStack.Children.Add(tags);

        Button SlotButton(int slot)
        {
            var button = CreateButton(GestureText(action, slot, _host.Settings.Hotkeys), "SecondaryActionButton",
                $"Hotkey {slot}: click, then press the key combination to assign.");
            button.MinWidth = 120;
            button.Height = 34;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            AutomationProperties.SetName(button, $"{action.Name} hotkey {slot}");
            button.Click += (_, _) => BeginRecording(action.Id, slot);
            return button;
        }
        Button ClearButton(int slot)
        {
            var button = CreateButton("Clear", "ResetActionButton", $"Unbind hotkey {slot} of this command.");
            AutomationProperties.SetName(button, $"Clear {action.Name} hotkey {slot}");
            button.Margin = new Thickness(0, 0, 5, 0);
            button.Click += (_, _) =>
            {
                _host.Settings.Hotkeys.Disable(action.Id, slot);
                _host.SettingChanged();
                _host.RebuildPage(focusHotkey: action.Id);
            };
            return button;
        }
        var shortcut = SlotButton(1);
        var shortcut2 = SlotButton(2);
        var feedback = new TextBlock { FontSize = 11, Foreground = Brush("#E08484"), TextWrapping = TextWrapping.Wrap,
            Width = 150, Visibility = Visibility.Collapsed };
        var reassign = CreateButton("Reassign", "ResetActionButton", "Move the captured shortcut from its current command to _window command.");
        reassign.Visibility = Visibility.Collapsed;
        reassign.Click += (_, _) => ResolveHotkeyConflict(action.Id);
        var reset = CreateButton("Reset", "ResetActionButton", "Restore the default keyboard shortcuts (Hotkey 1 and Hotkey 2).");
        reset.Visibility = customized ? Visibility.Visible : Visibility.Collapsed;
        reset.Click += (_, _) =>
        {
            _host.Settings.Hotkeys.Reset(action.Id);
            _host.SettingChanged();
            _host.RebuildPage(focusHotkey: action.Id);
        };

        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        shortcut.Margin = new Thickness(0, 0, 5, 0);
        shortcut2.Margin = new Thickness(0, 0, 5, 0);
        reassign.Margin = new Thickness(0, 0, 5, 0);
        commands.Children.Add(shortcut);
        commands.Children.Add(ClearButton(1));
        commands.Children.Add(shortcut2);
        commands.Children.Add(ClearButton(2));
        commands.Children.Add(reassign);
        commands.Children.Add(reset);
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(commands);
        feedback.Margin = new Thickness(0, 4, 0, 0);
        feedback.HorizontalAlignment = HorizontalAlignment.Right;
        controls.Children.Add(feedback);
        var content = new Grid { MinHeight = 57 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(labelStack);
        Grid.SetColumn(controls, 1);
        content.Children.Add(controls);
        var border = new Border
        {
            Padding = new Thickness(3, 7, 3, 7),
            BorderThickness = new Thickness(1),
            BorderBrush = customized ? Brush("#3A5369") : Brushes.Transparent,
            Child = content
        };
        _host.AddRow(new PageRow(SettingsCatalog.Hotkeys, null, action, border));
        _hotkeyControls.Add(new HotkeyControl(action, shortcut, shortcut2, feedback, reassign, reset));
        return border;
    }

    private static string GestureText(HotkeyAction action, int slot, HotkeySettings? hotkeys = null)
    {
        var gesture = hotkeys is null ? "" : HotkeyCatalog.GestureFor(hotkeys, action.Id, slot);
        return string.IsNullOrWhiteSpace(gesture) ? "Unbound" : HotkeyCatalog.Display(gesture);
    }

    private void BeginRecording(string actionId, int slot)
    {
        var row = _hotkeyControls.FirstOrDefault(control => control.Action.Id.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        CancelRecording();
        _pendingConflict = null;
        _recordingActionId = actionId;
        _recordingSlot = slot;
        row.Slot(slot).Content = "Press a key…";
        row.Feedback.Text = "Press a key combination. Esc cancels.";
        row.Feedback.Visibility = Visibility.Visible;
        row.Reassign.Visibility = Visibility.Collapsed;
        row.Slot(slot).Focus();
    }

    internal void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_recordingActionId is null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelRecording();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None) return;

        var actionId = _recordingActionId;
        var slot = _recordingSlot;
        var gesture = WpfHotkeyGestureAdapter.FromEvent(e);
        _recordingActionId = null;
        var row = _hotkeyControls.FirstOrDefault(control => control.Action.Id == actionId);
        if (row is null) return;
        // A conflict is any other slot of any command in the same context (and the other slot of this command) that already uses the key.
        var conflict = FindConflict(actionId, slot, gesture);
        if (conflict is { } found)
        {
            var (other, otherSlot) = found;
            _pendingConflict = (actionId, slot, gesture, other.Id, otherSlot);
            row.Feedback.Text = other.Id == actionId
                ? $"Already this command's Hotkey {otherSlot}."
                : $"Already used by {other.Name} ({other.Category}), Hotkey {otherSlot}.";
            row.Feedback.Visibility = Visibility.Visible;
            row.Reassign.Visibility = Visibility.Visible;
            row.Slot(slot).Content = "Conflict";
            return;
        }

        AssignHotkey(actionId, slot, gesture, null, 1);
    }

    private (HotkeyAction Action, int Slot)? FindConflict(string actionId, int slot, string gesture)
    {
        var wanted = NormalizeGesture(gesture);
        foreach (var other in HotkeyCatalog.All)
        {
            if (!HotkeyCatalog.SameContext(other.Id, actionId)) continue;
            foreach (var otherSlot in new[] { 1, 2 })
            {
                if (other.Id == actionId && otherSlot == slot) continue;
                var existing = HotkeyCatalog.GestureFor(_host.Settings.Hotkeys, other.Id, otherSlot);
                if (!string.IsNullOrWhiteSpace(existing) &&
                    string.Equals(NormalizeGesture(existing), wanted, StringComparison.OrdinalIgnoreCase))
                    return (other, otherSlot);
            }
        }
        return null;
    }

    private void AssignHotkey(string actionId, int slot, string gesture, HotkeyAction? replaced, int replacedSlot)
    {
        if (replaced is not null) _host.Settings.Hotkeys.Disable(replaced.Id, replacedSlot);
        _host.Settings.Hotkeys.Set(actionId, slot, gesture);
        _pendingConflict = null;
        _host.SettingChanged();
        _host.RebuildPage(focusHotkey: actionId);
        _host.SetStatus(replaced is null
            ? $"Bound {HotkeyCatalog.ById(actionId)?.Name ?? actionId} to {HotkeyCatalog.Display(gesture)}"
            : $"Moved {HotkeyCatalog.Display(gesture)} from {replaced.Name} to {HotkeyCatalog.ById(actionId)?.Name ?? actionId}");
    }

    private void ResolveHotkeyConflict(string actionId)
    {
        if (_pendingConflict is not { } pending || pending.ActionId != actionId) return;
        var conflictingAction = HotkeyCatalog.ById(pending.ConflictingActionId);
        if (conflictingAction is null) return;
        AssignHotkey(actionId, pending.Slot, pending.Gesture, conflictingAction, pending.ConflictingSlot);
    }

    private void CancelRecording()
    {
        if (_recordingActionId is { } actionId && _hotkeyControls.FirstOrDefault(row => row.Action.Id == actionId) is { } row)
        {
            row.Slot(_recordingSlot).Content = GestureText(row.Action, _recordingSlot, _host.Settings.Hotkeys);
            row.Feedback.Text = "";
            row.Feedback.Visibility = Visibility.Collapsed;
        }
        _recordingActionId = null;
    }

    internal void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (_recordingActionId is null) return;
        var clicked = e.OriginalSource as DependencyObject;
        for (var current = clicked; current is not null; current = ParentOf(current))
        {
            if (_hotkeyControls.Any(row => ReferenceEquals(current, row.Gesture) || ReferenceEquals(current, row.Gesture2))) return;
        }
        CancelRecording();
    }

    internal bool IsHotkeyCustomized(HotkeyAction action) => IsSlotCustomized(action, 1) || IsSlotCustomized(action, 2);

    private bool IsSlotCustomized(HotkeyAction action, int slot) => _host.Settings.Hotkeys.IsDisabled(action.Id, slot) ||
        !string.Equals(NormalizeGesture(HotkeyCatalog.GestureFor(_host.Settings.Hotkeys, action.Id, slot)),
            NormalizeGesture(HotkeyCatalog.DefaultFor(action, slot)), StringComparison.OrdinalIgnoreCase);

    internal static string HotkeySearchText(HotkeyAction action)
    {
        lock (HotkeySearchIndex)
        {
            if (!HotkeySearchIndex.TryGetValue(action.Id, out var text))
                HotkeySearchIndex[action.Id] = text =
                    $"hotkey shortcuts key binding commands {action.Name} {action.Category} {action.Description} {action.DefaultGesture} {action.Id}".ToLowerInvariant();
            return text;
        }
    }

    private static string NormalizeGesture(string gesture) => HotkeyCatalog.Canonical(gesture);

    private static DependencyObject? ParentOf(DependencyObject item) => item is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(item)
        : LogicalTreeHelper.GetParent(item);
}

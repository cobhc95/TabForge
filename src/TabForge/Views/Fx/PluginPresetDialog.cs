using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Plugins;

namespace TabForge.Views.Fx;

// Owns: the "Save preset" dialog for one plug-in: an editable list of its saved presets, Delete for a chosen preset,
//       and the replace confirmation. The name match is case-insensitive (Exists).
// Does not own: reading the plug-in state or writing the preset file (the caller does that with the returned name).
// Tests: none named; Exists is the pure name check.
internal static class PluginPresetDialog
{
    /// <summary>True when the text (trimmed) names one of the names, ignoring case.</summary>
    internal static bool Exists(IEnumerable<string> names, string text) =>
        !string.IsNullOrWhiteSpace(text) && names.Any(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Asks for a preset name; returns the trimmed name, or null when cancelled.</summary>
    internal static string? Ask(Window owner, PluginSlot slot)
    {
        var box = new ComboBox { IsEditable = true, Width = 230, Text = "My preset", Margin = new Thickness(0, 0, 6, 0) };
        var delete = new Button { Content = "Delete", Padding = new Thickness(14, 3, 14, 3), ToolTip = "Delete the chosen preset", IsEnabled = false };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(box); row.Children.Add(delete);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = $"Preset name for {slot.Name}:", Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(row); stack.Children.Add(buttons);
        var dialog = new Window { Title = "Save preset", Content = stack, Width = 380, SizeToContent = SizeToContent.Height, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        dialog.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        dialog.SetResourceReference(Window.ForegroundProperty, "TextBrush");

        IReadOnlyList<string> names = Array.Empty<string>();
        void UpdateDelete() => delete.IsEnabled = Exists(names, box.Text);
        void Reload()
        {
            var typed = box.Text;
            names = PluginLibrary.PresetNames(slot);
            box.ItemsSource = names;
            box.Text = typed;
            UpdateDelete();
        }
        box.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateDelete()));
        Reload();

        string? result = null;
        delete.Click += (_, _) =>
        {
            var typed = box.Text.Trim();
            var hit = names.FirstOrDefault(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase));
            if (hit is null || TabForge.Views.ThemedMessageBox.Show(dialog, $"Delete preset \"{hit}\"?", "Save preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            PluginLibrary.DeletePreset(slot, hit);
            Reload();
        };
        ok.Click += (_, _) =>
        {
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            if (Exists(names, text) && TabForge.Views.ThemedMessageBox.Show(dialog, $"Replace preset \"{text}\"?", "Save preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            result = text;
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            if (box.Template.FindName("PART_EditableTextBox", box) is TextBox editor) editor.SelectAll();
        };
        return dialog.ShowDialog() == true ? result : null;
    }
}

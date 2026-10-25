using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Save preset for one plug-in: an editable list of its saved presets, Delete for a chosen preset (after a question), OK and Cancel.
/// OK closes with the trimmed name, asking first when it replaces a saved preset; Cancel and Esc close with null.
/// Owns: the window's controls, layout and questions. Does not own: the texts and the name match (<see cref="PluginPresetNamePresenter"/>)
/// or the preset files (the caller's <c>names</c> and <c>delete</c>, PluginLibrary in the app; the preview passes sample names).
/// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
/// </summary>
public sealed class PluginPresetWindow : Window
{
    private readonly AvaloniaDialogHost _host;
    private readonly Func<IReadOnlyList<string>> _names;
    private readonly ComboBox _box = new() { IsEditable = true, Width = 230, Text = PluginPresetNamePresenter.DefaultName, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _delete = new() { Content = PluginPresetNamePresenter.DeleteButton, Padding = new Thickness(14, 3, 14, 3), IsEnabled = false };
    private IReadOnlyList<string> _shown = Array.Empty<string>();

    public PluginPresetWindow(AvaloniaDialogHost host, string pluginName, Func<IReadOnlyList<string>> names, Action<string> delete)
    {
        _host = host;
        _names = names;
        Title = PluginPresetNamePresenter.Title;
        Width = 380; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        ToolTip.SetTip(_delete, PluginPresetNamePresenter.DeleteTip);
        var ok = new Button { Content = PluginPresetNamePresenter.OkButton, IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = PluginPresetNamePresenter.CancelButton, IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        _box.PropertyChanged += (_, e) => { if (e.Property == ComboBox.TextProperty) UpdateDelete(); };
        _delete.Click += async (_, _) =>
        {
            if (PluginPresetNamePresenter.Match(_shown, _box.Text) is not { } hit || !await Confirm(PresetDeleteQuestionPresenter.TextFor(new[] { hit }))) return;
            delete(hit);
            Reload();
        };
        ok.Click += async (_, _) =>
        {
            var text = (_box.Text ?? "").Trim();
            if (text.Length == 0) return;
            if (PluginPresetNamePresenter.Exists(_shown, text) && !await Confirm(PluginPresetNamePresenter.ReplaceQuestion(text))) return;
            Close(text);
        };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = DialogMetrics.ContentInset,
            Children =
            {
                new TextBlock { Text = PluginPresetNamePresenter.Prompt(pluginName), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { _box, _delete } },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        Reload();
    }

    private void Reload()
    {
        var typed = _box.Text;
        _shown = _names();
        _box.ItemsSource = _shown;
        _box.Text = typed;
        UpdateDelete();
    }

    private void UpdateDelete() => _delete.IsEnabled = PluginPresetNamePresenter.Exists(_shown, _box.Text);

    private async Task<bool> Confirm(string text) =>
        await AvaloniaMessageBox.Show(_host, this, text, PluginPresetNamePresenter.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes;
}

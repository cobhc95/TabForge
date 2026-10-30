using System;
using System.Collections.Generic;
using System.Linq;
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
/// Save preset for one plug-in, or for any other preset kind: an editable list of its saved presets, Delete for a chosen preset (after a question), OK and Cancel.
/// The list shows the user's presets; built-in names (when given) show as "name (built in)" rows that cannot be saved as-is (picking one fills the copy suggestion).
/// An empty or invalid name disables OK with an inline message; a name that replaces a user preset shows an inline note (the plug-in path also asks before replacing).
/// OK closes with the trimmed name, Cancel and Esc close with null.
/// Owns: the window's controls, layout and questions. Does not own: the texts and the name match (<see cref="PluginPresetNamePresenter"/>)
/// or the preset files (the caller's <c>names</c> and <c>delete</c>, PluginLibrary in the app; the preview passes sample names).
/// Tests: ClipSnapPluginDialogTests, SavePresetDialogTests.
/// </summary>
public sealed class PluginPresetWindow : Window
{
    private readonly AvaloniaDialogHost _host;
    private readonly string _caption;
    private readonly Func<IReadOnlyList<string>>? _refresh;
    private readonly IReadOnlyList<string> _builtIns;
    private readonly Func<string, string?>? _validator;
    private readonly Action<string>? _delete;
    private readonly bool _confirmReplace;
    private readonly ComboBox _box = new() { IsEditable = true, Width = 230, Text = PluginPresetNamePresenter.DefaultName, Margin = new Thickness(0, 0, 6, 0), Name = "PromptText" };
    private readonly Button _deleteButton = new() { Content = PluginPresetNamePresenter.DeleteButton, Padding = new Thickness(14, 3, 14, 3), IsEnabled = false, Name = "DeleteButton" };
    private readonly Button _ok = new() { Content = PluginPresetNamePresenter.OkButton, IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0), Name = "OkButton" };
    private readonly Button _cancel = new() { Content = PluginPresetNamePresenter.CancelButton, IsCancel = true, Padding = new Thickness(14, 3, 14, 3), Name = "CancelButton" };
    private readonly TextBlock _note = new() { Name = "NoteText", TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private List<string> _users;

    /// <summary>Save preset for one plug-in: the list is read again after each delete; OK asks before replacing a saved preset.</summary>
    public PluginPresetWindow(AvaloniaDialogHost host, string pluginName, Func<IReadOnlyList<string>> names, Action<string> delete)
        : this(host, PluginPresetNamePresenter.Title, PluginPresetNamePresenter.Prompt(pluginName), PluginPresetNamePresenter.DefaultName,
            names(), null, null, delete, confirmReplace: true, refresh: names)
    {
    }

    /// <summary>Save preset for any preset kind over a snapshot read before opening (never per keystroke).
    /// <paramref name="builtIns"/> shows as un-pickable rows; <paramref name="validator"/> names why a typed name cannot be saved;
    /// <paramref name="delete"/> (null hides Delete) removes a user preset; <paramref name="confirmReplace"/> asks before replacing.</summary>
    public PluginPresetWindow(AvaloniaDialogHost host, string title, string prompt, string initialName,
        IReadOnlyList<string> userNames, IReadOnlyList<string>? builtIns = null,
        Func<string, string?>? validator = null, Action<string>? delete = null,
        bool confirmReplace = false, Func<IReadOnlyList<string>>? refresh = null)
    {
        _host = host;
        _caption = title;
        (_builtIns, _validator, _delete, _confirmReplace, _refresh) = (builtIns ?? Array.Empty<string>(), validator, delete, confirmReplace, refresh);
        _users = userNames.ToList();
        Title = title;
        Width = 380; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        ToolTip.SetTip(_deleteButton, PluginPresetNamePresenter.DeleteTip);
        _ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        _box.PropertyChanged += (_, e) => { if (e.Property == ComboBox.TextProperty) UpdateState(); };
        _box.SelectionChanged += (_, _) =>
        {
            if (PluginPresetNamePresenter.BuiltInOfRow(_box.SelectedItem as string) is { } builtIn)
            {
                _box.SelectedItem = null;
                _box.Text = PluginPresetNamePresenter.CopySuggestion(builtIn, _users);
            }
            UpdateState();
        };
        _deleteButton.Click += async (_, _) =>
        {
            if (_delete is null || PluginPresetNamePresenter.Match(_users, _box.Text) is not { } hit || !await Confirm(PresetDeleteQuestionPresenter.TextFor(new[] { hit }))) return;
            _delete(hit);
            Reload();
        };
        _ok.Click += async (_, _) =>
        {
            var text = (_box.Text ?? "").Trim();
            if (text.Length == 0 || Validate(text) is not null) return;
            if (_confirmReplace && PluginPresetNamePresenter.Exists(_users, text) && !await Confirm(PluginPresetNamePresenter.ReplaceQuestion(text))) return;
            Close(text);
        };
        _cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = DialogMetrics.ContentInset,
            Children =
            {
                new TextBlock { Name = "PromptLabel", Text = prompt, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { _box, _deleteButton } },
                _note,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _ok, _cancel } },
            },
        };
        _box.Text = initialName;
        Reload();
    }

    /// <summary>Why the trimmed name cannot be saved: the caller's check, else the built-in guard; null when it can.</summary>
    internal string? Validate(string text) =>
        _validator is not null ? _validator(text)
        : PluginPresetNamePresenter.BuiltInMatch(_builtIns, text) is not null ? PluginPresetNamePresenter.BuiltInError : null;

    private void Reload()
    {
        var typed = _box.Text;
        if (_refresh is not null) _users = _refresh().ToList();
        _box.ItemsSource = _builtIns.Select(PluginPresetNamePresenter.DisplayBuiltIn).Concat(_users).ToList();
        _box.Text = typed;
        UpdateState();
    }

    private void UpdateState()
    {
        var text = (_box.Text ?? "").Trim();
        var error = text.Length == 0 ? null : Validate(text);
        _ok.IsEnabled = text.Length > 0 && error is null;
        _deleteButton.IsVisible = _delete is not null;
        _deleteButton.IsEnabled = _delete is not null && PluginPresetNamePresenter.Exists(_users, _box.Text);
        ShowNote(error ?? (PluginPresetNamePresenter.Match(_users, text) is { } hit ? PluginPresetNamePresenter.ReplaceNote(hit) : ""), error is not null);
    }

    private void ShowNote(string text, bool error)
    {
        _note.Text = text;
        _note.IsVisible = text.Length > 0;
        if (text.Length > 0 && this.FindResource(error ? "Danger" : "TextMuted") is IBrush brush) _note.Foreground = brush;
    }

    private async Task<bool> Confirm(string text) =>
        await AvaloniaMessageBox.Show(_host, this, text, _caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes;
}

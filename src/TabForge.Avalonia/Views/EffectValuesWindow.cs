using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>A preset of a value-based effect: its name, whether it is built in, and its values by key.</summary>
public sealed record EffectValuesPreset(string Name, bool BuiltIn, IReadOnlyDictionary<string, double> Values);

/// <summary>The dialog's answer, as the WPF editor frame (ThemedEditorDialog) gives it.</summary>
public enum EffectValuesAnswer { Ok, Clean, Cancel }

/// <summary>
/// The dialog of a value-based effect (trill, grace note, harmonic), the twin of the WPF ValuesEffectDialog
/// in its ThemedEditorDialog frame: the editor's fields, the preset list, Save as preset, Delete preset, then OK, Clean and Cancel.
/// Owns: the frame, the preset list and its buttons. Does not own: the fields (OrnamentEditorWindows), the preset row label and
/// the Delete rule (EffectValuesPresenter), or storing presets (the caller's save callback and PresetDeleted).
/// Tests: TabForge.Avalonia.Tests.MixTableColourChooserEffectValuesTests.
/// </summary>
public sealed class EffectValuesWindow : Window
{
    private readonly List<EffectValuesPreset> _presets;
    private readonly Func<Dictionary<string, double>> _read;
    private readonly Action<IReadOnlyDictionary<string, double>> _write;
    private readonly Func<IReadOnlyDictionary<string, double>, EffectValuesPreset?> _save;
    private readonly ListBox _list = new() { Width = 190, MinHeight = 120, MaxHeight = 220, Name = "PresetList" };
    private readonly Button _delete = new() { Content = "Delete preset", Margin = new Thickness(0, 6, 0, 0), Name = "DeletePresetButton" };
    private readonly TextBlock _status = new() { Name = "StatusText", TextWrapping = TextWrapping.Wrap, MaxWidth = 190, Margin = new Thickness(0, 6, 0, 0) };

    /// <param name="save">Keeps the current values as a preset of the person's own; returns it, or null when it cannot be saved.</param>
    public EffectValuesWindow(string title, Control fields, Func<Dictionary<string, double>> read, Action<IReadOnlyDictionary<string, double>> write,
        IEnumerable<EffectValuesPreset> presets, bool canClean, Func<IReadOnlyDictionary<string, double>, EffectValuesPreset?> save)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _read = read; _write = write; _save = save; _presets = presets.ToList();
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var heading = new TextBlock { Text = "Presets", Margin = new Thickness(0, 0, 0, 4) };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        AutomationProperties.SetName(_list, "Presets");
        var saveButton = new Button { Content = "Save as preset…", Margin = new Thickness(0, 10, 0, 0), Name = "SavePresetButton" };
        ToolTip.SetTip(saveButton, "Keep these settings as a preset of your own");
        ToolTip.SetTip(_delete, "Delete the selected preset of your own");
        saveButton.Click += (_, _) => SaveAsPreset();
        _delete.Click += (_, _) => DeleteSelected();
        _list.SelectionChanged += (_, _) => { if (Selected is { } p) _write(p.Values); UpdateDelete(); };
        var side = new StackPanel { Margin = new Thickness(18, 0, 0, 0), Children = { heading, _list, saveButton, _delete, _status } };
        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), Children = { fields, side } };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 10, 14, 12) };
        actions.Children.Add(AnswerButton("OK", EffectValuesAnswer.Ok, "Apply the effect  (Enter)"));
        var clean = AnswerButton("Clean", EffectValuesAnswer.Clean, "Remove the effect from the note");
        clean.IsEnabled = canClean;
        actions.Children.Add(clean);
        actions.Children.Add(AnswerButton("Cancel", EffectValuesAnswer.Cancel, "Close without changing anything  (Esc)"));
        Content = new StackPanel { Children = { body, actions } };
        // Perf: the preset list is virtualized (ItemsSource + recycling template over a VirtualizingStackPanel).
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<EffectValuesPreset>((preset, _) => PresetRow(preset), supportsRecycling: true);
        FillList(null);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Answer(EffectValuesAnswer.Cancel);
            else if (e.Key == Key.Enter && e.Source is not (Button or TextBox)) Answer(EffectValuesAnswer.Ok);   // a focused button keeps its own Enter
            else return;
            e.Handled = true;
        };
    }

    public EffectValuesAnswer Answered { get; private set; } = EffectValuesAnswer.Cancel;

    /// <summary>The editor's current settings (the keys of its preset values).</summary>
    public Dictionary<string, double> Values => _read();

    /// <summary>The preset list rows as shown.</summary>
    public IReadOnlyList<string> PresetRows => _presets.Select(preset => EffectValuesPresenter.PresetLabel(preset.Name, preset.BuiltIn)).ToList();

    private EffectValuesPreset? Selected => _list.SelectedItem as EffectValuesPreset;

    private void FillList(string? select)
    {
        _list.ItemsSource = _presets.ToList();
        _list.SelectedItem = _presets.FirstOrDefault(preset => preset.Name == select);
        UpdateDelete();
    }

    /// <summary>The row content the virtualized template builds for a visible preset.</summary>
    private static Control PresetRow(EffectValuesPreset? preset) =>
        new TextBlock { Text = preset is null ? "" : EffectValuesPresenter.PresetLabel(preset.Name, preset.BuiltIn) };

    private void UpdateDelete() => _delete.IsEnabled = EffectValuesPresenter.CanDelete(Selected is not null, Selected?.BuiltIn == true);

    private void SaveAsPreset()
    {
        var saved = _save(_read());
        _status.Text = saved is null ? EffectValuesPresenter.SaveFailedMessage : "";
        if (saved is null) return;
        _presets.RemoveAll(p => p.Name == saved.Name);
        _presets.Add(saved);
        FillList(saved.Name);
    }

    /// <summary>Raised when Delete preset removes one of the person's own presets, so the caller can remove it from storage.</summary>
    public event Action<EffectValuesPreset>? PresetDeleted;

    private void DeleteSelected()
    {
        if (Selected is not { BuiltIn: false } p) return;
        _presets.Remove(p);
        PresetDeleted?.Invoke(p);
        FillList(null);
    }

    private void Answer(EffectValuesAnswer answer)
    {
        Answered = answer;
        Close(answer);
    }

    private Button AnswerButton(string text, EffectValuesAnswer answer, string tip)
    {
        var button = new Button { Content = text, MinWidth = DialogMetrics.PromptButtonMinWidth, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0), Name = text + "Button" };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => Answer(answer);
        return button;
    }
}

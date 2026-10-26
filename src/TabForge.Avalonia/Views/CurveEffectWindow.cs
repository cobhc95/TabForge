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
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Views;

/// <summary>The button the curve dialog closed with: OK applies the curve, Clean removes the effect, Cancel changes nothing.</summary>
public enum CurveEffectAnswer { Cancel, Ok, Clean }

/// <summary>
/// The bend and tremolo-bar editor dialog in its ThemedEditorDialog frame. The curve editor on the
/// left; the preset list, Save as preset and Delete preset on the right; then OK (the default), Clean and Cancel.
/// Owns: the dialog's controls, the preset list and its buttons, and the answer it closes with. Does not own: the point rules (EffectCurve),
/// the preset storage (EffectPresetStore), or the note write (EffectCurveFlow).
/// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// </summary>
public sealed class CurveEffectWindow : Window
{
    private const double PresetListWidth = 190, PresetListMinHeight = 150, PresetListMaxHeight = 220;
    private const double SideGap = 16, SideTopGap = 10, ButtonGap = 6, ButtonMinWidth = DialogMetrics.PromptButtonMinWidth, FrameMargin = 16;
    // The frame of the ThemedEditorDialog: the title strip, the body and the footer, inside a bordered rounded panel shown with a margin.
    private const double TitleHeight = 34, TitleFontSize = 12, TitleLeftPadding = 14, FrameCorner = 8, FrameBorder = 1, FrameOuterMargin = 12;
    private const double FooterSide = 14, FooterTop = 10, FooterBottom = 12;
    private const string SavePromptTitle = "Save as preset", SavePromptLabel = "Preset name:", SavePromptDefault = "My preset";
    private const string MineSuffix = "  (mine)";

    private readonly EffectEditorKind _kind;
    private readonly EffectPresetStore _presets;
    private readonly Action _presetsChanged;
    private readonly ListBox _list = new() { Width = PresetListWidth, MinHeight = PresetListMinHeight, MaxHeight = PresetListMaxHeight, Name = "PresetList" };
    private readonly Button _delete = new() { Content = "Delete preset", Name = "DeletePresetButton", Margin = new Thickness(0, ButtonGap, 0, 0) };
    // The entries behind the virtualized preset list's ItemsSource; PresetRows reads these, never the containers.
    private IReadOnlyList<EffectPresetEntry> _entries = Array.Empty<EffectPresetEntry>();

    /// <param name="presetsChanged">Called after a preset is saved or deleted, so the settings file is written.</param>
    public CurveEffectWindow(EffectEditorKind kind, string title, double minValue, double maxValue, IReadOnlyList<BendPointModel> start,
        EffectPresetStore presets, bool canClean, Action presetsChanged)
    {
        _kind = kind;
        _presets = presets;
        _presetsChanged = presetsChanged;
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        Curve = new EffectCurve(minValue, maxValue);
        Curve.SetPoints(start);
        Editor = new CurveEditorView(Curve);

        var heading = new TextBlock { Text = "Presets", Margin = new Thickness(0, 0, 0, 4) };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        AutomationProperties.SetName(_list, "Presets");
        var save = new Button { Content = "Save as preset…", Margin = new Thickness(0, SideTopGap, 0, 0), Name = "SavePresetButton" };
        ToolTip.SetTip(save, "Keep this curve as a preset of your own");
        ToolTip.SetTip(_delete, "Delete the selected preset of your own");
        save.Click += async (_, _) => await SaveAsPreset();
        _delete.Click += (_, _) => DeleteSelected();
        _list.SelectionChanged += (_, _) => { if (Selected is { } p) PickPreset(p); UpdateDelete(); };
        var side = new StackPanel { Margin = new Thickness(SideGap, 0, 0, 0), Children = { heading, _list, save, _delete } };
        var titleText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = TitleFontSize, Margin = new Thickness(TitleLeftPadding, 0, 0, 0) };
        titleText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush");
        var titleBar = new Border { Height = TitleHeight, Child = titleText, CornerRadius = new CornerRadius(FrameCorner, FrameCorner, 0, 0) };
        titleBar[!Border.BackgroundProperty] = new DynamicResourceExtension("ChromeStripBrush");
        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(FrameMargin), Children = { Editor, side } };

        var ok = new Button { Content = "OK", Name = "OkButton", MinWidth = ButtonMinWidth, IsDefault = true };
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ok[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        ok[!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");
        ok.Click += (_, _) => Finish(CurveEffectAnswer.Ok);
        var clean = new Button { Content = "Clean", Name = "CleanButton", MinWidth = ButtonMinWidth, IsEnabled = canClean };
        clean.Click += (_, _) => Finish(CurveEffectAnswer.Clean);
        var cancel = new Button { Content = "Cancel", Name = "CancelButton", MinWidth = ButtonMinWidth, IsCancel = true };
        cancel.Click += (_, _) => Finish(CurveEffectAnswer.Cancel);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = ButtonGap,
            Margin = new Thickness(FooterSide, FooterTop, FooterSide, FooterBottom), Children = { ok, clean, cancel },
        };
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, FrameCorner, FrameCorner), Child = actions };
        footer[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        footer[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        // The frame of the ThemedEditorDialog: a title strip, the body and a footer, inside a rounded panel with a border.
        var frame = new StackPanel { Children = { titleBar, body, footer } };
        var panel = new Border
        {
            BorderThickness = new Thickness(FrameBorder), CornerRadius = new CornerRadius(FrameCorner), ClipToBounds = true,
            Margin = new Thickness(FrameOuterMargin), Child = frame,
        };
        panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        panel[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        Content = panel;
        // Perf: the preset list is virtualized (ItemsSource + recycling template over a VirtualizingStackPanel).
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<EffectPresetEntry>((entry, _) => PresetRow(entry, entry is not null && IsBuiltIn(entry)), supportsRecycling: true);
        FillList(null);
    }

    /// <summary>The curve being edited (its points are the dialog's answer when OK is chosen).</summary>
    public EffectCurve Curve { get; }

    /// <summary>The curve editor (the drawing and pointer input).</summary>
    public CurveEditorView Editor { get; }

    /// <summary>The answer the dialog closed with (Cancel until a button is chosen).</summary>
    public CurveEffectAnswer Answered { get; private set; } = CurveEffectAnswer.Cancel;

    /// <summary>The preset list rows as shown: the name, with "(mine)" on the person's own presets.</summary>
    public IReadOnlyList<string> PresetRows => _entries.Select(entry => entry.Name + (IsBuiltIn(entry) ? "" : MineSuffix)).ToList();

    private EffectPresetEntry? Selected => _list.SelectedItem as EffectPresetEntry;

    private bool IsBuiltIn(EffectPresetEntry preset) => EffectPresetStore.BuiltIn(_kind).Contains(preset);

    /// <summary>Fills the curve from a preset, as picking it in the list does.</summary>
    public void PickPreset(EffectPresetEntry preset)
    {
        Curve.SetPoints(preset.Points);
        Editor.Refresh();
    }

    private void FillList(string? select)
    {
        _entries = _presets.List(_kind).Select(row => row.Preset).ToList();
        _list.ItemsSource = _entries;
        _list.SelectedItem = select is null ? null : _entries.FirstOrDefault(entry => entry.Name == select);
        UpdateDelete();
    }

    /// <summary>The row content the virtualized template builds for a visible preset.</summary>
    private static Control PresetRow(EffectPresetEntry? entry, bool builtIn) =>
        new TextBlock { Text = entry is null ? "" : entry.Name + (builtIn ? "" : MineSuffix) };

    private void UpdateDelete() => _delete.IsEnabled = Selected is { } p && !IsBuiltIn(p);

    private async System.Threading.Tasks.Task SaveAsPreset()
    {
        var name = await DialogLauncher.Ask<TextPromptWindow, string?>(this, () => new TextPromptWindow(SavePromptTitle, SavePromptLabel, SavePromptDefault));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_presets.Save(_kind, name, Curve.Snapshot())) return;
        _presetsChanged();
        FillList(name.Trim());
    }

    private void DeleteSelected()
    {
        if (Selected is not { } p || IsBuiltIn(p)) return;
        _presets.Delete(_kind, p.Name);
        _presetsChanged();
        FillList(null);
    }

    private void Finish(CurveEffectAnswer answer)
    {
        Answered = answer;
        Close(answer);
    }
}

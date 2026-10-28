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

/// <summary>The button the curve dialog closed with: OK applies the curve, Clean (the Remove button) removes the effect, Cancel changes nothing.</summary>
public enum CurveEffectAnswer { Cancel, Ok, Clean }

/// <summary>
/// The bend and tremolo-bar editor dialog in its ThemedEditorDialog frame. The curve editor on the
/// left; the type list and the person's presets (CurveSidePanel) on the right; then OK (the default), Remove, Reset and Cancel.
/// Owns: the dialog's controls and the answer it closes with. Does not own: the point rules (EffectCurve), the type list and presets (CurveSidePanel),
/// the preset storage (EffectPresetStore), or the note write (EffectCurveFlow).
/// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// </summary>
public sealed class CurveEffectWindow : Window
{
    private const double ButtonGap = 6, ButtonMinWidth = DialogMetrics.PromptButtonMinWidth, FrameMargin = 16;
    // The frame of the ThemedEditorDialog: the title strip, the body and the footer, inside a bordered rounded panel shown with a margin.
    private const double TitleHeight = 34, TitleFontSize = 12, TitleLeftPadding = 14, FrameCorner = 8, FrameBorder = 1, FrameOuterMargin = 12;
    private const double FooterSide = 14, FooterTop = 10, FooterBottom = 12;

    private readonly IReadOnlyList<BendPointModel> _start;

    /// <param name="presetsChanged">Called after a preset is saved or deleted, so the settings file is written.</param>
    /// <param name="previous">The level the note (or beat) before ended on, 0 when it has no bend: the types that start from it are greyed at 0.</param>
    public CurveEffectWindow(EffectEditorKind kind, string title, double minValue, double maxValue, IReadOnlyList<BendPointModel> start,
        EffectPresetStore presets, bool canClean, Action presetsChanged, double previous = 0)
    {
        _start = start.Select(p => p.Clone()).ToList();
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        Curve = new EffectCurve(minValue, maxValue);
        Curve.SetPoints(start);
        Editor = new CurveEditorView(Curve, kind == EffectEditorKind.Bend);
        Side = new CurveSidePanel(kind, presets, previous, this, Curve.Snapshot, SetCurve, presetsChanged);
        var titleText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = TitleFontSize, Margin = new Thickness(TitleLeftPadding, 0, 0, 0) };
        titleText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush");
        var titleBar = new Border { Height = TitleHeight, Child = titleText, CornerRadius = new CornerRadius(FrameCorner, FrameCorner, 0, 0) };
        titleBar[!Border.BackgroundProperty] = new DynamicResourceExtension("ChromeStripBrush");
        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(FrameMargin), Children = { Editor, Side } };

        var ok = new Button { Content = "OK", Name = "OkButton", MinWidth = ButtonMinWidth, IsDefault = true };
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ok[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        ok[!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");
        ok.Click += (_, _) => Finish(CurveEffectAnswer.Ok);
        var remove = new Button { Content = "Remove", Name = "RemoveButton", MinWidth = ButtonMinWidth, IsEnabled = canClean };
        remove.Click += (_, _) => Finish(CurveEffectAnswer.Clean);
        var reset = new Button { Content = "Reset", Name = "ResetButton", MinWidth = ButtonMinWidth };
        ToolTip.SetTip(reset, "Put back the curve the editor opened with");
        reset.Click += (_, _) => Reset();
        var cancel = new Button { Content = "Cancel", Name = "CancelButton", MinWidth = ButtonMinWidth, IsCancel = true };
        cancel.Click += (_, _) => Finish(CurveEffectAnswer.Cancel);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = ButtonGap,
            Margin = new Thickness(FooterSide, FooterTop, FooterSide, FooterBottom), Children = { ok, remove, reset, cancel },
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
    }

    /// <summary>The curve being edited (its points are the dialog's answer when OK is chosen).</summary>
    public EffectCurve Curve { get; }

    /// <summary>The curve editor (the drawing and pointer input).</summary>
    public CurveEditorView Editor { get; }

    /// <summary>The answer the dialog closed with (Cancel until a button is chosen).</summary>
    public CurveEffectAnswer Answered { get; private set; } = CurveEffectAnswer.Cancel;

    /// <summary>The type list and the person's presets beside the editor.</summary>
    internal CurveSidePanel Side { get; }

    /// <summary>Sets the curve to <paramref name="points"/> (a chosen type or preset) and redraws.</summary>
    public void SetCurve(IReadOnlyList<BendPointModel> points)
    {
        Curve.SetPoints(points);
        Editor.Refresh();
    }

    /// <summary>Puts back the curve the editor opened with.</summary>
    public void Reset() => SetCurve(_start);

    private void Finish(CurveEffectAnswer answer)
    {
        Answered = answer;
        Close(answer);
    }
}

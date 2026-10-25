using TabForge.Avalonia.Platform;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Paste Special, one window for every clipboard: for a score clip the repeat count, placement mode, octave shift, "keep string and fret" and (for bars) "copy bar settings";
/// for a copied timeline range the repeat count, mode (overwrite / merge / insert), what to paste, what an overlapped clip does and "remember". Then Paste / Cancel.
/// Closing returns the PasteSpecialOptions, or null when cancelled.
/// Owns: the window controls and keeping them in step with the presenter. Does not own: the choices, limits or words (PasteSpecialPresenter) or the paste.
/// Deviation: the window uses the platform title bar; the WPF window draws its own.
/// Tests: TabForge.Avalonia.Tests.PasteOptionsSpecialAndDeleteBarsTests, TimelinePasteTests.
/// </summary>
public sealed class PasteSpecialWindow : Window
{
    private readonly PasteSpecialPresenter _presenter;
    private readonly TextBox _repeat;
    private readonly RadioButton[] _modes;
    private readonly RadioButton[] _whats = System.Array.Empty<RadioButton>();
    private readonly RadioButton[] _overlaps = System.Array.Empty<RadioButton>();
    private readonly CheckBox? _remember;
    private readonly TextBlock _octaveText;
    private readonly Button _octaveDown;
    private readonly Button _octaveUp;
    private readonly CheckBox _keepFingering;
    private readonly CheckBox? _barSettings;

    public PasteSpecialWindow(bool bars) : this(new PasteSpecialPresenter(bars)) { }

    /// <summary>The window for a copied timeline range, starting from <paramref name="request"/>.</summary>
    public PasteSpecialWindow(RangePasteRequest request) : this(new PasteSpecialPresenter(request)) { }

    private PasteSpecialWindow(PasteSpecialPresenter presenter)
    {
        _presenter = presenter;
        Title = PasteSpecialPresenter.Title;
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var body = new StackPanel { Margin = new Thickness(20, 14), Spacing = 4 };
        body.Children.Add(Heading("Repeat"));
        _repeat = new TextBox { Name = "RepeatBox", Text = _presenter.Repeat.ToString(), Width = 52, MaxLength = 2, TextAlignment = TextAlignment.Center };
        _repeat.AddHandler(TextInputEvent, (_, e) => e.Handled = !PasteSpecialPresenter.IsDigits(e.Text ?? ""), RoutingStrategies.Tunnel);
        _repeat.LostFocus += (_, _) => { _presenter.SetRepeatText(_repeat.Text ?? ""); _repeat.Text = _presenter.Repeat.ToString(); };
        var hint = new TextBlock { Text = _presenter.RepeatHintText, VerticalAlignment = VerticalAlignment.Center };
        hint[!ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        body.Children.Add(Row(Step("RepeatDown", "−", "Fewer copies", () => StepRepeat(-1)), _repeat, Step("RepeatUp", "+", "More copies", () => StepRepeat(1)), hint));

        body.Children.Add(Heading(_presenter.ModeHeading));
        _modes = Radios(body, _presenter.ModeLabels, "Mode", "paste-special-mode", _presenter.Mode);
        if (_presenter.IsRange)
        {
            foreach (var mode in _modes) mode.IsCheckedChanged += (_, _) => ShowOverlapEnabled();
            if (_presenter.ShowWhat)
            {
                body.Children.Add(Heading("Paste"));
                _whats = Radios(body, PasteSpecialPresenter.WhatLabels, "What", "paste-special-what", _presenter.What);
            }
            if (_presenter.ShowOverlap)
            {
                body.Children.Add(Heading("Clips that overlap"));
                _overlaps = Radios(body, PasteSpecialPresenter.OverlapLabels, "Overlap", "paste-special-overlap", _presenter.Overlap);
                ShowOverlapEnabled();
            }
            _remember = new CheckBox { Name = "RememberBox", Content = PasteSpecialPresenter.RememberText, IsChecked = _presenter.Remember }.WithMargin(DialogMetrics.FooterGap);
            body.Children.Add(_remember);
        }

        _octaveText = new TextBlock { Name = "OctaveText", Width = 96, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _octaveDown = Step("OctaveDown", "−", "Down an octave", () => SetOctave(_presenter.Octave - 1));
        _octaveUp = Step("OctaveUp", "+", "Up an octave", () => SetOctave(_presenter.Octave + 1));
        _keepFingering = new CheckBox { Name = "KeepFingeringBox", Content = PasteSpecialPresenter.KeepFingeringText, Margin = new Thickness(6, 4, 0, 0) };
        ToolTip.SetTip(_keepFingering, PasteSpecialPresenter.KeepFingeringToolTip);
        _keepFingering.IsCheckedChanged += (_, _) => SetOctave(_presenter.Octave);
        if (_presenter.ShowNoteOptions)
        {
            body.Children.Add(Heading("Notes"));
            body.Children.Add(Row(new TextBlock { Text = "Octave shift", VerticalAlignment = VerticalAlignment.Center }, _octaveDown, _octaveText, _octaveUp));
            body.Children.Add(_keepFingering);
        }
        if (_presenter.HasBarSettings)
        {
            _barSettings = new CheckBox { Name = "BarSettingsBox", Content = PasteSpecialPresenter.BarSettingsText, IsChecked = true, Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(_barSettings, PasteSpecialPresenter.BarSettingsToolTip);
            body.Children.Add(_barSettings);
        }
        SetOctave(0);

        var paste = new Button { Name = "PasteButton", Content = "Paste", MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
        paste.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        paste.Click += (_, _) => Close(Current());
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", MinWidth = DialogMetrics.PromptButtonMinWidth, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = DialogMetrics.FooterGap, Children = { paste, cancel } });
        Content = body;
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(null); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    /// <summary>The choices as the controls stand now.</summary>
    public PasteSpecialOptions Current()
    {
        _presenter.SetRepeatText(_repeat.Text ?? "");
        _presenter.Mode = System.Math.Max(0, System.Array.FindIndex(_modes, r => r.IsChecked == true));
        if (_whats.Length > 0) _presenter.What = System.Math.Max(0, System.Array.FindIndex(_whats, r => r.IsChecked == true));
        if (_overlaps.Length > 0) _presenter.Overlap = System.Math.Max(0, System.Array.FindIndex(_overlaps, r => r.IsChecked == true));
        _presenter.Remember = _remember?.IsChecked == true;
        _presenter.KeepStringAndFret = _keepFingering.IsChecked == true;
        _presenter.CopyBarSettings = _barSettings?.IsChecked != false;
        return _presenter.Result();
    }

    // Merge never overlaps a clip, so the overlap choice is greyed out while Merge is picked.
    private void ShowOverlapEnabled()
    {
        var merge = _modes.Length > (int)RangePasteMode.Merge && _modes[(int)RangePasteMode.Merge].IsChecked == true;
        foreach (var overlap in _overlaps) overlap.IsEnabled = !merge;
    }

    private static RadioButton[] Radios(StackPanel body, System.Collections.Generic.IReadOnlyList<string> labels, string name, string group, int selected)
    {
        var radios = labels.Select((label, i) => new RadioButton
        {
            Name = name + i, Content = label, GroupName = group, IsChecked = i == selected, Margin = new Thickness(6, 0, 0, 0)
        }).ToArray();
        foreach (var radio in radios) body.Children.Add(radio);
        return radios;
    }

    private void StepRepeat(int by)
    {
        _presenter.SetRepeatText(_repeat.Text ?? "");
        _presenter.StepRepeat(by);
        _repeat.Text = _presenter.Repeat.ToString();
    }

    private void SetOctave(int value)
    {
        _presenter.KeepStringAndFret = _keepFingering.IsChecked == true;
        _presenter.SetOctave(value);
        _octaveText.Text = _presenter.OctaveText;
        _octaveDown.IsEnabled = _presenter.OctaveDownEnabled;
        _octaveUp.IsEnabled = _presenter.OctaveUpEnabled;
        _octaveText[!ForegroundProperty] = new DynamicResourceExtension(_presenter.OctaveOn ? "TextPrimary" : "TextMuted");
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2) };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6, 0, 0, 0) };
        foreach (var child in children)
        {
            child.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }
        return row;
    }

    private static Button Step(string name, string glyph, string tip, System.Action click)
    {
        var button = new Button { Name = name, Content = glyph, Width = 28, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }
}

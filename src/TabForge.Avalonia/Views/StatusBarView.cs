using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The status bar under the workspace: the status text, the MIDI activity dot, the position, bar duration and master readouts, the import
/// Cancel button, the timeline slot, and the audio device button or the MIDI output name, laid out as the app's footer.
/// Owns: the row, its columns and dividers, its sizes and readout colours, and showing the values it is given. Does not own: what the texts
/// say (StatusTextPresenter, AudioDeviceStatusPresenter, MidiOutputStatusPresenter, ImportStatusPresenter), the values (MainWindowStatusBar
/// reads them), or the notice row under it (StatusNoticeBarView).
/// Tests: TabForge.Avalonia.Tests.StatusBarViewTests.
/// </summary>
public sealed class StatusBarView : UserControl
{
    /// <summary>The status text before any action has set it.</summary>
    public const string ReadyText = "Ready";

    // The timeline slot in the middle: the arrangement's footer scroll bar sits here while its pane is open.
    private StatusTextReadout _shownReadout;
    private string? _shownOutput;
    private readonly ContentControl _timelineSlot = new() { Name = "TimelineScrollSlot", VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    private const double BarHeight = 28;
    private const double StatusFontSize = 12;
    private const double MidiFontSize = 13;
    private const double ReadoutFontSize = 13.8;
    private const double AudioDeviceFontSize = 11;
    private const double MidiDotSize = 9;
    private const double PositionMaxWidth = 365;
    private const double BarStateMaxWidth = 110;
    private const double MasterMaxWidth = 240;
    private const double OutputMaxWidth = 185;
    private const double AudioDeviceMaxWidth = 300;
    // The audio interface button: the WPF StatusLinkButton padding (6, 2), its 3 DIP corner and the gap from the MIDI readout on its left.
    private const double AudioDevicePaddingX = 6, AudioDevicePaddingY = 2, AudioDeviceCornerRadius = 3, AudioDeviceLeftGap = 4;
    private const string PositionTooltip = "Playing position (or edit cursor): measure · track · string · cell";
    private const string MasterTooltip = "Tempo · key signature · current note value";
    private const string MidiDotTooltip = "MIDI output activity: grey = idle, green = playing";
    private const string OutputTooltip = "MIDI output device for the selected track";
    private const string AudioInterfaceTooltip = "Audio interface settings";

    private readonly TextBlock _status = Text(StatusFontSize, "TextPrimary");
    private readonly TextBlock _position = Text(StatusFontSize, "TextPrimary", PositionMaxWidth);
    private readonly TextBlock _barState = Text(ReadoutFontSize, "LegibleBrush", BarStateMaxWidth);
    private readonly TextBlock _master = Text(ReadoutFontSize, "LegibleBrush", MasterMaxWidth);
    private readonly TextBlock _output = Text(StatusFontSize, "TextPrimary", OutputMaxWidth);
    private readonly TextBlock _audioDeviceText = Text(AudioDeviceFontSize, "TextPrimary", AudioDeviceMaxWidth);
    private readonly Button _cancelImport;
    private readonly Button _audioDevice;
    private AudioDeviceTip? _audioTip;   // the facts the audio button's tooltip shows now
    private bool _outputWanted;  // the song sends MIDI; the output name then shows unless the audio interface button takes its place

    public StatusBarView()
    {
        Name = "StatusBar";
        Height = BarHeight;
        DockPanel.SetDock(this, Dock.Bottom);
        AutomationProperties.SetName(this, "Status bar");
        _status.Name = "StatusText";
        _position.Name = "StatusPosition";
        _barState.Name = "StatusBarState";
        _master.Name = "StatusMaster";
        _output.Name = "StatusOutput";
        _audioDeviceText.Name = "AudioDeviceText";
        _cancelImport = new Button
        {
            Name = "CancelImport", Content = "Cancel", IsVisible = false, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_cancelImport, "Cancel import");
        _cancelImport.Click += (_, _) => CancelImportRequested?.Invoke();
        _audioDevice = AudioDeviceButton();
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _status, _cancelImport } };
        var midi = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { MidiDot(), Text(MidiFontSize, "TextPrimary", margin: new Thickness(5, 0, 0, 0), text: "MIDI") } };
        ToolTip.SetTip(midi, MidiDotTooltip);
        ToolTip.SetTip(_position, PositionTooltip);
        ToolTip.SetTip(_master, MasterTooltip);
        ToolTip.SetTip(_output, OutputTooltip);
        _output.Margin = new Thickness(8, 0, 5, 0);
        _output.Text = MidiOutputStatusPresenter.UnknownOutputText;
        var output = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _audioDevice, _output } };

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,*,Auto") };
        grid.Children.Add(Cell(status, new Thickness(5, 0, 9, 0), divider: true, column: 0));
        grid.Children.Add(Cell(midi, new Thickness(8, 0), divider: true, column: 1));
        grid.Children.Add(Cell(_position, new Thickness(8, 0), divider: true, column: 2));
        grid.Children.Add(Cell(_barState, new Thickness(8, 0), divider: true, column: 3));
        grid.Children.Add(Cell(_master, new Thickness(8, 0), divider: true, column: 4));
        grid.Children.Add(Cell(_timelineSlot, default, divider: false, column: 5, margin: new Thickness(8, 0)));
        grid.Children.Add(Cell(output, new Thickness(0), divider: false, column: 6));

        var bar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(5, 0), Child = grid };
        bar[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        bar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        Content = bar;
        ShowStatus(ReadyText, ImportStatusPresenter.IdleTooltip);
    }

    /// <summary>Puts the timeline's footer scroll bar in the middle slot, or clears it (null) when its pane closes.</summary>
    public void SetTimelineScrollBar(Control? bar) => _timelineSlot.Content = bar;

    /// <summary>The device button was clicked: the caller opens the audio settings.</summary>
    public event Action? AudioDeviceRequested;

    /// <summary>The Cancel button of a running import was clicked.</summary>
    public event Action? CancelImportRequested;

    /// <summary>Shows the status text and its tooltip.</summary>
    public void ShowStatus(string text, string tooltip)
    {
        _status.Text = text;
        ToolTip.SetTip(_status, tooltip);
    }

    /// <summary>Shows the position, bar duration and master readouts, and the MIDI output name when <paramref name="showOutput"/> (the song sends MIDI).</summary>
    public void ShowReadout(StatusTextReadout readout, string outputText, bool showOutput)
    {
        if (readout == _shownReadout && outputText == _shownOutput && showOutput == _outputWanted) return;   // nothing changed: no text, brush or tooltip is touched
        _shownReadout = readout;
        _shownOutput = outputText;
        _position.Text = readout.PositionText;
        _barState.Text = readout.BarStateText;
        _barState[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(readout.BarStateMarked ? "BarStateMarked" : "LegibleBrush");
        ToolTip.SetTip(_barState, readout.BarStateTooltip);
        _master.Text = StatusTextPresenter.MasterInfoText(readout.Tempo, readout.MasterKeyLabel, readout.DurationName, readout.Dots, readout.Triplet);
        _output.Text = outputText;
        _outputWanted = showOutput;
        _output.IsVisible = showOutput && !_audioDevice.IsVisible;
    }

    /// <summary>Shows the audio interface button with the running or idle device text, hidden when no device applies. While it shows, the MIDI output name is hidden (as the WPF status bar).</summary>
    public void ShowAudioDevice(AudioDeviceStatus status)
    {
        _audioDevice.IsVisible = status.DeviceButtonVisible;
        _output.IsVisible = _outputWanted && !status.DeviceButtonVisible;
        if (status.Text is null) return;
        _audioDeviceText.Text = status.Text;
    }

    /// <summary>Shows the audio button's tooltip: title, driver · device, rate · buffer · latency and the muted channels line when known. Rebuilt only when the facts change.</summary>
    public void ShowAudioTip(AudioDeviceTip tip)
    {
        if (tip == _audioTip) return;
        _audioTip = tip;
        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Name = "AudioTipTitle", Text = AudioInterfaceTooltip, FontWeight = FontWeight.Bold },
                new TextBlock { Name = "AudioTipDevice", Text = $"{tip.Driver} · {tip.Device}" },
                new TextBlock { Name = "AudioTipFormat", Text = tip.Format },
            },
        };
        if (tip.Channels is not null)
        {
            var channels = new TextBlock { Name = "AudioTipChannels", Text = tip.Channels, FontSize = AudioDeviceFontSize };
            channels[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
            panel.Children.Add(channels);
        }
        ToolTip.SetTip(_audioDevice, panel);
    }

    /// <summary>Shows the import status: the Cancel button while imports run, and their status text.</summary>
    public void ShowImports(ImportStatus status)
    {
        _cancelImport.IsVisible = status.CancelVisible;
        if (status.StatusText is not null) ShowStatus(status.StatusText, status.StatusTooltip);
        if (status.CancelText is not null) ToolTip.SetTip(_cancelImport, status.CancelText);
    }

    private Button AudioDeviceButton()
    {
        var button = new Button
        {
            Name = "AudioDevice", IsVisible = false, Focusable = false, Padding = new Thickness(AudioDevicePaddingX, AudioDevicePaddingY),
            Margin = new Thickness(AudioDeviceLeftGap, 0, 0, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(AudioDeviceCornerRadius),
            VerticalAlignment = VerticalAlignment.Center, Content = _audioDeviceText,
        };
        // A link with no border at rest and muted text; under the pointer it takes the WPF StatusLinkButton hover (fill, accent-soft border) and its text brightens.
        SetAudioDeviceHover(button, hovered: false);
        button.PointerEntered += (_, _) => SetAudioDeviceHover(button, hovered: true);
        button.PointerExited += (_, _) => SetAudioDeviceHover(button, hovered: false);
        ToolTip.SetTip(button, AudioInterfaceTooltip);
        AutomationProperties.SetName(button, "Audio device settings");
        button.Click += (_, _) => AudioDeviceRequested?.Invoke();
        return button;
    }

    // The button's fill and border come from the theme's hover keys while hovered, and are transparent at rest; the text is muted at rest.
    private void SetAudioDeviceHover(Button button, bool hovered)
    {
        if (hovered)
        {
            button[!Button.BackgroundProperty] = new DynamicResourceExtension("HoverBrush");
            button[!Button.BorderBrushProperty] = new DynamicResourceExtension("AccentSoftBrush");
            _audioDeviceText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
            return;
        }
        button.Background = AvaloniaPalette.TransparentBrush;
        button.BorderBrush = AvaloniaPalette.TransparentBrush;
        _audioDeviceText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
    }

    private static Ellipse MidiDot()
    {
        var dot = new Ellipse { Width = MidiDotSize, Height = MidiDotSize, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
        dot[!Shape.FillProperty] = new DynamicResourceExtension("MainMidiLedIdle");
        dot[!Shape.StrokeProperty] = new DynamicResourceExtension("MainMidiLedRing");
        return dot;
    }

    /// <summary>A text readout in a palette colour; <paramref name="maxWidth"/> trims it with an ellipsis.</summary>
    private static TextBlock Text(double fontSize, string paletteBrush, double maxWidth = double.PositiveInfinity, Thickness? margin = null, string text = "")
    {
        var block = new TextBlock
        {
            Text = text, FontSize = fontSize, MaxWidth = maxWidth, Margin = margin ?? new Thickness(0),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(paletteBrush);
        return block;
    }

    /// <summary>One column of the row: its content, its padding and, when <paramref name="divider"/>, the soft divider on its right.</summary>
    private static Border Cell(Control? content, Thickness padding, bool divider, int column, Thickness? margin = null)
    {
        var cell = new Border
        {
            Padding = padding, Margin = margin ?? new Thickness(0), Child = content,
            BorderThickness = divider ? new Thickness(0, 0, 1, 0) : new Thickness(0),
        };
        if (divider) cell[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        Grid.SetColumn(cell, column);
        return cell;
    }
}

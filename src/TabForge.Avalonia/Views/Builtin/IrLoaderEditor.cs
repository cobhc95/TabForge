using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TabForge.AudioEngine.Builtin.IrLoader;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the IR Loader's own window: the IR file row (or Left and Right rows) with Choose (browse, recent files), Separate Left / Right, Mix and Gain sliders,
//   Auto level, Trim silence, the A/B bypass button, the read-only latency / CPU / length / auto-gain line, the status line (a missing file in red),
//   the waveform and response picture, and dropping a .wav on the window.
// Does not own: the engine talk, recent files and pictures (IrLoaderEditor.Engine.cs), the picture (IrGraphView), the sound (IrLoaderEffect in the engine).
// Tests: BuiltinEffectTests.
public sealed partial class IrLoaderEditor : UserControl
{
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly Func<TopLevel, string?, Task<string?>> _pick;
    private readonly IUiThread _ui = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();
    private readonly IUiTimer _poll;
    private readonly IrGraphView _graph = new() { Name = "IrGraph", MinHeight = DialogMetrics.IrGraphMinHeight };
    private readonly TextBlock _fileText = PathText("IrFileText"), _leftText = PathText("IrLeftText"), _rightText = PathText("IrRightText");
    private readonly StackPanel _mainRow, _sideRows;
    private readonly BuiltinToggle _separate = Check("Separate Left / Right files", "IrSeparate", "Off: one file plays both sides (a stereo file left to left, right to right). On: a Left file and a Right file.");
    private readonly BuiltinToggle _autoLevel = Check("Auto level", "IrAutoLevel", "Scales the IR so its loudest frequency passes at 0 dB: a loud IR file cannot blast the speakers.");
    private readonly BuiltinToggle _trim = Check("Trim silence", "IrTrim", "Cuts the silence before the IR starts and its quiet ring-out at the end.");
    private readonly BuiltinToggle _ab = new() { Content = "A/B: IR on", Name = "IrBypass" };
    private readonly Slider _mix = new() { Minimum = 0, Maximum = 100, Name = "IrMix" };
    private readonly Slider _gain = new() { Minimum = -24, Maximum = 24, Name = "IrGain" };
    private readonly TextBlock _mixValue = Value("IrMixValue"), _gainValue = Value("IrGainValue");
    private readonly TextBlock _readout = new() { Name = "IrReadout", [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
    private readonly TextBlock _status = new() { Name = "IrStatus", TextWrapping = TextWrapping.Wrap };
    private bool _applying;

    /// <summary>The chain slot this editor edits.</summary>
    public PluginSlot Slot { get; }

    /// <summary>The title of a window that holds this editor.</summary>
    public string EffectTitle { get; }

    /// <summary>The opening size and the smallest size.</summary>
    public (double Width, double Height, double MinWidth, double MinHeight) PreferredSize { get; } =
        (DialogMetrics.IrLoaderWidth, DialogMetrics.IrLoaderHeight, DialogMetrics.IrLoaderMinWidth, DialogMetrics.IrLoaderMinHeight);

    public IrLoaderEditor(IFxChainHost host, TrackModel track, PluginSlot slot, Func<TopLevel, string?, Task<string?>>? pick = null)
    {
        (_host, _track, Slot, _pick) = (host, track, slot, pick ?? PickWav);
        Styles.Add(TrackPillButton.IdleLook());
        EffectTitle = $"IR Loader — {track.Name}";
        (MinWidth, MinHeight) = (PreferredSize.MinWidth, PreferredSize.MinHeight);
        FontSize = DialogMetrics.FontSize;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _mainRow = FileRow("IR file", _fileText, IrLoaderEffect.FileKey);
        _sideRows = new StackPanel { Children = { FileRow("Left file", _leftText, IrLoaderEffect.LeftKey), FileRow("Right file", _rightText, IrLoaderEffect.RightKey) } };
        var top = new StackPanel { Children = { _mainRow, _sideRows, _separate } };
        var bottom = new StackPanel
        {
            Children =
            {
                SliderRow("Mix", _mix, _mixValue, "Dry / wet: 100 % is the IR only."),
                SliderRow("Gain", _gain, _gainValue, "The level of the IR's sound, in dB."),
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { _autoLevel, _trim.WithMargin(DialogMetrics.IrButtonGap), _ab.WithMargin(DialogMetrics.IrButtonGap) } }.Spaced(DialogMetrics.IrRowGap),
                _readout.Spaced(DialogMetrics.IrRowGap),
                _status,
            },
        };
        ToolTip.SetTip(_ab, "A/B: compare the dry sound with the IR (the IR stays loaded).");
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        Content = DialogMetrics.Inset(new DockPanel { Children = { top, bottom, _graph.Spaced(DialogMetrics.IrSectionGap) } });
        _poll = _ui.CreateTimer(TimeSpan.FromMilliseconds(ChromeMetrics.BuiltinPollMs));
        WireControls();
        WireEngine();
        ShowFiles(StateText(IrLoaderEffect.FileKey), StateText(IrLoaderEffect.LeftKey), StateText(IrLoaderEffect.RightKey));
    }

    private void WireControls()
    {
        _mix.ValueChanged += (_, e) => { _mixValue.Text = $"{e.NewValue:0} %"; if (!_applying) Send(IrLoaderEffect.MixId, e.NewValue); };
        _gain.ValueChanged += (_, e) => { _gainValue.Text = $"{e.NewValue:+0.0;-0.0;0.0} dB"; if (!_applying) Send(IrLoaderEffect.GainId, e.NewValue); };
        _autoLevel.IsCheckedChanged += (_, _) => { if (!_applying) Send(IrLoaderEffect.AutoLevelId, _autoLevel.IsChecked == true ? 1 : 0); };
        _trim.IsCheckedChanged += (_, _) => { if (!_applying) Send(IrLoaderEffect.TrimId, _trim.IsChecked == true ? 1 : 0); };
        _separate.IsCheckedChanged += (_, _) => { ShowMode(); if (!_applying) Send(IrLoaderEffect.SeparateId, _separate.IsChecked == true ? 1 : 0); };
        _ab.IsCheckedChanged += (_, _) => { _ab.Content = _ab.IsChecked == true ? "A/B: dry" : "A/B: IR on"; if (!_applying) Send(IrLoaderEffect.BypassId, _ab.IsChecked == true ? 1 : 0); };
        _autoLevel.IsChecked = true;
        _mix.Value = 100;
        _gainValue.Text = "0.0 dB";
        ShowMode();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, new EventHandler<DragEventArgs>((_, e) => e.DragEffects = DroppedWav(e) is null ? DragDropEffects.None : DragDropEffects.Copy));
        AddHandler(DragDrop.DropEvent, new EventHandler<DragEventArgs>((_, e) => { if (DroppedWav(e) is { } path) SetFile(_separate.IsChecked == true ? IrLoaderEffect.LeftKey : IrLoaderEffect.FileKey, path); }));
    }

    private void ShowMode()
    {
        var separate = _separate.IsChecked == true;
        _mainRow.IsVisible = !separate;
        _sideRows.IsVisible = separate;
    }

    // A file row: its label, the file's path (trimmed in the middle by the column) and Choose, whose menu browses or picks a recent file.
    private StackPanel FileRow(string label, TextBlock path, string key)
    {
        var choose = new Button { Content = "Choose ▾", Name = "IrChoose" + key, Classes = { AvaloniaDefaultLooks.ButtonLook } };
        DialogMetrics.WithPadding(choose, DialogMetrics.IrButtonPadding);
        ToolTip.SetTip(choose, "Browse for a WAV impulse response, or pick one you used before.");
        var browseButton = new Button { Content = "Browse…", Name = "IrBrowse" + key, Classes = { AvaloniaDefaultLooks.ButtonLook } };
        DialogMetrics.WithPadding(browseButton, DialogMetrics.IrButtonPadding);
        ToolTip.SetTip(browseButton, "Open the file browser to pick a WAV impulse response.");
        browseButton.Click += (_, _) => Browse(key);
        var menu = new MenuFlyout();
        choose.Flyout = menu;
        menu.Opening += (_, _) => FillChooseMenu(menu, key);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{DialogMetrics.IrLabelWidth},*,Auto,Auto") };
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(path, 1);
        Grid.SetColumn(choose.WithMargin(DialogMetrics.IrButtonGap), 2);
        grid.Children.Add(name);
        grid.Children.Add(path);
        grid.Children.Add(choose);
        Grid.SetColumn(browseButton.WithMargin(DialogMetrics.IrButtonGap), 3);
        grid.Children.Add(browseButton);
        return new StackPanel { Children = { grid } }.Spaced(DialogMetrics.IrRowGap);
    }

    private void FillChooseMenu(MenuFlyout menu, string key)
    {
        menu.Items.Clear();
        var browse = new MenuItem { Header = "Browse…" };
        browse.Click += (_, _) => Browse(key);
        menu.Items.Add(browse);
        var recent = _host.PluginSettings.RecentImpulseFiles;
        if (recent.Count > 0) menu.Items.Add(new Separator());
        foreach (var file in recent)
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileName(file) };
            ToolTip.SetTip(item, file);
            item.Click += (_, _) => SetFile(key, file);
            menu.Items.Add(item);
        }
    }

    // The picker belongs to the window that shows this editor (embedded in the FX chain window or in its own); it opens without blocking the UI.
    private async void Browse(string key)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            if (await _pick(top, StartFolder(key)) is { } path) SetFile(key, path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.IO.IOException or ArgumentException)
        {
            _status.Text = "The file browser could not open: " + ex.Message;
        }
    }

    // Starts in the folder of the file already loaded, else in the folder of the last IR used.
    private string? StartFolder(string key)
    {
        var file = StateText(key);
        if (string.IsNullOrEmpty(file) && _host.PluginSettings.RecentImpulseFiles.Count > 0) file = _host.PluginSettings.RecentImpulseFiles[0];
        return string.IsNullOrEmpty(file) ? null : System.IO.Path.GetDirectoryName(file);
    }

    private Task<string?> PickWav(TopLevel top, string? folder) => Task.FromResult(new AvaloniaFileDialogs(_host.DialogFolders).OpenFile(new TabForge.Platform.FileDialogOptions
    {
        Key = TabForge.Services.DialogKeys.IrBrowse, Title = "Choose an impulse response", Filter = "WAV impulse responses (*.wav)|*.wav", InitialDirectory = folder,
    }, top));

    private static Grid SliderRow(string label, Slider slider, TextBlock value, string tip)
    {
        ToolTip.SetTip(slider, tip);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{DialogMetrics.IrLabelWidth},*,{DialogMetrics.IrValueWidth}") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value.WithMargin(DialogMetrics.IrButtonGap), 2);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid.Spaced(DialogMetrics.IrRowGap);
    }

    private static TextBlock PathText(string name) => new() { Name = name, Text = "(none)", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PrefixCharacterEllipsis };

    private static TextBlock Value(string name) => new() { Name = name, VerticalAlignment = VerticalAlignment.Center };

    private static BuiltinToggle Check(string text, string name, string tip)
    {
        var box = new BuiltinToggle { Content = text, Name = name };
        ToolTip.SetTip(box, tip);
        return box;
    }

    private static string? DroppedWav(DragEventArgs e)
    {
#pragma warning disable CS0618 // Avalonia 11.3 marks the IDataObject drag API obsolete; it still delivers the dropped file list (as MainWindowFileDrop).
        foreach (var item in e.Data.GetFiles() ?? Array.Empty<global::Avalonia.Platform.Storage.IStorageItem>())
            if (item.Path.IsFile && item.Path.LocalPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return item.Path.LocalPath;
#pragma warning restore CS0618
        return null;
    }
}

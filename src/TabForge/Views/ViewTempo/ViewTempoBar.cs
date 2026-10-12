using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabForge.Views.ViewTempo;

// Owns: the View and tempo pane's one row of four equal buttons (zoom, speed, tempo, time / key): their captions and tooltips,
//       and what opens from them (a click or Enter / Space: the preset menu; a right click or the Menu key: the fine-tune popup;
//       the wheel over zoom or speed: a step).
// Does not own: the values and how they are applied (IViewTempoHost; in the window, ViewTempoControlsHost over the pane's own
//       value controls), the menu rows (ViewTempoMenus) or the popup contents (ViewTempoPopups).
// Tests: TestToolbarZoomAndSpeed, TestToolbarZoomSpeedNarrow.
public sealed class ViewTempoBar : UniformGrid
{
    public const int Zoom = 0, Speed = 1, Tempo = 2, TimeKey = 3;

    private static readonly string[] Names = { "Zoom", "Playback speed", "Tempo", "Time signature and key" };
    private readonly Button[] _buttons = new Button[4];
    private readonly TextBlock[] _captions = new TextBlock[4];
    private IViewTempoHost? _host;
    private ViewTempoControlsHost? _found;

    public ViewTempoBar()
    {
        Rows = 1;
        Columns = 4;
        for (var i = 0; i < 4; i++) Children.Add(_buttons[i] = MakeButton(i));
        Loaded += (_, _) => { if (_host is null) Host = _found = ViewTempoControlsHost.Find(this); };
        Unloaded += (_, _) => { if (_found is null) return; Host = null; _found.Dispose(); _found = null; };
    }

    /// <summary>The app side. In the window it is found on load from the pane's own value controls.</summary>
    public IViewTempoHost? Host
    {
        get => _host;
        set
        {
            if (_host is not null) _host.Changed -= Refresh;
            _host = value;
            if (_host is not null) _host.Changed += Refresh;
            Refresh();
        }
    }

    public IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>The text a button shows.</summary>
    public string Caption(int kind) => _captions[kind].Text;

    /// <summary>The preset menu of a button, as a click opens it.</summary>
    public ContextMenu? BuildMenu(int kind) => _host is null ? null : ViewTempoMenus.Build(kind, _host);

    /// <summary>The full name of a short key: "C" is "C major", "Am" is "A minor".</summary>
    public static string FullKey(string shortKey) =>
        shortKey.EndsWith('m') ? shortKey[..^1] + " minor" : shortKey + " major";

    private void Refresh()
    {
        if (_host is not { } host) return;
        _captions[Zoom].Text = host.ZoomText.StartsWith("Fit", StringComparison.OrdinalIgnoreCase) ? "Fit" : host.ZoomText;
        _captions[Speed].Text = $"{host.SpeedPercent:0}%";
        _captions[Tempo].Text = host.Tempo.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _captions[TimeKey].Text = $"{host.TimeText} {host.KeyText}";
        TooltipShortcuts.Bind(_buttons[TimeKey], $"Time signature and key: {host.TimeText}, {FullKey(host.KeyText)}", "Bar.TimeSignature");
    }

    private Button MakeButton(int kind)
    {
        var caption = _captions[kind] = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        if (Mark(kind) is { } mark) content.Children.Add(mark);
        content.Children.Add(caption);

        var button = new Button { Content = content, Height = 27, MinWidth = 0, Padding = new Thickness(1, 0, 1, 0), Margin = new Thickness(2, 0, 2, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(button, Names[kind]);
        button.ToolTip = kind switch
        {
            Zoom => "Score zoom: click for presets, right-click to fine-tune",
            Speed => "Playback speed: click for presets, right-click to fine-tune",
            Tempo => "Tempo (BPM): click for presets, right-click to type",
            _ => "Time signature and key",
        };
        button.Click += (_, _) => OpenMenu(kind);
        button.MouseRightButtonUp += (_, e) => { e.Handled = true; OpenPopup(kind); };
        button.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Apps && !(e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) return;
            e.Handled = true;
            OpenPopup(kind);
        };
        if (kind is Zoom or Speed)
            button.PreviewMouseWheel += (_, e) =>
            {
                if (_host is not { } host) return;
                e.Handled = true;
                var up = e.Delta > 0;
                if (kind == Zoom) host.Run(up ? "View.ZoomIn" : "View.ZoomOut");
                else host.SetSpeedPercent(host.SpeedPercent + (up ? 5 : -5));
            };
        return button;
    }

    private void OpenMenu(int kind)
    {
        if (BuildMenu(kind) is not { } menu) return;
        menu.PlacementTarget = _buttons[kind];
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OpenPopup(int kind)
    {
        if (_host is not null) ViewTempoPopups.Build(kind, _host, _buttons[kind]).IsOpen = true;
    }

    // The small mark before the value: a magnifier, a play triangle, a quarter note; time / key has none.
    private static FrameworkElement? Mark(int kind)
    {
        if (kind == TimeKey) return null;
        if (kind == Zoom)
        {
            var glass = new Path
            {
                Data = Geometry.Parse("M7,4 A3,3 0 1 1 1,4 A3,3 0 1 1 7,4 M6.3,6.3 L9.5,9.5"), StrokeThickness = 1.4, Width = 11, Height = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), SnapsToDevicePixels = false,
            };
            glass.SetResourceReference(Shape.StrokeProperty, "MutedBrush");
            return glass;
        }
        var text = new TextBlock { Text = kind == Speed ? "▶" : "♩", FontSize = kind == Speed ? 9 : 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return text;
    }
}

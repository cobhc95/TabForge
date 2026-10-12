using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TabForge.Views.ViewTempo;

/// <summary>What the View and tempo bar needs from the app: the four values, a way to set them and a change notice.</summary>
public interface IViewTempoHost
{
    /// <summary>"Fit width" or a percentage ("125%").</summary>
    string ZoomText { get; }
    void SetZoom(string text);
    double SpeedPercent { get; }
    void SetSpeedPercent(double percent);
    int Tempo { get; }
    void SetTempo(int bpm);
    /// <summary>The time signature at the cursor bar ("4/4").</summary>
    string TimeText { get; }
    /// <summary>The short key at the cursor bar ("C", "Am").</summary>
    string KeyText { get; }
    /// <summary>Runs a catalogue command; false when it is not reachable.</summary>
    bool Run(string commandId);
    event Action? Changed;
}

// Owns: the bar's link to the window's five value controls (zoom box, speed box, tempo box, time and key labels), which stay
//       in the pane unseen: a value is set by writing the control and raising its own handler, and read back from its text.
// Does not own: the zoom, speed and tempo logic (ScoreZoomController, TransportControlsController, DocumentViewBinder), the bar's
//       buttons (ViewTempoBar) or the commands themselves.
// Tests: TestToolbarZoomAndSpeed, TestToolbarZoomSpeedNarrow.
public sealed class ViewTempoControlsHost : IViewTempoHost, IDisposable
{
    private readonly ComboBox _zoom, _speed;
    private readonly TextBox _tempo;
    private readonly TextBlock _time, _key;
    private readonly EventHandler _onChanged;

    public event Action? Changed;

    public ViewTempoControlsHost(ComboBox zoom, ComboBox speed, TextBox tempo, TextBlock time, TextBlock key)
    {
        (_zoom, _speed, _tempo, _time, _key) = (zoom, speed, tempo, time, key);
        _onChanged = (_, _) => Changed?.Invoke();
        ComboText.AddValueChanged(_zoom, _onChanged);
        ComboText.AddValueChanged(_speed, _onChanged);
        BoxText.AddValueChanged(_tempo, _onChanged);
        LabelText.AddValueChanged(_time, _onChanged);
        LabelText.AddValueChanged(_key, _onChanged);
    }

    private static DependencyPropertyDescriptor ComboText => DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox));
    private static DependencyPropertyDescriptor BoxText => DependencyPropertyDescriptor.FromProperty(TextBox.TextProperty, typeof(TextBox));
    private static DependencyPropertyDescriptor LabelText => DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    /// <summary>The host over the controls named in <paramref name="scope"/>'s name scope, or null when one is missing.</summary>
    public static ViewTempoControlsHost? Find(FrameworkElement scope) =>
        scope.FindName("ZoomCombo") is ComboBox zoom && scope.FindName("SpeedCombo") is ComboBox speed &&
        scope.FindName("TempoBox") is TextBox tempo && scope.FindName("TimeSigLabel") is TextBlock time &&
        scope.FindName("KeyLabelText") is TextBlock key
            ? new ViewTempoControlsHost(zoom, speed, tempo, time, key) : null;

    public void Dispose()
    {
        ComboText.RemoveValueChanged(_zoom, _onChanged);
        ComboText.RemoveValueChanged(_speed, _onChanged);
        BoxText.RemoveValueChanged(_tempo, _onChanged);
        LabelText.RemoveValueChanged(_time, _onChanged);
        LabelText.RemoveValueChanged(_key, _onChanged);
    }

    public string ZoomText => _zoom.Text;
    public void SetZoom(string text) => SetCombo(_zoom, text);

    public double SpeedPercent => Percent(_speed.Text, 100);
    public void SetSpeedPercent(double percent) => SetCombo(_speed, $"{Math.Clamp(percent, 25, 200):0}%");

    public int Tempo => int.TryParse(_tempo.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 120;
    public void SetTempo(int bpm)
    {
        _tempo.Text = bpm.ToString(CultureInfo.InvariantCulture);
        _tempo.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));   // the box's own commit
    }

    public string TimeText => _time.Text;
    public string KeyText => _key.Text;

    public bool Run(string commandId)
    {
        if (CommandSource(commandId) is not { } source) return false;
        source.RaiseEvent(new RoutedEventArgs(source is MenuItem ? MenuItem.ClickEvent : ButtonBase.ClickEvent));
        return true;
    }

    /// <summary>The control whose click runs the command: the zoom step buttons beside the zoom box, else the main menu's row.</summary>
    internal UIElement? CommandSource(string commandId)
    {
        var name = commandId == "View.ZoomIn" ? "Zoom in" : commandId == "View.ZoomOut" ? "Zoom out" : null;
        if (name is not null)
            return (_zoom.Parent as Panel)?.Children.OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == name);
        return Window.GetWindow(_zoom)?.FindName("MainMenu") is Menu menu ? MenuHotkey.Find(menu.Items, commandId) : null;
    }

    // A preset is selected (the box's selection handler applies it); any other value is typed and committed as on leaving the box.
    private static void SetCombo(ComboBox combo, string text)
    {
        var preset = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Content?.ToString(), text, StringComparison.Ordinal));
        if (preset is not null) { combo.SelectedItem = preset; return; }
        combo.SelectedIndex = -1;
        combo.Text = text;
        combo.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
    }

    internal static double Percent(string text, double fallback) =>
        double.TryParse(text.Trim().TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
}

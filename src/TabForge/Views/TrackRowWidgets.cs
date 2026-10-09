using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the track-row mix widgets raise on the arrangement panel (implemented by <see cref="ArrangementPanel"/>).</summary>
internal interface ITrackRowWidgetHost
{
    bool PanKnobs { get; }
    void RaiseMixEditStarting();
    void RaiseMixEditEnded();
    void RaisePanStyleChanged(bool knobs);
}

// Owns: the per-row mix widgets' behaviour: pan text and pan menu, mix-edit gesture bracketing on sliders, the instrument icon button.
// Does not own: building the row and placing cells (ArrangementPanel.TrackRows), applying the edits (MainWindow via the panel's events).
// Tests: TestTrackRowMenu, TestTrackRowRightClick, TestMuteSoloFast, TestTrackIconButton.
internal sealed class TrackRowWidgets
{
    private readonly ITrackRowWidgetHost _host;

    public TrackRowWidgets(ITrackRowWidgetHost host) => _host = host;

    public static string PanText(double value)
    {
        var offset = (int)Math.Round(value) - 64;
        return offset == 0 ? "Centre" : offset < 0 ? $"L {-offset}" : $"R {offset}";
    }

    public ContextMenu PanContextMenu(TrackModel track, FrameworkElement control)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)) };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header, Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
            item.Click += (_, _) => action();
            return item;
        }
        void SetPan(int value)
        {
            _host.RaiseMixEditStarting();
            if (control is KnobControl knob) knob.Value = value;
            else if (control is Slider slider) slider.Value = Math.Clamp(value, 0, 127) - 64;
            _host.RaiseMixEditEnded();
        }
        menu.Items.Add(Item("Centre pan", () => SetPan(64)));
        menu.Items.Add(Item("Set exact pan…", () =>
        {
            var text = GpDialogs.Prompt("Pan", "Pan from -63 (left) to +63 (right), 0 = centre:", (track.Pan - 64).ToString());
            if (int.TryParse(text, out var offset)) SetPan(Math.Clamp(offset + 64, 0, 127));
        }));
        menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource(MenuItem.SeparatorStyleKey) });
        var knobs = Item("Knob style", () => _host.RaisePanStyleChanged(true));
        knobs.IsCheckable = true; knobs.IsChecked = _host.PanKnobs; MenuMarks.SetIsRadio(knobs, true);
        var sliders = Item("Slider style", () => _host.RaisePanStyleChanged(false));
        sliders.IsCheckable = true; sliders.IsChecked = !_host.PanKnobs; MenuMarks.SetIsRadio(sliders, true);
        menu.Items.Add(knobs);
        menu.Items.Add(sliders);

        return menu;
    }

    public void AttachMixEditGestures(Slider slider)
    {
        slider.PreviewMouseLeftButtonDown += (_, _) => _host.RaiseMixEditStarting();
        slider.PreviewMouseLeftButtonUp += (_, _) => _host.RaiseMixEditEnded();
        slider.PreviewMouseWheel += (_, _) =>
        {
            _host.RaiseMixEditStarting();
            slider.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(() => _host.RaiseMixEditEnded()));
        };
        slider.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.PageUp or Key.PageDown or Key.Home or Key.End)
                _host.RaiseMixEditStarting();
        };
        slider.PreviewKeyUp += (_, _) => _host.RaiseMixEditEnded();
        slider.LostMouseCapture += (_, _) => _host.RaiseMixEditEnded();
        slider.LostKeyboardFocus += (_, _) => _host.RaiseMixEditEnded();
    }

    /// <summary>Size (DIPs) of the instrument icon in a track row; the row's default height (34) leaves room around it.</summary>
    public const double IconSize = 24;

    // Hover, pressed and keyboard-focus states for the icon button only (the global button styles stay untouched).
    internal static readonly ControlTemplate IconButtonTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
        "<Border x:Name='Chrome' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='Transparent' BorderBrush='Transparent' BorderThickness='1.5' CornerRadius='5' Padding='1'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource HoverBrush}'/>" +
        "<Setter TargetName='Chrome' Property='BorderBrush' Value='{DynamicResource BorderBrush}'/></Trigger>" +
        "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Chrome' Property='Background' Value='{DynamicResource PressBrush}'/></Trigger>" +
        "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{DynamicResource AccentBrush}'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>");

    /// <summary>The icon key of a track's instrument: one icon per catalogue sound, whatever else differs between the tracks.</summary>
    public static string IconKeyOf(TrackModel track)
    {
        var drum = track.MidiChannel == 9;
        return TabForge.Services.InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, drum) is { } entry
            ? TrackSilhouette.KeyFor(entry) : TrackSilhouette.KeyFor(track.MidiProgram, drum);
    }

    /// <summary>
    /// The instrument icon between the cogwheel and the record button: a click opens the instrument catalogue on the instrument's
    /// family (the other families collapsed, the search box focused) and <paramref name="choose"/> receives the chosen sound.
    /// </summary>
    public static Button InstrumentIconButton(TrackModel track, Action<string> choose, Action<string?>? preview = null)
    {
        var button = new Button
        {
            Content = TrackSilhouette.Element(IconKeyOf(track), IconSize), Template = IconButtonTemplate,
            Width = IconSize + 7, Height = IconSize + 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"{track.InstrumentName} — click to change", Cursor = Cursors.Hand, FocusVisualStyle = null,
        };
        AutomationProperties.SetName(button, $"Instrument: {track.InstrumentName}");
        button.Click += (_, _) =>
        {
            var colour = ColorConverter.ConvertFromString(track.ColorHex) is Color c ? c : Colors.SteelBlue;
            // The catalogue opens on the sound the track plays (its name, or its program when the name is not a catalogue sound).
            var current = TabForge.Services.InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, track.MidiChannel == 9)?.Name ?? track.InstrumentName;
            string? picked = null;
            try { picked = InstrumentPickerWindow.Show(Window.GetWindow(button), current, colour, focusFamily: true, preview: n => preview?.Invoke(n)); }
            finally
            {
                if (picked is not null && !string.Equals(picked, current, StringComparison.OrdinalIgnoreCase)
                    && FamilyChangePrompt.Confirm(Window.GetWindow(button), track, picked, TabForge.Services.InstrumentCatalog.Find(picked)?.IsDrumKit == true)) choose(picked);
                preview?.Invoke(null);   // after the commit: the engine returns to the track's (new or original) program
            }
        };
        return button;
    }

    /// <summary>An audio track's row shows the waveform icon where an instrument track has its instrument button (not clickable).</summary>
    public static FrameworkElement AudioIcon()
    {
        var icon = new Border
        {
            Child = TrackSilhouette.Element(TrackSilhouette.AudioKey, IconSize), Background = Brushes.Transparent, ToolTip = "Audio track",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(3, 2, 3, 2),
        };
        AutomationProperties.SetName(icon, "Audio track");
        return icon;
    }

    internal static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    internal static bool IsInteractiveTrackControl(DependencyObject? element, Border row)
    {
        while (element is not null && !ReferenceEquals(element, row))
        {
            if (element is ButtonBase or Slider or ComboBox or ComboBoxItem or KnobControl or Thumb or FxSplitButton or RecordArmButton or MonitorButton) return true;
            if (element is TextBox textBox && !textBox.IsReadOnly) return true;
            // Dropdown items live in a popup: its visual tree ends at the popup root, not the row,
            // but the click still routes here. Treat anything inside a popup as interactive.
            var parent = VisualTreeHelper.GetParent(element);
            if (parent is null && element is FrameworkElement { Parent: System.Windows.Controls.Primitives.Popup }) return true;
            if (parent is null && element.GetType().Name == "PopupRoot") return true;
            element = parent;
        }
        return false;
    }
}

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

// Owns: the per-row mix widgets' behaviour: pan text and pan menu, mix-edit gesture bracketing on sliders, the instrument picker button.
// Does not own: building the row and placing cells (ArrangementPanel.TrackRows), applying the edits (MainWindow via the panel's events).
// Tests: TestTrackRowMenu, TestTrackRowRightClick, TestMuteSoloFast.
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

    // standard instrument selector: the current sound as a button; click opens six quick picks, a
    // separator, then every GM family as a hover submenu (with the instrument badges), and VST plug-ins.
    public static Button InstrumentButton(TrackModel track, Action<string> choose)
    {
        var label = track.InstrumentName;   // the track keeps its own instrument whatever plug-ins its FX chain holds
        var entry = TabForge.Services.InstrumentCatalog.ForTrack(track.InstrumentName, track.MidiProgram, track.MidiChannel == 9);
        var content = new DockPanel { LastChildFill = true };
        var arrow = new TextBlock { Text = "▾", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(arrow, Dock.Right);
        content.Children.Add(arrow);
        var icon = TabForge.Views.InstrumentIcon.Element(entry, 16); // family colour, not the track colour
        icon.Margin = new Thickness(0, 0, 5, 0);
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content, Margin = new Thickness(4, 2, 4, 2), FontSize = 11, Padding = new Thickness(5, 1, 5, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Instrument / VST for this track",
            VerticalAlignment = VerticalAlignment.Center, MaxHeight = 30,   // stays a button-sized control when rows are tall
        };
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)), PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            MenuItem Item(TabForge.Services.InstrumentEntry e)
            {
                var item = new MenuItem
                {
                    Header = e.Name, Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
                    Icon = TabForge.Views.InstrumentIcon.Element(e, 18), IsCheckable = true,
                    IsChecked = string.Equals(e.Name, label, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) => choose(e.Name);
                return item;
            }
            // Top: every sound in the current instrument's family; then a clear divider and all families.
            var currentFamily = entry?.Category;
            var siblings = currentFamily is null ? new List<TabForge.Services.InstrumentEntry>()
                : TabForge.Services.InstrumentCatalog.All.Where(e => e.Category == currentFamily).ToList();
            if (siblings.Count > 0)
            {
                menu.Items.Add(MenuHeader(currentFamily!.ToUpperInvariant()));
                foreach (var e in siblings) menu.Items.Add(Item(e));
                menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource(MenuItem.SeparatorStyleKey) });
            }
            menu.Items.Add(MenuHeader("ALL INSTRUMENT FAMILIES"));
            foreach (var family in TabForge.Services.InstrumentCatalog.Categories)
            {
                var members = TabForge.Services.InstrumentCatalog.All.Where(e => e.Category == family).ToList();
                var sub = new MenuItem
                {
                    Header = family, Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
                    Icon = TabForge.Views.InstrumentIcon.Element(members[0], 18),
                };
                // Built on first hover so opening the menu stays instant.
                sub.Items.Add(new MenuItem());
                sub.SubmenuOpened += (_, _) =>
                {
                    if (sub.Items.Count == members.Count) return;
                    sub.Items.Clear();
                    foreach (var e in members) sub.Items.Add(Item(e));
                };
                menu.Items.Add(sub);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    private static MenuItem MenuHeader(string text)
    {
        var label = new TextBlock { Text = text, FontSize = Services.ThemeService.MinFontSize, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 1) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return new MenuItem { Header = label, IsEnabled = false, IsHitTestVisible = false, Focusable = false,
            Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
    }

    /// <summary>An audio track's row shows a waveform and "Audio" where an instrument track has its instrument picker.</summary>
    public static FrameworkElement AudioKindCell()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 4, 2) };
        var icon = AudioTrackIcon.Element(16);
        icon.Margin = new Thickness(0, 0, 6, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        var text = new TextBlock { Text = "Audio", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "LegibleBrush");
        content.Children.Add(text);
        content.ToolTip = "Audio track: audio and MIDI clips, no notation";
        AutomationProperties.SetName(content, "Audio track");
        return content;
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
